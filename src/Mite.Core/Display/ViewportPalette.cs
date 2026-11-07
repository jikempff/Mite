using System;
using System.Collections.Generic;
using System.Linq;

// The palette in a 3D viewport whose background is neither the white nor the
// black page: Rhino's default grey, a gradient, a user's own colour. The page
// colours of MitePalette were chosen against white and black; a neon on a mid
// grey has no luminance contrast left (family B's #ff1f4f is 1.45:1 on
// #a0a0a0), and it is luminance, not hue, that lets a thin line read.
//
// Sources
// - W3C WCAG 2.1, SC 1.4.11 Non-text Contrast: graphical objects need a
//   contrast ratio of at least 3:1 against adjacent colour(s); the contrast
//   ratio (L1 + 0.05) / (L2 + 0.05) and the relative luminance Y with the sRGB
//   linearisation are those of ColourScience (the same definitions).
// - Ware 1988, "Color sequences for univariate maps: theory, experiments and
//   principles", IEEE CG&A 8(5):41–49: "the luminance channel appears to
//   process shape … far more effectively than the chromatic channels" — form
//   (here: the lines of a net) is carried by luminance, so the criterion for
//   a preview colour is luminance contrast against the background, not hue.
// - Healey 1996, "Choosing effective colours for data visualization", UBC
//   TR-96-10: distinguishable colours are a matter of distance in a
//   perceptually balanced model — the roles are checked for ΔE*ab in CIELAB
//   after the adaptation, also for simulated dichromats (Machado et al. 2009,
//   as in MitePalette).
// - CIE 15:2004: sRGB → XYZ is linear, so scaling a colour in linear light
//   scales X, Y, Z together and keeps its chromaticity (x, y) — darkening a
//   neon this way keeps exactly its hue and saturation; mixing with white in
//   linear light keeps the hue and lowers the chroma.
//
// Rule (per role, for a background b with relative luminance Yb)
// 1. Start from the page theme the background is nearer to: Light when
//    Yb ≥ 0.1842 (CIELAB L* 50), Dark below.
// 2. The theme's own colour is kept when it already has 3:1 against b — on
//    the white and the black page every role comes back unchanged.
// 3. Otherwise both themes' colours are moved to exactly 3:1, in the one
//    direction the background allows for all roles alike: on a light
//    background darkened, Y' = (Yb + 0.05) / 3 − 0.05 by the scale Y'/Y in
//    linear light (feasible while Yb > 0.1, so always from L* 50 up); on a
//    dark one lightened, Y' = 3 (Yb + 0.05) − 0.05 by mixing with white
//    (feasible while Yb < 0.3, so always below L* 50). After rounding to 8
//    bits the colour is nudged on until the ratio is ≥ 3. Of the two
//    candidates the one with the larger CIELAB chroma wins — the most neon
//    that 3:1 allows — a tie going to the nearer theme.

namespace Mite.Core.Display;

/// <summary>What a Grasshopper preview or a bake draws, by role.</summary>
public enum ViewportRole
{
    /// <summary>Family A laths (neon blue).</summary>
    FamilyA,
    /// <summary>Family B laths (neon red).</summary>
    FamilyB,
    /// <summary>Family G, the geodesic diagonals (neon green).</summary>
    FamilyG,
    /// <summary>Marks: T-junction ends, umbilics, cut points — ink on a light background, neon yellow on a dark one.</summary>
    Mark,
    /// <summary>Warnings: free lath ends, limits — the family B red.</summary>
    Warn,
    /// <summary>Ink: contact dots, supports, text.</summary>
    Ink,
}

/// <summary>
/// The palette's roles adapted to any viewport background so that every one
/// keeps WCAG 2.1's 3:1 against it, with the page themes returned unchanged on
/// white and on black. Pure colour arithmetic; the Grasshopper plugin reads the
/// viewport's background and asks for <see cref="Colours"/>.
/// </summary>
public static class ViewportPalette
{
    /// <summary>WCAG 2.1 SC 1.4.11: the least contrast of a graphical object against the adjacent colour.</summary>
    public const double MinimumContrast = 3.0;

