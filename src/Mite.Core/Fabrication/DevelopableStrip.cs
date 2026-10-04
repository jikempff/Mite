using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// The strip a thin straight plank of width W forms along a lath: the
/// developable surface that contains the lath and stands perpendicular to the
/// reference surface (upright lath) or lies tangent to it (flat lath), where
/// that developable exists, and the least-strained twisted strip where it
/// does not.
///
/// Sources:
/// - Tang, Kilian, Bo, Wallner &amp; Pottmann 2016, "Analysis and design of
///   curved support structures" (Advances in Architectural Geometry): with the
///   sideways vector u = n × t, the developable orthogonal to the surface along
///   a curve has rulings r = κg n + τg t, the one tangent to it r = τg t − κn u;
///   the orthogonal strip develops straight on an asymptotic curve (κn = 0),
///   the tangent one on a geodesic (κg = 0).
/// - Schling, Wang, Hoyer &amp; Pottmann 2022, "Designing asymptotic geodesic
///   hybrid gridshells" (CAD 152, 103378) §3.4: the laths of an asymptotic /
///   geodesic gridshell as rectifying developables of their curves, second
///   boundary c̄ = c + w / (r_D·e₃) · r_D, Darboux vector r_D = τ e₁ + κ e₃.
/// - Starostin &amp; van der Heijden 2007, "The shape of a Möbius strip" (Nature
///   Materials 6): x(s, v) = r + v (b + η t), η = τ/κ, rulings at
///   β = arctan(1/η) to the tangent, regular while |w η'| &lt; 1; the centreline
///   is a geodesic of the strip, so the strip develops into a straight band.
/// - Freddi, Hornung, Mora &amp; Paroni 2016, "A corrected Sadowsky functional
///   for inextensible elastic ribbons" (J. Elasticity 123): the narrow-ribbon
///   limit is the rectifying developable only while |κ| ≥ |τ| (rulings within
///   45° of square); for |τ| &gt; |κ| the energy is 4τ² and the ribbon is no
///   longer that developable. Where κ = 0 and τ ≠ 0 (an inflection, e.g. an
///   asymptotic lath crossing the waist of a catenoid, or any straight twisted
///   lath) no developable strip exists at all.
///
/// Discretisation: every segment e_i spans a plane P_i with the strip
/// direction a_i (the bisected surface normal for upright laths, n̄ × e for
/// flat ones). The developable ruling at a station is the line P_{i−1} ∩ P_i,
/// so a quad between two such rulings is planar and develops exactly. Where
/// that ruling leans more than 45° from square (Freddi's twist regime) or the
/// boundaries would run backwards (edge of regression inside the strip) the
/// ruling is held at the limit angle in the plane of the tangent and the strip
/// direction — among such rulings the one with the least edge strain, which
/// for a curve with κ_m = 0 is the twist (W τ)²/8 of a helicoid — and the quad
/// is twisted. The pattern lays the planar quads down exactly (by their two
/// triangles); a twisted quad has no exact development and is laid down with
/// the lath's turning inside the strip's plane. Boundaries sit at ±W/2 across
/// the developed band.
/// </summary>
public static class DevelopableStrip
{
    public sealed class Options
    {
        /// <summary>Strip (plank) width W, measured across the developed band (default 0.1).</summary>
        public double Width { get; set; } = 0.1;

        /// <summary>True: the strip stands perpendicular to the surface (asymptotic laths). False: it lies on the surface (geodesic laths).</summary>
        public bool Upright { get; set; } = true;

        /// <summary>
        /// Largest lean of a ruling from square, degrees (default 45, the
        /// developable regime |κ| ≥ |τ| of Freddi et al. 2016). 90 accepts
        /// every developable ruling that clears its edge of regression; 0 gives
        /// the square ruled strip of Lath Unroll.
        /// </summary>
        public double MaxLean { get; set; } = 45.0;

        /// <summary>
        /// Arc length over which the two planes that meet in a ruling are
        /// measured on each side of a station (0 = one segment, the exact
        /// discrete developable; the mesh overload uses four mean mesh edges
        /// when 0, because a lath traced on facets turns at the facet scale and
        /// rulings — a second-derivative quantity — would follow that noise).
        /// </summary>
        public double Window { get; set; } = 0.0;
    }

    public sealed class Result
    {
        /// <summary>Lath stations (a closed lath repeats its first station at the end).</summary>
        public Vec3d[] Centre = Array.Empty<Vec3d>();

