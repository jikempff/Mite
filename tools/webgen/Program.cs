using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Dynamics;
using Mite.Core.Fabrication;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Tests;

var scenes = new Dictionary<string, object>();
var numbers = new Dictionary<string, object>();
double R3(double v) => Math.Round(v, 4);
double[] P(Vec3d v) => new[] { R3(v.X), R3(v.Y), R3(v.Z) };
double[][] Line(Vec3d[] l, int stride = 1) { var o = new List<double[]>(); for (int i = 0; i < l.Length; i += stride) o.Add(P(l[i])); if ((l.Length - 1) % stride != 0) o.Add(P(l[^1])); return o.ToArray(); }
double[][][] Edges(MeshData m, int every = 1)
{
    var e = m.BuildEdges(); var o = new List<double[][]>();
    for (int i = 0; i < e.Length; i += every) o.Add(new[] { P(m.Vertices[e[i].v0]), P(m.Vertices[e[i].v1]) });
    return o.ToArray();
}
double[][][] GridWire(MeshData grid, int nx, int ny, int every)
{
    // iso-lines of a (nx+1)x(ny+1) grid, every k-th row/col
    var o = new List<double[][]>();
    for (int j = 0; j <= ny; j += every) { var l = new List<double[]>(); for (int i = 0; i <= nx; i++) l.Add(P(grid.Vertices[j * (nx + 1) + i])); o.Add(l.ToArray()); }
    for (int i = 0; i <= nx; i += every) { var l = new List<double[]>(); for (int j = 0; j <= ny; j++) l.Add(P(grid.Vertices[j * (nx + 1) + i])); o.Add(l.ToArray()); }
    return o.ToArray();
}
double ArcLen(Vec3d[] l) { double s = 0; for (int i = 1; i < l.Length; i++) s += (l[i] - l[i - 1]).Length; return s; }
double[][][] SaddleWire(int div, double size, int every)
{
    var o = new List<double[][]>();
    for (int j = 0; j <= div; j += every) { var l = new List<double[]>(); for (int i = 0; i <= div; i++) { double x = size * (i / (double)div - 0.5), y = size * (j / (double)div - 0.5); l.Add(new[] { R3(x), R3(y), R3(x * x - y * y) }); } o.Add(l.ToArray()); }
    for (int i = 0; i <= div; i += every) { var l = new List<double[]>(); for (int j = 0; j <= div; j++) { double x = size * (i / (double)div - 0.5), y = size * (j / (double)div - 0.5); l.Add(new[] { R3(x), R3(y), R3(x * x - y * y) }); } o.Add(l.ToArray()); }
    return o.ToArray();
}
double[][][] TorusWire(double R, double r, int maj, int min, int everyMaj, int everyMin)
{
    var o = new List<double[][]>();
    for (int i = 0; i < maj; i += everyMaj) { var l = new List<double[]>(); for (int j = 0; j <= min; j++) { double th = 2 * Math.PI * i / maj, ph = 2 * Math.PI * j / min; l.Add(new[] { R3((R + r * Math.Cos(ph)) * Math.Cos(th)), R3((R + r * Math.Cos(ph)) * Math.Sin(th)), R3(r * Math.Sin(ph)) }); } o.Add(l.ToArray()); }
    for (int j = 0; j < min; j += everyMin) { var l = new List<double[]>(); for (int i = 0; i <= maj; i++) { double th = 2 * Math.PI * i / maj, ph = 2 * Math.PI * j / min; l.Add(new[] { R3((R + r * Math.Cos(ph)) * Math.Cos(th)), R3((R + r * Math.Cos(ph)) * Math.Sin(th)), R3(r * Math.Sin(ph)) }); } o.Add(l.ToArray()); }
    return o.ToArray();
}
double[][][] SphereWire(int n)
{
    var o = new List<double[][]>();
    for (int k = 1; k < n; k++) { double th = Math.PI * k / n; var l = new List<double[]>(); for (int i = 0; i <= 48; i++) { double ph = 2 * Math.PI * i / 48; l.Add(new[] { R3(Math.Sin(th) * Math.Cos(ph)), R3(Math.Sin(th) * Math.Sin(ph)), R3(Math.Cos(th)) }); } o.Add(l.ToArray()); }
    for (int k = 0; k < n; k++) { double ph = Math.PI * k / n; var l = new List<double[]>(); for (int i = 0; i <= 48; i++) { double th = 2 * Math.PI * i / 48; l.Add(new[] { R3(Math.Sin(th) * Math.Cos(ph)), R3(Math.Sin(th) * Math.Sin(ph)), R3(Math.Cos(th)) }); } o.Add(l.ToArray()); }
    return o.ToArray();
}

double[][][] RevolveWire(Func<double, double> radiusAt, double zMin, double zMax, int rings, int meridians)
{
    // parallels and meridians of a surface of revolution r = radiusAt(z)
    var o = new List<double[][]>();
    for (int k = 0; k <= rings; k++) { double z = zMin + (zMax - zMin) * k / rings, r = radiusAt(z); var l = new List<double[]>(); for (int i = 0; i <= 64; i++) { double th = 2 * Math.PI * i / 64; l.Add(new[] { R3(r * Math.Cos(th)), R3(r * Math.Sin(th)), R3(z) }); } o.Add(l.ToArray()); }
    for (int k = 0; k < meridians; k++) { double th = 2 * Math.PI * k / meridians; var l = new List<double[]>(); for (int i = 0; i <= 24; i++) { double z = zMin + (zMax - zMin) * i / 24, r = radiusAt(z); l.Add(new[] { R3(r * Math.Cos(th)), R3(r * Math.Sin(th)), R3(z) }); } o.Add(l.ToArray()); }
    return o.ToArray();
}
double ChordDev(Vec3d[] l) { var a = l[0]; var d = l[^1] - l[0]; double L = d.Length; if (L < 1e-15) return 0; d = d / L; double w = 0; foreach (var p in l) { var v = p - a; w = Math.Max(w, (v - Vec3d.Dot(v, d) * d).Length); } return w; }
double AngleDeg(Vec3d a, Vec3d b) => Math.Acos(Math.Min(1.0, Math.Abs(Vec3d.Dot(a.Normalized(), b.Normalized())))) * 180 / Math.PI;

string OutDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var sw = Stopwatch.StartNew();

// ---------- Curvature on torus (K, H, k1, k2) ----------
{
    var torus = TestMeshes.CreateTorus(3.0, 1.0, 64, 32);
    var pc = PrincipalCurvature.Compute(torus);
    var K = GaussianCurvature.Compute(torus);
    var H = MeanCurvature.Compute(torus).Values;
    // sample along a meridian (i = 0): phi from 0..2pi, compare with theory K = cos(phi)/(r(R + r cos phi)), H = (R + 2 r cos phi)/(2 r (R + r cos phi))
    var rows = new List<object>();
    for (int j = 0; j < 32; j += 2)
    {
        double phi = 2 * Math.PI * j / 32;
        double Kt = Math.Cos(phi) / (1.0 * (3.0 + Math.Cos(phi)));
        double Ht = (3.0 + 2 * Math.Cos(phi)) / (2 * (3.0 + Math.Cos(phi)));
        int vi = 0 * 32 + j;
        rows.Add(new { phi = R3(phi * 180 / Math.PI), K = R3(K[vi]), Kt = R3(Kt), H = R3(H[vi]), Ht = R3(Ht), k1 = R3(pc.K1[vi]), k2 = R3(pc.K2[vi]) });
    }
    numbers["torusCurvature"] = rows;
    // per-vertex K for colour map scene (torus 64x32 = 2048 verts, quads)
    var verts = torus.Vertices.Select(P).ToArray();
    var faces = new List<int[]>();
    for (int i = 0; i < 64; i++) for (int j = 0; j < 32; j++) { int a = i * 32 + j, b = ((i + 1) % 64) * 32 + j, c = ((i + 1) % 64) * 32 + (j + 1) % 32, d = i * 32 + (j + 1) % 32; faces.Add(new[] { a, b, c, d }); }
    scenes["torusK"] = new { verts, faces = faces.ToArray(), values = K.Select(R3).ToArray(), valuesH = H.Select(R3).ToArray() };
    // isocurves K = 0
    var iso = MeshIsocurves.Compute(torus, K, new[] { 0.0 })[0];
    scenes["torusIso"] = new { wire = TorusWire(3, 1, 64, 32, 4, 4), iso = iso.Select(l => Line(l, 2)).ToArray() };
    numbers["torusIso"] = iso.Select(l => new { pts = l.Length, len = R3(ArcLen(l)), closed = (l[0] - l[^1]).Length < 1e-9 }).ToArray();
    // streamlines: one meridian and one parallel
    var sl1 = Mite.Core.Streamlines.CurvatureStreamlines.Trace(torus, new[] { 5 * 32 + 3 }, pc, new Mite.Core.Streamlines.CurvatureStreamlines.Options { UseMaxCurvature = true });
    var sl2 = Mite.Core.Streamlines.CurvatureStreamlines.Trace(torus, new[] { 5 * 32 + 3 }, pc, new Mite.Core.Streamlines.CurvatureStreamlines.Options { UseMaxCurvature = false });
    var slA = EvenlySpacedNet.TraceField(torus, pc.D1, null, -1, new EvenlySpacedNet.Options { Spacing = 0.6 });
    scenes["torusStream"] = new { wire = TorusWire(3, 1, 64, 32, 8, 8), max = slA.Select(l => Line(l, 3)).ToArray(), min = sl2.Select(l => Line(l, 3)).ToArray() };
    numbers["torusStream"] = new { meridianLen = R3(ArcLen(sl1[0])), meridianTheory = R3(2 * Math.PI), parallelLen = R3(ArcLen(sl2[0])), autoCount = slA.Count };
    // lath analysis along the meridian streamline (flat strip 0.1 x 0.01)
    var proj = new MeshProjection(torus);
    var la = LathAnalysis.Analyze(proj, sl1[0], new LathAnalysis.Options { Width = 0.1, Thickness = 0.01, MaxStrain = 0.005, Upright = false });
    numbers["lathAnalysis"] = new { kn = la.NormalCurvature.Skip(5).Take(la.NormalCurvature.Length - 10).Select(Math.Abs).Average(), kg = la.GeodesicCurvature.Skip(5).Take(la.GeodesicCurvature.Length - 10).Select(Math.Abs).Average(), tg = la.GeodesicTorsion.Select(Math.Abs).Max(), util = R3(la.MaxUtilization), buildable = la.Buildable, theoryUtil = R3(1.0 * 0.005 / 0.005) };
    scenes["lathUtil"] = new { util = la.Utilization.Where((u, i) => i % 3 == 0).Select(R3).ToArray() };
}

