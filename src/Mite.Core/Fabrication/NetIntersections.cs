using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// Finds the crossings between the two lath families of a gridshell net (or
/// within a single family). Crossings drive joint placement: each one carries
/// the curves involved, the location along them, and the exact 3D position.
/// Segment distances are exact (closest points between 3D segments); a
/// uniform grid keeps the search near-linear in the segment count.
/// </summary>
public static class NetIntersections
{
    public readonly struct Crossing
    {
        /// <summary>Index of the crossing curve in family A / segment along it / parameter in [0,1].</summary>
        public readonly int CurveA;
        public readonly int SegmentA;
        public readonly double ParamA;

        public readonly int CurveB;
        public readonly int SegmentB;
        public readonly double ParamB;

        /// <summary>Midpoint of the closest approach.</summary>
        public readonly Vec3d Point;

        /// <summary>3D gap between the polylines at the crossing (~0 for curves on the same surface).</summary>
        public readonly double Gap;

        /// <summary>
        /// Which family list CurveA / CurveB index into: 0 = family A, 1 = family B.
        /// Cross-family crossings are (0, 1); same-family crossings and
        /// T-junctions within one family are (0, 0) or (1, 1).
        /// </summary>
        public readonly int FamilyOfA, FamilyOfB;

        /// <summary>True when one curve ends on the other (a T-junction) rather than crossing it.</summary>
        public bool IsTJunction => EndA || EndB;
        internal readonly bool EndA, EndB;

        public Crossing(int curveA, int segmentA, double paramA,
            int curveB, int segmentB, double paramB, Vec3d point, double gap)
            : this(curveA, segmentA, paramA, curveB, segmentB, paramB, point, gap, 0, 1, false, false) { }

        public Crossing(int curveA, int segmentA, double paramA,
            int curveB, int segmentB, double paramB, Vec3d point, double gap,
            int familyOfA, int familyOfB, bool endA = false, bool endB = false)
        {
            CurveA = curveA; SegmentA = segmentA; ParamA = paramA;
            CurveB = curveB; SegmentB = segmentB; ParamB = paramB;
            Point = point; Gap = gap;
            FamilyOfA = familyOfA; FamilyOfB = familyOfB;
            EndA = endA; EndB = endB;
        }
    }

    /// <summary>
    /// All node-forming contacts of a net: crossings between the families plus
    /// crossings and T-junctions within each family (a lath that ends on a
    /// neighbouring lath of its own family, as continuous tracing produces
    /// where curves merge). Use this for topology and structural coupling;
    /// <see cref="Find"/> for the A×B joints alone.
    /// </summary>
    public static List<Crossing> FindAll(
        IReadOnlyList<Vec3d[]> familyA, IReadOnlyList<Vec3d[]> familyB, double tolerance = 0.0)
    {
        var all = Find(familyA, familyB, tolerance);
        foreach (var c in Find(familyA, null, tolerance)) all.Add(c);
        foreach (var c in Find(familyB, null, tolerance))
            all.Add(new Crossing(c.CurveA, c.SegmentA, c.ParamA, c.CurveB, c.SegmentB, c.ParamB, c.Point, c.Gap, 1, 1, c.EndA, c.EndB));
        return all;
    }

    /// <summary>
    /// Finds crossings between family A and family B. When familyB is null,
    /// crossings within family A are reported instead (each unordered pair once).
    /// Tolerance is the maximum 3D gap accepted as a crossing; 0 selects an
    /// automatic value (5% of the average segment length — true crossings of
    /// on-surface curves have a gap far below that).
    /// </summary>
    public static List<Crossing> Find(
        IReadOnlyList<Vec3d[]> familyA, IReadOnlyList<Vec3d[]>? familyB = null, double tolerance = 0.0)
    {
        bool selfMode = familyB == null;
        var segsB = CollectSegments(selfMode ? familyA : familyB!);
        var segsA = CollectSegments(familyA);
        var lastA = LastSegments(familyA);
        var lastB = selfMode ? lastA : LastSegments(familyB!);

        double avgSeg = 0;
        int segCount = 0;
        foreach (var s in segsA) { avgSeg += (s.B - s.A).Length; segCount++; }
        foreach (var s in segsB) { avgSeg += (s.B - s.A).Length; segCount++; }
        avgSeg = segCount > 0 ? avgSeg / segCount : 1.0;

        double tol = tolerance > 0 ? tolerance : 0.05 * avgSeg;
        double cellSize = Math.Max(avgSeg, tol) + tol;

        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int i = 0; i < segsB.Count; i++)
        {
            var s = segsB[i];
            InsertSegment(grid, s, cellSize, tol, i);
        }

