using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Display;
using Mite.Core.Dynamics;
using Mite.Core.Fabrication;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Visualisation;
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
double MeanEdgeOf(MeshData m) { var e = m.BuildEdges(); return e.Average(x => (m.Vertices[x.v0] - m.Vertices[x.v1]).Length); }
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
    // per-vertex K, H, k1, k2 for the colour-map demos of the curvature cards (torus 64 × 32 = 2048 verts, quads)
    {
        var verts = torus.Vertices.Select(P).ToArray();
        var faces = new List<int[]>();
        for (int i = 0; i < 64; i++) for (int j = 0; j < 32; j++) { int a = i * 32 + j, b = ((i + 1) % 64) * 32 + j, c = ((i + 1) % 64) * 32 + (j + 1) % 32, d = i * 32 + (j + 1) % 32; faces.Add(new[] { a, b, c, d }); }
        scenes["torusK"] = new { verts, faces = faces.ToArray(), values = K.Select(R3).ToArray(), valuesH = H.Select(R3).ToArray(), valuesK1 = pc.K1.Select(R3).ToArray(), valuesK2 = pc.K2.Select(R3).ToArray() };
    }
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
    // the same meridian as a round bar Ø 10 mm: bending strain kn·d/2 = 1·0.005, whichever way it is "oriented"
    var laRound = LathAnalysis.Analyze(proj, sl1[0], new LathAnalysis.Options { Profile = LathProfile.Round(0.01), MaxStrain = 0.005 });
    var laRoundUp = LathAnalysis.Analyze(proj, sl1[0], new LathAnalysis.Options { Profile = LathProfile.Round(0.01), MaxStrain = 0.005, Upright = true });
    var sq = new LathProfile(0.02, 0.02).SectionProperties();
    numbers["lathRound"] = new { util = R3(laRound.MaxUtilization), utilUpright = R3(laRoundUp.MaxUtilization), theoryUtil = 1.0, squareJ = R3(sq.J / Math.Pow(0.02, 4) * 1000) / 1000, squareTwist = R3(sq.TwistLength / 0.02) };
}

// ---------- Curvature against the exact truth on the analytic catalogue (AnalyticSurface) ----------
{
    // Every shape is built from its closed-form parametrisation, so K, H, k1, k2 are known at every vertex
    // (do Carmo §3-3); the discrete estimators are measured against them over the interior vertices: mean
    // and largest absolute error, relative to the shape's largest curvature, and the mean error at half the
    // resolution for the convergence (the protocol of Magid, Soldea & Rivlin 2007).
    var catalogue = new (string key, string label, double p1, double p2, double p3, int res)[] {
        ("torus", "torus R = 3, r = 1", 0, 0, 0, 48),
        ("sphere", "unit sphere", 0, 0, 0, 36),
        ("ellipsoid", "ellipsoid 1.5 × 1 × 0.7 (four umbilics)", 0, 0, 0, 36),
        ("hyperboloid", "hyperboloid x² + y² − z² = 1", 0, 0, 0, 36),
        ("catenoid", "catenoid (H = 0)", 0, 0, 0, 36),
        ("monkey", "monkey saddle z = x³ − 3xy²", 0, 0, 0, 36),
        ("cone", "cone (K = 0)", 0, 0, 0, 36),
        ("wave", "wave z = 0.3 sin 2x cos 2y", 0, 0, 0, 36),
    };
    var truthRows = new List<object>();
    foreach (var (key, label, p1, p2, p3, res) in catalogue)
    {
        var s = AnalyticShapes.Surface(key, p1, p2, p3);
        (double[] mK, double[] mH, double[] m1, double[] m2, double maxK, double maxk, int n) Measure(int r, out MeshData mesh, out int[][] quads, out int[] border, out double[] K, out double[] H, out double[] k1, out double[] k2, out double[] Kt, out double[] Ht)
        {
            mesh = s.Mesh(r, out var uv, out quads);
            var truth = s.AtVertices(uv);
            K = GaussianCurvature.Compute(mesh); H = MeanCurvature.Compute(mesh).Values; var pc = PrincipalCurvature.Compute(mesh); k1 = pc.K1; k2 = pc.K2;
            var bnd = mesh.BuildBoundaryVertexFlags();
            Kt = truth.Select(t => t.Degenerate ? 0 : t.K).ToArray(); Ht = truth.Select(t => t.Degenerate ? 0 : t.H).ToArray();
            border = Enumerable.Range(0, mesh.VertexCount).Where(i => bnd[i] || truth[i].Degenerate).ToArray();
            double mK = 0, mH = 0, m1 = 0, m2 = 0, xK = 0, xH = 0, x1 = 0, x2 = 0, maxK = 0, maxk = 0; int n = 0;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (bnd[i] || truth[i].Degenerate) continue;
                n++; var t = truth[i];
                maxK = Math.Max(maxK, Math.Abs(t.K)); maxk = Math.Max(maxk, Math.Max(Math.Abs(t.K1), Math.Abs(t.K2)));
                double eK = Math.Abs(K[i] - t.K), eH = Math.Abs(H[i] - t.H), e1 = Math.Abs(k1[i] - t.K1), e2 = Math.Abs(k2[i] - t.K2);
                mK += eK; mH += eH; m1 += e1; m2 += e2; xK = Math.Max(xK, eK); xH = Math.Max(xH, eH); x1 = Math.Max(x1, e1); x2 = Math.Max(x2, e2);
            }
            return (new[] { mK / n, xK }, new[] { mH / n, xH }, new[] { m1 / n, x1 }, new[] { m2 / n, x2 }, maxK, maxk, n);
        }
        var full = Measure(res, out var mesh, out var quads, out var border, out var K, out var H, out var k1, out var k2, out var Kt, out var Ht);
        var half = Measure(res / 2, out _, out _, out _, out _, out _, out _, out _, out _, out _);
        double scaleK = Math.Max(full.maxK, full.maxk * full.maxk), scalek = full.maxk;
        object Err(double[] e, double scale) => new { mean = e[0], max = e[1], meanRel = R3(100 * e[0] / scale), maxRel = R3(100 * e[1] / scale) };
        truthRows.Add(new
        {
            key, label, resolution = res, vertices = mesh.VertexCount, interior = full.n, largestK = R3(full.maxK), largestPrincipal = R3(full.maxk),
            K = Err(full.mK, scaleK), H = Err(full.mH, scalek), k1 = Err(full.m1, scalek), k2 = Err(full.m2, scalek),
            halfRes = res / 2, halfK = R3(100 * half.mK[0] / scaleK), halfH = R3(100 * half.mH[0] / scalek), halfK1 = R3(100 * half.m1[0] / scalek), halfK2 = R3(100 * half.m2[0] / scalek),
            developable = key == "cone",
        });
        scenes["curv_" + key] = new
        {
            label, verts = mesh.Vertices.Select(P).ToArray(), faces = quads, b = border,
            K = K.Select(R3).ToArray(), H = H.Select(R3).ToArray(), k1 = k1.Select(R3).ToArray(), k2 = k2.Select(R3).ToArray(),
            Kt = Kt.Select(R3).ToArray(), Ht = Ht.Select(R3).ToArray(),
        };
    }
    numbers["curvTruth"] = truthRows;

    // the wave's saddle points: a valence-4 star whose edges follow the asymptotic lines is flat as a polyhedron,
    // so Gauss–Bonnet reads K = 0 there against −1.44 at any resolution; the Rusinkiewicz tensor still sees it
    {
        var s = AnalyticShapes.Surface("wave");
        var m = s.Mesh(48, out var uv, out _); var tr = s.AtVertices(uv);
        var K = GaussianCurvature.Compute(m); var pc = PrincipalCurvature.Compute(m); var bnd = m.BuildBoundaryVertexFlags();
        var val = new int[m.VertexCount]; foreach (var f in m.Faces) foreach (int i in f) val[i]++;
        int worst = Enumerable.Range(0, m.VertexCount).Where(i => !bnd[i]).OrderByDescending(i => Math.Abs(K[i] - tr[i].K)).First();
        numbers["flatStar"] = new { K = K[worst], truth = R3(tr[worst].K), valence = val[worst], tensor = R3(pc.K1[worst] * pc.K2[worst]), point = P(m.Vertices[worst]) };
    }

    // the ellipsoid's four umbilics (a > b > c): (±a √((a² − b²)/(a² − c²)), 0, ±c √((b² − c²)/(a² − c²)))
    {
        double a = 1.5, b = 1.0, c = 0.7;
        double ux = a * Math.Sqrt((a * a - b * b) / (a * a - c * c)), uz = c * Math.Sqrt((b * b - c * c) / (a * a - c * c));
        var m = AnalyticShapes.Build("ellipsoid", 0, 0, 0, 96);
        var pc = PrincipalCurvature.Compute(m);
        var found = Umbilics.Find(pc, 0.1);
        var clusters = new List<List<int>>();
        foreach (var i in found)
        {
            var cl = clusters.FirstOrDefault(cl => cl.Any(j => (m.Vertices[i] - m.Vertices[j]).Length < 0.3));
            if (cl == null) { cl = new List<int>(); clusters.Add(cl); }
            cl.Add(i);
        }
        var centres = clusters.Select(cl => new Vec3d(cl.Average(i => m.Vertices[i].X), cl.Average(i => m.Vertices[i].Y), cl.Average(i => m.Vertices[i].Z))).ToArray();
        double worstD = centres.Length == 0 ? 99 : centres.Max(cen => new[] { 1, -1 }.SelectMany(sx => new[] { 1, -1 }.Select(sz => (cen - new Vec3d(sx * ux, 0, sz * uz)).Length)).Min());
        numbers["umbilics"] = new { flagged = found.Length, clusters = clusters.Count, worstDistance = R3(worstD), ux = R3(ux), uz = R3(uz), vertices = m.VertexCount, tolerance = 0.1, centres = centres.Select(P).ToArray() };
        scenes["curv_ellipsoid_umbilics"] = new { pts = centres.Select(P).ToArray(), truth = new[] { 1, -1 }.SelectMany(sx => new[] { 1, -1 }.Select(sz => P(new Vec3d(sx * ux, 0, sz * uz)))).ToArray() };
    }
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
    // Lath Analysis on a traced closed great circle (48 divisions, tilted plane) and on a coarse
    // 45° latitude circle: kg = 0 and kg = tan 45° = 1; a flat 100 × 10 mm lath on the great
    // circle is at utilization kn·t/2 / 0.5 % = 1 exactly
    {
        var s48 = TestMeshes.CreateUnitSphere(48);
        var p48 = new MeshProjection(s48);
        var dir = new Vec3d(0, 0.6, 0.8);
        var loop = GeodesicCurves.Trace(s48, new[] { p48.NearestVertexGlobal(new Vec3d(1, 0, 0)) }, new[] { dir })[0];
        var pn = Vec3d.Cross(new Vec3d(1, 0, 0), dir).Normalized();
        double kink = 0; int hint = p48.NearestVertexGlobal(loop[0]);
        for (int i = 1; i < loop.Length - 1; i++)
        {
            var hit = p48.ClosestPoint(loop[i], hint); hint = hit.NearestVertex; var nn = hit.SmoothNormal;
            var ea = loop[i] - loop[i - 1]; ea = ea - Vec3d.Dot(ea, nn) * nn; var eb = loop[i + 1] - loop[i]; eb = eb - Vec3d.Dot(eb, nn) * nn;
            kink = Math.Max(kink, Math.Abs(Math.Atan2(Vec3d.Dot(nn, Vec3d.Cross(ea, eb)), Vec3d.Dot(ea, eb))) * 180 / Math.PI);
        }
        var laLoop = LathAnalysis.Analyze(p48, loop, new LathAnalysis.Options { Width = 0.1, Thickness = 0.01, MaxStrain = 0.005 });
        var lat = new Vec3d[40];
        for (int i = 0; i < 40; i++) { double th = 1.5 * Math.PI * i / 39; lat[i] = new Vec3d(Math.Cos(Math.PI / 4) * Math.Cos(th), Math.Cos(Math.PI / 4) * Math.Sin(th), Math.Sin(Math.PI / 4)); }
        var laLat = LathAnalysis.Analyze(p48, lat);
        numbers["lathLoop"] = new { points = loop.Length, closed = (loop[0] - loop[^1]).Length < 1e-12, offPlane = loop.Max(q => Math.Abs(Vec3d.Dot(q, pn))), edge = R3(p48.AverageEdgeLength), kinkDeg = R3(kink), kgMax = R3(laLoop.GeodesicCurvature.Max(Math.Abs)), knMean = R3(laLoop.NormalCurvature.Skip(3).Take(loop.Length - 6).Average(Math.Abs)), util = R3(laLoop.MaxUtilization), latKgErr = Enumerable.Range(2, 36).Max(i => Math.Abs(Math.Abs(laLat.GeodesicCurvature[i]) - 1.0)), latSpacing = R3((lat[1] - lat[0]).Length) };
    }
    var a = new Vec3d(1, 0, 0); var b = new Vec3d(-0.5, 0.5, 0.7071).Normalized();
    var sp = ShortestPath.Compute(proj, a, b)!.Value;
    var vp = ShortestPath.Dijkstra(sphere, proj.NearestVertexGlobal(a), proj.NearestVertexGlobal(b))!;
    var fo = FlipGeodesic.Compute(sphere, new[] { a }, new[] { b }, out _)![0]!;
    scenes["geoPath"] = new { wire = SphereWire(8), dijkstra = Line(vp.Select(v => sphere.Vertices[v]).ToArray()), path = Line(sp.Points), flip = Line(fo.Points), ends = new[] { P(a), P(b) } };
    numbers["geoPath"] = new { length = R3(sp.Length), theory = R3(Math.Acos(Vec3d.Dot(a, b))), dijkstraLen = R3(ArcLen(vp.Select(v => sphere.Vertices[v]).ToArray())), flipLength = R3(fo.Length), flipConverged = fo.Converged, flipPoints = fo.Points.Length };
    var cheb = ChebyshevNet.Compute(sphere, 1 + 12 * 48 + 5, new Vec3d(0, 1, 0), new ChebyshevNet.Options { EdgeLength = 0.2, CountU = 5, CountV = 5, Angle = Math.PI / 2 });
    int nu = cheb.Points.GetLength(0), nv = cheb.Points.GetLength(1);
    var u = new List<double[][]>(); var v = new List<double[][]>(); var edgeErr = new List<double>();
    for (int i = 0; i < nu; i++) { var l = new List<double[]>(); for (int j = 0; j < nv; j++) if (cheb.Valid[i, j]) l.Add(P(cheb.Points[i, j])); if (l.Count > 1) u.Add(l.ToArray()); }
    for (int j = 0; j < nv; j++) { var l = new List<double[]>(); for (int i = 0; i < nu; i++) if (cheb.Valid[i, j]) l.Add(P(cheb.Points[i, j])); if (l.Count > 1) v.Add(l.ToArray()); }
    for (int i = 0; i + 1 < nu; i++) for (int j = 0; j < nv; j++) if (cheb.Valid[i, j] && cheb.Valid[i + 1, j]) edgeErr.Add(Math.Abs((cheb.Points[i + 1, j] - cheb.Points[i, j]).Length - 0.2) / 0.2);
    scenes["cheb"] = new { wire = SphereWire(8), u = u.ToArray(), v = v.ToArray() };
    var shear = new List<double>();
    for (int i = 0; i + 1 < nu; i++) for (int j = 0; j + 1 < nv; j++) if (cheb.Valid[i, j] && cheb.Valid[i + 1, j] && cheb.Valid[i, j + 1]) { var e1 = (cheb.Points[i + 1, j] - cheb.Points[i, j]).Normalized(); var e2 = (cheb.Points[i, j + 1] - cheb.Points[i, j]).Normalized(); shear.Add(Math.Acos(Math.Abs(Vec3d.Dot(e1, e2))) * 180 / Math.PI); }
    // the closed-form compass net on the exact sphere (TestMeshes.ChebyshevSphereNet): the seed
    // vertex 1 + 12·48 + 5 lies on the equator at longitude 5·2π/48, d1 is the equator's tangent
    // there, d2 = n × d1 points north; the mesh net differs by the depth of the facets below the
    // sphere (right triangles with legs e: e²/4) carried from parents to children
    Vec3d cp0 = sphere.Vertices[1 + 12 * 48 + 5];
    Vec3d cd1 = (new Vec3d(0, 1, 0) - Vec3d.Dot(new Vec3d(0, 1, 0), cp0) * cp0).Normalized();
    Vec3d cd2 = Vec3d.Cross(cp0, cd1).Normalized();
    var (chebExact, chebExactValid) = TestMeshes.ChebyshevSphereNet(cp0, cd1, cd2, 0.2, 5, 5);
    double chebNodeErr = 0; int chebCompared = 0;
    for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++) if (cheb.Valid[i, j] && chebExactValid[i, j]) { chebNodeErr = Math.Max(chebNodeErr, (cheb.Points[i, j] - chebExact[i, j]).Length); chebCompared++; }
    double chebFacet = Math.Pow(2 * Math.PI / 48, 2) / 4;
    // a 12 × 12 patch locks: Hazzidakis makes the shear angle fall with the enclosed curvature;
    // MinAngle 30° stops the net before any crossing falls below 30°
    var chebFree = ChebyshevNet.Compute(sphere, 1 + 12 * 48 + 5, new Vec3d(0, 1, 0), new ChebyshevNet.Options { EdgeLength = 0.2, CountU = 12, CountV = 12 });
    var chebLock = ChebyshevNet.Compute(sphere, 1 + 12 * 48 + 5, new Vec3d(0, 1, 0), new ChebyshevNet.Options { EdgeLength = 0.2, CountU = 12, CountV = 12, MinAngle = Math.PI / 6 });
    double MinShear(ChebyshevNet.Result r) { double m = 180; foreach (double w in r.ShearAngles) if (!double.IsNaN(w)) m = Math.Min(m, Math.Min(w, Math.PI - w) * 180 / Math.PI); return m; }
    var lu = new List<double[][]>(); var lv = new List<double[][]>();
    int lnu = chebLock.Points.GetLength(0), lnv = chebLock.Points.GetLength(1);
    for (int i = 0; i < lnu; i++) { var l = new List<double[]>(); for (int j = 0; j < lnv; j++) { if (chebLock.Valid[i, j]) l.Add(P(chebLock.Points[i, j])); else { if (l.Count > 1) lu.Add(l.ToArray()); l.Clear(); } } if (l.Count > 1) lu.Add(l.ToArray()); }
    for (int j = 0; j < lnv; j++) { var l = new List<double[]>(); for (int i = 0; i < lnu; i++) { if (chebLock.Valid[i, j]) l.Add(P(chebLock.Points[i, j])); else { if (l.Count > 1) lv.Add(l.ToArray()); l.Clear(); } } if (l.Count > 1) lv.Add(l.ToArray()); }
    scenes["chebLock"] = new { wire = SphereWire(8), u = lu.ToArray(), v = lv.ToArray() };
    // the angle map: node angles ω by central differences, Hazzidakis' integral per cell
    // ∬K dA = ω(i+1,j) + ω(i,j+1) − ω(i,j) − ω(i+1,j+1) against the spherical area of the cell (K = 1)
    var w = ChebyshevNet.ShearAngles(cheb); var cellK = ChebyshevNet.CellCurvature(cheb);
    double SphTri(Vec3d p, Vec3d q, Vec3d r) { p = p.Normalized(); q = q.Normalized(); r = r.Normalized(); return 2 * Math.Atan2(Math.Abs(Vec3d.Dot(p, Vec3d.Cross(q, r))), 1 + Vec3d.Dot(p, q) + Vec3d.Dot(q, r) + Vec3d.Dot(r, p)); }
    double sumH = 0, sumA = 0; var cellErr = new List<double>();
    for (int i = 0; i + 1 < nu; i++) for (int j = 0; j + 1 < nv; j++)
    {
        if (double.IsNaN(cellK[i, j])) continue;
        Vec3d p0 = cheb.Points[i, j], p1 = cheb.Points[i + 1, j], p2 = cheb.Points[i + 1, j + 1], p3 = cheb.Points[i, j + 1];
        double area = 0.5 * (SphTri(p0, p1, p2) + SphTri(p0, p2, p3) + SphTri(p1, p2, p3) + SphTri(p1, p3, p0));
        sumH += cellK[i, j]; sumA += area; cellErr.Add(Math.Abs(cellK[i, j] - area) / area);
    }
    cellErr.Sort();
    var nodeAngles = w.Cast<double>().Where(x => !double.IsNaN(x)).ToList();
    // the same construction where the truth is exact: a flat lattice at 60° and the unrolled square net of a cylinder
    var plane = TestMeshes.CreateQuadGrid(20, 20, 0.5).ToTriangulated();
    var flat = ChebyshevNet.Compute(plane, 10 * 21 + 10, new Vec3d(1, 0, 0), new ChebyshevNet.Options { EdgeLength = 0.4, CountU = 5, CountV = 5, Angle = Math.PI / 3 });
    var flatW = ChebyshevNet.ShearAngles(flat).Cast<double>().Where(x => !double.IsNaN(x)).ToList();
    var flatK = ChebyshevNet.CellCurvature(flat).Cast<double>().Where(x => !double.IsNaN(x)).ToList();
    var cyl = TestMeshes.CreateCylinder(1.0, 4.0, 64, 32);
    int cylSeed = 0; { double bd = double.MaxValue; for (int k = 0; k < cyl.VertexCount; k++) { double dd = (cyl.Vertices[k] - new Vec3d(1, 0, 0)).LengthSquared; if (dd < bd) { bd = dd; cylSeed = k; } } }
    var cylNet = ChebyshevNet.Compute(cyl, cylSeed, new Vec3d(0, 0, 1), new ChebyshevNet.Options { EdgeLength = 0.3, CountU = 4, CountV = 4 });
    var cylW = ChebyshevNet.ShearAngles(cylNet).Cast<double>().Where(x => !double.IsNaN(x)).ToList();
    var cylK = ChebyshevNet.CellCurvature(cylNet).Cast<double>().Where(x => !double.IsNaN(x)).ToList();
    numbers["cheb"] = new { nodes = u.Sum(l => l.Length), valid = u.Sum(l => l.Length), total = nu * nv, maxEdgeErr = edgeErr.Max() * 100, meanEdgeErr = edgeErr.Average() * 100, minAngle = R3(shear.Min()), maxAngle = R3(shear.Max()),
        nodeErr = chebNodeErr, facet = chebFacet, compared = chebCompared, netEdgeErr = cheb.EdgeError,
        freeNodes = chebFree.ValidCount, freeMinShear = R3(MinShear(chebFree)), lockNodes = chebLock.ValidCount, lockMinShear = R3(MinShear(chebLock)), locked = chebLock.Locked, failed = chebLock.Failed,
        nodeMin = R3(nodeAngles.Min()), nodeMax = R3(nodeAngles.Max()), locked45 = ChebyshevNet.CountLocked(cheb, 45), locked30 = ChebyshevNet.CountLocked(cheb, 30), hazzidakis = R3(sumH), sphericalArea = R3(sumA), cells = cellErr.Count, cellMedianErr = R3(100 * cellErr[cellErr.Count / 2]), cellWorstErr = R3(100 * cellErr[cellErr.Count - 1]), flatAngleErr = flatW.Max(x => Math.Abs(x - 60)), flatIntegral = flatK.Max(Math.Abs), flatNodes = flatW.Count, cylAngleErr = cylW.Max(x => Math.Abs(x - 90)), cylIntegral = cylK.Max(Math.Abs), cylCells = cylK.Count };
}

// ---------- Triaxial ellipsoid: four umbilics (lemons), lines of curvature on confocal quadrics ----------
{
    const double EA = 1.5, EB = 1.0, EC = 0.7;
    var ell = TestMeshes.CreateEllipsoid(EA, EB, EC, 96);
    var pc = PrincipalCurvature.Compute(ell);
    var K = GaussianCurvature.Compute(ell); var H = MeanCurvature.Compute(ell).Values;
    double edge = MeanEdgeOf(ell);
    var um = TestMeshes.EllipsoidUmbilics(EA, EB, EC);
    double errK = 0, errH = 0, dirErr = 0;
    for (int i = 0; i < ell.VertexCount; i++)
    {
        var p = ell.Vertices[i]; if (Math.Abs(p.Z) > 0.95 * EC) continue;
        var (Kt, Ht) = AnalyticShapes.EllipsoidCurvature(p, EA, EB, EC);
        errK = Math.Max(errK, Math.Abs(K[i] - Kt) / Kt); errH = Math.Max(errH, Math.Abs(H[i] - Ht) / Ht);
        AnalyticShapes.EllipsoidPrincipal(p, EA, EB, EC, out double k1t, out double k2t, out Vec3d d1t, out _, out _);
        if (um.Min(u => (u - p).Length) > 0.3 && k1t - k2t > 0.3) dirErr = Math.Max(dirErr, AngleDeg(pc.D1[i], d1t));
    }
    var t = Stopwatch.StartNew();
    var located = Umbilics.Locate(ell, pc);
    t.Stop();
    var offsets = um.Select(u => located.Length == 0 ? 99 : located.Min(d => (d.Point - u).Length) / edge).ToArray();
    var torus = TestMeshes.CreateTorus(3.0, 1.0, 64, 32);
    var locTorus = Umbilics.Locate(torus, PrincipalCurvature.Compute(torus));
    var monkey = AnalyticShapes.Build("monkey", 2.0, 1.0, 0, 48);
    var locMonkey = Umbilics.Locate(monkey, PrincipalCurvature.Compute(monkey), 2 * MeanEdgeOf(monkey));
    var revo = AnalyticShapes.Build("ellipsoid", 1.0, 1.0, 0.6, 96);
    var locRevo = Umbilics.Locate(revo, PrincipalCurvature.Compute(revo));
    // lines of curvature from seeds at several distances from the umbilics, compared with the confocal quadric through the seed
    Vec3d OnE(Vec3d q) { double s2 = Math.Sqrt(q.X * q.X / (EA * EA) + q.Y * q.Y / (EB * EB) + q.Z * q.Z / (EC * EC)); return q / s2; }
    var lineRows = new List<object>(); var maxLines = new List<double[][]>(); var minLines = new List<double[][]>();
    foreach (bool max in new[] { true, false })
    {
        double Lam(Vec3d q) { var (l1, l2) = AnalyticShapes.EllipsoidConfocal(q, EA, EB, EC); return max ? l2 : l1; }
        double range = max ? EA * EA - EB * EB : EB * EB - EC * EC;
        foreach (double margin in new[] { 0.15, 0.3, 0.5, 0.7, 0.9 })
        {
            int seed = Enumerable.Range(0, ell.VertexCount).Where(i => ell.Vertices[i].Y > 0.3 && Math.Abs(ell.Vertices[i].Z) < 0.5)
                .OrderBy(i => Math.Abs((max ? Lam(ell.Vertices[i]) - EB * EB : EB * EB - Lam(ell.Vertices[i])) / range - margin)).First();
            var l = Mite.Core.Streamlines.CurvatureStreamlines.Trace(ell, new[] { seed }, pc, new Mite.Core.Streamlines.CurvatureStreamlines.Options { UseMaxCurvature = max, MaxSteps = 4000 })[0];
            double lamSeed = Lam(l[Enumerable.Range(0, l.Length).OrderBy(i => (l[i] - ell.Vertices[seed]).Length).First()]);
            double worst = 0, sum = 0;
            foreach (var p in l)
            {
                AnalyticShapes.EllipsoidPrincipal(p, EA, EB, EC, out _, out _, out Vec3d d1, out Vec3d d2, out _);
                const double h = 1e-5;
                double g1 = (Lam(OnE(p + h * d1)) - Lam(OnE(p - h * d1))) / (2 * h), g2 = (Lam(OnE(p + h * d2)) - Lam(OnE(p - h * d2))) / (2 * h);
                double off = Math.Abs(Lam(p) - lamSeed) / Math.Max(Math.Sqrt(g1 * g1 + g2 * g2), 1e-9);
                worst = Math.Max(worst, off); sum += off;
            }
            (max ? maxLines : minLines).Add(Line(l, 2));
            lineRows.Add(new { family = max ? "max" : "min", margin, points = l.Length, closed = (l[0] - l[^1]).Length < 1e-9, worstOff = R3(worst / edge), meanOff = R3(sum / l.Length / edge), nearestUmbilic = R3(l.Min(p => um.Min(u => (u - p).Length)) / edge) });
        }
    }
    // the umbilic connection: the max-curvature line from (a, 0, 0) runs along the ellipse y = 0 and leaves it at the umbilic
    int seedA = Enumerable.Range(0, ell.VertexCount).OrderBy(i => (ell.Vertices[i] - new Vec3d(EA, 0, 0)).Length).First();
    var conn = Mite.Core.Streamlines.CurvatureStreamlines.Trace(ell, new[] { seedA }, pc, new Mite.Core.Streamlines.CurvatureStreamlines.Options { UseMaxCurvature = true, MaxSteps = 4000 })[0];
    int at = Enumerable.Range(0, conn.Length).OrderBy(i => (conn[i] - new Vec3d(EA, 0, 0)).Length).First();
    double maxY = 0; var leaveZ = new List<double>(); var connPts = new List<Vec3d>();
    foreach (int dir in new[] { 1, -1 })
    {
        double lastZ = 0;
        for (int i = at; i >= 0 && i < conn.Length; i += dir) { if (Math.Abs(conn[i].Y) > 0.5 * edge) break; lastZ = Math.Abs(conn[i].Z); if (lastZ < um[0].Z - edge) maxY = Math.Max(maxY, Math.Abs(conn[i].Y)); if (dir > 0) connPts.Add(conn[i]); else connPts.Insert(0, conn[i]); }
        leaveZ.Add(lastZ);
    }
    double[][][] EllWire() { var w = SphereWire(8); foreach (var l in w) foreach (var q in l) { q[0] = R3(q[0] * EA); q[1] = R3(q[1] * EB); q[2] = R3(q[2] * EC); } return w; }
    scenes["umbilics"] = new { wire = EllWire(), max = maxLines.ToArray(), min = minLines.ToArray(), connection = Line(connPts.ToArray(), 2), located = located.Select(d => P(d.Point)).ToArray(), exact = um.Select(P).ToArray() };
    numbers["umbilics"] = new
    {
        a = EA, b = EB, c = EC, vertices = ell.VertexCount, edge = R3(edge), ms = t.ElapsedMilliseconds,
        errK = R3(errK), errH = R3(errH), dirErr = R3(dirErr),
        located = located.Length, lemons = located.Count(d => d.Index > 0), stars = located.Count(d => d.Index < 0), indexSum = Umbilics.IndexSum(located),
        deltaAgrees = located.Count(d => Math.Sign(d.Delta) == Math.Sign(d.Index)),
        offsets = offsets.Select(R3).ToArray(), x0 = R3(um[0].X), z0 = R3(um[0].Z), kUmbilic = R3(AnalyticShapes.EllipsoidUmbilicCurvature(EA, EB, EC)),
        torusPoints = locTorus.Length, torusSum = Umbilics.IndexSum(locTorus),
        monkeyPoints = locMonkey.Length, monkeyIndex = locMonkey.Length == 1 ? locMonkey[0].Index : 0, monkeyOffset = R3(locMonkey.Length == 1 ? locMonkey[0].Point.Length / MeanEdgeOf(monkey) : 99),
        revoPoints = locRevo.Length, revoSum = Umbilics.IndexSum(locRevo), revoMinZ = R3(locRevo.Length == 0 ? 0 : locRevo.Min(d => Math.Abs(d.Point.Z))),
        regionFlagged = Umbilics.Find(pc, 0.05).Length,
        lines = lineRows,
        connMaxY = R3(maxY / edge), connLeaveZ = leaveZ.Select(z => R3(z)).ToArray(), connLeaveErr = R3(leaveZ.Max(z => Math.Abs(z - um[0].Z)) / edge)
    };
}

