"""Regenerates components.json and icons.json from the Grasshopper sources.

    cd tools/webgen && python3 extract_components.py

Reads every src/Mite.Grasshopper/Components/*Component.cs: the base(...) call
(name, nickname, description, tab, icon) and the pManager.Add<Type>Parameter
calls of RegisterInputParams / RegisterOutputParams (name, nickname,
description, access, default, Optional flags, the shared Shape / Section pair
of RegisterSectionInputs). Icons are the embedded 24 px PNGs as data URIs.
Run it whenever a component's parameters change; the bench page and its
mode-menu checks read the result.
"""
import base64, json, os, re

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
GH = os.path.join(ROOT, "src", "Mite.Grasshopper")


def split_args(s, start):
    """Top-level arguments of the call whose '(' is at s[start]; returns (args, end)."""
    depth, i, cur, args = 0, start, [], []
    while i < len(s):
        ch = s[i]
        if ch == '"':
            j = i + 1
            while s[j] != '"':
                j += 2 if s[j] == "\\" else 1
            cur.append(s[i:j + 1]); i = j + 1; continue
        if ch in "([{":
            depth += 1
            if depth == 1: i += 1; continue
        elif ch in ")]}":
            depth -= 1
            if depth == 0:
                args.append("".join(cur).strip()); return args, i
        elif ch == "," and depth == 1:
            args.append("".join(cur).strip()); cur = []; i += 1; continue
        cur.append(ch); i += 1
    raise ValueError("unbalanced call")


CONSTANTS = {}


def load_constants():
    for fn in os.listdir(GH):
        if not fn.endswith(".cs"): continue
        src = open(os.path.join(GH, fn), encoding="utf-8").read()
        cls = re.search(r"class\s+(\w+)", src)
        if not cls: continue
        for m in re.finditer(r"const\s+string\s+(\w+)\s*=\s*", src):
            j = m.end(); parts = []
            while True:
                lit = re.match(r'\s*"((?:[^"\\]|\\.)*)"\s*(\+)?', src[j:], re.S)
                if not lit: break
                parts.append(lit.group(1)); j += lit.end()
                if not lit.group(2): break
            CONSTANTS[cls.group(1) + "." + m.group(1)] = unescape("".join(parts))


def unescape(s):
    return s.replace('\\"', '"').replace("\\n", "\n").replace("\\\\", "\\")


def text(expr):
    """Value of a string expression: literals and known constants joined by +."""
    out = []
    for tok in re.findall(r'"(?:[^"\\]|\\.)*"|[A-Za-z_][\w.]*', expr):
        if tok.startswith('"'): out.append(unescape(tok[1:-1]))
        elif tok in CONSTANTS: out.append(CONSTANTS[tok])
    return "".join(out)


def params(body, section_pair):
    ps = []
    i = 0
    while True:
        m = re.search(r"pManager\.Add(\w+)Parameter\s*\(|RegisterSectionInputs\(pManager\)|pManager\[([^\]]+)\]\.Optional\s*=\s*true", body[i:])
        if not m: break
        k = i + m.start()
        if m.group(0).startswith("RegisterSectionInputs"):
            ps.extend(json.loads(json.dumps(section_pair))); i = k + len(m.group(0)); continue
        if m.group(2) is not None:
            idx = m.group(2).strip()
            n = len(ps) - 1 if idx == "pManager.ParamCount - 1" else int(idx)
            ps[n]["optional"] = True; i = k + len(m.group(0)); continue
        args, end = split_args(body, k + len(m.group(0)) - 1)
        p = {"name": text(args[0]), "nick": text(args[1]), "type": m.group(1), "desc": text(args[2]),
             "access": args[3].split(".")[-1], "default": args[4] if len(args) > 4 else None}
        ps.append(p); i = end + 1
    return ps


def method_body(src, name):
    m = re.search(r"void\s+" + name + r"\s*\([^)]*\)\s*\{", src)
    if not m: return ""
    depth, i = 1, m.end()
    while depth:
        depth += {"{": 1, "}": -1}.get(src[i], 0); i += 1
    return src[m.end():i - 1]


def main():
    load_constants()
    base = open(os.path.join(GH, "MiteComponent.cs"), encoding="utf-8").read()
    section_pair = params(method_body(base, "RegisterSectionInputs"), [])
    comps, icons = [], {}
    cdir = os.path.join(GH, "Components")
    for fn in sorted(os.listdir(cdir)):
        if not fn.endswith("Component.cs"): continue
        src = open(os.path.join(cdir, fn), encoding="utf-8").read()
        m = re.search(r":\s*base\s*\(", src)
        args, _ = split_args(src, m.end() - 1)
        guid = re.search(r'ComponentGuid\s*=>\s*new\s*(?:Guid)?\("([^"]+)"\)', src)
        comps.append({"file": fn, "name": text(args[0]), "nick": text(args[1]), "desc": text(args[2]),
                      "tab": text(args[3]), "icon": text(args[4]), "guid": guid.group(1) if guid else None,
                      "inputs": params(method_body(src, "RegisterInputParams"), section_pair),
                      "outputs": params(method_body(src, "RegisterOutputParams"), section_pair)})
    for fn in sorted(os.listdir(os.path.join(GH, "Resources"))):
        if fn.endswith(".png"):
            data = open(os.path.join(GH, "Resources", fn), "rb").read()
            icons[fn[:-4]] = "data:image/png;base64," + base64.b64encode(data).decode()
    here = os.path.dirname(os.path.abspath(__file__))
    # Keep the order of the existing files (small diffs); new entries go last.
    cpath, ipath = os.path.join(here, "components.json"), os.path.join(here, "icons.json")
    if os.path.exists(cpath):
        order = [c["name"] for c in json.load(open(cpath, encoding="utf-8"))]
        comps.sort(key=lambda c: order.index(c["name"]) if c["name"] in order else len(order))
    if os.path.exists(ipath):
        order = list(json.load(open(ipath, encoding="utf-8")))
        icons = dict(sorted(icons.items(), key=lambda kv: order.index(kv[0]) if kv[0] in order else len(order)))
    with open(cpath, "w", encoding="utf-8") as f:
        f.write(json.dumps(comps, indent=1, ensure_ascii=False))
    with open(ipath, "w", encoding="utf-8") as f:
        f.write(json.dumps(icons, indent=1))
    print(len(comps), "components,", sum(len(c["inputs"]) for c in comps), "inputs,", len(icons), "icons")


if __name__ == "__main__":
    main()
