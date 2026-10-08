// Exact geodesic paths by edge flips (FlipOut).
//
// Sources:
//   Sharp & Crane 2020, "You can find geodesic paths in triangle meshes by just flipping edges",
//     ACM TOG 39(6) 249 — Algorithm 1 (FlipOut on a joint a–b–c whose wedge angle α < π: flip the
//     first wedge edge b–n_j whose outer angle β_j < π until none is left, then replace a–b–c by
//     the outer arc of the wedge; Theorem 4.1: the new path is shorter) and Algorithm 2
//     (MakePathGeodesic: repeat on the joint of smallest α; Theorem 4.2: terminates; the result has
//     α ≥ π on both sides of every interior vertex, i.e. it is a geodesic of the polyhedral surface).
//     Boundary wedges have α = ∞. Endpoints are vertices; points inside a face are inserted first.
//   Sharp, Soliman & Crane 2019, "Navigating intrinsic triangulations" — signposts and tracing
//     (see IntrinsicTriangulation).
//   Mitchell, Mount & Papadimitriou 1987, "The discrete geodesic problem", SIAM J. Comput. 16(4),
//     Lemmas 3.3–3.4 — a geodesic unfolds to a straight segment across faces, and passes through a
//     vertex only if it leaves an angle ≥ π on each side: never through a cone apex (Θ < 2π),
//     possibly through a saddle vertex (Θ > 2π).
using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Exact shortest-path geodesics on a triangle mesh (FlipOut, Sharp &amp; Crane 2020). A start
/// edge path is shortened by intrinsic edge flips until it is straight across every face and edge
/// and leaves at least π on both sides of every vertex it passes; it is then traced back onto the
/// mesh as a polyline through its edge crossings. The result is the geodesic of the polyhedral
/// surface itself (no step size, no smoothing tolerance), locally shortest in the class of the
/// start path (which side of each vertex it takes). The start path is the shortest path of a
/// Steiner graph (see FlipGeodesicStart.cs), which takes the same sides as the true shortest path
/// unless two ways differ by less than its ~1 % distortion; the plain Dijkstra start is tried too.
/// </summary>
public static partial class FlipGeodesic
{
    public class Options
    {
        /// <summary>A joint counts as straight when both side angles are at least π − this (radians).</summary>
        public double AngleTolerance { get; set; } = 1e-9;

        /// <summary>
        /// Steiner points per edge for the start path (Lanthier, Maheshwari &amp; Sack 2001); 0 starts
        /// from the plain Dijkstra edge path, as in Sharp &amp; Crane 2020.
        /// </summary>
        public int SteinerPoints { get; set; } = 3;

        /// <summary>Also run FlipOut from the plain Dijkstra edge path and keep the shorter geodesic.</summary>
        public bool AlsoGraphStart { get; set; } = true;

        /// <summary>Cap on FlipOut steps per path (0 = automatic: a safety cap of one million; Sharp &amp; Crane prove termination).</summary>
        public int MaxSteps { get; set; } = 0;
    }

    public sealed class Result
    {
        /// <summary>Polyline on the mesh: endpoints, the vertices the geodesic passes and every edge crossing.</summary>
        public Vec3d[] Points { get; internal set; } = Array.Empty<Vec3d>();
        /// <summary>Length of the geodesic (sum of its intrinsic edge lengths).</summary>
        public double Length { get; internal set; }
        /// <summary>Length of the edge path it started from.</summary>
        public double StartLength { get; internal set; }
        /// <summary>FlipOut steps (joints shortened).</summary>
        public int Steps { get; internal set; }
        /// <summary>Intrinsic edge flips.</summary>
        public int Flips { get; internal set; }
        /// <summary>True when every joint leaves at least π − tolerance on both sides.</summary>
        public bool Converged { get; internal set; }
        /// <summary>Smallest side angle over the joints of the result (π or more on a geodesic; ∞ when none).</summary>
        public double MinJointAngle { get; internal set; }
        /// <summary>Mesh vertices the geodesic passes through (saddle or flat vertices only, on a geodesic).</summary>
        public Vec3d[] Vertices { get; internal set; } = Array.Empty<Vec3d>();
        /// <summary>Which start path gave the result: "Steiner" or "Dijkstra".</summary>
        public string Start { get; internal set; } = "";
        /// <summary>Largest gap between a traced edge's end and its target vertex (rounding level when sound).</summary>
        public double TraceMiss { get; internal set; }
    }

