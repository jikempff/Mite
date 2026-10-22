using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// The faces of a lath net — the panels between the laths — and how planar
/// they are. Turns the structural graph of <see cref="NetTopology"/> (nodes,
/// members) into the quad-dominant mesh a planar-quad (PQ) check or
/// <c>Planarization</c> wants, with every face's planarity measured.
///
/// Why: a quad mesh with planar faces is the discrete form of a conjugate
/// curve network (Sauer 1970; Liu, Pottmann, Wallner, Yang &amp; Wang 2006,
/// "Geometric modeling with conical meshes and developable surfaces", SIGGRAPH,
/// §2: "Quad meshes with planar faces may be seen as a discrete version of
/// so-called conjugate curve networks on a surface"). Curvature lines are the
/// most important conjugate network (ibid. §4; Zadravec, Schiftner &amp; Wallner
/// 2010, "Designing quad-dominant meshes with planar faces", CGF 29(5), §1:
/// planarization "in general succeeds if it follows a network of conjugate
/// curves"); asymptotic directions are the self-conjugate, worst case
/// (Liu et al. 2006 §4; Pottmann, Liu, Wallner, Bobenko &amp; Wang 2007,
/// "Discrete surfaces for architectural design", §3.4). For a net with
/// parameter step h and unit directions t₁, t₂ the four corners of a panel
/// close up to (p₀₀ − p₁₀ − p₀₁ + p₁₁)·n = h²·II(t₁, t₂) + O(h³) (Taylor), so
/// the distance between the panel's diagonals is ½h²|II(t₁, t₂)| + O(h³):
/// zero at second order on a conjugate net (II(t₁, t₂) = 0), largest on an
/// asymptotic net. Normalised by the mean diagonal (~h) this is Zadravec's
/// δ_PQ,n (ibid. §6, red above 0.01): O(h²) for conjugate nets, O(h) otherwise.
///
/// How: every node gets a normal (the plane of its member directions,
/// oriented along a reference mesh or by propagation), the members leaving a
/// node are sorted counter-clockwise about it, and the faces are traced as
/// the cycles of the embedded graph (next edge = the one clockwise from the
/// reverse edge); clockwise cycles are the outer boundary and the holes and
/// are dropped. Free tails are left out; a closed lath's two tails are fused
/// into one member so the face across the seam exists.
/// </summary>
public static class NetFaces
{
    public readonly struct Face
    {
        /// <summary>Corner nodes, counter-clockwise about the surface normal.</summary>
        public readonly int[] Nodes;

        /// <summary>Member index of each edge Nodes[i] → Nodes[i + 1] (indices into <see cref="NetTopology.Result.Members"/>, or −1 for a fused seam member).</summary>
        public readonly int[] Members;

        /// <summary>Largest distance of a corner from the face's plane (model units) — the Planarize Mesh measure.</summary>
        public readonly double Deviation;

        /// <summary>Quads: the distance between the two diagonal lines (model units). Triangles 0; polygons: Deviation.</summary>
        public readonly double DiagonalDistance;

        /// <summary>Dimensionless planarity: quads DiagonalDistance / mean diagonal (Zadravec et al. 2010, δ_PQ,n; 0.01 is their red); polygons Deviation / mean corner distance from the centroid; triangles 0.</summary>
        public readonly double Planarity;

        public readonly double Area;
        public readonly Vec3d Centroid;
        public readonly Vec3d Normal;

        public Face(int[] nodes, int[] members, double deviation, double diagonalDistance, double planarity, double area, Vec3d centroid, Vec3d normal)
        {
            Nodes = nodes; Members = members; Deviation = deviation; DiagonalDistance = diagonalDistance;
            Planarity = planarity; Area = area; Centroid = centroid; Normal = normal;
        }

        public int Sides => Nodes.Length;
    }

    public sealed class Result
    {
        public List<Face> Faces { get; } = new List<Face>();

        /// <summary>Unit normal at every node of the topology (zero for nodes without members).</summary>
        public Vec3d[] NodeNormals { get; set; } = Array.Empty<Vec3d>();

        public int Triangles { get; set; }
        public int Quads { get; set; }
        public int Polygons { get; set; }

        /// <summary>Cycles dropped as clockwise: the outer boundary of every connected part and every hole.</summary>
        public int OuterLoops { get; set; }

