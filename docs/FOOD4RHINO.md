# Food4Rhino listing — Mite 1.5.1

Paste-ready copy for https://www.food4rhino.com/en/app/mite (edit form). Images
to upload are listed at the end; the package itself goes up with
`yak push dist/mite-1.5.1-rh8_0-any.yak`, which also sets the package icon (1.5.0 is on the server; 1.5.1 adds the Kinetics panel).

## Title
Mite — curvature, form finding and gridshell nets for Grasshopper

## Short description (one line)
Mesh curvature analysis, form finding, asymptotic / geodesic / Chebyshev nets, lath buildability and fabrication — open source, pure .NET, with a browser twin at kempffsele.me/mite.

## Description
Mite is an open-source Grasshopper toolkit (Rhino 8, Windows and Mac) for
designing gridshells from meshes: 39 components in seven panels, no external
dependencies, MIT licence.

**Curvature.** Principal, Gaussian and mean curvature per vertex (Rusinkiewicz
tensors, tensor-smoothed), curvature streamlines, umbilics, isocurves and a
one-step colour map.

**Form finding.** Planarization, minimal surfaces, force density method and
dynamic relaxation on a built-in sparse solver — tens of thousands of vertices
in seconds.

**Webs.** Asymptotic webs after Schling: nodes every spacing along the two
asymptotic curves through a seed, every other node where the curves through
them cross — clean quad nets with no stubs or T-junctions that keep the
surface's symmetry (flat points become singular nodes, surfaces of revolution
rotational webs). An AAG option makes one family of node diagonals geodesic:
gridshells of three families of straight flat slats, two upright and one
lying on the surface (Schling, Wang, Hoyer & Pottmann 2022) — node to node or
through the lath midpoints (tri-hex), at a chosen crossing angle. AGG Web gives
the two-geodesic counterpart, Web Propagation grows an AAG web free in space
from a drawn boundary strip (Wang, Almaskin & Pottmann 2025).

**Nets.** Asymptotic nets (both families of zero-normal-curvature curves,
combed labels, minimum crossing angle), geodesic nets (straightest geodesics
with Jacobi-field seeding for even strips, from a seed or from a border),
Chebyshev nets (equal edge lengths, the kinematics of elastic gridshells),
conjugate nets (curvature lines, the layout for planar quads) and exact shortest
geodesic paths (edge flips, Sharp & Crane 2020). Curves run border to border; merged traces end on their
neighbour as T-junctions.

**Buildability.** Lath Analysis splits each curve into geodesic curvature,
normal curvature and geodesic torsion in the Darboux frame and converts them
to bending and twist strains for the chosen section — flat or upright strip,
round bar or your own profile; Gridshell Analysis solves the whole net as a
beam frame with one section per lath family (crossings and T-junctions coupled,
laths stacked in layers joined through their lever arm, supports on the border,
utilisation per lath, linear buckling factors and mode shapes).

**Fabrication.** Lath Sweep (rectangular, round or custom sections, flat or
upright — one profile for every curve, and the same profile the strain check
and the beam frame use), Net Joints (half-lap and egg-crate
notches at every crossing), Lath Unroll (exact cutting patterns), Lath Segment
(stock lengths with splice joints away from joints), Lath Labels (IDs and a
BOM), Net Topology (nodes and members for any downstream solver).

**Kinetics.** Net Kinetics moves a scissor-jointed asymptotic net as a
semi-compliant grid mechanism (fixed, sliding and moved nodes, cables, a fold
slider) and reads every state elastically: bending and twist strains of each
lath, the strain energy along the motion and the natural, least-energy state.
Mechanism Modes shows how a net can move before you drive it; Kinetic State
reads any moment of a motion to bake or pass on.

**Tested against analytic truth.** Cylinder, hyperboloid, catenoid, Enneper,
ruled patches and Schwarz D are part of the test suite with closed-form
curvature, rulings and asymptotic directions; every algorithm is checked
where the answer is exact. The same library runs in the browser at
kempffsele.me/mite — try a net on your own OBJ before installing anything.

Source, examples and roadmap: https://github.com/jikempff/Mite