    /// <summary>
    /// Geodesics between pairs of points (each projected onto the mesh first; points are paired
    /// index by index, the shorter list reusing its last point). Returns null when the mesh is not a
    /// manifold, consistently oriented triangle mesh — the reason is given — and per pair null
    /// when the points lie on different mesh parts.
    /// </summary>
    public static Result?[]? Compute(MeshData mesh, IReadOnlyList<Vec3d> from, IReadOnlyList<Vec3d> to,
        out string? reason, Options? options = null)
    {
        options ??= new Options();
        reason = null;
        int count = Math.Max(from.Count, to.Count);
        if (count == 0 || from.Count == 0 || to.Count == 0) return Array.Empty<Result?>();
        var tri = mesh.ToTriangulated();
        if (tri.FaceCount == 0) { reason = "the mesh has no faces"; return null; }

        // FlipOut needs a consistently oriented manifold: orient the faces like Mesh Cleanup does
        if (EdgeUsedTwice(tri))
        {
            var faces = tri.Faces.Select(f => (int[])f.Clone()).ToList();
            MeshCleanup.UnifyWinding(faces);
            tri = new MeshData(tri.Vertices, faces.ToArray());
        }

        // project the points (exactly, over the whole mesh) and insert them as vertices
        var proj = new MeshProjection(tri);
        tri = proj.Mesh;
        var refiner = new Refiner(tri);
        if (refiner.EdgeUsedTwice)
        {
            reason = "the mesh is non-manifold or its faces are not consistently oriented (an edge is used twice in the same direction)";
            return null;
        }
        var endpoints = new int[count, 2];
        var hits = new Dictionary<Vec3d, MeshProjection.Hit>();
        var ids = new Dictionary<Vec3d, int>();
        for (int i = 0; i < count; i++)
            for (int e = 0; e < 2; e++)
            {
                var p = e == 0 ? from[Math.Min(i, from.Count - 1)] : to[Math.Min(i, to.Count - 1)];
                if (!ids.TryGetValue(p, out int vi))
                {
                    var hit = proj.ClosestPointGlobal(p);
                    hits[p] = hit;
                    vi = hit.Face < 0 ? -1 : refiner.InBaseFace(hit.Face, hit.Point, hit.Bary, 1e-12);
                    ids[p] = vi;
                }
                endpoints[i, e] = vi;
            }

        // start paths: shortest paths of the Steiner graph, inserted as chains of mesh vertices
        var starts = new List<int>?[count];
        int m = options.SteinerPoints;
        if (m > 0)
        {
            var edges = tri.BuildEdges();
            var edgeIndex = new Dictionary<long, int>(edges.Length * 2, new LongHash());
            for (int e = 0; e < edges.Length; e++) edgeIndex[((long)edges[e].v0 << 32) | (uint)edges[e].v1] = e;
            var edgeFacesL = new List<int>[edges.Length];
            var faceEdges = new int[tri.FaceCount][];
            for (int f = 0; f < tri.FaceCount; f++)
            {
                var t = tri.Faces[f];
                faceEdges[f] = new int[3];
                for (int c = 0; c < 3; c++)
                {
                    int a = t[c], b = t[(c + 1) % 3];
                    int ei = edgeIndex[a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a];
                    faceEdges[f][c] = ei;
                    (edgeFacesL[ei] ??= new List<int>()).Add(f);
                }
            }
            var edgeFaces = edgeFacesL.Select(l => l?.ToArray() ?? Array.Empty<int>()).ToArray();
            var vertexFaces = tri.BuildVertexFaces();
            for (int i = 0; i < count; i++)
            {
                int a = endpoints[i, 0], b = endpoints[i, 1];
                if (a < 0 || b < 0 || a == b) continue;
                var ha = hits[from[Math.Min(i, from.Count - 1)]];
                var hb = hits[to[Math.Min(i, to.Count - 1)]];
                var nodes = SteinerPath(tri, faceEdges, edges, edgeFaces, vertexFaces, ha.Point, ha.Face, hb.Point, hb.Face, m);
                if (nodes == null) continue;
                var seq = new List<int>();
                for (int k = 0; k < nodes.Count; k++)
                {
                    var nd = nodes[k];
                    int v;
                    if (nd.A < 0) v = k == 0 ? a : b;
                    else if (nd.B < 0) v = nd.A;
                    else
                    {
                        var pa = tri.Vertices[nd.A]; var pb = tri.Vertices[nd.B];
                        v = refiner.OnBaseEdge(nd.A, nd.B, nd.T, pa + nd.T * (pb - pa));
                    }
                    if (v < 0) { seq = null; break; }
                    if (seq.Count == 0 || seq[seq.Count - 1] != v) seq.Add(v);
                }
                starts[i] = seq;
            }
        }

        var it = IntrinsicTriangulation.Build(refiner.V, refiner.F, out reason);
        if (it == null) return null;

        var results = new Result?[count];
        for (int i = 0; i < count; i++)
        {
            int a = endpoints[i, 0], b = endpoints[i, 1];
            if (a < 0 || b < 0) continue;
            // every pair starts from the input triangulation: the long, thin edges left by an
            // earlier pair's flips would steer the next Dijkstra path round the wrong side of a cone
            // point (measured: 32 % too long on the cone test before the reset)
            Result? best = null;
            if (starts[i] != null)
            {
                it.Reset();
                best = Solve(it, a, b, options, starts[i]);
                if (best != null) best.Start = "Steiner";
            }
            if (best == null || options.AlsoGraphStart)
            {
                it.Reset();
                var r = Solve(it, a, b, options);
                if (r != null) r.Start = "Dijkstra";
                if (best == null || (r != null && r.Length < best.Length - 1e-12 * Math.Max(1, best.Length))) best = r;
            }
            results[i] = best;
        }
        return results;
    }

