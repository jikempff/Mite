using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mite.Core.Display;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// The palette in a Rhino viewport (ViewportPalette): the page colours on a
/// white or black background, WCAG 2.1's 3:1 (SC 1.4.11) kept on every grey by
/// darkening in linear light (chromaticity preserved) or lightening towards
/// white (hue preserved), and the numbers of 2026-11-07 as the record.
/// </summary>
public class ViewportPaletteTests
{
    private static readonly Rgb White = new Rgb(255, 255, 255), Black = new Rgb(0, 0, 0);
    private static double CR(Rgb a, Rgb b) => ColourScience.ContrastRatio(a, b);

    // what the Mite Net preview drew until 1.6.0 (the web app's 1.2 teal / plum, amber T-ends, red free ends)
    private static readonly Rgb OldA = new Rgb(15, 118, 110), OldB = new Rgb(157, 47, 107), OldT = new Rgb(245, 158, 13), OldFree = new Rgb(204, 38, 38);

    [Fact]
    public void OnTheWhiteAndTheBlackPage_EveryRoleIsThePageColour()
    {
        Assert.Equal(Theme.Light, ViewportPalette.ThemeFor(White));
        Assert.Equal(Theme.Dark, ViewportPalette.ThemeFor(Black));
        foreach (var role in ViewportPalette.Roles)
        {
            Assert.Equal(ViewportPalette.PageColour(role, Theme.Light), ViewportPalette.Colour(role, White));
            Assert.Equal(ViewportPalette.PageColour(role, Theme.Dark), ViewportPalette.Colour(role, Black));
        }
        // the page colours are MitePalette's, the marks the web app's rule (ink on white, neon yellow on black)
        Assert.Equal(MitePalette.FamilyA(Theme.Light), ViewportPalette.Colour(ViewportRole.FamilyA, White));
        Assert.Equal(MitePalette.FamilyB(Theme.Dark), ViewportPalette.Colour(ViewportRole.FamilyB, Black));
        Assert.Equal(MitePalette.FamilyG(Theme.Light), ViewportPalette.Colour(ViewportRole.FamilyG, White));
        Assert.Equal(MitePalette.Ink(Theme.Light), ViewportPalette.Colour(ViewportRole.Mark, White));
        Assert.Equal("#d9ff00", ViewportPalette.Colour(ViewportRole.Mark, Black).Hex);
        Assert.Equal(MitePalette.Warn(Theme.Light), ViewportPalette.Colour(ViewportRole.Warn, White));
        Assert.Equal(MitePalette.Ink(Theme.Dark), ViewportPalette.Colour(ViewportRole.Ink, Black));
        // and every one of them already has 3:1 there (WCAG 2.1, 1.4.11), which is why nothing is changed
        foreach (var role in ViewportPalette.Roles)
        {
            Assert.True(CR(ViewportPalette.PageColour(role, Theme.Light), White) >= 3.0, role.ToString());
            Assert.True(CR(ViewportPalette.PageColour(role, Theme.Dark), Black) >= 3.0, role.ToString());
        }
    }

