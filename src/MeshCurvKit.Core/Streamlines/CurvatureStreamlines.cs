using System;
using System.Collections.Generic;
using MeshCurvKit.Core.Geometry;
using MeshCurvKit.Core.Curvature;

namespace MeshCurvKit.Core.Streamlines;

public static class CurvatureStreamlines
{
    public class Options
    {
        public double StepSize { get; set; } = 0.01;
        public int MaxSteps { get; set; } = 1000;
        public bool UseMaxCurvature { get; set; } = true;
    }

    /// <summary>
    /// Traces streamlines along principal curvature directions using RK4 integration.
    /// </summary>
    public static List<Vec3d[]> Trace(MeshData mesh, int[] seedVertices, PrincipalCurvature.Result curvature, Options? options = null)
    {
        options ??= new Options();
        var triMesh = mesh.ToTriangulated();
        var result = new List<Vec3d[]>();

        foreach (int seed in seedVertices)
        {
            var forward = TraceOne(triMesh, seed, curvature, options, false);
            var backward = TraceOne(triMesh, seed, curvature, options, true);

            var line = new List<Vec3d>();
            for (int i = backward.Count - 1; i > 0; i--)
                line.Add(backward[i]);
            line.AddRange(forward);

            if (line.Count > 1)
                result.Add(line.ToArray());
        }

        return result;
    }

    private static List<Vec3d> TraceOne(MeshData mesh, int startVertex, PrincipalCurvature.Result curv, Options opts, bool reverse)
    {
        var points = new List<Vec3d>();
        Vec3d pos = mesh.Vertices[startVertex];
        points.Add(pos);

        var dirs = opts.UseMaxCurvature ? curv.D1 : curv.D2;
        double sign = reverse ? -1.0 : 1.0;
        int closestVert = startVertex;

        for (int step = 0; step < opts.MaxSteps; step++)
        {
            closestVert = FindClosestVertex(mesh, pos, closestVert);
            if (closestVert < 0) break;

            Vec3d dir = sign * InterpolateDirection(mesh, pos, closestVert, dirs);
            if (dir.LengthSquared < 1e-20) break;

            // Ensure consistent direction along streamline
            if (points.Count > 1)
            {
                Vec3d prevDir = pos - points[points.Count - 2];
                if (Vec3d.Dot(dir, prevDir) < 0) dir = -dir;
            }

            Vec3d k1 = dir;
            Vec3d mid1 = pos + 0.5 * opts.StepSize * k1;
            int cv2 = FindClosestVertex(mesh, mid1, closestVert);
            if (cv2 < 0) break;
            Vec3d k2 = sign * InterpolateDirection(mesh, mid1, cv2, dirs);
            if (Vec3d.Dot(k2, k1) < 0) k2 = -k2;

            Vec3d mid2 = pos + 0.5 * opts.StepSize * k2;
            int cv3 = FindClosestVertex(mesh, mid2, closestVert);
            if (cv3 < 0) break;
            Vec3d k3 = sign * InterpolateDirection(mesh, mid2, cv3, dirs);
            if (Vec3d.Dot(k3, k1) < 0) k3 = -k3;

            Vec3d end = pos + opts.StepSize * k3;
            int cv4 = FindClosestVertex(mesh, end, closestVert);
            if (cv4 < 0) break;
            Vec3d k4 = sign * InterpolateDirection(mesh, end, cv4, dirs);
            if (Vec3d.Dot(k4, k1) < 0) k4 = -k4;

            Vec3d newPos = pos + (opts.StepSize / 6.0) * (k1 + 2.0 * k2 + 2.0 * k3 + k4);
            if ((newPos - pos).LengthSquared < 1e-20) break;

            pos = newPos;
            points.Add(pos);
        }

        return points;
    }

    private static int FindClosestVertex(MeshData mesh, Vec3d pos, int hint)
    {
        int best = hint;
        double bestDist = (mesh.Vertices[hint] - pos).LengthSquared;

        // Simple local search from hint
        var neighbors = mesh.BuildVertexNeighbors();
        foreach (int n in neighbors[hint])
        {
            double d = (mesh.Vertices[n] - pos).LengthSquared;
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    private static Vec3d InterpolateDirection(MeshData mesh, Vec3d pos, int closestVert, Vec3d[] dirs)
    {
        return dirs[closestVert].Normalized();
    }
}
