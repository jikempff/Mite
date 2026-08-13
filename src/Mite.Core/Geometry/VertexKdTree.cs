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

    // Lomuto quickselect: order[k] ends as the k-th smallest along the axis,
    // with smaller entries to its left
    private static void Select(Vec3d[] pts, int[] order, int lo, int hi, int k, int axis)
    {
        while (hi - lo > 1)
        {
            int pivotIndex = (lo + hi - 1) / 2;
            double pivot = Axis(pts[order[pivotIndex]], axis);
            (order[pivotIndex], order[hi - 1]) = (order[hi - 1], order[pivotIndex]);

            int store = lo;
            for (int i = lo; i < hi - 1; i++)
            {
                if (Axis(pts[order[i]], axis) < pivot)
                {
                    (order[i], order[store]) = (order[store], order[i]);
                    store++;
                }
            }
            (order[hi - 1], order[store]) = (order[store], order[hi - 1]);

            if (store == k) return;
            if (store < k) lo = store + 1;
            else hi = store;
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
