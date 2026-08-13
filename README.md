# Mite

Open-source C# toolkit for mesh curvature analysis and form-finding. Pure .NET with zero native dependencies — runs on Windows, macOS, and Linux.

## Features

### Curvature Analysis
- **Principal Curvature** — k1, k2 values and directions per vertex (Rusinkiewicz 2004)
- **Gaussian Curvature** — angle deficit method with mixed Voronoi areas
- **Mean Curvature** — cotangent Laplacian

### Streamlines
- **Curvature Streamlines** — RK4 integration along principal curvature directions

### Form-Finding
- **Planarization** — iterative quad mesh planarization
- **Minimal Surface** — exact cotangent Laplace solves with frozen weights (Pinkall–Polthier style); converges in a few iterations instead of thousands of flow steps
- **Force Density Method** — equilibrium solving for cable nets and shells, with singular-system detection

### Gridshells
- **Asymptotic Net** — both families of asymptotic curves (zero normal curvature) for asymptotic gridshells, with optional evenly-spaced auto-seeding
- **Geodesic Net** — straightest geodesics traced on the mesh for geodesic (lath) gridshells, with optional evenly-spaced auto-seeding
- **Chebyshev Net** — equal-edge-length nets by the compass method: the kinematics of elastic gridshells bent from flat lattices
- **Lath Analysis** — buildability check for strip laths: Darboux-frame decomposition (geodesic curvature, normal curvature, geodesic torsion) converted to bending strains against a material limit

### Fabrication
- **Lath Sweep** — extrudes on-surface curves (geodesic, asymptotic, streamline) into solid laths with a rectangular profile riding in the surface frame; flat mode for geodesic gridshells, upright (egg-crate) mode for asymptotic gridshells, with surface offset
- **Net Joints** — finds crossings between the lath families and builds lap-joint notch solids (half-lap for flat laths, slots for upright ones) with lap fraction and clearance, ready for boolean subtraction

### Dynamics
- Spring, gravity, drag, smoothness, and area minimization forces
- Explicit Euler integration

## Projects

| Project | Target | Description |
|---------|--------|-------------|
| `Mite.Core` | net10.0 + net48 | Core library, no Rhino dependency |
| `Mite.Grasshopper` | net48 | Grasshopper plugin (13 components) |
| `Mite.Tests` | net10.0 | Unit tests against analytic surfaces |

## Install

### Via Yak (recommended)

In the Rhino command line:

```
_PackageManager
```

Search for **mite** and click Install.

### Manual

Drop `Mite.Grasshopper.gha` and `Mite.Core.dll` into your Grasshopper Libraries folder.

## Quick Start

```csharp
using Mite.Core.Geometry;
using Mite.Core.Curvature;

// Load a mesh
var mesh = MeshData.LoadObj("model.obj");

// Compute principal curvatures
var result = PrincipalCurvature.Compute(mesh);
// result.K1, result.K2 — curvature values per vertex
// result.D1, result.D2 — curvature directions per vertex

// Compute Gaussian curvature
double[] K = GaussianCurvature.Compute(mesh);

// Compute mean curvature
var mean = MeanCurvature.Compute(mesh);
// mean.Values — scalar H per vertex
// mean.Normals — mean curvature normal per vertex
```

## Build

```bash
dotnet build
dotnet test
```

### Build Yak Package

```powershell
.\build-yak.ps1
```

This builds the Grasshopper project in Release mode, stages the files into `dist/`, and runs `yak build` to produce the `.yak` package. Requires the [Yak CLI](https://developer.rhino3d.com/guides/yak/the-package-manager-command-line-tool/).

## Grasshopper Components

Components appear under the **Mite** tab:

- **Curvature** — Principal Curvature, Gaussian Curvature, Mean Curvature, Curvature Streamlines
- **FormFinding** — Planarize Mesh, Minimal Surface, Force Density Method
- **Gridshells** — Asymptotic Net, Geodesic Net, Chebyshev Net, Lath Analysis
- **Fabrication** — Lath Sweep, Net Joints

A typical gridshell workflow: trace a **Geodesic Net** or **Asymptotic Net**, check the
strips with **Lath Analysis**, extrude them with **Lath Sweep**, and cut the crossings
with **Net Joints** notch solids.

## License

MIT
