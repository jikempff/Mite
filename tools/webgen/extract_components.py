#!/usr/bin/env python3
"""Regenerates components.json from the Grasshopper component sources.

Reads src/Mite.Grasshopper/Components/*.cs: the base(...) call (name, nick,
description, tab, icon), ComponentGuid, and every pManager.Add*Parameter /
AddParameter(new MiteNetParameter(), ...) call in RegisterInputParams and
RegisterOutputParams, expanding the shared helpers RegisterSectionInputs,
RegisterNetInput and RegisterNetOutput (MiteComponent.cs). Optional flags come
from `pManager[i].Optional = true` (also inside a foreach over indices).

Run from tools/webgen:  python3 extract_components.py
The component order of the existing components.json is kept; new components
are appended.
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "..", "src", "Mite.Grasshopper")
COMP_DIR = os.path.join(SRC, "Components")

STR = r'"(?:[^"\\]|\\.)*"'


def cs_string(expr):
    """Concatenated C# string literals ("a" + "b") -> python string; None if not literal."""
    parts = re.findall(STR, expr)
    if not parts:
        return None
    return "".join(json.loads(p) for p in parts)


def const(name):
    src = open(os.path.join(SRC, "MiteComponent.cs"), encoding="utf-8").read()
    src += open(os.path.join(SRC, "SectionInput.cs"), encoding="utf-8").read()
    m = re.search(r"const string " + name + r"\s*=\s*((?:\s*\+?\s*" + STR + r")+)\s*;", src, re.S)
    return cs_string(m.group(1)) if m else ""


def split_args(s):
    """Top-level comma split of an argument list."""
    out, depth, cur, i = [], 0, "", 0
    while i < len(s):
        ch = s[i]
        if ch == '"':
            m = re.match(STR, s[i:])
            cur += m.group(0)
            i += len(m.group(0))
            continue
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
        i += 1
    if cur.strip():
        out.append(cur.strip())
    return out


def calls(body, prefix):
    """Yields (method, args) for every `prefix.Method(...)` call in order, with balanced parens."""
    for m in re.finditer(re.escape(prefix) + r"\.(\w+)\(", body):
        i = m.end()
        depth, j = 1, i
        while depth and j < len(body):
            if body[j] == '"':
                j += len(re.match(STR, body[j:]).group(0))
                continue
            depth += {"(": 1, ")": -1}.get(body[j], 0)
            j += 1
        yield m.start(), m.group(1), body[i:j - 1]


def method_body(src, name):
    m = re.search(r"override void " + name + r"\([^)]*\)\s*\{", src)
    if not m:
        return ""
    i, depth = m.end(), 1
    while depth:
        if src[i] == '"':
            i += len(re.match(STR, src[i:]).group(0))
            continue
        depth += {"{": 1, "}": -1}.get(src[i], 0)
        i += 1
    return src[m.end():i - 1]


def default_of(arg):
    arg = arg.strip()
    if arg.startswith("new "):
        return arg
    return arg


def params(body, is_input):
    out = []
    events = []
    for pos, meth, args in calls(body, "pManager"):
        events.append((pos, "call", meth, args))
    for m in re.finditer(r"\bRegister(SectionInputs|NetInput|NetOutput)\(pManager(.*?)\);", body):
        events.append((m.start(), "helper", m.group(1), m.group(2)))
    for m in re.finditer(r"pManager\[(\d+)\]\.Optional\s*=\s*true", body):
        events.append((m.start(), "opt", int(m.group(1)), None))
    for m in re.finditer(r"foreach\s*\(int \w+ in new\[\]\s*\{([\d,\s]+)\}\)\s*pManager\[\w+\]\.Optional\s*=\s*true", body):
        for k in m.group(1).split(","):
            events.append((m.start(), "opt", int(k), None))
    for m in re.finditer(r"pManager\[pManager\.ParamCount - 1\]\.Optional\s*=\s*true", body):
        events.append((m.start(), "optlast", None, None))
    events.sort(key=lambda e: e[0])
    optional = set()
    for _, kind, meth, args in events:
        if kind == "call":
            if meth == "AddParameter":
                a = split_args(args)
                typ = "Mite Net" if "MiteNetParameter" in a[0] else a[0]
                name, nick, desc, access = cs_string(a[1]), cs_string(a[2]), cs_string(a[3]), a[4].split(".")[-1]
                out.append(dict(name=name, nick=nick, type=typ, desc=desc, access=access, default=None))
                continue
            m = re.match(r"Add(\w+)Parameter$", meth)
            if not m:
                continue
            a = split_args(args)
            typ = m.group(1)
            p = dict(name=cs_string(a[0]), nick=cs_string(a[1]), type=typ, desc=cs_string(a[2]),
                     access=a[3].split(".")[-1], default=default_of(a[4]) if len(a) > 4 else None)
            out.append(p)
        elif kind == "helper":
            extra = cs_string(args) if args and '"' in args else None
            if meth == "SectionInputs":
                out.append(dict(name="Shape", nick="Sh", type="Integer", desc=const("ShapeDescription"), access="item", default="0"))
                out.append(dict(name="Section", nick="Sc", type="Curve", desc=const("SectionDescription"), access="item", default=None))
                optional.add(len(out) - 1)
            elif meth == "NetInput":
                d = const("NetInputDescription") + (". " + extra if extra else "")
                out.append(dict(name="Net", nick="Nt", type="Mite Net", desc=d, access="item", default=None))
                optional.add(len(out) - 1)
            elif meth == "NetOutput":
                d = ("The whole net in one wire (surface + both families): plug it into Net Joints, Net Topology, Lath Sweep, "
                     "Lath Analysis, Gridshell Analysis, Lath Segment, Lath Unroll, Lath Labels or Net Kinetics; "
                     "Mite Net adds a profile and reports the lath ends") + (". " + extra if extra else "")
                out.append(dict(name="Net", nick="Nt", type="Mite Net", desc=d, access="item", default=None))
        elif kind == "opt":
            optional.add(meth)
        elif kind == "optlast":
            optional.add(len(out) - 1)
    if is_input:
        for i in optional:
            if i < len(out):
                out[i]["optional"] = True
    return out


def component(path):
    src = open(path, encoding="utf-8").read()
    m = re.search(r":\s*base\((.*?)\)\s*\{\s*\}", src, re.S)
    if not m:
        return None
    a = split_args(m.group(1))
    guid = re.search(r'ComponentGuid\s*=>\s*new\("([0-9A-Fa-f-]+)"\)', src)
    return dict(file=os.path.basename(path), name=cs_string(a[0]), nick=cs_string(a[1]), desc=cs_string(a[2]),
                tab=cs_string(a[3]), icon=cs_string(a[4]), guid=guid.group(1) if guid else None,
                inputs=params(method_body(src, "RegisterInputParams"), True),
                outputs=params(method_body(src, "RegisterOutputParams"), False))


def main():
    old = json.load(open(os.path.join(HERE, "components.json"), encoding="utf-8"))
    order = [c["name"] for c in old]
    comps = [c for c in (component(os.path.join(COMP_DIR, f)) for f in sorted(os.listdir(COMP_DIR)) if f.endswith(".cs")) if c]
    comps.sort(key=lambda c: order.index(c["name"]) if c["name"] in order else len(order))
    with open(os.path.join(HERE, "components.json"), "w", encoding="utf-8") as f:
        json.dump(comps, f, ensure_ascii=False, indent=1)
    print(len(comps), "components written")


if __name__ == "__main__":
    main()
