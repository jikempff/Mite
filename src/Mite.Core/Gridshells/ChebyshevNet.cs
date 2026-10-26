// Shear angles and the Hazzidakis reading of a Chebyshev net
// -----------------------------------------------------------
// A Chebyshev net has |r_u| = |r_v| = 1; its only freedom is the angle ω(u, v)
// between the two families, and Gauss' equation reduces to ω_uv = −K sin ω
// (Hazzidakis 1879; Stoker 1969 §VI; Garg et al. 2014 eq. 1 with the shear
// γ = π/2 − ω: K cos γ = γ_uv). Integrated over one cell this is Hazzidakis'
// formula ∬_cell K dA = 2π − Σ α_i, the α_i being the four interior angles of
// the cell (Garg et al. 2014 eq. 2) — so every cell of a net built on a
// surface reads the total curvature it covers, with no normals, no fitting.
// Existence: a global net without singularities needs the positive and the
// negative total curvature each below 2π (Burago, Ivanov & Malev 2007;
// Masson & Monasse 2017; the quadrant between two orthogonal geodesics needs
// each below π/2, Bakelman), and the angles then stay away from 0 and π;
// where they reach them the net locks (Masson 2017; Sageman-Furnas et al.
// 2019: "the angle between adjacent rods collapses"). Material limits are
// tighter: Garg et al. measured on real wire mesh an exponential rise of the
// shearing force from |γ| ≈ 45°, i.e. cell angles outside [45°, 135°], and
// set γ_max = π/4; Sageman-Furnas et al. 2019 bound the angle by ζ = 30°.
// ShearAngle / ShearAngles give ω per node in degrees (tangents by central
// differences of the nodes), CellCurvature the Hazzidakis integral per cell
// in its alternating form ω(i+1,j) + ω(i,j+1) − ω(i,j) − ω(i+1,j+1), CellArea
// the cell's chord area (so their ratio estimates K), CountLocked the nodes
// outside [lock, 180° − lock]. Measured on the unit sphere (L = 0.2 … 0.1):
// the per-cell integral converges to the spherical area as O(L²), see
// tests/Mite.Tests/ChebyshevAngleTests.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Chebyshev net construction by the compass method. A Chebyshev net has the
/// same edge length in both families everywhere — the kinematics of an elastic
/// gridshell: a flat lattice of constant-length laths with rotating joints,
/// bent into shape. The two axis curves are geodesics traced from the seed;
/// every interior node is placed by intersecting constant-length edges from
/// its two parents on the surface.
///
/// Literature and what is implemented (2026-10-19):
/// - Chebyshev 1878 (Sur la coupe des vêtements): a net of inextensible threads,
///   ds² = du² + 2 cos ω du dv + dv², ω the shear angle; opposite sides of every
///   net quadrilateral are equal (Bianchi, Darboux). Gauss equation
///   ω_uv + K sin ω = 0 (Papadopoulos 2016 §2; Marvan 2024 eq. 6.5).
/// - Hazzidakis 1880: for a net quadrilateral Q with interior angles α₁…α₄,
///   ∫∫_Q K dA = 2π − (α₁ + α₂ + α₃ + α₄). (Integrate the Gauss equation with
///   dA = sin ω du dv: the corner angles are ω, π − ω, ω, π − ω.) Hence on
///   K &gt; 0 the shear angle shrinks away from the axes until the cell locks
///   (ω → 0 or π), and no cell can carry |∫∫K dA| ≥ 2π.
/// - Existence: Bieberbach 1926 / Bakelman 1965 locally; Samelson &amp;
///   Dayawansa 1995 (|∫K| &lt; π/2 on every measurable set), Burago, Ivanov &amp;
///   Malev 2007 (positive and negative total curvature each &lt; 2π), Masson &amp;
///   Monasse 2017 (∫|K| &lt; 2π); Masson 2017 (thesis) extends this with
///   singularities for gridshells. A compass net that locks is therefore not
///   a tracing failure but the surface's total curvature.
/// - Compass method (Otto, Hennicke &amp; Matsushita 1974, IL10; Bouhaya, Baverel
///   &amp; Caron 2015 §3): two axis curves through the origin, nodes an edge
///   length apart along them; the fourth node of every cell is the
///   intersection of the surface with the two spheres of radius L about its
///   two parents, on the far side of the diagonal parent. Here that
///   intersection is solved by Newton's method in the tangent plane with
///   reprojection onto the mesh (|x − a|² = |x − b|² = L² to 1e-10 L²), so
///   every edge is L to round-off; the Jacobian determinant is 4 L² sin ω,
///   singular exactly where the net locks. The axes are sampled by chord L
///   as well, so the whole net is a discrete Chebyshev net in the sense of
///   Garg et al. 2014 (Wire mesh design §9.2: η_len = max |‖e‖/L − 1| = 0).
/// - Shear-angle bound (Garg et al. 2014 §6.1, min ⟨U,V⟩ ≥ cos ζ, ζ = 30° in
///   their examples): <see cref="Options.MinAngle"/> stops the net where a
///   cell would shear below it, before the laths touch.
/// </summary>
public static class ChebyshevNet
{
    public class Options
    {
        public double EdgeLength { get; set; } = 1.0;

