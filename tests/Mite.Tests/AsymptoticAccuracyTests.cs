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
/// Closed-form asymptotic curves on the catenoid x = c cosh(v/c) (cos u, sin u), z = v.
/// In these isothermal coordinates (E = G = cosh² v, F = 0) the second
/// fundamental form is −du² + dv² (c = 1), so the asymptotic curves are the
/// straight lines u ± v = const of the parameter plane: they cross every
/// meridian at exactly 45° and each other at exactly 90° (minimal surface).
/// The exact distance of a point from the asymptotic curve u − v = k is
/// |Δ| cosh v / √2 with Δ = (u − v) − k, which makes a rigorous error metric
/// for any traced curve. (Do Carmo, Differential Geometry of Curves and
/// Surfaces, §3-2 / §3-3; Struik, Lectures on Classical Differential
/// Geometry, §2-6.)
/// </summary>
public class AsymptoticAccuracyTests
{
    private readonly ITestOutputHelper _out;
    public AsymptoticAccuracyTests(ITestOutputHelper output) { _out = output; }

    private static (double u, double v) Param(Vec3d p) => (Math.Atan2(p.Y, p.X), p.Z);

    /// <summary>Exact unit asymptotic directions at a point (both families).</summary>
    private static (Vec3d a, Vec3d b) ExactDirections(Vec3d p)
    {
        var (u, v) = Param(p);
        double ch = Math.Cosh(v), sh = Math.Sinh(v);
        var ru = new Vec3d(-ch * Math.Sin(u), ch * Math.Cos(u), 0);
        var rv = new Vec3d(sh * Math.Cos(u), sh * Math.Sin(u), 1);
        return ((ru + rv).Normalized(), (ru - rv).Normalized());
    }

    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    /// <summary>
    /// Distance of every point of a traced curve from the exact asymptotic
    /// curve through its first point, in model units (worst and RMS), and the
    /// worst angle between consecutive segments and the exact direction.
    /// </summary>
    internal static (double worst, double rms, double worstAngleDeg) CurveError(Vec3d[] line)
    {
        var (u0, v0) = Param(line[0]);
        // which family: compare the first segment with the exact directions
        var (ea, eb) = ExactDirections(line[0]);
        Vec3d t0 = (line[Math.Min(3, line.Length - 1)] - line[0]).Normalized();
        bool familyA = Math.Abs(Vec3d.Dot(t0, ea)) >= Math.Abs(Vec3d.Dot(t0, eb));
        double k = familyA ? u0 - v0 : u0 + v0;
        double worst = 0, sum = 0; int n = 0; double worstAng = 0;
        for (int i = 0; i < line.Length; i++)
        {
            var (u, v) = Param(line[i]);
            double delta = Wrap((familyA ? u - v : u + v) - k);
            double d = Math.Abs(delta) * Math.Cosh(v) / Math.Sqrt(2.0);
            worst = Math.Max(worst, d); sum += d * d; n++;
            if (i > 0)
            {
                Vec3d t = (line[i] - line[i - 1]).Normalized();
                var (xa, xb) = ExactDirections(0.5 * (line[i] + line[i - 1]));
                Vec3d e = familyA ? xa : xb;
                double ang = Math.Acos(Math.Min(1.0, Math.Abs(Vec3d.Dot(t, e)))) * 180 / Math.PI;
                worstAng = Math.Max(worstAng, ang);
            }
        }
        return (worst, Math.Sqrt(sum / Math.Max(1, n)), worstAng);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(128, 64)]
    public void Catenoid_AsymptoticDirections_At45DegreesToMeridians(int segments, int rows)
    {
        var mesh = TestMeshes.CreateCatenoid(1.0, 2.0, segments, rows);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double worst = 0, sum = 0; int n = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (!field.Exists[i]) continue;
            var (ea, eb) = ExactDirections(mesh.Vertices[i]);
            double best = Math.Max(Math.Abs(Vec3d.Dot(field.Family1[i], ea)), Math.Abs(Vec3d.Dot(field.Family1[i], eb)));
            double ang = Math.Acos(Math.Min(1.0, best)) * 180 / Math.PI;
            worst = Math.Max(worst, ang); sum += ang; n++;
        }
        _out.WriteLine($"catenoid {segments}x{rows}: direction error mean {sum / n:F3}°, worst {worst:F3}° over {n} vertices");
        // error by row (z band)
        var byRow = new double[rows + 1];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (!field.Exists[i]) continue;
            var (ea, eb) = ExactDirections(mesh.Vertices[i]);
            double best = Math.Max(Math.Abs(Vec3d.Dot(field.Family1[i], ea)), Math.Abs(Vec3d.Dot(field.Family1[i], eb)));
            byRow[i / segments] = Math.Max(byRow[i / segments], Math.Acos(Math.Min(1.0, best)) * 180 / Math.PI);
        }
        _out.WriteLine("worst per row: " + string.Join(" ", byRow.Select(x => x.ToString("F2"))));
        // before the border repair: mean 0.4–0.5°, worst 7.5–8.1° (the three border rows)
        Assert.True(worst < 1.0, $"worst asymptotic direction error {worst:F2}°");
        Assert.True(sum / n < 0.15, $"mean asymptotic direction error {sum / n:F3}°");
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(128, 64)]
    public void Catenoid_TracedAsymptoticCurve_FollowsTheClosedForm(int segments, int rows)
    {
        var mesh = TestMeshes.CreateCatenoid(1.0, 2.0, segments, rows);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        int seed = (rows / 2) * segments; // on the throat
        double edge = new MeshProjection(mesh).AverageEdgeLength;
        foreach (int passes in new[] { 10, 0 })
        {
            var opts = new AsymptoticCurves.Options { SmoothingPasses = passes };
            var curves = AsymptoticCurves.Trace(mesh, new[] { seed }, pc, false, opts);
            Assert.Single(curves);
            var line = curves[0];
            var (worst, rms, ang) = CurveError(line);
            double len = 0; for (int i = 1; i < line.Length; i++) len += (line[i] - line[i - 1]).Length;
            _out.WriteLine($"catenoid {segments}x{rows}, fairing {passes}: {line.Length} pts, length {len:F3}, worst offset {worst:F4} ({worst / edge:F2} edges), rms {rms:F4}, worst tangent error {ang:F2}°");
            var (u0, v0) = Param(line[0]);
            var (ea0, eb0) = ExactDirections(line[0]);
            Vec3d t0 = (line[Math.Min(3, line.Length - 1)] - line[0]).Normalized();
            bool famA = Math.Abs(Vec3d.Dot(t0, ea0)) >= Math.Abs(Vec3d.Dot(t0, eb0));
            double kk = famA ? u0 - v0 : u0 + v0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i += Math.Max(1, line.Length / 12)) { var (u, v) = Param(line[i]); sb.Append($" z={v:F2}:{Math.Abs(Wrap((famA ? u - v : u + v) - kk)) * Math.Cosh(v) / Math.Sqrt(2):F4}"); }
            _out.WriteLine("offset along z:" + sb);
            Assert.True(len > 2.5, $"curve should run rim to rim (length {len:F2})");
            // before the border repair the curve left its line by 0.019 (0.2–0.35 edges) at both
            // resolutions — the error was made in the last rows and carried across the surface
            Assert.True(worst < 0.06 * edge, $"traced curve leaves its closed-form line by {worst:F4} = {worst / edge:F2} edges");
            Assert.True(ang < 2.5, $"worst tangent error {ang:F2}°");
        }
    }
}

