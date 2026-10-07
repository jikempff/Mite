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
/// Farthest-point seeding (NetLayout.FillFarthest; Mebarki, Alliez &amp;
/// Devillers 2005) checked where the answer is exact — a uniform field on a
/// plane, whose curves must sit at the centres of the successive largest voids,
/// and the meridians of a torus, whose gaps must all lie in the saturation band
/// — and against the neighbour fill (Jobard &amp; Lefer 1997) on anticlastic
/// surfaces with the independent <see cref="PlacementMetrics"/>.
/// </summary>
public class FarthestPointSeedingTests
{
    private readonly ITestOutputHelper _out;
    public FarthestPointSeedingTests(ITestOutputHelper o) { _out = o; }

    internal static MeshData FlatGrid(int n, double size)
    {
        var v = new List<Vec3d>(); var f = new List<int[]>();
        for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) v.Add(new Vec3d(-size / 2 + size * i / n, -size / 2 + size * j / n, 0));
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i; f.Add(new[] { a, a + 1, a + n + 2 }); f.Add(new[] { a, a + n + 2, a + n + 1 }); }
        return new MeshData(v.ToArray(), f.ToArray());
    }

    /// <summary>
    /// Mebarki's placement in one dimension: curves y = c across the strip
    /// |y| ≤ half, a frame ½ Spacing outside each border, the first curve at
    /// y = first; every interval of width ≥ Saturation · Spacing is split at its
    /// midpoint until none is left. Intervals split independently, so the final
    /// set does not depend on the order.
    /// </summary>
    internal static List<double> PredictStrip(double half, double spacing, double saturation, double first)
    {
        var cuts = new List<double> { -half - 0.5 * spacing, first, half + 0.5 * spacing };
        var curves = new List<double> { first };
        while (true)
        {
            cuts.Sort();
            int bi = -1; double bw = saturation * spacing - 1e-12;
            for (int i = 0; i + 1 < cuts.Count; i++) if (cuts[i + 1] - cuts[i] >= bw) { bw = cuts[i + 1] - cuts[i]; bi = i; }
            if (bi < 0) break;
            double m = 0.5 * (cuts[bi] + cuts[bi + 1]);
            cuts.Add(m); curves.Add(m);
        }
        curves.Sort();
        return curves;
    }

    [Fact]
    public void Plane_UniformField_CurvesAtTheVoidCentres()
    {
        var mesh = FlatGrid(40, 2.0);
        var dirs = Enumerable.Repeat(new Vec3d(1, 0, 0), mesh.VertexCount).ToArray();
        double s = 0.13;
        foreach (bool continuous in new[] { false, true })
        {
            var o = new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.FillFarthest, Continuous = continuous };
            var curves = EvenlySpacedNet.TraceField(mesh, dirs, null, -1, o);
            var expected = PredictStrip(1.0, s, 1.6, 0.0);
            var ys = curves.Select(c => c.Average(p => p.Y)).OrderBy(y => y).ToList();
            _out.WriteLine($"continuous={continuous}: expected {string.Join(" ", expected.Select(y => (y / s).ToString("F3")))}");
            _out.WriteLine($"                 measured {string.Join(" ", ys.Select(y => (y / s).ToString("F3")))}");
            Assert.Equal(expected.Count, ys.Count);
            for (int i = 0; i < ys.Count; i++)
                Assert.True(Math.Abs(ys[i] - expected[i]) < 0.02 * s, $"curve {i}: y = {ys[i] / s:F3} S, expected {expected[i] / s:F3} S");
            foreach (var c in curves)
            {
                // straight, full width
                Assert.True(c.Max(p => Math.Abs(p.Y - c[0].Y)) < 1e-6);
                Assert.True(c.Min(p => p.X) < -1 + 1e-6 && c.Max(p => p.X) > 1 - 1e-6);
            }
            // the saturation band: every gap in [0.8, 1.6) S, the last curve
            // less than 0.8 S plus the ½ S frame from the border
            for (int i = 1; i < ys.Count; i++)
            {
                double g = (ys[i] - ys[i - 1]) / s;
                Assert.InRange(g, 0.8, 1.6);
            }
            Assert.True(o.LargestGap < 1.6 * s, $"largest gap {o.LargestGap / s:F3} S");
            Assert.Equal(0, o.RejectedSeeds);
        }

        // the neighbour fill on the same strip: exactly Spacing apart (ladder from the seed)
        var jl = EvenlySpacedNet.TraceField(mesh, dirs, null, -1, new EvenlySpacedNet.Options { Spacing = s, Continuous = false });
        var yj = jl.Select(c => c.Average(p => p.Y)).OrderBy(y => y).ToList();
        for (int i = 1; i < yj.Count; i++) Assert.True(Math.Abs((yj[i] - yj[i - 1]) / s - 1.0) < 0.01);
    }

    [Fact]
    public void Torus_Meridians_EveryGapInTheSaturationBand()
    {
        // Lines of maximum curvature on a torus (R = 3, r = 1) are the meridian
        // circles u = const; their spacing grows from (R − r)Δu inside to
        // (R + r)Δu outside, so no layout can keep it constant. Farthest-point
        // seeding splits a gap once its widest part (the outer equator) reaches
        // Saturation · Spacing, so every outer gap ends in [0.8, 1.6] · Spacing.
        double R = 3, r = 1;
        var mesh = AnalyticShapes.Build("torus", R, r, 0, 64);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        double s = 0.37;
        var o = new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.FillFarthest, Continuous = false };
        var curves = EvenlySpacedNet.TraceField(mesh, pc.D1, null, -1, o);
        var us = new List<double>();
        foreach (var c in curves)
        {
            Assert.True((c[0] - c[^1]).Length < 1e-9, "meridian closes");
            double u0 = Math.Atan2(c[0].Y, c[0].X);
            double spread = c.Max(p => { double d = Math.Atan2(p.Y, p.X) - u0; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return Math.Abs(d); });
            Assert.True(spread < 0.5 * Math.PI / 180, $"meridian stays in its plane: {spread * 180 / Math.PI:F2}°");
            us.Add(Math.Atan2(c.Average(p => p.Y), c.Average(p => p.X)));
        }
        us.Sort();
        var outer = new List<double>();
        for (int i = 0; i < us.Count; i++)
        {
            double du = (i + 1 < us.Count ? us[i + 1] : us[0] + 2 * Math.PI) - us[i];
            outer.Add(2 * (R + r) * Math.Sin(du / 2) / s);
        }
        _out.WriteLine($"{curves.Count} meridians, outer chord gaps {outer.Min():F3}–{outer.Max():F3} S, inner {outer.Min() * (R - r) / (R + r):F3}–{outer.Max() * (R - r) / (R + r):F3} S");
        // exact count: the first meridian, then halving: N = 2^k meridians, the
        // smallest k whose half-gap at the outer equator — the distance
        // 2(R + r) sin(π / 2N) from the midpoint to the nearest meridian — is
        // below ½ Saturation · Spacing
        int expected = 1;
        while (2 * (R + r) * Math.Sin(Math.PI / (2 * expected)) >= 0.8 * s) expected *= 2;
        Assert.Equal(expected, curves.Count);
        Assert.True(outer.Min() >= 0.8 * 0.98, $"smallest outer gap {outer.Min():F3} S");
        Assert.True(outer.Max() <= 1.6 * 1.02, $"largest outer gap {outer.Max():F3} S");
        Assert.Equal(0, o.RejectedSeeds);
    }

    public static IEnumerable<object[]> Anticlastic() =>
        new[] { "saddle", "catenoid", "hyperboloid" }.SelectMany(n => new[] { new object[] { n, false }, new object[] { n, true } });

    [Theory]
    [MemberData(nameof(Anticlastic))]
    public void Asymptotic_NoVoidAboveSaturation_AndNoMoreFreeEndsThanTheNeighbourFill(string name, bool continuous)
    {
        var mesh = AnalyticShapes.Build(name, 0, 0, 0, 48);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = mesh.BoundingBoxDiagonal() / 25.0;
        var oj = new EvenlySpacedNet.Options { Spacing = s, Continuous = continuous };
        var of = new EvenlySpacedNet.Options { Spacing = s, Continuous = continuous, Layout = NetLayout.FillFarthest };
        var jl = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, oj, field.Family2);
        var fp = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, of, field.Family2);
        var mj = PlacementMetrics.Measure(mesh, field.Exists, jl, s);
        var mf = PlacementMetrics.Measure(mesh, field.Exists, fp, s);
        _out.WriteLine($"{name} {(continuous ? "continuous" : "classic")}\n  neighbour fill: {mj}\n  farthest point: {mf}  (seeds rejected {of.RejectedSeeds}, largest gap left {of.LargestGap / s:F2} S)");

        // saturation: away from the region border no point is farther than
        // ½ Saturation · Spacing from a curve (measured on the curve polylines)
        Assert.Equal(0, of.RejectedSeeds);
        Assert.True(mf.LargestInteriorVoid <= 0.8 + 0.02, $"largest interior void {mf.LargestInteriorVoid:F3} S");
        Assert.True(of.LargestGap < 1.6 * s);
        // every curve starts in the middle of a void: no more T-junctions than the neighbour fill
        Assert.True(mf.FreeEnds <= mj.FreeEnds, $"free ends {mf.FreeEnds} vs {mj.FreeEnds}");
        Assert.True(mf.GapP95 <= 1.6, $"gap p95 {mf.GapP95:F2} S");
        Assert.True(fp.All(c => c.Length > 2));
        if (name == "saddle" && !continuous) Assert.Equal(0, mf.FreeEnds);
        // the neighbour fill packs curves tighter than asked where they
        // converge; seeded in the voids the median gap is closer to Spacing
        if (name != "saddle") Assert.True(Math.Abs(1 - mf.GapMedian) < Math.Abs(1 - mj.GapMedian), $"median gap {mf.GapMedian:F2} vs {mj.GapMedian:F2}");
    }

    [Fact]
    public void Saturation_TradesCurveCountForGapWidth()
    {
        var mesh = AnalyticShapes.Build("saddle", 0, 0, 0, 48);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = mesh.BoundingBoxDiagonal() / 25.0;
        int prev = int.MaxValue;
        foreach (double sat in new[] { 1.2, 1.6, 2.0 })
        {
            var o = new EvenlySpacedNet.Options { Spacing = s, Continuous = false, Layout = NetLayout.FillFarthest, Saturation = sat };
            var c = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, o, field.Family2);
            var m = PlacementMetrics.Measure(mesh, field.Exists, c, s);
            _out.WriteLine($"saturation {sat}: {m}");
            Assert.True(c.Count <= prev);
            Assert.True(m.LargestInteriorVoid <= 0.5 * sat + 0.02, $"void {m.LargestInteriorVoid:F3} S at saturation {sat}");
            prev = c.Count;
        }
        // below 1 the seeds would land closer than the gaps they split: clamped to 1
        var low = new EvenlySpacedNet.Options { Spacing = s, Continuous = false, Layout = NetLayout.FillFarthest, Saturation = 0.3, MaxCurves = 500 };
        var cl = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, low, field.Family2);
        var one = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Continuous = false, Layout = NetLayout.FillFarthest, Saturation = 1.0, MaxCurves = 500 }, field.Family2);
        Assert.Equal(one.Count, cl.Count);
        Assert.False(low.ReachedMaxCurves);
    }

    [Fact]
    public void MaxCurves_AndCancel_StopTheFill()
    {
        var mesh = AnalyticShapes.Build("saddle", 0, 0, 0, 40);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = mesh.BoundingBoxDiagonal() / 25.0;
        var o = new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.FillFarthest, MaxCurves = 5 };
        var c = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, o, field.Family2);
        Assert.Equal(5, c.Count);
        Assert.True(o.ReachedMaxCurves);
        int calls = 0;
        var oc = new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.FillFarthest, ShouldCancel = () => ++calls > 3 };
        var cc = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, oc, field.Family2);
        Assert.True(oc.Cancelled);
        Assert.Equal(4, cc.Count);
    }

    [Fact]
    public void Conjugate_And_Streamlines_AcceptTheLayout_Geodesics_KeepTheNeighbourFill()
    {
        var mesh = AnalyticShapes.Build("saddle", 0, 0, 0, 40);
        double s = mesh.BoundingBoxDiagonal() / 20.0;
        var conj = ConjugateNet.Trace(mesh, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.FillFarthest });
        Assert.True(conj.FamilyA.Count >= 5 && conj.FamilyB.Count >= 5, $"{conj.FamilyA.Count} + {conj.FamilyB.Count}");

        // geodesics: FillFarthest falls back to the neighbour fill, curve for curve
        var proj = new MeshProjection(mesh);
        int seed = proj.NearestVertexGlobal(new Vec3d(0, 0, 0));
        var g0 = EvenlySpacedNet.TraceGeodesics(mesh, seed, new Vec3d(1, 1, 0), new EvenlySpacedNet.Options { Spacing = s });
        var g1 = EvenlySpacedNet.TraceGeodesics(mesh, seed, new Vec3d(1, 1, 0), new EvenlySpacedNet.Options { Spacing = s, Layout = NetLayout.FillFarthest });
        Assert.Equal(g0.Count, g1.Count);
        for (int i = 0; i < g0.Count; i++)
        {
            Assert.Equal(g0[i].Length, g1[i].Length);
            for (int k = 0; k < g0[i].Length; k++) Assert.True((g0[i][k] - g1[i][k]).Length < 1e-12);
        }
    }
}