        /// <summary>Strip boundary at −W/2 (opposite the strip direction).</summary>
        public Vec3d[] EdgeA = Array.Empty<Vec3d>();

        /// <summary>Strip boundary at +W/2 (the side of the surface normal for upright strips, of n × t for flat ones).</summary>
        public Vec3d[] EdgeB = Array.Empty<Vec3d>();

        /// <summary>Unit ruling per station, pointing to EdgeB.</summary>
        public Vec3d[] Rulings = Array.Empty<Vec3d>();

        /// <summary>Angle between the developable's ruling and the lath per station, degrees (90 = square; before any limit is applied).</summary>
        public double[] RulingAngle = Array.Empty<double>();

        /// <summary>True where the station carries the developable's ruling, false where the ruling is held at the limit (twist regime).</summary>
        public bool[] OnDevelopable = Array.Empty<bool>();

        /// <summary>3D strip: one quad per segment (EdgeA_k, EdgeA_k+1, EdgeB_k+1, EdgeB_k).</summary>
        public MeshData Strip = new(Array.Empty<Vec3d>(), Array.Empty<int[]>());

        /// <summary>Flat pattern (z = 0): centreline and both boundaries.</summary>
        public Vec3d[] FlatCentre = Array.Empty<Vec3d>();
        public Vec3d[] FlatA = Array.Empty<Vec3d>();
        public Vec3d[] FlatB = Array.Empty<Vec3d>();

        /// <summary>Flat pattern mesh, same faces as Strip.</summary>
        public MeshData FlatMesh = new(Array.Empty<Vec3d>(), Array.Empty<int[]>());

        public bool Closed;

        /// <summary>3D length of the lath.</summary>
        public double Length;

        public double Width;

        /// <summary>Share of the lath length whose quads are planar pieces of the developable (0..1).</summary>
        public double DevelopableFraction;

        /// <summary>True when the whole strip is the developable (DevelopableFraction = 1).</summary>
        public bool Developable;

        /// <summary>Lean from square at which twisted stations hold their rulings, degrees (MaxLean, lowered when the strip would pass its edge of regression).</summary>
        public double LeanUsed;

        /// <summary>Largest lean of the developable's rulings from square, degrees (90 where it does not exist).</summary>
        public double MaxDevelopableLean;

        /// <summary>
        /// Smallest ratio of boundary advance to centreline advance over both
        /// boundaries (the discrete 1 + v(η' − κ(1 + η²)) of the smooth strip);
        /// positive for a regular strip.
        /// </summary>
        public double MinAdvance;

        /// <summary>
        /// Largest curvature of the developed centreline on the developable
        /// stretches: the in-plane bend a straight plank needs there (κn for
        /// upright, κg for flat laths). Twisted stretches are left out — a
        /// non-developable strip has no exact flat pattern, and the one given
        /// there (the lath's in-plane turning) is only an approximation.
        /// </summary>
        public double MaxDevelopedCurvature;

        /// <summary>Largest distance of the developed centreline from the chord through its ends.</summary>
        public double Bow;

        /// <summary>
        /// Largest mean elongation of the two edges against the centreline
        /// over all quads: the membrane strain a flat plank needs to take the
        /// strip. 0 on the developable; (W τ)²/8 on a straight twisted lath.
        /// </summary>
        public double EdgeStrain;

        /// <summary>Largest distance between the diagonals of a developable quad (round-off for an exact developable).</summary>
        public double MaxPlanarity;

        /// <summary>Largest distance between the diagonals of any quad.</summary>
        public double MaxTwistGap;

        /// <summary>Empty, or what kept parts of the strip off the developable.</summary>
        public string Note = "";
    }

    /// <summary>Builds the strip along a lath on a mesh; normals are the mesh's smooth normals at the stations.</summary>
    public static Result? Build(MeshProjection proj, Vec3d[] polyline, Options? options = null)
    {
        if (polyline == null || polyline.Length < 2) return null;
        var normals = new Vec3d[polyline.Length];
        int hint = proj.NearestVertexGlobal(polyline[0]);
        if (hint < 0) return null;
        for (int i = 0; i < polyline.Length; i++)
        {
            var hit = proj.ClosestPoint(polyline[i], hint);
            normals[i] = hit.SmoothNormal;
            hint = hit.NearestVertex;
        }
        options ??= new Options();
        if (options.Window <= 0)
            options = new Options { Width = options.Width, Upright = options.Upright, MaxLean = options.MaxLean, Window = 4.0 * proj.AverageEdgeLength };
        return Build(polyline, normals, options);
    }

