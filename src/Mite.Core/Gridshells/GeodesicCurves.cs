using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Straightest-geodesic tracing on meshes: step in the tangent direction,
/// project back onto the surface, and keep the in-surface component of the
/// travel direction. Geodesics minimize bending about the strong axis, making
/// them the natural layout for geodesic (lath) gridshells.
/// </summary>
public static class GeodesicCurves
{
    public class Options
    {
        public double StepSize { get; set; } = 0.01;
        public int MaxSteps { get; set; } = 1000;
    }

    /// <summary>
    /// Traces a geodesic through each seed vertex along the corresponding
    /// direction (the geodesic extends both ways from the seed). If fewer
    /// directions than seeds are supplied, the last direction is reused.
    /// </summary>
    public static List<Vec3d[]> Trace(MeshData mesh, int[] seedVertices, Vec3d[] directions, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(mesh);
        var result = new List<Vec3d[]>();

        if (directions.Length == 0) return result;

        for (int i = 0; i < seedVertices.Length; i++)
        {
            int seed = seedVertices[i];
            if (seed < 0 || seed >= proj.Mesh.VertexCount) continue;

            Vec3d dir = directions[Math.Min(i, directions.Length - 1)];
            if (dir.LengthSquared < 1e-20) continue;
            dir = dir.Normalized();

            var forward = TraceOne(proj, seed, dir, options);
            var backward = TraceOne(proj, seed, -dir, options);
            var line = FieldTracer.Join(backward, forward);

            if (line.Length > 1)
                result.Add(line);
        }

        return result;
    }

    private static List<Vec3d> TraceOne(MeshProjection proj, int startVertex, Vec3d dir, Options opts)
    {
        var mesh = proj.Mesh;
        var points = new List<Vec3d>();
        Vec3d pos = mesh.Vertices[startVertex];
        points.Add(pos);

        // Flatten the requested direction into the surface at the seed
        var start = proj.ClosestPoint(pos, startVertex);
        dir = TangentComponent(dir, start.Normal);
        if (dir.LengthSquared < 1e-20) return points;
        dir = dir.Normalized();

        int vert = startVertex;
        for (int step = 0; step < opts.MaxSteps; step++)
        {
            var hit = proj.ClosestPoint(pos + opts.StepSize * dir, vert);
            Vec3d newPos = hit.Point;

            // Stalled against a boundary
            if ((newPos - pos).LengthSquared < 0.01 * opts.StepSize * opts.StepSize) break;

            Vec3d travel = newPos - pos;
            pos = newPos;
            vert = hit.NearestVertex;
            points.Add(pos);

            // Continue straight: the travel direction flattened into the new tangent plane
            Vec3d t = TangentComponent(travel, hit.Normal);
            if (t.LengthSquared < 1e-20) break;
            dir = t.Normalized();
        }

        return points;
    }

    private static Vec3d TangentComponent(Vec3d v, Vec3d normal) =>
        v - Vec3d.Dot(v, normal) * normal;
}
