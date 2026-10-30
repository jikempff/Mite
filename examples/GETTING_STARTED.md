# Getting Started with Mite

Install via Rhino's `_PackageManager` (search **mite**), then find the **Mite**
tab in Grasshopper. All components take a plain Rhino mesh — no plugins or
special data types required. The icons use the same colours as the web app
and the bench: blue and red are the two lath families, green a geodesic
family or a form-found mesh, yellow an analysis, white a surface or a cut
sheet, black the marks — and they read on Rhino's light and dark canvases.

## 1. See curvature (2 minutes)

1. Add a **Mesh Sphere** (Mesh > Primitive) or reference any mesh.
2. Wire it into **Gaussian Curvature** (Mite > Curvature).
3. Wire the mesh and `K` into **Mesh Colour Map** (Mite > Util): neon blue
   is negative, white zero, crimson positive — the same colours as the web
   app, with equal lightness at both ends so −K and +K of the same size read
   equally strong. `P` = 1 is the sequential map (black → neon blue → neon
   green → white) for magnitudes, 2 the utilization map, 3–5 the 1.3.0
   colours. (Or build your own chain with **Bounds** + **Remap Numbers** +
   **Gradient** + **Mesh Colours**.)
3. Wire the mesh and `K` into **Mesh Colour Map** (Mite > Util): neon blue is
   negative, white zero, neon red positive — the same colours as the web app.
   `L` is a legend strip standing beside the mesh; wire `Lb` and `Pt` into a
   **Text Tag** to label its ticks (nice numbers inside the domain, 0 always
   among them) and both ends. (Or build your own chain with **Bounds** +
   **Remap Numbers** + **Gradient** + **Mesh Colours**.)
4. Optional: wire the mesh and `K` into **Mesh Isocurves** with level `0` to
   outline the saddle-shaped regions.

Domes show positive values, saddles negative, flat regions zero. **Mean
Curvature** and **Principal Curvature** work the same way; Principal also
outputs the two curvature *directions* per vertex, which you can preview with
**Vector Display**.

Meshes with seams or duplicate vertices (STL imports, meshed Breps) are welded
automatically; per-vertex outputs always line up with the input mesh.

**Umbilics** tells you where direction fields break down. `P` / `I` / `Mk`
flag every vertex whose two curvatures are within `T` of each other (a
region); `U` / `Ix` locate the isolated umbilics themselves, the points the
principal directions turn around, with their index: +½ for the lemons and
monstars of a convex surface, −½ for a star such as the flat point of a
monkey saddle. On a closed mesh the indices add up to 2 (sphere type) or 0
(torus). Try a scaled Mesh Sphere (1.5 × 1 × 0.7): exactly four `U` points in
the plane of the longest and shortest axes, all +½, and Curvature Streamlines
seeded near them show the lemon pattern. Set `Cn` to a few mesh edges when a
nearly spherical mesh reports many ±½ pairs close together — they are facet
noise and cancel.
To see how good the numbers are, drop a **Test Shape** (Mite > Curvature), type
`ellipsoid` (or `torus`, `catenoid`, `monkey`, `cone` …) into `N` and wire its
`M` into Gaussian Curvature: the component's own `K` is the exact value at every
vertex, so **Mesh Colour Map** on `K` from both, or a **Subtraction**, shows the
estimator's error. Its `K1`, `K2`, `D1`, `D2` and `A1`, `A2` are the exact
principal and asymptotic values; `Umbilics` on the ellipsoid finds its four
umbilics where the component's `|K1 − K2|` is zero.

For dirty meshes (booleans, imports, joined pieces) put **Mesh Cleanup**
(Mite > Util) first: it welds, drops degenerate, duplicate and isolated
elements and turns closed meshes outward, so the sign of Mean Curvature is
right. Read its `Report`: if it says triangles are below 1°, set `Sliver` to
`1` to collapse those needles and flip those caps (no vertex moves); edges it
lists on `NonManifold` must be fixed in Rhino.

## 2. Trace a gridshell net (5 minutes)

Use a doubly-curved mesh. For asymptotic nets it must have anticlastic
(saddle-shaped, K < 0) regions — a minimal-surface-like shape is ideal.

**Asymptotic Web** (Mite > Gridshells) — start here for an asymptotic gridshell:

- `M` — your mesh; `S` — a seed point (leave empty: the point nearest the
  bounding-box centre, which is the centre of a symmetric surface)
- `Sp` — the node distance along the two asymptotic curves through the seed.
  That is the only design variable of an asymptotic web (Schling): every other
  node is where the curve through a node of one seed curve crosses the curve
  through a node of the other, so there are no stubs and no T-junctions
- `Sy` — `−1` detects the symmetry: a rotation about the seed normal (a 3-fold
  Enneper surface, a saddle) is traced once and rotated, a surface of
  revolution becomes a rotational web whose meridian diagonals are geodesics
- `G` (AAG) — `True` makes one family of node diagonals geodesic and keeps
  every node star planar: three families of straight, flat slats, two upright
  and one lying on the surface (Schling, Wang, Hoyer & Pottmann 2022). The
  surface moves slightly; the `R` report says how far and how exact the result is
- `Rf` (Refine) — with AAG on, `1` or `2` turns the joint-to-joint chords into
  smooth laths: every edge is split into 2 or 4 and the fine net re-optimised so
  the laths stay asymptotic (upright) and the diagonals geodesic (flat), with the
  joints where AAG put them. `Sf` is the fine surface. Expect the turning per
  step in the report to halve with each level; `2` is about five times slower

`A` and `B` are the upright laths, `G` the flat diagonals, `N` the nodes and `Q`
the net as a quad mesh. A seed on a flat point (the centre of a 3-fold Enneper
surface) is a singular node where six laths meet. Triangulating with `G` is what
makes the grid stiff: on a 10 m Enneper web of 60 × 6 mm timber the deflection
under self-weight and 0.5 kN/m² drops from 1.7 m (quads) to 11 mm (AAG).

**AGG Web** (Mite > Gridshells) — the other hybrid: two families of flat
slats on the surface and one of upright slats (Schling, Wang, Hoyer & Pottmann
2022, §2.3). Feed it a quad net of two lath families and the mesh:

- `M` — the mesh; `A`, `B` — the two lath families, each A crossing each B at
  most once (a **Geodesic Net** traced at ±45° to the asymptotic directions,
  an **Asymptotic Web**'s `A` / `B`, or your own curves)
- `F` — which diagonal family becomes asymptotic: `−1` takes the one whose
  edges lie nearer the surface's tangent planes at the start (right when the
  net was traced on the surface); `0` / `1` to say so yourself
- `It`, `Px`, `Fa` — iterations (40; the last third without fairness),
  proximity to the surface (0.3; lower lets the surface change more), fairness
- `T`, `S` — crossing tolerance and curve sampling, as on Net Topology

`A` and `B` come back as geodesic laths node to node (flat slats), `D` are the
asymptotic diagonals (upright slats), `O` the other diagonals, `Q` the quad
mesh, `Ge` / `As` the largest angle errors in degrees (0 = exact), `Dv` how far
the surface moved; `R` has the numbers before and after. Exact on a cylinder
with helices as laths and the rulings as diagonals; elsewhere expect a
fraction of a degree and a surface move of a percent of the spacing, and a
warning above 1° when no AGG web lies close to the surface with these laths.
**Web Propagation** (Mite > Gridshells) — an AAG web without a surface, from
a strip you draw (Wang, Almaskin & Pottmann 2025):

- `V0`, `V1` — two neighbouring geodesic rows as polylines with the same number
  of vertices (or any curves with `N` nodes each); the second row's vertex j
  sits between vertices j and j+1 of the first — the component detects the
  order and direction and tells you when it reversed them
- `R` — rows to add; `O` — `0` repeats the strip's triangles at every position
  (constant node spacing along the asymptotic laths), `1` makes the new
  triangles equilateral; `F` — a weak fairness along each new row
- `G` (AAG) — `True` runs the global optimisation of Asymptotic Web once more
  on the finished web, as the paper does after propagation

`A` and `B` are the asymptotic laths between the rows (upright strips), `G` the
rows themselves (geodesic, flat strips — the two drawn ones first), `N` the
nodes, `Q` the quad net and `E` the geodesic error of every new row in
degrees (expect ~1e-13: the stars are planar and the rows geodesic to
round-off; a warning names the row where the strip could not continue).
Try it on two neighbouring meridians of a catenoid web from **Asymptotic Web**
(`Sy` = −1 on a catenoid; two adjacent `G` diagonals with the same node count):
the web grows on from the strip and stays an AAG web wherever it goes.

