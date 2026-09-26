# Mite roadmap — plugin, web bench, typologies, kinetics, literature

Working list for the twice-daily improvement sessions. Each session picks
the highest item that is not done, researches it properly (papers listed
below and whatever else is needed), implements it with tests, and reports.
Keep this file current: tick items, add findings, add new items at the
right priority. Sessions alternate between typology/algorithm work (A, D, E)
and UX work (B, C); the previous session's commit says which it was.

Rules for every session
- Commits are authored and committed by José I. Kempff <jikempff@gmail.com>,
  unsigned, with no Co-Authored-By or tool attribution of any kind.
- Never break the public core API without a note here; keep the Grasshopper
  input order (append new inputs at the end) so saved definitions survive.
- Every change ships with a regression test in `tests/Mite.Tests` and, when
  it changes behaviour a user can see, a line in README / GETTING_STARTED
  and a note or self-test in the Mite Bench page generator (`tools/webgen`).
- Numbers, not adjectives: report measured errors against analytic truth.

Priorities set by José (2026-09-25), in order
1. Kinetic / adaptive asymptotic gridshells (E): simulate the folding of a
   scissor-jointed asymptotic net (moving canopies), drive it from a "Fold"
   parameter in Grasshopper and a slider in the bench — first version done
   2026-09-26 (kinematic solve, exact on doubly ruled grids); next: elastic
   states, speed, per-state strains, the design side.
2. Surface catalogue from the Studio X submission (A): done for catenoid,
   ruled patch, Enneper 2/3, Schwarz D; Schoen's Batwing and the gyroid open.
3. Plugin UX (B): profiles for every curve (done), `Mite Net` type,
   previews, mode menus, example files.
4. Web bench (C): favicon (done), shape switch on every demo, distributions,
   kinetic "fold" demo.
5. Food4Rhino release assets (F).

## A. Test typologies (core + bench)

Analytic shapes with known answers, so every algorithm is checked where the
truth is exact. Add each to `tests/Mite.Tests/TestMeshes.cs` and to the bench.

- [x] Cylinder / barrel vault (K = 0): 2026-09-25, `CreateCylinder`,
      `RuledSurfaceTests`, bench shape switch. k1 within 0.55% of 1/R, k2 and
      K exactly 0, no vertex flagged anticlastic, net empty; curvature lines
      exact; the geodesic helix exposed the tracer drift fixed in D.
- [x] Hyperboloid of one sheet: 2026-09-25, `CreateHyperboloid` with
      closed-form K and rulings. Asymptotic directions within 0.73° (mean
      0.17°) of the rulings; every net curve straight to 0.009 over 2.83, rim
      to rim, no family mixing; K by angle deficit within 0.003 of
      −1/(1+2z²)², but k1·k2 from the Rusinkiewicz tensor is up to 6% off at
      the rims (coarser facets) — data point for curvature estimation in D.
      Lath Analysis gave kn up to 0.18 on straight rulings (facet noise); kn
      is now read from the normal's rotation (≤ 0.006), τg = √−K to 0.007
      with opposite signs per family (Beltrami–Enneper).
