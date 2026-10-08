# Mite Bench generator

Builds the review page ("Mite Bench"): every component with its inputs and
outputs, what to expect, self-tests run on the real Mite.Core library, and
live 3D/2D views of the actual output on analytic shapes.

```bash
cd tools/webgen
dotnet run -c Release      # runs the library, writes scenes.json + numbers.json
python3 build.py           # assembles site/index.html from template.html + notes.py
cd ../.. && node tools/webgen/selftest.js --min 71   # all self-tests, light and dark
```

`selftest.js` opens the page in headless Chromium once per colour scheme and
prints `light: 71 PASS, 0 FAIL, 0 not run, 0 page errors`; it exits 1 on any
FAIL, a check that did not run, a page error, or fewer checks than `--min`.
It needs Playwright (`npm i playwright`, or `NODE_PATH` pointing at an
install; `CHROMIUM_PATH` selects a browser binary). CI
(`.github/workflows/ci.yml`) runs it on every push and keeps the page as a
build artifact; raise `--min` there when `notes.py` gains checks.

- `Program.cs` — runs the algorithms on the test shapes and records geometry
  (`scenes.json`) and measurements (`numbers.json`).
- `components.json` — inputs/outputs of the 28 Grasshopper components
  (extracted from the component sources; regenerate when parameters change).
- `notes.py` — per component: which demo to show, "what to expect" bullets,
  and self-test checks as JavaScript expressions over `N` (numbers) and `SC` (scenes).
- `template.html` / `build.py` — page shell and assembly.

`site/index.html` is self-contained (≈1 MB) and is published as a Claude
artifact; open it locally in any browser as well.
