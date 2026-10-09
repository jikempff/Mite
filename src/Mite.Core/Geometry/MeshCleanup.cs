// Mesh healing for analysis: weld, collapsed / degenerate / duplicate faces, consistent and
// outward orientation, slivers (needles and caps), isolated vertices, and a defect report.
//
// Sources:
//   Attene, Campen & Kobbelt 2013, "Polygon mesh repairing: an application perspective" (ACM
//     Comput. Surv. 45(2)) and their EG 2012 tutorial "A practical guide to polygon mesh
//     repairing": the defect taxonomy used for the report — isolated vertices ("a vertex that is
//     not incident to any edge"), singular (non-manifold) edges, incoherently oriented faces,
//     holes, degenerate elements; caps "can be resolved by 'swapping' the edge opposite to the
//     flat corner", needles by "collapsing the edge opposite to the degenerate corner"; this
//     converges for exact degeneracies, "not for near-degeneracies", so what is left is reported.
//   Attene 2010, "A lightweight approach to repairing digitized polygon meshes" (The Visual
//     Computer 26) — MeshFix: local edits around each defect, not a global resampling.
//   Botsch & Kobbelt 2001, "A robust procedure to eliminate degenerate faces from triangle
//     meshes" (VMV 2001): needles ("longest edge much longer than the shortest one") are removed
//     by collapsing their shortest edge; caps (maximum angle close to 180°) are the other class.
//   CGAL 6.2 Polygon Mesh Repair, remove_almost_degenerate_faces (independent implementation):
//     "needles are removed by collapsing their shortest edges, caps are removed by flipping the
//     edge opposite to the largest angle (with the exception of caps on the boundary that are
//     simply removed from the mesh)"; is_outward_oriented: a closed mesh is positively oriented
//     when "the normal vectors to all its faces point outside the domain bounded by" it.
//   Dey, Edelsbrunner, Guha & Nekhayev 1999, "Topology preserving edge contraction" (Publ. Inst.
//     Math. 66): an edge ab of a 2-manifold may be contracted iff Lk a ∩ Lk b = Lk ab; with a
//     border, a dummy vertex ω joined to every border edge closes the complex.
//   Garland & Heckbert 1997, "Surface simplification using quadric error metrics" (SIGGRAPH):
//     the error of a position is the sum of squared distances to the planes of the faces it
//     leaves; "if the normal flips, that contraction can be ... disallowed".
//
// What is implemented (triangle with angles α ≤ β ≤ γ, shortest edge s opposite α, longest edge
// L opposite γ): a triangle with α below the Sliver angle is a NEEDLE when L ≥ 4·s (CGAL's default
// needle_threshold; Botsch & Kobbelt's "longest edge much longer than the shortest one") and is
// removed by collapsing s; otherwise L/s = sin γ / sin α < 4 forces γ close to 180° and it is a
// CAP, removed by flipping L (or dropped when L is a border edge). The end of s that goes is the
// one whose move onto the other leaves the smaller Garland–Heckbert error; on a tie a border end
// stays, then the older (lower-index) vertex.
//   A collapse needs the link condition, triangles all round, no face normal turning by ≥ 90°,
//   and the new faces' smallest angle above the sliver's; a flip needs a manifold interior edge,
//   no existing apex–opposite edge, no fold, and a larger smallest angle afterwards (Lawson's
//   max–min criterion). Vertices are never moved, only removed.
//   Orientation: winding is propagated across manifold edges only; a closed part is turned
//   outward (signed volume Σ det(p0 − c, p1 − c, p2 − c)/6 > 0 about its centroid c), an open one
//   keeps the winding of the larger share of its input face area.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mite.Core.Geometry;

/// <summary>
/// Mesh healing: weld coincident vertices, drop degenerate and duplicate
/// faces, unify face winding (outward on closed parts, the input's majority on
/// open ones), optionally remove sliver triangles, drop isolated vertices, and
/// report what is left (non-manifold edges, orientation conflicts, holes).
/// Most Mite algorithms assume a clean manifold mesh; Rhino meshes in the wild
/// routinely have duplicate vertices (from joins/explodes), zero-area faces,
/// slivers from booleans and mixed winding.
/// </summary>
public static class MeshCleanup
{
    /// <summary>Settings for <see cref="Compute(MeshData, Options)"/>.</summary>
    public sealed class Options
    {
        /// <summary>Weld distance; 0 = automatic (1e-6 of the average edge length).</summary>
        public double WeldTolerance { get; set; }

        /// <summary>Make the winding consistent (and outward on closed parts).</summary>
        public bool UnifyWinding { get; set; } = true;

        /// <summary>
        /// Triangles with an angle below this many degrees are slivers and are
        /// collapsed (needles) or flipped (caps); 0 = leave them (default).
        /// </summary>
        public double SliverAngle { get; set; }

        /// <summary>Drop vertices that no face uses (default true).</summary>
        public bool RemoveIsolatedVertices { get; set; } = true;
    }

