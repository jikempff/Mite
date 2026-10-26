#!/usr/bin/env python3
"""Regenerates components.json from the Grasshopper component sources.

Parses each src/Mite.Grasshopper/Components/*Component.cs: the base(...)
constructor call (name, nick, description, tab, icon), ComponentGuid, and
every pManager.Add*Parameter(...) call of RegisterInputParams /
RegisterOutputParams in source order (RegisterSectionInputs is expanded from
MiteComponent.cs), with GH_ParamAccess, the default literal and the Optional
flags. Run it whenever a component's parameters change:

    python3 tools/webgen/extract_components.py
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COMP_DIR = os.path.join(ROOT, "src", "Mite.Grasshopper", "Components")
OUT = os.path.join(ROOT, "tools", "webgen", "components.json")

STR = r'"((?:[^"\\]|\\.)*)"'                      # one C# string literal
CONCAT = STR + r'(?:\s*\+\s*' + STR + r')*'         # "a" + "b" + ...


def unescape(s):
    return s.encode().decode("unicode_escape") if "\\" in s else s


def join_concat(text):
    """Joins a chain of concatenated string literals into one string."""
    return "".join(unescape(m) for m in re.findall(STR, text))


def split_args(body):
    """Splits a C# argument list at top-level commas."""
    args, depth, cur, in_str, esc = [], 0, "", False, False
    for ch in body:
        if in_str:
            cur += ch
            if esc: esc = False
            elif ch == "\\": esc = True
            elif ch == '"': in_str = False
            continue
        if ch == '"': in_str = True; cur += ch
        elif ch in "([{": depth += 1; cur += ch
        elif ch in ")]}": depth -= 1; cur += ch
        elif ch == "," and depth == 0: args.append(cur.strip()); cur = ""
        else: cur += ch
    if cur.strip(): args.append(cur.strip())
    return args


def method_body(src, name):
    m = re.search(r"void\s+" + name + r"\s*\([^)]*\)\s*\{", src)
    if not m: return ""
    i, depth = m.end(), 1
    while i < len(src) and depth:
        depth += {"{": 1, "}": -1}.get(src[i], 0); i += 1
    return src[m.end():i - 1]


def calls(body, pattern):
    """Yields (method, [args]) for every pManager.<pattern>(...) call, in order."""
    for m in re.finditer(r"pManager\.(" + pattern + r")\s*\(", body):
        i, depth = m.end(), 1
        while depth:
            depth += {"(": 1, ")": -1}.get(body[i], 0); i += 1
        yield m.group(1), split_args(body[m.end():i - 1])


def resolve_text(arg, consts):
    if re.match(r'^\s*"', arg): return join_concat(arg)
    key = arg.strip().split(".")[-1]
    return consts.get(key, arg.strip())


def params(body, consts, section_body):
    out = []
    for method, args in calls(body, r"Add[A-Za-z]+Parameter|RegisterSectionInputs"):
        pass
    # expand RegisterSectionInputs(pManager) in place
    for m in re.finditer(r"(pManager\.Add[A-Za-z]+Parameter\s*\(|RegisterSectionInputs\s*\(\s*pManager\s*\))", body):
        if m.group(1).startswith("RegisterSectionInputs"):
            out.extend(params(section_body, consts, ""))
            continue
        i, depth = m.end(), 1
        while depth:
            depth += {"(": 1, ")": -1}.get(body[i], 0); i += 1
        args = split_args(body[m.end():i - 1])
        kind = re.match(r"pManager\.Add([A-Za-z]+)Parameter", m.group(1)).group(1)
        p = {"name": join_concat(args[0]), "nick": join_concat(args[1]), "type": kind,
             "desc": resolve_text(args[2], consts), "access": args[3].split(".")[-1] if len(args) > 3 else "item",
             "default": None}
        if len(args) > 4: p["default"] = args[4]
        out.append(p)
    # Optional flags: pManager[i].Optional = true  (i literal or ParamCount - 1)
    idx = 0
    for m in re.finditer(r"(pManager\.Add[A-Za-z]+Parameter\s*\(|RegisterSectionInputs\s*\(\s*pManager\s*\)|pManager\[([^\]]+)\]\.Optional\s*=\s*true)", body):
        if m.group(2) is not None:
            expr = m.group(2).strip()
            if expr.isdigit(): k = int(expr)
            elif "ParamCount" in expr: k = idx - 1
            else: continue
            if 0 <= k < len(out): out[k]["optional"] = True
        elif m.group(1).startswith("RegisterSectionInputs"):
            idx += 2
            if "Optional" in section_body: out[idx - 1]["optional"] = True
        else:
            idx += 1
    return out


def main():
    base_src = open(os.path.join(ROOT, "src", "Mite.Grasshopper", "MiteComponent.cs"), encoding="utf-8").read()
    section_src = open(os.path.join(ROOT, "src", "Mite.Grasshopper", "SectionInput.cs"), encoding="utf-8").read()
    consts = {}
    for m in re.finditer(r"const\s+string\s+(\w+)\s*=\s*(" + CONCAT + r")\s*;", section_src + base_src):
        consts[m.group(1)] = join_concat(m.group(2))
    section_body = method_body(base_src, "RegisterSectionInputs")

    comps = []
    for f in sorted(os.listdir(COMP_DIR)):
        if not f.endswith("Component.cs"): continue
        src = open(os.path.join(COMP_DIR, f), encoding="utf-8").read()
        m = re.search(r":\s*base\s*\(", src)
        if not m: continue
        i, depth = m.end(), 1
        while depth:
            depth += {"(": 1, ")": -1}.get(src[i], 0); i += 1
        args = split_args(src[m.end():i - 1])
        if len(args) < 5: continue
        guid = re.search(r"ComponentGuid\s*=>\s*new\s*(?:Guid)?\s*\(\s*" + STR, src)
        comps.append({
            "file": f, "name": join_concat(args[0]), "nick": join_concat(args[1]), "desc": join_concat(args[2]),
            "tab": join_concat(args[3]), "icon": join_concat(args[4]), "guid": guid.group(1) if guid else "",
            "inputs": params(method_body(src, "RegisterInputParams"), consts, section_body),
            "outputs": params(method_body(src, "RegisterOutputParams"), consts, section_body),
        })
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(comps, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    print(f"{len(comps)} components, {sum(len(c['inputs']) for c in comps)} inputs, {sum(len(c['outputs']) for c in comps)} outputs -> {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
