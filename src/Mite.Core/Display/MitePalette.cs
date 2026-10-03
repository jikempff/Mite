using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// One palette for Grasshopper, the web app and the bench: black, white and neon.
//
// Sources
// - Kovesi 2015, "Good Colour Maps: How to Design Them" (arXiv:1509.03700):
//   perceptual uniformity is carried by lightness, so a map's CIELAB L* must
//   change at a uniform rate; a diverging map's two ends need the same
//   lightness (and ideally the same chroma).
// - Moreland 2009, "Diverging Color Maps for Scientific Visualization" (ISVC):
//   two saturated ends of comparable magnitude through an unsaturated centre.
// - Crameri, Shephard & Heron 2020, "The misuse of colour in science
//   communication" (Nat. Commun. 11:5444): avoid hues that only differ for
//   people with normal colour vision.
// - Machado, Oliveira & Fernandes 2009, "A Physiologically-based Model for
//   Simulation of Color Vision Deficiency" (IEEE TVCG 15(6)): protanopia and
//   deuteranopia matrices at severity 1, applied in linear RGB.
// - W3C WCAG 2.1: relative luminance, contrast ratio, 3:1 for graphical
//   objects (SC 1.4.11). CIE 15:2004 for sRGB → XYZ (D65) → CIELAB.
//
// The maps are built, not picked: each arm is the app's sRGB path (neon blue →
// white, white → crimson; black → neon blue → neon green → white) cut at equal
// L* steps (EqualLightnessRamp), so L* is linear in t to within 1 and the
// diverging map is symmetric in L* to within 0.6.

namespace Mite.Core.Display;

/// <summary>An 8-bit sRGB colour.</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R, G, B;
    public Rgb(int r, int g, int b)
    {
        R = (byte)Math.Max(0, Math.Min(255, r));
        G = (byte)Math.Max(0, Math.Min(255, g));
        B = (byte)Math.Max(0, Math.Min(255, b));
    }

    /// <summary>Parses "#rrggbb" or "rrggbb".</summary>
    public static Rgb FromHex(string hex)
    {
        string h = hex.Trim().TrimStart('#');
        if (h.Length != 6) throw new FormatException("Expected #rrggbb, got " + hex);
        int v = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    public string Hex => "#" + R.ToString("x2") + G.ToString("x2") + B.ToString("x2");
    public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;
    public override bool Equals(object? obj) => obj is Rgb o && Equals(o);
    public override int GetHashCode() => (R << 16) | (G << 8) | B;
    public override string ToString() => Hex;
}

/// <summary>Light (white page) or dark (black page) theme.</summary>
public enum Theme { Light, Dark }

/// <summary>Simulated colour-vision deficiency (Machado et al. 2009, severity 1).</summary>
public enum Cvd { None, Protanopia, Deuteranopia }

/// <summary>
/// sRGB colour science used to design and check the palette: linearisation,
/// WCAG relative luminance and contrast, CIELAB (D65), ΔE*ab and simulated
/// dichromacy.
/// </summary>
public static class ColourScience
{
    /// <summary>sRGB channel (0..255) to linear light (0..1), IEC 61966-2-1 / WCAG 2.1.</summary>
    public static double ToLinear(double c255)
    {
        double c = c255 / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Linear light (0..1) back to an sRGB channel (0..255, not rounded).</summary>
    public static double FromLinear(double l)
    {
        l = Math.Max(0, Math.Min(1, l));
        return 255.0 * (l <= 0.0031308 ? 12.92 * l : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055);
    }

    /// <summary>WCAG 2.1 relative luminance Y = 0.2126 R + 0.7152 G + 0.0722 B (linear).</summary>
    public static double RelativeLuminance(double r, double g, double b)
        => 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);

    public static double RelativeLuminance(Rgb c) => RelativeLuminance(c.R, c.G, c.B);

