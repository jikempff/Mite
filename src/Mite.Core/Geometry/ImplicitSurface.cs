using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Geometry;

/// <summary>
/// Zero level set of an implicit function on a cubic grid by marching
/// tetrahedra (six tetrahedra per cell, Kuhn subdivision), with vertices
/// welded on grid edges and faces oriented consistently. Used for the triply
/// periodic minimal surfaces of the shape catalogue (Schwarz D, gyroid,
/// Schwarz P from their nodal approximations).
/// </summary>
public static class ImplicitSurface
{
    public static MeshData MarchingTetrahedra(Func<Vec3d, double> f, double lo, double hi, int cells)
    {
        cells = Math.Max(2, cells);
        int n = cells + 1;
        double h = (hi - lo) / cells;
        var val = new double[n * n * n];
        Vec3d G(int i, int j, int k) => new Vec3d(lo + i * h, lo + j * h, lo + k * h);
        int Id(int i, int j, int k) => (i * n + j) * n + k;
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) for (int k = 0; k < n; k++) val[Id(i, j, k)] = f(G(i, j, k));

        var verts = new List<Vec3d>();
        var edgeVert = new Dictionary<(int, int), int>();
        var faces = new List<int[]>();
        int VertexOnEdge(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (edgeVert.TryGetValue(key, out int id)) return id;
            double va = val[a], vb = val[b];
            double t = Math.Abs(vb - va) < 1e-15 ? 0.5 : va / (va - vb);
            int ia = a / (n * n), ja = (a / n) % n, ka = a % n;
            int ib = b / (n * n), jb = (b / n) % n, kb = b % n;
            Vec3d p = G(ia, ja, ka) + t * (G(ib, jb, kb) - G(ia, ja, ka));
            id = verts.Count; verts.Add(p); edgeVert[key] = id;
            return id;
        }
        int[][] tets = { new[] { 0, 1, 3, 7 }, new[] { 0, 1, 5, 7 }, new[] { 0, 2, 3, 7 }, new[] { 0, 2, 6, 7 }, new[] { 0, 4, 5, 7 }, new[] { 0, 4, 6, 7 } };
        for (int i = 0; i < cells; i++) for (int j = 0; j < cells; j++) for (int k = 0; k < cells; k++)
        {
            var c = new int[8];
            for (int b = 0; b < 8; b++) c[b] = Id(i + ((b >> 2) & 1), j + ((b >> 1) & 1), k + (b & 1));
            foreach (var t in tets)
            {
                var v = new[] { c[t[0]], c[t[1]], c[t[2]], c[t[3]] };
                var inside = new List<int>(); var outside = new List<int>();
                foreach (int id in v) (val[id] < 0 ? inside : outside).Add(id);
                if (inside.Count == 0 || outside.Count == 0) continue;
                if (inside.Count == 1 || outside.Count == 1)
                {
                    int apex = inside.Count == 1 ? inside[0] : outside[0];
                    var others = inside.Count == 1 ? outside : inside;
                    int a = VertexOnEdge(apex, others[0]), b2 = VertexOnEdge(apex, others[1]), c2 = VertexOnEdge(apex, others[2]);
                    faces.Add(inside.Count == 1 ? new[] { a, b2, c2 } : new[] { a, c2, b2 });
                }
                else
                {
                    int a = VertexOnEdge(inside[0], outside[0]), b2 = VertexOnEdge(inside[0], outside[1]);
                    int c2 = VertexOnEdge(inside[1], outside[1]), d2 = VertexOnEdge(inside[1], outside[0]);
                    faces.Add(new[] { a, b2, c2 });
                    faces.Add(new[] { a, c2, d2 });
                }
            }
        }
        // Weld the on-edge vertices that coincide where a grid value is exactly 0,
        // drop the slivers that produces and orient all faces consistently
        var raw = new MeshData(verts.ToArray(), faces.ToArray());
        return MeshCleanup.Compute(raw, 1e-9 * (hi - lo), unifyWinding: true).Mesh;
    }

    /// <summary>Nodal approximation of the Schwarz D (diamond) surface (Schnering &amp; Nesper 1991).</summary>
    public static double SchwarzD(Vec3d p) =>
        Math.Sin(p.X) * Math.Sin(p.Y) * Math.Sin(p.Z) + Math.Sin(p.X) * Math.Cos(p.Y) * Math.Cos(p.Z) +
        Math.Cos(p.X) * Math.Sin(p.Y) * Math.Cos(p.Z) + Math.Cos(p.X) * Math.Cos(p.Y) * Math.Sin(p.Z);

    /// <summary>Nodal approximation of the gyroid.</summary>
    public static double Gyroid(Vec3d p) =>
        Math.Sin(p.X) * Math.Cos(p.Y) + Math.Sin(p.Y) * Math.Cos(p.Z) + Math.Sin(p.Z) * Math.Cos(p.X);

    /// <summary>Nodal approximation of the Schwarz P surface.</summary>
    public static double SchwarzP(Vec3d p) => Math.Cos(p.X) + Math.Cos(p.Y) + Math.Cos(p.Z);
}