    /// <summary>
    /// Builds the strip along a polyline with a surface normal per vertex.
    /// Returns null for fewer than two distinct points.
    /// </summary>
    public static Result? Build(Vec3d[] polyline, Vec3d[] normals, Options? options = null)
    {
        options ??= new Options();
        if (options.Width <= 0) throw new ArgumentException("Strip width must be positive.", nameof(options));
        if (polyline == null || normals == null || normals.Length != polyline.Length) return null;

        var pts = new List<Vec3d>(polyline.Length);
        var nrm = new List<Vec3d>(polyline.Length);
        for (int i = 0; i < polyline.Length; i++)
        {
            if (pts.Count > 0 && (polyline[i] - pts[pts.Count - 1]).LengthSquared <= 1e-24) continue;
            pts.Add(polyline[i]);
            nrm.Add(normals[i]);
        }
        if (pts.Count < 2) return null;
        bool closed = pts.Count > 3 && (pts[0] - pts[pts.Count - 1]).LengthSquared < 1e-18;
        if (closed) { pts.RemoveAt(pts.Count - 1); nrm.RemoveAt(nrm.Count - 1); }
        int n = pts.Count;
        int segs = closed ? n : n - 1;
        for (int i = 1; i < n; i++)
            if (Vec3d.Dot(nrm[i], nrm[i - 1]) < 0) nrm[i] = -nrm[i];

        // Segment frames: e (unit), a (strip direction, unit, ⟂ e), m = e × a (plane normal)
        var e = new Vec3d[segs];
        var len = new double[segs];
        var a = new Vec3d[segs];
        var m = new Vec3d[segs];
        for (int i = 0; i < segs; i++)
        {
            int j = (i + 1) % n;
            Vec3d d = pts[j] - pts[i];
            len[i] = d.Length;
            e[i] = d / len[i];
            Vec3d nb = nrm[i] + nrm[j];
            if (nb.LengthSquared < 1e-20) nb = nrm[i];
            nb = nb - Vec3d.Dot(nb, e[i]) * e[i];
            if (nb.LengthSquared < 1e-24) nb = AnyPerpendicular(e[i]);
            nb = nb.Normalized();
            a[i] = options.Upright ? nb : Vec3d.Cross(nb, e[i]).Normalized();
            m[i] = Vec3d.Cross(e[i], a[i]).Normalized();
        }

        // Arc-length stations for the windows
        var arc = new double[n + 1];
        for (int i = 0; i < segs; i++) arc[i + 1] = arc[i] + len[i];
        double total = arc[segs];
        double half = Math.Max(0, options.Window) * 0.5;

        // Station tangent t and strip direction s (⟂ t), and the developable ruling
        var t = new Vec3d[n];
        var s = new Vec3d[n];
        var r = new Vec3d[n];
        var theta = new double[n];      // developable ruling angle to the lath, radians in [0, π]
        var exists = new bool[n];
        for (int i = 0; i < n; i++)
        {
            bool interior = closed || (i > 0 && i < n - 1);
            int pi = interior ? (i - 1 + segs) % segs : (i == 0 ? 0 : segs - 1);
            int ni = interior ? i % segs : pi;
            Vec3d tt = e[pi] + e[ni];
            if (tt.LengthSquared < 1e-20) tt = e[ni];
            tt = tt.Normalized();
            Vec3d ss = a[pi] + a[ni];
            ss = ss - Vec3d.Dot(ss, tt) * tt;
            if (ss.LengthSquared < 1e-24) ss = a[ni] - Vec3d.Dot(a[ni], tt) * tt;
            if (ss.LengthSquared < 1e-24) ss = AnyPerpendicular(tt);
            ss = ss.Normalized();
            t[i] = tt; s[i] = ss;
            if (!interior) continue;   // end of an open lath: the frame of its end segment

            // Planes behind and ahead of the station, each over `half` of arc length —
            // equal on both sides (shortened near the ends of an open lath) so the
            // station's frame is not biased by the curvature
            double hi = closed ? half : Math.Min(half, Math.Min(arc[i], total - arc[i]));
            var (eb, ab) = WindowPlane(pts, a, len, closed, i, -1, hi);
            var (ef, af) = WindowPlane(pts, a, len, closed, i, +1, hi);
            // the smooth frame of the station: window chords and strip directions averaged
            Vec3d tw = eb + ef;
            if (tw.LengthSquared > 1e-20)
            {
                tw = tw.Normalized();
                Vec3d sw = ab + af;
                sw = sw - Vec3d.Dot(sw, tw) * tw;
                if (sw.LengthSquared > 1e-20) { t[i] = tw; s[i] = sw.Normalized(); }
            }
            Vec3d mb = Vec3d.Cross(eb, ab), mf = Vec3d.Cross(ef, af);
            Vec3d c = Vec3d.Cross(mb, mf);
            if (c.Length < 1e-12)
            {
                // Coplanar neighbours: any line in the common plane is a ruling; take the square one.
                r[i] = s[i]; theta[i] = Math.PI / 2; exists[i] = true;
                continue;
            }
            c = c.Normalized();
            if (Vec3d.Dot(c, s[i]) < 0) c = -c;
            double al = Math.Atan2(Vec3d.Dot(c, ab), Vec3d.Dot(c, eb));
            double be = Math.Atan2(Vec3d.Dot(c, af), Vec3d.Dot(c, ef));
            exists[i] = al > 0 && al < Math.PI && be > 0 && be < Math.PI;
            r[i] = c;
            theta[i] = exists[i] ? 0.5 * (al + be) : (Vec3d.Dot(c, t[i]) > 0 ? 0 : Math.PI);
        }
        if (!closed && half > 0 && n > 2)
        {
            // Stations closer than half a window to an end see a truncated window and
            // would carry facet noise: they take the ruling of the nearest station with
            // full windows (a station's own tangent is noisier than the window planes).
            int first = -1, last = -1;
            for (int i = 1; i < n - 1; i++)
                if (arc[i] >= half && total - arc[i] >= half) { if (first < 0) first = i; last = i; }
            if (first < 0) { first = last = n / 2; }
            for (int i = 0; i < n; i++)
            {
                if (i >= first && i <= last) continue;
                int src = i < first ? first : last;
                theta[i] = theta[src]; exists[i] = exists[src];
                r[i] = r[src]; t[i] = t[src]; s[i] = s[src];
            }
        }
        else if (!closed)
        {
            if (n == 2)
            {
                r[0] = r[1] = s[0];
                theta[0] = theta[1] = Math.PI / 2;
                exists[0] = exists[1] = true;
            }
            else
            {
                // End rulings parallel to their neighbour, in the end plane
                r[0] = r[1]; theta[0] = theta[1]; exists[0] = exists[1];
                r[n - 1] = r[n - 2]; theta[n - 1] = theta[n - 2]; exists[n - 1] = exists[n - 2];
            }
        }

        int ns = closed ? n + 1 : n;
        double w = options.Width;
        double maxLean = Math.Max(0, Math.Min(90, options.MaxLean));
        double devLean = 0;
        for (int i = 0; i < n; i++) devLean = Math.Max(devLean, exists[i] ? Math.Abs(90 - theta[i] * 180 / Math.PI) : 90);

        // Lower the admissible lean until both boundaries advance everywhere
        Attempt? best = null;
        for (double lean = maxLean; ; lean -= 5)
        {
            if (lean < 0) lean = 0;
            var at = Place(pts, t, s, r, theta, exists, closed, w, lean, len);
            if (best == null || at.MinAdvance > best.MinAdvance) best = at;
            if (at.MinAdvance > 0) { best = at; break; }
            if (lean <= 0) break;
        }

        var res = Finish(best!, pts, len, closed, w, half);
        res.MaxDevelopableLean = devLean;
        res.RulingAngle = Enumerable.Range(0, ns).Select(k => exists[k % n] ? theta[k % n] * 180 / Math.PI : (theta[k % n] < 1 ? 0.0 : 180.0)).ToArray();
        var notes = new List<string>();
        if (devLean > best!.Lean + 1e-9)
            notes.Add(devLean >= 90 - 1e-9
                ? "no developable along part of the lath (κ = 0 with twist: a straight or inflecting twisted stretch)"
                : $"the developable's rulings lean up to {devLean:0.#}° from square; beyond {best.Lean:0.#}° the strip twists (|τ| > |κ|, Freddi et al. 2016)");
        if (best.Lean < maxLean - 1e-9)
            notes.Add($"limit lowered to {best.Lean:0.#}° so the boundaries clear the edge of regression at width {w:0.###}");
        if (best.MinAdvance <= 0)
            notes.Add("a boundary still runs backwards: the lath turns too sharply for this width");
        res.Note = string.Join("; ", notes);
        return res;
    }

