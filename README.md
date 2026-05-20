# MeshCurvKit

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
- **Minimal Surface** — cotangent Laplacian flow with fixed boundaries
- **Force Density Method** — equilibrium solving for cable nets and shells

### Dynamics
- Spring, gravity, drag, smoothness, and area minimization forces
- Explicit Euler integration

## Projects

| Project | Target | Description |
|---------|--------|-------------|
| `MeshCurvKit.Core` | net10.0 | Core library, no Rhino dependency |
| `MeshCurvKit.Grasshopper` | net48 | Grasshopper plugin (7 components) |
| `MeshCurvKit.Tests` | net10.0 | Unit tests against analytic surfaces |

## Quick Start

```csharp
using MeshCurvKit.Core.Geometry;
using MeshCurvKit.Core.Curvature;

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

## Grasshopper Components

Drop `MeshCurvKit.Grasshopper.gha` into your Grasshopper Libraries folder. Components appear under the **MeshCurvKit** tab:

- **Curvature** — Principal Curvature, Gaussian Curvature, Mean Curvature, Curvature Streamlines
- **FormFinding** — Planarize Mesh, Minimal Surface, Force Density Method

## License

MIT