// ---------- Sphere: principal curvature accuracy, umbilics, geodesics, geodesic path, chebyshev ----------
{
    var sphere = TestMeshes.CreateUnitSphere(24);
    var pc = PrincipalCurvature.Compute(sphere);
    var K = GaussianCurvature.Compute(sphere); var H = MeanCurvature.Compute(sphere).Values;
    numbers["sphere"] = new { k1 = R3(pc.K1.Average()), k2 = R3(pc.K2.Average()), K = R3(K.Average()), H = R3(H.Average()), umbilics = Umbilics.Find(pc, 0.05).Length, vertices = sphere.VertexCount };
    var geoSphere = EvenlySpacedNet.TraceGeodesics(sphere, 1 + 12 * 48 + 5, new Vec3d(0, 1, 0), new EvenlySpacedNet.Options { Spacing = 0.35 });
    numbers["sphereGeoClosed"] = new { count = geoSphere.Count, closed = geoSphere.Count(l => (l[0] - l[^1]).Length < 1e-9), firstLen = R3(ArcLen(geoSphere[0])) };
    var proj = new MeshProjection(sphere);
    var a = new Vec3d(1, 0, 0); var b = new Vec3d(-0.5, 0.5, 0.7071).Normalized();
    var sp = ShortestPath.Compute(proj, a, b)!.Value;
    var vp = ShortestPath.Dijkstra(sphere, proj.NearestVertexGlobal(a), proj.NearestVertexGlobal(b))!;
    scenes["geoPath"] = new { wire = SphereWire(8), dijkstra = Line(vp.Select(v => sphere.Vertices[v]).ToArray()), path = Line(sp.Points), ends = new[] { P(a), P(b) } };
    numbers["geoPath"] = new { length = R3(sp.Length), theory = R3(Math.Acos(Vec3d.Dot(a, b))), dijkstraLen = R3(ArcLen(vp.Select(v => sphere.Vertices[v]).ToArray())) };
    var cheb = ChebyshevNet.Compute(sphere, 1 + 12 * 48 + 5, new Vec3d(0, 1, 0), new ChebyshevNet.Options { EdgeLength = 0.2, CountU = 5, CountV = 5, Angle = Math.PI / 2 });
    int nu = cheb.Points.GetLength(0), nv = cheb.Points.GetLength(1);
    var u = new List<double[][]>(); var v = new List<double[][]>(); var edgeErr = new List<double>();
    for (int i = 0; i < nu; i++) { var l = new List<double[]>(); for (int j = 0; j < nv; j++) if (cheb.Valid[i, j]) l.Add(P(cheb.Points[i, j])); if (l.Count > 1) u.Add(l.ToArray()); }
    for (int j = 0; j < nv; j++) { var l = new List<double[]>(); for (int i = 0; i < nu; i++) if (cheb.Valid[i, j]) l.Add(P(cheb.Points[i, j])); if (l.Count > 1) v.Add(l.ToArray()); }
    for (int i = 0; i + 1 < nu; i++) for (int j = 0; j < nv; j++) if (cheb.Valid[i, j] && cheb.Valid[i + 1, j]) edgeErr.Add(Math.Abs((cheb.Points[i + 1, j] - cheb.Points[i, j]).Length - 0.2) / 0.2);
    scenes["cheb"] = new { wire = SphereWire(8), u = u.ToArray(), v = v.ToArray() };
    var shear = new List<double>();
    for (int i = 0; i + 1 < nu; i++) for (int j = 0; j + 1 < nv; j++) if (cheb.Valid[i, j] && cheb.Valid[i + 1, j] && cheb.Valid[i, j + 1]) { var e1 = (cheb.Points[i + 1, j] - cheb.Points[i, j]).Normalized(); var e2 = (cheb.Points[i, j + 1] - cheb.Points[i, j]).Normalized(); shear.Add(Math.Acos(Math.Abs(Vec3d.Dot(e1, e2))) * 180 / Math.PI); }
    numbers["cheb"] = new { nodes = u.Sum(l => l.Length), valid = u.Sum(l => l.Length), total = nu * nv, maxEdgeErr = R3(edgeErr.Max() * 100), meanEdgeErr = R3(edgeErr.Average() * 100), minAngle = R3(shear.Min()), maxAngle = R3(shear.Max()) };
}