    private static bool EdgeUsedTwice(MeshData tri)
    {
        var seen = new HashSet<long>(new LongHash());
        foreach (var t in tri.Faces)
            for (int c = 0; c < 3; c++)
                if (!seen.Add(((long)t[c] << 32) | (uint)t[(c + 1) % 3])) return true;
        return false;
    }

    /// <summary>Geodesic between two vertices of an intrinsic triangulation (it is modified by flips).</summary>
    public static Result? Solve(IntrinsicTriangulation it, int a, int b, Options? options = null, IReadOnlyList<int>? start = null)
    {
        options ??= new Options();
        var res = new Result();
        if (a == b)
        {
            res.Points = new[] { it.Positions[a] };
            res.Converged = true; res.MinJointAngle = double.PositiveInfinity;
            return res;
        }
        var path = start != null ? EdgePath(it, start) : Dijkstra(it, a, b);
        if (path == null || path.Count == 0 || it.Origin(path[0]) != a || it.Head(path[path.Count - 1]) != b) path = Dijkstra(it, a, b);
        if (path == null) return null;
        res.StartLength = path.Sum(h => it.Length(h));
        int flips0 = it.FlipCount;
        int maxSteps = options.MaxSteps > 0 ? options.MaxSteps : 1_000_000;
        double eps = options.AngleTolerance;

        // joint j sits between path[j−1] and path[j] (j = 1..m−1); its two side angles come from the
        // signposts, which flips elsewhere never change (directions are intrinsic), so only the
        // joints of a replaced stretch need recomputing
        var onPath = new bool[it.HalfedgeCount];
        foreach (int h in path) { onPath[h] = true; onPath[it.Twin(h)] = true; }
        var left = new List<double> { double.PositiveInfinity };
        var right = new List<double> { double.PositiveInfinity };
        for (int j = 1; j < path.Count; j++)
        {
            JointAngles(it, path[j - 1], path[j], out double l, out double r);
            left.Add(l); right.Add(r);
        }

        int steps = 0;
        double minAngle;
        while (true)
        {
            int best = -1; double bestAngle = double.PositiveInfinity; bool bestLeft = false;
            for (int j = 1; j < path.Count; j++)
            {
                if (left[j] < bestAngle) { bestAngle = left[j]; best = j; bestLeft = true; }
                if (right[j] < bestAngle) { bestAngle = right[j]; best = j; bestLeft = false; }
            }
            minAngle = bestAngle;
            if (best < 0 || bestAngle >= Math.PI - eps) break;
            if (steps >= maxSteps) break;
            var arc = FlipOut(it, path, best, bestLeft, onPath);
            if (arc == null)
            {
                // the wedge could not be shortened (rounding at an angle within ~1e-12 of π):
                // treat this side as straight and go on with the others
                if (bestLeft) left[best] = Math.PI; else right[best] = Math.PI;
                continue;
            }
            onPath[path[best - 1]] = false; onPath[it.Twin(path[best - 1])] = false;
            onPath[path[best]] = false; onPath[it.Twin(path[best])] = false;
            foreach (int h in arc) { onPath[h] = true; onPath[it.Twin(h)] = true; }
            path.RemoveRange(best - 1, 2);
            path.InsertRange(best - 1, arc);
            // the old joint `best` is replaced by arc.Count − 1 new ones; the joints at a and c change too
            left.RemoveAt(best); right.RemoveAt(best);
            for (int q = 0; q < arc.Count - 1; q++) { left.Insert(best, 0); right.Insert(best, 0); }
            int j0 = Math.Max(1, best - 1), j1 = Math.Min(path.Count - 1, best - 1 + arc.Count);
            for (int j = j0; j <= j1; j++)
            {
                JointAngles(it, path[j - 1], path[j], out double l, out double r);
                left[j] = l; right[j] = r;
            }
            steps++;
        }
        res.Steps = steps;
        res.Flips = it.FlipCount - flips0;
        res.Converged = minAngle >= Math.PI - eps;
        res.MinJointAngle = minAngle;
        res.Length = path.Sum(h => it.Length(h));

        // trace back onto the input mesh
        var pts = new List<Vec3d>();
        var through = new List<Vec3d>();
        double miss = 0;
        for (int k = 0; k < path.Count; k++)
        {
            var seg = it.TraceEdge(path[k], out double m);
            miss = Math.Max(miss, m);
            if (k == 0) pts.AddRange(seg); else { through.Add(seg[0]); pts.AddRange(seg.Skip(1)); }
        }
        res.Points = Dedupe(pts);
        res.Vertices = through.ToArray();
        res.TraceMiss = miss;
        return res;
    }

