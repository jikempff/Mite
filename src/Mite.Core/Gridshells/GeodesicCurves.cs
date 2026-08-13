using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Straightest-geodesic tracing on meshes: step in the tangent direction with a
/// midpoint scheme, project back onto the surface, and keep the in-surface
/// component of the travel direction. The smooth (barycentric-interpolated)
/// normal is used for all tangent projections so the direction varies
/// continuously across facet boundaries instead of jumping with face normals.
/// Geodesics minimize bending about the strong axis, making them the natural
/// layout for geodesic (lath) gridshells.
/// </summary>
public static class GeodesicCurves
{
    public class Options
    {
        public double StepSize { get; set; } = 0.01;
        public int MaxSteps { get; set; } = 1000;

        /// <summary>On-surface Laplacian fairing passes applied to each traced curve (0 disables).</summary>
        public int SmoothingPasses { get; set; } = 10;

        /// <summary>
        /// Optional cancellation probe checked between curves; return true to
        /// stop tracing and keep the curves produced so far (e.g. wire this to
        /// the host's Esc-key check so long solves stay interruptible).
        /// </summary>
        public Func<bool>? ShouldCancel { get; set; }

    }

    /// <summary>
    /// Traces a geodesic through each seed vertex along the corresponding
    /// direction (the geodesic extends both ways from the seed; closed loops
    /// are detected and returned as a single cycle). If fewer directions than
    /// seeds are supplied, the last direction is reused.
    /// </summary>
    public static List<Vec3d[]> Trace(MeshData mesh, int[] seedVertices, Vec3d[] directions, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(mesh);
        var result = new List<Vec3d[]>();

        if (directions.Length == 0) return result;

        for (int i = 0; i < seedVertices.Length; i++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            int seed = seedVertices[i];
            if (seed < 0 || seed >= proj.Mesh.VertexCount) continue;

            Vec3d dir = directions[Math.Min(i, directions.Length - 1)];
            if (dir.LengthSquared < 1e-20) continue;

            dir = SeedTangent(proj, seed, dir);
            if (dir.LengthSquared < 1e-20) continue;

            var line = TraceBothFrom(proj, proj.Mesh.Vertices[seed], seed, dir,
                options.StepSize, options.MaxSteps, null);

            if (line.Length > 1)
                result.Add(CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses));
        }

        return result;
    }

    /// <summary>
    /// Traces both ways from a point and joins the halves. A forward half that
    /// closes into a loop is returned directly, without duplicating the cycle.
    /// </summary>
    internal static Vec3d[] TraceBothFrom(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d dir,
        double stepSize, int maxSteps, Func<Vec3d, bool>? stopNear)
    {
        var forward = TraceOneFrom(proj, startPos, startHint, dir, stepSize, maxSteps, stopNear, out bool closed);
        if (closed) return forward.ToArray();

        var backward = TraceOneFrom(proj, startPos, startHint, -dir, stepSize, maxSteps, stopNear, out _);
        return FieldTracer.Join(backward, forward);
    }

    internal static List<Vec3d> TraceOneFrom(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d dir,
        double stepSize, int maxSteps, Func<Vec3d, bool>? stopNear)
    {
        return TraceOneFrom(proj, startPos, startHint, dir, stepSize, maxSteps, stopNear, out _);
    }

    internal static List<Vec3d> TraceOneFrom(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d dir,
        double stepSize, int maxSteps, Func<Vec3d, bool>? stopNear, out bool closedLoop)
    {
        closedLoop = false;
        var points = new List<Vec3d>();

        var start = proj.ClosestPoint(startPos, startHint);
        Vec3d pos = start.Point;
        points.Add(pos);

        // Flatten the requested direction into the surface at the seed
        dir = TangentComponent(dir, start.SmoothNormal);
        if (dir.LengthSquared < 1e-20) return points;
        dir = dir.Normalized();

        Vec3d startNormal = start.SmoothNormal;
        Vec3d initialDir = dir;
        // Capture radius scaled to the mesh: integration drift over a full loop
        // is a fraction of the edge length, far more than one step (see
        // FieldTracer.TryCloseLoop)
        double captureRadius = Math.Max(0.75 * stepSize, 0.5 * proj.AverageEdgeLength);
        double leaveRadius = Math.Max(8.0 * stepSize, 2.0 * captureRadius);
        double maxStartDist = 0.0;

        int vert = start.NearestVertex;
        for (int step = 0; step < maxSteps; step++)
        {
            // Midpoint scheme: transport the direction to the half-step point
            // before taking the full step
            var midHit = proj.ClosestPoint(pos + 0.5 * stepSize * dir, vert);
            Vec3d dMid = TangentComponent(dir, midHit.SmoothNormal);
            if (dMid.LengthSquared < 1e-20) break;
            dMid = dMid.Normalized();

            Vec3d intended = pos + stepSize * dMid;
            var hit = proj.ClosestPoint(intended, midHit.NearestVertex);
            Vec3d newPos = hit.Point;

            // Fell off the mesh: the projection clamped the step to a boundary
            // far from the intended target. End cleanly at the edge instead of
            // letting the geodesic crawl along the boundary.
            if ((newPos - intended).Length > 0.5 * stepSize)
            {
                Vec3d clampTravel = newPos - pos;
                if (Vec3d.Dot(clampTravel, dMid) > 0 && clampTravel.LengthSquared > 0.01 * stepSize * stepSize)
                    points.Add(newPos);
                break;
            }

            // Stalled against a boundary
            if ((newPos - pos).LengthSquared < 0.01 * stepSize * stepSize) break;

            // Ran into an already-traced curve
            if (stopNear != null && stopNear(newPos)) break;

            Vec3d travel = newPos - pos;
            pos = newPos;
            vert = hit.NearestVertex;
            points.Add(pos);

            maxStartDist = Math.Max(maxStartDist, (pos - points[0]).Length);

            // Closed loop: returned to the start after traveling away
            if (step > 4 && FieldTracer.TryCloseLoop(points, pos, startNormal, initialDir,
                    travel.Normalized(), stepSize, captureRadius, leaveRadius, maxStartDist,
                    out Vec3d closing))
            {
                points.Add(closing);
                closedLoop = true;
                break;
            }

            // Continue straight: the travel direction flattened into the new tangent plane
            Vec3d t = TangentComponent(travel, hit.SmoothNormal);
            if (t.LengthSquared < 1e-20) break;
            dir = t.Normalized();
        }

        return points;
    }

    /// <summary>
    /// Flattens a requested seed direction into the surface. When the request is
    /// (near) parallel to the normal — where the tangent component vanishes — an
    /// arbitrary but deterministic tangent is used so the trace can still start.
    /// </summary>
    internal static Vec3d SeedTangent(MeshProjection proj, int seedVertex, Vec3d dir)
    {
        var hit = proj.ClosestPoint(proj.Mesh.Vertices[seedVertex], seedVertex);
        Vec3d n = hit.SmoothNormal;
        Vec3d t = TangentComponent(dir, n);
        if (t.LengthSquared > 1e-12 * dir.LengthSquared)
            return t.Normalized();

        Vec3d axis = Math.Abs(n.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
        t = Vec3d.Cross(n, axis);
        return t.LengthSquared < 1e-20 ? Vec3d.Zero : t.Normalized();
    }

    private static Vec3d TangentComponent(Vec3d v, Vec3d normal) =>
        v - Vec3d.Dot(v, normal) * normal;
}
