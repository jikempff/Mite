using System;

namespace Mite.Core.Geometry;

/// <summary>
/// Exact nearest-neighbor search over mesh vertices: a balanced kd-tree with
/// branch pruning. Returns the same vertex as a linear scan, in O(log n)
/// typical time instead of O(n) — matters when seeding traces on large meshes.
/// </summary>
internal sealed class VertexKdTree
{
    private readonly Vec3d[] _points;
    private readonly int[] _order;
    private readonly int _root;

    // Node layout in _order: implicit binary tree over a median-sorted index array
    private VertexKdTree(Vec3d[] points, int[] order, int root)
    {
        _points = points;
        _order = order;
        _root = root;
    }

    public static VertexKdTree Build(Vec3d[] points)
    {
        var order = new int[points.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        if (order.Length == 0) return new VertexKdTree(points, order, -1);
        int root = BuildRecursive(points, order, 0, order.Length, 0);
        return new VertexKdTree(points, order, root);
    }

    // Median-split build into an implicit balanced tree over the order array:
    // after partitioning, every node sits at the mid of its subarray, which the
    // query traversal recomputes the same way
    private static int BuildRecursive(Vec3d[] pts, int[] order, int lo, int hi, int depth)
    {
        if (lo >= hi) return -1;
        int axis = depth % 3;
        int mid = (lo + hi) / 2;
        Select(pts, order, lo, hi, mid, axis);
        BuildRecursive(pts, order, lo, mid, depth + 1);
        BuildRecursive(pts, order, mid + 1, hi, depth + 1);
        return mid;
    }

    // Three-way (Dutch-flag) quickselect: order[k] ends as the k-th smallest
    // along the axis, with smaller entries to its left. The equal-band keeps
    // the selection linear on meshes with many repeated coordinates (planar
    // grids, extrusions), where a two-way partition degrades to quadratic.
    private static void Select(Vec3d[] pts, int[] order, int lo, int hi, int k, int axis)
    {
        var rng = new Random(12345);
        while (hi - lo > 1)
        {
            int pivotIndex = lo + rng.Next(hi - lo);
            double pivot = Axis(pts[order[pivotIndex]], axis);

            int lt = lo, i = lo, gt = hi - 1;
            while (i <= gt)
            {
                double v = Axis(pts[order[i]], axis);
                if (v < pivot)
                {
                    (order[i], order[lt]) = (order[lt], order[i]);
                    lt++; i++;
                }
                else if (v > pivot)
                {
                    (order[i], order[gt]) = (order[gt], order[i]);
                    gt--;
                }
                else i++;
            }

            // order[lt..gt] all equal the pivot
            if (k < lt) hi = lt;
            else if (k > gt) lo = gt + 1;
            else return;
        }
    }

    private static double Axis(Vec3d p, int axis) =>
        axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

    /// <summary>Index of the vertex nearest to p (exact).</summary>
    public int Nearest(Vec3d p)
    {
        if (_root < 0) return -1;
        int best = -1;
        double bestDist = double.MaxValue;
        NearestRecursive(p, 0, _order.Length, 0, ref best, ref bestDist);
        return best;
    }

    private void NearestRecursive(Vec3d p, int lo, int hi, int depth, ref int best, ref double bestDist)
    {
        if (lo >= hi) return;
        int mid = (lo + hi) / 2;
        int idx = _order[mid];
        double d = (_points[idx] - p).LengthSquared;
        if (d < bestDist) { bestDist = d; best = idx; }

        int axis = depth % 3;
        double diff = Axis(p, axis) - Axis(_points[idx], axis);
        int nearLo = lo, nearHi = mid, farLo = mid + 1, farHi = hi;
        if (diff > 0)
        {
            nearLo = mid + 1; nearHi = hi; farLo = lo; farHi = mid;
        }

        NearestRecursive(p, nearLo, nearHi, depth + 1, ref best, ref bestDist);
        if (diff * diff < bestDist)
            NearestRecursive(p, farLo, farHi, depth + 1, ref best, ref bestDist);
    }
}