        /// <summary>Nodes per side of the seed in the first family.</summary>
        public int CountU { get; set; } = 10;

        /// <summary>Nodes per side of the seed in the second family.</summary>
        public int CountV { get; set; } = 10;

        /// <summary>Angle between the two families at the seed (default 90 degrees).</summary>
        public double Angle { get; set; } = Math.PI / 2;

        /// <summary>
        /// Smallest shear angle (radians) a cell may have. A node whose cell
        /// would shear below it, or above π − it, is not placed, so the net
        /// stops where the laths would lock (Garg et al. 2014 use 30°).
        /// 0 (default) places every node the compass can reach.
        /// </summary>
        public double MinAngle { get; set; } = 0.0;
    }

    public readonly struct Result
    {
        /// <summary>Net nodes, indexed [u, v]. Only entries flagged in Valid are meaningful.</summary>
        public readonly Vec3d[,] Points;

        /// <summary>True where the net reached the surface; ragged at boundaries.</summary>
        public readonly bool[,] Valid;

        /// <summary>Indices of the seed node.</summary>
        public readonly int OriginU;
        public readonly int OriginV;

        /// <summary>
        /// Shear angle (radians, 0…π) between the two lath directions at each
        /// valid node, from its forward (else backward) neighbours; NaN where a
        /// family has no neighbour.
        /// </summary>
        public readonly double[,] ShearAngles;

        /// <summary>Largest relative edge-length deviation max |‖e‖/L − 1| over the net (Garg et al. 2014, η_len).</summary>
        public readonly double EdgeError;

        /// <summary>Nodes the compass reached but <see cref="Options.MinAngle"/> refused (the net locked there).</summary>
        public readonly int Locked;

        /// <summary>Compass steps whose two spheres do not meet the mesh (left the surface, or the cell collapsed).</summary>
        public readonly int Failed;

        public Result(Vec3d[,] points, bool[,] valid, int originU, int originV)
            : this(points, valid, originU, originV, new double[points.GetLength(0), points.GetLength(1)], 0, 0, 0) { }

        public Result(Vec3d[,] points, bool[,] valid, int originU, int originV,
            double[,] shearAngles, double edgeError, int locked, int failed)
        {
            Points = points;
            Valid = valid;
            OriginU = originU;
            OriginV = originV;
            ShearAngles = shearAngles;
            EdgeError = edgeError;
            Locked = locked;
            Failed = failed;
        }

        /// <summary>Number of valid nodes.</summary>
        public int ValidCount
        {
            get { int n = 0; foreach (bool b in Valid) if (b) n++; return n; }
        }
    }

