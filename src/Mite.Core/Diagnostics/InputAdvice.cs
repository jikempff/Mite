// Runtime messages that carry their own fix.
//
// Every message says what was measured (with the number), why it matters for
// the result, and what to change (with a number to change it to). Sources:
//   Shneiderman 1982, "Designing computer system messages" (Commun. ACM 25(9)):
//     specific, constructive ("indicate what users should do to correct the
//     problem"), positive tone (no "illegal", "invalid", "bad"), user-centred.
//   Neusesser & Sunwall 2023, "Error-message guidelines" and the scoring rubric
//     (Nielsen Norman Group): concise and precise description, constructive
//     advice, no blame; design by impact; "the very worst error messages are
//     those that don't exist" — safeguard against likely mistakes.
//   Becker et al. 2019, "Compiler error messages considered unhelpful" (ITiCSE
//     WGR): provide context, show solutions or hints, use logical argumentation,
//     report errors at the right time.
// Grasshopper levels follow impact: Warning when the output is probably not
// what was meant, Remark when it is usable and the note only explains it.
// The checks themselves are geometric: the curvature direction field is known
// at the vertices and interpolated linearly inside each face, so the mesh edge
// is the field's resolution; asymptotic directions satisfy
// k1 cos²t + k2 sin²t = 0 and cross at 2·min(t, 90° − t).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Geometry;

namespace Mite.Core.Diagnostics;

/// <summary>How much a runtime note matters (maps onto Grasshopper's Remark / Warning / Error).</summary>
public enum AdviceLevel { Remark, Warning, Error }

/// <summary>
/// One runtime note: a stable code, its level, the message (problem, reason,
/// fix) and the numbers behind it, so callers and tests need not parse text.
/// </summary>
public sealed class Advice
{
    public Advice(string code, AdviceLevel level, string message, double measured = double.NaN,
        double limit = double.NaN, double suggested = double.NaN)
    {
        Code = code; Level = level; Message = message;
        Measured = measured; Limit = limit; Suggested = suggested;
    }

    /// <summary>Stable identifier, e.g. "spacing-below-edge".</summary>
    public string Code { get; }
    public AdviceLevel Level { get; }
    /// <summary>Problem with its number, why it matters, and the change to make.</summary>
    public string Message { get; }
    /// <summary>The measured value that triggered the note (NaN when none).</summary>
    public double Measured { get; }
    /// <summary>The threshold it was compared with (NaN when none).</summary>
    public double Limit { get; }
    /// <summary>The value the message suggests (NaN when it suggests none).</summary>
    public double Suggested { get; }

    public override string ToString() => $"{Level} [{Code}] {Message}";
}

/// <summary>
/// Checks of component inputs against the mesh and the geometry, each
/// returning advice with the fix in the message. Pure functions: no Rhino.
/// </summary>
public static partial class InputAdvice
{
    /// <summary>
    /// Numbers in messages: three significant digits, current culture (as
    /// Grasshopper shows them), plain digits between 1e-4 and 1e7 (no "2.59E+03").
    /// </summary>
    public static string Format(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return v.ToString(CultureInfo.CurrentCulture);
        double r = RoundSignificant(v, 3);
        double a = Math.Abs(r);
        if (a == 0 || (a >= 1e-4 && a < 1e7)) return r.ToString("0.#######", CultureInfo.CurrentCulture);
        return r.ToString("G3", CultureInfo.CurrentCulture);
    }

