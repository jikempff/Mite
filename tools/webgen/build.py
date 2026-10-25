import json, html
from notes import NOTES

comps = json.load(open("components.json"))
scenes = json.load(open("scenes.json"))
numbers = json.load(open("numbers.json"))
icons = json.load(open("icons.json"))

TAB_ORDER = ["Curvature", "Form Finding", "Gridshells", "Analysis", "Fabrication", "Kinetics", "Util"]
TAB_BLURB = {
    "Curvature": "Per-vertex differential geometry. Every output is one item per input mesh vertex, so it wires straight into Mesh Colours.",
    "Form Finding": "Equilibrium shapes on a fast sparse solver. Leave Fixed empty to pin the boundary.",
    "Gridshells": "Curve networks on the mesh. Curves are continuous border to border by default (Continuous input); step, spacing and step count default to 0 = derived from the mesh.",
    "Analysis": "Can it be built, and does it stand up.",
    "Fabrication": "From curves to parts: solids, joints, cutting patterns, stock pieces, labels.",
    "Kinetics": "Nets that move: scissor-jointed asymptotic grids as mechanisms.",
    "Util": "Mesh intake, projection and colouring helpers.",
}
COMP_ORDER = ["Principal Curvature", "Gaussian Curvature", "Mean Curvature", "Curvature Streamlines", "Umbilics",
              "Planarize Mesh", "Minimal Surface", "Force Density Method", "Dynamic Relaxation",
              "Asymptotic Net", "Geodesic Net", "Chebyshev Net", "Conjugate Net", "Geodesic Path",
              "Lath Analysis", "Gridshell Analysis", "Mesh Isocurves",
              "Lath Sweep", "Net Joints", "Lath Unroll", "Lath Segment", "Lath Labels", "Lath Preview", "Net Topology",
              "Net Kinetics",
              "Mesh Cleanup", "Pull To Mesh", "Mesh Colour Map"]
byname = {c["name"]: c for c in comps}
assert set(byname) == set(COMP_ORDER), set(byname) ^ set(COMP_ORDER)

def esc(s): return html.escape(s, quote=True)
def slug(s): return s.lower().replace(" ", "-")

def fmt_default(p):
    d = p.get("default")
    if d is None: return "—"
    d = d.replace("Vector3d.XAxis", "1,0,0").replace("Vector3d.Zero", "0,0,0").replace("new Vector3d(0, 0, -1000)", "0,0,−1000")
    d = d.replace("Math.PI / 2", "90°").replace('"L"', "L")
    return d

TYPE_LABEL = {"Mesh": "mesh", "Integer": "int", "Number": "num", "Vector": "vec", "Boolean": "bool", "Point": "pt", "Curve": "crv",
              "Angle": "angle", "Text": "text", "Plane": "plane", "Box": "box", "Line": "line", "Colour": "colour", "Interval": "domain"}

def param_rows(ps):
    rows = []
    for p in ps:
        acc = {"item": "", "list": "list", "tree": "tree"}[p["access"]]
        opt = " optional" if p.get("optional") else ""
        rows.append(f'<tr><td class="pn"><span class="nick">{esc(p["nick"])}</span> {esc(p["name"])}</td>'
                    f'<td class="pt"><span class="ty">{TYPE_LABEL.get(p["type"], p["type"].lower())}{(" · " + acc) if acc else ""}</span>{("<span class=opt>optional</span>" if opt else "")}</td>'
                    f'<td class="pd">{esc(p["desc"])}</td><td class="pv">{esc(fmt_default(p))}</td></tr>')
    return "\n".join(rows)