    /// <summary>
    /// Side angles of the joint between path halfedges hin (a→b) and hout (b→c): left is swept
    /// counter-clockwise from b→c to b→a, right from b→a to b→c; ∞ when the sweep crosses the border.
    /// Read from the signposts (rescaled directions at b), so the cost does not grow with the degree.
    /// </summary>
    public static void JointAngles(IntrinsicTriangulation it, int hin, int hout, out double left, out double right)
    {
        int back = it.Twin(hin); // b→a
        int b = it.Origin(hout);
        double scale = it.AngleSum(b) / (2 * Math.PI);
        double po = it.Signpost(hout), pb = it.Signpost(back);
        if (it.IsBorderVertex(b))
        {
            // border fans run from 0 to 2π without wrapping; the outside lies beyond both ends
            left = pb >= po ? (pb - po) * scale : double.PositiveInfinity;
            right = po >= pb ? (po - pb) * scale : double.PositiveInfinity;
            return;
        }
        double d = pb - po;
        d %= 2 * Math.PI;
        if (d < 0) d += 2 * Math.PI;
        left = d * scale;
        right = (2 * Math.PI - d) * scale;
    }

    private static List<int>? Fan(IntrinsicTriangulation it, int from, int to)
    {
        var fan = new List<int>();
        int h = from;
        int guard = 100000;
        while (guard-- > 0)
        {
            fan.Add(h);
            if (h == to) return fan;
            if (it.Face(h) < 0) return null;
            h = it.Ccw(h);
        }
        return null;
    }

    /// <summary>
    /// Algorithm 1 of Sharp &amp; Crane 2020 on the joint between path[j−1] and path[j], on the left
    /// (counter-clockwise from b→c to b→a) or right side. Returns the replacing halfedges a→…→c.
    /// </summary>
    private static List<int>? FlipOut(IntrinsicTriangulation it, List<int> path, int j, bool left, bool[] onPath)
    {
        int hin = path[j - 1], hout = path[j];
        int back = it.Twin(hin);
        int from = left ? hout : back, to = left ? back : hout;
        var fan = Fan(it, from, to);
        if (fan == null) return null;
        // flip the first wedge edge whose outer angle is below π, until none is left; a flip only
        // changes the outer angles of its two neighbours, so step back one and scan on
        int k = 1;
        while (k + 1 < fan.Count)
        {
            int e = fan[k];
            double beta = it.Corner(it.Prev(fan[k - 1])) + it.Corner(it.Next(e));
            if (beta < Math.PI && !onPath[e] && it.Flip(e))
            {
                fan.RemoveAt(k);
                k = Math.Max(1, k - 1);
            }
            else k++;
        }
        var arc = new List<int>();
        int m = fan.Count - 1;
        if (left) for (int i = m - 1; i >= 0; i--) arc.Add(it.Twin(it.Next(fan[i])));
        else for (int i = 0; i < m; i++) arc.Add(it.Next(fan[i]));
        double before = it.Length(hin) + it.Length(hout);
        double after = 0;
        foreach (int h in arc) after += it.Length(h);
        if (!(after < before)) return null;
        return arc;
    }