public class BorderRepairTests
{
    private readonly ITestOutputHelper _out;
    public BorderRepairTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void Saddle_DirectionErrorByBorderDistance()
    {
        var mesh = TestMeshes.CreateSaddle(40, 2.0);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        int div = 40;
        var worst = new double[4];
        var worstAt = new string[4];
        for (int j = 0; j <= div; j++)
            for (int i = 0; i <= div; i++)
            {
                int v = j * (div + 1) + i;
                if (!field.Exists[v]) continue;
                var p = mesh.Vertices[v];
                var ea = new Vec3d(1, 1, 2 * p.X - 2 * p.Y).Normalized();
                var eb = new Vec3d(1, -1, 2 * p.X + 2 * p.Y).Normalized();
                double best = Math.Max(Math.Abs(Vec3d.Dot(field.Family1[v], ea)), Math.Abs(Vec3d.Dot(field.Family1[v], eb)));
                double ang = Math.Acos(Math.Min(1.0, best)) * 180 / Math.PI;
                int d = Math.Min(Math.Min(i, div - i), Math.Min(j, div - j));
                if (d > 3) d = 3;
                if (ang > worst[d]) { worst[d] = ang; worstAt[d] = $"({i},{j})"; }
            }
        _out.WriteLine($"saddle worst direction error by border distance: 0:{worst[0]:F2}° at {worstAt[0]}  1:{worst[1]:F2}° at {worstAt[1]}  2:{worst[2]:F2}° at {worstAt[2]}  ≥3:{worst[3]:F2}° at {worstAt[3]}");
        // one-sided normal differences gave 8.5° / 6.0° / 1.6° on the three border rows;
        // jet-fitted rings and the extrapolated border row give ≤ 3° / 0.3° / 0.3°
        Assert.True(worst[0] < 3.0, $"border row {worst[0]:F2}°");
        Assert.True(worst[1] < 0.5 && worst[2] < 0.5, $"rows 1–2: {worst[1]:F2}° / {worst[2]:F2}°");
    }
}