        /// <summary>Two-sided cycles dropped: a lens between two laths that touch twice.</summary>
        public int Lenses { get; set; }

        /// <summary>Members dropped because another member runs between the same nodes along the same path.</summary>
        public int DuplicateEdges { get; set; }

        /// <summary>Members that took part (both ends on nodes, after fusing closed laths).</summary>
        public int EdgeCount { get; set; }

        /// <summary>Nodes with at least one such member.</summary>
        public int NodeCount { get; set; }

        /// <summary>Tails (members with a free end) left out.</summary>
        public int Tails { get; set; }

        public double MaxPlanarity { get; set; }
        public double MeanPlanarity { get; set; }
        public double MaxDeviation { get; set; }
        public double MeanDeviation { get; set; }
        public double TotalArea { get; set; }

        /// <summary>The quad-dominant mesh: vertices are the nodes that bound a face (<paramref name="nodeOfVertex"/> maps back), faces in <see cref="Faces"/> order.</summary>
        public MeshData ToMesh(out int[] nodeOfVertex)
        {
            var map = new Dictionary<int, int>();
            var verts = new List<Vec3d>();
            var nodeIdx = new List<int>();
            var faces = new int[Faces.Count][];
            for (int f = 0; f < Faces.Count; f++)
            {
                var corners = Faces[f].Nodes;
                var face = new int[corners.Length];
                for (int k = 0; k < corners.Length; k++)
                {
                    int n = corners[k];
                    if (!map.TryGetValue(n, out int v)) { v = verts.Count; map[n] = v; verts.Add(_nodes[n]); nodeIdx.Add(n); }
                    face[k] = v;
                }
                faces[f] = face;
            }
            nodeOfVertex = nodeIdx.ToArray();
            return new MeshData(verts.ToArray(), faces);
        }

        internal Vec3d[] _nodes = Array.Empty<Vec3d>();
    }