**Asymptotic Net** (Mite > Gridshells) — the older evenly spaced layouts:

- `M` — your mesh
- `A` (AutoSpace) — `True` (the default)
- `Sp` (Spacing) — roughly mesh size / 15, or leave `0` for an automatic value
- `St` (Step) — leave `0`: it is derived from the spacing and mesh
- `An` (MinAngle) — 15°: leave out the band near K = 0 where the two families
  collapse onto each other (unbuildable crossings, family swaps)
- `Ct` (Continuous) — `True` (the default): every curve runs border to border,
  and a curve that merges into its neighbour ends on it (a T-junction) instead
  of floating in mid-surface. Set `False` for classic evenly spaced streamlines
  that stop at 0.4 × Spacing from a neighbour — more even, but laths end
  mid-surface

You get two crossing curve families that only exist where K < 0 (the `K`
output flags those vertices). `A` is always the right-handed family and `B`
the left-handed one: the two asymptotic curves through a point twist in
opposite senses (geodesic torsion +√−K and −√−K), so the colours mean the same
thing on every surface — on both sides of a flat point, on separate saddle
regions, on a hyperboloid (`A` = the rulings that wind like a right-hand
screw). Use that when one family is to be built upright and the other flat. These are the layouts buildable from straight
flat strips held upright. Seeds can be vertex indices (`S`) or points (`P`);
with AutoSpace only the first one matters.

**Geodesic Net** works everywhere (no curvature restriction): give it one seed
(index or point), one direction vector, AutoSpace `True`, and a Spacing. New
geodesics start at the angle that keeps the strip to their neighbour closest
to constant width (Jacobi field), so families stay even on domes and saddles.
`Bd` (FromBorder) grows the family from the border edge nearest the seed
instead — with `Ba` (BorderAngle) that gives the helix layout of a vault.
Where geodesics converge (a dome, radial laths towards an opening) a lath that
meets a neighbour of its family ends on it straight, as a board does: it is cut
at the crossing, never bent sideways onto the neighbour; if it does not reach
the neighbour within two spacings it ends free at the contact. Holes in the
mesh are borders like any other: laths end on an opening's rim and the webs
seed on every rim.
**Geodesic Path** gives the single shortest lath between two points. With
`Me` (Method) 1, the default, it is the exact geodesic of the mesh: a polyline
straight across every face and edge, through every edge crossing (set `S` to
a spacing for a smooth resampled curve). It goes round a cone tip, and through
a saddle vertex only where the geodesic really runs through it — refine the mesh
there if you want the lath to pass beside the vertex. `Me` 0 is the older
curve shortening, kept for comparison.

**Net Trim** is the cookie cutter: Schling's networks are designed on a
surface larger than the building and cropped to the outline. Trace the net on
the big mesh, draw the outline as one or more closed curves on the surface (or
in plan above it, then set `D` to 0,0,−1), and feed curves, mesh and outline
to **Net Trim**: `A` / `B` are the pieces inside, `C` the cut ends (border
ends for Net Topology, Net Joints and Flat Kit), `P` the outline as pulled
onto the mesh. A loop inside another loop cuts a hole, `Iv` keeps the outside
instead (an opening in a net), `In` points choose the regions by hand.

**Chebyshev Net**: seed vertex + direction + `L` (lath joint spacing, try mesh
size / 12). Outputs both lath families and a quad net mesh whose edges all have
length `L` — the flat-lattice kinematics of an elastic gridshell. Every edge is
`L` to round-off (the compass step is solved exactly). On a dome the cells
shear more with every ring away from the seed (Hazzidakis: the curvature a
cell encloses is 2π minus its four angles) until the laths lock; set `Am`
(MinAngle, 20–30°) to stop the net before any crossing falls below that, and
read `R` (Report) for the node count, the shear range and the nodes refused.
length `L` — the flat-lattice kinematics of an elastic gridshell. `An` is the
angle between the two laths at every node (90° = square cell): colour the
nodes by `|An − 90|` and watch for nodes outside 45° … 135°, where wire mesh
locks (Garg et al. 2014) — the web app's *angle map* does exactly this, with a
lock-angle slider. `Kc` is the Gaussian curvature each cell covers, read from
the net's own angles (Hazzidakis: ∬K dA = 2π − Σ corner angles): its sum tells
how much of the 2π a single net can carry the patch has used. In the app,
**Isocurves** now label every curve with its level.

