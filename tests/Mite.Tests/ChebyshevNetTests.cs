using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Chebyshev nets by the compass method against closed forms.
///
/// A discrete Chebyshev net is a quad net with every edge of the same length L
/// (Garg et al. 2014, Wire mesh design, §9.2: η_len = max |‖e‖/L − 1| is zero
/// for a Chebyshev net). The compass method (Otto, Hennicke &amp; Matsushita 1974,
/// IL10 Gitterschalen; Bouhaya, Baverel &amp; Caron 2015 §3) places the fourth
/// node of every cell at the intersection of the surface with the two spheres
/// of radius L about its two parents. Where the surface is a plane, a prism or
/// the unit sphere that intersection has a closed form, so the net is known
/// exactly and the construction is checked node by node.
/// </summary>
public class ChebyshevNetTests
{
    private readonly ITestOutputHelper _out;
    public ChebyshevNetTests(ITestOutputHelper output) { _out = output; }

    // ---------------------------------------------------------------- helpers

    /// <summary>Largest relative edge-length deviation over the valid net (Garg et al. 2014, η_len).</summary>
    internal static double EdgeError(ChebyshevNet.Result net, double L)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        double worst = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                if (!net.Valid[i, j]) continue;
                if (i + 1 < nu && net.Valid[i + 1, j])
                    worst = Math.Max(worst, Math.Abs((net.Points[i + 1, j] - net.Points[i, j]).Length / L - 1));
                if (j + 1 < nv && net.Valid[i, j + 1])
                    worst = Math.Max(worst, Math.Abs((net.Points[i, j + 1] - net.Points[i, j]).Length / L - 1));
            }
        return worst;
    }

    private static int ValidCount(ChebyshevNet.Result net)
    {
        int n = 0;
        foreach (bool b in net.Valid) if (b) n++;
        return n;
    }

    internal static (Vec3d[,] pts, bool[,] valid) ExactSphereNet(Vec3d p0, Vec3d d1, Vec3d d2, double L, int cu, int cv) =>
        TestMeshes.ChebyshevSphereNet(p0, d1, d2, L, cu, cv);

    private static (double maxErr, int compared) Compare(ChebyshevNet.Result net, Vec3d[,] exact, bool[,] exactValid)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        double worst = 0; int n = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                if (!net.Valid[i, j] || !exactValid[i, j]) continue;
                worst = Math.Max(worst, (net.Points[i, j] - exact[i, j]).Length);
                n++;
            }
        return (worst, n);
    }

    // ------------------------------------------------------------------ plane

    [Theory]
    [InlineData(90.0)]
    [InlineData(60.0)]
    public void Plane_IsTheExactLattice(double angleDeg)
    {
        // Any triangulation of a plane: the net is the lattice seed + iL d1 + jL d2
        var plane = TestMeshes.CreateIrregularPlane(24, 4.0, 0.35, 5);
        int seed = 12 * 25 + 12;
        double L = 0.3, ang = angleDeg * Math.PI / 180;
        var net = ChebyshevNet.Compute(plane, seed, new Vec3d(1, 0, 0),
            new ChebyshevNet.Options { EdgeLength = L, CountU = 4, CountV = 4, Angle = ang });
        Vec3d p0 = plane.Vertices[seed];
        Vec3d d1 = new Vec3d(1, 0, 0), d2 = new Vec3d(Math.Cos(ang), Math.Sin(ang), 0);
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        double worst = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                Assert.True(net.Valid[i, j], $"node ({i},{j}) missing");
                Vec3d e = p0 + (i - net.OriginU) * L * d1 + (j - net.OriginV) * L * d2;
                worst = Math.Max(worst, (net.Points[i, j] - e).Length);
            }
        double edge = EdgeError(net, L);
        _out.WriteLine($"plane {angleDeg}°: node error {worst:E2}, edge error {edge:E2}");
        Assert.True(worst < 1e-9, $"lattice node error {worst:E2}");
        Assert.True(edge < 1e-9, $"edge error {edge:E2}");
    }

    // ---------------------------------------------------------------- cylinder

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    public void Cylinder_IsTheRolledLattice(int segments)
    {
        // Unit cylinder, one family along the generators (exact edges L), the
        // other round the circumference (chord L = angular step 2 asin(L/2)):
        // node (i, j) = (cos jδ, sin jδ, iL). On the prism the nodes sit on the
        // facets, so the error is bounded by the sagitta edge²/8.
        var cyl = TestMeshes.CreateCylinder(1.0, 4.0, segments, 64);
        int seed = 32 * segments;                                   // (1, 0, 0) on the middle row
        double L = 0.25;
        var net = ChebyshevNet.Compute(cyl, seed, new Vec3d(0, 0, 1),
            new ChebyshevNet.Options { EdgeLength = L, CountU = 6, CountV = 6 });
        double delta = 2 * Math.Asin(L / 2);
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        double worst = 0; int n = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                Assert.True(net.Valid[i, j], $"node ({i},{j}) missing");
                int a = i - net.OriginU, b = j - net.OriginV;
                // d2 = n × d1 = (1,0,0) × (0,0,1) = (0,−1,0): the v family runs clockwise
                Vec3d e = new Vec3d(Math.Cos(b * delta), -Math.Sin(b * delta), a * L);
                worst = Math.Max(worst, (net.Points[i, j] - e).Length);
                n++;
            }
        double edgeLen = 2 * Math.Sin(Math.PI / segments), sagitta = edgeLen * edgeLen / 8;
        double edge = EdgeError(net, L);
        _out.WriteLine($"cylinder {segments}: node error {worst:E2} (sagitta {sagitta:E2}), edge error {edge:E2}, {n} nodes");
        Assert.True(worst < 2.0 * sagitta, $"node error {worst:E2} exceeds twice the facet sagitta {sagitta:E2}");
        Assert.True(edge < 1e-9, $"edge error {edge:E2}");
    }

    // ------------------------------------------------------------------ sphere

    public static IEnumerable<object[]> SphereCases()
    {
        yield return new object[] { 24, 0.2, 5 };
        yield return new object[] { 48, 0.2, 5 };
        yield return new object[] { 48, 0.1, 10 };
    }

    [Theory]
    [MemberData(nameof(SphereCases))]
    public void Sphere_MatchesTheClosedFormCompassNet(int subdivisions, double L, int count)
    {
        var sphere = TestMeshes.CreateUnitSphere(subdivisions);
        int seed = 1 + (subdivisions / 2 - 1) * 2 * subdivisions;   // (1, 0, 0) on the equator
        var net = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 1, 0),
            new ChebyshevNet.Options { EdgeLength = L, CountU = count, CountV = count });
        Vec3d p0 = new Vec3d(1, 0, 0), d1 = new Vec3d(0, 1, 0), d2 = new Vec3d(0, 0, 1); // n × d1
        var (exact, exactValid) = ExactSphereNet(p0, d1, d2, L, count, count);
        var (worst, compared) = Compare(net, exact, exactValid);
        // the equatorial facets are right triangles with legs e: their centroid
        // sits (hypotenuse/2)²/2 = e²/4 inside the sphere, and the compass
        // propagates that offset from parents to children
        double edgeLen = 2 * Math.PI / (2 * subdivisions), facet = edgeLen * edgeLen / 4;
        double edge = EdgeError(net, L);
        int valid = ValidCount(net), exactCount = 0;
        foreach (bool b in exactValid) if (b) exactCount++;
        double offSphere = 0;
        foreach (var (i, j) in Nodes(net)) offSphere = Math.Max(offSphere, Math.Abs(net.Points[i, j].Length - 1));
        _out.WriteLine($"sphere {subdivisions}, L {L}: node error {worst:E2} over {compared} nodes (facet depth {facet:E2}), " +
                       $"edge error {edge:E2}, valid {valid} / exact {exactCount}, off sphere {offSphere:E2}, " +
                       $"net edge error {net.EdgeError:E2}, failed {net.Failed}, locked {net.Locked}");
        Assert.Equal(exactCount, valid);
        Assert.Equal(exactCount, compared);
        Assert.True(worst < 1.5 * facet, $"node error {worst:E2} exceeds 1.5 × the facet depth {facet:E2}");
        Assert.True(edge < 1e-9, $"edge error {edge:E2}");
        Assert.Equal(edge, net.EdgeError, 12);
        Assert.Equal(0, net.Failed);
        Assert.True(offSphere < facet, $"nodes leave the sphere by {offSphere:E2}");
    }

    [Fact]
    public void Sphere_ErrorConvergesWithTheMesh()
    {
        double L = 0.2;
        var errs = new List<double>();
        foreach (int sub in new[] { 24, 48, 96 })
        {
            var sphere = TestMeshes.CreateUnitSphere(sub);
            int seed = 1 + (sub / 2 - 1) * 2 * sub;
            var net = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 1, 0),
                new ChebyshevNet.Options { EdgeLength = L, CountU = 5, CountV = 5 });
            var (exact, exactValid) = ExactSphereNet(new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1), L, 5, 5);
            errs.Add(Compare(net, exact, exactValid).maxErr);
        }
        _out.WriteLine($"sphere node error 24 / 48 / 96: {errs[0]:E2} / {errs[1]:E2} / {errs[2]:E2}");
        // facet sagitta is O(edge²): each refinement should cut the error by ~4; demand at least 2.5
        Assert.True(errs[1] < errs[0] / 2.5, $"24 → 48: {errs[0]:E2} → {errs[1]:E2}");
        Assert.True(errs[2] < errs[1] / 2.5, $"48 → 96: {errs[1]:E2} → {errs[2]:E2}");
    }

    private static IEnumerable<(int, int)> Nodes(ChebyshevNet.Result net)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
                if (net.Valid[i, j]) yield return (i, j);
    }
}

