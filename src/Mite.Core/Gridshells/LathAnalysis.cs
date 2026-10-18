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
///
/// Strain model, checked 2026-10-18 against the strip literature:
/// Tang, Kilian, Bo, Wallner &amp; Pottmann 2016 (Analysis and design of
/// curved support structures, AAG 2016, §2) — a strip tangent to the
/// surface develops straight iff kg = 0 (geodesic), a strip orthogonal to
/// it iff kn = 0 (asymptotic), and the Darboux rotation d = τg T − kn g + kg N
/// names the bending axis of each term; Pottmann et al. 2010 (Geodesic
/// patterns, §1): timber panels bend "only about their weak axis" and twist,
/// never sideways, so a flat lath takes kn about its width (fibre t/2), kg
/// about the surface normal (fibre w/2) and τg as twist; Schling, Kilian,
/// Wang, Schikore &amp; Pottmann 2018 (Design and construction of curved
/// support structures with repetitive parameters, AAG 2018): upright
/// lamellas (100 × 1.5 mm steel, 60 × 4 mm plywood in the 2016 prototype)
/// take kg about the weak axis and τg = ½(k2 − k1) sin 2φ as twist, which
/// the FE model applies as strain loads. Fibre strain ε = κ · (depth/2) and
/// the Saint-Venant twist shear γ = k(b/t) · τg · t (Timoshenko &amp; Goodier
/// 1970, §109; <see cref="Fabrication.LathProfile.SectionProperties"/>) are
/// the textbook strip formulas; γ is compared with the normal strain limit
/// through the von Mises equivalent ε_eq = γ / √3. None of the sources
/// contradicts this decomposition; Schling's own timber limits (Repetitive
/// Structures, 2018) were not accessible and are left to the MaxStrain input.
///
/// The geodesic curvature is the only quantity read from the polyline's own
/// turning. It is measured as the total turning in the tangent plane over
/// the window divided by the arc between the mid-points of the two end
/// chords, which is exact for a circle at any sampling (the sum of the
/// exterior angles of an inscribed polygon between two chord mid-points is
/// their arc over R). Reading the angle between two chords of half the
/// window and dividing by their mean length — the previous estimator — was
/// biased by sampling/window as soon as the window was shorter than the
/// polyline's spacing (a 40-point 45° latitude circle on a 48-division unit
/// sphere read kg = 1.36 instead of 1.00; a 30-point torus circle 0.58
/// instead of 0.33), see LathAnalysisTests.
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

        /// <summary>
        /// Optional section (round bar, custom polygon, or a rectangle with
        /// its own orientation). When set it overrides Width, Thickness and
        /// Upright: fibre distances come from the section's extents and the
        /// twist shear from its Saint-Venant twist length, so one profile is
        /// applied to every curve exactly as Lath Sweep builds it.
        /// </summary>
        public Fabrication.LathProfile? Profile { get; set; }
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

            // Darboux frame of the mid tangent
            Vec3d t = (tPrev + tNext);
            if (t.LengthSquared < 1e-20) t = tNext;
            t = t.Normalized();
            Vec3d nrm = normals[i];
            Vec3d g = Vec3d.Cross(nrm, t);
            if (g.LengthSquared > 1e-20) g = g.Normalized();

            // Normal curvature and geodesic torsion from the rotation of the
            // surface normal over the window (N' = -kn T - tau_g g): both read
            // the smooth normal, so facet kinks in the polyline do not leak in
            double ds = lPrev + lNext;
            Vec3d dN = (nf - nb) / ds;
            kn[i] = -Vec3d.Dot(dN, t);
            tg[i] = -Vec3d.Dot(dN, g);
        }

        GeodesicCurvatureByTurning(polyline, normals, arc, closed, window, kg);

        // Strain per mode. Flat strip: kn bends about the width axis (fiber
        // distance t/2), kg about the surface normal (fiber distance w/2).
        // Upright strip: the two swap. Twist gives a surface shear strain
        // γ = τ · TwistLength (t for a thin rectangle, d/2 for a round bar —
        // LathProfile.SectionProperties), compared against the (normal)
        // strain limit through the von Mises equivalence ε_eq = γ / √3.
        var profile = options.Profile ?? new Fabrication.LathProfile(options.Width, options.Thickness, options.Upright);
        var sp = profile.SectionProperties();
        bool upright = profile.Upright;
        double easyHalf = sp.ExtentB; // second axis: the surface normal in Flat mode
        double hardHalf = sp.ExtentA; // first axis: across the curve in Flat mode
        double twistFactor = sp.TwistLength / Math.Sqrt(3.0);

        double maxUtil = 0.0;
        for (int i = 0; i < n; i++)
        {
            double easyK = upright ? kg[i] : kn[i];
            double hardK = upright ? kn[i] : kg[i];

            double strain = Math.Max(
                Math.Abs(easyK) * easyHalf,
                Math.Max(Math.Abs(hardK) * hardHalf, Math.Abs(tg[i]) * twistFactor));

            util[i] = strain / options.MaxStrain;
            if (util[i] > maxUtil) maxUtil = util[i];
        }

        return new Result(kg, kn, tg, util, maxUtil, maxUtil <= 1.0);
    }

    /// <summary>
    /// Geodesic curvature per vertex as the in-surface turning over the window.
    /// At every vertex the incoming and outgoing segments are projected into
    /// the tangent plane of the smooth normal and their signed angle φ (about
    /// the normal, positive towards g = N × T) is the vertex's in-plane
    /// turning — the discrete development of the curve. For vertex i the
    /// window is widened to whole vertices on both sides (at least one each,
    /// never beyond the lath's interior, never round a closed loop onto
    /// itself), and kg = Σ φ over those vertices divided by the arc between
    /// the mid-points of the chord before the first and the chord after the
    /// last of them. For an inscribed polygon of a circle that quotient is
    /// exactly 1/R whatever the sampling. End points of an open lath are 0.
    /// </summary>
    internal static void GeodesicCurvatureByTurning(Vec3d[] polyline, Vec3d[] normals, double[] arc, bool closed, double window, double[] kg)
    {
        int n = polyline.Length;
        int m = closed ? n - 1 : n; // distinct vertices
        if (m < 3) return;

        double Seg(int j) { j %= m; if (j < 0) j += m; return arc[j + 1] - arc[j]; } // segment j → j + 1
        int Wrap(int j) { j %= m; if (j < 0) j += m; return j; }

        var phi = new double[m];
        for (int j = 0; j < m; j++)
        {
            if (!closed && (j == 0 || j == m - 1)) continue;
            Vec3d nj = normals[j];
            Vec3d a = polyline[j] - polyline[Wrap(j - 1)]; a = a - Vec3d.Dot(a, nj) * nj;
            Vec3d b = polyline[Wrap(j + 1)] - polyline[j]; b = b - Vec3d.Dot(b, nj) * nj;
            if (a.LengthSquared < 1e-30 || b.LengthSquared < 1e-30) continue;
            phi[j] = Math.Atan2(Vec3d.Dot(nj, Vec3d.Cross(a, b)), Vec3d.Dot(a, b));
        }

        // Chord → arc: a chord whose end tangents turn by θ spans an arc of
        // l · (θ/2) / sin(θ/2); θ is taken as the mean of the two end turnings.
        // Without it a 12-point 45° latitude circle read kg 1.5 % high.
        var segArc = new double[closed ? m : n - 1];
        for (int j = 0; j < segArc.Length; j++)
        {
            double theta = 0.5 * (phi[j] + phi[Wrap(j + 1)]);
            double f = Math.Abs(theta) > 1e-6 ? (0.5 * theta) / Math.Sin(0.5 * theta) : 1.0 + theta * theta / 24.0;
            segArc[j] = Seg(j) * f;
        }
        double SegA(int j) { j %= m; if (j < 0) j += m; return segArc[j]; }

        double half = 0.5 * window;
        for (int i = 0; i < m; i++)
        {
            if (!closed && (i == 0 || i == m - 1)) continue;
            int kb = 0, kf = 0;
            double sBack = 0, sFwd = 0;
            bool CanBack() => closed ? kb + kf < m - 2 : i - kb - 1 >= 1;
            bool CanFwd() => closed ? kb + kf < m - 2 : i + kf + 1 <= m - 2;
            while ((kb == 0 || sBack < half) && CanBack()) { kb++; sBack += SegA(i - kb); }
            while ((kf == 0 || sFwd < half) && CanFwd()) { kf++; sFwd += SegA(i + kf - 1); }
            if (kb == 0 && kf == 0) continue;

            double sum = 0;
            for (int k = -kb; k <= kf; k++) sum += phi[Wrap(i + k)];
            // arc from the mid-point of the chord before vertex i−kb to the
            // mid-point of the chord after vertex i+kf
            double len = 0.5 * SegA(i - kb - 1) + 0.5 * SegA(i + kf);
            for (int k = -kb; k < kf; k++) len += SegA(i + k);
            if (len > 1e-15) kg[i] = sum / len;
        }
        if (closed) kg[n - 1] = kg[0];
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
