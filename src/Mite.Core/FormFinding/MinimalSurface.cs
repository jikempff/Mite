using System;
using Mite.Core.Geometry;

namespace Mite.Core.FormFinding;

public static class MinimalSurface
{
    public class Options
    {
        public int MaxIterations { get; set; } = 100;
        public double StepSize { get; set; } = 0.5;
        public double Tolerance { get; set; } = 1e-8;
    }

    public readonly struct Result
    {
        public readonly Vec3d[] Vertices;
        public readonly int Iterations;
        public readonly double Residual;

        public Result(Vec3d[] vertices, int iterations, double residual)
        {
            Vertices = vertices; Iterations = iterations; Residual = residual;
        }
    }

    /// <summary>
    /// Finds a minimal surface by flowing interior vertices via the cotangent Laplacian
    /// while keeping boundary/fixed vertices pinned.
    /// </summary>
    public static Result Compute(MeshData mesh, bool[] fixedVertices, Options? options = null)
    {
        options ??= new Options();
        var triMesh = mesh.ToTriangulated();
        var verts = new Vec3d[triMesh.VertexCount];
        Array.Copy(triMesh.Vertices, verts, triMesh.VertexCount);

        int iter;
        double residual = double.MaxValue;

        for (iter = 0; iter < options.MaxIterations; iter++)
        {
            var laplacian = ComputeCotangentLaplacian(triMesh, verts);

            residual = 0;
            for (int i = 0; i < triMesh.VertexCount; i++)
            {
                if (fixedVertices[i]) continue;
                residual = Math.Max(residual, laplacian[i].Length);
                verts[i] = verts[i] + options.StepSize * laplacian[i];
            }

            if (residual < options.Tolerance) { iter++; break; }
        }

        return new Result(verts, iter, residual);
    }

    private static Vec3d[] ComputeCotangentLaplacian(MeshData mesh, Vec3d[] verts)
    {
        int nv = verts.Length;
        var lap = new Vec3d[nv];
        var weights = new double[nv];

        foreach (var f in mesh.Faces)
        {
            for (int j = 0; j < 3; j++)
            {
                int i0 = f[j];
                int i1 = f[(j + 1) % 3];
                int i2 = f[(j + 2) % 3];

                Vec3d e1 = verts[i0] - verts[i2];
                Vec3d e2 = verts[i1] - verts[i2];
                double area = Vec3d.Cross(e1, e2).Length;
                if (area < 1e-15) continue;

                double cosA = Vec3d.Dot(e1, e2) / (e1.Length * e2.Length);
                double sinA = Math.Sqrt(Math.Max(0, 1.0 - cosA * cosA));
                double cotA = cosA / (sinA + 1e-15);

                Vec3d diff = verts[i1] - verts[i0];
                lap[i0] = lap[i0] + cotA * diff;
                lap[i1] = lap[i1] - cotA * diff;
                weights[i0] += cotA;
                weights[i1] += cotA;
            }
        }

        for (int i = 0; i < nv; i++)
        {
            if (weights[i] > 1e-15)
                lap[i] = lap[i] / weights[i];
        }

        return lap;
    }
}