public class ChebyshevLockingTests
{
    private readonly ITestOutputHelper _out;
    public ChebyshevLockingTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void Sphere_ShearsToLocking_AndHazzidakisHolds()
    {
        // A big patch on the unit sphere: Hazzidakis (∫∫K dA = 2π − Σα) makes
        // the shear angle fall away from the axes until the cells collapse.
        // The compass circles still meet past the fold, so the net does not
        // stop by itself (that is what MinAngle is for); up to the fold the
        // mesh net must follow the closed form, and the cell defects must
        // integrate K = 1.
        double L = 0.2; int count = 12;
        var sphere = TestMeshes.CreateUnitSphere(48);
        int seed = 1 + 23 * 96;
        var net = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 1, 0),
            new ChebyshevNet.Options { EdgeLength = L, CountU = count, CountV = count });
        var (exact, exactValid) = ChebyshevNetTests.ExactSphereNet(new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1), L, count, count);
        int nu = 2 * count + 1, nv = nu;
        double ExactShear(int i, int j)
        {
            Vec3d p = exact[i, j];
            Vec3d du = i + 1 < nu && exactValid[i + 1, j] ? exact[i + 1, j] - p : p - exact[i - 1, j];
            Vec3d dv = j + 1 < nv && exactValid[i, j + 1] ? exact[i, j + 1] - p : p - exact[i, j - 1];
            return Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(du.Normalized(), dv.Normalized()))));
        }
        double lim = 10 * Math.PI / 180;
        // a node is open when every angle between a u-edge and a v-edge at it
        // (both directions where the neighbours exist) lies in [10°, 170°]
        bool ExactOpen(int i, int j) => ExactOpenAt(i, j, lim);
        bool ExactOpenAt(int i, int j, double lim)
        {
            Vec3d p = exact[i, j];
            var us = new List<Vec3d>(); var vs = new List<Vec3d>();
            if (i + 1 < nu && exactValid[i + 1, j]) us.Add(exact[i + 1, j] - p);
            if (i > 0 && exactValid[i - 1, j]) us.Add(exact[i - 1, j] - p);
            if (j + 1 < nv && exactValid[i, j + 1]) vs.Add(exact[i, j + 1] - p);
            if (j > 0 && exactValid[i, j - 1]) vs.Add(exact[i, j - 1] - p);
            foreach (var u in us) foreach (var v in vs)
            {
                double w = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(u.Normalized(), v.Normalized()))));
                if (w < lim || w > Math.PI - lim) return false;
            }
            return true;
        }
        double worstAngle = 0, worstPos = 0, minShear = Math.PI; int compared = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                Assert.Equal(exactValid[i, j], net.Valid[i, j]);
                if (!net.Valid[i, j]) continue;
                double w = net.ShearAngles[i, j];
                if (double.IsNaN(w)) continue;
                minShear = Math.Min(minShear, w);
                // compare only where the closed form has not folded: every
                // node between this one and the seed shears more than 10°
                bool open = true;
                for (int a = Math.Min(count, i); a <= Math.Max(count, i) && open; a++)
                    for (int b = Math.Min(count, j); b <= Math.Max(count, j) && open; b++)
                        if (!ExactOpen(a, b)) open = false;
                if (!open) continue;
                compared++;
                worstAngle = Math.Max(worstAngle, Math.Abs(w - ExactShear(i, j)));
                worstPos = Math.Max(worstPos, (net.Points[i, j] - exact[i, j]).Length);
            }
        _out.WriteLine($"locking patch: {net.ValidCount} nodes, failed {net.Failed}, min shear {minShear * 180 / Math.PI:F2}°; " +
                       $"before the fold: {compared} nodes, shear vs exact {worstAngle * 180 / Math.PI:F3}°, position {worstPos:E2}");
        Assert.True(minShear < 5 * Math.PI / 180, $"the net should shear below 5° at the corners, got {minShear * 180 / Math.PI:F1}°");
        Assert.True(compared > 200);
        Assert.True(worstAngle < 0.5 * Math.PI / 180, $"shear angles differ from the closed form by {worstAngle * 180 / Math.PI:F3}°");
        Assert.True(worstPos < 3e-3, $"nodes differ from the closed form by {worstPos:E2}");

        // Hazzidakis is Gauss–Bonnet on the net: on the exact discrete net,
        // split every cell by the same diagonal; the angle deficit
        // 2π − Σ (six triangle angles) at an interior node is the curvature
        // of a third of the incident area (the angle-deficit estimate, as in
        // GaussianCurvature): Σ deficit / Σ area → K = 1 over the nodes whose
        // cells are open above 30° (thin triangles near the fold spoil the
        // local estimate: 0.97 with every open node, 0.995 above 30°)
        double sumDeficit = 0, sumArea = 0; int nodes = 0; double worstRatio = 0;
        for (int i = 1; i + 1 < nu; i++)
            for (int j = 1; j + 1 < nv; j++)
            {
                bool all = true;
                for (int a = i - 1; a <= i + 1 && all; a++)
                    for (int b = j - 1; b <= j + 1 && all; b++)
                        all = exactValid[a, b] && ExactOpenAt(a, b, 30 * Math.PI / 180);
                if (!all) continue;
                Vec3d p = exact[i, j];
                // every cell split by the diagonal (a,b)–(a+1,b+1): a consistent
                // triangulation in which each interior node has six neighbours
                var ring = new[] { exact[i + 1, j], exact[i + 1, j + 1], exact[i, j + 1],
                                   exact[i - 1, j], exact[i - 1, j - 1], exact[i, j - 1] };
                double Ang(Vec3d u, Vec3d v) => Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(u.Normalized(), v.Normalized()))));
                double sum = 0, area = 0;
                for (int k = 0; k < 6; k++)
                {
                    Vec3d u = ring[k] - p, v = ring[(k + 1) % 6] - p;
                    sum += Ang(u, v);
                    area += Vec3d.Cross(u, v).Length / 6;            // a third of each triangle
                }
                double deficit = 2 * Math.PI - sum;
                sumDeficit += deficit; sumArea += area; nodes++;
                worstRatio = Math.Max(worstRatio, Math.Abs(deficit / area - 1));
            }
        _out.WriteLine($"Gauss–Bonnet: {nodes} nodes, Σ deficit {sumDeficit:F4} vs Σ dual area {sumArea:F4} (ratio {sumDeficit / sumArea:F4}), worst node ratio error {worstRatio:F3}");
        Assert.True(nodes > 100);
        Assert.True(Math.Abs(sumDeficit / sumArea - 1) < 0.02, $"Σ deficit / Σ area = {sumDeficit / sumArea:F4}");
    }

    [Fact]
    public void MinAngle_StopsTheNetBeforeItLocks()
    {
        double L = 0.2; int count = 12;
        var sphere = TestMeshes.CreateUnitSphere(48);
        int seed = 1 + 23 * 96;
        var free = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 1, 0),
            new ChebyshevNet.Options { EdgeLength = L, CountU = count, CountV = count });
        var bounded = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 1, 0),
            new ChebyshevNet.Options { EdgeLength = L, CountU = count, CountV = count, MinAngle = 30 * Math.PI / 180 });
        double minFree = double.MaxValue, minBounded = double.MaxValue;
        foreach (double w in free.ShearAngles) if (!double.IsNaN(w)) minFree = Math.Min(minFree, w);
        foreach (double w in bounded.ShearAngles) if (!double.IsNaN(w)) minBounded = Math.Min(minBounded, w);
        _out.WriteLine($"free: {free.ValidCount} nodes, min shear {minFree * 180 / Math.PI:F2}°; " +
                       $"MinAngle 30°: {bounded.ValidCount} nodes, locked {bounded.Locked}, min shear {minBounded * 180 / Math.PI:F2}°");
        Assert.True(minFree < 30 * Math.PI / 180);
        Assert.True(bounded.Locked > 0);
        Assert.True(bounded.ValidCount < free.ValidCount);
        Assert.True(minBounded >= 30 * Math.PI / 180 - 1e-9, $"a node shears to {minBounded * 180 / Math.PI:F2}°");
        // every bounded node is a free node at the same place
        int nu = 2 * count + 1;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nu; j++)
                if (bounded.Valid[i, j])
                {
                    Assert.True(free.Valid[i, j]);
                    Assert.True((bounded.Points[i, j] - free.Points[i, j]).Length < 1e-12);
                }
    }
}