    private sealed class Attempt
    {
        public double Lean;
        public Vec3d[] EdgeA = Array.Empty<Vec3d>(), EdgeB = Array.Empty<Vec3d>(), Rulings = Array.Empty<Vec3d>();
        public bool[] OnDev = Array.Empty<bool>();
        public Vec3d[] Normal = Array.Empty<Vec3d>();   // strip normal t × s per station
        public Vec3d[] Tangent = Array.Empty<Vec3d>();
        public double[] Theta = Array.Empty<double>(), Lam = Array.Empty<double>();
        public double MinAdvance;
    }

    private static Attempt Place(List<Vec3d> pts, Vec3d[] t, Vec3d[] s, Vec3d[] r, double[] theta, bool[] exists,
        bool closed, double w, double leanDeg, double[] len)
    {
        int n = pts.Count;
        int ns = closed ? n + 1 : n;
        var at = new Attempt { Lean = leanDeg };
        at.EdgeA = new Vec3d[ns];
        at.EdgeB = new Vec3d[ns];
        at.Rulings = new Vec3d[ns];
        at.OnDev = new bool[ns];
        at.Normal = new Vec3d[ns];
        at.Tangent = new Vec3d[ns];
        at.Theta = new double[ns];
        at.Lam = new double[ns];
        for (int k = 0; k < ns; k++)
        {
            int i = k % n;
            Vec3d rr;
            double th;
            double lean = exists[i] ? Math.Abs(90 - theta[i] * 180 / Math.PI) : 90;
            if (exists[i] && lean <= leanDeg + 1e-9)
            {
                rr = r[i]; th = theta[i]; at.OnDev[k] = true;
            }
            else
            {
                // Twist regime: mirror the lean back towards square, continuously —
                // equal to the developable's at the limit, square (a helicoid) where
                // the developable's ruling runs along the lath (κ = 0).
                double mapped = leanDeg >= 90 - 1e-9 ? 0 : leanDeg * (90 - lean) / (90 - leanDeg);
                double sign = theta[i] < Math.PI / 2 ? -1 : 1;
                th = (90 + sign * mapped) * Math.PI / 180;
                rr = Math.Cos(th) * t[i] + Math.Sin(th) * s[i];
            }
            double lam = 0.5 * w / Math.Max(Math.Sin(th), 1e-9);
            at.Normal[k] = Vec3d.Cross(t[i], s[i]).Normalized();
            at.Tangent[k] = t[i];
            at.Theta[k] = th;
            at.Lam[k] = lam;
            at.Rulings[k] = rr;
            at.EdgeA[k] = pts[i] - lam * rr;
            at.EdgeB[k] = pts[i] + lam * rr;
        }
        double minAdv = double.PositiveInfinity;
        for (int k = 0; k + 1 < ns; k++)
        {
            Vec3d c = pts[(k + 1) % n] - pts[k % n];
            double l2 = c.LengthSquared;
            minAdv = Math.Min(minAdv, Vec3d.Dot(at.EdgeA[k + 1] - at.EdgeA[k], c) / l2);
            minAdv = Math.Min(minAdv, Vec3d.Dot(at.EdgeB[k + 1] - at.EdgeB[k], c) / l2);
        }
        at.MinAdvance = minAdv;
        return at;
    }

