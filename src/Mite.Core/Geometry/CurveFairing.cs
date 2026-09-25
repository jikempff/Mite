using System;

namespace Mite.Core.Geometry;

/// <summary>
/// Constrained fairing for polylines lying on a mesh. Each pass is a Taubin
/// λ|μ step (a smoothing step followed by a slightly larger un-shrinking
/// step), with every moved point projected back onto the surface. Unlike
/// plain Laplacian smoothing — which is a discrete geodesic-curvature flow
/// and pulls curvature lines toward geodesics and shrinks closed loops toward
/// the poles of a sphere — this removes the facet-scale kinks of traced
/// curves while keeping their global shape. Endpoints stay fixed; closed
/// loops (first == last point) are smoothed across the seam.
/// </summary>
public static class CurveFairing
{
    public static Vec3d[] SmoothOnSurface(
        MeshProjection proj, Vec3d[] line, int iterations = 10, double strength = 0.5)
    {
        if (line.Length < 5 || iterations <= 0) return line;

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

        // Taubin: λ shrink, μ expand with |μ| slightly > λ (pass-band ~ 0.1)
        double lambda = Math.Max(0.0, Math.Min(0.9, strength));
        double mu = -lambda / (1.0 - 0.1 * lambda);

        var next = new Vec3d[n];
        for (int iter = 0; iter < iterations; iter++)
        {
            Step(proj, pts, next, hints, closed, lambda);
            Step(proj, pts, next, hints, closed, mu);
        }

        return pts;
    }

    private static void Step(MeshProjection proj, Vec3d[] pts, Vec3d[] next, int[] hints, bool closed, double factor)
    {
        int n = pts.Length;
        Array.Copy(pts, next, n);

        // Distinct movable points: 0..n-2 for closed loops; open lines keep
        // their two end points AND the points next to them, so the end
        // segments (the exact border exits of the tracer) keep their
        // direction — smoothing up to a fixed end pulled the last interior
        // point off the trend and left an 8° hook on faceted borders.
        int first = closed ? 0 : 2;
        int last = closed ? n - 2 : n - 3;
        for (int i = first; i <= last; i++)
        {
            Vec3d prev = pts[i == 0 ? n - 2 : i - 1];
            Vec3d nxt = pts[i + 1];
            Vec3d target = pts[i] + factor * (0.5 * (prev + nxt) - pts[i]);
            var h = proj.ClosestPoint(target, hints[i]);
            next[i] = h.Point;
            hints[i] = h.NearestVertex;
        }
        if (closed) next[n - 1] = next[0];

        Array.Copy(next, pts, n);
    }
}
