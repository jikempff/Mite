using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// Splits laths into segments that fit stock material length, placing cuts
/// away from net crossings (a splice must not coincide with a joint). The cut
/// points are shared between consecutive segments, so the pieces reassemble
/// into the original lath.
/// </summary>
public static class LathSegmentation
{
    public readonly struct Result
    {
        /// <summary>Sub-polylines; consecutive segments share their cut point.</summary>
        public readonly List<Vec3d[]> Segments;

        /// <summary>Cut locations on the lath.</summary>
        public readonly Vec3d[] CutPoints;

        /// <summary>Arc-length position of each cut along the original lath.</summary>
        public readonly double[] CutArcLengths;

        public Result(List<Vec3d[]> segments, Vec3d[] cutPoints, double[] cutArcLengths)
        {
            Segments = segments;
            CutPoints = cutPoints;
            CutArcLengths = cutArcLengths;
        }
    }

    /// <summary>
    /// Segments one lath. stockLength is the available material length; a cut
    /// candidate that lands within margin of a joint is pulled back to
    /// margin before the joint (repeatedly, until it clears every joint).
    /// Joints are given as arc-length positions along the polyline (see
    /// <see cref="JointArcLengths"/>). With a positive overlap every segment
    /// is extended by overlap/2 past each cut so consecutive pieces lap over
    /// each other (the splice length); cuts are then spaced so each piece,
    /// including its overlaps, still fits the stock.
    /// </summary>
    public static Result Segment(
        Vec3d[] polyline, double stockLength, double margin,
        IReadOnlyList<double>? jointArcLengths = null, double overlap = 0.0)
    {
        if (stockLength <= 0)
            throw new ArgumentException("Stock length must be positive.", nameof(stockLength));
        margin = Math.Max(0.0, margin);
        overlap = Math.Max(0.0, overlap);
        if (overlap >= stockLength)
            throw new ArgumentException("Splice overlap must be shorter than the stock length.", nameof(overlap));

        var segments = new List<Vec3d[]>();
        var cutPoints = new List<Vec3d>();
        var cutArcs = new List<double>();

        int n = polyline.Length;
        if (n < 2)
        {
            if (n == 1) segments.Add(polyline);
            return new Result(segments, cutPoints.ToArray(), cutArcs.ToArray());
        }

        // Cumulative arc length
        var arc = new double[n];
        for (int i = 1; i < n; i++)
            arc[i] = arc[i - 1] + (polyline[i] - polyline[i - 1]).Length;
        double total = arc[n - 1];

        if (total <= stockLength)
        {
            segments.Add(polyline);
            return new Result(segments, cutPoints.ToArray(), cutArcs.ToArray());
        }

        var joints = new List<double>();
        if (jointArcLengths != null)
        {
            foreach (double j in jointArcLengths)
                if (j > 0 && j < total) joints.Add(j);
            joints.Sort();
        }

        // Cut positions
        double spacing = stockLength - overlap;
        double start = 0.0;
        while (total - start - 0.5 * overlap > spacing)
        {
            double end = start + spacing;
            double minEnd = start + 0.25 * spacing;

            // Keep the full stock length when it clears every joint by the
            // margin; otherwise pull the cut back to the longest piece that
            // does. When the joints are so dense that nothing in the useful
            // range clears the margin, take the longest piece whose clearance
            // is at least half the best clearance available, instead of an
            // arbitrary clash.
            if (margin > 0 && joints.Count > 0 && Clearance(joints, end) < margin)
            {
                var candidates = new List<double> { end, minEnd };
                for (int k = 0; k < joints.Count; k++)
                {
                    candidates.Add(joints[k] - margin);
                    candidates.Add(joints[k] + margin);
                    if (k + 1 < joints.Count) candidates.Add(0.5 * (joints[k] + joints[k + 1]));
                }
                candidates.RemoveAll(p => p < minEnd || p > start + spacing);
                double bestClear = candidates.Max(p => Clearance(joints, p));
                double threshold = bestClear >= margin ? margin : 0.5 * bestClear;
                end = candidates.Where(p => Clearance(joints, p) >= threshold - 1e-12).Max();
            }

            cutArcs.Add(end);
            cutPoints.Add(PointAtArc(polyline, arc, end, out _));
            start = end;
        }

        // Pieces between consecutive cuts, extended by the overlap
        for (int c = 0; c <= cutArcs.Count; c++)
        {
            double a = c == 0 ? 0.0 : cutArcs[c - 1] - 0.5 * overlap;
            double b = c == cutArcs.Count ? total : cutArcs[c] + 0.5 * overlap;
            a = Math.Max(0.0, a);
            b = Math.Min(total, b);
            if (b - a > 1e-12) segments.Add(SubPolyline(polyline, arc, a, b));
        }

        return new Result(segments, cutPoints.ToArray(), cutArcs.ToArray());
    }

