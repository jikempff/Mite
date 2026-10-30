using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Surface-free design of an AAG web by propagation from a boundary strip,
/// after Wang, Almaskin &amp; Pottmann 2025, "Computational design of
/// asymptotic geodesic hybrid gridshells via propagation algorithms"
/// (Computer-Aided Design 178, 103800, §3.3 and eqs. 20–22; code
/// github.com/wangbolun300/WebsViaPropagation, <c>tools.cpp</c>
/// <c>construct_single_developable_strips_by_intersect_rectifying_AAG_followings</c>
/// and <c>polyline.cpp adjustAagOffset</c>). The discrete definitions are
/// Sauer's (Differenzengeometrie 1970, as used by Schling, Wang, Hoyer &amp;
/// Pottmann 2022 §3.2): an A-net has planar vertex stars, a discrete geodesic
/// has its osculating plane through the vertex normal.
/// <para>
/// The web is a triangle strip pattern of three families: the rows V (the
/// geodesic family, flat slats) and the two asymptotic families U and W
/// (upright slats) that run between consecutive rows — row k's vertex j is
/// joined by U to row k+1's vertex j and by W to row k+1's vertex j−1, so
/// every row has the same n vertices, each new row adds n U edges and n−1
/// W edges, and the A-net quads are (k,j) (k+1,j) (k+2,j−1) (k+1,j−1) with
/// the rows as their diagonals. The user draws the first two rows (two
/// neighbouring geodesics of the design); the rest is determined up to a
/// few design degrees of freedom per row:
/// <list type="number">
/// <item>Planar star at row k's vertex j (paper eq. 1 at the vertices of
/// V_{k−1}): the plane P_j through p_j, p_{j}^{k−1} and p_{j+1}^{k−1} (its two
/// incoming asymptotic edges) must also contain the two outgoing ones, so
/// the new vertex j lies on the line L_j = P_j ∩ P_{j+1} (the ruling of the
/// discrete rectifying developable of the row); the paper's foot point on
/// the medial line is taken here as the point of L_j nearest the midpoint of
/// the row edge, so both stars are planar exactly (the reference code
/// satisfies only the right-hand plane exactly and leaves the rest to the
/// global optimisation). New vertex j = f_j + r_j c_j with one scalar r_j.</item>
/// <item>Geodesic row (eq. 22): at every interior new vertex the normal
/// n = u × w of its two incoming asymptotic edges lies in the osculating
/// plane of the new row, det(n, e_in, e_out) = 0, normalised by |e_in||e_out|
/// so it reads as the sine of an angle. n−2 conditions for the n−1 line
/// parameters plus the last vertex (whose star has no incoming W edge: it
/// gets the plane of the three edges it has, two free coordinates).</item>
/// <item>The free directions are closed by soft targets: the strip's own
/// triangle repeated at every position (Offset 0: the U and W edge lengths
/// of the previous step, constant node spacing along the asymptotic laths)
/// or the equilateral apex of the paper's initial guess (eq. 21, Offset 1: a
/// triangle of height √3/2 of the row edge), a weak fairness of the new row
/// (eq. 8) — or, when <see cref="Options.Targets"/> is given, a target
/// position per vertex. Levenberg–Marquardt on the row (dense, n+2 unknowns)
/// followed by a least-norm projection onto the geodesic conditions alone,
/// instead of the paper's block-coordinate descent on r, which it describes
/// as reaching E_aag 0.053 after 16 sweeps; here the residual goes to
/// round-off (~1e-13°) on every row.</item>
/// </list>
/// The result is an <see cref="AsymptoticWeb.Result"/> so that the global
/// optimisation <see cref="AagWeb.Optimize(AsymptoticWeb.Result, AagWeb.Options)"/>
/// (the paper's step after propagation, eq. 11 with λ_fair 1e-4) and the
/// lath tools downstream take it as any other web.
/// </para>
/// </summary>
public static class WebPropagation
{
    public class Options
    {
        /// <summary>Rows to add after the two given ones.</summary>
        public int Rows { get; set; } = 10;

