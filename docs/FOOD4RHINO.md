# Food4Rhino listing — Mite 1.2.4

Paste-ready copy for https://www.food4rhino.com/en/app/mite (edit form). Images
to upload are listed at the end; the package itself goes up with
`yak push dist/mite-1.2.4-rh8_0-any.yak`, which also sets the package icon.

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
to bending and twist strains for a flat or upright strip; Gridshell Analysis
solves the whole net as a beam frame (crossings and T-junctions coupled,
supports on the border, utilisation per lath).

**Fabrication.** Lath Sweep (rectangular, round or custom sections, flat or
upright, one profile for every curve), Net Joints (half-lap and egg-crate
notches at every crossing), Lath Unroll (exact cutting patterns), Lath Segment
(stock lengths with splice joints away from joints), Lath Labels (IDs and a
BOM), Net Topology (nodes and members for any downstream solver).

**Tested against analytic truth.** Cylinder, hyperboloid, catenoid, Enneper,
ruled patches and Schwarz D are part of the test suite with closed-form
curvature, rulings and asymptotic directions; every algorithm is checked
where the answer is exact. The same library runs in the browser at
kempffsele.me/mite — try a net on your own OBJ before installing anything.

Source, examples and roadmap: https://github.com/jikempff/Mite

## Version notes — 1.2.4
- New logo and a full icon set in the Grasshopper house style.
- Lath Sweep: `Shape` (rectangle / round bar / custom closed section curve) applied to every curve, riding in the surface frame.
- Lath Analysis: normal curvature read from the surface normal's rotation — straight rulings now report kn ≈ 0 instead of facet noise (an upright lath was falsely at utilisation 1.8).
- Geodesic tracer: parallel transport of the direction — a 45° helix on a cylinder stays at 45.01° instead of drifting to 42° with small steps.
- 1.2.2: combed asymptotic families with a minimum crossing angle; Jacobi-field geodesic seeding and border seeding; the browser app.
- Test surfaces: cylinder, hyperboloid, catenoid, 2/3-fold Enneper, bilinear ruled patch, Schwarz D, gyroid.

## Keywords
gridshell, asymptotic, geodesic, chebyshev, curvature, minimal surface, form finding, force density, dynamic relaxation, lath, timber, fabrication, unroll, planarization, mesh

## Images (in `food4rhino/`, upload in this order)
1. `app_catenoid.png` — asymptotic net on a catenoid in the browser app (hero).
2. `app_schwarzd.png` — Schwarz D patch with its asymptotic net.
3. `app_hyperboloid.png` — hyperboloid: the net is the two families of rulings.
4. `02-asymptotic-net-hyperboloid.png` — bench card with self-tests.
5. `06-lath-sweep.png`, `07-net-joints.png`, `08-gridshell-analysis.png` — fabrication and analysis cards.
6. `10-component-icons.png` — the component set.
7. Rhino/Grasshopper screenshots (to take on your machine): the Mite tab, a
   catenoid definition Asymptotic Net → Lath Sweep → Net Joints, Gridshell
   Analysis deformed net, Lath Preview colouring, Lath Unroll patterns.
Logo: `logo/mite-logo-512.png` (transparent, 512 × 512).
