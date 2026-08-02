using System;
using Mite.Core.Geometry;

namespace Mite.Core.Curvature;

public static class GaussianCurvature
{
    /// <summary>
    /// Computes per-vertex Gaussian curvature using the angle deficit method.
    /// K_i = (2π - Σ angles_i) / A_i (mixed Voronoi area). Boundary vertices are
    /// set to zero: the 2π deficit only applies to full vertex stars, so open
    /// mesh edges would otherwise report huge spurious curvature.
    /// </summary>
    public static double[] Compute(MeshData mesh)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        var boundary = triMesh.BuildBoundaryVertexFlags();
        var angleSum = new double[nv];
        var areas = new double[nv];

        foreach (var f in triMesh.Faces)
        {
            Vec3d p0 = triMesh.Vertices[f[0]];
            Vec3d p1 = triMesh.Vertices[f[1]];
            Vec3d p2 = triMesh.Vertices[f[2]];

            Vec3d e01 = p1 - p0, e02 = p2 - p0;
            Vec3d e10 = p0 - p1, e12 = p2 - p1;
            Vec3d e20 = p0 - p2, e21 = p1 - p2;

            double a0 = AngleBetween(e01, e02);
            double a1 = AngleBetween(e10, e12);
            double a2 = AngleBetween(e20, e21);

            angleSum[f[0]] += a0;
            angleSum[f[1]] += a1;
            angleSum[f[2]] += a2;

            double faceArea = Vec3d.Cross(e01, e02).Length * 0.5;
            MixedVoronoiArea(f[0], f[1], f[2], a0, a1, a2, faceArea, e01, e02, e10, e12, e20, e21, areas);
        }

        var K = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            if (areas[i] > 1e-15 && !boundary[i])
                K[i] = (2.0 * Math.PI - angleSum[i]) / areas[i];
        }
        return K;
    }

    private static double AngleBetween(Vec3d a, Vec3d b)
    {
        double d = Vec3d.Dot(a.Normalized(), b.Normalized());
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d)));
    }

    private static void MixedVoronoiArea(
        int i0, int i1, int i2,
        double a0, double a1, double a2,
        double faceArea,
        Vec3d e01, Vec3d e02, Vec3d e10, Vec3d e12, Vec3d e20, Vec3d e21,
        double[] areas)
    {
        bool obtuse = a0 > Math.PI / 2 || a1 > Math.PI / 2 || a2 > Math.PI / 2;

        if (!obtuse)
        {
            double cot1 = Cot(a1), cot2 = Cot(a2), cot0 = Cot(a0);
            areas[i0] += (e01.LengthSquared * cot2 + e02.LengthSquared * cot1) / 8.0;
            areas[i1] += (e10.LengthSquared * cot2 + e12.LengthSquared * cot0) / 8.0;
            areas[i2] += (e20.LengthSquared * cot1 + e21.LengthSquared * cot0) / 8.0;
        }
        else
        {
            if (a0 > Math.PI / 2) { areas[i0] += faceArea / 2; areas[i1] += faceArea / 4; areas[i2] += faceArea / 4; }
            else if (a1 > Math.PI / 2) { areas[i0] += faceArea / 4; areas[i1] += faceArea / 2; areas[i2] += faceArea / 4; }
            else { areas[i0] += faceArea / 4; areas[i1] += faceArea / 4; areas[i2] += faceArea / 2; }
        }
    }

    private static double Cot(double angle) => Math.Cos(angle) / Math.Sin(angle);
}