### Layout: web or fill

Every net component has a `Ly` (Layout) input at the bottom. `1` (web from
the border) seeds curves exactly `Sp` apart along the mesh border and traces
each one border to border — no curve ever stops on a neighbour, so every lath
is continuous; `2` does the same from the crossing curve through the seed.
Between seeds the spacing is what the surface dictates: asymptotic curves are
never equidistant except on special surfaces (on a catenoid neighbours
separate as cosh z), and geodesics converge where K > 0. `0` (the evenly
spaced fill) keeps the spacing instead, by inserting curves where it opens and
stopping them where it closes — those ends are T-junctions, which Net Joints
and Gridshell Analysis handle, but the laths are shorter. Use the web for the
structure you will build and the fill when you want a uniform density preview.
A web fills whatever its seed line cannot reach (behind a K = 0 line, a band a
single cross misses) with further seed crosses one spacing from the nearest
curve, and only ends a curve early where it would touch a curve of its own
family — the orange end squares in the web app.

### Modes: right-click the component

Layout, AutoSpace, Continuous, FromBorder, Upright, Shape and the other
switch-like inputs do not need a number typed into them: right-click the
component and pick from its submenus (`Layout: Evenly spaced fill ▸ Web from
the border`). The line under the component always names the active modes —
`Fill · Continuous` on a fresh Asymptotic Net, `Seeds` on a fresh Geodesic
Net (AutoSpace off, so Layout does not apply and is not shown). If the input
is wired to a value list, the menu moves the list; any other wire wins and the
menu says so. **Attach value lists** in the same menu puts a named drop-down
on every unwired mode input when you prefer to see them on the canvas.
`3` (farthest-point fill; Asymptotic Net, Conjugate Net, Curvature
Streamlines) is a second kind of fill: instead of starting each new curve one
spacing beside an existing one, it starts it in the middle of the widest gap
left, and stops when no gap is wider than 1.6 × `Sp`. You get fewer curves of
the same length and fewer T-junctions, and the typical gap is closer to `Sp`
(fill `0` lets curves converge to 0.7–0.8 × `Sp` after starting exactly `Sp`
apart), but the gaps vary more: anything from 0.8 to 1.6 × `Sp` where the
surface spreads the curves. On a torus the meridians come out as 2, 4, 8 …
2^k evenly spread circles. Geodesic Net ignores `3` and uses `0`.
### Guide: Schling's seed curve

The last input of every net component, `Gd` (Guide), takes a curve and
overrides the layout: the curve is pulled onto the mesh (draw it on the
surface, or a diagonal above it, or a plan curve — it is projected to the
closest surface points) and nodes are placed every `Sp` of arc length along
it, one curve of each family through every node, border to border. This is
how Schling lays out asymptotic gridshells: nodes at a repeated distance along
a seed curve, the two families drawn alternately through each other's nodes,
and how the Studio X catalogue places its origin points along a middle curve.
Neighbouring curves are then `Sp × sin(angle to the guide)` apart — the node
distance is what you set, not the lath spacing. A guide drawn along one
family's own curve seeds only the other family (its own curve is traced once).
On Geodesic Net the geodesics leave the guide in the first `D` direction
carried along the guide by parallel transport, so they stay parallel on a
cylinder and fan out round a latitude circle of a dome. Open curves get their
nodes centred; closed curves get L / round(L / Sp) nodes exactly equal apart.
The component reports the node count and spacing as a remark.

## 3. Check buildability (2 minutes)

Wire any net's curves into **Lath Analysis** together with the same mesh:

- `Up` (Upright) — `True` for asymptotic nets, `False` for geodesic nets
- `W` / `T` — strip cross-section in model units
- `E` (MaxStrain) — allowable bending strain; 0.005 suits timber,
  0.002 steel, 0.008 GFRP
- `Sh` / `Sc` (Shape / Section, last inputs) — the same choice as on Lath
  Sweep: `0` rectangle, `1` round bar of diameter `W` (then `Up` does not
  matter: a bar bends and twists the same way round), `2` your own closed
  section curve. The strain check uses that section's real fibre distances.

