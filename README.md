# Mite

Open-source C# toolkit for mesh curvature analysis, form finding, gridshell net design and lath fabrication. Pure .NET with zero dependencies — runs on Windows, macOS, and Linux.

## Try it in the browser

[kempffsele.me/mite](https://kempffsele.me/mite) runs Mite.Core compiled to WebAssembly: analytic shapes, a free-form loft or your own OBJ/STL, every curvature mode, all the nets, lath buildability with cutting patterns and a beam-frame check — the same code as the plugin, nothing uploaded. Source in `src/Mite.Web`.

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
- **Asymptotic Net** — both families of asymptotic curves (zero normal curvature) for asymptotic gridshells, with combed family labels, a minimum crossing angle and evenly-spaced auto-seeding
- **Geodesic Net** — straightest geodesics for geodesic (lath) gridshells; families grow with Jacobi-field start angles for even strips, from a seed or from a border edge
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

### Kinetics
- **Net Kinetics** — moves a scissor-jointed asymptotic net as a mechanism (Schikore, Schling, Oberbichler & Bauer 2020; Wan, Crolla & Schling 2025): constant joint spacing along the laths, laths perpendicular to the moving surface normal, rest bend kept with a chosen stiffness; driven by fixed / sliding supports, moved points or actuator cables, with a `Fold` slider and per-state drift / deviation / miss diagnostics. Exact on the doubly ruled hyperboloid grid (1e-12), settles traced nets in one step

### Utilities
- **Mesh Cleanup** — weld vertices (also far from the origin), reduce collapsed faces, drop degenerate/duplicate faces, unify winding
- **Pull To Mesh** — project hand-drawn curves and points onto a mesh, with optional on-surface fairing
- **Mesh Colour Map** — colour a mesh by per-vertex values in one step (diverging palette centred on zero for signed curvature)

All Grasshopper components weld coincident vertices on intake (STL imports, Brep meshes with seams) and map per-vertex results back to the original mesh, so **Mesh Colours** and **Deconstruct Mesh** line up.

## Projects

| Project | Target | Description |
|---------|--------|-------------|
| `Mite.Core` | net10.0 + net48 | Core library, no Rhino dependency |
| `Mite.Grasshopper` | net48 | Grasshopper plugin (28 components) |
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
- **Kinetics** — Net Kinetics
- **Util** — Mesh Cleanup, Pull To Mesh, Mesh Colour Map

A typical gridshell workflow: heal the mesh with **Mesh Cleanup**, trace a **Geodesic Net**
or **Asymptotic Net**, check strips with **Lath Analysis** and the whole network with
**Gridshell Analysis**, extrude with **Lath Sweep**, cut crossings with **Net Joints**,
split to stock with **Lath Segment**, and produce cutting patterns with **Lath Unroll**
plus IDs and a BOM from **Lath Labels**.

## Changelog

### 1.2.8
- **Net Kinetics** (new component, Mite > Kinetics) and `Kinetics/ScissorNet` in the core: a scissor-jointed asymptotic net as a semi-compliant grid mechanism. Unknowns are joint positions and unit normals; residuals are constant joint spacing, segment ⟂ normal at both ends (Wan, Crolla & Schling 2025, *Geometry-driven development of semi-compliant kinetic asymptotic structures*, Adv. Eng. Informatics 68), unit normals, and the change of each lath's turning against its rest bend in the moving lath frame (Wan et al.'s plain second difference would straighten curved laths and penalise uneven joint spacing); drivers are fixed nodes, sliding ground nodes, moved nodes and cables; Levenberg–Marquardt on the sparse envelope solver, one state per fold step. Exact ground truth from Schikore, Schling, Oberbichler & Bauer 2020 (*Kinetics and Design of Semi-Compliant Grid Mechanisms*, AAG 2020): the doubly ruled grid of straight rods tangent to a circle, whose joints sit at rod positions ρ tan(πm/n) for every tilt β (`ScissorNet.HyperboloidMechanism`), is reproduced through the whole motion to 2e-12 with zero joint drift; a planar lazy-tongs lattice shears exactly; a traced catenoid net standing on sliding ground nodes follows a 20 % top-ring pull with joint drift 3e-4 and 0.04° asymptotic deviation, rising by 0.52 as it narrows. Bench: Kinetics tab with a fold slider on both. Discrete finding: the hinge constraints alone leave a kink mode at every joint (four coplanar segments, not two straight laths), so the bending stiffness is what makes the discrete grid behave like the smooth mechanism — with stiffness 0 the hyperboloid grid folds along 100° kinks while satisfying every constraint.