// ---------- Saddle: asymptotic net ----------
{
    var saddle = TestMeshes.CreateSaddle(40, 2.0);
    var pc = PrincipalCurvature.Compute(saddle);
    var field = AsymptoticCurves.ComputeDirections(pc);
    var opts = new EvenlySpacedNet.Options { Spacing = 0.15 };
    var t = Stopwatch.StartNew();
    var famA = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts, field.Family2);
    var optsB = new EvenlySpacedNet.Options { Spacing = 0.15 };
    var famB = EvenlySpacedNet.TraceField(saddle, field.Family2, field.Exists, -1, optsB, field.Family1);
    t.Stop();
    var proj = new MeshProjection(saddle);
    int wrong = 0, total = 0;
    foreach (var c in famA) for (int i = 1; i < c.Length; i += 5) { var tg = (c[i] - c[i - 1]).Normalized(); int vi = proj.NearestVertexGlobal(c[i]); if (!field.Exists[vi]) continue; total++; if (Math.Abs(Vec3d.Dot(tg, field.Family2[vi])) > Math.Abs(Vec3d.Dot(tg, field.Family1[vi])) + 0.2) wrong++; }
    scenes["asym"] = new { wire = SaddleWire(40, 2.0, 5), a = famA.Select(l => Line(l, 3)).ToArray(), b = famB.Select(l => Line(l, 3)).ToArray() };
    var endsA = Ends(famA.Concat(famB).ToList(), proj);
    numbers["asym"] = new { a = famA.Count, b = famB.Count, minLen = R3(Math.Min(famA.Min(ArcLen), famB.Min(ArcLen))), maxLen = R3(Math.Max(famA.Max(ArcLen), famB.Max(ArcLen))), wrongFamily = wrong, samples = total, ms = t.ElapsedMilliseconds, step = R3(opts.ResolvedStepSize), spacing = 0.15, ends = endsA.ends, boundaryEnds = endsA.boundary, tEnds = endsA.onCurve, floatingEnds = endsA.floating, maxEndTurn = R3(endsA.endTurn), maxTurn = R3(endsA.turn) };
    // classic (non-continuous) mode for comparison
    var famA2 = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Continuous = false }, field.Family2);
    var famB2 = EvenlySpacedNet.TraceField(saddle, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Continuous = false }, field.Family1);
    var endsA2 = Ends(famA2.Concat(famB2).ToList(), proj);
    numbers["asymClassic"] = new { a = famA2.Count, b = famB2.Count, ends = endsA2.ends, boundaryEnds = endsA2.boundary, tEnds = endsA2.onCurve, floatingEnds = endsA2.floating };
    // conjugate net on saddle
    var conj = ConjugateNet.Trace(saddle, -1, new EvenlySpacedNet.Options { Spacing = 0.2 });
    scenes["conj"] = new { wire = SaddleWire(40, 2.0, 5), a = conj.FamilyA.Select(l => Line(l, 3)).ToArray(), b = conj.FamilyB.Select(l => Line(l, 3)).ToArray() };
    var endsC = Ends(conj.FamilyA.Concat(conj.FamilyB).ToList(), proj);
    numbers["conj"] = new { a = conj.FamilyA.Count, b = conj.FamilyB.Count, ends = endsC.ends, boundaryEnds = endsC.boundary, tEnds = endsC.onCurve, floatingEnds = endsC.floating, maxEndTurn = R3(endsC.endTurn) };
    // geodesic net on the saddle
    var geoOpts = new EvenlySpacedNet.Options { Spacing = 0.2 };
    var geo = EvenlySpacedNet.TraceGeodesics(saddle, 20 * 41 + 20, new Vec3d(1, 0.35, 0), geoOpts);
    var endsG = Ends(geo, proj);
    scenes["sphereGeo"] = new { wire = SaddleWire(40, 2.0, 5), curves = geo.Select(l => Line(l, 3)).ToArray() };
    numbers["sphereGeo"] = new { count = geo.Count, lengths = geo.Select(l => R3(ArcLen(l))).OrderByDescending(x => x).Take(5).ToArray(), ends = endsG.ends, boundaryEnds = endsG.boundary, tEnds = endsG.onCurve, floatingEnds = endsG.floating, maxEndTurn = R3(endsG.endTurn), minLen = R3(geo.Min(ArcLen)), spacing = 0.2 };
    // Net joints + topology on the asymptotic net
    var xs = NetIntersections.Find(famA, famB);
    var xsAll = NetIntersections.FindAll(famA, famB);
    var topo = NetTopology.Build(famA, famB, xsAll, 0.0);
    var angles = new List<double>();
    foreach (var x in xs) { var ta = NetIntersections.TangentAt(famA, x, false); var tb = NetIntersections.TangentAt(famB, x, true); angles.Add(Math.Acos(Math.Abs(Vec3d.Dot(ta, tb))) * 180 / Math.PI); }
    scenes["joints"] = new { a = famA.Select(l => Line(l, 3)).ToArray(), b = famB.Select(l => Line(l, 3)).ToArray(), nodes = xs.Select(x => P(x.Point)).ToArray() };
    numbers["joints"] = new { crossings = xs.Count, contacts = xsAll.Count, tJunctions = xsAll.Count(x => x.IsTJunction), maxGap = R3(xs.Max(x => x.Gap)), minAngle = R3(angles.Min()), maxAngle = R3(angles.Max()), members = topo.Members.Count, nodes = topo.Nodes.Length, valence4 = topo.Valence.Count(vv => vv == 4), tails = topo.Members.Count(m => m.NodeStart < 0 || m.NodeEnd < 0), boundaryEnds = endsA.boundary };
    // lath sweep of one asymptotic curve, upright
    var longest = famA.OrderByDescending(ArcLen).First();
    var swp = StripSweep.Sweep(proj, longest, new LathProfile(0.08, 0.012, true))!.Value;
    double vol = 0; foreach (var f in swp.Mesh.Faces) for (int i = 1; i + 1 < f.Length; i++) vol += Vec3d.Dot(swp.Mesh.Vertices[f[0]], Vec3d.Cross(swp.Mesh.Vertices[f[i]], swp.Mesh.Vertices[f[i + 1]])) / 6;
    scenes["sweep"] = new { wire = SaddleWire(40, 2.0, 5), mesh = new { verts = swp.Mesh.Vertices.Select(P).ToArray(), faces = swp.Mesh.Faces }, center = Line(longest, 2) };
    numbers["sweep"] = new { stations = swp.Centers.Length, faces = swp.Mesh.FaceCount, volume = vol, expected = R3(0.08 * 0.012 * ArcLen(longest)) };
    // unroll the same lath
    var un = StripUnroll.Unroll(proj, longest, new LathProfile(0.08, 0.012, true))!.Value;
    scenes["unroll"] = new { a = Line(un.EdgeA), b = Line(un.EdgeB), c = Line(un.Centerline) };
    numbers["unroll"] = new { length3d = R3(ArcLen(longest)), length2d = R3(ArcLen(un.Centerline)), width = un.Width, maxBow = R3(un.Centerline.Max(p => p.Y) - un.Centerline.Min(p => p.Y)) };
    // segmentation of the same lath with the crossings on it
    var jointArcs = LathSegmentation.JointArcLengths(longest, xs.Select(x => x.Point).ToList(), 0.08);
    var seg = LathSegmentation.Segment(longest, 1.2, 0.08, jointArcs, 0.2);
    var arc = new double[longest.Length]; for (int i = 1; i < arc.Length; i++) arc[i] = arc[i - 1] + (longest[i] - longest[i - 1]).Length;
    scenes["segment"] = new { total = R3(arc[^1]), joints = jointArcs.Select(R3).ToArray(), cuts = seg.CutArcLengths.Select(R3).ToArray(), pieces = seg.Segments.Select(s => { double s0 = 0; var a0 = s[0]; int k = Array.FindIndex(longest, p => (p - a0).Length < 1e-9); return new[] { R3(ArcAt(longest, arc, s[0])), R3(ArcAt(longest, arc, s[^1])) }; }).ToArray(), stock = 1.2, margin = 0.08, splice = 0.2 };
    numbers["segment"] = new { pieces = seg.Segments.Count, cuts = seg.CutArcLengths.Length, minJointDist = R3(seg.CutArcLengths.Length == 0 ? 0 : seg.CutArcLengths.Min(c => jointArcs.Length == 0 ? 99 : jointArcs.Min(j => Math.Abs(j - c)))), maxPiece = R3(seg.Segments.Max(ArcLen)) };
    // Gridshell analysis of the asymptotic net: supports at curve ends, 1 kN/m
    var laths = famA.Concat(famB).Select(l => Sub(l, 0.05)).ToList();
    var sup = new List<Vec3d>(); foreach (var l in laths) foreach (var e in new[] { l[0], l[^1] }) { var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e)); if (proj.IsOnBoundary(h, 1e-4)) sup.Add(e); }
    var t2 = Stopwatch.StartNew();
    var fr = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, new LathProfile(0.08, 0.012, true), new Vec3d(0, 0, -1000));
    t2.Stop();
    var defl = new List<double[][]>();
    var nodeOf = new Dictionary<(int, int), int>();
    for (int n = 0; n < fr.NodeMap.Length; n++) foreach (var (c, i) in fr.NodeMap[n]) nodeOf.TryAdd((c, i), n);
    for (int c = 0; c < laths.Count; c++) { var l = new List<double[]>(); for (int i = 0; i < laths[c].Length; i++) { var d = nodeOf.TryGetValue((c, i), out int n) ? fr.Displacements[n] : Vec3d.Zero; l.Add(P(laths[c][i] + 100 * d)); } defl.Add(l.ToArray()); }
    scenes["frame"] = new { original = laths.Select(l => Line(l)).ToArray(), deformed = defl.ToArray(), scale = 100, supports = sup.Select(P).ToArray() };
    numbers["frame"] = new { nodes = fr.Nodes.Length, dof = fr.Nodes.Length * 6, ms = t2.ElapsedMilliseconds, maxDisp = fr.MaxDisplacement, maxUtil = R3(fr.MaxUtilization), supports = fr.SupportNodeCount, elements = fr.Utilization.Length };
}

