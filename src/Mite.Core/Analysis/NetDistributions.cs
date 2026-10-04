// Distributions of net metrics: spacing across each family, crossing angles,
// utilization — the whole spread, not only the extremes.
//
// Sources
// - Freedman & Diaconis 1981, "On the histogram as a density estimator: L2
//   theory" (Z. Wahrscheinlichkeitstheorie 57): cell width = twice the
//   interquartile range divided by the cube root of the sample size.
// - Scott 1979, "On optimal and data-based histograms" (Biometrika 66):
//   h* = (6 / ∫f'²)^(1/3) n^(-1/3); the IQR rule is its robust form.
// - Sturges 1926 (k = ⌈log2 n⌉ + 1 classes) as the fallback when IQR = 0.
// - Hyndman & Fan 1996, "Sample quantiles in statistical packages" (Am. Stat.
//   50): quantiles by linear interpolation between order statistics (type 7).
// - Jobard & Lefer 1997 (evenly spaced streamlines, d_sep) and Mebarki,
//   Alliez & Devillers 2005, who note that "a high quality streamline
//   placement ... has no formal definition": the spacing distribution below
//   is that definition for a lath family.
// - Pottmann, Huang, Deng, Schiftner, Lin, Fu & Wallner 2010, "Geodesic
//   patterns" (ACM TOG 29): strip width measured across the curve, tolerance
//   bands of 5 % and 7.5 % around the target width (their breakpoint rule).

using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Core.Analysis;

/// <summary>
/// A sorted sample with its summary statistics and a histogram. Quantiles use
/// linear interpolation between order statistics (Hyndman–Fan type 7, the R
/// and NumPy default).
/// </summary>
public sealed class Distribution
{
    /// <summary>The sample, sorted ascending (NaN and infinities removed).</summary>
    public double[] Values { get; }
    public int Count => Values.Length;
    public double Min => Count > 0 ? Values[0] : double.NaN;
    public double Max => Count > 0 ? Values[Count - 1] : double.NaN;
    public double Mean { get; }
    /// <summary>Sample standard deviation (n − 1); 0 for fewer than two values.</summary>
    public double StdDev { get; }
    public double Median => Quantile(0.5);
    public double Q1 => Quantile(0.25);
    public double Q3 => Quantile(0.75);
    public double Iqr => Q3 - Q1;
    /// <summary>StdDev / Mean (0 when the mean is 0).</summary>
    public double CoefficientOfVariation => Mean != 0 ? StdDev / Math.Abs(Mean) : 0;

    public Distribution(IEnumerable<double> values)
    {
        var v = values.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToArray();
        Array.Sort(v);
        Values = v;
        if (v.Length == 0) { Mean = double.NaN; StdDev = 0; return; }
        double s = 0;
        foreach (double x in v) s += x;
        Mean = s / v.Length;
        if (v.Length > 1)
        {
            double q = 0;
            foreach (double x in v) q += (x - Mean) * (x - Mean);
            StdDev = Math.Sqrt(q / (v.Length - 1));
        }
    }

    /// <summary>Quantile p ∈ [0, 1] (type 7: h = (n − 1) p, linear between neighbours).</summary>
    public double Quantile(double p)
    {
        if (Count == 0) return double.NaN;
        if (p <= 0) return Values[0];
        if (p >= 1) return Values[Count - 1];
        double h = (Count - 1) * p;
        int lo = (int)Math.Floor(h);
        if (lo >= Count - 1) return Values[Count - 1];
        return Values[lo] + (h - lo) * (Values[lo + 1] - Values[lo]);
    }

    /// <summary>Share of the sample (0..1) inside the closed interval [lo, hi].</summary>
    public double ShareWithin(double lo, double hi)
    {
        if (Count == 0) return 0;
        int n = 0;
        foreach (double x in Values) if (x >= lo && x <= hi) n++;
        return n / (double)Count;
    }

    /// <summary>Share of the sample within ±tolerance (relative) of target.</summary>
    public double ShareNear(double target, double relativeTolerance) =>
        ShareWithin(target * (1 - relativeTolerance), target * (1 + relativeTolerance));

