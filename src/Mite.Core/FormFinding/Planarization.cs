using System;
using Mite.Core.Geometry;

namespace Mite.Core.FormFinding;

public static class Planarization
{
    public class Options
    {
        public int MaxIterations { get; set; } = 100;
        public double Tolerance { get; set; } = 1e-6;
        public double Strength { get; set; } = 1.0;

        /// <summary>Optional cancellation probe checked between iterations; true stops and returns the current geometry.</summary>
        public Func<bool>? ShouldCancel { get; set; }

        /// <summary>Optional progress sink (stage "planarizing"): iteration fraction or the deviation's log decay to the tolerance.</summary>
        public ProgressHandler? Progress { get; set; }

        /// <summary>Set after a run: true when ShouldCancel stopped the iterations.</summary>
        public bool Cancelled { get; internal set; }
    }

    public readonly struct Result
    {
        public readonly Vec3d[] Vertices;
        public readonly double[] FaceDeviations;
        public readonly int Iterations;

        public Result(Vec3d[] vertices, double[] deviations, int iterations)
        {
            Vertices = vertices;
            FaceDeviations = deviations;
            Iterations = iterations;
        }
    }

    /// <summary>
    /// Iterative quad planarization by projecting vertices onto best-fit planes per face.
    /// </summary>
    public static Result Compute(MeshData mesh, bool[] fixedVertices, Options? options = null)
    {
        options ??= new Options();
        var verts = new Vec3d[mesh.VertexCount];
        Array.Copy(mesh.Vertices, verts, mesh.VertexCount);

        double[] deviations = new double[mesh.FaceCount];
        int iter = 0;
        var progress = new ProgressScope(options.Progress, "planarizing");
        double firstDev = 0;
        bool finished = false;
        options.Cancelled = false;

        for (iter = 0; iter < options.MaxIterations; iter++)
        {
            if (options.ShouldCancel?.Invoke() == true) { options.Cancelled = true; break; }
            var targets = new Vec3d[mesh.VertexCount];
            var weights = new double[mesh.VertexCount];
            double maxDev = 0;

            for (int fi = 0; fi < mesh.FaceCount; fi++)
            {
                var f = mesh.Faces[fi];
                if (f.Length < 4) { deviations[fi] = 0; continue; }

                FitPlane(verts, f, out Vec3d centroid, out Vec3d normal);

                double dev = 0;
                foreach (int vi in f)
                {
                    double d = Vec3d.Dot(verts[vi] - centroid, normal);
                    dev = Math.Max(dev, Math.Abs(d));
                    Vec3d projected = verts[vi] - d * normal;
                    targets[vi] = targets[vi] + projected;
                    weights[vi] += 1.0;
                }
                deviations[fi] = dev;
                maxDev = Math.Max(maxDev, dev);
            }

            if (firstDev <= 0) firstDev = maxDev;
            progress.Report(ProgressScope.Converging(firstDev, maxDev, options.Tolerance, iter, options.MaxIterations));
            if (maxDev < options.Tolerance) { iter++; finished = true; break; }

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (fixedVertices != null && fixedVertices[i]) continue;
                if (weights[i] > 0)
                {
                    Vec3d target = targets[i] / weights[i];
                    verts[i] = verts[i] + options.Strength * (target - verts[i]);
                }
            }
        }

        // Report the deviation of the geometry actually returned (the loop
        // measures before it moves vertices, so its last values are stale)
        deviations = ComputeDeviation(new MeshData(verts, mesh.Faces));
        if (finished || iter >= options.MaxIterations) progress.Done();
        return new Result(verts, deviations, iter);
    }

    /// <summary>
    /// Computes planarity deviation per face without modifying geometry.
    /// </summary>
    public static double[] ComputeDeviation(MeshData mesh)
    {
        var deviations = new double[mesh.FaceCount];
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            var f = mesh.Faces[fi];
            if (f.Length < 4) continue;

            FitPlane(mesh.Vertices, f, out Vec3d centroid, out Vec3d normal);
            double maxDev = 0;
            foreach (int vi in f)
            {
                double d = Math.Abs(Vec3d.Dot(mesh.Vertices[vi] - centroid, normal));
                maxDev = Math.Max(maxDev, d);
            }
            deviations[fi] = maxDev;
        }
        return deviations;
    }

    private static void FitPlane(Vec3d[] vertices, int[] face, out Vec3d centroid, out Vec3d normal)
    {
        centroid = Vec3d.Zero;
        foreach (int vi in face) centroid = centroid + vertices[vi];
        centroid = centroid / face.Length;

        normal = Vec3d.Zero;
        for (int i = 0; i < face.Length; i++)
        {
            Vec3d curr = vertices[face[i]] - centroid;
            Vec3d next = vertices[face[(i + 1) % face.Length]] - centroid;
            normal = normal + Vec3d.Cross(curr, next);
        }
        normal = normal.Normalized();
    }
}
