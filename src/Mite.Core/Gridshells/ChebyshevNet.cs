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
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Chebyshev net construction by the compass method. A Chebyshev net has the
/// same edge length in both families everywhere — the kinematics of an elastic
/// gridshell: a flat lattice of constant-length laths with rotating joints,
/// bent into shape. The two axis curves are geodesics traced from the seed;
/// every interior node is placed by intersecting constant-length edges from
/// its two parents on the surface.
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

        public Result(Vec3d[,] points, bool[,] valid, int originU, int originV)
        {
            Points = points;
            Valid = valid;
            OriginU = originU;
            OriginV = originV;
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

        // Axis curves: geodesics resampled at exact edge length
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, d1, L, cu, ou, ov, 1, 0);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, -d1, L, cu, ou, ov, -1, 0);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, d2, L, cv, ou, ov, 0, 1);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, -d2, L, cv, ou, ov, 0, -1);

        // Fill the four quadrants with the compass rule:
        // guess = uParent + vParent - diagonal, then enforce both edge lengths
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

                        Vec3d pA = pts[ip, j], pB = pts[i, jp], pD = pts[ip, jp];
                        Vec3d guess = pA + pB - pD;

                        var hit = proj.ClosestPoint(guess, hints[ip, j]);
                        if ((hit.Point - guess).Length > L) continue;

                        Vec3d x = hit.Point;
                        for (int k = 0; k < 8; k++)
                        {
                            Vec3d ta = x - pA, tb = x - pB;
                            if (ta.LengthSquared < 1e-20 || tb.LengthSquared < 1e-20) break;
                            Vec3d target = 0.5 * ((pA + L * ta.Normalized()) + (pB + L * tb.Normalized()));
                            hit = proj.ClosestPoint(target, hit.NearestVertex);
                            x = hit.Point;
                        }

                        double eA = (x - pA).Length, eB = (x - pB).Length;
                        if (Math.Abs(eA - L) > 0.2 * L || Math.Abs(eB - L) > 0.2 * L) continue;

                        pts[i, j] = x;
                        valid[i, j] = true;
                        hints[i, j] = hit.NearestVertex;
                    }
                }
            }
        }

        return new Result(pts, valid, ou, ov);
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

        // Resample the geodesic at chord length L from the previous node — the
        // same straight edge length the compass rule gives every interior edge.
        // (Arc length would leave the axis chords short by L³κ²/24 — on a
        // unit cylinder with L = 0.3 that is 0.37 %, read by the first interior
        // nodes as a 0.42° shear that no mesh refinement removes.)
        int placed = 0;
        Vec3d prev = start;
        int hint = startHint;
        for (int i = 1; i < line.Count && placed < count; i++)
        {
            Vec3d a = line[i - 1], b = line[i];
            while (placed < count && (b - prev).Length >= L)
            {
                // |a + t (b − a) − prev| = L, the root in [0, 1]
                Vec3d d = b - a, w = a - prev;
                double qa = d.LengthSquared, qb = 2 * Vec3d.Dot(d, w), qc = w.LengthSquared - L * L;
                double t;
                if (qa < 1e-30) t = 1;
                else
                {
                    double disc = Math.Max(0, qb * qb - 4 * qa * qc);
                    t = (-qb + Math.Sqrt(disc)) / (2 * qa);
                    t = Math.Max(0, Math.Min(1, t));
                }
                Vec3d p = a + t * d;
                var hit = proj.ClosestPoint(p, hint);
                hint = hit.NearestVertex;

                placed++;
                int iu = ou + du * placed, iv = ov + dv * placed;
                pts[iu, iv] = hit.Point;
                valid[iu, iv] = true;
                hints[iu, iv] = hint;

                prev = hit.Point;
                a = p; // continue along the same segment from the new node
            }
        }
    }
}