cards = []
nav = []
for tab in TAB_ORDER:
    names = [n for n in COMP_ORDER if byname[n]["tab"] == tab]
    nav.append(f'<div class="navtab"><div class="navtab-h">{esc(tab)}</div>' +
               "".join(f'<a href="#{slug(n)}" data-nav="{slug(n)}"><i class="dot"></i>{esc(n)}</a>' for n in names) + '</div>')
    cards.append(f'<section class="tab" id="tab-{slug(tab)}"><header class="tab-h"><h2>{esc(tab)}</h2><p>{esc(TAB_BLURB[tab])}</p></header>')
    for n in names:
        c = byname[n]; note = NOTES[n]
        demo = note.get("demo")
        demo_html = ""
        if demo:
            kind = "plot2d" if note.get("plot2d") else "plot1d" if note.get("plot1d") else "sparkline" if demo == "lathUtil" else "3d"
            controls = ""
            if note.get("before_after"):
                controls = '<div class="demo-ctl"><label><input type="radio" name="ba-' + slug(n) + '" value="before"> input</label><label><input type="radio" name="ba-' + slug(n) + '" value="after" checked> result</label><label><input type="radio" name="ba-' + slug(n) + '" value="both"> both</label></div>'
            if note.get("shapes"):
                controls = '<div class="demo-ctl"><span class="ctl-l">shape</span>' + "".join(
                    f'<label><input type="radio" name="sh-{slug(n)}" value="{key}" {"checked" if k == 0 else ""}> {lbl}</label>'
                    for k, (key, lbl) in enumerate(note["shapes"])) + '</div>'
            if note.get("slider"):
                controls = '<div class="demo-ctl">' + ("".join(
                    f'<label><input type="radio" name="sh-{slug(n)}" value="{key}" {"checked" if k == 0 else ""}> {lbl}</label>'
                    for k, (key, lbl) in enumerate(note.get("shapes", []))) + '<span class="ctl-l" style="flex-basis:100%"></span>' if note.get("shapes") else "") + \
                    f'<label style="flex-basis:100%"><span class="ctl-l">{esc(note["slider"])}</span><input type="range" min="0" max="1" step="0.001" value="1" style="flex:1"></label><span class="fold-label" style="flex-basis:100%"></span></div>'
            if demo == "torusK":
                mode = note.get("demoMode", "K")
                controls = '<div class="demo-ctl"><span class="ctl-l">show</span>' + "".join(
                    f'<label><input type="radio" name="tk-{slug(n)}" value="{m}" {"checked" if m == mode else ""}> {lbl}</label>'
                    for m, lbl in [("K", "K"), ("H", "H"), ("k1", "k1"), ("k2", "k2")]) + '</div>'
            hint = "drag to rotate · scroll to zoom" if kind == "3d" else ""
            demo_html = f'<div class="demo{" tall" if note.get("tall") else ""}" data-demo="{demo}" data-kind="{kind}" data-id="{slug(n)}">{controls}<canvas></canvas><div class="demo-legend" id="lg-{slug(n)}"></div><div class="demo-hint">{hint}</div></div>'
        checks_html = ""
        if note["checks"]:
            checks_html = '<ul class="checks">' + "".join(
                f'<li data-check="{esc(js)}"><span class="chip">…</span><b>{esc(label)}</b><span class="ct"></span></li>' for label, js in note["checks"]) + '</ul>'
        else:
            checks_html = '<p class="nocheck">No numeric self-test for this component; it is UI plumbing over outputs checked elsewhere.</p>'
        expect_html = "".join(f"<li>{esc(e)}</li>" for e in note["expect"])
        cards.append(f'''
<article class="card" id="{slug(n)}" data-name="{esc(n)}">
  <header class="card-h">
    <img src="{icons[c["icon"]]}" alt="" width="24" height="24" class="icon">
    <div><h3>{esc(n)} <span class="nickname">{esc(c["nick"])}</span></h3><p class="desc">{esc(c["desc"])}</p></div>
    <label class="ok"><input type="checkbox" data-ok="{slug(n)}"><span>Makes sense</span></label>
  </header>
  <div class="card-body">
    <div class="col-a">
      <h4>What to expect</h4>
      <ul class="expect">{expect_html}</ul>
      <h4>Self-test on the real library</h4>
      {checks_html}
      {demo_html}
    </div>
    <div class="col-b">
      <details open><summary>Inputs <span class="count">{len(c["inputs"])}</span></summary>
      <div class="tbl"><table><thead><tr><th>Param</th><th>Type</th><th>Meaning</th><th>Default</th></tr></thead><tbody>{param_rows(c["inputs"])}</tbody></table></div></details>
      <details open><summary>Outputs <span class="count">{len(c["outputs"])}</span></summary>
      <div class="tbl"><table><thead><tr><th>Param</th><th>Type</th><th>Meaning</th><th></th></tr></thead><tbody>{param_rows(c["outputs"])}</tbody></table></div></details>
      <label class="notes-l" for="note-{slug(n)}">Your notes</label>
      <textarea id="note-{slug(n)}" data-note="{slug(n)}" rows="3" placeholder="What is unclear, wrong, or missing?"></textarea>
    </div>
  </div>
</article>''')
    cards.append("</section>")

