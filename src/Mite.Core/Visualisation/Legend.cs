using System;
using System.Collections.Generic;
using System.Globalization;
using Mite.Core.Geometry;

namespace Mite.Core.Visualisation;

/// <summary>
/// Colour-bar legends: nice tick values inside a value range (Heckbert 1990,
/// "Nice numbers for graph labels", Graphics Gems I, pp. 61–63 — the step is
/// the range rounded to 1, 2 or 5 × 10ⁿ and divided by target − 1, rounded
/// again; here the ticks are kept inside the range, since a colour bar cannot
/// be extended beyond the data the way an axis can, and the exact ends are
/// labelled separately), the colour samples of a bar, and a coloured strip
/// mesh with its tick points for the Grasshopper viewport. The same tick rule
/// is in the JavaScript twin (<see cref="ColourMap.JavaScriptModule"/>), so
/// the web app, the bench and Mesh Colour Map label a range identically.
/// </summary>
public static class Legend
{
    public sealed class Result
    {
        public ColourMap.Palette Palette;
        public double Lo, Hi;
        /// <summary>Nice tick values inside [Lo, Hi], ascending; 0 is one of them whenever the range contains it.</summary>
        public double[] Ticks = Array.Empty<double>();
        /// <summary>Tick labels (invariant culture, as many decimals as the step needs).</summary>
        public string[] Labels = Array.Empty<string>();
        /// <summary>Tick positions along the bar, 0 = Lo, 1 = Hi.</summary>
        public double[] Positions = Array.Empty<double>();
        /// <summary>The tick step (0 when the range is degenerate).</summary>
        public double Step;
        /// <summary>Colour samples along the bar, Samples entries from Lo to Hi.</summary>
        public ColourMap.Rgb[] Colours = Array.Empty<ColourMap.Rgb>();
        /// <summary>Labels of the two ends.</summary>
        public string LoLabel = "", HiLabel = "";
    }

    /// <summary>Heckbert's nice number: 1, 2, 5 × 10ⁿ nearest (round) or not below (ceiling) x.</summary>
    public static double NiceNum(double x, bool round)
    {
        if (!(x > 0) || double.IsInfinity(x)) return 0;
        int e = (int)Math.Floor(Math.Log10(x));
        double p = Math.Pow(10, e), f = x / p, nf;
        if (round) nf = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10;
        else nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
        return nf * p;
    }

    /// <summary>The tick step for a range: nicenum(nicenum(hi − lo) / (target − 1)), Heckbert's loose labelling with NTICK = target.</summary>
    public static double TickStep(double lo, double hi, int target = 5)
    {
        if (!(hi > lo)) return 0;
        return NiceNum(NiceNum(hi - lo, false) / (Math.Max(2, target) - 1), true);
    }

    /// <summary>Nice tick values inside [lo, hi] (multiples of the step).</summary>
    public static double[] Ticks(double lo, double hi, int target = 5)
    {
        double d = TickStep(lo, hi, target);
        if (!(d > 0)) return Array.Empty<double>();
        var outp = new List<double>();
        for (long k = (long)Math.Ceiling(lo / d - 1e-9); k * d <= hi + 1e-9 * d; k++) outp.Add(k == 0 ? 0 : k * d);
        return outp.ToArray();
    }

    /// <summary>Decimals a step needs: max(0, −floor(log10 step)).</summary>
    public static int Decimals(double step) => step > 0 ? Math.Max(0, -(int)Math.Floor(Math.Log10(step) + 1e-9)) : 0;

    /// <summary>Formats a tick with the step's decimals (invariant culture, "-0.0" normalised to "0.0").</summary>
    public static string Label(double v, int decimals)
    {
        string s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        string zero = 0.0.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return s == "-" + zero ? zero : s;
    }