public class CylinderBorderTests
{
    private readonly ITestOutputHelper _out;
    public CylinderBorderTests(ITestOutputHelper output) { _out = output; }
    [Fact]
    public void BorderRows_StayDevelopable()
    {
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 64, 32);
        var pc = PrincipalCurvature.Compute(cyl, 1);
        for (int j = 0; j < 5; j++) { int v = j * 64; _out.WriteLine($"row {j}: k1 {pc.K1[v]:F6} k2 {pc.K2[v]:E2} d1.z {pc.D1[v].Z:E2} n {pc.Normals[v].X:F5},{pc.Normals[v].Y:F5},{pc.Normals[v].Z:E2}"); }
        // rows 1–2 are jet fits (k2 ~ 1e-5 on the exact circle), row 0 is extrapolated (k1 within 2 %)
        Assert.True(Math.Abs(pc.K2[64]) < 1e-4 && Math.Abs(pc.K2[128]) < 1e-4, "jet-fitted rings keep k2 ≈ 0");
        Assert.InRange(pc.K1[0], 0.98, 1.02);
        Assert.True(Math.Abs(pc.D1[0].Z) < 1e-3, "border-row max direction stays circumferential");
    }
}

public class WebLayoutTests
{
    private readonly ITestOutputHelper _out;
    public WebLayoutTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void Catenoid_BorderWeb_EveryCurveRunsRimToRim_NoTJunctions_SpacingFollowsCoshV()
    {
        var mesh = TestMeshes.CreateCatenoid(1.0, 2.0, 96, 48);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = 0.4;
        var oa = new EvenlySpacedNet.Options { Spacing = s, SmoothingPasses = 0 };
        var a = EvenlySpacedNet.TraceFieldWeb(mesh, field.Family1, field.Exists, -1, oa, field.Family2, EvenlySpacedNet.WebSeeding.Border);
        var b = EvenlySpacedNet.TraceFieldWeb(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s }, field.Family1, EvenlySpacedNet.WebSeeding.Border);
        _out.WriteLine($"web: {a.Count} + {b.Count} curves");
        Assert.True(a.Count >= 10 && b.Count >= 10, $"{a.Count} + {b.Count} curves");

