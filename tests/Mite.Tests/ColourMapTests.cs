using System;
using System.IO;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Visualisation;
using Xunit;
using Xunit.Abstractions;
using static Mite.Core.Visualisation.ColourMap;

namespace Mite.Tests;

/// <summary>
/// The shared colour maps and legends: colour science against published
/// reference values (sRGB ↔ CIELAB, Moreland 2009 Tables 1–2), the lightness
/// uniformity Kovesi 2015 asks for, the utilisation thresholds of the web app,
/// Heckbert's nice ticks, the legend strip, and the JavaScript twin on disk.
/// </summary>
public class ColourMapTests
{
    private readonly ITestOutputHelper _out;
    public ColourMapTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void SrgbToLab_MatchesReferenceValues()
    {
        // D65 reference values (Lindbloom / colormine): sRGB red and blue
        var white = SrgbToLab(1, 1, 1);
        Assert.InRange(white[0], 99.99, 100.01); Assert.InRange(Math.Abs(white[1]), 0, 0.01); Assert.InRange(Math.Abs(white[2]), 0, 0.01);
        var red = SrgbToLab(1, 0, 0);
        Assert.InRange(red[0], 53.2 - 0.1, 53.2 + 0.1); Assert.InRange(red[1], 80.1 - 0.2, 80.1 + 0.2); Assert.InRange(red[2], 67.2 - 0.2, 67.2 + 0.2);
        var blue = SrgbToLab(0, 0, 1);
        Assert.InRange(blue[0], 32.3 - 0.1, 32.3 + 0.1); Assert.InRange(blue[1], 79.2 - 0.2, 79.2 + 0.2); Assert.InRange(blue[2], -107.9 - 0.2, -107.9 + 0.2);
        // Kovesi 2015: "RGB red and blue have lightness of about 53 and 32, chroma 105 and 134"
        Assert.InRange(Chroma(new Rgb(255, 0, 0)), 104, 106); Assert.InRange(Chroma(new Rgb(0, 0, 255)), 133, 135);
    }

    [Fact]
    public void LabRoundTrip_ReturnsTheSameBytes()
    {
        foreach (var c in new[] { NeonBlue, NeonRed, Black, SeqBlue, SeqTeal, SeqGreen, White, Grey, new Rgb(1, 2, 3), new Rgb(254, 128, 0) })
        {
            var lab = SrgbToLab(c);
            Assert.Equal(c, LabToSrgb(lab[0], lab[1], lab[2]));
            var msh = LabToMsh(lab[0], lab[1], lab[2]); var back = MshToLab(msh[0], msh[1], msh[2]);
            for (int k = 0; k < 3; k++) Assert.InRange(Math.Abs(back[k] - lab[k]), 0, 1e-6);
        }
    }

    [Fact]
    public void OutOfGamut_IsBroughtBackAtConstantLightness()
    {
        // a very chromatic cyan at L* 50 is outside sRGB; the result keeps L* and has less chroma
        var c = LabToSrgb(50, -80, -40);
        Assert.InRange(Math.Abs(Lightness(c) - 50), 0, 0.3);
        Assert.InRange(Chroma(c), 10, 89);
    }

    [Fact]
    public void Moreland_CoolWarm_ReproducesHisTables()
    {
        // Table 1: control points red (80, 1.08, 0.5), blue (80, 1.08, −1.1)
        var red = SrgbToMsh(new Rgb(180, 4, 38)); var blue = SrgbToMsh(new Rgb(59, 76, 192));
        Assert.InRange(red[0], 79, 81); Assert.InRange(red[1], 1.07, 1.09); Assert.InRange(red[2], 0.49, 0.51);
        Assert.InRange(blue[0], 79, 81); Assert.InRange(blue[1], 1.07, 1.09); Assert.InRange(blue[2], -1.11, -1.09);
        // Table 2: 0.5 → (221, 221, 221), 0.25 → (141, 176, 254), 0.75 → (244, 154, 123)
        Rgb b = new Rgb(59, 76, 192), r = new Rgb(180, 4, 38);
        AssertNear(new Rgb(221, 221, 221), Moreland(b, r, 0.5), 1);
        AssertNear(new Rgb(141, 176, 254), Moreland(b, r, 0.25), 2);
        AssertNear(new Rgb(244, 154, 123), Moreland(b, r, 0.75), 2);
        Assert.Equal(b, Moreland(b, r, 0)); Assert.Equal(r, Moreland(b, r, 1));
    }

