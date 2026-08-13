using System;
using System.Collections.Generic;

namespace Mite.Core.Geometry;

/// <summary>
/// Mesh healing: weld coincident vertices, drop degenerate and duplicate
/// faces, unify face winding. Most Mite algorithms assume a clean manifold
/// mesh; Rhino meshes in the wild routinely have duplicate vertices (from
/// joins/explodes), zero-area faces, and mixed winding.
/// </summary>
public static class MeshCleanup
{
    public readonly struct Result
    {
        public readonly MeshData Mesh;
        public readonly int WeldedVertices;
        public readonly int RemovedDegenerateFaces;
        public readonly int RemovedDuplicateFaces;

        public Result(MeshData mesh, int welded, int degenerate, int duplicate)
        {
            Mesh = mesh;
            WeldedVertices = welded;
            RemovedDegenerateFaces = degenerate;
            RemovedDuplicateFaces = duplicate;
        }
    }

    /// <summary>
    /// Cleans a mesh. weldTolerance 0 selects an automatic value (1e-6 of the
    /// average edge length). Faces are kept as polygons; only triangles are
    /// considered for the area-based degenerate test after triangulation-free
    /// polygon area computation.
    /// </summary>
    public static Result Compute(MeshData mesh, double weldTolerance = 0.0, bool unifyWinding = true)
    {
        int nv = mesh.VertexCount;

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
            if (count > 0) avgEdge = sum / count;
        }
        double tol = weldTolerance > 0 ? weldTolerance : 1e-6 * avgEdge;

        // --- Weld vertices: grid hash, first vertex in a cluster is the representative
        var map = new int[nv];
        var kept = new List<int>();
        var grid = new Dictionary<(int, int, int), List<int>>();
        double cell = Math.Max(tol, 1e-12);

        for (int i = 0; i < nv; i++)
        {
            Vec3d p = mesh.Vertices[i];
            var (kx, ky, kz) = Key(p, cell);
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
        var newVerts = new Vec3d[kept.Count];
        for (int i = 0; i < kept.Count; i++) compact[kept[i]] = i;
        for (int i = 0; i < nv; i++) map[i] = compact[map[i]];
        for (int i = 0; i < kept.Count; i++) newVerts[i] = mesh.Vertices[kept[i]];
        int welded = nv - kept.Count;

        // --- Drop degenerate faces (repeated vertices or near-zero area)
        int degenerate = 0;
        double minArea = 1e-12 * avgEdge * avgEdge;
        var faces = new List<int[]>();
        foreach (var f in mesh.Faces)
        {
            var nf = new int[f.Length];
            for (int i = 0; i < f.Length; i++) nf[i] = map[f[i]];

            bool bad = false;
            for (int i = 0; i < nf.Length && !bad; i++)
                for (int j = i + 1; j < nf.Length && !bad; j++)
                    if (nf[i] == nf[j]) bad = true;

            if (!bad && nf.Length >= 3 && PolygonArea(newVerts, nf) < minArea) bad = true;

            if (bad && f.Length >= 3) { degenerate++; continue; }
            if (nf.Length < 3) { degenerate++; continue; }
            faces.Add(nf);
        }

        // --- Drop duplicate faces (same vertex set)
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

        if (unifyWinding) UnifyWinding(unique);

        return new Result(new MeshData(newVerts, unique.ToArray()), welded, degenerate, duplicate);
    }

    private static (int, int, int) Key(Vec3d p, double cell) =>
        ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));

    private static double PolygonArea(Vec3d[] verts, int[] face)
    {
        Vec3d n = Vec3d.Zero;
        Vec3d a = verts[face[0]];
        for (int i = 1; i < face.Length - 1; i++)
            n = n + Vec3d.Cross(verts[face[i]] - a, verts[face[i + 1]] - a);
        return 0.5 * n.Length;
    }

    /// <summary>
    /// BFS over face adjacency, flipping faces so shared edges are always
    /// traversed in opposite directions by the two adjacent faces.
    /// </summary>
    private static void UnifyWinding(List<int[]> faces)
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
}
