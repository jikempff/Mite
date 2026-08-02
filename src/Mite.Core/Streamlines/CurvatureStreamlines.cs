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
            if (seed < 0 || seed >= proj.Mesh.VertexCount) continue;

            var line = FieldTracer.TraceBoth(proj, proj.Mesh.Vertices[seed], seed,
                dirs, null, null, options.StepSize, options.MaxSteps, null);

            if (line.Length > 1)
                result.Add(CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses));
        }

        return result;
    }
}
