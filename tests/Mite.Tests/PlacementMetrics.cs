using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Tests;

/// <summary>
/// Placement quality of one curve family, measured independently of the
/// algorithm that made it (used by the farthest-point seeding tests and the
/// bench): curve count and lengths, free ends (ends neither on the mesh
/// border nor at the edge of the usable region — T-junctions or stops), the
/// gap to the neighbouring curve on either side measured across the curve in
/// the tangent plane (every Spacing / 4, skipping half a Spacing at each end),
/// and the largest void (distance from a usable vertex to the nearest curve
/// point). Lengths are in units of Spacing.
/// </summary>
public static class PlacementMetrics
{
    public sealed class Result
    {
        public int Curves;
        public double TotalLength, MeanLength;
        public int Ends, FreeEnds;
        public double GapP5, GapMedian, GapP95;
        /// <summary>Share of gap samples within [0.8, 1.25] Spacing.</summary>
        public double GapNear;
        public int GapSamples;
        /// <summary>Largest distance from a usable vertex to the nearest curve point.</summary>
        public double LargestVoid;
        /// <summary>The same, counting only vertices farther than half a Spacing from the border of the usable region.</summary>
        public double LargestInteriorVoid;

        public override string ToString() =>
            $"{Curves} curves, length {TotalLength:F1} S (mean {MeanLength:F2}), free ends {FreeEnds}/{Ends}, " +
            $"gap p5/50/95 {GapP5:F2}/{GapMedian:F2}/{GapP95:F2} S ({GapNear:P0} within [0.8, 1.25]), " +
            $"largest void {LargestVoid:F2} S (interior {LargestInteriorVoid:F2} S)";
    }

    public static Result Measure(MeshData mesh, bool[]? mask, IList<Vec3d[]> curves, double spacing)
    {
        var proj = new MeshProjection(mesh);
        var nb = mesh.BuildVertexNeighbors();
        double s = spacing;
        var grid = new Dictionary<(int, int, int), List<(Vec3d p, int c)>>();
        (int, int, int) K(Vec3d p) => ((int)Math.Floor(p.X / s), (int)Math.Floor(p.Y / s), (int)Math.Floor(p.Z / s));
        for (int ci = 0; ci < curves.Count; ci++)
            foreach (var p in curves[ci])
            {
                var k = K(p);
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(Vec3d, int)>();
                l.Add((p, ci));
            }
        IEnumerable<(Vec3d p, int c)> Near(Vec3d p, int r)
        {
            var (x, y, z) = K(p);
            for (int a = -r; a <= r; a++) for (int b = -r; b <= r; b++) for (int c = -r; c <= r; c++)
                if (grid.TryGetValue((x + a, y + b, z + c), out var l)) foreach (var e in l) yield return e;
        }

        // border of the usable region and the distance to it
        var onBorder = mesh.BuildBoundaryVertexFlags();
        var regionBorder = new bool[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mask != null && !mask[i]) continue;
            regionBorder[i] = onBorder[i];
            if (mask != null) foreach (int w in nb[i]) if (!mask[w]) regionBorder[i] = true;
        }
        var borderPts = Enumerable.Range(0, mesh.VertexCount).Where(i => regionBorder[i]).Select(i => mesh.Vertices[i]).ToArray();

        var r = new Result();
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mask != null && !mask[i]) continue;
            var v = mesh.Vertices[i];
            double best = 4.0;
            foreach (var e in Near(v, 4)) best = Math.Min(best, (e.p - v).Length / s);
            r.LargestVoid = Math.Max(r.LargestVoid, best);
            double db = borderPts.Length == 0 ? double.MaxValue : borderPts.Min(q => (q - v).Length) / s;
            if (db > 0.5) r.LargestInteriorVoid = Math.Max(r.LargestInteriorVoid, best);
        }

        var gaps = new List<double>();
        var lens = new List<double>();
        for (int ci = 0; ci < curves.Count; ci++)
        {
            var c = curves[ci];
            double len = 0;
            for (int i = 1; i < c.Length; i++) len += (c[i] - c[i - 1]).Length;
            lens.Add(len / s);
            double acc = 0, next = 0.5 * s;
            int h = proj.NearestVertexGlobal(c[0]);
            for (int i = 1; i + 1 < c.Length; i++)
            {
                acc += (c[i] - c[i - 1]).Length;
                if (acc < next || acc > len - 0.5 * s) continue;
                next += 0.25 * s;
                var hit = proj.ClosestPoint(c[i], h); h = hit.NearestVertex;
                Vec3d t = c[i + 1] - c[i - 1];
                if (t.LengthSquared < 1e-24) continue;
                t = t.Normalized();
                Vec3d u = Vec3d.Cross(hit.SmoothNormal, t);
                if (u.LengthSquared < 1e-24) continue;
                u = u.Normalized();
                double left = 9, right = 9;
                foreach (var e in Near(c[i], 3))
                {
                    if (e.c == ci) continue;
                    var w = e.p - c[i];
                    double across = Vec3d.Dot(w, u);
                    if (Math.Abs(Vec3d.Dot(w, t)) > 0.3 * Math.Abs(across)) continue;
                    double d = w.Length / s;
                    if (across > 0) left = Math.Min(left, d); else right = Math.Min(right, d);
                }
                if (left < 3) gaps.Add(left);
                if (right < 3) gaps.Add(right);
            }

            if ((c[0] - c[^1]).LengthSquared < 1e-18) continue;
            foreach (var e in new[] { c[0], c[^1] })
            {
                r.Ends++;
                var hh = proj.ClosestPoint(e, proj.NearestVertexGlobal(e));
                if (proj.IsOnBoundary(hh, 1e-3)) continue;
                if (mask != null)
                {
                    int v0 = hh.NearestVertex;
                    bool edge = !mask[v0];
                    foreach (int w in nb[v0]) if (!mask[w]) edge = true;
                    if (edge) continue;
                }
                r.FreeEnds++;
            }
        }
        gaps.Sort();
        double Q(double q) => gaps.Count == 0 ? double.NaN : gaps[Math.Min(gaps.Count - 1, (int)Math.Round(q * (gaps.Count - 1)))];
        r.Curves = curves.Count;
        r.TotalLength = lens.Sum();
        r.MeanLength = lens.Count > 0 ? lens.Average() : 0;
        r.GapSamples = gaps.Count;
        r.GapP5 = Q(0.05); r.GapMedian = Q(0.5); r.GapP95 = Q(0.95);
        r.GapNear = gaps.Count == 0 ? 0 : gaps.Count(g => g >= 0.8 && g <= 1.25) / (double)gaps.Count;
        return r;
    }
}
