# Mite

Open-source C# toolkit for mesh curvature analysis, form finding, gridshell net design and lath fabrication. Pure .NET with zero dependencies — runs on Windows, macOS, and Linux.

## Features

### Curvature Analysis
- **Principal Curvature** — k1, k2 values and directions per vertex (Rusinkiewicz 2004), with tangent-frame tensor smoothing and right-handed (D1, D2, N) frames for consistent direction fields
- **Gaussian Curvature** — angle deficit method with mixed Voronoi areas
- **Mean Curvature** — cotangent Laplacian over the same mixed Voronoi areas (H and K are consistently normalized); degenerate triangles are ignored
- **Umbilics** — flags vertices where k1 ≈ k2 (direction fields are undefined there; key for clean net layouts)
- **Mesh Isocurves** — level sets of any per-vertex field (marching triangles); K = 0 outlines the anticlastic regions asymptotic laths can occupy

### Streamlines
- **Curvature Streamlines** — RK2 integration along principal curvature directions, from seeds or evenly spaced (AutoSpace)

### Form-Finding
- **Planarization** — iterative quad mesh planarization
- **Minimal Surface** — exact cotangent Laplace solves with frozen weights (Pinkall–Polthier style); converges in a few iterations instead of thousands of flow steps
- **Force Density Method** — equilibrium solving for cable nets (q > 0) and compression shells (q < 0), with singular-system detection and a deterministic edge order
- **Dynamic Relaxation** — particle-spring form finding with kinetic damping: gravity / point loads, soap-film tension, pre-tension and smoothing
- All linear systems run on a built-in sparse envelope LDLᵀ solver (reverse Cuthill–McKee ordering): tens of thousands of unknowns solve in seconds

### Gridshells
- **Asymptotic Net** — both families of asymptotic curves (zero normal curvature) for asymptotic gridshells, with optional evenly-spaced auto-seeding
- **Geodesic Net** — straightest geodesics traced on the mesh for geodesic (lath) gridshells, with optional evenly-spaced auto-seeding
- **Chebyshev Net** — equal-edge-length nets by the compass method: the kinematics of elastic gridshells bent from flat lattices
- **Conjugate Net** — both principal families evenly spaced: an approximate conjugate net, the layout for planar-quad (PQ) panelization
- **Geodesic Path** — shortest geodesic between two points (graph search + on-surface curve shortening)
- Tracing is scale-aware: step, step count and spacing default to values derived from the mesh, so millimetre and metre models behave the same
- Curves are continuous: with **Continuous** on (default) every traced curve runs border to border or closes on itself, and a curve that merges into a neighbour ends exactly on it as a T-junction — no laths floating in the middle of the surface. Curves meet the mesh border along their own direction (no hook, no crawl along the edge), also on the staircase borders of trimmed or subdivided quad meshes

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
- **Net Topology** — nodes and members of a two-family net as a structural graph (for Karamba-style analysis or a node schedule); same-family crossings and T-junctions are nodes too

### Utilities
- **Mesh Cleanup** — weld vertices (also far from the origin), reduce collapsed faces, drop degenerate/duplicate faces, unify winding
- **Pull To Mesh** — project hand-drawn curves and points onto a mesh, with optional on-surface fairing
- **Mesh Colour Map** — colour a mesh by per-vertex values in one step (diverging palette centred on zero for signed curvature)

All Grasshopper components weld coincident vertices on intake (STL imports, Brep meshes with seams) and map per-vertex results back to the original mesh, so **Mesh Colours** and **Deconstruct Mesh** line up.

## Projects

| Project | Target | Description |
|---------|--------|-------------|
| `Mite.Core` | net10.0 + net48 | Core library, no Rhino dependency |
| `Mite.Grasshopper` | net48 | Grasshopper plugin (27 components) |
| `Mite.Tests` | net10.0 | Unit tests against analytic surfaces |

## Install

### Via Yak (recommended)

In the Rhino command line:

```
_PackageManager
```

Search for **mite** and click Install.

### Manual

Drop `Mite.Grasshopper.gha`, `Mite.Core.dll` and `Microsoft.Bcl.HashCode.dll` into your Grasshopper Libraries folder.

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
// mean.CurvatureNormals — mean curvature normal per vertex
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
- **Form Finding** — Planarize Mesh, Minimal Surface, Force Density Method, Dynamic Relaxation
- **Gridshells** — Asymptotic Net, Geodesic Net, Chebyshev Net, Conjugate Net, Geodesic Path
- **Analysis** — Lath Analysis, Gridshell Analysis, Mesh Isocurves
- **Fabrication** — Lath Sweep, Net Joints, Lath Unroll, Lath Segment, Lath Labels, Lath Preview, Net Topology
- **Util** — Mesh Cleanup, Pull To Mesh, Mesh Colour Map

A typical gridshell workflow: heal the mesh with **Mesh Cleanup**, trace a **Geodesic Net**
or **Asymptotic Net**, check strips with **Lath Analysis** and the whole network with
**Gridshell Analysis**, extrude with **Lath Sweep**, cut crossings with **Net Joints**,
split to stock with **Lath Segment**, and produce cutting patterns with **Lath Unroll**
plus IDs and a BOM from **Lath Labels**.

## License

MIT