    /// <summary>
    /// Halfedges joining a chain of vertices; where two consecutive vertices share no edge the gap is
    /// bridged by Dijkstra. Immediate back-steps (h followed by its twin) are removed.
    /// </summary>
    public static List<int>? EdgePath(IntrinsicTriangulation it, IReadOnlyList<int> vertices)
    {
        var path = new List<int>();
        for (int k = 0; k + 1 < vertices.Count; k++)
        {
            int x = vertices[k], y = vertices[k + 1];
            if (x == y) continue;
            int found = -1;
            var fan = it.FanOf(x);
            if (fan != null) foreach (int h in fan) if (it.Head(h) == y) { found = h; break; }
            if (found >= 0) Push(path, it, found);
            else
            {
                var gap = Dijkstra(it, x, y);
                if (gap == null) return null;
                foreach (int h in gap) Push(path, it, h);
            }
        }
        return path;
    }

    private static void Push(List<int> path, IntrinsicTriangulation it, int h)
    {
        if (path.Count > 0 && path[path.Count - 1] == it.Twin(h)) path.RemoveAt(path.Count - 1);
        else path.Add(h);
    }

    /// <summary>Shortest edge path (halfedges a→…→b) over the intrinsic edges, or null when disconnected.</summary>
    public static List<int>? Dijkstra(IntrinsicTriangulation it, int a, int b)
    {
        int nv = it.VertexCount;
        var dist = new double[nv];
        var via = new int[nv];
        for (int i = 0; i < nv; i++) { dist[i] = double.PositiveInfinity; via[i] = -1; }
        dist[a] = 0;
        var heap = new BinaryHeap();
        heap.Push(0, a);
        while (heap.Count > 0)
        {
            var (d, u) = heap.Pop();
            if (d > dist[u]) continue;
            if (u == b) break;
            var fan = it.FanOf(u);
            if (fan == null) continue;
            foreach (int h in fan)
            {
                int w = it.Head(h);
                double nd = d + it.Length(h);
                if (nd < dist[w]) { dist[w] = nd; via[w] = h; heap.Push(nd, w); }
            }
        }
        if (double.IsPositiveInfinity(dist[b])) return null;
        var path = new List<int>();
        for (int v = b; v != a; v = it.Origin(via[v])) path.Add(via[v]);
        path.Reverse();
        return path;
    }

    private static Vec3d[] Dedupe(List<Vec3d> pts)
    {
        var res = new List<Vec3d>(pts.Count);
        foreach (var p in pts)
            if (res.Count == 0 || (p - res[res.Count - 1]).LengthSquared > 1e-28) res.Add(p);
        return res.ToArray();
    }

    /// <summary>Polyline length.</summary>
    public static double PolylineLength(IReadOnlyList<Vec3d> pts)
    {
        double s = 0;
        for (int i = 1; i < pts.Count; i++) s += (pts[i] - pts[i - 1]).Length;
        return s;
    }

    private sealed class BinaryHeap
    {
        private readonly List<(double k, int v)> _a = new();
        public int Count => _a.Count;
        public void Push(double k, int v)
        {
            _a.Add((k, v));
            int i = _a.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_a[p].k <= _a[i].k) break;
                (_a[p], _a[i]) = (_a[i], _a[p]); i = p;
            }
        }
        public (double k, int v) Pop()
        {
            var top = _a[0];
            var last = _a[_a.Count - 1];
            _a.RemoveAt(_a.Count - 1);
            if (_a.Count > 0)
            {
                _a[0] = last;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, s = i;
                    if (l < _a.Count && _a[l].k < _a[s].k) s = l;
                    if (r < _a.Count && _a[r].k < _a[s].k) s = r;
                    if (s == i) break;
                    (_a[s], _a[i]) = (_a[i], _a[s]); i = s;
                }
            }
            return top;
        }
    }
}