// ---------- Geodesic Path: exact geodesics by edge flips (FlipOut) on a polyhedral cone and a saddle vertex ----------
{
    // the cone is intrinsically flat except at the apex: its development is exact
    double alpha = 0.6; int n = 48;
    var cone = TestMeshes.CreatePolyCone(alpha, 2.0, n, 16, 0.6, 7);
    var (_, om) = TestMeshes.ConeAngles(alpha, n);
    var rnd = new Random(11);
    var from = new List<Vec3d>(); var to = new List<Vec3d>(); var exact = new List<double>();
    for (int k = 0; k < 200; k++)
    {
        int i = rnd.Next(n), j = rnd.Next(n);
        double p1 = rnd.NextDouble(), p2 = rnd.NextDouble(), q1 = rnd.NextDouble(), q2 = rnd.NextDouble();
        double s1 = (0.05 + 0.85 * rnd.NextDouble()) * 2.0 / (p1 + p2), s2 = (0.05 + 0.85 * rnd.NextDouble()) * 2.0 / (q1 + q2);
        from.Add(TestMeshes.ConePoint(alpha, n, i, p1 * s1, p2 * s1)); to.Add(TestMeshes.ConePoint(alpha, n, j, q1 * s2, q2 * s2));
        exact.Add(TestMeshes.ConeDistance(alpha, n, TestMeshes.ConeUnfold(alpha, n, i, p1 * s1, p2 * s1), TestMeshes.ConeUnfold(alpha, n, j, q1 * s2, q2 * s2)));
    }
    var t = Stopwatch.StartNew();
    var res = FlipGeodesic.Compute(cone, from, to, out _)!;
    t.Stop();
    var projC = new MeshProjection(cone);
    var tOld = Stopwatch.StartNew();
    var old = new List<double>();
    for (int k = 0; k < 40; k++) old.Add(ShortestPath.Compute(projC, from[k], to[k])!.Value.Length);
    tOld.Stop();
    double maxErr = Enumerable.Range(0, 200).Max(k => Math.Abs(res[k]!.Length - exact[k]));
    double oldErr = Enumerable.Range(0, 40).Max(k => Math.Abs(old[k] - exact[k]) / exact[k]);
    int throughApex = res.Count(r => r!.Vertices.Any(v => v.Length < 1e-12));
    // the demo pair: two points either side of the apex, near it
    int i0 = 3, i1 = i0 + 22;
    var pa = TestMeshes.ConePoint(alpha, n, i0, 0.3, 0.3); var pb = TestMeshes.ConePoint(alpha, n, i1, 0.45, 0.45);
    var demo = FlipGeodesic.Compute(cone, new[] { pa }, new[] { pb }, out _)![0]!;
    var demoOld = ShortestPath.Compute(projC, pa, pb)!.Value;
    double demoExact = TestMeshes.ConeDistance(alpha, n, TestMeshes.ConeUnfold(alpha, n, i0, 0.3, 0.3), TestMeshes.ConeUnfold(alpha, n, i1, 0.45, 0.45));
    var coneWire = new List<double[][]>();
    for (int i = 0; i < n; i += 2) coneWire.Add(new[] { P(Vec3d.Zero), P(2.0 * TestMeshes.ConeGenerator(alpha, n, i)) });
    for (int r = 2; r <= 16; r += 2) coneWire.Add(Enumerable.Range(0, n + 1).Select(i => P(2.0 * r / 16 * TestMeshes.ConeGenerator(alpha, n, i))).ToArray());
    scenes["geoPathCone"] = new { wire = coneWire.ToArray(), flip = Line(demo.Points), old = Line(demoOld.Points), ends = new[] { P(pa), P(pb) }, apex = new[] { P(Vec3d.Zero) } };
    numbers["geoPathCone"] = new { pairs = 200, maxErr = maxErr, msPerPath = R3(t.Elapsed.TotalMilliseconds / 200), oldMsPerPath = R3(tOld.Elapsed.TotalMilliseconds / 40), oldMaxRel = R3(oldErr * 100), throughApex, converged = res.Count(r => r!.Converged), deficit = R3(2 * Math.PI - om), demoLen = demo.Length, demoExact, demoOld = R3(demoOld.Length), demoThroughApex = R3(pa.Length + pb.Length) };

    // a single saddle vertex (total angle > 2π): the geodesic between opposite sectors runs through it
    var fan = TestMeshes.CreateSaddleFan(4, 0.6);
    var itF = IntrinsicTriangulation.Build(fan.Vertices, fan.Faces, out _)!;
    Vec3d In(int sector, double rr) => rr * (0.5 * fan.Vertices[1 + sector] + 0.5 * fan.Vertices[1 + (sector + 1) % 8]);
    var sa = In(0, 0.7); var sb = In(4, 0.85);
    var rs = FlipGeodesic.Compute(fan, new[] { sa }, new[] { sb }, out _)![0]!;
    var fanEdges = fan.BuildEdges().Select(e => new[] { P(fan.Vertices[e.v0]), P(fan.Vertices[e.v1]) }).ToArray();
    scenes["geoPathSaddle"] = new { wire = fanEdges, flip = Line(rs.Points), ends = new[] { P(sa), P(sb) }, apex = new[] { P(Vec3d.Zero) } };
    numbers["geoPathSaddle"] = new { angle = R3(itF.AngleSum(0) * 180 / Math.PI), length = rs.Length, exact = sa.Length + sb.Length, through = rs.Vertices.Length, minJoint = R3(rs.MinJointAngle * 180 / Math.PI) };
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
    scenes["asymClassic"] = new { a = famA2.Select(l => Line(l, 3)).ToArray(), b = famB2.Select(l => Line(l, 3)).ToArray() };

    // ---------- progress reports (the Message line of the components) ----------
    {
        // every long solver reports its progress between units of work: in [0, 1],
        // never backwards, 1 at the end; the tracers report the covered area
        // (Σ length × spacing / area), the solvers the residual's log decay
        var progressRows = new List<object>();
        static (List<double> f, List<string> stages) Rec(out Mite.Core.ProgressHandler h)
        {
            var f = new List<double>(); var st = new List<string>();
            h = (x, s) => { f.Add(x); st.Add(s); };
            return (f, st);
        }
        static object Row(string name, List<double> f, List<string> st, double coverage, long ms)
        {
            bool mono = true; for (int i = 1; i < f.Count; i++) if (f[i] <= f[i - 1]) mono = false;
            return new { name, reports = f.Count, monotone = mono, first = f.Count > 0 ? Math.Round(f[0], 4) : -1, last = f.Count > 0 ? f[^1] : -1, coverage = double.IsNaN(coverage) ? (double?)null : Math.Round(coverage, 3), stages = string.Join(" → ", st.Distinct()), ms };
        }
        var (fA, sA) = Rec(out var hA);
        var tp = Stopwatch.StartNew();
        var oA = new EvenlySpacedNet.Options { Spacing = 0.15, Progress = hA };
        EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, oA, field.Family2);
        progressRows.Add(Row("Asymptotic Net, saddle fill (family A)", fA, sA, oA.CoverageEstimate, tp.ElapsedMilliseconds));
        var (fW, sW) = Rec(out var hW); tp.Restart();
        var oW = new EvenlySpacedNet.Options { Spacing = 0.15, Layout = NetLayout.WebBorder, Progress = hW };
        EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, oW, field.Family2);
        progressRows.Add(Row("Asymptotic Net, saddle web from the border (family A)", fW, sW, oW.CoverageEstimate, tp.ElapsedMilliseconds));
        var sphereP = TestMeshes.CreateUnitSphere(24);
        var (fG, sG) = Rec(out var hG); tp.Restart();
        var oG = new EvenlySpacedNet.Options { Spacing = 0.2, Progress = hG };
        EvenlySpacedNet.TraceGeodesics(sphereP, 1 + 11 * 48, new Vec3d(0, 0, 1), oG);
        progressRows.Add(Row("Geodesic Net, sphere fill", fG, sG, oG.CoverageEstimate, tp.ElapsedMilliseconds));
        var catP = TestMeshes.CreateCatenoid(1.0, 2.0, 64, 32);
        var webP = AsymptoticWeb.Build(catP, PrincipalCurvature.Compute(catP), -1, new AsymptoticWeb.Options { Spacing = 0.3 });
        var (fO, sO) = Rec(out var hO); tp.Restart();
        AagWeb.Optimize(catP, webP, new AagWeb.Options { Iterations = 40, Progress = hO });
        progressRows.Add(Row("Asymptotic Web, AAG on the catenoid", fO, sO, double.NaN, tp.ElapsedMilliseconds));
        var gridP = TestMeshes.CreateQuadGrid(20, 20, 1.0);
        var (fD, sD) = Rec(out var hD); tp.Restart();
        DynamicRelaxation.Compute(gridP, gridP.BuildBoundaryVertexFlags(), new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000, Progress = hD });
        progressRows.Add(Row("Dynamic Relaxation, hanging net", fD, sD, double.NaN, tp.ElapsedMilliseconds));
        // Esc: a cancel probe that fires after the third report stops the fill at the next candidate
        var (fC, sC) = Rec(out var hC); tp.Restart();
        var oC = new EvenlySpacedNet.Options { Spacing = 0.15, Progress = hC, ShouldCancel = () => fC.Count >= 3 };
        var cut = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, oC, field.Family2);
        tp.Stop();
        numbers["progress"] = new { rows = progressRows, cancelled = new { curves = cut.Count, full = famA.Count, cancelled = oC.Cancelled, last = fC.Count > 0 ? fC[^1] : -1, ms = tp.ElapsedMilliseconds, fullMs = t.ElapsedMilliseconds } };
    }
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
    // Panels of the nets (NetFaces): the asymptotic saddle net is skew, the curvature-line net nearly planar;
    // on the hyperboloid the meridian / parallel panels are planar exactly and the ruling panels' planarity ∝ h
    {
        object Census(NetFaces.Result r) => new { faces = r.Faces.Count, quads = r.Quads, triangles = r.Triangles, polygons = r.Polygons, lenses = r.Lenses, euler = r.NodeCount - r.EdgeCount + r.Faces.Count + r.Lenses, loops = r.OuterLoops, meanPlanarity = r.MeanPlanarity, maxPlanarity = r.MaxPlanarity, maxDeviation = r.MaxDeviation };
        var fAsym = NetFaces.Build(topo, saddle);
        var conjTopo = NetTopology.Build(conj.FamilyA, conj.FamilyB, NetIntersections.FindAll(conj.FamilyA, conj.FamilyB));
        var fConj = NetFaces.Build(conjTopo, saddle);
        Vec3d[] Samp(Func<double, Vec3d> f, double t0, double t1, int n) { var pts = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) pts[i] = f(t0 + (t1 - t0) * i / n); return pts; }
        List<Vec3d[]> Rulings(int n, int sign) { var list = new List<Vec3d[]>(); for (int i = 0; i < n; i++) { double th = 2 * Math.PI * i / n; list.Add(Samp(z => new Vec3d(Math.Cos(th) - sign * z * Math.Sin(th), Math.Sin(th) + sign * z * Math.Cos(th), z), -0.8, 0.8, 2)); } return list; }
        var hypRef = TestMeshes.CreateHyperboloid(1, 1, 0.8, 64, 32);
        NetFaces.Result HypFaces(List<Vec3d[]> fa, List<Vec3d[]> fb) => NetFaces.Build(NetTopology.Build(fa, fb, NetIntersections.FindAll(fa, fb)), hypRef);
        var r24 = HypFaces(Rulings(24, 1), Rulings(24, -1)); var r48 = HypFaces(Rulings(48, 1), Rulings(48, -1));
        var mer = new List<Vec3d[]>(); var par = new List<Vec3d[]>();
        for (int i = 0; i < 24; i++) { double th = 2 * Math.PI * i / 24; mer.Add(Samp(z => new Vec3d(Math.Sqrt(1 + z * z) * Math.Cos(th), Math.Sqrt(1 + z * z) * Math.Sin(th), z), -0.8, 0.8, 40)); }
        for (int j = 0; j < 7; j++) { double z = -0.8 + 1.6 * j / 6, rr = Math.Sqrt(1 + z * z); par.Add(Samp(tt => new Vec3d(rr * Math.Cos(tt), rr * Math.Sin(tt), z), 0, 2 * Math.PI, 96)); }
        var lines = HypFaces(mer, par);
        numbers["panels"] = new { asym = Census(fAsym), conj = Census(fConj), rulings24 = Census(r24), rulings48 = Census(r48), curvatureLines = Census(lines) };
    }
    // lath sweep of one asymptotic curve, upright
    var longest = famA.OrderByDescending(ArcLen).First();
    var swp = StripSweep.Sweep(proj, longest, new LathProfile(0.08, 0.012, true))!.Value;
    double vol = 0; foreach (var f in swp.Mesh.Faces) for (int i = 1; i + 1 < f.Length; i++) vol += Vec3d.Dot(swp.Mesh.Vertices[f[0]], Vec3d.Cross(swp.Mesh.Vertices[f[i]], swp.Mesh.Vertices[f[i + 1]])) / 6;
    scenes["sweep"] = new { wire = SaddleWire(40, 2.0, 5), mesh = new { verts = swp.Mesh.Vertices.Select(P).ToArray(), faces = swp.Mesh.Faces }, center = Line(longest, 2) };
    numbers["sweep"] = new { stations = swp.Centers.Length, faces = swp.Mesh.FaceCount, volume = vol, expected = R3(0.08 * 0.012 * ArcLen(longest)) };
    // round and custom (triangular) sections along the same lath: volume = section area × length
    var roundProfile = LathProfile.Round(0.05, 24);
    var swpR = StripSweep.Sweep(proj, longest, roundProfile)!.Value;
    double volR = 0; foreach (var f in swpR.Mesh.Faces) for (int i = 1; i + 1 < f.Length; i++) volR += Vec3d.Dot(swpR.Mesh.Vertices[f[0]], Vec3d.Cross(swpR.Mesh.Vertices[f[i]], swpR.Mesh.Vertices[f[i + 1]])) / 6;
    var triProfile = LathProfile.Custom(new (double, double)[] { (-0.04, 0), (0.04, 0), (0, 0.06) }, upright: true);
    var swpT = StripSweep.Sweep(proj, longest, triProfile)!.Value;
    double volT = 0; foreach (var f in swpT.Mesh.Faces) for (int i = 1; i + 1 < f.Length; i++) volT += Vec3d.Dot(swpT.Mesh.Vertices[f[0]], Vec3d.Cross(swpT.Mesh.Vertices[f[i]], swpT.Mesh.Vertices[f[i + 1]])) / 6;
    numbers["sweepSections"] = new { roundVolume = volR, roundExpected = roundProfile.SectionArea() * ArcLen(longest), triVolume = volT, triExpected = triProfile.SectionArea() * ArcLen(longest), roundVerts = swpR.Mesh.VertexCount };
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
    // every lath of the net cut into 1.2 stock (no joints: the cuts are the stock rule alone), greedy and balanced,
    // and the pieces packed into boards: first-fit decreasing against the exact optimum (branch and bound)
    {
        var allLaths = famA.Concat(famB).ToList();
        var greedy = allLaths.SelectMany(l => LathSegmentation.Segment(l, 1.2, 0.0, null, 0.1).Segments.Select(ArcLen)).ToList();
        var balanced = allLaths.SelectMany(l => LathSegmentation.Segment(l, 1.2, 0.0, null, 0.1, true).Segments.Select(ArcLen)).ToList();
        var pg = CuttingStock.Pack(greedy, 1.2, 0.005);
        var pb = CuttingStock.Pack(balanced, 1.2, 0.005);
        int optG = CuttingStock.Optimal(greedy, 1.2, 0.005, out bool exactG, 500_000);
        numbers["cuttingStock"] = new
        {
            laths = allLaths.Count, piecesGreedy = greedy.Count, piecesBalanced = balanced.Count,
            boardsGreedy = pg.Boards.Count, lowerGreedy = pg.LowerBound, optimumGreedy = optG, exactGreedy = exactG, materialGreedy = R3(pg.Material),
            boardsBalanced = pb.Boards.Count, lowerBalanced = pb.LowerBound, materialBalanced = R3(pb.Material),
            shortestGreedy = R3(greedy.Min()), shortestBalanced = R3(balanced.Min()), longestBalanced = R3(balanced.Max()),
            sumGreedy = R3(greedy.Sum()), sumBalanced = R3(balanced.Sum()), lathLength = R3(allLaths.Sum(ArcLen)), splicesGreedy = greedy.Count - allLaths.Count, splicesBalanced = balanced.Count - allLaths.Count,
            ffdBound = Math.Floor(11.0 / 9.0 * optG + 6.0 / 9.0 + 1e-9), maxLoadGreedy = R3(pg.Boards.Max(b => b.Sum(i => greedy[i]) + 0.005 * (b.Count - 1)))
        };
    }
    // Gridshell analysis of the asymptotic net: supports at curve ends, 1 kN/m
    var laths = famA.Concat(famB).Select(l => Sub(l, 0.05)).ToList();
    var sup = new List<Vec3d>(); foreach (var l in laths) foreach (var e in new[] { l[0], l[^1] }) { var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e)); if (proj.IsOnBoundary(h, 1e-4)) sup.Add(e); }
    var t2 = Stopwatch.StartNew();
    var fr = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, new LathProfile(0.08, 0.012, true), new Vec3d(0, 0, -1000));
    t2.Stop();
    // linear buckling of the same net (two modes), timed separately
    var t3 = Stopwatch.StartNew();
    var frB = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, new LathProfile(0.08, 0.012, true), new Vec3d(0, 0, -1000), new FrameAnalysis.Options { BucklingModes = 2 });
    t3.Stop();
    // the same net with the axial forces alone (Euler, as 1.6.0): the flexural–torsional terms can only lower the first factor of a strip in bending
    var frE = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, new LathProfile(0.08, 0.012, true), new Vec3d(0, 0, -1000), new FrameAnalysis.Options { BucklingModes = 2, LateralTorsional = false });
    var defl = new List<double[][]>();
    var nodeOf = new Dictionary<(int, int), int>();
    for (int n = 0; n < fr.NodeMap.Length; n++) foreach (var (c, i) in fr.NodeMap[n]) nodeOf.TryAdd((c, i), n);
    for (int c = 0; c < laths.Count; c++) { var l = new List<double[]>(); for (int i = 0; i < laths[c].Length; i++) { var d = nodeOf.TryGetValue((c, i), out int n) ? fr.Displacements[n] : Vec3d.Zero; l.Add(P(laths[c][i] + 100 * d)); } defl.Add(l.ToArray()); }
    scenes["frame"] = new { original = laths.Select(l => Line(l)).ToArray(), deformed = defl.ToArray(), scale = 100, supports = sup.Select(P).ToArray() };
    numbers["frame"] = new { nodes = fr.Nodes.Length, dof = fr.Nodes.Length * 6, ms = t2.ElapsedMilliseconds, maxDisp = fr.MaxDisplacement, maxUtil = R3(fr.MaxUtilization), supports = fr.SupportNodeCount, elements = fr.Utilization.Length };
    // Joint eccentricity: the same net with family B stacked one lath depth above family A
    {
        var profile = new LathProfile(0.08, 0.012, true);
        double depth = profile.NormalHigh - profile.NormalLow;
        var offsets = Enumerable.Range(0, laths.Count).Select(c => c < famA.Count ? 0.0 : depth).ToList();
        var wind = new Vec3d(1000, 0, 0);
        var w0 = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, profile, wind);
        var w1 = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, profile, wind, new FrameAnalysis.Options { AxisOffsets = offsets });
        var g1 = FrameAnalysis.Compute(saddle, laths, xs.Select(x => x.Point).ToList(), sup, profile, new Vec3d(0, 0, -1000), new FrameAnalysis.Options { AxisOffsets = offsets });
        double TorsionOf(FrameAnalysis.Result r, bool lower) => Enumerable.Range(0, r.Torsion.Length).Where(i => (r.ElementSource[i].Curve < famA.Count) == lower).Select(i => r.Torsion[i]).DefaultIfEmpty(0).Max();
        // the exact L-frame: a one-element cantilever A (4 m, flat 100 × 20 mm, axis on the surface) with a 2 m lath B
        // on its tip 50 mm above it, 300 N/m along B: B pulls q·b on A's tip through the stack → torque e·q·b in A
        var flat = new LathProfile(0.1, 0.02, false);
        var plane = TestMeshes_QuadGrid(4, 4, 10.0);
        const double L = 4.0, b = 2.0, q = 300.0, e = 0.05;
        var la = new[] { new Vec3d(2, 3, 0), new Vec3d(2 + L, 3, 0) };
        var lb = Enumerable.Range(0, 9).Select(i => new Vec3d(2 + L, 3 + b * i / 8.0, 0)).ToArray();
        var lf = FrameAnalysis.Compute(plane, new[] { la, lb }, new[] { lb[0] }, new[] { la[0] }, flat, new Vec3d(0, q, 0), new FrameAnalysis.Options { AxisOffsets = new[] { 0.0, e } });
        var sec = flat.SectionProperties();
        double G = 11e9 / 2.6, T = e * q * b, phi = T * L / (G * sec.J);
        numbers["frameStack"] = new
        {
            depth, eccentricity = w1.Eccentricity, joints = w1.JointCount,
            windDisp0 = R3(w0.MaxDisplacement * 1000), windDisp1 = R3(w1.MaxDisplacement * 1000), windUtil0 = R3(w0.MaxUtilization), windUtil1 = R3(w1.MaxUtilization),
            windTorsionLower0 = R3(TorsionOf(w0, true)), windTorsionLower1 = R3(TorsionOf(w1, true)), windTorsionUpper1 = R3(TorsionOf(w1, false)),
            gravUtil0 = R3(fr.MaxUtilization), gravUtil1 = R3(g1.MaxUtilization), gravDisp1 = R3(g1.MaxDisplacement * 1000),
            momentError0 = w0.MomentEquilibriumError, momentError1 = w1.MomentEquilibriumError, forceError1 = w1.EquilibriumError,
            lTorque = lf.Torsion[0], lTorqueExact = T, lTwist = phi, lMomentError = lf.MomentEquilibriumError, lReactionTorque = Math.Abs(lf.ReactionMoments[lf.SupportNodes[0]].X),
        };
    }
    // first buckling mode of the net, drawn at 5 % of the net's size
    var modeLines = new List<double[][]>();
    if (frB.BucklingModes.Length > 0)
    {
        var mode = frB.BucklingModes[0];
        var (bmn, bmx) = saddle.BoundingBox();
        double amp = 0.05 * (bmx - bmn).Length;
        for (int c = 0; c < laths.Count; c++) { var l = new List<double[]>(); for (int i = 0; i < laths[c].Length; i++) { var d = nodeOf.TryGetValue((c, i), out int n) ? mode[n] : Vec3d.Zero; l.Add(P(laths[c][i] + amp * d)); } modeLines.Add(l.ToArray()); }
    }
    scenes["frameMode"] = new { original = laths.Select(l => Line(l)).ToArray(), mode = modeLines.ToArray(), supports = sup.Select(P).ToArray() };
    numbers["frameBuckling"] = new { factors = frB.BucklingFactors.Select(f => R3(f)).ToArray(), missed = frB.BucklingMissed, iterations = frB.BucklingIterations, converged = frB.BucklingConverged, ms = t3.ElapsedMilliseconds - t2.ElapsedMilliseconds, minAxial = R3(frB.AxialForce.Min()), compressed = frB.AxialForce.Count(a => a < 0), elements = frB.AxialForce.Length,
        eulerFactors = frE.BucklingFactors.Select(f => R3(f)).ToArray(), eulerMissed = frE.BucklingMissed };
    // Euler checks with closed forms: fixed-free column P_cr = π²EI/(4L²) (Timoshenko & Gere §2.2), a round bar's double
    // eigenvalue, and a fixed-base portal frame against the exact beam-column stability functions (Chen & Lui 1987 ch. 5)
    {
        double E = 11e9, L = 3.0, w = 0.1, th = 0.01;
        var flat = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        FrameAnalysis.Result Column(int n, LathProfile prof, int modes) { var lath = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) lath[i] = new Vec3d(L * i / n, 5, 0.01); return FrameAnalysis.Compute(flat, new[] { lath }, null, new[] { new Vec3d(0, 5, 0.01) }, prof, Vec3d.Zero, new FrameAnalysis.Options { E = E, BucklingModes = modes, BucklingTolerance = 1e-10, PointLoads = new List<(Vec3d, Vec3d)> { (new Vec3d(L, 5, 0.01), new Vec3d(-1.0, 0, 0)) } }); }
        double Imin = w * th * th * th / 12, exact = Math.PI * Math.PI * E * Imin / (4 * L * L);
        var errs = new[] { 2, 4, 8, 16 }.Select(n => Column(n, new LathProfile(w, th, false), 1).BucklingFactors[0] / exact - 1).ToArray();
        double d = 0.02, Ir = Math.PI * Math.Pow(d, 4) / 64, exactR = Math.PI * Math.PI * E * Ir / (4 * L * L);
        var rb = Column(8, LathProfile.Round(d), 2);
        // portal: columns along x (h = 3) at y = 2 and y = 5, beam along y at x = 3; bases fixed; P = 1 on each column top
        double h = 3.0, lb = 3.0, EI = E * Imin;
        double Det(double Pc) { double phi = h * Math.Sqrt(Pc / EI), Phi = 2 - 2 * Math.Cos(phi) - phi * Math.Sin(phi); double f1 = phi * phi * phi * Math.Sin(phi) / (12 * Phi), f2 = phi * phi * (1 - Math.Cos(phi)) / (6 * Phi), f3 = phi * (Math.Sin(phi) - phi * Math.Cos(phi)) / (4 * Phi); double kvv = 24 * f1 * EI / (h * h * h), kvt = -6 * f2 * EI / (h * h), ktt = 4 * f3 * EI / h + 4 * EI / lb, k12 = 2 * EI / lb; return kvv * (ktt * ktt - k12 * k12) - kvt * (kvt * ktt - k12 * kvt) + kvt * (kvt * k12 - ktt * kvt); }
        double pe = Math.PI * Math.PI * EI / (h * h), step = pe / 2000, a = step, b = a; for (int i = 0; i < 20000; i++) { b = a + step; double fb = Det(b); if (fb <= 0 || double.IsNaN(fb)) break; a = b; } for (int i = 0; i < 200; i++) { double m = 0.5 * (a + b); if (Det(m) > 0) a = m; else b = m; }
        double exactPortal = 0.5 * (a + b);
        Vec3d[] Seg(Vec3d p0, Vec3d p1, int n) { var pts = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) pts[i] = p0 + (double)i / n * (p1 - p0); return pts; }
        var portalLaths = new List<Vec3d[]> { Seg(new Vec3d(0, 2, 0.01), new Vec3d(h, 2, 0.01), 8), Seg(new Vec3d(0, 2 + lb, 0.01), new Vec3d(h, 2 + lb, 0.01), 8), Seg(new Vec3d(h, 2, 0.01), new Vec3d(h, 2 + lb, 0.01), 8) };
        var portal = FrameAnalysis.Compute(flat, portalLaths, null, new[] { new Vec3d(0, 2, 0.01), new Vec3d(0, 2 + lb, 0.01) }, new LathProfile(w, th, true), Vec3d.Zero, new FrameAnalysis.Options { E = E, BucklingModes = 1, BucklingTolerance = 1e-10, PointLoads = new List<(Vec3d, Vec3d)> { (new Vec3d(h, 2, 0.01), new Vec3d(-1, 0, 0)), (new Vec3d(h, 2 + lb, 0.01), new Vec3d(-1, 0, 0)) } });
        // lateral–torsional buckling of the upright strip (Timoshenko & Gere 1961 ch. 6; FrameAnalysis class remarks): a vertical
        // tip load on the clamped strip, P_cr = 4.0126 √(E I G J)/L² (eq. 6-30), and a uniform load, (q L)_cr = 12.8538 √(E I G J)/L² (eq. 6-33)
        var up = new LathProfile(w, th, true); var upSec = up.SectionProperties();
        double rig = Math.Sqrt(E * Math.Min(upSec.IA, upSec.IB) * (E / 2.6) * upSec.J);
        FrameAnalysis.Result Strip(int n, double tip, double q, bool lateral) { var lath = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) lath[i] = new Vec3d(2 + L * i / n, 5, 0.01); return FrameAnalysis.Compute(flat, new[] { lath }, null, new[] { lath[0] }, up, new Vec3d(0, 0, q), new FrameAnalysis.Options { E = E, BucklingModes = 1, BucklingTolerance = 1e-10, LateralTorsional = lateral, PointLoads = tip != 0 ? new List<(Vec3d, Vec3d)> { (lath[n], new Vec3d(0, 0, tip)) } : null }); }
        double ltbTipExact = 4.0125993435793 * rig / (L * L), ltbLoadExact = 12.853763321383 * rig / (L * L);
        var ltbTipErr = new[] { 4, 8, 16, 32 }.Select(n => Math.Abs(Strip(n, -1.0, 0, true).BucklingFactors[0] / ltbTipExact - 1)).ToArray();
        var ltbLoad = Strip(32, 0, -1.0, true);
        var ltbOff = Strip(16, -1.0, 0, false);
        numbers["buckling"] = new { cantileverErr = errs, cantileverExact = exact, roundFactors = rb.BucklingFactors, roundExact = exactR, roundMissed = rb.BucklingMissed, portalExact = exactPortal, portalFactor = portal.BucklingFactors.Length > 0 ? portal.BucklingFactors[0] : 0, portalExactDimless = exactPortal * h * h / EI, portalMissed = portal.BucklingMissed,
            ltbTipErr, ltbTipExact, ltbLoadFactor = ltbLoad.BucklingFactors.Length > 0 ? ltbLoad.BucklingFactors[0] * L : 0, ltbLoadExact, ltbLoadMissed = ltbLoad.BucklingMissed, ltbOffFactors = ltbOff.BucklingFactors.Length };

    }
    // ---- Per-family profiles: the asymptotic–geodesic hybrid (Schling, Wang, Hoyer & Pottmann 2022; Schling, Wan, Wang & D'Acunto 2023)
    // 1. Closed form: two simply supported beams crossing at mid-span, pinned, P at the crossing — A upright, B flat
    //    (same 100 × 20 mm strip). δ = P / (kA + kB), k = 48 E I / L³; each lath rated with its own section modulus.
    {
        var grid = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double Lh = 8.0, Pload = 400.0, w = 0.1, tt = 0.02, E = 11e9, allow = 20e6;
        Vec3d[] Seg(Vec3d a0, Vec3d b0, int n) { var pts = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) pts[i] = a0 + (double)i / n * (b0 - a0); return pts; }
        var la = Seg(new Vec3d(1, 5, 0), new Vec3d(9, 5, 0), 16); var lb = Seg(new Vec3d(5, 1, 0), new Vec3d(5, 9, 0), 16);
        var pa = new LathProfile(w, tt, true); var pb = new LathProfile(w, tt, false);
        var rh = FrameAnalysis.Compute(grid, new[] { la, lb }, new[] { new Vec3d(5, 5, 0) }, new[] { la[0], la[^1], lb[0], lb[^1] }, new[] { pa, pb }, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, AllowableStress = allow, Support = FrameAnalysis.SupportKind.Pinned, PointLoads = new[] { (new Vec3d(5, 5, 0), new Vec3d(0, 0, -Pload)) } });
        double IA = tt * w * w * w / 12, IB = w * tt * tt * tt / 12, kA = 48 * E * IA / (Lh * Lh * Lh), kB = 48 * E * IB / (Lh * Lh * Lh);
        double delta = Pload / (kA + kB), PA = kA * delta, PB = kB * delta;
        double uA = 0, uB = 0; for (int e = 0; e < rh.Utilization.Length; e++) { if (rh.ElementSource[e].Curve == 0) uA = Math.Max(uA, rh.Utilization[e]); else uB = Math.Max(uB, rh.Utilization[e]); }
        numbers["hybridCross"] = new { delta = rh.MaxDisplacement, deltaTheory = delta, shareA = PA / Pload, utilA = uA, utilATheory = (PA * Lh / 4) / (IA / (w / 2)) / allow, utilB = uB, utilBTheory = (PB * Lh / 4) / (IB / (tt / 2)) / allow, torsion = rh.Torsion.Max() };
    }
    // 2. The AGH net on the catenoid: asymptotic family A upright (100 × 12), meridian geodesics B flat (100 × 12) —
    //    kn(A) ≈ 0, kg(B) ≈ 0, kn(B) = 1/cosh² z; every crossing a threaded joint (slot in A); frame with both sections.
    {
        const int segC = 96, rowsC = 48;
        var cat = TestMeshes.CreateCatenoid(1.0, 2.0, segC, rowsC);
        var projCat = new MeshProjection(cat);
        var pcC = PrincipalCurvature.Compute(cat, 2);
        var fieldC = AsymptoticCurves.ComputeDirections(pcC, cat, 15.0);
        var aghA = EvenlySpacedNet.TraceField(cat, fieldC.Family1, fieldC.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, fieldC.Family2);
        int waist = (rowsC / 2) * segC;
        var seedsC = Enumerable.Range(0, segC / 6).Select(i => waist + 6 * i).ToArray();
        var aghB = GeodesicCurves.Trace(cat, seedsC, seedsC.Select(_ => new Vec3d(0, 0, 1)).ToArray(), new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 4000 });
        var pUp = new LathProfile(0.1, 0.012, true); var pFlat = new LathProfile(0.1, 0.012, false);
        var lathA = LathAnalysis.Analyze(cat, aghA, new LathAnalysis.Options { Profile = pUp, Window = 0.1 });
        var lathB = LathAnalysis.Analyze(cat, aghB, new LathAnalysis.Options { Profile = pFlat, Window = 0.1 });
        double Interior(double[] v) => v.Length > 10 ? v.Skip(5).Take(v.Length - 10).Select(Math.Abs).Max() : v.Select(Math.Abs).Max();
        double knErr = 0, knMean = 0; int knCnt = 0;
        for (int c = 0; c < aghB.Count; c++) for (int i = 5; i + 5 < aghB[c].Length; i++) { double truth = 1.0 / Math.Pow(Math.Cosh(aghB[c][i].Z), 2); knErr = Math.Max(knErr, Math.Abs(Math.Abs(lathB[c].NormalCurvature[i]) - truth)); knMean += Math.Abs(lathB[c].NormalCurvature[i]); knCnt++; }
        var xsH = NetIntersections.Find(aghA, aghB);
        int threaded = 0, lapJ = 0, apart = 0; double slotDepth = 0;
        foreach (var x in xsH)
        {
            var ta = NetIntersections.TangentAt(aghA, x, false); var tb = NetIntersections.TangentAt(aghB, x, true);
            var n = projCat.ClosestPoint(x.Point, projCat.NearestVertexGlobal(x.Point)).SmoothNormal;
            if (!JointGeometry.TryBuildNotches(x.Point, ta, tb, n, pUp, pFlat, 0.5, 0.0, out var na, out _, out var kind)) continue;
            if (kind == JointKind.SlotInA) { threaded++; slotDepth = Math.Max(slotDepth, 2 * na.HalfZ); } else if (kind == JointKind.Lap) lapJ++; else apart++;
        }
        // frame: A upright + B flat vs all upright, rim ends fixed, 1 kN/m
        var lathsH = aghA.Concat(aghB).Select(l => Sub(l, 0.05)).ToList();
        var profH = aghA.Select(_ => pUp).Concat(aghB.Select(_ => pFlat)).ToList();
        var supH = new List<Vec3d>(); foreach (var l in lathsH) foreach (var e in new[] { l[0], l[^1] }) { var h = projCat.ClosestPoint(e, projCat.NearestVertexGlobal(e)); if (projCat.IsOnBoundary(h, 1e-4)) supH.Add(e); }
        var jH = xsH.Select(x => x.Point).ToList();
        var tHyb = Stopwatch.StartNew();
        var frH = FrameAnalysis.Compute(cat, lathsH, jH, supH, profH, new Vec3d(0, 0, -1000));
        tHyb.Stop();
        var frU = FrameAnalysis.Compute(cat, lathsH, jH, supH, pUp, new Vec3d(0, 0, -1000));
        double peakA = 0, peakB = 0, peakAU = 0, peakBU = 0;
        for (int e = 0; e < frH.Utilization.Length; e++) { if (frH.ElementSource[e].Curve < aghA.Count) peakA = Math.Max(peakA, frH.Utilization[e]); else peakB = Math.Max(peakB, frH.Utilization[e]); }
        for (int e = 0; e < frU.Utilization.Length; e++) { if (frU.ElementSource[e].Curve < aghA.Count) peakAU = Math.Max(peakAU, frU.Utilization[e]); else peakBU = Math.Max(peakBU, frU.Utilization[e]); }
        var nodeOfH = new Dictionary<(int, int), int>();
        for (int n = 0; n < frH.NodeMap.Length; n++) foreach (var (c, i) in frH.NodeMap[n]) nodeOfH.TryAdd((c, i), n);
        var deflH = new List<double[][]>();
        for (int c = 0; c < lathsH.Count; c++) { var l = new List<double[]>(); for (int i = 0; i < lathsH[c].Length; i++) { var d = nodeOfH.TryGetValue((c, i), out int n) ? frH.Displacements[n] : Vec3d.Zero; l.Add(P(lathsH[c][i] + 100 * d)); } deflH.Add(l.ToArray()); }
        scenes["jointsAgh"] = new { wire = Edges(cat, 6), a = aghA.Select(l => Line(l, 3)).ToArray(), b = aghB.Select(l => Line(l, 3)).ToArray(), nodes = xsH.Select(x => P(x.Point)).ToArray() };
        scenes["frameAgh"] = new { wire = Edges(cat, 6), originalA = aghA.Select(l => Line(l, 3)).ToArray(), originalB = aghB.Select(l => Line(l, 3)).ToArray(), deformed = deflH.ToArray(), scale = 100, supports = supH.Select(P).ToArray() };
        numbers["agh"] = new { lathsA = aghA.Count, lathsB = aghB.Count, knA = R3(lathA.Max(r => Interior(r.NormalCurvature))), knAMean = R3(lathA.Average(r => r.NormalCurvature.Select(Math.Abs).Average())), kgB = R3(lathB.Max(r => Interior(r.GeodesicCurvature))), knBMean = R3(knMean / Math.Max(1, knCnt)), knBErr = R3(knErr), crossings = xsH.Count, threaded, lap = lapJ, apart, slotDepth = R3(slotDepth),
            frameMs = tHyb.ElapsedMilliseconds, maxDisp = R3(frH.MaxDisplacement * 1000), maxDispUniform = R3(frU.MaxDisplacement * 1000), peakA = R3(peakA), peakB = R3(peakB), peakAUniform = R3(peakAU), peakBUniform = R3(peakBU), supports = frH.SupportNodeCount, elements = frH.Utilization.Length };
    }
}

