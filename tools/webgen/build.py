import json, html
from notes import NOTES

comps = json.load(open("components.json"))
scenes = json.load(open("scenes.json"))
numbers = json.load(open("numbers.json"))
icons = json.load(open("icons.json"))

TAB_ORDER = ["Curvature", "Form Finding", "Gridshells", "Analysis", "Fabrication", "Util"]
TAB_BLURB = {
    "Curvature": "Per-vertex differential geometry. Every output is one item per input mesh vertex, so it wires straight into Mesh Colours.",
    "Form Finding": "Equilibrium shapes on a fast sparse solver. Leave Fixed empty to pin the boundary.",
    "Gridshells": "Curve networks on the mesh. Curves are continuous border to border by default (Continuous input); step, spacing and step count default to 0 = derived from the mesh.",
    "Analysis": "Can it be built, and does it stand up.",
    "Fabrication": "From curves to parts: solids, joints, cutting patterns, stock pieces, labels.",
    "Util": "Mesh intake, projection and colouring helpers.",
}
COMP_ORDER = ["Principal Curvature", "Gaussian Curvature", "Mean Curvature", "Curvature Streamlines", "Umbilics",
              "Planarize Mesh", "Minimal Surface", "Force Density Method", "Dynamic Relaxation",
              "Asymptotic Net", "Geodesic Net", "Chebyshev Net", "Conjugate Net", "Geodesic Path",
              "Lath Analysis", "Gridshell Analysis", "Mesh Isocurves",
              "Lath Sweep", "Net Joints", "Lath Unroll", "Lath Segment", "Lath Labels", "Lath Preview", "Net Topology",
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
            if demo == "torusK":
                mode = note.get("demoMode", "K")
                controls = '<div class="demo-ctl"><span class="ctl-l">show</span>' + "".join(
                    f'<label><input type="radio" name="tk-{slug(n)}" value="{m}" {"checked" if m == mode else ""}> {lbl}</label>'
                    for m, lbl in [("K", "K"), ("H", "H"), ("k1", "k1"), ("k2", "k2")]) + '</div>'
            hint = "drag to rotate · scroll to zoom" if kind == "3d" else ""
            demo_html = f'<div class="demo" data-demo="{demo}" data-kind="{kind}" data-id="{slug(n)}">{controls}<canvas></canvas><div class="demo-legend" id="lg-{slug(n)}"></div><div class="demo-hint">{hint}</div></div>'
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

page = open("template.html").read()
page = page.replace("/*__NAV__*/", "".join(nav)).replace("/*__CARDS__*/", "".join(cards))
page = page.replace("/*__SCENES__*/", json.dumps(scenes, separators=(",", ":"))).replace("/*__NUMBERS__*/", json.dumps(numbers)).replace("/*__ORDER__*/", json.dumps([slug(n) for n in COMP_ORDER]))
open("site/index.html", "w").write(page)
print("bytes", len(page))