- [x] Catalogue of the Studio X submission (Kempff, Srisuta, Thanachanan,
      Hao 2026, p. 48) — 2026-09-25, `MinimalSurfaceCatalogueTests`, bench
      shapes on the Asymptotic Net card:
      - catenoid ("concave cylinder"): |H| ≤ 0.001, K within 0.002 of
        −1/cosh⁴z, asymptotic directions at 45° ± 1° to the parallels, net
        21 + 21 rim to rim;
      - 2-fold Enneper: |H| ≤ 0.02 (k1 up to 2), directions within 0.7° of
        X_u ± X_v, families orthogonal within 0.8°, 34 + 34 curves rim to rim;
      - 3-fold Enneper: directions within 1.6° of the Hopf-differential
        prediction θ = (±π/2 − arg w)/2; the flat point at the centre turns
        the asymptotic cross by 180° per loop, so the two families are ONE
        family globally — 17 laths end on neighbours as T-junctions, none
        floating. This is the monkey-saddle case the old roadmap asked for;
        the per-vertex A/B labelling cannot be made consistent there, only a
        global family assignment by continuation could (open item below);
      - ruled surface as a skew bilinear patch: directions within 0.02° of
        the two rulings, every net curve straight (bow < 0.013);
      - Schwarz D (Schling's pavilion surface): nodal approximation by
        marching tetrahedra (`MarchingTetrahedra`, 3823 vertices), relaxed
        with Minimal Surface (30 iterations): mean |H| 0.66 → 0.016 against
        k1 0.75, K ≤ 0 everywhere, 25 + 24 curves rim to rim.
- [ ] Schoen's Batwing (the fourth catalogue surface): no trigonometric
      nodal approximation is in common use; build it from Weierstrass data
      (Weber's "Batwing" parametrisation) or from its known boundary
      polygon relaxed with Minimal Surface, then compare with the
      submission's asymptotic layout (p. 48: needs iterated origin points).
- [ ] Gyroid and Schwarz P patches by the same marching-tetrahedra route
      (nodal forms sin x cos y + sin y cos z + sin z cos x = 0 and
      cos x + cos y + cos z = 0), for the kinetic Schwarz D prototype of
      Wan, Crolla & Schling 2025.
- [ ] Global family assignment for surfaces with flat points (3-fold
      Enneper, monkey saddle z = x³ − 3xy²): continuation of the asymptotic
      cross along a spanning tree with a branch cut, so A/B are consistent
      except across one seam; Umbilics should flag flat points (k1 = k2 = 0)
      as well as umbilics.
- [ ] Origin-point strategies of the submission (p. 48): seeds along a
      middle curve, along a diagonal pulled to the surface, along a
      projected curve — expose as "seed curve" input on the nets and test
      that the resulting families are the same as farthest-point seeding
      (D) up to spacing.
- [ ] Ellipsoid a ≠ b ≠ c: exactly four umbilics; curvature-line net has the
      classic lemon pattern.
- [ ] Cone (developable with an apex): projection and tracing near a
      singular vertex.
- [ ] Annulus / disk with holes: inner boundaries, loops around holes,
      curves ending on inner borders.
- [ ] Trimmed staircase quad mesh (already in ContinuityTests) and a
      Catmull–Clark-subdivided cube (Weaverbird-like: quads, extraordinary
      vertices of valence 3, dense border rows).
- [ ] Dirty meshes for Mesh Cleanup: unwelded seams, duplicate faces,
      flipped faces, slivers, millimetre scale, far from origin.
- [x] Marching tetrahedra and the Enneper chart moved into the core
      (`Geometry/ImplicitSurface.cs`, `AnalyticShapes.EnneperPoint`); TestMeshes
      delegates to them — 2026-09-25.
- [x] Analytic typologies as code for the web app: `Geometry/AnalyticShapes.cs`
      (saddle, sphere, dome, ellipsoid, torus, hyperboloid, monkey saddle,
      catenoid, Enneper, vault, cone, annulus, wave) — 1.2.2. Open: unify
      with `tests/Mite.Tests/TestMeshes.cs` (cylinder, hyperboloid, catenoid,
      Enneper n-fold, bilinear patch, Schwarz D carry closed-form K, rulings
      and asymptotic directions there) so one generator serves the app, the
      bench and the tests, and write the analytic-curvature tests for the
      AnalyticShapes members that have none yet (dome, ellipsoid, monkey
      saddle, vault, cone, annulus, wave).
- [ ] Bench: a typology switcher on every demo (done on Asymptotic Net and
      Geodesic Net), with the analytic truth and the measured error side by
      side; reuse the unified shape generator.

## B. Plugin UX (Grasshopper)

- [x] Component icons in the Grasshopper house style — 2026-09-25,
      `tools/generate_icons.py` rewritten after the recovered original icon
      methodology (github.com/aidannewsome/grasshopper-icons, SKILL.md: 24 px
      with 2 px margin, one colour family per icon, no black outlines, light
      from the upper left, white node dots, dark lattice on a light patch,
      +1/+1 blur-2 drop shadow). Families: curvature amber, form finding
      orange-red, gridshells amber patch with dark-red curves, analysis
      green, fabrication timber brown, util greys; tab icon = the K mark of
      kempffsele.me on an amber tile. Open: review at 100% in Rhino (dark
      and light canvas), then tune the two or three that read weakly
      (Principal Curvature arrows, Gridshell Analysis arch).
- [x] Profile selection for all curves — 2026-09-25: Lath Sweep gained
      `Shape` (0 rectangle, 1 round bar, 2 custom closed planar Section) and
      `Section`, appended after Sampling; `LathProfile.Round/Custom`, any
      section rides in the surface frame (X across / Y normal, Upright swaps
      them) and applies to every curve of the list. Volume = area × length
      to 1e-6 on straight laths, within 0.1% on the saddle lath in the
      bench. Open: Net Joints notches and Lath Unroll still assume the
      rectangle (they use Width/Thickness as the bounding box); per-family
      profiles (A upright, B flat — the AGH hybrid); a "Profile" value list
      auto-attached to Shape.
- [ ] A `Mite Net` data type (GH_Goo) carrying mesh + families + contacts +
      per-family profile, so Net Joints, Net Topology, Gridshell Analysis,
      Lath Segment, Lath Labels and Net Kinetics (E) take one wire.
- [ ] Custom viewport preview on the net components: family colours,
      T-junction and border-end markers, contact dots; consistent colours
      across components and the bench.
- [ ] Replace boolean switches that are really modes (AutoSpace,
      Continuous, Upright, MaxDir, Shape) by a right-click mode menu or an
      auto-attached value list; keep the inputs for compatibility.
- [ ] Progress and cancellation feedback: percentage in the component
      message while tracing / solving, Esc already cancels.
- [ ] Runtime messages with the fix in the message ("spacing 0.02 is below
      the mesh edge 0.05: raise Spacing or subdivide").
- [ ] Interactive seed picking (Rhino point pick button on the component)
      and a Bake that writes families to layers with their colours.
- [ ] Example definitions per typology shipped in the yak package
      (`examples/*.gh`) and linked from the component tooltips; one per
      catalogue surface plus the kinetic canopy.
- [ ] Ribbon order reviewed against the workflow order
      (mesh → curvature → net → analysis → fabrication → kinetics).

## C. Web app and bench UX

The interactive app (`src/Mite.Web`, Mite.Core on WebAssembly, deployed at
kempffsele.me/mite) carries the typology switcher, live parameters, a
draggable loft, OBJ/STL drop, all analysis modes and nets, lath plots with
unrolled patterns, frame analysis and exports. The bench (`tools/webgen`)
remains the self-test review page.

- [x] App: kernel in a Web Worker (stop = restart), seed/direction handle,
      quality card, end markers, family toggles, hover, camera presets
      (1.2.2).
- [ ] App: lath segmentation, joints and nesting sheet (material %).
- [ ] App: planarization and Chebyshev angle map; isocurve labels.
- [x] App: catalogue extended with n-fold Enneper, the bilinear ruled patch,
      Schwarz D and gyroid (nodal form → marching tetrahedra → relaxation);
      Lath block section choice (rectangle / round); favicon, touch icon and
      Open Graph share image — 2026-09-25.
- [x] App: the plugin's component icons in the panel — a strip under each
      block names the Grasshopper component the active control runs (Analysis
      and Net follow the chip; Lath and Structure list theirs); chips stay
      text-only, icons at 24 px from `tools/generate_icons.py` — 2026-09-25.
- [x] App: boot race fixed (shape change during the first load traced the
      default net at the placeholder size and hung on "tracing…");
      `tools/web-smoke.js` is the headless regression — 2026-09-25.
- [x] Accuracy at the border: jet-fitted rings and an extrapolated border row
      replace the one-sided normal differences (8° → 0.7° direction error on
      a catenoid rim; traced curves 0.35 → 0.01–0.03 edge from the closed
      form); tangent-plane border exits, end-preserving fairing — 2026-09-25.
- [x] Web layout (border / seed cross) on all four net components and the
      app: every curve border to border, no T-junctions, seeds exactly
      Spacing apart; verified on the catenoid against u ∓ v = const and on
      the cylinder (parallel helices) — 2026-09-25.
- [ ] Interior field accuracy: the remaining 0.01–0.1° / 0.01-edge interior
      error is below the mesh's own faceting (the traced polyline lies on the
      facets, whose sagitta is edge²κ/8); tracing on a smooth limit surface
      (Loop / jet) would be the next step, only worth it on coarse meshes.
- [x] Web coverage: gap filling by further seed crosses one Spacing from
      the nearest curve (≤ 0.5 % uncovered on ten surfaces; was up to 94 %
      for a single cross); same-family contact cut (0.15 Spacing, 30°) —
      2026-09-25.
- [ ] Web from a user guide curve (Schling's layout: seeds along a drawn
      curve instead of the border or the other family).
- [x] Profile applied to every curve *and every check*: Lath Analysis and
      Gridshell Analysis take Shape / Section; `LathProfile.SectionProperties`
      (rectangle, round, custom polygon) feeds strain fibres, twist length,
      A / I / J — 2026-09-25.
- [ ] App: the kinetic "fold" slider of E (core `ScissorNet` is ready, the
      wasm binding and a worker call are missing); custom section curves in
      the lath views; a "buildable" summary that waits for the colouring pass.
- [ ] App: run `tools/web-smoke.js` from `tools/build-web.sh` (needs a
      Playwright install on the deploying machine).
- [ ] Bench to reuse the app's typologies once the shape generators are
      unified (A).

- [x] Favicon: the K mark of kempffsele.me (`tools/webgen/favicon.svg`,
      inlined by build.py) — 2026-09-25.
- [x] Shape switch on the Asymptotic Net (saddle, hyperboloid, catenoid,
      Enneper 2/3, ruled, Schwarz D) and Geodesic Net (saddle, cylinder)
      demos; the viewer re-applies the scene view on switch — 2026-09-25.
- [ ] Shape switch on the remaining demos and a "classic vs continuous"
      toggle on the net demos.
- [x] Kinetic demo: a "fold" slider that plays the state sequence of E on
      the hyperboloid twist and on the catenoid net — 2026-09-26 (Net
      Kinetics card, Kinetics tab).
- [ ] Metrics with distributions, not only extremes: spacing histogram,
      crossing-angle histogram, utilisation histogram (small, consistent
      charts).
- [ ] Colour-map legends identical to Mesh Colour Map in Grasshopper.
- [ ] Search / filter (failing tests only, by component, by tab); keyboard
      navigation; ticked state summary exportable as a GitHub issue draft.
- [ ] Component cards show the Grasshopper icon at readable size and the
      parameter order as it appears on the canvas (icons refreshed
      2026-09-25).
- [ ] Page weight: 1.7 MB with the catalogue scenes; thin the wireframes or
      load scenes lazily per card.

## D. Algorithms to research and improve (with literature)

- [x] Geodesic families: Jacobi-field start angles after Pottmann et al. 2010
      (§4 evolution), border seeding; regions unreachable by sideways growth
      get their own seed (field nets) — 1.2.2. Asymptotic families are combed
      over the mesh and a MinAngle input keeps nets out of the near-parabolic
      band — 1.2.2. Still open: farthest-point seeding (next item) and a check
      that combing handles the 3-fold Enneper flat point (A).
- [ ] Evenly spaced curve families on surfaces: Jobard & Lefer 1997
      (Creating evenly-spaced streamlines of arbitrary density); Mebarki,
      Alliez & Devillers 2005 (Farthest point seeding for efficient
      placement of streamlines) — farthest-point seeding gives longer,
      better-spaced curves than the neighbour-offset seeding used now.
- [ ] Asymptotic gridshells: Schling, Hitrec & Barthel 2017 (Designing Grid
      Structures Using Asymptotic Curve Networks, Humanizing Digital
      Reality / AAG); Schling, Wang, Hoyer & Pottmann 2018 (Design and
      Construction of Curved Support Structures with Repetitive Parameters,
      AAG 2018 — the paper José's list attributes to the 2017 title);
      Schling 2018 dissertation (Repetitive Structures) — asymptotic-line
      nets on minimal surfaces, lath orientation, joint geometry, elastic
      assembly, the τg = ½(k2 − k1) sin 2α relation used in Lath Analysis.
- [ ] Asymptotic–geodesic hybrid (AGH) gridshells: Schling, Wan, Wang &
      D'Acunto 2023 (Asymptotic Geodesic Hybrid Timber Gridshell, AAG 2023);
      Wan, D'Acunto & Schling 2024 (Structural behaviour of asymptotic
      geodesic hybrid timber gridshells, Engineering Structures). One family
      asymptotic (upright, stiff) and one geodesic (flat, wraps around the
      upright laths), forming a tri-hex network; full-scale timber prototype,
      nonlinear FE calibrated on load tests, simulated live-load capacity
      7.0 kN/m²; joint rotational stiffness and supports govern performance.
      Mite work: a `Hybrid Net` mode (asymptotic family A + geodesic family
      B started orthogonal to A at the seeds), per-family profiles (B above),
      a joint rotational-stiffness parameter in Gridshell Analysis, and a
      bench check that the AGH net on the catenoid has A upright / B flat
      with kn(B) ≠ 0 and kg(B) = 0.
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
      normal-cycle tensors are more robust on irregular meshes. Data point:
      k1·k2 is 6% off at the hyperboloid rims while angle-deficit K is 0.3%.
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
      N' = −kn T − τg g using the smooth mesh normal. Open: kg is still the
      polyline's in-surface turning and carries facet noise (up to 0.23 on
      the equator of a 24-division sphere where the truth is 0) — consider a
      least-squares fit over the window or the tangent's rotation about the
      transported normal; and the strain model itself (τ·t/√3 twist strain,
      w/2 and t/2 fibre distances) against Schling's timber limits.
- [ ] Mesh repair and welding: Attene 2010 (A lightweight approach to
      repairing digitized polygon meshes); Botsch et al. 2010 (Polygon Mesh
      Processing, ch. 8) — for Mesh Cleanup.

## E. Kinetic and adaptive asymptotic structures (new, top priority)

Literature
- Schikore, Schling, Oberbichler & Bauer 2020, Kinetics and Design of
  Semi-Compliant Grid Mechanisms (AAG 2020; the roadmap used to attribute
  this to "Schling & Barthel 2021"): an asymptotic or geodesic lath grid
  with scissor (single-axis) joints is a mechanism — rigid-body shearing at
  the joints plus compliant bending/twisting of the strips — and its motion
  is governed by geometry alone (constant joint spacing along each lath,
  joints rotating about the surface normal). Doubly ruled grids (hyperbolic
  paraboloid, hyperboloid) move as rigid-body mechanisms and are the exact
  test cases; the strain energy is ½∫(GI_t κ_x² + EI_y κ_y² + EI_z κ_z²) ds,
  read on a "curvature-square diagram" against the state parameter; the
  kinetic umbrella's predicted natural diameter (4.47 m) matched FEM (4.43 m).
  Follow-up: Schling & Schikore 2023, Morphology of Kinetic Asymptotic Grids
  (DMS 2022): singularities and supports decide how far a grid can move.
- Wan, Crolla & Schling 2025, Geometry-driven development of semi-compliant
  kinetic asymptotic structures (Advanced Engineering Informatics 68,
  103762): design and motion are computed by nonlinear least squares
  (trust-region reflective) on a discrete net with per-vertex normals,
  minimising constraint energies — (1) asymptotic: every edge segment
  perpendicular to the vertex normals at both ends, (2) constant edge
  length between joints, (3) unit normals, (4) optional fairness/bending
  energy — and "transformation between service stages" is the same
  optimisation with a moved boundary or actuator; FE and three prototypes
  (kinetic canopy, 920 mm span, 15 GFRP strips per layer, 137 scissor
  joints; kinetic umbrella, 16 strips per layer, 8 × 80 mm, 192 aluminium
  joints; kinetic Schwarz D, 24 variable-length strips, 98 joints) confirm
  the predicted motion.
- Schling, Wan, Wang & D'Acunto 2023 and Wan, D'Acunto & Schling 2024 (AGH
  gridshells, see D) for the hybrid variant and for the joint stiffness
  and support conditions that decide whether a net moves or stands.
- Background: Lienhard 2014 (Bending-active structures); D'Amico et al.
  2014 (elastic gridshell dynamic relaxation with bending stiffness);
  Bouaziz et al. 2014 (Projective dynamics) for a constraint-projection
  solver that fits Mite's existing sparse symmetric solver.

Plan
- [x] Core `Kinetics/ScissorNet` — 2026-09-26. Nodes = joints + free lath
      ends (+ optional subdivision nodes, `maxSegment`), laths = node
      sequences, unknowns = positions + unit normals (6 per node; fixed nodes
      keep their normal free). Residuals: joint spacing (|e| − l)/l, e·n_a/l
      and e·n_b/l, n·n − 1 (all × HingeWeight 10), bending = change of the
      lath's turning (v_{i+1} − v_i)/l_{i+1} − (v_i − v_{i−1})/l_i against its
      rest value carried in the lath frame (× √Stiffness), targets, cables
      and sliding ground nodes (Wan et al.'s Σ v_z²). Levenberg–Marquardt on
      the envelope LDLᵀ, damping λ·diag, one state per fold step from the
      previous state. `FromTopology` builds it from Net Topology.
      Findings:
      - Wan et al.'s fairness Σ|2v_i − v_{i−1} − v_{i+1}|² is unusable for a
        given net: it penalises uneven joint spacing on straight rods (the
        hyperboloid grid's ρ tan(πm/n) spacing) and straightens curved laths
        at the settle step. Measuring the change of turning against the rest
        state (rest turning re-expressed in the moving frame t, n, n × t)
        keeps straight rods straight, pre-bent laths pre-bent and the input
        stress-free.
      - The hinge constraints alone do not make a mechanism: at a joint they
        only ask the four segments to be coplanar, so every lath may kink in
        the tangent plane at every joint (DOF count of the 12 × 5 hyperboloid
        grid: 180 − 96 − 72 − 6 = 6, not 1). With Stiffness 0 the grid folds
        along 100° kinks while satisfying every constraint to 1e-12; the
        bending term is what selects the smooth mechanism.
      - Weights are a conditioning question, not only a modelling one: hinge
        weight 100 needed ~300 LM iterations where 10 needs 15 (the diagonal
        damping over-damps the soft directions of DOFs shared with stiff
        rows); with all residuals zero at the exact mechanisms the answer is
        the same at any weight.
      - Cost: envelope factorisation of 6n unknowns; 367 nodes × 6 states
        ≈ 5 s, 983 nodes ≈ 20× slower. Fine for a batch, slow for a slider.
- [x] Analytic tests (exact ground truth) — `KineticsTests`, 6 tests:
      1. Hyperboloid mechanism (corrected from the cylinder-twist formula
         above, which is not a constant-spacing mechanism: on a twisted
         cylinder the crossing positions slide along the rods). The exact
         family: n straight rods per family tangent to the circle of radius
         ρ, pinned where they cross, tilted by β about their tangent points —
         a hyperboloid with waist ρ cos β whose joints sit at rod positions
         s_m = ρ tan(πm/n) independent of β (`HyperboloidMechanism`). Driven
         by a cable across the top ring (β 30° → 65°, 8 states): every state
         matches the closed form in all pairwise distances to 2.5e-12, joint
         drift 1e-14, asymptotic deviation 1e-12°, waist scissor angle 2β;
         also through NetIntersections/NetTopology with target drivers and
         with rods subdivided into 3 pieces (laths stay straight to 1e-12).
      2. Planar rhombic lattice (lazy tongs) sheared from 90° to 60° by one
         corner, with fixed or sliding ground nodes: every node on the exact
         lattice to 1e-7, angles exact.
      3. Bilinear patch: not done. Note that a square grid of rods with
         pinned crossings can only shear in its plane (|a|=|b|, a·b = |a|²
         forces a = b); the 1-DOF hypar mechanism needs a non-planar start
         and moving corners — worth a test with |a|,|b|,|c|,|d|, a·b fixed.
      4. Associate family (catenoid ↔ helicoid): not done.
- [x] Grasshopper `Net Kinetics` (Mite > Kinetics, icon, smoke test) —
      2026-09-26: A / B curves, Fixed, Move / To, Cables / Lengths, Fold,
      Steps, Stiffness, Segment, Iterations, Tolerance, Sampling, Mesh
      (normals), Slide / SlideNormal → A, B, Joints, Normals, Angles,
      StatesA / StatesB trees, Folds, Drift, Deviation, Miss, Report.
      Open: per-state Lath Analysis strains and a "buildable through the
      motion" flag (wire the state trees into Lath Analysis for now); a
      viewport preview that plays the states; a `Mite Net` input (B).
- [x] Bench: Kinetics tab, fold slider on the hyperboloid twist (13 states,
      2e-12) and on the catenoid net standing on sliding ground nodes with
      two crossed top cables (20 % pull: drift 3e-4, 0.04° off asymptotic,
      cables 1 % short, rise 0.52, scissor angles 78–90° → 60–71°) —
      2026-09-26. Open: strain envelope along the motion.
- [ ] Speed: a block-aware ordering or a preconditioned CG on JᵀJ for nets
      above ~500 nodes; reuse the symbolic envelope across iterations.
- [x] Elastic reading of the states — 2026-09-26 (session 1b),
      `Kinetics/KineticStrain`: kn, kg from the lath's turning at each node
      split in the node's Darboux frame, τg from the normal's rotation about
      each segment (exact ∫τg ds on an asymptotic lath), strains as Lath
      Analysis (fibre distances, Saint-Venant twist length), Schikore's
      Π = ½∫(GJτ² + EIκ²) ds per lath and state, self-weight potential
      ρAg∫z ds, the material-free Σ∫κ² ds, and `NaturalState` = least Total
      along the motion (−1 when flat). Solver additions: subdivision nodes get
      a uniform-twist residual (equal rotation rate of the normal on both
      sides, angle form so unit normals stay exact) and free tails a
      zero-twist residual — without them those normals spin freely and the
      tail twist read 40× utilization on the catenoid. Exact tests: the
      hyperboloid rod's normal turns by atan(s/b), b = ρ sin β, so τg =
      b/(b² + s²) and ∫τg² ds has a closed form: the joint-to-joint twist is
      exact to 1e-9 and Σ½GJ(Δθ)²/l is the Jensen lower bound (0.989–0.998
      of the integral); a circular lath gives kn or kg = 1/R to 1e-12 and
      ½EIκ²L; the planar lattice stores nothing. Finding: a lath hinged only
      at the joints twists uniformly between them, so its twist energy does
      not converge to ½GJ∫(√−K)² ds along the surface curve with
      subdivision — the surface value is an upper bound.
      Open: actuation forces (gradient of Π along the driver), a true
      energy-minimising solve between the drivers (currently the geometric
      LM solution is read elastically, the rest-relative bending term is a
      regulariser, not EI), twist energy of the *change* of τ against the
      rest state for pre-twisted laths, and the Lath Preview colouring per
      state in the viewport.
- [ ] Then the design side of Wan et al.: given a target family of surfaces
      (open/closed canopy), find the net whose motion passes through both —
      alternate the kinetic solve with the layout solve.

## F. Food4Rhino release assets

- [x] Bench screenshots (2026-09-25, delivered as `food4rhino/`): page
      overview, Asymptotic Net on the hyperboloid, Schwarz D and Enneper,
      Geodesic Net helices on the cylinder, Lath Sweep, Net Joints,
      Gridshell Analysis, Principal Curvature, and the icon sheet.
- [ ] Rhino/Grasshopper screenshots (need Rhino on José's machine): the
      Mite ribbon tab with the new icons; a definition on the catenoid
      running Asymptotic Net → Lath Sweep (upright, then round) → Net Joints
      with the viewport preview; Gridshell Analysis deformed net; Lath
      Preview traffic-light colouring; Lath Unroll patterns. 1600 × 1000,
      light Rhino UI, no other plugins visible.
- [ ] Description text and version notes from README's Unreleased section;
      bump the yak manifest.

## Done

- [x] 2026-09-26 (session 1, E): `Kinetics/ScissorNet` and the Net Kinetics
      component — scissor-jointed asymptotic nets as mechanisms with exact
      hyperboloid and lazy-tongs tests, sliding ground supports, cables, and
      the bench Kinetics tab with a fold slider; version 1.2.8 (1.2.7 was the
      nets/webs release on main).
- [x] 2026-09-25 (session 2): minimal-surface catalogue (catenoid, Enneper
      2/3, bilinear ruled patch, Schwarz D with marching tetrahedra +
      relaxation) with analytic tests and bench shapes; icons v2 in the
      Grasshopper house style; Lath Sweep section shapes (rectangle, round,
      custom) for all curves; bench favicon from kempffsele.me; Food4Rhino
      bench screenshots; roadmap v2 with José's priorities and the kinetic
      programme (E).
- [x] 2026-09-25 (session 1): ruled-surface typologies (cylinder,
      hyperboloid) with analytic tests and bench shape switches; Lath
      Analysis kn from the normal's rotation; geodesic tracer parallel
      transport (see A and D).
- [x] 1.2.2: combed asymptotic families, MinAngle, Jacobi geodesic seeding,
      border seeding, Mite.Web (WebAssembly app at kempffsele.me/mite),
      AnalyticShapes.
- [x] 1.2.0 review release (27 components).
- [x] Continuous nets, clean border exits, T-junction contacts and coupling
      (ec293c3); windowed lath curvature; drift-tolerant loop closure;
      geodesic candidate seeding; joint-aware segmentation fallback.
- [x] Bench generator moved into the repo (`tools/webgen`).
