# Getting Started with Mite

Install via Rhino's `_PackageManager` (search **mite**), then find the **Mite**
tab in Grasshopper. All components take a plain Rhino mesh — no plugins or
special data types required.

## 1. See curvature (2 minutes)

1. Add a **Mesh Sphere** (Mesh > Primitive) or reference any mesh.
2. Wire it into **Gaussian Curvature** (Mite > Curvature).
3. Color the mesh: wire `K` into a **Gradient** (remap the domain with
   **Bounds** + **Remap Numbers**) and feed the colors plus the mesh into
   **Mesh Colours**.

Domes show positive values, saddles negative, flat regions zero. **Mean
Curvature** and **Principal Curvature** work the same way; Principal also
outputs the two curvature *directions* per vertex, which you can preview with
**Vector Display**.

## 2. Trace a gridshell net (5 minutes)

Use a doubly-curved mesh. For asymptotic nets it must have anticlastic
(saddle-shaped, K < 0) regions — a minimal-surface-like shape is ideal.

**Asymptotic Net** (Mite > Gridshells):

- `M` — your mesh
- `A` (AutoSpace) — `True`
- `Sp` (Spacing) — roughly mesh size / 15
- `St` (Step) — roughly Spacing / 10

You get two crossing curve families that only exist where K < 0. These are the
layouts buildable from straight flat strips held upright.

**Geodesic Net** works everywhere (no curvature restriction): give it one seed
vertex index, one direction vector, AutoSpace `True`, and a Spacing.

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

## Units

All lengths (Spacing, Step, EdgeLength, Width, Thickness) are in model units.
The defaults assume meter-scale models — scale them up ~1000x if you model in
millimeters.