        var crossings = new List<Crossing>();
        for (int i = 0; i < segsA.Count; i++)
        {
            var sa = segsA[i];
            foreach (int j in Candidates(grid, sa, cellSize, tol))
            {
                var sb = segsB[j];
                if (selfMode)
                {
                    // Each unordered pair once; skip trivial same-curve neighbors
                    if (sb.Curve < sa.Curve) continue;
                    if (sb.Curve == sa.Curve)
                    {
                        if (sb.Segment <= sa.Segment + 1) continue;
                        // Closed loops: first and last segments share the seam vertex
                        if ((sa.A - sb.B).LengthSquared < 1e-24 || (sa.B - sb.A).LengthSquared < 1e-24) continue;
                    }
                }

                double gap = SegmentQueries.SegmentSegment(sa.A, sa.B, sb.A, sb.B,
                    out double s, out double t, out Vec3d c1, out Vec3d c2);
                if (gap > tol) continue;

                // Params at the far end (≈1) are reported by the next segment
                // as ≈0 — skipping them here keeps exactly one report per
                // crossing. The last segment of a curve has no next segment: a
                // contact at its far end is the curve's end touching the other
                // curve (a T-junction) and must be kept.
                bool aEnd = s > 1.0 - 1e-9, bEnd = t > 1.0 - 1e-9;
                if (aEnd && sa.Segment != lastA[sa.Curve]) continue;
                if (bEnd && sb.Segment != lastB[sb.Curve]) continue;
                bool aStart = s < 1e-9 && sa.Segment == 0, bStart = t < 1e-9 && sb.Segment == 0;

                crossings.Add(new Crossing(
                    sa.Curve, sa.Segment, s, sb.Curve, sb.Segment, t,
                    0.5 * (c1 + c2), gap, 0, selfMode ? 0 : 1, aEnd || aStart, bEnd || bStart));
            }
        }

        // A crossing that falls within tol of a polyline vertex is found by
        // both segments sharing that vertex (one at s≈1, one at s≈0 after
        // clamping). Merge crossings of the same curve pair closer than 2·tol,
        // keeping the tighter one, so each node yields exactly one joint.
        var byPair = new Dictionary<(int, int), List<int>>();
        var keep = new List<Crossing>(crossings.Count);
        double merge2 = 4.0 * tol * tol;
        foreach (var c in crossings)
        {
            var key = (c.CurveA, c.CurveB);
            if (!byPair.TryGetValue(key, out var list)) { list = new List<int>(); byPair[key] = list; }
            int dup = -1;
            foreach (int k in list)
                if ((keep[k].Point - c.Point).LengthSquared <= merge2) { dup = k; break; }
            if (dup < 0) { list.Add(keep.Count); keep.Add(c); }
            else if (c.Gap < keep[dup].Gap) keep[dup] = c;
        }
        return keep;
    }

    /// <summary>Tangent of a polyline at a crossing (its segment direction).</summary>
    public static Vec3d TangentAt(IReadOnlyList<Vec3d[]> family, Crossing c, bool familyB)
    {
        var line = family[familyB ? c.CurveB : c.CurveA];
        int seg = familyB ? c.SegmentB : c.SegmentA;
        return (line[seg + 1] - line[seg]).Normalized();
    }

    private readonly struct Seg
    {
        public readonly Vec3d A, B;
        public readonly int Curve, Segment;
        public Seg(Vec3d a, Vec3d b, int curve, int segment) { A = a; B = b; Curve = curve; Segment = segment; }
    }

    private static int[] LastSegments(IReadOnlyList<Vec3d[]> family)
    {
        var last = new int[family.Count];
        for (int c = 0; c < family.Count; c++)
        {
            var line = family[c];
            int l = -1;
            for (int i = 0; i + 1 < line.Length; i++)
                if ((line[i + 1] - line[i]).LengthSquared > 1e-24) l = i;
            last[c] = l;
        }
        return last;
    }

    private static List<Seg> CollectSegments(IReadOnlyList<Vec3d[]> family)
    {
        var segs = new List<Seg>();
        for (int c = 0; c < family.Count; c++)
        {
            var line = family[c];
            for (int i = 0; i + 1 < line.Length; i++)
                if ((line[i + 1] - line[i]).LengthSquared > 1e-24)
                    segs.Add(new Seg(line[i], line[i + 1], c, i));
        }
        return segs;
    }

    private static (int, int, int) Key(Vec3d p, double cell) =>
        ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));

    private static void InsertSegment(
        Dictionary<(int, int, int), List<int>> grid, Seg s, double cell, double pad, int index)
    {
        Vec3d lo = new(
            Math.Min(s.A.X, s.B.X) - pad, Math.Min(s.A.Y, s.B.Y) - pad, Math.Min(s.A.Z, s.B.Z) - pad);
        Vec3d hi = new(
            Math.Max(s.A.X, s.B.X) + pad, Math.Max(s.A.Y, s.B.Y) + pad, Math.Max(s.A.Z, s.B.Z) + pad);
        var k0 = Key(lo, cell);
        var k1 = Key(hi, cell);
        for (int x = k0.Item1; x <= k1.Item1; x++)
            for (int y = k0.Item2; y <= k1.Item2; y++)
                for (int z = k0.Item3; z <= k1.Item3; z++)
                {
                    var key = (x, y, z);
                    if (!grid.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        grid[key] = list;
                    }
                    list.Add(index);
                }
    }

    private static IEnumerable<int> Candidates(
        Dictionary<(int, int, int), List<int>> grid, Seg s, double cell, double pad)
    {
        Vec3d lo = new(
            Math.Min(s.A.X, s.B.X) - pad, Math.Min(s.A.Y, s.B.Y) - pad, Math.Min(s.A.Z, s.B.Z) - pad);
        Vec3d hi = new(
            Math.Max(s.A.X, s.B.X) + pad, Math.Max(s.A.Y, s.B.Y) + pad, Math.Max(s.A.Z, s.B.Z) + pad);
        var k0 = Key(lo, cell);
        var k1 = Key(hi, cell);
        var seen = new HashSet<int>();
        for (int x = k0.Item1; x <= k1.Item1; x++)
            for (int y = k0.Item2; y <= k1.Item2; y++)
                for (int z = k0.Item3; z <= k1.Item3; z++)
                {
                    if (!grid.TryGetValue((x, y, z), out var list)) continue;
                    foreach (int idx in list)
                        if (seen.Add(idx))
                            yield return idx;
                }
    }
}