    /// <summary>WCAG contrast ratio (L1 + 0.05) / (L2 + 0.05), 1 … 21.</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>CIELAB (L*, a*, b*) of an sRGB colour, D65 white (CIE 15:2004).</summary>
    public static (double L, double A, double B) ToLab(double r, double g, double b)
    {
        double lr = ToLinear(r), lg = ToLinear(g), lb = ToLinear(b);
        double x = 0.4124 * lr + 0.3576 * lg + 0.1805 * lb;
        double y = 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
        double z = 0.0193 * lr + 0.1192 * lg + 0.9505 * lb;
        double fx = F(x / 0.95047), fy = F(y), fz = F(z / 1.08883);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    public static (double L, double A, double B) ToLab(Rgb c) => ToLab(c.R, c.G, c.B);

    /// <summary>CIELAB lightness L* (0 black … 100 white).</summary>
    public static double Lightness(double r, double g, double b) => ToLab(r, g, b).L;
    public static double Lightness(Rgb c) => ToLab(c).L;

    /// <summary>CIE76 colour difference ΔE*ab.</summary>
    public static double DeltaE(Rgb a, Rgb b)
    {
        var p = ToLab(a); var q = ToLab(b);
        return Math.Sqrt((p.L - q.L) * (p.L - q.L) + (p.A - q.A) * (p.A - q.A) + (p.B - q.B) * (p.B - q.B));
    }

    /// <summary>
    /// What a dichromat sees: Machado et al. 2009 matrices (severity 1) applied
    /// to linear RGB, clamped and re-encoded.
    /// </summary>
    public static Rgb Simulate(Rgb c, Cvd type)
    {
        if (type == Cvd.None) return c;
        double[,] m = type == Cvd.Protanopia ? Protan : Deutan;
        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
        double R2 = m[0, 0] * r + m[0, 1] * g + m[0, 2] * b;
        double G2 = m[1, 0] * r + m[1, 1] * g + m[1, 2] * b;
        double B2 = m[2, 0] * r + m[2, 1] * g + m[2, 2] * b;
        return new Rgb((int)Math.Round(FromLinear(R2)), (int)Math.Round(FromLinear(G2)), (int)Math.Round(FromLinear(B2)));
    }

    /// <summary>Smallest ΔE*ab between two colours over normal vision, protanopia and deuteranopia.</summary>
    public static double WorstCaseDeltaE(Rgb a, Rgb b)
        => new[] { Cvd.None, Cvd.Protanopia, Cvd.Deuteranopia }.Min(t => DeltaE(Simulate(a, t), Simulate(b, t)));

    private static double F(double t)
    {
        const double d = 6.0 / 29.0;
        return t > d * d * d ? Math.Pow(t, 1.0 / 3.0) : t / (3 * d * d) + 4.0 / 29.0;
    }

    private static readonly double[,] Protan =
    {
        { 0.152286, 1.052583, -0.204868 },
        { 0.114503, 0.786281, 0.099216 },
        { -0.003882, -0.048116, 1.051998 },
    };

    private static readonly double[,] Deutan =
    {
        { 0.367322, 0.860646, -0.227968 },
        { 0.280085, 0.672501, 0.047413 },
        { -0.011820, 0.042940, 0.968881 },
    };
}

/// <summary>
/// Mite's palette — black, white and neon — shared by the Grasshopper
/// components, the web app (src/Mite.Web) and the bench (tools/webgen).
/// Colour maps are evenly spaced stops interpolated linearly in sRGB, exactly
/// as the app's colormaps.js samples them.
/// </summary>
public static class MitePalette
{
    // ---------- theme colours (src/Mite.Web/wwwroot/css/app.css :root) ----------

    /// <summary>Page background.</summary>
    public static Rgb Paper(Theme t) => t == Theme.Light ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
    /// <summary>Raised surfaces (panels; the bench's page behind its white cards).</summary>
    public static Rgb Surface(Theme t) => t == Theme.Light ? new Rgb(250, 250, 250) : new Rgb(11, 11, 11);
    /// <summary>Chips, hovers, inactive tracks.</summary>
    public static Rgb Surface2(Theme t) => t == Theme.Light ? new Rgb(241, 241, 241) : new Rgb(21, 21, 21);
    /// <summary>Hints and tick labels (not for body text).</summary>
    public static Rgb Faint(Theme t) => t == Theme.Light ? new Rgb(160, 160, 160) : new Rgb(102, 102, 102);
    /// <summary>Text and drawn ink.</summary>
    public static Rgb Ink(Theme t) => t == Theme.Light ? new Rgb(10, 10, 10) : new Rgb(255, 255, 255);
    /// <summary>Secondary text.</summary>
    public static Rgb Muted(Theme t) => t == Theme.Light ? new Rgb(107, 107, 107) : new Rgb(154, 154, 154);
    /// <summary>Hairlines, the mesh wireframe.</summary>
    public static Rgb Line(Theme t) => t == Theme.Light ? new Rgb(226, 226, 226) : new Rgb(38, 38, 38);
    /// <summary>Family A (first asymptotic / curvature family): neon blue.</summary>
    public static Rgb FamilyA(Theme t) => t == Theme.Light ? Rgb.FromHex("#2a3cff") : Rgb.FromHex("#5b6bff");
    /// <summary>Family B (second family): neon red.</summary>
    public static Rgb FamilyB(Theme t) => t == Theme.Light ? Rgb.FromHex("#ff1f4f") : Rgb.FromHex("#ff3d6a");
    /// <summary>
    /// Family G (geodesic diagonals of AAG webs): neon green. On white it is
    /// the app's darker "ok" green #00a653 — #00c865 had 2.2:1 against white,
    /// below WCAG's 3:1 for graphical objects.
    /// </summary>
    public static Rgb FamilyG(Theme t) => t == Theme.Light ? Rgb.FromHex("#00a653") : Rgb.FromHex("#00ff85");
    /// <summary>The brand neon (progress bars, highlights; on black only).</summary>
    public static Rgb Neon => Rgb.FromHex("#00ff85");
    /// <summary>Warnings and limits.</summary>
    public static Rgb Warn(Theme t) => FamilyB(t);
    /// <summary>Passed checks.</summary>
    public static Rgb Ok(Theme t) => t == Theme.Light ? Rgb.FromHex("#00a653") : Rgb.FromHex("#00ff85");

