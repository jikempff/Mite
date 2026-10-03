# Extracts one Grasshopper component's name, description, inputs and outputs
# from its C# source as a components.json entry:
#   python3 extract_component.py ../../src/Mite.Grasshopper/Components/AsymptoticWebComponent.cs
import re, json, sys
src = open(sys.argv[1]).read()
def strings(expr):
    # concatenated C# string literals
    return "".join(json.loads('"' + m + '"') for m in re.findall(r'"((?:[^"\\]|\\.)*)"', expr))
def split_args(s):
    out, depth, cur, instr = [], 0, "", False
    i = 0
    while i < len(s):
        ch = s[i]
        if instr:
            cur += ch
            if ch == '\\': cur += s[i+1]; i += 1
            elif ch == '"': instr = False
        elif ch == '"': instr = True; cur += ch
        elif ch in "([": depth += 1; cur += ch
        elif ch in ")]": depth -= 1; cur += ch
        elif ch == ',' and depth == 0: out.append(cur.strip()); cur = ""
        else: cur += ch
        i += 1
    if cur.strip(): out.append(cur.strip())
    return out
def params(section):
    res = []
    for m in re.finditer(r'pManager\.Add(\w+)Parameter\((.*?)\);\s*$', section, re.S | re.M):
        typ, args = m.group(1), split_args(m.group(2))
        name, nick, desc = strings(args[0]), strings(args[1]), strings(args[2])
        access = args[3].split('.')[-1]
        default = args[4] if len(args) > 4 else None
        res.append(dict(name=name, nick=nick, type=typ, desc=desc, access=access, default=default))
    return res
ins = src[src.index("RegisterInputParams"):src.index("RegisterOutputParams")]
outs = src[src.index("RegisterOutputParams"):src.index("SolveInstance")]
inputs = params(ins)
for m in re.finditer(r'pManager\[(\d+)\]\.Optional = true', ins): inputs[int(m.group(1))]["optional"] = True
ctor = re.search(r':\s*base\((.*?)\)\s*\{\s*\}', src, re.S).group(1)
a = split_args(ctor)
guid = re.search(r'new\("([0-9A-Fa-f-]+)"\)', src).group(1)
entry = dict(file=sys.argv[1].split('/')[-1], name=strings(a[0]), nick=strings(a[1]), desc=strings(a[2]), tab=strings(a[3]), icon=strings(a[4]), guid=guid, inputs=inputs, outputs=params(outs))
print(json.dumps(entry, ensure_ascii=False))
