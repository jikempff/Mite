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
/// Nets seeded from a guide curve (<see cref="EvenlySpacedNet.Options.Guide"/>):
/// nodes every Spacing of arc length along the pulled guide, one curve of every
/// family through each node (Schling, Hitrec &amp; Barthel 2017: the net drawn
/// alternately from the nodes of the other family, the node distance the one
/// repeated parameter), geodesic families with the first direction carried
/// along the guide by parallel transport (Pottmann et al. 2010 §2). Checked
/// against closed forms: the catenoid's asymptotic lines u ∓ v = const, the
/// cylinder's 45° helices, the sphere's meridians, the saddle's straight
/// asymptotic lines x ± y = const.
/// </summary>
public class GuideCurveTests
{
    private readonly ITestOutputHelper _out;
    public GuideCurveTests(ITestOutputHelper o) { _out = o; }

    private static Vec3d[] Circle(double r, double z, int n)
    {
        var pts = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) { double t = 2 * Math.PI * i / n; pts[i] = new Vec3d(r * Math.Cos(t), r * Math.Sin(t), z); }
        pts[n] = pts[0];
        return pts;
    }

    private static double DistanceToPolyline(Vec3d p, Vec3d[] line)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vec3d a = line[i], ab = line[i + 1] - a;
            double l2 = ab.LengthSquared;
            double t = l2 < 1e-30 ? 0 : Math.Max(0, Math.Min(1, Vec3d.Dot(p - a, ab) / l2));
            best = Math.Min(best, (a + t * ab - p).Length);
        }
        return best;
    }

    private static double ArcLength(Vec3d[] c)
    {
        double s = 0; for (int i = 1; i < c.Length; i++) s += (c[i] - c[i - 1]).Length; return s;
    }

    /// <summary>Largest distance of any point of the curve from the catenoid asymptotic line through its start (u ∓ v = const).</summary>
    private static double CatenoidLineError(Vec3d[] c, out bool famA)
    {
        double u0 = Math.Atan2(c[0].Y, c[0].X), v0 = c[0].Z;
        Vec3d t0 = (c[Math.Min(3, c.Length - 1)] - c[0]).Normalized();
        double ch0 = Math.Cosh(v0), sh0 = Math.Sinh(v0);
        var ru0 = new Vec3d(-ch0 * Math.Sin(u0), ch0 * Math.Cos(u0), 0); var rv0 = new Vec3d(sh0 * Math.Cos(u0), sh0 * Math.Sin(u0), 1);
        famA = Math.Abs(Vec3d.Dot(t0, (ru0 + rv0).Normalized())) >= Math.Abs(Vec3d.Dot(t0, (ru0 - rv0).Normalized()));
        double k = famA ? u0 - v0 : u0 + v0, worst = 0;
        foreach (var q in c)
        {
            double u = Math.Atan2(q.Y, q.X), v = q.Z;
            double d = (famA ? u - v : u + v) - k;
            while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
            worst = Math.Max(worst, Math.Abs(d) * Math.Cosh(v) / Math.Sqrt(2.0));
        }
        return worst;
    }

    [Fact]
    public void Catenoid_WaistGuide_NodesEverySpacing_BothFamiliesThroughEveryNode()
    {
        // closed guide: n = round(2π / 0.3) = 21 nodes exactly 2π / 21 apart on the waist;
        // asymptotic lines u − v = k and u + v = k through every node, rim to rim
        var mesh = TestMeshes.CreateCatenoid(1, 2, 96, 48);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = 0.3;
        var guide = Circle(1.0, 0.0, 180);
        var oa = new EvenlySpacedNet.Options { Spacing = s, Guide = guide, StepSize = 0.02 };
        var ob = new EvenlySpacedNet.Options { Spacing = s, Guide = guide, StepSize = 0.02 };
        var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, oa, field.Family2);
        var B = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, ob, field.Family1);
        int n = (int)Math.Round(2 * Math.PI / s);
        Assert.Equal(n, oa.GuideNodes.Count);
        Assert.Equal(n, ob.GuideNodes.Count);
        double sExact = 2 * Math.PI / n;
        double worstNodeGap = 0, worstNodeZ = 0;
        for (int i = 0; i < n; i++)
        {
            var p = oa.GuideNodes[i]; var q = oa.GuideNodes[(i + 1) % n];
            double th = Math.Atan2(q.Y, q.X) - Math.Atan2(p.Y, p.X);
            while (th < 0) th += 2 * Math.PI;
            worstNodeGap = Math.Max(worstNodeGap, Math.Abs(th - sExact));
            worstNodeZ = Math.Max(worstNodeZ, Math.Abs(p.Z));
            Assert.Equal(0.0, (p - ob.GuideNodes[i]).Length, 12);
        }
        _out.WriteLine($"{n} nodes, angular gap error {worstNodeGap:E2} rad, |z| ≤ {worstNodeZ:E2}; A {A.Count}, B {B.Count} curves");
        Assert.True(worstNodeGap < 1e-4, $"nodes are not 2π/{n} apart: {worstNodeGap:E2}"); // 2e-5 from the 96-gon chords (angle is not linear in chord length)
        Assert.True(worstNodeZ < 1e-4, "nodes left the waist");

        Assert.Equal(n, A.Count);
        Assert.Equal(n, B.Count);
        double worstLine = 0, worstThrough = 0;
        foreach (var c in A.Concat(B))
        {
            foreach (var e in new[] { c[0], c[^1] })
                Assert.True(proj.IsOnBoundary(proj.ClosestPoint(e, proj.NearestVertexGlobal(e)), 1e-4), "guide web curves run rim to rim");
            worstLine = Math.Max(worstLine, CatenoidLineError(c, out _));
        }
        foreach (var p in oa.GuideNodes)
        {
            double da = A.Min(c => DistanceToPolyline(p, c)), db = B.Min(c => DistanceToPolyline(p, c));
            worstThrough = Math.Max(worstThrough, Math.Max(da, db));
        }
        int famA = A.Count(c => { CatenoidLineError(c, out bool a); return a; });
        _out.WriteLine($"closed-form offset ≤ {worstLine:F4} (edge {proj.AverageEdgeLength:F3}); every node has an A and a B curve within {worstThrough:E2}; {famA} of {A.Count} A curves are u − v = const");
        Assert.True(worstLine < 0.5 * proj.AverageEdgeLength, $"curve off its asymptotic line by {worstLine:F4}");
        Assert.True(worstThrough < 1e-3, $"a node without both families: {worstThrough:E2}");
        Assert.True(famA == A.Count || famA == 0, "family A mixed");
    }

    [Fact]
    public void Catenoid_GuideOffTheSurface_PullsToTheWaist_SameNodes()
    {
        // the circle r = 1.5 in z = 0 is off the surface; its closest points on the
        // catenoid r = cosh z are the waist (f(z) = (cosh z − 1.5)² + z² has its
        // minimum at z = 0), so the pulled guide gives the same nodes
        var mesh = TestMeshes.CreateCatenoid(1, 2, 96, 48);
        var proj = new MeshProjection(mesh);
        var on = EvenlySpacedNet.GuideSeeds(proj, Circle(1.0, 0.0, 180), 0.3, default, -1, Vec3d.Zero);
        var off = EvenlySpacedNet.GuideSeeds(proj, Circle(1.5, 0.0, 180), 0.3, default, -1, Vec3d.Zero);
        Assert.Equal(21, on.Count);
        Assert.Equal(on.Count, off.Count);
        double worst = on.Zip(off, (a, b) => (a.pos - b.pos).Length).Max();
        _out.WriteLine($"pulled guide vs on-surface guide: nodes differ by ≤ {worst:E2}");
        Assert.True(worst < 1e-4, $"pulled nodes differ by {worst:E2}");
    }

    [Fact]
    public void Catenoid_OpenMeridianGuide_NodesCentredAtExactArcLength()
    {
        // meridian z ∈ [−0.9, 0.9]: arc length ∫cosh z dz = 2 sinh 0.9 = 2.0530,
        // 7 nodes 0.3 apart centred, z_k = asinh(−sinh 0.9 + margin + 0.3 k)
        var mesh = TestMeshes.CreateCatenoid(1, 2, 96, 96);
        var proj = new MeshProjection(mesh);
        var guide = Enumerable.Range(0, 181).Select(i => { double z = -0.9 + 1.8 * i / 180.0; return new Vec3d(Math.Cosh(z), 0, z); }).ToArray();
        double s = 0.3, L = 2 * Math.Sinh(0.9);
        var nodes = EvenlySpacedNet.GuideSeeds(proj, guide, s, default, -1, Vec3d.Zero);
        int n = (int)Math.Floor(L / s) + 1;
        double margin = 0.5 * (L - (n - 1) * s);
        Assert.Equal(n, nodes.Count);
        double worst = 0;
        for (int k = 0; k < n; k++)
        {
            double zk = Asinh(-Math.Sinh(0.9) + margin + k * s);
            worst = Math.Max(worst, Math.Abs(nodes[k].pos.Z - zk));
        }
        _out.WriteLine($"{n} nodes, margin {margin:F4}, z error ≤ {worst:E2} (mesh row {2.0 / 96:F4})");
        Assert.True(worst < 2e-3, $"node z off by {worst:E2}");
        static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1));
    }

    [Fact]
    public void Cylinder_CircleGuide_HelicesAtTheTransportedAngle_SpacingTimesSine()
    {
        // guide = the circle z = 0 (a geodesic of the cylinder, so parallel transport
        // keeps the 45° angle): n = round(2π / 0.3) = 21 helices through the nodes,
        // perpendicular distance 2π/21 · sin 45° — Spacing is the node distance
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 96, 48);
        var proj = new MeshProjection(cyl);
        double alpha = Math.PI / 4, s = 0.3;
        int seed = 24 * 96;
        var opts = new EvenlySpacedNet.Options { Spacing = s, Guide = Circle(1.0, 0.0, 180), StepSize = 0.02 };
        var g = EvenlySpacedNet.TraceGeodesics(cyl, seed, new Vec3d(0, Math.Sin(alpha), Math.Cos(alpha)), opts);
        int n = (int)Math.Round(2 * Math.PI / s);
        Assert.Equal(n, opts.GuideNodes.Count);
        Assert.Equal(n, g.Count);
        double worstRise = 0, worstThrough = 0;
        foreach (var c in g)
        {
            foreach (var e in new[] { c[0], c[^1] })
                Assert.True(proj.IsOnBoundary(proj.ClosestPoint(e, proj.NearestVertexGlobal(e)), 1e-4), "helices run rim to rim");
            double unwrapped = 0, prev = Math.Atan2(c[0].Y, c[0].X);
            for (int i = 1; i < c.Length; i++)
            {
                double t = Math.Atan2(c[i].Y, c[i].X), d = t - prev;
                while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                unwrapped += d; prev = t;
                worstRise = Math.Max(worstRise, Math.Abs(Math.Abs(c[i].Z - c[0].Z) - Math.Abs(unwrapped) / Math.Tan(alpha)));
            }
        }
        foreach (var p in opts.GuideNodes) worstThrough = Math.Max(worstThrough, g.Min(c => DistanceToPolyline(p, c)));
        var phases = g.Select(c => { var m = c.OrderBy(q => Math.Abs(q.Z)).First(); double th = Math.Atan2(m.Y, m.X) - m.Z * Math.Tan(alpha); return (th % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI); }).OrderBy(x => x).ToList();
        var gaps = phases.Select((k, i) => ((i + 1 < phases.Count ? phases[i + 1] : phases[0] + 2 * Math.PI) - k) * Math.Sin(alpha)).ToList();
        double want = 2 * Math.PI / n * Math.Sin(alpha);
        _out.WriteLine($"{g.Count} helices, rise error ≤ {worstRise:E2}, through the nodes within {worstThrough:E2}; perpendicular gaps {gaps.Min():F4} … {gaps.Max():F4} (2π/{n} · sin 45° = {want:F4})");
        Assert.True(worstRise < 0.01, $"not 45° helices: {worstRise:F4}");
        Assert.True(worstThrough < 1e-3, $"a node without its helix: {worstThrough:E2}");
        Assert.True(gaps.All(x => Math.Abs(x - want) < 0.01 * want), "perpendicular gaps are not Spacing · sin α");
    }

    /// <summary>Zone of the unit sphere, colatitude in [α0, α1], triangulated like the cylinder (row 0 at α0).</summary>
    private static MeshData SphereZone(double a0, double a1, int segments, int rows)
    {
        var verts = new List<Vec3d>(); var faces = new List<int[]>();
        for (int j = 0; j <= rows; j++)
        {
            double a = a0 + (a1 - a0) * j / rows;
            for (int i = 0; i < segments; i++)
            {
                double th = 2 * Math.PI * i / segments;
                verts.Add(new Vec3d(Math.Sin(a) * Math.Cos(th), Math.Sin(a) * Math.Sin(th), Math.Cos(a)));
            }
        }
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * segments + i, b = j * segments + (i + 1) % segments, c = b + segments, d = a + segments;
                faces.Add(new[] { a, b, c }); faces.Add(new[] { a, c, d });
            }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    [Fact]
    public void SphereZone_EquatorGuide_MeridiansInTheirPlanes()
    {
        // guide = the equator (a geodesic): the north direction transported along it
        // stays north, so every curve is a meridian rim to rim, in a plane through the axis
        var zone = SphereZone(Math.PI / 2 - 0.65, Math.PI / 2 + 0.65, 120, 40);
        var proj = new MeshProjection(zone);
        double s = 0.5;
        int seed = proj.NearestVertexGlobal(new Vec3d(1, 0, 0));
        var opts = new EvenlySpacedNet.Options { Spacing = s, Guide = Circle(1.0, 0.0, 180), StepSize = 0.02 };
        var g = EvenlySpacedNet.TraceGeodesics(zone, seed, new Vec3d(0, 0, 1), opts);
        int n = (int)Math.Round(2 * Math.PI / s);
        Assert.Equal(n, opts.GuideNodes.Count);
        Assert.Equal(n, g.Count);
        double worstPlane = 0, worstThrough = 0, worstLen = 0, want = 1.3;
        foreach (var c in g)
        {
            foreach (var e in new[] { c[0], c[^1] })
                Assert.True(proj.IsOnBoundary(proj.ClosestPoint(e, proj.NearestVertexGlobal(e)), 1e-4), "meridians run rim to rim");
            var p0 = opts.GuideNodes.OrderBy(q => DistanceToPolyline(q, c)).First();
            var nrm = new Vec3d(-p0.Y, p0.X, 0).Normalized();
            worstPlane = Math.Max(worstPlane, c.Max(q => Math.Abs(Vec3d.Dot(q, nrm))));
            worstLen = Math.Max(worstLen, Math.Abs(ArcLength(c) - want));
        }
        foreach (var p in opts.GuideNodes) worstThrough = Math.Max(worstThrough, g.Min(c => DistanceToPolyline(p, c)));
        _out.WriteLine($"{g.Count} meridians for {n} nodes; out of plane ≤ {worstPlane:E2}, through the nodes within {worstThrough:E2}, length error ≤ {worstLen:E2} (2 × 0.65)");
        Assert.True(worstPlane < 5e-3, $"a meridian leaves its plane by {worstPlane:E2}");
        Assert.True(worstThrough < 1e-3, $"a node without its meridian: {worstThrough:E2}");
        Assert.True(worstLen < 0.01, $"meridian length off by {worstLen:E2}");
    }

    [Fact]
    public void SphereZone_LatitudeGuide_DirectionsParallelTransported_Holonomy()
    {
        // guide = the latitude circle at colatitude α = 60° (not a geodesic, κg = cot α):
        // the north vector carried along it by parallel transport turns against the
        // meridian by θ cos α after the azimuth θ (∫κg ds = cot α · θ sin α); the
        // geodesics leave the nodes in those directions (Pottmann et al. 2010 §2) —
        // 13° between neighbours here, so they fan out and cross
        var zone = SphereZone(Math.PI / 2 - 0.9, Math.PI / 2 + 0.9, 180, 60);
        var proj = new MeshProjection(zone);
        double alpha = Math.PI / 3, r = Math.Sin(alpha), z = Math.Cos(alpha), s = 0.4;
        int seed = proj.NearestVertexGlobal(new Vec3d(r, 0, z));
        var nodes = EvenlySpacedNet.GuideSeeds(proj, Circle(r, z, 360), s, new Vec3d(r, 0, z), seed, new Vec3d(-z, 0, r));
        int n = (int)Math.Round(2 * Math.PI * r / s);
        Assert.Equal(n, nodes.Count);
        double worst = 0; int sign = 0;
        var report = new List<string>();
        for (int k = 0; k < n; k++)
        {
            var (p, _, d) = nodes[k];
            double th = Math.Atan2(p.Y, p.X); if (th < 0) th += 2 * Math.PI;
            var north = new Vec3d(-z * Math.Cos(th), -z * Math.Sin(th), r);
            var east = new Vec3d(-Math.Sin(th), Math.Cos(th), 0);
            double ang = Math.Atan2(Vec3d.Dot(d, east), Vec3d.Dot(d, north));
            // transported from node 0 the short way round: θ for θ ≤ π, θ − 2π beyond
            double expect = (th <= Math.PI ? th : th - 2 * Math.PI) * Math.Cos(alpha);
            if (k == 1) sign = ang * expect >= 0 ? 1 : -1;
            // a direction, not a vector (the node opposite the seed is the seam, ±90°)
            double err = Math.Abs(ang - sign * expect) % Math.PI;
            err = Math.Min(err, Math.PI - err);
            worst = Math.Max(worst, err);
            if (k % 3 == 0) report.Add($"θ {th * 180 / Math.PI:F0}°: {ang * 180 / Math.PI:F2}° ({sign * expect * 180 / Math.PI:F2}°)");
        }
        _out.WriteLine($"{n} nodes, holonomy error ≤ {worst * 180 / Math.PI:F3}°; " + string.Join(", ", report));
        Assert.True(worst < 0.2 * Math.PI / 180, $"transported direction off by {worst * 180 / Math.PI:F2}°");

        // and the geodesics do leave the nodes in those directions
        var opts = new EvenlySpacedNet.Options { Spacing = s, Guide = Circle(r, z, 360), StepSize = 0.02, WebFillGaps = false };
        var g = EvenlySpacedNet.TraceGeodesics(zone, seed, new Vec3d(-z, 0, r), opts);
        int through = 0; double worstDir = 0;
        for (int k = 0; k < n; k++)
        {
            var (p, _, d) = nodes[k];
            var c = g.OrderBy(q => DistanceToPolyline(p, q)).First();
            if (DistanceToPolyline(p, c) > 1e-3) continue;
            int i = Enumerable.Range(1, c.Length - 2).OrderBy(j => (c[j] - p).LengthSquared).First();
            Vec3d t = (c[i + 1] - c[i - 1]).Normalized();
            worstDir = Math.Max(worstDir, Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(t, d)))) * 180 / Math.PI);
            through++;
        }
        _out.WriteLine($"{g.Count} geodesics (cut where they meet, K > 0); {through} nodes keep their geodesic, direction within {worstDir:F2}°");
        Assert.True(through >= n / 2, $"only {through} of {n} nodes keep a geodesic");
        Assert.True(worstDir < 2.0, $"geodesic leaves its node {worstDir:F2}° off the transported direction");
    }

    [Fact]
    public void Saddle_GuideAlongOneFamily_SeedsOnlyTheOther_SchlingSeedCurve()
    {
        // z = x² − y²: the asymptotic lines are x ± y = c. A guide along x = y (one
        // curve of family A) re-seeds nothing in A beyond the gap filling, while
        // family B gets one line x + y = c through every node, exactly Spacing apart
        var saddle = TestMeshes.CreateSaddle(60, 2.0);
        var proj = new MeshProjection(saddle);
        var pc = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(pc, saddle, 15.0);
        double s = 0.25;
        var guide = new[] { new Vec3d(-0.9, -0.9, 0), new Vec3d(0.9, 0.9, 0) };
        // which family runs along x = y at the centre
        int centre = 30 * 61 + 30;
        var along = new Vec3d(1, 1, 0).Normalized();
        bool f1Along = Math.Abs(Vec3d.Dot(field.Family1[centre].Normalized(), along)) > Math.Abs(Vec3d.Dot(field.Family2[centre].Normalized(), along));
        var dA = f1Along ? field.Family1 : field.Family2; var dB = f1Along ? field.Family2 : field.Family1;
        var oa = new EvenlySpacedNet.Options { Spacing = s, Guide = guide, StepSize = 0.02, WebFillGaps = false };
        var ob = new EvenlySpacedNet.Options { Spacing = s, Guide = guide, StepSize = 0.02, WebFillGaps = false };
        var A = EvenlySpacedNet.TraceField(saddle, dA, field.Exists, -1, oa, dB);
        var B = EvenlySpacedNet.TraceField(saddle, dB, field.Exists, -1, ob, dA);
        double L = 1.8 * Math.Sqrt(2);
        int n = (int)Math.Floor(L / s + 1e-9) + 1;
        Assert.Equal(n, oa.GuideNodes.Count);
        var gapsAlong = Enumerable.Range(0, n - 1).Select(i => (oa.GuideNodes[i + 1] - oa.GuideNodes[i]).Length).ToList();
        _out.WriteLine($"{n} nodes along x = y, gaps {gapsAlong.Min():F5} … {gapsAlong.Max():F5} (Spacing {s}); A {A.Count} curve(s), B {B.Count}");
        Assert.True(gapsAlong.All(x => Math.Abs(x - s) < 1e-3), "node gaps are not Spacing");
        Assert.Single(A);
        Assert.Equal(n, B.Count);
        double worstStraight = 0, worstThrough = 0;
        foreach (var c in B)
        {
            // x + y = const along the whole line
            double k = c[0].X + c[0].Y;
            worstStraight = Math.Max(worstStraight, c.Max(q => Math.Abs(q.X + q.Y - k)) / Math.Sqrt(2));
        }
        foreach (var p in oa.GuideNodes) worstThrough = Math.Max(worstThrough, Math.Min(B.Min(c => DistanceToPolyline(p, c)), 1));
        var ks = B.Select(c => (c[0].X + c[0].Y) / Math.Sqrt(2)).OrderBy(x => x).ToList();
        var dk = Enumerable.Range(0, ks.Count - 1).Select(i => ks[i + 1] - ks[i]).ToList();
        _out.WriteLine($"B lines x + y = const to {worstStraight:E2}, through the nodes within {worstThrough:E2}, line offsets {dk.Min():F4} … {dk.Max():F4}");
        Assert.True(worstStraight < 0.02, $"B curve bows by {worstStraight:F4}");
        Assert.True(worstThrough < 2e-3, $"a node without its B curve: {worstThrough:E2}");
        Assert.True(dk.All(x => Math.Abs(x - s) < 0.02 * s), "B lines are not Spacing apart");
    }

    [Fact]
    public void Guide_TooShortOrDegenerate_FallsBackToTheLayout()
    {
        var saddle = TestMeshes.CreateSaddle(30, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(pc, saddle, 15.0);
        var plain = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3, StepSize = 0.02 }, field.Family2);
        var one = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3, StepSize = 0.02, Guide = new[] { new Vec3d(0, 0, 0) } }, field.Family2);
        Assert.Equal(plain.Count, one.Count);
        var proj = new MeshProjection(saddle);
        Assert.Empty(EvenlySpacedNet.GuideSeeds(proj, new[] { new Vec3d(0, 0, 0), new Vec3d(0, 0, 0) }, 0.3, default, -1, Vec3d.Zero));
    }
}