static double ArcAt(Vec3d[] line, double[] arc, Vec3d p)
{
    double best = double.MaxValue, bestArc = 0;
    for (int i = 0; i + 1 < line.Length; i++) { var d = line[i + 1] - line[i]; double len = d.Length; if (len < 1e-15) continue; double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - line[i], d) / (len * len))); double dist = (line[i] + t * d - p).LengthSquared; if (dist < best) { best = dist; bestArc = arc[i] + t * len; } }
    return bestArc;
}
static Vec3d[] Sub(Vec3d[] l, double spacing) => ShortestPath.Resample(l, spacing);
static MeshData TestMeshes_QuadGrid(int nx, int ny, double size)
{
    var v = new List<Vec3d>(); var f = new List<int[]>();
    for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) v.Add(new Vec3d(size * i / nx, size * j / ny, 0));
    for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) f.Add(new[] { j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i });
    return new MeshData(v.ToArray(), f.ToArray());
}
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

    // Guide curve: 45° helices seeded every Spacing along the circle z = 0 (a
    // geodesic, so the transported direction keeps 45°); n = round(2π / S)
    // nodes, perpendicular spacing 2π/n · sin 45°
    {
        var guide = Enumerable.Range(0, 181).Select(i => { double a = 2 * Math.PI * i / 180; return new Vec3d(Math.Cos(a), Math.Sin(a), 0); }).ToArray();
        guide[180] = guide[0];
        double al = Math.PI / 4, S = 0.3;
        var og = new EvenlySpacedNet.Options { Spacing = S, Guide = guide, StepSize = 0.02 };
        var tg = Stopwatch.StartNew();
        var gh = EvenlySpacedNet.TraceGeodesics(cyl, seedC, new Vec3d(0, Math.Sin(al), Math.Cos(al)), og);
        tg.Stop();
        int n = (int)Math.Round(2 * Math.PI / S);
        double rise = 0, through = 0;
        foreach (var c in gh)
        {
            double th = Math.Atan2(c[0].Y, c[0].X), acc = 0;
            for (int i = 1; i < c.Length; i++) { double tt = Math.Atan2(c[i].Y, c[i].X), d = tt - th; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; acc += d; th = tt; rise = Math.Max(rise, Math.Abs(Math.Abs(c[i].Z - c[0].Z) - Math.Abs(acc) / Math.Tan(al))); }
        }
        foreach (var p in og.GuideNodes) through = Math.Max(through, gh.Min(c => { double b = double.MaxValue; for (int i = 0; i + 1 < c.Length; i++) { var a = c[i]; var ab = c[i + 1] - a; double l2 = ab.LengthSquared; double tt = l2 < 1e-30 ? 0 : Math.Max(0, Math.Min(1, Vec3d.Dot(p - a, ab) / l2)); b = Math.Min(b, (a + tt * ab - p).Length); } return b; }));
        var phases = gh.Select(c => { var m = c.OrderBy(q => Math.Abs(q.Z)).First(); double th = Math.Atan2(m.Y, m.X) - m.Z * Math.Tan(al); return (th % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI); }).OrderBy(x => x).ToList();
        var gaps = phases.Select((k, i) => ((i + 1 < phases.Count ? phases[i + 1] : phases[0] + 2 * Math.PI) - k) * Math.Sin(al)).ToList();
        var eg = Ends(gh, projC);
        scenes["cylGuide"] = new { wire = RevolveWire(z => 1.0, -1, 1, 8, 16), curves = gh.Select(l => Line(l, 2)).ToArray(), guide = Line(guide, 1), nodes = og.GuideNodes.Select(q => new[] { q.X, q.Y, q.Z }).ToArray() };
        numbers["cylGuide"] = new { nodes = og.GuideNodes.Count, expectedNodes = n, curves = gh.Count, riseErr = R3(rise), through = R3(through * 1e6) / 1e6, gapMin = R3(gaps.Min()), gapMax = R3(gaps.Max()), gapTheory = R3(2 * Math.PI / n * Math.Sin(al)), spacing = S, boundaryEnds = eg.boundary, ends = eg.ends, ms = tg.ElapsedMilliseconds };
    }
    // ---------------------------------------------------------------- AGG Web
    // (1) the exact cylinder web: helices at ±50° with the rulings as diagonals, (2) the same
    // web with its interior nodes scattered by a tenth of the spacing, optimised back,
    // (3) the cylinder web carried onto a cone (r = 1 + z/4), (4) a ±45° lattice on z = xy
    {
        var swAgg = Stopwatch.StartNew();
        object Quality(AggWeb.Quality q) => new { geo = q.GeodesicError, geoMean = q.MeanGeodesicError, bis = q.BisectorError, asym = q.AsymptoticError, asymMean = q.MeanAsymptoticError, edges = q.AsymptoticEdges, normal = q.SurfaceNormalError, asymSurface = q.SurfaceAsymptoticError, dev = q.MaxDeviation, devMean = q.MeanDeviation };
        double[][][] Fam(IEnumerable<Vec3d[]> lines) => lines.Select(l => Line(l)).ToArray();
        double AxisDeg(IEnumerable<Vec3d[]> lines) { double w = 0; foreach (var l in lines) for (int k = 0; k + 1 < l.Length; k++) { var e = (l[k + 1] - l[k]).Normalized(); w = Math.Max(w, Math.Acos(Math.Min(1, Math.Abs(e.Z))) * 180 / Math.PI); } return w; }

        var cylMesh = TestMeshes.CreateCylinder(1.0, 3.0, 256, 48);
        var cylNet = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 12, 10);
        int rul = cylNet.RulingFamily;
        var exactQ = AggWeb.Measure(cylNet, rul, cylMesh);
        scenes["aggCyl"] = new { wire = RevolveWire(z => 1.0, -1.2, 1.2, 8, 16), a = Fam(cylNet.Chains(true).Select(ch => ch.Select(i => cylNet.Nodes[i]).ToArray())), b = Fam(cylNet.Chains(false).Select(ch => ch.Select(i => cylNet.Nodes[i]).ToArray())), d = Fam(cylNet.DiagonalPolylines(rul)), o = Fam(cylNet.DiagonalPolylines(1 - rul)) };

        var rnd = new Random(7);
        var scattered = cylNet.Nodes.Select((p, i) => cylNet.NeighborsA[i].Length + cylNet.NeighborsB[i].Length < 4 ? p : p + 0.1 * 0.15 * new Vec3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1)).ToList();
        var scatNet = AggWeb.QuadNet.FromNeighbors(scattered, cylNet.NeighborsA, cylNet.NeighborsB);
        var scat = AggWeb.Optimize(cylMesh, scatNet, new AggWeb.Options { Iterations = 40 });
        scenes["aggScatter"] = new { wire = RevolveWire(z => 1.0, -1.2, 1.2, 8, 16), a = Fam(scat.A), b = Fam(scat.B), d = Fam(scat.Asymptotic), o = Fam(scat.Other), before = Fam(scatNet.Chains(true).Concat(scatNet.Chains(false)).Select(ch => ch.Select(i => scatNet.Nodes[i]).ToArray())) };

        var coneMesh = AnalyticShapes.Revolution(z => 1 + 0.25 * z, z => z, -1.6, 1.6, 64, 256, false, false).ToTriangulated();
        var coneStart = AggWeb.QuadNet.Cylinder(1.0, 50, 0.16, 14, 12);
        var cone = AggWeb.Optimize(coneMesh, coneStart, new AggWeb.Options { Iterations = 60, AsymptoticFamily = coneStart.RulingFamily, BorderHold = 0.01 });
        var apex = new Vec3d(0, 0, -4);
        double genWorst = 0, genSum = 0; int genCnt = 0;
        foreach (var l in cone.Asymptotic) for (int k = 0; k + 1 < l.Length; k++) { var e = (l[k + 1] - l[k]).Normalized(); var g = (l[k] - apex).Normalized(); double a = Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(e, g)))) * 180 / Math.PI; genWorst = Math.Max(genWorst, a); genSum += a; genCnt++; }
        scenes["aggCone"] = new { wire = RevolveWire(z => 1 + 0.25 * z, -1.5, 1.5, 8, 16), a = Fam(cone.A), b = Fam(cone.B), d = Fam(cone.Asymptotic), o = Fam(cone.Other), before = Fam(coneStart.Chains(true).Concat(coneStart.Chains(false)).Select(ch => ch.Select(i => coneStart.Nodes[i]).ToArray())) };

        var saddleMesh = AnalyticShapes.Grid((x, y) => new Vec3d(x, y, x * y), -2.4, 2.4, -2.4, 2.4, 120, 120).ToTriangulated();
        double ss = 0.2 / Math.Sqrt(2);
        var saddleStart = AggWeb.QuadNet.FromLattice((i, j) => { double x = (i + j - 14) * ss, y = (i - j) * ss; return new Vec3d(x, y, x * y); }, 15, 15);
        var saddle = AggWeb.Optimize(saddleMesh, saddleStart, new AggWeb.Options { Iterations = 60 });
        scenes["aggSaddle"] = new { wire = GridWire(saddleMesh, 120, 120, 10), a = Fam(saddle.A), b = Fam(saddle.B), d = Fam(saddle.Asymptotic), o = Fam(saddle.Other), before = Fam(saddleStart.Chains(true).Concat(saddleStart.Chains(false)).Select(ch => ch.Select(i => saddleStart.Nodes[i]).ToArray())) };

        numbers["aggWeb"] = new
        {
            nodes = cylNet.Nodes.Count, regular = cylNet.RegularNodes, quads = cylNet.Quads.Count, chains = cylNet.DiagonalChains.Count, conflicts = cylNet.LabelConflicts,
            exact = Quality(exactQ), exactAxisDeg = AxisDeg(cylNet.DiagonalPolylines(rul)), circles = AggWeb.Measure(cylNet, 1 - rul).AsymptoticError,
            scatterStart = Quality(scat.Initial), scatter = Quality(scat.Final), scatterAxisDeg = AxisDeg(scat.Asymptotic), scatterMoved = scat.MaxMove, scatterRadiusErr = scat.Nodes.Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 1)),
            coneStart = Quality(cone.Initial), cone = Quality(cone.Final), coneGenMean = genSum / Math.Max(1, genCnt), coneGenWorst = genWorst, coneMoved = cone.MaxMove,
            saddleStart = Quality(saddle.Initial), saddle = Quality(saddle.Final), saddleMoved = saddle.MaxMove,
            ms = swAgg.ElapsedMilliseconds,
        };
    }

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

// ---------- Minimal-surface catalogue (Studio X submission p. 48 + Schwarz D): asymptotic nets ----------
{
    var p00 = new Vec3d(0, 0, 0); var p10 = new Vec3d(2, 0, 0.8); var p01 = new Vec3d(0.3, 1.5, 0.6); var p11 = new Vec3d(2.2, 1.6, -0.5);
    var catalogue = new (string key, string label, MeshData mesh, double spacing, int wireEvery)[] {
        ("catenoid", "catenoid (concave cylinder)", TestMeshes.CreateCatenoid(1, 2, 64, 32), 0.3, 4),
        ("enneper2", "Enneper, 2-fold", TestMeshes.CreateEnneper(2, 1.0, 24, 72), 0.12, 3),
        ("enneper3", "Enneper, 3-fold", TestMeshes.CreateEnneper(3, 0.9, 24, 72), 0.12, 3),
        ("bilinear", "ruled surface (bilinear patch)", TestMeshes.CreateBilinearPatch(p00, p10, p01, p11, 40, 40), 0.15, 4),
        ("schwarzD", "Schwarz D patch (relaxed)", TestMeshes.CreateSchwarzD(0, Math.PI, 24), 0.25, 9),
    };
    var rows = new List<object>();
    foreach (var (key, label, mesh, spacing, wireEvery) in catalogue)
    {
        var pc = PrincipalCurvature.Compute(mesh);
        var H = MeanCurvature.Compute(mesh).Values; var K = GaussianCurvature.Compute(mesh);
        var bnd = mesh.BuildBoundaryVertexFlags();
        var interior = Enumerable.Range(0, mesh.VertexCount).Where(v => !bnd[v]).ToArray();
        var field = AsymptoticCurves.ComputeDirections(pc);
        var proj = new MeshProjection(mesh);
        var tt = Stopwatch.StartNew();
        var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = spacing }, field.Family2);
        var B = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = spacing }, field.Family1);
        tt.Stop();
        var e = Ends(A.Concat(B).ToList(), proj);
        double orth = 0; int cnt = 0;
        foreach (int v in interior) if (field.Exists[v]) { orth += Math.Abs(90 - AngleDeg(field.Family1[v], field.Family2[v])); cnt++; }
        scenes["asymCat_" + key] = new { label, wire = Edges(mesh, wireEvery), a = A.Select(l => Line(l, 3)).ToArray(), b = B.Select(l => Line(l, 3)).ToArray() };
        rows.Add(new { key, label, a = A.Count, b = B.Count, ends = e.ends, boundaryEnds = e.boundary, tEnds = e.onCurve, floatingEnds = e.floating, maxTurn = R3(e.turn), meanAbsH = R3(interior.Average(v => Math.Abs(H[v]))), meanK1 = R3(interior.Average(v => Math.Abs(pc.K1[v]))), positiveK = interior.Count(v => K[v] > 1e-6), meanOrthDev = R3(cnt > 0 ? orth / cnt : 0), ms = tt.ElapsedMilliseconds, vertices = mesh.VertexCount, spacing });
    }
    numbers["asymCatalogue"] = rows;

    // Family labels by the sign of the geodesic torsion (Beltrami–Enneper:
    // the asymptotic directions twist +√−K and −√−K): exact against closed
    // forms, and no curve changes family next to a flat point
    {
        (int mislabeled, int jumps, int samples, int curves) Purity(MeshData mesh, PrincipalCurvature.Result pc, AsymptoticCurves.DirectionField field, List<Vec3d[]> a, List<Vec3d[]> b)
        {
            var pj = new MeshProjection(mesh);
            int mis = 0, jumps = 0, samples = 0;
            foreach (var (fam, sign) in new[] { (a, 1), (b, -1) })
                foreach (var c in fam)
                {
                    int pos = 0, neg = 0;
                    for (int i = 1; i < c.Length; i++)
                    {
                        var t = c[i] - c[i - 1]; if (t.LengthSquared < 1e-20) continue;
                        int v = pj.NearestVertexGlobal(0.5 * (c[i] + c[i - 1])); if (!field.Exists[v]) continue;
                        double tau = AsymptoticCurves.GeodesicTorsion(pc, v, t.Normalized());
                        if (Math.Abs(tau) < 0.5 * AsymptoticCurves.GeodesicTorsion(pc, v, field.Family1[v])) continue;
                        if (tau > 0) pos++; else neg++;
                    }
                    samples += pos + neg; jumps += Math.Min(pos, neg);
                    if (pos + neg > 0 && (pos >= neg ? 1 : -1) != sign) mis++;
                }
            return (mis, jumps, samples, a.Count + b.Count);
        }

        // monkey saddle z = x³ − 3xy²: exact τg from the Hessian (upward normal)
        var monkey = AnalyticShapes.Build("monkey", 2.0, 1.0, 0, 48);
        var pcM = PrincipalCurvature.Compute(monkey);
        var fM = AsymptoticCurves.ComputeDirections(pcM, monkey, 15.0);
        int monkeyN = 0, monkeyWrong = 0;
        for (int v = 0; v < monkey.VertexCount; v++)
        {
            var q = monkey.Vertices[v];
            if (!fM.Exists[v] || q.X * q.X + q.Y * q.Y < 0.0025) continue;
            double fx = 3 * q.X * q.X - 3 * q.Y * q.Y, fy = -6 * q.X * q.Y, w = Math.Sqrt(1 + fx * fx + fy * fy);
            var nrm = new Vec3d(-fx, -fy, 1) / w;
            double Tau(Vec3d t) { t = t.Normalized(); var u = Vec3d.Cross(nrm, t); return (6 * q.X * t.X * u.X - 6 * q.Y * (t.X * u.Y + t.Y * u.X) - 6 * q.X * t.Y * u.Y) / w; }
            monkeyN++;
            if (!(Tau(fM.Family1[v]) > 0 && Tau(fM.Family2[v]) < 0)) monkeyWrong++;
        }
        // 3-fold Enneper: Hopf differential w dw², "plus" is one family all the way round
        var enn = TestMeshes.CreateEnneper(3, 0.9, 24, 72, out var uvE);
        var pcE = PrincipalCurvature.Compute(enn);
        var fE = AsymptoticCurves.ComputeDirections(pcE, enn, 15.0);
        int onPlus = 0, onMinus = 0;
        for (int v = 0; v < enn.VertexCount; v++)
        {
            if (!fE.Exists[v] || uvE[v].u * uvE[v].u + uvE[v].v * uvE[v].v < 0.01) continue;
            var (pl, mi) = TestMeshes.EnneperAsymptoticDirections(3, uvE[v].u, uvE[v].v);
            if (AngleDeg(fE.Family1[v], pl) < AngleDeg(fE.Family1[v], mi)) onPlus++; else onMinus++;
        }
        // hyperboloid x² + y² − z² = 1: family 1 = the right-handed rulings, τg = +√−K
        var hy = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, 64, 32);
        var pcH = PrincipalCurvature.Compute(hy);
        var fH = AsymptoticCurves.ComputeDirections(pcH, hy, 15.0);
        var bH = hy.BuildBoundaryVertexFlags();
        int hyN = 0, hyWrong = 0; double hyTau = 0;
        for (int v = 0; v < hy.VertexCount; v++)
        {
            if (!fH.Exists[v] || bH[v]) continue;
            var (pl, mi) = TestMeshes.HyperboloidRulings(hy.Vertices[v]);
            hyN++;
            if (AngleDeg(fH.Family1[v], pl) > AngleDeg(fH.Family1[v], mi)) hyWrong++;
            double root = Math.Sqrt(-TestMeshes.HyperboloidGaussianCurvature(hy.Vertices[v].Z));
            hyTau = Math.Max(hyTau, Math.Abs(AsymptoticCurves.GeodesicTorsion(pcH, v, fH.Family1[v]) - root) / root);
        }
        // nets next to flat points and on the triply periodic patch: locked (default) and unlocked tracing
        var netRows = new List<object>();
        foreach (var (shape, layout, label) in new[] { ("enneper3", NetLayout.Fill, "Enneper 3, fill"), ("monkey", NetLayout.WebBorder, "monkey saddle, web"), ("schwarzd", NetLayout.WebBorder, "Schwarz D, web"), ("wave", NetLayout.WebBorder, "wave, web") })
        {
            var m = AnalyticShapes.Build(shape, 0, 0, 0, 48).ToTriangulated();
            var pcN = PrincipalCurvature.Compute(m);
            var fN = AsymptoticCurves.ComputeDirections(pcN, m, 15.0);
            double sp = 0.05 * m.BoundingBoxDiagonal();
            var res = new Dictionary<bool, (int mislabeled, int jumps, int samples, int curves)>();
            List<Vec3d[]> lockedA = new(), lockedB = new();
            foreach (bool lk in new[] { true, false })
            {
                var A = EvenlySpacedNet.TraceField(m, fN.Family1, fN.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, Layout = layout, LockFamily = lk }, fN.Family2);
                var B = EvenlySpacedNet.TraceField(m, fN.Family2, fN.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, Layout = layout, LockFamily = lk }, fN.Family1);
                res[lk] = Purity(m, pcN, fN, A, B);
                if (lk) { lockedA = A; lockedB = B; }
            }
            if (shape == "monkey")
                scenes["asymCat_monkeyWeb"] = new { label = "monkey saddle, web (families by twist)", wire = Edges(m, 4), a = lockedA.Select(l => Line(l, 3)).ToArray(), b = lockedB.Select(l => Line(l, 3)).ToArray() };
            netRows.Add(new { label, curves = res[true].curves, mislabeled = res[true].mislabeled, jumps = res[true].jumps, samples = res[true].samples, unlockedMislabeled = res[false].mislabeled, unlockedJumps = res[false].jumps, unlockedCurves = res[false].curves });
        }
        numbers["asymFam"] = new { monkeyN, monkeyWrong, onPlus, onMinus, hyN, hyWrong, hyTau = R3(hyTau), nets = netRows };
    }

    // Web layout on the catenoid: every curve rim to rim, no T-junctions, and the
    // closed-form check u ∓ v = const (isothermal coordinates, II = −du² + dv²)
    {
        var mesh = TestMeshes.CreateCatenoid(1, 2, 96, 48);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        var tt = Stopwatch.StartNew();
        var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3, Layout = NetLayout.WebBorder }, field.Family2);
        var B = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3, Layout = NetLayout.WebBorder }, field.Family1);
        tt.Stop();
        var e = Ends(A.Concat(B).ToList(), proj);
        double worstOff = 0, worstDir = 0; int nDir = 0; double sumDir = 0;
        foreach (var c in A.Concat(B))
        {
            double u0 = Math.Atan2(c[0].Y, c[0].X), v0 = c[0].Z;
            Vec3d t0 = (c[Math.Min(3, c.Length - 1)] - c[0]).Normalized();
            double ch0 = Math.Cosh(v0), sh0 = Math.Sinh(v0);
            var ru0 = new Vec3d(-ch0 * Math.Sin(u0), ch0 * Math.Cos(u0), 0); var rv0 = new Vec3d(sh0 * Math.Cos(u0), sh0 * Math.Sin(u0), 1);
            bool famA = Math.Abs(Vec3d.Dot(t0, (ru0 + rv0).Normalized())) >= Math.Abs(Vec3d.Dot(t0, (ru0 - rv0).Normalized()));
            double k = famA ? u0 - v0 : u0 + v0;
            foreach (var q in c)
            {
                double u = Math.Atan2(q.Y, q.X), v = q.Z;
                double d = (famA ? u - v : u + v) - k;
                while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                worstOff = Math.Max(worstOff, Math.Abs(d) * Math.Cosh(v) / Math.Sqrt(2.0));
            }
        }
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (!field.Exists[i]) continue;
            var q = mesh.Vertices[i];
            double u = Math.Atan2(q.Y, q.X), v = q.Z, ch = Math.Cosh(v), sh = Math.Sinh(v);
            var ru = new Vec3d(-ch * Math.Sin(u), ch * Math.Cos(u), 0); var rv = new Vec3d(sh * Math.Cos(u), sh * Math.Sin(u), 1);
            double best = Math.Max(Math.Abs(Vec3d.Dot(field.Family1[i], (ru + rv).Normalized())), Math.Abs(Vec3d.Dot(field.Family1[i], (ru - rv).Normalized())));
            double ang = Math.Acos(Math.Min(1.0, best)) * 180 / Math.PI;
            worstDir = Math.Max(worstDir, ang); sumDir += ang; nDir++;
        }
        scenes["asymCat_catenoidWeb"] = new { label = "catenoid, web from the rims", wire = Edges(mesh, 6), a = A.Select(l => Line(l, 3)).ToArray(), b = B.Select(l => Line(l, 3)).ToArray() };
        numbers["asymWeb"] = new { a = A.Count, b = B.Count, ends = e.ends, boundaryEnds = e.boundary, tEnds = e.onCurve, floatingEnds = e.floating, worstOffset = R3(worstOff * 1000) / 1000, edge = R3(proj.AverageEdgeLength), worstDirDeg = R3(worstDir), meanDirDeg = R3(sumDir / Math.Max(1, nDir)), ms = tt.ElapsedMilliseconds };
    }

    // Guide curve on the catenoid: nodes every Spacing along the waist circle
    // (Schling's seed curve), both families through every node, every curve rim
    // to rim on the closed-form lines u ∓ v = const; the same from a circle
    // drawn 0.5 outside the surface, pulled onto it
    {
        var mesh = TestMeshes.CreateCatenoid(1, 2, 96, 48);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double S = 0.3;
        Vec3d[] Circle(double r) { var g = Enumerable.Range(0, 181).Select(i => { double a = 2 * Math.PI * i / 180; return new Vec3d(r * Math.Cos(a), r * Math.Sin(a), 0); }).ToArray(); g[180] = g[0]; return g; }
        var guide = Circle(1.0);
        var oa = new EvenlySpacedNet.Options { Spacing = S, Guide = guide, StepSize = 0.02 };
        var ob = new EvenlySpacedNet.Options { Spacing = S, Guide = guide, StepSize = 0.02 };
        var tt = Stopwatch.StartNew();
        var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, oa, field.Family2);
        var B = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, ob, field.Family1);
        tt.Stop();
        int n = (int)Math.Round(2 * Math.PI / S);
        var e = Ends(A.Concat(B).ToList(), proj);
        double worstOff = 0, through = 0, gapErr = 0;
        double Dist(Vec3d p, Vec3d[] c) { double b = double.MaxValue; for (int i = 0; i + 1 < c.Length; i++) { var a = c[i]; var ab = c[i + 1] - a; double l2 = ab.LengthSquared; double q = l2 < 1e-30 ? 0 : Math.Max(0, Math.Min(1, Vec3d.Dot(p - a, ab) / l2)); b = Math.Min(b, (a + q * ab - p).Length); } return b; }
        foreach (var c in A.Concat(B))
        {
            double u0 = Math.Atan2(c[0].Y, c[0].X), v0 = c[0].Z;
            Vec3d t0 = (c[Math.Min(3, c.Length - 1)] - c[0]).Normalized();
            double ch0 = Math.Cosh(v0), sh0 = Math.Sinh(v0);
            var ru0 = new Vec3d(-ch0 * Math.Sin(u0), ch0 * Math.Cos(u0), 0); var rv0 = new Vec3d(sh0 * Math.Cos(u0), sh0 * Math.Sin(u0), 1);
            bool famA = Math.Abs(Vec3d.Dot(t0, (ru0 + rv0).Normalized())) >= Math.Abs(Vec3d.Dot(t0, (ru0 - rv0).Normalized()));
            double k = famA ? u0 - v0 : u0 + v0;
            foreach (var q in c)
            {
                double u = Math.Atan2(q.Y, q.X), v = q.Z;
                double d = (famA ? u - v : u + v) - k;
                while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                worstOff = Math.Max(worstOff, Math.Abs(d) * Math.Cosh(v) / Math.Sqrt(2.0));
            }
        }
        for (int i = 0; i < oa.GuideNodes.Count; i++)
        {
            var p = oa.GuideNodes[i]; var q = oa.GuideNodes[(i + 1) % oa.GuideNodes.Count];
            double th = Math.Atan2(q.Y, q.X) - Math.Atan2(p.Y, p.X); while (th < 0) th += 2 * Math.PI;
            gapErr = Math.Max(gapErr, Math.Abs(th - 2 * Math.PI / n));
            through = Math.Max(through, Math.Max(A.Min(c => Dist(p, c)), B.Min(c => Dist(p, c))));
        }
        var pulled = EvenlySpacedNet.GuideSeeds(proj, Circle(1.5), S, default, -1, Vec3d.Zero);
        double pulledDiff = pulled.Count == oa.GuideNodes.Count ? pulled.Zip(oa.GuideNodes, (x, y) => (x.pos - y).Length).Max() : double.NaN;
        scenes["asymCat_catenoidGuide"] = new { label = "catenoid, web from a guide (the waist)", wire = Edges(mesh, 6), a = A.Select(l => Line(l, 3)).ToArray(), b = B.Select(l => Line(l, 3)).ToArray(), guide = Line(guide, 1), nodes = oa.GuideNodes.Select(q => new[] { q.X, q.Y, q.Z }).ToArray() };
        numbers["asymGuide"] = new { nodes = oa.GuideNodes.Count, expectedNodes = n, a = A.Count, b = B.Count, ends = e.ends, boundaryEnds = e.boundary, tEnds = e.onCurve, floatingEnds = e.floating, worstOffset = R3(worstOff * 1000) / 1000, edge = R3(proj.AverageEdgeLength), through = R3(through * 1e6) / 1e6, gapErr = R3(gapErr * 1e6) / 1e6, pulledNodes = pulled.Count, pulledDiff = R3(pulledDiff * 1e6) / 1e6, spacing = S, ms = tt.ElapsedMilliseconds };
    }
    // Flat Kit of the 3-fold Enneper web, nested on a 600 × 400 mm bed at 1 : 5 and 1 : 2.5:
    // first-fit shelves against the area bound and the 1.3.1 next-fit rule; strips longer than
    // the bed split into lapped pieces between the joints (nothing on a "longer bed" drawing)
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var w = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options());
        var proj = new MeshProjection(mesh);
        var tk = Stopwatch.StartNew();
        var (net, motion, anchor) = FlatKit.Press(w.A, w.B, w.Seed, new Vec3d(0, 0, 1), q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
        var kit = FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), new FlatKit.Options { Width = 0.1, Thickness = 0.01, Upright = true });
        tk.Stop();
        int NextFit(double mm)
        {
            // the shelf rule of 1.3.1: longest first, a new row whenever the strip does not fit the current one
            int sheets = 0; double x = 0, y = 0, rowH = 0; bool open = false; const double G = 4, SW = 600, SH = 400;
            foreach (var st in kit.Strips.Where(st => st.Length * mm + 2 * G <= SW).OrderByDescending(st => st.Length))
            {
                double L = st.Length * mm, W = st.Width * mm;
                if (!open || x + L + G > SW) { if (open) { y += rowH + G; x = G; rowH = 0; } if (!open || y + W + G > SH) { sheets++; open = true; y = G; x = G; rowH = 0; } }
                x += L + G; rowH = Math.Max(rowH, W);
            }
            return sheets;
        }
        object Row(double mm)
        {
            var tn = Stopwatch.StartNew();
            var n = KitExport.Nest(kit, new KitExport.Options { ToMm = mm });
            tn.Stop();
            // laps clear of the joints: no joint mark within the lap (half the lap plus half the mark from the cut)
            int lapNearJoint = 0;
            foreach (var st in n.Strips.Where(st => st.Label.Contains("/")))
                foreach (var g in st.Marks.Where(m => m.Kind == FlatKit.MarkKind.Splice).GroupBy(m => m.Label))
                {
                    double lo = g.Min(m => m.At) - 0.25 * n.Splice / mm, hi = g.Max(m => m.At) + 0.25 * n.Splice / mm;
                    if (st.Marks.Any(m => m.Kind != FlatKit.MarkKind.Splice && m.At + 0.5 * m.Size > lo && m.At - 0.5 * m.Size < hi)) lapNearJoint++;
                }
            // no two strips overlap on a sheet and every corner lies inside it (axis-aligned shelves; the diagonal is checked in the tests)
            double minSep = double.MaxValue; bool inside = true;
            for (int i = 0; i < n.SheetCount; i++)
            {
                var on = n.Placements.Where(pl => pl.Sheet == i).ToList();
                foreach (var pl in on)
                {
                    double L = pl.Strip.Length * mm, W = pl.Strip.Width * mm, c = Math.Cos(pl.Angle), sn = Math.Sin(pl.Angle);
                    foreach (var (lx, ly) in new[] { (0.0, 0.0), (L, 0.0), (L, W), (0.0, W) })
                    { double X = pl.X + lx * c - ly * sn, Y = pl.Y + lx * sn + ly * c; if (X < 4 - 1e-6 || X > 596 + 1e-6 || Y < 4 - 1e-6 || Y > 396 + 1e-6) inside = false; }
                }
                for (int a = 0; a < on.Count; a++) for (int b = a + 1; b < on.Count; b++)
                {
                    if (on[a].Angle != 0 || on[b].Angle != 0) continue;
                    double ax1 = on[a].X + on[a].Strip.Length * mm, ay1 = on[a].Y + on[a].Strip.Width * mm, bx1 = on[b].X + on[b].Strip.Length * mm, by1 = on[b].Y + on[b].Strip.Width * mm;
                    double sep = Math.Max(Math.Max(on[b].X - ax1, on[a].X - bx1), Math.Max(on[b].Y - ay1, on[a].Y - by1));
                    minSep = Math.Min(minSep, sep);
                }
            }
            return new
            {
                scale = R3(1000 / mm), sheets = n.SheetCount, areaBound = n.AreaBound, nextFit = NextFit(mm), utilisation = R3(n.Utilisation), rows = n.Rows,
                longer = kit.Strips.Count(st => st.Length * mm + 8 > 600), diagonal = n.DiagonalStrips, split = n.SplitStrips, pieces = n.Pieces, strips = n.Strips.Count,
                splice = R3(n.Splice), spliceMin = R3(n.SpliceMin), lapClashes = n.LapClashes, lapNearJoint, tooLong = n.TooLong, inside, minSep = minSep == double.MaxValue ? 99 : R3(minSep), ms = tn.ElapsedMilliseconds,
                summary = KitExport.NestingSummary(n),
            };
        }
        var n25 = KitExport.Nest(kit, new KitExport.Options { ToMm = 400 });
        object[] Rects(KitExport.Nesting nn, int sheet) => nn.Placements.Where(pl => pl.Sheet == sheet).Select(pl => (object)new { x = R3(pl.X), y = R3(pl.Y), l = R3(pl.Strip.Length * 400), w = R3(pl.Strip.Width * 400), a = R3(pl.Angle), piece = pl.Strip.Label.Contains("/"), label = pl.Strip.Label }).ToArray();
        scenes["kit"] = new { sheetW = 600, sheetH = 400, sheets = Enumerable.Range(0, n25.SheetCount).Select(i => new { title = n25.Sheets[i].Title, rects = Rects(n25, i) }).ToArray() };
        numbers["kit"] = new { strips = kit.Strips.Count, joints = kit.Joints.Count, pressMs = tk.ElapsedMilliseconds, s5 = Row(200), s33 = Row(300), s25 = Row(400), s2 = Row(500) };
    }

    // Web coverage with gap filling: the part of the usable region farther than
    // 0.75 Spacing from any curve, for the fill and both webs, on surfaces where
    // the border alone cannot reach everything (K = 0 lines, flat points, no border)
    {
        var covRows = new List<object>();
        foreach (var name in new[] { "monkey", "wave", "torus", "schwarzd" })
        {
            var mesh = AnalyticShapes.Build(name, 0, 0, 0, 40);
            var pc = PrincipalCurvature.Compute(mesh);
            var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
            double sp = mesh.BoundingBoxDiagonal() / 25.0;
            var row = new Dictionary<string, object> { ["name"] = name };
            foreach (var (lay, key) in new[] { (NetLayout.Fill, "fill"), (NetLayout.WebBorder, "border"), (NetLayout.WebCross, "cross") })
            {
                var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, Layout = lay }, field.Family2);
                var all = A.SelectMany(c => c).ToArray();
                int n = 0, un = 0;
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    if (!field.Exists[i]) continue; n++;
                    var v = mesh.Vertices[i]; double best = double.MaxValue;
                    foreach (var q in all) { double d = (q - v).LengthSquared; if (d < best) best = d; }
                    if (Math.Sqrt(best) > 0.75 * sp) un++;
                }
                var e = Ends(A, new MeshProjection(mesh));
                row[key] = new { curves = A.Count, uncovered = R3(100.0 * un / Math.Max(1, n)), tEnds = e.onCurve };
            }
            covRows.Add(row);
        }
        numbers["webCoverage"] = covRows;
    }
}

