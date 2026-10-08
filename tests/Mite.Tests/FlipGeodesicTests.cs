using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Exact geodesics by edge flips (FlipOut, Sharp &amp; Crane 2020) against closed-form shortest
/// paths: the polyhedral cone (intrinsically flat except at the apex, exact development), the
/// prism cylinder, an irregular flat triangulation and a single saddle vertex (Mitchell, Mount &amp;
/// Papadimitriou 1987: a geodesic passes a vertex only with ≥ π on both sides). Also the cone as a
/// test typology for the curvature estimators (K = 0 except the apex, k = cos α / r).
/// </summary>
public class FlipGeodesicTests
{
    private readonly ITestOutputHelper _out;
    public FlipGeodesicTests(ITestOutputHelper output) { _out = output; }

    private const double Alpha = 0.6;
    private const int N = 48;
    private const double Slant = 2.0;

    private static (Vec3d p, (double rho, double psi) u) RandomConePoint(Random rnd)
    {
        int i = rnd.Next(N);
        double a = rnd.NextDouble(), b = rnd.NextDouble();
        double s = (0.15 + 0.75 * rnd.NextDouble()) * Slant / Math.Max(a + b, 1e-9);
        a *= s; b *= s;
        return (TestMeshes.ConePoint(Alpha, N, i, a, b), TestMeshes.ConeUnfold(Alpha, N, i, a, b));
    }

    /// <summary>Largest distance from the points to the mesh (exact closest point over all faces).</summary>
    private static double OffMesh(MeshData mesh, IEnumerable<Vec3d> pts)
    {
        var proj = new MeshProjection(mesh);
        double worst = 0;
        foreach (var p in pts)
        {
            var h = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
            worst = Math.Max(worst, (h.Point - p).Length);
        }
        return worst;
    }

    [Fact]
    public void Cone_FlipOutMatchesTheExactDevelopment()
    {
        // irregular triangles (vertices jittered along the generators keep every quad planar)
        var cone = TestMeshes.CreatePolyCone(Alpha, Slant, N, 16, 0.6, 7);
        var rnd = new Random(11);
        var from = new List<Vec3d>(); var to = new List<Vec3d>(); var exact = new List<double>();
        for (int k = 0; k < 40; k++)
        {
            var p = RandomConePoint(rnd); var q = RandomConePoint(rnd);
            from.Add(p.p); to.Add(q.p); exact.Add(TestMeshes.ConeDistance(Alpha, N, p.u, q.u));
        }
        var res = FlipGeodesic.Compute(cone, from, to, out var reason);
        Assert.True(res != null, reason);
        var proj = new MeshProjection(cone);
        double worst = 0, worstOld = 0, worstPoly = 0, worstMiss = 0;
        for (int k = 0; k < from.Count; k++)
        {
            var r = res![k];
            Assert.NotNull(r);
            Assert.True(r!.Converged, $"pair {k}: smallest joint angle {r.MinJointAngle}");
            worst = Math.Max(worst, Math.Abs(r.Length - exact[k]));
            worstPoly = Math.Max(worstPoly, Math.Abs(FlipGeodesic.PolylineLength(r.Points) - r.Length));
            worstMiss = Math.Max(worstMiss, r.TraceMiss);
            Assert.True((r.Points[0] - from[k]).Length < 1e-12 && (r.Points[^1] - to[k]).Length < 1e-12);
            Assert.Empty(r.Vertices); // never through the apex (MMP Lemma 3.4), nor any flat vertex here
            var old = ShortestPath.Compute(proj, from[k], to[k])!.Value;
            worstOld = Math.Max(worstOld, Math.Abs(old.Length - exact[k]) / exact[k]);
        }
        double off = OffMesh(cone, res!.SelectMany(r => r!.Points));
        _out.WriteLine($"cone: max |L − exact| {worst:E2}, polyline vs intrinsic {worstPoly:E2}, trace miss {worstMiss:E2}, off mesh {off:E2}; curve shortening max rel. error {worstOld:E2}");
        Assert.True(worst < 1e-10, $"max length error {worst:E2}");
        Assert.True(worstPoly < 1e-10, $"traced polyline length differs by {worstPoly:E2}");
        Assert.True(worstMiss < 1e-9, $"trace miss {worstMiss:E2}");
        Assert.True(off < 1e-12, $"polyline leaves the mesh by {off:E2}");
    }

