using System;

namespace Mite.Core.Geometry;

/// <summary>
/// Shared discrete differential operators on triangle meshes (Meyer et al.
/// 2003). Every routine here guards against degenerate triangles: a sliver
/// with a collinear corner has a cotangent that tends to infinity, which
/// would otherwise leak values of 1e29 into curvature results.
/// </summary>
public static class DiscreteOperators
{
    /// <summary>Relative area threshold below which a triangle is ignored.</summary>
    public const double DegenerateAreaRatio = 1e-10;

    /// <summary>
    /// Cotangent of the angle between two edge vectors leaving a corner,
    /// computed as dot/|cross| (no acos/sin round trip). Returns 0 for
    /// degenerate corners.
    /// </summary>
    public static double Cot(Vec3d u, Vec3d v)
    {
        double cross = Vec3d.Cross(u, v).Length;
        double scale = Math.Sqrt(u.LengthSquared * v.LengthSquared);
        if (scale < 1e-300 || cross < 1e-8 * scale) return 0.0;
        return Vec3d.Dot(u, v) / cross;
    }

    /// <summary>True when the triangle has (near) zero area relative to its edge lengths.</summary>
    public static bool IsDegenerate(Vec3d p0, Vec3d p1, Vec3d p2)
    {
        Vec3d e1 = p1 - p0, e2 = p2 - p0;
        double area2 = Vec3d.Cross(e1, e2).LengthSquared;
        double scale = e1.LengthSquared * e2.LengthSquared;
        return scale < 1e-300 || area2 < DegenerateAreaRatio * DegenerateAreaRatio * scale;
    }

    /// <summary>
    /// Per-vertex mixed Voronoi areas (Voronoi region for non-obtuse triangles,
    /// half / quarter of the face area at / opposite an obtuse corner).
    /// The mesh must be triangulated.
    /// </summary>
    public static double[] MixedVoronoiAreas(MeshData tri)
    {
        var areas = new double[tri.VertexCount];
        foreach (var f in tri.Faces)
        {
            if (f.Length != 3) continue;
            Vec3d p0 = tri.Vertices[f[0]], p1 = tri.Vertices[f[1]], p2 = tri.Vertices[f[2]];
            if (IsDegenerate(p0, p1, p2)) continue;

            Vec3d e01 = p1 - p0, e02 = p2 - p0;
            Vec3d e10 = p0 - p1, e12 = p2 - p1;
            Vec3d e20 = p0 - p2, e21 = p1 - p2;

            double faceArea = Vec3d.Cross(e01, e02).Length * 0.5;
            double cot0 = Cot(e01, e02), cot1 = Cot(e10, e12), cot2 = Cot(e20, e21);
            bool obtuse0 = Vec3d.Dot(e01, e02) < 0;
            bool obtuse1 = Vec3d.Dot(e10, e12) < 0;
            bool obtuse2 = Vec3d.Dot(e20, e21) < 0;

            if (!obtuse0 && !obtuse1 && !obtuse2)
            {
                areas[f[0]] += (e01.LengthSquared * cot2 + e02.LengthSquared * cot1) / 8.0;
                areas[f[1]] += (e10.LengthSquared * cot2 + e12.LengthSquared * cot0) / 8.0;
                areas[f[2]] += (e20.LengthSquared * cot1 + e21.LengthSquared * cot0) / 8.0;
            }
            else if (obtuse0) { areas[f[0]] += faceArea / 2; areas[f[1]] += faceArea / 4; areas[f[2]] += faceArea / 4; }
            else if (obtuse1) { areas[f[0]] += faceArea / 4; areas[f[1]] += faceArea / 2; areas[f[2]] += faceArea / 4; }
            else { areas[f[0]] += faceArea / 4; areas[f[1]] += faceArea / 4; areas[f[2]] += faceArea / 2; }
        }
        return areas;
    }

    /// <summary>
    /// Cotangent Laplacian applied to the vertex positions,
    /// L_i = Σ_j (cot α_ij + cot β_ij) (x_i - x_j) (sign convention: points
    /// along the mean curvature normal for a convex vertex when divided by
    /// the negative area; callers fix the sign against the vertex normal).
    /// </summary>
    public static Vec3d[] CotangentLaplacian(MeshData tri)
    {
        var lap = new Vec3d[tri.VertexCount];
        foreach (var f in tri.Faces)
        {
            if (f.Length != 3) continue;
            Vec3d p0 = tri.Vertices[f[0]], p1 = tri.Vertices[f[1]], p2 = tri.Vertices[f[2]];
            if (IsDegenerate(p0, p1, p2)) continue;

            for (int j = 0; j < 3; j++)
            {
                int i0 = f[j], i1 = f[(j + 1) % 3], i2 = f[(j + 2) % 3];
                // Angle at i2, opposite edge (i0, i1)
                double cot = Cot(tri.Vertices[i0] - tri.Vertices[i2], tri.Vertices[i1] - tri.Vertices[i2]);
                Vec3d diff = tri.Vertices[i0] - tri.Vertices[i1];
                lap[i0] = lap[i0] + cot * diff;
                lap[i1] = lap[i1] - cot * diff;
            }
        }
        return lap;
    }

    /// <summary>
    /// Symmetric cotangent edge weights w_ij = (cot α + cot β) / 2 for the
    /// given edge list (as produced by <see cref="MeshData.BuildEdges"/>).
    /// Negative weights on obtuse configurations are clamped to a small
    /// positive value so the resulting Laplacian stays positive definite.
    /// </summary>
    public static double[] CotangentEdgeWeights(MeshData tri, (int v0, int v1)[] edges, double minWeight = 1e-6)
    {
        var index = new System.Collections.Generic.Dictionary<(int, int), int>(edges.Length);
        for (int e = 0; e < edges.Length; e++) index[edges[e]] = e;

        var w = new double[edges.Length];
        foreach (var f in tri.Faces)
        {
            if (f.Length != 3) continue;
            Vec3d p0 = tri.Vertices[f[0]], p1 = tri.Vertices[f[1]], p2 = tri.Vertices[f[2]];
            if (IsDegenerate(p0, p1, p2)) continue;
            for (int j = 0; j < 3; j++)
            {
                int i0 = f[j], i1 = f[(j + 1) % 3], i2 = f[(j + 2) % 3];
                double cot = Cot(tri.Vertices[i0] - tri.Vertices[i2], tri.Vertices[i1] - tri.Vertices[i2]);
                var key = i0 < i1 ? (i0, i1) : (i1, i0);
                if (index.TryGetValue(key, out int e)) w[e] += 0.5 * cot;
            }
        }
        for (int e = 0; e < w.Length; e++)
            if (w[e] < minWeight) w[e] = minWeight;
        return w;
    }
}