    /// <summary>What the cleanup found and did beyond the weld / face counts.</summary>
    public sealed class Diagnostics
    {
        /// <summary>Vertices no face used, dropped (their Map entry is −1).</summary>
        public int IsolatedVertices { get; internal set; }
        /// <summary>Faces whose winding in the output is the reverse of the input.</summary>
        public int FlippedFaces { get; internal set; }
        /// <summary>Edge-connected parts of the output.</summary>
        public int Components { get; internal set; }
        /// <summary>Parts without border and without non-manifold edges (oriented outward).</summary>
        public int ClosedComponents { get; internal set; }
        /// <summary>Edges used by more than two faces (output vertex indices).</summary>
        public (int A, int B)[] NonManifoldEdges { get; internal set; } = Array.Empty<(int, int)>();
        /// <summary>Manifold edges whose two faces still run the same way (a non-orientable, Möbius-like part).</summary>
        public int OrientationConflicts { get; internal set; }
        /// <summary>Border loops of the output (an open sheet has 1, an annulus 2, a closed mesh 0).</summary>
        public int BorderLoops { get; internal set; }
        /// <summary>Needles removed by collapsing their shortest edge.</summary>
        public int NeedlesCollapsed { get; internal set; }
        /// <summary>Caps removed by flipping their longest edge.</summary>
        public int CapsFlipped { get; internal set; }
        /// <summary>Caps on the border removed with their face.</summary>
        public int CapsRemoved { get; internal set; }
        /// <summary>Triangles still below the sliver angle (below 1° when slivers are not removed).</summary>
        public int SliversLeft { get; internal set; }
        /// <summary>Smallest triangle angle before the sliver pass, in degrees (NaN without triangles).</summary>
        public double MinAngleBefore { get; internal set; } = double.NaN;
        /// <summary>Smallest triangle angle of the output, in degrees (NaN without triangles).</summary>
        public double MinAngleAfter { get; internal set; } = double.NaN;
        /// <summary>One line per finding, with the fix where there is one.</summary>
        public string Report { get; internal set; } = "";
    }

    public readonly struct Result
    {
        public readonly MeshData Mesh;
        public readonly int WeldedVertices;
        public readonly int RemovedDegenerateFaces;
        public readonly int RemovedDuplicateFaces;

        /// <summary>
        /// Index of the cleaned vertex each input vertex was mapped to; a vertex
        /// removed by a sliver collapse maps to the vertex it was collapsed onto,
        /// an isolated vertex that was dropped maps to −1.
        /// </summary>
        public readonly int[] VertexMap;

        /// <summary>Orientation, slivers, isolated vertices and the remaining defects.</summary>
        public readonly Diagnostics Info;

        public Result(MeshData mesh, int welded, int degenerate, int duplicate, int[] vertexMap)
            : this(mesh, welded, degenerate, duplicate, vertexMap, new Diagnostics()) { }

        public Result(MeshData mesh, int welded, int degenerate, int duplicate, int[] vertexMap, Diagnostics info)
        {
            Mesh = mesh;
            WeldedVertices = welded;
            RemovedDegenerateFaces = degenerate;
            RemovedDuplicateFaces = duplicate;
            VertexMap = vertexMap;
            Info = info ?? new Diagnostics();
        }
    }

    /// <summary>Angle in degrees under which a triangle is reported as a sliver when slivers are not removed.</summary>
    public const double ReportSliverAngle = 1.0;

    /// <summary>
    /// Cleans a mesh. weldTolerance 0 selects an automatic value (1e-6 of the
    /// average edge length). Faces are kept as polygons; the degenerate test
    /// uses the polygon's vector area.
    /// </summary>
    public static Result Compute(MeshData mesh, double weldTolerance = 0.0, bool unifyWinding = true) =>
        Compute(mesh, new Options { WeldTolerance = weldTolerance, UnifyWinding = unifyWinding });

    /// <summary>Cleans a mesh with the given options.</summary>
    public static Result Compute(MeshData mesh, Options options)
    {
        options ??= new Options();
        int nv = mesh.VertexCount;
        var info = new Diagnostics();

        double avgEdge = 1.0;
        {
            double sum = 0;
            int count = 0;
            foreach (var f in mesh.Faces)
                for (int i = 0; i < f.Length; i++)
                {
                    sum += (mesh.Vertices[f[(i + 1) % f.Length]] - mesh.Vertices[f[i]]).Length;
                    count++;
                }
            if (count > 0 && sum > 0) avgEdge = sum / count;
        }
        double tol = options.WeldTolerance > 0 ? options.WeldTolerance : 1e-6 * avgEdge;

        // --- Weld vertices: grid hash, first vertex in a cluster is the representative
        var map = new int[nv];
        var kept = new List<int>();
        var grid = new Dictionary<(long, long, long), List<int>>();
        // Cells are a few tolerances wide and keyed relative to the bounding
        // box minimum with 64-bit keys, so far-from-origin (georeferenced)
        // models neither overflow the key nor collapse into a single bucket
        double cell = Math.Max(4.0 * tol, 1e-12);
        Vec3d origin = nv > 0 ? mesh.BoundingBox().Min : Vec3d.Zero;

        for (int i = 0; i < nv; i++)
        {
            Vec3d p = mesh.Vertices[i];
            var (kx, ky, kz) = Key(p - origin, cell);
            int rep = -1;
            double tol2 = tol * tol;
            for (int dx = -1; dx <= 1 && rep < 0; dx++)
                for (int dy = -1; dy <= 1 && rep < 0; dy++)
                    for (int dz = -1; dz <= 1 && rep < 0; dz++)
                    {
                        if (!grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var list)) continue;
                        foreach (int j in list)
                            if ((mesh.Vertices[j] - p).LengthSquared <= tol2) { rep = j; break; }
                    }

            if (rep < 0)
            {
                rep = i;
                kept.Add(i);
                var key = (kx, ky, kz);
                if (!grid.TryGetValue(key, out var l)) { l = new List<int>(); grid[key] = l; }
                l.Add(i);
            }
            map[i] = rep;
        }