        // every end on a rim, every curve a full rim-to-rim asymptotic line
        foreach (var c in a.Concat(b))
        {
            foreach (var e in new[] { c[0], c[^1] })
            {
                var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e));
                Assert.True(proj.IsOnBoundary(h, 1e-4), $"end at z = {e.Z:F3} is not on a rim");
            }
            Assert.True(Math.Abs(Math.Abs(c[0].Z) - 1) < 1e-3 && Math.Abs(Math.Abs(c[^1].Z) - 1) < 1e-3, "ends on the rims z = ±1");
            var (worst, _, _) = AsymptoticAccuracyTests.CurveError(c);
            if (worst >= 0.01)
            {
                var sb = new System.Text.StringBuilder();
                var (u0, v0) = (Math.Atan2(c[0].Y, c[0].X), c[0].Z);
                for (int i = 0; i < c.Length; i += Math.Max(1, c.Length / 15)) { var (u, v) = (Math.Atan2(c[i].Y, c[i].X), c[i].Z); double dA = u - v - (u0 - v0), dB = u + v - (u0 + v0); while (dA > Math.PI) dA -= 2 * Math.PI; while (dA < -Math.PI) dA += 2 * Math.PI; while (dB > Math.PI) dB -= 2 * Math.PI; while (dB < -Math.PI) dB += 2 * Math.PI; sb.Append($" z={v:F2}:A{dA * Math.Cosh(v) / 1.4142:F4}/B{dB * Math.Cosh(v) / 1.4142:F4}"); }
                _out.WriteLine("offsets:" + sb);
            }
            Assert.True(worst < 0.01, $"curve leaves its closed-form line by {worst:F4}");
        }

        // family A curves are the lines u − v = k: the seeds put k on a regular
        // ladder whose perpendicular rung is s at the rim (cosh 1) — so Δk = s√2 / cosh 1 —
        // and the same curves sit s / cosh 1 = 0.65 s apart at the throat: not
        // "equidistant", and cannot be, but exactly as the surface dictates
        var ks = a.Select(c => { double u = Math.Atan2(c[0].Y, c[0].X), v = c[0].Z; double k = u - v; while (k < 0) k += 2 * Math.PI; while (k >= 2 * Math.PI) k -= 2 * Math.PI; return k; }).OrderBy(k => k).ToList();
        var gaps = new List<double>();
        for (int i = 0; i < ks.Count; i++) { double g = (i + 1 < ks.Count ? ks[i + 1] : ks[0] + 2 * Math.PI) - ks[i]; gaps.Add(g); }
        double dkTheory = s * Math.Sqrt(2.0) / Math.Cosh(1.0);
        double dkMean = gaps.Average();
        _out.WriteLine($"Δk mean {dkMean:F4} (theory {dkTheory:F4}), min {gaps.Min():F4}, max {gaps.Max():F4}; rim spacing {dkMean * Math.Cosh(1) / Math.Sqrt(2):F3} throat spacing {dkMean / Math.Sqrt(2):F3} (s = {s})");
        Assert.InRange(dkMean / dkTheory, 0.9, 1.1);
        Assert.True(gaps.Max() < 1.6 * dkTheory && gaps.Min() > 0.5 * dkTheory, $"ladder rungs {gaps.Min():F3} … {gaps.Max():F3} vs {dkTheory:F3}");
    }

    [Fact]
    public void Cylinder_GeodesicCrossWeb_ParallelHelicesExactlySpacingApart()
    {
        // K = 0: geodesics stay parallel (Jacobi J'' = 0), so a web seeded every
        // s along the perpendicular geodesic is a set of 45° helices exactly s apart
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 96, 48);
        var proj = new MeshProjection(cyl);
        double alpha = Math.PI / 4, s = 0.3;
        int seed = 24 * 96;
        var opts = new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.WebCross, StepSize = 0.02 };
        var g = EvenlySpacedNet.TraceGeodesics(cyl, seed, new Vec3d(0, Math.Sin(alpha), Math.Cos(alpha)), opts);
        Assert.True(g.Count >= 6, $"{g.Count} geodesics");
        foreach (var c in g)
        {
            foreach (var e in new[] { c[0], c[^1] })
                Assert.True(proj.IsOnBoundary(proj.ClosestPoint(e, proj.NearestVertexGlobal(e)), 1e-4), "helices run rim to rim");
            // helix: z − z0 = (θ − θ0) cot α along the whole curve
            double th0 = Math.Atan2(c[0].Y, c[0].X), unwrapped = 0, worst = 0, prev = th0;
            for (int i = 1; i < c.Length; i++)
            {
                double t = Math.Atan2(c[i].Y, c[i].X), d = t - prev;
                while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                unwrapped += d; prev = t;
                worst = Math.Max(worst, Math.Abs(Math.Abs(c[i].Z - c[0].Z) - Math.Abs(unwrapped) / Math.Tan(alpha)));
            }
            Assert.True(worst < 0.01, $"web geodesic is not a 45° helix, rise error {worst:F4}");
        }
        // perpendicular spacing: helix phases θ − z·tan α around the cylinder; neighbours
        // differ by Δ, perpendicular distance Δ·sin α. The cross seeds and the gap
        // fill both lay curves exactly s apart; the one gap that closes the ring
        // takes the remainder (2π sin α / s is not an integer)
        var phases = g.Select(c => { var m = c.OrderBy(q => Math.Abs(q.Z)).First(); double th = Math.Atan2(m.Y, m.X) - m.Z * Math.Tan(alpha); return (th % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI); }).OrderBy(x => x).ToList();
        var gaps = phases.Select((k, i) => ((i + 1 < phases.Count ? phases[i + 1] : phases[0] + 2 * Math.PI) - k) * Math.Sin(alpha)).ToList();
        _out.WriteLine("perpendicular gaps: " + string.Join(" ", gaps.Select(x => x.ToString("F3"))));
        int exact = gaps.Count(x => Math.Abs(x - s) < 0.01 * s);
        Assert.True(exact >= gaps.Count - 1, $"{exact} of {gaps.Count} gaps are s ± 1 %: {string.Join(" ", gaps.Select(x => x.ToString("F3")))}");
        Assert.True(gaps.All(x => x > 0.7 * s && x < 1.02 * s), "the closing gap lies between 0.7 s and s");
    }

    [Fact]
    public void Saddle_CrossWeb_CurvesFromTheSeedCross_ReachTheBorder()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var proj = new MeshProjection(saddle);
        var pc = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(pc, saddle, 15.0);
        int seed = 20 * 41 + 20;
        var a = EvenlySpacedNet.TraceFieldWeb(saddle, field.Family1, field.Exists, seed, new EvenlySpacedNet.Options { Spacing = 0.2 }, field.Family2, EvenlySpacedNet.WebSeeding.Cross);
        Assert.True(a.Count >= 8, $"{a.Count} curves");
        foreach (var c in a)
            foreach (var e in new[] { c[0], c[^1] })
                Assert.True(proj.IsOnBoundary(proj.ClosestPoint(e, proj.NearestVertexGlobal(e)), 1e-4), "web curves run border to border");
        // the seeds lie on the other family's line through the centre (x = −y): the curves are x − y = const at spacing 0.2·√2 in k... check they are straight rulings
        foreach (var c in a)
        {
            double dev = 0; Vec3d p0 = c[0], d = (c[^1] - c[0]).Normalized();
            foreach (var q in c) dev = Math.Max(dev, Vec3d.Cross(q - p0, d).Length);
            Assert.True(dev < 0.01, $"asymptotic lines of z = x² − y² are straight, deviation {dev:F4}");
        }
    }
}

