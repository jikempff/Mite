using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mite.Core.Display;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// One palette for Grasshopper, the web app and the bench (black, white and
/// neon), checked against colour science with exact references: CIE 15:2004
/// CIELAB, WCAG 2.1 contrast, Machado et al. 2009 dichromat simulation, and
/// Kovesi 2015's rule that lightness must change at a uniform rate.
/// </summary>
public class PaletteTests
{
    private static double[] LightnessProfile(Rgb[] stops, int n = 401)
        => Enumerable.Range(0, n).Select(i => { var c = MitePalette.SampleExact(stops, i / (double)(n - 1)); return ColourScience.Lightness(c.R, c.G, c.B); }).ToArray();

    [Fact]
    public void ColourScience_MatchesPublishedReferences()
    {
        // CIELAB (D65) of the sRGB primaries and white/black (Lindbloom; CIE 15:2004)
        var w = ColourScience.ToLab(new Rgb(255, 255, 255));
        Assert.InRange(w.L, 99.99, 100.01); Assert.InRange(Math.Abs(w.A) + Math.Abs(w.B), 0, 0.02);
        Assert.InRange(ColourScience.Lightness(new Rgb(0, 0, 0)), 0, 1e-9);
        var red = ColourScience.ToLab(new Rgb(255, 0, 0));
        Assert.InRange(red.L, 53.2, 53.3); Assert.InRange(red.A, 80.0, 80.2); Assert.InRange(red.B, 67.1, 67.3);
        var blue = ColourScience.ToLab(new Rgb(0, 0, 255));
        Assert.InRange(blue.L, 32.2, 32.4); Assert.InRange(blue.A, 79.1, 79.3); Assert.InRange(blue.B, -107.9, -107.7);
        // middle grey: L* 53.59 for sRGB 128
        Assert.InRange(ColourScience.Lightness(new Rgb(128, 128, 128)), 53.5, 53.7);

        // WCAG 2.1: black on white is 21:1, #777777 on white 4.48:1 (the classic just-fails-AA grey)
        Assert.Equal(21.0, ColourScience.ContrastRatio(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 9);
        Assert.InRange(ColourScience.ContrastRatio(Rgb.FromHex("#777777"), new Rgb(255, 255, 255)), 4.47, 4.49);

        // Machado et al. 2009 rows sum to 1: greys are seen unchanged by dichromats
        foreach (var t in new[] { Cvd.Protanopia, Cvd.Deuteranopia })
            foreach (int g in new[] { 0, 64, 128, 200, 255 })
            {
                var s = ColourScience.Simulate(new Rgb(g, g, g), t);
                Assert.InRange(Math.Abs(s.R - g) + Math.Abs(s.G - g) + Math.Abs(s.B - g), 0, 2);
            }
        // and pure red loses most of its luminance for a protanope
        Assert.True(ColourScience.Lightness(ColourScience.Simulate(new Rgb(255, 0, 0), Cvd.Protanopia)) < 45);

        Assert.Equal("#2a3cff", Rgb.FromHex("#2A3CFF").Hex);
    }

    [Fact]
    public void DivergingMap_IsSymmetricAndUniformInLightness()
    {
        var L = LightnessProfile(MitePalette.Diverging);
        int mid = L.Length / 2;
        double asym = Enumerable.Range(0, mid).Max(i => Math.Abs(L[i] - L[L.Length - 1 - i]));
        Assert.True(asym < 0.6, $"L* asymmetry {asym:F2}");
        Assert.InRange(Math.Abs(L[0] - L[^1]), 0, 0.05);           // equal end lightness (Kovesi, Moreland)
        Assert.InRange(L[mid], 97.0, 97.5);                         // white centre
        Assert.Equal(MitePalette.FamilyA(Theme.Light), MitePalette.Diverging[0]); // negative end = family A
        // lightness linear along each arm (uniform perceptual rate)
        double dev = 0;
        for (int i = 0; i < L.Length; i++)
        {
            double s = i <= mid ? i / (double)mid : (L.Length - 1 - i) / (double)mid;
            dev = Math.Max(dev, Math.Abs(L[i] - (L[0] + (L[mid] - L[0]) * s)));
        }
        Assert.True(dev < 1.0, $"max deviation from linear L* {dev:F2}");
        // strictly darker towards both ends
        for (int i = 1; i <= mid; i++) Assert.True(L[i] > L[i - 1]);
        for (int i = mid + 1; i < L.Length; i++) Assert.True(L[i] < L[i - 1]);

        // 1.3.0 (the app's previous stops) for the record: blue end L* 29.8, red end 46.0
        var old = new[] { (0.00, 0.10, 0.12, 0.85), (0.25, 0.45, 0.52, 1.00), (0.50, 0.97, 0.97, 0.97), (0.75, 1.00, 0.45, 0.55), (1.00, 0.85, 0.05, 0.20) };
        double OldL(double t)
        {
            for (int i = 1; i < old.Length; i++)
                if (t <= old[i].Item1)
                {
                    double f = (t - old[i - 1].Item1) / (old[i].Item1 - old[i - 1].Item1);
                    return ColourScience.Lightness(255 * (old[i - 1].Item2 + (old[i].Item2 - old[i - 1].Item2) * f),
                        255 * (old[i - 1].Item3 + (old[i].Item3 - old[i - 1].Item3) * f), 255 * (old[i - 1].Item4 + (old[i].Item4 - old[i - 1].Item4) * f));
                }
            return 0;
        }
        double oldAsym = Enumerable.Range(0, 200).Max(i => Math.Abs(OldL(i / 400.0) - OldL(1 - i / 400.0)));
        Assert.InRange(oldAsym, 16.0, 16.3);
    }

    [Fact]
    public void SequentialMap_IsMonotoneAndUniformInLightness()
    {
        var L = LightnessProfile(MitePalette.Sequential);
        for (int i = 1; i < L.Length; i++) Assert.True(L[i] > L[i - 1], $"L* not increasing at {i}");
        double dev = Enumerable.Range(0, L.Length).Max(i => Math.Abs(L[i] - (L[0] + (L[^1] - L[0]) * i / (L.Length - 1.0))));
        Assert.True(dev < 1.0, $"max deviation from linear L* {dev:F2}");
        Assert.InRange(L[0], 2.5, 3.0); Assert.InRange(L[^1], 99.0, 99.2);
    }

    [Fact]
    public void Maps_AreTheEqualLightnessCutsOfTheirPaths()
    {
        // the stored stops are reproducible: cut the sRGB paths at equal L* steps
        var neg = MitePalette.EqualLightnessRamp(MitePalette.DivergingPathNegative, 6);
        var pos = MitePalette.EqualLightnessRamp(MitePalette.DivergingPathPositive, 6);
        var div = neg.Concat(pos.Take(6).Reverse()).ToArray();
        var seq = MitePalette.EqualLightnessRamp(MitePalette.SequentialPath, 12);
        void Same(Rgb[] a, Rgb[] b)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.True(Math.Abs(a[i].R - b[i].R) <= 1 && Math.Abs(a[i].G - b[i].G) <= 1 && Math.Abs(a[i].B - b[i].B) <= 1, $"stop {i}: {a[i]} vs {b[i]}");
        }
        Same(div, MitePalette.Diverging);
        Same(seq, MitePalette.Sequential);
        // the positive end is family B's hue darkened to family A's lightness
        Assert.InRange(Math.Abs(ColourScience.Lightness(MitePalette.DivergingPathPositive[0]) - ColourScience.Lightness(MitePalette.FamilyA(Theme.Light))), 0, 0.05);
        var hb = ColourScience.ToLab(MitePalette.FamilyB(Theme.Light)); var he = ColourScience.ToLab(MitePalette.DivergingPathPositive[0]);
        Assert.InRange(Math.Abs(Math.Atan2(hb.B, hb.A) - Math.Atan2(he.B, he.A)) * 180 / Math.PI, 0, 2.0);
        Assert.Throws<ArgumentException>(() => MitePalette.EqualLightnessRamp(new[] { new Rgb(255, 255, 255), new Rgb(0, 0, 0) }, 4));
    }