    // ---------- colour maps ----------

    /// <summary>
    /// Diverging map for signed values (K, H, k2), centred on zero: neon blue
    /// (= family A) at −max, white at 0, crimson at +max. Both ends at L* 39.5;
    /// 13 stops at equal L* steps along the sRGB paths.
    /// </summary>
    public static readonly Rgb[] Diverging =
    {
        new(42, 60, 255), new(83, 98, 253), new(118, 130, 252), new(151, 159, 251), new(183, 189, 249), new(215, 218, 248),
        new(247, 247, 247),
        new(239, 213, 221), new(231, 179, 194), new(223, 144, 166), new(214, 108, 138), new(204, 66, 105), new(188, 0, 53),
    };

    /// <summary>
    /// Sequential map for magnitudes (|k|, lengths, errors): black → neon blue →
    /// neon green → white, 13 stops at equal L* steps (2.7 → 99.1).
    /// </summary>
    public static readonly Rgb[] Sequential =
    {
        new(10, 10, 10), new(18, 23, 73), new(24, 33, 122), new(30, 44, 172), new(37, 54, 224), new(34, 89, 229),
        new(24, 127, 192), new(16, 157, 164), new(8, 185, 137), new(1, 212, 112), new(93, 231, 160), new(176, 244, 208),
        new(245, 255, 247),
    };

    /// <summary>The sRGB path the diverging map's negative arm is cut from (end → centre).</summary>
    public static readonly Rgb[] DivergingPathNegative = { new(42, 60, 255), new(247, 247, 247) };
    /// <summary>The sRGB path the diverging map's positive arm is cut from (end → centre).
    /// The end is family B's hue at family A's lightness (CIELAB L* 39.5, the largest chroma in gamut).</summary>
    public static readonly Rgb[] DivergingPathPositive = { new(188, 0, 53), new(247, 247, 247) };
    /// <summary>The sRGB path the sequential map is cut from (the app's 1.3.0 stops).</summary>
    public static readonly Rgb[] SequentialPath = { new(10, 10, 10), new(41, 61, 255), new(0, 217, 107), new(245, 255, 247) };

    /// <summary>Samples evenly spaced stops at t ∈ [0, 1] by linear interpolation in sRGB (not rounded).</summary>
    public static (double R, double G, double B) SampleExact(IReadOnlyList<Rgb> stops, double t)
    {
        if (double.IsNaN(t)) t = 0.5;
        t = Math.Max(0, Math.Min(1, t));
        double x = t * (stops.Count - 1);
        int i = Math.Min(stops.Count - 2, (int)Math.Floor(x));
        double f = x - i;
        Rgb a = stops[i], b = stops[i + 1];
        return (a.R + (b.R - a.R) * f, a.G + (b.G - a.G) * f, a.B + (b.B - a.B) * f);
    }

    /// <summary>Samples evenly spaced stops at t ∈ [0, 1], rounded to 8 bits.</summary>
    public static Rgb Sample(IReadOnlyList<Rgb> stops, double t)
    {
        var c = SampleExact(stops, t);
        return new Rgb(Round(c.R), Round(c.G), Round(c.B));
    }

    /// <summary>Diverging colour of a signed value v with the symmetric range ±range.</summary>
    public static Rgb DivergingColour(double v, double range)
        => Sample(Diverging, 0.5 + 0.5 * Math.Max(-1, Math.Min(1, v / (range > 0 ? range : 1e-300))));

    /// <summary>Neutral grey for values that could not be computed (NaN).</summary>
    public static Rgb NoData => new(153, 153, 153);