    /// <summary>Share of the sample strictly above the threshold.</summary>
    public double ShareAbove(double threshold)
    {
        if (Count == 0) return 0;
        int n = 0;
        foreach (double x in Values) if (x > threshold) n++;
        return n / (double)Count;
    }

    /// <summary>
    /// Histogram of the sample. bins &gt; 0 fixes the number of bins; 0 picks
    /// the Freedman–Diaconis width 2·IQR·n^(−1/3), or Sturges' ⌈log2 n⌉ + 1
    /// bins when the IQR is zero. lo/hi fix the range (values outside are
    /// counted in Below / Above); NaN uses the sample's range. The bin count is
    /// capped at maxBins.
    /// </summary>
    public Histogram Histogram(int bins = 0, double lo = double.NaN, double hi = double.NaN, int maxBins = 40)
    {
        if (double.IsNaN(lo)) lo = Min;
        if (double.IsNaN(hi)) hi = Max;
        string rule = "fixed";
        if (Count == 0) return new Histogram(new[] { 0.0, 1.0 }, new int[1], 0, 0, "empty");
        if (!(hi > lo))
        {
            // all values equal (or a degenerate range): one bin centred on the value
            double w = Math.Abs(lo) > 0 ? Math.Abs(lo) * 1e-3 : 1e-3;
            int inside = Values.Count(x => x >= lo - w && x <= lo + w);
            return new Histogram(new[] { lo - w, lo + w }, new[] { inside }, Values.Count(x => x < lo - w), Values.Count(x => x > lo + w), "single value");
        }
        if (bins <= 0)
        {
            double iqr = Iqr;
            if (iqr > 0 && Count > 1)
            {
                double h = 2 * iqr / Math.Pow(Count, 1.0 / 3.0);
                bins = (int)Math.Ceiling((hi - lo) / h - 1e-9);
                rule = "Freedman–Diaconis";
            }
            else
            {
                bins = (int)Math.Ceiling(Math.Log(Math.Max(1, Count), 2)) + 1;
                rule = "Sturges";
            }
        }
        if (bins < 1) bins = 1;
        if (bins > maxBins) { bins = maxBins; rule += " (capped)"; }
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++) edges[i] = lo + (hi - lo) * i / bins;
        edges[bins] = hi;
        var counts = new int[bins];
        int below = 0, above = 0;
        double width = (hi - lo) / bins;
        foreach (double x in Values)
        {
            if (x < lo) { below++; continue; }
            if (x > hi) { above++; continue; }
            int k = (int)((x - lo) / width);
            if (k >= bins) k = bins - 1; // the last bin is closed on the right
            if (k < 0) k = 0;
            counts[k]++;
        }
        return new Histogram(edges, counts, below, above, rule);
    }
}

/// <summary>Bin edges (one more than the counts) and counts of a histogram.</summary>
public sealed class Histogram
{
    public double[] Edges { get; }
    public int[] Counts { get; }
    /// <summary>Values below the first edge / above the last edge (fixed ranges only).</summary>
    public int Below { get; }
    public int Above { get; }
    /// <summary>Which bin rule produced the bins ("Freedman–Diaconis", "Sturges", "fixed", …).</summary>
    public string Rule { get; }
    public int Bins => Counts.Length;
    public double BinWidth => (Edges[Edges.Length - 1] - Edges[0]) / Math.Max(1, Counts.Length);

    public Histogram(double[] edges, int[] counts, int below, int above, string rule)
    {
        Edges = edges; Counts = counts; Below = below; Above = above; Rule = rule;
    }
}

/// <summary>
/// Spacing, crossing-angle and utilization distributions of a lath net.
/// </summary>
public static class NetDistributions
{
    public sealed class SpacingOptions
    {
        /// <summary>Arc-length step between samples along each curve (0 = automatic: Target / 4, or the mean segment length when Target is 0).</summary>
        public double Step { get; set; }
        /// <summary>Neighbours farther than this are ignored — the side counts as open (0 = automatic: 3 × Target, or 3 × the median nearest spacing when Target is 0).</summary>
        public double MaxDistance { get; set; }
        /// <summary>The intended spacing (0 = the median of the measured spacings).</summary>
        public double Target { get; set; }
    }

