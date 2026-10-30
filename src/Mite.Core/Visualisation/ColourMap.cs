using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mite.Core.Visualisation;

/// <summary>
/// One colour language for Mite: the diverging, sequential and utilisation
/// maps used by Mesh Colour Map and Lath Preview in Grasshopper, by the web
/// app (src/Mite.Web, through the generated js/colormap-lut.js) and by the
/// bench. Every surface samples the same look-up tables, so a value gets the
/// same colour everywhere.
///
/// The maps are built, not hand-drawn:
///  - the diverging map runs from José's neon blue through white to neon red
///    along Moreland's Msh path (Moreland 2009, Diverging color maps for
///    scientific visualization, ISVC: CIELAB in polar form M = |Lab|,
///    s = acos(L/M), h = atan2(b, a); each half interpolated linearly in
///    (M, s, h) towards a white control point for its chroma and hue, the hue
///    held — "AdjustHue" only spins it when the white is brighter than the end
///    colour — and the lightness taken linear between the ends);
///  - the sequential map (black → neon blue → teal → neon green → white) is a
///    piecewise-linear path through the control points in CIELAB (Kovesi's
///    order-1 B-spline);
///  - both are then re-parametrised so that successive entries differ by the
///    same lightness L* (Kovesi 2015, Good colour maps: how to design them,
///    arXiv:1509.03700, §3: cumulative lightness difference, inverted by
///    interpolation, iterated), which is the uniformity Crameri, Shephard &
///    Heron 2020 (Nature Communications 11, 5444) ask of a scientific map, and
///    a monotone lightness that stays readable in greyscale and for colour-
///    vision deficiencies;
///  - colours that leave the sRGB gamut are brought back by reducing chroma at
///    constant lightness, so the equalised lightness survives;
///  - the utilisation map keeps its step at 1 (yellow below the limit, red
///    above): it is a threshold map, where a visible edge is the point.
///
/// sRGB ↔ CIELAB uses the D65 white and the IEC 61966-2-1 transfer curve
/// (Moreland 2009 eq. 1–3).
/// </summary>
public static class ColourMap
{
    /// <summary>Palette ids as the Mesh Colour Map component counts them.</summary>
    public enum Palette { Diverging = 0, Sequential = 1, Utilisation = 2 }