    static void AssertNear(Rgb expected, Rgb actual, int tol)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= tol && Math.Abs(expected.G - actual.G) <= tol && Math.Abs(expected.B - actual.B) <= tol,
            $"expected {expected} ± {tol}, got {actual}");
    }

    [Fact]
    public void DivergingTable_EndsCentreHueAndLightness()
    {
        var t = DivergingTable;
        Assert.Equal(TableSize, t.Length);
        Assert.Equal(NeonBlue, t[0]); Assert.Equal(NeonRed, t[TableSize - 1]);
        Assert.Equal(t[128], Diverging(0.5));
        // the centre is the white control point: L* 95, no chroma
        Assert.InRange(Lightness(t[128]), CentreLightness - 0.5, CentreLightness + 0.5);
        Assert.InRange(Chroma(t[128]), 0, 1.0);
        // Moreland: each half keeps its hue (the end is more saturated than the white, so AdjustHue leaves h alone)
        double hb = Hue(NeonBlue) * 180 / Math.PI, hr = Hue(NeonRed) * 180 / Math.PI;
        double worstB = 0, worstR = 0;
        for (int i = 0; i < 128; i++) if (Chroma(t[i]) > 10) worstB = Math.Max(worstB, Math.Abs(Hue(t[i]) * 180 / Math.PI - hb));
        for (int i = 129; i < TableSize; i++) if (Chroma(t[i]) > 10) worstR = Math.Max(worstR, Math.Abs(Hue(t[i]) * 180 / Math.PI - hr));
        // Moreland's AdjustHue spins the hue of an end that is less saturated (smaller M) than the white it fades to:
        // the neon red (M 92) is, the neon blue (M 114) is not
        var mr = SrgbToMsh(NeonRed); var mb = SrgbToMsh(NeonBlue);
        double spinR = Math.Abs(AdjustHue(mr[0], mr[1], mr[2], CentreLightness) - mr[2]) * 180 / Math.PI;
        double spinB = Math.Abs(AdjustHue(mb[0], mb[1], mb[2], CentreLightness) - mb[2]) * 180 / Math.PI;
        _out.WriteLine($"hue drift: blue half {worstB:F2}° (Moreland spin {spinB:F1}°), red half {worstR:F2}° (spin {spinR:F1}°)");
        Assert.Equal(0, spinB, 9); Assert.InRange(spinR, 10, 25);
        Assert.InRange(worstB, 0, 2); Assert.InRange(worstR, 0, spinR + 2);
        // lightness rises monotonically to the centre and falls after it
        for (int i = 1; i <= 128; i++) Assert.True(Lightness(t[i]) >= Lightness(t[i - 1]) - 0.05, $"L* not monotone at {i}");
        for (int i = 129; i < TableSize; i++) Assert.True(Lightness(t[i]) <= Lightness(t[i - 1]) + 0.05, $"L* not monotone at {i}");
        _out.WriteLine($"L*: blue end {Lightness(t[0]):F1}, centre {Lightness(t[128]):F1}, red end {Lightness(t[256]):F1}");
    }

    [Fact]
    public void DivergingTable_LightnessStepsAreUniform()
    {
        // Kovesi 2015: successive entries differ by the same lightness. Measured over windows of 8 entries
        // (8-bit rounding makes single steps jitter by ±0.4 L*), each half within 10 % of its mean.
        var t = DivergingTable;
        foreach (var (from, to, name) in new[] { (0, 128, "blue half"), (128, 256, "red half") })
        {
            double mean = Math.Abs(Lightness(t[to]) - Lightness(t[from])) / (to - from);
            double worst = 0;
            for (int i = from; i + 8 <= to; i += 4)
            {
                double step = Math.Abs(Lightness(t[i + 8]) - Lightness(t[i])) / 8;
                worst = Math.Max(worst, Math.Abs(step / mean - 1));
            }
            _out.WriteLine($"{name}: mean ΔL* {mean:F3} per entry, worst window deviation {100 * worst:F1} %");
            Assert.InRange(worst, 0, 0.10);
        }
        // and the old RGB-interpolated ramp of the app, for the record: its steps varied by far more
        var old = new Rgb[257];
        double[][] st = { new[] { 0.10, 0.12, 0.85 }, new[] { 0.45, 0.52, 1.00 }, new[] { 0.97, 0.97, 0.97 }, new[] { 1.00, 0.45, 0.55 }, new[] { 0.85, 0.05, 0.20 } };
        for (int i = 0; i < 257; i++) { double x = i / 64.0; int j = Math.Min(3, (int)Math.Floor(x)); double f = x - j; old[i] = new Rgb((int)Math.Round(255 * (st[j][0] + (st[j + 1][0] - st[j][0]) * f)), (int)Math.Round(255 * (st[j][1] + (st[j + 1][1] - st[j][1]) * f)), (int)Math.Round(255 * (st[j][2] + (st[j + 1][2] - st[j][2]) * f))); }
        double oldWorst = 0, oldMean = Math.Abs(Lightness(old[128]) - Lightness(old[0])) / 128;
        for (int i = 0; i + 8 <= 128; i += 4) oldWorst = Math.Max(oldWorst, Math.Abs(Math.Abs(Lightness(old[i + 8]) - Lightness(old[i])) / 8 / oldMean - 1));
        _out.WriteLine($"old RGB ramp, blue half: worst window deviation {100 * oldWorst:F0} %");
        Assert.True(oldWorst > 0.3, "the old ramp was expected to be far from uniform");
    }

    [Fact]
    public void SequentialTable_MonotoneUniformAndNotGrey()
    {
        var t = SequentialTable;
        Assert.Equal(TableSize, t.Length);
        Assert.Equal(Black, t[0]); Assert.Equal(White, t[TableSize - 1]);
        for (int i = 1; i < TableSize; i++) Assert.True(Lightness(t[i]) >= Lightness(t[i - 1]) - 0.05, $"L* not monotone at {i}");
        double mean = (Lightness(t[256]) - Lightness(t[0])) / 256, worst = 0;
        for (int i = 0; i + 8 <= 256; i += 4) worst = Math.Max(worst, Math.Abs((Lightness(t[i + 8]) - Lightness(t[i])) / 8 / mean - 1));
        double minChroma = 1e9; for (int i = 24; i < 232; i++) minChroma = Math.Min(minChroma, Chroma(t[i]));
        _out.WriteLine($"sequential: mean ΔL* {mean:F3}, worst window deviation {100 * worst:F1} %, min chroma inside {minChroma:F1}");
        Assert.InRange(worst, 0, 0.12);
        Assert.True(minChroma > 20, "the map greys out in the middle");
        // the neon blue and green are passed through (the nearest entry within 2 units per channel)
        Assert.True(t.Any(c => Math.Abs(c.R - SeqBlue.R) <= 2 && Math.Abs(c.G - SeqBlue.G) <= 2 && Math.Abs(c.B - SeqBlue.B) <= 2), "neon blue missing");
        Assert.True(t.Any(c => Math.Abs(c.R - SeqGreen.R) <= 2 && Math.Abs(c.G - SeqGreen.G) <= 2 && Math.Abs(c.B - SeqGreen.B) <= 2), "neon green missing");
    }

    [Fact]
    public void Utilisation_KeepsTheAppsThresholds()
    {
        // the web app's ramp (colormaps.js, 1.3.0): green 0, yellow-green 0.7, neon yellow just below 1, neon red at 1, dark red to 1.3, beyond
        static Rgb App(double u)
        {
            double[] g = { 0.00, 0.85, 0.42 }, yg = { 0.55, 0.95, 0.10 }, y = { 0.85, 1.00, 0.00 }, r = { 1.00, 0.12, 0.31 }, dr = { 0.75, 0.02, 0.18 };
            double[] Mix(double[] a, double[] b, double t) => new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
            double[] c = u < 0.7 ? Mix(g, yg, u / 0.7) : u < 1.0 ? Mix(yg, y, (u - 0.7) / 0.3) : u < 1.3 ? Mix(r, dr, (u - 1.0) / 0.3) : new[] { 0.35, 0.00, 0.08 };
            return new Rgb((int)Math.Round(255 * c[0]), (int)Math.Round(255 * c[1]), (int)Math.Round(255 * c[2]));
        }
        foreach (double u in new[] { 0, 0.1, 0.35, 0.69, 0.7, 0.85, 0.995, 0.999, 1.0, 1.001, 1.15, 1.29, 1.3, 2, 7 })
            AssertNear(App(u), Utilisation(u), 1);
        Assert.Equal(new Rgb(0, 217, 107), Utilisation(0));
        Assert.Equal(new Rgb(255, 31, 79), Utilisation(1.0));
        Assert.Equal(UtilisationOver, Utilisation(1.3));
        Assert.Equal(Grey, Utilisation(double.NaN));
        Assert.Equal(Utilisation(0), Utilisation(-0.2));
        // the steps at the limit (u = 1) and at 1.3 are the only discontinuities (as in the app): every other neighbour pair is close
        var t = UtilisationTable;
        for (int i = 1; i < t.Length; i++)
        {
            int d = Math.Abs(t[i].R - t[i - 1].R) + Math.Abs(t[i].G - t[i - 1].G) + Math.Abs(t[i].B - t[i - 1].B);
            if (i == 200) Assert.True(d > 200, "the step at u = 1 is missing");
            else if (i == 260) Assert.True(d > 100, "the step at u = 1.3 is missing");
            else Assert.True(d <= 6, $"jump of {d} at entry {i}");
        }
    }

    [Fact]
    public void Index_NearestEntry()
    {
        Assert.Equal(0, Index(-1)); Assert.Equal(0, Index(0)); Assert.Equal(256, Index(1)); Assert.Equal(256, Index(2));
        Assert.Equal(128, Index(0.5)); Assert.Equal(128, Index(0.5019)); Assert.Equal(129, Index(0.502)); Assert.Equal(128, Index(double.NaN));
    }

    [Fact]
    public void Map_RobustSymmetricDomainAndGreyForNaN()
    {
        var values = Enumerable.Range(0, 1000).Select(i => -0.3 + 0.001 * i).ToList(); // −0.3 … 0.699
        values[10] = 50; values[20] = -50; values[30] = double.NaN;
        var m = Map(values, Palette.Diverging);
        Assert.True(m.Automatic);
        Assert.InRange(m.Hi, 0.66, 0.70); Assert.Equal(-m.Hi, m.Lo);     // 98th percentile, symmetric
        Assert.Equal(Grey, m.Colours[30]);
        Assert.Equal(NeonRed, m.Colours[10]); Assert.Equal(NeonBlue, m.Colours[20]); // clamped
        int zero = values.IndexOf(values.First(v => Math.Abs(v) < 1e-9));
        Assert.Equal(Diverging(0.5), m.Colours[zero]);
        var s = Map(values, Palette.Sequential);
        Assert.InRange(s.Lo, -0.29, -0.27); Assert.InRange(s.Hi, 0.66, 0.70);
        var e = Map(values, Palette.Diverging, -1, 1);
        Assert.False(e.Automatic); Assert.Equal(-1, e.Lo); Assert.Equal(1, e.Hi);
        var u = Map(new[] { 0.0, 1.0, 2.0, double.NaN }, Palette.Utilisation, -5, 5);
        Assert.Equal(0, u.Lo); Assert.Equal(1.3, u.Hi, 9); Assert.Equal(Utilisation(1), u.Colours[1]); Assert.Equal(Grey, u.Colours[3]);
        Assert.Equal(-1, Map(new double[0], Palette.Diverging).Lo);
    }

    [Theory]
    [InlineData(105.0, 543.0, 100.0, "200 300 400 500", "105", "543")]
    [InlineData(2.03, 2.17, 0.05, "2.05 2.10 2.15", "2.03", "2.17")]
    [InlineData(-0.37, 0.37, 0.2, "-0.2 0.0 0.2", "-0.37", "0.37")]
    [InlineData(0.0, 1.3, 0.5, "0.0 0.5 1.0", "0", "1.3")]
    [InlineData(-0.0042, 0.0042, 0.002, "-0.004 -0.002 0.000 0.002 0.004", "-0.0042", "0.0042")]
    [InlineData(0.0, 1.0, 0.2, "0.0 0.2 0.4 0.6 0.8 1.0", "0", "1")]
    [InlineData(-1.0, 0.25, 0.5, "-1.0 -0.5 0.0", "-1", "0.25")]
    [InlineData(-123456.0, 1e6, 500000.0, "0 500000 1000000", "-123000", "1000000")]
    public void Legend_Ticks_AreHeckbertsNiceNumbersInsideTheRange(double lo, double hi, double step, string labels, string loLabel, string hiLabel)
    {
        var L = Legend.Build(lo, hi, Palette.Diverging, 16);
        Assert.Equal(step, L.Step, 12);
        Assert.Equal(labels, string.Join(" ", L.Labels));
        Assert.Equal(loLabel, L.LoLabel); Assert.Equal(hiLabel, L.HiLabel);
        foreach (var t in L.Ticks) Assert.InRange(t, lo - 1e-12, hi + 1e-12);
        if (lo < 0 && hi > 0) Assert.Contains(0.0, L.Ticks);
        for (int i = 0; i < L.Ticks.Length; i++) Assert.InRange(L.Positions[i], (L.Ticks[i] - lo) / (hi - lo) - 1e-12, (L.Ticks[i] - lo) / (hi - lo) + 1e-12);
        Assert.Equal(16, L.Colours.Length);
        Assert.Equal(Diverging(0), L.Colours[0]); Assert.Equal(Diverging(1), L.Colours[15]);
    }

    [Fact]
    public void Legend_DegenerateAndSwappedRanges()
    {
        var L = Legend.Build(1, 1, Palette.Sequential);
        Assert.Empty(L.Ticks); Assert.Equal(0, L.Step);
        var S = Legend.Build(5, -5, Palette.Diverging);
        Assert.Equal(-5, S.Lo); Assert.Equal(5, S.Hi); Assert.Equal("-4 -2 0 2 4", string.Join(" ", S.Labels));
        Assert.Equal(Utilisation(0), Legend.Build(0, 1.3, Palette.Utilisation, 3).Colours[0]);
        Assert.Equal(Utilisation(1.3), Legend.Build(0, 1.3, Palette.Utilisation, 3).Colours[2]);
    }

    [Fact]
    public void Legend_Strip_GeometryAndColours()
    {
        var L = Legend.Build(-0.37, 0.37, Palette.Diverging, 64);
        var origin = new Vec3d(2, 3, -1);
        var s = Legend.BuildStrip(L, origin, new Vec3d(0, 0, 2), new Vec3d(3, 0, 0), 4.0, 0.5);
        Assert.Equal(64, s.Mesh.FaceCount); Assert.Equal(130, s.Mesh.VertexCount); Assert.Equal(130, s.VertexColours.Length);
        Assert.All(s.Mesh.Faces, f => Assert.Equal(4, f.Length));
        Assert.Equal(origin, s.Mesh.Vertices[0]);
        Assert.Equal(new Vec3d(2.5, 3, -1), s.Mesh.Vertices[1]);
        Assert.Equal(new Vec3d(2, 3, 3), s.Mesh.Vertices[128]);
        for (int i = 0; i < 64; i++) { Assert.Equal(L.Colours[i], s.VertexColours[2 * i]); Assert.Equal(L.Colours[i], s.VertexColours[2 * i + 1]); }
        Assert.Equal(L.Colours[63], s.VertexColours[129]);
        // ticks −0.2, 0, 0.2 sit at 0.2297, 0.5, 0.7703 of the height, two widths to the right
        Assert.Equal(3, s.TickPoints.Length);
        Assert.InRange(s.TickPoints[1].Z, -1 + 2 - 1e-12, -1 + 2 + 1e-12);
        Assert.InRange(s.TickPoints[0].Z, -1 + 4 * 0.17 / 0.74 - 1e-9, -1 + 4 * 0.17 / 0.74 + 1e-9);
        Assert.Equal(3.0, s.TickPoints[0].X, 12);
        Assert.Equal(new Vec3d(3, 3, -1), s.LoPoint); Assert.Equal(new Vec3d(3, 3, 3), s.HiPoint);
        // every face is a planar quad in the XZ plane with the expected area
        double area = 0;
        foreach (var f in s.Mesh.Faces) { var a = s.Mesh.Vertices[f[0]]; var b = s.Mesh.Vertices[f[1]]; var d = s.Mesh.Vertices[f[3]]; area += Vec3d.Cross(b - a, d - a).Length; }
        Assert.Equal(2.0, area, 9);
    }

    [Fact]
    public void JavaScriptTwin_OnDiskIsCurrent()
    {
        // src/Mite.Web/wwwroot/js/colormap-lut.js is generated from this class by the bench generator;
        // the app and the bench load it, so a stale file would mean different colours on the web
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Mite.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.False(dir == null, "repository root not found");
        string path = Path.Combine(dir!, "src", "Mite.Web", "wwwroot", "js", "colormap-lut.js");
        Assert.True(File.Exists(path), path + " is missing: run `cd tools/webgen && dotnet run -c Release`");
        string expected = JavaScriptModule().Replace("\r\n", "\n"), actual = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.True(expected == actual, "colormap-lut.js is stale: run `cd tools/webgen && dotnet run -c Release`");
        Assert.Contains("'" + NeonBlue.Hex + "'", actual); Assert.Contains("'" + UtilisationOver.Hex.Replace("'", "") + "'", actual);
        Assert.Equal(TableSize * 2 + UtilisationSize, actual.Split('#').Length - 1 - 2); // every table entry once, plus UTILISATION_OVER and NAN_GREY
    }
}
