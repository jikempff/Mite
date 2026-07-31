using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Follows a per-vertex tangent direction field across a mesh, projecting each
/// step back onto the surface. Handles the sign ambiguity of line fields by
/// aligning each sample with the previous travel direction.
/// </summary>
internal static class FieldTracer
{
    internal static List<Vec3d> Trace(
        MeshProjection proj, int startVertex, Vec3d[] dirs, bool[]? mask,
        double stepSize, int maxSteps, bool reverse)
    {
        var mesh = proj.Mesh;
        var points = new List<Vec3d>();
        Vec3d pos = mesh.Vertices[startVertex];
        points.Add(pos);

        int vert = startVertex;
        Vec3d prevDir = (reverse ? -1.0 : 1.0) * dirs[startVertex];
        if (prevDir.LengthSquared < 1e-20) return points;

        for (int step = 0; step < maxSteps; step++)
        {
            if (mask != null && !mask[vert]) break;

            Vec3d dir = dirs[vert];
            if (dir.LengthSquared < 1e-20) break;
            if (Vec3d.Dot(dir, prevDir) < 0) dir = -dir;

            var hit = proj.ClosestPoint(pos + stepSize * dir, vert);
            Vec3d newPos = hit.Point;

            // Stalled against a boundary
            if ((newPos - pos).LengthSquared < 0.01 * stepSize * stepSize) break;

            prevDir = (newPos - pos).Normalized();
            pos = newPos;
            vert = hit.NearestVertex;
            points.Add(pos);
        }

        return points;
    }

    /// <summary>Joins a backward and forward trace into one polyline through the seed.</summary>
    internal static Vec3d[] Join(List<Vec3d> backward, List<Vec3d> forward)
    {
        var line = new List<Vec3d>();
        for (int i = backward.Count - 1; i > 0; i--)
            line.Add(backward[i]);
        line.AddRange(forward);
        return line.ToArray();
    }
}
