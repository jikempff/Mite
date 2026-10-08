# Mite Bench generator

Builds the review page ("Mite Bench"): every component with its inputs and
outputs, what to expect, self-tests run on the real Mite.Core library, and
live 3D/2D views of the actual output on analytic shapes.

```bash
cd tools/webgen
python3 extract_components.py   # components.json + icons.json from the sources
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
- `components.json` — inputs/outputs of the 29 Grasshopper components,
  generated from the component sources by `python3 extract_components.py`
  (run it whenever a parameter changes; it expands the shared Shape/Section
  and Net helpers of `MiteComponent.cs`).
- `notes.py` — per component: which demo to show, "what to expect" bullets,
  and self-test checks as JavaScript expressions over `N` (numbers) and `SC` (scenes).
- `template.html` / `build.py` — page shell and assembly.
- `review.js` / `review.css` — the review tools (find box and facets with
  counts, keyboard shortcuts, GitHub issue draft); `build.py` appends them to
  the page. Their pure functions are tested in
  `tests/Mite.Tests/BenchReviewTests.cs`.
- `bench-smoke.js` — headless check of the built page: every self-test chip
  PASS, then the find box, facets, keys and issue link driven in Chromium:
  `node bench-smoke.js site/index.html [light|dark]` (needs `playwright`).

`site/index.html` is self-contained (≈1 MB) and is published as a Claude
artifact; open it locally in any browser as well.
