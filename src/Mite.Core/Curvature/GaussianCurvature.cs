using System;
using Mite.Core.Geometry;

namespace Mite.Core.Curvature;

public static class GaussianCurvature
{
    /// <summary>
    /// Computes per-vertex Gaussian curvature using the angle deficit method.
    /// K_i = (2π - Σ angles_i) / A_i (mixed Voronoi area). Boundary vertices are
    /// set to zero: the 2π deficit only applies to full vertex stars, so open
    /// mesh edges would otherwise report huge spurious curvature. Degenerate
    /// (zero-area) triangles contribute neither angles nor area.
    /// </summary>
    public static double[] Compute(MeshData mesh)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        var boundary = triMesh.BuildBoundaryVertexFlags();
        var angleSum = new double[nv];
        var areas = DiscreteOperators.MixedVoronoiAreas(triMesh);

        foreach (var f in triMesh.Faces)
        {
            Vec3d p0 = triMesh.Vertices[f[0]];
            Vec3d p1 = triMesh.Vertices[f[1]];
            Vec3d p2 = triMesh.Vertices[f[2]];
            if (DiscreteOperators.IsDegenerate(p0, p1, p2)) continue;

            angleSum[f[0]] += AngleBetween(p1 - p0, p2 - p0);
            angleSum[f[1]] += AngleBetween(p0 - p1, p2 - p1);
            angleSum[f[2]] += AngleBetween(p0 - p2, p1 - p2);
        }

        var K = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            if (areas[i] > 1e-300 && !boundary[i])
                K[i] = (2.0 * Math.PI - angleSum[i]) / areas[i];
        }
        return K;
    }

    private static double AngleBetween(Vec3d a, Vec3d b)
    {
        double la2 = a.LengthSquared, lb2 = b.LengthSquared;
        if (la2 < 1e-300 || lb2 < 1e-300) return 0.0;
        double d = Vec3d.Dot(a, b) / Math.Sqrt(la2 * lb2);
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d)));
    }
}
