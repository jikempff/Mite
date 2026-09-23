using System;
using Mite.Core.Geometry;

namespace Mite.Core.Curvature;

public static class MeanCurvature
{
    public readonly struct Result
    {
        /// <summary>Signed mean curvature H per vertex (positive when curving toward the vertex normal).</summary>
        public readonly double[] Values;

        /// <summary>Mean curvature normal vectors H·n per vertex (not unit length).</summary>
        public readonly Vec3d[] CurvatureNormals;

        /// <summary>Kept for API compatibility; same as <see cref="CurvatureNormals"/>.</summary>
        public Vec3d[] Normals => CurvatureNormals;

        public Result(double[] values, Vec3d[] curvatureNormals)
        {
            Values = values;
            CurvatureNormals = curvatureNormals;
        }
    }

    /// <summary>
    /// Computes per-vertex mean curvature via the cotangent Laplacian with
    /// mixed Voronoi areas (the same areas Gaussian curvature uses, so H and
    /// K are consistently normalized and H² − K is meaningful).
    /// The mean curvature normal is K_i = Δx_i / (2 A_i) with |K_i| = 2H,
    /// so H_i = |Δx_i| / (4 A_i). Boundary vertices are set to zero: the
    /// one-sided Laplacian there measures boundary shape, not surface curvature.
    /// Degenerate (zero-area) triangles are ignored.
    /// </summary>
    public static Result Compute(MeshData mesh)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        var laplacian = DiscreteOperators.CotangentLaplacian(triMesh);
        var areas = DiscreteOperators.MixedVoronoiAreas(triMesh);

        var H = new double[nv];
        var Hn = new Vec3d[nv];
        var vertexNormals = triMesh.ComputeVertexNormalsMax();
        var boundary = triMesh.BuildBoundaryVertexFlags();

        for (int i = 0; i < nv; i++)
        {
            if (areas[i] < 1e-300 || boundary[i]) continue;
            // laplacian/(2A) is the mean curvature normal, magnitude 2H
            Vec3d hn = laplacian[i] / (4.0 * areas[i]);
            if (double.IsNaN(hn.X) || double.IsInfinity(hn.X)) continue;
            double sign = Vec3d.Dot(hn, vertexNormals[i]) > 0 ? 1.0 : -1.0;
            H[i] = sign * hn.Length;
            Hn[i] = hn;
        }

        return new Result(H, Hn);
    }
}
