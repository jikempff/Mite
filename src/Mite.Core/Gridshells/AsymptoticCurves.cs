using System;
using System.Collections.Generic;
using Mite.Core.Geometry;
using Mite.Core.Curvature;

namespace Mite.Core.Gridshells;

/// <summary>
/// Asymptotic direction fields and curve tracing. Asymptotic curves follow
/// directions of zero normal curvature and exist only where Gaussian curvature
/// is negative. They are the natural layout for gridshells built from straight
/// flat strips (asymptotic gridshells).
/// </summary>
public static class AsymptoticCurves
{
    public class Options
    {
        public double StepSize { get; set; } = 0.01;
        public int MaxSteps { get; set; } = 1000;

        /// <summary>On-surface Laplacian fairing passes applied to each traced curve (0 disables).</summary>
        public int SmoothingPasses { get; set; } = 10;

        /// <summary>
        /// Traces stop where the blended field magnitude drops below this
        /// fraction of unit length (0..1). Vertices without asymptotic
        /// directions contribute zero to the blend, so this fades traces out
        /// smoothly at the border of the anticlastic region; lower values trace
        /// deeper into near-parabolic areas.
        /// </summary>
        public double MinFieldMagnitude { get; set; } = 0.3;

        /// <summary>
        /// Optional cancellation probe checked between curves; return true to
        /// stop tracing and keep the curves produced so far (e.g. wire this to
        /// the host's Esc-key check so long solves stay interruptible).
        /// </summary>
        public Func<bool>? ShouldCancel { get; set; }

    }

    public readonly struct DirectionField
    {
        public readonly Vec3d[] Family1;
        public readonly Vec3d[] Family2;
        public readonly bool[] Exists;

        public DirectionField(Vec3d[] family1, Vec3d[] family2, bool[] exists)
        {
            Family1 = family1;
            Family2 = family2;
            Exists = exists;
        }
    }

    /// <summary>
    /// Computes the two asymptotic direction fields from principal curvatures.
    /// The normal curvature at angle t from D1 is k1 cos^2(t) + k2 sin^2(t),
    /// which vanishes at tan(t) = +/- sqrt(-k1/k2) when k1 * k2 &lt; 0. The
    /// existence test is relative to the local curvature magnitude: an absolute
    /// epsilon on k1*k2 (units 1/length^2) breaks under model rescaling and
    /// lets near-parabolic noise spawn directions at arbitrary angles.
    /// </summary>
    public static DirectionField ComputeDirections(PrincipalCurvature.Result curvature)
    {
        int nv = curvature.K1.Length;
        var f1 = new Vec3d[nv];
        var f2 = new Vec3d[nv];
        var exists = new bool[nv];

        for (int i = 0; i < nv; i++)
        {
            double k1 = curvature.K1[i], k2 = curvature.K2[i];
            double scale = Math.Max(Math.Abs(k1), Math.Abs(k2));
            if (k1 * k2 >= -1e-6 * scale * scale) continue;

            double t = Math.Atan(Math.Sqrt(-k1 / k2));
            double c = Math.Cos(t), s = Math.Sin(t);
            f1[i] = (c * curvature.D1[i] + s * curvature.D2[i]).Normalized();
            f2[i] = (c * curvature.D1[i] - s * curvature.D2[i]).Normalized();
            exists[i] = true;
        }

        return new DirectionField(f1, f2, exists);
    }

    /// <summary>
    /// Traces asymptotic curves of one family from each seed vertex.
    /// Seeds in regions of non-negative Gaussian curvature are skipped;
    /// traces stop when they leave the anticlastic region.
    /// </summary>
    public static List<Vec3d[]> Trace(
        MeshData mesh, int[] seedVertices, PrincipalCurvature.Result curvature,
        bool secondFamily, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(mesh);
        var field = ComputeDirections(curvature);

        // Both families are passed to the tracer: the family labels are derived
        // from principal directions whose signs are arbitrary per vertex, so a
        // single label does not identify a geometrically consistent family. The
        // tracer keeps continuity by picking the best-aligned candidate; the
        // primary field only selects which family the curve starts in.
        var primary = secondFamily ? field.Family2 : field.Family1;
        var secondary = secondFamily ? field.Family1 : field.Family2;

        var result = new List<Vec3d[]>();
        foreach (int seed in seedVertices)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            if (seed < 0 || seed >= proj.Mesh.VertexCount || !field.Exists[seed]) continue;

            var line = FieldTracer.TraceBoth(proj, proj.Mesh.Vertices[seed], seed,
                primary, secondary, field.Exists, options.StepSize, options.MaxSteps, null,
                options.MinFieldMagnitude);

            if (line.Length > 1)
                result.Add(CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses));
        }

        return result;
    }
}
