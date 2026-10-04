using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Analysis;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Distributions of net metrics (roadmap C, "metrics with distributions, not
/// only extremes"). Ground truth:
/// - quantiles of 1..1000 by type-7 interpolation (Hyndman &amp; Fan 1996) and
///   the Freedman–Diaconis width 2·IQR·n^(−1/3);
/// - parallel lines / concentric circles in a plane: spacing = their distance;
/// - meridians of the unit sphere Δ apart: the plane normal to the meridian at
///   polar angle θ cuts the next meridian at tan θ' = tan θ / cos Δ;
/// - the asymptotic lines u − v = c of the catenoid (cosh v cos u, cosh v sin u, v):
///   neighbours Δc apart are cosh v · Δc / √2 apart across the curves, so the
///   share of a lath within 5 % of the waist spacing is sinh(acosh 1.05) / sinh 2;
/// - the rulings x = a and y = b of the hyperbolic paraboloid z = xy cross at
///   cos φ = |ab| / √((1 + a²)(1 + b²)).
/// </summary>
public class NetDistributionsTests
{
    [Fact]
    public void Distribution_Quantiles_And_FreedmanDiaconis_Bins()
    {
        var d = new Distribution(Enumerable.Range(1, 1000).Select(i => (double)i));
        Assert.Equal(1000, d.Count);
        Assert.Equal(500.5, d.Mean, 12);
        Assert.Equal(500.5, d.Median, 12);
        Assert.Equal(250.75, d.Q1, 12);
        Assert.Equal(750.25, d.Q3, 12);
        Assert.Equal(Math.Sqrt(1000.0 * 1001.0 / 12.0), d.StdDev, 9);
        Assert.Equal(51.0 / 1000.0, d.ShareNear(500, 0.05), 12); // 475..525
        Assert.Equal(0.5, d.ShareAbove(500), 12);

        var h = d.Histogram();
        Assert.StartsWith("Freedman", h.Rule);
        // h = 2·499.5 / 10 = 99.9, range 999 → exactly 10 bins of 100 values
        Assert.Equal(10, h.Bins);
        Assert.All(h.Counts, c => Assert.Equal(100, c));
        Assert.Equal(99.9, h.BinWidth, 9);

        // a fixed range counts what falls outside
        var hf = d.Histogram(4, 101, 900);
        Assert.Equal(100, hf.Below);
        Assert.Equal(100, hf.Above);
        Assert.Equal(800, hf.Counts.Sum());
    }

    [Fact]
    public void Distribution_Degenerate_Samples()
    {
        var one = new Distribution(Enumerable.Repeat(0.3, 7));
        var h1 = one.Histogram();
        Assert.Single(h1.Counts);
        Assert.Equal(7, h1.Counts[0]);
        Assert.Equal(0.0, one.StdDev);

        // IQR = 0 but a spread: Sturges, ⌈log2 10⌉ + 1 = 5 bins
        var zeros = new Distribution(Enumerable.Repeat(0.0, 9).Concat(new[] { 1.0 }));
        var hs = zeros.Histogram();
        Assert.Equal("Sturges", hs.Rule);
        Assert.Equal(5, hs.Bins);
        Assert.Equal(9, hs.Counts[0]);
        Assert.Equal(1, hs.Counts[4]);

        var empty = new Distribution(new[] { double.NaN, double.PositiveInfinity });
        Assert.Equal(0, empty.Count);
        Assert.Equal(0, empty.Histogram().Counts.Sum());
    }

    private static Vec3d Up(Vec3d _) => new Vec3d(0, 0, 1);

    private static Vec3d[] Segment(Vec3d a, Vec3d b, int n) =>
        Enumerable.Range(0, n + 1).Select(i => a + (i / (double)n) * (b - a)).ToArray();

    [Fact]
    public void Spacing_ParallelLines_Is_Their_Distance()
    {
        var lines = Enumerable.Range(0, 9).Select(k => Segment(new Vec3d(0, 0.25 * k, 0), new Vec3d(2, 0.25 * k, 0), 40)).ToList();
        var r = NetDistributions.FamilySpacing(lines, Up, new NetDistributions.SpacingOptions { Target = 0.25 });
        Assert.All(r.Spacing.Values, v => Assert.Equal(0.25, v, 12));
        int perCurve = r.Points.Length / 9;
        Assert.Equal(32, perCurve); // step = Target / 4 = 0.0625 over length 2
        Assert.Equal(1.0, r.Within5, 12);
        // the two border lines have one open side each
        Assert.Equal(2 * perCurve, r.OpenSides);
        Assert.Equal((7 * 2 + 2) * perCurve, r.Spacing.Count);

        // a missing line opens a gap of 0.5 seen from both of its neighbours
        var gap = lines.Where((l, k) => k != 4).ToList();
        var g = NetDistributions.FamilySpacing(gap, Up, new NetDistributions.SpacingOptions { Target = 0.25 });
        Assert.Equal(2 * perCurve, g.Spacing.Values.Count(v => Math.Abs(v - 0.5) < 1e-12));
        Assert.Equal(12.0 / 14.0, g.Within5, 12);
        Assert.Equal(0.5, g.Spacing.Max, 12);

        // the same through a flat mesh's normals
        var mesh = TestMeshes.CreateQuadGrid(8, 8, 0.25);
        var m = NetDistributions.FamilySpacing(new MeshProjection(mesh), lines, new NetDistributions.SpacingOptions { Target = 0.25 });
        Assert.All(m.Spacing.Values, v => Assert.Equal(0.25, v, 9));
        Assert.Equal(r.Spacing.Count, m.Spacing.Count);
    }

    [Fact]
    public void Spacing_ConcentricCircles_Closed_Loops()
    {
        // closed polygons with 720 sides: the radial cut of the neighbouring
        // polygon is within r(1 − cos(π/720)) of the circle
        var rings = Enumerable.Range(0, 6).Select(k =>
        {
            double rad = 1 + 0.2 * k;
            return Enumerable.Range(0, 721).Select(i => { double a = 2 * Math.PI * (i % 720) / 720; return new Vec3d(rad * Math.Cos(a), rad * Math.Sin(a), 0); }).ToArray();
        }).ToList();
        var r = NetDistributions.FamilySpacing(rings, Up, new NetDistributions.SpacingOptions { Target = 0.2, MaxDistance = 0.5 });
        Assert.True(r.Spacing.Count > 0);
        Assert.All(r.Spacing.Values, v => Assert.InRange(v, 0.2 - 1e-4, 0.2 + 1e-4));
        Assert.Equal(1.0, r.Within5, 12);
        // the innermost and outermost ring each have one open side, the loop's
        // own far side (2r away) is not taken as a neighbour
        int inner = r.Curve.Count(c => c == 0);
        Assert.All(Enumerable.Range(0, r.Points.Length).Where(i => r.Curve[i] == 0), i => Assert.True(double.IsNaN(r.Right[i]) ^ double.IsNaN(r.Left[i])));
        Assert.True(inner > 0);
    }

    [Fact]
    public void Spacing_SphereMeridians_Matches_ClosedForm()
    {
        const int n = 12; double delta = 2 * Math.PI / n;
        var meridians = Enumerable.Range(0, n).Select(k =>
        {
            double ph = k * delta;
            return Enumerable.Range(0, 801).Select(i => { double th = 0.3 + (Math.PI - 0.6) * i / 800; return new Vec3d(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th)); }).ToArray();
        }).ToList();
        var r = NetDistributions.FamilySpacing(meridians, p => p.Normalized(), new NetDistributions.SpacingOptions { Step = 0.02, MaxDistance = 0.8 });
        double worst = 0; int checkedSides = 0;
        for (int i = 0; i < r.Points.Length; i++)
        {
            var p = r.Points[i];
            double th = Math.Acos(Math.Max(-1, Math.Min(1, p.Z)));
            double thq = Math.Atan2(Math.Sin(th), Math.Cos(th) * Math.Cos(delta));
            var pp = new Vec3d(Math.Sin(th), 0, Math.Cos(th));
            var qq = new Vec3d(Math.Sin(thq) * Math.Cos(delta), Math.Sin(thq) * Math.Sin(delta), Math.Cos(thq));
            double expected = (qq - pp).Length;
            foreach (double side in new[] { r.Left[i], r.Right[i] })
            {
                // a cut lies on a chord of the 800-gon: sagitta ≤ (π/800)²/8
                Assert.False(double.IsNaN(side));
                worst = Math.Max(worst, Math.Abs(side - expected));
                checkedSides++;
            }
        }
        Assert.True(worst < 1e-4, $"worst deviation from tan θ' = tan θ / cos Δ: {worst}");
        Assert.Equal(2 * r.Points.Length, checkedSides);
        // the cut at the equator is the chord 2 sin(Δ/2)
        Assert.Equal(2 * Math.Sin(delta / 2), r.Spacing.Max, 3);
    }

    [Fact]
    public void Spacing_CatenoidAsymptoticLines_Grow_As_Cosh()
    {
        const int n = 24; double dc = 2 * Math.PI / n;
        Vec3d X(double u, double v) => new Vec3d(Math.Cosh(v) * Math.Cos(u), Math.Cosh(v) * Math.Sin(u), v);
        var family = Enumerable.Range(0, n).Select(k => Enumerable.Range(0, 1601).Select(i => { double v = -2 + 4.0 * i / 1600; return X(k * dc + v, v); }).ToArray()).ToList();
        Vec3d Normal(Vec3d p) { double v = p.Z, u = Math.Atan2(p.Y, p.X); return new Vec3d(Math.Cos(u), Math.Sin(u), -Math.Sinh(v)) / Math.Cosh(v); }
        double waist = dc / Math.Sqrt(2);
        var r = NetDistributions.FamilySpacing(family, Normal, new NetDistributions.SpacingOptions { Target = waist, Step = 0.02, MaxDistance = 1.5 * Math.Cosh(2) * waist });
        // exact cut: solve (X(c0 ± Δc + v, v) − p)·t = 0 on the neighbouring
        // curve (t = X_u + X_v at p); first order this is cosh v · Δc/√2
        Vec3d Xu(double u, double v) => new Vec3d(-Math.Cosh(v) * Math.Sin(u), Math.Cosh(v) * Math.Cos(u), 0);
        Vec3d Xv(double u, double v) => new Vec3d(Math.Sinh(v) * Math.Cos(u), Math.Sinh(v) * Math.Sin(u), 1);
        double worst = 0, worstFirstOrder = 0; int compared = 0;
        for (int i = 0; i < r.Points.Length; i++)
        {
            var p = r.Points[i];
            double v0 = p.Z, u0 = Math.Atan2(p.Y, p.X), c0 = u0 - v0;
            var t = (Xu(u0, v0) + Xv(u0, v0)).Normalized();
            var exact = new List<double>();
            foreach (double sgn in new[] { 1.0, -1.0 })
            {
                double F(double v) => Vec3d.Dot(X(c0 + sgn * dc + v, v) - p, t);
                double lo = v0 - 0.6, hi = v0 + 0.6;
                if (F(lo) * F(hi) > 0) continue;
                for (int it = 0; it < 80; it++) { double m = 0.5 * (lo + hi); if (F(lo) * F(m) <= 0) hi = m; else lo = m; }
                double vq = 0.5 * (lo + hi);
                if (Math.Abs(vq) <= 2) exact.Add((X(c0 + sgn * dc + vq, vq) - p).Length);
            }
            var measured = new[] { r.Left[i], r.Right[i] }.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToArray();
            exact.Sort();
            if (exact.Count == 2) Assert.Equal(2, measured.Length);
            if (measured.Length != exact.Count) continue; // one neighbour cut lies right at the rim
            // (within the last ~0.13 of height the cut runs past the rim: ~6 % of the sides by arc length)
            for (int k = 0; k < exact.Count; k++)
            {
                worst = Math.Max(worst, Math.Abs(measured[k] - exact[k]));
                worstFirstOrder = Math.Max(worstFirstOrder, Math.Abs(measured[k] / (Math.Cosh(v0) * waist) - 1));
                compared++;
            }
        }
        Assert.True(compared > 0.9 * 2 * r.Points.Length, $"{compared} sides compared of {2 * r.Points.Length}");
        // polyline chords: sagitta of 1/400 of a 4-unit lath
        Assert.True(worst < 2e-4, $"worst deviation from the exact cut: {worst}");
        // the first-order law holds to O(Δc · tanh v): 7 % at the rims for Δc = 15°
        Assert.True(worstFirstOrder < 0.08, $"worst relative deviation from cosh v · Δc/√2: {worstFirstOrder}");
        // share within 5 % of the waist spacing, by arc length (ds = √2 cosh v dv)
        double share = Math.Sinh(Acosh(1.05)) / Math.Sinh(2);
        Assert.InRange(r.Within5, share - 0.015, share + 0.015);
        // the widest gap measured is the inward cut from the rim: along the other
        // asymptotic line from v = 2 down to 2 − Δc/2, i.e. about √2 (sinh 2 − sinh(2 − Δc/2))
        Assert.InRange(r.Spacing.Max, 0.97 * Math.Sqrt(2) * (Math.Sinh(2) - Math.Sinh(2 - dc / 2)), 1.03 * Math.Sqrt(2) * (Math.Sinh(2) - Math.Sinh(2 - dc / 2)));
    }

    private static double Acosh(double x) => Math.Log(x + Math.Sqrt(x * x - 1));

    [Fact]
    public void CrossingAngles_Hypar_Rulings_ClosedForm()
    {
        var vals = Enumerable.Range(0, 9).Select(i => -1 + 0.25 * i).ToArray();
        // x = a: (a, y, a y); y = b: (x, b, x b)
        var A = vals.Select(a => Enumerable.Range(0, 23).Select(i => { double y = -1.1 + 0.1 * i; return new Vec3d(a, y, a * y); }).ToArray()).ToList();
        var B = vals.Select(b => Enumerable.Range(0, 23).Select(i => { double x = -1.1 + 0.1 * i; return new Vec3d(x, b, x * b); }).ToArray()).ToList();
        var res = NetDistributions.CrossingAngles(A, B);
        var expected = new List<double>();
        foreach (double a in vals) foreach (double b in vals)
            expected.Add(Math.Acos(Math.Abs(a * b) / Math.Sqrt((1 + a * a) * (1 + b * b))) * 180 / Math.PI);
        expected.Sort();
        Assert.Equal(81, res.Angles.Count);
        for (int i = 0; i < 81; i++) Assert.Equal(expected[i], res.Angles.Values[i], 9);
        for (int i = 0; i < res.Points.Length; i++)
        {
            double a = res.Points[i].X, b = res.Points[i].Y;
            Assert.Equal(Math.Acos(Math.Abs(a * b) / Math.Sqrt((1 + a * a) * (1 + b * b))) * 180 / Math.PI, res.PerCrossing[i], 9);
        }
        Assert.Equal(60.0, res.Angles.Min, 9);   // at (±1, ±1)
        Assert.Equal(90.0, res.Angles.Max, 9);   // on the axes
    }

    [Fact]
    public void CrossingAngles_Curved_Polylines_Use_Interpolated_Tangents()
    {
        // two circles of the unit sphere crossing at the poles of the x axis at a
        // known angle: the equator and a great circle tilted by 40° about x
        double tilt = 40 * Math.PI / 180;
        var eq = Enumerable.Range(0, 97).Select(i => { double a = 2 * Math.PI * i / 96 + 0.013; return new Vec3d(Math.Cos(a), Math.Sin(a), 0); }).ToArray();
        var tl = Enumerable.Range(0, 97).Select(i => { double a = 2 * Math.PI * i / 96 + 0.029; return new Vec3d(Math.Cos(a), Math.Sin(a) * Math.Cos(tilt), Math.Sin(a) * Math.Sin(tilt)); }).ToArray();
        var res = NetDistributions.CrossingAngles(new[] { eq }, new[] { tl }, 0.01);
        Assert.Equal(2, res.Angles.Count);
        Assert.All(res.Angles.Values, a => Assert.InRange(a, 39.9, 40.1));
    }

    [Fact]
    public void Utilization_PerLath_And_Pooled()
    {
        var r1 = new LathAnalysis.Result(new double[3], new double[3], new double[3], new[] { 0.2, 0.9, 0.4 }, 0.9, true);
        var r2 = new LathAnalysis.Result(new double[2], new double[2], new double[2], new[] { 1.3, 0.5 }, 1.3, false);
        var per = NetDistributions.Utilization(new[] { r1, r2 });
        Assert.Equal(new[] { 0.9, 1.3 }, per.Values);
        Assert.Equal(0.5, per.ShareAbove(1), 12);
        var pooled = NetDistributions.Utilization(new[] { r1, r2 }, perLath: false);
        Assert.Equal(5, pooled.Count);
        Assert.Equal(0.2, pooled.ShareAbove(1), 12);

        // a flat strip on a great circle of the unit sphere: kn = 1, so the
        // bending strain t/2 equals MaxStrain = t/2 everywhere (utilization 1)
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        var circle = Enumerable.Range(0, 241).Select(i => { double a = 2 * Math.PI * i / 240; return new Vec3d(Math.Cos(a), Math.Sin(a), 0); }).ToArray();
        var la = LathAnalysis.Analyze(proj, circle, new LathAnalysis.Options { Width = 0.05, Thickness = 0.01, MaxStrain = 0.005 });
        var u = NetDistributions.Utilization(new[] { la }, perLath: false);
        Assert.InRange(u.Median, 0.95, 1.05);
    }
}