    private static Result Finish(Attempt at, List<Vec3d> pts, double[] len, bool closed, double w, double half)
    {
        int n = pts.Count;
        int ns = at.EdgeA.Length;
        var centre = new Vec3d[ns];
        for (int k = 0; k < ns; k++) centre[k] = pts[k % n];
        var edgeA = at.EdgeA;
        var edgeB = at.EdgeB;

        // Flat pattern. A quad of the developable is planar and is laid down exactly by its
        // two triangles (A_k, B_k, B_k+1), (A_k, B_k+1, A_k+1). A twisted quad has no exact
        // development: there the centreline is laid down with its turning inside the strip's
        // own plane (t, s) — the in-plane bend a straight plank would need — and the rulings
        // at their angle to it, so the pattern of a twisted straight lath stays straight.
        var flatA = new Vec3d[ns];
        var flatB = new Vec3d[ns];
        var flatC0 = new Vec3d[ns];
        Vec3d Dir(double ang) => new Vec3d(Math.Cos(ang), Math.Sin(ang), 0);
        // Turning of the polyline at station k inside the strip's plane (t, s): summed along
        // the lath it telescopes to the turning between the end segments, so facet-scale kinks
        // of a traced polyline do not accumulate (a window-averaged tangent would: its tilt
        // grows with the window² and with the change of κτ along the lath)
        double Turn(int k)
        {
            if (k <= 0 || k >= ns - 1) return 0;
            Vec3d m = at.Normal[k];
            Vec3d d0 = centre[k] - centre[k - 1], d1 = centre[k + 1] - centre[k];
            Vec3d p0 = d0 - Vec3d.Dot(d0, m) * m, p1 = d1 - Vec3d.Dot(d1, m) * m;
            return Math.Atan2(Vec3d.Dot(m, Vec3d.Cross(p0, p1)), Vec3d.Dot(p0, p1));
        }
        flatC0[0] = Vec3d.Zero;
        flatB[0] = at.Lam[0] * Dir(at.Theta[0]);
        flatA[0] = -1.0 * flatB[0];
        Vec3d forward = new(1, 0, 0);
        double strain = 0, planar = 0, gap = 0, devLen = 0, total = 0;
        for (int k = 0; k < ns - 1; k++)
        {
            if (at.OnDev[k] && at.OnDev[k + 1])
            {
                flatB[k + 1] = StripUnroll.PlacePoint(flatA[k], flatB[k], (edgeB[k + 1] - edgeA[k]).Length, (edgeB[k + 1] - edgeB[k]).Length, forward);
                flatA[k + 1] = StripUnroll.PlacePoint(flatA[k], flatB[k + 1], (edgeA[k + 1] - edgeA[k]).Length, (edgeA[k + 1] - edgeB[k + 1]).Length, forward);
                flatC0[k + 1] = 0.5 * (flatA[k + 1] + flatB[k + 1]);
            }
            else
            {
                // flat tangent at station k from its ruling, then turn in the strip's plane
                Vec3d rk = flatB[k] - flatA[k];
                double psi0 = Math.Atan2(rk.Y, rk.X) - at.Theta[k];
                double seg = psi0 + 0.5 * Turn(k);
                double psi1 = seg + 0.5 * Turn(k + 1);
                flatC0[k + 1] = flatC0[k] + (centre[k + 1] - centre[k]).Length * Dir(seg);
                flatB[k + 1] = flatC0[k + 1] + at.Lam[k + 1] * Dir(psi1 + at.Theta[k + 1]);
                flatA[k + 1] = flatC0[k + 1] - at.Lam[k + 1] * Dir(psi1 + at.Theta[k + 1]);
            }
            Vec3d sd = flatC0[k + 1] - flatC0[k];
            if (sd.LengthSquared > 1e-24) forward = sd.Normalized();

            double c = (centre[k + 1] - centre[k]).Length;
            double mean = 0.5 * ((edgeA[k + 1] - edgeA[k]).Length + (edgeB[k + 1] - edgeB[k]).Length);
            strain = Math.Max(strain, Math.Abs(mean / c - 1));
            double q = QuadGap(edgeA[k], edgeA[k + 1], edgeB[k + 1], edgeB[k]);
            gap = Math.Max(gap, q);
            total += c;
            if (at.OnDev[k] && at.OnDev[k + 1]) { devLen += c; planar = Math.Max(planar, q); }
        }
        var flatC = flatC0;

        // Developed curvature over the window (chords half a window either side), only
        // where every quad involved is on the developable (there the development is exact)
        var sArc = new double[ns];
        for (int k = 1; k < ns; k++) sArc[k] = sArc[k - 1] + (centre[k] - centre[k - 1]).Length;
        double maxCurv = 0;
        for (int k = 1; k < ns - 1; k++)
        {
            int kb = k - 1, kf = k + 1;
            while (kb > 0 && sArc[k] - sArc[kb] < half) kb--;
            while (kf < ns - 1 && sArc[kf] - sArc[k] < half) kf++;
            bool exact = true;
            for (int q = kb; q <= kf && exact; q++) exact = at.OnDev[q];
            if (!exact) continue;
            Vec3d d0 = flatC[k] - flatC[kb], d1 = flatC[kf] - flatC[k];
            double turn = Math.Atan2(d0.X * d1.Y - d0.Y * d1.X, d0.X * d1.X + d0.Y * d1.Y);
            maxCurv = Math.Max(maxCurv, Math.Abs(turn) / (0.5 * (sArc[kf] - sArc[kb])));
        }

        double frac = total > 0 ? devLen / total : 0;
        return new Result
        {
            Closed = closed,
            Width = w,
            Centre = centre,
            EdgeA = edgeA,
            EdgeB = edgeB,
            Rulings = at.Rulings,
            OnDevelopable = at.OnDev,
            Strip = QuadStrip(edgeA, edgeB),
            FlatCentre = flatC,
            FlatA = flatA,
            FlatB = flatB,
            FlatMesh = QuadStrip(flatA, flatB),
            Length = len.Sum(),
            DevelopableFraction = frac,
            Developable = devLen >= total - 1e-12 * Math.Max(1, total),
            LeanUsed = at.Lean,
            MinAdvance = at.MinAdvance,
            MaxDevelopedCurvature = maxCurv,
            Bow = ChordDeviation(flatC),
            EdgeStrain = strain,
            MaxPlanarity = planar,
            MaxTwistGap = gap,
        };
    }