static double ArcAt(Vec3d[] line, double[] arc, Vec3d p)
{
    double best = double.MaxValue, bestArc = 0;
    for (int i = 0; i + 1 < line.Length; i++) { var d = line[i + 1] - line[i]; double len = d.Length; if (len < 1e-15) continue; double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - line[i], d) / (len * len))); double dist = (line[i] + t * d - p).LengthSquared; if (dist < best) { best = dist; bestArc = arc[i] + t * len; } }
    return bestArc;
}
static Vec3d[] Sub(Vec3d[] l, double spacing) => ShortestPath.Resample(l, spacing);
static (int ends, int boundary, int onCurve, int floating, double endTurn, double turn) Ends(List<Vec3d[]> all, MeshProjection pj)
{
    int ends = 0, onBoundary = 0, onCurve = 0, floating = 0; double maxEndTurn = 0, maxTurn = 0;
    foreach (var l in all)
    {
        bool closed = (l[0] - l[^1]).LengthSquared < 1e-24;
        if (!closed) foreach (var (p, atEnd) in new[] { (l[0], false), (l[^1], true) })
        {
            ends++;
            var h = pj.ClosestPoint(p, pj.NearestVertexGlobal(p));
            bool bnd = pj.IsOnBoundary(h, 1e-4) || (h.Point - p).Length > 1e-6;
            double best = 1e9;
            foreach (var m in all) { if (ReferenceEquals(m, l)) continue; for (int i = 0; i + 1 < m.Length; i++) { var ab = m[i + 1] - m[i]; double tt = Math.Max(0, Math.Min(1, Vec3d.Dot(p - m[i], ab) / Math.Max(ab.LengthSquared, 1e-30))); best = Math.Min(best, (m[i] + tt * ab - p).Length); } }
            if (bnd) onBoundary++; else if (best < 1e-6) onCurve++; else floating++;
            if (l.Length >= 3)
            {
                Vec3d u = atEnd ? l[^2] - l[^3] : l[1] - l[0], v = atEnd ? l[^1] - l[^2] : l[2] - l[1];
                if (u.Length > 1e-9 && v.Length > 1e-9) maxEndTurn = Math.Max(maxEndTurn, Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(u, v) / (u.Length * v.Length)))) * 180 / Math.PI);
            }
        }
        for (int i = 1; i + 1 < l.Length; i++)
        {
            var u = l[i] - l[i - 1]; var v = l[i + 1] - l[i];
            if (u.Length < 1e-9 || v.Length < 1e-9) continue;
            maxTurn = Math.Max(maxTurn, Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(u, v) / (u.Length * v.Length)))) * 180 / Math.PI);
        }
    }
    return (ends, onBoundary, onCurve, floating, maxEndTurn, maxTurn);
}

