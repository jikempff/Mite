# Getting Started with Mite

Install via Rhino's `_PackageManager` (search **mite**), then find the **Mite**
tab in Grasshopper. All components take a plain Rhino mesh — no plugins or
special data types required.

## 1. See curvature (2 minutes)

1. Add a **Mesh Sphere** (Mesh > Primitive) or reference any mesh.
2. Wire it into **Gaussian Curvature** (Mite > Curvature).
3. Wire the mesh and `K` into **Mesh Colour Map** (Mite > Util): blue is
   negative, white zero, red positive. (Or build your own chain with
   **Bounds** + **Remap Numbers** + **Gradient** + **Mesh Colours**.)
4. Optional: wire the mesh and `K` into **Mesh Isocurves** with level `0` to
   outline the saddle-shaped regions.

Domes show positive values, saddles negative, flat regions zero. **Mean
Curvature** and **Principal Curvature** work the same way; Principal also
outputs the two curvature *directions* per vertex, which you can preview with
**Vector Display**.

Meshes with seams or duplicate vertices (STL imports, meshed Breps) are welded
automatically; per-vertex outputs always line up with the input mesh.

## 2. Trace a gridshell net (5 minutes)

Use a doubly-curved mesh. For asymptotic nets it must have anticlastic
(saddle-shaped, K < 0) regions — a minimal-surface-like shape is ideal.

**Asymptotic Net** (Mite > Gridshells):

- `M` — your mesh
- `A` (AutoSpace) — `True` (the default)
- `Sp` (Spacing) — roughly mesh size / 15, or leave `0` for an automatic value
- `St` (Step) — leave `0`: it is derived from the spacing and mesh
- `Ct` (Continuous) — `True` (the default): every curve runs border to border,
  and a curve that merges into its neighbour ends on it (a T-junction) instead
  of floating in mid-surface. Set `False` for classic evenly spaced streamlines
  that stop at 0.4 × Spacing from a neighbour — more even, but laths end
  mid-surface

You get two crossing curve families that only exist where K < 0 (the `K`
output flags those vertices). These are the layouts buildable from straight
flat strips held upright. Seeds can be vertex indices (`S`) or points (`P`);
with AutoSpace only the first one matters.

**Geodesic Net** works everywhere (no curvature restriction): give it one seed
(index or point), one direction vector, AutoSpace `True`, and a Spacing.
**Geodesic Path** gives the single shortest lath between two points.

**Chebyshev Net**: seed vertex + direction + `L` (lath joint spacing, try mesh
size / 12). Outputs both lath families and a quad net mesh whose edges all have
length `L` — the flat-lattice kinematics of an elastic gridshell.

## 3. Check buildability (2 minutes)

Wire any net's curves into **Lath Analysis** together with the same mesh:

- `Up` (Upright) — `True` for asymptotic nets, `False` for geodesic nets
- `W` / `T` — strip cross-section in model units
- `E` (MaxStrain) — allowable bending strain; 0.005 suits timber,
  0.002 steel, 0.008 GFRP

`B` tells you per lath whether it can be physically bent into place; `U` is the
peak strain utilization (over 1 fails). Graft the per-point `u` tree into a
gradient on the curves to color-code where laths are overstressed.

Sanity values: on an asymptotic lath `Kn` should sit near 0 (it is measured from
the surface normal, so mesh facets do not spike it) and `Tg` near √−K of the
surface — on a hyperboloid throat with K = −1 an upright 100 × 10 mm timber lath
reaches utilization 1.15 from twist alone.

## 4. Extrude laths and cut the joints (3 minutes)

Wire the net curves into **Lath Sweep** (Mite > Fabrication) with the same mesh:

- `W` / `T` — strip cross-section (same values you analyzed)
- `U` (Upright) — `True` for asymptotic nets, `False` for geodesic nets
- `O` (Offset) — lift the laths off the surface, e.g. half the cladding depth
- `Sh` (Shape) — `0` rectangle W × T, `1` round bar of diameter W, `2` your own
  closed planar `Sc` (Section) curve; whichever you pick is applied to every
  curve in the list and stays normal to the surface along the whole net

You get one closed strip mesh per curve. Then wire both curve families into
**Net Joints** to get the crossing points, joint planes, crossing angles, and a
pair of notch solids per crossing (`Na` / `Nb`). Feed the swept laths and their
notches into **Solid Difference** to cut half-lap joints (egg-crate slots when
Upright is on); `L` sets the lap fraction (0.5 = half-lap), `Cl` the fit
clearance.

## 5. From model to workshop (5 minutes)

- **Lath Segment** — splits laths longer than your stock (`St`), keeping cuts
  `Ma` away from joints. Consecutive pieces overlap by `SL` and the splice
  notch solids (`Ne` for the upstream piece, `Ns` for the downstream one) cut
  a half-lap splice into that overlap. Outputs are one branch per lath.
- **Lath Unroll** — flat cutting patterns per lath, laid out in a row (`G`
  gap). Export the `P` curves for CNC/laser.
- **Lath Labels** — IDs (`L000`, `L001`, ...; set the prefix and start number
  per family), midpoint tag anchors, and a CSV bill of materials with lengths
  (and utilization if you wire Lath Analysis in).
- **Lath Preview** — colors the swept laths by utilization: green OK, red over.
- **Net Topology** — nodes and members of the net as a graph, for a structural
  package or a node schedule.

Optionally, check the whole network structurally with **Gridshell Analysis**
(Mite > Analysis): supports at the boundary, a downward `L` load in N/m, and it
reports deflections, per-element and per-lath stress utilization (`Ul` goes
straight into **Lath Preview**). Geometry is converted to metres internally,
so `E`, `Al` (Pa) and `L` (N/m) are always SI whatever your model units.

## Units

All lengths (Spacing, Step, EdgeLength, Width, Thickness, Offset, Margin,
StockLength, SpliceLength, Sampling, MaxSegment, Gap, Clearance) are in model
units. Tracing and sampling parameters default to `0` = automatic, derived
from the mesh size, so they work at any scale; the fixed defaults of the
fabrication inputs (Width 0.1, Thickness 0.01, StockLength 3) assume
metre-scale models — scale them up ~1000x if you model in millimetres.

## Form finding

- **Minimal Surface** and **Force Density Method** fix the mesh boundary when
  `F` is left empty. Force densities `Q` follow the `E` (Edges) output order.
- **Dynamic Relaxation** hangs a net under `G` (gravity) or point loads, or
  relaxes it as a soap film (`Te`); `R` < 1 pre-tensions the edges.