        // Compact indices
        var compact = new int[nv];
        var verts = new Vec3d[kept.Count];
        for (int i = 0; i < kept.Count; i++) compact[kept[i]] = i;
        for (int i = 0; i < nv; i++) map[i] = compact[map[i]];
        for (int i = 0; i < kept.Count; i++) verts[i] = mesh.Vertices[kept[i]];
        int welded = nv - kept.Count;

        // --- Reduce collapsed faces, drop degenerate ones (near-zero area)
        int degenerate = 0;
        double minArea = 1e-12 * avgEdge * avgEdge;
        var faces = new List<int[]>();
        foreach (var f in mesh.Faces)
        {
            // Welding can merge cyclically adjacent corners (a quad at a
            // sphere pole becomes a triangle); collapse those runs instead of
            // discarding the whole face, which would open a hole
            var nf = new List<int>(f.Length);
            for (int i = 0; i < f.Length; i++)
            {
                int v = map[f[i]];
                if (nf.Count > 0 && nf[nf.Count - 1] == v) continue;
                nf.Add(v);
            }
            while (nf.Count > 1 && nf[0] == nf[nf.Count - 1]) nf.RemoveAt(nf.Count - 1);

            bool bad = nf.Count < 3;
            for (int i = 0; i < nf.Count && !bad; i++)
                for (int j = i + 1; j < nf.Count && !bad; j++)
                    if (nf[i] == nf[j]) bad = true; // non-adjacent repeat: bow-tie

            var arr = nf.ToArray();
            if (!bad && PolygonArea(verts, arr) < minArea) bad = true;

            if (bad) { degenerate++; continue; }
            faces.Add(arr);
        }

        // --- Drop duplicate faces (same vertex set, either winding)
        int duplicate = 0;
        var seen = new HashSet<string>();
        var unique = new List<int[]>();
        foreach (var f in faces)
        {
            var sorted = (int[])f.Clone();
            Array.Sort(sorted);
            string key = string.Join(",", sorted);
            if (seen.Add(key)) unique.Add(f);
            else duplicate++;
        }

        // Each face's input winding, to count the faces reversed at the end
        var inputFaces = unique.Select(f => (int[])f.Clone()).ToList();

        if (options.UnifyWinding) Orient(unique, verts);

        // --- Slivers
        info.MinAngleBefore = MinTriangleAngle(verts, unique);
        double sliver = options.SliverAngle;
        var removedVertex = new bool[verts.Length];
        var faceAlive = unique.Select(_ => true).ToList();
        var collapsedTo = new Dictionary<int, int>();
        if (sliver > 0) RemoveSlivers(verts, unique, faceAlive, removedVertex, collapsedTo, Math.Min(sliver, 60.0), info);

        var outFaces = new List<int[]>();
        int flipped = 0;
        for (int i = 0; i < unique.Count; i++)
        {
            if (!faceAlive[i]) continue;
            outFaces.Add(unique[i]);
            // faces rebuilt by a cap flip are new faces and are not compared
            if (SameCycle(unique[i], inputFaces[i]) == -1) flipped++;
        }
        info.FlippedFaces = flipped;