    /// <summary>An 8-bit sRGB colour.</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public readonly byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public Rgb(int r, int g, int b) : this((byte)Clamp(r, 0, 255), (byte)Clamp(g, 0, 255), (byte)Clamp(b, 0, 255)) { }
        public string Hex => "#" + R.ToString("x2") + G.ToString("x2") + B.ToString("x2");
        public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;
        public override bool Equals(object? obj) => obj is Rgb o && Equals(o);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
        public override string ToString() => Hex;
        public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);
        public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);
        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    // ---- José's palette (kempff/seleme: black, white and neon) ----
    public static readonly Rgb NeonBlue = new Rgb(42, 60, 255);     // #2a3cff, family A (MitePalette.FamilyA, light theme)
    public static readonly Rgb NeonRed = new Rgb(217, 13, 51);      // 0.85 0.05 0.20
    public static readonly Rgb Black = new Rgb(10, 10, 10);         // 0.04
    public static readonly Rgb SeqBlue = new Rgb(41, 61, 255);      // 0.16 0.24 1.00
    public static readonly Rgb SeqTeal = new Rgb(20, 139, 181);     // halfway between the two neons in RGB
    public static readonly Rgb SeqGreen = new Rgb(0, 217, 107);     // 0.00 0.85 0.42
    public static readonly Rgb White = new Rgb(245, 255, 247);      // 0.96 1.00 0.97
    public static readonly Rgb Grey = new Rgb(153, 153, 153);       // NaN / not analysed

    /// <summary>Lightness L* of the white control point at the centre of the diverging map (Kovesi 2015 suggests staying below 95).</summary>
    public const double CentreLightness = 95.0;

    /// <summary>Number of entries of the diverging and sequential tables (t = i / 256).</summary>
    public const int TableSize = 257;
    /// <summary>Utilisation table: u = i × UtilisationStep for i = 0 … 260 (0 … 1.3); beyond that <see cref="UtilisationOver"/>.</summary>
    public const int UtilisationSize = 261;
    public const double UtilisationStep = 0.005;
    public static readonly Rgb UtilisationOver = new Rgb(89, 0, 20);

    static Rgb[]? _diverging, _sequential, _utilisation;

    /// <summary>The 257-entry diverging table, neon blue → white → neon red; entry 128 is the centre.</summary>
    public static Rgb[] DivergingTable => _diverging ??= BuildDiverging();
    /// <summary>The 257-entry sequential table, black → neon blue → neon green → white.</summary>
    public static Rgb[] SequentialTable => _sequential ??= BuildSequential();
    /// <summary>The 261-entry utilisation table over 0 … 1.3.</summary>
    public static Rgb[] UtilisationTable => _utilisation ??= BuildUtilisation();

    /// <summary>Table index of a parameter t ∈ [0, 1]: the nearest entry (the same rule as the JavaScript twin).</summary>
    public static int Index(double t)
    {
        if (double.IsNaN(t)) return TableSize / 2;
        if (t <= 0) return 0;
        if (t >= 1) return TableSize - 1;
        return (int)Math.Round(t * (TableSize - 1), MidpointRounding.AwayFromZero); // as JavaScript Math.round
    }

    /// <summary>Colour of the diverging map at t ∈ [0, 1] (0.5 = the centre, white).</summary>
    public static Rgb Diverging(double t) => DivergingTable[Index(t)];
    /// <summary>Colour of the sequential map at t ∈ [0, 1].</summary>
    public static Rgb Sequential(double t) => SequentialTable[Index(t)];

    /// <summary>
    /// Utilisation colour: green up to 0.7, yellow at 1 (the limit), red just above it,
    /// darkening to 1.3 and beyond; NaN (a lath that could not be analysed) is grey.
    /// </summary>
    public static Rgb Utilisation(double u)
    {
        if (double.IsNaN(u)) return Grey;
        // floor, not round: a value just below the limit stays yellow, the step sits exactly at u = 1
        int i = (int)Math.Floor(Math.Max(0.0, u) / UtilisationStep + 1e-9);
        if (i >= UtilisationSize) return UtilisationOver;
        return UtilisationTable[i];
    }

    /// <summary>Colour of a palette at t ∈ [0, 1]; for the utilisation palette t is read as u ∈ [0, 1.3].</summary>
    public static Rgb Sample(Palette palette, double t)
    {
        switch (palette)
        {
            case Palette.Sequential: return Sequential(t);
            case Palette.Utilisation: return Utilisation(t * 1.3);
            default: return Diverging(t);
        }
    }

    /// <summary>Result of <see cref="Map"/>.</summary>
    public sealed class Mapped
    {
        public Rgb[] Colours = Array.Empty<Rgb>();
        /// <summary>The value range that was mapped to the palette.</summary>
        public double Lo, Hi;
        /// <summary>True when the range came from the 2 %–98 % percentiles (no domain given).</summary>
        public bool Automatic;
    }

    /// <summary>
    /// Colours one value per vertex. Without a domain the range is the robust
    /// 2 %–98 % percentile band of the finite values, made symmetric about zero
    /// for the diverging palette; NaN and infinite values are grey. The
    /// utilisation palette maps u directly (its range is 0 … 1.3).
    /// </summary>
    public static Mapped Map(IReadOnlyList<double> values, Palette palette, double? lo = null, double? hi = null)
    {
        var res = new Mapped { Colours = new Rgb[values.Count] };
        if (palette == Palette.Utilisation)
        {
            res.Lo = 0; res.Hi = UtilisationSize * UtilisationStep - UtilisationStep;
            for (int i = 0; i < values.Count; i++) res.Colours[i] = Utilisation(values[i]);
            return res;
        }
        if (lo.HasValue && hi.HasValue && Math.Abs(hi.Value - lo.Value) > 0)
        {
            res.Lo = Math.Min(lo.Value, hi.Value); res.Hi = Math.Max(lo.Value, hi.Value);
        }
        else
        {
            res.Automatic = true;
            var sorted = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
            if (sorted.Length == 0) { res.Lo = -1; res.Hi = 1; }
            else
            {
                res.Lo = sorted[(int)(0.02 * (sorted.Length - 1))];
                res.Hi = sorted[(int)(0.98 * (sorted.Length - 1))];
                if (palette == Palette.Diverging)
                {
                    double m = Math.Max(Math.Abs(res.Lo), Math.Abs(res.Hi));
                    res.Lo = -m; res.Hi = m;
                }
                if (res.Hi - res.Lo < 1e-300) { res.Lo -= 1; res.Hi += 1; }
            }
        }
        for (int i = 0; i < values.Count; i++)
        {
            double v = values[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) { res.Colours[i] = Grey; continue; }
            double t = (v - res.Lo) / (res.Hi - res.Lo);
            res.Colours[i] = palette == Palette.Sequential ? Sequential(t) : Diverging(t);
        }
        return res;
    }

    // ------------------------------------------------------------------
    // Colour science: sRGB ↔ linear ↔ XYZ (D65) ↔ CIELAB ↔ Msh
    // ------------------------------------------------------------------

    const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

    static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    static double ToSrgb(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
    static double F(double x) => x > 0.008856 ? Math.Pow(x, 1.0 / 3) : 7.787 * x + 16.0 / 116;
    static double Finv(double f) { double c = f * f * f; return c > 0.008856 ? c : (f - 16.0 / 116) / 7.787; }

    /// <summary>sRGB (0 … 1 per channel) to CIELAB (L*, a*, b*), D65.</summary>
    public static double[] SrgbToLab(double r, double g, double b)
    {
        double rl = ToLinear(r), gl = ToLinear(g), bl = ToLinear(b);
        double x = 0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl;
        double y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl;
        double z = 0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl;
        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
        return new[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
    }

    public static double[] SrgbToLab(Rgb c) => SrgbToLab(c.R / 255.0, c.G / 255.0, c.B / 255.0);

    /// <summary>CIELAB to linear RGB (unclipped; a channel outside 0 … 1 means the colour is out of gamut).</summary>
    public static double[] LabToLinear(double L, double a, double b)
    {
        double fy = (L + 16) / 116, fx = fy + a / 500, fz = fy - b / 200;
        double x = Xn * Finv(fx), y = Yn * Finv(fy), z = Zn * Finv(fz);
        return new[]
        {
            3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
            -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
            0.0556434 * x - 0.2040259 * y + 1.0572252 * z
        };
    }

    /// <summary>
    /// CIELAB to 8-bit sRGB. A colour outside the sRGB gamut is brought in by
    /// scaling its chroma (a*, b*) towards grey at the same L* (bisection on
    /// the scale), so the lightness is kept exactly; L* itself is clamped to
    /// 0 … 100.
    /// </summary>
    public static Rgb LabToSrgb(double L, double a, double b)
    {
        L = Math.Max(0, Math.Min(100, L));
        double[] lin = LabToLinear(L, a, b);
        if (!InGamut(lin))
        {
            double lo = 0, hi = 1;
            for (int it = 0; it < 30; it++)
            {
                double k = 0.5 * (lo + hi);
                if (InGamut(LabToLinear(L, a * k, b * k))) lo = k; else hi = k;
            }
            lin = LabToLinear(L, a * lo, b * lo);
        }
        return new Rgb(
            (int)Math.Round(255 * ToSrgb(Clamp01(lin[0]))),
            (int)Math.Round(255 * ToSrgb(Clamp01(lin[1]))),
            (int)Math.Round(255 * ToSrgb(Clamp01(lin[2]))));
    }

    static bool InGamut(double[] lin) => lin[0] >= -1e-6 && lin[0] <= 1 + 1e-6 && lin[1] >= -1e-6 && lin[1] <= 1 + 1e-6 && lin[2] >= -1e-6 && lin[2] <= 1 + 1e-6;
    static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Lightness L* of a colour.</summary>
    public static double Lightness(Rgb c) => SrgbToLab(c)[0];
    /// <summary>Chroma √(a*² + b*²) of a colour.</summary>
    public static double Chroma(Rgb c) { var lab = SrgbToLab(c); return Math.Sqrt(lab[1] * lab[1] + lab[2] * lab[2]); }
    /// <summary>Hue angle atan2(b*, a*) in radians.</summary>
    public static double Hue(Rgb c) { var lab = SrgbToLab(c); return Math.Atan2(lab[2], lab[1]); }

    /// <summary>CIELAB to Moreland's Msh: M = |Lab|, s = acos(L/M), h = atan2(b, a).</summary>
    public static double[] LabToMsh(double L, double a, double b)
    {
        double M = Math.Sqrt(L * L + a * a + b * b);
        double s = M > 1e-12 ? Math.Acos(Math.Max(-1.0, Math.Min(1.0, L / M))) : 0;
        double h = Math.Atan2(b, a);
        return new[] { M, s, h };
    }

    public static double[] MshToLab(double M, double s, double h) =>
        new[] { M * Math.Cos(s), M * Math.Sin(s) * Math.Cos(h), M * Math.Sin(s) * Math.Sin(h) };

    public static double[] SrgbToMsh(Rgb c) { var lab = SrgbToLab(c); return LabToMsh(lab[0], lab[1], lab[2]); }

    /// <summary>Moreland's AdjustHue: the hue a saturated colour takes when it fades to an unsaturated one of magnitude M_unsat.</summary>
    public static double AdjustHue(double mSat, double sSat, double hSat, double mUnsat)
    {
        if (mSat >= mUnsat) return hSat; // best we can do
        double spin = sSat * Math.Sqrt(mUnsat * mUnsat - mSat * mSat) / (mSat * Math.Sin(sSat));
        return hSat > -Math.PI / 3 ? hSat + spin : hSat - spin;
    }

    static double RadDiff(double a, double b) { double d = Math.Abs(a - b) % (2 * Math.PI); return d > Math.PI ? 2 * Math.PI - d : d; }

    /// <summary>
    /// Moreland's InterpolateColor (2009, Fig. 11): linear interpolation in Msh
    /// between two colours, with a white control point of magnitude
    /// max(M1, M2, whiteM) inserted when both are saturated (s &gt; 0.05) and
    /// their hues differ by more than π/3. Returns CIELAB.
    /// </summary>
    public static double[] MorelandLab(Rgb c1, Rgb c2, double t, double whiteM = 88)
    {
        var m1 = SrgbToMsh(c1); var m2 = SrgbToMsh(c2);
        return MorelandLab(m1, m2, t, whiteM);
    }

    static double[] MorelandLab(double[] m1, double[] m2, double t, double whiteM)
    {
        double M1 = m1[0], s1 = m1[1], h1 = m1[2], M2 = m2[0], s2 = m2[1], h2 = m2[2];
        if (s1 > 0.05 && s2 > 0.05 && RadDiff(h1, h2) > Math.PI / 3)
        {
            double Mmid = Math.Max(Math.Max(M1, M2), whiteM);
            if (t < 0.5) { M2 = Mmid; s2 = 0; h2 = 0; t *= 2; }
            else { M1 = Mmid; s1 = 0; h1 = 0; t = 2 * t - 1; }
        }
        if (s1 < 0.05 && s2 > 0.05) h1 = AdjustHue(M2, s2, h2, M1);
        else if (s2 < 0.05 && s1 > 0.05) h2 = AdjustHue(M1, s1, h1, M2);
        double M = (1 - t) * M1 + t * M2, s = (1 - t) * s1 + t * s2, h = (1 - t) * h1 + t * h2;
        return MshToLab(M, s, h);
    }

    /// <summary>Moreland's interpolation as an sRGB colour (his cool–warm map is Moreland(59,76,192 → 180,4,38)).</summary>
    public static Rgb Moreland(Rgb c1, Rgb c2, double t, double whiteM = 88)
    {
        var lab = MorelandLab(c1, c2, t, whiteM);
        return LabToSrgb(lab[0], lab[1], lab[2]);
    }

    // ------------------------------------------------------------------
    // Building the tables
    // ------------------------------------------------------------------

    /// <summary>
    /// Kovesi's lightness equalisation: samples the lightness L*(u) of a path
    /// finely, accumulates |ΔL*|, and returns n parameters u_i at which the
    /// cumulative lightness change is i/(n−1) of the total. The mapping is
    /// applied to itself once more to remove the interpolation error of the
    /// first pass. The lightness must be the path's continuous value (before
    /// 8-bit rounding): rounding noise would be counted as lightness change.
    /// </summary>
    public static double[] EqualiseLightness(Func<double, double> lightness, int n, int samples = 4096)
    {
        double[] u = new double[n];
        for (int i = 0; i < n; i++) u[i] = i / (double)(n - 1);
        for (int pass = 0; pass < 2; pass++)
        {
            // cumulative lightness along the current parametrisation (piecewise linear in u)
            var us = new double[samples + 1]; var cum = new double[samples + 1];
            double prevL = 0;
            for (int k = 0; k <= samples; k++)
            {
                double w = k / (double)samples; // position along the current table
                double x = w * (n - 1); int j = Math.Min(n - 2, (int)Math.Floor(x)); double f = x - j;
                us[k] = u[j] + (u[j + 1] - u[j]) * f;
                double L = lightness(us[k]);
                cum[k] = k == 0 ? 0 : cum[k - 1] + Math.Abs(L - prevL);
                prevL = L;
            }
            double total = cum[samples];
            if (total < 1e-9) return u;
            var nu = new double[n];
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                double target = total * i / (n - 1);
                while (c < samples - 1 && cum[c + 1] < target) c++;
                double span = cum[c + 1] - cum[c];
                double g = span > 0 ? (target - cum[c]) / span : 0;
                nu[i] = us[c] + (us[c + 1] - us[c]) * Math.Max(0, Math.Min(1, g));
            }
            nu[0] = 0; nu[n - 1] = 1;
            u = nu;
        }
        return u;
    }

    static double ClampL(double L) => L < 0 ? 0 : L > 100 ? 100 : L;

    static Rgb[] BuildDiverging()
    {
        var white = LabToMsh(CentreLightness, 0, 0); // (95, 0, 0)
        var blue = SrgbToMsh(NeonBlue); var red = SrgbToMsh(NeonRed);
        int half = (TableSize - 1) / 2;
        var table = new Rgb[TableSize];
        // Moreland's Msh path gives the chroma and hue; the lightness is taken
        // linear between the ends. (The neons are more saturated than the
        // white in M, so Moreland's L* = M cos s would overshoot the centre by
        // 1.6 just before it; a straight L* keeps each half monotone and the
        // lightness equalisation exact.)
        double[] HalfLab(double[] from, double[] to, double t)
        {
            var lab = MorelandLab(from, to, t, CentreLightness);
            lab[0] = (1 - t) * from[0] * Math.Cos(from[1]) + t * to[0] * Math.Cos(to[1]);
            return lab;
        }
        var u1 = EqualiseLightness(t => ClampL(HalfLab(blue, white, t)[0]), half + 1);
        var u2 = EqualiseLightness(t => ClampL(HalfLab(white, red, t)[0]), half + 1);
        for (int i = 0; i <= half; i++) { var lab = HalfLab(blue, white, u1[i]); table[i] = LabToSrgb(lab[0], lab[1], lab[2]); }
        for (int i = 0; i <= half; i++) { var lab = HalfLab(white, red, u2[i]); table[half + i] = LabToSrgb(lab[0], lab[1], lab[2]); }
        table[0] = NeonBlue; table[TableSize - 1] = NeonRed;
        return table;
    }

    static Rgb[] BuildSequential()
    {
        // Kovesi's recipe: a piecewise-linear path through control points in
        // CIELAB (an order-1 B-spline), then lightness equalisation. The teal
        // between blue and green is the RGB midpoint the app's ramp passed
        // through; without it a straight Lab line between the two neons
        // greys out, and a hue sweep in LCh hits the sRGB gamut wall at cyan.
        var cps = new[] { Black, SeqBlue, SeqTeal, SeqGreen, White };
        int n = cps.Length;
        var L = new double[n]; var A = new double[n]; var B = new double[n];
        for (int i = 0; i < n; i++) { var lab = SrgbToLab(cps[i]); L[i] = lab[0]; A[i] = lab[1]; B[i] = lab[2]; }
        double[] Path(double t)
        {
            double x = t * (n - 1); int j = Math.Min(n - 2, (int)Math.Floor(x)); double f = x - j;
            return new[] { L[j] + (L[j + 1] - L[j]) * f, A[j] + (A[j + 1] - A[j]) * f, B[j] + (B[j + 1] - B[j]) * f };
        }
        var u = EqualiseLightness(t => ClampL(Path(t)[0]), TableSize);
        var table = new Rgb[TableSize];
        for (int i = 0; i < TableSize; i++) { var lab = Path(u[i]); table[i] = LabToSrgb(lab[0], lab[1], lab[2]); }
        table[0] = Black; table[TableSize - 1] = White;
        return table;
    }

    static Rgb[] BuildUtilisation()
    {
        var table = new Rgb[UtilisationSize];
        for (int i = 0; i < UtilisationSize; i++) table[i] = UtilisationExact(Math.Round(i * UtilisationStep, 9)); // exact at the breakpoints 0.7 and 1
        return table;
    }

    /// <summary>The web app's utilisation ramp (1.3.0): green → yellow-green → neon yellow below 1, neon red darkening above.</summary>
    static Rgb UtilisationExact(double u)
    {
        double[] g = { 0.00, 0.85, 0.42 }, yg = { 0.55, 0.95, 0.10 }, y = { 0.85, 1.00, 0.00 }, r = { 1.00, 0.12, 0.31 }, dr = { 0.75, 0.02, 0.18 };
        double[] c;
        if (u < 0.7) c = Mix(g, yg, u / 0.7);
        else if (u < 1.0) c = Mix(yg, y, (u - 0.7) / 0.3);
        else if (u < 1.3) c = Mix(r, dr, (u - 1.0) / 0.3);
        else return UtilisationOver;
        // half away from zero, as JavaScript's Math.round for the app's positive channels
        return new Rgb((int)Math.Round(255 * c[0], MidpointRounding.AwayFromZero), (int)Math.Round(255 * c[1], MidpointRounding.AwayFromZero), (int)Math.Round(255 * c[2], MidpointRounding.AwayFromZero));
    }

    static double[] Mix(double[] a, double[] b, double t) => new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };

    // ------------------------------------------------------------------
    // Uniformity measures (for tests and the bench)
    // ------------------------------------------------------------------

    /// <summary>Lightness steps |ΔL*| between successive entries of a table.</summary>
    public static double[] LightnessSteps(Rgb[] table)
    {
        var d = new double[table.Length - 1];
        double prev = Lightness(table[0]);
        for (int i = 1; i < table.Length; i++) { double L = Lightness(table[i]); d[i - 1] = Math.Abs(L - prev); prev = L; }
        return d;
    }

    // ------------------------------------------------------------------
    // The JavaScript twin
    // ------------------------------------------------------------------

    /// <summary>
    /// The ES module the web app and the bench load (src/Mite.Web/wwwroot/js/
    /// colormap-lut.js): the three tables as hex strings and the same index
    /// rules, tick and label functions as this class. Regenerated by the bench
    /// generator; a test checks the file on disk is current.
    /// </summary>
    public static string JavaScriptModule()
    {
        var sb = new StringBuilder();
        sb.Append("// Generated by Mite.Core (ColourMap.JavaScriptModule) — do not edit.\n");
        sb.Append("// Regenerate with `cd tools/webgen && dotnet run -c Release`; tests/Mite.Tests ColourMapTests checks it is current.\n");
        sb.Append("// One colour language: these tables are the ones Mesh Colour Map and Lath Preview use in Grasshopper.\n");
        Table(sb, "DIVERGING_LUT", DivergingTable);
        Table(sb, "SEQUENTIAL_LUT", SequentialTable);
        Table(sb, "UTILISATION_LUT", UtilisationTable);
        sb.Append("export const UTILISATION_STEP = ").Append(UtilisationStep.ToString(CultureInfo.InvariantCulture)).Append(";\n");
        sb.Append("export const UTILISATION_OVER = '").Append(UtilisationOver.Hex).Append("';\n");
        sb.Append("export const NAN_GREY = '").Append(Grey.Hex).Append("';\n");
        sb.Append(@"export function lutIndex(t, n) { if (Number.isNaN(t)) return (n - 1) >> 1; if (t <= 0) return 0; if (t >= 1) return n - 1; return Math.round(t * (n - 1)); }
export function diverging(t) { return DIVERGING_LUT[lutIndex(t, DIVERGING_LUT.length)]; }
export function sequential(t) { return SEQUENTIAL_LUT[lutIndex(t, SEQUENTIAL_LUT.length)]; }
export function utilisation(u) { if (!Number.isFinite(u)) return NAN_GREY; const i = Math.floor(Math.max(0, u) / UTILISATION_STEP + 1e-9); return i >= UTILISATION_LUT.length ? UTILISATION_OVER : UTILISATION_LUT[i]; }
export function hexToRgb(h) { return [parseInt(h.slice(1, 3), 16) / 255, parseInt(h.slice(3, 5), 16) / 255, parseInt(h.slice(5, 7), 16) / 255]; }
// Heckbert 1990 (Graphics Gems I, 'Nice numbers for graph labels'): nice step, ticks inside the range
export function niceNum(x, round) { const e = Math.floor(Math.log10(x)); const f = x / Math.pow(10, e); let nf; if (round) nf = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10; else nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10; return nf * Math.pow(10, e); }
export function tickStep(lo, hi, target = 5) { if (!(hi > lo)) return 0; return niceNum(niceNum(hi - lo, false) / (target - 1), true); }
export function ticks(lo, hi, target = 5) { const d = tickStep(lo, hi, target); if (!(d > 0)) return []; const out = []; for (let k = Math.ceil(lo / d - 1e-9); k * d <= hi + 1e-9 * d; k++) out.push(k === 0 ? 0 : k * d); return out; }
export function decimals(step) { return step > 0 ? Math.max(0, -Math.floor(Math.log10(step) + 1e-9)) : 0; }
export function label(v, dec) { const s = v.toFixed(dec); return s === '-' + (0).toFixed(dec) ? (0).toFixed(dec) : s; }
");
        return sb.ToString();
    }

    static void Table(StringBuilder sb, string name, Rgb[] table)
    {
        sb.Append("export const ").Append(name).Append(" = [");
        for (int i = 0; i < table.Length; i++)
        {
            if (i % 16 == 0) sb.Append("\n ");
            sb.Append('\'').Append(table[i].Hex).Append('\'');
            if (i < table.Length - 1) sb.Append(',');
        }
        sb.Append("];\n");
    }
}
