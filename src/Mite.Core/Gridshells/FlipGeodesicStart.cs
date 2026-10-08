// Start paths for FlipOut.
//
// FlipOut (Sharp & Crane 2020) returns the shortest path in the class of its start path: which side
// of each vertex it passes. A Dijkstra path along mesh edges can pick the wrong side of a cone point
// or of a saddle vertex when the two candidates are within the edge graph's distortion (≈ 8 % on a
// regular triangulation); the paper reports > 95 % minimal paths from Dijkstra starts. The start
// path here is instead the shortest path in a Steiner graph:
//   Lanthier, Maheshwari & Sack, "Approximating weighted shortest paths on polyhedral surfaces",
//     Carleton TR-96-32 (1996), Algorithmica 30(4) 527–562 (2001) — the fixed scheme: m Steiner
//     points evenly on every edge, a graph edge between any two points on the boundary of the same
//     face (straight inside the face, so every graph path lies on the surface), extra length per
//     crossed face ≤ |l_e|/(m + 1); "only a small number of Steiner points (6) per edge suffice to
//     obtain close-to-optimal approximations" on terrains.
// The Steiner path is made an edge path by inserting its points as mesh vertices in path order (an
// edge split joins the new vertex to the opposite corners, so consecutive points of one face end up
// joined by an edge); A* with the straight-line distance (a lower bound of the surface distance)
// keeps the search inside an ellipse round the two endpoints.
using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

public static partial class FlipGeodesic
{
    /// <summary>
    /// Triangle mesh refined by point insertion (edge and face splits) without changing the surface.
    /// Tracks, for every edge of the base mesh, the points inserted on it, and for every base face
    /// its current sub-faces.
    /// </summary>
    internal sealed class Refiner
    {
        public readonly List<Vec3d> V;
        public readonly List<int[]> F;
        private readonly List<int> _faceBase = new();
        private readonly Dictionary<int, List<int>> _subFaces = new();
        private readonly Dictionary<long, int> _edgeFace = new(new LongHash());
        private readonly Dictionary<long, List<(double t, int v)>> _onEdge = new(new LongHash());
        private readonly int[][] _baseFaces;

        /// <summary>Some directed edge belongs to two faces: a non-manifold edge or inconsistent winding.</summary>
        public bool EdgeUsedTwice { get; }

        public Refiner(MeshData tri)
        {
            V = new List<Vec3d>(tri.Vertices);
            F = new List<int[]>(tri.FaceCount);
            _baseFaces = tri.Faces;
            for (int f = 0; f < tri.FaceCount; f++)
            {
                var t = (int[])tri.Faces[f].Clone();
                F.Add(t); _faceBase.Add(f);
                _subFaces[f] = new List<int> { f };
                for (int c = 0; c < 3; c++)
                {
                    long k = Key(t[c], t[(c + 1) % 3]);
                    if (_edgeFace.ContainsKey(k)) EdgeUsedTwice = true;
                    _edgeFace[k] = f;
                }
            }
        }

        private static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        private void SetFace(int f, int a, int b, int c)
        {
            F[f] = new[] { a, b, c };
            _edgeFace[Key(a, b)] = f; _edgeFace[Key(b, c)] = f; _edgeFace[Key(c, a)] = f;
        }

        private int AddFace(int baseFace, int a, int b, int c)
        {
            int f = F.Count;
            F.Add(new[] { a, b, c }); _faceBase.Add(baseFace);
            _subFaces[baseFace].Add(f);
            _edgeFace[Key(a, b)] = f; _edgeFace[Key(b, c)] = f; _edgeFace[Key(c, a)] = f;
            return f;
        }

        /// <summary>Splits edge u–w (both of its faces) at p; returns the new vertex, or −1 when u–w is not an edge.</summary>
        public int SplitEdge(int u, int w, Vec3d p)
        {
            bool has1 = _edgeFace.TryGetValue(Key(u, w), out int f1);
            bool has2 = _edgeFace.TryGetValue(Key(w, u), out int f2);
            if (!has1 && !has2) return -1;
            int m = V.Count;
            V.Add(p);
            if (has1)
            {
                int o = Opposite(F[f1], u, w);
                _edgeFace.Remove(Key(u, w));
                SetFace(f1, u, m, o);
                AddFace(_faceBase[f1], m, w, o);
            }
            if (has2)
            {
                int x = Opposite(F[f2], w, u);
                _edgeFace.Remove(Key(w, u));
                SetFace(f2, w, m, x);
                AddFace(_faceBase[f2], m, u, x);
            }
            return m;
        }