    public sealed class SpacingResult
    {
        /// <summary>All one-sided spacings of the family (left and right of every sample, open sides left out).</summary>
        public Distribution Spacing { get; }
        /// <summary>Sample points along the curves.</summary>
        public Vec3d[] Points { get; }
        /// <summary>Index of the curve each sample lies on.</summary>
        public int[] Curve { get; }
        /// <summary>Spacing to the nearest curve of the family on the left (n × t side) / right; NaN = open side (border curve or gap wider than MaxDistance).</summary>
        public double[] Left { get; }
        public double[] Right { get; }
        /// <summary>The target the shares refer to (the given Target, or the median).</summary>
        public double Target { get; }
        public double Step { get; }
        public double MaxDistance { get; }
        /// <summary>Share of the measured spacings within ±5 % / ±7.5 % of Target (Pottmann et al. 2010's strip-width tolerances).</summary>
        public double Within5 => Spacing.ShareNear(Target, 0.05);
        public double Within75 => Spacing.ShareNear(Target, 0.075);
        /// <summary>Number of samples with at least one open side.</summary>
        public int OpenSides { get; }

        internal SpacingResult(Distribution d, Vec3d[] pts, int[] curve, double[] left, double[] right, double target, double step, double maxDist, int open)
        {
            Spacing = d; Points = pts; Curve = curve; Left = left; Right = right; Target = target; Step = step; MaxDistance = maxDist; OpenSides = open;
        }
    }

    /// <summary>
    /// Spacing of one family measured across the curves: at samples every Step
    /// of arc length, the plane through the sample normal to the curve's tangent
    /// t is cut with every other curve of the family; the nearest cut on each
    /// side (sign of (q − p)·(n × t)) within MaxDistance and within 45° of the
    /// tangent plane is that side's spacing, as the 3D distance |q − p|. This is
    /// the strip width of Pottmann et al. 2010 (measured perpendicular to the
    /// curve); on parallel lines it is their distance exactly. A curve's own
    /// other passes count as neighbours once they are 2 MaxDistance of arc away
    /// (closed loops, spirals).
    /// </summary>
    public static SpacingResult FamilySpacing(IReadOnlyList<Vec3d[]> family, Func<Vec3d, Vec3d> normalAt, SpacingOptions? options = null)
    {
        options ??= new SpacingOptions();
        var curves = family.Where(c => c != null && c.Length >= 2).ToList();
        // segment table
        var segA = new List<Vec3d>(); var segB = new List<Vec3d>(); var segCurve = new List<int>(); var segArc = new List<double>();
        var curveLen = new double[curves.Count]; var closed = new bool[curves.Count];
        double totalSeg = 0; int nSeg = 0;
        for (int c = 0; c < curves.Count; c++)
        {
            var l = curves[c]; double s = 0;
            for (int i = 0; i + 1 < l.Length; i++)
            {
                double len = (l[i + 1] - l[i]).Length;
                if (len <= 0) continue;
                segA.Add(l[i]); segB.Add(l[i + 1]); segCurve.Add(c); segArc.Add(s + len / 2);
                s += len; totalSeg += len; nSeg++;
            }
            curveLen[c] = s;
            closed[c] = s > 0 && (l[0] - l[l.Length - 1]).Length < 1e-9 * Math.Max(1, s);
        }
        double target = options.Target > 0 ? options.Target : 0;
        double step = options.Step > 0 ? options.Step : target > 0 ? target / 4 : nSeg > 0 ? totalSeg / nSeg : 1;

        // samples
        var pts = new List<Vec3d>(); var tan = new List<Vec3d>(); var cid = new List<int>(); var arc = new List<double>();
        for (int c = 0; c < curves.Count; c++)
        {
            double L = curveLen[c];
            if (L <= 0) continue;
            var cum = Cumulative(curves[c]);
            int n = Math.Max(1, (int)Math.Round(L / step));
            for (int k = 0; k < n; k++)
            {
                double s = (k + 0.5) * L / n;
                double h = Math.Min(0.5 * L / n, 0.5 * step);
                var p = PointAt(curves[c], cum, s);
                var t = PointAt(curves[c], cum, Math.Min(L, s + h)) - PointAt(curves[c], cum, Math.Max(0, s - h));
                if (t.Length <= 0) continue;
                pts.Add(p); tan.Add(t.Normalized()); cid.Add(c); arc.Add(s);
            }
        }

        double maxDist = options.MaxDistance > 0 ? options.MaxDistance : target > 0 ? 3 * target : 0;
        if (maxDist <= 0)
        {
            // no target, no cap: estimate the typical spacing from nearest
            // distances to other curves' vertices, then allow three of it
            var near = new List<double>();
            for (int i = 0; i < pts.Count; i += Math.Max(1, pts.Count / 200))
            {
                double best = double.MaxValue;
                for (int c = 0; c < curves.Count; c++)
                {
                    if (c == cid[i]) continue;
                    foreach (var q in curves[c]) { double d = (q - pts[i]).LengthSquared; if (d < best) best = d; }
                }
                if (best < double.MaxValue) near.Add(Math.Sqrt(best));
            }
            maxDist = near.Count > 0 ? 3 * new Distribution(near).Median : 1;
        }

        // spatial hash of segments, cell = maxDist: a cut within maxDist of a
        // sample lies on a segment whose box touches one of the 27 cells around it
        double cell = maxDist;
        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int s = 0; s < segA.Count; s++)
        {
            var a = segA[s]; var b = segB[s];
            int x0 = Cell(Math.Min(a.X, b.X), cell), x1 = Cell(Math.Max(a.X, b.X), cell);
            int y0 = Cell(Math.Min(a.Y, b.Y), cell), y1 = Cell(Math.Max(a.Y, b.Y), cell);
            int z0 = Cell(Math.Min(a.Z, b.Z), cell), z1 = Cell(Math.Max(a.Z, b.Z), cell);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
            {
                if (!grid.TryGetValue((x, y, z), out var list)) grid[(x, y, z)] = list = new List<int>();
                list.Add(s);
            }
        }