    public static Result Compute(MeshData mesh, int seedVertex, Vec3d direction, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(mesh);
        double L = options.EdgeLength;

        int cu = Math.Max(1, options.CountU);
        int cv = Math.Max(1, options.CountV);
        int nu = 2 * cu + 1, nv = 2 * cv + 1;
        int ou = cu, ov = cv;

        var pts = new Vec3d[nu, nv];
        var valid = new bool[nu, nv];
        var hints = new int[nu, nv];

        if (seedVertex < 0 || seedVertex >= proj.Mesh.VertexCount || L <= 0)
            return new Result(pts, valid, ou, ov);

        var seedHit = proj.ClosestPoint(proj.Mesh.Vertices[seedVertex], seedVertex);
        Vec3d n0 = seedHit.SmoothNormal;

        // Family directions in the seed's tangent plane
        Vec3d d1 = direction - Vec3d.Dot(direction, n0) * n0;
        if (d1.LengthSquared < 1e-20)
            d1 = Vec3d.Cross(n0, Math.Abs(n0.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0));
        d1 = d1.Normalized();
        Vec3d d2 = (Math.Cos(options.Angle) * d1 +
                    Math.Sin(options.Angle) * Vec3d.Cross(n0, d1).Normalized()).Normalized();

        pts[ou, ov] = seedHit.Point;
        valid[ou, ov] = true;
        hints[ou, ov] = seedHit.NearestVertex;

        // Axis curves: geodesics sampled at exact chord length
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, d1, L, cu, ou, ov, 1, 0);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, -d1, L, cu, ou, ov, -1, 0);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, d2, L, cv, ou, ov, 0, 1);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, -d2, L, cv, ou, ov, 0, -1);

        double minAngle = Math.Max(0.0, Math.Min(Math.PI / 2, options.MinAngle));
        int locked = 0, failed = 0;

        // Fill the four quadrants with the compass rule: the fourth node of the
        // cell (ip,jp) (ip,j) (i,jp) is the surface point an edge length from
        // both (ip,j) and (i,jp), on the far side of the diagonal (ip,jp)
        foreach (int su in new[] { 1, -1 })
        {
            foreach (int sv in new[] { 1, -1 })
            {
                for (int a = 1; a <= cu; a++)
                {
                    for (int b = 1; b <= cv; b++)
                    {
                        int i = ou + su * a, j = ov + sv * b;
                        int ip = i - su, jp = j - sv;

                        if (!valid[ip, j] || !valid[i, jp] || !valid[ip, jp]) continue;

                        if (!CompassPoint(proj, pts[ip, j], pts[i, jp], pts[ip, jp], L, hints[ip, j], out var x, out int hint))
                        {
                            failed++;
                            continue;
                        }

                        if (minAngle > 0 && !CrossingsOk(pts, valid, x, i, j, su, sv, minAngle))
                        {
                            locked++;
                            continue;
                        }

                        pts[i, j] = x;
                        valid[i, j] = true;
                        hints[i, j] = hint;
                    }
                }
            }
        }

        var shear = new double[nu, nv];
        double edgeError = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                shear[i, j] = valid[i, j] ? ShearAngleAt(pts, valid, i, j) : double.NaN;
                if (!valid[i, j]) continue;
                if (i + 1 < nu && valid[i + 1, j])
                    edgeError = Math.Max(edgeError, Math.Abs((pts[i + 1, j] - pts[i, j]).Length / L - 1));
                if (j + 1 < nv && valid[i, j + 1])
                    edgeError = Math.Max(edgeError, Math.Abs((pts[i, j + 1] - pts[i, j]).Length / L - 1));
            }

        return new Result(pts, valid, ou, ov, shear, edgeError, locked, failed);
    }

    /// <summary>
    /// The compass step: the point of the mesh at distance L from both a and b
    /// on the far side of d. Newton's method on f = (|x−a|² − L², |x−b|² − L²)
    /// in the tangent plane of the current facet, reprojected onto the mesh
    /// after every step; the 2 × 2 Jacobian has determinant 4 L² sin ω, so it
    /// is singular only where the cell has collapsed. Returns false when the
    /// spheres do not meet the mesh (the guess lies more than L off the
    /// surface, Newton does not converge, or the root is on d's side).
    /// </summary>
    internal static bool CompassPoint(MeshProjection proj, Vec3d a, Vec3d b, Vec3d d, double L, int hint,
        out Vec3d x, out int hintOut)
    {
        Vec3d guess = a + b - d;
        var hit = proj.ClosestPoint(guess, hint);
        x = hit.Point;
        hintOut = hit.NearestVertex;
        if ((x - guess).Length > L) return false;

        double L2 = L * L, tol = 1e-14 * L2, loose = 1e-9 * L2;
        bool converged = false;
        Vec3d best = x; int bestHint = hintOut; double bestF = double.MaxValue;
        for (int k = 0; k < 40; k++)
        {
            Vec3d ra = x - a, rb = x - b;
            double fa = ra.LengthSquared - L2, fb = rb.LengthSquared - L2;
            double f = Math.Max(Math.Abs(fa), Math.Abs(fb));
            if (f < bestF) { bestF = f; best = x; bestHint = hintOut; }
            if (f < tol) { converged = true; break; }

            Vec3d n = hit.Normal.LengthSquared > 0.5 ? hit.Normal : hit.SmoothNormal;
            if (n.LengthSquared < 1e-20) return false;
            n = n.Normalized();
            Vec3d e1 = Vec3d.Cross(n, Math.Abs(n.X) < 0.8 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0)).Normalized();
            Vec3d e2 = Vec3d.Cross(n, e1);

            double j11 = 2 * Vec3d.Dot(ra, e1), j12 = 2 * Vec3d.Dot(ra, e2);
            double j21 = 2 * Vec3d.Dot(rb, e1), j22 = 2 * Vec3d.Dot(rb, e2);
            double det = j11 * j22 - j12 * j21;
            if (Math.Abs(det) < 1e-9 * L2 * L2) return false;        // sin ω ≈ 0: the cell is locked

            double s1 = (-fa * j22 + fb * j12) / det;
            double s2 = (-j11 * fb + j21 * fa) / det;
            Vec3d step = s1 * e1 + s2 * e2;
            double len = step.Length;
            if (len > L) step = (L / len) * step;                        // never jump more than an edge

            hit = proj.ClosestPoint(x + step, hit.NearestVertex);
            if ((hit.Point - x).LengthSquared < 1e-30 * L2 && k > 2) break; // stuck (border)
            x = hit.Point;
            hintOut = hit.NearestVertex;
        }
        if (!converged)
        {
            // Newton chatters across a facet edge: take the best iterate if it
            // is within a tolerance that is still invisible in the geometry
            if (bestF >= loose) return false;
            x = best; hintOut = bestHint;
        }

        // The two spheres meet in a circle symmetric about the plane through
        // a, b and the normal; the compass takes the root away from d
        Vec3d m = 0.5 * (a + b);
        return Vec3d.Dot(x - m, m - d) > 0;
    }

    /// <summary>
    /// The crossing angles a new node x at (i, j) creates: at x between its two
    /// edges, and at each parent between the new edge and the parent's existing
    /// edges of the other family (towards the seed and, when already placed,
    /// away from it). Every angle a lath crossing can show is one of these, so
    /// a net built under this rule has no crossing outside [min, π − min].
    /// </summary>
    private static bool CrossingsOk(Vec3d[,] pts, bool[,] valid, Vec3d x, int i, int j, int su, int sv, double min)
    {
        int nu = pts.GetLength(0), nv = pts.GetLength(1);
        int ip = i - su, jp = j - sv;
        Vec3d pa = pts[ip, j], pb = pts[i, jp];
        bool Ok(Vec3d u, Vec3d v)
        {
            double w = AngleBetween(u, v);
            return !(w < min || w > Math.PI - min);
        }
        if (!Ok(x - pa, x - pb)) return false;
        // at the u-parent: new u-edge against its v-edges
        if (!Ok(x - pa, pts[ip, jp] - pa)) return false;
        int jn = j + sv;
        if (jn >= 0 && jn < nv && valid[ip, jn] && !Ok(x - pa, pts[ip, jn] - pa)) return false;
        // at the v-parent: new v-edge against its u-edges
        if (!Ok(x - pb, pts[ip, jp] - pb)) return false;
        int inx = i + su;
        if (inx >= 0 && inx < nu && valid[inx, jp] && !Ok(x - pb, pts[inx, jp] - pb)) return false;
        return true;
    }

    /// <summary>Angle between the two lath directions at node (i, j) in radians, NaN without a neighbour in each family.</summary>
    public static double ShearAngle(in Result net, int i, int j) =>
        net.Valid[i, j] ? ShearAngleAt(net.Points, net.Valid, i, j) : double.NaN;

    private static double ShearAngleAt(Vec3d[,] pts, bool[,] valid, int i, int j)
    {
        int nu = pts.GetLength(0), nv = pts.GetLength(1);
        Vec3d p = pts[i, j];
        Vec3d? du = null, dv = null;
        if (i + 1 < nu && valid[i + 1, j]) du = pts[i + 1, j] - p;
        else if (i > 0 && valid[i - 1, j]) du = p - pts[i - 1, j];
        if (j + 1 < nv && valid[i, j + 1]) dv = pts[i, j + 1] - p;
        else if (j > 0 && valid[i, j - 1]) dv = p - pts[i, j - 1];
        if (du == null || dv == null) return double.NaN;
        return AngleBetween(du.Value, dv.Value);
    }

    private static double AngleBetween(Vec3d u, Vec3d v)
    {
        double lu = u.Length, lv = v.Length;
        if (lu < 1e-300 || lv < 1e-300) return double.NaN;
        double c = Vec3d.Dot(u, v) / (lu * lv);
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c)));
    }

    /// <summary>
    /// Angle ω (degrees) between the two lath directions at node (i, j): the
    /// u-tangent and v-tangent by central differences of the neighbouring
    /// nodes (second-order one-sided differences on the net's rim, a plain
    /// chord where only one neighbour exists). NaN for an invalid node or a
    /// node with no neighbour in one family. 90 is a square cell; Garg's shear
    /// is 90 − ω; the net locks where ω reaches 0 or 180.
    /// </summary>
    public static double ShearAngle(Result net, int i, int j)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        if (i < 0 || j < 0 || i >= nu || j >= nv || !net.Valid[i, j]) return double.NaN;
        var tu = Tangent(net, i, j, 1, 0);
        var tv = Tangent(net, i, j, 0, 1);
        if (tu == null || tv == null) return double.NaN;
        return Angle(tu.Value, tv.Value) * 180.0 / Math.PI;
    }

    /// <summary>Tangent of the (du, dv) family at node (i, j), pointing towards increasing index.</summary>
    private static Vec3d? Tangent(Result net, int i, int j, int du, int dv)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        bool Ok(int a, int b) => a >= 0 && b >= 0 && a < nu && b < nv && net.Valid[a, b];
        Vec3d P(int a, int b) => net.Points[a, b];
        bool fwd = Ok(i + du, j + dv), bwd = Ok(i - du, j - dv);
        Vec3d t;
        if (fwd && bwd) t = P(i + du, j + dv) - P(i - du, j - dv);
        // on the rim: one-sided differences of third order where three nodes follow (their
        // O(L³) error keeps the cell integral converging; second order would leave an O(L²)
        // error there, as large as K·L² itself), else second, else the chord
        else if (fwd && Ok(i + 3 * du, j + 3 * dv))
            t = -11.0 / 6 * P(i, j) + 3 * P(i + du, j + dv) - 1.5 * P(i + 2 * du, j + 2 * dv) + P(i + 3 * du, j + 3 * dv) / 3;
        else if (bwd && Ok(i - 3 * du, j - 3 * dv))
            t = 11.0 / 6 * P(i, j) - 3 * P(i - du, j - dv) + 1.5 * P(i - 2 * du, j - 2 * dv) - P(i - 3 * du, j - 3 * dv) / 3;
        else if (fwd && Ok(i + 2 * du, j + 2 * dv)) t = -3 * P(i, j) + 4 * P(i + du, j + dv) - P(i + 2 * du, j + 2 * dv);
        else if (bwd && Ok(i - 2 * du, j - 2 * dv)) t = 3 * P(i, j) - 4 * P(i - du, j - dv) + P(i - 2 * du, j - 2 * dv);
        else if (fwd) t = P(i + du, j + dv) - P(i, j);
        else if (bwd) t = P(i, j) - P(i - du, j - dv);
        else return null;
        return t.LengthSquared < 1e-30 ? null : t;
    }

    /// <summary>ShearAngle at every node, indexed like Points; NaN where undefined.</summary>
    public static double[,] ShearAngles(Result net)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        var a = new double[nu, nv];
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
                a[i, j] = ShearAngle(net, i, j);
        return a;
    }

    /// <summary>
    /// Number of nodes whose angle lies outside [lockAngle, 180° − lockAngle]:
    /// cells sheared past the material limit (Garg et al. 2014: 45°).
    /// </summary>
    public static int CountLocked(Result net, double lockAngleDeg)
    {
        int n = 0;
        var a = ShearAngles(net);
        foreach (double w in a)
            if (!double.IsNaN(w) && (w < lockAngleDeg || w > 180.0 - lockAngleDeg)) n++;
        return n;
    }

    /// <summary>
    /// Hazzidakis' integral per cell: ∬ K dA over the cell with corners
    /// (i, j), (i+1, j), (i+1, j+1), (i, j+1) equals 2π minus the sum of its
    /// four interior angles, i.e. ω(i+1, j) + ω(i, j+1) − ω(i, j) − ω(i+1, j+1)
    /// in radians with the node angles of <see cref="ShearAngle"/> (the
    /// alternating form −Δ_uv ω of ω_uv = −K sin ω). Indexed [nu − 1, nv − 1];
    /// NaN where a corner is missing. Exactly 0 on a plane, positive on a
    /// dome, negative on a saddle; divide by <see cref="CellArea"/> for K.
    /// (The corner angles of the chord polygon do not work here: their
    /// one-sided O(L) errors are as large as K·L² itself.)
    /// </summary>
    public static double[,] CellCurvature(Result net)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        var w = ShearAngles(net);
        var k = new double[Math.Max(0, nu - 1), Math.Max(0, nv - 1)];
        const double rad = Math.PI / 180.0;
        for (int i = 0; i + 1 < nu; i++)
            for (int j = 0; j + 1 < nv; j++)
            {
                double w00 = w[i, j], w10 = w[i + 1, j], w11 = w[i + 1, j + 1], w01 = w[i, j + 1];
                if (double.IsNaN(w00) || double.IsNaN(w10) || double.IsNaN(w11) || double.IsNaN(w01))
                { k[i, j] = double.NaN; continue; }
                k[i, j] = (w10 + w01 - w00 - w11) * rad;
            }
        return k;
    }

    /// <summary>
    /// Chord area of a cell: the mean of its two diagonal triangulations
    /// (equal for a planar cell). NaN where a corner is missing.
    /// </summary>
    public static double CellArea(Result net, int i, int j)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        if (i < 0 || j < 0 || i + 1 >= nu || j + 1 >= nv) return double.NaN;
        if (!(net.Valid[i, j] && net.Valid[i + 1, j] && net.Valid[i + 1, j + 1] && net.Valid[i, j + 1])) return double.NaN;
        Vec3d p0 = net.Points[i, j], p1 = net.Points[i + 1, j], p2 = net.Points[i + 1, j + 1], p3 = net.Points[i, j + 1];
        double a1 = 0.5 * (Vec3d.Cross(p1 - p0, p2 - p0).Length + Vec3d.Cross(p2 - p0, p3 - p0).Length);
        double a2 = 0.5 * (Vec3d.Cross(p0 - p1, p3 - p1).Length + Vec3d.Cross(p3 - p1, p2 - p1).Length);
        return 0.5 * (a1 + a2);
    }

    private static double Angle(Vec3d a, Vec3d b)
    {
        // atan2 form: exact for tiny and for near-π angles where acos loses digits
        return Math.Atan2(Vec3d.Cross(a, b).Length, Vec3d.Dot(a, b));
    }

    private static void FillAxis(
        MeshProjection proj, Vec3d[,] pts, bool[,] valid, int[,] hints,
        Vec3d start, int startHint, Vec3d dir, double L, int count,
        int ou, int ov, int du, int dv)
    {
        double step = L / 8.0;
        int maxSteps = 16 * count + 16;
        var line = GeodesicCurves.TraceOneFrom(proj, start, startHint, dir, step, maxSteps, null);

        // Nodes a chord L apart along the geodesic: on the segment whose end
        // first reaches distance L from the previous node, the parameter t
        // where the projected point p(t) is exactly L away (the polyline's
        // vertices lie on the facets but its segments cut through them, so
        // the root is taken on the projected curve by regula falsi)
        int placed = 0;
        Vec3d prev = start;
        int hint = startHint;
        double L2 = L * L;
        double tLo = 0;                                              // where prev sits on the current segment
        for (int i = 1; i < line.Count && placed < count; i++)
        {
            if ((line[i] - prev).LengthSquared < L2) { tLo = 0; continue; }
            Vec3d p0 = line[i - 1], seg = line[i] - p0;
            if (seg.LengthSquared < 1e-300) { tLo = 0; continue; }

            double lo = tLo, hi = 1.0;
            double fLo = (proj.ClosestPoint(p0 + lo * seg, hint).Point - prev).LengthSquared - L2;
            double fHi = (line[i] - prev).LengthSquared - L2;
            if (fLo >= 0) { lo = 0; fLo = (p0 - prev).LengthSquared - L2; }
            if (fLo >= 0) { tLo = 0; continue; }                       // no bracket: skip the segment

            MeshProjection.Hit hit = proj.ClosestPoint(p0 + hi * seg, hint);
            double t = hi;
            int side = 0;
            for (int k = 0; k < 80; k++)
            {
                t = hi - fHi * (hi - lo) / (fHi - fLo);                  // Illinois regula falsi
                hit = proj.ClosestPoint(p0 + t * seg, hint);
                double f = (hit.Point - prev).LengthSquared - L2;
                if (Math.Abs(f) < 1e-13 * L2 || hi - lo < 1e-15) break;
                if (f > 0) { hi = t; fHi = f; if (side == 1) fLo *= 0.5; side = 1; }
                else { lo = t; fLo = f; if (side == -1) fHi *= 0.5; side = -1; }
            }
            hint = hit.NearestVertex;

            placed++;
            int iu = ou + du * placed, iv = ov + dv * placed;
            pts[iu, iv] = hit.Point;
            valid[iu, iv] = true;
            hints[iu, iv] = hint;
            prev = hit.Point;
            tLo = t;
            i--;                                                     // the next node may lie on the same segment
        }
    }
}