        private static int Opposite(int[] t, int a, int b)
        {
            for (int c = 0; c < 3; c++) if (t[c] != a && t[c] != b) return t[c];
            return -1;
        }

        /// <summary>Inserts a point on base edge (a, b) at parameter t from a (0..1); reuses a point already there.</summary>
        public int OnBaseEdge(int a, int b, double t, Vec3d p)
        {
            if (a > b) { (a, b) = (b, a); t = 1 - t; }
            if (t <= 1e-12) return a;
            if (t >= 1 - 1e-12) return b;
            long k = Key(a, b);
            if (!_onEdge.TryGetValue(k, out var list)) { list = new List<(double, int)>(); _onEdge[k] = list; }
            int lo = a, hi = b; double tlo = 0, thi = 1; int at = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (Math.Abs(list[i].t - t) <= 1e-12) return list[i].v;
                if (list[i].t < t) { lo = list[i].v; tlo = list[i].t; at = i + 1; }
                else { hi = list[i].v; thi = list[i].t; break; }
            }
            int m = SplitEdge(lo, hi, p);
            if (m < 0) return -1;
            list.Insert(at, (t, m));
            return m;
        }

        /// <summary>Inserts a point lying inside base face fb (barycentric coordinates on the base face).</summary>
        public int InBaseFace(int fb, Vec3d p, Vec3d bary, double snapBary)
        {
            var bt = BaseFaceVertices(fb);
            // near a corner or an edge of the base face
            if (bary.X >= 1 - snapBary) return bt[0];
            if (bary.Y >= 1 - snapBary) return bt[1];
            if (bary.Z >= 1 - snapBary) return bt[2];
            if (bary.X <= snapBary) return OnBaseEdge(bt[1], bt[2], bary.Z / Math.Max(bary.Y + bary.Z, 1e-300), p);
            if (bary.Y <= snapBary) return OnBaseEdge(bt[2], bt[0], bary.X / Math.Max(bary.X + bary.Z, 1e-300), p);
            if (bary.Z <= snapBary) return OnBaseEdge(bt[0], bt[1], bary.Y / Math.Max(bary.X + bary.Y, 1e-300), p);
            // the current sub-face that contains p
            int best = -1; double bestMin = double.NegativeInfinity; Vec3d bestB = default;
            foreach (int f in _subFaces[fb])
            {
                var t = F[f];
                var b = Bary(p, V[t[0]], V[t[1]], V[t[2]]);
                double mn = Math.Min(b.X, Math.Min(b.Y, b.Z));
                if (mn > bestMin) { bestMin = mn; best = f; bestB = b; }
            }
            var tri = F[best];
            for (int c = 0; c < 3; c++) if (bestB[c] >= 1 - snapBary) return tri[c];
            for (int c = 0; c < 3; c++)
                if (bestB[c] <= snapBary)
                {
                    int u = tri[(c + 1) % 3], w = tri[(c + 2) % 3];
                    return SplitEdge(u, w, p);
                }
            int m = V.Count;
            V.Add(p);
            int a0 = tri[0], a1 = tri[1], a2 = tri[2];
            SetFace(best, a0, a1, m);
            AddFace(fb, a1, a2, m);
            AddFace(fb, a2, a0, m);
            return m;
        }

        private int[] BaseFaceVertices(int fb) => _baseFaces[fb];

        public bool HasEdge(int a, int b) => _edgeFace.ContainsKey(Key(a, b)) || _edgeFace.ContainsKey(Key(b, a));

