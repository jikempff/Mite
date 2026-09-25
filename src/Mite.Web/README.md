# Mite.Web — Mite in the browser

The interactive page served at [kempffsele.me/mite](https://kempffsele.me/mite):
Mite.Core compiled to WebAssembly (AOT) behind a three.js viewport. Same
algorithms, same numbers as the Grasshopper plugin; nothing is uploaded.

- `Api.cs` — the JS-callable surface (`[JSExport]`): analytic shapes, mesh
  load + weld, curvature, nets, lath analysis + unroll + sweep, frame analysis.
- `wwwroot/js/runtime.js` — boots the .NET runtime, fetches the `.gz`
  companions and inflates them in the browser (static hosts do not compress
  `.wasm`).
- `wwwroot/js/viewer.js` — three.js scene: vertex-colour analysis, zebra
  shader, fat-line curve families, picking, draggable loft handles.
- `wwwroot/js/app.js` — panel wiring; `loft.js`, `loaders.js` (OBJ/STL/PLY),
  `plots.js`, `export.js`, `colormaps.js`.
- `wwwroot/vendor` — three.js r170 (MIT).

Build and deploy:

```bash
dotnet workload install wasm-tools
tools/build-web.sh ../kempffseleme/mite     # Mac / Linux → static site folder, ~4 MB, ~2 MB over the wire
```

```powershell
.\tools\build-web.ps1 ..\kempffseleme\mite   # Windows (PowerShell; `bash` there is usually WSL without dotnet)
```

The target is the `mite/` folder of the site repo, github.com/jikempff/kempffseleme
(GitHub Pages, custom domain kempffsele.me): clone it next to Mite, build into
it, then `git add mite && git commit && git push` in that repo.

Local run: `python3 -m http.server` inside the output folder and open
`index.html` (ES modules need http, not file://).

Smoke test (Playwright, headless): `node tools/web-smoke.js http://localhost:8765`
— boot race, mid-trace shape switches, component strips and icons. The
component icons under `wwwroot/icons` are the plugin's, written by
`tools/generate_icons.py` at 48 px.
