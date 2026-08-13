using System;
using System.Collections.Generic;
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
    /// margin before the joint. Joints are given as arc-length positions along
    /// the polyline (see <see cref="JointArcLengths"/>).
    /// </summary>
    public static Result Segment(
        Vec3d[] polyline, double stockLength, double margin,
        IReadOnlyList<double>? jointArcLengths = null)
    {
        if (stockLength <= 0)
            throw new ArgumentException("Stock length must be positive.", nameof(stockLength));
        margin = Math.Max(0.0, margin);

        // Cumulative arc length
        int n = polyline.Length;
        var arc = new double[n];
        for (int i = 1; i < n; i++)
            arc[i] = arc[i - 1] + (polyline[i] - polyline[i - 1]).Length;
        double total = arc[n - 1];

        var segments = new List<Vec3d[]>();
        var cutPoints = new List<Vec3d>();
        var cutArcs = new List<double>();

        if (n < 2 || total <= stockLength)
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

        double start = 0.0;
        int startIndex = 0; // polyline index at or after 'start'
        while (total - start > stockLength)
        {
            double end = start + stockLength;

            // Pull the cut back when it lands on or just past a joint
            foreach (double j in joints)
            {
                if (j >= end - margin && j <= end + margin)
                {
                    double candidate = j - margin;
                    // Keep a minimum useful segment; otherwise accept the clash
                    if (candidate - start > 0.25 * stockLength)
                        end = candidate;
                    break;
                }
                if (j > end + margin) break;
            }

            Vec3d cutPoint = PointAtArc(polyline, arc, end, out int endIndex);
            cutPoints.Add(cutPoint);
            cutArcs.Add(end);

            // Build the segment [start, end]
            var seg = new List<Vec3d>();
            if ((polyline[startIndex] - PointAtArc(polyline, arc, start, out _)).LengthSquared > 1e-24)
                seg.Add(PointAtArc(polyline, arc, start, out _));
            for (int i = startIndex; i <= endIndex && i < n; i++)
                if (arc[i] > start && arc[i] < end)
                    seg.Add(polyline[i]);
            seg.Add(cutPoint);
            segments.Add(seg.ToArray());

            start = end;
            startIndex = endIndex;
        }

        // Final segment [start, total]
        var last = new List<Vec3d>();
        for (int i = 0; i < n; i++)
            if (arc[i] > start) last.Add(polyline[i]);
        last.Insert(0, PointAtArc(polyline, arc, start, out _));
        if (last.Count >= 2) segments.Add(last.ToArray());

        return new Result(segments, cutPoints.ToArray(), cutArcs.ToArray());
    }

    /// <summary>
    /// Arc-length positions of joint points along a polyline (projection by
    /// closest point per segment). Feed the Points output of Net Joints here.
    /// </summary>
    public static double[] JointArcLengths(Vec3d[] polyline, IReadOnlyList<Vec3d> jointPoints)
    {
        int n = polyline.Length;
        var arc = new double[n];
        for (int i = 1; i < n; i++)
            arc[i] = arc[i - 1] + (polyline[i] - polyline[i - 1]).Length;

        var result = new double[jointPoints.Count];
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
            result[k] = bestArc;
        }
        return result;
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