// ---------- Asymptotic Web: AAG webs and their refinement (Schling, Wang, Hoyer & Pottmann 2022, §3.4) ----------
{
    var t = Stopwatch.StartNew();
    double[][][] Lines(IEnumerable<Vec3d[]> ls) => ls.Select(l => Line(l)).ToArray();
    Vec3d[] Chop(Vec3d[] l, int m) { var o = new List<Vec3d>(); for (int k = 0; k + 1 < l.Length; k++) for (int j = 0; j < m; j++) o.Add(l[k] + (j / (double)m) * (l[k + 1] - l[k])); o.Add(l[^1]); return o.ToArray(); }
    // rotational AAG web on the catenoid: chords, ×2, ×4
    var cat = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 48).ToTriangulated();
    var webC = AsymptoticWeb.Build(cat, PrincipalCurvature.Compute(cat, 2), -1, new AsymptoticWeb.Options { Spacing = 0.3 });
    var aagC = AagWeb.Optimize(cat, webC);
    var wireC = Edges(cat, 8);
    scenes["aag_chords"] = new { label = "catenoid — AAG web, laths as joint-to-joint chords", wire = wireC, a = Lines(aagC.A), b = Lines(aagC.B), g = Lines(aagC.G), joints = aagC.Nodes.Select(P).ToArray() };
    var refRows = new List<object>();
    foreach (int levels in new[] { 1, 2 })
    {
        var tr = Stopwatch.StartNew();
        var f = AagRefinement.Refine(cat, webC, aagC, new AagRefinement.Options { Levels = levels });
        tr.Stop();
        scenes["aag_ref" + f.Subdivisions] = new { label = $"catenoid — refined ×{f.Subdivisions}", a = Lines(f.A), b = Lines(f.B), g = Lines(f.G), joints = aagC.Nodes.Select(P).ToArray() };
        refRows.Add(new { m = f.Subdivisions, fine = f.Vertices.Length, turnBefore = R3(f.InitialMaxTurning), turnAfter = R3(f.MaxTurning), star = R3(f.StarError), geo = R3(f.GeodesicError), kn = R3(f.MaxNormalCurvature), kg = R3(f.MaxGeodesicCurvature), shift = f.JointShift, dev = R3(f.MaxDeviation), ms = tr.ElapsedMilliseconds });
    }
    // 3-fold Enneper: singular web, AAG kinks at the rays with the 1.3.0 fairness
    var en = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
    var webE = AsymptoticWeb.Build(en, PrincipalCurvature.Compute(en, 2), -1, new AsymptoticWeb.Options());
    var aagOld = AagWeb.Optimize(en, webE, new AagWeb.Options { Fairness = 0.05 });
    var te = Stopwatch.StartNew();
    var aagE = AagWeb.Optimize(en, webE);
    te.Stop();
    var fE = AagRefinement.Refine(en, webE, aagE, new AagRefinement.Options { Levels = 1 });
    scenes["aag_enneper3"] = new { label = "3-fold Enneper — AAG web refined ×2 (six rays)", wire = Edges(en, 6), a = Lines(fE.A), b = Lines(fE.B), g = Lines(fE.G), joints = aagE.Nodes.Select(P).ToArray() };

    // exact lattices: joints on the closed-form asymptotic lines
    // catenoid r = cosh z: φ ± z = const
    const double D = 0.1; int I = 16;
    var latC = TestMeshes.AsymptoticLattice((i, j) => { double phi = (i + j - I) * D, z = (i - j) * D; return Math.Abs(z) > 1.0 + 1e-9 ? null : new Vec3d(Math.Cosh(z) * Math.Cos(phi), Math.Cosh(z) * Math.Sin(phi), z); }, I, I);
    var catFine = AnalyticShapes.Build("catenoid", 1.0, 1.15, 0, 192).ToTriangulated();
    var projCF = new MeshProjection(catFine);
    Vec3d Orient(MeshProjection pr, Vec3d p, Vec3d n) { var h = pr.ClosestPoint(p, pr.NearestVertexGlobal(p)); n = n.Normalized(); return Vec3d.Dot(h.SmoothNormal, n) < 0 ? -1.0 * n : n; }
    var nC = latC.Nodes.Select(p => Orient(projCF, p, new Vec3d(p.X, p.Y, -Math.Sinh(p.Z) * Math.Cosh(p.Z)))).ToArray();
    var fC = AagRefinement.Refine(catFine, latC, latC.Nodes, nC, 0, new AagRefinement.Options { Levels = 1, JointWeight = 10 });
    var chC = AagWeb.Optimize(catFine, latC, new AagWeb.Options { Iterations = 0 });
    double Inv(Vec3d p, int s) => Math.Atan2(p.Y, p.X) + s * p.Z;
    int Sgn(Vec3d[] l) => Math.Abs(Math.IEEERemainder(Inv(l[^1], 1) - Inv(l[0], 1), 2 * Math.PI)) < Math.Abs(Math.IEEERemainder(Inv(l[^1], -1) - Inv(l[0], -1), 2 * Math.PI)) ? 1 : -1;
    double Off(Vec3d[] l, int s, int m) { double w = 0; for (int k = 0; k + m < l.Length; k += m) { double c0 = Inv(l[k], s); for (int j = k + 1; j < k + m; j++) w = Math.Max(w, Math.Abs(Math.IEEERemainder(Inv(l[j], s) - c0, 2 * Math.PI)) * Math.Cosh(l[j].Z)); } return w; }
    double offRef = fC.A.Concat(fC.B).Max(l => Off(l, Sgn(l), 2)), offChord = chC.A.Concat(chC.B).Max(l => Off(Chop(l, 2), Sgn(l), 2));
    double catRef = fC.G.SelectMany(l => l).Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Cosh(p.Z)));
    double catChord = chC.G.SelectMany(l => Chop(l, 2)).Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Cosh(p.Z)));
    // hyperboloid x² + y² − z² = 1: rulings cross at s = tan((ψ − φ)/2)
    const double DH = 0.14; int IH = 14;
    var latH = TestMeshes.AsymptoticLattice((i, j) => { double phi = i * DH, psi = j * DH, s = Math.Tan(0.5 * (psi - phi)); return Math.Abs(s) > 0.75 ? null : new Vec3d(Math.Cos(phi) - s * Math.Sin(phi), Math.Sin(phi) + s * Math.Cos(phi), s); }, IH, IH);
    var hypFine = AnalyticShapes.Build("hyperboloid", 1.0, 1.0, 0.8, 192).ToTriangulated();
    var projHF = new MeshProjection(hypFine);
    var nH = latH.Nodes.Select(p => Orient(projHF, p, new Vec3d(p.X, p.Y, -p.Z))).ToArray();
    var fH = AagRefinement.Refine(hypFine, latH, latH.Nodes, nH, 0, new AagRefinement.Options { Levels = 1, JointWeight = 10 });
    var chH = AagWeb.Optimize(hypFine, latH, new AagWeb.Options { Iterations = 0 });
    double bow = 0, len = 0;
    foreach (var l in fH.A.Concat(fH.B)) { var a0 = l[0]; var d = (l[^1] - a0).Normalized(); len = Math.Max(len, (l[^1] - a0).Length); foreach (var p in l) { var v = p - a0; bow = Math.Max(bow, (v - Vec3d.Dot(v, d) * d).Length); } }
    double hypRef = fH.G.SelectMany(l => l).Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Sqrt(1 + p.Z * p.Z)));
    double hypChord = chH.G.SelectMany(l => Chop(l, 2)).Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Sqrt(1 + p.Z * p.Z)));
    t.Stop();
    numbers["aagRefine"] = new
    {
        catenoid = refRows,
        aagMs = te.ElapsedMilliseconds,
        kinkOld = R3(aagOld.MaxLathTurning), kink = R3(aagE.MaxLathTurning), webTurn = R3(AagWeb.Optimize(en, webE, new AagWeb.Options { Iterations = 0 }).MaxLathTurning),
        enGeo = R3(aagE.GeodesicError), enStar = R3(aagE.StarError),
        enRef = new { star = R3(fE.StarError), kn = R3(fE.MaxNormalCurvature), kg = R3(fE.MaxGeodesicCurvature), turnG = R3(AagRefinement.MaxTurning(aagE.G)), turnGAfter = R3(AagRefinement.MaxTurning(fE.G)) },
        offRef, offChord, catRef, catChord, bow, len = R3(len), hypRef, hypChord,
        ms = t.ElapsedMilliseconds
    };
}

// ---------- Holes: annulus typologies (inner borders, loops around the hole, straight geodesic laths at contacts) ----------
{
    double rin = 0.4, rout = 1.2, kk = 0.5;
    var ann = AnalyticShapes.Build("annulus", rin, rout, kk, 48);
    var projA = new MeshProjection(ann);
    double Rho(Vec3d p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);
    (int inner, int outer, int onCurve, int floating, int inHole) HoleEnds(List<Vec3d[]> all)
    {
        int inner = 0, outer = 0, onC = 0, fl = 0, inHole = 0;
        foreach (var l in all)
        {
            foreach (var q in l) if (Rho(q) < rin - 2e-3) inHole++;
            if ((l[0] - l[^1]).LengthSquared < 1e-24) continue;
            foreach (var q in new[] { l[0], l[^1] })
            {
                double r = Rho(q);
                if (Math.Abs(r - rin) < 3e-3) { inner++; continue; }
                if (Math.Abs(r - rout) < 3e-3) { outer++; continue; }
                double best = 1e9;
                foreach (var m in all) { if (ReferenceEquals(m, l)) continue; for (int i = 0; i + 1 < m.Length; i++) { var ab = m[i + 1] - m[i]; double tt = Math.Max(0, Math.Min(1, Vec3d.Dot(q - m[i], ab) / Math.Max(ab.LengthSquared, 1e-30))); best = Math.Min(best, (m[i] + tt * ab - q).Length); } }
                if (best < 1e-6) onC++; else fl++;
            }
        }
        return (inner, outer, onC, fl, inHole);
    }
    // in-surface turning of the laths: max and mean discrete geodesic curvature × spacing (a geodesic lath has none)
    (double max, double mean) Turning(MeshData mesh, List<Vec3d[]> curves, double spacing)
    {
        var pj = new MeshProjection(mesh); double mx = 0, sum = 0; int cnt = 0;
        foreach (var c in curves)
        {
            int hint = pj.NearestVertexGlobal(c[0]);
            for (int i = 1; i + 1 < c.Length; i++)
            {
                var h = pj.ClosestPoint(c[i], hint); hint = h.NearestVertex; var nrm = h.SmoothNormal;
                Vec3d e0 = c[i] - c[i - 1], e1 = c[i + 1] - c[i];
                e0 -= Vec3d.Dot(e0, nrm) * nrm; e1 -= Vec3d.Dot(e1, nrm) * nrm;
                double l0 = e0.Length, l1 = e1.Length; if (l0 < 1e-12 || l1 < 1e-12) continue;
                double kg = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(e0, e1) / (l0 * l1)))) / (0.5 * (l0 + l1)) * spacing;
                mx = Math.Max(mx, kg); sum += kg; cnt++;
            }
        }
        return (mx, cnt > 0 ? sum / cnt : 0);
    }
    // asymptotic web on the annular saddle z = k (x² − y²): every curve a straight line x ± y = c, ending on either rim
    {
        var pc = PrincipalCurvature.Compute(ann, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, ann, 15.0);
        var A = EvenlySpacedNet.TraceField(ann, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Layout = NetLayout.WebBorder }, field.Family2);
        var B = EvenlySpacedNet.TraceField(ann, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Layout = NetLayout.WebBorder }, field.Family1);
        var all = A.Concat(B).ToList();
        var he = HoleEnds(all);
        double bow = all.Max(ChordDev), dirErr = 0;
        foreach (var c in all) { var d = c[^1] - c[0]; dirErr = Math.Max(dirErr, Math.Abs(Math.Atan2(Math.Abs(d.Y), Math.Abs(d.X)) * 180 / Math.PI - 45)); }
        // coverage: usable vertices farther than 0.75 spacing from every curve
        int unc = 0, usable = 0;
        for (int i = 0; i < ann.VertexCount; i++) { if (!field.Exists[i]) continue; usable++; double best = 1e9; foreach (var m in all) foreach (var q in m) best = Math.Min(best, (q - ann.Vertices[i]).Length); if (best > 0.75 * 0.15) unc++; }
        scenes["asymAnn"] = new { wire = Edges(ann, 5), a = A.Select(l => Line(l, 3)).ToArray(), b = B.Select(l => Line(l, 3)).ToArray() };
        numbers["asymAnn"] = new { a = A.Count, b = B.Count, innerEnds = he.inner, outerEnds = he.outer, tEnds = he.onCurve, floatingEnds = he.floating, inHole = he.inHole, bow = R3(bow), dirErrDeg = R3(dirErr), uncovered = R3(100.0 * unc / Math.Max(1, usable)), loops = EvenlySpacedNet.BoundaryLoops(ann).Count };
    }
    // geodesics from the outer rim on the annular saddle: radial laths converge towards the hole and
    // end on their neighbours — straight, as a board does, not bent onto them
    {
        int seed = Enumerable.Range(0, ann.VertexCount).OrderBy(i => (ann.Vertices[i] - new Vec3d(rout, 0, kk * rout * rout)).LengthSquared).First();
        var opts = new EvenlySpacedNet.Options { Spacing = 0.15, FromBorder = true };
        var g = EvenlySpacedNet.TraceGeodesics(ann, seed, new Vec3d(-1, 0, 0), opts);
        var he = HoleEnds(g);
        var tu = Turning(ann, g, 0.15);
        scenes["annGeo"] = new { wire = Edges(ann, 5), curves = g.Select(l => Line(l, 2)).ToArray() };
        numbers["annGeo"] = new { count = g.Count, innerEnds = he.inner, outerEnds = he.outer, tEnds = he.onCurve, floatingEnds = he.floating, inHole = he.inHole, maxTurn = R3(tu.max), meanTurn = R3(tu.mean) };
        // the same on a flat annulus (geodesics are straight lines: bow is the whole error) and on a dome from its rim
        var flat = AnalyticShapes.Parametric((u, v) => new Vec3d(v * Math.Cos(u), v * Math.Sin(u), 0), 0, 2 * Math.PI, rin, rout, 64, 16, true, false, false, false);
        int seedF = Enumerable.Range(0, flat.VertexCount).OrderBy(i => (flat.Vertices[i] - new Vec3d(1.0, 0, 0)).LengthSquared).First();
        var gf = EvenlySpacedNet.TraceGeodesics(flat, seedF, new Vec3d(0, 1, 0), new EvenlySpacedNet.Options { Spacing = 0.15, FromBorder = true });
        var hef = HoleEnds(gf);
        var dome = AnalyticShapes.Build("dome", 0, 0, 0, 48);
        double sd = dome.BoundingBoxDiagonal() / 25.0;
        var gd = EvenlySpacedNet.TraceGeodesics(dome, dome.VertexCount / 2, new Vec3d(1, 0, 0), new EvenlySpacedNet.Options { Spacing = sd, FromBorder = true });
        var ed = Ends(gd, new MeshProjection(dome));
        var tud = Turning(dome, gd, sd);
        numbers["geoStraight"] = new { flatCount = gf.Count, flatBow = gf.Max(ChordDev), flatTEnds = hef.onCurve, flatFloating = hef.floating, flatBowBefore = 0.0147, domeCount = gd.Count, domeMaxTurn = R3(tud.max), domeMeanTurn = R3(tud.mean), domeMaxTurnBefore = 0.296, domeMeanTurnBefore = 0.0134, domeTEnds = ed.onCurve, domeFloating = ed.floating };
    }
    // exact shortest paths around the hole on a flat annulus: the taut string around the inscribed polygon
    {
        int nA = 40;
        var flat = AnalyticShapes.Parametric((u, v) => new Vec3d(v * Math.Cos(u), v * Math.Sin(u), 0), 0, 2 * Math.PI, rin, 1.5, nA, 12, true, false, false, false);
        var poly = Enumerable.Range(0, nA).Select(i => (rin * Math.Cos(2 * Math.PI * i / nA), rin * Math.Sin(2 * Math.PI * i / nA))).ToList();
        var rnd = new Random(5);
        var from = new List<Vec3d>(); var to = new List<Vec3d>(); var exact = new List<double>();
        for (int k = 0; k < 40; k++)
        {
            double r1 = rin + (1.5 - rin) * (0.05 + 0.9 * rnd.NextDouble()), r2 = rin + (1.5 - rin) * (0.05 + 0.9 * rnd.NextDouble());
            double t1 = 2 * Math.PI * rnd.NextDouble(), t2 = t1 + Math.PI * (0.6 + 0.4 * rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
            var pp = new Vec3d(r1 * Math.Cos(t1), r1 * Math.Sin(t1), 0); var qq = new Vec3d(r2 * Math.Cos(t2), r2 * Math.Sin(t2), 0);
            from.Add(pp); to.Add(qq); exact.Add(TestMeshes.PlanarDistanceAroundPolygon((pp.X, pp.Y), (qq.X, qq.Y), poly));
        }
        var res = FlipGeodesic.Compute(flat, from, to, out _)!;
        double maxErr = 0; int around = 0;
        for (int k = 0; k < from.Count; k++) { maxErr = Math.Max(maxErr, Math.Abs(res[k]!.Length - exact[k])); if (exact[k] > (from[k] - to[k]).Length + 1e-12) around++; }
        var demo = res[Enumerable.Range(0, from.Count).OrderByDescending(k => exact[k] - (from[k] - to[k]).Length).First()]!;
        int kd = Enumerable.Range(0, from.Count).OrderByDescending(k => exact[k] - (from[k] - to[k]).Length).First();
        scenes["geoPathAnnulus"] = new { wire = Edges(flat, 3), flip = Line(demo.Points), ends = new[] { P(from[kd]), P(to[kd]) } };
        numbers["geoPathAnnulus"] = new { pairs = from.Count, around, maxErr, converged = res.Count(r => r!.Converged), demoLen = demo.Length, demoExact = exact[kd], demoStraight = R3((from[kd] - to[kd]).Length) };
    }
}

