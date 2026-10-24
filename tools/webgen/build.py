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
COMP_ORDER = [
    "Principal Curvature",
    "Gaussian Curvature",
    "Mean Curvature",
    "Curvature Streamlines",
    "Umbilics",
    "Planarize Mesh",
    "Minimal Surface",
    "Force Density Method",
    "Dynamic Relaxation",
    "Asymptotic Net",
    "Geodesic Net",
    "Chebyshev Net",
    "Conjugate Net",
    "Geodesic Path",
    "Net Trim",
    "Mite Net",
    "Asymptotic Web",
    "Lath Analysis",
    "Gridshell Analysis",
    "Net Distributions",
    "Mesh Isocurves",
    "Lath Sweep",
    "Net Joints",
    "Lath Unroll",
    "Lath Strip",
    "Lath Segment",
    "Lath Labels",
    "Lath Preview",
    "Flat Kit",
    "Net Topology",
    "Net Kinetics",
    "Mesh Cleanup",
    "Pull To Mesh",
    "Mesh Colour Map",
]
COMP_ORDER = ["Principal Curvature", "Gaussian Curvature", "Mean Curvature", "Curvature Streamlines", "Umbilics", "Test Shape",
              "Planarize Mesh", "Minimal Surface", "Force Density Method", "Dynamic Relaxation",
              "Asymptotic Net", "Geodesic Net", "Chebyshev Net", "Conjugate Net", "Geodesic Path",
              "Lath Analysis", "Gridshell Analysis", "Mesh Isocurves",
              "Lath Sweep", "Net Joints", "Lath Unroll", "Lath Segment", "Lath Labels", "Lath Preview", "Flat Kit", "Net Topology",
              "Net Kinetics",
              "Mesh Cleanup", "Pull To Mesh", "Mesh Colour Map"]
byname = {c["name"]: c for c in comps}
assert set(byname) == set(COMP_ORDER), set(byname) ^ set(COMP_ORDER)

def esc(s): return html.escape(s, quote=True)
def slug(s): return s.lower().replace(" ", "-")

def fmt_default(p):
    d = p.get("default")
    if d is None: return "—"
    d = d.replace("Vector3d.XAxis", "1,0,0").replace("Vector3d.ZAxis", "0,0,1").replace("Vector3d.Zero", "0,0,0").replace("new Vector3d(0, 0, -1000)", "0,0,−1000")
    d = d.replace("Math.PI / 2", "90°").replace('"L"', "L")
    return d

TYPE_LABEL = {"Mesh": "mesh", "Integer": "int", "Number": "num", "Vector": "vec", "Boolean": "bool", "Point": "pt", "Curve": "crv",
              "Angle": "angle", "Text": "text", "Plane": "plane", "Box": "box", "Line": "line", "Colour": "colour", "Interval": "domain", "Mite Net": "net"}

MODES = numbers.get("modes", {}).get("byComponent", {})


def menu_html(comp, p):
    """The right-click menu of a mode input (Mite.Core.Modes.ModeCatalog): options, default marked."""
    m = next((x for x in MODES.get(comp, {}).get("modes", []) if x["input"] == p["name"]), None)
    if not m: return ""
    opts = " ".join(f'<span class="mo{" on" if o["v"] == m["def"] else ""}" title="{o["v"]}">{esc(o["label"])}</span>' for o in m["options"])
    when = ""
    if m.get("activeWhen"):
        ctrl = next(x for x in MODES[comp]["modes"] if x["input"] == m["activeWhen"])
        lab = next(o["label"] for o in ctrl["options"] if o["v"] == m["activeValue"])
        when = f'<span class="mw">only with {esc(ctrl["input"])}: {esc(lab)}</span>'
    return f'<div class="menu"><span class="ml">menu</span>{opts}{when}</div>'


def param_rows(ps, comp=None):
    rows = []
    for p in ps:
        acc = {"item": "", "list": "list", "tree": "tree"}[p["access"]]
        opt = " optional" if p.get("optional") else ""
        rows.append(f'<tr><td class="pn"><span class="nick">{esc(p["nick"])}</span> {esc(p["name"])}</td>'
                    f'<td class="pt"><span class="ty">{TYPE_LABEL.get(p["type"], p["type"].lower())}{(" · " + acc) if acc else ""}</span>{("<span class=opt>optional</span>" if opt else "")}</td>'
                    f'<td class="pd">{esc(p["desc"])}{menu_html(comp, p) if comp else ""}</td><td class="pv">{esc(fmt_default(p))}</td></tr>')
    return "\n".join(rows)