    /// <summary>
    /// Utilization colour (1 = at the limit), identical to the web app's
    /// utilizationColor: green 0 → yellow-green 0.7 → neon yellow just below 1,
    /// then a lightness drop to neon red at 1 (L* 94 → 55) darkening to 1.3, and
    /// a dark red beyond. NaN is grey.
    /// </summary>
    public static Rgb Utilization(double u)
    {
        if (double.IsNaN(u) || double.IsInfinity(u)) return NoData;
        if (u < 0.7) return Mix(0.00, 0.85, 0.42, 0.55, 0.95, 0.10, Math.Max(0, u) / 0.7);
        if (u < 1.0) return Mix(0.55, 0.95, 0.10, 0.85, 1.00, 0.00, (u - 0.7) / 0.3);
        if (u < 1.3) return Mix(1.00, 0.12, 0.31, 0.75, 0.02, 0.18, (u - 1.0) / 0.3);
        return new Rgb(89, 0, 20);
    }

    /// <summary>
    /// Cuts a piecewise-linear sRGB path into <paramref name="steps"/> + 1 stops
    /// at equal CIELAB L* steps (Kovesi 2015: uniform lightness is what makes a
    /// map perceptually uniform). The path's L* must increase monotonically.
    /// </summary>
    public static Rgb[] EqualLightnessRamp(IReadOnlyList<Rgb> path, int steps, int samplesPerSegment = 2000)
    {
        if (path.Count < 2 || steps < 1) throw new ArgumentException("Need a path of two colours or more and one step or more.");
        var pts = new List<(double r, double g, double b)>();
        var ls = new List<double>();
        for (int i = 0; i + 1 < path.Count; i++)
            for (int k = 0; k < samplesPerSegment; k++)
            {
                double s = k / (double)samplesPerSegment;
                Rgb a = path[i], c = path[i + 1];
                pts.Add((a.R + (c.R - a.R) * s, a.G + (c.G - a.G) * s, a.B + (c.B - a.B) * s));
            }
        Rgb last = path[path.Count - 1];
        pts.Add((last.R, last.G, last.B));
        foreach (var p in pts) ls.Add(ColourScience.Lightness(p.r, p.g, p.b));
        for (int i = 1; i < ls.Count; i++)
            if (ls[i] <= ls[i - 1])
                throw new ArgumentException("The path's lightness must increase monotonically.");
        var outp = new Rgb[steps + 1];
        for (int k = 0; k <= steps; k++)
        {
            double target = ls[0] + (ls[ls.Count - 1] - ls[0]) * k / steps;
            int j = 0;
            while (j < ls.Count - 1 && ls[j] < target) j++;
            var p = pts[j];
            outp[k] = new Rgb(Round(p.r), Round(p.g), Round(p.b));
        }
        return outp;
    }

    /// <summary>
    /// The palette as data for the bench and other generators: theme colours
    /// as hex, maps as evenly spaced [r, g, b] stops.
    /// </summary>
    public static Dictionary<string, object> Export()
    {
        Dictionary<string, string> theme(Theme t) => new Dictionary<string, string>
        {
            ["paper"] = Paper(t).Hex, ["surface"] = Surface(t).Hex, ["surface2"] = Surface2(t).Hex, ["faint"] = Faint(t).Hex, ["ink"] = Ink(t).Hex, ["muted"] = Muted(t).Hex, ["line"] = Line(t).Hex,
            ["famA"] = FamilyA(t).Hex, ["famB"] = FamilyB(t).Hex, ["famG"] = FamilyG(t).Hex,
            ["neon"] = Neon.Hex, ["warn"] = Warn(t).Hex, ["ok"] = Ok(t).Hex,
        };
        int[][] stops(Rgb[] s) => s.Select(c => new[] { (int)c.R, c.G, c.B }).ToArray();
        return new Dictionary<string, object>
        {
            ["light"] = theme(Theme.Light),
            ["dark"] = theme(Theme.Dark),
            ["diverging"] = stops(Diverging),
            ["sequential"] = stops(Sequential),
            ["utilization"] = Enumerable.Range(0, 131).Select(i => { var c = Utilization(i * 0.01); return new[] { (int)c.R, c.G, c.B }; }).ToArray(),
        };
    }

    private static Rgb Mix(double r0, double g0, double b0, double r1, double g1, double b1, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return new Rgb(Round(255 * (r0 + (r1 - r0) * t)), Round(255 * (g0 + (g1 - g0) * t)), Round(255 * (b0 + (b1 - b0) * t)));
    }

    /// <summary>Rounds half away from zero, as JavaScript's Math.round does for the app's positive channels.</summary>
    internal static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