// ---------- Net Kinetics: scissor-jointed asymptotic nets as mechanisms ----------
{
    // 1. Exact: the doubly ruled hyperboloid grid of Schikore et al. 2020 (16 rods per family, 7 joints each),
    //    driven by a cable across the top ring from β = 25° to 70°; every state must match the closed form.
    int n = 16, levels = 3, rows = 2 * levels + 1; double rho = 1.0;
    double b0 = 25 * Math.PI / 180, b1 = 70 * Math.PI / 180;
    var (nodes0, laths0, normals0) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(n, levels, rho, b0);
    var net = new Mite.Core.Kinetics.ScissorNet(nodes0, laths0, n, normals0);
    int top0 = 2 * levels, topOpp = (n / 2) * rows + 2 * levels, waist0 = levels;
    double TopDiameter(double beta) { var (nn, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(n, levels, rho, beta); return (nn[top0] - nn[topOpp]).Length; }
    int steps = 12;
    var t = Stopwatch.StartNew();
    var res = net.Solve(new Mite.Core.Kinetics.ScissorNet.Options { Fixed = new[] { waist0 }, Cables = new[] { (top0, topOpp) }, CableLengthsAt = f => new[] { TopDiameter(b0 + f * (b1 - b0)) }, Steps = steps, Tolerance = 1e-11 });
    t.Stop();
    var stateRows = new List<object>(); var stateScenes = new List<object>();
    double worstShape = 0, worstDrift = 0, worstAsym = 0, worstCable = 0; int worstIt = 0;
    foreach (var st in res.States)
    {
        double beta = b0 + st.Fold * (b1 - b0);
        var (truth, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(n, levels, rho, beta);
        double shape = 0;
        for (int i = 0; i < truth.Length; i++) for (int j = i + 1; j < truth.Length; j++) shape = Math.Max(shape, Math.Abs((st.Nodes[i] - st.Nodes[j]).Length - (truth[i] - truth[j]).Length));
        worstShape = Math.Max(worstShape, shape); worstDrift = Math.Max(worstDrift, st.LengthDrift); worstAsym = Math.Max(worstAsym, st.AsymptoticDeviation); worstCable = Math.Max(worstCable, st.CableMiss); worstIt = Math.Max(worstIt, st.Iterations);
        var ang = net.CrossingAngles(st);
        var polys = net.LathPolylines(st);
        stateRows.Add(new { fold = R3(st.Fold), betaDeg = R3(beta * 180 / Math.PI), waist = R3((st.Nodes[waist0] - st.Nodes[(n / 2) * rows + levels]).Length / 2), waistTheory = R3(rho * Math.Cos(beta)), shapeErr = shape, drift = st.LengthDrift, asymDeg = st.AsymptoticDeviation, cableMiss = st.CableMiss, waistAngle = R3(ang[waist0]), waistAngleTheory = R3(2 * beta * 180 / Math.PI > 90 ? 180 - 2 * beta * 180 / Math.PI : 2 * beta * 180 / Math.PI), iterations = st.Iterations, converged = st.Converged });
        stateScenes.Add(new { a = polys.Take(n).Select(l => Line(l)).ToArray(), b = polys.Skip(n).Select(l => Line(l)).ToArray(), joints = st.Nodes.Select(P).ToArray() });
    }
    // elastic reading (Schikore et al. 2020): straight rods → twist only; along a ruling the normal turns by
    // θ(s) = atan(s / b), b = ρ sin β, so τg = b / (b² + s²) and ∫τg² ds = [s / (2(b² + s²)) + atan(s / b) / (2b)];
    // the discrete joint-to-joint twist (uniform between hinges) is a lower bound of that surface integral
    var rodOpt = new Mite.Core.Kinetics.KineticStrain.Options { Profile = LathProfile.Round(0.02), YoungsModulus = 11e9, ShearModulus = 0.7e9, MaxStrain = 0.005 };
    var rodElastic = Mite.Core.Kinetics.KineticStrain.Analyze(net, res, rodOpt);
    double Jrod = LathProfile.Round(0.02).SectionProperties().J, sEnd = rho * Math.Tan(Math.PI * levels / n);
    double F(double b, double s) => s / (2 * (b * b + s * s)) + Math.Atan(s / b) / (2 * b);
    var elasticRows = new List<object>();
    for (int k = 0; k < res.States.Count; k++)
    {
        double beta = b0 + res.States[k].Fold * (b1 - b0), bb = rho * Math.Sin(beta);
        double exact = 2 * n * 0.5 * 0.7e9 * Jrod * (F(bb, sEnd) - F(bb, -sEnd));
        elasticRows.Add(new { fold = R3(res.States[k].Fold), twist = rodElastic[k].TwistEnergy, exact, ratio = R3(rodElastic[k].TwistEnergy / exact), bending = rodElastic[k].BendingEnergy, util = R3(rodElastic[k].MaxUtilization), utilTheory = R3(0.01 / Math.Sqrt(3.0) / bb / 0.005 * (bb * Math.Atan(rho * Math.Tan(Math.PI / n) / bb) / (rho * Math.Tan(Math.PI / n)))) });
    }
    // strain envelope over the motion (2026-10-02): the waist twist atan(s₁/b)/s₁ falls as β grows, so every rod peaks in
    // the first state (β = 25°), at the closed form, governed by twist
    var rodEnv = Mite.Core.Kinetics.KineticStrain.Envelope(rodElastic);
    double s1h = rho * Math.Tan(Math.PI / n), bh0 = rho * Math.Sin(b0);
    var envHyp = new { max = rodEnv.MaxUtilization, theory = 0.01 / Math.Sqrt(3.0) * (Math.Atan(s1h / bh0) / s1h) / 0.005, states = rodEnv.LathState.Distinct().ToArray(), sources = rodEnv.LathSource.Select(x => x.ToString()).Distinct().ToArray(), buildable = rodEnv.BuildableLaths, laths = rodEnv.LathUtilization.Length };
    numbers["kinHyp"] = new { rods = 2 * n, joints = nodes0.Length, states = res.States.Count, worstShape, worstDrift, worstAsym, worstCable, worstIt, ms = t.ElapsedMilliseconds, rows = stateRows, elastic = elasticRows, natural = Mite.Core.Kinetics.KineticStrain.NaturalState(rodElastic), envelope = envHyp };
    scenes["kinHyp"] = new { states = stateScenes };

    // 2. A traced asymptotic net on the catenoid (the Studio X "concave cylinder") standing on the ground (bottom-rim
    //    joints slide in z = −1, Wan et al.'s ground nodes), top ring pulled in by 20 % with two crossed cables; the laths
    //    keep their rest bend (stiffness 1) and the hinges their spacing as far as they can.
    var cat = TestMeshes.CreateCatenoid(1, 2, 64, 32);
    var pcC = PrincipalCurvature.Compute(cat);
    var fieldC = AsymptoticCurves.ComputeDirections(pcC);
    var catA = EvenlySpacedNet.TraceField(cat, fieldC.Family1, fieldC.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, fieldC.Family2);
    var catB = EvenlySpacedNet.TraceField(cat, fieldC.Family2, fieldC.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, fieldC.Family1);
    var topoC = NetTopology.Build(catA, catB, NetIntersections.FindAll(catA, catB, 0));
    var projC = new MeshProjection(cat);
    var netC = Mite.Core.Kinetics.ScissorNet.FromTopology(topoC, catA.Count + catB.Count, catA.Count, 0, p => projC.ClosestPoint(p, -1).SmoothNormal);
    var top = Enumerable.Range(0, netC.Nodes.Length).Where(i => netC.Nodes[i].Z > 0.95).ToArray();
    int c0 = top.OrderBy(i => netC.Nodes[i].X).First(), c1 = top.OrderByDescending(i => netC.Nodes[i].X).First();
    int c2 = top.OrderBy(i => netC.Nodes[i].Y).First(), c3 = top.OrderByDescending(i => netC.Nodes[i].Y).First();
    var ground = Enumerable.Range(0, netC.Nodes.Length).Where(i => netC.Nodes[i].Z < -0.95).ToArray();
    double d0 = (netC.Nodes[c0] - netC.Nodes[c1]).Length, d1 = (netC.Nodes[c2] - netC.Nodes[c3]).Length;
    t.Restart();
    var resC = netC.Solve(new Mite.Core.Kinetics.ScissorNet.Options { Sliding = ground, Cables = new[] { (c0, c1), (c2, c3) }, CableLengthsAt = f => new[] { d0 * (1 - 0.2 * f), d1 * (1 - 0.2 * f) }, Steps = 5 });
    t.Stop();
    var catRows = new List<object>(); var catScenes = new List<object>();
    foreach (var st in resC.States)
    {
        var polys = netC.LathPolylines(st);
        var ang = netC.CrossingAngles(st).Where(a => !double.IsNaN(a)).ToArray();
        catRows.Add(new { fold = R3(st.Fold), drift = st.LengthDrift, asymDeg = st.AsymptoticDeviation, cableMiss = st.CableMiss, slideMiss = st.SlideMiss, cable = R3((st.Nodes[c0] - st.Nodes[c1]).Length), cableTarget = R3(d0 * (1 - 0.2 * st.Fold)), rise = R3(st.Nodes.Max(q => q.Z) - 1), maxMove = R3(st.Nodes.Zip(netC.Nodes, (a2, b2) => (a2 - b2).Length).Max()), minAngle = R3(ang.Min()), maxAngle = R3(ang.Max()), iterations = st.Iterations, converged = st.Converged });
        catScenes.Add(new { a = polys.Take(netC.CountA).Select(l => Line(l)).ToArray(), b = polys.Skip(netC.CountA).Select(l => Line(l)).ToArray(), joints = Enumerable.Range(0, netC.Nodes.Length).Where(i => netC.IsJoint[i]).Select(i => P(st.Nodes[i])).ToArray(), cables = new[] { new[] { P(st.Nodes[c0]), P(st.Nodes[c1]) }, new[] { P(st.Nodes[c2]), P(st.Nodes[c3]) } } });
    }
    // elastic reading of the catenoid motion: upright 80 × 12 mm timber laths, 0.5 % strain, self-weight 500 kg/m³
    var catOpt = new Mite.Core.Kinetics.KineticStrain.Options { Profile = new LathProfile(0.08, 0.012, true), YoungsModulus = 11e9, MaxStrain = 0.005, Density = 500 };
    var catElastic = Mite.Core.Kinetics.KineticStrain.Analyze(netC, resC, catOpt);
    int catNatural = Mite.Core.Kinetics.KineticStrain.NaturalState(catElastic);
    var catEnergyRows = catElastic.Select(e => new { fold = R3(e.Fold), util = R3(e.MaxUtilization), bending = R3(e.BendingEnergy), twist = R3(e.TwistEnergy), potential = R3(e.Potential), total = R3(e.Total), buildable = e.Buildable }).ToArray();
    // strain per state and envelope (2026-10-02) for the colour view, with upright 80 × 10 mm laths so the colours cross
    // the limit during the motion (the 12 mm section above is over it in every state: √−K·t/√3 at the throat is 1.39 × the limit)
    var thinElastic = Mite.Core.Kinetics.KineticStrain.Analyze(netC, resC, new Mite.Core.Kinetics.KineticStrain.Options { Profile = new LathProfile(0.08, 0.010, true), YoungsModulus = 11e9, MaxStrain = 0.005 });
    var thinEnv = Mite.Core.Kinetics.KineticStrain.Envelope(thinElastic);
    double envErr = 0;
    for (int l = 0; l < netC.Laths.Count; l++) envErr = Math.Max(envErr, Math.Abs(thinEnv.LathUtilization[l] - thinElastic.Max(e => e.Laths[l].MaxUtilization)));
    var strainCat = new
    {
        lathUtil = thinElastic.Select(e => e.LathUtilization.Select(R3).ToArray()).ToArray(),
        envelope = thinEnv.LathUtilization.Select(R3).ToArray(), envelopeState = thinEnv.LathState,
        sources = thinEnv.LathSource.GroupBy(x => x.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        buildable = thinEnv.BuildableLaths, worst = thinEnv.GoverningLath, worstState = thinEnv.GoverningState, max = R3(thinEnv.MaxUtilization),
        perState = thinElastic.Select(e => e.LathUtilization.Count(u => u > 1)).ToArray(), envErr,
    };
    numbers["kinCat"] = new { strain = strainCat, laths = netC.Laths.Count, joints = netC.IsJoint.Count(j => j), nodes = netC.Nodes.Length, ground = ground.Length, cableStart = R3(d0), elastic = catEnergyRows, natural = catNatural, naturalFold = catNatural >= 0 ? R3(resC.States[catNatural].Fold) : -1, settleMove = R3(resC.States[0].Nodes.Zip(netC.Nodes, (a2, b2) => (a2 - b2).Length).Max()), settleDeg = resC.States[0].AsymptoticDeviation, ms = t.ElapsedMilliseconds, rows = catRows };
    scenes["kinCat"] = new { wire = Edges(cat, 4), states = catScenes };

    // 3. Exact: the kinetic umbrella opened from its bundle. At β = 0 every rod lies in the plane and each A rod
    //    coincides with its B twin; the top ring's sliding planes rise by 2 s_max sin β while the bottom ring stays
    //    on the ground — through the bifurcation at β = 0 — and every state must be the closed form up to 60°.
    {
        int nb = 16, lb = 3, rowsB = 2 * lb + 1; double rhoB = 1.0, beta1 = 60 * Math.PI / 180;
        var (nB, lB, nmB) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(nb, lb, rhoB, 0.0);
        var netB = new Mite.Core.Kinetics.ScissorNet(nB, lB, nb, nmB);
        var topB = Enumerable.Range(0, nb).Select(k => k * rowsB + 2 * lb).ToArray();
        var botB = Enumerable.Range(0, nb).Select(k => k * rowsB).ToArray();
        double smax = rhoB * Math.Tan(Math.PI * lb / nb);
        var slideB = botB.Concat(topB).ToArray(); var topSet = new HashSet<int>(topB);
        int stepsB = 12;
        t.Restart();
        var resB = netB.Solve(new Mite.Core.Kinetics.ScissorNet.Options { Sliding = slideB, SlideOffsetsAt = f => { double beta = f * beta1; double dz = 2 * smax * Math.Sin(beta); return slideB.Select(i => topSet.Contains(i) ? dz : 0.0).ToArray(); }, Steps = stepsB, Tolerance = 1e-11, MaxIterations = 200 });
        t.Stop();
        var bRows = new List<object>(); var bScenes = new List<object>();
        double bShape = 0, bDrift = 0, bAsym = 0, bSlide = 0; int bIt = 0;
        foreach (var st in resB.States)
        {
            double beta = st.Fold * beta1;
            var (truth, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(nb, lb, rhoB, beta);
            double shape = 0;
            for (int i = 0; i < truth.Length; i++) for (int j = i + 1; j < truth.Length; j++) shape = Math.Max(shape, Math.Abs((st.Nodes[i] - st.Nodes[j]).Length - (truth[i] - truth[j]).Length));
            bShape = Math.Max(bShape, shape); bDrift = Math.Max(bDrift, st.LengthDrift); bAsym = Math.Max(bAsym, st.AsymptoticDeviation); bSlide = Math.Max(bSlide, st.SlideMiss); bIt = Math.Max(bIt, st.Iterations);
            var w = st.Nodes[lb];
            bRows.Add(new { fold = R3(st.Fold), betaDeg = R3(beta * 180 / Math.PI), waist = R3(Math.Sqrt(w.X * w.X + w.Y * w.Y)), waistTheory = R3(rhoB * Math.Cos(beta)), rise = R3(2 * smax * Math.Sin(beta)), shapeErr = shape, drift = st.LengthDrift, asymDeg = st.AsymptoticDeviation, slideMiss = st.SlideMiss, iterations = st.Iterations, converged = st.Converged });
            var polys = netB.LathPolylines(st);
            bScenes.Add(new { a = polys.Take(nb).Select(l => Line(l)).ToArray(), b = polys.Skip(nb).Select(l => Line(l)).ToArray(), joints = st.Nodes.Select(P).ToArray() });
        }
        numbers["kinBundle"] = new { rods = 2 * nb, joints = nB.Length, states = resB.States.Count, worstShape = bShape, worstDrift = bDrift, worstAsym = bAsym, worstSlide = bSlide, firstIt = resB.States[1].Iterations, worstIt = bIt, ms = t.ElapsedMilliseconds, rows = bRows };
        scenes["kinBundle"] = new { states = bScenes };
    }

    // 4. The flat kit deployed: the 3-fold Enneper web pressed flat (a hexagon), made exactly flat, and deployed
    //    on a table by opening every scissor to the right angle of the minimal surface, after a kick along the
    //    softest mode — the bifurcation at the flat state; compared with the design web by pairwise distances.
    //    Also measured: the softest modes, the lock without a kick, and the pull at the singular node.
    {
        var meshE = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var pcE = PrincipalCurvature.Compute(meshE, 2);
        var web = AsymptoticWeb.Build(meshE, pcE, -1, new AsymptoticWeb.Options());
        var topoE = NetTopology.Build(web.A, web.B, NetIntersections.FindAll(web.A, web.B, 0));
        var projE = new MeshProjection(meshE);
        var design = Mite.Core.Kinetics.ScissorNet.FromTopology(topoE, web.A.Count + web.B.Count, web.A.Count, 0, q => projE.ClosestPoint(q, projE.NearestVertexGlobal(q)).SmoothNormal);
        int centre = design.NearestNode(web.Seed);
        var up = new Vec3d(0, 0, 1);
        t.Restart();
        var pressOpt = new Mite.Core.Kinetics.ScissorNet.Options { Fairness = 0.3, Steps = 8, MaxIterations = 60 };
        design.PressFlat(pressOpt, centre, up);
        var pressed = design.Solve(pressOpt).Last;
        long pressMs = t.ElapsedMilliseconds;
        var flatState = new Mite.Core.Kinetics.ScissorNet.State { Nodes = pressed.Nodes.Select(q => new Vec3d(q.X, q.Y, 0)).ToArray(), Normals = pressed.Nodes.Select(_ => up).ToArray() };
        var flat = design.AtState(flatState);
        double zc = design.Nodes[centre].Z;
        var designH = design.Nodes.Select(q => q.Z - zc).ToArray();
        double Corr(double[] a, double[] b) { double num = 0, d1 = 0, d2 = 0; for (int k = 0; k < a.Length; k++) { num += a[k] * b[k]; d1 += a[k] * a[k]; d2 += b[k] * b[k]; } return num / Math.Sqrt(d1 * d2); }
        (double rms, double max) Shape(Vec3d[] a, Vec3d[] b) { double e = 0, mx = 0; long cnt = 0; for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) { double d = (a[i] - a[j]).Length - (b[i] - b[j]).Length; e += d * d; mx = Math.Max(mx, Math.Abs(d)); cnt++; } return (Math.Sqrt(e / cnt), mx); }
        // modes
        t.Restart();
        var modes = flat.SoftestModes(new Mite.Core.Kinetics.ScissorNet.Options { Fairness = 0.3, Fixed = new[] { centre } }, 3);
        long modeMs = t.ElapsedMilliseconds;
        var modeRows = modes.Select(md => new { stiffness = md.stiffness, outOfPlane = R3(Math.Sqrt(md.positions.Sum(q => q.Z * q.Z) / md.positions.Sum(q => q.LengthSquared))), corr = R3(Corr(designH, md.positions.Select(q => q.Z).ToArray())) }).ToArray();
        // the lock: held flat, no kick, no table
        t.Restart();
        var lockOpt = new Mite.Core.Kinetics.ScissorNet.Options { Fairness = 0.3, Steps = 4, MaxIterations = 60, Fixed = new[] { centre } };
        var lockNet = flat.DeployFromFlat(lockOpt, centre, up, Math.PI / 2, 0.0, out _);
        lockOpt.Ground = Array.Empty<int>();
        var locked = lockNet.Solve(lockOpt).Last;
        long lockMs = t.ElapsedMilliseconds;
        // the deployment
        t.Restart();
        var depOpt = new Mite.Core.Kinetics.ScissorNet.Options { Fairness = 0.3, Steps = 10, MaxIterations = 80 };
        var depNet = flat.DeployFromFlat(depOpt, centre, up, Math.PI / 2, 0.5, out var kickMode);
        var dep = depNet.Solve(depOpt);
        long depMs = t.ElapsedMilliseconds;
        var depRows = new List<object>(); var depScenes = new List<object>();
        foreach (var st in dep.States)
        {
            var (rms, mx) = Shape(st.Nodes, design.Nodes);
            var ang = design.CrossingAngles(st).Where(a => !double.IsNaN(a)).ToArray();
            double zmin = st.Nodes.Min(q => q.Z), zmax = st.Nodes.Max(q => q.Z);
            depRows.Add(new { fold = R3(st.Fold), rms = R3(rms / design.Scale), max = R3(mx / design.Scale), corr = R3(Corr(designH, st.Nodes.Select(q => q.Z - st.Nodes[centre].Z).ToArray())), rise = R3(zmax - zmin), drift = st.LengthDrift, asymDeg = st.AsymptoticDeviation, scissorMiss = R3(st.ScissorMiss), contacts = st.GroundContacts, groundMiss = st.GroundMiss, minAngle = R3(ang.Min()), maxAngle = R3(ang.Max()), iterations = st.Iterations, converged = st.Converged });
            var polys = design.LathPolylines(st);
            depScenes.Add(new { a = polys.Take(design.CountA).Select(l => Line(l)).ToArray(), b = polys.Skip(design.CountA).Select(l => Line(l)).ToArray(), joints = Enumerable.Range(0, design.Nodes.Length).Where(i => design.IsJoint[i]).Select(i => P(st.Nodes[i])).ToArray() });
        }
        // the pull at the singular node (José's sequence): low lobes held on the table, centre pulled to its design height
        t.Restart();
        var ends = design.LathEnds();
        var lowEnds = ends.Where(i => design.Nodes[i].Z - zc < -1e-6).ToArray();
        double rise = -designH.Min();
        var pullOpt = new Mite.Core.Kinetics.ScissorNet.Options { Fairness = 0.3, Steps = 10, MaxIterations = 80, Sliding = lowEnds };
        var pullNet = flat.Lift(pullOpt, centre, up, rise, 0.5);
        var pulled = pullNet.Solve(pullOpt).Last;
        long pullMs = t.ElapsedMilliseconds;
        var (prms, pmax) = Shape(pulled.Nodes, design.Nodes);
        var designRise = designH.Max() - designH.Min();
        var dLast = dep.Last; var (lrms, lmax) = Shape(dLast.Nodes, design.Nodes);
        numbers["kinDeploy"] = new Dictionary<string, object>
        {
            ["laths"] = design.Laths.Count, ["joints"] = design.IsJoint.Count(j => j), ["nodes"] = design.Nodes.Length, ["spacing"] = R3(design.Scale), ["ends"] = ends.Length, ["scissors"] = depOpt.Scissors.Count,
            ["pressDrift"] = pressed.LengthDrift, ["pressAsym"] = pressed.AsymptoticDeviation, ["pressHeight"] = pressed.Nodes.Max(q => Math.Abs(q.Z)), ["pressMs"] = pressMs,
            ["modes"] = modeRows, ["modeMs"] = modeMs, ["kickStiffness"] = kickMode?.stiffness ?? 0,
            ["lockHeight"] = locked.Nodes.Max(q => Math.Abs(q.Z)), ["lockScissorMiss"] = R3(locked.ScissorMiss), ["lockMs"] = lockMs,
            ["designRise"] = R3(designRise), ["rms"] = R3(lrms / design.Scale), ["max"] = R3(lmax / design.Scale), ["corr"] = R3(Corr(designH, dLast.Nodes.Select(q => q.Z - dLast.Nodes[centre].Z).ToArray())),
            ["worstDrift"] = dep.States.Max(x => x.LengthDrift), ["worstAsym"] = dep.States.Max(x => x.AsymptoticDeviation), ["contacts"] = dLast.GroundContacts, ["scissorMiss"] = R3(dLast.ScissorMiss), ["depMs"] = depMs, ["rows"] = depRows,
            ["pull"] = new { rise = R3(rise), lifted = R3(pulled.Nodes[centre].Z - pullNet.Nodes[centre].Z), miss = R3(pulled.TargetMiss), drift = pulled.LengthDrift, asymDeg = pulled.AsymptoticDeviation, rms = R3(prms / design.Scale), held = lowEnds.Length, ms = pullMs },
        };
        // the design aligned onto the deployed state for the overlay: mirror if the other branch came out, then the
        // best rotation about the table normal through the centre node (1-D search), the centre nodes coincident
        var dNodes = (Vec3d[])design.Nodes.Clone();
        if (Corr(designH, dLast.Nodes.Select(q => q.Z - dLast.Nodes[centre].Z).ToArray()) < 0) dNodes = dNodes.Select(q => new Vec3d(q.X, q.Y, 2 * zc - q.Z)).ToArray();
        var dc = dNodes[centre]; var lc = dLast.Nodes[centre];
        double AlignErr(double ang) { double c = Math.Cos(ang), sn = Math.Sin(ang), e = 0; for (int i = 0; i < dNodes.Length; i++) { var q = dNodes[i] - dc; var r = new Vec3d(c * q.X - sn * q.Y, sn * q.X + c * q.Y, q.Z) + lc; e += (r - dLast.Nodes[i]).LengthSquared; } return e; }
        double best = 0, bestE = double.MaxValue;
        for (int k = 0; k < 720; k++) { double a = k * Math.PI / 360; double e = AlignErr(a); if (e < bestE) { bestE = e; best = a; } }
        for (double h = Math.PI / 720; h > 1e-7; h /= 2) { if (AlignErr(best + h) < AlignErr(best)) best += h; else if (AlignErr(best - h) < AlignErr(best)) best -= h; }
        double cb = Math.Cos(best), sb = Math.Sin(best);
        var aligned = dNodes.Select(q => { var r = q - dc; return new Vec3d(cb * r.X - sb * r.Y, sb * r.X + cb * r.Y, r.Z) + lc; }).ToArray();
        double alignRms = Math.Sqrt(aligned.Zip(dLast.Nodes, (a, b) => (a - b).LengthSquared).Average()), alignMax = aligned.Zip(dLast.Nodes, (a, b) => (a - b).Length).Max();
        var nd = (Dictionary<string, object>)numbers["kinDeploy"]; nd["alignRms"] = R3(alignRms / design.Scale); nd["alignMax"] = R3(alignMax / design.Scale);
        var designPolys = design.LathPolylines(new Mite.Core.Kinetics.ScissorNet.State { Nodes = aligned });
        scenes["kinDeploy"] = new { design = designPolys.Select(l => Line(l)).ToArray(), states = depScenes };
    }
}

// ---------- Mechanism Modes: how a scissor net can move before any driver is set ----------
{
    // the doubly ruled hyperboloid grid (one exact mechanism, the closed form), the same grid held along its
    // bottom ring (none), and a flat 60° lattice of straight rods with one corner held (shear + hypar twist)
    const int mmN = 12, mmLv = 2; const double mmRho = 1.0, mmBeta = 30 * Math.PI / 180, mmEps = 1e-6;
    var (hn, hl, hnr) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(mmN, mmLv, mmRho, mmBeta);
    var hyp = new Mite.Core.Kinetics.ScissorNet(hn, hl, mmN, hnr);
    var tMm = Stopwatch.StartNew();
    var hm = Mite.Core.Kinetics.MechanismModes.Compute(hyp, new Mite.Core.Kinetics.ScissorNet.Options { Fixed = new[] { mmLv } }, new Mite.Core.Kinetics.MechanismModes.Options { Count = 4 });
    long hypMs = tMm.ElapsedMilliseconds;
    var (hpn, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(mmN, mmLv, mmRho, mmBeta + mmEps);
    var (hmn, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(mmN, mmLv, mmRho, mmBeta - mmEps);
    var dHyp = hn.Select((q, i) => (hpn[i] - hmn[i]) / (2 * mmEps) - (hpn[mmLv] - hmn[mmLv]) / (2 * mmEps)).ToArray();
    double Cos(Vec3d[] a, Vec3d[] b) { double nm = 0, x = 0, y = 0; for (int i = 0; i < a.Length; i++) { nm += Vec3d.Dot(a[i], b[i]); x += a[i].LengthSquared; y += b[i].LengthSquared; } return Math.Abs(nm) / Math.Sqrt(x * y); }
    int rows = 2 * mmLv + 1;
    var ring = Enumerable.Range(0, mmN).Select(k => k * rows).Concat(Enumerable.Range(0, mmN).Select(j => hl[mmN + j][rows - 1])).Distinct().ToArray();
    var held = Mite.Core.Kinetics.MechanismModes.Compute(hyp, new Mite.Core.Kinetics.ScissorNet.Options { Fixed = ring }, new Mite.Core.Kinetics.MechanismModes.Options { Count = 3 });
    int ln = 4; double lg = Math.PI / 3;
    var lnodes = new Vec3d[(ln + 1) * (ln + 1)];
    for (int i = 0; i <= ln; i++) for (int j = 0; j <= ln; j++) lnodes[i * (ln + 1) + j] = new Vec3d(i + j * Math.Cos(lg), j * Math.Sin(lg), 0);
    var llaths = new List<int[]>();
    for (int j = 0; j <= ln; j++) llaths.Add(Enumerable.Range(0, ln + 1).Select(i => i * (ln + 1) + j).ToArray());
    for (int i = 0; i <= ln; i++) llaths.Add(Enumerable.Range(0, ln + 1).Select(j => i * (ln + 1) + j).ToArray());
    var lat = new Mite.Core.Kinetics.ScissorNet(lnodes, llaths, ln + 1);
    var lm = Mite.Core.Kinetics.MechanismModes.Compute(lat, new Mite.Core.Kinetics.ScissorNet.Options { Fixed = new[] { 0 } }, new Mite.Core.Kinetics.MechanismModes.Options { Count = 6 });
    numbers["mechModes"] = new
    {
        hypMechanisms = hm.Mechanisms, hypModes = hm.Modes.Count, hypGap = hm.Gap, hypCosine = R3(Cos(hm.Modes[0].Displacements, dHyp) * 1e6) / 1e6,
        hypCosts = hm.Modes.Select(m => m.RelativeStiffness).ToArray(), hypMs,
        heldMechanisms = held.Mechanisms, heldSoftest = held.Modes[0].RelativeStiffness,
        latMechanisms = lm.Mechanisms, latGap = lm.Gap, latCosts = lm.Modes.Select(m => m.RelativeStiffness).ToArray(),
        latOut = lm.Modes.Where(m => m.IsMechanism).Select(m => R3(m.OutOfPlane)).ToArray(),
        tolerance = new Mite.Core.Kinetics.MechanismModes.Options().MechanismTolerance,
    };
    object Scene(Mite.Core.Kinetics.ScissorNet net, Mite.Core.Kinetics.MechanismModes.Result r, int k, double amp)
    {
        var rest = net.LathPolylines(new Mite.Core.Kinetics.ScissorNet.State { Nodes = net.Nodes, Normals = net.Normals });
        var moved = net.LathPolylines(Mite.Core.Kinetics.MechanismModes.Displaced(net, r.Modes[k], amp));
        return new { rest = rest.Select(l => Line(l)).ToArray(), a = moved.Take(net.CountA).Select(l => Line(l)).ToArray(), b = moved.Skip(net.CountA).Select(l => Line(l)).ToArray(),
            mechanism = r.Modes[k].IsMechanism, cost = r.Modes[k].RelativeStiffness };
    }
    scenes["mechModes"] = new Dictionary<string, object>
    {
        ["hyp0"] = Scene(hyp, hm, 0, 0.35), ["hyp1"] = Scene(hyp, hm, 1, 0.35), ["held0"] = Scene(hyp, held, 0, 0.35),
        ["lat0"] = Scene(lat, lm, 0, 0.8), ["lat1"] = Scene(lat, lm, 1, 0.8),
    };
}

// ---------- Kinetic State: one moment of a solved motion ----------
{
    // the hyperboloid grid's exact motion β = 20° … 40° over Fold 0 … 1, solved at 5 and at 9 states;
    // read at the solved states (exact) and halfway between (a chord: second order in the step)
    const int ksN = 12, ksLv = 2; const double ksRho = 1.0;
    double KsBeta(double f) => (20 + 20 * f) * Math.PI / 180;
    Mite.Core.Kinetics.KineticMotion KsMotion(int count)
    {
        var (n0, l0, q0) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(ksN, ksLv, ksRho, KsBeta(0));
        var net0 = new Mite.Core.Kinetics.ScissorNet(n0, l0, ksN, q0);
        var sts = Enumerable.Range(0, count).Select(k => { double f = k / (double)(count - 1); var (p, _, q) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(ksN, ksLv, ksRho, KsBeta(f)); return new Mite.Core.Kinetics.ScissorNet.State { Fold = f, Nodes = p, Normals = q }; }).ToList();
        return new Mite.Core.Kinetics.KineticMotion(net0, sts, new Mite.Core.Kinetics.KineticStrain.Options { Profile = new LathProfile(0.05, 0.005, true) });
    }
    (double err, double drift, double solvedErr) KsRead(int count)
    {
        var m = KsMotion(count); double e = 0, d = 0, se = 0;
        for (int k = 0; k < count; k++)
        {
            double f = k / (double)(count - 1);
            var (tr, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(ksN, ksLv, ksRho, KsBeta(f));
            se = Math.Max(se, m.At(f).Nodes.Zip(tr, (a, b) => (a - b).Length).Max());
            if (k + 1 == count) break;
            double fm = (k + 0.5) / (count - 1);
            var (tm, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(ksN, ksLv, ksRho, KsBeta(fm));
            var s = m.At(fm);
            e = Math.Max(e, s.Nodes.Zip(tm, (a, b) => (a - b).Length).Max()); d = Math.Max(d, m.SpacingDrift(s));
        }
        return (e, d, se);
    }
    var k5 = KsRead(5); var k9 = KsRead(9);
    var mk = KsMotion(5);
    var solvedUtil = Mite.Core.Kinetics.KineticStrain.Analyze(mk.Net, mk.States[2], mk.Strain).MaxUtilization;
    var readUtil = Mite.Core.Kinetics.KineticStrain.Analyze(mk.Net, mk.At(0.5), mk.Strain).MaxUtilization;
    numbers["kinState"] = new { err5 = k5.err, err9 = k9.err, ratio = R3(k5.err / k9.err), drift5 = k5.drift, drift9 = k9.drift, driftRatio = R3(k5.drift / k9.drift),
        solvedErr = Math.Max(k5.solvedErr, k9.solvedErr), utilSolved = solvedUtil, utilRead = readUtil };
    object KsScene(double fold)
    {
        var st = mk.At(fold); var (tr, _, _) = Mite.Core.Kinetics.ScissorNet.HyperboloidMechanism(ksN, ksLv, ksRho, KsBeta(fold));
        var read = mk.Net.LathPolylines(st);
        var exact = mk.Net.LathPolylines(new Mite.Core.Kinetics.ScissorNet.State { Nodes = tr });
        return new { a = read.Take(mk.Net.CountA).Select(l => Line(l)).ToArray(), b = read.Skip(mk.Net.CountA).Select(l => Line(l)).ToArray(), exact = exact.Select(l => Line(l)).ToArray(), drift = mk.SpacingDrift(st) };
    }
    scenes["kinState"] = new Dictionary<string, object> { ["solved"] = KsScene(0.5), ["between"] = KsScene(0.625) };
}

// ---------- Kinetic Supports / Kinetic Drivers: supports and drivers as one wire ----------
{
    // a square 4 × 4 lattice of straight rods held at a corner, its far corner pulled up by 1:
    // through a setup (points snapped to the nodes) and through direct node indices — the same motion
    const int sn = 3;
    Mite.Core.Kinetics.ScissorNet Lat()
    {
        var nodes = new Vec3d[(sn + 1) * (sn + 1)];
        for (int i = 0; i <= sn; i++) for (int j = 0; j <= sn; j++) nodes[i * (sn + 1) + j] = new Vec3d(i, j, 0);
        var laths = new List<int[]>();
        for (int j = 0; j <= sn; j++) laths.Add(Enumerable.Range(0, sn + 1).Select(i => i * (sn + 1) + j).ToArray());
        for (int i = 0; i <= sn; i++) laths.Add(Enumerable.Range(0, sn + 1).Select(j => i * (sn + 1) + j).ToArray());
        return new Mite.Core.Kinetics.ScissorNet(nodes, laths, sn + 1);
    }
    var supports = new Mite.Core.Kinetics.KineticSetup(fixedPoints: new[] { new Vec3d(0.05, -0.04, 0) });
    var drivers = new Mite.Core.Kinetics.KineticSetup(move: new[] { new Vec3d(3.03, 2.98, 0) }, to: new[] { new Vec3d(3, 3, 1) });
    var setup = Mite.Core.Kinetics.KineticSetup.Merge(supports, drivers);
    Mite.Core.Kinetics.ScissorNet.Result KsRun(IReadOnlyList<int> fixedIdx, IReadOnlyList<int> driven, IReadOnlyList<Vec3d> targets, out Mite.Core.Kinetics.ScissorNet net)
    {
        net = Lat(); var start = driven.Select(i => Lat().Nodes[i]).ToArray();
        return net.Solve(new Mite.Core.Kinetics.ScissorNet.Options { Fixed = fixedIdx, Driven = driven, Steps = 4, TargetsAt = t => start.Select((p, k) => p + t * (targets[k] - p)).ToArray() });
    }
    var res = setup.Resolve(Lat());
    var viaSetup = KsRun(res.Fixed, res.Driven, res.TargetEnd, out var netS);
    var direct = KsRun(new[] { 0 }, new[] { sn * (sn + 1) + sn }, new[] { new Vec3d(3, 3, 1) }, out _);
    double diff = 0; for (int k = 0; k < direct.States.Count; k++) for (int v = 0; v < direct.States[k].Nodes.Length; v++) diff = Math.Max(diff, (direct.States[k].Nodes[v] - viaSetup.States[k].Nodes[v]).Length);
    var mS = Mite.Core.Kinetics.MechanismModes.Compute(Lat(), new Mite.Core.Kinetics.ScissorNet.Options { Fixed = res.Fixed }, new Mite.Core.Kinetics.MechanismModes.Options { Count = 3 });
    var mD = Mite.Core.Kinetics.MechanismModes.Compute(Lat(), new Mite.Core.Kinetics.ScissorNet.Options { Fixed = new[] { 0 } }, new Mite.Core.Kinetics.MechanismModes.Options { Count = 3 });
    var merged = Mite.Core.Kinetics.KineticSetup.Merge(new Mite.Core.Kinetics.KineticSetup(move: new[] { Vec3d.Zero, new Vec3d(1, 0, 0) }, to: new[] { new Vec3d(9, 9, 9) }),
        new Mite.Core.Kinetics.KineticSetup(move: new[] { new Vec3d(2, 0, 0) }, to: new[] { new Vec3d(7, 7, 7) }));
    numbers["kinSetup"] = new
    {
        fixedIdx = res.Fixed, driven = res.Driven, snap = R3(res.LargestSnap), states = viaSetup.States.Count, motionDiff = diff, targetMiss = viaSetup.Last.TargetMiss,
        modesSame = mS.Mechanisms == mD.Mechanisms && mS.Modes.Zip(mD.Modes, (a, b) => Math.Abs(a.Stiffness - b.Stiffness)).Max() < 1e-12, mechanisms = mS.Mechanisms,
        mergedTo = merged.To.Select(v => v.X).ToArray(), problemsMoveNoTo = new Mite.Core.Kinetics.KineticSetup(move: new[] { Vec3d.Zero }).Problems().Count,
        problemsOk = setup.Problems().Count,
    };
    var restPolys = netS.LathPolylines(new Mite.Core.Kinetics.ScissorNet.State { Nodes = Lat().Nodes });
    var endPolys = netS.LathPolylines(viaSetup.Last);
    scenes["kinSetup"] = new
    {
        rest = restPolys.Select(l => Line(l)).ToArray(),
        a = endPolys.Take(netS.CountA).Select(l => Line(l)).ToArray(), b = endPolys.Skip(netS.CountA).Select(l => Line(l)).ToArray(),
        support = new[] { Line(new[] { new Vec3d(0, 0, 0), new Vec3d(-0.25, -0.25, -0.35), new Vec3d(0.25, 0.25, -0.35), new Vec3d(0, 0, 0) }) },
        pull = new[] { Line(new[] { new Vec3d(3, 3, 0), new Vec3d(3, 3, 1) }) },
    };
}

// ---------- Mite Net: one value for surface + both families (+ profile); lath-end classification ----------
{
    // (1) the saddle's classic (non-continuous) asymptotic net: T-junctions, compared with the bench's own Ends()
    var saddle = TestMeshes.CreateSaddle(40, 2.0);
    var pc = PrincipalCurvature.Compute(saddle);
    var field = AsymptoticCurves.ComputeDirections(pc);
    var a = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Continuous = false }, field.Family2);
    var b = EvenlySpacedNet.TraceField(saddle, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Continuous = false }, field.Family1);
    var net = new LathNet(saddle, a, b, NetKind.Asymptotic, 0.15).WithProfiles(LathProfile.Rectangle(0.08, 0.012, true), LathProfile.Rectangle(0.08, 0.012, false));
    var t = Stopwatch.StartNew();
    var ends = net.Ends();
    t.Stop();
    var bench = Ends(a.Concat(b).ToList(), new MeshProjection(saddle));
    // the bench counts an end as "border" when it is more than 1e-6 off the mesh; ends that are
    // off the facets (last chord under the surface) but exactly on another lath are T-junctions
    var projS = new MeshProjection(saddle);
    int offMeshT = ends.Count(e => e.Kind == LathEndKind.OnLath && !projS.IsOnBoundary(projS.ClosestPoint(e.Point, projS.NearestVertexGlobal(e.Point)), 1e-4)
        && (projS.ClosestPoint(e.Point, projS.NearestVertexGlobal(e.Point)).Point - e.Point).Length > 1e-6);
    var bytes = net.ToBytes(); var back = LathNet.FromBytes(bytes);
    bool exact = back.ToBytes().SequenceEqual(bytes) && Enumerable.Range(0, net.LathCount).All(i => net.Lath(i).SequenceEqual(back.Lath(i)))
        && back.ProfileA!.Value.Upright && !back.ProfileB!.Value.Upright;
    Func<LathEndKind, double[][]> endPts = k => ends.Where(e => e.Kind == k).Select(e => P(e.Point)).ToArray();
    scenes["miteNet"] = new { wire = SaddleWire(40, 2.0, 5), a = a.Select(l => Line(l, 3)).ToArray(), b = b.Select(l => Line(l, 3)).ToArray(), border = endPts(LathEndKind.Border), onLath = endPts(LathEndKind.OnLath), free = endPts(LathEndKind.Free), label = "saddle · classic fill (Continuous = false)" };
    int border = ends.Count(e => e.Kind == LathEndKind.Border), onLath = ends.Count(e => e.Kind == LathEndKind.OnLath), free = ends.Count(e => e.Kind == LathEndKind.Free);

    // (2) exact rulings of x² + y² − z² = 1, 16 per family (A at φ = 2πi/16, B half a step apart):
    // A_φ meets B_ψ once at z = tan((ψ − φ)/2), so 8 joints per ruling inside |z| ≤ 1
    var hyp = TestMeshes.CreateHyperboloid(1, 1, 1, 64, 32);
    Vec3d[] Ruling(double ang, double sgn) => Enumerable.Range(0, 21).Select(k => { double z = -1 + 0.1 * k; return new Vec3d(Math.Cos(ang) - sgn * z * Math.Sin(ang), Math.Sin(ang) + sgn * z * Math.Cos(ang), z); }).ToArray();
    var ha = Enumerable.Range(0, 16).Select(i => Ruling(2 * Math.PI * i / 16, 1)).ToList();
    var hb = Enumerable.Range(0, 16).Select(j => Ruling(2 * Math.PI * (j + 0.5) / 16, -1)).ToList();
    var hnet = new LathNet(hyp, ha, hb, NetKind.Asymptotic);
    var hj = hnet.Joints(); var hends = hnet.Ends(); var htopo = hnet.Topology();
    double hzErr = hj.Max(x => Math.Abs(x.Point.Z - Math.Tan(Math.IEEERemainder(2 * Math.PI * (x.CurveB + 0.5) / 16 - 2 * Math.PI * x.CurveA / 16, 2 * Math.PI) / 2)));
    scenes["miteNetHyp"] = new { wire = RevolveWire(z => Math.Sqrt(1 + z * z), -1, 1, 8, 16), a = ha.Select(l => Line(l, 4)).ToArray(), b = hb.Select(l => Line(l, 4)).ToArray(), border = hends.Where(e => e.Kind == LathEndKind.Border).Select(e => P(e.Point)).ToArray(), onLath = Array.Empty<double[]>(), free = hends.Where(e => e.Kind == LathEndKind.Free).Select(e => P(e.Point)).ToArray(), joints = hj.Select(x => P(x.Point)).ToArray(), label = "hyperboloid · 16 + 16 exact rulings" };

    numbers["miteNet"] = new { a = a.Count, b = b.Count, ends = ends.Count, border, onLath, free, benchEnds = bench.ends, benchBorder = bench.boundary, benchOnCurve = bench.onCurve, benchFloating = bench.floating, offMeshT, joints = net.Joints().Count, contacts = net.Contacts().Count, ms = t.ElapsedMilliseconds, bytes = bytes.Length, roundTrip = exact, summary = net.Summary(),
        hypJoints = hj.Count, hypJointsExpected = 128, hypZErr = hzErr, hypBorder = hends.Count(e => e.Kind == LathEndKind.Border), hypEnds = hends.Count, hypNodes = htopo.Nodes.Length, hypValence4 = htopo.Valence.Count(v => v == 4), hypMembers = htopo.Members.Count, hypLength = R3(hnet.TotalLengthA + hnet.TotalLengthB) };
}

// ---------- Flat Kit: the hub plate of the 3-fold Enneper web ----------
{
    // the kit of the 3-fold Enneper web (as FlatKitTests / HubPlateTests): 20 × 2 strips on a 2-unit web, 0.2 clearance,
    // the six rays of the flat point on a bolted sandwich of two hub plates
    var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
    var pc = PrincipalCurvature.Compute(mesh, 2);
    var web = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options());
    var proj = new MeshProjection(mesh);
    var t = Stopwatch.StartNew();
    var (net, motion, anchor) = FlatKit.Press(web.A, web.B, web.Seed, new Vec3d(0, 0, 1), q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
    var o = new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, Clearance = 0.0002, HubPlates = 2 };
    var kit = FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), o);
    var plain = FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, Clearance = 0.0002 });
    t.Stop();
    var plate = kit.Plates[0];
    var eo = new KitExport.Options { ToMm = 1000, SheetWidth = 2500, SheetHeight = 1250 };   // a 1 : 1 kit of the 2-unit web in metres: 2.5 × 1.25 m sheets
    var sheets = KitExport.StripSheets(kit, eo);
    var plateSheet = sheets.Last();
    var outlines = plateSheet.Shapes.OfType<KitExport.Poly>().Where(x => x.Layer == KitExport.Layer.Cut && x.Closed).ToList();
    double exactArea = (plate.ExactArea() + 0.25 * Math.PI * plate.Bolt * plate.Bolt) * 1e6;
    var hubStrips = kit.Strips.Where(x => x.Marks.Any(m => m.Label == "hub")).ToList();
    var rays = plate.Rays.OrderBy(r => r.Angle).ToList();
    double gapMin = double.MaxValue, gapMax = 0;
    for (int k = 0; k < rays.Count; k++) { double g = (k == rays.Count - 1 ? rays[0].Angle + 2 * Math.PI : rays[k + 1].Angle) - rays[k].Angle; gapMin = Math.Min(gapMin, g); gapMax = Math.Max(gapMax, g); }
    // the clash check at the strip ends: centre lines ≥ t + c apart
    double endGap = 2 * plate.EndRadius * Math.Sin(0.5 * gapMin);
    // the notch / outline check: every hub strip's outline removes exactly its slots and notches
    double worstStripArea = 0;
    foreach (var st in hubStrips)
    {
        double area = Math.Abs(HubPlate.Area(KitExport.StripOutline(st, 1000.0))) * 1e-6;
        double expect = st.Length * st.Width - st.Marks.Where(m => m.Kind != FlatKit.MarkKind.Hole).Sum(m => m.Size * m.Depth);
        worstStripArea = Math.Max(worstStripArea, Math.Abs(area - expect));
    }
    var one = hubStrips[0]; var oneRay = rays.First(r => r.Lath == one.Lath);
    numbers["hubPlate"] = new
    {
        strips = kit.Strips.Count, joints = kit.Joints.Count, hubs = kit.Joints.Count(j => j.Hub), rays = plate.Rays.Count,
        spacing = R3(web.Spacing), firstJoint = R3(plate.Rays.Min(r => r.FirstJoint)),
        gapMinDeg = Math.Round(gapMin * 180 / Math.PI, 5), gapMaxDeg = Math.Round(gapMax * 180 / Math.PI, 5), angleDeviation = plate.AngleDeviation, angleDrift = plate.AngleDrift, states = motion.States.Count,
        width = o.Width, thickness = o.Thickness, clearance = o.Clearance,
        slotWidth = plate.SlotWidth, endRadius = plate.EndRadius, key = plate.Key, notchDepth = plate.NotchDepth, innerRadius = plate.InnerRadius, radius = plate.Radius, freeRadius = plate.FreeRadius,
        bolt = plate.Bolt, bridge = plate.MinBridge, cramped = plate.Cramped, plates = plate.Count, stripWeb = plate.StripWeb(o.Width),
        endGap = endGap, clashLimit = o.Thickness + o.Clearance,
        outlines = outlines.Count, outlineArea = outlines.Count > 0 ? HubPlate.Area(outlines[0].Points) : 0, exactArea, areaError = outlines.Count > 0 ? Math.Abs(HubPlate.Area(outlines[0].Points) - exactArea) / exactArea : 1,
        hubStrips = hubStrips.Count, notches = hubStrips.Sum(x => x.Marks.Count(m => m.Label == "hub")), pinHoles = kit.Strips.Sum(x => x.Marks.Count(m => m.Kind == FlatKit.MarkKind.Hub)),
        plainPinHoles = plain.Strips.Sum(x => x.Marks.Count(m => m.Kind == FlatKit.MarkKind.Hub)),
        trim = hubStrips.Max(x => Math.Min(x.ExtendStart, x.ExtendEnd)), worstStripArea,
        untouched = kit.Strips.Where(x => !hubStrips.Contains(x)).All(x => Math.Abs(x.Length - plain.Strips[kit.Strips.IndexOf(x)].Length) < 1e-12),
        sheets = sheets.Count(d => d.Title.StartsWith("sheet")), plateSheet = plateSheet.Title, ms = t.ElapsedMilliseconds,
    };
    // scene: plan of the hub (mm) — plate outline, bolt, strip footprints to 1.6 R with their notch zones, the first joints; and one strip's elevation at its hub end
    double mm = 1000;
    var foot = new List<object>();
    foreach (var r in rays)
    {
        double ca = Math.Cos(r.Angle), sa = Math.Sin(r.Angle), h = 0.5 * o.Thickness * mm;
        double r0 = plate.EndRadius * mm, r1 = 1.6 * plate.Radius * mm, rk = (plate.EndRadius + plate.Key + o.Clearance) * mm;
        foot.Add(new { label = r.Label, rect = new[] { new[] { r0 * ca + h * sa, r0 * sa - h * ca }, new[] { r1 * ca + h * sa, r1 * sa - h * ca }, new[] { r1 * ca - h * sa, r1 * sa + h * ca }, new[] { r0 * ca - h * sa, r0 * sa + h * ca } }, key = new[] { new[] { r0 * ca, r0 * sa }, new[] { rk * ca, rk * sa } }, first = new[] { r.FirstJoint * mm * ca, r.FirstJoint * mm * sa } });
    }
    var elev = KitExport.StripOutline(one, mm);
    double hubEnd = oneRay.AtStart ? 0 : one.Length * mm;
    scenes["hubPlate"] = new
    {
        outline = plate.Outline().Select(q => new[] { q.X * mm, q.Y * mm }).ToArray(), bolt = plate.Bolt * mm * 0.5, radius = plate.Radius * mm, inner = plate.InnerRadius * mm, foot,
        strip = new { label = one.Label, outline = elev.Select(q => new[] { q.X, q.Y }).ToArray(), hubEnd, length = one.Length * mm, width = one.Width * mm, window = Math.Max(2.2 * plate.Radius, 1.12 * oneRay.FirstJoint) * mm,
            marks = one.Marks.Select(m => new { at = m.At * mm, size = m.Size * mm, depth = m.Depth * mm, kind = m.Kind.ToString(), label = m.Label }).ToArray() },
    };
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
    // Dynamic relaxation hanging net: Barnes' scheme (fictitious masses, reset to the kinetic-energy peak) against the 1.3.2 integrator
    t.Restart();
    var dr = DynamicRelaxation.Compute(grid, bnd, new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000 });
    t.Stop();
    var drOld = DynamicRelaxation.Compute(grid, bnd, new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000, FictitiousMasses = false, ResetToPeak = false });
    var drMesh = new MeshData(dr.Vertices, grid.Faces);
    scenes["dynrelax"] = new { before = GridWire(grid, n, n, 2), after = GridWire(drMesh, n, n, 2) };
    // the same net with rest length 0 is a force-density net: Schek's linear solution must agree
    var drQ = DynamicRelaxation.Compute(grid, bnd, new DynamicRelaxation.Options { Stiffness = 1, RestScale = 0, Loads = loads, MaxIterations = 5000, Tolerance = 1e-8 });
    double drVsFdm = drQ.Vertices.Zip(fd.Vertices, (a2, b2) => (a2 - b2).Length).Max();
    numbers["dynrelax"] = new { iterations = dr.Iterations, resets = dr.Resets, iterationsOld = drOld.Iterations, resetsOld = drOld.Resets, sameAsOld = Math.Round(dr.Vertices.Zip(drOld.Vertices, (a2, b2) => (a2 - b2).Length).Max(), 6), converged = dr.Converged, ms = t.ElapsedMilliseconds, sag = R3(-dr.Vertices.Min(p => p.Z)), residual = dr.Residual, maxForce = R3(dr.EdgeForces.Max()), minForce = R3(dr.EdgeForces.Min()), vsFdm = drVsFdm, vsFdmSteps = drQ.Iterations };
    // Elastic gridshell: 6 column laths of length 1 pushed to a chord of 0.8 (ends fixed), cross laths every 0.2;
    // every column is the pinned Euler elastica (rise k L / K(k), 2E(k)/K(k) − 1 = 0.8), the cross laths stay straight
    {
        int nx = 5, ny = 32; double L = 1, ratio = 0.8, pitch = 0.2;
        var gq = TestMeshes.CreateQuadGrid(nx, ny, 1.0);
        var gv = new Vec3d[gq.VertexCount];
        for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) { double s = j / (double)ny; gv[j * (nx + 1) + i] = new Vec3d(i * pitch - 0.5, s * ratio * L - 0.4, 0.05 * Math.Sin(Math.PI * s)); }
        var gm = new MeshData(gv, gq.Faces);
        var ge = gm.BuildEdges(); var rest = new double[ge.Length];
        for (int e = 0; e < ge.Length; e++) rest[e] = ge[e].v1 - ge[e].v0 == 1 ? pitch : L / ny;
        var gfix = new bool[gm.VertexCount]; for (int i = 0; i <= nx; i++) { gfix[i] = true; gfix[ny * (nx + 1) + i] = true; }
        t.Restart();
        var gs = DynamicRelaxation.Compute(gm, gfix, new DynamicRelaxation.Options { Stiffness = 1e6 * ny, Bending = 1, RestLengths = rest, Tolerance = 1e-9, MaxIterations = 400000 });
        t.Stop();
        // exact elastica: k from 2E(k)/K(k) − 1 = ratio, rise = k L / K(k), x(s) by quadrature of the tangent angle
        double Simpson(Func<double, double> f, double a, double b) { int m = 4000; double h = (b - a) / m, sum = f(a) + f(b); for (int i = 1; i < m; i++) sum += (i % 2 == 1 ? 4 : 2) * f(a + i * h); return sum * h / 3; }
        double K(double k) => Simpson(th => 1 / Math.Sqrt(1 - k * k * Math.Sin(th) * Math.Sin(th)), 0, Math.PI / 2);
        double E(double k) => Simpson(th => Math.Sqrt(1 - k * k * Math.Sin(th) * Math.Sin(th)), 0, Math.PI / 2);
        double lo = 1e-9, hi = 1 - 1e-9; for (int it = 0; it < 200; it++) { double k = 0.5 * (lo + hi); if (2 * E(k) / K(k) - 1 - ratio > 0) lo = k; else hi = k; }
        double kk = 0.5 * (lo + hi), riseExact = kk * L / K(kk);
        // exact curve: θ(s) with sin(θ/2) = k sn(2K s/L) — sampled by integrating dθ/ds = 2 k √(P/EI) cos φ... simpler: use the DR polyline's own x and the closed-form rise for the self-test, and draw the elastica from θ(s) by Simpson on the ODE
        double rise = 0, worstRise = 0;
        for (int i = 0; i <= nx; i++) { double r1 = 0; for (int j = 0; j <= ny; j++) r1 = Math.Max(r1, gs.Vertices[j * (nx + 1) + i].Z); rise = Math.Max(rise, r1); worstRise = Math.Max(worstRise, Math.Abs(r1 - riseExact) / riseExact); }
        double worstRow = 0; for (int j = 1; j < ny; j++) for (int i = 1; i <= nx; i++) worstRow = Math.Max(worstRow, (gs.Vertices[j * (nx + 1) + i] - gs.Vertices[j * (nx + 1) + i - 1] - new Vec3d(pitch, 0, 0)).Length);
        double arc = 0; for (int j = 0; j < ny; j++) arc += (gs.Vertices[(j + 1) * (nx + 1)] - gs.Vertices[j * (nx + 1)]).Length;
        double maxCurv = gs.Curvatures.Max(c => c.Length > 0 ? c.Max() : 0);
        // curvature of the exact elastica at midspan: κ = 2 k √(P/EI), √(P/EI) = 2 K(k) / L
        double curvExact = 2 * kk * 2 * K(kk) / L;
        scenes["dynrelaxGrid"] = new { before = GridWire(gm, nx, ny, 1), after = GridWire(new MeshData(gs.Vertices, gq.Faces), nx, ny, 1) };
        numbers["dynrelaxGrid"] = new { laths = gs.Laths.Length, iterations = gs.Iterations, resets = gs.Resets, converged = gs.Converged, ms = t.ElapsedMilliseconds, rise = R3(rise), riseExact = R3(riseExact), riseErr = worstRise, rowOff = worstRow, arc, maxCurv = R3(maxCurv), curvExact = R3(curvExact) };
    }
    // Planarization: noisy grid — the projection loop (Method 0) against the guided projection (Method 1)
    var rng = new Random(3); var nz = (Vec3d[])grid.Vertices.Clone();
    for (int i = 0; i < nz.Length; i++) nz[i] = new Vec3d(nz[i].X, nz[i].Y, 0.3 * Math.Sin(nz[i].X * 0.7) * Math.Cos(nz[i].Y * 0.5) + 0.15 * (rng.NextDouble() - 0.5));
    var noisy = new MeshData(nz, grid.Faces);
    var dev0 = Planarization.ComputeDeviation(noisy);
    t.Restart();
    var pl = Planarization.Compute(noisy, bnd, new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-9 });
    t.Stop(); long plMs = t.ElapsedMilliseconds;
    t.Restart();
    var gp = PlanarizationOptimizer.Optimize(noisy, bnd, null, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-9 });
    t.Stop();
    scenes["planar"] = new { before = GridWire(noisy, n, n, 2), after = GridWire(new MeshData(gp.Vertices, grid.Faces), n, n, 2) };
    double gridArea0 = double.MaxValue, gridArea = double.MaxValue;
    foreach (var f in grid.Faces)
    {
        Vec3d a0 = Vec3d.Zero, a1 = Vec3d.Zero;
        for (int e = 0; e < 4; e++) { a0 += Vec3d.Cross(nz[f[e]], nz[f[(e + 1) % 4]]); a1 += Vec3d.Cross(gp.Vertices[f[e]], gp.Vertices[f[(e + 1) % 4]]); }
        gridArea0 = Math.Min(gridArea0, 0.5 * a0.Length); gridArea = Math.Min(gridArea, 0.5 * a1.Length);
    }
    numbers["planar"] = new
    {
        devBefore = R3(dev0.Max()), devAfter = gp.MaxDeviation, iterations = gp.Iterations, maxMove = R3(gp.MaxDisplacement), ms = t.ElapsedMilliseconds, converged = gp.Converged, areaRatio = R3(gridArea / gridArea0),
        projDevAfter = pl.FaceDeviations.Max(), projIterations = pl.Iterations, projMaxMove = R3(pl.Vertices.Zip(nz, (a2, b2) => (a2 - b2).Length).Max()), projMs = plMs
    };
    // a conjugate net knocked off its surface: meridians × parallels of the hyperboloid, every node jittered by 0.02
    {
        var exact = TestMeshes.HyperboloidQuadRevolve(32, 10, 1.0);
        var knocked = TestMeshes.Jitter(exact, 0.02, 7, interiorOnly: false);
        var reference = TestMeshes.CreateHyperboloid(1, 1, 1.3, 96, 48);
        var refProj = new MeshProjection(reference);
        t.Restart();
        var r = PlanarizationOptimizer.Optimize(knocked, null, reference, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-9 });
        t.Stop();
        var p = Planarization.Compute(knocked, new bool[knocked.VertexCount], new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-9 });
        numbers["planarConjugate"] = new
        {
            nodes = knocked.VertexCount, quads = knocked.FaceCount, devBefore = R3(Planarization.ComputeDeviation(knocked).Max()), devAfter = r.MaxDeviation, iterations = r.Iterations, converged = r.Converged,
            maxMove = R3(r.MaxDisplacement), offSurface = r.MaxSurfaceDistance, fromExact = R3(exact.Vertices.Zip(r.Vertices, (a2, b2) => (a2 - b2).Length).Max()), ms = t.ElapsedMilliseconds,
            projDevAfter = p.FaceDeviations.Max(), projOffSurface = p.Vertices.Max(q => (refProj.ClosestPointGlobal(q).Point - q).Length)
        };
    }
    // the asymptotic ruling net of the same hyperboloid: self-conjugate, no planar realisation nearby — the optimiser must say so
    {
        var net = TestMeshes.HyperboloidRulingNet(24, 1.0, out int rulingRows);
        var reference = TestMeshes.CreateHyperboloid(1, 1, 1.3, 96, 48);
        var d0 = Planarization.ComputeDeviation(net);
        var r = PlanarizationOptimizer.Optimize(net, null, reference, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-9 });
        double minArea0 = double.MaxValue, minArea = double.MaxValue;
        foreach (var f in net.Faces)
        {
            Vec3d a0 = Vec3d.Zero, a1 = Vec3d.Zero;
            for (int e = 0; e < 4; e++) { a0 += Vec3d.Cross(net.Vertices[f[e]], net.Vertices[f[(e + 1) % 4]]); a1 += Vec3d.Cross(r.Vertices[f[e]], r.Vertices[f[(e + 1) % 4]]); }
            minArea0 = Math.Min(minArea0, 0.5 * a0.Length); minArea = Math.Min(minArea, 0.5 * a1.Length);
        }
        numbers["planarAsymptotic"] = new
        {
            nodes = net.VertexCount, quads = net.FaceCount, rows = rulingRows, devBefore = R3(d0.Max()), devMeanBefore = R3(d0.Average()), devAfter = r.MaxDeviation, iterations = r.Iterations,
            converged = r.Converged, stalled = r.Stalled, maxMove = R3(r.MaxDisplacement), meanEdge = R3(r.MeanEdge), offSurface = R3(r.MaxSurfaceDistance), areaRatio = R3(minArea / minArea0)
        };
    }
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
    // Dirty meshes with exact answers: orientation, needles, caps
    static string Canon(int[] f) { int s = 0; for (int i = 1; i < f.Length; i++) if (f[i] < f[s]) s = i; return string.Join(",", Enumerable.Range(0, f.Length).Select(i => f[(s + i) % f.Length])); }
    static int Agree(MeshData a, MeshData b) { var set = new HashSet<string>(a.Faces.Select(Canon)); return b.Faces.Count(f => set.Contains(Canon(f))); }
    static double Vol(MeshData m) { double v = 0; foreach (var f in m.Faces) for (int i = 1; i < f.Length - 1; i++) v += Vec3d.Dot(m.Vertices[f[0]], Vec3d.Cross(m.Vertices[f[i]], m.Vertices[f[i + 1]])) / 6; return v; }
    static double Area(MeshData m) => m.Faces.Sum(f => { Vec3d nn = Vec3d.Zero; for (int i = 1; i < f.Length - 1; i++) nn = nn + Vec3d.Cross(m.Vertices[f[i]] - m.Vertices[f[0]], m.Vertices[f[i + 1]] - m.Vertices[f[0]]); return 0.5 * nn.Length; });
    // (a) open saddle with face 0 and ~30 % of the faces reversed
    var sad = TestMeshes.CreateSaddle(16);
    var rr = new Random(2);
    var sadFaces = sad.Faces.Select((x, i) => i == 0 || rr.NextDouble() < 0.3 ? x.Reverse().ToArray() : x).ToArray();
    var sadDirty = new MeshData(sad.Vertices, sadFaces);
    var sadFix = MeshCleanup.Compute(sadDirty);
    var hSad = MeanCurvature.Compute(sad).Values; var hSadFix = MeanCurvature.Compute(sadFix.Mesh).Values;
    // (b) sphere with face 0 and ~70 % reversed
    var sph = TestMeshes.CreateUnitSphere(16);
    rr = new Random(11);
    var sphFaces = sph.Faces.Select((x, i) => i == 0 || rr.NextDouble() < 0.7 ? x.Reverse().ToArray() : x).ToArray();
    var sphFix = MeshCleanup.Compute(new MeshData(sph.Vertices, sphFaces));
    // (c) needles: interior edges split 1e-4 of their length from one end
    var ndV = sph.Vertices.ToList(); var ndF = sph.Faces.Select(f => (int[])f.Clone()).ToList(); var usedV = new HashSet<int>(); int splits = 0;
    var sphEdges = sph.BuildEdges();
    for (int e = 0; e < sphEdges.Length; e += 5)
    {
        var (ea, eb) = sphEdges[e];
        var adj = Enumerable.Range(0, ndF.Count).Where(fi => ndF[fi].Contains(ea) && ndF[fi].Contains(eb)).ToList();
        if (adj.Count != 2) continue;
        var ring = adj.SelectMany(fi => ndF[fi]).Distinct().ToList();
        if (ring.Any(usedV.Contains)) continue;
        foreach (int x in ring) usedV.Add(x);
        int np = ndV.Count; ndV.Add(ndV[ea] + 1e-4 * (ndV[eb] - ndV[ea]));
        foreach (int fi in adj)
        {
            var f = ndF[fi]; int ia = Array.IndexOf(f, ea); int ec = f.First(x => x != ea && x != eb);
            if (f[(ia + 1) % 3] == eb) { ndF[fi] = new[] { ea, np, ec }; ndF.Add(new[] { np, eb, ec }); }
            else { ndF[fi] = new[] { eb, np, ec }; ndF.Add(new[] { np, ea, ec }); }
        }
        splits++;
    }
    var needled = new MeshData(ndV.ToArray(), ndF.ToArray());
    var hClean = MeanCurvature.Compute(sph).Values; var hNeedled = MeanCurvature.Compute(needled).Values;
    var keepNeedles = MeshCleanup.Compute(needled);
    t.Restart();
    var noNeedles = MeshCleanup.Compute(needled, new MeshCleanup.Options { SliverAngle = 1.0 });
    t.Stop();
    var hFixed = MeanCurvature.Compute(noNeedles.Mesh).Values;
    var cleanPos = new HashSet<Vec3d>(sph.Vertices);
    // (d) caps on a flat 8 × 8 grid: a vertex 1e-5 off a cell diagonal
    int gn = 8; var gv = new List<Vec3d>(); var gf = new List<int[]>();
    for (int j = 0; j <= gn; j++) for (int i = 0; i <= gn; i++) gv.Add(new Vec3d(i / (double)gn, j / (double)gn, 0));
    var capCells = new HashSet<int> { 9, 12, 27, 30, 45, 49 };
    for (int j = 0; j < gn; j++)
        for (int i = 0; i < gn; i++)
        {
            int ca = j * (gn + 1) + i, cb = ca + 1, cc = cb + gn + 1, cd = ca + gn + 1;
            if (!capCells.Contains(j * gn + i)) { gf.Add(new[] { ca, cb, cc }); gf.Add(new[] { ca, cc, cd }); continue; }
            Vec3d mid = 0.5 * (gv[ca] + gv[cc]); int cm = gv.Count; gv.Add(mid + 1e-5 * (gv[cb] - mid).Normalized());
            gf.Add(new[] { ca, cb, cm }); gf.Add(new[] { cb, cc, cm }); gf.Add(new[] { ca, cm, cc }); gf.Add(new[] { ca, cc, cd });
        }
    var capped = new MeshData(gv.ToArray(), gf.ToArray());
    var capFix = MeshCleanup.Compute(capped, new MeshCleanup.Options { SliverAngle = 1.0 });
    numbers["cleanupDirty"] = new
    {
        saddleFaces = sad.FaceCount, saddleAgreeIn = Agree(sad, sadDirty), saddleAgreeOut = Agree(sad, sadFix.Mesh), saddleFlipped = sadFix.Info.FlippedFaces,
        saddleHSignFlips = hSad.Zip(hSadFix, (x, y) => x * y < 0 ? 1 : 0).Sum(),
        sphereFaces = sph.FaceCount, sphereAgreeOut = Agree(sph, sphFix.Mesh), sphereFlipped = sphFix.Info.FlippedFaces,
        sphereVolume = Math.Round(Vol(sphFix.Mesh), 4), sphereVolumeClean = Math.Round(Vol(sph), 4),
        splits, needleMinBefore = Math.Round(noNeedles.Info.MinAngleBefore, 5), needleMinAfter = Math.Round(noNeedles.Info.MinAngleAfter, 3),
        needlesReported = keepNeedles.Info.SliversLeft, needleReport = keepNeedles.Info.Report,
        needlesCollapsed = noNeedles.Info.NeedlesCollapsed, needlesLeft = noNeedles.Info.SliversLeft,
        needleVerts = noNeedles.Mesh.VertexCount, cleanVerts = sph.VertexCount,
        needleFacesBack = Agree(sph, noNeedles.Mesh), needleMoved = noNeedles.Mesh.Vertices.Count(q => !cleanPos.Contains(q)),
        hErrDirty = Math.Round(hClean.Select((h, i) => Math.Abs(h - hNeedled[i])).Max(), 4),
        hErrFixed = hClean.Length == hFixed.Length ? hClean.Zip(hFixed, (x, y) => Math.Abs(x - y)).Max() : -1,
        needleMs = t.ElapsedMilliseconds,
        caps = capCells.Count, capsFlipped = capFix.Info.CapsFlipped, capsLeft = capFix.Info.SliversLeft,
        capAreaBefore = Area(capped), capAreaAfter = Area(capFix.Mesh), capMinBefore = Math.Round(capFix.Info.MinAngleBefore, 6), capMinAfter = Math.Round(capFix.Info.MinAngleAfter, 2),
        capVerts = capFix.Mesh.VertexCount, capVertsIn = capped.VertexCount,
        report = sphFix.Info.Report,
    };
    // (e) cracks and T-junctions: a sphere from two hemispheres with 24 and 48 meridians (the odd
    // upper equator vertices sit the sagitta 1 − cos 7.5° outside the lower chords), and a
    // hyperboloid from two bands rotated half a step against each other
    static MeshData JoinMeshes(MeshData a, MeshData b) => new MeshData(a.Vertices.Concat(b.Vertices).ToArray(),
        a.Faces.Concat(b.Faces.Select(x => x.Select(i => i + a.VertexCount).ToArray())).ToArray());
    static MeshData Hemi(bool upper, int segments, int rings)
    {
        var hv = new List<Vec3d>(); var hf = new List<int[]>();
        for (int j = 0; j < rings; j++)
        {
            double phi = 0.5 * Math.PI * j / rings, z = Math.Sin(phi), r = Math.Cos(phi); if (!upper) z = -z;
            for (int i = 0; i < segments; i++) { double th = 2.0 * Math.PI * i / segments; hv.Add(new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z)); }
        }
        int pole = hv.Count; hv.Add(new Vec3d(0, 0, upper ? 1 : -1));
        for (int j = 0; j < rings - 1; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * segments + i, b = j * segments + (i + 1) % segments, c = b + segments, d = a + segments;
                if (upper) { hf.Add(new[] { a, b, c }); hf.Add(new[] { a, c, d }); } else { hf.Add(new[] { a, c, b }); hf.Add(new[] { a, d, c }); }
            }
        int last = (rings - 1) * segments;
        for (int i = 0; i < segments; i++) { int a = last + i, b = last + (i + 1) % segments; hf.Add(upper ? new[] { a, b, pole } : new[] { a, pole, b }); }
        return new MeshData(hv.ToArray(), hf.ToArray());
    }
    static MeshData Band(Func<double, double> radius, double z0, double z1, int segments, int rows, double phase)
    {
        var bv = new List<Vec3d>(); var bf = new List<int[]>();
        for (int j = 0; j <= rows; j++)
        {
            double z = z0 + (z1 - z0) * j / rows, r = radius(z);
            for (int i = 0; i < segments; i++) { double th = 2.0 * Math.PI * (i + phase) / segments; bv.Add(new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z)); }
        }
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * segments + i, b = j * segments + (i + 1) % segments, c = b + segments, d = a + segments;
                bf.Add(new[] { a, b, c }); bf.Add(new[] { a, c, d });
            }
        return new MeshData(bv.ToArray(), bf.ToArray());
    }
    static int Euler(MeshData m) { var set = new HashSet<long>(); foreach (var f in m.Faces) for (int i = 0; i < f.Length; i++) { int a = f[i], b = f[(i + 1) % f.Length]; set.Add(a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a); } return m.VertexCount - set.Count + m.FaceCount; }
    static double Deficit(MeshData m)
    {
        var tm = m.ToTriangulated(); var sum = new double[tm.VertexCount];
        foreach (var f in tm.Faces) for (int i = 0; i < 3; i++) { Vec3d u = tm.Vertices[f[(i + 1) % 3]] - tm.Vertices[f[i]], w = tm.Vertices[f[(i + 2) % 3]] - tm.Vertices[f[i]]; sum[f[i]] += Math.Atan2(Vec3d.Cross(u, w).Length, Vec3d.Dot(u, w)); }
        return sum.Sum(x => 2 * Math.PI - x);
    }
    var twoHemi = JoinMeshes(Hemi(false, 24, 6), Hemi(true, 48, 12));
    double sagitta = 1 - Math.Cos(Math.PI / 24);
    var hemiOff = MeshCleanup.Compute(twoHemi);
    t.Restart();
    var hemiOn = MeshCleanup.Compute(twoHemi, new MeshCleanup.Options { CrackTolerance = 0.01 });
    t.Stop();
    long hemiMs = t.ElapsedMilliseconds;
    var kHemi = GaussianCurvature.Compute(hemiOn.Mesh);
    var equator = Enumerable.Range(0, hemiOn.Mesh.VertexCount).Where(i => Math.Abs(hemiOn.Mesh.Vertices[i].Z) < 1e-9).ToArray();
    var kHemiOff = GaussianCurvature.Compute(hemiOff.Mesh);
    var equatorOff = Enumerable.Range(0, hemiOff.Mesh.VertexCount).Where(i => Math.Abs(hemiOff.Mesh.Vertices[i].Z) < 1e-9).ToArray();
    Func<double, double> hypR = z => Math.Sqrt(1 + z * z);
    var twoBands = JoinMeshes(Band(hypR, -1, 0, 64, 16, 0), Band(hypR, 0, 1, 64, 16, 0.5));
    var bandRows = new List<object>();
    long hypMs = 0;
    foreach (double ck in new[] { 0.0, 0.005 })
    {
        t.Restart();
        var bm = MeshCleanup.Compute(twoBands, new MeshCleanup.Options { CrackTolerance = ck });
        t.Stop(); if (ck > 0) hypMs = t.ElapsedMilliseconds;
        var bpc = PrincipalCurvature.Compute(bm.Mesh); var bfield = AsymptoticCurves.ComputeDirections(bpc);
        int total = 0, through = 0; double bow = 0, ang = 0;
        foreach (var (fam, other) in new[] { (bfield.Family1, bfield.Family2), (bfield.Family2, bfield.Family1) })
            foreach (var line in EvenlySpacedNet.TraceField(bm.Mesh, fam, bfield.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.25 }, other))
            {
                total++;
                if (line.Min(q => q.Z) < -0.999 && line.Max(q => q.Z) > 0.999) through++;
                Vec3d chord = line[line.Length - 1] - line[0]; double L = chord.Length;
                foreach (var q in line) bow = Math.Max(bow, Vec3d.Cross(q - line[0], chord).Length / L);
                var (plus, minus) = TestMeshes.HyperboloidRulings(line[line.Length / 2], 1.0, 1.0);
                double Ang(Vec3d u, Vec3d w) => Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(u.Normalized(), w.Normalized())))) * 180 / Math.PI;
                ang = Math.Max(ang, Math.Min(Ang(chord, plus), Ang(chord, minus)));
            }
        bandRows.Add(new { cracks = ck, curves = total, through, bow = R3(bow), angle = R3(ang), closed = bm.Info.CracksClosed, loops = bm.Info.BorderLoops, candidates = bm.Info.CrackCandidates });
    }
    numbers["cleanupCracks"] = new
    {
        sagitta = Math.Round(sagitta, 6),
        candidates = hemiOff.Info.CrackCandidates, candidateDistance = Math.Round(hemiOff.Info.CrackCandidateDistance, 6),
        loopsOff = hemiOff.Info.BorderLoops, partsOff = hemiOff.Info.Components, equatorKOffMax = equatorOff.Max(i => Math.Abs(kHemiOff[i])),
        closed = hemiOn.Info.CracksClosed, merged = hemiOn.Info.CrackVerticesMerged, left = hemiOn.Info.CracksLeft, maxGap = Math.Round(hemiOn.Info.CrackMaxGap, 6),
        loops = hemiOn.Info.BorderLoops, closedParts = hemiOn.Info.ClosedComponents, nonManifold = hemiOn.Info.NonManifoldEdges.Length,
        euler = Euler(hemiOn.Mesh), deficitError = Math.Abs(Deficit(hemiOn.Mesh) - 4 * Math.PI),
        offSphere = hemiOn.Mesh.Vertices.Max(q => Math.Abs(q.Length - 1)), facesAdded = hemiOn.Mesh.FaceCount - twoHemi.FaceCount,
        equatorK = Math.Round(equator.Average(i => kHemi[i]), 3), equatorKMin = Math.Round(equator.Min(i => kHemi[i]), 3), equatorKMax = Math.Round(equator.Max(i => kHemi[i]), 3),
        ms = hemiMs, hyperboloid = bandRows, hypMs, hypFaces = twoBands.FaceCount,
        report = hemiOff.Info.Report,
    };
    // pull a floating circle onto the sphere
    var proj = new MeshProjection(TestMeshes.CreateUnitSphere(32));
    var circ = new List<Vec3d>(); for (int i = 0; i <= 60; i++) { double a = 2 * Math.PI * i / 60; circ.Add(new Vec3d(0.9 * Math.Cos(a), 0.9 * Math.Sin(a), 0.9)); }
    var pulled = circ.Select(p => proj.ClosestPoint(p, -1).Point).ToArray();
    var faired = CurveFairing.SmoothOnSurface(proj, pulled, 10);
    scenes["pull"] = new { wire = SphereWire(8), before = Line(circ.ToArray()), after = Line(faired) };
    numbers["pull"] = new { maxDist = R3(circ.Zip(pulled, (a2, b2) => (a2 - b2).Length).Max()), radiusAfter = R3(faired.Select(p => p.Length).Average()) };
}