    /// <summary>Relative luminance of CIELAB L* 50, the split between the light and the dark page theme.</summary>
    public const double MidLuminance = 0.18418651851244416; // ((50 + 16) / 116)³

    /// <summary>All roles, in enum order.</summary>
    public static readonly ViewportRole[] Roles = { ViewportRole.FamilyA, ViewportRole.FamilyB, ViewportRole.FamilyG, ViewportRole.Mark, ViewportRole.Warn, ViewportRole.Ink };

    /// <summary>The page theme a background is nearer to: light from CIELAB L* 50 up.</summary>
    public static Theme ThemeFor(Rgb background) => ColourScience.RelativeLuminance(background) >= MidLuminance ? Theme.Light : Theme.Dark;

    /// <summary>
    /// A role's colour on the page of a theme (MitePalette). Marks are ink on
    /// the white page and neon yellow #d9ff00 on the black one — the web app's
    /// rule of 2026-11-07: neon yellow is 1.15:1 on white and every olive dark
    /// enough for 3:1 sits within ΔE 24 of family B for protanopes.
    /// </summary>
    public static Rgb PageColour(ViewportRole role, Theme theme)
    {
        switch (role)
        {
            case ViewportRole.FamilyA: return MitePalette.FamilyA(theme);
            case ViewportRole.FamilyB: return MitePalette.FamilyB(theme);
            case ViewportRole.FamilyG: return MitePalette.FamilyG(theme);
            case ViewportRole.Mark: return theme == Theme.Light ? MitePalette.Ink(theme) : Rgb.FromHex("#d9ff00");
            case ViewportRole.Warn: return MitePalette.Warn(theme);
            case ViewportRole.Ink: return MitePalette.Ink(theme);
            default: throw new ArgumentOutOfRangeException(nameof(role));
        }
    }

    /// <summary>A role's colour against a background: the page colour when it reads, else the adaptation of the header's rule.</summary>
    public static Rgb Colour(ViewportRole role, Rgb background)
    {
        Theme near = ThemeFor(background);
        Rgb own = PageColour(role, near);
        if (ColourScience.ContrastRatio(own, background) >= MinimumContrast) return own;
        // one direction for every role of a background, so the families keep one lightness order:
        // darker than a light background (always feasible from L* 50 up), lighter than a dark one
        var candidates = new List<(Rgb colour, double chroma, int tie)>();
        foreach (var theme in new[] { near, near == Theme.Light ? Theme.Dark : Theme.Light })
        {
            Rgb page = PageColour(role, theme);
            int tie = theme == near ? 0 : 1;
            bool ok = near == Theme.Light ? TryDarken(page, background, MinimumContrast, out var c) : TryLighten(page, background, MinimumContrast, out c);
            if (ok) candidates.Add((c, Chroma(c), tie));
        }
        if (candidates.Count == 0) // cannot happen: a light background has black at ≥ 4.7:1, a dark one white at ≥ 4.5:1
            return near == Theme.Light ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
        return candidates.OrderByDescending(c => c.chroma).ThenBy(c => c.tie).First().colour;
    }

    /// <summary>Every role's colour against a background, indexed like <see cref="Roles"/>.</summary>
    public static Rgb[] Colours(Rgb background) => Roles.Select(r => Colour(r, background)).ToArray();

    /// <summary>
    /// Darkens a colour in linear light (chromaticity kept) until it has the
    /// contrast against the background; false when the background itself is too
    /// dark for any darker colour to reach it (Yb ≤ contrast · 0.05 − 0.05).
    /// </summary>
    public static bool TryDarken(Rgb colour, Rgb background, double contrast, out Rgb result)
    {
        result = colour;
        double yb = ColourScience.RelativeLuminance(background);
        double target = (yb + 0.05) / contrast - 0.05;
        if (target <= 0) return false;
        double y = ColourScience.RelativeLuminance(colour);
        if (y <= 0) return false;
        double s = Math.Min(1.0, target / y);
        var lin = Linear(colour);
        for (int i = 0; i < 64; i++)
        {
            result = FromLinear(lin.r * s, lin.g * s, lin.b * s);
            if (ColourScience.ContrastRatio(result, background) >= contrast) return true;
            s *= 0.995;
        }
        return false;
    }

