# Mite Bench generator

Builds the review page ("Mite Bench"): every component with its inputs and
outputs, what to expect, self-tests run on the real Mite.Core library, and
live 3D/2D views of the actual output on analytic shapes.

```bash
cd tools/webgen
python3 extract_components.py   # components.json + icons.json from the sources
dotnet run -c Release      # runs the library, writes scenes.json + numbers.json
python3 build.py           # assembles site/index.html from template.html + notes.py
```

- `Program.cs` — runs the algorithms on the test shapes and records geometry
  (`scenes.json`) and measurements (`numbers.json`).
- `components.json` / `icons.json` — inputs/outputs and icons of the
  Grasshopper components, extracted from the component sources by
  `python3 extract_components.py` (run it whenever parameters change; after
  merging branches that touch components, regenerate instead of hand-merging).
- Mode menus: `Program.cs` records `Mite.Core.Modes.ModeCatalog` into
  `numbers.json` (`N.modes`); the tables show each mode input's menu, each card
  its Message at the defaults, and `notes.py` adds one self-test per component
  that the menu matches the parameter table.
- `notes.py` — per component: which demo to show, "what to expect" bullets,
  and self-test checks as JavaScript expressions over `N` (numbers) and `SC` (scenes).
- `template.html` / `build.py` — page shell and assembly.

`site/index.html` is self-contained (≈1 MB) and is published as a Claude
artifact; open it locally in any browser as well.
