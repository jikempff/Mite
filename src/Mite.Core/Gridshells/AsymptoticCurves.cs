// Family labels by the sign of the geodesic torsion (2026-10-06). Sources:
// Beltrami–Enneper theorem (K = −τ² along an asymptotic curve; e.g. Düldül &
// Düldül 2019, Int. Electron. J. Geom. 12(2)); geodesic torsion
// τg = (k2 − k1) sin α cos α (Encyclopaedia of Mathematics, "Geodesic
// torsion"; do Carmo 1976); Vaxman et al. 2016, "Directional field synthesis,
// design, and processing", CGF 35(2), §5.1 (2²-fields: index ±1/2 only);
// Garcia & Sotomayor 2005 (foliations labelled by the sign of τg).
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

        /// <summary>
        /// Every curve follows its own family only (default). The families are
        /// labelled exactly by the sign of their geodesic torsion, so a curve
        /// never needs the other family to stay continuous; false restores the
        /// behaviour of Mite 1.3.0 and earlier, where each sample took whichever family was
        /// best aligned and a curve passing near a flat point could jump.
        /// </summary>
        public bool LockFamily { get; set; } = true;
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
    /// <item>The two families are labelled by the sign of their geodesic
    /// torsion (<see cref="LabelByTwist"/>): Family1 twists one way, Family2
    /// the other, at every vertex, so the labels are consistent over the
    /// whole anticlastic region — around flat points and across separate
    /// saddle regions — without any propagation over the mesh. The mesh
    /// argument is kept for compatibility; it is no longer needed.</item>
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

        LabelByTwist(curvature, f1, f2, exists);
        return new DirectionField(f1, f2, exists);
    }

    /// <summary>
    /// Geodesic torsion of the tangent direction t at vertex i, read from the
    /// curvature tensor: τg(t) = II(t, n × t), i.e. (k2 − k1) sin α cos α for
    /// t at angle α from D1 (Bonnet; Encyclopaedia of Mathematics, "Geodesic
    /// torsion"), in the Darboux frame (t, u = n × t, n) with
    /// n' = −kn t − τg u. Mite's principal curvatures are positive where the
    /// surface bends away from its normal (a cylinder reads k1 = +1/R with
    /// outward normals), the opposite of the textbook II = ⟨x'', n⟩, so the
    /// sum Σ kⱼ (t·Dⱼ)(u·Dⱼ) is negated here: a right-handed helix on a
    /// cylinder reads +sinθ cosθ / R, its Frenet torsion. The value does not depend on the
    /// sign of t, of D1, D2 or of the normal (II and n × t both change sign
    /// with n), nor on whether the frame (D1, D2, n) is right-handed or the
    /// principal curvatures are ordered: only on the surface and the
    /// orientation of space. On an asymptotic direction it equals the Frenet
    /// torsion of the asymptotic curve, ±√−K (Beltrami–Enneper).
    /// </summary>
    public static double GeodesicTorsion(PrincipalCurvature.Result curvature, int i, Vec3d t)
    {
        Vec3d n = curvature.Normals != null && i < curvature.Normals.Length && curvature.Normals[i].LengthSquared > 1e-20
            ? curvature.Normals[i].Normalized()
            : Vec3d.Cross(curvature.D1[i], curvature.D2[i]).Normalized();
        Vec3d u = Vec3d.Cross(n, t);
        Vec3d d1 = curvature.D1[i], d2 = curvature.D2[i];
        return -(curvature.K1[i] * Vec3d.Dot(t, d1) * Vec3d.Dot(u, d1)
               + curvature.K2[i] * Vec3d.Dot(t, d2) * Vec3d.Dot(u, d2));
    }

    /// <summary>
    /// Labels the two asymptotic families by the sign of their geodesic
    /// torsion, vertex by vertex: Family1 is the right-handed family
    /// (τg = +√−K: its asymptotic curves twist like a right-handed screw, as
    /// the rulings (cos φ − s sin φ, sin φ + s cos φ, s) of the hyperboloid
    /// x² + y² − z² = 1), Family2 the left-handed one (τg = −√−K). This is
    /// the label "rotate D1 by +t towards D2" gives in the right-handed
    /// (D1, D2, n) frame with k1 ≥ k2 that
    /// <see cref="PrincipalCurvature.Compute(MeshData, int)"/> returns, so
    /// nets traced before keep their A and B.
    /// <para>
    /// Why this is global and exact: the two asymptotic directions through a
    /// point have geodesic torsions ±√−K, "negatives of one another"
    /// (Beltrami–Enneper: K = −τ² along an asymptotic curve, Düldül &amp;
    /// Düldül 2019; τg(α) = (k2 − k1) sin α cos α is odd in α). τg is continuous and never 0 where K &lt; 0, so
    /// following one family along any path in the anticlastic region keeps
    /// its sign — the families can never exchange, not even around a flat
    /// point (a monkey saddle, the centre of a 3-fold Enneper surface), where
    /// each family turns by a multiple of π per loop; an exchange would need
    /// an index ±1/4 singularity, which a pair of conjugate line fields does
    /// not have (Vaxman et al. 2016 §5.1: 2²-fields have singularities of
    /// index ±1/2). The labels change only on a curve that passes exactly
    /// through a point with K = 0. Garcia &amp; Sotomayor label the two
    /// foliations of their mean-curvature lines the same way, by the sign of
    /// the geodesic torsion.
    /// </para>
    /// <para>
    /// This replaces the breadth-first "combing" of 1.2.2, which compared the
    /// cross of each vertex with its neighbour's by alignment: one wrong
    /// comparison (two vertices whose cross turns by about 45°, as next to a
    /// flat point) relabelled the whole subtree behind it — 575 of 2396
    /// vertices on a 48 × 48 monkey saddle. Labels are unchanged wherever the
    /// combing was right, so saved definitions keep their families.
    /// </para>
    /// </summary>
    internal static void LabelByTwist(PrincipalCurvature.Result curvature, Vec3d[] f1, Vec3d[] f2, bool[] exists)
    {
        for (int i = 0; i < exists.Length; i++)
        {
            if (!exists[i]) continue;
            if (GeodesicTorsion(curvature, i, f1[i]) < GeodesicTorsion(curvature, i, f2[i]))
                (f1[i], f2[i]) = (f2[i], f1[i]);
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

        // The families are labelled exactly (sign of the geodesic torsion), so
        // a curve follows its own family; unlocked, the tracer may pick the
        // best-aligned candidate of either family at every sample.
        var primary = secondFamily ? field.Family2 : field.Family1;
        Vec3d[]? secondary = options.LockFamily ? null : secondFamily ? field.Family1 : field.Family2;

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
