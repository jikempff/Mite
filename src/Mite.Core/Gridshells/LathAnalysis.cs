using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Buildability analysis for gridshell laths. Given a polyline lying on a mesh,
/// decomposes its bending into the Darboux frame: geodesic curvature (in-surface
/// bend), normal curvature (out-of-surface bend), and geodesic torsion (twist).
/// These map directly onto the bending modes of a rectangular strip, so the
/// analysis reports whether a lath of given width, thickness, and material
/// strain limit can physically be bent along the curve.
///
/// Darboux frame (T, g = N × T, N) along a surface curve (do Carmo 1976,
/// Differential Geometry of Curves and Surfaces, §3-2; Schling, Hitrec &amp;
/// Barthel 2017, Designing Grid Structures Using Asymptotic Curve Networks):
///   T' = kg g + kn N,   N' = −kn T − τg g.
/// Normal curvature and geodesic torsion are therefore both read from the
/// rotation of the surface normal along the curve (kn = −N'·T, τg = −N'·g),
/// using the smooth interpolated normal of the mesh; only the geodesic
/// curvature needs the turning of the polyline itself. Reading kn from the
/// polyline's second differences instead is dominated by facet noise: on a
/// 64-gon hyperboloid the straight rulings (kn = 0 exactly) came out with
/// |kn| up to 0.18 while √−K = 0.87, i.e. an upright lath was reported at
/// utilization 1.8 where the truth is 0; the normal-rotation form gives
/// |kn| ≤ 0.004 on the same curve and is 10–100× closer to the analytic value
/// on cylinder, torus and sphere test curves as well (see RuledSurfaceTests).
/// On an asymptotic curve τg² = −K (Beltrami–Enneper), so the twist of a
/// straight asymptotic lath is set by the Gaussian curvature alone.
/// </summary>
public static class LathAnalysis
{
    public class Options
    {
        /// <summary>Strip cross-section dimension lying across the curve (default 0.1).</summary>
        public double Width { get; set; } = 0.1;

        /// <summary>Strip cross-section dimension through the strip (default 0.01).</summary>
        public double Thickness { get; set; } = 0.01;

        /// <summary>Maximum allowable bending strain, e.g. sigma/E (default 0.005).</summary>
        public double MaxStrain { get; set; } = 0.005;

        /// <summary>
        /// False: strip lies flat on the surface (geodesic gridshells) — normal
        /// curvature is the easy bending mode. True: strip stands upright,
        /// perpendicular to the surface (asymptotic gridshells) — geodesic
        /// curvature is the easy mode.
        /// </summary>
        public bool Upright { get; set; }

        /// <summary>
        /// Arc length over which curvature and torsion are measured (0 =
        /// automatic: twice the average mesh edge length). A polyline that lies
        /// on a faceted mesh turns by the dihedral angle at every facet edge,
        /// so vertex-to-vertex curvature spikes at the facet scale even on a
        /// perfect circle; measuring across a window the size of a few facets
        /// recovers the curvature of the underlying surface curve. Normal
        /// curvature and geodesic torsion are read from the smooth normal over
        /// the same window, so the window mainly governs the geodesic
        /// curvature's noise.
        /// </summary>
        public double Window { get; set; } = 0.0;
    }

    public readonly struct Result
    {
        /// <summary>In-surface bending per vertex (endpoints zero).</summary>
        public readonly double[] GeodesicCurvature;

        /// <summary>Out-of-surface bending per vertex (endpoints zero).</summary>
        public readonly double[] NormalCurvature;

        /// <summary>Twist rate per vertex.</summary>
        public readonly double[] GeodesicTorsion;

        /// <summary>Peak strain over all modes per vertex, as a fraction of MaxStrain.</summary>
        public readonly double[] Utilization;

        public readonly double MaxUtilization;
        public readonly bool Buildable;

        public Result(double[] kg, double[] kn, double[] tg, double[] utilization,
            double maxUtilization, bool buildable)
        {
            GeodesicCurvature = kg;
            NormalCurvature = kn;
            GeodesicTorsion = tg;
            Utilization = utilization;
            MaxUtilization = maxUtilization;
            Buildable = buildable;
        }
    }