// ---------- Ruled surfaces: cylinder (K = 0) and hyperboloid of one sheet (asymptotic curves = rulings) ----------
{
    int seg = 64, rows = 32;
    // Cylinder R = 1, height 2: k1 = 1, k2 = 0, K = 0, H = 0.5; no asymptotic directions; geodesics are helices
    var cyl = TestMeshes.CreateCylinder(1.0, 2.0, seg, rows);
    var pcC = PrincipalCurvature.Compute(cyl);
    var KC = GaussianCurvature.Compute(cyl); var HC = MeanCurvature.Compute(cyl).Values;
    var interior = Enumerable.Range(2, rows - 3).SelectMany(j => Enumerable.Range(0, seg).Select(i => j * seg + i)).ToArray();
    var fieldC = AsymptoticCurves.ComputeDirections(pcC);
    var asymC = EvenlySpacedNet.TraceField(cyl, fieldC.Family1, fieldC.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, fieldC.Family2);
    numbers["cylinder"] = new { k1Err = R3(interior.Max(v => Math.Abs(pcC.K1[v] - 1))), k2Max = interior.Max(v => Math.Abs(pcC.K2[v])), KMax = interior.Max(v => Math.Abs(KC[v])), HErr = interior.Max(v => Math.Abs(HC[v] - 0.5)), asymVertices = fieldC.Exists.Count(e => e), asymCurves = asymC.Count, vertices = cyl.VertexCount };
    // geodesic helices from one seed at 30°, 45°, 60° from the axis, plus the circle
    var projC = new MeshProjection(cyl);
    int seedC = (rows / 2) * seg;
    var helixLines = new List<double[][]>(); var helixRows = new List<object>();
    foreach (double deg in new[] { 30.0, 45.0, 60.0, 90.0 })
    {
        double al = deg * Math.PI / 180;
        var g = GeodesicCurves.Trace(cyl, new[] { seedC }, new[] { new Vec3d(0, Math.Sin(al), Math.Cos(al)) }, new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 })[0];
        double th = Math.Atan2(g[0].Y, g[0].X), acc = 0, rise = 0;
        for (int i = 0; i < g.Length; i++) { double t = Math.Atan2(g[i].Y, g[i].X), d = t - th; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; acc += d; th = t; if (deg < 89) rise = Math.Max(rise, Math.Abs(g[i].Z - (g[0].Z + acc / Math.Tan(al)))); }
        double measuredDeg = deg < 89 ? Math.Atan(Math.Abs(acc) / Math.Abs(g[^1].Z - g[0].Z)) * 180 / Math.PI : 90;
        var la = LathAnalysis.Analyze(projC, g);
        int n = g.Length; var mid = Enumerable.Range(n / 4, n / 2).ToArray();
        helixLines.Add(Line(g, 2));
        helixRows.Add(new { angle = deg, measured = R3(measuredDeg), riseErr = R3(rise), len = R3(ArcLen(g)), lenTheory = R3(deg < 89 ? 2 / Math.Cos(al) : 2 * Math.PI), closed = (g[0] - g[^1]).Length < 1e-9, kn = R3(mid.Average(i => Math.Abs(la.NormalCurvature[i]))), knTheory = R3(Math.Sin(al) * Math.Sin(al)), tg = R3(mid.Average(i => Math.Abs(la.GeodesicTorsion[i]))), tgTheory = R3(Math.Sin(al) * Math.Cos(al)), kgMax = R3(mid.Max(i => Math.Abs(la.GeodesicCurvature[i]))) });
    }
    scenes["cylGeo"] = new { wire = RevolveWire(z => 1.0, -1, 1, 8, 16), curves = helixLines.ToArray() };
    numbers["cylGeo"] = helixRows;

    // Hyperboloid x² + y² − z² = 1, z in [−1, 1]: K = −1/(1 + 2z²)², asymptotic curves are the straight rulings
    var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, seg, rows);
    var pcH = PrincipalCurvature.Compute(hyp);
    var KH = GaussianCurvature.Compute(hyp);
    var fieldH = AsymptoticCurves.ComputeDirections(pcH);
    var projH = new MeshProjection(hyp);
    double kErr = interior.Max(v => Math.Abs(KH[v] - TestMeshes.HyperboloidGaussianCurvature(hyp.Vertices[v].Z)));
    double dirWorst = 0, dirSum = 0;
    foreach (int v in interior) { var (dp, dm) = TestMeshes.HyperboloidRulings(hyp.Vertices[v]); double a = Math.Max(Math.Min(AngleDeg(fieldH.Family1[v], dp), AngleDeg(fieldH.Family1[v], dm)), Math.Min(AngleDeg(fieldH.Family2[v], dp), AngleDeg(fieldH.Family2[v], dm))); dirWorst = Math.Max(dirWorst, a); dirSum += a; }
    var tH = Stopwatch.StartNew();
    var hypA = EvenlySpacedNet.TraceField(hyp, fieldH.Family1, fieldH.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.25 }, fieldH.Family2);
    var hypB = EvenlySpacedNet.TraceField(hyp, fieldH.Family2, fieldH.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.25 }, fieldH.Family1);
    tH.Stop();
    int wrongH = 0, totalH = 0, rimEnds = 0, endsH = 0; double devMax = 0, rulingAngle = 0;
    foreach (var (fam, self, other) in new[] { (hypA, fieldH.Family1, fieldH.Family2), (hypB, fieldH.Family2, fieldH.Family1) })
        foreach (var c in fam)
        {
            devMax = Math.Max(devMax, ChordDev(c));
            var (dp, dm) = TestMeshes.HyperboloidRulings(projH.ClosestPoint(c[c.Length / 2], -1).Point);
            rulingAngle = Math.Max(rulingAngle, Math.Min(AngleDeg(c[^1] - c[0], dp), AngleDeg(c[^1] - c[0], dm)));
            for (int i = 1; i < c.Length; i += 5) { var tg = (c[i] - c[i - 1]).Normalized(); int vi = projH.NearestVertexGlobal(c[i]); if (!fieldH.Exists[vi]) continue; totalH++; if (Math.Abs(Vec3d.Dot(tg, other[vi])) > Math.Abs(Vec3d.Dot(tg, self[vi])) + 0.2) wrongH++; }
            foreach (var e in new[] { c[0], c[^1] }) { endsH++; if (Math.Abs(Math.Abs(e.Z) - 1) < 1e-3) rimEnds++; }
        }
    scenes["asymHyp"] = new { wire = RevolveWire(z => Math.Sqrt(1 + z * z), -1, 1, 8, 16), a = hypA.Select(l => Line(l, 3)).ToArray(), b = hypB.Select(l => Line(l, 3)).ToArray() };
    numbers["asymHyp"] = new { a = hypA.Count, b = hypB.Count, wrongFamily = wrongH, samples = totalH, ends = endsH, rimEnds, maxChordDev = R3(devMax), maxRulingAngle = R3(rulingAngle), dirWorst = R3(dirWorst), dirMean = R3(dirSum / interior.Length), KErr = R3(kErr), ms = tH.ElapsedMilliseconds, spacing = 0.25 };
    // Lath Analysis on one ruling of each family (upright 100 x 10 mm lath, limit 0.5%): kn = 0, |τg| = √−K, opposite signs
    var lathRows = new List<object>();
    foreach (bool fam in new[] { false, true })
    {
        var r = AsymptoticCurves.Trace(hyp, new[] { (rows / 2) * seg }, pcH, fam, new AsymptoticCurves.Options { StepSize = 0.02 })[0];
        var la = LathAnalysis.Analyze(projH, r, new LathAnalysis.Options { Upright = true, Width = 0.1, Thickness = 0.01, MaxStrain = 0.005 });
        int n = r.Length; var mid = Enumerable.Range(n / 4, n / 2).ToArray();
        int throat = Enumerable.Range(0, n).OrderBy(i => Math.Abs(r[i].Z)).First();
        lathRows.Add(new { family = fam ? "B" : "A", chordDev = R3(ChordDev(r)), knMax = R3(mid.Max(i => Math.Abs(la.NormalCurvature[i]))), kgMax = R3(mid.Max(i => Math.Abs(la.GeodesicCurvature[i]))), tgThroat = R3(la.GeodesicTorsion[throat]), tgErr = R3(mid.Max(i => Math.Abs(Math.Abs(la.GeodesicTorsion[i]) - Math.Sqrt(-TestMeshes.HyperboloidGaussianCurvature(r[i].Z))))), utilThroat = R3(la.Utilization[throat]), utilTheory = R3(0.01 / Math.Sqrt(3.0) / 0.005) });
    }
    numbers["hypLath"] = lathRows;
}

