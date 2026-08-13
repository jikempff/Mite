using System;
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
    /// Based on "Estimating Curvatures and Their Derivatives on Triangle Meshes" (Rusinkiewicz 2004):
    /// the second fundamental form is least-squares fitted per face from finite normal
    /// differences along the three edges, then rotated into each vertex's tangent frame
    /// and averaged with corner-angle weights.
    /// </summary>
    public static Result Compute(MeshData mesh, int radius = 2)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        int nf = triMesh.FaceCount;

        // Max-weighted normals are exact on spheres, which keeps the normal
        // finite differences (and hence curvature magnitudes) unbiased
        var vertexNormals = triMesh.ComputeVertexNormalsMax();

        var e1Basis = new Vec3d[nv];
        var e2Basis = new Vec3d[nv];
        for (int i = 0; i < nv; i++)
            ComputeTangentBasis(vertexNormals[i], out e1Basis[i], out e2Basis[i]);

        // Accumulated second fundamental form per vertex, in the vertex frame
        var accA = new double[nv];
        var accB = new double[nv];
        var accC = new double[nv];
        var accW = new double[nv];

        for (int fi = 0; fi < nf; fi++)
        {
            var f = triMesh.Faces[fi];
            Vec3d p0 = triMesh.Vertices[f[0]];
            Vec3d p1 = triMesh.Vertices[f[1]];
            Vec3d p2 = triMesh.Vertices[f[2]];

            // Edge j is opposite vertex j
            Vec3d[] edges = { p2 - p1, p0 - p2, p1 - p0 };

            Vec3d faceNormal = Vec3d.Cross(edges[2], -edges[1]);
            if (faceNormal.LengthSquared < 1e-30) continue;
            faceNormal = faceNormal.Normalized();

            // Face tangent frame
            Vec3d t = edges[2].Normalized();
            Vec3d b = Vec3d.Cross(faceNormal, t).Normalized();

            // Least-squares fit of II = [[a, m], [m, c]] in the face frame from
            // II * e_uv ≈ dn_uv along the three edges (6 equations, 3 unknowns).
            double m00 = 0, m01 = 0, m11 = 0, m12 = 0, m22 = 0;
            double r0 = 0, r1 = 0, r2 = 0;

            for (int j = 0; j < 3; j++)
            {
                // Edge j runs between the two vertices other than j
                Vec3d dn = vertexNormals[f[(j + 2) % 3]] - vertexNormals[f[(j + 1) % 3]];

                double u = Vec3d.Dot(edges[j], t);
                double v = Vec3d.Dot(edges[j], b);
                double dnU = Vec3d.Dot(dn, t);
                double dnV = Vec3d.Dot(dn, b);

                // Row [u, v, 0] -> dnU
                m00 += u * u;
                m01 += u * v;
                r0 += u * dnU;
                // Row [0, u, v] -> dnV
                m11 += u * u;
                m12 += u * v;
                r2 += v * dnV;
                // Shared terms
                m11 += v * v;
                m22 += v * v;
                r1 += v * dnU + u * dnV;
            }

            Solve3x3Symmetric(m00, m01, 0, m11, m12, m22, r0, r1, r2,
                out double fa, out double fm, out double fc);

            // Distribute to the face's vertices: rotate the face-frame tensor into
            // each vertex's tangent frame, weight by the corner angle
            for (int j = 0; j < 3; j++)
            {
                int vi = f[j];

                // The two face edges emanating from vertex j
                Vec3d cornerE1 = -edges[(j + 1) % 3];
                Vec3d cornerE2 = edges[(j + 2) % 3];
                double w = CornerAngle(cornerE1, cornerE2);
                if (w < 1e-12) continue;

                ProjectCurvatureTensor(t, b, faceNormal, fa, fm, fc,
                    e1Basis[vi], e2Basis[vi], vertexNormals[vi],
                    out double pa, out double pm, out double pc);

                accA[vi] += w * pa;
                accB[vi] += w * pm;
                accC[vi] += w * pc;
                accW[vi] += w;
            }
        }

        var k1 = new double[nv];
        var k2 = new double[nv];
        var d1 = new Vec3d[nv];
        var d2 = new Vec3d[nv];

        for (int i = 0; i < nv; i++)
        {
            double a = 0, b = 0, c = 0;
            if (accW[i] > 1e-15)
            {
                a = accA[i] / accW[i];
                b = accB[i] / accW[i];
                c = accC[i] / accW[i];
            }

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

    /// <summary>
    /// Solves the symmetric 3x3 system M x = r via Cramer's rule, where
    /// M = [[m00,m01,m02],[m01,m11,m12],[m02,m12,m22]]. Returns zeros when singular
    /// (isolated vertices or degenerate 1-rings).
    /// </summary>
    private static void Solve3x3Symmetric(
        double m00, double m01, double m02, double m11, double m12, double m22,
        double r0, double r1, double r2,
        out double a, out double b, out double c)
    {
        double det =
            m00 * (m11 * m22 - m12 * m12)
            - m01 * (m01 * m22 - m12 * m02)
            + m02 * (m01 * m12 - m11 * m02);

        double scale = Math.Max(Math.Max(Math.Abs(m00), Math.Abs(m11)), Math.Abs(m22));
        if (Math.Abs(det) < 1e-12 * Math.Max(scale * scale * scale, 1e-30))
        {
            a = 0; b = 0; c = 0;
            return;
        }

        a = (r0 * (m11 * m22 - m12 * m12)
           - m01 * (r1 * m22 - m12 * r2)
           + m02 * (r1 * m12 - m11 * r2)) / det;

        b = (m00 * (r1 * m22 - r2 * m12)
           - r0 * (m01 * m22 - m12 * m02)
           + m02 * (m01 * r2 - r1 * m02)) / det;

        c = (m00 * (m11 * r2 - m12 * r1)
           - m01 * (m01 * r2 - r1 * m02)
           + r0 * (m01 * m12 - m11 * m02)) / det;
    }

    private static double CornerAngle(Vec3d e1, Vec3d e2)
    {
        double l1 = e1.Length, l2 = e2.Length;
        if (l1 < 1e-15 || l2 < 1e-15) return 0;
        double d = Vec3d.Dot(e1, e2) / (l1 * l2);
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d)));
    }

    /// <summary>
    /// Re-expresses a curvature tensor given in the (oldU, oldV) tangent frame
    /// (with normal oldN) in the (newU, newV) frame with normal newN. The new
    /// frame is first rotated so its normal coincides with the old one
    /// (Rusinkiewicz 2004, "proj_curv").
    /// </summary>
    private static void ProjectCurvatureTensor(
        Vec3d oldU, Vec3d oldV, Vec3d oldN,
        double oldA, double oldB, double oldC,
        Vec3d newU, Vec3d newV, Vec3d newN,
        out double a, out double b, out double c)
    {
        RotateCoordSys(newU, newV, oldN, out Vec3d rU, out Vec3d rV);

        double u1 = Vec3d.Dot(rU, oldU);
        double v1 = Vec3d.Dot(rU, oldV);
        double u2 = Vec3d.Dot(rV, oldU);
        double v2 = Vec3d.Dot(rV, oldV);

        a = oldA * u1 * u1 + oldB * (2.0 * u1 * v1) + oldC * v1 * v1;
        b = oldA * u1 * u2 + oldB * (u1 * v2 + u2 * v1) + oldC * v1 * v2;
        c = oldA * u2 * u2 + oldB * (2.0 * u2 * v2) + oldC * v2 * v2;
    }

    /// <summary>
    /// Rotates the coordinate system (u, v) about the axis perpendicular to its
    /// normal and the target normal, so the frame becomes perpendicular to newNorm.
    /// </summary>
    private static void RotateCoordSys(Vec3d u, Vec3d v, Vec3d newNorm, out Vec3d rU, out Vec3d rV)
    {
        rU = u;
        rV = v;
        Vec3d oldNorm = Vec3d.Cross(u, v);
        double ndot = Vec3d.Dot(oldNorm, newNorm);
        if (ndot <= -1.0)
        {
            rU = -rU;
            rV = -rV;
            return;
        }

        Vec3d perpOld = newNorm - ndot * oldNorm;
        Vec3d dperp = (1.0 / (1.0 + ndot)) * (oldNorm + newNorm);
        rU = rU - Vec3d.Dot(rU, perpOld) * dperp;
        rV = rV - Vec3d.Dot(rV, perpOld) * dperp;
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

    /// <summary>
    /// Smooths the curvature field by averaging the shape operator as a
    /// world-frame 3x3 tensor S = k1 d1d1^T + k2 d2d2^T over each 1-ring, then
    /// re-extracting values and directions from the average. Scalar smoothing
    /// of k1/k2 alone leaves the directions noisy, which corrupts streamline
    /// and asymptotic tracing on coarse meshes; tensor averaging keeps values
    /// and directions consistent (and handles the arbitrary per-vertex sign of
    /// the direction fields, which plain vector averaging cannot).
    /// </summary>
    private static void SmoothCurvature(
        MeshData mesh, int iterations,
        double[] k1, double[] k2,
        Vec3d[] d1, Vec3d[] d2,
        Vec3d[] normals, Vec3d[] e1Basis, Vec3d[] e2Basis)
    {
        var neighbors = mesh.BuildVertexNeighbors();
        int nv = mesh.VertexCount;

        var tensors = new Matrix3d[nv];
        for (int i = 0; i < nv; i++)
            tensors[i] = k1[i] * Matrix3d.OuterProduct(d1[i], d1[i]) +
                         k2[i] * Matrix3d.OuterProduct(d2[i], d2[i]);

        for (int iter = 0; iter < iterations; iter++)
        {
            var smoothed = new Matrix3d[nv];
            for (int i = 0; i < nv; i++)
            {
                Matrix3d sum = tensors[i];
                int count = 1;
                foreach (int j in neighbors[i])
                {
                    sum = sum + tensors[j];
                    count++;
                }
                smoothed[i] = (1.0 / count) * sum;
            }
            tensors = smoothed;
        }

        for (int i = 0; i < nv; i++)
        {
            Matrix3d.EigenSymmetric(tensors[i], out double[] values, out Vec3d[] vectors);

            // Degenerate (flat or isolated) vertex: keep the frame-derived directions
            if (Math.Max(Math.Abs(values[0]), Math.Abs(values[2])) < 1e-15) continue;

            // The eigenvector best aligned with the vertex normal is the normal
            // direction; the other two eigenpairs are the principal curvatures.
            // (Picking the eigenvalue nearest zero fails on cylinders, where
            // k2 = 0 is indistinguishable from the normal's zero.)
            int n0 = 0;
            double best = -1;
            for (int e = 0; e < 3; e++)
            {
                double d = Math.Abs(Vec3d.Dot(vectors[e], normals[i]));
                if (d > best) { best = d; n0 = e; }
            }
            int a = (n0 + 1) % 3, b = (n0 + 2) % 3;

            if (values[a] >= values[b])
            {
                k1[i] = values[a]; d1[i] = vectors[a];
                k2[i] = values[b]; d2[i] = vectors[b];
            }
            else
            {
                k1[i] = values[b]; d1[i] = vectors[b];
                k2[i] = values[a]; d2[i] = vectors[a];
            }
        }
    }
}