    /// <summary>
    /// Lightens a colour by mixing it with white in linear light (hue kept,
    /// chroma lowered) until it has the contrast against the background; false
    /// when the background is too light for any lighter colour to reach it.
    /// </summary>
    public static bool TryLighten(Rgb colour, Rgb background, double contrast, out Rgb result)
    {
        result = colour;
        double yb = ColourScience.RelativeLuminance(background);
        double target = contrast * (yb + 0.05) - 0.05;
        if (target >= 1) return false;
        double y = ColourScience.RelativeLuminance(colour);
        double t = Math.Max(0.0, (target - y) / (1 - y));
        var lin = Linear(colour);
        for (int i = 0; i < 64; i++)
        {
            result = FromLinear(lin.r + (1 - lin.r) * t, lin.g + (1 - lin.g) * t, lin.b + (1 - lin.b) * t);
            if (ColourScience.ContrastRatio(result, background) >= contrast) return true;
            t = Math.Min(1.0, t + 0.005);
        }
        return false;
    }

    /// <summary>The mean of several colours in linear light — one background for a gradient fill.</summary>
    public static Rgb Average(IReadOnlyList<Rgb> colours)
    {
        if (colours == null || colours.Count == 0) throw new ArgumentException("At least one colour.", nameof(colours));
        double r = 0, g = 0, b = 0;
        foreach (var c in colours) { var l = Linear(c); r += l.r; g += l.g; b += l.b; }
        int n = colours.Count;
        return FromLinear(r / n, g / n, b / n);
    }

    /// <summary>CIELAB chroma C* = √(a*² + b*²).</summary>
    public static double Chroma(Rgb c)
    {
        var lab = ColourScience.ToLab(c);
        return Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
    }

    /// <summary>CIE 1931 chromaticity (x, y) of an sRGB colour (D65 primaries).</summary>
    public static (double x, double y) Chromaticity(Rgb c)
    {
        var l = Linear(c);
        double X = 0.4124 * l.r + 0.3576 * l.g + 0.1805 * l.b;
        double Y = 0.2126 * l.r + 0.7152 * l.g + 0.0722 * l.b;
        double Z = 0.0193 * l.r + 0.1192 * l.g + 0.9505 * l.b;
        double sum = X + Y + Z;
        return sum <= 0 ? (0.3127, 0.3290) : (X / sum, Y / sum);
    }

    /// <summary>CIELAB hue angle in degrees, 0 … 360.</summary>
    public static double HueAngle(Rgb c)
    {
        var lab = ColourScience.ToLab(c);
        double h = Math.Atan2(lab.B, lab.A) * 180 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    /// <summary>The smallest ΔE*ab between any two of the given colours over normal vision and simulated dichromacy.</summary>
    public static double WorstSeparation(IReadOnlyList<Rgb> colours)
    {
        double worst = double.PositiveInfinity;
        for (int i = 0; i < colours.Count; i++)
            for (int j = i + 1; j < colours.Count; j++)
                worst = Math.Min(worst, ColourScience.WorstCaseDeltaE(colours[i], colours[j]));
        return worst;
    }

    private static (double r, double g, double b) Linear(Rgb c)
        => (ColourScience.ToLinear(c.R), ColourScience.ToLinear(c.G), ColourScience.ToLinear(c.B));

    private static Rgb FromLinear(double r, double g, double b)
        => new Rgb(MitePalette.Round(ColourScience.FromLinear(r)), MitePalette.Round(ColourScience.FromLinear(g)), MitePalette.Round(ColourScience.FromLinear(b)));
}