    /// <summary>A short label for an end value: 3 significant digits, trailing zeros dropped.</summary>
    public static string EndLabel(double v)
    {
        if (v == 0) return "0";
        if (double.IsNaN(v) || double.IsInfinity(v)) return v.ToString(CultureInfo.InvariantCulture);
        int e = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        int dec = Math.Max(0, 2 - e);
        double scale = Math.Pow(10, e - 2);
        string s = (Math.Round(v / scale) * scale).ToString("F" + dec, CultureInfo.InvariantCulture);
        if (s.Contains(".")) s = s.TrimEnd('0').TrimEnd('.');
        return s == "-0" ? "0" : s;
    }

    /// <summary>Ticks, labels and colour samples of a bar over [lo, hi] for a palette.</summary>
    public static Result Build(double lo, double hi, ColourMap.Palette palette, int samples = 64, int target = 5)
    {
        if (hi < lo) { double t = lo; lo = hi; hi = t; }
        var r = new Result { Palette = palette, Lo = lo, Hi = hi, Step = TickStep(lo, hi, target) };
        r.Ticks = Ticks(lo, hi, target);
        int dec = Decimals(r.Step);
        r.Labels = new string[r.Ticks.Length]; r.Positions = new double[r.Ticks.Length];
        for (int i = 0; i < r.Ticks.Length; i++)
        {
            r.Labels[i] = Label(r.Ticks[i], dec);
            r.Positions[i] = hi > lo ? (r.Ticks[i] - lo) / (hi - lo) : 0.5;
        }
        r.LoLabel = EndLabel(lo); r.HiLabel = EndLabel(hi);
        samples = Math.Max(2, samples);
        r.Colours = new ColourMap.Rgb[samples];
        for (int i = 0; i < samples; i++) r.Colours[i] = ColourMap.Sample(palette, i / (double)(samples - 1));
        return r;
    }

    /// <summary>A coloured legend strip: the quad mesh, one colour per vertex, and the tick points on its right edge.</summary>
    public sealed class Strip
    {
        public MeshData Mesh = new MeshData(Array.Empty<Vec3d>(), Array.Empty<int[]>());
        public ColourMap.Rgb[] VertexColours = Array.Empty<ColourMap.Rgb>();
        /// <summary>One point per tick, just right of the strip, where its label belongs.</summary>
        public Vec3d[] TickPoints = Array.Empty<Vec3d>();
        /// <summary>The two end points (Lo at the bottom, Hi at the top), right of the strip.</summary>
        public Vec3d LoPoint, HiPoint;
        public double Height, Width;
    }

    /// <summary>
    /// Builds the strip at <paramref name="origin"/> (its lower-left corner),
    /// rising along <paramref name="up"/> by <paramref name="height"/> and
    /// extending along <paramref name="right"/> by <paramref name="width"/>:
    /// one quad per colour sample, vertex colours stepping with the samples,
    /// tick points one width to the right of the strip.
    /// </summary>
    public static Strip BuildStrip(Result legend, Vec3d origin, Vec3d up, Vec3d right, double height, double width)
    {
        int n = legend.Colours.Length;
        var u = up.Normalized(); var r = right.Normalized();
        var verts = new Vec3d[2 * (n + 1)];
        var cols = new ColourMap.Rgb[2 * (n + 1)];
        for (int i = 0; i <= n; i++)
        {
            double y = height * i / n;
            verts[2 * i] = origin + u * y;
            verts[2 * i + 1] = origin + u * y + r * width;
            var c = legend.Colours[Math.Min(n - 1, i)];
            cols[2 * i] = c; cols[2 * i + 1] = c;
        }
        var faces = new int[n][];
        for (int i = 0; i < n; i++) faces[i] = new[] { 2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2 };
        var s = new Strip { Mesh = new MeshData(verts, faces), VertexColours = cols, Height = height, Width = width };
        s.TickPoints = new Vec3d[legend.Positions.Length];
        for (int i = 0; i < legend.Positions.Length; i++) s.TickPoints[i] = origin + u * (height * legend.Positions[i]) + r * (2 * width);
        s.LoPoint = origin + r * (2 * width);
        s.HiPoint = origin + u * height + r * (2 * width);
        return s;
    }
}
