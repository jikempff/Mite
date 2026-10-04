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
3. The kinetic behaviour of any web in the browser (done for scissor / lift /
   spread drives, 1.3.0; the flat state as the assembly state — press flat,
   the 3-fold Enneper web into a hexagon — 1.3.1); next: speed, strain
   colouring per state, the deployment driven from the flat grid by pulling
   the singular node (José's Kangaroo sequence), the physical prototype
   dimensions.
6. The kit to build it (done 1.3.1: Flat Kit — flat assembly 1 : 1, strips
   with slots / holes / hub on laser sheets, deployment sequence, cut list,
   SVG / DXF / CSV); next: a physical prototype of the 3-fold Enneper to
   check slot play and the deployment against the model, hub plate design,
   supports, and the G laths in the kit.
   spread drives, 1.3.0; strain colouring per state and the envelope over the
   motion done 2026-10-02, see E); next: speed, the flat state as the
   assembly state.
4. One lath section for every curve (done in the web app 1.3.0; per-family
   profiles in Grasshopper still open).
5. Black, white and neon for everything visual (web app done 1.3.0; one
   palette in the core for Grasshopper, the app and the bench 2026-10-03, see
   C; icons and Food4Rhino images still in the old palette).

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
- [ ] Annulus / disk with holes: inner boundaries, loops around holes,
      curves ending on inner borders.
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
      = Grasshopper's selection colour). Open: the component's plain curve
      outputs still preview in Grasshopper green on top — a preview toggle,
      contact dots, and the same colours in Net Joints / Net Topology outputs.
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
- [ ] App 3D viewer: family colours per theme (it draws the light-theme
      colours on the dark theme too) and the remaining hard-coded neon
      constants (`NEON.yellow`, the seed green) through the palette.
- [ ] Grasshopper previews: the Mite Net preview (unmerged 2026-09-27 bundle)
      should take `MitePalette.FamilyA/B` and the end-class colours once merged;
      check them against Rhino's default grey viewport background.
- [ ] Icons in black / white / neon (`tools/generate_icons.py`) after the Mite
      Net bundle (two new icons) is merged, then the Food4Rhino screenshots.
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
- [ ] Mesh repair, next: (a) non-manifold edges and vertices are reported,
      not repaired — cut them by duplicating vertices (Guéziec, Taubin,
      Lazarus & Horn 2001, Cutting and stitching) and offer it as an option;
      (b) T-junctions and cracks (a vertex lying on the other side's edge, a
      seam with different vertex counts) are not welded — snap vertices into
      edges within Tolerance (Borodin, Novotni & Klein 2002, Progressive gap
      closing); (c) holes are counted (border loops), never filled: a hole
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
- Wang, Schling, Pottmann et al. 2024, Computational design of asymptotic
  geodesic hybrid gridshells via propagation algorithms (CAD 2024): webs grown
  from a boundary strip of two neighbouring polylines, row by row, with a
  global optimisation that may adjust the strip; GGG / AGG / AAG.
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
- [ ] AGG webs: a G-net (equal opposite angles) with asymptotic diagonals,
      initialised from a developable (cylinder web of Graf & Sauer) — the
      family with more freedom (Schling et al. 2022 §2.3).
- [ ] Propagation from a boundary strip (Wang et al. 2024) as a second way to
      start a web: two neighbouring polylines drawn by the user.
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
- [ ] Choose the seed spacing per ray (e.g. uniform in the asymptotic
      parameter instead of arc length) for more homogeneous cells, and a
      "cookie-cut" option: trace on a larger surface, trim to a boundary.
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

- [x] 2026-11-02 (session 1, D / priority 2): joint eccentricity in Gridshell
      Analysis — laths in layers on rigid end offsets (`AxisOffsets`, `Layer`
      / `Align`, `ReactionMoments`, moment equilibrium), five exact tests, two
      bench self-tests (77 PASS).
- [x] 2026-10-09 (session 1, A/D): dirty meshes for Mesh Cleanup and mesh
      repair — outward / majority winding, isolated vertices, needles and
      caps (`Sliver`), a defect report, 15 exact dirty-mesh tests, four bench
      self-tests (75 PASS).
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