    [Theory]
    [InlineData(0xa0a0a0)] // a mid-light grey of the kind Rhino's viewports default to
    [InlineData(0xc0c0c0)]
    [InlineData(0xf0f0f0)]
    [InlineData(0x808080)]
    [InlineData(0x777777)] // L* 50.0, the split
    [InlineData(0x767676)] // just below it
    [InlineData(0x606060)]
    [InlineData(0x404040)]
    [InlineData(0x202020)]
    [InlineData(0xb4c8dc)] // a bluish gradient's mean
    [InlineData(0x3c4a2e)] // a dark olive
    public void OnAnyBackground_EveryRoleKeepsThreeToOne(int hex)
    {
        var bg = new Rgb((hex >> 16) & 255, (hex >> 8) & 255, hex & 255);
        var colours = ViewportPalette.Colours(bg);
        Assert.Equal(ViewportPalette.Roles.Length, colours.Length);
        for (int i = 0; i < colours.Length; i++)
        {
            double cr = CR(colours[i], bg);
            Assert.True(cr >= 3.0, $"{ViewportPalette.Roles[i]} {colours[i].Hex} on {bg.Hex}: {cr:F3}");
            Assert.True(cr < 3.0 + 0.6 || CR(ViewportPalette.PageColour(ViewportPalette.Roles[i], ViewportPalette.ThemeFor(bg)), bg) >= 3.0,
                $"{ViewportPalette.Roles[i]} moved further than needed: {cr:F2}"); // moved colours stop at 3:1 (8-bit rounding and the nudge add < 0.6)
            Assert.Equal(colours[i], ViewportPalette.Colour(ViewportPalette.Roles[i], bg));
        }
        // one direction for all roles: everything darker than a light background, lighter than a dark one
        double yb = ColourScience.RelativeLuminance(bg);
        foreach (var c in colours)
            Assert.True(ViewportPalette.ThemeFor(bg) == Theme.Light ? ColourScience.RelativeLuminance(c) < yb : ColourScience.RelativeLuminance(c) > yb, c.Hex + " on " + bg.Hex);
        // warnings are family B's red, as in the web app
        Assert.Equal(colours[(int)ViewportRole.FamilyB], colours[(int)ViewportRole.Warn]);
    }

    [Fact]
    public void Darkening_KeepsChromaticity_Lightening_KeepsHue()
    {
        // CIE 15:2004: sRGB → XYZ is linear, so a scale in linear light leaves (x, y) where it was
        var grey = new Rgb(160, 160, 160);
        foreach (var page in new[] { MitePalette.FamilyA(Theme.Light), MitePalette.FamilyB(Theme.Light), MitePalette.FamilyG(Theme.Light), Rgb.FromHex("#d9ff00") })
        {
            Assert.True(ViewportPalette.TryDarken(page, grey, 3.0, out var dark));
            var (x0, y0) = ViewportPalette.Chromaticity(page); var (x1, y1) = ViewportPalette.Chromaticity(dark);
            Assert.InRange(Math.Abs(x1 - x0) + Math.Abs(y1 - y0), 0, 0.003); // 8-bit rounding only
            Assert.InRange(CR(dark, grey), 3.0, 3.1);
            Assert.True(ColourScience.RelativeLuminance(dark) < ColourScience.RelativeLuminance(page));
        }
        // mixing with white in linear light keeps the CIELAB hue angle (to a few degrees) and lowers the chroma
        var dim = new Rgb(96, 96, 96);
        foreach (var page in new[] { MitePalette.FamilyA(Theme.Dark), MitePalette.FamilyB(Theme.Dark) })
        {
            Assert.True(ViewportPalette.TryLighten(page, dim, 3.0, out var light));
            Assert.InRange(Math.Abs(ViewportPalette.HueAngle(light) - ViewportPalette.HueAngle(page)), 0, 5);
            Assert.True(ViewportPalette.Chroma(light) < ViewportPalette.Chroma(page));
            Assert.InRange(CR(light, dim), 3.0, 3.1);
        }
        // infeasible directions say so: nothing darker than a near-black has 3:1, nothing lighter than a near-white
        Assert.False(ViewportPalette.TryDarken(MitePalette.FamilyA(Theme.Light), new Rgb(40, 40, 40), 3.0, out _));
        Assert.False(ViewportPalette.TryLighten(MitePalette.FamilyA(Theme.Dark), new Rgb(230, 230, 230), 3.0, out _));
        // a colour that already reads is returned as it is
        Assert.True(ViewportPalette.TryDarken(MitePalette.FamilyA(Theme.Light), White, 3.0, out var same));
        Assert.Equal(MitePalette.FamilyA(Theme.Light), same);
    }