    [Fact]
    public void Cone_PathAroundTheApexNeverThroughIt()
    {
        // two points at slant 0.25 whose developed angles are 0.45 Ω apart: the graph path runs over
        // the apex (or next to it); the geodesic must go round on the shorter side
        var cone = TestMeshes.CreatePolyCone(Alpha, Slant, N, 16, 0.0, 1);
        var (th, om) = TestMeshes.ConeAngles(Alpha, N);
        double s = 0.25;
        int i0 = 3, i1 = i0 + (int)Math.Round(0.45 * N);
        var p = TestMeshes.ConePoint(Alpha, N, i0, 0.5 * s, 0.5 * s);
        var q = TestMeshes.ConePoint(Alpha, N, i1, 0.5 * s, 0.5 * s);
        double exact = TestMeshes.ConeDistance(Alpha, N, TestMeshes.ConeUnfold(Alpha, N, i0, 0.5 * s, 0.5 * s), TestMeshes.ConeUnfold(Alpha, N, i1, 0.5 * s, 0.5 * s));
        var r = FlipGeodesic.Compute(cone, new[] { p }, new[] { q }, out _)![0]!;
        double apexGap = double.PositiveInfinity;
        for (int k = 1; k < r.Points.Length; k++)
        {
            var e = r.Points[k] - r.Points[k - 1];
            double t = Math.Max(0, Math.Min(1, -Vec3d.Dot(r.Points[k - 1], e) / e.LengthSquared));
            apexGap = Math.Min(apexGap, (r.Points[k - 1] + t * e).Length);
        }
        double throughApex = p.Length + q.Length;
        // closed form of the gap: the developed chord's distance to the origin
        double dPsi = (i1 - i0) * th;
        double rho = TestMeshes.ConeUnfold(Alpha, N, i0, 0.5 * s, 0.5 * s).rho;
        double gapExact = rho * Math.Cos(Math.Min(dPsi, om - dPsi) / 2);
        _out.WriteLine($"apex: L {r.Length:F12} exact {exact:F12} (through apex {throughApex:F6}), closest approach {apexGap:F12} (exact {gapExact:F12}), start {r.StartLength:F6}, steps {r.Steps}");
        Assert.Equal(exact, r.Length, 10);
        Assert.True(r.Length < throughApex - 1e-3);
        Assert.Equal(gapExact, apexGap, 9);
        Assert.Empty(r.Vertices);
    }

    [Fact]
    public void PrismCylinder_FlipOutIsTheUnrolledStraightLine()
    {
        int seg = 64; double R = 1.0, H = 2.0;
        var cyl = TestMeshes.CreateCylinder(R, H, seg, 32);
        double w = 2 * R * Math.Sin(Math.PI / seg);
        Vec3d At(double u, double z) // u in segments around, z height
        {
            int i = (int)Math.Floor(u); double t = u - i;
            Vec3d Pt(int k) { double a = 2 * Math.PI * (((k % seg) + seg) % seg) / seg; return new Vec3d(R * Math.Cos(a), R * Math.Sin(a), z); }
            return (1 - t) * Pt(i) + t * Pt(i + 1);
        }
        var cases = new[] { (0.3, -0.85, 20.7, 0.9), (5.5, 0.2, 40.25, -0.6), (10.0, -0.5, 10.0, 0.5), (60.1, 0.0, 3.4, 0.31) };
        var res = FlipGeodesic.Compute(cyl, cases.Select(c => At(c.Item1, c.Item2)).ToList(), cases.Select(c => At(c.Item3, c.Item4)).ToList(), out _)!;
        for (int k = 0; k < cases.Length; k++)
        {
            var c = cases[k];
            double du = Math.Abs(c.Item3 - c.Item1) % seg; du = Math.Min(du, seg - du);
            double exact = Math.Sqrt(du * w * du * w + (c.Item4 - c.Item2) * (c.Item4 - c.Item2));
            _out.WriteLine($"cylinder {k}: L {res[k]!.Length:F12} exact {exact:F12}");
            Assert.Equal(exact, res[k]!.Length, 10);
            Assert.Equal(exact, FlipGeodesic.PolylineLength(res[k]!.Points), 10);
        }
    }

