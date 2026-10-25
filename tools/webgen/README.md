# Mite Bench generator

Builds the review page ("Mite Bench"): every component with its inputs and
outputs, what to expect, self-tests run on the real Mite.Core library, and
live 3D/2D views of the actual output on analytic shapes.

```bash
cd tools/webgen
dotnet run -c Release      # runs the library, writes scenes.json + numbers.json
python3 build.py           # assembles site/index.html from template.html + notes.py
```

- `Program.cs` — runs the algorithms on the test shapes and records geometry
  (`scenes.json`) and measurements (`numbers.json`).
- `components.json` — inputs/outputs of the 28 Grasshopper components
  (extracted from the component sources; regenerate when parameters change).
- `notes.py` — per component: which demo to show, "what to expect" bullets,
  and self-test checks as JavaScript expressions over `N` (numbers) and `SC` (scenes).
- `template.html` / `build.py` — page shell and assembly. `build.py` also
  measures the component icons (`icons.json`, written by
  `tools/generate_icons.py` together with `tools/icon_palette.json`) and puts
  the icon sheet with its three self-tests — size and border, palette,
  legibility on a light capsule and a dark menu — above the cards.

`site/index.html` is self-contained (≈1 MB) and is published as a Claude
artifact; open it locally in any browser as well.