### 1.2.7
- Web app favicon (SVG, 32 px, Apple touch icon): the new logo, black hexagon with the white knot, as on Food4Rhino — web only, no plugin change.
- Net layout: `Layout` input appended to Asymptotic Net, Conjugate Net, Curvature Streamlines and Geodesic Net — 0 evenly spaced fill (as before: curves inserted and stopped to keep the spacing, T-junctions), 1 web from the border, 2 web from the seed cross. A web seeds exactly Spacing apart (measured across the curves) along the border or along the crossing curve through the seed and traces every curve border to border, never stopping on a neighbour: the layout of built asymptotic gridshells (Schling 2018 §4.3) and border-seeded geodesic shells (Pirazzi & Weinand 2006). The spacing away from the seed line is what the surface dictates — asymptotic curves are never equidistant except on special surfaces (catenoid: neighbours separate as cosh z; verified against the closed form, Δk within 1 %). The web app defaults to the border web and explains its viewport markers (curve ends at the K = 0 line / on a neighbour / step limit, the seed square and direction knob) in a legend under the net.
- Net accuracy: the curvature tensor of the three border rows was a one-sided normal-difference estimate — 8° / 4° / 1° direction error on a catenoid rim — so every traced curve bent in its last rows and every curve started at the border carried that error across the surface (0.35 mesh edges off the closed-form asymptotic line). The two rings next to the border are now osculating-jet fits (degree-4 Monge patch, Cazals & Pouget 2005) and the border row is extrapolated from them: ≤ 0.7° / 0.02° / 0.02°, and a traced curve stays within 0.01–0.03 edge of the exact line (catenoid: u ∓ v = const in isothermal coordinates). Border exits are computed in the tangent plane, fairing keeps the end segments, and a start on the border no longer crawls along it.
- Webs cover the whole region: after the border (or the seed cross), further seed crosses are laid through whatever is still farther than 0.75 Spacing from every curve — behind a K = 0 line, a band a single cross misses, an island, a closed surface — placed exactly one Spacing from the nearest curve, perpendicular to it, so the ladder continues at the right spacing (cylinder: parallel helices 0.300 apart, the one closing gap takes the remainder). On ten test surfaces the webs now leave ≤ 0.5 % uncovered where the border or a single cross left up to 94 %. A web curve ends early only where it would touch a curve of its own family (within 0.15 Spacing and 30°): never on a regular patch, a few ends next to the flat point of a monkey saddle, and wherever geodesics of one family focus and cross (K > 0). Geodesic webs seed from the border edge nearest the seed (one family from opposite rims would be two families) and fill gaps with geodesics parallel-transported from their neighbour.

### 1.2.6
- One profile everywhere: Lath Analysis and Gridshell Analysis gain the `Shape` / `Section` inputs of Lath Sweep (appended, existing wiring untouched). The strain check reads the section's fibre distances and Saint-Venant twist length (round bar: d/2 in every direction, so `Upright` no longer matters; rectangle: γ = k·τ·t with k = 0.675 for a square → 1 for a thin strip after Timoshenko & Goodier), and the beam frame takes A, I and J from the section (circle formulas for bars, Green's-theorem moments and Roark's J ≈ A⁴/40 I_p for custom polygons, the full Saint-Venant J for rectangles instead of the thin-strip b t³/3). `LathProfile.Kind` / `SectionProperties()` and `LathAnalysis.Options.Profile` in the core; thin strips give the same numbers as before.
- Web app: the round-bar choice now really reaches the kernel (the JS binding dropped the argument, so 1.2.5 silently analysed and swept a rectangle); the beam frame follows the section too; "colour all laths by utilization" shows progress.
- Web app: each block names the plugin component it runs, with its Grasshopper icon (Analysis and Net strips follow the active chip; Lath and Structure list theirs), the knot mark in the header and the tab icon on the recipe button — the 48 px icons come from `tools/generate_icons.py`. Chips stay text-only for legibility.
- Web app: `tools/build-web.ps1` deploys from PowerShell (the bash script needs Git Bash or a Mac); `.gitattributes` keeps `*.sh` at LF on Windows checkouts.
- Web app: fixed a boot race — changing the shape while the first one was still loading traced the default net with the placeholder size (spacing 0.04 instead of 4 % of the mesh) and left the page on "tracing…" for minutes. `traceNet` now waits for the load, which traces the net itself. `tools/web-smoke.js` (Playwright) reproduces it and checks the component strips.