    [Fact]
    public void IrregularPlane_FlipOutIsTheStraightSegment()
    {
        var plane = TestMeshes.CreateIrregularPlane(16, 2.0, 0.35, 3);
        var rnd = new Random(5);
        var from = new List<Vec3d>(); var to = new List<Vec3d>();
        for (int k = 0; k < 30; k++)
        {
            from.Add(new Vec3d(0.05 + 1.9 * rnd.NextDouble(), 0.05 + 1.9 * rnd.NextDouble(), 0));
            to.Add(new Vec3d(0.05 + 1.9 * rnd.NextDouble(), 0.05 + 1.9 * rnd.NextDouble(), 0));
        }
        // plus mesh vertices and points on edges as endpoints (snap and edge-split insertion)
        from.Add(plane.Vertices[17 * 3 + 2]); to.Add(plane.Vertices[17 * 14 + 15]);
        var e0 = plane.Vertices[17 * 5 + 5]; var e1 = plane.Vertices[17 * 5 + 6];
        from.Add(0.5 * (e0 + e1)); to.Add(new Vec3d(1.9, 1.7, 0));
        var res = FlipGeodesic.Compute(plane, from, to, out _)!;
        double worstL = 0, worstLateral = 0;
        for (int k = 0; k < from.Count; k++)
        {
            var r = res[k]!;
            var a = from[k]; var d = (to[k] - a); double L = d.Length; var u = d / L;
            worstL = Math.Max(worstL, Math.Abs(r.Length - L));
            foreach (var x in r.Points) worstLateral = Math.Max(worstLateral, (x - a - Vec3d.Dot(x - a, u) * u).Length);
        }
        _out.WriteLine($"plane: max |L − |pq|| {worstL:E2}, max distance from the segment {worstLateral:E2}");
        Assert.True(worstL < 1e-11);
        Assert.True(worstLateral < 1e-10);
    }

    [Fact]
    public void SaddleVertex_GeodesicPassesThroughIt()
    {
        var fan = TestMeshes.CreateSaddleFan(4, 0.6);
        var it = IntrinsicTriangulation.Build(fan.Vertices, fan.Faces, out _)!;
        Assert.True(it.AngleSum(0) > 2 * Math.PI + 0.1, $"centre angle {it.AngleSum(0)}");
        // points inside the opposite central triangles 0 and 4, close to the centre
        Vec3d In(int sector, double r)
        {
            var a = fan.Vertices[1 + sector]; var b = fan.Vertices[1 + (sector + 1) % 8];
            return r * (0.5 * a + 0.5 * b);
        }
        var p = In(0, 0.3); var q = In(4, 0.2);
        var r = FlipGeodesic.Compute(fan, new[] { p }, new[] { q }, out _)![0]!;
        double exact = p.Length + q.Length;
        _out.WriteLine($"saddle: centre angle {it.AngleSum(0):F6}, L {r.Length:F12} exact {exact:F12}, min joint angle {r.MinJointAngle:F6}, through {r.Vertices.Length}");
        Assert.Equal(exact, r.Length, 12);
        Assert.Single(r.Vertices);
        Assert.True(r.Vertices[0].Length < 1e-15);
        Assert.True(r.MinJointAngle >= Math.PI - 1e-9);
    }

