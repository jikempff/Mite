#!/usr/bin/env python3
"""The Food4Rhino icon sheet: every component's icon by panel, 1600 x 1000.

    python3 tools/icon-sheet.py out.png        # needs Python Playwright (Chromium)

Reads the panels and their components from the README's "Components appear
under the Mite tab" list and the version from MiteInfo.cs, so the sheet
follows the release; the icons are the 2x copies the web app serves
(src/Mite.Web/wwwroot/icons), drawn at 64 px. Fails when a
component has no icon or the count disagrees with the README's.
"""
import base64, html, pathlib, re, sys

root = pathlib.Path(__file__).resolve().parent.parent
icons = root / "src/Mite.Web/wwwroot/icons"
# component name -> icon file stem where the two differ
STEM = {
    "Curvature Streamlines": "Streamlines", "Planarize Mesh": "Planarization",
    "Force Density Method": "ForceDensity", "AGG Web": "AggWeb",
}

readme = (root / "README.md").read_text(encoding="utf-8")
block = readme.split("Components appear under the **Mite** tab:", 1)[1]
panels = re.findall(r"^- \*\*(.+?)\*\* — (.+)$", block.split("\n\n", 2)[1], re.M)
version = re.search(r'Version => "([\d.]+)"', (root / "src/Mite.Grasshopper/MiteInfo.cs").read_text()).group(1)
stated = int(re.search(r"Grasshopper plugin \((\d+) components\)", readme).group(1))
count = sum(len(c.split(", ")) for _, c in panels)
if count != stated:
    sys.exit(f"README lists {count} components by panel but states {stated}")


def icon(name):
    f = icons / (STEM.get(name, name.replace(" ", "")) + ".png")
    if not f.exists():
        sys.exit(f"no icon for {name}: {f}")
    return "data:image/png;base64," + base64.b64encode(f.read_bytes()).decode()


# rows that fit 1600 px: Curvature + Form Finding, Gridshells, Analysis + Kinetics, Fabrication, Util
ORDER = ["Curvature", "Form Finding", "Gridshells", "Analysis", "Kinetics", "Fabrication", "Util"]
panels.sort(key=lambda pc: ORDER.index(pc[0]) if pc[0] in ORDER else len(ORDER))
WORDS = {5: "five", 6: "six", 7: "seven", 8: "eight", 9: "nine"}
cells = []
for panel, comps in panels:
    items = "".join(
        f'<div class="c"><img src="{icon(n)}" alt=""><span>{html.escape(n)}</span></div>' for n in comps.split(", "))
    cells.append(f'<section style="--n:{len(comps.split(", "))}"><h2>{html.escape(panel.upper())}</h2><div class="row">{items}</div></section>')

page = f"""<!doctype html><meta charset="utf-8"><style>
body{{margin:0;width:1600px;height:1000px;background:#fff;color:#111;font:15px/1.2 Inter,Helvetica,Arial,sans-serif}}
main{{padding:28px 60px 20px}}
h1{{font-size:44px;margin:0 0 10px;letter-spacing:-.5px}}
p{{margin:0 0 22px;color:#666;font-size:19px}}
.grid{{display:flex;flex-wrap:wrap;column-gap:32px;row-gap:18px}}
section{{flex:0 0 calc(var(--n) * 144px)}}
h2{{font-size:14px;font-weight:500;color:#555;margin:0 8px 6px;padding-bottom:8px;border-bottom:1px solid #ddd;letter-spacing:.3px}}
.row{{display:flex}}
.c{{width:144px;height:114px;display:flex;flex-direction:column;align-items:center;gap:8px;padding-top:8px}}
.c img{{width:64px;height:64px}}
.c span{{text-align:center;font-size:15px;max-width:136px}}
</style><main><h1>Mite {version}</h1>
<p>{count} components in {WORDS.get(len(panels), len(panels))} panels — black, white and neon: family A blue, family B red, geodesics green</p>
<div class="grid">{''.join(cells)}</div></main>"""

out = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "10-component-icons.png").resolve()
from playwright.sync_api import sync_playwright  # noqa: E402

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 1600, "height": 1000})
    pg.set_content(page)
    over = pg.evaluate("document.querySelector('main').scrollHeight > 1000 || document.querySelector('main').scrollWidth > 1600")
    pg.screenshot(path=str(out))
    b.close()
if over:
    sys.exit(f"the sheet overflows 1600 x 1000: {out}")
print(f"{out}: Mite {version}, {count} components in {len(panels)} panels")
