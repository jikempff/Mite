using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Analysis;

/// <summary>
/// Isocurves of a per-vertex scalar field (marching triangles). Level sets of
/// Gaussian curvature at K = 0 delimit the anticlastic regions where
/// asymptotic laths can exist; isolines of utilization or height are useful
/// for reading an analysis result directly on the model.
/// </summary>
public static class MeshIsocurves
{
    /// <summary>
    /// Extracts the isocurves of the field at each level as polylines (closed
    /// polylines repeat their first point at the end). Segments are joined
    /// through shared mesh edges, so each connected level-set component comes
    /// out as one polyline.
    /// </summary>
    public static List<List<Vec3d[]>> Compute(MeshData mesh, double[] values, IReadOnlyList<double> levels)
    {
        if (values.Length != mesh.VertexCount)
            throw new ArgumentException("One value per vertex is required.", nameof(values));

        var tri = mesh.ToTriangulated();
        var result = new List<List<Vec3d[]>>(levels.Count);
        foreach (double level in levels)
            result.Add(ExtractLevel(tri, values, level));
        return result;
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly int A, B;
        public EdgeKey(int a, int b) { if (a < b) { A = a; B = b; } else { A = b; B = a; } }
        public bool Equals(EdgeKey o) => A == o.A && B == o.B;
        public override bool Equals(object? obj) => obj is EdgeKey o && Equals(o);
        public override int GetHashCode() => unchecked(A * 73856093 ^ B * 19349663);
    }

    private static List<Vec3d[]> ExtractLevel(MeshData tri, double[] values, double level)
    {
        // Crossing point per edge (computed once so adjacent triangles share it)
        var crossing = new Dictionary<EdgeKey, Vec3d>();
        // Segments as pairs of edge keys; adjacency: edge -> segments using it
        var segments = new List<(EdgeKey e0, EdgeKey e1)>();
        var edgeSegments = new Dictionary<EdgeKey, List<int>>();

        foreach (var f in tri.Faces)
        {
            if (f.Length != 3) continue;
            var keys = new List<EdgeKey>(3);
            for (int j = 0; j < 3; j++)
            {
                int a = f[j], b = f[(j + 1) % 3];
                double va = values[a] - level, vb = values[b] - level;
                // A vertex exactly on the level counts as positive so it is
                // crossed by exactly one of its edges (Simulation of Simplicity)
                bool pa = va >= 0, pb = vb >= 0;
                if (pa == pb) continue;
                var key = new EdgeKey(a, b);
                if (!crossing.ContainsKey(key))
                {
                    double t = va / (va - vb);
                    t = Math.Max(0.0, Math.Min(1.0, t));
                    crossing[key] = tri.Vertices[a] + t * (tri.Vertices[b] - tri.Vertices[a]);
                }
                keys.Add(key);
            }
            if (keys.Count != 2) continue;
            int si = segments.Count;
            segments.Add((keys[0], keys[1]));
            foreach (var k in keys)
            {
                if (!edgeSegments.TryGetValue(k, out var list)) { list = new List<int>(2); edgeSegments[k] = list; }
                list.Add(si);
            }
        }

        // Chain segments into polylines
        var used = new bool[segments.Count];
        var polylines = new List<Vec3d[]>();

        // Open chains first (start at edges with a single segment), then loops
        for (int pass = 0; pass < 2; pass++)
        {
            for (int s = 0; s < segments.Count; s++)
            {
                if (used[s]) continue;
                bool isEnd = edgeSegments[segments[s].e0].Count == 1 || edgeSegments[segments[s].e1].Count == 1;
                if (pass == 0 && !isEnd) continue;

                // Choose the start edge: the one with no other segment
                EdgeKey startEdge = edgeSegments[segments[s].e0].Count == 1 ? segments[s].e0
                                  : edgeSegments[segments[s].e1].Count == 1 ? segments[s].e1 : segments[s].e0;

                var chain = new List<Vec3d> { crossing[startEdge] };
                int cur = s;
                EdgeKey curEdge = startEdge;
                while (true)
                {
                    used[cur] = true;
                    EdgeKey next = segments[cur].e0.Equals(curEdge) ? segments[cur].e1 : segments[cur].e0;
                    chain.Add(crossing[next]);
                    curEdge = next;

                    int nextSeg = -1;
                    foreach (int cand in edgeSegments[curEdge])
                        if (!used[cand]) { nextSeg = cand; break; }
                    if (nextSeg < 0) break;
                    cur = nextSeg;
                }
                if (chain.Count >= 2) polylines.Add(chain.ToArray());
            }
        }
        return polylines;
    }
}