    /// <summary>Analyzes one lath. Reuse a single MeshProjection for many laths.</summary>
    public static Result Analyze(MeshProjection proj, Vec3d[] polyline, Options? options = null)
    {
        options ??= new Options();
        int n = polyline.Length;

        var kg = new double[n];
        var kn = new double[n];
        var tg = new double[n];
        var util = new double[n];

        if (n < 2)
            return new Result(kg, kn, tg, util, 0.0, true);

        // Smooth surface normals along the lath (face normals jump at facet
        // crossings, which would corrupt the torsion finite difference)
        var normals = new Vec3d[n];
        int hint = proj.NearestVertexGlobal(polyline[0]);
        for (int i = 0; i < n; i++)
        {
            var hit = proj.ClosestPoint(polyline[i], hint);
            normals[i] = hit.SmoothNormal;
            hint = hit.NearestVertex;
        }

        bool closed = n > 3 && (polyline[0] - polyline[n - 1]).LengthSquared < 1e-18;
        double window = options.Window > 0 ? options.Window : 2.0 * proj.AverageEdgeLength;

        // Cumulative arc length
        var arc = new double[n];
        for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + (polyline[i] - polyline[i - 1]).Length;
        double total = arc[n - 1];

        // Point and normal at a signed arc position, wrapping for closed laths
        // and clamping to the ends of open ones
        (Vec3d p, Vec3d nrm, double s) At(double target)
        {
            if (closed)
            {
                target %= total;
                if (target < 0) target += total;
            }
            else target = Math.Max(0.0, Math.Min(total, target));
            int k = 1;
            while (k < n - 1 && arc[k] < target) k++;
            double segLen = arc[k] - arc[k - 1];
            double t = segLen > 1e-15 ? (target - arc[k - 1]) / segLen : 0.0;
            Vec3d nn = ((1 - t) * normals[k - 1] + t * normals[k]).Normalized();
            return (polyline[k - 1] + t * (polyline[k] - polyline[k - 1]), nn, target);
        }

        for (int i = 0; i < n; i++)
        {
            double s0 = arc[i];
            double half = 0.5 * window;
            double sBack = s0 - half, sFwd = s0 + half;
            if (!closed)
            {
                // Endpoints of an open lath: one-sided windows
                sBack = Math.Max(0.0, sBack);
                sFwd = Math.Min(total, sFwd);
                if (sFwd - sBack < 1e-12) continue;
            }
            var (pb, nb, _) = At(sBack);
            var (pf, nf, _) = At(sFwd);
            Vec3d pi = polyline[i];

            Vec3d ePrev = pi - pb, eNext = pf - pi;
            double lPrev = ePrev.Length, lNext = eNext.Length;
            if (lPrev < 1e-15 || lNext < 1e-15) continue;
            Vec3d tPrev = ePrev / lPrev, tNext = eNext / lNext;

            // Geodesic curvature: in-surface part of the polyline's turning
            // angle over the window, in the Darboux frame of the mid tangent
            double dot = Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(tPrev, tNext)));
            double kappa = Math.Acos(dot) / (0.5 * (lPrev + lNext));
            Vec3d t = (tPrev + tNext);
            if (t.LengthSquared < 1e-20) t = tNext;
            t = t.Normalized();
            Vec3d nrm = normals[i];
            Vec3d g = Vec3d.Cross(nrm, t);
            if (g.LengthSquared > 1e-20) g = g.Normalized();
            Vec3d bend = tNext - tPrev;
            if (bend.LengthSquared > 1e-20)
            {
                bend = bend.Normalized();
                kg[i] = kappa * Vec3d.Dot(bend, g);
            }

            // Normal curvature and geodesic torsion from the rotation of the
            // surface normal over the window (N' = -kn T - tau_g g): both read
            // the smooth normal, so facet kinks in the polyline do not leak in
            double ds = lPrev + lNext;
            Vec3d dN = (nf - nb) / ds;
            kn[i] = -Vec3d.Dot(dN, t);
            tg[i] = -Vec3d.Dot(dN, g);
        }

        // Strain per mode. Flat strip: kn bends about the width axis (fiber
        // distance t/2), kg about the surface normal (fiber distance w/2).
        // Upright strip: the two swap. Twist of a thin rectangle gives a
        // surface shear strain γ ≈ τ·t, compared against the (normal) strain
        // limit through the von Mises equivalence ε_eq = γ / √3.
        double easyHalf = 0.5 * options.Thickness;
        double hardHalf = 0.5 * options.Width;
        double twistFactor = options.Thickness / Math.Sqrt(3.0);

        double maxUtil = 0.0;
        for (int i = 0; i < n; i++)
        {
            double easyK = options.Upright ? kg[i] : kn[i];
            double hardK = options.Upright ? kn[i] : kg[i];

            double strain = Math.Max(
                Math.Abs(easyK) * easyHalf,
                Math.Max(Math.Abs(hardK) * hardHalf, Math.Abs(tg[i]) * twistFactor));

            util[i] = strain / options.MaxStrain;
            if (util[i] > maxUtil) maxUtil = util[i];
        }

        return new Result(kg, kn, tg, util, maxUtil, maxUtil <= 1.0);
    }

    /// <summary>Analyzes many laths against one mesh.</summary>
    public static List<Result> Analyze(MeshData mesh, IEnumerable<Vec3d[]> polylines, Options? options = null)
    {
        var proj = new MeshProjection(mesh);
        var results = new List<Result>();
        foreach (var line in polylines)
            results.Add(Analyze(proj, line, options));
        return results;
    }
}