    [Fact]
    public void Utilization_MatchesTheAppAndStepsInLightnessAtTheLimit()
    {
        // the app's utilizationColor (colormaps.js), channel by channel
        Assert.Equal(new Rgb(0, 217, 107), MitePalette.Utilization(0));
        Assert.Equal(new Rgb(140, 242, 26), MitePalette.Utilization(0.7));
        Assert.Equal(new Rgb(255, 31, 79), MitePalette.Utilization(1.0));
        Assert.Equal(new Rgb(89, 0, 20), MitePalette.Utilization(1.3));
        Assert.Equal(new Rgb(89, 0, 20), MitePalette.Utilization(7));
        Assert.Equal(MitePalette.NoData, MitePalette.Utilization(double.NaN));
        double below = ColourScience.Lightness(MitePalette.Utilization(0.999)), above = ColourScience.Lightness(MitePalette.Utilization(1.0));
        Assert.True(below - above > 35, $"lightness step at the limit {below:F1} → {above:F1}");
        // the step survives both dichromacies
        foreach (var t in new[] { Cvd.Protanopia, Cvd.Deuteranopia })
            Assert.True(ColourScience.Lightness(ColourScience.Simulate(MitePalette.Utilization(0.999), t))
                        - ColourScience.Lightness(ColourScience.Simulate(MitePalette.Utilization(1.0), t)) > 25);
    }

