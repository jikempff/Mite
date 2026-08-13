using System;
using System.Collections.Generic;
using Mite.Core.Geometry;
using Mite.Core.Curvature;
using Mite.Core.Gridshells;

namespace Mite.Core.Streamlines;

public static class CurvatureStreamlines
{
    public class Options
    {
        public double StepSize { get; set; } = 0.01;
        public int MaxSteps { get; set; } = 1000;
        public bool UseMaxCurvature { get; set; } = true;

        /// <summary>On-surface Laplacian fairing passes applied to each traced curve (0 disables).</summary>
        public int SmoothingPasses { get; set; } = 10;

        /// <summary>
        /// Traces stop where the blended field magnitude drops below this
        /// fraction of unit length (0..1). Corners with a vanishing direction
        /// contribute zero to the blend, so this fades traces out smoothly at
        /// region borders; lower values keep tracing deeper into degenerate
        /// (e.g. near-umbilical) regions.
        /// </summary>
        public double MinFieldMagnitude { get; set; } = 0.3;

        /// <summary>
        /// Optional cancellation probe checked between curves; return true to
        /// stop tracing and keep the curves produced so far (e.g. wire this to
        /// the host's Esc-key check so long solves stay interruptible).
        /// </summary>
        public Func<bool>? ShouldCancel { get; set; }

    }

    /// <summary>
    /// Traces streamlines along principal curvature directions. Each step moves
    /// in the local field direction and reprojects onto the mesh, so the curves
    /// stay on the surface. The line-field sign ambiguity is handled by aligning
    /// each sample with the previous travel direction.
    /// </summary>
    public static List<Vec3d[]> Trace(MeshData mesh, int[] seedVertices, PrincipalCurvature.Result curvature, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(mesh);
        var dirs = options.UseMaxCurvature ? curvature.D1 : curvature.D2;
        var result = new List<Vec3d[]>();

        foreach (int seed in seedVertices)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            if (seed < 0 || seed >= proj.Mesh.VertexCount) continue;

            var line = FieldTracer.TraceBoth(proj, proj.Mesh.Vertices[seed], seed,
                dirs, null, null, options.StepSize, options.MaxSteps, null, options.MinFieldMagnitude);

            if (line.Length > 1)
                result.Add(CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses));
        }

        return result;
    }
}
