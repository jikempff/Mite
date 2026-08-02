# Mite.SmokeTest

Headless smoke test for the Grasshopper component layer. Boots Rhino via
Rhino.Inside, instantiates every Mite component with real Rhino meshes, solves
them, and checks the outputs — the runtime behavior the unit tests cannot reach
(parameter registration, icon resources, mesh conversion, data trees).

Requires a licensed Rhino 8 at `C:\Program Files\Rhino 8`. Not part of the
solution build; run it manually after component changes:

```powershell
cd tools/Mite.SmokeTest
dotnet run
```

Exit code 0 means all checks passed.