`B` tells you per lath whether it can be physically bent into place; `U` is the
peak strain utilization (over 1 fails). Graft the per-point `u` tree into a
gradient on the curves to color-code where laths are overstressed.

What the numbers mean (checked against the strip literature, see the header
of `LathAnalysis.cs`): a flat lath bends with `Kn` about its width (fibre
t/2) and with `Kg` about the surface normal (fibre w/2, its strong axis), an
upright lath the other way round; both twist with `Tg`, and on an asymptotic
curve `Tg` = √−K. `Kg` is the lath's own in-surface turning, exact for a
circle however coarsely the curve is sampled; `Kn` and `Tg` come from the
surface normal. A geodesic should read `Kg` ≈ 0 — on a traced closed loop
(a great circle) it does so to 0.03 at unit radius; a loop that reads
`Kg` ≈ 1 with 1.3.2 was the loop closure's zigzag, not the lath.

For the whole network use **Gridshell Analysis**: the same curves, the joints
(crossing points; T-junctions are found automatically), support points and the
section. `St` holds the supports fixed (`0`) or pinned (`1`); `Jk` sets the joints
— `−1` rigid, `0` a free scissor bolt along the normal, or a rotational stiffness
in N·m/rad for a real bolted joint; `ρ` adds self-weight, `q` an area load in
N/m², `Lp`/`F` point loads. The `Rp` report must show the reactions equal to the
load (equilibrium error ≈ 1e-12); laths that no support reaches are listed and
left out instead of making the frame singular. `Ly` Layer (one value per curve,
e.g. `0` for every A curve and `1` for every B curve) stacks the laths: each
family keeps its own axis one lath depth apart at the crossings, joined through
a rigid link, so a horizontal load twists the lower laths through the lever arm
of the stack; `Al` says where layer 0 sits (as in Lath Sweep). Leave `Ly` empty
for the single-node joint. `Rm` gives the reaction moments, and the report adds
the eccentricity with a moment-equilibrium error.