### 1.2.5
- Web app: the catalogue gains n-fold Enneper, a skew bilinear ruled patch and Schwarz D / gyroid patches (marching tetrahedra + Minimal Surface relaxation, `AnalyticShapes` / `ImplicitSurface` in the core); the Lath block gets a section choice (rectangle or round bar); favicon back to the black hexagon mark, PNG/Apple icons and Open Graph share image for links.
- New Mite logo (black hexagon with the asymptotic knot) as package icon, Grasshopper tab icon and web app favicon; the tab icon is now registered as the category icon, so it shows in the ribbon tab and its tooltip instead of a plain "M". 1.2.3 went out with the previous logo and 1.2.4 with the hexagon tab icon; 1.2.5 has the knot tab icon and the loading-error fix.
- Lath Sweep: new `Shape` (0 rectangle, 1 round bar of diameter W, 2 custom closed planar `Section` curve) applied to every curve; the section always rides in the surface frame (across / normal, `Upright` swaps them). `LathProfile.Round` / `LathProfile.Custom` in the core; StripSweep sweeps any closed section.
- Component icons redrawn in the Grasshopper house style (amber surfaces, orange-red meshes, green analysis, timber fabrication, grey util) — `tools/generate_icons.py`; new Mite logo (black hexagon with the asymptotic knot) as package icon, tab icon and web app favicon.
- Test surfaces from the Studio X catalogue: catenoid, 2- and 3-fold Enneper, skew bilinear ruled patch, Schwarz D (marching tetrahedra + Minimal Surface relaxation); Mite Bench shows them on the Asymptotic Net card and uses the kempffsele.me favicon.
- Geodesic Net, Geodesic Path seeding and Chebyshev Net: the straightest-geodesic tracer parallel-transports its direction between tangent planes instead of re-deriving it from the projected step. On a 64-sided cylinder a 45° helix used to drift to 44.65° (step 0.02) and 42.2° (step 0.005); it now stays within 0.01°, and refining the step improves the result instead of worsening it.
- Lath Analysis: normal curvature `Kn` is read from the rotation of the surface normal along the lath (Darboux frame), like the torsion, instead of from the polyline's facet kinks. Straight rulings of a hyperboloid now report `Kn` ≤ 0.006 instead of up to 0.18 (which put an upright lath at utilization 1.8 where the truth is 0); circles and helices on a cylinder match theory to 1%.
- Analytic test typologies: cylinder (K = 0, helices, no asymptotic net) and hyperboloid of one sheet (asymptotic net = straight rulings, τg = √−K) in `tests/Mite.Tests/RuledSurfaceTests.cs` and as shape switches in the Mite Bench (Asymptotic Net, Geodesic Net, Lath Analysis, curvature cards).

### 1.2.2
- Asymptotic Net: family labels are combed over the mesh (A and B are consistent everywhere, no more mixed families on general surfaces); new `MinAngle` input keeps the net out of the near-parabolic band where the two families collapse onto each other; flat points no longer pass as anticlastic; regions the first seed cannot reach get their own seed.
- Geodesic Net: each new geodesic starts at the Jacobi-field angle that keeps its strip closest to constant width (Pottmann et al. 2010, "Geodesic patterns") instead of parallel to its neighbour — dome and saddle families lose their fans and merges; new `FromBorder` / `BorderAngle` inputs grow the family from a border edge (vault helices).
- Web app (kempffsele.me/mite): kernel in a Web Worker with stop button, seed direction handle in the viewport, real-unit spacing with presets, net quality card (strip width, crossing angles, end classes, buildable share), end markers, family toggles, hover, camera presets.

### 1.2.1
- Continuous curves (new `Continuous` input on Asymptotic Net, Geodesic Net, Conjugate Net, Curvature Streamlines): laths run border to border; merged traces end on their neighbour as T-junctions, never floating.
- Curves meet the mesh border along their own direction — no hooks, no crawling along the edge, also on staircase borders of trimmed or subdivided quad meshes.
- Net Intersections reports T-junctions and same-family crossings (`FindAll`); Net Topology merges coincident nodes; Gridshell Analysis couples lath ends resting on another lath automatically.
- Lath Analysis measures curvature over a window (new `Window` input) instead of per facet; Lath Segment picks the best-clearance cut when joints are denser than the margin.
- Closed streamlines/geodesics close after one loop on coarse meshes; evenly spaced families keep their spacing on strongly curved tubes.
- Mite Bench review page generator in `tools/webgen`; roadmap in `docs/ROADMAP.md`.

### 1.2.0
- Review release: 27 components, built-in sparse solver, Dynamic Relaxation, Mesh Isocurves, Pull To Mesh, Mesh Colour Map, Geodesic Path, Net Topology.

## License

MIT