# ---- the component icons: the sheet on both canvases and the measurements behind its self-tests
# (the same rules as tests/Mite.Tests/IconTests.cs: 24 px, the 2 px border, every opaque pixel a palette
# colour, a shade or a blend of two, and a fifth of the silhouette band at 3 : 1 on a light capsule and on a dark menu)
def icon_metrics():
    try:
        from PIL import Image
    except ImportError:
        return None
    import os
    pal = json.load(open(os.path.join("..", "icon_palette.json")))
    allowed = [tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) for h in pal["allowed"]]

    def lum(c):
        def ch(v):
            v = v / 255
            return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
        return 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2])

    def contrast(a, b):
        la, lb = sorted((lum(a), lum(b)), reverse=True)
        return (la + 0.05) / (lb + 0.05)

    def segdist(p, a, b):
        ab = [b[i] - a[i] for i in range(3)]
        L2 = sum(x * x for x in ab)
        t = 0 if L2 == 0 else max(0.0, min(1.0, sum((p[i] - a[i]) * ab[i] for i in range(3)) / L2))
        return sum((p[i] - a[i] - ab[i] * t) ** 2 for i in range(3)) ** 0.5

    res_dir = os.path.join("..", "..", "src", "Mite.Grasshopper", "Resources")
    out = dict(count=0, notSquare=0, border=0, blend=0.0, offPalette=0, opaque=0, light=1.0, dark=1.0, worstLight="", worstDark="")
    for name in sorted(os.listdir(res_dir)):
        if not name.endswith(".png"):
            continue
        im = Image.open(os.path.join(res_dir, name)).convert("RGBA")
        out["count"] += 1
        if im.size != (24, 24):
            out["notSquare"] += 1
            continue
        px = im.load()
        cov = [[px[x, y][3] >= 128 for x in range(24)] for y in range(24)]
        band = okL = okD = 0
        for y in range(24):
            for x in range(24):
                r, g, b, a = px[x, y]
                if (x < 2 or y < 2 or x >= 22 or y >= 22):
                    out["border"] = max(out["border"], a)
                if a >= 250:
                    out["opaque"] += 1
                    d = min(segdist((r, g, b), p, q) for p in allowed for q in allowed)
                    if d > 12:
                        out["offPalette"] += 1
                    out["blend"] = max(out["blend"], d)
                if not cov[y][x]:
                    continue
                if not any(not cov[yy][xx] for yy in range(max(0, y - 3), min(24, y + 4)) for xx in range(max(0, x - 3), min(24, x + 4))):
                    continue
                band += 1
                for bg, key in (((0xD2,) * 3, "L"), ((0x2D,) * 3, "D")):
                    c = tuple(int(v * a / 255 + bgv * (1 - a / 255)) for v, bgv in zip((r, g, b), bg))
                    if contrast(c, bg) >= 3:
                        if key == "L":
                            okL += 1
                        else:
                            okD += 1
        if band:
            if okL / band < out["light"]:
                out["light"], out["worstLight"] = okL / band, name[:-4]
            if okD / band < out["dark"]:
                out["dark"], out["worstDark"] = okD / band, name[:-4]
    out["blend"] = round(out["blend"], 1)
    out["light"] = round(out["light"], 3)
    out["dark"] = round(out["dark"], 3)
    out["palette"] = pal["palette"]
    return out