// ---------- Lath Strip: developable plank strips (Tang et al. 2016; Schling et al. 2022 §3.4; Starostin & van der Heijden 2007; Freddi et al. 2016) ----------
{
    object StripScene(DevelopableStrip.Result r, int stride)
    {
        // quads between every stride-th station, flagged developable when both ends are
        var quads = new List<double[][]>(); var dev = new List<bool>(); var rul = new List<double[][]>();
        int ns = r.Centre.Length;
        var ks = Enumerable.Range(0, ns).Where(k => k % stride == 0).ToList(); if (ks[^1] != ns - 1) ks.Add(ns - 1);
        for (int q = 0; q + 1 < ks.Count; q++)
        {
            int k0 = ks[q], k1 = ks[q + 1];
            bool d = true; for (int k = k0; k <= k1; k++) d &= r.OnDevelopable[k];
            quads.Add(new[] { P(r.EdgeA[k0]), P(r.EdgeA[k1]), P(r.EdgeB[k1]), P(r.EdgeB[k0]) }); dev.Add(d);
        }
        foreach (int k in ks) rul.Add(new[] { P(r.EdgeA[k]), P(r.EdgeB[k]) });
        return new { quads, dev, rulings = rul, centre = Line(r.Centre, 2) };
    }

    // 1. catenoid z ∈ [−2, 2]: upright 80 mm planks on the traced asymptotic laths
    var mesh = TestMeshes.CreateCatenoid(1.0, 4.0, 64, 64);
    var pc = PrincipalCurvature.Compute(mesh);
    var field = AsymptoticCurves.ComputeDirections(pc);
    var tt = Stopwatch.StartNew();
    var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, field.Family2);
    var proj = new MeshProjection(mesh);
    double w = 0.08, share = TestMeshes.CatenoidDevelopableShare(-2, 2), helicoid = Math.Sqrt(1 + w * w / 4) - 1;
    var catR = A.Where(l => Math.Abs(l[0].Z - l[^1].Z) > 3.9).Select(l => DevelopableStrip.Build(proj, l, new DevelopableStrip.Options { Width = w, Upright = true })!).ToList();
    tt.Stop();
    // drawn 250 mm wide so the rulings read at page scale (the numbers are for 80 mm)
    var shown = A.Where(l => Math.Abs(l[0].Z - l[^1].Z) > 3.9).Where((l, i) => i % 9 == 0).Take(3).Select(l => DevelopableStrip.Build(proj, l, new DevelopableStrip.Options { Width = 0.25, Upright = true })!).ToList();
    scenes["stripCat"] = new { label = "catenoid z ∈ [−2, 2], upright planks on asymptotic laths (drawn 250 mm wide)", wire = Edges(mesh, 8), strips = shown.Select(r => StripScene(r, 3)).ToArray() };
    numbers["stripCat"] = new { laths = catR.Count, shareMin = R3(catR.Min(r => r.DevelopableFraction)), shareMax = R3(catR.Max(r => r.DevelopableFraction)), shareTheory = R3(share), strain = catR.Max(r => r.EdgeStrain), strainTheory = helicoid, bow = R3(catR.Max(r => r.Bow)), planar = catR.Max(r => r.MaxPlanarity), width = w, ms = tt.ElapsedMilliseconds };

    // 1b. exact asymptotic line v ∈ [1.2, 2.2] (|τ| < |κ| throughout): rulings → κg n + τg t at second order
    var conv = new List<object>();
    foreach (int count in new[] { 41, 81, 161 })
    {
        var pts = Enumerable.Range(0, count).Select(i => TestMeshes.CatenoidAsymptoticPoint(1.2 + 1.0 * i / (count - 1))).ToArray();
        var r = DevelopableStrip.Build(pts, pts.Select(TestMeshes.CatenoidNormal).ToArray(), new DevelopableStrip.Options { Width = 0.1, Upright = true })!;
        double err = Enumerable.Range(1, count - 2).Max(i => Math.Abs(r.RulingAngle[i] - TestMeshes.CatenoidAsymptoticDarboux(1.2 + 1.0 * i / (count - 1)).thetaDeg));
        var sq = DevelopableStrip.Build(pts, pts.Select(TestMeshes.CatenoidNormal).ToArray(), new DevelopableStrip.Options { Width = 0.1, Upright = true, MaxLean = 0 })!;
        conv.Add(new { count, angleErr = err, bow = r.Bow, planar = r.MaxPlanarity, developable = r.Developable, lean = R3(r.MaxDevelopableLean), squareStrain = sq.EdgeStrain });
    }
    numbers["stripExact"] = conv;

    // 2. cylinder R = 1: flat 100 mm planks on geodesic helices at 60° and 30° to the axis
    var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 64, 32);
    var projC = new MeshProjection(cyl);
    var helixRows = new List<object>(); var helixScenes = new List<object>();
    foreach (double deg in new[] { 60.0, 30.0 })
    {
        double al = deg * Math.PI / 180;
        var g = GeodesicCurves.Trace(cyl, new[] { 16 * 64 }, new[] { new Vec3d(0, Math.Sin(al), Math.Cos(al)) }, new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 })[0];
        var r = DevelopableStrip.Build(projC, g, new DevelopableStrip.Options { Width = 0.1, Upright = false })!;
        int ns = r.Centre.Length; var mid = Enumerable.Range(ns / 8, 3 * ns / 4).ToArray();
        double axis = mid.Max(k => AngleDeg(r.Rulings[k], new Vec3d(0, 0, 1)));
        double kn = Math.Sin(al) * Math.Sin(al), tg = Math.Sin(al) * Math.Cos(al);
        double theory = deg > 45 ? 0 : Math.Sqrt(1 + 0.0025 * Math.Pow(tg - kn * kn / tg, 2)) - 1;
        helixRows.Add(new { deg, share = R3(r.DevelopableFraction), axisDeg = R3(axis), bow = r.Bow, strain = r.EdgeStrain, strainTheory = theory, lean = R3(r.MaxDevelopableLean), leanTheory = 90 - deg });
        helixScenes.Add(StripScene(r, 3));
    }
    scenes["stripCyl"] = new { label = "cylinder R = 1, flat 100 mm planks on helices at 60° (developable) and 30° (twists)", wire = RevolveWire(z => 1.0, -1, 1, 8, 16), strips = helixScenes.ToArray() };
    numbers["stripCyl"] = helixRows;

    // 3. hyperboloid x² + y² − z² = 1: a straight ruling has no developable; the plank twists as a helicoid
    var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, 64, 32);
    var pcH = PrincipalCurvature.Compute(hyp);
    var ruling = AsymptoticCurves.Trace(hyp, new[] { 16 * 64 }, pcH, false, new AsymptoticCurves.Options { StepSize = 0.02 })[0];
    var rH = DevelopableStrip.Build(new MeshProjection(hyp), ruling, new DevelopableStrip.Options { Width = 0.1, Upright = true })!;
    double zMin = ruling.Min(p => Math.Abs(p.Z));
    double tauMax = 1 / (1 + 2 * zMin * zMin);
    var rH25 = DevelopableStrip.Build(new MeshProjection(hyp), ruling, new DevelopableStrip.Options { Width = 0.25, Upright = true })!;
    scenes["stripHyp"] = new { label = "hyperboloid x² + y² − z² = 1, upright plank on a ruling (drawn 250 mm wide)", wire = RevolveWire(z => Math.Sqrt(1 + z * z), -1, 1, 8, 16), strips = new[] { StripScene(rH25, 3) } };
    numbers["stripHyp"] = new { share = R3(rH.DevelopableFraction), strain = rH.EdgeStrain, strainTheory = Math.Sqrt(1 + 0.0025 * tauMax * tauMax) - 1, lean = R3(rH.MaxDevelopableLean), length = R3(rH.Length) };
}

