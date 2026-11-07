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

Priorities set by José (2026-09-27), in order — they replace the list below
where they overlap
1. Schling's symmetric asymptotic and asymptotic-geodesic gridshells: no short,
   odd or straight stub segments; the math must follow his method (node
   spacing along the seed curves, symmetry, AAG). First version done
   2026-09-27 (1.3.0, `AsymptoticWeb`, `AagWeb`); next items in G below.
2. Structural analysis that can be trusted: chosen supports (done: border /
   lowest / picked, fixed / pinned), joint model (done: rigid / hinge /
   semi-rigid), loads (done: self-weight, area, point), joint eccentricity
   (done 2026-11-02: laths in layers on rigid end offsets, see D); next:
   buckling, the AAG erection sequence.
   semi-rigid), loads (done: self-weight, area, point), linear buckling (done
   2026-10-26, session 2: load factor, modes, Sturm check — see D); next: joint
   eccentricity, the AAG erection sequence, buckling in the web app's
   structure block, lateral–torsional terms for upright strips (done
   2026-11-06, see D), a geometrically nonlinear (arc-length) check of the
   first mode's imperfection sensitivity.
3. The kinetic behaviour of any web in the browser (done for scissor / lift /
   spread drives, 1.3.0; the flat state as the assembly state — press flat,
   the 3-fold Enneper web into a hexagon — 1.3.1; the deployment from the flat
   grid — 2026-10-23, session 2, in the core and Grasshopper: the flat state is
   a bifurcation, the kit deploys by opening its scissors, and pulling the
   singular node alone does not deploy it, see E; speed done 2026-11-07:
   the block LDLᵀ, press + deploy 18 → 3.3 s natively on 451 nodes, see E);
   next: strain colouring per state, the deployment in the browser (not
   timed in wasm this session; the damping and Jacobian follow-ups of the E
   speed item come first), the physical prototype dimensions.
6. The kit to build it (done 1.3.1: Flat Kit — flat assembly 1 : 1, strips
   with slots / holes / hub on laser sheets, deployment sequence, cut list,
   SVG / DXF / CSV); next: a physical prototype of the 3-fold Enneper to
   check slot play and the deployment against the model, hub plate design,
   supports, and the G laths in the kit.
   spread drives, 1.3.0; strain colouring per state and the envelope over the
   motion done 2026-10-02, see E); next: speed, the flat state as the
   assembly state.
   SVG / DXF / CSV; done 2026-10-24: the hub plate, section H); next: a
   physical prototype of the 3-fold Enneper to check slot play and the
   deployment against the model, supports, and the G laths in the kit.
4. One lath section for every curve (done in the web app 1.3.0; per-family
   profiles in Grasshopper still open).
5. Black, white and neon for everything visual (web app done 1.3.0; one
   palette in the core for Grasshopper, the app and the bench 2026-10-03, see
   C; icons and Food4Rhino images still in the old palette).
5. Black, white and neon for everything visual (web app done 1.3.0; icons
   done 2026-10-25, see B; bench and Food4Rhino images still in the old
   palette — the bench's 3D viewers and legends, the icon sheet on the bench
   is already in the new palette).
   profiles in Grasshopper: done 2026-10-27 for Net Joints and Gridshell
   Analysis — family B's own profile, the threaded joint of the AGH hybrid,
   one section per lath in the frame; the `Mite Net` wire of the 1.4.0 bundle
   carries the per-family profile to Lath Sweep / Lath Analysis).