    /// <summary>
    /// Chord direction and strip direction (⟂ chord) of the stretch from station i
    /// backwards (dir −1) or forwards (+1) over at least `half` of arc length
    /// (at least one segment; truncated at the ends of an open lath).
    /// </summary>
    private static (Vec3d e, Vec3d a) WindowPlane(List<Vec3d> pts, Vec3d[] a, double[] len, bool closed, int i, int dir, double half)
    {
        int n = pts.Count, segs = a.Length;
        Vec3d sum = Vec3d.Zero;
        double acc = 0;
        int j = i, steps = 0;
        while (true)
        {
            int seg = dir > 0 ? (closed ? j % segs : j) : (closed ? (j - 1 + segs) % segs : j - 1);
            if (seg < 0 || seg >= segs) break;
            sum = sum + len[seg] * a[seg];
            acc += len[seg];
            j = dir > 0 ? j + 1 : j - 1;
            if (closed) j = (j + n) % n;
            steps++;
            if (acc >= half || steps >= segs / 2 + 1) break;
            if (!closed && (j <= 0 || j >= n - 1)) break;
        }
        Vec3d chord = dir > 0 ? pts[j] - pts[i] : pts[i] - pts[j];
        Vec3d ec = chord.Normalized();
        Vec3d av = sum - Vec3d.Dot(sum, ec) * ec;
        if (av.LengthSquared < 1e-24) av = AnyPerpendicular(ec);
        return (ec, av.Normalized());
    }

