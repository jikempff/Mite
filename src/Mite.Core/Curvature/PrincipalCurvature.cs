using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Mite.Core.Geometry;

namespace Mite.Core.Curvature;

public static class PrincipalCurvature
{
    public readonly struct Result
    {
        public readonly double[] K1;
        public readonly double[] K2;
        public readonly Vec3d[] D1;
        public readonly Vec3d[] D2;

        public Result(double[] k1, double[] k2, Vec3d[] d1, Vec3d[] d2)
        {
            K1 = k1; K2 = k2; D1 = d1; D2 = d2;
        }
    }

    /// <summary>
    /// Computes principal curvatures and directions via per-face shape operator averaging.
    /// Based on "Estimating Curvatures and Their Derivatives on Triangle Meshes" (Rusinkiewicz 2004).
    /// </summary>
    public static Result Compute(MeshData mesh, int radius = 2)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        int nf = triMesh.FaceCount;

        var vertexNormals = triMesh.ComputeVertexNormals();
        var vertexFaces = triMesh.BuildVertexFaces();

        var e1Basis = new Vec3d[nv];
        var e2Basis = new Vec3d[nv];
        for (int i = 0; i < nv; i++)
            ComputeTangentBasis(vertexNormals[i], out e1Basis[i], out e2Basis[i]);

        var curv = new double[nv, 3];

        for (int fi = 0; fi < nf; fi++)
        {
            var f = triMesh.Faces[fi];
            Vec3d p0 = triMesh.Vertices[f[0]];
            Vec3d p1 = triMesh.Vertices[f[1]];
            Vec3d p2 = triMesh.Vertices[f[2]];

            Vec3d[] edges = { p2 - p1, p0 - p2, p1 - p0 };

            Vec3d faceNormal = Vec3d.Cross(edges[2], -edges[1]).Normalized();

            for (int j = 0; j < 3; j++)
            {
                int vi = f[j];
                int vj = f[(j + 1) % 3];

                Vec3d edge = triMesh.Vertices[vj] - triMesh.Vertices[vi];
                Vec3d dn = vertexNormals[vj] - vertexNormals[vi];

                double u = Vec3d.Dot(edge, e1Basis[vi]);
                double v = Vec3d.Dot(edge, e2Basis[vi]);
                double dnU = Vec3d.Dot(dn, e1Basis[vi]);
                double dnV = Vec3d.Dot(dn, e2Basis[vi]);

                curv[vi, 0] += dnU * u;
                curv[vi, 1] += dnU * v + dnV * u;
                curv[vi, 2] += dnV * v;
            }
        }

        var k1 = new double[nv];
        var k2 = new double[nv];
        var d1 = new Vec3d[nv];
        var d2 = new Vec3d[nv];

        for (int i = 0; i < nv; i++)
        {
            double a = curv[i, 0];
            double b = curv[i, 1] * 0.5;
            double c = curv[i, 2];

            DiagonalizeShapeOperator(a, b, c, out double kappa1, out double kappa2, out double theta);

            k1[i] = kappa1;
            k2[i] = kappa2;
            d1[i] = (Math.Cos(theta) * e1Basis[i] + Math.Sin(theta) * e2Basis[i]).Normalized();
            d2[i] = (-Math.Sin(theta) * e1Basis[i] + Math.Cos(theta) * e2Basis[i]).Normalized();

            if (k1[i] < k2[i])
            {
                (k1[i], k2[i]) = (k2[i], k1[i]);
                (d1[i], d2[i]) = (d2[i], d1[i]);
            }
        }

        if (radius > 1)
            SmoothCurvature(triMesh, radius - 1, k1, k2, d1, d2, vertexNormals, e1Basis, e2Basis);

        return new Result(k1, k2, d1, d2);
    }

    private static void ComputeTangentBasis(Vec3d normal, out Vec3d e1, out Vec3d e2)
    {
        Vec3d up = Math.Abs(normal.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
        e1 = Vec3d.Cross(normal, up).Normalized();
        e2 = Vec3d.Cross(normal, e1).Normalized();
    }

    private static void DiagonalizeShapeOperator(double a, double b, double c, out double k1, out double k2, out double theta)
    {
        double diff = a - c;
        if (Math.Abs(b) < 1e-15 && Math.Abs(diff) < 1e-15)
        {
            k1 = a;
            k2 = c;
            theta = 0;
            return;
        }

        theta = 0.5 * Math.Atan2(2.0 * b, diff);
        double cos2 = Math.Cos(theta) * Math.Cos(theta);
        double sin2 = Math.Sin(theta) * Math.Sin(theta);
        double sincos = Math.Sin(theta) * Math.Cos(theta);

        k1 = a * cos2 + 2.0 * b * sincos + c * sin2;
        k2 = a * sin2 - 2.0 * b * sincos + c * cos2;
    }

    private static void SmoothCurvature(
        MeshData mesh, int iterations,
        double[] k1, double[] k2,
        Vec3d[] d1, Vec3d[] d2,
        Vec3d[] normals, Vec3d[] e1Basis, Vec3d[] e2Basis)
    {
        var neighbors = mesh.BuildVertexNeighbors();

        for (int iter = 0; iter < iterations; iter++)
        {
            var newK1 = new double[mesh.VertexCount];
            var newK2 = new double[mesh.VertexCount];

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                double sumK1 = k1[i], sumK2 = k2[i];
                int count = 1;
                foreach (int j in neighbors[i])
                {
                    sumK1 += k1[j];
                    sumK2 += k2[j];
                    count++;
                }
                newK1[i] = sumK1 / count;
                newK2[i] = sumK2 / count;
            }

            Array.Copy(newK1, k1, mesh.VertexCount);
            Array.Copy(newK2, k2, mesh.VertexCount);
        }
    }
}