    [Fact]
    public void Sphere_FlipOutIsTheMeshGeodesicCloseToTheGreatCircle()
    {
        // the geodesic of the faceted sphere is not the great circle, but close: it must lie on
        // the facets, be no longer than the projected great circle, and stay within 1 % of the arc
        var sphere = TestMeshes.CreateUnitSphere(32);
        var a = new Vec3d(1, 0, 0);
        var b = new Vec3d(-0.5, 0.5, 0.7071).Normalized();
        var r = FlipGeodesic.Compute(sphere, new[] { a }, new[] { b }, out _)![0]!;
        var proj = new MeshProjection(sphere);
        // the great circle pulled onto the facets, finely sampled
        var gc = new List<Vec3d>();
        double ang = Math.Acos(Vec3d.Dot(a, b));
        var axis = Vec3d.Cross(a, b).Normalized(); var t = Vec3d.Cross(axis, a);
        for (int k = 0; k <= 4000; k++) { double s = ang * k / 4000; var x = Math.Cos(s) * a + Math.Sin(s) * t; gc.Add(proj.ClosestPoint(x, proj.NearestVertexGlobal(x)).Point); }
        double gcLen = FlipGeodesic.PolylineLength(gc);
        var old = ShortestPath.Compute(proj, a, b)!.Value;
        _out.WriteLine($"sphere: FlipOut {r.Length:F6}, projected great circle {gcLen:F6}, arc {ang:F6}, curve shortening {old.Length:F6}, off mesh {OffMesh(sphere, r.Points):E1}");
        Assert.True(r.Converged);
        Assert.True(r.Length <= gcLen + 1e-9);
        Assert.InRange(r.Length, ang * 0.99, ang * 1.01);
        Assert.True(OffMesh(sphere, r.Points) < 1e-12);
    }

