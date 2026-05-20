using System;
using MeshCurvKit.Core.Geometry;

namespace MeshCurvKit.Core.Curvature;

public static class MeanCurvature
{
    public readonly struct Result
    {
        public readonly double[] Values;
        public readonly Vec3d[] Normals;

        public Result(double[] values, Vec3d[] normals) { Values = values; Normals = normals; }
    }

    /// <summary>
    /// Computes per-vertex mean curvature via the cotangent Laplacian.
    /// H_i = |Δx_i| / (2 * A_i), where Δ is the cotangent-weight Laplacian.
    /// </summary>
    public static Result Compute(MeshData mesh)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        var laplacian = new Vec3d[nv];
        var areas = new double[nv];

        foreach (var f in triMesh.Faces)
        {
            for (int j = 0; j < 3; j++)
            {
                int i0 = f[j];
                int i1 = f[(j + 1) % 3];
                int i2 = f[(j + 2) % 3];

                Vec3d e1 = triMesh.Vertices[i0] - triMesh.Vertices[i2];
                Vec3d e2 = triMesh.Vertices[i1] - triMesh.Vertices[i2];
                double cosA = Vec3d.Dot(e1, e2) / (e1.Length * e2.Length + 1e-30);
                double sinA = Math.Sqrt(Math.Max(0, 1 - cosA * cosA));
                double cotA = cosA / (sinA + 1e-30);

                Vec3d diff = triMesh.Vertices[i0] - triMesh.Vertices[i1];
                laplacian[i0] = laplacian[i0] + cotA * diff;
                laplacian[i1] = laplacian[i1] - cotA * diff;
            }

            Vec3d v0 = triMesh.Vertices[f[0]];
            Vec3d v1 = triMesh.Vertices[f[1]];
            Vec3d v2 = triMesh.Vertices[f[2]];
            double faceArea = Vec3d.Cross(v1 - v0, v2 - v0).Length * 0.5;
            areas[f[0]] += faceArea / 3.0;
            areas[f[1]] += faceArea / 3.0;
            areas[f[2]] += faceArea / 3.0;
        }

        var H = new double[nv];
        var Hn = new Vec3d[nv];
        var vertexNormals = triMesh.ComputeVertexNormals();

        for (int i = 0; i < nv; i++)
        {
            if (areas[i] < 1e-15) continue;
            Vec3d hn = laplacian[i] / (2.0 * areas[i]);
            double sign = Vec3d.Dot(hn, vertexNormals[i]) > 0 ? 1.0 : -1.0;
            H[i] = sign * hn.Length;
            Hn[i] = hn;
        }

        return new Result(H, Hn);
    }
}