// ---------- Form finding on a 24x24 grid ----------
{
    int n = 24;
    var grid = TestMeshes.CreateQuadGrid(n, n, 1.0);
    var bnd = grid.BuildBoundaryVertexFlags();
    // Minimal surface with a warped boundary (z = 3 sin along the edge)
    var verts = (Vec3d[])grid.Vertices.Clone();
    for (int i = 0; i < verts.Length; i++) if (bnd[i]) { double x = verts[i].X, y = verts[i].Y; verts[i] = new Vec3d(x, y, 4 * Math.Sin(Math.PI * x / n) * Math.Sin(Math.PI * y / n) * 0 + 3 * Math.Cos(Math.PI * (x - y) / n)); }
    var warped = new MeshData(verts, grid.Faces);
    var t = Stopwatch.StartNew();
    var ms = MinimalSurface.Compute(warped, bnd, new MinimalSurface.Options { MaxIterations = 15 });
    t.Stop();
    var msMesh = new MeshData(ms.Vertices, grid.Faces);
    var Hms = MeanCurvature.Compute(msMesh).Values;
    var interiorH = new List<double>(); for (int i = 0; i < Hms.Length; i++) if (!bnd[i]) interiorH.Add(Math.Abs(Hms[i]));
    scenes["minimal"] = new { before = GridWire(warped, n, n, 2), after = GridWire(msMesh, n, n, 2) };
    numbers["minimal"] = new { vertices = grid.VertexCount, iterations = ms.Iterations, ms = t.ElapsedMilliseconds, residual = ms.Residual, meanAbsH = R3(interiorH.Average()), maxAbsH = R3(interiorH.Max()), boundaryH = R3(3.0 / 12) };
    // FDM tension net: q=1, load -1 per node
    var q = new double[grid.BuildEdges().Length]; Array.Fill(q, 1.0);
    var loads = new Vec3d[grid.VertexCount]; Array.Fill(loads, new Vec3d(0, 0, -0.2));
    t.Restart();
    var fd = ForceDensityMethod.Compute(grid, q, loads, bnd);
    t.Stop();
    var fdMesh = new MeshData(fd.Vertices, grid.Faces);
    // equilibrium check at the centre node: sum of edge forces + load
    var edges = grid.BuildEdges(); int c0 = (n / 2) * (n + 1) + n / 2; var res = new Vec3d(0, 0, -0.2);
    foreach (var e in edges) { if (e.v0 == c0) res = res + (fd.Vertices[e.v1] - fd.Vertices[c0]); else if (e.v1 == c0) res = res + (fd.Vertices[e.v0] - fd.Vertices[c0]); }
    scenes["fdm"] = new { before = GridWire(grid, n, n, 2), after = GridWire(fdMesh, n, n, 2) };
    numbers["fdm"] = new { ms = t.ElapsedMilliseconds, sag = R3(-fd.Vertices.Min(p => p.Z)), residual = res.Length, edges = edges.Length };
    // compression shell: q = -1, load up
    var qn = new double[edges.Length]; Array.Fill(qn, -1.0);
    var up = new Vec3d[grid.VertexCount]; Array.Fill(up, new Vec3d(0, 0, -0.2));
    var fdc = ForceDensityMethod.Compute(grid, qn, up, bnd);
    numbers["fdmCompression"] = new { rise = R3(fdc.Vertices.Max(p => p.Z)) };
    // Dynamic relaxation hanging net
    t.Restart();
    var dr = DynamicRelaxation.Compute(grid, bnd, new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000 });
    t.Stop();
    var drMesh = new MeshData(dr.Vertices, grid.Faces);
    scenes["dynrelax"] = new { before = GridWire(grid, n, n, 2), after = GridWire(drMesh, n, n, 2) };
    numbers["dynrelax"] = new { iterations = dr.Iterations, converged = dr.Converged, ms = t.ElapsedMilliseconds, sag = R3(-dr.Vertices.Min(p => p.Z)), residual = dr.Residual, maxForce = R3(dr.EdgeForces.Max()), minForce = R3(dr.EdgeForces.Min()) };
    // Planarization: noisy grid
    var rng = new Random(3); var nz = (Vec3d[])grid.Vertices.Clone();
    for (int i = 0; i < nz.Length; i++) nz[i] = new Vec3d(nz[i].X, nz[i].Y, 0.3 * Math.Sin(nz[i].X * 0.7) * Math.Cos(nz[i].Y * 0.5) + 0.15 * (rng.NextDouble() - 0.5));
    var noisy = new MeshData(nz, grid.Faces);
    var dev0 = Planarization.ComputeDeviation(noisy);
    var pl = Planarization.Compute(noisy, bnd, new Planarization.Options { MaxIterations = 100, Tolerance = 1e-6 });
    scenes["planar"] = new { before = GridWire(noisy, n, n, 2), after = GridWire(new MeshData(pl.Vertices, grid.Faces), n, n, 2) };
    numbers["planar"] = new { devBefore = R3(dev0.Max()), devAfter = pl.FaceDeviations.Max(), iterations = pl.Iterations, maxMove = R3(pl.Vertices.Zip(nz, (a2, b2) => (a2 - b2).Length).Max()) };
}