    private static MeshData QuadStrip(Vec3d[] edgeA, Vec3d[] edgeB)
    {
        int ns = edgeA.Length;
        var verts = new Vec3d[2 * ns];
        for (int k = 0; k < ns; k++) { verts[k] = edgeA[k]; verts[ns + k] = edgeB[k]; }
        var faces = new int[ns - 1][];
        for (int k = 0; k < ns - 1; k++) faces[k] = new[] { k, k + 1, ns + k + 1, ns + k };
        return new MeshData(verts, faces);
    }

    /// <summary>Distance between the two diagonals of the quad p0 p1 p2 p3 (0 when planar).</summary>
    private static double QuadGap(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3)
    {
        Vec3d d1 = p2 - p0, d2 = p3 - p1;
        Vec3d c = Vec3d.Cross(d1, d2);
        double cl = c.Length;
        if (cl < 1e-18) return 0;
        return Math.Abs(Vec3d.Dot(p1 - p0, c)) / cl;
    }

    private static double ChordDeviation(Vec3d[] line)
    {
        Vec3d o = line[0], d = line[line.Length - 1] - line[0];
        double L = d.Length;
        double worst = 0;
        if (L < 1e-15)
        {
            foreach (var p in line) worst = Math.Max(worst, (p - o).Length);
            return worst;
        }
        d = d / L;
        foreach (var p in line)
        {
            Vec3d v = p - o;
            worst = Math.Max(worst, (v - Vec3d.Dot(v, d) * d).Length);
        }
        return worst;
    }

    private static Vec3d AnyPerpendicular(Vec3d t)
    {
        Vec3d axis = Math.Abs(t.Z) < 0.9 ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
        return Vec3d.Cross(axis, t);
    }
}