    /// <summary>Distance from an arc-length position to the nearest joint (joints sorted ascending).</summary>
    private static double Clearance(List<double> joints, double pos)
    {
        int idx = joints.BinarySearch(pos);
        if (idx >= 0) return 0.0;
        idx = ~idx;
        double d = double.MaxValue;
        if (idx < joints.Count) d = Math.Min(d, joints[idx] - pos);
        if (idx > 0) d = Math.Min(d, pos - joints[idx - 1]);
        return d;
    }

    /// <summary>Polyline piece between arc-length positions a and b (inclusive ends).</summary>
    public static Vec3d[] SubPolyline(Vec3d[] polyline, double[] arc, double a, double b)
    {
        var seg = new List<Vec3d> { PointAtArc(polyline, arc, a, out _) };
        for (int i = 0; i < polyline.Length; i++)
            if (arc[i] > a + 1e-12 && arc[i] < b - 1e-12)
                seg.Add(polyline[i]);
        seg.Add(PointAtArc(polyline, arc, b, out _));
        return seg.ToArray();
    }

    /// <summary>
    /// Arc-length positions of joint points along a polyline (projection by
    /// closest point per segment). Feed the Points output of Net Joints here.
    /// </summary>
    public static double[] JointArcLengths(Vec3d[] polyline, IReadOnlyList<Vec3d> jointPoints, double maxDistance = 0.0)
    {
        int n = polyline.Length;
        var arc = new double[n];
        for (int i = 1; i < n; i++)
            arc[i] = arc[i - 1] + (polyline[i] - polyline[i - 1]).Length;

        var result = new List<double>(jointPoints.Count);
        double maxD2 = maxDistance > 0 ? maxDistance * maxDistance : double.MaxValue;
        for (int k = 0; k < jointPoints.Count; k++)
        {
            Vec3d p = jointPoints[k];
            double bestArc = 0, bestDist = double.MaxValue;
            for (int i = 0; i + 1 < n; i++)
            {
                Vec3d d = polyline[i + 1] - polyline[i];
                double len = d.Length;
                if (len < 1e-15) continue;
                double t = Vec3d.Dot(p - polyline[i], d) / (len * len);
                t = Math.Max(0.0, Math.Min(1.0, t));
                double dist = (polyline[i] + t * d - p).LengthSquared;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestArc = arc[i] + t * len;
                }
            }
            // Only joints that actually lie on this lath count; the joints of
            // the whole net are usually passed in, and a far-away crossing must
            // not pull cuts around on an unrelated lath
            if (bestDist <= maxD2) result.Add(bestArc);
        }
        return result.ToArray();
    }

    private static Vec3d PointAtArc(Vec3d[] polyline, double[] arc, double s, out int index)
    {
        int n = polyline.Length;
        for (int i = 1; i < n; i++)
        {
            if (arc[i] >= s)
            {
                double segLen = arc[i] - arc[i - 1];
                double t = segLen > 1e-15 ? (s - arc[i - 1]) / segLen : 0.0;
                index = i;
                return polyline[i - 1] + t * (polyline[i] - polyline[i - 1]);
            }
        }
        index = n - 1;
        return polyline[n - 1];
    }
}