## Version notes — 1.5.1
- Kinetics panel: Mechanism Modes shows how a scissor net can move before you set any driver — its softest motions, which of them are free (mechanisms), each drawn displaced; Kinetic State reads any moment of a Net Kinetics motion (new Motion output) to draw, bake or pass on, exact at the solved states.
- Web app: record the motion as a video (record button beside play), and a new symbol for loading your own mesh.
- Package: built from a clean folder (1.5.0 carried an unused old zip); new Package Manager description.

## Version notes — 1.5.0
- Gridshell Analysis reads the structure as it is built: one section per lath family (the asymptotic–geodesic hybrid, upright lamellas with flat slats), laths stacked in layers and bolted through the lever arm of the stack (joint eccentricity, reaction moments), and linear buckling factors with mode shapes and a Sturm check.
- New components: AGG Web and Web Propagation (Gridshells), Net Trim (cut a net to a boundary), Mite Net (one wire for surface, both families and profiles), Net Distributions (spacing, angles and utilization as distributions), Lath Strip (developable plank strips) — 37 components in all.
- Nets: seeded from a guide curve, a farthest-point fill, families labelled by their twist, tri-hex AAG webs at a constant crossing angle, smooth refined AAG laths, Chebyshev nets with an exact compass step, a lock angle and their own shear and curvature, geodesic laths that end straight on their neighbours.
- Form finding: Dynamic Relaxation after Barnes (elastic lath bending), Planarize Mesh by guided projection, panels of a net with their planarity.
- Fabrication: Flat Kit nests its strips on the laser bed and holds a 3-fold hub with laser-cut plates; Lath Segment packs pieces on stock boards with balanced cuts and kerf; Net Joints threads a flat slat through an upright lamella.
- Kinetics: deploy a net from its flat state on a table, with the strain of every lath along the motion.
- Everywhere: the active modes and the progress under each component, Esc on every long solve, runtime messages that name the fix, right-click mode menus, one black / white / neon colour language with legends, Mesh Cleanup that closes cracks and T-junctions, exact curvature on the analytic test shapes (Test Shape).

## Version notes — 1.3.2
- Geodesic Path gives the exact geodesic of the mesh (new Method input, on by default): the path is shortened by intrinsic edge flips until it is straight across every face and edge, then returned as a polyline through every edge crossing. It goes round a cone tip, never over it; on a cone the length matches the exact unfolded value to 1e-13. Faster than before (1.2–15×); the old curve shortening stays as Method 0.

## Version notes — 1.3.1
- Net Kinetics presses a net flat (new Flatten input): the lath ends slide onto a plane and the mechanism finds the flat assembly state — a 3-fold Enneper web lays flat into a hexagon with every joint spacing kept. Read backwards, it is the deployment of the flat grid. Also the default motion in the browser app.

## Version notes — 1.3.0
- New Asymptotic Web component: Schling's symmetric asymptotic webs — no short stubs, no T-junctions, the surface's symmetry kept (a 3-fold Enneper web is one sector rotated three times around a singular node of six laths; a catenoid web is rotational with geodesic meridian diagonals).
- AAG webs: one family of node diagonals optimised to exact geodesics with planar node stars — three families of straight flat slats. On a 10 m Enneper web of 60 × 6 mm timber the deflection drops from 1.7 m (quads) to 11 mm (AAG).
- Gridshell Analysis: every crossing is now its own frame node (dense nets used to collapse into a few nodes); fixed or pinned supports, rigid / hinged / semi-rigid joints, self-weight, area and point loads, reactions with an equilibrium check.
- Browser app: symmetric webs and AAG, one lath section in millimetres for every curve, supports and joints in the structure check, the net moving as a scissor mechanism, and a black / white / neon look.

## Version notes — 1.2.8
- New Net Kinetics component (Mite > Kinetics): a scissor-jointed asymptotic net moved as a mechanism — constant joint spacing, hinges normal to the surface, each lath's bending held against its rest shape; drivers are fixed, sliding and moved nodes and cables, one state per fold step. Checked against the exact doubly ruled hyperboloid mechanism of Schikore et al. (2020) to 2e-12 through the whole motion.
- Every kinetic state is read elastically: normal and geodesic curvature and geodesic torsion of every lath, strains for the chosen section, utilisation per lath, the strain energy along the motion and the natural (least-energy) configuration.

