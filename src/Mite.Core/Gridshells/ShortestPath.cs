using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Approximate shortest geodesic between two points on a mesh: Dijkstra over
/// the vertex graph gives a topologically correct (if jagged) path, which is
/// then resampled and straightened by on-surface curve shortening — plain
/// Laplacian smoothing with reprojection is a discrete geodesic-curvature
/// flow, so with the endpoints held fixed the path converges to a geodesic.
/// </summary>
public static class ShortestPath
{
    public class Options
    {
        /// <summary>Resampling length for the straightening stage (0 = half the average edge length).</summary>
        public double SampleLength { get; set; } = 0.0;

        /// <summary>Maximum straightening passes.</summary>
        public int MaxIterations { get; set; } = 500;

        /// <summary>Stops when the largest point movement per pass falls below this fraction of the sample length.</summary>
        public double Tolerance { get; set; } = 1e-4;
    }

    public readonly struct Result
    {
        public readonly Vec3d[] Points;
        public readonly double Length;
        public readonly int Iterations;

        public Result(Vec3d[] points, double length, int iterations)
        {
            Points = points; Length = length; Iterations = iterations;
        }
    }

    /// <summary>Shortest path between two points (each projected onto the mesh first).</summary>
    public static Result? Compute(MeshProjection proj, Vec3d from, Vec3d to, Options? options = null)
    {
        options ??= new Options();
        var mesh = proj.Mesh;
        if (mesh.VertexCount == 0 || mesh.FaceCount == 0) return null;

        var hFrom = proj.ClosestPoint(from, proj.NearestVertexGlobal(from));
        var hTo = proj.ClosestPoint(to, proj.NearestVertexGlobal(to));
        if (hFrom.Face < 0 || hTo.Face < 0) return null;

        var vertexPath = Dijkstra(mesh, hFrom.NearestVertex, hTo.NearestVertex);
        if (vertexPath == null) return null; // different components

        var coarse = new List<Vec3d> { hFrom.Point };
        foreach (int v in vertexPath) coarse.Add(mesh.Vertices[v]);
        coarse.Add(hTo.Point);

        double sample = options.SampleLength > 0 ? options.SampleLength : 0.5 * proj.AverageEdgeLength;

        // Coarse-to-fine straightening: the Laplacian flow removes the
        // zigzag of the vertex path quickly but straightens globally only by
        // diffusion, so it is run first on a coarse resampling (few points,
        // fast convergence) and refined toward the final spacing
        var pts = coarse.ToArray();
        int iter = 0;
        double totalLen = Length(pts);
        int levels = Math.Max(1, (int)Math.Ceiling(Math.Log(Math.Max(totalLen / (8.0 * sample), 1.0), 2.0)) + 1);
        for (int level = levels - 1; level >= 0; level--)
        {
            double spacing = sample * Math.Pow(2.0, level);
            pts = Resample(new List<Vec3d>(pts), spacing);
            if (pts.Length < 3) continue;
            iter += Straighten(proj, pts, hFrom.NearestVertex, options.MaxIterations, options.Tolerance * spacing);
        }

        return new Result(pts, Length(pts), iter);
    }

    /// <summary>Laplacian shortening with reprojection, endpoints fixed. Returns the passes used.</summary>
    private static int Straighten(MeshProjection proj, Vec3d[] pts, int startHint, int maxIterations, double tol)
    {
        int n = pts.Length;
        var hints = new int[n];
        int hint = startHint;
        for (int i = 0; i < n; i++)
        {
            var h = proj.ClosestPoint(pts[i], hint);
            hints[i] = h.NearestVertex;
            hint = h.NearestVertex;
        }

        var next = new Vec3d[n];
        next[0] = pts[0];
        next[n - 1] = pts[n - 1];
        int iter;
        for (iter = 0; iter < maxIterations; iter++)
        {
            double maxMove = 0;
            for (int i = 1; i < n - 1; i++)
            {
                Vec3d target = 0.5 * (pts[i - 1] + pts[i + 1]);
                var h = proj.ClosestPoint(target, hints[i]);
                next[i] = h.Point;
                hints[i] = h.NearestVertex;
                maxMove = Math.Max(maxMove, (next[i] - pts[i]).Length);
            }
            Array.Copy(next, pts, n);
            if (maxMove < tol) { iter++; break; }
        }
        return iter;
    }

    private static double Length(Vec3d[] pts)
    {
        double len = 0;
        for (int i = 1; i < pts.Length; i++) len += (pts[i] - pts[i - 1]).Length;
        return len;
    }

    /// <summary>Uniform arc-length resampling keeping both endpoints.</summary>
    public static Vec3d[] Resample(IReadOnlyList<Vec3d> line, double spacing)
    {
        if (line.Count < 2) return new List<Vec3d>(line).ToArray();
        double total = 0;
        for (int i = 1; i < line.Count; i++) total += (line[i] - line[i - 1]).Length;
        int n = Math.Max(1, (int)Math.Round(total / Math.Max(spacing, 1e-12)));
        double step = total / n;

        var result = new Vec3d[n + 1];
        result[0] = line[0];
        int seg = 1;
        double segStart = 0;
        double segLen = (line[1] - line[0]).Length;
        for (int k = 1; k < n; k++)
        {
            double s = k * step;
            while (seg < line.Count - 1 && segStart + segLen < s)
            {
                segStart += segLen;
                seg++;
                segLen = (line[seg] - line[seg - 1]).Length;
            }
            double t = segLen > 1e-15 ? (s - segStart) / segLen : 0.0;
            result[k] = line[seg - 1] + t * (line[seg] - line[seg - 1]);
        }
        result[n] = line[line.Count - 1];
        return result;
    }

    /// <summary>Vertex indices from source to target inclusive, or null when disconnected.</summary>
    public static List<int>? Dijkstra(MeshData mesh, int source, int target)
    {
        int nv = mesh.VertexCount;
        var neighbors = mesh.BuildVertexNeighbors();
        var dist = new double[nv];
        var prev = new int[nv];
        for (int i = 0; i < nv; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
        dist[source] = 0;

        var heap = new SortedSet<(double d, int v)>();
        heap.Add((0, source));
        while (heap.Count > 0)
        {
            var top = heap.Min;
            heap.Remove(top);
            int u = top.v;
            if (top.d > dist[u]) continue;
            if (u == target) break;
            foreach (int w in neighbors[u])
            {
                double nd = dist[u] + (mesh.Vertices[w] - mesh.Vertices[u]).Length;
                if (nd < dist[w])
                {
                    if (dist[w] != double.MaxValue) heap.Remove((dist[w], w));
                    dist[w] = nd;
                    prev[w] = u;
                    heap.Add((nd, w));
                }
            }
        }
        if (dist[target] == double.MaxValue) return null;

        var path = new List<int>();
        for (int v = target; v != -1; v = prev[v]) path.Add(v);
        path.Reverse();
        return path;
    }
}
