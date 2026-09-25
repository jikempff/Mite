# Mite roadmap — user experience, test typologies, literature

Working list for the twice-daily improvement sessions. Each session picks
the highest item that is not done, researches it properly (papers listed
below and whatever else is needed), implements it with tests, and reports.
Keep this file current: tick items, add findings, add new items at the
right priority.

Rules for every session
- Commits are authored and committed by José I. Kempff <jikempff@gmail.com>,
  unsigned, with no Co-Authored-By or tool attribution of any kind.
- Never break the public core API without a note here; keep the Grasshopper
  input order (append new inputs at the end) so saved definitions survive.
- Every change ships with a regression test in `tests/Mite.Tests` and, when
  it changes behaviour a user can see, a line in README / GETTING_STARTED
  and a note or self-test in the Mite Bench page generator (`tools/webgen`).

## A. Test typologies (core + bench)

Analytic shapes with known answers, so every algorithm is checked where the
truth is exact. Add each to `tests/Mite.Tests/TestMeshes.cs` and to the bench.

- [x] Cylinder / barrel vault (K = 0, developable): curvature lines are
      rulings and circles; geodesics are helices; asymptotic directions are
      the rulings only (degenerate family) — Asymptotic Net must not invent a
      second family. (2026-09-25, `TestMeshes.CreateCylinder`,
      `RuledSurfaceTests`, bench shape switch.) Findings: k1 within 0.55% of
      1/R, k2 and K exactly 0, no vertex flagged anticlastic, net empty;
      curvature lines exact. The geodesic helix exposed a drift in the
      straightest-geodesic tracer (below) that is now fixed.
- [x] Hyperboloid of one sheet (ruled, K < 0): asymptotic curves are exactly
      the two families of straight rulings — the sharpest test for the
      asymptotic tracer and for Lath Analysis (kn = 0, tg ≠ 0).
      (2026-09-25, `TestMeshes.CreateHyperboloid` with the closed-form K and
      ruling directions.) Findings: asymptotic directions within 0.73° (mean
      0.17°) of the rulings on a 64 × 32 revolve; every net curve is straight
      to 0.009 over length 2.83 and runs rim to rim, no family mixing; K by
      angle deficit within 0.003 of −1/(1+2z²)², but k1·k2 from the
      Rusinkiewicz tensor is up to 6% off near the rims (r = √2, coarser
      facets) — a data point for the curvature-estimation item in D. Lath
      Analysis reported kn up to 0.18 on the straight rulings (facet noise in
      the polyline second differences); kn is now read from the surface
      normal's rotation, giving ≤ 0.006, and τg matches √−K to 0.007 with
      opposite signs per family (Beltrami–Enneper).
- [ ] Monkey saddle z = x³ − 3xy² (flat umbilic with three asymptotic
      directions at the origin): stress test for family continuity and the
      umbilic mask.
- [ ] Ellipsoid a ≠ b ≠ c: exactly four umbilics; curvature-line net has the
      classic lemon pattern.
- [ ] Catenoid and Enneper patch (H = 0): minimal-surface reference for
      Minimal Surface, Dynamic Relaxation and asymptotic nets (on H = 0 the
      asymptotic families are orthogonal).
- [ ] Cone (developable with an apex): projection and tracing near a
      singular vertex.
- [ ] Annulus / disk with holes: inner boundaries, loops around holes,
      curves ending on inner borders.
- [ ] Trimmed staircase quad mesh (already in ContinuityTests) and a
      Catmull–Clark-subdivided cube (Weaverbird-like: quads, extraordinary
      vertices of valence 3, dense border rows).
- [ ] Dirty meshes for Mesh Cleanup: unwelded seams, duplicate faces,
      flipped faces, slivers, millimetre scale, far from origin.
- [ ] Bench: a typology switcher per demo (sphere / saddle / torus /
      cylinder / hyperboloid / monkey saddle / ellipsoid / annulus), with
      the analytic truth and the measured error shown side by side.

## B. Plugin UX (Grasshopper)

- [ ] A `Mite Net` data type (GH_Goo) carrying mesh + families + contacts,
      so Net Joints, Net Topology, Gridshell Analysis, Lath Segment and Lath
      Labels take one wire instead of A / B / mesh / points separately.
- [ ] Custom viewport preview on the net components: family colours,
      T-junction and border-end markers, contact dots; consistent colours
      across components and the bench.
- [ ] Replace boolean switches that are really modes (AutoSpace,
      Continuous, Upright, MaxDir) by a right-click mode menu or an
      auto-attached value list; keep the inputs for compatibility.
- [ ] Progress and cancellation feedback: percentage in the component
      message while tracing / solving, Esc already cancels.