def msg_html(comp):
    """The component's Message line at its defaults, as Grasshopper draws it under the component."""
    m = MODES.get(comp)
    return f'<span class="ghmsg" title="Grasshopper shows the active modes under the component; right-click it to change them">{esc(m["message"])}</span>' if m else ""


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
            kind = "palette" if demo == "palette" else "hist" if note.get("hist") else "plot2d" if note.get("plot2d") else "plot1d" if note.get("plot1d") else "sheet" if note.get("sheet") else "sparkline" if demo == "lathUtil" else "3d", kind = "plot2d" if note.get("plot2d") else "plot1d" if note.get("plot1d") else "plotKit" if note.get("plotKit") else "sparkline" if demo == "lathUtil" else "3d",
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
            if note.get("sheet"):
                controls = '<div class="demo-ctl"><span class="ctl-l">sheet</span><select></select></div>'
            if demo == "torusK":
            if demo == "curv":
                # curvature against the exact truth: shape × quantity × (measured | exact | error)
                mode = note.get("demoMode", "K")
                shape = note.get("demoShape", "torus")
                shapes = [("torus", "torus"), ("sphere", "sphere"), ("ellipsoid", "ellipsoid"), ("hyperboloid", "hyperboloid"),
                          ("catenoid", "catenoid"), ("monkey", "monkey saddle"), ("cone", "cone"), ("wave", "wave")]
                controls = ('<div class="demo-ctl" style="max-width:calc(100% - 160px)"><span class="ctl-l">shape</span>' + "".join(
                    f'<label><input type="radio" name="sh-{slug(n)}" value="{key}" {"checked" if key == shape else ""}> {lbl}</label>'
                    for key, lbl in shapes) +
                    '<span class="ctl-l" style="flex-basis:100%"></span><span class="ctl-l">show</span>' + "".join(
                    f'<label><input type="radio" name="tk-{slug(n)}" value="{m}" {"checked" if m == mode else ""}> {lbl}</label>'
                    for m, lbl in [("K", "K"), ("H", "H"), ("k1", "k1"), ("k2", "k2")]) + '</div>'
            if demo == "palette":
                controls = '<div class="demo-ctl"><span class="ctl-l">map</span>' + "".join(
                    f'<label><input type="radio" name="map-{slug(n)}" value="{k}" {"checked" if k == "div" else ""}> {lbl}</label>'
                    for k, lbl in [("div", "diverging"), ("seq", "sequential"), ("util", "utilization")]) + \
                    '<span class="ctl-l" style="margin-left:8px">colours</span>' + "".join(
                    f'<label><input type="radio" name="ver-{slug(n)}" value="{k}" {"checked" if k == "new" else ""}> {lbl}</label>'
                    for k, lbl in [("new", "Mite palette"), ("old", "1.3.0")]) + '</div>'
                    for m, lbl in [("K", "K"), ("H", "H"), ("k1", "k1"), ("k2", "k2"), ("um", "|k1 − k2|")]) +
                    '<span class="ctl-l">·</span>' + "".join(
                    f'<label><input type="radio" name="vw-{slug(n)}" value="{m}" {"checked" if m == note.get("demoView", "measured") else ""}> {lbl}</label>'
                    for m, lbl in [("measured", "measured"), ("truth", "exact"), ("error", "|error|")]) + '</div>')
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
    <div><h3>{esc(n)} <span class="nickname">{esc(c["nick"])}</span>{msg_html(n)}</h3><p class="desc">{esc(c["desc"])}</p></div>
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
      <div class="tbl"><table><thead><tr><th>Param</th><th>Type</th><th>Meaning</th><th>Default</th></tr></thead><tbody>{param_rows(c["inputs"], n)}</tbody></table></div></details>
      <details open><summary>Outputs <span class="count">{len(c["outputs"])}</span></summary>
      <div class="tbl"><table><thead><tr><th>Param</th><th>Type</th><th>Meaning</th><th></th></tr></thead><tbody>{param_rows(c["outputs"])}</tbody></table></div></details>
      <label class="notes-l" for="note-{slug(n)}">Your notes</label>
      <textarea id="note-{slug(n)}" data-note="{slug(n)}" rows="3" placeholder="What is unclear, wrong, or missing?"></textarea>
    </div>
  </div>
</article>''')
    cards.append("</section>")

page = open("template.html").read()
# favicon: the K mark of kempffsele.me (tools/webgen/favicon.svg), inlined so the single-file page keeps it
import base64, os
if os.path.exists("favicon.svg"):
    fav = base64.b64encode(open("favicon.svg", "rb").read()).decode()
    page = page.replace('<meta charset="utf-8">', '<meta charset="utf-8">\n<link rel="icon" type="image/svg+xml" href="data:image/svg+xml;base64,' + fav + '">', 1)
from palette_css import palette_vars
page = page.replace("/*__PALETTE_LIGHT__*/", palette_vars(numbers["palette"], "light")).replace("/*__PALETTE_DARK__*/", palette_vars(numbers["palette"], "dark"))
page = page.replace("/*__NAV__*/", "".join(nav)).replace("/*__CARDS__*/", "".join(cards))
page = page.replace("/*__SCENES__*/", json.dumps(scenes, separators=(",", ":"))).replace("/*__NUMBERS__*/", json.dumps(numbers)).replace("/*__ORDER__*/", json.dumps([slug(n) for n in COMP_ORDER]))
page = page.replace('<span id="total">28</span>', f'<span id="total">{len(COMP_ORDER)}</span>')
# review tools (find / filter, keyboard, GitHub issue draft) live in their own files and are
# appended here instead of being edited into template.html; the script runs after the page script
if os.path.exists("review.js"):
    page = page.replace("</style>", open("review.css").read() + "</style>", 1)
    page += "\n<script>\n" + open("review.js").read() + "</script>\n"
open("site/index.html", "w").write(page)
print("bytes", len(page))
