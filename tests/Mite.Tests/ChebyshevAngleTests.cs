using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// The shear angles of a Chebyshev net and Hazzidakis' formula
/// ∬_cell K dA = 2π − Σ α_i, checked where the truth is exact: the plane
/// (angles constant, integral 0), the unit sphere (integral = spherical
/// area of the cell, K = 1), the cylinder (K = 0, square cells).
/// </summary>
public class ChebyshevAngleTests
{
    private readonly ITestOutputHelper _out;
    public ChebyshevAngleTests(ITestOutputHelper output) { _out = output; }

    private static int Nearest(MeshData m, Vec3d p)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < m.VertexCount; i++)
        {
            double d = (m.Vertices[i] - p).LengthSquared;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private static IEnumerable<(int i, int j)> Nodes(ChebyshevNet.Result net)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
                if (net.Valid[i, j]) yield return (i, j);
    }

    private static IEnumerable<(int i, int j)> Cells(ChebyshevNet.Result net)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        for (int i = 0; i + 1 < nu; i++)
            for (int j = 0; j + 1 < nv; j++)
                if (net.Valid[i, j] && net.Valid[i + 1, j] && net.Valid[i + 1, j + 1] && net.Valid[i, j + 1])
                    yield return (i, j);
    }

    /// <summary>Area of the spherical triangle abc on the unit sphere (Van Oosterom &amp; Strackee 1983).</summary>
    private static double SphericalTriangle(Vec3d a, Vec3d b, Vec3d c)
    {
        a = a.Normalized(); b = b.Normalized(); c = c.Normalized();
        double num = Math.Abs(Vec3d.Dot(a, Vec3d.Cross(b, c)));
        double den = 1 + Vec3d.Dot(a, b) + Vec3d.Dot(b, c) + Vec3d.Dot(c, a);
        return 2 * Math.Atan2(num, den);
    }

    private static double SphericalQuad(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3) =>
        0.5 * (SphericalTriangle(p0, p1, p2) + SphericalTriangle(p0, p2, p3) +
               SphericalTriangle(p1, p2, p3) + SphericalTriangle(p1, p3, p0));

    [Theory]
    [InlineData(90.0)]
    [InlineData(60.0)]
    [InlineData(40.0)]
    public void Plane_AnglesAreTheSeedAngle_IntegralIsZero(double angleDeg)
    {
        // On the plane the compass method reproduces the rhombic lattice exactly:
        // the parallelogram guess pA + pB − pD is already the intersection.
        var grid = TestMeshes.CreateQuadGrid(20, 20, 0.5).ToTriangulated();
        int center = Nearest(grid, new Vec3d(5, 5, 0));
        var net = ChebyshevNet.Compute(grid, center, new Vec3d(1, 0, 0),
            new ChebyshevNet.Options { EdgeLength = 0.4, CountU = 5, CountV = 5, Angle = angleDeg * Math.PI / 180 });

        var angles = ChebyshevNet.ShearAngles(net);
        var nodes = Nodes(net).ToList();
        Assert.Equal(11 * 11, nodes.Count);
        double maxAngleErr = nodes.Max(n => Math.Abs(angles[n.i, n.j] - angleDeg));
        var k = ChebyshevNet.CellCurvature(net);
        var cells = Cells(net).ToList();
        Assert.Equal(10 * 10, cells.Count);
        double maxK = cells.Max(c => Math.Abs(k[c.i, c.j]));
        double area = cells.Sum(c => ChebyshevNet.CellArea(net, c.i, c.j));
        double exactArea = 100 * 0.4 * 0.4 * Math.Sin(angleDeg * Math.PI / 180);
        _out.WriteLine($"plane {angleDeg}°: max |ω − {angleDeg}| = {maxAngleErr:E2}°, max |Hazzidakis| = {maxK:E2}, area {area:F10} vs {exactArea:F10}");

        Assert.True(maxAngleErr < 1e-7, $"angles off by {maxAngleErr}");
        Assert.True(maxK < 1e-9, $"Hazzidakis integral {maxK} on a plane");
        Assert.True(Math.Abs(area - exactArea) < 1e-9 * exactArea);

        // Garg's material limit: a lattice sheared past 45° is locked everywhere, a square one nowhere
        int locked = ChebyshevNet.CountLocked(net, 45.0);
        if (angleDeg < 45) Assert.Equal(nodes.Count, locked);
        else Assert.Equal(0, locked);
        Assert.Equal(0, ChebyshevNet.CountLocked(net, angleDeg - 1e-6));
    }

    private (double total, double median, double p90, double worst, double kMin, double kMax, int cells) SphereCells(int subdivisions, double L, int count)
    {
        var sphere = TestMeshes.CreateUnitSphere(subdivisions);
        int seed = Nearest(sphere, new Vec3d(1, 0, 0));
        var net = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 0, 1),
            new ChebyshevNet.Options { EdgeLength = L, CountU = count, CountV = count });
        var k = ChebyshevNet.CellCurvature(net);
        var cells = Cells(net).ToList();
        double sumK = 0, sumArea = 0, kMin = double.MaxValue, kMax = double.MinValue;
        var rel = new List<double>();
        foreach (var (i, j) in cells)
        {
            double a = SphericalQuad(net.Points[i, j], net.Points[i + 1, j], net.Points[i + 1, j + 1], net.Points[i, j + 1]);
            double h = k[i, j];
            Assert.True(h > 0, $"cell ({i},{j}) reads K ≤ 0 on a sphere: {h}");
            rel.Add(Math.Abs(h - a) / a);
            double kEst = h / ChebyshevNet.CellArea(net, i, j);
            kMin = Math.Min(kMin, kEst); kMax = Math.Max(kMax, kEst);
            sumK += h; sumArea += a;
        }
        rel.Sort();
        var r = ((sumK - sumArea) / sumArea, rel[rel.Count / 2], rel[(int)(0.9 * rel.Count)], rel[rel.Count - 1], kMin, kMax, cells.Count);
        _out.WriteLine($"sphere {subdivisions} × {2 * subdivisions}, L = {L}: {cells.Count} cells, Σ Hazzidakis {sumK:F6} vs Σ spherical area {sumArea:F6} ({100 * r.Item1:+0.000;-0.000} %), " +
                       $"cell error median {100 * r.Item2:F2} % · 90th {100 * r.Item3:F2} % · worst {100 * r.Item4:F2} %, K estimate {kMin:F4} … {kMax:F4}");
        return r;
    }

    [Fact]
    public void Sphere_HazzidakisIntegralIsTheSphericalArea()
    {
        // K = 1: the Hazzidakis integral of each cell is the area of the cell.
        // Compared with the spherical quad through the four nodes (the cell's
        // sides are Chebyshev lines, not great circles: the two differ by O(L)
        // relative, so the agreement must improve with L — same net radius,
        // L halved, twice the count). The total telescopes to the four corner
        // angles of the net and is the sharper check. The nodes sit on the
        // facets, so the mesh must be fine against L: the facet sagitta
        // e²/8 moves the chord angles by e²/(8L), which must stay well
        // below K·L² — the 24 × 48 sphere with L = 0.2 reads 3.4 % median,
        // 96 × 192 reads 1.5 %.
        var coarse = SphereCells(96, 0.2, 5);
        var fine = SphereCells(144, 0.1, 10);
        Assert.True(coarse.cells >= 90 && fine.cells >= 360);
        Assert.True(Math.Abs(coarse.total) < 0.01, $"total off by {coarse.total}");
        Assert.True(coarse.median < 0.02 && coarse.p90 < 0.04 && coarse.worst < 0.15, $"cells {coarse.median} / {coarse.p90} / {coarse.worst}");
        Assert.InRange(coarse.kMin, 0.9, 1.1);
        Assert.InRange(coarse.kMax, 0.9, 1.15);
        Assert.True(Math.Abs(fine.total) < 0.5 * Math.Abs(coarse.total), $"total not converging: {coarse.total} → {fine.total}");
        Assert.True(fine.median < 0.8 * coarse.median, $"cells not converging: {coarse.median} → {fine.median}");
        Assert.True(fine.worst < 0.5 * coarse.worst, $"rim not converging: {coarse.worst} → {fine.worst}");
        Assert.InRange(fine.kMin, 0.95, 1.05);
        Assert.InRange(fine.kMax, 0.95, 1.05);
    }

    [Fact]
    public void Sphere_AnglesFollowSineGordon()
    {
        // ω_uv = −sin ω (K = 1) with ω = 90° on both geodesic axes: the Goursat
        // series starts ω = π/2 − uv + …, so ω(L, L) = 90° − L² radians.
        var sphere = TestMeshes.CreateUnitSphere(96);
        int seed = Nearest(sphere, new Vec3d(1, 0, 0));
        double L = 0.2;
        var net = ChebyshevNet.Compute(sphere, seed, new Vec3d(0, 0, 1),
            new ChebyshevNet.Options { EdgeLength = L, CountU = 5, CountV = 5 });
        var angles = ChebyshevNet.ShearAngles(net);
        int ou = net.OriginU, ov = net.OriginV;
        double w11 = angles[ou + 1, ov + 1], predicted = 90 - L * L * 180 / Math.PI;
        double axisErr = Math.Max(Math.Abs(angles[ou, ov] - 90), Math.Max(Math.Abs(angles[ou + 3, ov] - 90), Math.Abs(angles[ou, ov + 3] - 90)));
        double minW = Nodes(net).Min(n => angles[n.i, n.j]), maxW = Nodes(net).Max(n => angles[n.i, n.j]);
        _out.WriteLine($"ω(seed) = {angles[ou, ov]:F3}°, axes within {axisErr:F3}° of 90°, ω(1,1) = {w11:F3}° (series {predicted:F3}°), range {minW:F2}° … {maxW:F2}°");
        Assert.True(axisErr < 0.3, $"axis angles off by {axisErr}°");
        Assert.True(Math.Abs(w11 - predicted) < 0.3, $"ω(1,1) {w11} vs {predicted}");
        // the net closes in the two "diagonal" quadrants and opens in the other two,
        // symmetrically: ω(a, b) + ω(a, −b) ≈ 180°
        double asym = 0;
        for (int a = -5; a <= 5; a++) for (int b = 1; b <= 5; b++)
            if (net.Valid[ou + a, ov + b] && net.Valid[ou + a, ov - b]) asym = Math.Max(asym, Math.Abs(angles[ou + a, ov + b] + angles[ou + a, ov - b] - 180));
        _out.WriteLine($"ω(a,b) + ω(a,−b) − 180° within {asym:F3}°");
        Assert.True(asym < 0.5, $"asymmetry {asym}°");
        // one radian out along both axes the far corners have sheared past Garg's 45° limit
        int locked45 = ChebyshevNet.CountLocked(net, 45.0);
        int nodes = Nodes(net).Count();
        _out.WriteLine($"{locked45} of {nodes} nodes outside [45°, 135°], {ChebyshevNet.CountLocked(net, 30.0)} outside [30°, 150°]");
        Assert.True(locked45 > 0 && locked45 < nodes / 4, $"{locked45} locked of {nodes}");
        Assert.Equal(0, ChebyshevNet.CountLocked(net, 33.0));
        Assert.InRange(minW, 30, 90.0001);
        Assert.InRange(maxW, 89.9999, 150);
    }

    [Theory]
    [InlineData(64, 1.5, 6e-3)]
    [InlineData(256, 0.12, 4e-4)]
    public void Cylinder_FlatNet_SquareCells(int segments, double angleTol, double kTol)
    {
        // K = 0: the net is the unrolled square lattice — every angle 90°, every
        // Hazzidakis integral 0. What remains is the faceting of the n-gon
        // (sagitta e²/8 against the chord L = 0.3): 64 segments read ±1.2°,
        // 256 segments 16× less.
        var cyl = TestMeshes.CreateCylinder(1.0, 4.0, segments, 32);
        int seed = Nearest(cyl, new Vec3d(1, 0, 0));
        var net = ChebyshevNet.Compute(cyl, seed, new Vec3d(0, 0, 1),
            new ChebyshevNet.Options { EdgeLength = 0.3, CountU = 4, CountV = 4 });

        var angles = ChebyshevNet.ShearAngles(net);
        var k = ChebyshevNet.CellCurvature(net);
        var nodes = Nodes(net).ToList();
        var cells = Cells(net).ToList();
        Assert.Equal(81, nodes.Count);
        Assert.Equal(64, cells.Count);
        double maxAngleErr = nodes.Max(n => Math.Abs(angles[n.i, n.j] - 90));
        double maxK = cells.Max(c => Math.Abs(k[c.i, c.j]));
        double sumK = cells.Sum(c => k[c.i, c.j]);
        _out.WriteLine($"cylinder {segments}-gon: max |ω − 90°| = {maxAngleErr:F4}°, max |Hazzidakis| = {maxK:E2}, Σ = {sumK:E2} over {cells.Count} cells (area {cells.Sum(c => ChebyshevNet.CellArea(net, c.i, c.j)):F4})");
        Assert.True(maxAngleErr < angleTol, $"angles off by {maxAngleErr}°");
        Assert.True(maxK < kTol, $"Hazzidakis {maxK} on a developable");
        Assert.True(Math.Abs(sumK) < 2 * kTol);
        Assert.Equal(0, ChebyshevNet.CountLocked(net, 45.0));
    }

    private (double total, double median, double worst, int cells, int negative, double minW, double maxW) SaddleCells(double L, int count)
    {
        var saddle = TestMeshes.CreateSaddle(160, 2.0);
        int seed = Nearest(saddle, new Vec3d(0, 0, 0));
        var net = ChebyshevNet.Compute(saddle, seed, new Vec3d(1, 0, 0),
            new ChebyshevNet.Options { EdgeLength = L, CountU = count, CountV = count });
        var k = ChebyshevNet.CellCurvature(net);
        var angles = ChebyshevNet.ShearAngles(net);
        var cells = Cells(net).ToList();
        int negative = cells.Count(c => k[c.i, c.j] < 0);
        double sumK = cells.Sum(c => k[c.i, c.j]);
        double sumExact = 0;
        var rel = new List<double>();
        foreach (var (i, j) in cells)
        {
            // ∬ K dA over the plan projection of the cell (straight sides, like the sphere's geodesic quad):
            // bilinear map of the unit square, 24 × 24 midpoint rule, K dA = −4 dx dy / (1 + 4x² + 4y²)^(3/2)
            Vec3d p0 = net.Points[i, j], p1 = net.Points[i + 1, j], p2 = net.Points[i + 1, j + 1], p3 = net.Points[i, j + 1];
            double exact = 0; const int n = 24;
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++)
            {
                double xi = (a + 0.5) / n, eta = (b + 0.5) / n;
                double x = (1 - xi) * (1 - eta) * p0.X + xi * (1 - eta) * p1.X + xi * eta * p2.X + (1 - xi) * eta * p3.X;
                double y = (1 - xi) * (1 - eta) * p0.Y + xi * (1 - eta) * p1.Y + xi * eta * p2.Y + (1 - xi) * eta * p3.Y;
                double dxdxi = (1 - eta) * (p1.X - p0.X) + eta * (p2.X - p3.X), dydxi = (1 - eta) * (p1.Y - p0.Y) + eta * (p2.Y - p3.Y);
                double dxdeta = (1 - xi) * (p3.X - p0.X) + xi * (p2.X - p1.X), dydeta = (1 - xi) * (p3.Y - p0.Y) + xi * (p2.Y - p1.Y);
                double jac = Math.Abs(dxdxi * dydeta - dxdeta * dydxi) / (n * n);
                double g = 1 + 4 * (x * x + y * y);
                exact += -4 / Math.Pow(g, 1.5) * jac;
            }
            sumExact += exact;
            rel.Add(Math.Abs(k[i, j] - exact) / Math.Abs(exact));
        }
        rel.Sort();
        var nodes = Nodes(net).ToList();
        var r = ((sumK - sumExact) / sumExact, rel[rel.Count / 2], rel[rel.Count - 1], cells.Count, negative,
                 nodes.Min(nd => angles[nd.i, nd.j]), nodes.Max(nd => angles[nd.i, nd.j]));
        _out.WriteLine($"saddle 160 × 160, L = {L}: {negative}/{cells.Count} cells negative, Σ Hazzidakis {sumK:F5} vs ∬K dA {sumExact:F5} ({100 * r.Item1:+0.00;-0.00} %), " +
                       $"cell error median {100 * r.Item2:F2} % · worst {100 * r.Item3:F1} % (rim), angles {r.Item6:F1}° … {r.Item7:F1}°");
        return r;
    }

    [Fact]
    public void Saddle_NegativeCurvature_AnglesOpenAndClose()
    {
        // z = x² − y², K = −4/(1 + 4x² + 4y²)²: every cell reads K < 0 and the
        // integral over each cell approaches ∬K dA of its plan projection. The
        // worst cells sit on the rim (one-sided stencils); they converge too.
        var coarse = SaddleCells(0.15, 4);
        var fine = SaddleCells(0.075, 8);
        Assert.Equal(coarse.cells, coarse.negative);
        Assert.Equal(fine.cells, fine.negative);
        Assert.True(Math.Abs(coarse.total) < 0.04, $"total {coarse.total}");
        Assert.True(coarse.median < 0.05, $"median cell {coarse.median}");
        Assert.True(coarse.worst < 0.12, $"worst cell {coarse.worst}");
        Assert.True(fine.median < 0.6 * coarse.median, $"median not converging: {coarse.median} → {fine.median}");
        Assert.True(fine.worst < 0.75 * coarse.worst, $"rim not converging: {coarse.worst} → {fine.worst}");
        // the same net, the same angles: the opening/closing is a property of the surface, not of L
        Assert.True(Math.Abs(coarse.minW - fine.minW) < 1.5 && Math.Abs(coarse.maxW - fine.maxW) < 1.5);
    }
}