- [ ] Runtime messages with the fix in the message ("spacing 0.02 is below
      the mesh edge 0.05: raise Spacing or subdivide").
- [ ] Interactive seed picking (Rhino point pick button on the component)
      and a Bake that writes families to layers with their colours.
- [ ] Example definitions per typology shipped in the yak package
      (`examples/*.gh`) and linked from the component tooltips.
- [ ] Ribbon order and icons reviewed against the workflow order
      (mesh → curvature → net → analysis → fabrication).

## C. Web bench UX

- [ ] Typology switcher (see A) and a "classic vs continuous" toggle on the
      net demos.
- [ ] Metrics with distributions, not only extremes: spacing histogram,
      crossing-angle histogram, utilisation histogram (small, consistent
      charts).
- [ ] Colour-map legends identical to Mesh Colour Map in Grasshopper.
- [ ] Search / filter (failing tests only, by component, by tab); keyboard
      navigation; ticked state summary exportable as a GitHub issue draft.
- [ ] Component cards show the Grasshopper icon at readable size and the
      parameter order as it appears on the canvas.

## D. Algorithms to research and improve (with literature)

- [ ] Evenly spaced curve families on surfaces: Jobard & Lefer 1997
      (Creating evenly-spaced streamlines of arbitrary density); Mebarki,
      Alliez & Devillers 2005 (Farthest point seeding for efficient
      placement of streamlines) — farthest-point seeding gives longer,
      better-spaced curves than the neighbour-offset seeding used now.
- [ ] Asymptotic gridshells: Schling, Hitrec, Barthel 2017–2018 (Designing
      grid structures using asymptotic curve networks; Asymptotic
      Gridshell); Schling 2018 dissertation (Repetitive structures) —
      asymptotic-line nets on minimal surfaces, lath orientation, joint
      geometry, elastic assembly.
- [ ] Geodesic gridshells: Pirazzi & Weinand 2006 (Geodesic lines on
      free-form surfaces — optimized grids for timber rib shells); Pottmann
      et al. 2010 (Geodesic patterns) — geodesic strip patterns with
      constant width, breakpoints/segmentation.
- [ ] Straightest geodesics and shortest paths: Polthier & Schmies 1998
      (Straightest geodesics on polyhedral surfaces); Crane, Weischedel &
      Wardetzky 2013 (Geodesics in heat); Sharp & Crane 2020 (You can find
      geodesic paths in triangle meshes by just flipping edges) — the
      FlipOut algorithm would replace the Dijkstra + straightening in
      Geodesic Path with an exact geodesic.
      Partly done 2026-09-25: the tracer's direction update was biased. It
      flattened the projected travel into the next tangent plane, which
      shortens the component along the tilt between facet and smooth surface
      at every step and turns the trace toward the tilt axis; the drift grew
      with the number of steps (45° helix on a 64-gon cylinder: 44.65° at
      step 0.02, 42.2° at step 0.005). The direction is now parallel-
      transported by the minimal rotation between the smooth normals
      (discrete Levi-Civita transport, the "straightest" continuation of
      Polthier–Schmies): 45.011° / 45.002°, converging with the step.
      Still open: a true polyhedral straightest geodesic (walk each facet
      exactly, unfold at edges — exact on developable meshes, no step size)
      and FlipOut for Geodesic Path.
- [ ] Curvature estimation: Rusinkiewicz 2004 (Estimating curvatures and
      their derivatives on triangle meshes); Meyer, Desbrun, Schröder, Barr
      2003 (Discrete differential-geometry operators); Cohen-Steiner &
      Morvan 2003 (Restricted Delaunay triangulations and normal cycle) —
      normal-cycle tensors are more robust on irregular meshes.
- [ ] Conjugate and planar-quad nets: Liu, Pottmann, Wallner, Yang, Wang
      2006 (Geometric modeling with conical meshes and developable
      surfaces); Zadravec, Schiftner, Wallner 2010 (Designing quad-dominant
      meshes with planar faces); Pottmann, Asperl, Hofer, Kilian —
      Architectural Geometry (2007) for the whole vocabulary.
- [ ] Chebyshev nets and elastic gridshells: Masson 2017 (Existence and
      construction of Chebyshev nets and application to gridshells);
      Garg et al. 2014 (Wire mesh design); Baek, Reis 2019 (Rigidity of
      hemispherical elastic gridshells under point load indentation);
      Douthe, Baverel, Caron 2006 (Form-finding of a grid shell in
      composite materials) — compass method limits, shear-angle bounds.
- [ ] Form finding: Schek 1974 (Force density method); Barnes 1999
      (Form finding and analysis of tension structures by dynamic
      relaxation); Adriaenssens, Block, Veenendaal, Williams (eds.) 2014
      (Shell Structures for Architecture) — kinetic damping details,
      bending-active DR (Lienhard 2014, Bending-active structures;
      D'Amico et al. 2014 for elastic gridshell DR with bending stiffness).
- [ ] Lath bending and fabrication: Pottmann, Eigensatz, Vaxman, Wallner
      2015 (Architectural geometry review); Tang, Kilian, Bo, Wallner,
      Pottmann 2016 (Analysis and design of curved support structures);
      Schling's lath torsion/curvature limits for timber (kn, kg, tg strain
      formulas) — check the Lath Analysis strain model against them.
      2026-09-25: kn and τg now both come from the Darboux relation
      N' = −kn T − τg g using the smooth mesh normal (Schling, Hitrec &
      Barthel 2017 give τg = ½(k2 − k1) sin 2α, consistent with τg² = −K on
      asymptotic curves, verified on the hyperboloid). Open: kg is still the
      polyline's in-surface turning and carries facet noise (up to 0.23 on
      the equator of a 24-division sphere where the truth is 0) — consider a
      least-squares circle/parabola fit over the window, or the geodesic
      curvature from the tangent's rotation about the normal transported
      along the curve; and the strain model itself (τ·t/√3 twist strain,
      w/2 and t/2 fiber distances) against Schling's timber limits.
- [ ] Mesh repair and welding: Attene 2010 (A lightweight approach to
      repairing digitized polygon meshes); Botsch et al. 2010 (Polygon Mesh
      Processing, ch. 8) — for Mesh Cleanup.

## Done

- [x] 2026-09-25: ruled-surface typologies (cylinder, hyperboloid) with
      analytic tests and bench shape switches; Lath Analysis kn from the
      normal's rotation; geodesic tracer parallel transport (see A and D).

- [x] 1.2.0 review release (27 components).
- [x] Continuous nets, clean border exits, T-junction contacts and coupling
      (ec293c3); windowed lath curvature; drift-tolerant loop closure;
      geodesic candidate seeding; joint-aware segmentation fallback.
- [x] Bench generator moved into the repo (`tools/webgen`).