To see the whole spread rather than the worst lath, wire the two families into
**Net Distributions** with the mesh (and, if you like, Lath Analysis `U` into its
`U`). `S` / `P` give the local spacing at every sample (colour the points with
Mesh Colour Map's gradient, or graph them), `X` the crossing angles, `H` / `E` the
histograms for Quick Graph or a bar chart, and `R` a report with n, P5, median,
P95, CV and the share within ±5 % / ±7.5 % of `Sp`. Expect a continuous fill to
sit around 0.8 × Spacing (Spacing is its widest tolerated gap), a web to show the
surface's own spacing, and 90° crossings on minimal surfaces.

The same component reports **buckling**: `λ` is the load factor at which the
loaded net buckles elastically (Euler buckling from the axial forces of the
solve), `λs` the factors of the first `Bk` modes and `Bm` the first mode shape
drawn on the laths (5 % of the net's size, times `Sc`). Read `λ` together with
`Um`: a net with `Um` 0.3 and `λ` 0.8 fails by buckling, not by stress. Below 1
the component warns, below 3 it remarks — gridshells are usually designed for a
linear factor of 3 or more before imperfections and large deflections are
considered, and the factor ignores lateral–torsional effects of upright strips.
The report line also prints the Sturm check ("none missed below λ"): the solver
proves no lower mode was skipped. Set `Bk` to 0 while iterating a dense design;
the eigenvalue solve takes several times the static solve on nets with
thousands of nodes.

Two families with different sections — the asymptotic–geodesic hybrid of
Schling et al., upright asymptotic lamellas with flat geodesic slats threaded
through them: wire the asymptotic family into `C` with `U` = True and the
geodesic family into `Cb` with `Ub` = False (and `Wb` / `Tb` when the slats
have another section). Every lath is then analysed with its own A, I and J, the
outputs list family A's laths first and then B's, and the last line of `Rp`
gives each family's peak utilization. The flat slats are the soft members: on
the catenoid (asymptotic lamellas 100 × 12 upright, meridian slats 100 × 12
flat, 1 kN/m) the net deflects 2.8× more than with every lath upright.

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

### One wire: the Mite Net

Every net component also has a `Nt` (Net) output: the surface and both families
in one value. Plug it into the last input (`Nt`) of Net Joints, Net Topology,
Lath Sweep, Lath Analysis, Gridshell Analysis, Lath Segment, Lath Unroll, Lath
Labels or Net Kinetics and leave their Mesh / Curves inputs empty. Put a
**Mite Net** (Mite > Gridshells) in between to give the net its lath profile
once — `W`, `T`, `U`, `O`, `Sh`, `Sc`, and `Ub` to lay family B differently
(asymptotic A upright, geodesic B flat) — and every downstream component uses
it. Mite Net also joins two nets (`Nb`: its family A becomes family B, e.g. two
Geodesic Nets) and sorts the lath ends: `Eb` on the border, `El` on another
lath (T-junction, orange in the viewport), `Ef` free (red). Anything you wire
next to a Net overrides that part of it. With a Net, Gridshell Analysis needs
no Joints or Supports (every contact is coupled, every border end supported).
For the asymptotic–geodesic hybrid give family B its own profile: `Wb`, `Tb`,
`Ub` = False and `Ob` (appended; unset means "as A"). An upright A crossed by a
flat B gets a *threaded* joint instead of a lap: `Na` is a slot through the
lamella — B's footprint, as deep as the normal interval the two sections share,
open on the lamella's bottom edge when B lies on the surface (a 12 mm slat on a
100 × 12 lamella: a 12 mm slot plus a 5 mm bleed), a mortise through the middle
when A is centred (`Al` = 1) — and `Nb` is null, because the slat is not cut:
it slides through the lamellas during the kinetic erection and is bolted
(Schling et al. 2022 §4.2). `L` has no effect on threaded joints. If the two
families do not overlap along the normal (B lifted above A), the component
warns and cuts nothing: lower `Ob`, or connect them with a block and a bolt.

## 5. From model to workshop (5 minutes)

- **Lath Segment** — splits laths longer than your stock (`St`), keeping cuts
  `Ma` away from joints. Consecutive pieces overlap by `SL` and the splice
  notch solids (`Ne` for the upstream piece, `Ns` for the downstream one) cut
  a half-lap splice into that overlap. Outputs are one branch per lath.
- **Lath Unroll** — flat cutting patterns per lath, laid out in a row (`G`
  gap). Export the `P` curves for CNC/laser.
- **Lath Strip** — the strip a thin straight plank (sheet steel, GFRP, plywood
  lamella) forms along each lath: wire the same Mesh and curves, `W` the plank
  width, `U` True for upright asymptotic laths. `S` is the 3D strip and `R` its
  rulings; `P` the flat pattern, a straight band where the strip is
  developable. `D` says on which share of each lath that is true: an asymptotic
  lath that inflects (e.g. crossing a catenoid's waist) or runs straight must
  twist there instead, and `Es` gives the edge strain that twist costs the
  plank. Thick timber laths twist as rods: check those with Lath Analysis.
- **Lath Labels** — IDs (`L000`, `L001`, ...; set the prefix and start number
  per family), midpoint tag anchors, and a CSV bill of materials with lengths
  (and utilization if you wire Lath Analysis in).
- **Lath Preview** — colors the swept laths by utilization: green OK, neon
  yellow close to the limit, neon red over (the web app's colours).
- **Net Topology** — nodes and members of the net as a graph, for a structural
  package or a node schedule. Its `Me` output is the panel mesh of the net (one
  face per panel between the laths) and `Pl` / `Dv` say how skew each panel is
  (`Pl` is the distance between a quad's diagonals over its mean diagonal; above
  0.01 it is visibly skew). Planar quads need a conjugate net: a **Conjugate
  Net** (curvature lines) gives nearly planar panels, an **Asymptotic Net** the
  skewest ones a surface has, so **Planarize Mesh** on `Me` barely moves the
  first and cannot flatten the second without leaving the surface. Colour `Me`
  by `Pl` with **Mesh Colour Map** to see where glass or sheet panels would fit.

Optionally, check the whole network structurally with **Gridshell Analysis**
(Mite > Analysis): supports at the boundary, a downward `L` load in N/m, and it
reports deflections, per-element and per-lath stress utilization (`Ul` goes
straight into **Lath Preview**). Geometry is converted to metres internally,
so `E`, `Al` (Pa) and `L` (N/m) are always SI whatever your model units. The
beams take their area, inertia and torsion constant from the same `Sh` / `Sc`
section as Lath Sweep and Lath Analysis, so one profile feeds the model, the
strain check and the statics.

## 6. Make it move (kinetic asymptotic nets)

**Net Kinetics** (Mite > Kinetics) treats the crossings of an asymptotic net
as scissor hinges and moves the net as a mechanism, after Schikore, Schling,
Oberbichler & Bauer (2020) and Wan, Crolla & Schling (2025): joint spacing
along every lath stays constant, every lath segment stays perpendicular to the
moving surface normal (so upright laths remain straight-unrollable) and the
laths keep their rest bend with the given `K` stiffness. Wire the `A` / `B`
curves of an **Asymptotic Net**, then drive it:

- `F` fixed points hold the nearest joints; `Sl` ground points let joints slide
  in the plane through their rest position (normal `Sn`, default Z);
- `Mv` / `To` move the nearest joints to targets; `C` cables (lines between two
  joints) shorten or lengthen to `Le` at `Fo` = 1;
- (Lath Sweep, Net Joints and Lath Segment take `Al` Align: 0 the lath grows
  from the surface out, 1 centred on it, 2 grows in.)
- **Flat Kit** (Mite > Fabrication) takes the same `A` / `B` (and the web's
  seed as `P`) and gives the kit to build it: the flat assembly with joints
  numbered, the strips laid out on sheets with their slots or holes, the
  deployment states and a cut list; set `F` to a folder and `Wr` = True for
  the SVG / DXF / CSV files. `W` / `T` are the strip width and sheet thickness
  in model units; the files are in millimetres. The strips are nested on the
  `Sw` × `Sh` sheets first-fit decreasing (the fewest shelves; `R` reports the
  sheets, the material use and the area bound no nesting can beat). A strip
  longer than the sheet lies along the sheet's diagonal when it fits there and
  that costs no sheet; otherwise it is split into pieces (`A3/1`, `A3/2` …)
  lapped over `Sp` mm with two pin holes each, the lap placed between two
  joints (never under a slot, where the doubled thickness would not pass);
  `Sp` = 0 picks three strip widths or what the joint pitch allows, `Sp` < 0
  keeps such strips whole on a drawing of their own, for a longer bed.
  in model units; the files are in millimetres. `Hb` HubPlates (appended) puts
  a laser-cut plate at the hub where the six rays of a flat point end: 1 one
  plate flush with the inner edge, 2 two plates bolted through the centre; the
  rays stop short of the centre, get a notch for the plate, and the plate — a
  disc with a radial slot per strip, every slot labelled — comes on its own
  drawing and as the `Hp` output in place on the flat assembly. `Hr` sets its
  radius (0 = automatic). The report says how far the hub's angles move while
  deploying (a rigid plate needs that small: 6e-6° on the Enneper web).
- `Fl` Flatten = True presses the net flat: the first `F` joint (or the one
  nearest the centre) is held and every lath end slides onto the plane through
  it (normal `Sn`). On an **Asymptotic Web** of the 3-fold Enneper, with `F` on
  the seed, the net lays flat into a hexagon — the flat grid you assemble; read
  `SA` / `SB` from the last state back to the first for the deployment.
- `Dp` Deploy = 1 deploys the flat kit on a table: every lath end rests on a
  one-sided ground (it may lift off, not sink) and every scissor is opened
  towards `An` (90° for a web on a minimal surface). With `Fl` on as well the
  net is pressed flat first and deployed from there, so `SA` / `SB` are the
  deployment and the last state should be the design again (the 3-fold Enneper
  web comes back within a tenth of the joint spacing). `Kk` kicks the flat start
  along the net's softest mode by that fraction of the joint spacing — the flat
  state is a bifurcation and the solver cannot leave the plane without it; its
  sign picks the branch (the design or its mirror image). `Dp` = 2 puts the net
  on the table and lets your `Mv` / `To` points and cables do the driving:
  pulling the singular node alone does not deploy a grid (the report says how
  far it got and why).

Put `Fo` on a slider (0 = the settled rest state, 1 = drivers at their
targets) to animate; `St` states are solved in sequence and come out as trees
`SA` / `SB`. `Dr` (joint-spacing drift), `Dv` (degrees off asymptotic) and `Ms`
(driver miss) tell you whether the net can follow: the exact doubly ruled grids
(a hyperboloid of straight rods) follow to 1e-12, a traced catenoid net follows
a 20 % top-ring pull with drift 3e-4 and 0.05° deviation, a net with its whole
bottom ring fixed cannot move and reports a large `Ms`.

Every state is also checked elastically with the lath section (`U` / `W` / `T`
or `Sh` / `Sc`, as on Lath Analysis; `E` strain limit, `Em` Young's modulus,
`Rho` density for self-weight): `U` is the peak utilization per state, `Ul`
the per-lath utilization (last branch → **Lath Preview**), `En` the strain
energy Π = ½∫(GJτ² + EIκ²) ds per state (Schikore et al.'s curvature-square
diagram — plot it against `Fs`), `Nf` the fold of least energy, i.e. the
state the unactuated grid settles into, and `Ok` says whether the whole
motion stays within the limit. On the catenoid the upright laths sit at
utilization 1.37 from twist alone at rest (√−K·t/√3 at the throat) and relax
as the ring closes, so the natural state is the closed one and the cables
would hold it open. A lath has to survive every state it passes through, not
only the ends: the Report's "Through the motion" line counts the laths that stay
within `E` in every state and names the worst one (its index in `Ul`), the fold
where it peaks and whether bending (kn or kg) or twist governs — to see it per
lath, run `Ul` through Flip Matrix and Bounds (or open the web
app's Kinetics block, which colours the laths by their strain in the state on
the slider or by that envelope). Keep `Sg` at 0 (joints only) for interactive use; a value
subdivides the laths so they can bend between joints, at several times the
solve time (the twist energy does not change: a lath twists uniformly between
hinges).

## Reviewing a release on the Mite Bench

The bench page (`tools/webgen`, see its README) runs every component on
analytic shapes and shows the result. To review it quickly: type part of a
name or an input in **Find a component** (or press `/`), pick **Failing** to
see only components whose self-test fails, then walk the cards with `j` / `k`,
tick with `x` and write a note with `n` (`?` lists the keys; they can be
switched off there). **Open as GitHub issue** turns the ticks and notes into a
new issue on the Mite repository.

## Units

All lengths (Spacing, Step, EdgeLength, Width, Thickness, Offset, Margin,
StockLength, SpliceLength, Sampling, MaxSegment, Gap, Clearance) are in model
units. Tracing and sampling parameters default to `0` = automatic, derived
from the mesh size, so they work at any scale; the fixed defaults of the
fabrication inputs (Width 0.1, Thickness 0.01, StockLength 3) assume
metre-scale models — scale them up ~1000x if you model in millimetres.

If the numbers do not fit the model, the component says so with the value to
use: Width 80 on laths a few metres long reads "if you meant millimetres,
enter Width 0.08 and Thickness 0.012"; a Spacing below the mesh edge names
the edge and how many subdivisions would make it fit; a MinAngle that leaves
out the whole saddle names the angles at which the families actually cross.
Hover the orange or grey balloon on the component to read the message.

## Form finding

- **Planarize Mesh** makes the faces of a quad-dominant mesh planar. Wire the
  design surface into `R` so the nodes stay on it (without it the input mesh is
  the reference) and read `Mv` / `Ds`: how far nodes moved and how far off the
  surface they are. A conjugate or curvature-line net (Conjugate Net → Net
  Topology → `Me`) planarizes in about ten iterations and stays on the surface;
  an asymptotic net cannot — the component warns that no planar mesh exists
  nearby, or reports a planar mesh well off the surface. `Mt` 0 is the old
  per-face projection, kept for comparison.
- **Minimal Surface** and **Force Density Method** fix the mesh boundary when
  `F` is left empty. Force densities `Q` follow the `E` (Edges) output order.
- **Dynamic Relaxation** hangs a net under `G` (gravity) or point loads, or
  relaxes it as a soap film (`Te`); `R` < 1 pre-tensions the edges, `R` = 0
  turns the springs into force densities (then it agrees with FDM). With
  `B` > 0 (bending stiffness EI) the rows and columns of a quad mesh become
  elastic laths: give `R0` the cut length of every edge (`E` order) and a
  lath longer than its chord buckles into an elastica — the way to form-find
  an elastic gridshell from its flat grid. `La` returns the laths, `K` their
  curvature 1/R per node (moment = `B` × `K`).