    [Fact]
    public void OnRhinoLikeGrey_TheRecordOf2026_11_07()
    {
        // #a0a0a0 (L* 65.9): the light theme darkened to exactly 3:1 — the neons keep their hue and saturation
        var grey = new Rgb(160, 160, 160);
        Assert.Equal(Theme.Light, ViewportPalette.ThemeFor(grey));
        var c = ViewportPalette.Colours(grey);
        Assert.Equal("#2434e3", c[(int)ViewportRole.FamilyA].Hex);
        Assert.Equal("#a3102f", c[(int)ViewportRole.FamilyB].Hex);
        Assert.Equal("#005f2d", c[(int)ViewportRole.FamilyG].Hex);
        Assert.Equal("#0a0a0a", c[(int)ViewportRole.Mark].Hex);
        Assert.Equal("#0a0a0a", c[(int)ViewportRole.Ink].Hex);
        Assert.Equal(3.00, CR(c[(int)ViewportRole.FamilyA], grey), 2);
        Assert.Equal(3.00, CR(c[(int)ViewportRole.FamilyB], grey), 2);
        Assert.Equal(3.00, CR(c[(int)ViewportRole.FamilyG], grey), 2);
        Assert.Equal(7.57, CR(c[(int)ViewportRole.Mark], grey), 2);
        // the page colours drawn there would have been 2.52 / 1.44 / 1.22, the 1.6.0 preview's teal / plum / amber / red 2.09 / 2.63 / 1.22 / 2.07
        Assert.Equal(2.52, CR(MitePalette.FamilyA(Theme.Light), grey), 2);
        Assert.Equal(1.44, CR(MitePalette.FamilyB(Theme.Light), grey), 2);
        Assert.Equal(1.22, CR(MitePalette.FamilyG(Theme.Light), grey), 2);
        Assert.Equal(2.09, CR(OldA, grey), 2); Assert.Equal(2.63, CR(OldB, grey), 2); Assert.Equal(1.22, CR(OldT, grey), 2); Assert.Equal(2.07, CR(OldFree, grey), 2);
        // the old T-junction amber never had 3:1 on white either (2.15); the old free-end red did (5.42)
        Assert.Equal(2.15, CR(OldT, White), 2); Assert.Equal(5.42, CR(OldFree, White), 2);
        // A and B stay far apart for dichromats (Machado 2009; Crameri 2020), the marks apart from both
        Assert.InRange(ColourScience.WorstCaseDeltaE(c[0], c[1]), 90, 100);
        Assert.InRange(ColourScience.WorstCaseDeltaE(c[(int)ViewportRole.Mark], c[1]), 25, 27);
        // B against G is the palette's known weak pair for deuteranopes (ΔE 12.8 on white); darkening keeps it there
        Assert.InRange(ColourScience.WorstCaseDeltaE(c[1], c[2]), 11, 14);
        // #c0c0c0 (L* 77.7): family A already reads (3.62) and stays the page colour; B and G are darkened
        var light = new Rgb(192, 192, 192);
        Assert.Equal(MitePalette.FamilyA(Theme.Light), ViewportPalette.Colour(ViewportRole.FamilyA, light));
        Assert.Equal("#cf173e", ViewportPalette.Colour(ViewportRole.FamilyB, light).Hex);
        Assert.Equal("#007a3b", ViewportPalette.Colour(ViewportRole.FamilyG, light).Hex);
        // #606060 (L* 40.7): the dark theme lightened towards white
        var dim = new Rgb(96, 96, 96);
        Assert.Equal(Theme.Dark, ViewportPalette.ThemeFor(dim));
        Assert.Equal("#a9acff", ViewportPalette.Colour(ViewportRole.FamilyA, dim).Hex);
        Assert.Equal("#ff959f", ViewportPalette.Colour(ViewportRole.FamilyB, dim).Hex);
        Assert.Equal("#00ff85", ViewportPalette.Colour(ViewportRole.FamilyG, dim).Hex); // 4.68 already
        Assert.Equal("#d9ff00", ViewportPalette.Colour(ViewportRole.Mark, dim).Hex); // 5.47 already
    }