// ---------- Net Trim: cookie-cutting a net to a boundary ----------
{
    // exact asymptotic lines x ∓ y = c of z = x² − y² inside a circle of radius 1 in plan:
    // one piece per line with |c| < √2, ends at x = c/2 ± ½√(2 − c²), length √(2 − c²)·√(2 + 4c²)
    var saddle = TestMeshes.CreateSaddle(96, 3.0);
    var proj = new MeshProjection(saddle);
    Vec3d OnS(double x, double y) => new(x, y, x * x - y * y);
    Vec3d[] LineA(double c) { double x0 = Math.Max(-1.5, c - 1.5), x1 = Math.Min(1.5, c + 1.5); return Enumerable.Range(0, 240).Select(i => { double x = x0 + (x1 - x0) * i / 239; return OnS(x, x - c); }).ToArray(); }
    Vec3d[] LineB(double c) { double x0 = Math.Max(-1.5, c - 1.5), x1 = Math.Min(1.5, c + 1.5); return Enumerable.Range(0, 240).Select(i => { double x = x0 + (x1 - x0) * i / 239; return OnS(x, c - x); }).ToArray(); }
    Vec3d[] Circle(double r, int n = 360, double z = double.NaN) => Enumerable.Range(0, n + 1).Select(i => { double th = 2 * Math.PI * i / n, x = r * Math.Cos(th), y = r * Math.Sin(th); return double.IsNaN(z) ? OnS(x, y) : new Vec3d(x, y, z); }).ToArray();
    var cs = Enumerable.Range(-7, 15).Select(i => 0.2 * i).ToArray();
    var linesA = cs.Select(LineA).ToList(); var linesB = cs.Select(LineB).ToList();
    var t = Stopwatch.StartNew();
    var cut = NetTrim.Trim(proj, new[] { linesA, linesB }, new[] { Circle(1.0) });
    t.Stop();
    double worstEnd = 0, worstLen = 0, worstOut = 0;
    for (int f = 0; f < 2; f++)
        for (int i = 0; i < cut.Families[f].Count; i++)
        {
            double c = cs[cut.Source[f][i]]; var piece = cut.Families[f][i];
            double h = 0.5 * Math.Sqrt(2 - c * c), xa = c / 2 - h, xb = c / 2 + h;
            Vec3d ea = f == 0 ? OnS(xa, xa - c) : OnS(xa, c - xa), eb = f == 0 ? OnS(xb, xb - c) : OnS(xb, c - xb);
            worstEnd = Math.Max(worstEnd, Math.Min((piece[0] - ea).Length + (piece[^1] - eb).Length, (piece[0] - eb).Length + (piece[^1] - ea).Length));
            worstLen = Math.Max(worstLen, Math.Abs(ArcLen(piece) - Math.Sqrt(2 - c * c) * Math.Sqrt(2 + 4 * c * c)));
            foreach (var p in piece) worstOut = Math.Max(worstOut, Math.Sqrt(p.X * p.X + p.Y * p.Y) - 1);
        }
    int expectedPieces = cs.Count(c => Math.Abs(c) < Math.Sqrt(2));
    // the same loop drawn in plan at z = 5 and projected down
    var plan = NetTrim.Trim(proj, new[] { linesA }, new[] { Circle(1.0, 360, 5.0) }, new NetTrim.Options { Direction = new Vec3d(0, 0, -1) });
    double planDiff = 0;
    for (int i = 0; i < Math.Min(plan.Families[0].Count, cut.Families[0].Count); i++)
        planDiff = Math.Max(planDiff, Math.Max((plan.Families[0][i][0] - cut.Families[0][i][0]).Length, (plan.Families[0][i][^1] - cut.Families[0][i][^1]).Length));
    // annulus R = 1.2 / r = 0.5: the ring is one loop from the border (kept), the hole two (dropped)
    var ring = NetTrim.Trim(proj, new[] { linesA, linesB }, new[] { Circle(1.2), Circle(0.5, 180) });
    int ringPiecesExpected = 0; double ringWorst = 0;
    foreach (double c in cs) ringPiecesExpected += 2 * (Math.Abs(c) < 0.5 * Math.Sqrt(2) ? 2 : Math.Abs(c) < 1.2 * Math.Sqrt(2) ? 1 : 0);
    foreach (var fam in ring.Families) foreach (var piece in fam) foreach (var e in new[] { piece[0], piece[^1] }) { double rho = Math.Sqrt(e.X * e.X + e.Y * e.Y); ringWorst = Math.Max(ringWorst, Math.Min(Math.Abs(rho - 1.2), Math.Abs(rho - 0.5))); }
    var hole = NetTrim.Trim(proj, new[] { linesA }, new[] { Circle(1.2), Circle(0.5, 180) }, new NetTrim.Options { Inside = new[] { Vec3d.Zero } });
    // a traced classic net (T-junction ends) cut to the circle: nothing outside, every inside vertex kept
    var pc = PrincipalCurvature.Compute(saddle);
    var field = AsymptoticCurves.ComputeDirections(pc);
    var o = new EvenlySpacedNet.Options { Spacing = 0.25, Continuous = false, MaxCurves = 80 };
    var famA = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, o);
    var famB = EvenlySpacedNet.TraceField(saddle, field.Family2, field.Exists, -1, o);
    var t2 = Stopwatch.StartNew();
    var net = NetTrim.Trim(proj, new[] { famA, famB }, new[] { Circle(1.0) });
    t2.Stop();
    var kept = new HashSet<Vec3d>(); foreach (var fam in net.Families) foreach (var piece in fam) foreach (var p in piece) kept.Add(p);
    int insideV = 0, insideKept = 0, outsideKept = 0;
    foreach (var fam in new[] { famA, famB }) foreach (var curve in fam) foreach (var p in curve) { double rho = Math.Sqrt(p.X * p.X + p.Y * p.Y); if (rho < 1 - 2e-3) { insideV++; if (kept.Contains(p)) insideKept++; } if (rho > 1 + 2e-3 && kept.Contains(p)) outsideKept++; }
    double cutOff = net.Cuts.Count == 0 ? 0 : net.Cuts.Max(q => Math.Abs(Math.Sqrt(q.X * q.X + q.Y * q.Y) - 1));
    // random points: inside / outside against the plan radius
    var region = NetTrim.Region.Build(proj, new[] { Circle(1.0) });
    var rnd = new Random(7); int tested = 0, wrong = 0, near = 0;
    for (int i = 0; i < 4000; i++) { double x = -1.45 + 2.9 * rnd.NextDouble(), y = -1.45 + 2.9 * rnd.NextDouble(), rho = Math.Sqrt(x * x + y * y); if (Math.Abs(rho - 1) < 2e-3) continue; tested++; if (Math.Abs(rho - 1) < 0.05) near++; if (region.IsInside(OnS(x, y)) != (rho < 1)) wrong++; }
    // hyperboloid rulings between two parallels z = ±0.6: length 2h√2
    var hyp = TestMeshes.CreateHyperboloid(1, 1, 1, 96, 48);
    var projH = new MeshProjection(hyp);
    double hh = 0.6;
    Vec3d[] Ring(double z) => Enumerable.Range(0, 361).Select(i => { double th = 2 * Math.PI * i / 360, r = Math.Sqrt(1 + z * z); return new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z); }).ToArray();
    Vec3d[] Ruling(double th, int sign) => Enumerable.Range(0, 201).Select(i => { double u = -1 + 2.0 * i / 200; return new Vec3d(Math.Cos(th) - sign * u * Math.Sin(th), Math.Sin(th) + sign * u * Math.Cos(th), u); }).ToArray();
    var rulA = Enumerable.Range(0, 24).Select(i => Ruling(2 * Math.PI * i / 24, +1)).ToList();
    var rulB = Enumerable.Range(0, 24).Select(i => Ruling(2 * Math.PI * i / 24 + 0.1, -1)).ToList();
    var band = NetTrim.Trim(projH, new[] { rulA, rulB }, new[] { Ring(hh), Ring(-hh) });
    double bandZ = 0, bandLen = 0;
    foreach (var fam in band.Families) foreach (var piece in fam) { bandZ = Math.Max(bandZ, Math.Max(Math.Abs(Math.Abs(piece[0].Z) - hh), Math.Abs(Math.Abs(piece[^1].Z) - hh))); bandLen = Math.Max(bandLen, Math.Abs(ArcLen(piece) - 2 * hh * Math.Sqrt(2))); }
    numbers["trim"] = new
    {
        lines = 2 * cs.Length, pieces = cut.Pieces, expectedPieces = 2 * expectedPieces, removed = cut.RemovedWhole, cuts = cut.Cuts.Count, regions = cut.Regions, inside = cut.InsideRegions, samples = cut.BoundarySamples, edge = R3(proj.AverageEdgeLength),
        worstEnd, worstLen, worstOut, ms = t.ElapsedMilliseconds, planDiff, planOff = plan.BoundaryOffMesh,
        ringRegions = ring.Regions, ringInside = ring.InsideRegions, ringPieces = ring.Pieces, ringPiecesExpected, ringWorst, holeInside = hole.InsideRegions, holePieces = hole.Pieces, holePiecesExpected = cs.Count(c => Math.Abs(c) < 0.5 * Math.Sqrt(2)),
        netCurves = famA.Count + famB.Count, netPieces = net.Pieces, netCut = net.CutCurves, netRemoved = net.RemovedWhole, netCuts = net.Cuts.Count, insideV, insideKept, outsideKept, cutOff, netMs = t2.ElapsedMilliseconds,
        tested, wrong, near,
        bandRegions = band.Regions, bandInside = band.InsideRegions, bandPieces = band.Pieces, bandZ, bandLen, bandExact = R3(2 * hh * Math.Sqrt(2)),
        report = net.Report,
    };
    scenes["trim"] = new { wire = SaddleWire(96, 3.0, 12), full = famA.Concat(famB).Select(l => Line(l, 3)).ToArray(), a = net.Families[0].Select(l => Line(l, 2)).ToArray(), b = net.Families[1].Select(l => Line(l, 2)).ToArray(), loop = net.Boundary.Select(l => Line(l, 2)).ToArray(), cuts = net.Cuts.Select(P).ToArray() };
    scenes["trimRing"] = new { wire = SaddleWire(96, 3.0, 12), full = linesA.Concat(linesB).Select(l => Line(l, 6)).ToArray(), a = ring.Families[0].Select(l => Line(l, 4)).ToArray(), b = ring.Families[1].Select(l => Line(l, 4)).ToArray(), loop = ring.Boundary.Select(l => Line(l, 2)).ToArray(), cuts = ring.Cuts.Select(P).ToArray() };
    scenes["trimHyp"] = new { wire = RevolveWire(z => Math.Sqrt(1 + z * z), -1, 1, 8, 16), full = rulA.Concat(rulB).Select(l => Line(l, 20)).ToArray(), a = band.Families[0].Select(l => Line(l, 10)).ToArray(), b = band.Families[1].Select(l => Line(l, 10)).ToArray(), loop = band.Boundary.Select(l => Line(l, 2)).ToArray(), cuts = band.Cuts.Select(P).ToArray() };
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

// ---------- palette: black, white and neon (Mite.Core MitePalette) ----------
{
    double Lab(double r, double g, double b) => ColourScience.Lightness(r, g, b);
    double[] Profile(Rgb[] stops, int n = 401) => Enumerable.Range(0, n).Select(i => { var c = MitePalette.SampleExact(stops, i / (double)(n - 1)); return Lab(c.R, c.G, c.B); }).ToArray();
    (double asym, double dev) Diverging(double[] L)
    {
        int mid = L.Length / 2; double asym = 0, dev = 0;
        for (int i = 0; i < mid; i++) asym = Math.Max(asym, Math.Abs(L[i] - L[L.Length - 1 - i]));
        for (int i = 0; i < L.Length; i++) { double s = i <= mid ? i / (double)mid : (L.Length - 1 - i) / (double)mid; double e = i <= mid ? L[0] : L[^1]; dev = Math.Max(dev, Math.Abs(L[i] - (e + (L[mid] - e) * s))); }
        return (asym, dev);
    }
    double SeqDev(double[] L) => Enumerable.Range(0, L.Length).Max(i => Math.Abs(L[i] - (L[0] + (L[^1] - L[0]) * i / (L.Length - 1.0))));
    bool Monotone(double[] L) => Enumerable.Range(1, L.Length - 1).All(i => L[i] > L[i - 1]);
    Rgb[] Even(params double[][] c) => c.Select(x => new Rgb((int)Math.Round(255 * x[0]), (int)Math.Round(255 * x[1]), (int)Math.Round(255 * x[2]))).ToArray();
    // the 1.3.0 maps, resampled evenly (app diverging and sequential had uneven stops: sample them exactly)
    (double r, double g, double b) OldSample((double t, double r, double g, double b)[] st, double t)
    {
        for (int i = 1; i < st.Length; i++) if (t <= st[i].t) { double f = (t - st[i - 1].t) / (st[i].t - st[i - 1].t); return (255 * (st[i - 1].r + (st[i].r - st[i - 1].r) * f), 255 * (st[i - 1].g + (st[i].g - st[i - 1].g) * f), 255 * (st[i - 1].b + (st[i].b - st[i - 1].b) * f)); }
        return (255 * st[^1].r, 255 * st[^1].g, 255 * st[^1].b);
    }
    var oldDiv = new[] { (0.00, 0.10, 0.12, 0.85), (0.25, 0.45, 0.52, 1.00), (0.50, 0.97, 0.97, 0.97), (0.75, 1.00, 0.45, 0.55), (1.00, 0.85, 0.05, 0.20) };
    var oldSeq = new[] { (0.00, 0.04, 0.04, 0.04), (0.35, 0.16, 0.24, 1.00), (0.70, 0.00, 0.85, 0.42), (1.00, 0.96, 1.00, 0.97) };
    double[] OldProfile((double, double, double, double)[] st) => Enumerable.Range(0, 401).Select(i => { var c = OldSample(st, i / 400.0); return Lab(c.r, c.g, c.b); }).ToArray();
    int[][] OldStops((double, double, double, double)[] st, int n = 33) => Enumerable.Range(0, n).Select(i => { var c = OldSample(st, i / (n - 1.0)); return new[] { (int)Math.Round(c.r), (int)Math.Round(c.g), (int)Math.Round(c.b) }; }).ToArray();
    var ghDiv = new[] { new Rgb(33, 102, 172), new Rgb(247, 247, 247), new Rgb(178, 24, 43) };
    var ghVir = new[] { new Rgb(68, 1, 84), new Rgb(59, 82, 139), new Rgb(33, 145, 140), new Rgb(94, 201, 98), new Rgb(253, 231, 37) };

    var dNew = Diverging(Profile(MitePalette.Diverging)); var dOld = Diverging(OldProfile(oldDiv)); var dGh = Diverging(Profile(ghDiv));
    var Ld = Profile(MitePalette.Diverging); var Ls = Profile(MitePalette.Sequential); var LsOld = OldProfile(oldSeq);
    double utilBelow = ColourScience.Lightness(MitePalette.Utilization(0.999)), utilAt = ColourScience.Lightness(MitePalette.Utilization(1.0));
    double utilStepCvd = new[] { Cvd.Protanopia, Cvd.Deuteranopia }.Min(t => ColourScience.Lightness(ColourScience.Simulate(MitePalette.Utilization(0.999), t)) - ColourScience.Lightness(ColourScience.Simulate(MitePalette.Utilization(1.0), t)));
    object Fam(Theme th) => new
    {
        contrastA = Math.Round(ColourScience.ContrastRatio(MitePalette.FamilyA(th), MitePalette.Paper(th)), 2),
        contrastB = Math.Round(ColourScience.ContrastRatio(MitePalette.FamilyB(th), MitePalette.Paper(th)), 2),
        contrastG = Math.Round(ColourScience.ContrastRatio(MitePalette.FamilyG(th), MitePalette.Paper(th)), 2),
        abWorst = Math.Round(ColourScience.WorstCaseDeltaE(MitePalette.FamilyA(th), MitePalette.FamilyB(th)), 1),
        agWorst = Math.Round(ColourScience.WorstCaseDeltaE(MitePalette.FamilyA(th), MitePalette.FamilyG(th)), 1),
        bgWorst = Math.Round(ColourScience.WorstCaseDeltaE(MitePalette.FamilyB(th), MitePalette.FamilyG(th)), 1),
    };
    var pal = MitePalette.Export();
    pal["old"] = new { diverging = OldStops(oldDiv), sequential = OldStops(oldSeq) };
    double R2(double v) => Math.Round(v, 2);
    pal["check"] = new
    {
        divAsym = R2(dNew.asym), divDev = R2(dNew.dev), divEnds = new[] { R2(Ld[0]), R2(Ld[^1]) }, divCentre = R2(Ld[200]),
        divAsymOld = R2(dOld.asym), divDevOld = R2(dOld.dev), divEndsOld = new[] { R2(OldProfile(oldDiv)[0]), R2(OldProfile(oldDiv)[^1]) }, divAsymGh = R2(dGh.asym),
        seqDev = R2(SeqDev(Ls)), seqMonotone = Monotone(Ls), seqRange = new[] { R2(Ls[0]), R2(Ls[^1]) }, seqDevOld = R2(SeqDev(LsOld)), seqDevGh = R2(SeqDev(Profile(ghVir))),
        utilStep = R2(utilBelow - utilAt), utilStepCvd = R2(utilStepCvd),
        light = Fam(Theme.Light), dark = Fam(Theme.Dark),
        benchOldAB = Math.Round(ColourScience.WorstCaseDeltaE(Rgb.FromHex("#0F7B8A"), Rgb.FromHex("#9A3D6E")), 1),
        appOldGLight = Math.Round(ColourScience.ContrastRatio(Rgb.FromHex("#00c865"), new Rgb(255, 255, 255)), 2),
        divProfile = Ld.Where((_, i) => i % 10 == 0).Select(x => Math.Round(x, 2)).ToArray(),
        seqProfile = Ls.Where((_, i) => i % 10 == 0).Select(x => Math.Round(x, 2)).ToArray(),
        divProfileOld = OldProfile(oldDiv).Where((_, i) => i % 10 == 0).Select(x => Math.Round(x, 2)).ToArray(),
        seqProfileOld = LsOld.Where((_, i) => i % 10 == 0).Select(x => Math.Round(x, 2)).ToArray(),
    };
    numbers["palette"] = pal;
}

// ---------- Net Distributions: spacing, crossing angles, utilization as distributions ----------
{
    object Stats(Distribution d, double target = double.NaN) => new
    {
        n = d.Count, min = R3(d.Min), p5 = R3(d.Quantile(0.05)), median = R3(d.Median), p95 = R3(d.Quantile(0.95)), max = R3(d.Max),
        cv = R3(100 * d.CoefficientOfVariation),
        within5 = double.IsNaN(target) ? -1 : R3(100 * d.ShareNear(target, 0.05)), within75 = double.IsNaN(target) ? -1 : R3(100 * d.ShareNear(target, 0.075)),
        fdBins = d.Histogram().Bins, fdRule = d.Histogram().Rule
    };
    object Hist(Distribution d, double lo, double hi, int bins) { var h = d.Histogram(bins, lo, hi); return new { lo, hi, counts = h.Counts, below = h.Below, above = h.Above }; }

    // 1. analytic ground truth
    // parallel lines and concentric circles in a plane
    Vec3d Up(Vec3d _) => new Vec3d(0, 0, 1);
    var lines = Enumerable.Range(0, 9).Select(k => Enumerable.Range(0, 41).Select(i => new Vec3d(0.05 * i, 0.25 * k, 0)).ToArray()).ToList();
    var rl = NetDistributions.FamilySpacing(lines, Up, new NetDistributions.SpacingOptions { Target = 0.25 });
    var rings = Enumerable.Range(0, 6).Select(k => Enumerable.Range(0, 721).Select(i => { double a = 2 * Math.PI * (i % 720) / 720; return new Vec3d((1 + 0.2 * k) * Math.Cos(a), (1 + 0.2 * k) * Math.Sin(a), 0); }).ToArray()).ToList();
    var rr = NetDistributions.FamilySpacing(rings, Up, new NetDistributions.SpacingOptions { Target = 0.2, MaxDistance = 0.5 });
    // sphere meridians Δ = 30° apart: the cut at polar angle θ meets the next meridian at tan θ' = tan θ / cos Δ
    double delta = 2 * Math.PI / 12;
    var mer = Enumerable.Range(0, 12).Select(k => Enumerable.Range(0, 801).Select(i => { double th = 0.3 + (Math.PI - 0.6) * i / 800, ph = k * delta; return new Vec3d(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th)); }).ToArray()).ToList();
    var rm = NetDistributions.FamilySpacing(mer, p => p.Normalized(), new NetDistributions.SpacingOptions { Step = 0.02, MaxDistance = 0.8 });
    double merWorst = 0;
    for (int i = 0; i < rm.Points.Length; i++)
    {
        double th = Math.Acos(Math.Max(-1, Math.Min(1, rm.Points[i].Z))), thq = Math.Atan2(Math.Sin(th), Math.Cos(th) * Math.Cos(delta));
        double ex = (new Vec3d(Math.Sin(thq) * Math.Cos(delta), Math.Sin(thq) * Math.Sin(delta), Math.Cos(thq)) - new Vec3d(Math.Sin(th), 0, Math.Cos(th))).Length;
        merWorst = Math.Max(merWorst, Math.Max(Math.Abs(rm.Left[i] - ex), Math.Abs(rm.Right[i] - ex)));
    }
    // catenoid asymptotic lines u − v = c, 24 around: spacing cosh v · Δc/√2 to first order
    double dc = 2 * Math.PI / 24, waist = dc / Math.Sqrt(2);
    Vec3d Xc(double u, double v) => new Vec3d(Math.Cosh(v) * Math.Cos(u), Math.Cosh(v) * Math.Sin(u), v);
    var catLines = Enumerable.Range(0, 24).Select(k => Enumerable.Range(0, 1601).Select(i => { double v = -2 + 4.0 * i / 1600; return Xc(k * dc + v, v); }).ToArray()).ToList();
    var rc = NetDistributions.FamilySpacing(catLines, p => { double u = Math.Atan2(p.Y, p.X); return new Vec3d(Math.Cos(u), Math.Sin(u), -Math.Sinh(p.Z)) / Math.Cosh(p.Z); }, new NetDistributions.SpacingOptions { Target = waist, Step = 0.02, MaxDistance = 1.5 * Math.Cosh(2) * waist });
    double catShareTheory = Math.Sinh(Math.Log(1.05 + Math.Sqrt(1.05 * 1.05 - 1))) / Math.Sinh(2);
    // hyperbolic paraboloid z = xy: rulings x = a and y = b cross at cos φ = |ab| / √((1 + a²)(1 + b²))
    var vals = Enumerable.Range(0, 9).Select(i => -1 + 0.25 * i).ToArray();
    var hA = vals.Select(a => Enumerable.Range(0, 23).Select(i => { double y = -1.1 + 0.1 * i; return new Vec3d(a, y, a * y); }).ToArray()).ToList();
    var hB = vals.Select(b => Enumerable.Range(0, 23).Select(i => { double x = -1.1 + 0.1 * i; return new Vec3d(x, b, x * b); }).ToArray()).ToList();
    var ha = NetDistributions.CrossingAngles(hA, hB);
    double hypWorst = 0;
    for (int i = 0; i < ha.Points.Length; i++) { double a = ha.Points[i].X, b = ha.Points[i].Y; hypWorst = Math.Max(hypWorst, Math.Abs(ha.PerCrossing[i] - Math.Acos(Math.Abs(a * b) / Math.Sqrt((1 + a * a) * (1 + b * b))) * 180 / Math.PI)); }
    numbers["distAnalytic"] = new
    {
        linesWorst = rl.Spacing.Values.Max(v => Math.Abs(v - 0.25)), linesN = rl.Spacing.Count, linesOpen = rl.OpenSides,
        ringsWorst = rr.Spacing.Values.Max(v => Math.Abs(v - 0.2)), ringsN = rr.Spacing.Count,
        meridianWorst = merWorst, meridianN = 2 * rm.Points.Length, meridianEquator = R3(rm.Spacing.Max), meridianEquatorTheory = R3(2 * Math.Sin(delta / 2)),
        catShare = R3(100 * rc.Within5), catShareTheory = R3(100 * catShareTheory), catWaist = R3(rc.Spacing.Min), catWaistTheory = R3(waist),
        hypN = ha.Angles.Count, hypWorst, hypMin = R3(ha.Angles.Min), hypMax = R3(ha.Angles.Max),
    };

    // 2. traced nets: fill vs web on the catenoid, fill on the saddle
    var distRows = new Dictionary<string, object>();
    var cases = new (string key, string label, MeshData mesh, double spacing, NetLayout layout, bool continuous)[]
    {
        ("saddleFill", "saddle z = x² − y², fill (continuous)", TestMeshes.CreateSaddle(40, 2.0), 0.15, NetLayout.Fill, true),
        ("saddleClassic", "saddle z = x² − y², fill (classic)", TestMeshes.CreateSaddle(40, 2.0), 0.15, NetLayout.Fill, false),
        ("catFill", "catenoid, fill (continuous)", TestMeshes.CreateCatenoid(1, 2, 96, 48), 0.3, NetLayout.Fill, true),
        ("catWeb", "catenoid, web from the rims", TestMeshes.CreateCatenoid(1, 2, 96, 48), 0.3, NetLayout.WebBorder, true),
    };
    foreach (var (key, label, mesh, spacing, layout, continuous) in cases)
    {
        var tt = Stopwatch.StartNew();
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = spacing, Layout = layout, Continuous = continuous }, field.Family2);
        var B = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = spacing, Layout = layout, Continuous = continuous }, field.Family1);
        var proj = new MeshProjection(mesh);
        var t2 = Stopwatch.StartNew();
        var sA = NetDistributions.FamilySpacing(proj, A, new NetDistributions.SpacingOptions { Target = spacing });
        var sB = NetDistributions.FamilySpacing(proj, B, new NetDistributions.SpacingOptions { Target = spacing });
        var ang = NetDistributions.CrossingAngles(A, B);
        t2.Stop();
        // upright 60 × 8 mm laths, timber strain limit: on an asymptotic lath the twist τg = √−K governs
        var laths = A.Concat(B).Select(l => LathAnalysis.Analyze(proj, l, new LathAnalysis.Options { Width = 0.06, Thickness = 0.008, MaxStrain = 0.005, Upright = true })).ToList();
        var uPer = NetDistributions.Utilization(laths);
        var both = new Distribution(sA.Spacing.Values.Concat(sB.Spacing.Values));
        // closed-form crossing angle: on z = x² − y² the asymptotic directions are
        // (1, 1, 2(x − y)) and (1, −1, 2(x + y)); on the catenoid (H = 0) 90°
        double angWorst = 0; int angOff2 = 0;
        for (int i = 0; i < ang.Points.Length; i++)
        {
            var q = ang.Points[i]; double exact = 90;
            if (key.StartsWith("saddle"))
            {
                var d1 = new Vec3d(1, 1, 2 * (q.X - q.Y)); var d2 = new Vec3d(1, -1, 2 * (q.X + q.Y));
                exact = Math.Acos(Math.Abs(Vec3d.Dot(d1, d2)) / (d1.Length * d2.Length)) * 180 / Math.PI;
            }
            angWorst = Math.Max(angWorst, Math.Abs(ang.PerCrossing[i] - exact));
            if (Math.Abs(ang.PerCrossing[i] - exact) > 2) angOff2++;
        }
        distRows[key] = new
        {
            label, spacing, a = A.Count, b = B.Count, msMeasure = t2.ElapsedMilliseconds,
            spacingA = Stats(sA.Spacing, spacing), spacingB = Stats(sB.Spacing, spacing), spacingAll = Stats(both, spacing),
            openA = sA.OpenSides, samplesA = sA.Points.Length,
            angles = Stats(ang.Angles), angWorst = R3(angWorst), angOff2, tinyGaps = both.Values.Count(v => v < 0.15 * spacing), util = Stats(uPer), utilOver = R3(100 * uPer.ShareAbove(1)),
            hA = Hist(new Distribution(sA.Spacing.Values.Select(v => v / spacing)), 0, 2.5, 25),
            hB = Hist(new Distribution(sB.Spacing.Values.Select(v => v / spacing)), 0, 2.5, 25),
            hAng = Hist(ang.Angles, 0, 90, 18),
            hUtil = Hist(uPer, 0, 1.5, 15),
        };
    }
    numbers["dist"] = distRows;
}

// ---------- Asymptotic Web (Schling): catenoid rotational web against the isothermal lattice ----------
{
    var mesh = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 64).ToTriangulated();
    var t = Stopwatch.StartNew();
    var pcW = PrincipalCurvature.Compute(mesh, 2);
    var w = AsymptoticWeb.Build(mesh, pcW, -1, new AsymptoticWeb.Options { Spacing = 0.3 });
    t.Stop();
    // asymptotic lines of the catenoid are u ± v = const (u azimuth, v = z): every node on the lattice of the seed's nodes
    double dphi = 2 * Math.PI / Math.Max(1, w.A.Count), u0 = Math.Atan2(w.Seed.Y, w.Seed.X), lat = 0;
    foreach (var p in w.Nodes)
        foreach (double x in new[] { Math.Atan2(p.Y, p.X) + p.Z, Math.Atan2(p.Y, p.X) - p.Z })
        { double r = (x - u0) / dphi; lat = Math.Max(lat, Math.Abs(r - Math.Round(r))); }
    numbers["asymWebSchling"] = new { nodes = w.Nodes.Count, a = w.A.Count, b = w.B.Count, rotational = w.RotationalWeb, latticeErr = R3(lat), meridianKg = w.DiagonalGeodesicError[0], symmetryError = w.SymmetryError, ms = t.ElapsedMilliseconds };
    scenes["asymWebSchling"] = new { wire = Edges(mesh, 16), A = w.A.Select(l => Line(l, 4)).ToArray(), B = w.B.Select(l => Line(l, 4)).ToArray() };
}