    /// <summary>
    /// Traces the faces of a net graph. <paramref name="reference"/> (the
    /// surface the net lies on) only orients the node normals; without it the
    /// orientation is propagated across the graph and the global sign is the
    /// one that makes most cycles counter-clockwise.
    /// </summary>
    public static Result Build(NetTopology.Result topo, MeshData? reference = null)
    {
        var result = new Result { _nodes = topo.Nodes };
        int nn = topo.Nodes.Length;
        var normals = new Vec3d[nn];
        result.NodeNormals = normals;
        if (nn == 0) return result;

        // ---- edges: members with both ends on nodes; closed laths fuse their two tails
        var edgeStart = new List<int>();
        var edgeEnd = new List<int>();
        var edgeMember = new List<int>();
        var edgeDirStart = new List<Vec3d>(); // outgoing direction at the start node
        var edgeDirEnd = new List<Vec3d>();   // outgoing direction at the end node
        int tails = 0;
        var members = topo.Members;
        // members per lath in arc order (NetTopology emits them that way)
        var byLath = new Dictionary<int, List<int>>();
        for (int m = 0; m < members.Count; m++)
        {
            var mem = members[m];
            if (mem.Points == null || mem.Points.Length < 2) continue;
            if (!byLath.TryGetValue(mem.Lath, out var list)) { list = new List<int>(); byLath[mem.Lath] = list; }
            list.Add(m);
        }
        foreach (var kv in byLath)
        {
            var list = kv.Value;
            var first = members[list[0]]; var last = members[list[list.Count - 1]];
            Vec3d p0 = first.Points[0], p1 = last.Points[last.Points.Length - 1];
            // closed when the ends meet within a quarter of the end segments: the member ends were snapped to
            // the crossing nodes, which sit half the crossing gap off the lath
            double seg = Math.Min(SegmentLength(first.Points, true), SegmentLength(last.Points, false));
            bool closed = (p1 - p0).Length <= 0.25 * seg;
            bool headTail = first.NodeStart < 0, footTail = last.NodeEnd < 0;
            for (int k = 0; k < list.Count; k++)
            {
                int m = list[k];
                var mem = members[m];
                int s = mem.NodeStart, e = mem.NodeEnd;
                Vec3d ds = OutDir(mem.Points, true), de = OutDir(mem.Points, false);
                int memberIndex = m;
                if (closed && list.Count > 1 && (headTail || footTail))
                {
                    // a closed lath's seam lies inside one panel: the tail after the last station and the
                    // tail before the first are one piece round the seam (or one of them is a station)
                    if (k == 0 && headTail && footTail) continue; // fused into the trailing tail below
                    if (k == list.Count - 1 && footTail)
                    {
                        if (headTail) { e = first.NodeEnd; de = OutDir(first.Points, false); memberIndex = -1; }
                        else e = first.NodeStart;
                    }
                    else if (k == 0 && headTail && !footTail) s = last.NodeEnd;
                }
                if (s >= 0 && e >= 0)
                {
                    if (s == e) continue; // a loop on one node carries no panel
                    edgeStart.Add(s); edgeEnd.Add(e); edgeMember.Add(memberIndex);
                    edgeDirStart.Add(ds); edgeDirEnd.Add(de);
                }
                else tails++;
            }
        }
        result.Tails = tails;
        // two members between the same nodes along the same path (laths that run together for a piece,
        // as a tracer leaves them where curves merge) carry no panel between them: keep one
        {
            var seen = new Dictionary<(int, int), List<int>>();
            var keep = new List<int>();
            for (int e = 0; e < edgeStart.Count; e++)
            {
                var key = edgeStart[e] < edgeEnd[e] ? (edgeStart[e], edgeEnd[e]) : (edgeEnd[e], edgeStart[e]);
                bool dup = false;
                if (seen.TryGetValue(key, out var others))
                    foreach (int o in others)
                    {
                        bool sameWay = edgeStart[o] == edgeStart[e];
                        var ds = sameWay ? edgeDirStart[o] : edgeDirEnd[o];
                        var de = sameWay ? edgeDirEnd[o] : edgeDirStart[o];
                        if (Vec3d.Dot(ds, edgeDirStart[e]) > 0.9999 && Vec3d.Dot(de, edgeDirEnd[e]) > 0.9999) { dup = true; break; }
                    }
                if (dup) { result.DuplicateEdges++; continue; }
                if (others == null) { others = new List<int>(); seen[key] = others; }
                others.Add(e);
                keep.Add(e);
            }
            if (keep.Count != edgeStart.Count)
            {
                edgeStart = keep.Select(e => edgeStart[e]).ToList(); edgeEnd = keep.Select(e => edgeEnd[e]).ToList();
                edgeMember = keep.Select(e => edgeMember[e]).ToList();
                edgeDirStart = keep.Select(e => edgeDirStart[e]).ToList(); edgeDirEnd = keep.Select(e => edgeDirEnd[e]).ToList();
            }
        }
        int ne = edgeStart.Count;
        result.EdgeCount = ne;
        if (ne == 0) return result;

        // ---- half-edges leaving each node: h = 2e (start → end), 2e + 1 (end → start)
        var leaving = new List<int>[nn];
        for (int e = 0; e < ne; e++)
        {
            (leaving[edgeStart[e]] ??= new List<int>()).Add(2 * e);
            (leaving[edgeEnd[e]] ??= new List<int>()).Add(2 * e + 1);
        }
        Vec3d DirOf(int h) => (h & 1) == 0 ? edgeDirStart[h >> 1] : edgeDirEnd[h >> 1];
        int FromOf(int h) => (h & 1) == 0 ? edgeStart[h >> 1] : edgeEnd[h >> 1];
        int ToOf(int h) => (h & 1) == 0 ? edgeEnd[h >> 1] : edgeStart[h >> 1];

        // ---- node normals: the plane of the member directions (smallest scatter eigenvector)
        int nodeCount = 0;
        for (int v = 0; v < nn; v++)
        {
            if (leaving[v] == null) continue;
            nodeCount++;
            normals[v] = PlaneNormal(leaving[v].Select(DirOf).ToList());
        }
        result.NodeCount = nodeCount;

        // orientation: along the reference surface, or propagated through the graph
        VertexKdTree? tree = reference != null && reference.VertexCount > 0 ? VertexKdTree.Build(reference.Vertices) : null;
        Vec3d[]? refNormals = tree != null ? reference!.ComputeVertexNormals() : null;
        var oriented = new bool[nn];
        if (tree != null)
        {
            for (int v = 0; v < nn; v++)
            {
                if (leaving[v] == null) continue;
                var rn = refNormals![tree.Nearest(topo.Nodes[v])];
                if (normals[v].LengthSquared == 0) { normals[v] = rn.Normalized(); oriented[v] = true; continue; }
                double d = Vec3d.Dot(normals[v], rn);
                if (Math.Abs(d) > 1e-9) { if (d < 0) normals[v] = -normals[v]; oriented[v] = true; }
            }
        }
        // propagate to whatever is still unoriented (or everything, without a reference)
        for (int s = 0; s < nn; s++)
        {
            if (leaving[s] == null || oriented[s]) continue;
            if (normals[s].LengthSquared == 0) continue;
            oriented[s] = true;
            var queue = new Queue<int>(); queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int h in leaving[v])
                {
                    int w = ToOf(h);
                    if (oriented[w]) continue;
                    if (normals[w].LengthSquared == 0) normals[w] = normals[v];
                    else if (Vec3d.Dot(normals[w], normals[v]) < 0) normals[w] = -normals[w];
                    oriented[w] = true;
                    queue.Enqueue(w);
                }
            }
        }

        // without a reference surface the global sign is a convention: the net's normals point up (z) on
        // average — the usual roof — or along +x, then +y, when the net stands vertical
        if (tree == null)
        {
            Vec3d sum = Vec3d.Zero;
            for (int v = 0; v < nn; v++) sum = sum + normals[v];
            double key = Math.Abs(sum.Z) > 1e-9 * (1 + sum.Length) ? sum.Z : Math.Abs(sum.X) > 1e-9 * (1 + sum.Length) ? sum.X : sum.Y;
            if (key < 0) for (int v = 0; v < nn; v++) normals[v] = -normals[v];
        }

        // ---- sort the leaving half-edges counter-clockwise about the node normal
        var order = new int[nn][];
        var pos = new int[2 * ne];
        for (int v = 0; v < nn; v++)
        {
            if (leaving[v] == null) continue;
            var n = normals[v];
            var list = leaving[v];
            Vec3d u = Vec3d.Zero;
            foreach (int h in list) { var d = DirOf(h); var t = d - Vec3d.Dot(d, n) * n; if (t.LengthSquared > 1e-24) { u = t.Normalized(); break; } }
            if (u.LengthSquared == 0) u = Perp(n);
            var w = Vec3d.Cross(n, u);
            var angles = list.Select(h => { var d = DirOf(h); return Math.Atan2(Vec3d.Dot(d, w), Vec3d.Dot(d, u)); }).ToArray();
            var idx = Enumerable.Range(0, list.Count).ToArray();
            Array.Sort(angles, idx);
            order[v] = idx.Select(i => list[i]).ToArray();
            for (int k = 0; k < order[v].Length; k++) pos[order[v][k]] = k;
        }

        // ---- trace the cycles: after u → v continue with the edge clockwise from v → u
        var visited = new bool[2 * ne];
        var cycles = new List<List<int>>();
        for (int h0 = 0; h0 < 2 * ne; h0++)
        {
            if (visited[h0]) continue;
            var cyc = new List<int>();
            int h = h0;
            int guard = 0;
            while (!visited[h] && guard++ <= 2 * ne)
            {
                visited[h] = true;
                cyc.Add(h);
                int v = ToOf(h);
                int rev = h ^ 1;
                var ord = order[v];
                int k = pos[rev];
                h = ord[(k - 1 + ord.Length) % ord.Length];
            }
            cycles.Add(cyc);
        }

        // ---- orientation of every cycle: counter-clockwise about the normals = a panel
        var signed = new double[cycles.Count];
        for (int c = 0; c < cycles.Count; c++)
        {
            var cyc = cycles[c];
            if (cyc.Count < 3) { signed[c] = 0; continue; }
            Vec3d nf = Vec3d.Zero, cen = Vec3d.Zero;
            foreach (int h in cyc) { nf = nf + normals[FromOf(h)]; cen = cen + topo.Nodes[FromOf(h)]; }
            cen = cen / cyc.Count;
            Vec3d area = Vec3d.Zero;
            for (int k = 0; k < cyc.Count; k++)
            {
                var p = topo.Nodes[FromOf(cyc[k])] - cen;
                var q = topo.Nodes[FromOf(cyc[(k + 1) % cyc.Count])] - cen;
                area = area + Vec3d.Cross(p, q);
            }
            signed[c] = 0.5 * Vec3d.Dot(area, nf.LengthSquared > 0 ? nf.Normalized() : Vec3d.Zero);
        }

        // ---- faces
        double sumPl = 0, sumDev = 0;
        for (int c = 0; c < cycles.Count; c++)
        {
            var cyc = cycles[c];
            double s = signed[c];
            if (cyc.Count < 3) { if (cyc.Count == 2) result.Lenses++; continue; }
            if (s <= 0) { result.OuterLoops++; continue; }
            var nodes = new int[cyc.Count];
            var mems = new int[cyc.Count];
            for (int k = 0; k < cyc.Count; k++) { nodes[k] = FromOf(cyc[k]); mems[k] = edgeMember[cyc[k] >> 1]; }
            // a cycle through the same node twice (a pinched panel) is kept only if it has three distinct corners
            if (nodes.Distinct().Count() < 3) { result.Lenses++; continue; }
            var corners = nodes.Select(i => topo.Nodes[i]).ToArray();
            var face = Measure(corners, nodes, mems);
            result.Faces.Add(face);
            if (face.Sides == 3) result.Triangles++; else if (face.Sides == 4) result.Quads++; else result.Polygons++;
            result.MaxPlanarity = Math.Max(result.MaxPlanarity, face.Planarity);
            result.MaxDeviation = Math.Max(result.MaxDeviation, face.Deviation);
            sumPl += face.Planarity; sumDev += face.Deviation;
            result.TotalArea += face.Area;
        }
        if (result.Faces.Count > 0) { result.MeanPlanarity = sumPl / result.Faces.Count; result.MeanDeviation = sumDev / result.Faces.Count; }
        return result;
    }

    /// <summary>Planarity of one panel from its corners (counter-clockwise or not — the measures are sign-free).</summary>
    public static Face Measure(Vec3d[] corners, int[]? nodes = null, int[]? members = null)
    {
        int k = corners.Length;
        nodes ??= Enumerable.Range(0, k).ToArray();
        members ??= Enumerable.Repeat(-1, k).ToArray();
        Vec3d cen = Vec3d.Zero;
        foreach (var p in corners) cen = cen + p;
        cen = cen / k;
        Vec3d nf = Vec3d.Zero;
        for (int i = 0; i < k; i++) nf = nf + Vec3d.Cross(corners[i] - cen, corners[(i + 1) % k] - cen);
        double area = 0.5 * nf.Length;
        var n = nf.LengthSquared > 0 ? nf.Normalized() : Vec3d.Zero;
        double dev = 0;
        foreach (var p in corners) dev = Math.Max(dev, Math.Abs(Vec3d.Dot(p - cen, n)));
        double diag = 0, pl = 0;
        if (k == 4)
        {
            diag = DiagonalDistance(corners[0], corners[1], corners[2], corners[3]);
            double mean = 0.5 * ((corners[2] - corners[0]).Length + (corners[3] - corners[1]).Length);
            pl = mean > 0 ? diag / mean : 0;
        }
        else if (k > 4)
        {
            diag = dev;
            double mean = 0; foreach (var p in corners) mean += (p - cen).Length; mean /= k;
            pl = mean > 0 ? dev / mean : 0;
        }
        return new Face(nodes, members, dev, diag, pl, area, cen, n);
    }

    /// <summary>Distance between the lines of the two diagonals a–c and b–d of a quad (Zadravec, Schiftner &amp; Wallner 2010, §6).</summary>
    public static double DiagonalDistance(Vec3d a, Vec3d b, Vec3d c, Vec3d d)
    {
        var d1 = c - a; var d2 = d - b;
        var x = Vec3d.Cross(d1, d2);
        double len = x.Length;
        if (len < 1e-300) return 0; // parallel diagonals: a degenerate quad
        return Math.Abs(Vec3d.Dot(b - a, x)) / len;
    }

    /// <summary>Per-face planarity (quads: diagonal distance / mean diagonal; polygons: plane deviation / mean radius) of any mesh.</summary>
    public static double[] Planarity(MeshData mesh)
    {
        var pl = new double[mesh.FaceCount];
        for (int f = 0; f < mesh.FaceCount; f++)
        {
            var face = mesh.Faces[f];
            if (face.Length < 4) continue;
            pl[f] = Measure(face.Select(i => mesh.Vertices[i]).ToArray()).Planarity;
        }
        return pl;
    }

    private static double SegmentLength(Vec3d[] pts, bool atStart)
    {
        int n = pts.Length;
        if (atStart) { for (int i = 1; i < n; i++) { double d = (pts[i] - pts[0]).Length; if (d > 0) return d; } }
        else { for (int i = n - 2; i >= 0; i--) { double d = (pts[i] - pts[n - 1]).Length; if (d > 0) return d; } }
        return 0;
    }

    /// <summary>
    /// Direction in which a member leaves a node: towards its point a quarter of its length along. The
    /// tangent itself would put two laths that touch tangentially (a lens between them, or a lath ending
    /// on its neighbour at a shallow angle) in an arbitrary order; a quarter of the way along, the one
    /// that bulges left is left. A lath piece between two crossings turns little over that quarter, far
    /// less than the crossing angle, so transversal members keep their order.
    /// </summary>
    private static Vec3d OutDir(Vec3d[] pts, bool atStart)
    {
        int n = pts.Length;
        double total = 0;
        for (int i = 1; i < n; i++) total += (pts[i] - pts[i - 1]).Length;
        double target = 0.25 * total;
        Vec3d origin = atStart ? pts[0] : pts[n - 1];
        double acc = 0;
        if (atStart)
        {
            for (int i = 1; i < n; i++)
            {
                double seg = (pts[i] - pts[i - 1]).Length;
                if (acc + seg >= target && seg > 0)
                {
                    var q = pts[i - 1] + (pts[i] - pts[i - 1]) * ((target - acc) / seg);
                    var d = q - origin; if (d.LengthSquared > 1e-28) return d.Normalized();
                }
                acc += seg;
            }
            for (int i = 1; i < n; i++) { var d = pts[i] - pts[0]; if (d.LengthSquared > 1e-28) return d.Normalized(); }
        }
        else
        {
            for (int i = n - 2; i >= 0; i--)
            {
                double seg = (pts[i] - pts[i + 1]).Length;
                if (acc + seg >= target && seg > 0)
                {
                    var q = pts[i + 1] + (pts[i] - pts[i + 1]) * ((target - acc) / seg);
                    var d = q - origin; if (d.LengthSquared > 1e-28) return d.Normalized();
                }
                acc += seg;
            }
            for (int i = n - 2; i >= 0; i--) { var d = pts[i] - pts[n - 1]; if (d.LengthSquared > 1e-28) return d.Normalized(); }
        }
        return Vec3d.Zero;
    }

    /// <summary>Unit normal of the plane that fits a set of directions best (smallest eigenvector of Σ ddᵀ); zero when they are collinear.</summary>
    private static Vec3d PlaneNormal(List<Vec3d> dirs)
    {
        if (dirs.Count >= 2)
        {
            // the cross product with the largest magnitude is a good, exact start for the usual valence 2–4 nodes
            Vec3d best = Vec3d.Zero; double bestLen = 0;
            for (int i = 0; i < dirs.Count; i++)
                for (int j = i + 1; j < dirs.Count; j++)
                {
                    var x = Vec3d.Cross(dirs[i], dirs[j]);
                    double l = x.LengthSquared;
                    if (l > bestLen) { bestLen = l; best = x; }
                }
            if (bestLen < 1e-20) return Vec3d.Zero;
            var n = best.Normalized();
            // refine: a few inverse-iteration steps on M = Σ ddᵀ shifted, i.e. power iteration on (tr M)I − M
            double m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0;
            foreach (var d in dirs) { m00 += d.X * d.X; m01 += d.X * d.Y; m02 += d.X * d.Z; m11 += d.Y * d.Y; m12 += d.Y * d.Z; m22 += d.Z * d.Z; }
            double tr = m00 + m11 + m22;
            for (int it = 0; it < 12; it++)
            {
                var y = new Vec3d(tr * n.X - (m00 * n.X + m01 * n.Y + m02 * n.Z),
                                  tr * n.Y - (m01 * n.X + m11 * n.Y + m12 * n.Z),
                                  tr * n.Z - (m02 * n.X + m12 * n.Y + m22 * n.Z));
                if (y.LengthSquared < 1e-30) break;
                var yn = y.Normalized();
                if (Vec3d.Dot(yn, n) < 0) yn = -yn;
                n = yn;
            }
            return n;
        }
        return Vec3d.Zero;
    }

    private static Vec3d Perp(Vec3d n)
    {
        var a = Math.Abs(n.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
        var p = Vec3d.Cross(n, a);
        return p.LengthSquared > 0 ? p.Normalized() : new Vec3d(0, 0, 1);
    }
}
