using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mite.Core.Display;
using Mite.Core.Visualisation;
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
    public void DivergingMap_IsUniformInLightnessOnEachArm()
    {
        // the stops are cut from ColourMap's diverging table (Moreland's path,
        // Kovesi's equalisation): neon blue = family A at the negative end,
        // the white control point (L* 95) at the centre, neon red at the positive end
        var L = LightnessProfile(MitePalette.Diverging);
        int mid = L.Length / 2;
        Assert.InRange(L[mid], ColourMap.CentreLightness - 0.6, ColourMap.CentreLightness + 0.6);
        Assert.Equal(MitePalette.FamilyA(Theme.Light), MitePalette.Diverging[0]); // negative end = family A
        Assert.Equal(ColourMap.NeonRed.Hex, MitePalette.Diverging[^1].Hex);
        // lightness linear along each arm (uniform perceptual rate)
        double dev = 0;
        for (int i = 0; i < L.Length; i++)
        {
            double s = i <= mid ? i / (double)mid : (L.Length - 1 - i) / (double)mid;
            double end = i <= mid ? L[0] : L[^1];
            dev = Math.Max(dev, Math.Abs(L[i] - (end + (L[mid] - end) * s)));
        }
        Assert.True(dev < 1.0, $"max deviation from linear L* {dev:F2}");
        // strictly darker towards both ends
        for (int i = 1; i <= mid; i++) Assert.True(L[i] > L[i - 1]);
        for (int i = mid + 1; i < L.Length; i++) Assert.True(L[i] < L[i - 1]);

        // 1.3.0 (the app's previous stops) for the record: an L* asymmetry of 16 between the arms, no equalisation
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
    public void Maps_AreTheStopsOfTheSharedColourMapTables()
    {
        // one colour language: the stops are samples of ColourMap's tables, so
        // Grasshopper, the web app and the bench cannot drift apart
        void Same(Rgb[] a, ColourMap.Rgb[] table)
        {
            Assert.Equal(13, a.Length);
            for (int i = 0; i < a.Length; i++)
            {
                var c = table[ColourMap.Index(i / 12.0)];
                Assert.True(a[i].R == c.R && a[i].G == c.G && a[i].B == c.B, $"stop {i}: {a[i]} vs {c}");
            }
        }
        Same(MitePalette.Diverging, ColourMap.DivergingTable);
        Same(MitePalette.Sequential, ColourMap.SequentialTable);
        Assert.Equal(ColourMap.NeonBlue.Hex, MitePalette.DivergingPathNegative[0].Hex);
        Assert.Equal(ColourMap.NeonRed.Hex, MitePalette.DivergingPathPositive[0].Hex);
        Assert.Equal(5, MitePalette.SequentialPath.Length);
        // the equal-lightness cutter stays for other paths
        var ramp = MitePalette.EqualLightnessRamp(new[] { new Rgb(0, 0, 0), new Rgb(255, 255, 255) }, 4);
        Assert.Equal(5, ramp.Length);
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

    [Fact]
    public void WebApp_UsesTheSamePalette()
    {
        // the maps reach the app through js/colormap-lut.js (ColourMapTests.JavaScriptTwin_OnDiskIsCurrent); colormaps.js derives its legend stops from it
        string js = File.ReadAllText(RepoFile("src", "Mite.Web", "wwwroot", "js", "colormaps.js"));
        Assert.Contains("from './colormap-lut.js'", js);

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
            Assert.Equal(MitePalette.Seed(th).Hex, v["seed"]);
            Assert.Equal(MitePalette.Mark(th).Hex, v["mark"]);
        }

        // the 3D viewer draws roles, resolved per theme through palette.js (2026-11-07; before, the light theme's
        // constants were hard-coded and drawn on the dark page too): no colour literal apart from its lights
        string viewer = File.ReadAllText(RepoFile("src", "Mite.Web", "wwwroot", "js", "viewer.js"));
        Assert.Contains("from './palette.js'", viewer);
        var literals = Regex.Matches(viewer, @"0x[0-9a-fA-F]{6}|'#[0-9a-fA-F]{6}'").Cast<Match>().Select(m => m.Value.ToLowerInvariant()).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "0x000000", "0x8a8f99", "0xffffff" }, literals); // transparent clear / emissive off, the hemisphere light's ground, white lights and the vertex-colour multiplier
        foreach (string role in new[] { "'famA'", "'famB'", "'famG'", "'seed'", "'solid'", "'highlight'", "'ink'", "'mesh'", "'paper'" }) Assert.Contains(role, viewer);
        Assert.DoesNotContain("FAMILY_A", viewer); Assert.DoesNotContain("NEON", viewer);
        // app.js passes roles too: no colour literal, no viewer constants
        string app = File.ReadAllText(RepoFile("src", "Mite.Web", "wwwroot", "js", "app.js"));
        Assert.Empty(Regex.Matches(app, @"0x[0-9a-fA-F]{6}|'#[0-9a-fA-F]{6}'|#[0-9a-fA-F]{6}\b"));
        Assert.DoesNotContain("NEON", app); Assert.DoesNotContain("FAMILY_", app); Assert.DoesNotContain("SEED_COLOR", app);
        foreach (string role in new[] { "'mark'", "'seed'", "'famA'", "'famB'", "'ink'", "'solid'", "'warn'", "var(--seed)", "var(--${" }) Assert.Contains(role, app);
    }

    [Fact]
    public void JavaScriptTwin_OnDiskIsCurrent()
    {
        // src/Mite.Web/wwwroot/js/palette.js is generated from MitePalette by the bench generator; the viewer resolves its roles through it
        string path = RepoFile("src", "Mite.Web", "wwwroot", "js", "palette.js");
        Assert.True(File.Exists(path), path + " is missing: run `cd tools/webgen && dotnet run -c Release`");
        string expected = MitePalette.JavaScriptModule().Replace("\r\n", "\n"), actual = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.True(expected == actual, "palette.js is stale: run `cd tools/webgen && dotnet run -c Release`");
        foreach (var th in new[] { Theme.Light, Theme.Dark })
        {
            string block = Regex.Match(actual, @"export const " + (th == Theme.Light ? "LIGHT" : "DARK") + @" = \{(.*?)\};", RegexOptions.Singleline).Groups[1].Value;
            var pairs = Regex.Matches(block, @"(\w+): '(#[0-9a-f]{6})'").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            Assert.Equal(MitePalette.ThemeColourNames, pairs.Keys.ToArray());
            Assert.Equal(MitePalette.FamilyA(th).Hex, pairs["famA"]); Assert.Equal(MitePalette.FamilyB(th).Hex, pairs["famB"]); Assert.Equal(MitePalette.FamilyG(th).Hex, pairs["famG"]);
            Assert.Equal(MitePalette.Seed(th).Hex, pairs["seed"]); Assert.Equal(MitePalette.Mark(th).Hex, pairs["mark"]); Assert.Equal(MitePalette.Solid(th).Hex, pairs["solid"]);
            Assert.Equal(MitePalette.Mesh.Hex, pairs["mesh"]); Assert.Equal(MitePalette.Highlight.Hex, pairs["highlight"]);
            var export = (Dictionary<string, string>)MitePalette.Export()[th == Theme.Light ? "light" : "dark"];
            Assert.Equal(export.OrderBy(kv => kv.Key), pairs.OrderBy(kv => kv.Key));
        }
        Assert.Contains("export function colour(theme, role)", actual); Assert.Contains("export function palette(dark)", actual);
    }

    [Fact]
    public void ViewerRoles_ReadOnTheirPage_AndTheDarkPageGainsOverTheLightConstants()
    {
        // 1.6.0 drew the light theme's family colours, neon green seeds (#00d86b) and neon yellow marks (#d9ff00) on both pages
        var black = MitePalette.Paper(Theme.Dark); var white = MitePalette.Paper(Theme.Light);
        double C(Rgb a, Rgb b) => ColourScience.ContrastRatio(a, b);
        Assert.Equal(3.19, C(MitePalette.FamilyA(Theme.Light), black), 2); Assert.Equal(4.99, C(MitePalette.FamilyA(Theme.Dark), black), 2);
        Assert.Equal(5.56, C(MitePalette.FamilyB(Theme.Light), black), 2); Assert.Equal(6.13, C(MitePalette.FamilyB(Theme.Dark), black), 2);
        Assert.Equal(6.58, C(MitePalette.FamilyG(Theme.Light), black), 2); Assert.Equal(15.64, C(MitePalette.FamilyG(Theme.Dark), black), 2);
        Assert.Equal(1.90, C(Rgb.FromHex("#00d86b"), white), 2); Assert.Equal(3.19, C(MitePalette.Seed(Theme.Light), white), 2);
        Assert.Equal(1.15, C(Rgb.FromHex("#d9ff00"), white), 2); Assert.Equal(19.80, C(MitePalette.Mark(Theme.Light), white), 2);
        foreach (var th in new[] { Theme.Light, Theme.Dark })
        {
            var paper = MitePalette.Paper(th);
            // WCAG 2.1 SC 1.4.11: 3:1 for graphical objects against the adjacent colour
            Assert.True(C(MitePalette.Seed(th), paper) >= 3.0, $"{th} seed {C(MitePalette.Seed(th), paper):F2}");
            Assert.True(C(MitePalette.Mark(th), paper) >= 3.0, $"{th} mark {C(MitePalette.Mark(th), paper):F2}");
            Assert.True(C(MitePalette.Solid(th), paper) >= 7.0);
            // the marks tell apart from every family for simulated protanopes and deuteranopes (Crameri et al. 2020):
            // on black neon yellow ΔE ≥ 37, on white ink ΔE ≥ 42 — an olive dark enough for 3:1 on white would sit at ΔE ≤ 24 from family B
            foreach (var fam in new[] { MitePalette.FamilyA(th), MitePalette.FamilyB(th), MitePalette.FamilyG(th) })
                Assert.True(ColourScience.WorstCaseDeltaE(MitePalette.Mark(th), fam) >= 35, $"{th} mark vs {fam}: {ColourScience.WorstCaseDeltaE(MitePalette.Mark(th), fam):F1}");
        }
        Assert.InRange(ColourScience.WorstCaseDeltaE(Rgb.FromHex("#5f6e00"), MitePalette.FamilyB(Theme.Light)), 10, 24); // the olive that was not chosen
        Assert.Equal(MitePalette.Ink(Theme.Light), MitePalette.Mark(Theme.Light)); Assert.Equal("#d9ff00", MitePalette.Mark(Theme.Dark).Hex);
        Assert.Equal(MitePalette.Ok(Theme.Light), MitePalette.Seed(Theme.Light)); Assert.Equal(MitePalette.Neon, MitePalette.Seed(Theme.Dark));
        Assert.Equal("#dbdbdb", MitePalette.Mesh.Hex); // 0.86 × 255, the viewer's grey since 1.3.0
        Assert.Equal(MitePalette.ThemeColourNames.Length, ((Dictionary<string, string>)MitePalette.Export()["light"]).Count);
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
        Assert.Equal((MitePalette.Sequential[0].R + MitePalette.Sequential[1].R) / 2.0, h.R, 9);
        Assert.Equal(MitePalette.Diverging[0], MitePalette.DivergingColour(-5, 1));
        Assert.Equal(MitePalette.Diverging[12], MitePalette.DivergingColour(5, 1));
    }
}