        /// <summary>Shape of the new triangles: 0 repeats the strip's own triangle at each position (a symmetric strip continues its symmetry exactly), a value &gt; 0 asks for equilateral triangles of that height factor (1 = equilateral).</summary>
        public double Offset { get; set; } = 0.0;

        /// <summary>Weight of the second differences along the new row (eq. 8), relative to the geodesic condition.</summary>
        public double Fairness { get; set; } = 0.01;

        /// <summary>Weight pulling every vertex to its target (equilateral apex or <see cref="Targets"/>).</summary>
        public double TargetWeight { get; set; } = 0.05;

        /// <summary>Optional target positions per new row (null entries fall back to the equilateral apex).</summary>
        public IReadOnlyList<Vec3d[]?>? Targets { get; set; }

        /// <summary>Levenberg–Marquardt iterations per row.</summary>
        public int Iterations { get; set; } = 40;

        /// <summary>Stop when the largest step falls below this fraction of the row edge length.</summary>
        public double Tolerance { get; set; } = 1e-13;

        public Func<bool>? ShouldCancel { get; set; }
    }

    public sealed class Result
    {
        /// <summary>All rows, the two given ones first.</summary>
        public List<Vec3d[]> Rows { get; } = new List<Vec3d[]>();
        /// <summary>The web: A = U laths, B = W laths, diagonal family 0 = the rows.</summary>
        public AsymptoticWeb.Result Web { get; set; } = new AsymptoticWeb.Result();
        /// <summary>Per new row: largest angle (degrees) between the row's osculating plane and the vertex normal at its interior vertices.</summary>
        public List<double> GeodesicError { get; } = new List<double>();
        /// <summary>Per new row: largest angle (degrees) between an outgoing asymptotic edge and the star plane of the vertex it leaves.</summary>
        public List<double> StarError { get; } = new List<double>();
        /// <summary>Per new row: Levenberg–Marquardt iterations used.</summary>
        public List<int> Iterations { get; } = new List<int>();
        public double MaxGeodesicError => GeodesicError.Count == 0 ? 0 : GeodesicError.Max();
        public double MaxStarError => StarError.Count == 0 ? 0 : StarError.Max();
        /// <summary>Rows actually added.</summary>
        public int RowsBuilt { get; set; }
        /// <summary>Why the propagation stopped early (null when every row was built).</summary>
        public string? Stopped { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Grows the web from the strip (first, second): both rows need the same
    /// number of vertices (≥ 3); second[j] is the vertex joined to first[j]
    /// by a U edge and to first[j+1] by a W edge.
    /// </summary>
    public static Result Propagate(IReadOnlyList<Vec3d> first, IReadOnlyList<Vec3d> second, Options? options = null)
    {
        options ??= new Options();
        var res = new Result();
        if (first == null || second == null || first.Count != second.Count || first.Count < 3)
        {
            res.Stopped = "the two rows need the same number of vertices, at least three";
            return res;
        }
        var prev = first.ToArray();
        var last = second.ToArray();
        res.Rows.Add(prev); res.Rows.Add(last);
        for (int k = 0; k < options.Rows; k++)
        {
            if (options.ShouldCancel?.Invoke() == true) { res.Stopped = "cancelled"; break; }
            Vec3d[]? target = options.Targets != null && k < options.Targets.Count ? options.Targets[k] : null;
            var next = NextRow(prev, last, options, target, out double geo, out int iterations, out string? why);
            if (next == null) { res.Stopped = why; break; }
            res.Rows.Add(next);
            res.GeodesicError.Add(geo);
            res.StarError.Add(StarErrorDegrees(prev, last, next));
            res.Iterations.Add(iterations);
            res.RowsBuilt++;
            prev = last; last = next;
        }
        res.Web = BuildWeb(res.Rows);
        if (res.Stopped != null) res.Notes.Add($"stopped after {res.RowsBuilt} of {options.Rows} rows: {res.Stopped}");
        return res;
    }

    /// <summary>
    /// Puts a drawn strip into the convention of <see cref="Propagate"/>: the
    /// second row is reversed when it runs against the first, and both are
    /// reversed when second[j] sits between first[j−1] and first[j] rather than
    /// between first[j] and first[j+1] (the pairing with the smaller total
    /// distance from the row-edge midpoints wins). Returns false when the rows
    /// cannot be paired (different counts, fewer than three vertices).
    /// </summary>
    public static bool Align(IReadOnlyList<Vec3d> first, IReadOnlyList<Vec3d> second, out Vec3d[] row0, out Vec3d[] row1, out bool reversed)
    {
        row0 = first.ToArray(); row1 = second.ToArray(); reversed = false;
        int n = row0.Length;
        if (n < 3 || row1.Length != n) return false;
        if (Vec3d.Dot(row0[n - 1] - row0[0], row1[n - 1] - row1[0]) < 0) Array.Reverse(row1);
        double Pairing(Vec3d[] a, Vec3d[] b)
        {
            double s = 0;
            for (int j = 0; j + 1 < n; j++) s += (b[j] - 0.5 * (a[j] + a[j + 1])).Length;
            return s;
        }
        var r0 = (Vec3d[])row0.Clone(); var r1 = (Vec3d[])row1.Clone();
        Array.Reverse(r0); Array.Reverse(r1);
        if (Pairing(r0, r1) < Pairing(row0, row1)) { row0 = r0; row1 = r1; reversed = true; }
        return true;
    }

    /// <summary>
    /// The ruling L_j of the discrete rectifying strip of the row <paramref name="last"/>
    /// over its edge (j, j+1): the intersection of the star planes P_j and P_{j+1}
    /// (through last[j] and its incoming edges from prev[j], prev[j+1]; likewise
    /// at j+1). Foot = the point of L_j nearest the edge midpoint, Direction =
    /// unit, pointing away from the previous row; Parallel = the planes
    /// coincide (within 1e-9), the direction is then the in-plane normal of the edge.
    /// </summary>
    public static (Vec3d Foot, Vec3d Direction, bool Parallel) Ruling(Vec3d[] prev, Vec3d[] last, int j)
    {
        int n = last.Length;
        var nj = StarNormal(prev, last, j);
        var m = 0.5 * (last[j] + last[j + 1]);
        var forward = m - 0.5 * (prev[j] + prev[j + 1]);
        if (j + 1 >= n - 1)
        {
            // the last vertex has no incoming W edge, so P_{n−1} is not yet defined: the
            // new vertex n−2 is free in P_{n−2}; foot = the midpoint projected into it,
            // direction = the in-plane normal of the row edge, forward
            var f0 = m - Vec3d.Dot(m - last[j], nj) * nj;
            var d0 = Vec3d.Cross(nj, last[j + 1] - last[j]);
            if (d0.Length < 1e-300) d0 = forward - Vec3d.Dot(forward, nj) * nj;
            d0 = d0.Normalized();
            if (Vec3d.Dot(d0, forward) < 0) d0 = -d0;
            return (f0, d0, true);
        }
        var nk = StarNormal(prev, last, j + 1);
        var c = Vec3d.Cross(nj, nk);
        bool parallel = c.Length < 1e-9;
        Vec3d foot;
        if (parallel)
        {
            var e = last[j + 1] - last[j];
            c = Vec3d.Cross(nj, e);
            if (c.Length < 1e-15) c = forward;
            // closest point of the (single) plane to the midpoint
            foot = m - Vec3d.Dot(m - last[j], nj) * nj;
        }
        else
        {
            // p = m + α nj + β nk on both planes: a 2 × 2 Lagrange system
            double a11 = Vec3d.Dot(nj, nj), a12 = Vec3d.Dot(nj, nk), a22 = Vec3d.Dot(nk, nk);
            double b1 = Vec3d.Dot(nj, last[j] - m), b2 = Vec3d.Dot(nk, last[j + 1] - m);
            double det = a11 * a22 - a12 * a12;
            double alpha = (b1 * a22 - b2 * a12) / det, beta = (a11 * b2 - a12 * b1) / det;
            foot = m + alpha * nj + beta * nk;
        }
        c = c.Normalized();
        if (Vec3d.Dot(c, forward) < 0) c = -c;
        return (foot, c, parallel);
    }

    /// <summary>Unit normal of the star plane at last[j], j ≤ n−2: the plane of its two incoming asymptotic edges (from prev[j] and prev[j+1]). The last vertex has one incoming edge only; its plane is fixed by the next row (see <see cref="StarErrorDegrees"/>).</summary>
    public static Vec3d StarNormal(Vec3d[] prev, Vec3d[] last, int j)
    {
        if (j + 1 >= last.Length) throw new ArgumentOutOfRangeException(nameof(j), "the last vertex of a row has no incoming W edge");
        var a = prev[j] - last[j];
        var b = prev[j + 1] - last[j];
        var c = Vec3d.Cross(a, b);
        return c.Normalized();
    }

    /// <summary>Sine of the angle between the row's osculating plane at v and the normal n: det(n, e_in, e_out) / (|e_in||e_out|).</summary>
    public static double GeodesicResidual(Vec3d n, Vec3d before, Vec3d v, Vec3d after)
    {
        var p = v - before; var q = after - v;
        double s = p.Length * q.Length;
        return s > 1e-300 ? Vec3d.Dot(n, Vec3d.Cross(p, q)) / s : 0;
    }

    /// <summary>Largest angle (degrees) between the osculating plane of each interior vertex of <paramref name="row"/> and the normal of its incoming edges from <paramref name="below"/>.</summary>
    public static double GeodesicErrorDegrees(Vec3d[] below, Vec3d[] row)
    {
        double worst = 0;
        for (int j = 1; j + 1 < row.Length; j++)
        {
            var n = StarNormal(below, row, j);
            double s = Math.Abs(GeodesicResidual(n, row[j - 1], row[j], row[j + 1]));
            worst = Math.Max(worst, Math.Asin(Math.Min(1, s)));
        }
        return worst * 180 / Math.PI;
    }

    /// <summary>Largest angle (degrees) between an outgoing asymptotic edge of <paramref name="last"/> (to <paramref name="next"/>) and the star plane of its vertex (incoming edges from <paramref name="prev"/>).</summary>
    public static double StarErrorDegrees(Vec3d[] prev, Vec3d[] last, Vec3d[] next)
    {
        int n = last.Length;
        double worst = 0;
        for (int j = 0; j < n; j++)
        {
            Vec3d nj;
            if (j + 1 < n) nj = StarNormal(prev, last, j);
            else
            {
                // the last vertex: its star is the incoming U edge and the two outgoing edges
                var c = Vec3d.Cross(prev[n - 1] - last[n - 1], next[n - 2] - last[n - 1]);
                if (c.Length < 1e-300) continue;
                nj = c.Normalized();
            }
            void Check(Vec3d e)
            {
                double l = e.Length;
                if (l > 1e-300) worst = Math.Max(worst, Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(nj, e)) / l)));
            }
            Check(next[j] - last[j]);                 // U out
            if (j >= 1) Check(next[j - 1] - last[j]); // W out
        }
        return worst * 180 / Math.PI;
    }

    /// <summary>
    /// The next row from the two last ones (see the class remarks); null with a
    /// reason when the strip is degenerate. <paramref name="target"/> replaces the
    /// repeated / equilateral apex as the soft target per vertex.
    /// </summary>
    public static Vec3d[]? NextRow(Vec3d[] prev, Vec3d[] last, Options options, Vec3d[]? target, out double geodesicError, out int iterations, out string? why)
    {
        int n = last.Length;
        geodesicError = 0; iterations = 0; why = null;
        if (n < 3 || prev.Length != n) { why = "rows need the same number of vertices, at least three"; return null; }
        double ell = 0;
        for (int j = 0; j + 1 < n; j++) ell += (last[j + 1] - last[j]).Length;
        ell /= n - 1;
        if (ell <= 0) { why = "a row has coincident vertices"; return null; }
        double offset = Math.Max(0, options.Offset);

        // rulings L_j for the vertices on lines (j = 0..n−3) and the plane basis for the two last vertices
        var foot = new Vec3d[n]; var dir = new Vec3d[n];
        for (int j = 0; j + 1 < n; j++)
        {
            var (f, c, _) = Ruling(prev, last, j);
            foot[j] = f; dir[j] = c;
            if (double.IsNaN(c.X) || double.IsNaN(f.X)) { why = $"degenerate star at vertex {j} (collinear edges)"; return null; }
        }
        // the side lengths of the target triangle over the row edge (j, j+1): Offset 0 repeats the
        // strip's own triangle (prev[j], prev[j+1], last[j]) — the U and W edges keep their lengths,
        // constant node spacing along the asymptotic laths (a translational strip continues exactly;
        // a rotational web does not, its rows alternate ring parity); Offset > 0 asks for the
        // equilateral triangle of that height factor (the paper's initial guess, eq. 21)
        (double U, double W) Sides(int j)
        {
            double L = (last[j + 1] - last[j]).Length;
            if (offset > 0) { double side = L * Math.Sqrt(0.25 + 0.75 * offset * offset); return (side, side); }
            return ((last[j] - prev[j]).Length, (last[j] - prev[j + 1]).Length);
        }
        // apex at distances (lu, lw) from (p, q) in the plane of the edge and the forward direction d
        Vec3d Apex(Vec3d p, Vec3d q, double lu, double lw, Vec3d d)
        {
            var e = q - p; double L = e.Length;
            if (L < 1e-300) return p + lu * d;
            e = e / L;
            var y = d - Vec3d.Dot(d, e) * e; double ly = y.Length;
            if (ly < 1e-300) return p + lu * d;
            y = y / ly;
            double x = (lu * lu - lw * lw + L * L) / (2 * L);
            double hh = lu * lu - x * x;
            return p + x * e + (hh > 0 ? Math.Sqrt(hh) : 0) * y;
        }
        var r0 = new double[n - 2];
        for (int j = 0; j + 2 < n; j++)
        {
            Vec3d t;
            if (target != null && j < target.Length) t = target[j];
            else { var (lu, lw) = Sides(j); t = Apex(last[j], last[j + 1], lu, lw, dir[j]); }
            r0[j] = Vec3d.Dot(t - foot[j], dir[j]);
        }
        // vertex n−2: free in the star plane P_{n−2} (basis e1 = the in-plane edge normal, e2 = n × e1)
        var nA = StarNormal(prev, last, n - 2);
        var eA1 = dir[n - 2]; eA1 = (eA1 - Vec3d.Dot(eA1, nA) * nA).Normalized();
        var eA2 = Vec3d.Cross(nA, eA1);
        var baseA = foot[n - 2];
        Vec3d VertexA(double a, double b) => baseA + a * eA1 + b * eA2;
        double aA0, bA0;
        {
            Vec3d t;
            if (target != null && n - 2 < target.Length) t = target[n - 2];
            else { var (lu, lw) = Sides(n - 2); t = Apex(last[n - 2], last[n - 1], lu, lw, dir[n - 2]); }
            aA0 = Vec3d.Dot(t - baseA, eA1); bA0 = Vec3d.Dot(t - baseA, eA2);
        }
        // vertex n−1: free in the plane of the last vertex's three edges, which turns with vertex n−2
        (Vec3d E1, Vec3d E2) BasisB(Vec3d vA)
        {
            var e1 = (vA - last[n - 1]).Normalized();
            var u = last[n - 1] - prev[n - 1];
            var e2 = u - Vec3d.Dot(u, e1) * e1;
            double l2 = e2.Length;
            e2 = l2 > 1e-300 ? e2 / l2 : Vec3d.Cross(nA, e1);
            return (e1, e2);
        }
        Vec3d VertexB(Vec3d vA, double a, double b)
        {
            var (e1, e2) = BasisB(vA);
            return last[n - 1] + a * e1 + b * e2;
        }
        double aB0, bB0;
        {
            var vA = VertexA(aA0, bA0);
            var (e1, e2) = BasisB(vA);
            Vec3d t;
            if (target != null && n - 1 < target.Length) t = target[n - 1];
            else
            {
                // the triangle (last[n−1], new[n−2], new[n−1]) repeats (prev[n−1], last[n−2], last[n−1]):
                // U length from last[n−1], row-edge length from new[n−2]; equilateral when Offset > 0
                double lw = (vA - last[n - 1]).Length;
                double lu, le;
                if (offset > 0) { lu = le = lw * Math.Sqrt(0.25 + 0.75 * offset * offset); }
                else { lu = (last[n - 1] - prev[n - 1]).Length; le = (last[n - 1] - last[n - 2]).Length; }
                t = Apex(last[n - 1], vA, lu, le, e2);
            }
            aB0 = Vec3d.Dot(t - last[n - 1], e1); bB0 = Vec3d.Dot(t - last[n - 1], e2);
        }

        // unknowns x: r_0..r_{n−3}, (a, b) of vertex n−2, (a, b) of vertex n−1
        int nx = n + 2;
        var x = new double[nx];
        for (int j = 0; j + 2 < n; j++) x[j] = r0[j];
        x[n - 2] = aA0; x[n - 1] = bA0; x[n] = aB0; x[n + 1] = bB0;
        var x0 = (double[])x.Clone();

        Vec3d[] Positions(double[] xx)
        {
            var v = new Vec3d[n];
            for (int j = 0; j + 2 < n; j++) v[j] = foot[j] + xx[j] * dir[j];
            v[n - 2] = VertexA(xx[n - 2], xx[n - 1]);
            v[n - 1] = VertexB(v[n - 2], xx[n], xx[n + 1]);
            return v;
        }
        double wt = Math.Max(0, options.TargetWeight), wf = Math.Max(0, options.Fairness);
        int nr = (n - 2) + nx + 3 * (n - 2);
        void Residuals(double[] xx, double[] r)
        {
            var v = Positions(xx);
            int k = 0;
            for (int j = 1; j + 1 < n; j++)
            {
                var nn = Vec3d.Cross(v[j] - last[j], v[j] - last[j + 1]);
                double ln = nn.Length;
                r[k++] = ln > 1e-300 ? GeodesicResidual(nn / ln, v[j - 1], v[j], v[j + 1]) : 0;
            }
            for (int i = 0; i < nx; i++) r[k++] = wt * (xx[i] - x0[i]) / ell;
            for (int j = 1; j + 1 < n; j++)
            {
                var d = (v[j - 1] - 2 * v[j] + v[j + 1]) / ell;
                r[k++] = wf * d.X; r[k++] = wf * d.Y; r[k++] = wf * d.Z;
            }
        }
        double Cost(double[] r) { double c = 0; for (int i = 0; i < r.Length; i++) c += r[i] * r[i]; return c; }

        var res = new double[nr]; var tmpP = new double[nr]; var tmpM = new double[nr];
        var J = new double[nr, nx];
        var H = new double[nx, nx]; var g = new double[nx]; var dx = new double[nx];
        // stage 1: the soft problem (targets and fairness choose the free directions);
        // stage 2: the geodesic conditions alone, damped Gauss–Newton steps of least
        // norm, so the row lands on the constraint manifold next to the soft optimum
        int total = 0;
        for (int stage = 0; stage < 2; stage++)
        {
        if (stage == 1) { wt = 0; wf = 0; }
        Residuals(x, res);
        double cost = Cost(res), lambda = stage == 0 ? 1e-6 : 1e-9;
        int it;
        for (it = 0; it < Math.Max(1, options.Iterations); it++)
        {
            // central-difference Jacobian (the unknowns are lengths; the row edge sets the step)
            double hstep = 1e-6 * ell;
            var xp = (double[])x.Clone();
            for (int c = 0; c < nx; c++)
            {
                xp[c] = x[c] + hstep; Residuals(xp, tmpP);
                xp[c] = x[c] - hstep; Residuals(xp, tmpM);
                xp[c] = x[c];
                for (int i = 0; i < nr; i++) J[i, c] = (tmpP[i] - tmpM[i]) / (2 * hstep);
            }
            for (int a = 0; a < nx; a++)
            {
                g[a] = 0;
                for (int i = 0; i < nr; i++) g[a] += J[i, a] * res[i];
                for (int b = a; b < nx; b++)
                {
                    double s = 0;
                    for (int i = 0; i < nr; i++) s += J[i, a] * J[i, b];
                    H[a, b] = s; H[b, a] = s;
                }
            }
            bool accepted = false;
            for (int attempt = 0; attempt < 12 && !accepted; attempt++)
            {
                if (!SolveDamped(H, g, lambda, dx)) { lambda *= 10; continue; }
                var xn = new double[nx];
                for (int c = 0; c < nx; c++) xn[c] = x[c] + dx[c];
                Residuals(xn, tmpP);
                double cn = Cost(tmpP);
                if (cn < cost)
                {
                    accepted = true;
                    Array.Copy(xn, x, nx); Array.Copy(tmpP, res, nr);
                    cost = cn; lambda = Math.Max(lambda / 4, 1e-15);
                }
                else lambda *= 10;
            }
            if (!accepted) break;
            double step = 0;
            for (int c = 0; c < nx; c++) step = Math.Max(step, Math.Abs(dx[c]));
            if (step < options.Tolerance * ell || cost < 1e-30) { it++; break; }
        }
        total += it;
        }
        iterations = total;
        var next = Positions(x);
        // sanity: the row must move forward and keep its edges
        for (int j = 0; j < n; j++)
        {
            var forward = last[j] - prev[j];
            if (Vec3d.Dot(next[j] - last[j], forward) <= 0) { why = $"the new row folds back at vertex {j}"; return null; }
            if (j + 1 < n && (next[j + 1] - next[j]).Length < 0.02 * ell) { why = $"the new row collapses between vertices {j} and {j + 1}"; return null; }
        }
        geodesicError = GeodesicErrorDegrees(last, next);
        return next;
    }

    private static bool SolveDamped(double[,] H, double[] g, double lambda, double[] dx)
    {
        int n = g.Length;
        var A = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) A[i, j] = H[i, j] + (i == j ? lambda * (1 + H[i, i]) : 0);
        // Cholesky
        for (int j = 0; j < n; j++)
        {
            double d = A[j, j];
            for (int k = 0; k < j; k++) d -= A[j, k] * A[j, k];
            if (d <= 0 || double.IsNaN(d)) return false;
            d = Math.Sqrt(d); A[j, j] = d;
            for (int i = j + 1; i < n; i++)
            {
                double s = A[i, j];
                for (int k = 0; k < j; k++) s -= A[i, k] * A[j, k];
                A[i, j] = s / d;
            }
        }
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = -g[i];
            for (int k = 0; k < i; k++) s -= A[i, k] * y[k];
            y[i] = s / A[i, i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int k = i + 1; k < n; k++) s -= A[k, i] * dx[k];
            dx[i] = s / A[i, i];
        }
        return true;
    }

    /// <summary>
    /// The rows as a web: nodes row-major, A = U laths (vertex j of every row),
    /// B = W laths ((k, j) → (k+1, j−1)), quads (k,j) (k+1,j) (k+2,j−1) (k+1,j−1),
    /// diagonal family 0 = the rows (geodesic), family 1 = the other quad diagonals.
    /// </summary>
    public static AsymptoticWeb.Result BuildWeb(IReadOnlyList<Vec3d[]> rows)
    {
        var web = new AsymptoticWeb.Result();
        int K = rows.Count;
        if (K == 0) return web;
        int n = rows[0].Length;
        int Id(int k, int j) => k * n + j;
        for (int k = 0; k < K; k++) foreach (var p in rows[k]) web.Nodes.Add(p);
        int N = K * n;
        var nbA = new List<int>[N]; var nbB = new List<int>[N];
        for (int i = 0; i < N; i++) { nbA[i] = new List<int>(2); nbB[i] = new List<int>(2); }
        for (int k = 0; k + 1 < K; k++)
            for (int j = 0; j < n; j++)
            {
                nbA[Id(k, j)].Add(Id(k + 1, j)); nbA[Id(k + 1, j)].Add(Id(k, j));
                if (j >= 1) { nbB[Id(k, j)].Add(Id(k + 1, j - 1)); nbB[Id(k + 1, j - 1)].Add(Id(k, j)); }
            }
        web.NeighborsA = nbA.Select(l => l.ToArray()).ToArray();
        web.NeighborsB = nbB.Select(l => l.ToArray()).ToArray();
        for (int k = 0; k + 2 < K; k++)
            for (int j = 1; j < n; j++)
                web.Quads.Add(new[] { Id(k, j), Id(k + 1, j), Id(k + 2, j - 1), Id(k + 1, j - 1) });
        // A laths: vertex j across the rows; B laths: (k, j) → (k+1, j−1)
        for (int j = 0; j < n; j++) web.A.Add(Enumerable.Range(0, K).Select(k => rows[k][j]).ToArray());
        void WalkB(int k, int j)
        {
            var chain = new List<Vec3d>();
            while (k < K && j >= 0) { chain.Add(rows[k][j]); k++; j--; }
            if (chain.Count >= 2) web.B.Add(chain.ToArray());
        }
        for (int j = 1; j < n; j++) WalkB(0, j);
        for (int k = 1; k < K; k++) WalkB(k, n - 1);
        // diagonals: family 0 = the rows, family 1 = (k, j) → (k+2, j−1)
        for (int k = 0; k < K; k++)
        {
            web.Diagonals.Add(rows[k]); web.DiagonalFamily.Add(0);
            web.DiagonalNodes.Add(Enumerable.Range(0, n).Select(j => Id(k, j)).ToArray());
        }
        void WalkD(int k, int j)
        {
            var ids = new List<int>();
            while (k < K && j >= 0) { ids.Add(Id(k, j)); k += 2; j--; }
            if (ids.Count >= 2)
            {
                web.Diagonals.Add(ids.Select(i => web.Nodes[i]).ToArray()); web.DiagonalFamily.Add(1);
                web.DiagonalNodes.Add(ids.ToArray());
            }
        }
        for (int j = 1; j < n; j++) { WalkD(0, j); WalkD(1, j); }
        for (int k = 2; k < K; k++) WalkD(k, n - 1);
        double spacing = 0; int cnt = 0;
        foreach (var r in rows) for (int j = 0; j + 1 < r.Length; j++) { spacing += (r[j + 1] - r[j]).Length; cnt++; }
        web.Spacing = cnt > 0 ? spacing / cnt : 1;
        web.Step = web.Spacing;
        web.SymmetryOrder = 1;
        web.Notes.Add($"propagated web: {K} rows × {n} vertices");
        return web;
    }

    /// <summary>
    /// Nodes of the propagated web as rows (row k, vertex j) — the inverse of <see cref="BuildWeb"/> for an optimised web.
    /// </summary>
    public static List<Vec3d[]> RowsOf(IReadOnlyList<Vec3d> nodes, int rowLength)
    {
        var rows = new List<Vec3d[]>();
        for (int k = 0; k * rowLength + rowLength <= nodes.Count; k++)
        {
            var r = new Vec3d[rowLength];
            for (int j = 0; j < rowLength; j++) r[j] = nodes[k * rowLength + j];
            rows.Add(r);
        }
        return rows;
    }
}
