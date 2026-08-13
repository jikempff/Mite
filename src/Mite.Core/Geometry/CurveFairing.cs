using System;

namespace Mite.Core.Geometry;

/// <summary>
/// Constrained Laplacian fairing for polylines lying on a mesh: each pass moves
/// interior points toward their neighbor midpoint, then projects them back onto
/// the surface. Endpoints stay fixed; closed loops (first == last point) are
/// smoothed across the seam. This is a lightweight stand-in for the fairness
/// energies used in gridshell optimization (e.g. Wang et al., CAD 2024) and
/// removes the residual facet-scale kinks of traced curves.
/// </summary>
public static class CurveFairing
{
    public static Vec3d[] SmoothOnSurface(
        MeshProjection proj, Vec3d[] line, int iterations = 10, double strength = 0.5)
    {
        if (line.Length < 3 || iterations <= 0) return line;

        bool closed = (line[0] - line[line.Length - 1]).LengthSquared < 1e-24;
        int n = line.Length;
        var pts = (Vec3d[])line.Clone();

        var hints = new int[n];
        int hint = proj.NearestVertexGlobal(pts[0]);
        for (int i = 0; i < n; i++)
        {
            var h = proj.ClosestPoint(pts[i], hint);
            hints[i] = h.NearestVertex;
            hint = h.NearestVertex;
        }

        for (int iter = 0; iter < iterations; iter++)
        {
            // Interior points move; for closed loops the seam point (0 == n-1)
            // moves too. Distinct points are 0..n-2 in both cases
            int first = closed ? 0 : 1;
            int last = n - 2;

            Vec3d prevOriginal = closed ? pts[n - 2] : pts[0];
            for (int i = first; i <= last; i++)
            {
                Vec3d next = pts[i + 1];
                Vec3d target = pts[i] + strength * (0.5 * (prevOriginal + next) - pts[i]);
                var h = proj.ClosestPoint(target, hints[i]);

                prevOriginal = pts[i];
                pts[i] = h.Point;
                hints[i] = h.NearestVertex;
            }
            if (closed) pts[n - 1] = pts[0];
        }

        return pts;
    }
}
