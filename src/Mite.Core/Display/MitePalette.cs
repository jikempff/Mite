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

    // ---------- the 3D viewers (web app viewer.js, bench scenes) ----------
    // Roles rather than colours: the app's viewer resolves every object's
    // role through the current theme (palette.js, the JavaScript twin of this
    // class) and recolours it when the theme changes.

    /// <summary>
    /// The seed, picked points and the direction knob: green. On white the
    /// app's neon #00d86b read 1.9:1 (WCAG 1.4.11 asks 3:1 of graphical
    /// objects against the adjacent colour), so the light theme takes the
    /// darker "ok" green (3.2:1); on black the brand neon (15.6:1).
    /// </summary>
    public static Rgb Seed(Theme t) => Ok(t);
    /// <summary>
    /// Marks: umbilics, T-junction ends, cut points — the icon language's
    /// "black the marks". On black the neon yellow #d9ff00 (18:1, ΔE ≥ 37 to
    /// A, B and G for simulated dichromats); on white neon yellow is 1.15:1
    /// and every olive dark enough for 3:1 (L* ≤ 55) collapses onto family
    /// B for protanopes (ΔE ≤ 24), so the light theme draws marks in ink
    /// (19.8:1, ΔE ≥ 43 to every family).
    /// </summary>
    public static Rgb Mark(Theme t) => t == Theme.Light ? Ink(t) : Rgb.FromHex("#d9ff00");
    /// <summary>The analysed surface before any colouring: a light grey lit by the viewer, the same on both pages.</summary>
    public static Rgb Mesh => new Rgb(219, 219, 219);
    /// <summary>A lit solid in ink (swept laths, hub plates): ink lightened or darkened by 10 % so the shading still reads.</summary>
    public static Rgb Solid(Theme t) => t == Theme.Light ? new Rgb(26, 26, 26) : new Rgb(230, 230, 230);
    /// <summary>Hover and drag highlight (emissive): the brand neon.</summary>
    public static Rgb Highlight => Neon;

    // ---------- colour maps ----------
    // The maps themselves live in Mite.Core.Visualisation.ColourMap (one
    // language for Grasshopper, the web app and the bench: Moreland's path for
    // the diverging map, Kovesi's lightness equalisation for both); this class
    // exposes them as evenly spaced stops for the generators and the CSS.

    static Rgb[] StopsOf(Visualisation.ColourMap.Rgb[] table, int n)
        => Enumerable.Range(0, n).Select(i => { var c = table[Visualisation.ColourMap.Index(i / (double)(n - 1))]; return new Rgb(c.R, c.G, c.B); }).ToArray();
    static Rgb Of(Visualisation.ColourMap.Rgb c) => new Rgb(c.R, c.G, c.B);

    static Rgb[]? _diverging, _sequential;

    /// <summary>
    /// Diverging map for signed values (K, H, k2), centred on zero: neon blue
    /// (= family A) at −max, white (L* 95) at 0, neon red at +max — 13 stops
    /// of <see cref="Visualisation.ColourMap.DivergingTable"/> at equal L* steps per arm.
    /// </summary>
    public static Rgb[] Diverging => _diverging ??= StopsOf(Visualisation.ColourMap.DivergingTable, 13);

    /// <summary>
    /// Sequential map for magnitudes (|k|, lengths, errors): black → neon blue →
    /// teal → neon green → white, 13 stops of <see cref="Visualisation.ColourMap.SequentialTable"/>
    /// at equal L* steps (2.7 → 99.1).
    /// </summary>
    public static Rgb[] Sequential => _sequential ??= StopsOf(Visualisation.ColourMap.SequentialTable, 13);

    /// <summary>The control points of the diverging map's negative arm (end → centre).</summary>
    public static Rgb[] DivergingPathNegative => new[] { Of(Visualisation.ColourMap.NeonBlue), Of(Visualisation.ColourMap.Diverging(0.5)) };
    /// <summary>The control points of the diverging map's positive arm (end → centre).</summary>
    public static Rgb[] DivergingPathPositive => new[] { Of(Visualisation.ColourMap.NeonRed), Of(Visualisation.ColourMap.Diverging(0.5)) };
    /// <summary>The control points of the sequential map (CIELAB piecewise-linear path).</summary>
    public static Rgb[] SequentialPath => new[] { Of(Visualisation.ColourMap.Black), Of(Visualisation.ColourMap.SeqBlue), Of(Visualisation.ColourMap.SeqTeal), Of(Visualisation.ColourMap.SeqGreen), Of(Visualisation.ColourMap.White) };

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
        return Of(Visualisation.ColourMap.Utilisation(u));
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
            ["seed"] = Seed(t).Hex, ["mark"] = Mark(t).Hex, ["mesh"] = Mesh.Hex, ["solid"] = Solid(t).Hex, ["highlight"] = Highlight.Hex,
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

    /// <summary>The names of the theme colours <see cref="Export"/> and <see cref="JavaScriptModule"/> carry, in their order.</summary>
    public static readonly string[] ThemeColourNames = { "paper", "surface", "surface2", "faint", "ink", "muted", "line", "famA", "famB", "famG", "neon", "warn", "ok", "seed", "mark", "mesh", "solid", "highlight" };

    /// <summary>
    /// The web app's twin of the theme colours (src/Mite.Web/wwwroot/js/palette.js,
    /// written by the bench generator next to colormap-lut.js): one object per
    /// theme with every colour as "#rrggbb", and <c>colour(theme, role)</c> /
    /// <c>hex(theme, role)</c> for the viewer. Rendered deterministically so
    /// PaletteTests can check the file on disk is current.
    /// </summary>
    public static string JavaScriptModule()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("// Generated by Mite.Core (MitePalette.JavaScriptModule) — do not edit.\n");
        sb.Append("// Regenerate with `cd tools/webgen && dotnet run -c Release`; tests/Mite.Tests PaletteTests checks it is current.\n");
        sb.Append("// One palette: black, white and neon, per theme — the colours the Grasshopper previews, the bench and app.css use.\n");
        var export = Export();
        foreach (var (name, t) in new[] { ("LIGHT", Theme.Light), ("DARK", Theme.Dark) })
        {
            var d = (Dictionary<string, string>)export[t == Theme.Light ? "light" : "dark"];
            sb.Append("export const ").Append(name).Append(" = {");
            for (int i = 0; i < ThemeColourNames.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(i % 6 == 0 ? "\n  " : " ");
                sb.Append(ThemeColourNames[i]).Append(": '").Append(d[ThemeColourNames[i]]).Append('\'');
            }
            sb.Append("\n};\n");
        }
        sb.Append("export const ROLES = [");
        for (int i = 0; i < ThemeColourNames.Length; i++) sb.Append(i > 0 ? ", '" : "'").Append(ThemeColourNames[i]).Append('\'');
        sb.Append("];\n");
        sb.Append(@"/** The theme's colours: palette(true) for the black page, palette(false) for the white one. */
export function palette(dark) { return dark ? DARK : LIGHT; }
/** A role's '#rrggbb' in a theme; a '#rrggbb' or 0xrrggbb passed as the role is returned unchanged (hex). */
export function hex(theme, role) { if (typeof role === 'number') return '#' + role.toString(16).padStart(6, '0'); if (typeof role === 'string' && role[0] === '#') return role; const c = theme[role]; if (c === undefined) throw new Error('palette: unknown role ' + role); return c; }
/** A role's colour as the 0xrrggbb number three.js takes. */
export function colour(theme, role) { return parseInt(hex(theme, role).slice(1), 16); }
/** A role's colour as [r, g, b] in 0..1. */
export function rgb(theme, role) { const h = hex(theme, role); return [parseInt(h.slice(1, 3), 16) / 255, parseInt(h.slice(3, 5), 16) / 255, parseInt(h.slice(5, 7), 16) / 255]; }
");
        return sb.ToString();
    }

    private static Rgb Mix(double r0, double g0, double b0, double r1, double g1, double b1, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return new Rgb(Round(255 * (r0 + (r1 - r0) * t)), Round(255 * (g0 + (g1 - g0) * t)), Round(255 * (b0 + (b1 - b0) * t)));
    }

    /// <summary>Rounds half away from zero, as JavaScript's Math.round does for the app's positive channels.</summary>
    internal static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