        private static Vec3d Bary(Vec3d p, Vec3d a, Vec3d b, Vec3d c)
        {
            Vec3d v0 = b - a, v1 = c - a, v2 = p - a;
            double d00 = Vec3d.Dot(v0, v0), d01 = Vec3d.Dot(v0, v1), d11 = Vec3d.Dot(v1, v1);
            double d20 = Vec3d.Dot(v2, v0), d21 = Vec3d.Dot(v2, v1);
            double den = d00 * d11 - d01 * d01;
            if (Math.Abs(den) < 1e-300) return new Vec3d(1, 0, 0);
            double bv = (d11 * d20 - d01 * d21) / den, bw = (d00 * d21 - d01 * d20) / den;
            return new Vec3d(1 - bv - bw, bv, bw);
        }
    }

    internal sealed class LongHash : IEqualityComparer<long>
    {
        public bool Equals(long x, long y) => x == y;
        public int GetHashCode(long x)
        {
            ulong z = unchecked((ulong)x * 0x9E3779B97F4A7C15UL);
            z ^= z >> 29;
            return unchecked((int)(z ^ (z >> 32)));
        }
    }

    /// <summary>A node of the Steiner graph: a base vertex, or parameter t on base edge (a, b), or an endpoint.</summary>
    internal readonly struct SteinerNode
    {
        public readonly int A, B; public readonly double T; // vertex: B = −1; endpoint: A = −1
        public SteinerNode(int a, int b, double t) { A = a; B = b; T = t; }
    }

    /// <summary>
    /// Shortest path in the fixed-scheme Steiner graph (m points per edge) between two points given
    /// by their base faces, by A*. Returns the nodes from the start to the end, endpoints included.
    /// </summary>
    internal static List<SteinerNode>? SteinerPath(MeshData tri, int[][] faceEdges, (int a, int b)[] edges, int[][] edgeFaces, int[][] vertexFaces,
        Vec3d pa, int fa, Vec3d pb, int fb, int m)
    {
        int nv = tri.VertexCount, ne = edges.Length;
        int nodeCount = nv + ne * m + 2;
        int idA = nodeCount - 2, idB = nodeCount - 1;
        Vec3d Pos(int id)
        {
            if (id < nv) return tri.Vertices[id];
            if (id == idA) return pa;
            if (id == idB) return pb;
            int e = (id - nv) / m, k = (id - nv) % m;
            double t = (k + 1.0) / (m + 1);
            var (a, b) = edges[e];
            return tri.Vertices[a] + t * (tri.Vertices[b] - tri.Vertices[a]);
        }
        var g = new Dictionary<int, double>();
        var via = new Dictionary<int, int>();
        var closed = new HashSet<int>();
        var heap = new BinaryHeap();
        g[idA] = 0; heap.Push((pa - pb).Length, idA);
        var buf = new List<int>();
        void FaceNodes(int f, List<int> into)
        {
            var t = tri.Faces[f];
            into.Add(t[0]); into.Add(t[1]); into.Add(t[2]);
            foreach (int e in faceEdges[f]) for (int k = 0; k < m; k++) into.Add(nv + e * m + k);
            if (f == fa) into.Add(idA);
            if (f == fb) into.Add(idB);
        }
        while (heap.Count > 0)
        {
            var (_, u) = heap.Pop();
            if (!closed.Add(u)) continue;
            if (u == idB) break;
            var pu = Pos(u);
            double gu = g[u];
            buf.Clear();
            if (u < nv) foreach (int f in vertexFaces[u]) FaceNodes(f, buf);
            else if (u == idA) FaceNodes(fa, buf);
            else if (u == idB) FaceNodes(fb, buf);
            else foreach (int f in edgeFaces[(u - nv) / m]) FaceNodes(f, buf);
            foreach (int w in buf)
            {
                if (w == u || closed.Contains(w)) continue;
                var pw = Pos(w);
                double nd = gu + (pw - pu).Length;
                if (!g.TryGetValue(w, out double old) || nd < old)
                {
                    g[w] = nd; via[w] = u;
                    heap.Push(nd + (pw - pb).Length, w);
                }
            }
        }
        if (!closed.Contains(idB)) return null;
        var ids = new List<int>();
        for (int v = idB; ; v = via[v]) { ids.Add(v); if (v == idA) break; }
        ids.Reverse();
        var nodes = new List<SteinerNode>(ids.Count);
        foreach (int id in ids)
        {
            if (id == idA || id == idB) nodes.Add(new SteinerNode(-1, -1, 0));
            else if (id < nv) nodes.Add(new SteinerNode(id, -1, 0));
            else
            {
                int e = (id - nv) / m, k = (id - nv) % m;
                nodes.Add(new SteinerNode(edges[e].a, edges[e].b, (k + 1.0) / (m + 1)));
            }
        }
        return nodes;
    }
}