5. Black, white and neon for everything visual (web app done 1.3.0; bench,
5. Black, white and neon for everything visual (web app done 1.3.0; the
   Grasshopper colour maps — Mesh Colour Map, Lath Preview — and the bench's
   curvature and utilisation colours done 2026-10-30 from one core table;
   icons and Food4Rhino images still in the old palette).

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
        the asymptotic cross by 180° per loop. CORRECTED 2026-10-06: a 180°
        turn maps each line onto itself, so the families stay separate all
        the way round; the "one family globally" reading came from the
        uncombed per-vertex labels. The T-junctions seen there were curves
        of the fill jumping to the other family next to the flat point
        (14 of 56), fixed by the twist labels below;
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
- [x] Global family assignment for surfaces with flat points (3-fold
      Enneper, monkey saddle z = x³ − 3xy²) — 2026-10-06. No continuation
      or seam is needed: the two asymptotic directions through a point have
      geodesic torsion +√−K and −√−K (Beltrami–Enneper; τg = (k2 − k1)
      sin α cos α, "negatives of one another"), a sign that cannot change
      where K < 0, does not depend on the sign of the tangent or of the
      normal, and so labels the families exactly, vertex by vertex: A =
      right-handed (`AsymptoticCurves.GeodesicTorsion`, `LabelByTwist`). A
      family exchange around a singularity would need index ±1/4, which a
      pair of conjugate line fields cannot have (Vaxman et al. 2016 §5.1);
      the families change only on a curve passing exactly through a K = 0
      point (the rays of a singular web). The 1.2.2 breadth-first combing
      was the defect: on a 48 × 48 monkey saddle one wrong neighbour
      comparison next to the flat point relabelled 575 of 2396 vertices;
      separate saddle regions (the wave) were labelled independently. With
      exact labels every curve is traced in its own family
      (`LockFamily`, default on): 3-fold Enneper fill 14 / 56 curves in the
      other family → 0, Schwarz D web 3 / 59 + 102 stray samples → 0 + 2,
      wave web 2 / 40 + 100 → 0 + 2; saddle, catenoid, hyperboloid, Enneper,
      ruled patch and torus nets keep the same curve counts and lengths. Tests:
      `AsymptoticFamilyTests` (12), bench: two self-tests and a monkey-saddle
      web on the Asymptotic Net card.
- [ ] Umbilics should flag flat points (k1 = k2 = 0, K ≤ 0 around them) as
      well as umbilics, and report the index of the asymptotic cross
      (Hopf differential wⁿ dw² on minimal surfaces: each family turns by
      −nπ per loop); the Asymptotic Web already finds them by its rays.
- [ ] Field tracing across a K = 0 point: a curve through a flat point
      continues in the other family (monkey saddle: the line at 30° is A on
      one side and B beyond). What locked tracing does on such a curve is not
      tested yet; decide whether Asymptotic Net should end it on the point
      (as the web's rays do) — none of the bench nets passes exactly
      through one.
- [ ] Origin-point strategies of the submission (p. 48): seeds along a
- [ ] Global family assignment for surfaces with flat points (3-fold
      Enneper, monkey saddle z = x³ − 3xy²): continuation of the asymptotic
      cross along a spanning tree with a branch cut, so A/B are consistent
      except across one seam; Umbilics should flag flat points (k1 = k2 = 0)
      as well as umbilics.
- [x] Origin-point strategies of the submission (p. 48): seeds along a
      middle curve, along a diagonal pulled to the surface, along a
      projected curve — done 2026-10-19 (session 1, B/C/A) as the `Guide`
      input of the four net components (`EvenlySpacedNet.Options.Guide`,
      `GuideSeeds`, `WebSeeding.Guide`): the curve is pulled onto the mesh,
      nodes every Spacing of arc length along it, one curve of each family
      through every node (Schling, Hitrec & Barthel 2017: the net drawn
      alternately from the nodes of the other family, the node-to-node
      distance the one repeated parameter). Findings: (1) the facet closest
      point of a point well off the surface is the foot of the *facet*
      normal, d · tan(dihedral/2) off the ridge on a round-off-chosen side —
      a circle 0.5 outside the catenoid waist zigzagged to z = ±0.0104 and
      came out 43 % too long; the pull now slides along the smooth tangent
      plane until p − q ∥ smooth normal (9e-6 from the on-surface guide);
      (2) the perpendicular spacing of a guide web is Spacing · sin(angle to
      the guide), by construction — Schling's parameter is the node
      distance, not the lath spacing; (3) geodesics from a guide take the
      first direction by parallel transport (Pottmann et al. 2010 §2), which
      round a latitude circle of colatitude α turns by θ cos α (holonomy,
      measured to 0.011°) so neighbouring geodesics fan out 13° apart at
      S = 0.4 on the unit sphere and are cut where they meet — a user who
      wants a constant angle to the guide needs a geodesic guide or a
      future `GuideAngle` option. Measured: catenoid waist 21 nodes 2π/21
      apart (2e-5 rad, the 96-gon chords), both lines u ∓ v = const through
      every node (1e-5), 84/84 ends on the rims, 0.0009 off the closed form;
      meridian guide nodes at the exact arc positions (8e-6); cylinder 21
      helices 2π/21 · sin 45° apart (±1e-4); saddle diagonal guide → B lines
      x + y = const through 11 nodes exactly 0.25 apart, A untouched. Not
      done: the comparison with farthest-point seeding (that code is in the
      unmerged 1.4.0 bundle), several guides at once (feed one; the gap
      filling covers the rest), a constant-angle option for geodesics.
- [ ] Ellipsoid a ≠ b ≠ c: exactly four umbilics; curvature-line net has the
      classic lemon pattern.
      projected curve — expose as "seed curve" input on the nets and test
      that the resulting families are the same as farthest-point seeding
      (D) up to spacing.
- [x] Ellipsoid a ≠ b ≠ c: exactly four umbilics; curvature-line net has the
      classic lemon pattern — 2026-10-20 (session 2), `EllipsoidTests` (12),
      `TestMeshes.CreateEllipsoid`, closed forms in `AnalyticShapes`
      (`EllipsoidUmbilics` at (±a √((a² − b²)/(a² − c²)), 0, ±c √((b² −
      c²)/(a² − c²))), `EllipsoidCurvature` K = h⁴/(a²b²c²), H = h³(a² + b² +
      c² − |p|²)/(2a²b²c²), `EllipsoidPrincipal` exact shape operator,
      `EllipsoidConfocal` the Dupin coordinates λ₁ ∈ (c², b²), λ₂ ∈ (b², a²)).
      Read: Panou & Korakitis 2011 (arXiv 1104.5145, curvatures and umbilics),
      Garcia & Sotomayor 2009 (Dynamical behavior of Darboux curves, arXiv
      0912.3749: the four umbilics are D1 lemons of index ½, the ellipse y = 0
      is the union of the umbilics and their separatrices; principal
      coordinates from the confocal quadrics), Garcia & Sotomayor 2007 (Lines
      of curvature on surfaces, historical comments: Monge's integration, all
      other lines of curvature closed), Delmarcelle & Hesselink 1994 / Tricoche,
      Scheuermann & Hagen 2001 (degenerate points of 2D symmetric tensor
      fields: linear deviator, δ > 0 wedge, δ < 0 trisector, indices ±½),
      Berry & Hannay 1977 / Porteous 2001 via the Wikipedia survey (lemon,
      monstar, star; genus 0 → index sum 2). Measured (96 segments, edge
      0.067): K and H within 1.4 % and the principal directions within 0.7° of
      the closed form away from the umbilics (k1, k2 individually up to 6 %,
      the Rusinkiewicz facet bias noted on the hyperboloid); the max-curvature
      lines keep λ₂ and the min-curvature lines keep λ₁ (checked against the
      exact shape operator at 500 random points); traced Curvature Streamlines
      stay within 1 mesh edge (mean 0.1–0.2) of the confocal quadric through
      their seed for both families, even 1 edge from an umbilic, and every one
      is closed (Monge); λ itself is a poor yardstick (its gradient varies 10×
      over the surface: a 21 % spread in λ₁ was 0.9 edge laterally). The
      umbilic connection: the max line from (a, 0, 0) stays on y = 0 to 4e-4
      edge and leaves it 0.44 edge past the umbilic, where the ellipse turns
      into a min-curvature line.
      Umbilics located (D item below): exactly four, all +½, sum 2, 0.44 /
      0.31 / 0.30 edges from the closed form on 48 / 96 / 144 segments.
      Open: the tracer cannot resolve the field inside the facet ring round an
      umbilic (the lemon's one separatrix vs the three of a monstar is not
      checked); lemon vs monstar needs the cubic form (Berry & Hannay).
- [x] Ellipsoid a ≠ b ≠ c: exactly four umbilics — 2026-10-23 (session 1).
      `AnalyticShapes.Surface("ellipsoid")` (1.5 × 1 × 0.7) has |k1 − k2| = 0
      to round-off exactly at (±a √((a² − b²)/(a² − c²)), 0, ±c √((b² − c²)/
      (a² − c²))) = (±1.264, 0, ±0.377) and at no other local minimum of a
      720 × 360 scan; `Umbilics.Find` on the 96-division mesh flags four
      clusters within 0.04 of them at tolerance 0.05–0.2 and nothing else
      (the chart's poles, k1 = c/b², k2 = c/a², are not umbilics). Bench:
      Umbilics card with the markers and a self-test. Open: the curvature-line
      net's lemon pattern (Conjugate Net on the ellipsoid as a bench shape).
- [ ] Cone (developable with an apex): projection and tracing near a
      singular vertex. Partly done 2026-10-08 (session 1):
      `TestMeshes.CreatePolyCone` — n generators through the apex, rings
      optionally jittered along them, so every quad is planar and the mesh is
      intrinsically flat except at the apex; exact development
      (`ConeAngles`, `ConeUnfold`, `ConeDistance`). Measured: apex deficit
      = 2π − nθ_f to 1e-12 and 5e-15 elsewhere; principal curvature cos α / r
      within 0.8 % from ring 7 out, 3 % on rings 4–6 (the radius-2 ring
      reaches the apex), rulings exact. Projection: the hinted
      `MeshProjection.ClosestPoint` misses the containing facet for 32 of
      2000 points near the apex (up to 1.3e-2 off, slant 2) because the
      Euclidean nearest vertex lies sectors away; new `ClosestPointGlobal`
      is exact (ball of radius d + longest edge through the kd-tree) and is
      used by both Geodesic Path methods. Geodesic paths: exact (D below).
      Open: the streamline / geodesic-net tracers near the apex (they still
      use the hinted projection at every step), and the cone as a bench
      shape on the curvature cards.
- [x] Annulus / disk with holes — 2026-10-25 (session 2), `AnnulusTests` (15):
      the annular saddle z = k (x² − y²) (asymptotic curves = the lines
      x ± y = c, cut by the hole), a saddle with three holes
      (`TestMeshes.CreatePerforated`: grid vertices inside a hole dropped,
      the kept neighbours snapped onto the circle, 4 border loops), the
      paraboloid annulus z = ρ²/2 (curvature lines = parallels and
      meridians) and a flat annulus (geodesics = straight lines; the shortest
      path round the hole = the taut string round the inscribed 40-gon,
      `PlanarDistanceAroundPolygon`, the shorter hull arc). Measured: every
      asymptotic curve (fill and both webs) ends on the hole's rim or the
      outer rim, none enters a hole, 0 % uncovered, bow ≤ 0.011 on the
      48-gon rim mesh (the facet sagitta ~0.003 plus the 0.23° direction
      error), 0.0037 on the 60 × 60 grid; the web seeds on every border loop
      as designed. Max-curvature lines close round the hole as single loops;
      min-curvature lines run rim to rim with length within 4e-5 of
      ∫√(1+ρ²) dρ and wander < 0.06° in azimuth. Isocurves round the hole are
      single closed loops (length within the 96-gon chord factor). FlipOut
      round the hole: 40 random pairs, 33 round the hole, max |L − exact|
      4e-14, a border wedge having infinite angle. Found and fixed (D below):
      geodesic laths converging towards the hole were bent sideways onto
      their neighbours (bow 0.0147 on the flat annulus). Open: the closed
      parallels are 0.5–0.9 % short of 2πρ with a radial drift of 0.03–0.04
      per loop — not the tracer (step 0.01 gives the same) but the direction
      field: Rusinkiewicz D1 on the 96 × 24 paraboloid mesh is tilted 0.25°
      on average (1.06° worst) off the parallel in the interior, a chirality
      of the one-way diagonals; the normal-cycle estimator of the 10-05
      bundle should be measured on this case.
- [ ] Trimmed staircase quad mesh (already in ContinuityTests) and a
      Catmull–Clark-subdivided cube (Weaverbird-like: quads, extraordinary
      vertices of valence 3, dense border rows).
- [x] Dirty meshes for Mesh Cleanup — 2026-10-09 (session 1), with the D
      item "Mesh repair and welding": `DirtyMeshTests` (15) builds each defect
      from a clean mesh so the repair is compared with the original exactly —
      polygon soups (unwelded seams), duplicates in both windings, isolated
      vertices, face 0 + 30 % / 70 % reversed (open saddle / closed sphere),
      a 1 m sphere in millimetres at (2500 km, −1200 km), needles (edges split
      1e-4 from an end), caps (a vertex 1e-5 off an edge, interior and on the
      border), a non-manifold fin, a Möbius strip, an annulus, two spheres one
      inside out. Findings: (1) the BFS winding took face 0 as the reference
      and crossed non-manifold edges: one flipped first face turned a sphere
      inside out (signed volume −4.12, mean H −1.0009) and reversed the sign
      of H at 217 of 225 saddle vertices; now closed parts go outward by
      signed volume and open parts keep their majority (area) winding;
      (2) needles were invisible to the zero-area test (smallest angle 0.001°,
      H error 0.61 at the split points) — `Sliver` collapses them back to the
      original faces exactly; (3) vertices orphaned by removed faces stayed in
      the mesh; (4) `long` edge keys hashed to a ^ b (as in the FlipOut
      finding): 31.6 s → 0.45 s on a 75 k-face soup.
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
      Done 2026-10-23 (session 1): `Geometry/AnalyticSurface` — every
      catalogue shape as x(u, v) with closed-form x_u, x_v, x_uu, x_uv, x_vv
      (or F, ∇F, H(F) for the nodal Schwarz D / gyroid), giving exact K, H,
      k1, k2, principal and asymptotic directions at any point (do Carmo
      §3-3; Goldman 2005 for the implicit case); `AnalyticShapes.Build` is
      now generated from that surface (`Surface(name).Mesh(res)`, meshes
      bit-identical to before, checked by hash on 34 meshes) and
      `Mesh(res, out uv, out quads)` returns the chart parameters of every
      vertex. `AnalyticSurfaceTests` (22): derivatives of the 15 parametric
      shapes vs central differences (< 1e-6), closed forms of the sphere,
      torus, hyperboloid, catenoid, Enneper n-fold (incl. the Hopf
      directions of TestMeshes), monkey saddle (MathWorld K and |H|),
      ellipsoid (K and the four umbilics), vault / cone (K = 0, one ruling),
      bilinear patch (rulings), implicit sphere / cylinder / Schwarz D nodal
      (K ≤ 0 at all 3811 vertices, mean |H| 0.028 vs k1 0.76 — the nodal
      approximation is not minimal); TestMeshes' catenoid, torus and
      hyperboloid lie on AnalyticShapes' surfaces (1e-12); and the discrete
      estimators against the truth on dome, ellipsoid, monkey saddle, vault,
      cone, annulus, wave, sphere, torus at resolutions 48 and 96 (mean
      absolute error over the interior vertices relative to the largest
      curvature, the protocol of Magid, Soldea & Rivlin 2007): K ≤ 2.5 %,
      H ≤ 2.5 %, k1 / k2 ≤ 10 %, every one falling by ≥ 25 % per doubling,
      K exactly 0 on the developables. TestMeshes keeps its own generators
      (tests index their vertices); the cross-check is the unification.
      Findings: (1) a valence-4 vertex whose four edges follow the asymptotic
      directions has a coplanar one-ring — the polyhedron is flat there — so
      the angle deficit reads K = 0 however fine the mesh (wave saddle
      points: 0 vs −1.44; the bilinear patch's one 4-valent vertex: 0 vs
      −0.22); the Rusinkiewicz tensor reads −1.15 (two-ring normals). (2)
      The ring next to a collapsed pole reads K and H 15 % high on the
      sphere at every resolution (the fan is self-similar); the mean error
      still converges. (3) Monkey saddle: the asymptotic lines make the angle
      ±45° − 3θ/2 with the ray (z = r³ cos 3θ), so the line field turns by
      −180° per loop (index −½) and each line returns to itself; the two
      families are joined through the three straight asymptotic lines through
      the flat point, which carry family A on one side and B on the other —
      a sharper statement of the "one family globally" note above.
- [x] Bench: a typology switcher on every demo (done on Asymptotic Net and
- [x] Rotational CRPC surface (constant ratio of principal curvatures,
      k_m / k_p = −a) — 2026-10-31: `AnalyticShapes.Build("crpc", a, h)` by
      RK4 on r″ = a(1 + r′²)/r (first integral 1 + r′² = r^(2a)); a = 1 gives
      cosh z to 1e-14, a = 1/3 the closed form to 1e-14; the discrete ratio
      |k₁/k₂| is within 0.004 of 1/3 and the asymptotic web crosses at
      60.5–60.8° (theory 60°, Wang & Pottmann 2022). The surface class of a
      constant-angle asymptotic gridshell (the AGH gridshell's 60°).
- [ ] Bench: a typology switcher on every demo (done on Asymptotic Net and
      Geodesic Net), with the analytic truth and the measured error side by
      side; reuse the unified shape generator — 2026-10-23 (session 1) for
      the curvature cards: Principal / Gaussian / Mean Curvature, Umbilics,
      Mesh Colour Map and the new Test Shape card share one viewer with eight
      shapes (torus, sphere, ellipsoid, hyperboloid, catenoid, monkey saddle,
      cone, wave) × (K, H, k1, k2, |k1 − k2|) × (measured, exact, |error|),
      the mean / max error and the half-resolution error in the legend and
      in `numbers.curvTruth`. Open: Curvature Streamlines, Mesh Isocurves,
      Conjugate Net, Chebyshev Net and the lath cards are still single-shape.

## B. Plugin UX (Grasshopper)

- [x] Component icons in the Grasshopper house style — 2026-09-25,
      `tools/generate_icons.py` rewritten after the recovered original icon
      methodology (github.com/aidannewsome/grasshopper-icons, SKILL.md: 24 px
      with 2 px margin, one colour family per icon, no black outlines, light
      from the upper left, white node dots, dark lattice on a light patch,
      +1/+1 blur-2 drop shadow). Families: curvature amber, form finding
      orange-red, gridshells amber patch with dark-red curves, analysis
      green, fabrication timber brown, util greys; tab icon = the K mark of
      kempffsele.me on an amber tile.
- [x] Component icons in the Mite palette (black, white and neon; José's
      priority 5 of 2026-09-27) — 2026-10-25 (session 1, B). Same generator,
      same helper API (`FAM` keeps the old family names `surface`, `mesh`,
      `math`, `param`, `timber` as aliases of `white`, `g`, `y`, `grey`, `a`,
      so icon functions added by other bundles still draw), every icon
      function restyled: blue (#2a3cff) and red (#ff1f4f) are the lath
      families A and B, green (#00d86b) a geodesic family or a form-found
      mesh, yellow (#d9ff00) an analysis, white a surface or a cut sheet,
      grey the util patches, black (#0a0a0a) the marks only (arrows, nodes,
      cut lines, text). Lath Preview carries the app's utilization ramp and
      Mesh Colour Map its diverging map. Rules followed (Rutten's own, McNeel
      "Grasshopper Icons" guide and discourse 74786): 24 px with the 2 px
      border (drawings now live in the 20 px area — the amber set reached the
      border with alpha up to 254), region outlines in a dark shade of the
      fill (half luminance; never full black, which Rutten "no longer
      recommends"), subtle lighting from the upper left across each region's
      own box (blue regions lit harder: blue is dark, 0.11 luminance), the
      drop shadow +1/+1, blur 2 px, 32 %, lines 1.2–2 px (Material: 2 dp,
      1.5 dp for detail). Downsampled by area averaging (box) instead of
      Lanczos: no ringing, so every pixel is a true blend of what was drawn.
      Measured (`IconTests`, also on the bench): all 31 icons 24 px, border
      alpha ≤ 31 (shadow only); every opaque pixel within 12 RGB of a
      palette colour, shade or two-colour blend except 88 of 3811 (crossings
      of a blue and a red line on white); the silhouette band (within 3 px
      of the outline) contrasts 3 : 1 (WCAG 2.1 SC 1.4.11) on a light capsule
      (#d2d2d2) for ≥ 25 % of its pixels (weakest Dynamic Relaxation) and on
      a dark menu (#2d2d2d) for ≥ 23 % (weakest Net Joints); the amber set
      read 36 % / 0 % at the weakest (the tab icon and Dynamic Relaxation
      vanished on dark). Findings:
      - No flat colour contrasts 3 : 1 with both canvases: #d2d2d2 and
        #2d2d2d are 8.9 : 1 apart, so a colour at 3 : 1 to one is at most
        2.97 : 1 to the other. Legibility on both therefore needs two-tone
        shapes: white regions with a dark rim, lit faces on dark fills, black
        marks on a 1 px white halo, neon lines on the bare canvas on a dark
        rim. The amber set got this for free from its mid-luminance fills
        with dark-red rims; black/white/neon has to construct it.
      - Lanczos downsampling overshoots next to black-on-red edges (a pixel
        #fc002f that is no blend of anything drawn); the box filter has no
        such pixel and reads the same at 24 px.
      - The capsule colours are assumptions: Grasshopper's light capsule is
        a lighter grey than #d2d2d2 (which only helps the rims) and the dark
        ribbon / menus of Rhino 8 dark mode are near #2d2d2d; the real
        canvas was not available here (Linux).
      Open: review at 100 % in Rhino on both canvases (the amber set was
      never reviewed either); after the 1.4.0 merge rerun the generator so
      the six icons added by other bundles (Mite Net, Lath Strip, Net
      Distributions, Net Trim, Test Shape, AGG Web) are restyled — they draw
      through the aliased family names and come out white/green/yellow/blue
      without a hand pass; retake the Food4Rhino icon sheet from the bench;
      the bench's viewers (teal / plum families, legends) are the 1.4.0
      palette bundle's.
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
- [x] A `Mite Net` data type — 2026-09-27 (session UX, B). Core
      `Gridshells/LathNet`: immutable surface + family A + family B + optional
      per-family `LathProfile`, kind, spacing; joints / contacts / topology on
      demand; `Ends()` classifies every lath end as border / on a lath / free;
      `ToBytes`/`FromBytes` exact. Grasshopper: `MiteNetGoo` (preview, bake,
      internalise), `MiteNetParameter`, the **Mite Net** component (build from
      curves, set the profile with `Ub` for a differently laid B, join two nets
      with `NetB`, report the ends), a `Net` output on Asymptotic / Geodesic /
      Conjugate / Chebyshev Net and Net Kinetics, a `Net` input appended to Net
      Joints, Net Topology, Lath Sweep, Lath Analysis, Gridshell Analysis, Lath
      Segment, Lath Unroll, Lath Labels and Net Kinetics. Rule: anything wired
      beside the Net overrides that part (saved definitions unchanged; Mesh and
      Curves inputs became optional). Tests: `LathNetTests` (exact hyperboloid
      rulings 128 joints / 64 rim ends / 288 members; a planar net with every
      end kind; byte-exact round trip; per-family profiles; agreement with the
      bench's end count on three traced nets). Findings: the bench counted
      3 of 8 T-junction ends of the classic saddle fill as border (1.3e-4 off
      the facets, 2e-18 from the lath); `components.json` had drifted from the
      sources (now generated by `tools/webgen/extract_components.py`); Net
      Topology's NodeLaths mis-indexed same-family contacts (fixed); the smoke
      test still expected 27 components (now 29 + the parameter icon).
      Open: contacts are recomputed by each consumer (cache per net value, or
      carry them); Gridshell Analysis and Net Kinetics take family A's profile
      for both families (per-family sections in `FrameAnalysis` and
      `KineticStrain`); Net Joints / Lath Unroll still use the profile's
      bounding rectangle for round and custom sections; a "Net" wire from
      Curvature Streamlines; the web app's end classes (it adds "at the K = 0
      line", which needs the field) should move onto `LathNet.Ends` with a
      region mask; the smoke test's new Net wire case needs a Windows run.
- [~] Custom viewport preview on the net components: done for every `Net`
      output and the Mite Net parameter (family A teal / B magenta,
      T-junction ends orange, free ends red, the web app's colours; selected
      = Grasshopper's selection colour). 2026-11-07 (session 3, B): the
      colours are the palette's roles against the viewport background (see
      the C entry "Grasshopper previews" below) and the plain curve outputs
      A / B / G / Cu / Cv behind a `Net` output start with their preview off
      (`MiteComponent.HidePreviewsBehindNet`: new instances only, Grasshopper
      stores each output's Preview flag in the definition; right-click ›
      Preview brings them back). Open: contact dots (`LathNet.Joints()` is
      cheap enough for the preview cache — decide whether they are on by
      default or only while the owner is selected), the same colours in Net
      Joints / Net Topology outputs, and a check in Rhino that an output
      hidden by the constructor is listed as such in its context menu.
- [x] Replace boolean switches that are really modes (AutoSpace,
      Continuous, Upright, MaxDir, Shape) by a right-click mode menu or an
      auto-attached value list; keep the inputs for compatibility.
      rectangle (they use Width/Thickness as the bounding box); a "Profile"
      value list auto-attached to Shape. (Done 2026-10-05: mode menus on every mode input.)
- [x] Per-family profiles (A upright, B flat — the AGH hybrid) — 2026-10-27
      (session 1, B / D). Net Joints: `WidthB`, `ThicknessB`, `UprightB`,
      `OffsetB` appended (unset = as A); Gridshell Analysis: `CurvesB`,
      `WidthB`, `ThicknessB`, `UprightB` appended. Core:
      `JointGeometry.TryBuildNotches(… out JointKind)` — same orientation:
      the lap pair as before; mixed: a slot through the upright lath over the
      flat lath's footprint (box in the flat lath's frame, along it
      t_up/sin θ + w_flat |cos θ|/sin θ, across w_flat, normal = the interval
      [max(lows), min(highs)] the two sections share, 5 % bleed past the
      lamella's edge where the slot opens on it, only the clearance where it
      stays inside — a mortise), the flat lath uncut (`NotchSolid.IsEmpty`),
      `JointKind.None` with nothing cut when the sections do not overlap;
      `TryBuildLapNotches` keeps its signature and now returns the threaded
      joint for mixed profiles (it used to return false, and Net Joints
      counted the crossing as "nearly tangential").
      `FrameAnalysis.Compute(…, IReadOnlyList<LathProfile> profiles, …)`:
      A, Iy, Iz, J, Wy, Wz, twist length, the beam frame (upright: local y
      along the normal) and ρgA self-weight per lath; the single-profile call
      delegates and is bit-identical. Measured (`HybridNetTests`, 13): slot
      boxes equal to the closed form at 90° and 60°, with and without
      clearance (volume to 1e-15); mortise and top-edge cases; symmetric when
      B is the upright one; a cross of an upright and a flat 100 × 20 beam
      (pinned, P at the crossing) shares P as 48EI/L³ says (A 96.15 %) with
      δ = P/(k_A + k_B) to 1e-9, no torsion, each lath's utilization equal to
      M/W of its own section to 1e-9; self-weight per lath ρgAL⁴/8EI within
      0.1 % of the lumped FE and equal to the single-lath run to 1e-12. The
      catenoid AGH net (A = one asymptotic family, Spacing 0.3, 21 laths;
      B = 16 meridian geodesics): interior max |kn(A)| 0.021 (mean 0.0065;
      the rim samples reach 0.115 at the border exit, the known 1° turn),
      max |kg(B)| 0.0061, |kn(B)| = 1/cosh² z within 0.039 (mean 0.759 vs
      tanh 1 = 0.762), 108 of 108 crossings threaded with a 0.017 slot; the
      hybrid frame (rim ends fixed, 1 kN/m) deflects 8.2 mm against 3.0 mm
      with every lath upright, peaks A 1.63 / B 1.44. Literature: Schling,
      Wang, Hoyer & Pottmann 2022 §2.2 (geodesic strips with osculating planes
      normal to S, asymptotic strips tangent), §4.2 (timber AGG web: geodesic
      slats interwoven between the top and bottom asymptotic lamellas, M8
      bolts through the gaps, slats offset by half a grid), §1.2 (steel web
      notched in a single layer); Schling, Wan, Wang & D'Acunto 2023 (AAG
      2023, "flat geodesic planks interlaced at the midpoint of the
      asymptotic beams", 60 m² demonstrator); Hoyer, D'Acunto & Schling 2024
      (IASS 2024: 25 × 100 mm ash split to 12 × 100, geodesic slats slide
      through the asymptotic grid during forming, blocks between the twin
      lamellas); Wan, D'Acunto & Schling 2024 (Eng. Struct.: live load
      7.0 kN/m², joint stiffness and supports govern — abstract only, the
      paper is paywalled). Open: Lath Analysis has no family input (run it
      twice, or through the Mite Net wire); a custom `Section` per family;
      Lath Unroll / Flat Kit flat strips for the G laths (H); the AGH
      tri-hex layout itself (geodesics at the mid points between nodes,
      §4.2) is a web item, not a joint item; a twin-lamella option (two
      upright strips with a gap = the slat thickness, blocks between them)
      would be the literal timber detail.
- [ ] A `Mite Net` data type (GH_Goo) carrying mesh + families + contacts +
      per-family profile, so Net Joints, Net Topology, Gridshell Analysis,
      Lath Segment, Lath Labels and Net Kinetics (E) take one wire.
- [ ] Custom viewport preview on the net components: family colours,
      T-junction and border-end markers, contact dots; consistent colours
      across components and the bench.
- [x] Mode menus — 2026-10-05 (session 2). One catalogue,
      `Mite.Core.Modes.ModeCatalog` (new API, nothing existing changed):
      31 mode inputs on 15 components (AutoSpace, Layout, Continuous,
      FromBorder, MaxDir, Upright, Shape, SupportType, Symmetry, AAG,
      Diagonal, Palette, Smooth, UnifyWinding), each with named options, a
      short Message word per option, the registered default and the input it
      depends on (Layout / Continuous / FromBorder need AutoSpace, Diagonal
      needs AAG). `MiteComponent.Modes.cs` turns it into (1) a right-click
      submenu per mode that writes the input's own persistent value with
      undo, or moves the value list feeding it, and says "set by a wire"
      otherwise; (2) the Message line under the component, built after every
      solve from the values that actually arrived ("Fill · Continuous",
      "Seeds", "Layout mixed"; modes that do not apply stay silent and are
      marked "not used now" in the menu); (3) "Attach value lists", one undo
      step. Inputs keep type, position and default, so saved definitions are
      untouched. Literature: Norman 1983 (mode errors come from missing
      feedback of the state: remove modes, mark them distinctly, or use
      distinct commands), Raskin 2000 (not modal while the state is at the
      locus of attention), McNeel's "Custom Component Options" pattern.
      Tests: `ModeCatalogTests` (28) check the catalogue against every
      component source (names, types, defaults, tooltips explaining each
      integer value) and fail when an item Boolean or an integer whose
      tooltip enumerates "0 = … 1 = …" has no menu. Smoke test: catalogue
      against the live params, Message after a Lath Sweep solve.
      Not done / follow-ups: (a) no automatic value list on placement — it
      would also fire when a file is opened unless Read() is used to tell
      the two apart; menus are on demand instead. (b) After merging the
      unmerged bundles: the curvature-measures `Method` input (0/1/2) and
      the Mite Net component's switches must be catalogued — the test fails
      until they are, by design; the palette bundle may rename Mesh Colour
      Map palettes. (c) Check in Rhino: menu text width, value-list
      placement left of the grip, undo of a menu pick and of the attach.
- [ ] Progress and cancellation feedback: percentage in the component
      message while tracing / solving, Esc already cancels. The Message
      line now carries the modes (above): show progress in front of them
      during a solve and restore them in AfterSolveInstance.
- [x] Progress and cancellation feedback: percentage in the component
      message while tracing / solving, Esc already cancels — 2026-11-01
      (session 2). `Mite.Core/Progress.cs`: `ProgressHandler(fraction,
      stage)` and `ProgressScope` (window [from, to], clamped, never
      backwards, `Sub` for nested stages, `Converging` = max of the iteration
      fraction and the residual's log decay to the tolerance); a `Progress`
      option on EvenlySpacedNet (all three tracers), AsymptoticCurves,
      GeodesicCurves, CurvatureStreamlines, ConjugateNet (A then B),
      AsymptoticWeb (rays), AagWeb, ScissorNet (states × iterations),
      DynamicRelaxation, MinimalSurface, Planarization, FrameAnalysis (stages)
      and FlipGeodesic (pairs). `MiteComponent`: `Progress(stage, from, to)`
      and `ReportProgress(stage, i, n)` write "tracing A 42 %" into Message
      and paint with `Instances.RedrawCanvas()` (Rutten: it paints at once),
      at most every 250 ms and only after the first second (Nielsen's 1 / 10 s
      limits, Myers 1985 for the percentage); the previous Message is put back
      in AfterSolveInstance. Esc added to Planarize, the exact Geodesic Path
      and the kinetic iterations; the net tracers now poll Esc per candidate
      instead of per source curve. Findings:
      - The tracers' fraction is coverage: Σ L × ¾ Spacing / area of the
        masked region. Curves of a finished family are ½ … 1 Spacing apart
        (seeded at d_sep, stopped at ½ d_sep: Jobard & Lefer 1997), so with a
        full Spacing per strip the estimate ran to 1.2 … 1.35 and the bar sat
        at 100 % for the last fifth of every trace; with ¾ it ends at 0.87 …
        1.03 on the asymptotic fills and webs (saddle, catenoid, hyperboloid,
        Enneper), 0.90 … 0.97 on the sphere's geodesics, 0.72 on a cylinder's
        helices (exactly a Spacing apart: finishes early) and 1.37 for
        geodesics converging from a saddle's border.
      - Cancellation was only polled per dequeued source curve in the
        Jobard–Lefer loop: a source spawns up to 2 L / Spacing candidates, so
        Esc on a fine net could take seconds to land (9 curves after a probe
        that fired at the third; now 3).
      - Not verified in Rhino (no Rhino here): that `Instances.RedrawCanvas()`
        paints synchronously on the Mac build too, and the width of the Message
        box with the longest words ("planarizing 100 %").
      Open: Chebyshev Net, Force Density Method, Mesh Cleanup and the Flat Kit's
      press-flat solve have no progress (quick, or files other bundles rewrite);
      a `Cancel` button in the component menu for people without an Esc habit;
      the web app's worker could forward the same reports to its status line.
- [ ] Runtime messages with the fix in the message ("spacing 0.02 is below
      the mesh edge 0.05: raise Spacing or subdivide").
      message while tracing / solving, Esc already cancels.
- [x] Runtime messages with the fix in the message — 2026-10-06 (session
      2). `Mite.Core.Diagnostics.InputAdvice` (new API, pure, tested): every
      message is problem + number, reason, fix + number, with a stable code
      and the measured / limit / suggested values as fields. Checks: Spacing
      below the mean mesh edge (raise to the edge or subdivide ⌈log₂(e/s)⌉×);
      Spacing ≥ half the diagonal of the usable region (→ one or two curves;
      suggests a tenth); Step > Spacing / 4 or > one edge (suggests the
      automatic step); no K < 0 / flat mesh; MinAngle leaving out every saddle
      vertex (the crossing range found, 2·min(t, 90° − t) with
      tan t = √(−k1/k2), and the MinAngle that keeps half / all of it) or
      ≥ 25 % of it (Remark); SeedPoints more than two edges off the mesh;
      Seeds out of range; Width / Thickness > ¼ or < 1/10 000 of the median
      lath (named as a mm/m slip with the values to enter when the swapped
      reading fits). Wired through `MiteComponent` (Report, ResolveSeeds,
      SeedPointVertex, ReportTracing(…, proj, region), CheckSection) into
      Asymptotic, Geodesic, Conjugate and Chebyshev Net, Curvature
      Streamlines, Lath Sweep / Analysis / Segment / Unroll, Gridshell
      Analysis, Net Joints and Net Kinetics; one call per component, no
      input or output changed. Literature: Shneiderman 1982 (specific,
      constructive, positive tone), Neusesser & Sunwall 2023 (NN/g
      guidelines and rubric: precise, constructive, no blame, by impact,
      safeguard likely mistakes), Becker et al. 2019 (context, hints,
      logical argumentation, right time). Finding: on z = x² − 0.01y² (K < 0
      at every vertex, families crossing at 11.4°–13.3°) Asymptotic Net used
      to say "no anticlastic (K < 0) region" — false; the default MinAngle
      15° was the cause. Tests: `InputAdviceTests` (13) check each message
      against closed forms, that the suggested value fixes the case, and
      that the bench shapes at their spacings raise none.
      Not done / follow-ups: (a) Asymptotic Web Spacing and Chebyshev
      EdgeLength against the mesh edge; (b) mesh intake: non-manifold edges
      and disconnected pieces with "run Mesh Cleanup" (needs a manifold
      check in the core); (c) the generic "No curves traced" endings of
      Geodesic / Conjugate Net should name their cause; (d) E / Allowable
      unit slips (MPa typed as Pa) in Gridshell Analysis and Net Kinetics;
      (e) after the mode-menus bundle: "Spacing has no effect without
      AutoSpace"-type notes from the catalogue's dependencies; (f) check in
      Rhino that long balloon texts wrap and that a message is not repeated
      for families A and B (deduplicated through RuntimeMessages(level)).
- [~] Interactive seed picking (Rhino point pick button on the component)
      and a Bake that writes families to layers with their colours. Bake done
      2026-11-07 (session 3): `MiteNetGoo.BakeAll` puts the laths on
      `Mite::A` / `Mite::B` (`LayerTable.AddPath`, Rhino 8; created in the
      family colours for the application's viewport background, an existing
      layer keeps its colour; objects coloured by layer, named A0 … / B0 …,
      one group). Open: the seed picking; baking the lath ends as points on
      `Mite::Ends`.
- [ ] Example definitions per typology shipped in the yak package
      (`examples/*.gh`) and linked from the component tooltips; one per
      catalogue surface plus the kinetic canopy.
- [ ] Ribbon order reviewed against the workflow order
      (mesh → curvature → net → analysis → fabrication → kinetics).
- [x] Net Trim (cookie cutter) — 2026-10-21, see G: boundary curves, plan
      projection, holes, Inside / Invert, cut ends for the fabrication
      components.
- [x] `Test Shape` component (Mite > Curvature) — 2026-10-23 (session 1):
      the analytic test surface by name with P1–P3 and a resolution, as the
      mesh the bench and the tests measure, with the exact `K`, `H`, `K1`,
      `K2`, `D1`, `D2`, `A1`, `A2`, the chart `UV` and a `Report`; the
      collapsed poles are evaluated 1e-7 of the chart inside the singular
      row, the relaxed nodal surfaces say their values are approximate.
      Smoke test: sphere exact at every vertex, hyperboloid throat K = −1 and
      unit asymptotic directions. Open: a value list for `Name`, and a
      `Quads` option returning the parameter grid instead of the triangles.

## C. Web app and bench UX

The interactive app (`src/Mite.Web`, Mite.Core on WebAssembly, deployed at
kempffsele.me/mite) carries the typology switcher, live parameters, a
draggable loft, OBJ/STL drop, all analysis modes and nets, lath plots with
unrolled patterns, frame analysis and exports. The bench (`tools/webgen`)
remains the self-test review page.

- [x] App: kernel in a Web Worker (stop = restart), seed/direction handle,
      quality card, end markers, family toggles, hover, camera presets
      (1.2.2).
- [x] Nesting sheet with the material % — 2026-10-20 (session 1, C / José's
      priority 6, the kit). `KitExport.Nest`: strips sorted by width then
      length decreasing, each on the first shelf of the first sheet where it
      fits (Berkey & Wang 1987, finite first-fit; one strip width makes every
      shelf a 1-D bin, FFD ≤ 11/9 OPT + 6/9 after Dósa 2007) instead of the
      1.3.1 next-fit rule (a row closed as soon as one strip did not fit;
      asymptotic 1.691, Baker & Coffman 1981; for mixed heights FFDH 1.7 vs
      NFDH 2, Coffman, Garey, Johnson & Tarjan 1980). Strips longer than the
      bed: along the bed's diagonal when they fit there (`LaneLength`: the
      sheet's chord at the lane's offset, clipped by both long edges of the
      strip — 661.7 mm for a 30 mm strip on 600 × 400 with a 4 mm gap; the
      lanes beside it take shorter strips end to end), kept only when the
      diagonal costs no sheet (its corners are lost: a sheet of lanes fills
      to ~45–60 %); else split into lapped pieces (`Split`, two pin holes per
      lap, the cut set between two joints by `LathSegmentation.Segment`, the
      lap three strip widths or what the joint pitch leaves, 20 mm at least,
      shortened per strip where joints sit closer; laps that still reach a
      joint are counted, `LapClashes`, not hidden); `Splice` < 0 keeps the
      1.3.1 overflow drawing. Material % = outline area / sheet area, per
      sheet in its title and for the kit, with the area bound. 3-fold Enneper
      kit, 600 × 400 bed: 1 : 5 — 3 sheets → 2 (= the area bound), 43 → 61 %;
      1 : 3.3 — 5 sheets both ways (48 shelves: nothing pairs but 324 + 252);
      1 : 2.5 — was 3 sheets + 30 strips "cut on a longer bed", now 7 sheets,
      72 %, 60 pieces lapped over 32 mm, 0 laps near a slot (a fixed 3-width
      lap of 120 mm straddled two slots at the 46 mm joint pitch — the lap
      must follow the pitch); 1 : 1.4 — 22 sheets, 75 %, 138 pieces, laps
      47–62 mm. Tests (`KitNestingTests`, 10): the classic FFD-beats-NFD list
      packed to the millimetre, mixed widths, gaps, the lane's closed form
      (len − w·W/H on the diagonal) and maximality, diagonal vs split choice,
      pieces that reassemble and carry every joint once, the Enneper kit
      against the area bound and the next-fit count. Web app: material use,
      area bound and splits in the Kit summary, a switch for splitting;
      `web-smoke.js` checks a 300 × 200 bed. Grasshopper: `Splice` appended.
      Not done / follow-ups: (a) shelves only — strips shorter than the sheet
      height could stand upright in the leftover of a row (a skyline or
      guillotine packer, Lodi, Martello & Monaci 2002) and the 1 : 3.3 case
      would gain; (b) a laser-cut finger joint (fingers in the sheet plane,
      no doubled thickness, glue only — the Downland / Savill joint in 2-D)
      as an alternative to the lap; (c) the app has no lath segmentation or
      3-D joint view yet (the Kit covers slots and holes on the strips);
      (d) mixed strip widths per family are nested but untested on a kit
      (`FlatKit.Options` has one width).
- [ ] App: lath segmentation and a 3-D joint view (the Kit's slots and holes
      are on the strips only).
- [x] App: lath segmentation and joints — 2026-10-31 (session 2, C/B): a
      **Pieces** block (Lath Segment + Net Joints on the current net): stock
      length, splice, joint margin, kerf, equal pieces, notch solids; the
      pieces drawn alternately in the family colour with the cuts marked; the
      boards as a nesting chart. Core `Fabrication/CuttingStock`: first-fit
      decreasing (Johnson 1973; FFD ≤ 11/9 OPT + 6/9, Dósa 2007) with a kerf
      per saw cut, the Martello–Toth L₂ lower bound, an exact branch and bound
      for small lists; `LathSegmentation.Segment(…, balance)`: the fewest
      pieces that fit the stock made equal, (L + (n − 1)·splice)/n each.
      Grasshopper Lath Segment: `Balance`, `Kerf` appended; `Board`, `Boards`,
      `Material`, `Report` appended. Measured: FFD optimal in 60/60 random
      lath-piece lists (6–15 pieces, exact optimum by branch and bound), at the
      L₂ bound in 181/200 lists of 20–40 pieces (mean gap 0.10 boards); Dósa's
      worst case reproduced (8 boards for an optimum of 6). Catenoid web in the
      app (40 laths of 11.8 m, 6 m stock, 300 mm splice, 150 mm margin, 3 mm
      kerf): 120 pieces, 80 splices, 300 joints, nearest cut–joint 184 mm,
      84 boards (L₂ 83, 98.2 %) with 0.52 m stubs; balanced 120 boards
      (70 %), shortest piece 3.97 m. Finding: equal pieces always cost boards
      on equal laths — a piece of L/n with n = ⌈L/S⌉ is longer than S/2, so no
      two share a board; only mixed lath lengths let them pair. Timber
      practice (Harris & Roynon 2008, Savill; Downland the same): 6 m factory
      lengths finger-jointed from 0.3–1.4 m pieces, scarf joints 1 : 7 on site,
      laths up to 37 m — i.e. the splice is a glued joint anywhere and the
      offcut goes into the next lath, which is the greedy + packing model.
      Open: (a) a *minimum piece length* rule (move only the last cut so the
      stub is at least, say, 2 × splice — the practical middle between greedy
      and balanced); (b) staggered splices: cuts on neighbouring laths of one
      family should not line up (a weak line across the shell) — measure and
      stagger; (c) the nesting sheet of the strips (material %) is in the
      10-20 kit-nesting bundle, not on main; (d) in Grasshopper the boards are
      a tree of indices — a `Cut List` CSV (board → pieces → lath / ordinal /
      length) like Flat Kit's would close the loop; (e) Net Joints in the
      app returns notch solids only for same-orientation crossings (the flat
      G slats on upright A / B have no lap notch, as in Grasshopper).
- [ ] App: planarization and Chebyshev angle map; isocurve labels.
- [x] App: lath segmentation, joints and nesting sheet (material %). (Done 2026-10-31: the Pieces block of the web app.)
- [x] App: planarization — 2026-10-22 (session 1, C with B): a *panels ·
      planarity* switch on every two-family net (Net Topology → Mesh) colours
      the panels by Zadravec's δ_PQ,n (red from 0.01) and *planarize* runs
      Planarize Mesh on the panel mesh (border held or free) and reports the
      deviation before / after, how far the nodes moved and left the surface.
      Measured on the catenoid: curvature-line panels mean δ 5.1e-4 (100 %
      ≤ 0.01), planarized almost in place (1.5 → 0.1 mm, nodes ≤ 3 mm);
      asymptotic web mean δ 6.8e-2 (0 % ≤ 0.01), not flat after 300
      iterations with nodes moved up to 0.9 m — Liu et al. 2006's point made
      visible. `tools/web-smoke.js` checks both (33 PASS).
      Open: the Chebyshev angle map (colour the Chebyshev nodes by shear
      angle with the Hazzidakis check 2π − Σα = ∫∫K per quad; the 10-19
      compass bundle's `Report` carries the angles) and isocurve labels.
- [ ] App: planarization (done in the unmerged 2026-10-22 bundle: panels /
      planarity on Net Topology, the app's *panels · planarity* block).
- [x] App: Chebyshev angle map and isocurve labels — 2026-10-26 (session 1,
      C). Cells coloured by the node angle (blue closing, red opening, black
      past the lock angle, 45° default after Garg et al. 2014, Sageman-Furnas
      et al. 2019 use 30°), legend, locked count, Hazzidakis total against
      2π; the lock angle recolours in place. One label per isocurve at a
      golden-ratio fraction of its length, faded behind the surface (ray
      test every 150 ms). Core: `ChebyshevNet.ShearAngle(s)` (central
      differences; third-order one-sided on the rim), `CellCurvature`
      (Hazzidakis ω(i+1,j) + ω(i,j+1) − ω(i,j) − ω(i+1,j+1)), `CellArea`,
      `CountLocked`; Grasshopper `Curvature` (`Kc`) output appended.
      Findings: (1) the corner angles of the chord polygon do not work for
      Hazzidakis — their one-sided O(L) errors are as large as K·L² (the
      sphere read 12 % of its area); the alternating node-angle form with
      central differences converges, and the rim needs third-order one-sided
      stencils (second order left −14 … −40 % on the saddle's rim cells,
      third order −9 % at L = 0.15, −3.5 % at 0.075). (2) The axis geodesics
      were resampled by arc length while every interior edge is a chord of
      length L: the axis chords were short by L³κ²/24 and the first interior
      nodes read a 0.42° shear on a unit cylinder that no mesh refinement
      removed. Resampled at chord length the cylinder's net is the exact
      square lattice (90° ± 2e-13, ∬K dA ≤ 2e-15) and the sphere's cell
      error halved (1.58 → 0.55 % median). (3) The default app net on the
      catenoid (8 nodes per side) covers ∬K dA = −5.9 of the −2π a single
      net can carry and locks 56 of 153 nodes at 45°: the catenoid needs a
      smaller patch or several nets (Masson's singularities). (4) The nodes
      sit on the facets: with the net edge L only 3 mesh edges the
      Hazzidakis reading carries facet noise e²/(8L) in the angles (24 × 48
      sphere 3.4 % median cell error, 96 × 192 0.5 %). Follow-ups: a Lock
      input on the Grasshopper component (mode-menu catalogue) and a
      coloured `NetMesh`; the 10-19b bundle's `MinAngle` stop uses its own
      shear angle — replace it by `ShearAngle` when merging; Chebyshev
      nets with singularities (Masson 2017) where the 2π budget is exceeded.
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
      2026-10-18: on closed loops most of that error was the loop closure's
      reprojection zigzag (D, lath bending item), now gone; traced great
      circles stay within 0.03 edge of their plane with in-plane kinks ≤ 0.4°.
- [x] Web coverage: gap filling by further seed crosses one Spacing from
      the nearest curve (≤ 0.5 % uncovered on ten surfaces; was up to 94 %
      for a single cross); same-family contact cut (0.15 Spacing, 30°) —
      2026-09-25.
- [x] Web from a user guide curve (Schling's layout: seeds along a drawn
      curve instead of the border or the other family) — 2026-10-19: the
      core and Grasshopper `Guide` input (A above); the app has no curve
      drawing, so its `web · guide` layout (4) takes the horizontal section
      through the seed as the guide and draws the nodes. Open: a drawn guide
      in the app (click a polyline on the surface), several guides.
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
      toggle on the net demos. 2026-10-23: the curvature cards have the
      switch (A above) and the Asymptotic Net demo has a "saddle · classic"
      shape (Continuous off, the `asymClassic` numbers it already measured);
      still single-shape: Curvature Streamlines, Mesh Isocurves, Conjugate
      Net (the ellipsoid's lemon pattern belongs here), Chebyshev Net, the
      lath cards; Geodesic Net has no classic view.
- [x] Kinetic demo: a "fold" slider that plays the state sequence of E on
      the hyperboloid twist and on the catenoid net — 2026-09-26 (Net
      Kinetics card, Kinetics tab).
- [x] Metrics with distributions, not only extremes: spacing histogram,
- [x] Parameter tables from the sources — 2026-10-05 (session 2):
      `tools/webgen/extract_components.py` regenerates `components.json` and
      `icons.json` (the hand-kept file had lost 15 inputs — Continuous,
      MinAngle, FromBorder, BorderAngle, six Gridshell Analysis inputs — three
      outputs and the whole Asymptotic Web component). It keeps the file order
      and is idempotent; after merging bundles that touch components,
      regenerate instead of hand-merging. Asymptotic Web now has a bench card
      (catenoid web on the isothermal lattice, meridian diagonals geodesic).
      The review counter counts the cards instead of a fixed 28.
- [ ] Metrics with distributions, not only extremes: spacing histogram,
      crossing-angle histogram, utilisation histogram (small, consistent
      charts).
- [ ] Colour-map legends identical to Mesh Colour Map in Grasshopper. (App:
      the colour bars render again — 2026-10-02; the bench still has no
      utilization legend bar, only swatches.)
- [x] Colour-map legends identical to Mesh Colour Map in Grasshopper — 2026-10-03:
      one palette, black / white / neon, in the core (`Display/MitePalette`,
      `ColourScience`); Mesh Colour Map, Lath Preview, the app (colormaps.js,
      app.css, viewer.js) and the bench draw from it, and `PaletteTests` reads
      the app's files so they cannot drift apart. Literature: Kovesi 2015 (Good
      colour maps: uniform L*, equal-lightness diverging ends, sine-ramp test),
      Moreland 2009 (diverging maps), Crameri et al. 2020, Machado, Oliveira &
      Fernandes 2009 (dichromat simulation in linear RGB), WCAG 2.1 (contrast,
      SC 1.4.11 3:1 for graphics), CIE 15:2004 (CIELAB). Findings:
      - the 1.3.0 app diverging map ran from L* 29.8 (blue) to 46.0 (red):
        equal |K| read with different strength (asymmetry 16.1 L*); the maps
        are now cut from the same sRGB paths at equal L* steps — asymmetry
        0.58, L* within 0.96 of linear (was 5.6); sequential within 0.95 (was
        6.2); Grasshopper's RdBu was 6.4 asymmetric, viridis-like 1.8;
      - equal chroma at the diverging ends (Kovesi's second rule) is not
        reachable with neon blue: at L* 39.5 the crimson end has chroma 70 vs
        114; equal chroma would need L* 53 and a duller blue — kept the neon;
      - the bench's teal / plum families were ΔE 15 apart for simulated
        protanopes / deuteranopes, the app's blue / red are ΔE 102 (light) /
        76 (dark);
      - the app's light-theme green #00c865 had 2.2:1 on white (WCAG asks 3:1
        for graphics): now #00a653 (3.2:1); the 3D viewer used yet another
        green (#00d86b), now the same.
- [ ] Family G against B for deuteranopes: neon red and neon green are ΔE 12
      apart (light theme) / 25 (dark) for a simulated deuteranope — exactly
      Crameri et al.'s warning. No lightness for G separates it from B for
      both protanopes (B darkens to L* 43) and deuteranopes (B at L* 61) while
      keeping 3:1 on white; a teal G (#029ebf, ΔE ≥ 42 to A and B) or a lime G
      on black (#aff803, ΔE 57) would, or a dashed line style for G. José to
      choose; then change `MitePalette.FamilyG` (tests follow).
- [x] App 3D viewer: family colours per theme and the hard-coded neon
      constants through the palette — 2026-11-07 (session 1). `MitePalette`
      gains the viewer's roles `Seed`, `Mark`, `Mesh`, `Solid`, `Highlight`
      and `JavaScriptModule()`; the bench generator writes
      `src/Mite.Web/wwwroot/js/palette.js` next to `colormap-lut.js`, and
      `viewer.js` draws roles it resolves again on `setTheme` (materials and
      per-point role arrays keep their role; `colorCurves` overrides and raw
      colours stay). Measured (WCAG 2.1 contrast, Machado dichromacy): on the
      black page family A 3.19 → 4.99, B 5.56 → 6.13, G 6.58 → 15.64; the
      seed #00d86b on white 1.90 → 3.19 (`Ok` green); marks: neon yellow is
      1.15:1 on white and no olive with 3:1 keeps ΔE > 24 from B for
      protanopes, so the light theme's marks are ink (19.8:1, ΔE ≥ 43) and
      the dark theme's neon yellow (18.3:1, ΔE ≥ 37). Replaced off-palette
      constants: the slate wireframe (#3b4048 / #9aa3ad → ink at 0.35), the
      slate loft handles (#1f2937 → solid), amber notches (#ffb000 → warn),
      the 1.3.0 teal / plum fallbacks in `plots.js` and `app.js`. Tests:
      `PaletteTests` (the generated file current, CSS `--seed` / `--mark`,
      no colour literal left in `viewer.js` / `app.js`, the contrasts);
      `tools/web-smoke.js` 4c toggles the theme live; bench check on the
      Mesh Colour Map card. Open: the marks on the light page share ink with
      the crossing dots (sizes 7 vs 3.5 tell them apart; a dark olive would
      not read for protanopes — José to decide if yellow is wanted there);
      the uncoloured mesh stays #dbdbdb on both pages (a lit object), so
      markers on the surface read against L* 87 whatever the page — the
      dark theme's A / B / G are 3.0 / 2.5 / 1.0:1 on it (the light ones
      4.8 / 2.7 / 2.3): the neon green G of an AAG web nearly vanishes on
      the grey surface of the dark page, as it did before this change, and
      the neon yellow marks are 1.2:1 on it on either page. Follow-up: a
      darker surface on the dark page (`Mesh(Theme)`), or dark-page family
      and mark colours chosen against the surface rather than the paper —
      José to decide which; the pieces' pale shades (blends towards the
      page) are redrawn on toggle, not re-themed in place.
- [x] Grasshopper previews take the palette against the viewport background —
      2026-11-07 (session 3, B / C). Core `Display/ViewportPalette` (new file,
      nothing existing changed; `ViewportRole` FamilyA / FamilyB / FamilyG /
      Mark / Warn / Ink): for a background of relative luminance Yb the page
      theme is Light from CIELAB L* 50 (Yb ≥ 0.1842) and Dark below; a role's
      page colour is kept when it has WCAG 2.1's 3:1 (SC 1.4.11) — so white
      and black return MitePalette exactly, marks ink / #d9ff00 as the web
      app's 11-07 rule — else both themes' colours are moved to 3:1 in the one
      direction the background allows, darkened by the scale (Yb + 0.05)/3 −
      0.05 over Y in linear light (CIE 15:2004: XYZ scale together, the
      chromaticity stays — measured |Δx| + |Δy| ≤ 0.003) on light
      backgrounds, mixed with white in linear light (hue kept within 5°) on
      dark ones, and the one with the larger CIELAB chroma is drawn. Read:
      Ware 1988 (IEEE CG&A 8(5): the luminance channel processes shape far
      more effectively than the chromatic channels — a thin line needs
      luminance contrast), Healey 1996 (UBC TR-96-10: distinguishability as
      distance in a perceptual model), WCAG 2.1 (via ColourScience; w3.org
      is unreachable from the sandbox). Grasshopper `ViewportColours` reads
      the background the pipeline clears to (`DisplayPipelineAttributes`:
      solid fill, the linear-light mean of a gradient, else
      `AppearanceSettings.ViewportBackgroundColor`; bitmap / renderer /
      transparent fills fall back to the latter) and caches one colour set
      per background; `MiteNetGoo.Draw` takes it per frame. Measured on seven
      backgrounds white … #404040: least contrast 3.00 (the 1.6.0 teal / plum
      / amber / red read 2.09 / 2.63 / 1.22 / 2.07 on #a0a0a0, the amber
      2.15 even on white); on #a0a0a0 (L* 65.9) A #2434e3, B #a3102f, G
      #005f2d, marks ink 7.57:1, A–B ΔE 94.8 for dichromats, chroma kept
      ≥ 67 %; #c0c0c0 keeps A (3.62) and darkens B / G; #606060 (L* 40.7)
      lightens the dark theme to A #a9acff, B #ff959f. Tests
      `ViewportPaletteTests` (17); bench: Mite Net self-test. Findings: (1)
      McNeel's documentation does not state the default viewport background
      colour (Options › Appearance › Colors lists the setting without its
      value), which is why the plugin reads the live value instead of
      assuming #a0a0a0; (2) on a mid grey (L* 50–54) everything must be dark
      and B against G falls to ΔE 7.6–9.5 for deuteranopes — the known weak
      pair of the page palette (ΔE 12.8 on white, see "Family G against B"
      above), not made worse in kind; (3) a Grasshopper preview sits on the
      mesh's own preview too (GH's translucent material), which no single
      background accounts for. Open: Net Joints / Net Topology / Lath
      Preview outputs through the same roles; the Rhino check that
      `DisplayPipelineAttributes.FillMode` reports `DefaultColor` for the
      stock modes (then the application colour is the right one); once the
      11-07 viewer patch is merged, `ViewportPalette.PageColour(Mark)` can
      delegate to `MitePalette.Mark`.
- [ ] Icons in black / white / neon (`tools/generate_icons.py`) after the Mite
      Net bundle (two new icons) is merged, then the Food4Rhino screenshots.
      charts) — 2026-10-04 (session 2), `Analysis/NetDistributions`
      (`Distribution`, `Histogram`, `FamilySpacing`, `CrossingAngles`,
      `Utilization`), the Grasshopper **Net Distributions** component
      (Analysis) and a bench card with three small histograms per net (saddle
      fill continuous / classic, catenoid fill / web) and 7 self-tests.
      Definitions: spacing is cut across the lath (plane normal to its tangent,
      nearest cut on each side of the surface normal, 3D chord) — the strip
      width of Pottmann et al. 2010, whose 5 % / 7.5 % breakpoint tolerances
      give the "within" shares; histograms with Freedman & Diaconis 1981 bins
      (Sturges when IQR = 0), type-7 quantiles (Hyndman & Fan 1996).
      Closed forms: parallel lines exact, 720-gon rings 2e-6, sphere meridians
      tan θ′ = tan θ / cos Δ 2e-6, exact catenoid asymptotic lines to 2e-4
      (first-order law cosh v · Δc/√2, 7 % off at the rims for Δc = 15°), the
      z = xy ruling crossings 3e-14°; traced nets: crossing angles within 0.84°
      of the closed form on the saddle, 0.23° / 0.51° of 90° on the catenoid.
      Findings:
      - A continuous fill treats Spacing as the widest gap it tolerates, not
        the typical one: median gap 0.82 Spacing (saddle and catenoid), P95
        1.03–1.07, P5 0.44 (saddle; laths may converge to 0.15 Spacing). Only
        20 % (saddle) / 14 % (catenoid) of the lath length is within ±5 % of
        Spacing.
      - A web from the rims shows the surface's own spacing: on the catenoid
        Spacing at the rims, Spacing / cosh 1 at the waist (P5 0.189 vs 0.194),
        median 0.73 Spacing.
      - Classic mode: 30 of 492 crossings are more than 2° off the closed-form
        angle (worst 22°), all next to T-junction tips — the end blend over two
        Spacings bends the last part of a lath off the asymptotic direction;
        its spacing histogram has a bar at 0 (the merging tips).
- [ ] Fill calibration: decide whether `Spacing` means the median gap (scale the
      seed offset by ~1.2 and re-measure with Net Distributions) or stays the
      widest gap (document it on the component); compare with farthest-point
      seeding (D) by the same histograms.
- [ ] Classic-mode end blend: snap the tip onto the neighbour without bending
      the last two Spacings (or blend over one segment) and re-check the
      crossing angles next to T-junctions (bench: `dist.saddleClassic.angOff2`).
- [ ] The same three histograms in the web app's quality card, and a coloured
      spacing preview (points by local spacing) on the Grasshopper component
      (with the custom preview item in B).
- [ ] Colour-map legends identical to Mesh Colour Map in Grasshopper.
- [x] Search / filter (failing tests only, by component, by tab); keyboard
      navigation; ticked state summary exportable as a GitHub issue draft —
      2026-10-07 (session 2). `tools/webgen/review.js` + `review.css`,
      appended by `build.py` (template.html untouched, so the bundles that
      edit it do not conflict here). Literature: Shneiderman 1994 (dynamic
      queries: rapid, incremental, reversible, feedback within 100 ms —
      measured 8 ms median, 65 ms worst per keystroke on the 28-card page),
      Hearst 2009 ch. 8 (facets with query previews, no empty result sets,
      keyword search inside the facets, one click to clear), WAI-ARIA APG
      keyboard interface (shortcuts never replace Tab; avoid browser/AT
      keys; never lose focus), WCAG 2.1 SC 2.1.4 (single-key shortcuts can be
      switched off) and 4.1.3 (result count in a role=status region), GitHub
      Docs on issues/new query parameters. Find box over name, nickname, tab,
      description, expectations, self-test names and parameter rows (case,
      accents, minus/superscripts folded; terms AND-ed; "quoted phrases");
      facets All / Failing / To review / Reviewed / With notes and a tab
      picker, each showing the count that choosing it would give, disabled at
      0 except the active one; empty state with "Clear the filters"; state in
      the URL (?q=&show=&tab=). Keys `/` `j` `k` `x` `n` `f` `?` and Esc,
      inert in text fields and with Ctrl/Alt/Meta; a switch in the `?` dialog
      turns the single keys off (remembered per browser). "Open as GitHub
      issue" / "Copy as Markdown": a task list with failing self-tests first,
      title "Mite Bench review: a/b make sense, n self-tests failing"; title
      and body only (labels/assignees 404 for anyone without triage rights,
      per GitHub's docs); bodies over an 8 000-character URL budget are cut at
      whole lines with a note, the full text goes to the clipboard (28 cards
      ticked with 150-character notes: 6 131 characters, fits).
      Tests: `BenchReviewTests` (30, Jint 4.16.4 runs the pure functions:
      folding, AND/phrase queries, every facet count equal to the size of
      the result of choosing it over 120 states, key map incl. SC 2.1.4,
      URL round trip, exact Markdown, URL budget at 8 000 and 3 000 with
      multi-byte notes); `tools/webgen/bench-smoke.js` drives the built page
      in Chromium (40 checks, light and dark).
      Open: (a) GitHub's real URL limit is undocumented and could not be
      probed from the session (the proxy answered 403); 8 000 is an
      assumption — José can try a long review once. (b) Mode-menus bundle:
      its Message lines and menu tables are not in the search text yet
      (add their selectors to `textOf` after merging). (c) Per-card "copy link" to a
      filtered view; arrow-key roving focus inside the facet group (APG
      toolbar pattern) if the chips grow.
- [x] Colour-map legends identical to Mesh Colour Map in Grasshopper —
      2026-10-30 (session 2, C + B): one colour language. `Visualisation/
      ColourMap` builds three tables (diverging neon blue – white – neon red,
      sequential black – blue – teal – green – white, utilisation traffic
      light) and `Visualisation/Legend` the nice ticks and the strip; the
      plugin (Mesh Colour Map, Lath Preview), the web app (`colormap-lut.js`,
      generated from the core; a test fails when it is stale) and the bench
      sample the same tables by the same nearest-entry rule, and the bench
      checks every face of its torus demo against the library's colours.
      Literature: Moreland 2009 (Diverging color maps for scientific
      visualization, ISVC — Msh = polar CIELAB, interpolation through a white
      control point, AdjustHue; his cool–warm Tables 1–2 are reproduced to
      ±2 per channel as a test), Kovesi 2015 (Good colour maps: how to design
      them, arXiv:1509.03700 — equal lightness steps by cumulative |ΔL*|,
      lightness ≤ 95, equal-chroma ends), Crameri, Shephard & Heron 2020 (The
      misuse of colour in science communication, Nat. Commun. 11, 5444 —
      perceptual order, CVD and greyscale readability), Heckbert 1990 (Nice
      numbers for graph labels, Graphics Gems I — 1, 2, 5 × 10ⁿ steps,
      nicenum(range) / (NTICK − 1)), Talbot, Lin & Hanrahan 2010 (An
      extension of Wilkinson's algorithm for positioning tick labels — read,
      not implemented: its coverage/density scoring is for axes that may be
      extended; a colour bar cannot be, so ticks are kept inside the domain
      and the exact ends labelled separately). Findings:
      - Lightness equalisation must run on the continuous L* of the path, not
        on the 8-bit colours: rounding noise (±0.2 L*) counted as |ΔL*|
        inflated the cumulative where the true gradient is small and left
        half-size steps (46–59 % window deviation instead of 5–7 %).
      - José's neons are more saturated than the white in Moreland's M
        (114 and 92 against 95), so Moreland's L* = M cos s overshoots the
        centre by 1.6 just before it on the blue half; the lightness is
        therefore taken linear between the ends and only chroma and hue follow
        the Msh path. The neon red (M 92 < 95) still gets Moreland's hue spin
        (17° towards orange near white), the blue (M 114) does not.
      - The blue end is darker than the red (L* 29.9 vs 46.0), so the blue
        half has 33 % more lightness contrast per unit value: Kovesi and
        Moreland ask for equal-lightness ends. Kept José's exact colours;
        equal ends would mean a darker red (≈ #92001e at L* 29.9) or a lighter
        blue (≈ #694bff at L* 46).
      - A hue sweep blue → cyan → green in LCh hits the sRGB gamut wall at
        cyan (a visible seam where chroma is clipped 95 → 44); a straight
        CIELAB line between the neons greys out (chroma 21). A teal control
        point (the RGB midpoint of the app's ramp) gives a smooth path with
        chroma ≥ 28.
      - The app's viewport colour bar had been 9 px wide since the marker
        legend (`.legend i { width: 9px }`) was added: scoped to `.hud-bl`.
      Open: the Grasshopper legend labels need a Text Tag (no text in a
      component preview without a custom preview — B item); a `Legend` input
      to switch the strip off; the Food4Rhino screenshots in the new colours;
      per-component colour of curves (family A / B) is still the viewer's.
- [ ] Search / filter (failing tests only, by component, by tab); keyboard
      navigation; ticked state summary exportable as a GitHub issue draft.
- [ ] Component cards show the Grasshopper icon at readable size and the
      parameter order as it appears on the canvas (icons refreshed
      2026-09-25).
- [ ] Page weight: 1.7 MB with the catalogue scenes; thin the wireframes or
      load scenes lazily per card. 2026-10-23: 2.23 → 2.79 MB — the eight
      curvature scenes carry six values per vertex (K, H, k1, k2, exact K and
      H) at 4 decimals plus the quad grid; lazy loading per card is the fix.

## D. Algorithms to research and improve (with literature)

- [x] Geodesic families: Jacobi-field start angles after Pottmann et al. 2010
      (§4 evolution), border seeding; regions unreachable by sideways growth
      get their own seed (field nets) — 1.2.2. Asymptotic families are combed
      over the mesh and a MinAngle input keeps nets out of the near-parabolic
      band — 1.2.2. Still open: farthest-point seeding (next item). The
      combing is replaced by exact twist labels (A, 2026-10-06).
- [ ] Evenly spaced curve families on surfaces: Jobard & Lefer 1997
      band — 1.2.2. Farthest-point seeding done 2026-10-07 (next item). Still
      open: a check that combing handles the 3-fold Enneper flat point (A).
- [x] Evenly spaced curve families on surfaces: Jobard & Lefer 1997
      (Creating evenly-spaced streamlines of arbitrary density); Mebarki,
      Alliez & Devillers 2005 (Farthest point seeding for efficient
      placement of streamlines) — 2026-10-07: `NetLayout.FillFarthest`
      (Layout = 3 on Asymptotic Net, Conjugate Net, Curvature Streamlines;
      `EvenlySpacedNet.Farthest.cs`; new API only: `Options.Saturation`,
      `LargestGap`, `RejectedSeeds`). Read from INRIA RR-5524 and CGAL's
      Stream_lines_2 source. Their parameters disagree by a factor 2 (prose:
      seed voids whose circumcircle *diameter* exceeds saturation × spacing;
      CGAL: circumradius ≥ dSep·dRat, stop at dSep, frame dSep outside); both
      agree with Mite's Spacing S = 2·dSep: seeds ≥ 0.8 S from every curve,
      frame ½ S outside the border (no seed on the border itself), stop
      distance kept as TestFactor / MergeFactor so the two fills differ only
      in where curves start. On the mesh the Delaunay voids are a vertex
      distance field (Dijkstra carrying the nearest curve point, Euclidean
      re-measure), the seed refined to the empty circle's centre by a ray
      scan and a compass search with exact polyline / border distances.
      Exact checks: uniform field on a plane — all 15 curves at the
      predicted void centres within 0.0002 S; torus meridians — exactly 2^k,
      every outer gap in [0.8, 1.6] S (Layout 0: 56 meridians, 0.89–1.89 S).
      Saddle / catenoid / hyperboloid (family A, classic): 25 / 27 / 35
      curves instead of 31 / 33 / 39, same mean length, free ends 0 / 0 / 0
      instead of 4 / 0 / 0, median gap 0.85 / 0.89 / 0.81 S instead of
      0.83 / 0.73 / 0.72, but p95 1.39 / 1.35 / 1.28 instead of ≈ 1.0.
      Finding: the paper's "longer streamlines" did not show on these
      surfaces (mean length equal) — Mite's continuous tracing already runs
      curves border to border; what FPS changes is fewer T-junctions and a
      spacing that honours S in the median instead of converging below it.
      Not done / follow-ups:
      - Geodesic families keep Layout 0: a geodesic has no direction of its
        own in the middle of a void; seeding it parallel to the nearest
        curve (transported) gave 60 of 112 free ends on the saddle and
        4 rejected seeds on a cylinder vault. A geodesic FPS would need the
        Jacobi start angle of the neighbour fill at the void centre.
      - The web app has no Layout 3 yet (its Layout 3 is the asymptotic
        web): add a "farthest" chip in `Api.Net` (e.g. Layout 4).
      - Mebarki's multiresolution (lengthen existing curves before densifying)
        and variable density (spacing from a field, e.g. utilisation) are
        not implemented.
      - A tapering / Turk–Banks pass for the gaps that end up near 1.6 S.
      - Cost: 0.8–2.5 s per family against 0.2–0.6 s for Layout 0 (the
        vertex scan per seed is O(n); a heap of vertex keys would remove it).
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
      B started orthogonal to A at the seeds) — open; per-family profiles
      (B above) — done 2026-10-27; a joint rotational-stiffness parameter in
      Gridshell Analysis — done 1.3.0; the bench check that the AGH net on
      the catenoid has A upright / B flat with kn(B) ≠ 0 and kg(B) = 0 —
      done 2026-10-27 (Net Joints and Gridshell Analysis cards, catenoid ·
      hybrid shape, four self-tests; the numbers are in B above).
      B started orthogonal to A at the seeds), per-family profiles (B above),
      a joint rotational-stiffness parameter in Gridshell Analysis, and a
      bench check that the AGH net on the catenoid has A upright / B flat
      with kn(B) ≠ 0 and kg(B) = 0.
      Done 2026-10-31 (session 1), the layout itself: the AGH gridshell's web
      is an AAG web whose geodesic planks are "interlaced at the midpoint of
      the asymptotic beams to create a tri-hex pattern" (Wan, D'Acunto &
      Schling 2024 §2.2.1; Schling et al. 2022 §4.2 built the first module
      "shifted by half a grid"), with "constant 60° intersection angles
      between asymptotic curves". See G below (tri-hex AAG web, constant
      crossing angle, the rotational CRPC test surface).
- [ ] Geodesic gridshells: Pirazzi & Weinand 2006 (Geodesic lines on
      free-form surfaces — optimized grids for timber rib shells); Pottmann
      et al. 2010 (Geodesic patterns) — geodesic strip patterns with
      constant width, breakpoints/segmentation.
      Partly done 2026-10-25 (session 2): geodesic laths end straight at
      contacts. Read: Pottmann et al. 2010 §2 (the strip width obeys
      w″ + Kw = 0, constant only where K = 0, so geodesic strips converge
      wherever K > 0 — and on the flat annulus the radial laths converge
      linearly), §3 "extension to broken geodesics" (a pattern of geodesic
      arcs meeting at breakpoints, inserted where the width leaves
      W ± 5–7.5 %; the arcs themselves are never bent); Weinand & Pirazzi
      2006 (WCTE Portland): boards along geodesics stay straight and are
      "subjected only to bending about their weak axis and to torsion".
      Mite's geodesic families (`TraceGeodesics`, all layouts) pulled a lath
      that stopped beside a neighbour sideways onto it and blended the shift
      over the last two spacings (`SnapEnds` / `BlendEnd`, shared with the
      asymptotic nets): a smooth in-surface bend — strong-axis bending of a
      flat lath, 7° of turn on a flat annulus (bow 0.0147 at Spacing 0.15),
      in-surface turning up to 0.30 × Spacing on a dome seeded from its rim.
      Now `EndGeodesicAtNeighbours`: the geodesic is traced in full; from the
      seed outwards the first point within the merge radius (within 30° of
      the neighbour for webs, any approach for the fill) is the contact; if
      the straight continuation crosses that neighbour within two spacings
      the lath ends exactly at the crossing (segment–segment test against the
      stored polylines, the neighbour's own point, so Net Topology sees the
      T-junction), else at the contact with a free end (Pottmann's
      breakpoint: the strip has narrowed to the lath width). Numbers: flat
      annulus bow 0.0147 → 3e-15 (rim seeds) and 7e-5 (web); dome from the
      rim max turning 0.296 → 0.090 × Spacing, mean 0.0134 → 0.0028, 24 / 24
      T-junctions on the neighbour; dome web 0.238 → 0.030; catenoid web
      0.255 → 0.047. The remaining 0.05–0.09 × Spacing maxima are the first
      segment after a border seed and the border exit (a 0.5–1° turn,
      pre-existing). Open: (a) the same for the asymptotic nets — the blend
      turns an asymptotic curve by up to 7° off its direction, kn = k1 sin 14°
      on a minimal surface, which an upright lath cannot take either; the
      asymptotic fill relies on the T-junction geometry, so the cut must keep
      the end on the neighbour (a short straight continuation along the
      asymptotic direction) — measure first with `AnnulusTests.GeodesicQuality`
      on the catenoid; (b) Pottmann's width-driven breakpoints proper: end a
      geodesic where the strip width leaves [W − ε₁, W + ε₂] and start the
      next arc bisecting the angle, instead of only where laths touch;
      (c) constant-width geodesic strips (Pottmann §4 level sets, §5 the
      piecewise-geodesic vector field) for Lath Strip.
- [x] Straightest geodesics and shortest paths: Polthier & Schmies 1998
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
      Done 2026-10-08 (session 1): FlipOut for Geodesic Path
      (`Gridshells/FlipGeodesic`, `Geometry/IntrinsicTriangulation`, Method
      input appended, default 1). Literature: Sharp & Crane 2020 (Alg. 1
      FlipOut on the joint of smallest wedge angle; flippable = convex quad;
      boundary wedges ∞; Thm 4.1 shorter, Thm 4.2 terminates), Sharp,
      Soliman & Crane 2019 (signposts φ_ij rescaled by 2π/Θ_i, flip update
      φ_ik = φ_ij + 2π/Θ_i θ_i^jk, trace by unfolding), Mitchell, Mount &
      Papadimitriou 1987 (Lemma 3.4: through a vertex only with ≥ π on both
      sides), Lanthier, Maheshwari & Sack 2001 (fixed Steiner scheme).
      Measured: polyhedral cone 1000/1000 random pairs equal to the exact
      development (< 1e-13) and none through the apex; prism cylinder,
      irregular flat triangulation and a saddle vertex (through it, |p|+|q|)
      to 1e-12; angle sums kept to 6e-13 over 985 random flips, every edge
      traced to its head within 2e-13. Findings:
      - Sharing one intrinsic triangulation across pairs is wrong: the long
        thin edges left by earlier flips steered a later Dijkstra start round
        the wrong side of the apex (32 % too long); every pair now resets.
      - FlipOut keeps the side of every vertex its start path takes. From
        plain Dijkstra 998/1000 cone pairs were shortest (the 2 misses had
        the other way round within 2.7 %), and on the torus / Schwarz D the
        Dijkstra-started result was up to 1.7 % longer than the old curve
        shortening found. A Steiner-graph start (3 points per edge, A*)
        fixed the cone (1000/1000) and the torus (now up to 1.5 % shorter
        than the old method, which itself took the wrong side); Schwarz D,
        where every vertex is a saddle, stays within −0.24 … +0.14 % of the
        old method over 20 random pairs: geodesics of nearly equal length
        are many there and the start path decides which one FlipOut returns.
      - long.GetHashCode XOR-folds the halves, so (a, b) / (b, a) edge keys
        collided: a 20 k-vertex build took 12 s; a mixing comparer makes it
        0.2 s. Cost now: 0.1–0.5 s per 20 paths on 2–4 k vertices, 2 s on
        17–20 k (1.2–15× faster than the curve shortening).
      - The signpost walk (`IntrinsicTriangulation.TraceFromVertex`) is the
        exact polyhedral straightest geodesic asked for above (unfolding per
        facet, half the total angle on either side at a vertex). Open: use it
        in the Geodesic Net / Jacobi tracer, which still steps through the
        smooth-normal tangent planes.
      - API: additions only (`FlipGeodesic`, `IntrinsicTriangulation`,
        `MeshProjection.ClosestPointGlobal`); `ShortestPath.Compute` now
        projects its end points with the exact global search.
      - Open: global minimality on saddle-rich meshes (several starts, e.g.
        Steiner graphs with 3 and 6 points, or a heat-method distance, Crane
        et al. 2013, to pick the class); paths through mesh vertices could
        be reported as a viewport marker once the custom previews (B) land.
- [ ] Curvature estimation: Rusinkiewicz 2004 (Estimating curvatures and
      their derivatives on triangle meshes); Meyer, Desbrun, Schröder, Barr
      2003 (Discrete differential-geometry operators); Cohen-Steiner &
      Morvan 2003 (Restricted Delaunay triangulations and normal cycle) —
      normal-cycle tensors are more robust on irregular meshes. Data point:
      k1·k2 is 6% off at the hyperboloid rims while angle-deficit K is 0.3%.
- [x] Umbilics as degenerate points of the direction field — 2026-10-20
      (session 2), `Umbilics.Locate` / `Degenerate` / `IndexSum` (the vertex
      threshold `Find` unchanged; Grasshopper `Cancel` input, `Umbilics` and
      `Index` outputs appended; web app marks them). Literature: Delmarcelle &
      Hesselink 1994 (The topology of symmetric, second-order tensor fields)
      and Tricoche, Scheuermann & Hagen 2001 (Topology-based visualization of
      2D symmetric tensor fields): the deviator (α, β) = (½(S₁₁ − S₂₂), S₁₂)
      vanishes at the umbilic, is interpolated linearly per cell, δ = det
      ∂(α, β)/∂(x, y) > 0 wedge (+½), < 0 trisector (−½); Ray, Vallet, Li &
      Lévy 2008 (N-symmetry direction field design) and Crane, Desbrun &
      Schröder 2010 (Trivial connections) for the per-face index of a
      vertex-based N-RoSy field: Σ of the parallel-transported angle changes
      along the edges (wrapped to ±π/N) plus the holonomy of the transport
      round the face, over 2π; Hopf 1926 / Porteous 2001 for Σ index = χ.
      Findings: (1) the per-face linear deviator alone is not consistent
      across faces on a mesh (each face transports the corner tensors into its
      own plane), so when the umbilic falls on a vertex — as it does on the
      symmetric ellipsoid mesh — the zero sits just outside every fan face or
      inside two mirror faces: 0 or 8 points instead of 4. The winding index
      per face is exact (every value a multiple of ½ to round-off, the sum
      exactly χ: 2 on the ellipsoid and the ellipsoid of revolution, 0 on the
      torus) and selects one face per umbilic; the deviator zero is then only
      used to place the point inside that face (clamped). (2) Delmarcelle's δ
      agrees in sign with the winding index at every located point. (3) The
      ellipsoid of revolution's poles (index 1, non-generic) come out as four
      +½ wedges in the pole fans, sum 2 — the discrete field splits them, as a
      perturbation would. Cost: 11 ms on 4514 vertices.
      Open: lemon vs monstar from the cubic form (Berry & Hannay 1977,
      Maekawa, Wolter & Patrikalakis 1996); the separatrices (one or three
      lines of curvature through the umbilic, Tricoche's cubic in u = tan θ)
      as curves, which Curvature Streamlines could use to start clean nets at
      the umbilics; the located umbilics as seeds for Asymptotic Web's flat
      points (a star at a flat point, K = 0 and k1 = k2 = 0, is exactly the
      3-fold Enneper centre); a sub-facet position by a jet fit round the face.
- [ ] Conjugate and planar-quad nets: Liu, Pottmann, Wallner, Yang, Wang
- [x] Conjugate and planar-quad nets: Liu, Pottmann, Wallner, Yang, Wang
      2006 (Geometric modeling with conical meshes and developable
      surfaces); Zadravec, Schiftner, Wallner 2010 (Designing quad-dominant
      meshes with planar faces); Pottmann, Asperl, Hofer, Kilian —
      Architectural Geometry (2007) for the whole vocabulary.
- [x] Chebyshev nets and elastic gridshells: Masson 2017 (Existence and
- [x] Conjugate and planar-quad nets, first part — 2026-10-22 (session 1):
      `Fabrication/NetFaces` traces the panels of any net (the faces of the
      net graph: members sorted about the node normal by their quarter-point
      direction so tangentially touching laths order correctly, next edge =
      clockwise from the reverse edge, clockwise cycles = outer loops and
      holes dropped, tails out, closed laths' seams fused, duplicate members
      along one path dropped) and measures each panel: the distance between a
      quad's diagonals over the mean diagonal (Zadravec, Schiftner & Wallner
      2010, §6; red above 0.01) and the largest corner distance from the
      plane (the Planarize Mesh measure). Read: Liu, Pottmann, Wallner, Yang &
      Wang 2006 (§1.1, §2 "quad meshes with planar faces may be seen as a
      discrete version of so-called conjugate curve networks", Sauer 1970;
      §2.2 the angle-sum planarity constraint; §4 curvature lines the main
      conjugate net, asymptotic directions "degenerate"); Zadravec et al. 2010
      (§1 planarization "in general succeeds if it follows a network of
      conjugate curves", §3 the conjugate directions lie between the
      asymptotic ones, §6 δ_PQ,n and the proximity term); Pottmann, Liu,
      Wallner, Bobenko & Wang 2007 §3.4 (asymptotic directions "cause heavy
      distortions"). Derived: the corners of a panel with step h close up to
      (p₀₀ − p₁₀ − p₀₁ + p₁₁)·n = h²·II(t₁,t₂) + O(h³), so the diagonal
      distance is ½h²|II(t₁,t₂)| + O(h³) — zero at second order on a conjugate
      net, maximal on an asymptotic one. Exact tests (`NetFacesTests`, 7): a
      lifted-corner quad's diagonal distance δ/(2√(1 + δ²/2)); the x / y
      grid of z = x² − y² (a translation surface) planar to 1e-12, every
      panel counter-clockwise, areas summing to the patch within 1 %; the
      catenoid's meridians × closed parallels planar to 1e-12 with the seam
      panels present (Euler 0 on the annulus); the hyperboloid's rulings:
      diagonal distance within 0.2 % of ½h² at the throat and δ_PQ,n
      5.9e-2 → 2.9e-2 (ratio 2.02) when the ruling count doubles, its
      meridians × parallels 4e-16; exact RK4-traced curvature lines of a
      cubic graph: δ 8.2e-5 → 1.5e-5 (ratio 5.4, ≥ 4 = second order) against
      asymptotic lines 7.7e-2 → 3.8e-2 (ratio 2.0), the central panels at
      0.98–0.99 of the predicted h²; a traced classic saddle net with
      T-junctions: V − E + F + lenses = 1, pentagons at the T-junctions, every
      panel along the mesh normal; Planarize Mesh leaves the curvature-line
      mesh in place (< 1e-9) and flattens the ruling mesh only by moving nodes
      0.4 (off the hyperboloid by 0.83), and not at all with the outer ring
      held (8.86e-3 → 8.82e-3). Findings: (1) NetTopology's member ends are
      snapped to the crossing nodes, half the crossing gap off the lath, so a
      closed lath's seam has to be recognised within a quarter segment, not at
      1e-6; (2) the tracer leaves two laths running together for a piece where
      curves merge — identical members between the same nodes — which no face
      tracer can order: they are deduplicated; (3) the simple projection
      planarizer converges slowly (2000 iterations to 1e-5 on 912 skew quads,
      300 iterations take the catenoid's curvature-line panels only from
      1.5 to 0.1 mm) and drifts the mesh when nothing is held.
      Open: a Newton / Gauss–Newton planarizer with Liu's fairness and
      proximity terms (a few iterations instead of thousands, the mesh kept
      near the surface); conical / circular meshes from curvature-line nets
      (offset meshes for multi-layer structures, Pottmann et al. 2007 §4);
      panels of the Asymptotic Web's own quads are its `Quads` already — the
      web's planarity could be reported there; a `Panels` output on Mite Net
      once that lands.
      Planarizer done 2026-10-22 (session 2), `FormFinding/PlanarizationOptimizer`
      behind Planarize Mesh `Method` 1 (default; 0 = the old per-face
      projection). Read: Tang, Sun, Gomes, Wallner & Pottmann 2014
      (Form-finding with polyhedral meshes made simple, §3–4: auxiliary unit
      normals n_f, planarity n_f·(v_i − v_j) = 0 on every edge, one guided
      projection step = ‖HΔ + φ‖² + ‖Kx − s‖² + ε²‖Δ‖² → min, ε = 1e-3, about
      10 iterations, weights only for regularisation); Liu et al. 2006 §3.2
      (angle-sum / determinant constraints, fairness + closeness with weights
      halved every iteration so planarity gets exact, SQP vs penalty); Zadravec
      et al. 2010 §5–6 (f_PQ + λ_fair‖Δ² − Δ²_orig‖² + proximity through the
      tangent plane at the closest point + boundary proximity, Gauss–Newton,
      13–39 iterations, δ_PQ 3e-4 … 1.3e-3 with δ_prox 3e-4); Bouaziz, Deuss,
      Schwartzburg, Weise & Pauly 2012 (Shape-Up: the local–global projection
      scheme the old Method 0 is a Jacobi version of; it cannot beat a compromise
      either); Poranne, Ovreiu & Gotsman 2013 (planar meshes as a linear space
      once the normals are fixed). Implemented: Tang's constraints and step with
      Zadravec's relative fairness, Liu's decay (0.4 per iteration), LM damping
      on the gain ratio, a tangential anchor at the start positions, normals
      eliminated per face (3 × 3 Schur complement) before the envelope LDLᵀ,
      everything in units of the mean edge length, a fold guard (no face below
      ¼ of its area). Numbers: exact PQ meshes fixed to 1e-12; jittered grid
      with plane reference back to z = 0 in 13 iterations (projection loop
      1.7e-4 after 2000); conjugate net knocked 0.02 off a hyperboloid planar
      to 7e-10 in 11 iterations, 0.014 off (projection 2.3e-5 / 0.016); 1 600
      nodes 10 iterations / 6 s; scale and position invariant to 4e-13.
      Findings:
      - The raw bilinear residual n·(v_i − v_j) scales with the edge, so a
        least-squares path lowers it by shrinking quads; dividing by the rest
        length removes that route but a folded quad (bow-tie, equal edges,
        zero area) is still a planar solution — the fold guard refuses it.
      - Measuring soft terms in bounding-box units while the planarity rows
        are angles under-weights anchor and closeness by the number of edges
        across the mesh (×34 on a 24 × 24 grid): the step then sheared quads
        sideways by half an edge. Edge units fix it.
      - Zadravec's relative fairness is what keeps quads from crumpling: at
        weight 0.1 a noisy 24 × 24 grid with its border held folds quads into
        the guard and stalls; at 0.5 / 1.0 it planarizes with every quad above
        0.39 / 0.55 of its area. Default 1.0; the price is that a jittered
        input keeps more of its jitter (0.009 → 0.014 off the surface).
      - With constant weights (Tang, Zadravec) the fixed point is a compromise
        Hᵀφ = −Kᵀ(Kx − s), planarity 1e-5 … 5e-2; the decay is what makes it
        exact. Near the manifold the soft cost dominates and a step that halves
        the hard cost raises the total by parts in 10⁵, which a plain LM
        refuses — such steps are accepted (≤ 1 % total increase) and the
        damping is reset when the weights decay.
      - The hyperboloid's asymptotic ruling net has no planar realisation on
        its surface (self-conjugate directions): the optimiser either stalls
        with the quads intact (Decay 0.5) or finds a planar mesh 0.15 off the
        surface with nodes moved an edge length (Decay 0.4); the projection
        loop moves nodes 0.5 and is still at 1.4e-4 after 2000 iterations. The
        component warns in both cases. A warm start from the projection loop
        is erratic (converges from 20 projection steps, stalls from 100 or
        500): not used.
      Open: the panels of a traced net (this morning's `NetFaces` bundle)
      wired into Planarize Mesh through Net Topology's `Mesh` output with the
      design surface as `Reference`; Zadravec's boundary proximity (border
      nodes on the border curve rather than the tangent plane); conical
      (ω₁ + ω₃ = ω₂ + ω₄) and circular variants on the same solver (Liu §2.2,
      Pottmann et al. 2007 §4) for offset meshes; nested dissection or a
      supernodal LDLᵀ for 4 k-node nets (16 s now; 3 s per factorisation);
      `AagWeb` and `ScissorNet` could share the row-based `NormalEquations`
      (they keep their own copies); the 1.4.0 mode catalogue must list Planarize
      Mesh `Method` (0 projection, 1 guided projection) — its test fails until
      it does, by design.
- [ ] Chebyshev nets and elastic gridshells: Masson 2017 (Existence and
      construction of Chebyshev nets and application to gridshells);
      Garg et al. 2014 (Wire mesh design); Baek, Reis 2019 (Rigidity of
      hemispherical elastic gridshells under point load indentation);
      Douthe, Baverel, Caron 2006 (Form-finding of a grid shell in
      composite materials) — compass method limits, shear-angle bounds.
      Done 2026-10-19 (session 2): the compass step is exact and the net
      stops where it locks. Read: Chebyshev 1878 via Papadopoulos 2016
      (arXiv 1608.02724: ds² = du² + 2 cos ω du dv + dv², Gauss equation
      ω_uv + K sin ω = 0), Marvan 2024 (arXiv 2403.12626, eq. 6.5, the same
      sign), Hazzidakis 1880 (∫∫_Q K dA = 2π − Σα, re-derived from the Gauss
      equation: corner angles ω, π − ω, ω, π − ω), Samelson & Dayawansa 1995
      (|∫K| < π/2 on every measurable set), Burago, Ivanov & Malev 2007
      (positive and negative total curvature each < 2π), Masson & Monasse
      2017 (∫|K| < 2π; title and abstract only), Otto, Hennicke & Matsushita
      1974 and Bouhaya, Baverel & Caron 2015 §3 (fourth node = surface ∩ two
      spheres of radius L), Garg et al. 2014 (η_len = max |‖e‖/L − 1|, angle
      bound ζ = 30°). Not accessible: Masson's thesis itself (HAL is behind a
      bot wall) and Garg's full text (36 MB post-print). Changes: Newton in
      the tangent plane with reprojection for the compass point (Jacobian
      determinant 4 L² sin ω, singular exactly at locking), axes sampled by
      chord, `MinAngle` refusing every node that would create a crossing
      outside [min, π − min] at itself or its parents, `ShearAngles`,
      `EdgeError`, `Locked`, `Failed` in the result, `MinAngle` / `Report` on
      the component. Measured (`ChebyshevNetTests`, 10 tests): edge error
      1e-14 everywhere (0.2–0.3 % before); flat lattice exact to 1e-15 at 90°
      and 60°; prism cylinder 1.6e-3 / 4.3e-4 at 64 / 128 sides (sagitta
      1.2e-3 / 3.0e-4); unit sphere against the closed-form compass net
      (x·a = x·b = 1 − L²/2) 5.1e-3 / 1.1e-3 / 3.0e-4 at 24 / 48 / 96
      divisions — the facets lie e²/4 inside the sphere and the compass
      carries that offset on; before the fix 1.4e-3 at 48 and 1.6e-3 at 96
      (no convergence). Findings: (1) the compass never fails on a dome —
      the circles still meet when the cell has folded through ω = 0, so
      the old net ran on into a crumpled sheet past the lock (12 × 12 patch
      at L = 0.2: ω down to 0.5°, then the mesh and closed-form nets choose
      different roots); a shear bound is the only honest stop; (2) checking
      the angle at the new node alone leaves crossings at its parents 4–5°
      below the bound, so every angle the new node creates (at itself and
      at both parents, against their edges on both sides) is checked;
      (3) Hazzidakis on the discrete net: the angle deficit of a vertex
      star in a consistent triangulation of the cells integrates K = 1 to
      0.5 % over the nodes whose cells stay above 30° (0.97 with every open
      node — thin triangles near the fold spoil the local estimate), and
      the skew-quad corner-angle defect does not (0.45): the discrete
      Hazzidakis identity needs the triangulated deficit, not the quad's.
      Open: free (non-geodesic) axis curves — Masson's Cauchy problem from
      two drawn curves (the `Guide` input of the nets would carry them);
      nets with singularities (Masson 2017, Sageman-Furnas et al. 2019) for
      domes whose total curvature exceeds the bound; the one-soliton
      sine-Gordon net on the sphere as a smooth closed form (the discrete
      net converges to it only as L → 0); a shear-angle colour map on the
      net mesh once the custom previews (B) land; the AGH hybrid needs the
      geodesic family wrapped through the compass cells (D above).
      Partly done 2026-10-26 (C item above): shear-angle bounds (Garg's
      γ_max = π/4 from Instron tests, Sageman-Furnas' ζ = 30°), Hazzidakis'
      formula ∬K dA = 2π − Σ α_i as the per-cell curvature reading, the 2π
      existence bound (Burago, Ivanov & Malev 2007: ω⁺, ω⁻ < 2π, a quadrant
      between orthogonal geodesics needs each < π/2; Masson & Monasse 2017),
      the chord-length axis fix. Open: Masson's nets with conical
      singularities past the 2π budget; the compass method's own limits
      (10-19b bundle: MinAngle stop, locking report); elastic rigidity
      (Baek & Reis). Read: Garg et al. 2014 (full text, §3–5), Sageman-Furnas,
      Chern, Ben-Chen & Vaxman 2019 (full text), Burago, Ivanov & Malev 2007
      (arXiv math/0506580, theorems 1–3); Masson 2017 and Douthe et al. 2006
      only through secondary sources (HAL blocks automated access).
- [x] Linear buckling of the lath frame — 2026-10-26 (session 2),
      `FrameAnalysis.Options.BucklingModes`, `Numerics/SubspaceEigen`,
      `SparseSymmetricSolver.NegativePivotCount`; Gridshell Analysis `Buckling`
      input and `BucklingFactor` / `Factors` / `Mode` outputs appended.
      Literature: McGuire, Gallagher & Ziemian 2000 (Matrix Structural
      Analysis, 2nd ed., eq. 9.19: the 12 × 12 consistent geometric stiffness
      with the axial P/L and the Wagner I_p/A torsion term; the Bucknell PDF
      is behind a Cloudflare gate — cited from the edition, verified against
      Gavin 2012, Geometric stiffness effects in 2D and 3D frames, Duke CEE
      421L, whose Timoshenko form reduces to it at Φ = 0), Przemieniecki 1968
      ch. 15, Bathe & Wilson 1973 / Bathe 1996 §11.6 (subspace iteration,
      Sturm sequence check, K_G φ = κ K φ for indefinite K_G), Timoshenko &
      Gere 1961 §2.2 (Euler loads), Chen & Lui 1987 ch. 5 (exact beam-column
      stiffness in compression, used for the portal reference).
      Measured: fixed–free column π²EI/4L² from above, error 5.1e-4 / 3.3e-5 /
      2.1e-6 / 1.3e-7 with 2 / 4 / 8 / 16 elements (ratios 15.6, 15.9, 16.0 —
      the O(h⁴) of the cubic element); mode 1 − cos(πx/2L) to 3e-12; round bar
      double eigenvalue equal to 1e-14 relative, 2e-6 off the closed form at 8
      elements; fixed-base portal (h = l, equal EI) P h²/EI = 7.37915 from the
      stability-function determinant, FE 3.1e-3 / 2.1e-4 / 8.5e-6 with 2 / 4 /
      8 elements per member; two-arch grid: rigid λ 0.091 / 0.109 / 0.136,
      scissor-hinged 0.023 / 0.045 / 0.081 (1 kN/m, 80 × 20 mm upright, R = 5);
      bench saddle net (54 laths, 3013 nodes, 1 kN/m, 80 × 12 mm): λ₁ = 32.65,
      λ₂ = 32.65 (a near-double pair from the saddle's symmetry), none missed.
      Findings: (1) the Sturm check must be made just below λ₁, not just above
      the last factor — the saddle's λ₂ sits 0.003 % above λ₁, so a "count
      ≤ λ_last·1.001" test flagged a missing mode that was only the twin; and
      its margin must exceed the convergence error: Rayleigh–Ritz estimates
      converge from above by ~3× the last change at ratio 0.75, so a margin of
      1 × tolerance counted λ₁ itself as "missed" on the saddle net (it is
      20 × tolerance now);
      (2) convergence is set by the spectrum, not the solver: the saddle net
      needs 42–60 iterations (ratio |λ₁/λ_{q+1}| ≈ 0.75) and more vectors
      (q = 17, 25) cut the count but not the time; the static deflection as a
      start vector saves ~15 %; compressed-row products instead of the Builder
      dictionaries 12 → 8.5 s; what is left is the envelope solve (~20 ms each
      on 18 k DOF) — a Lanczos or a fill-reducing ordering is the next step;
      (3) members in tension make −K_g indefinite and give negative factors
      (reversed load); they are filtered, and with nothing in compression the
      solve is skipped. Open: bending-moment terms of the geometric stiffness
      (lateral–torsional buckling of upright laths, the realistic failure of
      deep thin strips — Trahair 1993; done 2026-11-06, next item), the web
      app's structure block (payload field + stats line, off by default for
      speed), imperfection sensitivity by a nonlinear (arc-length) solve with
      the first mode as the shape.
- [x] Lateral–torsional buckling: bending-moment terms of the geometric
      stiffness — 2026-11-06 (session 1, D; José's priority 2 "lateral–torsional
      terms for upright strips"). `FrameAnalysis.Options.LateralTorsional`
      (default on), `GeometricStiffness(N, L, I_p/A, M_yA, M_yB, M_zA, M_zB)`,
      Gridshell Analysis `Lateral` input appended (29), catalogued as a mode.
      Read: Timoshenko & Gere 1961 ch. 6 (§6.1 the pure-bending case M_cr =
      π√(EI_y C)/L from the equilibrium of the twisted section; §6.4 the
      cantilever: eq. 6-30 P_cr = 4.013 √(EI_y C)/l², eq. 6-33 (ql)_cr = 12.85
      √(EI_y C)/l² — both recomputed here by shooting on C φ″ + M(x)² φ/EI = 0,
      φ(0) = 0, φ′(L) = 0: 4.0125993 = twice the first zero of J_{−1/4},
      12.853763); Trahair 1993, Flexural-Torsional Buckling of Structures
      (ch. 17 energy method: ½∫[EI_y u″² + EI_w φ″² + GJ φ′² + N(u′² + r₀²φ′²)
      + 2 M_x u″ φ] dz — from memory of the edition, not re-read); the
      prebuckling-deflection factor 1/(1 − I_y/I_x) of Trahair & Woolcock and
      Vacharajittiphan, Woolcock & Trahair 1974, refined by a GJ/EI_x term in
      Pi & Trahair 1992 (Prebuckling deflections and lateral buckling I,
      J. Struct. Eng. 118(11) 2949; paywalled), read through the survey of
      Abu Reden & Ádány 2025 (Periodica Polytechnica CE 69(3) 723–737, which
      confirms the 1 − I_y/I_x factor for open sections of small torsional
      rigidity and lists the Chwalla, Davidson, Clark & Knoll, Trahair &
      Woolcock and Pi & Trahair forms); McGuire, Gallagher & Ziemian 2000 eq.
      9.21 (the full K_g with moment terms — cited from the edition, not
      transcribed: the entries here are derived from the energy); Gavin 2012
      (Duke CEE 421L, checked: axial terms only, T·J_x/(A L) torsion term);
      Yang & McGuire 1986, Stiffness matrix for geometric nonlinear analysis
      (J. Struct. Eng. 112(4): induced joint moments of semitangential end
      moments — paywalled, title and abstract only). Derivation: Vlasov
      kinematics, the section turns with the deformed axis and twists about
      it, axial displacement u − y v′ − z w′ + (z v′ − y w′) φ; Green strain
      to second order ε₂ = ½(v′² + w′²) + ½(y² + z²) φ′² + (z v″ − y w″) φ
      (the φ′ cross terms of V′², W′² cancel against the derivative of the
      axial term); with σ = N/A + M_y z/I_y − M_z y/I_z (M_y = ∫zσ, M_z =
      −∫yσ, right-handed) U_σ = ∫[½N(v′² + w′²) + ½N(I_p/A)φ′² + (M_y v″ +
      M_z w″)φ] dx — the factor 1 (not ½) on the coupling is what gives
      Timoshenko's π√(EIC)/L; the ½ of the rotation-vector parametrisation
      would give 2π. Cubic Hermite v, w, linear φ, M linear along the element
      → 16 coupling entries (sympy), e.g. (v₁,θx₁) −M_yA/L, (θx₁,θz₁)
      −(5M_yA + M_yB)/6, (θx₂,θz₂) (M_yA + 5M_yB)/6. Section moments from the
      element end forces: M_A = −f_A (the stress resultant on the +x face is
      the negative of the end force at A), M_B = +f_B. Through the rigid end
      offsets like K_e. Only the strong-axis moment is coupled (I_y > I_z →
      M_y; I_z > I_y → M_z): the classical theory would otherwise report a
      spurious factor for a flat lath bent flatwise and for round bars, which
      Trahair's factor (1 − I_minor/I_major) ≤ 0 rules out. Measured
      (`LateralTorsionalBucklingTests`, 6 tests): tip-loaded upright strip
      100 × 10, L = 3: 49.82 / 49.26 / 49.12 / 49.08 N vs 49.069 (4 / 8 / 16 /
      32 elements, errors 1.5e-2 / 3.9e-3 / 9.7e-4 / 2.4e-4, ratio 4.0 — the
      linear twist is O(h²)), mode sideways (z < 1e-6), no axial force (< 1e-9);
      uniform load 157.44 / 157.25 / 157.20 vs 157.186 N (1.6e-3 / 4.3e-4 /
      1.1e-4 with 8 / 16 / 32); uniform-moment span with u = u′ = φ = 0 at the
      ends (round Ø200 cross bars on pins through the strip's nodes, overhang
      loads P at a = 0.3; the coupled equations give 2 − 2cos kL − kL sin kL
      = 0, first root 2π): 234.31 / 231.46 / 230.74 vs 230.507 N·m (1.7e-2 /
      4.1e-3 / 1.0e-3 with 10 / 20 / 40), midpoint moves most, bar nodes still;
      ½qᵀK_g q = the quadrature of U_σ to 1e-12 on random q; rigid motions cost
      the moment terms nothing; flat strip and round bar: no factor; Euler
      column bit-identical with the terms on and off; bench saddle net (54
      laths, 3013 nodes, 80 × 12 upright, 1 kN/m): λ₁ 32.65 → 30.15 (−7.7 %),
      λ₂ 32.65 → 30.15, Sturm 0 missed, 64 iterations, 10 s; bench 233 PASS
      light and dark. Findings: (1) the
      classical formulas are the safe side by the prebuckling factor (about
      +1 % for 100 × 10, +2 % for 80 × 12), so the factor stays a lower bound
      of the exact value for strips, as long as the Euler part is not the
      governing one; (2) the Sturm count works unchanged with the indefinite
      −K_g of a pure-bending case (no compression anywhere); (3) a fork support
      (twist held, both rotations free) does not exist among the support kinds
      — the test builds it from pinned cross bars, and users wanting
      Timoshenko's simply supported case need the same trick or a `Fork`
      support kind. Open: the prebuckling-curvature correction (Pi & Trahair
      1992), whose element form needs the paper itself; the
      torque's second-order terms and the induced joint moments (Yang &
      McGuire 1986) for members at angles carrying large torques; warping
      stiffness for sections that have it; a `Fork` support kind; the web
      app's structure block still has no buckling.
- [ ] Form finding: Schek 1974 (Force density method); Barnes 1999
- [x] Form finding: Schek 1974 (Force density method); Barnes 1999
      (Form finding and analysis of tension structures by dynamic
      relaxation); Adriaenssens, Block, Veenendaal, Williams (eds.) 2014
      (Shell Structures for Architecture) — kinetic damping details,
      bending-active DR (Lienhard 2014, Bending-active structures;
      D'Amico et al. 2014 for elastic gridshell DR with bending stiffness).
- [x] Joint eccentricity in Gridshell Analysis — 2026-11-02 (session 1,
      José's priority 2 "next: … joint eccentricity"). `FrameAnalysis.Options.
      AxisOffsets` (signed height of each lath's axis along the surface normal),
      `Layer` / `Align` inputs on the component (layer k → axis at Lift +
      k · depth of the profile), `ReactionMoments` output, `Result.Eccentricity`,
      `TotalMoment`, `MomentEquilibriumError`. The frame's nodes stay on the
      surface (where joints, supports and point loads are given) and every beam
      end hangs on a rigid link r = h · n: u_axis = u_node + θ × r, θ shared,
      K_nodes = Hᵀ K H with H_end = [[I, −[r]×], [0, I]], lumped loads go to the
      node as the force plus r × f, recovery from H u — the rigid end offset of
      Oasys GSA ("Offsets" theory page: u_pseudo = T u_node, f_node = Tᵀ
      f_pseudo, same T), Midas Beam End Offsets and Karamba3D's beam
      eccentricity, and the "staggered levels" model D'Amico, Kermani & Zhang
      2014 (Eng. Struct. 81, §4.3.1) call faithful to the geometry (they keep a
      single node for stability of their nonlinear solve; the linear rigid-link
      form here has no small link element, so no conditioning cost). Layering
      after Schling et al. 2018 (AAG 2018, §3.2: the timber asymptotic
      gridshell's lamella layers "arranged on two levels"); the caveat of
      Erkmen & Saleh 2012 (rigid bars over-constrain the slip of *parallel*
      composite beams) does not apply to laths coupled at isolated crossings.
      Measured (`EccentricJointTests`, bench Gridshell Analysis card): L-frame,
      1-element cantilever A (4 m, flat 100 × 20) with a 2 m lath B on its tip
      50 mm above it, 300 N/m along B → torsion of A = e·q·b = 30 N·m to
      1.1e-10, reaction torque the same, tip deflection P L³/3EI to 1e-9, B's
      far node moved by v + q b²/2EA and −θz·b to 1e-9, twist 0.122 rad; the
      same frame under a vertical load: every element force and reaction equal
      to the e = 0 run to 1e-9 (statically determinate, offsets ∥ load);
      Hᵀ K H symmetric to 1e-12, rigid motions of the nodes cost 0 to 1e-10;
      hyperboloid ruling grid (16 + 16 rulings, 144 joints, rim fixed) with
      family − one depth (60 mm) above family +: force and moment equilibrium
      to 1e-12 for wind and gravity, rigid and scissor joints; wind 1 kN/m:
      displacement 32.0 → 29.4 mm, utilization 3.31 → 3.29; gravity: 2.99 →
      2.90 mm, 0.926 → 0.950. Bench saddle net (80 × 12 upright, 525 joints,
      e = 80 mm): wind utilization 0.86 → 0.98, displacement 2.12 → 2.04 mm;
      gravity 1.001 → 1.017. Without `Layer` every number is bit-identical to
      before (the stiffness scatter and load lumping are the same code path).
      Findings: (1) with r ∥ n the vertical load produces no eccentric moment
      (r × f = 0) and a determinate frame's forces do not change — the stack
      matters for horizontal loads and for indeterminate nets, where the layer
      stiffens and shifts the load paths (the saddle net's displacement drops
      5 % while its peak stress rises 14 %); (2) the Displacements output is
      the surface node's, not the axis's: the two differ by θ × r, which
      cancels back at the lath's own nodes, so deformed curves stay on the
      surface points; (3) the T-junction coupling counts rim ends sharing a
      point as joints (144 of 112 crossings on the hyperboloid grid).
      Open: per-family profiles (the 10-27 bundle) give each layer its own
      depth — then h = Σ depths of the layers below + ½ own depth; the Mite
      Net wire (1.4.0) can set `Layer` from the family; a support on an offset
      lath holds the surface point with a rigid bracket to the axis (a pinned
      support then carries the axial force with the arm h — document or add a
      "support on the axis" switch); the shear-block coupled double layer of
      Downland / Mannheim (two parallel laths per rib, D'Amico 2014 c_s) is not
      this model; buckling (10-26b) should take the offsets through the same
      H (K_g too).
      Done 2026-10-21 (session 2): `Dynamics/DynamicRelaxation` rewritten
      after Barnes (read: Barnes' 1977 thesis ch. 2 — central differences,
      Δt_crit = √(2M/S), slack links; Veenendaal & Block 2011 — DR with
      lumped stiffness, Q = T/L as the geometric stiffness; D'Amico, Kermani
      & Zhang 2014 — bending-active DR, residual tolerance; the Oasys GSA
      theory manual — fictitious mass from the summed nodal stiffness, peak
      by a parabola; Kuijvenhoven & Hoogenboom 2012; Barnes 1999 itself and
      Adriaenssens & Barnes 2001 were paywalled, their formulas re-derived
      from the recurrence and cross-checked against the secondary sources):
      fictitious masses M_i = Δt² S_i / 2 (S_i = Σ k + |T|/L + smoothing +
      film cotangent weights + 8 EI/ℓ³ per lath), half-step start, reset to
      the peak position x − 3Δt/2 v + Δt²/2 R/M (the trajectory at t − Δt/2),
      masses refreshed at every reset. Options keep the 1.3.2 integrator
      (`FictitiousMasses` / `ResetToPeak` off) for comparison, `PeakFit`
      (parabolic peak, GSA / Alamatian 2022) off by default. New
      `Dynamics/LathBending`: laths through quad meshes, κ = 1/R of the
      three-point circle (Adriaenssens & Barnes 2001), forces = exact −∇ of
      Σ ½EI κ² ℓ̄ (finite-difference check 2e-9), self-equilibrated per lath.
      Measured (steps 1.3.2 → Barnes): hanging net 20 × 20 630 → 186, elastic
      chain 1439 → 183, catenoid film 1411 → 336, elastica 34 043 → 5 860,
      cantilever 4743 → 2385; equilibria identical to 3e-6. Closed forms:
      elastic chain 6e-10 (shooting), RestScale 0 = FDM 2e-11, film = catenoid
      0.05 edge², elastica rise 0.204 / 0.049 / 0.011 % at 16 / 32 / 64
      segments (second order), cantilever tip 0.28 / 0.067 / 0.017 % against
      P(L − h/2)³/3EI, quad-mesh gridshell of 6 elasticas 0.049 % with the
      cross laths straight to 3e-9; FDM saddle 1e-13, chain parabola 4e-16.
      Findings: (1) at the Gerschgorin bound (λ = 1) the stiffest mode sits
      on the stability limit — a velocity reset alone then stalls (35 000
      steps on the net, no convergence on the cantilever) while the reset to
      the peak damps it and λ = 1 is the fastest choice (λ 1.25 / 2 / 4 cost
      5–100 % more); without the peak reset the masses are doubled.
      (2) The parabolic peak was faster on 2 of 5 cases and slower on 3, so
      Barnes' t − Δt/2 stays the default. (3) A node-based beam clamps at
      h/2 past the first fixed node (its lumped curvature covers [h/2, 3h/2]):
      an O(h) effect of the discretisation, not of the solver; the rest is
      O(h²). (4) Stiff axial springs with soft bending converge slowly
      (kinetic damping resets on the axial vibration): EA of 1e6 × n needs
      13 000 steps for 6 × 33 nodes — fine for a batch, not for a slider.
      Open: slack cables (T < 0 → EA = 0 as in Barnes 1977), torsion of the
      laths (D'Amico's 6-DOF element / du Peloux's 4-DOF element — Mite's
      Lath Analysis reads the twist after the fact), per-lath EI, laths given
      as curves instead of derived from the quads, a web-app form-finding
      block, viscous damping as an option, and the actuation side of E (the
      same bending element could drive Net Kinetics' elastic states).
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
      Done 2026-10-18 (session 1): (1) the kg noise was not the estimator but
      the *loop closure*: `FieldTracer.CloseSmoothly` spread the closure gap
      (~1 % of the loop, the tracer's drift) as one constant 3D vector and
      reprojected; half-way round a sphere that vector points into the
      surface and the reprojection lands on whichever facet beside the edge
      is nearer (0.04 · tan(dihedral/2) = 1.2e-3 on a 48-division unit
      sphere), so a traced great circle zigzagged ±1.2e-3 point by point:
      in-plane kinks of 9°, |kg| up to 0.45 (24 divisions) / 1.12 (48) on a
      geodesic, a flat lath at utilization 11 where the truth is 1. The gap
      is now spread in each point's tangent plane and the round-off stub
      before the closing point dropped: kinks ≤ 0.4°, |kg| ≤ 0.03,
      utilization 1.00, unfaired loops kink-free (every closed loop of
      Geodesic / Asymptotic / Conjugate Net, Curvature Streamlines and the
      web benefits). (2) The two-chord kg estimator was biased whenever the
      window (2 edges) was shorter than the polyline's spacing: 40 points on
      a 45° latitude circle of the 48-sphere read 1.36 for 1.00, a 30-point
      torus crown circle 0.58 for 0.33. kg is now the tangent-plane turning
      summed over the window (widened to whole vertices) over the arc between
      the end-chord mid-points, with the chord→arc factor (θ/2)/sin(θ/2):
      exact for circles at any sampling (5e-4 at 40 points, 1.1 % at 12
      points = five edges apart, where the inscribed geodesic polygon differs
      from the circle by the Gaussian curvature of the slivers). Traced
      geodesics now read |kg| 0.01–0.03 at the default window, 0.004–0.012 at
      four edges; hyperboloid rulings 0.010–0.016 as before. (3) Strain
      model checked and kept: Tang et al. 2016 §2 (straight development iff
      kg = 0 for tangent strips, kn = 0 for orthogonal ones; the Darboux
      rotation names the bending axes), Pottmann et al. 2010 §1 (panels bend
      about the weak axis only and twist), Schling et al. 2018 (kg and τg of
      upright lamellas applied as strain loads; 100 × 1.5 mm steel, 60 × 4 mm
      plywood, 80 × 8 mm bamboo, 8 × 80 mm GRP in the built structures),
      Timoshenko & Goodier §109 for the twist. Schling 2018 (Repetitive
      Structures) and Wan, D'Acunto & Schling 2024 were not accessible (TUM
      mediatum behind a bot wall, Elsevier), so the allowable timber strains
      stay an input (MaxStrain). `GeodesicCurvatureTests` (15 exact tests).
      Open: the remaining in-plane wobble of traced curves (0.01–0.03 kg on a
      unit sphere) comes from the tracer's own facet-scale lateral error, not
      from the estimator; tracing on a smooth limit surface (C) would remove
      it. The chord→arc correction is not applied to the kn / τg window.
- [x] Mesh repair and welding — 2026-10-09: orientation (outward / majority,
      manifold edges only), isolated vertices, slivers (needles collapsed
      under the Dey et al. 1999 link condition with a dummy border vertex,
      the end with the smaller Garland–Heckbert 1997 quadric goes; caps
      flipped by Lawson's max–min, border caps dropped — Botsch & Kobbelt
      2001, CGAL `remove_almost_degenerate_faces`, needle ratio 4) and a
      defect report after Attene, Campen & Kobbelt 2013 (non-manifold edges,
      non-orientable parts, border loops, parts). Read: Botsch & Kobbelt 2001
      (TU Dortmund PDF), the EG 2012 repair tutorial, CGAL 6.2 docs, Garland
      & Heckbert 1997; Attene 2010 itself and Botsch et al. 2010 ch. 8 were
      not accessible (restricted PDF / book), only the abstract and the
      tutorial's summary of MeshFix.
- [x] Mesh repair, next (b): cracks and T-junctions — 2026-11-01 (session 1,
      D/A). `MeshCleanup.Options.CrackTolerance` (Grasshopper `Cracks`, input
      and output appended): a border vertex within the distance of a border
      edge it does not belong to is joined to it — merged into the nearer end
      when that is within the distance, otherwise the edge's face is split at
      the vertex (triangle → 2, quad → 3 triangles, n-gon → (n+1)-gon) — the
      vertex–edge contraction of Borodin, Novotni & Klein 2002 in order of
      increasing distance (priority queue, stale pairs re-measured), as given
      in Botsch et al. 2007 course notes §4.4.4 / 2010 PMP §8.2 (the paper
      itself was not fetchable; Barequet & Sharir 1995 for the border and
      the ε as the a-priori gap size; Guéziec et al. 2001 for "no new complex
      edges"; Rhino's MatchMeshEdge as the independent implementation:
      vertices first, then split edges, nothing moved beyond the tolerance).
      Runs before the orientation pass so winding crosses the mended seam.
      Findings:
      - The vertex must NOT be moved onto the chord (Borodin's merge point is
        free). On the sphere from hemispheres with 24 / 48 meridians the 24
        T-vertices sit the sagitta 0.00856 outside the lower chords; snapped
        onto the chord the equator read K = −0.10 … 1.67, kept in place (the
        edge bends through it) K = 0.80 … 1.38, mean 1.09, every vertex still
        on the sphere; Gauss–Bonnet Σ deficit = 4π to 2e-13, χ = 2 either way.
      - Zipping a double wall (two sheets 0.01 apart, Cracks 0.02) at its rim
        tries to split a coarse corner face twice from the same fine
        triangle: the second split would be that triangle's duplicate and
        give its diagonal a third face. Every new edge of the split pieces
        is checked, the snap refused and counted (`CracksLeft`, 2 of 32).
      - On a hyperboloid from two bands rotated half a step (both seams
        T-vertices of each other) 130 asymptotic curves end on the seam; after
        the weld 54 rulings run rim to rim, bow 0.0045, 0.08° off the exact
        rulings, length 0.15 % off 2√2; K on the mended seam −0.84 (truth −1,
        the zigzag seam).
      - Cost: none measurable (whole cleanup 0.47 → 0.40 s on 50 k faces,
        4.7 → 4.5 s on 450 k; the border grid has a cell of max(edge, 2·tol)).
      - With `Cracks` 0 the report probes at a tenth of the average edge
        (0.05 missed the 24-meridian sagitta, which is 0.066 of the fine edge)
        and names the value to set, rounded up to two digits.
      Open: a vertex–vertex merge keeps the edge's end and moves the other
      vertex onto it (≤ tolerance) — a midpoint would halve the move but
      moves both; an automatic tolerance (the probe distance) once it has been
      tried on real Rhino exports; the web app's OBJ / STL drop could probe
      and offer the weld; the 1.4.0 mode catalogue needs no entry (a
      distance, not a mode).
- [ ] Mesh repair, next: (a) non-manifold edges and vertices are reported,
      not repaired — cut them by duplicating vertices (Guéziec, Taubin,
      Lazarus & Horn 2001, Cutting and stitching) and offer it as an option;
      (c) holes are counted (border loops), never filled: a hole
      filler (Liepa 2003) only on request, since gridshell surfaces have
      intended borders; (d) self-intersections are not detected; (e) slivers
      inside quads are left (only triangles are collapsed / flipped); (f) a
      bench card with a picture (the card has numbers only).
      2026-10-04: the plank's own shape is now the developable of Lath Strip
      (G); in the twist regime |τ| > |κ| its edge strain (W/2)²(τ − cκ)²/2 is
      the sheet-material counterpart of the rod twist strain above.
- [ ] Mesh repair and welding: Attene 2010 (A lightweight approach to
      repairing digitized polygon meshes); Botsch et al. 2010 (Polygon Mesh
      Processing, ch. 8) — for Mesh Cleanup.
      bench card with a picture (the card has numbers only); (g) cracks wider
      than the tolerance on one side only (a chain of fine vertices against a
      coarse edge that drifts away) — Barequet & Kumar's edge-pair score
      would pair the chains instead of single vertices.

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
- [x] Strain per state and the envelope over the motion — 2026-10-02 (UX
      session, José's priority 3). Core: `KineticStrain.Envelope` → per lath
      the peak utilization over all states, the state and the governing
      strain (`StrainSource`: bending from kn, from kg, or twist;
      `LathResult.Governing` / `GoverningNode`), laths within the limit in
      every state. Literature: Schikore et al. 2020 evaluate stresses at one
      (open) state only and read the energy along the motion as line plots;
      Wan, Man, Crolla & Schling 2025 (Design and construction of an
      asymptotic kinetic transformable canopy, Automation in Construction
      180, 106508) check bending and shear stress at each of six stages
      (GFRP 75 × 12 mm, max 168 MPa / 18 MPa) and report bending energy
      falling while twist rises — so the per-state check and the
      bending/twist split are what the plot and colours now show. Web app:
      lath colour = strain now / envelope / family, legend per state, energy
      diagram (Π stacked bending + twist on one scale, natural state dot)
      under the utilization on an absolute axis with the limit at 1, hover
      with strain now and envelope, a "through the motion" stats line;
      `tools/web-smoke.js` §9. Grasshopper: Net Kinetics' Report names the
      envelope (no new outputs yet, see below). Bench: "catenoid, strain per
      state" view (upright 80 × 10 mm: 41 → 41 → 41 → 41 → 38 → 0 laths over
      the limit as the ring closes) and an exact self-test (hyperboloid rods
      all peak in the first state, at atan(s₁/b)/s₁·(d/2)/√3 to 1e-6, by
      twist). Exact core tests: a closing-and-opening hyperboloid motion puts
      every rod's envelope in the middle state at the closed form (1e-7); a
      circular lath names kn or kg by the section orientation.
      Findings: (1) the viewport colour-bar legends of the app had collapsed
      to 9 px swatches since 1.2.7 (a later `.legend i` rule for the marker
      legend overrode them) — fixed, smoke-tested at 160 px. (2) The
      utilization colours are green → neon yellow (L* 76 → 94) below the
      limit and red (L* 55 → 16) above: the limit is a 40-unit lightness
      drop, so it stays readable with red–green colour-vision deficiency
      (Crameri, Shephard & Heron 2020 warn against red–green pairs of
      *similar* lightness); green at low utilization vs red over the limit
      differ by 22 L*. (3) Lienhard, Alpermann, Gengnagel & Knippers 2013
      (Active bending, a review) ask σ/E > 2.5 ‰ for bending-active
      materials and > 10 ‰ for kinetic ones (fatigue): a repeatedly moved
      timber lath at MaxStrain 0.5 % is optimistic; worth a note on the
      MaxStrain input or a "cycles" factor.
- [ ] Net Kinetics: append `Envelope` / `EnvelopeFold` outputs once the
      unmerged Mite Net bundle (2026-09-27, which appends `Net` to this
      component) is in, so the two do not claim the same output slot.
- [x] Speed: a block-aware ordering and the symbolic analysis reused across
      iterations — 2026-11-07 (session 2, E / José's priority 3 "next:
      speed"). `Numerics/BlockLdl3` (new API, nothing existing changed):
      `BlockSymmetricMatrix3` (upper block triangle, 3 × 3 blocks),
      `BlockLdl3` (symbolic once: minimum-degree ordering of the block graph
      — Tinney & Walker 1967, George & Liu 1989 — up to 8 192 blocks, above
      it nested dissection with minimum-degree leaves, George & Liu 1981
      ch. 8; block elimination tree and column counts as Davis 2005's
      `ldl_symbolic`; numeric: the up-looking algorithm with every scalar a
      3 × 3 block, Y_i = A_ki − Σ Y_j L_ijᵀ along the etree path, L_ki =
      Y_i D_i⁻¹ through the block's own LDLᵀ — an explicit cofactor inverse
      lost four digits on ill-conditioned blocks — D_k −= L_ki Y_iᵀ; the
      damping as a diagonal shift at factor time) and `BlockNormalEquations3`
      (JᵀJ and Jᵀr from the Jacobian rows through a cached map from the row
      pattern to the block positions; a row whose columns change — a
      one-sided contact — remaps within the pattern, a new block pair
      re-analyses). `ScissorNet.Solve` uses it for every state; `Result`
      reports `Unknowns`, `Factorizations`, `PatternAnalyses`,
      `PatternRemaps`, `FactorBlocks`, `SolverMs` and Net Kinetics prints
      them. Measured (this sandbox, Release): the 1.6.0 envelope solver spent
      14.4 of the deployment's 17.8 s in 439 factorizations of 2 706 unknowns
      (32 ms each: 12 ms ordering, 2 ms symbolic, 17 ms numeric, fill 375 k
      entries); the scalar nested-dissection `SparseLdl` had the same cost
      (fill 243 k, the same 32 ms); the block version on the same ordering
      5.0 s, on minimum degree 2.4 s (fill 10 695 blocks = 96 k entries, 4 ms
      per factorization). Hyperboloid grids 112 / 416 / 912 / 1 600 nodes:
      0.70 → 0.31 s, 7.7 → 1.7 s, 34.0 → 5.0 s, 164 → 19 s, states identical
      to 1e-12 (an exact mechanism); the flat 3-fold Enneper kit (451 nodes)
      pressed 3.8 → 0.9 s, deployed 14.0 → 2.4 s with states within 4e-6
      (2.6e-5 of the spacing) of the 1.6.0 ones — a mechanism's soft
      directions are decided by round-off, both solvers reach the same
      residual; the pressed state agrees to 7e-11. Findings: (1) nested
      dissection on BFS level structures is a poor ordering for these block
      graphs — on the 912-node hyperboloid its fill is 2.2× minimum degree's
      (74 550 vs 33 382 blocks) and a leaf of 8 instead of 64 only takes a
      quarter off; on a synthetic denser graph the two are level, so the
      hybrid (dissection, minimum-degree leaves of 1 024) is kept for the
      large sizes where the explicit elimination graph gets slow (2.7 s at
      7 104 blocks, 31 s at 28 032); (2) the remaining time of the deployment
      is now evaluation (0.8 s: the central differences of the rest-bend and
      twist rows), assembly (0.5 s) and the factorizations (1.7 s);
      (3) 439 factorizations for 198 iterations: the ×10 / ÷10 damping
      rejects more than half of the trial steps on the kick states. Open:
      Nielsen's gain-ratio damping update (Madsen, Nielsen & Tingleff 2004)
      to cut the rejected trials; a flat (CSR) Jacobian instead of the per-row
      lists; analytic Jacobians for the rest-bend and twist rows; the same
      block solver and ordering for `SoftestModes` / `MechanismModes`
      (envelope, once per call, 0.2 s) and for `AagWeb` / `AagRefinement`
      (scalar `SparseLdl`, whose nested dissection should at least take
      minimum-degree leaves — not measured here); supernodes (Ng & Peyton
      1993) if the 3 × 3 blocks are not enough for the browser.
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
- [x] Deployment from the flat state — 2026-10-23 (session 2),
      `ScissorNet.DeployFromFlat` / `OnTable` / `Lift` / `AtState` /
      `SoftestModes` / `Kick` / `Stiffness`, `Options.Ground` (one-sided
      table, Bouaziz et al. 2014 §4.1 half-space projection; penetration as
      stiff as a hinge, `GroundWeight` as a soft weight), `Options.Scissors`
      (crossing-angle drivers), Net Kinetics `Deploy` / `Angle` / `Kick`.
      Literature: Schling & Schikore 2022 (DMS 2022, Morphology of Kinetic
      Asymptotic Grids: "networks with one, two and four singularities cause
      increasing rigidity and limit the kinetic transformability"; stiffness
      does not change the transformation path when the grid is highly
      constrained), Schikore 2023 (TUM dissertation, the kinetic umbrella
      opening from its bundle), Pellegrino 1993 / Kumar & Pellegrino 2000
      (mechanisms as the null space of the compatibility matrix, kinematic
      bifurcations), the Caltech 2024 kirigami kinematics paper (SVD
      predictor–corrector, rank drop at the flat configuration, a 1e-3 rad
      perturbation to pick the branch), Tachi 2009 (rigid-origami simulation:
      Euler steps projected onto the null space, rank condition). Findings:
      - The flat state is a pitchfork bifurcation and Gauss–Newton cannot
        pass it: at an exactly flat grid every out-of-plane first derivative
        of an in-plane residual vanishes by symmetry, the coupling lives in
        Σ rᵢ ∇²rᵢ (dropped by GN / LM / trust-region reflective), so a solver
        held flat stays flat for any drive (test: scissors driven to 90°, the
        net stays at height 0 and the scissors 41° short); random kicks of
        1e-3 … 5e-2 × spacing do not help, the hinge constraints pull them
        back. The deployment branch is the softest non-rigid mode of JᵀJ
        (inverse iteration, rigid motions that keep the pinned nodes still
        deflated): on the 3-fold Enneper hexagon stiffness 6e-5, 100 % out of
        plane, |correlation| 0.999 with the design heights, the next mode
        (in-plane lazy tongs) 140× stiffer; on the hyperboloid grid the exact
        mechanism to 1e-13 (correlation 1.0000 with d/dβ), next mode 0.22.
        A kick of 0.1 … 2 × spacing along it is enough; the sign is the branch.
      - The drive that deploys a minimal-surface web is "every scissor to
        90°" (Schling: asymptotic curves on minimal surfaces cross at right
        angles) with the lath ends on a one-sided table: pairwise distances
        within 0.07 × spacing of the design (rms, 0.33 max; nodes 0.09 / 0.2
        after rigid alignment), rise 0.895 vs 0.905, drift 6e-4, 0.05° off
        asymptotic, 6 ends on the table, scissors 5.8° short of 90° where the
        design itself crosses at 60° (the rays). The reverse press (every end
        lifted to its design height) returns to the design within 0.08 ×
        spacing too; the in-plane lazy-tongs modes are what the height drive
        does not control.
      - Pulling the singular node (José's Kangaroo sequence) does not deploy
        the flat grid, kicked or not: the deployment mode moves that node by
        nothing relative to the lath ends (a saddle's centre is at the mean
        height of its rim). With three lobes held it rises 0.13 of 0.45 and the
        asymptotic condition gives (1.3°, a strained bump, further from the
        design than the flat state); with a strong pull (target weight 100) a
        dome-like bump with 2–6 % joint drift and 5° deviation; with nothing
        held it lifts off the table as a rigid body; with a soft weight on the
        ends it stays flat. A physical kit lifted at its centre must be
        bending its lamellas about the strong axis.
      - The kinetic umbrella opens from its fully collapsed bundle (β = 0, A
        rod k on top of B rod k, both joints of a pair on one point) by lifting
        the top ring's planes: the closed form at every state to 3e-12 through
        the bifurcation, 17 iterations for the first step and 5 after — the
        rising planes drive the mode directly, no kick needed.
      - The 3-fold Enneper web is a mechanism only to 1e-3 (the press's joint
        drift); its deployment therefore "costs" that much and the LM weights
        decide whether a weak drive is followed — one singularity, as Schling
        & Schikore say, limits the transformability.
      Open: the deployment in the web app (press + deploy ≈ 15 s natively,
      far too slow in wasm without the speed item); a second-order
      predictor (Kumar & Pellegrino's higher-order compatibility) instead of
      the kick, which would also give the branch tangent at any bifurcation
      along a motion; the scissor drive on a chosen ring of joints (a hub
      motor) and the actuation force along the drive (gradient of Π); gravity
      as a linear potential instead of the quadratic `GroundWeight`.
- [ ] Then the design side of Wan et al.: given a target family of surfaces
      (open/closed canopy), find the net whose motion passes through both —
      alternate the kinetic solve with the layout solve.

## G. Asymptotic webs and AAG / AGG gridshells (José's first priority, 2026-09-27)

Literature
- Schling, Wang, Hoyer & Pottmann 2022, Designing asymptotic geodesic hybrid
  gridshells (Computer-Aided Design 152, 103378): discrete A-nets (planar
  vertex stars), G-nets (equal opposite angles), AAG / AGG / AGAG webs as
  quadratic constraint systems with auxiliary normals and binormals, solved
  by guided projection; initialisation from rotational surfaces (AAG) or
  developables (AGG); fairness 5e-4 → 0, proximity 0.1 → 0; singular
  vertices of valence N ≥ 6 at flat points with diagonals breaking at the
  rays; strips from C³ quintic splines and rectifying developables.
- Wang, Almaskin & Pottmann 2025, Computational design of asymptotic geodesic
  hybrid gridshells via propagation algorithms (Computer-Aided Design 178,
  103800; this list used to attribute it to "Wang, Schling, Pottmann et al.
  2024" — Bolun Wang, not Hui Wang, and Schling is not an author): webs grown
  from a boundary strip of two neighbouring polylines, row by row, then a
  global optimisation (eq. 11, λ_fair 1e-4, λ_appro 1); GGG (§3.1, Sauer's
  equal angles, rays from bisector normals), AGG (§3.2, block-coordinate
  descent on two ray parameters, a guide curve for the free direction), AAG
  (§3.3: the new vertex on the ruling L_i of the rectifying strip, foot points
  on its medial line, descent on the scalar r). Code:
  github.com/wangbolun300/WebsViaPropagation.
- Wang, Schling, Pottmann et al. 2024, Computational design of asymptotic
  geodesic hybrid gridshells via propagation algorithms (CAD 2024): webs grown
  from a boundary strip of two neighbouring polylines, row by row, with a
  global optimisation that may adjust the strip; GGG / AGG / AAG.
- Schling, Wan, Wang & D'Acunto 2023, Asymptotic Geodesic Hybrid Timber
  Gridshell (AAG 2023) and Wan, D'Acunto & Schling 2024, Structural behaviour
  of asymptotic geodesic hybrid timber gridshells (Engineering Structures
  311, 118094): the built AGH web — flat geodesic planks interlaced at the
  midpoints of the upright asymptotic beams (tri-hex), constant 60°
  intersection angles, geodesic boundaries, concentric A–A nodes; 60 m²,
  12 × 100 mm ash planks, live-load capacity 7.0 kN/m² in FE. Wang &
  Pottmann 2022, Characteristic parameterizations of surfaces with a constant
  ratio of principal curvatures (CAGD 93): asymptotic curves cross at 2γ with
  tan²γ = −k₁/k₂, so a constant node angle makes the surface CRPC.
- Schling, Hitrec & Barthel 2017 and Schling 2018 (Repetitive Structures):
  asymptotic networks drawn by alternating curves through each other's
  intersections; on minimal surfaces the families cross at 90° (identical
  nodes), τg = ½(k2 − k1) sin 2α; cookie-cut the boundary from a larger
  surface, balanced K for a homogeneous net.

Done 2026-09-27 (1.3.0)
- [x] `AsymptoticWeb`: seed curves with nodes every spacing, all other nodes as
      crossings; flat-point seeds as singular nodes (rays where the field is
      radial on a circle around the seed); rotation about the seed normal
      detected and one sector rotated; surfaces of revolution as rotational
      webs (N copies, meridian diagonals geodesic = rotational AAG); tails
      shorter than 0.35 spacing cut at their last node; quads, node
      neighbours, diagonal chains (topological: opposite quads; broken at
      rays), diagonal geodesic error per family. Closed-form tests on the
      catenoid, the Enneper surface, the hyperbolic paraboloid, the 3-fold
      Enneper (`AsymptoticWebTests`).
- [x] `AagWeb`: A-net stars, unit normals, geodesic diagonals (the binormal
      eliminated: n·((va − v) × (vc − v)) normalised by the edge lengths),
      proximity through tangent planes, fairness, border anchors;
      Levenberg–Marquardt on the envelope solver. 3-fold Enneper 56.7° →
      0.08°; classical Enneper does not converge (its curvature-line
      diagonals cannot all become geodesic near the surface: stays ~60° at the
      mirror planes) — reported, not hidden.
- [x] Web app and Grasshopper `Asymptotic Web` component.

Open
- [x] AGG webs — 2026-10-24 (session 2): `Gridshells/AggWeb` and the
      `AGG Web` component (31 components). A G-net (Sauer's equal opposite
      angles, eq. 9 of Schling et al. 2022, the laths' bisectors g₁ + g₃ and
      g₂ + g₄ as the normal, eq. 10) with one family of asymptotic diagonals
      (both diagonal edges at a node in that tangent plane, eq. 12), solved by
      Levenberg–Marquardt on the node positions alone (the paper's Table 1)
      with proximity, fairness (off for the final third), a reference-guided
      start and a tangential hold of the border. `AggWeb.QuadNet` builds the
      net from two crossing families (Net Topology), an Asymptotic Web, a
      lattice function or as the exact cylinder web of Fig. 9 (three parallel
      pencils in the development, Graf & Sauer 1924; exact to 1e-13° because
      the half-turn about the cylinder normal maps each equally spaced helix
      onto itself). Measured: scattered interior nodes (0.1 spacing) return to
      2e-6°; cylinder web onto a cone r = 1 + z/4: within 2e-4 of the cone,
      0.04° / 0.02°, diagonals 2.8° (mean) off the generators; z = xy lattice
      8.9° → 0.26°, surface moved 0.5 % of the spacing. Findings:
      - The discrete asymptotic condition is blind near a parabolic direction:
        a chord ψ off the generator of a developable leaves the tangent plane
        by ½ κ₁ h sin²ψ (0.03° at 4° here), so on K = 0 surfaces the
        diagonals follow the rulings only to a few degrees; on K < 0 the two
        asymptotic directions are simple roots and the condition is first
        order. The test checks the angle out of the plane, not the direction.
      - Sauer's angle condition has sawtooth solutions: a lath zigzagging in
        the surface has equal opposite angles too, with its principal normal
        in the surface. Started far from a solution (a lattice with 9° of
        error, a web 2 spacings off its surface) Gauss–Newton finds them;
        the reference-guided rows (bisectors along the mesh normal, diagonals
        in its plane) and a step cap of ¼ spacing keep it on the smooth branch.
        The auxiliary-normal form (eq. 11, n·b = 0, as Wang, Almaskin &
        Pottmann 2024 prefer) was tried and dropped: with the normal defined
        by the diagonals it only asks the normal to lie in each lath's
        osculating plane, which the exact cylinder web satisfies but so does a
        larger set of nets (opposite angles read 0.002° while its residual was
        1e-14).
      - Counting: eq. 9 + eq. 12 are four conditions per node on three
        coordinates (both diagonal edges at a node); in the smooth limit the
        two edge conditions are one. A one-row-per-edge form (3 per node) was
        tried and is no better in practice; the paper's form is kept and the
        least-squares residual reported.
      - Two-colouring the diagonal chains by shared nodes alone leaves the two
        parity classes of a lattice unrelated; the two diagonals of every quad
        must be coloured differently as well.
      - The automatic family choice reads the reference normals at the start
        and takes the wrong diagonal when the web starts far from the surface
        (the cylinder web on the cone picks the circles): the component says so
        when the start is more than a spacing off.
      Open: (a) AGG from the development directly for a developable target
      (three pencils with the apex, geometric spacing — the exact cone web)
      instead of carrying the cylinder web over; (b) continuation of the
      reference (cylinder → target in steps) for large deformations; (c) the
      web app's Web block (the 1.4.0 `Api.cs`); (d) Lath Strip / Flat Kit for
      the flat G laths and the upright A diagonals of an AGG web; (e) AGAG
      (both diagonal families) by the same code with a second asymptotic
      family — the paper found numerically exact ones from AGG starts.
- [x] Propagation from a boundary strip (Wang et al. 2024) as a second way to
      start a web: two neighbouring polylines drawn by the user. (Done 2026-10-30: Web Propagation, Wang, Almaskin & Pottmann 2025.)
- [x] Refinement: subdivide the AAG web and re-optimise so the laths between
      nodes are smooth — 2026-10-03, `Gridshells/AagRefinement`, Asymptotic Web
      `Refine` (input 8) and `Surface` (output 6). Schling et al. 2022 §3.4:
      bilinear subdivision, then the refined original polylines keep planar
      stars (cross direction from the fine mesh line, (v⁻ − v⁺)·n = 0) or
      geodesic osculating planes; joints held (weight 3), fine vertices on the
      reference shifted by the joints' interpolated offsets. Exact checks with
      joints on closed-form curves: catenoid laths 1.0e-4 off φ ± z = const
      (chords 2.7e-3), meridians 4e-5 off the catenary (chords 7.2e-3);
      hyperboloid rulings straight to 5e-6, meridians 3.5e-5 off the hyperbola
      (chords 2.9e-3); rotational AAG web: turning per step 24.4° → 12.7° (×2)
      → 6.8° (×4). Findings:
      - Second-difference fairness pulls each lath toward its chords: 4.8e-4
        off the exact curve (1.4e-3, half the chord sagitta, with the final
        fairness at 10 %); third differences along the laths (zero on
        circles) bring it to 1.0e-4. Too little fairness lets the fine laths
        zigzag inside the tangent plane (38° steps at fairness 0.01 with
        second differences): the A-net and geodesic conditions do not see
        in-plane kinks.
      - The paper's schedule matters: fair first, then 1 % of it; a phase that
        stalls must hand over to the next instead of ending the solve.
      - ×4 is not more accurate than ×2 against the exact curves (1.25e-4 vs
        1.0e-4): the joints are held and the fine net between them is a
        compromise at that level; ×4 only makes the laths smoother (turning
        per step halves again). ×2 is the core default; the component's
        Refine defaults to 0 (off).
- [ ] Strip boundaries from the rectifying developables (§3.4 "Extraction of
      strips"): for each refined lath with normals n, across vector q (q = n
      for an upright asymptotic strip, q = n × t for a flat geodesic strip),
      ruling r = τ t + κ⊥ q with τ the rotation rate of q about t and
      κ⊥ = −t′·(t × q); the other boundary c + w/(r̂·q) r̂; the in-plane
      curvature t′·q must vanish (that is the asymptotic / geodesic property).
      Report the ruling angle atan(τ/κ⊥) per vertex: on near-straight laths
      (hyperboloid rulings) the rectifying developable degenerates and the
      real strip twists elastically instead.
- [ ] AAG on singular webs: laths still turn by 21° where they cross the rays
      of the 3-fold Enneper web (the web's own chains 17.6°, the 1.3.0 default
      fairness 44.5°). Look at the diagonal break at the rays (Schling et al.
      Fig. 14 drop the fairness across the singular polylines, not the lath
      fairness) and at the short edges next to the rays.
- [ ] Refine in the web app (`Mite.Web/Api.cs`, AAG block).
- [ ] AGG webs: a G-net (equal opposite angles) with asymptotic diagonals,
      initialised from a developable (cylinder web of Graf & Sauer) — the
      family with more freedom (Schling et al. 2022 §2.3).
- [x] Propagation from a boundary strip (Wang, Almaskin & Pottmann 2025) as a
      second way to start a web — 2026-10-30 (session 1, G/D):
      `Gridshells/WebPropagation` and the **Web Propagation** component
      (31 components). The strip is two neighbouring geodesic rows with the
      same node count, row k's vertex j joined by U to row k+1's vertex j and
      by W to row k+1's vertex j−1 (the paper's u_{i,1}, w_{i−1,1}); each new
      row adds n U and n−1 W laths and the A × B quads have the rows as
      diagonals. Construction, exact where the paper's is approximate: the
      planar star at vertex j of the last row puts the new vertex j on
      L_j = P_j ∩ P_{j+1} (the ruling of the discrete rectifying developable;
      foot = the point of L_j nearest the row-edge midpoint instead of the
      paper's medial line, which keeps both planes exact — the reference code
      keeps only the right-hand plane), so the stars are planar to round-off
      by construction; the geodesic condition of the new row, det(n, e_in,
      e_out) / (|e_in||e_out|) = 0 with n = u × w (eq. 22), is solved on the n−1
      ruling parameters plus the last vertex (no incoming W edge: free in the
      plane of its three edges) by Levenberg–Marquardt with soft targets, then
      a damped least-norm Gauss–Newton projection, so both conditions hold to
      ~1e-13° (the paper reports E_aag 0.053 after 16 sweeps on a row). The
      n−2 conditions leave 4 free directions per row: Offset 0 repeats the
      strip's triangles (U and W lengths kept — constant node spacing along
      the asymptotic laths), Offset 1 the equilateral apex of eq. 21; a weak
      row fairness (eq. 8); `Options.Targets` pins them to given positions.
      `AagWeb.Optimize(web, options)` is the reference-free overload for the
      paper's global step. Exact test typologies (`WebPropagationTests`,
      11 tests): the rotational A-net condition reduces to "(ρ_t, z_t)
      collinear with (ρ_{t±1} cos δ, z_{t±1})" (the two mirror chords of a star
      are parallel), so uniform rings give ρ_t = R cosh(a t), cosh a = 1 / cos δ
      — a discrete catenoid, an exact discrete AAG web (stars 3e-14°,
      meridians 2e-14°, and it lies on ρ = R cosh(z/R) for h = aR); the
      hyperboloid's ruling net ρ_k = R / cos kδ, z_k = R cot β tan kδ is the
      second (both families straight). Measured: rulings through the exact
      next vertices to 1e-11; with the exact rows as targets both webs are
      reproduced from their first two rows to 1e-14 over 8–10 rows, the
      propagated laths straight to 4e-14; from the catenoid strip alone the
      web is a different, equally exact AAG web (1.4e-13° / 1.2e-13°, 12 rows
      in 52 ms) where the naive offset is 8.6° / 9.0° off; the global AAG
      step then moves nodes by 2e-15; a planar equilateral strip continues as
      the exact lattice (1e-9); a 20 % z-stretch of the targets is followed
      only in the free directions (the row stays an AAG web to 1e-13°).
      Findings: (1) the rotation by one meridian step is not a symmetry of
      the staggered web (rows alternate ring parity), so no local rule —
      repeat the triangle, equilateral — continues an exact rotational web
      from two rows; it is reproduced only with the exact rows as targets,
      which is what the test uses. (2) Soft targets alone leave the geodesic
      residual at the trade-off (0.24° with weights 1 / 0.05); the second,
      targets-off projection stage is what reaches 1e-13. (3) The last vertex
      of a row has no incoming W edge, so P_{n−1} is undefined from the
      strip; the reference code copies the neighbouring crease and
      extrapolates the foot (third differences) — here the vertex is free in
      the plane of its three edges and the star at the last node is measured
      with the plane that the new row defines. (4) With Offset 0 the lath
      spacing still drifts (52 % over 12 catenoid rows) because the geodesic
      condition decides the shape; the exact catenoid web stretches its own
      U edges 2.6× over the same rings. Open: AGG (§3.2) and GGG (§3.1)
      propagation; the guide curve of §3.2 as a `Guide` input to steer the
      free directions (the core `Targets` option is ready); propagation on a
      given mesh (proximity through the tangent planes, Asymptotic Web's
      `Proximity`) so the drawn strip can come from a surface; a Mite Net
      output; the web app; the paper's "approximate the first strip" (make
      a drawn first row geodesic before propagating).
- [x] Tri-hex (midpoint) AAG web and constant crossing angle — 2026-10-31
      (session 1): `AsymptoticWeb.Result.MidChains` (the geodesic candidates
      through the lath midpoints: a topological walk that cuts one corner off
      every quad it crosses — two segments per quad per family, numbered like
      the node diagonals), `AagWeb.Options.Midpoint` (the web refined by one
      vertex per lath edge; planar stars at the nodes towards the midpoints,
      asymptotic half-edges n_m · (p − m) = n_m · (q − m) = 0 at the midpoint
      vertices, the geodesic condition along the mid chains — Schling et al.
      2022 eqs. 6 / 13 at every vertex with its own normal), `CrossingAngle`
      (t_A · t_B = cos θ at every node, sign-aligned per iteration),
      `Geodesics` and `Angle` inputs, `Angles` and `Midpoints` outputs on
      Asymptotic Web; `AnalyticShapes.Build("crpc", a, h)` — the rotational
      surface with k_m / k_p = −a (r″ = a(1 + r′²)/r; a = 1/3 in closed form
      r = t³, z = 3/2(t√(t² − 1) + arcosh t)), whose asymptotic curves cross
      at 2 arctan √a (Wang & Pottmann 2022, CRPC surfaces). Measured: catenoid
      tri-hex 39.8° → 0.0013°, stars 0.006°, asymptotic at the midpoints
      0.005°, nodes 9e-4 off the surface, crossings shifted 2.5 % (6.6 % max)
      of the spacing, G chains in the quarter-step meridian planes to 3e-3;
      3-fold Enneper 76.5° → 0.025° (stars 0.12°); classical Enneper, where
      the node web fails (63.6° → 10.7°), 75.4° → 0.33°; CRPC web 60.5–60.8°
      as traced, 60.00–60.12° after AAG with Angle 60 (geodesic 0.03°).
      Findings:
      - The chord midpoints are not a discrete geodesic: on quads that are
        not parallelograms they alternate sides of the smooth mid curve by
        O(spacing²), and the polyline through them turns sideways at every
        vertex (39° osculating-plane tilt on the catenoid, where the smooth
        answer is a plane through the axis). Any fixed "midpoint" rule (chord,
        arc length) zigzags; the midpoint vertices have to be unknowns, which
        is the refinement item below for the G family.
      - The two node-diagonal families were labelled per sub-lattice by
        straightness (the diagonals through the even and the odd nodes never
        share a quad-adjacency), so on the hyperbolic paraboloid, where both
        directions are plane sections of equal curvature, `G` mixed x = const
        and y = const chains. Fixed by tying the sub-lattices with parallelism
        across every lath edge (union–find with parity; rays of a singular web
        skipped because the families swap there).
      - With the midpoint vertices as extra freedom the classical Enneper
        surface, which carries no node-to-node AAG web near it (1.3.0
        finding), carries a tri-hex one with nodes 1e-3 off the surface — but
        the nodes slide along the surface by up to 0.11 (44 % of the spacing)
        and the crossing angles open from 83–90° to 66–90°: the proximity term
        is a tangent-plane distance and does not hold the web's parameter
        lines. A node-anchoring or "stay on the traced curve" option would.
      - A constant angle other than 90° needs a non-minimal surface
        (k₁/k₂ = −tan²γ): the catenoid web asked for 60° reaches 59–62° at
        every node but the AAG conditions blow up (44° / 12°) and the nodes
        move 0.62 (2 spacings) — correctly reported, not hidden. Designing an
        AGH-like 60° web therefore starts from a CRPC surface (the new shape,
        or Wang & Pottmann's construction), not from a minimal surface.
      Open: the hyperbolic paraboloid web does not converge in either mode
      (node 53° → 13–21°, tri-hex diverges to 70°) — both plane-section
      diagonal families are far from geodesic and the LM steps lower the cost
      while the worst triple grows; a continuation in the proximity weight or
      the paper's vertex-control term would be the next thing to try. The
      midpoint vertices are not yet carried into the lath tools as crossings
      (Net Joints / Lath Segment see only A × B; the `Midpoints` output is
      there for a Mite Net). The "equal intersection angles" of Wan et al.
      also cover the G–A angle (120° / 60° in a regular kagome): not imposed.
- [ ] Refinement: subdivide the AAG web and re-optimise so the laths between
      nodes are smooth (now straight node-to-node chords after AAG; the
      tri-hex mode already refines every lath by its midpoint vertex, with
      the asymptotic condition there); then the strip boundaries from the
      rectifying developables (quintic splines).
- [ ] Choose the seed spacing per ray (e.g. uniform in the asymptotic
      parameter instead of arc length) for more homogeneous cells.
- [x] Cookie-cut: trace on a larger surface, trim to a boundary — 2026-10-21
      (session 1, B/G), `Gridshells/NetTrim` and the Net Trim component.
      Literature: Schling, Kilian, Wang, Schikore & Pottmann 2018 (AAG 2018)
      §2.2 "the reference surface should be larger than the actual structure",
      §2.3 "strip quality deteriorates when approaching the boundary", §3.1
      the outline from the site; Schling, Wang, Hoyer & Pottmann 2022 (CAD
      152) §3.3 "select just a part of the initial AAG-web", §4.2 "cropped by
      a 4x4 m square boundary", §4.3 "cropped on one side to fit the original
      facade"; Haines 1994 (Graphics Gems IV) for the point-in-polygon side /
      crossing tests and the half-open vertex rule; Möller–Trumbore 1997 for
      the ray–triangle projection. Method: boundary pulled sample by sample
      until consecutive samples sit in edge-adjacent faces (closest point or
      a ray along Direction), those faces are a wall, the other vertices are
      flooded into regions, same-side components merged and opposite sides
      joined per loop, BFS depth from the mesh border, odd depth = inside
      (holes come free), Inside points / Invert override; wall-band points by
      the side of the nearest boundary segment; cuts by 2D segment
      intersection in the local tangent plane. Measured on z = x² − y² (edge
      0.066): line ends and lengths within 5.5e-4 of the closed form (the
      facet sagitta), annulus ends within 1.1e-4, hyperboloid band 2h√2 within
      4e-5, a non-convex loop's pieces equal to exact root finding, a traced
      classic net keeps 2695 / 2695 inside vertices and 0 outside, 0 / 3985
      random points misclassified; 105 ms for 30 lines on 18 k faces.
      Findings: (1) the flooded components are not the regions — the wall
      band isolates vertices whose whole one-ring is on wall faces (17
      components for 3 regions on the annulus), and a region graph through
      one-rings gave them the wrong depth; merging components by the side
      they lie on at each boundary sample fixed it; (2) a loop with no
      evidence of an inside (an arc that separates nothing) must classify
      its wall band as outside, or the band's left side is kept.
      Open: the web app has no curve drawing, so no trim there yet (a
      plan-circle "cookie" slider around the seed would do); the pulled
      boundary lies on the facets (smooth-foot slide as the guide curve
      does); ending the kept pieces on a lath along the cut (an edge lath
      from the pulled boundary, Schling's "edge lamellas"); trimming the
      Asymptotic Web's quads and node lists, not only its lath polylines;
      a Trim input on the net components themselves once the Mite Net type
      carries the families.
- [ ] Speed: the AAG solve is ~2 s natively / ~12 s in the browser for 385
      nodes (envelope LDLᵀ of 6N unknowns): nested dissection or a
      supernodal factorisation. Half done 2026-10-03: `Numerics/SparseLdl`
      (elimination tree + up-looking LDLᵀ after Davis 2005, nested dissection
      on BFS level structures after George 1973) under `AagWeb` and
      `AagRefinement` — 385 joints 2.1 s → 1.2 s, its ×2 refinement 27 s →
      7.6 s, identical results. Open: supernodes (3–6 unknowns per vertex
      factor as dense blocks), the JᵀJ assembly through a hash map (now about
      half the refinement time), and the same solver for `ScissorNet`.
- [ ] Global family assignment across flat points for the older Asymptotic
      Net layouts (the web already handles them by rays).
- [x] Lath strips as developables — 2026-10-04, `Fabrication/DevelopableStrip`
      and the Lath Strip component (Fabrication). Sources: Tang, Kilian, Bo,
      Wallner & Pottmann 2016 (rulings r = κg n + τg t for the strip
      orthogonal to the surface, τg t − κn u for the tangent one, u = n × t);
      Schling, Wang, Hoyer & Pottmann 2022 §3.4 (c̄ = c + w/(r_D·e₃) r_D);
      Starostin & van der Heijden 2007 (x = r + v(b + ηt), η = τ/κ, regular
      while |wη'| < 1); Freddi, Hornung, Mora & Paroni 2016 (corrected
      Sadowsky functional: the narrow ribbon is the rectifying developable only
      while |κ| ≥ |τ|; energy 4τ² beyond). Discrete: ruling = intersection of
      the planes of the neighbouring segments (spanned by the segment and the
      bisected normal, or n̄ × e), so quads are planar and the pattern exact;
      on traced laths the planes are measured over four mesh edges either side.
      Where the developable's ruling leans more than MaxLean (45°) from square
      the ruling is mirrored back towards square (cot θ = 1/η: continuous at
      45°, a helicoid where κ = 0 — Mite's choice, not from the papers) and the
      strip twists; EdgeStrain reports the mean edge elongation the plank
      needs there. Exact tests: helix 60° flat = the cylinder (1e-13), great
      circle closed band, latitude = tangent cone (discrete pyramid radius to
      1e-9, tan φ0 to 2e-4), catenoid asymptotic line v ∈ [1.2, 2.2]: ruling
      angle error 2.6e-3° → 6.5e-4° → 1.6e-4° (41/81/161 stations, second
      order), quads planar to 6e-16, band straight to 2e-6; catenoid v ∈ [−2, 2]:
      developable on 60.8 % (closed-form share of |τ| ≤ |κ| 61.0 %), edge
      strain at the throat = helicoid (W/2)²τ²/2 to 0.1 %; hyperboloid ruling:
      no developable, helicoid strain to 0.4 %; helix 30°: twist regime, edge
      strain √(1 + (W/2)²(τ − κ²/τ)²) − 1 to 1 %. On meshes (64-gon cylinder,
      64 × 64 catenoid): rulings within 1.4° of the axis, strains within 3–5 %
      of the closed forms (the window averages the twist peak).
      Findings:
      - An asymptotic lath that crosses the waist of a catenoid inflects there
        by symmetry (the 180° rotation about the throat normal maps the lath
        to itself reversed, so κg = κn = 0 while τg = 1): no developable
        strip exists across the throat, and with |τ| > |κ| for |v| < 1.12 the
        twisting stretch is 39 % of a rim-to-rim lath of the z ∈ [−2, 2]
        catenoid — independent of the plank width. The studio catenoid
        (z ∈ [−1, 1]) is twist regime over its whole height (|η| ≥ 1.2). So
        upright sheet lamellas on catenoid-like AAG webs twist (helicoid edge
        strain (W/2)²τ²/2: 8e-4 for 80 mm at τ = 1), they are not bent
        developables; the meridian G laths (τg = 0) are developable with
        square rulings everywhere.
      - Lath Unroll's square strip (rulings along the surface normal / n × t)
        is not developable wherever τg ≠ 0: on the catenoid lath above it needs
        edge strain 1.1e-4 at W = 0.1 where the developable needs none.
      - The ribbon model is for thin planks. Timber laths (w/t ≈ 5–10) carry
        twist as rods; Lath Analysis's Saint-Venant twist remains the check
        for them.
      - On a triangulated mesh the traced polylines carry a normal curvature
        of a few 1e-2 that the exact curve does not have (one asymptotic
        family runs along the triangle diagonals, the other across them): the
        developed band bows 5e-2 over a 5 m catenoid lath instead of 2e-3.
- [ ] Lath Strip follow-ups: read the in-plane turning of the pattern from
      the smooth normals (κn as in Lath Analysis) instead of the polyline, so
      facet bias does not bow the pattern; let Lath Sweep, Lath Unroll and Net
      Joints follow the developable (a Strip mode, appended after the Mite
      Net bundle's Net input); Wunderlich's finite-width energy and the
      thickness at which a lath leaves the ribbon regime; strips on the
      refined AAG laths (needs the AAG refinement bundle); the Lath Strip in
      the web app.
      supernodal factorisation.
- [x] Global family assignment across flat points for the older Asymptotic
      Net layouts — 2026-10-06, by the sign of the geodesic torsion (see A).
      The web's own ray logic is untouched; its labels now come from the
      same exact rule (all AsymptoticWeb tests unchanged).

## H. Fabrication kit (José's priority 6)

- [x] Hub plate — 2026-10-24 (session 1, UX / fabrication), `Fabrication/HubPlate`,
      `FlatKit.Options.HubPlates` (0 pin hole as before, 1 one plate flush with
      the inner edge, 2 a bolted sandwich), `HubRadius`, `HubThickness`,
      `HubBolt`; Flat Kit inputs `HubPlates` / `HubRadius` and output `HubPlate`
      appended; a hub choice in the web app's Kit block (two plates by default
      there, pin hole by default in Grasshopper). Literature: Schling, Hitrec &
      Barthel 2017 (singularities of valence ≠ 4; the steel prototype's double
      slots with a bolt and star washers at every node — the slots fix angle and
      position, the bolt the stacking), Schling, Wang, Hoyer & Pottmann 2022
      (singular vertices of valence ≥ 6 at flat points), Schling & Schikore 2022
      (singularities constrain the motion), Cichocka 2024 IASS (slot width for
      a 90° → 75° rotation with the kerf included: 0.84 mm design for 0.69 mm
      plywood), Fab Academy press-fit notes (parametric notches, measured
      kerf, chamfers, test cuts), SendCutSend tolerance notes (bridges ≥ 1×
      the thickness, slots ≥ 1.5× their width from features). None of the
      asymptotic-gridshell prototypes in the literature has a singular node
      (the pavilions avoid them, the kinetic umbrella and canopy are
      singularity-free grids), so the plate is designed from the slot-joint
      practice above and checked against the kinematics: the scissor model
      keeps the Enneper hub's angles to 6e-6° through the press (rays at 60°
      ± 5e-7° in the flat state), so a rigid plate is consistent with the
      motion — at a singular node the mechanism does not turn the rays.
      Rules (header of HubPlate.cs): slot s = t + c, strips end at
      s / (2 sin(α_min/2)) or a bridge past the bolt, key max(1.5 s, 2 t_p,
      w/10), r_in = r_end + key or the bridge rule, R = 0.45 of the first-joint
      distance ≥ r_in + w, cramped otherwise. Tests (`HubPlateTests`, 9): the
      Enneper hub (6 rays, radii in closed form, simple star outline with the
      exact area to 2e-5, strips trimmed and notched, the other 48 strips
      bit-identical, flat strips and `HubPlates` 0 unchanged), an irregular
      planar star (55 / 70 / 55 / 60 / 60 / 60°: the plate follows the measured
      rays, deviation 10° reported), the exports (plate sheet, cut-list rows,
      README, DXF, valid SVG). Bench: Flat Kit card (new on the bench; the
      component had none) with the hub in plan and a strip end in elevation,
      four self-tests (79 PASS).
      Findings: (1) a 1 : 1 kit of the 2-unit Enneper web with 20 × 2 mm strips
      gets Ø 103 mm plates with the slots 43 mm long; with the 1.3.1 test
      proportions (strips as wide as the joint spacing) no plate fits and the
      component says so; (2) the key length, not the clash radius, sets the
      inner disc (4.5 → 8.5 mm): six slots 2.2 mm wide leave 6 mm bridges at
      that radius, well above the 2 mm sheet; (3) `StripOutline` had to drop
      repeated corners once a notch reaches a strip end; (4) the mesh decides
      how exactly the rays sit at 60°: the n-fold Enneper chart at resolution
      40 (the web app's mesh) is not 3-fold symmetric (40 / 3), the traced rays
      leave 60° by 1.0–2.0° (1.45° on the app's coarse web, 1.05° at the 0.115
      spacing) and then the press turns them by 0.04–0.4° relative to each other
      — nothing in the scissor model holds a singular node's angles (six laths
      pinned at one node, no bending term across it); at resolution 48 (48 / 3)
      the rays are at 60° ± 1e-7° and move 6e-6°. The plate follows the
      measured flat rays either way, the component reports both numbers and
      warns above 0.5° of drift, the web app shows the drift (0.40° on its
      coarse web).
      Open: (a) a lath passing *through* a hub (three through-laths instead of
      six rays) gets no plate — it needs a three-way halving (each strip
      notched a third) or the plate split in two half-discs; (b) kerf as an
      input (today the clearance stands in for it: Cichocka's slots came out
      0.32–0.35 mm wider than drawn on plywood, ≈ 0.17 mm of kerf per side);
      (c) 45° lead-in chamfers on the slot mouths (Fab Academy) in the strip and plate outlines; (d) the plates on
      the assembly drawing and in the web app's assembly SVG (today only on
      the plate sheet and the `HubPlate` output); (e) nest the plates among
      the strips once the 2026-10-20 nesting lands; (f) flat strips at a hub:
      a stacked hub with spacers, one strip per layer; (g) washers / a central
      spacer ring between the two plates so the bolt does not crush the
      strips' web; (h) the physical prototype: cut the Enneper kit with two
      plates and measure the hub's stiffness against the pin-hole version;
      (i) a hub-angle constraint in `ScissorNet` (keep the ray angles of a
      singular node, as a rigid plate does) so the model and the kit agree on
      a coarse or unsymmetric web; (j) a mesh resolution divisible by the
      symmetry order for the n-fold Enneper in the web app (40 → 48).

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
- [x] Description text and version notes from README's Unreleased section;
      bump the yak manifest. (1.5.0: Food4Rhino version notes written, manifest at 1.5.0.)
- [x] CI and release hygiene (2026-10-08, session 2; step 4 of the 1.4.0
      integration): `.github/workflows/ci.yml` (Linux, .NET 10: Core +
      Grasshopper build, tests, bench build + `tools/webgen/selftest.js`
      light/dark, web publish + `tools/web-smoke.js`); `ReleaseConsistencyTests`
      (one version in 9 places, component count in 5 places = component
      classes, panel count, README list by panel, icons drawn / embedded / on
      disk, bytecode ignored); `build-yak.sh` for the Mac; `build.py` makes
      `site/` and uses UTF-8; the committed `.pyc` untracked.
      Findings: GitHub's default `run` shell is `bash -e` without pipefail, so
      `node web-smoke.js | tee` would have hidden a failure — the workflow sets
      `shell: bash` (= `-eo pipefail`). `build.py` failed on a fresh clone
      (`site/` is git-ignored and was never created). The bench has cards for
      28 of the 30 components (Asymptotic Web, Flat Kit missing).
- [ ] Watch the first CI run on GitHub (not observable from the sessions):
      runner time of the web job (~4 min smoke test here) and whether
      `playwright install --with-deps chromium` needs a newer Playwright than
      1.56.0 on the then-current ubuntu-latest image.
- [ ] Raise the bench self-test floor (`--min 71` in ci.yml) after the 1.4.0
      integration adds checks; consider deriving it from `notes.py`.
- [ ] A tag-triggered workflow that attaches the bench page and the web
      site zip to a GitHub release (the .yak still needs Rhino's yak, so it
      stays a local `build-yak.sh` / `build-yak.ps1` step).

## Done

- [x] 2026-11-02 (session 1, D / priority 2): joint eccentricity in Gridshell
      Analysis — laths in layers on rigid end offsets (`AxisOffsets`, `Layer`
      / `Align`, `ReactionMoments`, moment equilibrium), five exact tests, two
      bench self-tests (77 PASS).
- [x] 2026-10-18 (session 1, D): Lath Analysis geodesic curvature — the loop
      closure's lateral zigzag found and fixed (`CloseSmoothly` spreads the
      gap in the tangent plane: a traced great circle read |kg| 1.12 / flat
      lath utilization 11, now 0.03 / 1.00), the kg estimator made exact for
      circles at any sampling (1.36 → 1.00 on a coarse latitude circle), the
      strain model checked against Tang 2016, Pottmann 2010 and Schling 2018;
      15 exact tests, two bench self-tests (77 PASS).
- [x] 2026-10-19 (session 1, B/C with A): nets seeded from a guide curve —
      `Guide` input on Asymptotic / Conjugate / Geodesic Net and Curvature
      Streamlines, Schling's node-spaced seed curve and the Studio X origin
      points; smooth-surface pull of off-surface guides; parallel-transported
      geodesic directions; 8 exact tests, 3 bench self-tests (78 PASS), the
      app's `web · guide` layout (web smoke 28/28).
- [x] 2026-10-19 (session 2, D): Chebyshev Net — exact compass step (Newton
      with reprojection, chord-sampled axes, every edge L to 1e-14), checked
      against the closed-form compass net on the sphere, the rolled lattice
      on a prism and the flat lattice; Hazzidakis locking understood and
      stopped by `MinAngle`; `Report` output; bench card with the closed-form
      and locking self-tests (77 PASS).
- [x] 2026-10-20 (session 1, C): Flat Kit nesting — first-fit decreasing
      shelves, diagonal lanes, lapped pieces between the joints, material %
      with the area bound; `KitNestingTests` (10); web app Kit summary and
      smoke checks; `Splice` input on Flat Kit.
- [x] 2026-10-20 (session 2, A/D): the triaxial ellipsoid as a test typology
      with closed-form umbilics, curvatures, shape operator and confocal
      coordinates; umbilics located as the degenerate points of the principal
      direction field with their index (`Umbilics.Locate`, Grasshopper and web
      outputs appended); 12 exact tests, six bench self-tests on the Umbilics
      card (81 PASS), two web smoke checks.
- [x] 2026-10-21 (session 1, B/G): Net Trim — cookie-cutting a net to a
      boundary drawn on or above the mesh (closest point or projected), holes
      by loop depth, Inside / Invert, exact cuts; 10 closed-form tests, six
      bench self-tests (81 PASS), smoke case, icon.
- [x] 2026-10-21 (session 2, D): Dynamic Relaxation after Barnes — fictitious
      masses, reset to the kinetic-energy peak, half-step start, 3–6 × fewer
      steps at the same equilibria; elastic laths (bending stiffness, cut
      lengths) checked against the Euler elastica; exact FDM tests.
- [x] 2026-10-22 (session 1, C/B with D): panels of a net and their planarity
      — `NetFaces` (faces of the net graph, Zadravec's δ_PQ,n and the plane
      deviation), Net Topology `Mesh` / `Planarity` / `Deviation` outputs, the
      web app's panels switch and planarize button, 7 exact tests, two bench
      self-tests (77 PASS), web smoke 33 PASS.
- [x] 2026-10-22 (session 2, D): Planarize Mesh by guided projection (Tang et
      al. 2014 with Liu et al. 2006's decay and Zadravec et al. 2010's relative
      fairness): planar to 1e-9 in 10–15 iterations on conjugate nets, fixed
      points exact, honest stall or far-off-surface warning on asymptotic nets;
      11 tests, three bench self-tests (77 PASS).
- [x] 2026-10-23 (session 1, A/C/B): exact curvature on the analytic
      catalogue (`AnalyticSurface`, 22 tests, nine shapes measured against
      the truth), the ellipsoid's four umbilics, the bench curvature cards
      with measured / exact / |error| on eight shapes (81 PASS) and the
      `Test Shape` component (31 components).

- [x] 2026-10-23 (session 2, E): deployment from the flat state — one-sided
      ground, scissor drivers, the softest mode of the Gauss–Newton matrix as
      the deployment branch, `Deploy` / `Angle` / `Kick` on Net Kinetics; exact
      umbrella-from-bundle test, 12 deployment tests, four bench self-tests
      (79 PASS).
- [x] 2026-10-24 (session 1, UX / fabrication, H): hub plates for the Flat Kit —
      `HubPlate` in the core, `HubPlates` / `HubRadius` inputs and `HubPlate`
      output appended to Flat Kit, the hub choice in the web app, nine exact
      tests, the Flat Kit bench card with four self-tests (79 PASS).
- [x] 2026-10-24 (session 2, G/D): AGG webs — `Gridshells/AggWeb`, the AGG
      Web component, the exact cylinder web as a test typology, 16 tests
      (eight with closed-form truth, seven Jacobian checks), bench card with
      four shapes and four self-tests (79 PASS).
- [x] 2026-10-25 (session 1, B): component icons in the Mite palette —
      black, white and neon — with `IconTests` (palette, border, legibility
      on a light capsule and a dark menu) and the icon sheet with three
      self-tests on the bench (78 PASS).
- [x] 2026-10-25 (session 2, A/D): holes as a test typology (annular saddle,
      three-hole saddle, paraboloid annulus, flat annulus; `AnnulusTests`, 15)
      and geodesic laths that end straight on their neighbours instead of
      being bent onto them (Pirazzi & Weinand 2006, Pottmann et al. 2010);
      bench `saddle · hole` / `flat annulus` shapes, 79 self-tests.
- [x] 2026-10-26 (session 1, C/D): Chebyshev angle map and isocurve labels
      in the web app; `ShearAngle`, Hazzidakis `CellCurvature`, `CountLocked`
      in the core, `Curvature` output on Chebyshev Net; the chord-length axis
      fix; 8 exact tests (plane, sphere, cylinder, saddle with convergence),
      3 bench self-tests (78 PASS), 9 web-smoke checks (37 PASS).
- [x] 2026-10-26 (session 2, D / José's priority 2): linear buckling in
      Gridshell Analysis — consistent geometric stiffness, subspace iteration,
      Sturm check, mode shapes; eight exact tests (Euler column, round bar,
      orientation, portal vs stability functions, arch grid), four bench
      self-tests (79 PASS), `extract_components.py` for the bench parameters.
- [x] 2026-10-27 (session 1, B/D): per-family profiles for the
      asymptotic–geodesic hybrid — family B's own profile on Net Joints with
      threaded joints (a slot through the upright lamella, the flat slat
      uncut) and on Gridshell Analysis with one section per lath; 13 exact
      tests, the catenoid AGH net on the bench (79 PASS).
- [x] 2026-10-30 (session 1, G/D): Web Propagation — AAG webs grown from a
      drawn boundary strip (Wang, Almaskin & Pottmann 2025) with exact planar
      stars and geodesic rows, two closed-form discrete AAG webs (discrete
      catenoid, hyperboloid rulings) as test typologies, the reference-free
      AAG optimisation, bench card with four self-tests (79 PASS).
- [x] 2026-10-30 (session 2, C/B): one colour language — `Visualisation/
      ColourMap` and `Legend` in the core (Moreland 2009 Msh path, Kovesi 2015
      lightness equalisation, Heckbert 1990 ticks), Mesh Colour Map legend
      outputs, Lath Preview and the web app on the same tables, bench colour
      bars with ticks, 21 tests, five bench self-tests (81 PASS).
- [x] 2026-10-31 (session 1, G/D): tri-hex (midpoint) AAG webs — the AGH
      gridshell layout — and a constant crossing angle on Asymptotic Web; the
      rotational CRPC surface as a test typology; the diagonal-family
      labelling fixed across sub-lattices; six exact tests, bench Asymptotic
      Web card with five shapes and five self-tests (80 PASS).
- [x] 2026-10-31 (session 2, C/B): Pieces — stock lengths, splices, joints and
      boards in the web app and in Lath Segment (`CuttingStock`, balanced
      segmentation, boards / material / report; 7 exact tests, 10 web-smoke
      checks, 2 bench self-tests).
- [x] 2026-11-01 (session 1, D/A): Mesh Cleanup closes cracks and T-junctions
      (`Cracks`): vertex–edge contraction in order of distance after Borodin
      et al. 2002, vertices kept in place; exact tests on two flat patches, a
      sphere from two hemispheres with different meridian counts (Gauss–Bonnet),
      a double wall, and a hyperboloid whose rulings cross the mended seam;
      two bench self-tests (77 PASS).
- [x] 2026-11-01 (session 2, B): progress in the Message line and Esc on every
      long component — `ProgressHandler` / `ProgressScope` in the core, a
      `Progress` option on fourteen solvers, coverage-based progress for the
      nets, residual-decay progress for the iterative solvers; 13 tests, two
      bench self-tests (77 PASS).
- [x] 2026-10-09 (session 1, A/D): dirty meshes for Mesh Cleanup and mesh
      repair — outward / majority winding, isolated vertices, needles and
      caps (`Sliver`), a defect report, 15 exact dirty-mesh tests, four bench
      self-tests (75 PASS).
- [x] 2026-10-08 (session 2, release hygiene): CI workflow, release
      consistency tests, `build-yak.sh`, bench self-test runner (see F).
- [x] 2026-10-08 (session 1, D/A): exact geodesic paths by edge flips
      (FlipOut + signposts + Steiner-graph start) for Geodesic Path, exact
      global closest point, the polyhedral cone as a test typology with its
      exact development; bench Geodesic Path card with sphere / cone /
      saddle-vertex shapes and four self-tests.
- [x] 2026-09-27 (UX, B): the Mite Net data type — one wire for surface,
      families, profiles; lath-end classification; Net outputs/inputs on 13
      components; bench card and generated parameter tables (see B).
- [x] 2026-10-03 (session 1, G): smooth AAG laths by subdivision and
      re-optimisation (Schling et al. 2022 §3.4) with exact catenoid and
      hyperboloid lattice tests; AAG fairness default 0.05 → 0.2 (44.5° kinks
      at the rays → 21°); nested-dissection sparse LDLᵀ for the AAG solves;
      bench card for Asymptotic Web (29 components; `extract_component.py`
      writes a `components.json` entry from a component source — the Asymptotic
      Net entry has drifted, 9 of 11 inputs).

- [x] 2026-10-04 (session 2, C): Net Distributions — spacing across each family,
      crossing angles and utilization as histograms in the core, Grasshopper
      and the bench (see C).
- [x] 2026-10-05 (session 2, B): mode menus — right-click submenus, the
      Message line and value lists from one `ModeCatalog`, checked against
      the component sources; bench parameter tables regenerated from the
      sources, menus and Messages on the cards, Asymptotic Web card.
- [x] 2026-10-06 (session 1, A/D/G): asymptotic families labelled by the
      sign of their geodesic torsion (A right-handed, B left-handed) instead
      of breadth-first combing; curves locked to their family; exact tests on
      helices, hyperboloid rulings, monkey saddle and 3-fold Enneper.
- [x] 2026-10-06 (session 2, B): runtime messages with the fix in them —
      `InputAdvice` checks of spacing, step, saddle region / MinAngle, seeds
      and lath sections, wired into 12 components, bench self-tests.
- [x] 2026-10-07 (session 1, D): farthest-point seeding (Mebarki, Alliez &
      Devillers 2005) as Layout 3 of the field nets, with exact plane and
      torus placements and a side-by-side measure against the neighbour
      fill (`PlacementMetrics`, bench checks on Asymptotic Net and Curvature
      Streamlines).
- [x] 2026-10-07 (session 2, C): Mite Bench review tools — find box,
      facets with counts, keyboard shortcuts, GitHub issue draft.
- [x] 2026-09-27 (José's session, 1.3.0): Asymptotic Web and AAG webs (G);
      Gridshell Analysis joints inserted exactly (the snap had chained joints
      into a few nodes), supports fixed / pinned, joint stiffness, self-weight,
      area and point loads, reactions and equilibrium, torsion in the
      utilisation, unsupported parts reported; web app: symmetric webs, AAG,
      one section for every lath in mm against a span in m, structure
      supports and joints, kinetics block, black / white / neon palette.
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