// ---------- Mesh cleanup + pull to mesh ----------
{
    var sphere = TestMeshes.CreateUnitSphere(16);
    // explode into per-face triangles far from origin
    var verts = new List<Vec3d>(); var faces = new List<int[]>(); var off = new Vec3d(5e5, 3e5, 0);
    foreach (var f in sphere.Faces) { int b = verts.Count; foreach (int vi in f) verts.Add(sphere.Vertices[vi] + off); faces.Add(new[] { b, b + 1, b + 2 }); }
    faces.Add(new[] { 0, 1, 2 }); // duplicate face
    var t = Stopwatch.StartNew();
    var cl = MeshCleanup.Compute(new MeshData(verts.ToArray(), faces.ToArray()));
    t.Stop();
    numbers["cleanup"] = new { inVerts = verts.Count, outVerts = cl.Mesh.VertexCount, expectedVerts = sphere.VertexCount, welded = cl.WeldedVertices, degenerate = cl.RemovedDegenerateFaces, duplicate = cl.RemovedDuplicateFaces, ms = t.ElapsedMilliseconds };
    // pull a floating circle onto the sphere
    var proj = new MeshProjection(TestMeshes.CreateUnitSphere(32));
    var circ = new List<Vec3d>(); for (int i = 0; i <= 60; i++) { double a = 2 * Math.PI * i / 60; circ.Add(new Vec3d(0.9 * Math.Cos(a), 0.9 * Math.Sin(a), 0.9)); }
    var pulled = circ.Select(p => proj.ClosestPoint(p, -1).Point).ToArray();
    var faired = CurveFairing.SmoothOnSurface(proj, pulled, 10);
    scenes["pull"] = new { wire = SphereWire(8), before = Line(circ.ToArray()), after = Line(faired) };
    numbers["pull"] = new { maxDist = R3(circ.Zip(pulled, (a2, b2) => (a2 - b2).Length).Max()), radiusAfter = R3(faired.Select(p => p.Length).Average()) };
}

// ---------- kd-tree / projection scale ----------
{
    var grid = TestMeshes.CreateQuadGrid(300, 300, 1.0);
    var t = Stopwatch.StartNew(); var proj = new MeshProjection(grid); t.Stop();
    var t2 = Stopwatch.StartNew(); var rng = new Random(1); int ok = 0;
    for (int i = 0; i < 20000; i++) { var p = new Vec3d(rng.NextDouble() * 300, rng.NextDouble() * 300, rng.NextDouble()); var h = proj.ClosestPoint(p, -1); if (Math.Abs(h.Point.Z) < 1e-9) ok++; }
    t2.Stop();
    numbers["projection"] = new { vertices = grid.VertexCount, buildMs = t.ElapsedMilliseconds, queries = 20000, queryMs = t2.ElapsedMilliseconds, exact = ok };
}

numbers["totalMs"] = sw.ElapsedMilliseconds;
var opt = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true };
File.WriteAllText(Path.Combine(OutDir, "scenes.json"), JsonSerializer.Serialize(scenes, opt));
File.WriteAllText(Path.Combine(OutDir, "numbers.json"), JsonSerializer.Serialize(numbers, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Console.WriteLine(File.ReadAllText(Path.Combine(OutDir, "numbers.json")));
Console.WriteLine("scenes bytes: " + new FileInfo(Path.Combine(OutDir, "scenes.json")).Length);
