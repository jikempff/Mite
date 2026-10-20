using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Streamlines;

namespace Mite.Tests;

/// <summary>
/// The triaxial ellipsoid as a test typology: exactly four umbilics (lemons,
/// index +½) at closed-form positions, closed-form K and H, lines of
/// curvature on the confocal quadrics (Dupin), and the ellipse y = 0 as the
/// union of the umbilics and their separatrices (Garcia &amp; Sotomayor 2009).
/// </summary>
public class EllipsoidTests
{
    private readonly ITestOutputHelper _out;
    public EllipsoidTests(ITestOutputHelper output) { _out = output; }

    const double A = 1.5, B = 1.0, C = 0.7;

    static MeshData Ellipsoid(int n) => AnalyticShapes.Build("ellipsoid", A, B, C, n);

    static double MeanEdge(MeshData m)
    {
        var e = m.BuildEdges(); double s = 0;
        foreach (var (v0, v1) in e) s += (m.Vertices[v0] - m.Vertices[v1]).Length;
        return s / e.Length;
    }

    [Fact]
    public void ClosedForms_AgreeWithEachOther()
    {
        // the umbilics lie on the surface, have k1 = k2 = ac/b³ and K = H² there;
        // elsewhere k1 k2 = K and (k1 + k2)/2 = H from the two independent formulas
        var um = TestMeshes.EllipsoidUmbilics(A, B, C);
        Assert.Equal(4, um.Length);
        double ku = AnalyticShapes.EllipsoidUmbilicCurvature(A, B, C);
        foreach (var u in um)
        {
            Assert.Equal(1.0, u.X * u.X / (A * A) + u.Y * u.Y / (B * B) + u.Z * u.Z / (C * C), 12);
            AnalyticShapes.EllipsoidPrincipal(u, A, B, C, out double k1, out double k2, out _, out _, out _);
            Assert.Equal(ku, k1, 12); Assert.Equal(ku, k2, 12);
            var (K, H) = AnalyticShapes.EllipsoidCurvature(u, A, B, C);
            Assert.Equal(ku * ku, K, 12); Assert.Equal(ku, H, 12);
            var (l1, l2) = AnalyticShapes.EllipsoidConfocal(u, A, B, C);
            Assert.Equal(B * B, l1, 9); Assert.Equal(B * B, l2, 9); // both confocal coordinates reach b² only at the umbilics
        }
        var rnd = new Random(5);
        for (int i = 0; i < 500; i++)
        {
            double u = 2 * Math.PI * rnd.NextDouble(), v = Math.Acos(2 * rnd.NextDouble() - 1);
            var p = new Vec3d(A * Math.Sin(v) * Math.Cos(u), B * Math.Sin(v) * Math.Sin(u), C * Math.Cos(v));
            AnalyticShapes.EllipsoidPrincipal(p, A, B, C, out double k1, out double k2, out Vec3d d1, out Vec3d d2, out Vec3d n);
            var (K, H) = AnalyticShapes.EllipsoidCurvature(p, A, B, C);
            Assert.Equal(K, k1 * k2, 10);
            Assert.Equal(H, 0.5 * (k1 + k2), 10);
            Assert.True(k1 >= k2 && k2 > 0);
            Assert.Equal(0.0, Vec3d.Dot(d1, n), 12); Assert.Equal(0.0, Vec3d.Dot(d1, d2), 12);
            // Dupin: the principal directions are tangent to the confocal quadrics through p
            var (l1, l2) = AnalyticShapes.EllipsoidConfocal(p, A, B, C);
            Assert.Equal(1.0, p.X * p.X / (A * A - l1) + p.Y * p.Y / (B * B - l1) + p.Z * p.Z / (C * C - l1), 9);
            Assert.Equal(1.0, p.X * p.X / (A * A - l2) + p.Y * p.Y / (B * B - l2) + p.Z * p.Z / (C * C - l2), 9);
            Vec3d gq1 = new Vec3d(p.X / (A * A - l1), p.Y / (B * B - l1), p.Z / (C * C - l1)).Normalized();
            Vec3d gq2 = new Vec3d(p.X / (A * A - l2), p.Y / (B * B - l2), p.Z / (C * C - l2)).Normalized();
            // the line of curvature on the quadric λ is tangent to both surfaces: ⊥ n and ⊥ ∇Q_λ
            if (Math.Abs(p.Y) > 0.05 && Math.Abs(p.X) > 0.05 && Math.Abs(p.Z) > 0.05)
            {
                Vec3d along1 = Vec3d.Cross(n, gq1).Normalized(), along2 = Vec3d.Cross(n, gq2).Normalized();
                // which family keeps which coordinate: the k1 (max) line keeps λ₂, the k2 (min) line keeps λ₁
                Assert.True(Math.Abs(Vec3d.Dot(along2, d1)) > 1 - 1e-6, $"max-curvature direction vs λ₂-line at {p}: {Vec3d.Dot(along2, d1)}");
                Assert.True(Math.Abs(Vec3d.Dot(along1, d2)) > 1 - 1e-6, $"min-curvature direction vs λ₁-line at {p}: {Vec3d.Dot(along1, d2)}");
            }
        }
    }

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    public void MeshCurvature_MatchesClosedForm(int n)
    {
        var mesh = Ellipsoid(n);
        var pc = PrincipalCurvature.Compute(mesh);
        var K = GaussianCurvature.Compute(mesh);
        var H = MeanCurvature.Compute(mesh).Values;
        double errK = 0, errH = 0, errk1 = 0, errk2 = 0, dirErr = 0; int cnt = 0;
        var um = TestMeshes.EllipsoidUmbilics(A, B, C);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Vertices[i];
            if (Math.Abs(p.Z) > 0.95 * C) continue; // pole fans
            var (Kt, Ht) = AnalyticShapes.EllipsoidCurvature(p, A, B, C);
            AnalyticShapes.EllipsoidPrincipal(p, A, B, C, out double k1, out double k2, out Vec3d d1, out _, out _);
            errK = Math.Max(errK, Math.Abs(K[i] - Kt) / Kt);
            errH = Math.Max(errH, Math.Abs(H[i] - Ht) / Ht);
            errk1 = Math.Max(errk1, Math.Abs(pc.K1[i] - k1) / k1);
            errk2 = Math.Max(errk2, Math.Abs(pc.K2[i] - k2) / k2);
            double du = um.Min(u => (u - p).Length);
            if (du > 0.3 && k1 - k2 > 0.3)
            {
                dirErr = Math.Max(dirErr, Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(pc.D1[i], d1)))) * 180 / Math.PI);
                cnt++;
            }
        }
        _out.WriteLine($"n={n}: max rel err K {errK:E2}, H {errH:E2}, k1 {errk1:E2}, k2 {errk2:E2}; max direction error {dirErr:F3}° over {cnt} vertices away from the umbilics");
        double tol = n >= 96 ? 0.02 : 0.05;
        Assert.True(errK < tol, $"K {errK}");
        Assert.True(errH < tol, $"H {errH}");
        Assert.True(errk1 < 3 * tol && errk2 < 3 * tol, $"k1 {errk1} k2 {errk2}");
        Assert.True(dirErr < 3.0, $"direction {dirErr}°");
    }

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    [InlineData(144)]
    public void Locate_FindsExactlyFourLemons(int n)
    {
        var mesh = Ellipsoid(n);
        var pc = PrincipalCurvature.Compute(mesh);
        double edge = MeanEdge(mesh);
        var raw = Umbilics.Locate(mesh, pc);
        var pts = Umbilics.Locate(mesh, pc, 2 * edge);
        var um = TestMeshes.EllipsoidUmbilics(A, B, C);
        _out.WriteLine($"n={n}, edge {edge:F4}: raw {raw.Length} points (sum {Umbilics.IndexSum(raw)}), after cancelling pairs within 2 edges {pts.Length} (sum {Umbilics.IndexSum(pts)})");
        foreach (var d in raw) _out.WriteLine($"  raw {d.Point} index {d.Index} δ {d.Delta:E2} contrast {d.Contrast:F3} nearest umbilic {um.Min(u => (u - d.Point).Length) / edge:F2} edges");
        Assert.Equal(2.0, Umbilics.IndexSum(raw), 12); // Poincaré–Hopf for the line field: χ = 2
        Assert.Equal(4, pts.Length);
        Assert.All(pts, d => Assert.Equal(0.5, d.Index));
        var dist = um.Select(u => pts.Min(d => (d.Point - u).Length) / edge).ToArray();
        _out.WriteLine($"  distance of each analytic umbilic to its nearest located one: {string.Join(", ", dist.Select(x => x.ToString("F3")))} edges");
        Assert.All(dist, x => Assert.True(x < 1.0, $"umbilic {x} edges off"));
        // one located point per umbilic
        Assert.Equal(4, um.Select(u => pts.Select((d, i) => (d, i)).OrderBy(t => (t.d.Point - u).Length).First().i).Distinct().Count());
    }

    [Fact]
    public void Locate_TorusHasNoUmbilics_IndexSumZero()
    {
        var torus = TestMeshes.CreateTorus(3.0, 1.0, 64, 32);
        var pc = PrincipalCurvature.Compute(torus);
        var pts = Umbilics.Locate(torus, pc);
        Assert.Empty(pts); // k1 = 1 > k2 ∈ [−0.5, 0.25] everywhere
        Assert.Equal(0.0, Umbilics.IndexSum(pts));
    }

    [Fact]
    public void Locate_MonkeySaddle_StarAtTheFlatPoint()
    {
        // z = x³ − 3xy²: K = 0 and k1 = k2 = 0 at the origin — a star (index −½), the
        // three asymptotic directions. Elsewhere K < 0 so k1 ≠ k2.
        var mesh = AnalyticShapes.Build("monkey", 2.0, 1.0, 0, 48);
        var pc = PrincipalCurvature.Compute(mesh);
        double edge = MeanEdge(mesh);
        var pts = Umbilics.Locate(mesh, pc, 2 * edge);
        _out.WriteLine($"monkey saddle: {pts.Length} points: {string.Join("; ", pts.Select(d => d.Point + " " + d.Index))}");
        Assert.Single(pts);
        Assert.Equal(-0.5, pts[0].Index);
        Assert.True(pts[0].Point.Length < 0.5 * edge, $"star {pts[0].Point.Length / edge} edges from the origin");
    }

    [Fact]
    public void Locate_Saddle_NoUmbilic()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var pts = Umbilics.Locate(saddle, pc);
        Assert.Empty(pts);
    }

    [Fact]
    public void Locate_EllipsoidOfRevolution_IndexTwoAtThePoles()
    {
        // a = b: the two poles are non-generic umbilics of index 1 each; a discrete
        // field resolves each as wedges near the pole whose indices still sum to 2
        var mesh = AnalyticShapes.Build("ellipsoid", 1.0, 1.0, 0.6, 96);
        var pc = PrincipalCurvature.Compute(mesh);
        var pts = Umbilics.Locate(mesh, pc);
        _out.WriteLine($"revolution: {pts.Length} points, sum {Umbilics.IndexSum(pts)}; |z| of the points: {string.Join(", ", pts.Select(d => Math.Abs(d.Point.Z).ToString("F3")))}");
        Assert.Equal(2.0, Umbilics.IndexSum(pts), 12);
        Assert.All(pts, d => Assert.True(Math.Abs(d.Point.Z) > 0.55, $"a degenerate point away from the poles at {d.Point}"));
    }

    [Fact]
    public void Streamlines_StayOnConfocalQuadrics()
    {
        // Dupin: a line of curvature is the intersection with one confocal quadric,
        // λ₂ = const for the max-curvature family, λ₁ = const for the min-curvature
        // family. The traced line is compared with the exact line through its seed
        // by the lateral offset |λ − λ_seed| / |∇_S λ| at every point (λ itself is a
        // poor yardstick: its gradient varies by an order of magnitude over the
        // surface). Seeds at several distances from the umbilics, where the lines of
        // both families bunch (the lemon).
        var mesh = Ellipsoid(96);
        var pc = PrincipalCurvature.Compute(mesh);
        double edge = MeanEdge(mesh);
        var um = TestMeshes.EllipsoidUmbilics(A, B, C);
        Vec3d On(Vec3d q) { double s2 = Math.Sqrt(q.X * q.X / (A * A) + q.Y * q.Y / (B * B) + q.Z * q.Z / (C * C)); return q / s2; }
        foreach (bool max in new[] { true, false })
        {
            double Lam(Vec3d q) { var (l1, l2) = AnalyticShapes.EllipsoidConfocal(q, A, B, C); return max ? l2 : l1; }
            double range = max ? A * A - B * B : B * B - C * C;
            foreach (double margin in new[] { 0.3, 0.5, 0.7, 0.9 })
            {
                // the vertex whose kept coordinate is nearest the margin (fraction of its range from b²)
                int seed = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.Vertices[i].Y > 0.3 && Math.Abs(mesh.Vertices[i].Z) < 0.5)
                    .OrderBy(i => Math.Abs((max ? Lam(mesh.Vertices[i]) - B * B : B * B - Lam(mesh.Vertices[i])) / range - margin)).First();
                var l = CurvatureStreamlines.Trace(mesh, new[] { seed }, pc, new CurvatureStreamlines.Options { UseMaxCurvature = max, MaxSteps = 4000 })[0];
                double lamSeed = Lam(l[Enumerable.Range(0, l.Length).OrderBy(i => (l[i] - mesh.Vertices[seed]).Length).First()]);
                double worst = 0, sum = 0, otherMin = double.MaxValue, otherMax = double.MinValue;
                foreach (var p in l)
                {
                    AnalyticShapes.EllipsoidPrincipal(p, A, B, C, out _, out _, out Vec3d d1, out Vec3d d2, out _);
                    const double h = 1e-5;
                    double g1 = (Lam(On(p + h * d1)) - Lam(On(p - h * d1))) / (2 * h), g2 = (Lam(On(p + h * d2)) - Lam(On(p - h * d2))) / (2 * h);
                    double off = Math.Abs(Lam(p) - lamSeed) / Math.Max(Math.Sqrt(g1 * g1 + g2 * g2), 1e-9);
                    worst = Math.Max(worst, off); sum += off;
                    var (o1, o2) = AnalyticShapes.EllipsoidConfocal(p, A, B, C);
                    double other = max ? o1 : o2;
                    otherMin = Math.Min(otherMin, other); otherMax = Math.Max(otherMax, other);
                }
                bool closed = (l[0] - l[^1]).Length < 1e-9;
                double nearest = l.Min(p => um.Min(u => (u - p).Length));
                double otherSpread = (otherMax - otherMin) / (max ? B * B - C * C : A * A - B * B);
                _out.WriteLine($"{(max ? "max" : "min")} line at margin {margin}: {l.Length} pts, closed {closed}, lateral offset from the exact line worst {worst / edge:F2} / mean {sum / l.Length / edge:F2} edges, nearest umbilic {nearest / edge:F1} edges, sweeps {otherSpread * 100:F0} % of the other coordinate");
                Assert.True(closed, "every line of curvature of the ellipsoid but the four umbilic connections is closed (Monge)");
                Assert.True(worst < 1.0 * edge, $"traced line {worst / edge:F2} edges off the exact line of curvature");
                Assert.True(sum / l.Length < 0.3 * edge, $"mean offset {sum / l.Length / edge:F2} edges");
                Assert.True(otherSpread > 0.9, "the line should sweep the other coordinate");
            }
        }
    }

    [Fact]
    public void Streamline_UmbilicConnection_RunsAlongTheEllipseToTheUmbilic()
    {
        // the ellipse y = 0 is a max-curvature line for |x| > x0 (between the
        // umbilic and the end of the long axis) and a min-curvature line for
        // |x| < x0; the max line seeded at (a, 0, 0) follows it and leaves it at
        // the umbilic (Garcia & Sotomayor 2009, Prop. 4: four umbilic connections)
        int n = 96;
        var mesh = Ellipsoid(n);
        var pc = PrincipalCurvature.Compute(mesh);
        double edge = MeanEdge(mesh);
        var um = TestMeshes.EllipsoidUmbilics(A, B, C);
        int seed = Enumerable.Range(0, mesh.VertexCount).OrderBy(i => (mesh.Vertices[i] - new Vec3d(A, 0, 0)).Length).First();
        Assert.True(Math.Abs(mesh.Vertices[seed].Y) < 1e-9);
        var line = CurvatureStreamlines.Trace(mesh, new[] { seed }, pc, new CurvatureStreamlines.Options { UseMaxCurvature = true, MaxSteps = 4000 })[0];
        double z0 = um[0].Z;
        // walk from the seed both ways until the line leaves the ellipse y = 0 (by half an edge)
        int at = Enumerable.Range(0, line.Length).OrderBy(i => (line[i] - new Vec3d(A, 0, 0)).Length).First();
        double maxY = 0; int inside = 0; var leaveZ = new List<double>();
        foreach (int dir in new[] { 1, -1 })
        {
            double lastZ = 0;
            for (int i = at; i >= 0 && i < line.Length; i += dir)
            {
                if (Math.Abs(line[i].Y) > 0.5 * edge) break;
                lastZ = Math.Abs(line[i].Z);
                if (lastZ < z0 - edge) { maxY = Math.Max(maxY, Math.Abs(line[i].Y)); inside++; }
            }
            leaveZ.Add(lastZ);
        }
        _out.WriteLine($"umbilic connection: {line.Length} points, {inside} between the umbilics, max |y| there {maxY:E2} ({maxY / edge:F3} edges); leaves the ellipse at |z| = {leaveZ[0]:F3} / {leaveZ[1]:F3} (umbilic at {z0:F3}, edge {edge:F3})");
        Assert.True(inside > 10);
        Assert.True(maxY < 0.1 * edge, $"left the ellipse by {maxY / edge} edges before the umbilic");
        Assert.All(leaveZ, z => Assert.True(Math.Abs(z - z0) < 1.5 * edge, $"leaves the ellipse at |z| = {z}, {(z - z0) / edge} edges from the umbilic"));
    }
}