    [Fact]
    public void GradientMean_IsTakenInLinearLight()
    {
        // the mean of white and black in linear light is the grey of half the light, #bcbcbc (not the 8-bit midpoint #808080)
        Assert.Equal("#bcbcbc", ViewportPalette.Average(new[] { White, Black }).Hex);
        Assert.Equal(0.5, ColourScience.RelativeLuminance(ViewportPalette.Average(new[] { White, Black })), 2);
        Assert.Equal(White, ViewportPalette.Average(new[] { White, White, White, White }));
        Assert.Throws<ArgumentException>(() => ViewportPalette.Average(new Rgb[0]));
    }

    [Fact]
    public void TheMidLuminance_IsLStarFifty()
    {
        Assert.Equal(50.0, 116 * Math.Pow(ViewportPalette.MidLuminance, 1.0 / 3.0) - 16, 9);
        Assert.Equal(Theme.Light, ViewportPalette.ThemeFor(new Rgb(119, 119, 119))); // L* 50.0
        Assert.Equal(Theme.Dark, ViewportPalette.ThemeFor(new Rgb(118, 118, 118)));  // L* 49.6
    }

    [Fact]
    public void GrasshopperPreview_DrawsRolesAndBakesToLayers()
    {
        // the Mite Net goo draws through ViewportColours (roles against the pipeline's background) — no colour literal is left,
        // the bake writes Mite::A / Mite::B, and the components with a Net output start with the plain curve previews off
        string goo = File.ReadAllText(RepoFile("src", "Mite.Grasshopper", "MiteNet.cs"));
        Assert.DoesNotContain("Color.FromArgb(", goo);
        foreach (string s in new[] { "ViewportRole.FamilyA", "ViewportRole.FamilyB", "ViewportRole.Mark", "ViewportRole.Warn", "ViewportColours.Background(pipeline)", "ColorFromLayer", "LayerPath(", "\"Mite\"" })
            Assert.Contains(s, goo);
        string colours = File.ReadAllText(RepoFile("src", "Mite.Grasshopper", "PaletteColours.cs"));
        foreach (string s in new[] { "ViewportPalette.Colours(", "ViewportPalette.Average(", "AppearanceSettings.ViewportBackgroundColor", "FrameBufferFillMode.SolidColor", "FrameBufferFillMode.Gradient2Color", "FrameBufferFillMode.Gradient4Color", "GetFill(" })
            Assert.Contains(s, colours);
        string comp = File.ReadAllText(RepoFile("src", "Mite.Grasshopper", "MiteComponent.cs"));
        Assert.Contains("HidePreviewsBehindNet()", comp);
        Assert.Contains("c.Hidden = true", comp);
        // every curve output the rule hides is one of the Net's families: A, B, G, Cu, Cv — the nicknames the components use
        var nick = Regex.Matches(comp, @"HiddenBehindNet = \{([^}]*)\}").Cast<Match>().Single().Groups[1].Value;
        Assert.Equal(new[] { "A", "B", "G", "Cu", "Cv" }, Regex.Matches(nick, "\"(\\w+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray());
        foreach (var (file, nicks) in new[] { ("AsymptoticNetComponent.cs", "A|B"), ("ConjugateNetComponent.cs", "A|B"), ("ChebyshevNetComponent.cs", "Cu|Cv"), ("GeodesicNetComponent.cs", "G"), ("MiteNetComponent.cs", "A|B"), ("NetKineticsComponent.cs", "A|B"), ("KineticStateComponent.cs", "A|B") })
        {
            string src = File.ReadAllText(RepoFile("src", "Mite.Grasshopper", "Components", file));
            Assert.Contains("RegisterNetOutput(", src);
            foreach (var n in nicks.Split('|'))
                Assert.True(Regex.IsMatch(src, "AddCurveParameter\\(\"\\w+\", \"" + n + "\""), file + " output " + n);
        }
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mite.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