icon_numbers = icon_metrics()
if icon_numbers:
    numbers["icons"] = icon_numbers
ICON_ORDER = [byname[n]["icon"] for n in COMP_ORDER] + sorted(k for k in icons if k != "Mite_Tab" and k not in {byname[n]["icon"] for n in COMP_ORDER}) + ["Mite_Tab"]
icon_sheet = "".join(f'<img src="{icons[k]}" alt="{esc(k)}" title="{esc(k)}" width="24" height="24">' for k in ICON_ORDER if k in icons)
ICON_CHECKS = [
    ("Every icon is 24 × 24 px and keeps the 2 px border (only the drop shadow enters it)",
     "[N.icons.notSquare===0&&N.icons.border<=64, N.icons.count+' icons, border alpha at most '+N.icons.border+' of 255']"),
    ("Black, white and neon only: every opaque pixel is a palette colour, a shade of one or an anti-aliased blend of two (crossings excepted)",
     "[N.icons.offPalette<=0.3*N.icons.opaque, N.icons.offPalette+' of '+N.icons.opaque+' opaque pixels are three-colour crossings; the rest within 12 (RGB) of a palette blend']"),
    ("Legible on a light capsule (#d2d2d2) and on a dark menu (#2d2d2d): a fifth of every icon's silhouette band contrasts 3 : 1 (WCAG 1.4.11)",
     "[N.icons.light>=0.2&&N.icons.dark>=0.2, 'weakest on light: '+N.icons.worstLight+' '+N.icons.light+'; on dark: '+N.icons.worstDark+' '+N.icons.dark]"),
]
icon_checks_html = '<ul class="checks">' + "".join(
    f'<li data-check="{esc(js)}"><span class="chip">…</span><b>{esc(label)}</b><span class="ct"></span></li>' for label, js in ICON_CHECKS) + '</ul>' if icon_numbers else \
    '<p class="nocheck">Icon metrics need Pillow (pip install pillow) at build time.</p>'
icons_html = f'''
<section class="iconsheet" id="icons">
  <h4>Component icons · black, white and neon</h4>
  <p>The Grasshopper icons in the Mite palette (tools/generate_icons.py): blue and red are the two lath families, green a geodesic family or a form-found mesh, yellow an analysis, white a surface or a cut sheet, black the marks. Shown on a light component capsule and on a dark menu, at 1 : 1 and 2 : 1.</p>
  <div class="strip light">{icon_sheet}</div>
  <div class="strip dark">{icon_sheet}</div>
  <div class="strip light big">{icon_sheet}</div>
  <div class="strip dark big">{icon_sheet}</div>
  {icon_checks_html}
</section>'''

page = open("template.html").read()
# favicon: the K mark of kempffsele.me (tools/webgen/favicon.svg), inlined so the single-file page keeps it
import base64, os
if os.path.exists("favicon.svg"):
    fav = base64.b64encode(open("favicon.svg", "rb").read()).decode()
    page = page.replace('<meta charset="utf-8">', '<meta charset="utf-8">\n<link rel="icon" type="image/svg+xml" href="data:image/svg+xml;base64,' + fav + '">', 1)
page = page.replace("/*__NAV__*/", "".join(nav)).replace("/*__CARDS__*/", "".join(cards)).replace("/*__ICONS__*/", icons_html)
page = page.replace("/*__SCENES__*/", json.dumps(scenes, separators=(",", ":"))).replace("/*__NUMBERS__*/", json.dumps(numbers)).replace("/*__ORDER__*/", json.dumps([slug(n) for n in COMP_ORDER]))
open("site/index.html", "w").write(page)
print("bytes", len(page))