    [Fact]
    public void QuadMeshes_DisconnectedParts_AndNonManifoldInput()
    {
        // quads are triangulated; a straight path on a flat quad grid
        var grid = TestMeshes.CreateQuadGrid(6, 6, 1.0);
        var r = FlipGeodesic.Compute(grid, new[] { new Vec3d(0.2, 0.3, 0) }, new[] { new Vec3d(5.7, 4.1, 0) }, out _)![0]!;
        Assert.Equal(Math.Sqrt(5.5 * 5.5 + 3.8 * 3.8), r.Length, 12);

        // two separate triangles: no path, per pair
        var two = new MeshData(new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(5, 0, 0), new Vec3d(6, 0, 0), new Vec3d(5, 1, 0) },
            new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 } });
        var rr = FlipGeodesic.Compute(two, new[] { new Vec3d(0.2, 0.2, 0), new Vec3d(0.1, 0.1, 0) }, new[] { new Vec3d(5.2, 0.2, 0), new Vec3d(0.3, 0.4, 0) }, out _)!;
        Assert.Null(rr[0]);
        Assert.NotNull(rr[1]);

        // three triangles on one edge: not a manifold, reported instead of a wrong answer
        var nm = new MeshData(new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(0.5, 1, 0), new Vec3d(0.5, -1, 0), new Vec3d(0.5, 0, 1) },
            new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 }, new[] { 1, 0, 4 } });
        Assert.Null(FlipGeodesic.Compute(nm, new[] { new Vec3d(0.5, 0.5, 0) }, new[] { new Vec3d(0.5, -0.5, 0) }, out var reason));
        Assert.Contains("manifold", reason);

        // a flipped face is re-oriented first (as Mesh Cleanup does), so the path is still exact
        var flippedFaces = grid.ToTriangulated().Faces.Select(f => (int[])f.Clone()).ToArray();
        Array.Reverse(flippedFaces[7]); Array.Reverse(flippedFaces[20]);
        var flipped = new MeshData(grid.Vertices, flippedFaces);
        var rf = FlipGeodesic.Compute(flipped, new[] { new Vec3d(0.2, 0.3, 0) }, new[] { new Vec3d(5.7, 4.1, 0) }, out var why)!;
        Assert.True(rf != null, why);
        Assert.Equal(Math.Sqrt(5.5 * 5.5 + 3.8 * 3.8), rf![0]!.Length, 12);

        // same point twice: a zero-length path
        var z = FlipGeodesic.Compute(grid, new[] { new Vec3d(1.5, 1.5, 0) }, new[] { new Vec3d(1.5, 1.5, 0) }, out _)![0]!;
        Assert.Equal(0.0, z.Length);
    }

    [Fact]
    public void Flip_KeepsLengthsAnglesAndSignposts()
    {
        // a flip changes the triangulation, not the surface: angle sums stay, every edge traces to its head
        var cone = TestMeshes.CreatePolyCone(Alpha, Slant, 16, 6, 0.5, 2);
        var it = IntrinsicTriangulation.Build(cone.Vertices, cone.Faces, out _)!;
        var sums = Enumerable.Range(0, it.VertexCount).Select(it.AngleSum).ToArray();
        var rnd = new Random(1);
        int flips = 0;
        for (int k = 0; k < 2000; k++) if (it.Flip(rnd.Next(it.HalfedgeCount))) flips++;
        double worstSum = 0, worstMiss = 0;
        for (int v = 0; v < it.VertexCount; v++)
        {
            double s = 0;
            foreach (int h in it.FanOf(v)!) if (it.Face(h) >= 0) s += it.Corner(h);
            worstSum = Math.Max(worstSum, Math.Abs(s - sums[v]));
        }
        for (int h = 0; h < it.HalfedgeCount; h++)
        {
            var pts = it.TraceEdge(h, out double miss);
            worstMiss = Math.Max(worstMiss, miss);
            Assert.Equal(it.Length(h), FlipGeodesic.PolylineLength(pts), 9);
        }
        _out.WriteLine($"{flips} random flips: angle sums kept to {worstSum:E1}, every edge traces to its head within {worstMiss:E1}");
        Assert.True(flips > 200);
        Assert.True(worstSum < 1e-10);
        Assert.True(worstMiss < 1e-9);
        // apex angle sum is the exact face-angle total Ω
        Assert.Equal(TestMeshes.ConeAngles(Alpha, 16).total, it.AngleSum(0), 12);
    }

    [Fact]
    public void Cone_CurvatureTypology()
    {
        // K = 0 at every interior vertex; the apex carries the whole deficit 2π − Ω; principal
        // curvature at ring vertices k1 = cos α / r (r = distance from the axis), k2 = 0 along the generator
        var cone = TestMeshes.CreatePolyCone(Alpha, Slant, N, 16, 0.0, 1);
        var K = GaussianCurvature.Compute(cone);
        var area = DiscreteOperators.MixedVoronoiAreas(cone.ToTriangulated());
        var border = cone.BuildBoundaryVertexFlags();
        double deficit = K[0] * area[0];
        double maxFlat = 0;
        for (int v = 1; v < cone.VertexCount; v++) if (!border[v]) maxFlat = Math.Max(maxFlat, Math.Abs(K[v] * area[v]));
        var (_, om) = TestMeshes.ConeAngles(Alpha, N);
        var pc = PrincipalCurvature.Compute(cone);
        double worstK = 0, worstNear = 0, worstDir = 0;
        for (int j = 4; j <= 13; j++)
        {
            double ringWorst = 0;
            for (int i = 0; i < N; i++)
            {
                int v = 1 + (j - 1) * N + i;
                var x = cone.Vertices[v];
                double r = Math.Sqrt(x.X * x.X + x.Y * x.Y);
                double k = Math.Cos(Alpha) / r;
                // the sign follows the face orientation: compare magnitudes, and take the direction
                // of the curvature closer to 0 as the ruling
                bool firstBig = Math.Abs(pc.K1[v]) >= Math.Abs(pc.K2[v]);
                double big = firstBig ? Math.Abs(pc.K1[v]) : Math.Abs(pc.K2[v]);
                double small = firstBig ? Math.Abs(pc.K2[v]) : Math.Abs(pc.K1[v]);
                double err = Math.Max(Math.Abs(big - k), small) / k;
                if (j >= 7) worstK = Math.Max(worstK, err); else worstNear = Math.Max(worstNear, err);
                ringWorst = Math.Max(ringWorst, err);
                var g = TestMeshes.ConeGenerator(Alpha, N, i);
                var dRuling = firstBig ? pc.D2[v] : pc.D1[v];
                worstDir = Math.Max(worstDir, Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(dRuling, g)))) * 180 / Math.PI);
            }
            _out.WriteLine($"ring {j}: max relative error {ringWorst:P2}");
        }
        _out.WriteLine($"cone: apex deficit {deficit:F12} (exact {2 * Math.PI - om:F12}, smooth {2 * Math.PI * (1 - Math.Sin(Alpha)):F6}); max |deficit| elsewhere {maxFlat:E1}; |k| vs cos α / r max rel {worstK:P2} from ring 7 out, {worstNear:P2} on rings 4–6 (the 2-ring reaches the apex); D2 vs generator max {worstDir:F3}°");
        Assert.Equal(2 * Math.PI - om, deficit, 9);
        Assert.True(maxFlat < 1e-9);
        Assert.True(worstK < 0.01, $"k {worstK:P2}");
        Assert.True(worstNear < 0.04, $"k near the apex {worstNear:P2}");
        Assert.True(worstDir < 0.5, $"D2 off the generator by {worstDir:F3}°");
    }

    [Fact]
    public void Cone_ThousandPairs_ShortestUnlessBothWaysRoundAreNearlyEqual()
    {
        // FlipOut returns the geodesic in the class of its Dijkstra start path (Sharp & Crane 2020
        // report > 95 % minimal). Round a cone point the two classes are the two ways round the apex;
        // the graph metric can only confuse them when their lengths are within its distortion
        int exact = 0, total = 0, ambiguousMisses = 0, otherMisses = 0;
        double worstMiss = 0;
        foreach (var jit in new[] { 0.0, 0.6 })
        {
            var cone = TestMeshes.CreatePolyCone(Alpha, Slant, N, 16, jit, 7);
            var rnd = new Random(99);
            var (_, om) = TestMeshes.ConeAngles(Alpha, N);
            var from = new List<Vec3d>(); var to = new List<Vec3d>(); var ex = new List<double>(); var other = new List<double>();
            for (int k = 0; k < 500; k++)
            {
                int i = rnd.Next(N), j = rnd.Next(N);
                double a = rnd.NextDouble(), b = rnd.NextDouble(), c = rnd.NextDouble(), d = rnd.NextDouble();
                double s1 = (0.05 + 0.85 * rnd.NextDouble()) * Slant / (a + b), s2 = (0.05 + 0.85 * rnd.NextDouble()) * Slant / (c + d);
                var u1 = TestMeshes.ConeUnfold(Alpha, N, i, a * s1, b * s1); var u2 = TestMeshes.ConeUnfold(Alpha, N, j, c * s2, d * s2);
                from.Add(TestMeshes.ConePoint(Alpha, N, i, a * s1, b * s1)); to.Add(TestMeshes.ConePoint(Alpha, N, j, c * s2, d * s2));
                ex.Add(TestMeshes.ConeDistance(Alpha, N, u1, u2));
                double D = Math.Abs(u1.psi - u2.psi) % om; double Dl = Math.Max(D, om - D);
                other.Add(Math.Sqrt(Math.Max(0, u1.rho * u1.rho + u2.rho * u2.rho - 2 * u1.rho * u2.rho * Math.Cos(Dl))));
            }
            var res = FlipGeodesic.Compute(cone, from, to, out _)!;
            for (int k = 0; k < 500; k++)
            {
                total++;
                var r = res[k]!;
                Assert.True(r.Converged);
                if (Math.Abs(r.Length - ex[k]) < 1e-9) { exact++; continue; }
                // a miss must be the exact geodesic the other way round, and that way must be close
                Assert.Equal(other[k], r.Length, 9);
                double gap = (other[k] - ex[k]) / ex[k];
                worstMiss = Math.Max(worstMiss, gap);
                if (gap < 0.05) ambiguousMisses++; else otherMisses++;
            }
        }
        _out.WriteLine($"{exact}/{total} shortest; misses: {ambiguousMisses} where the other way round is < 5 % longer (worst {worstMiss:P1}), {otherMisses} others");
        Assert.True(exact >= 0.99 * total);
        Assert.Equal(0, otherMisses);
    }

    [Fact]
    public void ClosestPointGlobal_FindsTheFaceNextToTheApex()
    {
        // the hinted local projection searches the faces round the Euclidean nearest vertex; next to
        // a cone apex that vertex can be several sectors away from the face holding the point
        var cone = TestMeshes.CreatePolyCone(Alpha, Slant, N, 16, 0.6, 7);
        var proj = new MeshProjection(cone);
        var rnd = new Random(4);
        int localMisses = 0; double worstLocal = 0, worstGlobal = 0;
        for (int k = 0; k < 2000; k++)
        {
            int i = rnd.Next(N);
            double a = rnd.NextDouble(), b = rnd.NextDouble();
            double s = (0.02 + 0.3 * rnd.NextDouble()) * Slant / (a + b);
            var p = TestMeshes.ConePoint(Alpha, N, i, a * s, b * s);
            double gl = (proj.ClosestPointGlobal(p).Point - p).Length;
            double lo = (proj.ClosestPoint(p, proj.NearestVertexGlobal(p)).Point - p).Length;
            worstGlobal = Math.Max(worstGlobal, gl); worstLocal = Math.Max(worstLocal, lo);
            if (lo > 1e-9) localMisses++;
        }
        _out.WriteLine($"points within 0.32 slant of the apex: hinted projection misses {localMisses}/2000 (worst {worstLocal:E2}), global worst {worstGlobal:E2}");
        Assert.True(worstGlobal < 1e-12);
        Assert.True(localMisses > 0, "the cone should exercise the failure of the local search");
    }

    private static double LegacySurfaceLength(MeshProjection proj, Vec3d a, Vec3d b)
    {
        // curve shortening, resampled at 1/20 edge and pulled onto the facets: its length as a curve on the surface
        var old = ShortestPath.Compute(proj, a, b)!.Value;
        return FlipGeodesic.PolylineLength(ShortestPath.Resample(old.Points, proj.AverageEdgeLength / 20).Select(p => proj.ClosestPointGlobal(p).Point).ToArray());
    }

    [Theory]
    [InlineData("torus", 60, 30, 3e-4)]
    [InlineData("schwarzd", 36, 8, 5e-3)]
    public void CatalogueSurfaces_NoLongerThanCurveShortening(string shape, int resolution, int pairs, double slack)
    {
        // no closed form here: compare with the old curve shortening as a surface curve. On the torus
        // the old method follows its Dijkstra start round the wrong side of the hole's saddle vertices
        // in some pairs (up to 2 % too long); on Schwarz D, where every vertex is a saddle, geodesics
        // of nearly equal length are many and FlipOut may return one up to ~0.3 % longer
        var mesh = AnalyticShapes.Build(shape, 0, 0, 0, resolution);
        var proj = new MeshProjection(mesh);
        var rnd = new Random(1);
        var from = new List<Vec3d>(); var to = new List<Vec3d>();
        for (int k = 0; k < pairs; k++) { from.Add(mesh.Vertices[rnd.Next(mesh.VertexCount)]); to.Add(mesh.Vertices[rnd.Next(mesh.VertexCount)]); }
        var res = FlipGeodesic.Compute(mesh, from, to, out var reason);
        Assert.True(res != null, reason);
        double worst = double.NegativeInfinity, best = double.PositiveInfinity, miss = 0;
        for (int k = 0; k < pairs; k++)
        {
            var r = res![k]!;
            Assert.True(r.Converged);
            miss = Math.Max(miss, r.TraceMiss);
            double old = LegacySurfaceLength(proj, from[k], to[k]);
            double d = (r.Length - old) / old;
            worst = Math.Max(worst, d); best = Math.Min(best, d);
        }
        _out.WriteLine($"{shape} ({mesh.VertexCount} vertices): FlipOut vs curve shortening over {pairs} pairs: from {best:P2} to {worst:P2}; trace miss {miss:E1}");
        Assert.True(worst < slack, $"{worst:P2}");
        Assert.True(miss < 1e-8);
    }

    [Fact]
    public void BowTieVertex_IsSplitIntoOneVertexPerSheet()
    {
        // two fans touching at one vertex: each sheet gets its own copy of the vertex
        var v = new[] { new Vec3d(0, 0, 0), new Vec3d(1, -1, 0), new Vec3d(1, 1, 0), new Vec3d(-1, 1, 0), new Vec3d(-1, -1, 0) };
        var mesh = new MeshData(v, new[] { new[] { 0, 1, 2 }, new[] { 0, 3, 4 } });
        var it = IntrinsicTriangulation.Build(mesh.Vertices, mesh.Faces, out var reason);
        Assert.True(it != null, reason);
        Assert.Equal(1, it!.SplitVertices);
        var r = FlipGeodesic.Compute(mesh, new[] { new Vec3d(0.8, -0.3, 0), new Vec3d(0.8, -0.3, 0) }, new[] { new Vec3d(0.8, 0.5, 0), new Vec3d(-0.8, 0.3, 0) }, out _)!;
        Assert.Equal(0.8, r[0]!.Length, 12);
        // across the touching point: geodesic through the shared vertex is not available, no path
        Assert.Null(r[1]);
    }
}
