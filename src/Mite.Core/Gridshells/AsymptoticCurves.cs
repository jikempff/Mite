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
        /// <summary>Integration step (0 = automatic, from the mesh edge length).</summary>
        public double StepSize { get; set; } = 0.0;

        /// <summary>Steps per half-curve (0 = automatic, from the mesh size).</summary>
        public int MaxSteps { get; set; } = 0;

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

        /// <summary>
        /// Minimum crossing angle (degrees) between the two families for a
        /// vertex to count as usable anticlastic region; see
        /// <see cref="ComputeDirections(PrincipalCurvature.Result, MeshData?, double)"/>.
        /// </summary>
        public double MinCrossingAngle { get; set; } = 15.0;
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
    public static DirectionField ComputeDirections(PrincipalCurvature.Result curvature) =>
        ComputeDirections(curvature, null, 0.0);

    /// <summary>
    /// As above, with two refinements for nets:
    /// <list type="bullet">
    /// <item>When a mesh is given, the principal direction field is combed
    /// first (sign propagation over the vertex graph from the vertex with the
    /// strongest anticlastic curvature). Family 1 is "rotate D1 by +t", so the
    /// labels of the two families depend on the sign of D1, which is arbitrary
    /// per vertex; without combing the same geometric family carries both
    /// labels across the surface and traced nets colour their families at
    /// random. Combing is exact away from umbilics; around an umbilic a seam
    /// remains (a line field of index 1/2 cannot be combed), which the tracer
    /// crosses by continuity.</item>
    /// <item>minCrossingAngle (degrees) restricts Exists to vertices where the
    /// two families cross at more than that angle (the angle is 2t). Near the
    /// parabolic line K = 0 the families collapse onto each other: curves
    /// traced there swap families at will and the resulting laths would cross
    /// at a few degrees, which no joint can build. 10–20° is a practical
    /// threshold; 0 keeps the raw K &lt; 0 region.</item>
    /// </list>
    /// </summary>
    public static DirectionField ComputeDirections(PrincipalCurvature.Result curvature, MeshData? mesh, double minCrossingAngle)
    {
        int nv = curvature.K1.Length;
        var f1 = new Vec3d[nv];
        var f2 = new Vec3d[nv];
        var exists = new bool[nv];
        double minHalf = 0.5 * Math.Max(0.0, minCrossingAngle) * Math.PI / 180.0;

        var d1 = curvature.D1;
        var d2 = curvature.D2;

        // Flat points (k1 ≈ k2 ≈ 0 to rounding) pass a purely relative test with
        // directions made of noise; compare with the mesh's own curvature scale
        double kRef = 0;
        for (int i = 0; i < nv; i++) kRef += Math.Max(Math.Abs(curvature.K1[i]), Math.Abs(curvature.K2[i]));
        kRef = nv > 0 ? kRef / nv : 0;
        double kFloor = 1e-3 * kRef;

        for (int i = 0; i < nv; i++)
        {
            double k1 = curvature.K1[i], k2 = curvature.K2[i];
            double scale = Math.Max(Math.Abs(k1), Math.Abs(k2));
            if (scale <= kFloor) continue;
            if (k1 * k2 >= -1e-6 * scale * scale) continue;

            double t = Math.Atan(Math.Sqrt(-k1 / k2));
            // t in (0, pi/2); the families cross at 2t, or pi - 2t when t > pi/4
            double crossing = Math.Min(t, Math.PI / 2 - t);
            if (crossing < minHalf) continue;
            double c = Math.Cos(t), s = Math.Sin(t);
            f1[i] = (c * d1[i] + s * d2[i]).Normalized();
            f2[i] = (c * d1[i] - s * d2[i]).Normalized();
            exists[i] = true;
        }

        if (mesh != null && mesh.VertexCount == nv) CombFamilies(mesh, f1, f2, exists);
        return new DirectionField(f1, f2, exists);
    }

    /// <summary>
    /// Makes the family labels continuous over the usable region: breadth-first
    /// from the most anticlastic vertex of each connected region, every vertex
    /// takes as "family 1" whichever of its two asymptotic lines is closer to
    /// family 1 of the neighbour that reached it. The raw labels ("rotate D1 by
    /// +t") swap wherever the sign of D1 flips and wherever k1 and k2 exchange
    /// roles (D1 turns by 90°), which happens all over a general surface.
    /// </summary>
    internal static void CombFamilies(MeshData mesh, Vec3d[] f1, Vec3d[] f2, bool[] exists)
    {
        int nv = mesh.VertexCount;
        var nbrs = mesh.BuildVertexNeighbors();
        var done = new bool[nv];
        var queue = new Queue<int>();
        var K = new double[nv];
        for (int i = 0; i < nv; i++) K[i] = exists[i] ? -Math.Abs(Vec3d.Dot(f1[i], f2[i])) : 1; // most orthogonal families first
        for (int start = 0; start < nv; start++)
        {
            if (done[start] || !exists[start]) continue;
            // find the component's best-conditioned vertex
            int best = start;
            var comp = new List<int>();
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>(); stack.Push(start);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                comp.Add(v);
                if (K[v] < K[best]) best = v;
                foreach (int n in nbrs[v]) if (exists[n] && !done[n] && seen.Add(n)) stack.Push(n);
            }
            done[best] = true;
            queue.Enqueue(best);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int n in nbrs[v])
                {
                    if (done[n] || !exists[n]) continue;
                    double same = Math.Abs(Vec3d.Dot(f1[v], f1[n]));
                    double other = Math.Abs(Vec3d.Dot(f1[v], f2[n]));
                    if (other > same) { var t = f1[n]; f1[n] = f2[n]; f2[n] = t; }
                    done[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
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
        var field = ComputeDirections(curvature, mesh, options.MinCrossingAngle);

        // Both families are passed to the tracer: the family labels are derived
        // from principal directions whose signs are arbitrary per vertex, so a
        // single label does not identify a geometrically consistent family. The
        // tracer keeps continuity by picking the best-aligned candidate; the
        // primary field only selects which family the curve starts in.
        var primary = secondFamily ? field.Family2 : field.Family1;
        var secondary = secondFamily ? field.Family1 : field.Family2;

        double step = TraceDefaults.ResolveStep(options.StepSize, 0, proj);
        int maxSteps = TraceDefaults.ResolveMaxSteps(options.MaxSteps, step, proj);

        var result = new List<Vec3d[]>();
        foreach (int seed in seedVertices)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            if (seed < 0 || seed >= proj.Mesh.VertexCount || !field.Exists[seed]) continue;

            var line = FieldTracer.TraceBoth(proj, proj.Mesh.Vertices[seed], seed,
                primary, secondary, field.Exists, step, maxSteps, null,
                options.MinFieldMagnitude);

            if (line.Length > 1)
                result.Add(CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses));
        }

        return result;
    }
}
