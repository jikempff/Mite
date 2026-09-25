# Food4Rhino listing — Mite 1.2.6

Paste-ready copy for https://www.food4rhino.com/en/app/mite (edit form). Images
to upload are listed at the end; the package itself goes up with
`yak push dist/mite-1.2.6-rh8_0-any.yak`, which also sets the package icon (1.2.5 is on the server already).

## Title
Mite — curvature, form finding and gridshell nets for Grasshopper

## Short description (one line)
Mesh curvature analysis, form finding, asymptotic / geodesic / Chebyshev nets, lath buildability and fabrication — open source, pure .NET, with a browser twin at kempffsele.me/mite.

## Description
Mite is an open-source Grasshopper toolkit (Rhino 8, Windows and Mac) for
designing gridshells from meshes: 27 components in six panels, no external
dependencies, MIT licence.

**Curvature.** Principal, Gaussian and mean curvature per vertex (Rusinkiewicz
tensors, tensor-smoothed), curvature streamlines, umbilics, isocurves and a
one-step colour map.

**Form finding.** Planarization, minimal surfaces, force density method and
dynamic relaxation on a built-in sparse solver — tens of thousands of vertices
in seconds.

**Nets.** Asymptotic nets (both families of zero-normal-curvature curves,
combed labels, minimum crossing angle), geodesic nets (straightest geodesics
with Jacobi-field seeding for even strips, from a seed or from a border),
Chebyshev nets (equal edge lengths, the kinematics of elastic gridshells),
conjugate nets (curvature lines, the layout for planar quads) and shortest
geodesic paths. Curves run border to border; merged traces end on their
neighbour as T-junctions.

**Buildability.** Lath Analysis splits each curve into geodesic curvature,
normal curvature and geodesic torsion in the Darboux frame and converts them
to bending and twist strains for the chosen section — flat or upright strip,
round bar or your own profile; Gridshell Analysis solves the whole net as a
beam frame with that same section (crossings and T-junctions coupled,
supports on the border, utilisation per lath).

**Fabrication.** Lath Sweep (rectangular, round or custom sections, flat or
upright — one profile for every curve, and the same profile the strain check
and the beam frame use), Net Joints (half-lap and egg-crate
notches at every crossing), Lath Unroll (exact cutting patterns), Lath Segment
(stock lengths with splice joints away from joints), Lath Labels (IDs and a
BOM), Net Topology (nodes and members for any downstream solver).

**Tested against analytic truth.** Cylinder, hyperboloid, catenoid, Enneper,
ruled patches and Schwarz D are part of the test suite with closed-form
curvature, rulings and asymptotic directions; every algorithm is checked
where the answer is exact. The same library runs in the browser at
kempffsele.me/mite — try a net on your own OBJ before installing anything.

Source, examples and roadmap: https://github.com/jikempff/Mite

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

## Images (in `food4rhino/`, upload in this order)
1. `app-01-catenoid-asymptotic.png` — asymptotic net on a catenoid in the browser app (hero).
2. `app-02-catenoid-gaussian.png` — Gaussian curvature colouring under the net.
3. `app-03-catenoid-lath-utilization.png` — one lath analysed (plots + unrolled pattern), every lath coloured by utilisation.
4. `app-04-catenoid-beam-frame.png` — Gridshell Analysis: deformed net and utilisation.
5. `app-05-schwarz-d-asymptotic.png`, `app-06-hyperboloid-rulings.png`, `app-07-enneper-3fold.png` — catalogue surfaces.
6. `app-08-saddle-geodesic-net.png` — geodesic net.
7. `app-09-catenoid-dark.png` — dark theme.
8. `10-component-icons.png` — the component set (bench sheet); `02-asymptotic-net-hyperboloid.png`, `06-lath-sweep.png`, `07-net-joints.png` — bench cards with self-tests.
9. Rhino/Grasshopper screenshots (to take on your machine): the Mite tab, a
   catenoid definition Asymptotic Net → Lath Analysis (Shape 1) → Lath Sweep → Net Joints,
   Gridshell Analysis deformed net, Lath Preview colouring, Lath Unroll patterns.
Logo: `logo/mite-logo-512.png` (transparent, 512 × 512).