    /// <summary>Rounds to <paramref name="digits"/> significant digits (0.35, not 0.35000000000000003).</summary>
    public static double RoundSignificant(double v, int digits)
    {
        if (v == 0 || double.IsNaN(v) || double.IsInfinity(v)) return v;
        int decimals = digits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(v)));
        if (decimals >= 0 && decimals <= 15) return Math.Round(v, decimals);
        double scale = Math.Pow(10, -decimals);
        return Math.Round(v / scale) * scale;
    }

    /// <summary>Bounding-box diagonal of the vertices where <paramref name="region"/> is true (all when null).</summary>
    public static double RegionSize(MeshData mesh, bool[]? region)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        int n = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (region != null && (i >= region.Length || !region[i])) continue;
            var p = mesh.Vertices[i];
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            n++;
        }
        if (n == 0) return 0;
        double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Spacing and step of an evenly spaced family against the mesh resolution
    /// and the size of the region the curves can run in.
    /// </summary>
    /// <param name="spacing">Spacing used (resolved).</param>
    /// <param name="spacingGiven">True when the user set it (automatic spacing is never below 4 edges).</param>
    /// <param name="step">Step used (resolved).</param>
    /// <param name="stepGiven">True when the user set it.</param>
    /// <param name="meanEdge">Mean mesh edge length.</param>
    /// <param name="regionSize">Diagonal of the region the family can fill (see <see cref="RegionSize"/>).</param>
    /// <param name="name">The input's name in the message ("Spacing"; Chebyshev Net's "EdgeLength").</param>
    /// <param name="what">What the input spaces: "curves" (a family), "nodes" (a web's node distance).</param>
    /// <param name="reason">Why the mesh edge is the resolution (null: the interpolated curvature directions).</param>
    public static List<Advice> Spacing(double spacing, bool spacingGiven, double step, bool stepGiven,
        double meanEdge, double regionSize, string name = "Spacing", string what = "curves", string? reason = null)
    {
        var list = new List<Advice>();
        if (!(spacing > 0) || !(meanEdge > 0)) return list;

        if (spacingGiven && spacing < meanEdge)
        {
            int subdivisions = (int)Math.Ceiling(Math.Log(meanEdge / spacing, 2));
            list.Add(new Advice("spacing-below-edge", AdviceLevel.Warning,
                $"{name} {Format(spacing)} is below the mean mesh edge {Format(meanEdge)}. " +
                (reason ?? "The curvature directions are known at the vertices and interpolated inside each face, ") +
                $"so {what} closer than one edge are placed by the facets, not by the surface. " +
                $"Raise {name} to {Format(meanEdge)} or more, or subdivide the mesh {subdivisions}× " +
                "(each subdivision halves the edges).",
                spacing, meanEdge, meanEdge));
        }

        if (regionSize > 0 && spacing >= 0.5 * regionSize)
        {
            double suggested = RoundSignificant(regionSize / 10.0, 2);
            list.Add(new Advice("spacing-exceeds-region", AdviceLevel.Warning,
                $"{name} {Format(spacing)} is more than half the size of the region the {what} can run in " +
                $"({Format(regionSize)} across), so each family gets one or two {what}. " +
                $"Lower {name} to about {Format(suggested)} (a tenth of the region) for a full net.",
                spacing, 0.5 * regionSize, suggested));
        }

        if (stepGiven && step > 0)
        {
            if (step > 0.25 * spacing)
            {
                double suggested = RoundSignificant(Math.Min(0.1 * spacing, 0.5 * meanEdge), 2);
                list.Add(new Advice("step-coarse-for-spacing", AdviceLevel.Warning,
                    $"Step {Format(step)} is more than a quarter of Spacing {Format(spacing)}. " +
                    "A trace checks its distance to the other curves once per step, so it can pass a neighbour " +
                    $"before it notices. Set Step to {Format(suggested)} or to 0 (automatic).",
                    step, 0.25 * spacing, suggested));
            }
            else if (step > meanEdge)
            {
                double suggested = RoundSignificant(0.5 * meanEdge, 2);
                list.Add(new Advice("step-longer-than-edge", AdviceLevel.Warning,
                    $"Step {Format(step)} is longer than the mean mesh edge {Format(meanEdge)}: " +
                    "each step skips faces without reading their directions, so curves cut across curved parts. " +
                    $"Set Step to {Format(suggested)} or to 0 (automatic: the smaller of Spacing / 10 and half an edge).",
                    step, meanEdge, suggested));
            }
        }
        return list;
    }

    /// <summary>
    /// Crossing angle (degrees, 0–90) of the two asymptotic directions at a
    /// vertex with principal curvatures k1, k2; NaN where k1·k2 ≥ 0.
    /// From k1 cos²t + k2 sin²t = 0: tan t = √(−k1/k2), crossing = 2·min(t, 90° − t).
    /// </summary>
    public static double AsymptoticCrossingAngle(double k1, double k2)
    {
        if (!(k1 * k2 < 0)) return double.NaN;
        double t = Math.Atan(Math.Sqrt(-k1 / k2));
        return 2.0 * Math.Min(t, 0.5 * Math.PI - t) * 180.0 / Math.PI;
    }

    /// <summary>Counts behind <see cref="SaddleRegion"/>.</summary>
    public readonly struct SaddleCensus
    {
        public SaddleCensus(int vertices, int saddle, int usable, double minCrossing, double medianCrossing, double maxCrossing,
            double minK, double maxK, bool flat)
        {
            Vertices = vertices; Saddle = saddle; Usable = usable;
            MinCrossing = minCrossing; MedianCrossing = medianCrossing; MaxCrossing = maxCrossing;
            MinK = minK; MaxK = maxK; Flat = flat;
        }
        public int Vertices { get; }
        /// <summary>Vertices with K &lt; 0 (same test as the asymptotic field).</summary>
        public int Saddle { get; }
        /// <summary>Saddle vertices whose families cross at MinAngle or more.</summary>
        public int Usable { get; }
        /// <summary>Smallest crossing angle (degrees) over the saddle vertices; NaN when none.</summary>
        public double MinCrossing { get; }
        /// <summary>Median crossing angle (degrees) over the saddle vertices; NaN when none.</summary>
        public double MedianCrossing { get; }
        /// <summary>Largest crossing angle (degrees) over the saddle vertices; NaN when none.</summary>
        public double MaxCrossing { get; }
        public double MinK { get; }
        public double MaxK { get; }
        /// <summary>True when the mesh has no measurable curvature at all.</summary>
        public bool Flat { get; }
    }

    /// <summary>
    /// Census of the saddle region with the same thresholds as
    /// AsymptoticCurves.ComputeDirections (relative K &lt; 0 test, flat floor at
    /// 0.1 % of the mean curvature scale, crossing angle against MinAngle).
    /// </summary>
    /// <param name="diagonal">Mesh diagonal; when given, a mean curvature below 1e-6 / diagonal counts as flat.</param>
    public static SaddleCensus CountSaddle(PrincipalCurvature.Result curvature, double minAngle, double diagonal = 0)
    {
        int nv = curvature.K1.Length;
        double kRef = 0;
        for (int i = 0; i < nv; i++) kRef += Math.Max(Math.Abs(curvature.K1[i]), Math.Abs(curvature.K2[i]));
        kRef = nv > 0 ? kRef / nv : 0;
        double kFloor = 1e-3 * kRef;
        int saddle = 0, usable = 0;
        var crossings = new List<double>();
        double minK = double.MaxValue, maxK = double.MinValue;
        for (int i = 0; i < nv; i++)
        {
            double k1 = curvature.K1[i], k2 = curvature.K2[i];
            double K = k1 * k2;
            minK = Math.Min(minK, K); maxK = Math.Max(maxK, K);
            double scale = Math.Max(Math.Abs(k1), Math.Abs(k2));
            if (scale <= kFloor) continue;
            if (K >= -1e-6 * scale * scale) continue;
            saddle++;
            double c = AsymptoticCrossingAngle(k1, k2);
            crossings.Add(c);
            if (c >= Math.Max(0.0, minAngle)) usable++;
        }
        crossings.Sort();
        double minCross = crossings.Count > 0 ? crossings[0] : double.NaN;
        double maxCross = crossings.Count > 0 ? crossings[crossings.Count - 1] : double.NaN;
        // upper median: at least half the saddle vertices cross at this angle or more
        double medianCross = crossings.Count > 0 ? crossings[crossings.Count / 2] : double.NaN;
        if (nv == 0) { minK = 0; maxK = 0; }
        bool flat = !(kRef > 0) || (diagonal > 0 && kRef * diagonal < 1e-6);
        return new SaddleCensus(nv, saddle, usable, minCross, medianCross, maxCross, minK, maxK, flat);
    }

    /// <summary>
    /// Explains an empty or shrunken asymptotic region: no K &lt; 0 at all, or
    /// MinAngle leaving out all (Warning) or a large share (Remark, ≥ 25 %) of it.
    /// </summary>
    public static List<Advice> SaddleRegion(PrincipalCurvature.Result curvature, double minAngle, double diagonal = 0)
    {
        var c = CountSaddle(curvature, minAngle, diagonal);
        var list = new List<Advice>();
        if (c.Vertices == 0) return list;
        if (c.Flat)
        {
            list.Add(new Advice("mesh-flat", AdviceLevel.Warning,
                "The mesh is flat: it has no curvature, so there are no asymptotic directions to follow " +
                "(every direction is asymptotic). Bend it into a saddle, or use Geodesic Net for straight laths on a plane.",
                0, double.NaN, double.NaN));
            return list;
        }
        if (c.Saddle == 0)
        {
            list.Add(new Advice("no-saddle", AdviceLevel.Warning,
                $"No saddle-shaped part: at all {c.Vertices} vertices the surface bends the same way in every direction " +
                $"(K = k1·k2 from {Format(c.MinK)} to {Format(c.MaxK)}, never clearly below 0), and asymptotic curves exist only where K < 0. " +
                "On a dome or vault use Curvature Streamlines or Geodesic Net; for asymptotic laths the surface needs a saddle.",
                c.MinK, 0, double.NaN));
            return list;
        }
        if (c.Usable == 0)
        {
            double half = Math.Max(0, Math.Floor(c.MedianCrossing));
            double all = Math.Max(0, Math.Floor(c.MinCrossing));
            list.Add(new Advice("min-angle-excludes-all", AdviceLevel.Warning,
                $"MinAngle {Format(minAngle)}° leaves out all {c.Saddle} saddle vertices: there the two families cross at " +
                $"{Format(c.MinCrossing)}° to {Format(c.MaxCrossing)}°. " +
                (half > all
                    ? $"Lower MinAngle to {Format(half)}° to keep at least half of the region or to {Format(all)}° to keep all of it"
                    : $"Lower MinAngle to {Format(all)}° to keep all of it") +
                " (the laths will cross that flat), or make the saddle stronger.",
                c.MaxCrossing, minAngle, half));
            return list;
        }
        double excluded = 1.0 - c.Usable / (double)c.Saddle;
        if (excluded >= 0.25)
        {
            list.Add(new Advice("min-angle-excludes-part", AdviceLevel.Remark,
                $"MinAngle {Format(minAngle)}° leaves out {Math.Round(100 * excluded)} % of the saddle vertices " +
                $"({c.Saddle - c.Usable} of {c.Saddle}), where the families cross at a shallower angle near the K = 0 line. " +
                "Lower MinAngle to let the net reach further, at the price of flatter crossings.",
                excluded, 0.25, double.NaN));
        }
        return list;
    }

    /// <summary>
    /// A seed point far from the mesh: Remark beyond two mean edges (and 1 %
    /// of the diagonal), Warning beyond 10 % of the diagonal.
    /// </summary>
    /// <param name="index">Position of the point in the SeedPoints list.</param>
    /// <param name="distance">Distance from the point to the mesh surface.</param>
    public static Advice? SeedPoint(int index, double distance, double meanEdge, double diagonal)
    {
        double limit = Math.Max(2.0 * meanEdge, 0.01 * diagonal);
        if (!(distance > limit)) return null;
        var level = distance > 0.1 * diagonal ? AdviceLevel.Warning : AdviceLevel.Remark;
        return new Advice("seed-off-surface", level,
            $"SeedPoint {index} is {Format(distance)} from the mesh (mean edge {Format(meanEdge)}); " +
            "the nearest vertex is used instead. If that is not where you meant to start, " +
            "move the point onto the surface (Pull To Mesh) or check that it belongs to this mesh.",
            distance, limit, double.NaN);
    }

    /// <summary>Seed indices outside 0 … vertexCount − 1.</summary>
    public static Advice? SeedIndices(IReadOnlyList<int> outOfRange, int vertexCount)
    {
        if (outOfRange == null || outOfRange.Count == 0) return null;
        string shown = string.Join(", ", outOfRange.Take(5).Select(i => i.ToString(CultureInfo.CurrentCulture)));
        if (outOfRange.Count > 5) shown += $" and {outOfRange.Count - 5} more";
        return new Advice("seed-index-out-of-range", AdviceLevel.Warning,
            $"Seed{(outOfRange.Count > 1 ? "s" : "")} {shown} skipped: the mesh has {vertexCount} vertices (0 to {vertexCount - 1}). " +
            "Indices refer to the input mesh and change after Mesh Cleanup or remeshing; SeedPoints keep working.",
            outOfRange.Count, vertexCount - 1, double.NaN);
    }

    /// <summary>
    /// Lath section against the lath lengths, to catch unit slips: a section
    /// bigger than a quarter of the median lath (millimetres typed into a
    /// metre model) or smaller than 1/10 000 of it (metres in a millimetre model).
    /// </summary>
    /// <param name="width">Section width in model units.</param>
    /// <param name="thickness">Section thickness in model units (NaN when the component has none).</param>
    /// <param name="lathLengths">Lengths of the laths, model units.</param>
    /// <param name="modelToMetres">Model unit in metres (1 for metres, 0.001 for millimetres).</param>
    /// <param name="unitName">Name of the model unit for the message.</param>
    public static Advice? SectionScale(double width, double thickness, IEnumerable<double> lathLengths,
        double modelToMetres, string unitName)
    {
        var lengths = lathLengths.Where(l => l > 0 && !double.IsNaN(l) && !double.IsInfinity(l)).OrderBy(l => l).ToArray();
        if (lengths.Length == 0 || !(width > 0)) return null;
        double median = lengths.Length % 2 == 1 ? lengths[lengths.Length / 2]
            : 0.5 * (lengths[lengths.Length / 2 - 1] + lengths[lengths.Length / 2]);
        bool hasT = thickness > 0;
        double big = hasT ? Math.Max(width, thickness) : width;
        double small = hasT ? Math.Min(width, thickness) : width;
        string which = hasT && thickness > width ? "Thickness" : "Width";
        string sizes = hasT ? $"Width {Format(width)} × Thickness {Format(thickness)}" : $"Width {Format(width)}";
        if (!(modelToMetres > 0)) modelToMetres = 1;

        if (big > 0.25 * median)
        {
            // Read the numbers as millimetres: do they fit then?
            double asMm = 0.001 / modelToMetres;
            double wMm = width * asMm, tMm = hasT ? thickness * asMm : double.NaN;
            bool unitSlip = asMm < 0.999 && big * asMm <= 0.25 * median;
            string fix = unitSlip
                ? $"Width and Thickness are in model units ({unitName}); if you meant millimetres, enter Width {Format(wMm)}" +
                  (hasT ? $" and Thickness {Format(tMm)}." : ".")
                : $"Width and Thickness are in model units ({unitName}); laths this deep cannot follow the surface — " +
                  "check the values or use longer laths.";
            return new Advice("section-larger-than-laths", AdviceLevel.Warning,
                $"{sizes} is large against the laths: {which} is {Format(big / median)} × the median lath length {Format(median)}. {fix}",
                big / median, 0.25, unitSlip ? wMm : double.NaN);
        }
        if (small < 1e-4 * median)
        {
            double asM = 1.0 / modelToMetres;
            bool unitSlip = asM > 1.001 && small * asM >= 1e-4 * median && big * asM <= 0.25 * median;
            string fix = unitSlip
                ? $"Width and Thickness are in model units ({unitName}); if you meant metres, enter Width {Format(width * asM)}" +
                  (hasT ? $" and Thickness {Format(thickness * asM)}." : ".")
                : $"Width and Thickness are in model units ({unitName}); check the values.";
            return new Advice("section-tiny", AdviceLevel.Warning,
                $"{sizes} is tiny against the laths: {(small == width ? "Width" : "Thickness")} is 1/{Format(median / small)} of the median lath length {Format(median)}. {fix}",
                small / median, 1e-4, unitSlip ? width * asM : double.NaN);
        }
        return null;
    }
}