        var left = new double[pts.Count]; var right = new double[pts.Count];
        var stamp = new int[segA.Count];
        for (int s = 0; s < stamp.Length; s++) stamp[s] = -1;
        var values = new List<double>();
        int open = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var t = tan[i];
            var n = normalAt(p);
            var u = Vec3d.Cross(n, t);
            double ul = u.Length;
            left[i] = right[i] = double.NaN;
            if (ul < 1e-12) { open++; continue; }
            u = u / ul;
            var nn = Vec3d.Cross(t, u); // the normal made orthogonal to t
            double bestL = double.MaxValue, bestR = double.MaxValue;
            int cx = Cell(p.X, cell), cy = Cell(p.Y, cell), cz = Cell(p.Z, cell);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                foreach (int s in list)
                {
                    if (stamp[s] == i) continue;
                    stamp[s] = i;
                    int c = segCurve[s];
                    if (c == cid[i])
                    {
                        double ds = Math.Abs(segArc[s] - arc[i]);
                        if (closed[c]) ds = Math.Min(ds, curveLen[c] - ds);
                        if (ds < 2 * maxDist) continue;
                    }
                    double da = Vec3d.Dot(segA[s] - p, t), db = Vec3d.Dot(segB[s] - p, t);
                    if (da * db > 0 || da == db) continue;
                    var q = segA[s] + (da / (da - db)) * (segB[s] - segA[s]);
                    var d = q - p;
                    double lat = Vec3d.Dot(d, u);
                    if (Math.Abs(Vec3d.Dot(d, nn)) > Math.Abs(lat)) continue; // across a fold, not across the surface
                    double dist = d.Length;
                    if (dist > maxDist || dist <= 0) continue;
                    if (lat > 0) { if (dist < bestL) bestL = dist; }
                    else if (dist < bestR) bestR = dist;
                }
            }
            if (bestL < double.MaxValue) { left[i] = bestL; values.Add(bestL); }
            if (bestR < double.MaxValue) { right[i] = bestR; values.Add(bestR); }
            if (double.IsNaN(left[i]) || double.IsNaN(right[i])) open++;
        }
        var dist0 = new Distribution(values);
        if (target <= 0) target = dist0.Count > 0 ? dist0.Median : 0;
        return new SpacingResult(dist0, pts.ToArray(), cid.ToArray(), left, right, target, step, maxDist, open);
    }

    /// <summary>Family spacing with the surface normals of a mesh (smooth vertex normals at the closest point).</summary>
    public static SpacingResult FamilySpacing(MeshProjection proj, IReadOnlyList<Vec3d[]> family, SpacingOptions? options = null)
    {
        int hint = -1;
        return FamilySpacing(family, p =>
        {
            var hit = proj.ClosestPoint(p, hint);
            hint = hit.NearestVertex;
            return hit.SmoothNormal.Length > 0 ? hit.SmoothNormal : hit.Normal;
        }, options);
    }

    public sealed class AngleResult
    {
        /// <summary>Crossing angles in degrees, 0..90 (the acute angle between the two laths' lines).</summary>
        public Distribution Angles { get; }
        /// <summary>The angle at each crossing, in the order of <see cref="Points"/> (Angles.Values is sorted).</summary>
        public double[] PerCrossing { get; }
        public Vec3d[] Points { get; }
        internal AngleResult(Distribution a, double[] perCrossing, Vec3d[] p) { Angles = a; PerCrossing = perCrossing; Points = p; }
    }

    /// <summary>
    /// Crossing angles between the two families: at every A × B crossing found
    /// by <see cref="NetIntersections.Find"/>, the acute angle between the two
    /// polylines' tangents. The tangent is the chord over the segments on either
    /// side of the crossing, so a crossing on a vertex is not biased to one side.
    /// </summary>
    public static AngleResult CrossingAngles(IReadOnlyList<Vec3d[]> familyA, IReadOnlyList<Vec3d[]> familyB, double tolerance = 0.0)
    {
        var xs = NetIntersections.Find(familyA, familyB, tolerance);
        var ang = new List<double>(); var pts = new List<Vec3d>();
        foreach (var x in xs)
        {
            var ta = Tangent(familyA[x.CurveA], x.SegmentA, x.ParamA);
            var tb = Tangent(familyB[x.CurveB], x.SegmentB, x.ParamB);
            double c = Math.Abs(Vec3d.Dot(ta, tb));
            ang.Add(Math.Acos(Math.Min(1.0, c)) * 180.0 / Math.PI);
            pts.Add(x.Point);
        }
        return new AngleResult(new Distribution(ang), ang.ToArray(), pts.ToArray());
    }

    /// <summary>
    /// Utilization distribution of analysed laths: perLath = true takes each
    /// lath's peak (what decides whether it can be built); false pools every
    /// station of every lath.
    /// </summary>
    public static Distribution Utilization(IEnumerable<LathAnalysis.Result> laths, bool perLath = true) =>
        new Distribution(perLath ? laths.Select(r => r.MaxUtilization) : laths.SelectMany(r => r.Utilization));

    // ---------- helpers ----------

    private static int Cell(double x, double h) => (int)Math.Floor(x / h);

    private static Vec3d Tangent(Vec3d[] l, int seg, double param)
    {
        // vertex tangents (central chords) interpolated along the segment: exact
        // on straight laths, and not biased to one side when a crossing sits
        // near a vertex of a curved polyline
        Vec3d VertexTangent(int i)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(l.Length - 1, i + 1);
            var d = l[b] - l[a];
            return d.Length > 0 ? d.Normalized() : d;
        }
        double w = param < 0 ? 0 : param > 1 ? 1 : param;
        var t = (1 - w) * VertexTangent(seg) + w * VertexTangent(seg + 1);
        return t.Length > 0 ? t.Normalized() : (l[seg + 1] - l[seg]).Normalized();
    }

    private static double[] Cumulative(Vec3d[] l)
    {
        var cum = new double[l.Length];
        for (int i = 1; i < l.Length; i++) cum[i] = cum[i - 1] + (l[i] - l[i - 1]).Length;
        return cum;
    }

    internal static Vec3d PointAt(Vec3d[] l, double[] cum, double s)
    {
        if (s <= 0) return l[0];
        if (s >= cum[cum.Length - 1]) return l[l.Length - 1];
        int lo = 0, hi = cum.Length - 1; // cum[lo] <= s < cum[hi]
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= s) lo = m; else hi = m; }
        double len = cum[hi] - cum[lo];
        return len > 0 ? l[lo] + ((s - cum[lo]) / len) * (l[hi] - l[lo]) : l[lo];
    }
}