    [Fact]
    public void FamilyColours_ReadOnTheirPageAndForDichromats()
    {
        foreach (var th in new[] { Theme.Light, Theme.Dark })
        {
            var paper = MitePalette.Paper(th);
            foreach (var c in new[] { MitePalette.FamilyA(th), MitePalette.FamilyB(th), MitePalette.FamilyG(th) })
                Assert.True(ColourScience.ContrastRatio(c, paper) >= 3.0, $"{th} {c}: {ColourScience.ContrastRatio(c, paper):F2}:1 (WCAG 1.4.11 asks 3:1)");
            Assert.True(ColourScience.ContrastRatio(MitePalette.Ink(th), paper) >= 7.0);
            Assert.True(ColourScience.ContrastRatio(MitePalette.Muted(th), paper) >= 4.5);
            // A and B — the two families of every net — stay far apart for protanopes and deuteranopes
            Assert.True(ColourScience.WorstCaseDeltaE(MitePalette.FamilyA(th), MitePalette.FamilyB(th)) > 60);
        }
        // the bench's 1.3.0 teal / plum collapsed for dichromats (ΔE ≈ 15)
        Assert.InRange(ColourScience.WorstCaseDeltaE(Rgb.FromHex("#0F7B8A"), Rgb.FromHex("#9A3D6E")), 10, 16);
    }

    // ---------- identity with the web app ----------

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mite.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    private static Rgb[] JsStops(string js, string name)
    {
        var m = Regex.Match(js, @"const " + name + @" = evenStops\(\[(.*?)\]\);", RegexOptions.Singleline);
        Assert.True(m.Success, name + " not found in colormaps.js");
        return Regex.Matches(m.Groups[1].Value, @"\[\s*(\d+),\s*(\d+),\s*(\d+)\s*\]").Cast<Match>()
            .Select(x => new Rgb(int.Parse(x.Groups[1].Value), int.Parse(x.Groups[2].Value), int.Parse(x.Groups[3].Value))).ToArray();
    }

