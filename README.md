# Mite

Open-source C# toolkit for mesh curvature analysis and form-finding. Pure .NET with zero native dependencies — runs on Windows, macOS, and Linux.

## Features

### Curvature Analysis
- **Principal Curvature** — k1, k2 values and directions per vertex (Rusinkiewicz 2004), with shape-tensor smoothing for consistent direction fields
- **Gaussian Curvature** — angle deficit method with mixed Voronoi areas
- **Mean Curvature** — cotangent Laplacian
- **Umbilics** — flags vertices where k1 ≈ k2 (direction fields are undefined there; key for clean net layouts)

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
- **Conjugate Net** — both principal families evenly spaced: an approximate conjugate net, the layout for planar-quad (PQ) panelization

### Analysis
- **Lath Analysis** — buildability check for strip laths: Darboux-frame decomposition (geodesic curvature, normal curvature, geodesic torsion) converted to bending strains against a material limit
- **Gridshell Analysis** — linear statics of the whole lath network as a coupled 3D beam frame: displacements and per-lath stress utilization (validated against Euler-Bernoulli theory)

### Fabrication
- **Lath Sweep** — extrudes on-surface curves (geodesic, asymptotic, streamline) into solid laths with a rectangular profile riding in the surface frame; flat mode for geodesic gridshells, upright (egg-crate) mode for asymptotic gridshells, with surface offset
- **Net Joints** — finds crossings between the lath families and builds lap-joint notch solids (half-lap for flat laths, slots for upright ones) with lap fraction and clearance, ready for boolean subtraction
- **Lath Unroll** — flat 2D cutting patterns from laths (exact per-triangle isometry), laid out in a row for CNC
- **Lath Segment** — splits laths to stock length, cuts kept away from joints, with half-lap splice notch solids
- **Lath Labels** — lath IDs, label anchor points, and a CSV bill of materials
- **Lath Preview** — color-codes laths by utilization (green → red)

### Utilities
- **Mesh Cleanup** — weld vertices, drop degenerate/duplicate faces, unify winding. Heal imported meshes before analysis

### Dynamics

### Dynamics
- Spring, gravity, drag, smoothness, and area minimization forces
- Explicit Euler integration

## Projects

| Project | Target | Description |
|---------|--------|-------------|
| `Mite.Core` | net10.0 + net48 | Core library, no Rhino dependency |
| `Mite.Grasshopper` | net48 | Grasshopper plugin (21 components) |
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

- **Curvature** — Principal Curvature, Gaussian Curvature, Mean Curvature, Curvature Streamlines, Umbilics
- **FormFinding** — Planarize Mesh, Minimal Surface, Force Density Method
- **Gridshells** — Asymptotic Net, Geodesic Net, Chebyshev Net, Conjugate Net
- **Analysis** — Lath Analysis, Gridshell Analysis
- **Fabrication** — Lath Sweep, Net Joints, Lath Unroll, Lath Segment, Lath Labels, Lath Preview
- **Util** — Mesh Cleanup

A typical gridshell workflow: heal the mesh with **Mesh Cleanup**, trace a **Geodesic Net**
or **Asymptotic Net**, check strips with **Lath Analysis** and the whole network with
**Gridshell Analysis**, extrude with **Lath Sweep**, cut crossings with **Net Joints**,
split to stock with **Lath Segment**, and produce cutting patterns with **Lath Unroll**
plus IDs and a BOM from **Lath Labels**.

## License

MIT