        // --- Isolated vertices (never used, or collapsed away)
        var used = new bool[verts.Length];
        foreach (var f in outFaces) foreach (int v in f) used[v] = true;
        var remap = new int[verts.Length];
        var finalVerts = new List<Vec3d>(verts.Length);
        int isolated = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            if (!used[i] && (options.RemoveIsolatedVertices || removedVertex[i]))
            {
                remap[i] = -1;
                if (!removedVertex[i]) isolated++;
                continue;
            }
            remap[i] = finalVerts.Count;
            finalVerts.Add(verts[i]);
        }
        info.IsolatedVertices = isolated;
        // A collapsed vertex maps to where it was collapsed to (chains follow to the survivor)
        for (int i = 0; i < verts.Length; i++)
        {
            if (!removedVertex[i]) continue;
            int k = i, guard = 0;
            while (removedVertex[k] && collapsedTo.TryGetValue(k, out int next) && guard++ < verts.Length) k = next;
            remap[i] = remap[k];
        }
        for (int i = 0; i < nv; i++) map[i] = remap[map[i]];
        var finalFaces = outFaces.Select(f => f.Select(v => remap[v]).ToArray()).ToArray();
        var result = new MeshData(finalVerts.ToArray(), finalFaces);

        // --- What is left
        Census(result, info);
        info.MinAngleAfter = MinTriangleAngle(result.Vertices, finalFaces);
        info.SliversLeft = CountSlivers(result.Vertices, finalFaces, sliver > 0 ? sliver : ReportSliverAngle);
        info.Report = BuildReport(welded, degenerate, duplicate, info, sliver, options.UnifyWinding);

        return new Result(result, welded, degenerate, duplicate, map, info);
    }

    private static (long, long, long) Key(Vec3d p, double cell) =>
        ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));

    private static double PolygonArea(Vec3d[] verts, int[] face) => 0.5 * VectorArea(verts, face).Length;

    private static Vec3d VectorArea(Vec3d[] verts, int[] face)
    {
        Vec3d n = Vec3d.Zero;
        Vec3d a = verts[face[0]];
        for (int i = 1; i < face.Length - 1; i++)
            n = n + Vec3d.Cross(verts[face[i]] - a, verts[face[i + 1]] - a);
        return n;
    }

    /// <summary>+1 when b is a rotation of a, −1 when it is a rotation of a reversed, 0 otherwise.</summary>
    private static int SameCycle(int[] a, int[] b)
    {
        if (a.Length != b.Length) return 0;
        int n = a.Length;
        int s = Array.IndexOf(b, a[0]);
        if (s < 0) return 0;
        bool fwd = true, back = true;
        for (int i = 0; i < n; i++)
        {
            if (b[(s + i) % n] != a[i]) fwd = false;
            if (b[((s - i) % n + n) % n] != a[i]) back = false;
        }
        return fwd ? 1 : back ? -1 : 0;
    }

    /// <summary>
    /// Mixing hash for edge keys: long.GetHashCode XOR-folds its halves, so the key
    /// (a &lt;&lt; 32 | b) would hash to a ^ b and neighbouring edges would collide.
    /// </summary>
    private sealed class EdgeHash : IEqualityComparer<long>
    {
        public static readonly EdgeHash Instance = new EdgeHash();
        public bool Equals(long x, long y) => x == y;
        public int GetHashCode(long x)
        {
            ulong z = unchecked((ulong)x * 0x9E3779B97F4A7C15UL);
            z ^= z >> 29;
            return unchecked((int)(z ^ (z >> 32)));
        }
    }

    private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // ------------------------------------------------------------------ orientation

    /// <summary>
    /// Makes the winding consistent across manifold edges, then turns every
    /// closed part outward and every open part to the winding most of its
    /// input face area had. Non-manifold edges are not crossed.
    /// </summary>
    internal static void Orient(List<int[]> faces, Vec3d[] verts)
    {
        int nf = faces.Count;
        var edgeUse = new Dictionary<long, List<int>>(EdgeHash.Instance);
        for (int fi = 0; fi < nf; fi++)
        {
            var f = faces[fi];
            for (int i = 0; i < f.Length; i++)
            {
                long key = EdgeKey(f[i], f[(i + 1) % f.Length]);
                if (!edgeUse.TryGetValue(key, out var list)) { list = new List<int>(2); edgeUse[key] = list; }
                list.Add(fi);
            }
        }

        var original = faces.Select(f => (int[])f.Clone()).ToArray();
        var comp = new int[nf];
        for (int i = 0; i < nf; i++) comp[i] = -1;
        var queue = new Queue<int>();
        var members = new List<List<int>>();
        for (int start = 0; start < nf; start++)
        {
            if (comp[start] >= 0) continue;
            int c = members.Count;
            var list = new List<int>();
            comp[start] = c;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int fi = queue.Dequeue();
                list.Add(fi);
                var f = faces[fi];
                for (int i = 0; i < f.Length; i++)
                {
                    int a = f[i], b = f[(i + 1) % f.Length];
                    var uses = edgeUse[EdgeKey(a, b)];
                    if (uses.Count != 2) continue; // border or non-manifold: do not cross
                    int other = uses[0] == fi ? uses[1] : uses[0];
                    if (other == fi || comp[other] >= 0) continue;
                    comp[other] = c;
                    // The neighbour must run b→a; if it runs a→b, reverse it
                    if (RunsForward(faces[other], a, b)) Array.Reverse(faces[other]);
                    queue.Enqueue(other);
                }
            }
            members.Add(list);
        }

        foreach (var list in members)
        {
            bool closed = true;
            foreach (int fi in list)
            {
                var f = faces[fi];
                for (int i = 0; i < f.Length && closed; i++)
                {
                    int a = f[i], b = f[(i + 1) % f.Length];
                    var uses = edgeUse[EdgeKey(a, b)];
                    if (uses.Count != 2) closed = false;
                    else
                    {
                        int other = uses[0] == fi ? uses[1] : uses[0];
                        if (RunsForward(faces[other], a, b)) closed = false; // non-orientable
                    }
                }
                if (!closed) break;
            }

            bool reverse;
            if (closed)
            {
                Vec3d centre = Vec3d.Zero;
                int cnt = 0;
                foreach (int fi in list) foreach (int v in faces[fi]) { centre = centre + verts[v]; cnt++; }
                centre = centre / Math.Max(1, cnt);
                double vol = 0;
                foreach (int fi in list)
                {
                    var f = faces[fi];
                    Vec3d p0 = verts[f[0]] - centre;
                    for (int i = 1; i < f.Length - 1; i++)
                        vol += Vec3d.Dot(p0, Vec3d.Cross(verts[f[i]] - centre, verts[f[i + 1]] - centre));
                }
                reverse = vol < 0;
            }
            else
            {
                double keepArea = 0, flipArea = 0;
                foreach (int fi in list)
                {
                    double area = PolygonArea(verts, faces[fi]);
                    if (SameCycle(faces[fi], original[fi]) == -1) flipArea += area; else keepArea += area;
                }
                reverse = flipArea > keepArea;
            }
            if (reverse) foreach (int fi in list) Array.Reverse(faces[fi]);
        }
    }

    private static bool RunsForward(int[] f, int a, int b)
    {
        for (int i = 0; i < f.Length; i++)
            if (f[i] == a && f[(i + 1) % f.Length] == b) return true;
        return false;
    }

    /// <summary>
    /// BFS over face adjacency, flipping faces so shared edges are always
    /// traversed in opposite directions by the two adjacent faces.
    /// </summary>
    internal static void UnifyWinding(List<int[]> faces)
    {
        // Edge -> list of (face, direction the edge is used in)
        var edgeUse = new Dictionary<(int, int), List<(int face, int dir)>>();
        for (int fi = 0; fi < faces.Count; fi++)
        {
            var f = faces[fi];
            for (int i = 0; i < f.Length; i++)
            {
                int a = f[i], b = f[(i + 1) % f.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeUse.TryGetValue(key, out var list))
                {
                    list = new List<(int, int)>();
                    edgeUse[key] = list;
                }
                list.Add((fi, a < b ? 1 : -1));
            }
        }

        var visited = new bool[faces.Count];
        var queue = new Queue<int>();
        for (int start = 0; start < faces.Count; start++)
        {
            if (visited[start]) continue;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int fi = queue.Dequeue();
                var f = faces[fi];
                for (int i = 0; i < f.Length; i++)
                {
                    int a = f[i], b = f[(i + 1) % f.Length];
                    var key = a < b ? (a, b) : (b, a);
                    int myDir = a < b ? 1 : -1;
                    foreach (var (other, otherDir) in edgeUse[key])
                    {
                        if (other == fi || visited[other]) continue;
                        visited[other] = true;
                        // Same direction along the shared edge -> flip the neighbor
                        if (otherDir == myDir)
                            Array.Reverse(faces[other]);
                        queue.Enqueue(other);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ angles

    private static double MinTriangleAngle(Vec3d[] verts, IEnumerable<int[]> faces)
    {
        double min = double.NaN;
        foreach (var f in faces)
        {
            if (f.Length != 3) continue;
            double a = MinAngle(verts[f[0]], verts[f[1]], verts[f[2]]);
            if (double.IsNaN(min) || a < min) min = a;
        }
        return min;
    }

    private static int CountSlivers(Vec3d[] verts, IEnumerable<int[]> faces, double deg)
    {
        int n = 0;
        foreach (var f in faces)
            if (f.Length == 3 && MinAngle(verts[f[0]], verts[f[1]], verts[f[2]]) < deg) n++;
        return n;
    }

    /// <summary>Corner angles in degrees at p0, p1, p2 (atan2 form, accurate near 0° and 180°).</summary>
    internal static (double A0, double A1, double A2) Angles(Vec3d p0, Vec3d p1, Vec3d p2) =>
        (Corner(p1 - p0, p2 - p0), Corner(p2 - p1, p0 - p1), Corner(p0 - p2, p1 - p2));

    private static double Corner(Vec3d u, Vec3d v) =>
        Math.Atan2(Vec3d.Cross(u, v).Length, Vec3d.Dot(u, v)) * 180.0 / Math.PI;

    private static double MinAngle(Vec3d p0, Vec3d p1, Vec3d p2)
    {
        var (a, b, c) = Angles(p0, p1, p2);
        return Math.Min(a, Math.Min(b, c));
    }

    // ------------------------------------------------------------------ slivers

    /// <summary>A sliver whose longest edge is at least this many times its shortest is a needle (CGAL's default).</summary>
    public const double NeedleRatio = 4.0;

    private static void RemoveSlivers(Vec3d[] verts, List<int[]> faces, List<bool> alive, bool[] removedVertex,
        Dictionary<int, int> collapsedTo, double sliverDeg, Diagnostics info)
    {
        int nv = verts.Length;
        for (int pass = 0; pass < 50; pass++)
        {
            // Adjacency of the current mesh
            var vf = new List<int>[nv];
            for (int i = 0; i < nv; i++) vf[i] = new List<int>();
            var edgeFaces = new Dictionary<long, List<int>>(EdgeHash.Instance);
            for (int fi = 0; fi < faces.Count; fi++)
            {
                if (!alive[fi]) continue;
                var f = faces[fi];
                foreach (int v in f) vf[v].Add(fi);
                for (int i = 0; i < f.Length; i++)
                {
                    long key = EdgeKey(f[i], f[(i + 1) % f.Length]);
                    if (!edgeFaces.TryGetValue(key, out var l)) { l = new List<int>(2); edgeFaces[key] = l; }
                    l.Add(fi);
                }
            }

            var candidates = new List<(double min, int face)>();
            for (int fi = 0; fi < faces.Count; fi++)
            {
                if (!alive[fi] || faces[fi].Length != 3) continue;
                var f = faces[fi];
                double m = MinAngle(verts[f[0]], verts[f[1]], verts[f[2]]);
                if (m < sliverDeg) candidates.Add((m, fi));
            }
            if (candidates.Count == 0) return;
            candidates.Sort((x, y) => x.min != y.min ? x.min.CompareTo(y.min) : x.face.CompareTo(y.face));

            var touched = new bool[nv];
            bool changed = false;
            foreach (var (minAng, fi) in candidates)
            {
                if (!alive[fi]) continue;
                var f = faces[fi];
                // adjacency around an edited vertex is stale until the next pass
                if (touched[f[0]] || touched[f[1]] || touched[f[2]]) { changed = true; continue; }

                var ang = Angles(verts[f[0]], verts[f[1]], verts[f[2]]);
                double[] a = { ang.A0, ang.A1, ang.A2 };
                int iMin = 0, iMax = 0;
                for (int i = 1; i < 3; i++) { if (a[i] < a[iMin]) iMin = i; if (a[i] > a[iMax]) iMax = i; }
                if (iMin == iMax) continue;
                // edge lengths are proportional to the sines of the opposite angles
                double ratio = Math.Sin(a[iMax] * Math.PI / 180.0) / Math.Max(1e-300, Math.Sin(a[iMin] * Math.PI / 180.0));

                bool done;
                if (ratio < NeedleRatio)
                    done = TryFlipOrDropCap(verts, faces, alive, vf, edgeFaces, fi, f[iMax], minAng, info, touched);
                else
                    done = TryCollapse(verts, faces, alive, vf, edgeFaces, f[(iMin + 1) % 3], f[(iMin + 2) % 3],
                        minAng, info, touched, removedVertex, collapsedTo);
                if (done) changed = true;
            }
            if (!changed) return;
        }
    }

    private static bool IsBorderVertex(int v, List<int>[] vf, Dictionary<long, List<int>> edgeFaces, List<int[]> faces)
    {
        foreach (int fi in vf[v])
        {
            var f = faces[fi];
            int i = Array.IndexOf(f, v);
            int prev = f[(i + f.Length - 1) % f.Length], next = f[(i + 1) % f.Length];
            if (edgeFaces[EdgeKey(v, prev)].Count != 2 || edgeFaces[EdgeKey(v, next)].Count != 2) return true;
        }
        return false;
    }

    private static HashSet<int> Neighbours(int v, List<int>[] vf, List<int[]> faces)
    {
        var s = new HashSet<int>();
        foreach (int fi in vf[v]) foreach (int x in faces[fi]) if (x != v) s.Add(x);
        return s;
    }

    /// <summary>Sum of squared distances of p to the planes of the faces around v (Garland–Heckbert).</summary>
    private static double QuadricError(Vec3d[] verts, List<int[]> faces, List<int>[] vf, int v, Vec3d p)
    {
        double e = 0;
        foreach (int fi in vf[v])
        {
            var f = faces[fi];
            Vec3d n = VectorArea(verts, f);
            double len = n.Length;
            if (len < 1e-300) continue;
            double d = Vec3d.Dot(n / len, p - verts[f[0]]);
            e += d * d;
        }
        return e;
    }

    private static bool TryCollapse(Vec3d[] verts, List<int[]> faces, List<bool> alive, List<int>[] vf,
        Dictionary<long, List<int>> edgeFaces, int u, int w, double oldMin, Diagnostics info, bool[] touched,
        bool[] removedVertex, Dictionary<int, int> collapsedTo)
    {
        if (!edgeFaces.TryGetValue(EdgeKey(u, w), out var shared) || shared.Count > 2) return false;
        foreach (int fi in vf[u]) if (faces[fi].Length != 3) return false;
        foreach (int fi in vf[w]) if (faces[fi].Length != 3) return false;

        bool bu = IsBorderVertex(u, vf, edgeFaces, faces), bw = IsBorderVertex(w, vf, edgeFaces, faces);
        bool borderEdge = shared.Count == 1;
        if (bu && bw && !borderEdge) return false; // would pinch two border stretches together

        // Link condition Lk u ∩ Lk w = Lk uw, with a dummy vertex ω (−1) joined to every border edge
        var lu = Neighbours(u, vf, faces);
        var lw = Neighbours(w, vf, faces);
        lu.Remove(w); lw.Remove(u);
        var linkEdge = new HashSet<int>();
        foreach (int fi in shared) foreach (int x in faces[fi]) if (x != u && x != w) linkEdge.Add(x);
        var common = new HashSet<int>(lu);
        common.IntersectWith(lw);
        if (bu && bw) common.Add(-1);
        var expected = new HashSet<int>(linkEdge);
        if (borderEdge) expected.Add(-1);
        if (!common.SetEquals(expected)) return false;
        // ... and no common link edge: x–y in a triangle with u and with w (a tetrahedron would
        // collapse), or x–ω (u–x and w–x both border edges: the ear would be cut off)
        foreach (int x in linkEdge)
        {
            if (edgeFaces[EdgeKey(u, x)].Count == 1 && edgeFaces[EdgeKey(w, x)].Count == 1) return false;
            foreach (int y in linkEdge)
                if (x < y && vf[u].Any(fi => faces[fi].Contains(x) && faces[fi].Contains(y)) &&
                    vf[w].Any(fi => faces[fi].Contains(x) && faces[fi].Contains(y))) return false;
        }

        // Which end goes: a border end stays; otherwise the smaller plane error; then the older vertex stays
        int remove;
        if (bu != bw) remove = bu ? w : u;
        else
        {
            double eRemoveW = QuadricError(verts, faces, vf, w, verts[u]); // w moved onto u
            double eRemoveU = QuadricError(verts, faces, vf, u, verts[w]);
            double scale = (verts[u] - verts[w]).LengthSquared;
            remove = Math.Abs(eRemoveW - eRemoveU) > 1e-9 * scale ? (eRemoveW < eRemoveU ? w : u) : Math.Max(u, w);
        }
        int keep = remove == u ? w : u;

        // Faces around `remove` that survive: no fold, and better than the sliver
        foreach (int fi in vf[remove])
        {
            var f = faces[fi];
            if (f.Contains(keep)) continue;
            Vec3d nOld = VectorArea(verts, f);
            var g = f.Select(x => x == remove ? keep : x).ToArray();
            Vec3d nNew = VectorArea(verts, g);
            if (Vec3d.Dot(nOld, nNew) <= 0) return false;
            if (MinAngle(verts[g[0]], verts[g[1]], verts[g[2]]) <= oldMin) return false;
        }

        foreach (int x in Neighbours(remove, vf, faces)) touched[x] = true;
        foreach (int fi in vf[remove])
        {
            var f = faces[fi];
            if (f.Contains(keep)) { alive[fi] = false; continue; }
            for (int i = 0; i < 3; i++) if (f[i] == remove) f[i] = keep;
        }
        removedVertex[remove] = true;
        collapsedTo[remove] = keep;
        touched[remove] = touched[keep] = true;
        info.NeedlesCollapsed++;
        return true;
    }

    private static bool TryFlipOrDropCap(Vec3d[] verts, List<int[]> faces, List<bool> alive, List<int>[] vf,
        Dictionary<long, List<int>> edgeFaces, int fi, int apex, double oldMin, Diagnostics info, bool[] touched)
    {
        var f = faces[fi];
        int ia = Array.IndexOf(f, apex);
        int p = f[(ia + 1) % 3], q = f[(ia + 2) % 3]; // f = (apex, p, q) cyclically, long edge p→q
        var uses = edgeFaces[EdgeKey(p, q)];
        if (uses.Count == 1)
        {
            // Border cap: drop the face when the apex is interior, so the border runs p → apex → q
            if (IsBorderVertex(apex, vf, edgeFaces, faces)) return false;
            alive[fi] = false;
            touched[apex] = touched[p] = touched[q] = true;
            info.CapsRemoved++;
            return true;
        }
        if (uses.Count != 2) return false;
        int gi = uses[0] == fi ? uses[1] : uses[0];
        var g = faces[gi];
        if (g.Length != 3) return false;
        int d = g.First(x => x != p && x != q);
        if (d == apex || edgeFaces.ContainsKey(EdgeKey(apex, d))) return false;

        var t1 = new[] { apex, p, d };
        var t2 = new[] { apex, d, q };
        Vec3d n1 = VectorArea(verts, f), n2 = VectorArea(verts, g);
        Vec3d m1 = VectorArea(verts, t1), m2 = VectorArea(verts, t2);
        Vec3d nRef = n1 + n2;
        if (Vec3d.Dot(m1, nRef) <= 0 || Vec3d.Dot(m2, nRef) <= 0) return false; // the quad would fold
        double before = Math.Min(oldMin, MinAngle(verts[g[0]], verts[g[1]], verts[g[2]]));
        double after = Math.Min(MinAngle(verts[t1[0]], verts[t1[1]], verts[t1[2]]),
                                MinAngle(verts[t2[0]], verts[t2[1]], verts[t2[2]]));
        if (after <= before) return false;

        faces[fi] = t1;
        faces[gi] = t2;
        touched[apex] = touched[p] = touched[q] = touched[d] = true;
        info.CapsFlipped++;
        return true;
    }

    // ------------------------------------------------------------------ census and report

    private static void Census(MeshData mesh, Diagnostics info)
    {
        var edgeUse = new Dictionary<long, (int count, int dirSum, int a, int b)>(EdgeHash.Instance);
        foreach (var f in mesh.Faces)
            for (int i = 0; i < f.Length; i++)
            {
                int a = f[i], b = f[(i + 1) % f.Length];
                long key = EdgeKey(a, b);
                edgeUse.TryGetValue(key, out var e);
                edgeUse[key] = (e.count + 1, e.dirSum + (a < b ? 1 : -1), Math.Min(a, b), Math.Max(a, b));
            }
        var nonManifold = new List<(int, int)>();
        int conflicts = 0;
        var borderNext = new Dictionary<int, List<int>>();
        foreach (var e in edgeUse.Values)
        {
            if (e.count > 2) nonManifold.Add((e.a, e.b));
            else if (e.count == 2 && e.dirSum != 0) conflicts++;
            else if (e.count == 1)
            {
                if (!borderNext.TryGetValue(e.a, out var la)) { la = new List<int>(); borderNext[e.a] = la; }
                if (!borderNext.TryGetValue(e.b, out var lb)) { lb = new List<int>(); borderNext[e.b] = lb; }
                la.Add(e.b); lb.Add(e.a);
            }
        }
        nonManifold.Sort();
        info.NonManifoldEdges = nonManifold.ToArray();
        info.OrientationConflicts = conflicts;

        // Border loops: components of the border-edge graph (a pinched figure eight counts once)
        var seen = new HashSet<int>();
        int loops = 0;
        foreach (int s in borderNext.Keys)
        {
            if (!seen.Add(s)) continue;
            loops++;
            var stack = new Stack<int>();
            stack.Push(s);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                foreach (int x in borderNext[v]) if (seen.Add(x)) stack.Push(x);
            }
        }
        info.BorderLoops = loops;

        // Parts over shared edges; closed = no border edge and no non-manifold edge
        int nf = mesh.FaceCount;
        var parent = new int[nf];
        for (int i = 0; i < nf; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var firstFace = new Dictionary<long, int>(EdgeHash.Instance);
        var openFace = new bool[nf];
        for (int fi = 0; fi < nf; fi++)
        {
            var f = mesh.Faces[fi];
            for (int i = 0; i < f.Length; i++)
            {
                long key = EdgeKey(f[i], f[(i + 1) % f.Length]);
                if (edgeUse[key].count != 2) openFace[fi] = true;
                if (firstFace.TryGetValue(key, out int g)) parent[Find(fi)] = Find(g);
                else firstFace[key] = fi;
            }
        }
        var partOpen = new Dictionary<int, bool>();
        for (int fi = 0; fi < nf; fi++)
        {
            int r = Find(fi);
            partOpen[r] = (partOpen.TryGetValue(r, out bool o) && o) || openFace[fi];
        }
        info.Components = partOpen.Count;
        info.ClosedComponents = partOpen.Values.Count(o => !o);
    }

    private static string BuildReport(int welded, int degenerate, int duplicate, Diagnostics info, double sliver, bool unify)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void Line(string s) { if (sb.Length > 0) sb.Append('\n'); sb.Append(s); }
        if (welded > 0) Line(string.Format(ci, "Welded {0} vertices.", welded));
        if (degenerate > 0) Line(string.Format(ci, "Removed {0} degenerate (zero-area or collapsed) faces.", degenerate));
        if (duplicate > 0) Line(string.Format(ci, "Removed {0} duplicate faces.", duplicate));
        if (info.IsolatedVertices > 0)
            Line(string.Format(ci, "Dropped {0} isolated vertices (no face used them; their Map entry is -1).", info.IsolatedVertices));
        if (info.FlippedFaces > 0)
            Line(string.Format(ci, "Reversed {0} faces so the winding agrees; closed parts face outward, open parts keep the winding most of their area had.",
                info.FlippedFaces));
        if (info.NeedlesCollapsed + info.CapsFlipped + info.CapsRemoved > 0)
            Line(string.Format(ci, "Slivers below {0:0.##}°: collapsed {1} needles, flipped {2} caps, dropped {3} border caps; smallest angle {4:0.###}° → {5:0.###}°.",
                sliver, info.NeedlesCollapsed, info.CapsFlipped, info.CapsRemoved, info.MinAngleBefore, info.MinAngleAfter));
        if (info.SliversLeft > 0)
        {
            if (sliver > 0)
                Line(string.Format(ci, "{0} triangles are still below {1:0.##}°: removing them would fold the mesh or change its topology. Remesh that area.",
                    info.SliversLeft, sliver));
            else
                Line(string.Format(ci, "{0} triangles have an angle below {1:0.##}° (smallest {2:0.###}°): set Sliver to {1:0.##} to collapse or flip them.",
                    info.SliversLeft, ReportSliverAngle, info.MinAngleAfter));
        }
        if (info.NonManifoldEdges.Length > 0)
            Line(string.Format(ci, "{0} edges are shared by more than two faces (non-manifold, see the NonManifold output): split the mesh there or delete the extra faces.",
                info.NonManifoldEdges.Length));
        if (unify && info.OrientationConflicts > 0)
            Line(string.Format(ci, "{0} edges cannot be oriented consistently (a Möbius-like part): the normals flip across them.",
                info.OrientationConflicts));
        if (info.Components > 1) Line(string.Format(ci, "{0} separate parts ({1} closed).", info.Components, info.ClosedComponents));
        if (sb.Length == 0) Line("Clean: nothing to repair.");
        return sb.ToString();
    }
}