    [Fact]
    public void WebApp_UsesTheSamePalette()
    {
        string js = File.ReadAllText(RepoFile("src", "Mite.Web", "wwwroot", "js", "colormaps.js"));
        Assert.Equal(MitePalette.Diverging, JsStops(js, "DIVERGING"));
        Assert.Equal(MitePalette.Sequential, JsStops(js, "SEQUENTIAL"));

        string css = File.ReadAllText(RepoFile("src", "Mite.Web", "wwwroot", "css", "app.css"));
        Dictionary<string, string> Vars(string selector)
        {
            int i = css.IndexOf(selector + " {", StringComparison.Ordinal);
            Assert.True(i >= 0, selector);
            string block = css.Substring(i, css.IndexOf('}', i) - i);
            return Regex.Matches(block, @"--([\w-]+):\s*(#[0-9a-fA-F]{6})").Cast<Match>().ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value.ToLowerInvariant());
        }
        foreach (var (sel, th) in new[] { (":root", Theme.Light), (":root[data-theme=\"dark\"]", Theme.Dark) })
        {
            var v = Vars(sel);
            Assert.Equal(MitePalette.Paper(th).Hex, v["bg"]);
            Assert.Equal(MitePalette.Surface(th).Hex, v["surface"]);
            Assert.Equal(MitePalette.Surface2(th).Hex, v["surface2"]);
            Assert.Equal(MitePalette.Faint(th).Hex, v["faint"]);
            Assert.Equal(MitePalette.Ink(th).Hex, v["ink"]);
            Assert.Equal(MitePalette.Muted(th).Hex, v["muted"]);
            Assert.Equal(MitePalette.Line(th).Hex, v["line"]);
            Assert.Equal(MitePalette.FamilyA(th).Hex, v["famA"]);
            Assert.Equal(MitePalette.FamilyB(th).Hex, v["famB"]);
            Assert.Equal(MitePalette.FamilyG(th).Hex, v["famG"]);
            Assert.Equal(MitePalette.Ok(th).Hex, v["ok"]);
            Assert.Equal(MitePalette.Warn(th).Hex, v["warn"]);
        }

        // the 3D viewer's family colours are the light theme's
        string viewer = File.ReadAllText(RepoFile("src", "Mite.Web", "wwwroot", "js", "viewer.js"));
        foreach (var (name, c) in new[] { ("FAMILY_A", MitePalette.FamilyA(Theme.Light)), ("FAMILY_B", MitePalette.FamilyB(Theme.Light)), ("FAMILY_G", MitePalette.FamilyG(Theme.Light)) })
        {
            var m = Regex.Match(viewer, @"export const " + name + @" = 0x([0-9a-fA-F]{6});");
            Assert.True(m.Success, name);
            Assert.Equal(c.Hex, "#" + m.Groups[1].Value.ToLowerInvariant());
        }
    }

    [Fact]
    public void Export_CarriesEveryMapForTheBench()
    {
        var e = MitePalette.Export();
        Assert.Equal(13, ((int[][])e["diverging"]).Length);
        Assert.Equal(13, ((int[][])e["sequential"]).Length);
        Assert.Equal(131, ((int[][])e["utilization"]).Length);
        Assert.Equal("#2a3cff", ((Dictionary<string, string>)e["light"])["famA"]);
        Assert.Equal("#000000", ((Dictionary<string, string>)e["dark"])["paper"]);
        // Sample at a stop returns the stop; halfway between two stops is their mean
        Assert.Equal(MitePalette.Diverging[6], MitePalette.Sample(MitePalette.Diverging, 0.5));
        var h = MitePalette.SampleExact(MitePalette.Sequential, 0.5 / 12);
        Assert.Equal((10 + 18) / 2.0, h.R, 9);
        Assert.Equal(MitePalette.Diverging[0], MitePalette.DivergingColour(-5, 1));
        Assert.Equal(MitePalette.Diverging[12], MitePalette.DivergingColour(5, 1));
    }
}