// ---------- Mode menus: the catalogue against the parameter tables (components.json) ----------
{
    var comps = JsonDocument.Parse(File.ReadAllText("components.json")).RootElement;
    var rows = new Dictionary<string, object>();
    var problems = new List<string>();
    var missing = new List<string>();
    int inputs = 0;
    foreach (var c in comps.EnumerateArray())
    {
        string name = c.GetProperty("name").GetString()!;
        var modes = Mite.Core.Modes.ModeCatalog.For(name);
        foreach (var p in c.GetProperty("inputs").EnumerateArray())
        {
            string type = p.GetProperty("type").GetString()!, access = p.GetProperty("access").GetString()!, pn = p.GetProperty("name").GetString()!;
            string desc = p.GetProperty("desc").GetString()!;
            bool hasDefault = p.TryGetProperty("default", out var dflt) && dflt.ValueKind != JsonValueKind.Null;
            bool modeLike = access == "item" && hasDefault && (type == "Boolean" || (type == "Integer"
                && System.Text.RegularExpressions.Regex.IsMatch(desc, @"(?<![\d\-−])0 =") && System.Text.RegularExpressions.Regex.IsMatch(desc, @"(?<![\d\-−])1 =")));
            if (modeLike && modes.All(m => m.Input != pn)) missing.Add(name + "." + pn);
        }
        if (modes.Count == 0) continue;
        var list = new List<object>();
        foreach (var m in modes)
        {
            inputs++;
            JsonElement? src = null;
            foreach (var p in c.GetProperty("inputs").EnumerateArray()) if (p.GetProperty("name").GetString() == m.Input) src = p;
            string want = m.Kind == Mite.Core.Modes.ModeKind.Boolean ? "Boolean" : "Integer";
            string? def = src?.GetProperty("default").GetString();
            int? defVal = def == "true" ? 1 : def == "false" ? 0 : int.TryParse(def?.Replace("−", "-"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dv) ? dv : null;
            if (src == null) problems.Add($"{name}.{m.Input}: no such input");
            else if (src.Value.GetProperty("type").GetString() != want) problems.Add($"{name}.{m.Input}: type {src.Value.GetProperty("type").GetString()}, catalogue {want}");
            else if (defVal != m.Default) problems.Add($"{name}.{m.Input}: default {def}, catalogue {m.Default}");
            list.Add(new { input = m.Input, def = m.Default, activeWhen = m.ActiveWhenInput, activeValue = m.ActiveWhenValue,
                options = m.Options.Select(o => new { v = o.Value, label = o.Label, shortLabel = o.Short }).ToArray() });
        }
        rows[name] = new { message = Mite.Core.Modes.ModeCatalog.Message(name, _ => null), modes = list };
    }
    numbers["modes"] = new { components = rows.Count, inputs, problems, missing, byComponent = rows };
}

// ---- Runtime messages with the fix (Mite.Core.Diagnostics.InputAdvice) ----
{
    // z = x² − 0.01 y² on [−0.3, 0.3]²: a saddle so shallow that the families
    // cross at 11.4° (centre) to 13.3°, so the default MinAngle 15° leaves nothing
    const double a = 0.01, h = 0.3;
    var shallow = AnalyticShapes.Grid((x, y) => new Vec3d(x, y, x * x - a * y * y), -h, h, -h, h, 30, 30);
    double Exact(Vec3d p)
    {
        double m = 1 / Math.Sqrt(a);
        var tp = new Vec3d(1, m, 2 * p.X - 2 * a * p.Y * m); var tm = new Vec3d(1, -m, 2 * p.X + 2 * a * p.Y * m);
        return Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(tp, tm)) / (tp.Length * tm.Length))) * 180 / Math.PI;
    }
    var pcS = PrincipalCurvature.Compute(shallow);
    var census = Mite.Core.Diagnostics.InputAdvice.CountSaddle(pcS, 15);
    var adv = Mite.Core.Diagnostics.InputAdvice.SaddleRegion(pcS, 15, shallow.BoundingBoxDiagonal());
    var fieldFix = AsymptoticCurves.ComputeDirections(pcS, shallow, adv[0].Suggested);
    var famFix = EvenlySpacedNet.TraceField(shallow, fieldFix.Family1, fieldFix.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.06 }, fieldFix.Family2);
    var minAngleRow = new
    {
        code = adv[0].Code, message = adv[0].Message, usable15 = census.Usable, saddle = census.Saddle,
        minCross = R3(census.MinCrossing), maxCross = R3(census.MaxCrossing),
        exactMin = R3(shallow.Vertices.Min(Exact)), exactMax = R3(shallow.Vertices.Max(Exact)),
        suggested = adv[0].Suggested, usableFix = fieldFix.Exists.Count(e => e), curvesFix = famFix.Count
    };

    // Spacing against the region: half the region → one or two curves; the suggested tenth fills it
    var sad = TestMeshes.CreateSaddle(30, 2.0);
    var pcSad = PrincipalCurvature.Compute(sad);
    var fSad = AsymptoticCurves.ComputeDirections(pcSad, sad, 15);
    var projSad = new MeshProjection(sad);
    double region = Mite.Core.Diagnostics.InputAdvice.RegionSize(sad, fSad.Exists);
    var oBig = new EvenlySpacedNet.Options { Spacing = 0.55 * region };
    int nBig = EvenlySpacedNet.TraceField(sad, fSad.Family1, fSad.Exists, -1, oBig, fSad.Family2).Count;
    var advR = Mite.Core.Diagnostics.InputAdvice.Spacing(oBig.ResolvedSpacing, true, oBig.ResolvedStepSize, false, projSad.AverageEdgeLength, region);
    var oFix = new EvenlySpacedNet.Options { Spacing = advR[0].Suggested };
    int nFix = EvenlySpacedNet.TraceField(sad, fSad.Family1, fSad.Exists, -1, oFix, fSad.Family2).Count;
    var advE = Mite.Core.Diagnostics.InputAdvice.Spacing(0.5 * projSad.AverageEdgeLength, true, 0.05 * projSad.AverageEdgeLength, false, projSad.AverageEdgeLength, region);
    var spacingRow = new
    {
        region = R3(region), spacing = R3(oBig.ResolvedSpacing), curves = nBig, code = advR[0].Code, message = advR[0].Message,
        suggested = advR[0].Suggested, curvesFix = nFix, edge = R3(projSad.AverageEdgeLength), edgeCode = advE[0].Code, edgeMessage = advE[0].Message
    };

    // Unit slips on the lath section (lengths of the saddle net in metres / millimetres)
    var lengths = EvenlySpacedNet.TraceField(sad, fSad.Family1, fSad.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15 }, fSad.Family2).Select(ArcLen).ToArray();
    var mmInM = Mite.Core.Diagnostics.InputAdvice.SectionScale(80, 12, lengths, 1.0, "Meters");
    var mInMm = Mite.Core.Diagnostics.InputAdvice.SectionScale(0.08, 0.012, lengths.Select(l => 1000 * l), 0.001, "Millimeters");
    var okM = Mite.Core.Diagnostics.InputAdvice.SectionScale(0.08, 0.012, lengths, 1.0, "Meters");
    var unitsRow = new { mmInM = mmInM?.Message, mmSuggested = mmInM?.Suggested, mInMm = mInMm?.Message, mSuggested = mInMm?.Suggested, silent = okM == null };

    // No false alarms on the bench's own shapes and spacings
    int alarms = 0; var alarmText = new List<string>();
    foreach (var (name, mesh, sp) in new (string, MeshData, double)[] {
        ("saddle", sad, 0.15), ("saddle auto", sad, 0), ("hyperboloid", TestMeshes.CreateHyperboloid(), 0.25),
        ("catenoid", TestMeshes.CreateCatenoid(), 0.2), ("Enneper 2", TestMeshes.CreateEnneper(2), 0) })
    {
        var pcX = PrincipalCurvature.Compute(mesh); var fX = AsymptoticCurves.ComputeDirections(pcX, mesh, 15); var pX = new MeshProjection(mesh);
        var oX = new EvenlySpacedNet.Options { Spacing = sp };
        EvenlySpacedNet.TraceField(mesh, fX.Family1, fX.Exists, -1, oX, fX.Family2);
        var l = Mite.Core.Diagnostics.InputAdvice.Spacing(oX.ResolvedSpacing, sp > 0, oX.ResolvedStepSize, false, pX.AverageEdgeLength, Mite.Core.Diagnostics.InputAdvice.RegionSize(mesh, fX.Exists));
        l.AddRange(Mite.Core.Diagnostics.InputAdvice.SaddleRegion(pcX, 15, mesh.BoundingBoxDiagonal()).Where(x => x.Level == Mite.Core.Diagnostics.AdviceLevel.Warning));
        alarms += l.Count; alarmText.AddRange(l.Select(x => name + ": " + x.Code));
    }
    numbers["advice"] = new { minAngle = minAngleRow, spacing = spacingRow, units = unitsRow, falseAlarms = alarms, falseAlarmText = alarmText };
}

// ---------- Farthest-point seeding (Layout 3; Mebarki, Alliez & Devillers 2005) ----------
{
    var t = Stopwatch.StartNew();
    // 1. uniform field on a 2 × 2 plane: the curves must sit at the centres of the
    //    successive widest voids (frame ½ Spacing outside the border, voids split
    //    while wider than 1.6 Spacing)
    int gn = 40; double gs = 2.0, sp = 0.13;
    var gv = new List<Vec3d>(); var gf = new List<int[]>();
    for (int j = 0; j <= gn; j++) for (int i = 0; i <= gn; i++) gv.Add(new Vec3d(-gs / 2 + gs * i / gn, -gs / 2 + gs * j / gn, 0));
    for (int j = 0; j < gn; j++) for (int i = 0; i < gn; i++) { int a = j * (gn + 1) + i; gf.Add(new[] { a, a + 1, a + gn + 2 }); gf.Add(new[] { a, a + gn + 2, a + gn + 1 }); }
    var flat = new MeshData(gv.ToArray(), gf.ToArray());
    var uni = Enumerable.Repeat(new Vec3d(1, 0, 0), flat.VertexCount).ToArray();
    var fl = EvenlySpacedNet.TraceField(flat, uni, null, -1, new EvenlySpacedNet.Options { Spacing = sp, Layout = NetLayout.FillFarthest, Continuous = false });
    var cuts = new List<double> { -1 - 0.5 * sp, 0, 1 + 0.5 * sp }; var expect = new List<double> { 0 };
    while (true)
    {
        cuts.Sort(); int bi = -1; double bw = 1.6 * sp - 1e-12;
        for (int i = 0; i + 1 < cuts.Count; i++) if (cuts[i + 1] - cuts[i] >= bw) { bw = cuts[i + 1] - cuts[i]; bi = i; }
        if (bi < 0) break;
        double m = 0.5 * (cuts[bi] + cuts[bi + 1]); cuts.Add(m); expect.Add(m);
    }
    expect.Sort();
    var ys = fl.Select(c => c.Average(q => q.Y)).OrderBy(y => y).ToList();
    double planeErr = ys.Count == expect.Count ? ys.Zip(expect, (a, b) => Math.Abs(a - b) / sp).Max() : double.NaN;
    // 2. torus (R = 3, r = 1) max-curvature lines = meridians: 2^k of them, every outer gap in [0.8, 1.6) Spacing
    var torus = AnalyticShapes.Build("torus", 3, 1, 0, 64);
    var tpc = PrincipalCurvature.Compute(torus, 2);
    double ts = 0.37;
    var mer = EvenlySpacedNet.TraceField(torus, tpc.D1, null, -1, new EvenlySpacedNet.Options { Spacing = ts, Layout = NetLayout.FillFarthest, Continuous = false });
    var us = mer.Select(c => Math.Atan2(c.Average(q => q.Y), c.Average(q => q.X))).OrderBy(u => u).ToList();
    var outer = us.Select((u, i) => 2 * 4.0 * Math.Sin(((i + 1 < us.Count ? us[i + 1] : us[0] + 2 * Math.PI) - u) / 2) / ts).ToList();
    int merExpected = 1; while (2 * 4.0 * Math.Sin(Math.PI / (2 * merExpected)) >= 0.8 * ts) merExpected *= 2;
    var merJl = EvenlySpacedNet.TraceField(torus, tpc.D1, null, -1, new EvenlySpacedNet.Options { Spacing = ts, Continuous = false });
    var usJ = merJl.Select(c => Math.Atan2(c.Average(q => q.Y), c.Average(q => q.X))).OrderBy(u => u).ToList();
    var outerJ = usJ.Select((u, i) => 2 * 4.0 * Math.Sin(((i + 1 < usJ.Count ? usJ[i + 1] : usJ[0] + 2 * Math.PI) - u) / 2) / ts).ToList();
    numbers["torusFarthest"] = new { curves = mer.Count, expected = merExpected, minGap = R3(outer.Min()), maxGap = R3(outer.Max()), fillCurves = merJl.Count, fillMinGap = R3(outerJ.Min()), fillMaxGap = R3(outerJ.Max()) };
    scenes["torusFarthest"] = new { wire = TorusWire(3, 1, 64, 32, 8, 8), max = mer.Select(l => Line(l, 3)).ToArray() };
    // 3. the neighbour fill and the farthest-point fill side by side (family A, classic mode, Spacing = diagonal / 25)
    var rows = new List<object>();
    foreach (var name in new[] { "saddle", "catenoid", "hyperboloid" })
    {
        var mesh = AnalyticShapes.Build(name, 0, 0, 0, 48);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = mesh.BoundingBoxDiagonal() / 25.0;
        object Row(NetLayout lay)
        {
            var o = new EvenlySpacedNet.Options { Spacing = s, Layout = lay, Continuous = false };
            var c = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, o, field.Family2);
            var m = PlacementMetrics.Measure(mesh, field.Exists, c, s);
            return new { curves = m.Curves, length = R3(m.TotalLength), freeEnds = m.FreeEnds, ends = m.Ends, p5 = R3(m.GapP5), median = R3(m.GapMedian), p95 = R3(m.GapP95), near = R3(100 * m.GapNear), interiorVoid = R3(m.LargestInteriorVoid), rejected = o.RejectedSeeds };
        }
        rows.Add(new { name, fill = Row(NetLayout.Fill), far = Row(NetLayout.FillFarthest) });
    }
    // scene: the saddle of the card, both families by farthest-point seeding
    var saddle = TestMeshes.CreateSaddle(40, 2.0);
    var spc = PrincipalCurvature.Compute(saddle);
    var sf = AsymptoticCurves.ComputeDirections(spc);
    var fa = EvenlySpacedNet.TraceField(saddle, sf.Family1, sf.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Layout = NetLayout.FillFarthest }, sf.Family2);
    var fb = EvenlySpacedNet.TraceField(saddle, sf.Family2, sf.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.15, Layout = NetLayout.FillFarthest }, sf.Family1);
    scenes["asymCat_farthest"] = new { label = "saddle, farthest-point fill (Layout 3)", wire = SaddleWire(40, 2.0, 5), a = fa.Select(l => Line(l, 3)).ToArray(), b = fb.Select(l => Line(l, 3)).ToArray() };
    t.Stop();
    numbers["farthest"] = new { planeCurves = ys.Count, planeExpected = expect.Count, planeErr = R3(planeErr), rows, sceneA = fa.Count, sceneB = fb.Count, ms = t.ElapsedMilliseconds };
}

// ---------- Web Propagation: AAG webs grown from a boundary strip (Wang, Almaskin & Pottmann 2025) ----------
{
    // the exact discrete catenoid web: N meridians, rings ρ_t = R cosh(a t), z_t = a R t, cosh a = 1 / cos(2π/N)
    Vec3d[] CatRow(int m, int n, int N, int t0)
    {
        double delta = 2 * Math.PI / N, a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        var row = new Vec3d[n];
        for (int j = 0; j < n; j++) { int t = m + 2 * j - t0; row[j] = new Vec3d(Math.Cosh(a * t) * Math.Cos(m * delta), Math.Cosh(a * t) * Math.Sin(m * delta), a * t); }
        return row;
    }
    Vec3d[] HypRow(int m, int n, int N, int t0, double beta)
    {
        double delta = 2 * Math.PI / N;
        var row = new Vec3d[n];
        for (int j = 0; j < n; j++) { int k = m + 2 * j - t0; double rho = 1 / Math.Cos(k * delta); row[j] = new Vec3d(rho * Math.Cos(m * delta), rho * Math.Sin(m * delta), Math.Tan(k * delta) / Math.Tan(beta)); }
        return row;
    }
    int nN = 48, nn = 9, rowsAdd = 12;
    var t = Stopwatch.StartNew();
    var rep = WebPropagation.Propagate(CatRow(0, nn, nN, 8), CatRow(1, nn, nN, 8), new WebPropagation.Options { Rows = rowsAdd });
    t.Stop();
    var t2 = Stopwatch.StartNew();
    var equi = WebPropagation.Propagate(CatRow(0, nn, nN, 8), CatRow(1, nn, nN, 8), new WebPropagation.Options { Rows = rowsAdd, Offset = 1.0 });
    t2.Stop();
    // the naive offset: every row translated by the last step
    var naive = new List<Vec3d[]> { CatRow(0, nn, nN, 8), CatRow(1, nn, nN, 8) };
    for (int k = 0; k < rowsAdd; k++) { var pr = naive[^2]; var la = naive[^1]; naive.Add(la.Select((v, j) => v + (la[j] - pr[j])).ToArray()); }
    double naiveStar = 0, naiveGeo = 0;
    for (int k = 2; k < naive.Count; k++) { naiveStar = Math.Max(naiveStar, WebPropagation.StarErrorDegrees(naive[k - 2], naive[k - 1], naive[k])); naiveGeo = Math.Max(naiveGeo, WebPropagation.GeodesicErrorDegrees(naive[k - 1], naive[k])); }
    // exact reproduction with the exact rows as targets: catenoid (24 meridians) and hyperboloid rulings (60)
    var exactCat = Enumerable.Range(0, 10).Select(m => CatRow(m, 7, 24, 8)).ToList();
    var repCat = WebPropagation.Propagate(exactCat[0], exactCat[1], new WebPropagation.Options { Rows = 8, Targets = exactCat.Skip(2).ToList(), TargetWeight = 1, Fairness = 0 });
    double catErr = 0; for (int k = 2; k < exactCat.Count; k++) catErr = Math.Max(catErr, repCat.Rows[k].Zip(exactCat[k], (p, q) => (p - q).Length).Max());
    var exactHyp = Enumerable.Range(0, 12).Select(m => HypRow(m, 6, 60, 10, 0.6)).ToList();
    var repHyp = WebPropagation.Propagate(exactHyp[0], exactHyp[1], new WebPropagation.Options { Rows = 10, Targets = exactHyp.Skip(2).ToList(), TargetWeight = 1, Fairness = 0 });
    double hypErr = 0; for (int k = 2; k < exactHyp.Count; k++) hypErr = Math.Max(hypErr, repHyp.Rows[k].Zip(exactHyp[k], (p, q) => (p - q).Length).Max());
    double hypBow = 0;
    foreach (var lath in repHyp.Web.A.Concat(repHyp.Web.B))
        for (int i = 1; i + 1 < lath.Length; i++) { Vec3d a1 = lath[i] - lath[i - 1], b1 = lath[i + 1] - lath[i]; hypBow = Math.Max(hypBow, Vec3d.Cross(a1, b1).Length / (a1.Length * b1.Length)); }
    // the global AAG step on the propagated web finds nothing to fix
    var aag = AagWeb.Optimize(rep.Web, new AagWeb.Options { Iterations = 8, Fairness = 0 });
    double moved = aag.Nodes.Zip(rep.Web.Nodes, (p, q) => (p - q).Length).Max();
    double spacingDrift = 0;
    foreach (var lath in rep.Web.A.Concat(rep.Web.B)) { double first = (lath[1] - lath[0]).Length; for (int i = 1; i + 1 < lath.Length; i++) spacingDrift = Math.Max(spacingDrift, Math.Abs((lath[i + 1] - lath[i]).Length / first - 1)); }
    object Scene(WebPropagation.Result r, string label) => new
    {
        label,
        strip = r.Rows.Take(2).Select(l => Line(l)).ToArray(),
        rows = r.Rows.Skip(2).Select(l => Line(l)).ToArray(),
        a = r.Web.A.Select(l => Line(l)).ToArray(),
        b = r.Web.B.Select(l => Line(l)).ToArray(),
        nodes = r.Web.Nodes.Select(P).ToArray(),
    };
    scenes["webProp"] = Scene(rep, "catenoid strip · Offset 0 (repeat the strip's triangles)");
    scenes["webPropEqui"] = Scene(equi, "catenoid strip · Offset 1 (equilateral)");
    scenes["webPropNaive"] = new { label = "naive offset (no AAG)", strip = naive.Take(2).Select(l => Line(l)).ToArray(), rows = naive.Skip(2).Select(l => Line(l)).ToArray(), a = WebPropagation.BuildWeb(naive).A.Select(l => Line(l)).ToArray(), b = WebPropagation.BuildWeb(naive).B.Select(l => Line(l)).ToArray(), nodes = naive.SelectMany(l => l).Select(P).ToArray() };
    scenes["webPropHyp"] = Scene(repHyp, "hyperboloid rulings reproduced from two meridians (exact targets)");
    numbers["webProp"] = new
    {
        rows = rep.Rows.Count, rowsBuilt = rep.RowsBuilt, nodes = rep.Web.Nodes.Count, quads = rep.Web.Quads.Count, lathsU = rep.Web.A.Count, lathsW = rep.Web.B.Count,
        star = rep.MaxStarError, geodesic = rep.MaxGeodesicError, ms = t.ElapsedMilliseconds, iterations = rep.Iterations.ToArray(), spacingDrift = R3(spacingDrift),
        equiStar = equi.MaxStarError, equiGeodesic = equi.MaxGeodesicError, equiRows = equi.RowsBuilt, equiMs = t2.ElapsedMilliseconds,
        naiveStar = R3(naiveStar), naiveGeodesic = R3(naiveGeo),
        catenoidExact = catErr, hyperboloidExact = hypErr, hyperboloidBow = hypBow,
        aagMoved = moved, aagStar = aag.StarError, aagGeodesic = aag.GeodesicError,
    };
}

// ---------- Colour maps and legends (one colour language: Grasshopper, web app, bench) ----------
{
    var torus = TestMeshes.CreateTorus(3.0, 1.0, 64, 32);
    var K = GaussianCurvature.Compute(torus);
    var H = MeanCurvature.Compute(torus).Values;
    var faces = new List<int[]>();
    for (int i = 0; i < 64; i++) for (int j = 0; j < 32; j++) { int a = i * 32 + j, b = ((i + 1) % 64) * 32 + j, c = ((i + 1) % 64) * 32 + (j + 1) % 32, d = i * 32 + (j + 1) % 32; faces.Add(new[] { a, b, c, d }); }
    // face colours as the bench draws them: the mean of the face's (rounded) vertex values on the automatic symmetric domain
    object FaceColours(double[] vals)
    {
        var v = vals.Select(R3).ToArray();
        var m = ColourMap.Map(v, ColourMap.Palette.Diverging);
        var cols = faces.Select(f => ColourMap.Diverging((f.Average(vi => v[vi]) - m.Lo) / (m.Hi - m.Lo)).Hex).ToArray();
        var lg = Legend.Build(m.Lo, m.Hi, ColourMap.Palette.Diverging, 32);
        return new { lo = m.Lo, hi = m.Hi, colours = cols, ticks = lg.Ticks, labels = lg.Labels, loLabel = lg.LoLabel, hiLabel = lg.HiLabel, step = lg.Step };
    }
    double Worst(ColourMap.Rgb[] t, int from, int to)
    {
        double mean = Math.Abs(ColourMap.Lightness(t[to]) - ColourMap.Lightness(t[from])) / (to - from), worst = 0;
        for (int i = from; i + 8 <= to; i += 4) worst = Math.Max(worst, Math.Abs(Math.Abs(ColourMap.Lightness(t[i + 8]) - ColourMap.Lightness(t[i])) / 8 / mean - 1));
        return R3(100 * worst);
    }
    var div = ColourMap.DivergingTable; var seq = ColourMap.SequentialTable;
    var old = new ColourMap.Rgb[257];
    double[][] st = { new[] { 0.10, 0.12, 0.85 }, new[] { 0.45, 0.52, 1.00 }, new[] { 0.97, 0.97, 0.97 }, new[] { 1.00, 0.45, 0.55 }, new[] { 0.85, 0.05, 0.20 } };
    for (int i = 0; i < 257; i++) { double x = i / 64.0; int j = Math.Min(3, (int)Math.Floor(x)); double f = x - j; old[i] = new ColourMap.Rgb((int)Math.Round(255 * (st[j][0] + (st[j + 1][0] - st[j][0]) * f)), (int)Math.Round(255 * (st[j][1] + (st[j + 1][1] - st[j][1]) * f)), (int)Math.Round(255 * (st[j][2] + (st[j + 1][2] - st[j][2]) * f))); }
    double minChroma = 1e9; for (int i = 24; i < 232; i++) minChroma = Math.Min(minChroma, ColourMap.Chroma(seq[i]));
    var legends = new[] { (105.0, 543.0), (2.03, 2.17), (-0.37, 0.37), (0.0, 1.3), (-0.0042, 0.0042), (0.0, 1.0), (-1.0, 0.25) }
        .Select(d => { var lg = Legend.Build(d.Item1, d.Item2, ColourMap.Palette.Diverging, 8); return new { lo = d.Item1, hi = d.Item2, step = lg.Step, ticks = lg.Ticks, labels = lg.Labels, loLabel = lg.LoLabel, hiLabel = lg.HiLabel }; }).ToArray();
    numbers["colourMap"] = new
    {
        divEnds = new[] { div[0].Hex, div[128].Hex, div[256].Hex },
        divL = new[] { R3(ColourMap.Lightness(div[0])), R3(ColourMap.Lightness(div[128])), R3(ColourMap.Lightness(div[256])) },
        divCentreChroma = R3(ColourMap.Chroma(div[128])), centreLightness = ColourMap.CentreLightness,
        divStepBlue = R3(Math.Abs(ColourMap.Lightness(div[128]) - ColourMap.Lightness(div[0])) / 128), divStepRed = R3(Math.Abs(ColourMap.Lightness(div[256]) - ColourMap.Lightness(div[128])) / 128),
        divWorstBlue = Worst(div, 0, 128), divWorstRed = Worst(div, 128, 256), oldWorstBlue = Worst(old, 0, 128), oldWorstRed = Worst(old, 128, 256),
        seqEnds = new[] { seq[0].Hex, seq[256].Hex }, seqL = new[] { R3(ColourMap.Lightness(seq[0])), R3(ColourMap.Lightness(seq[256])) },
        seqStep = R3((ColourMap.Lightness(seq[256]) - ColourMap.Lightness(seq[0])) / 256), seqWorst = Worst(seq, 0, 256), seqMinChroma = R3(minChroma),
        seqMonotone = Enumerable.Range(1, 256).All(i => ColourMap.Lightness(seq[i]) >= ColourMap.Lightness(seq[i - 1]) - 0.05),
        moreland = new[] { ColourMap.Moreland(new ColourMap.Rgb(59, 76, 192), new ColourMap.Rgb(180, 4, 38), 0.25).Hex, ColourMap.Moreland(new ColourMap.Rgb(59, 76, 192), new ColourMap.Rgb(180, 4, 38), 0.5).Hex, ColourMap.Moreland(new ColourMap.Rgb(59, 76, 192), new ColourMap.Rgb(180, 4, 38), 0.75).Hex },
        util = new[] { 0, 0.35, 0.7, 0.999, 1.0, 1.15, 1.3, 2.0 }.Select(u => ColourMap.Utilisation(u).Hex).ToArray(),
        legends,
        torusK = FaceColours(K), torusH = FaceColours(H),
        tableSize = ColourMap.TableSize, utilSize = ColourMap.UtilisationSize,
    };
    // the JavaScript twin the app and the bench load
    string js = ColourMap.JavaScriptModule();
    File.WriteAllText(Path.Combine(OutDir, "colormap-lut.js"), js);
    string appJs = Path.GetFullPath(Path.Combine(OutDir, "..", "..", "src", "Mite.Web", "wwwroot", "js"));
    if (Directory.Exists(appJs)) File.WriteAllText(Path.Combine(appJs, "colormap-lut.js"), js);
}


// ---------- Asymptotic Web: tri-hex (midpoint) AAG webs and the constant crossing angle ----------
{
    AsymptoticWeb.Result WebOf(MeshData mesh, double sp, int seed = -1) => AsymptoticWeb.Build(mesh, PrincipalCurvature.Compute(mesh, 2), seed, new AsymptoticWeb.Options { Spacing = sp });
    object Scene(string label, MeshData mesh, int wireEvery, AagWeb.Result r, object? view = null) => new
    {
        label, view, wire = Edges(mesh, wireEvery),
        a = r.A.Select(l => Line(l)).ToArray(), b = r.B.Select(l => Line(l)).ToArray(), g = r.G.Select(l => Line(l)).ToArray(),
        pts = (r.Midpoint ? r.Midpoints : r.Nodes).Select(P).ToArray(),
    };
    double PlaneErr(IEnumerable<Vec3d[]> chains)
    {
        double worst = 0;
        foreach (var d in chains)
        {
            double sx = d.Sum(p => Math.Cos(Math.Atan2(p.Y, p.X))), sy = d.Sum(p => Math.Sin(Math.Atan2(p.Y, p.X)));
            double phi = Math.Atan2(sy, sx); var n = new Vec3d(-Math.Sin(phi), Math.Cos(phi), 0);
            foreach (var p in d) worst = Math.Max(worst, Math.Abs(Vec3d.Dot(p, n)));
        }
        return worst;
    }
    double? Nz(double v) => double.IsNaN(v) ? null : v;
    object Row(AagWeb.Result r, double spacing, double plane = double.NaN) => new
    {
        spacing, geo0 = R3(r.InitialGeodesicError), geo = Math.Round(r.GeodesicError, 5), star0 = R3(r.InitialStarError), star = Math.Round(r.StarError, 5),
        asym0 = Nz(R3(r.InitialAsymptoticError)), asym = Nz(Math.Round(r.AsymptoticError, 5)), dev = r.MeanDeviation, devMax = r.MaxDeviation, nodeMove = r.MaxNodeMove,
        shift = R3(r.MeanMidpointShift), shiftMax = R3(r.MaxMidpointShift), plane = Nz(plane),
        angle0 = R3(r.InitialAngleMean), angleMin0 = R3(r.InitialAngleMin), angleMax0 = R3(r.InitialAngleMax), angle = R3(r.AngleMean), angleMin = R3(r.AngleMin), angleMax = R3(r.AngleMax),
        iterations = r.Iterations, midpoints = r.Midpoints.Length, chainsG = r.G.Count,
    };
    var t = Stopwatch.StartNew();
    // catenoid: the mid-chain topology and the tri-hex optimisation
    var cat = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 48).ToTriangulated();
    var wc = WebOf(cat, 0.3);
    double midErr = 0; int seg0 = 0, seg1 = 0, chains0 = 0;
    for (int i = 0; i < wc.MidChains.Count; i++)
    {
        for (int k = 0; k < wc.MidChains[i].Length; k++) { var (p, q) = wc.MidChainEdges[i][k]; midErr = Math.Max(midErr, (wc.MidChains[i][k] - 0.5 * (wc.Nodes[p] + wc.Nodes[q])).Length); }
        if (wc.MidChainFamily[i] == 0) { seg0 += wc.MidChains[i].Length - 1; chains0++; } else seg1 += wc.MidChains[i].Length - 1;
    }
    var rcTri = AagWeb.Optimize(cat, wc, new AagWeb.Options { Midpoint = true, Iterations = 24 });
    var rcNode = AagWeb.Optimize(cat, wc);
    var rc60 = AagWeb.Optimize(cat, wc, new AagWeb.Options { Midpoint = true, CrossingAngle = 60, Iterations = 40 });
    scenes["asymWebTri_catenoid"] = Scene("catenoid, tri-hex AAG web (spacing 0.3)", cat, 4, rcTri);
    scenes["asymWebNode_catenoid"] = Scene("catenoid, node-to-node AAG web (the meridians)", cat, 4, rcNode);
    // the rotational CRPC surface k1/k2 = −1/3: 60° crossings as traced
    var crpc = AnalyticShapes.Build("crpc", 1.0 / 3.0, 1.2, 0, 48).ToTriangulated();
    var pcC = PrincipalCurvature.Compute(crpc, 2);
    double ratioErr = 0;
    for (int i = 0; i < crpc.VertexCount; i++)
    {
        if (Math.Abs(crpc.Vertices[i].Z) > 0.9) continue;
        double q = Math.Abs(pcC.K1[i] / pcC.K2[i]); q = Math.Min(q, 1 / q);
        ratioErr = Math.Max(ratioErr, Math.Abs(q - 1.0 / 3.0));
    }
    var wCrpc = WebOf(crpc, 0.3);
    var rCrpc = AagWeb.Optimize(crpc, wCrpc, new AagWeb.Options { Midpoint = true, CrossingAngle = 60 });
    scenes["asymWebTri_crpc"] = Scene("rotational CRPC surface k₁/k₂ = −1/3: asymptotic laths cross at 60° (tri-hex AAG, Angle 60)", crpc, 4, rCrpc);
    // Enneper surfaces
    var en3 = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
    var w3 = WebOf(en3, 0);
    var r3 = AagWeb.Optimize(en3, w3, new AagWeb.Options { Midpoint = true });
    scenes["asymWebTri_enneper3"] = Scene("3-fold Enneper, tri-hex AAG web (singular seed, six rays)", en3, 3, r3, new { yaw = -0.5, pitch = 1.0 });
    var en = AnalyticShapes.Build("enneper", 1.2, 0, 0, 72).ToTriangulated();
    var wEn = WebOf(en, 0.25, new MeshProjection(en).NearestVertexGlobal(Vec3d.Zero));
    var rEn = AagWeb.Optimize(en, wEn, new AagWeb.Options { Midpoint = true, Iterations = 48 });
    var rEnNode = AagWeb.Optimize(en, wEn, new AagWeb.Options { Iterations = 48 });
    scenes["asymWebTri_enneper"] = Scene("classical Enneper, tri-hex AAG web (the node web does not converge here)", en, 4, rEn, new { yaw = -0.5, pitch = 1.0 });
    t.Stop();
    numbers["triHex"] = new
    {
        catQuads = wc.Quads.Count, catLaths = wc.A.Count, catMidErr = midErr, catSeg0 = seg0, catSeg1 = seg1, catChains0 = chains0, catChordKg = R3(wc.MidGeodesicError[0]),
        cat = Row(rcTri, wc.Spacing, PlaneErr(rcTri.G)), catNode = Row(rcNode, wc.Spacing, PlaneErr(rcNode.G)), cat60 = Row(rc60, wc.Spacing),
        crpc = new { ratioErr = R3(ratioErr), spacing = wCrpc.Spacing, geo0 = R3(rCrpc.InitialGeodesicError), geo = Math.Round(rCrpc.GeodesicError, 5), star = Math.Round(rCrpc.StarError, 5), asym = Math.Round(rCrpc.AsymptoticError, 5), angle0 = R3(rCrpc.InitialAngleMean), angleMin0 = R3(rCrpc.InitialAngleMin), angleMax0 = R3(rCrpc.InitialAngleMax), angle = R3(rCrpc.AngleMean), angleMin = R3(rCrpc.AngleMin), angleMax = R3(rCrpc.AngleMax), nodeMove = rCrpc.MaxNodeMove },
        enneper3 = Row(r3, w3.Spacing), enneper = Row(rEn, wEn.Spacing), enneperNode = Row(rEnNode, wEn.Spacing),
        ms = t.ElapsedMilliseconds,
    };
}

numbers["totalMs"] = sw.ElapsedMilliseconds;
var opt = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true };
File.WriteAllText(Path.Combine(OutDir, "scenes.json"), JsonSerializer.Serialize(scenes, opt));
File.WriteAllText(Path.Combine(OutDir, "numbers.json"), JsonSerializer.Serialize(numbers, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Console.WriteLine(File.ReadAllText(Path.Combine(OutDir, "numbers.json")));
Console.WriteLine("scenes bytes: " + new FileInfo(Path.Combine(OutDir, "scenes.json")).Length);