## Version notes — 1.2.7
- Nets follow the exact curves: the curvature estimate at the border rows is an osculating-jet fit instead of a one-sided difference (8° → 0.7° direction error at a catenoid rim), so traced asymptotic curves stay within 0.01–0.03 mesh edge of the closed-form lines.
- New `Layout` input on Asymptotic Net, Conjugate Net, Curvature Streamlines and Geodesic Net: 1 = web from the border, 2 = web from a seed cross — every curve runs border to border, seeds exactly one spacing apart, gaps filled at the right spacing; 0 = the evenly spaced fill as before.
- Browser app: layout choice (border web by default) and a legend that explains every marker in the viewport.

## Version notes — 1.2.6
- One profile everywhere: Lath Analysis and Gridshell Analysis take the `Shape` / `Section` inputs of Lath Sweep — round bars and custom sections are strain-checked with their real fibre distances and Saint-Venant twist, and the beam frame gets their A, I, J.
- 1.2.5: new logo (black hexagon, knot tab icon), full icon set in the Grasshopper house style, loading-error fix; Lath Sweep `Shape` (rectangle / round bar / custom closed section) applied to every curve.
- Lath Analysis: normal curvature read from the surface normal's rotation — straight rulings now report kn ≈ 0 instead of facet noise (an upright lath was falsely at utilisation 1.8).
- Geodesic tracer: parallel transport of the direction — a 45° helix on a cylinder stays at 45.01° instead of drifting to 42° with small steps.
- 1.2.2: combed asymptotic families with a minimum crossing angle; Jacobi-field geodesic seeding and border seeding; the browser app.
- Test surfaces: cylinder, hyperboloid, catenoid, 2/3-fold Enneper, bilinear ruled patch, Schwarz D, gyroid.
- Browser app at kempffsele.me/mite: each block names the plugin component it runs (with its icon), catalogue surfaces, section choice, share image.

## Keywords
gridshell, asymptotic, geodesic, chebyshev, curvature, minimal surface, form finding, force density, dynamic relaxation, lath, timber, fabrication, unroll, planarization, mesh

## Images (in `food4rhino/`, upload in this order; 1600 × 1000, from the web app and the bench at 1.5.1)
1. `app-01-catenoid-asymptotic.png` — the symmetric asymptotic web on a catenoid (hero).
2. `app-02-catenoid-gaussian.png` — Gaussian curvature under the web (K < 0 everywhere: a minimal surface).
3. `app-03-catenoid-beam-frame.png` — Gridshell Analysis: the net as a beam frame, utilisation per element.
4. `app-04-catenoid-pieces.png` — Pieces: laths cut to stock with splices clear of the joints, boards packed.
5. `app-05-catenoid-panels-planarity.png` — curvature-line panels and their planarity.
6. `app-06-dome-chebyshev-angle-map.png` — Chebyshev net on a dome with its shear-angle map.
7. `app-07-hyperboloid-rulings.png`, `app-08-enneper-3fold-web.png` — catalogue surfaces: straight rulings, the 3-fold web.
8. `app-09-enneper-kinetics-strain.png`, `app-10-enneper-pressed-flat.png` — Net Kinetics: the web pressed flat, laths coloured by strain.
9. `app-11-enneper-flat-kit.png` — Flat Kit: the flat assembly, deployment and nested strip sheets.
10. `app-12-enneper-dark.png` — dark theme.
11. `10-component-icons.png` — the 39 components by panel.
12. `bench-13-mechanism-modes.png`, `bench-14-kinetic-state.png`, `bench-15-gridshell-analysis.png`, `bench-16-asymptotic-web.png` — Mite Bench cards: what to expect, self-tests on the real library, a live view.
13. Rhino/Grasshopper screenshots (to take on your machine): the Mite tab, a
    catenoid definition Asymptotic Net → Lath Analysis → Lath Sweep → Net Joints,
    Gridshell Analysis deformed net, Net Kinetics with Kinetic State on a slider.
Logo: `logo/mite-logo-512.png` (transparent, 512 × 512).
