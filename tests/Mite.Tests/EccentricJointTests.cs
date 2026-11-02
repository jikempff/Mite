using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Joint eccentricity in Gridshell Analysis (2026-11-02): laths stacked in
/// layers keep their own axes, each beam end hangs on a rigid link from its
/// node on the surface, and the forces handed between layers at a crossing act
/// with the lever arm of the stack. Checked against closed forms: an L-frame
/// whose upper lath pulls on the lower one (torsion e·F exactly), the
/// invariance of a statically determinate frame under offsets parallel to the
/// load, the rigid-link transfer matrix itself, and the moment equilibrium of
/// a ruling grid on the hyperboloid with the two families in two layers.
/// </summary>
public class EccentricJointTests
{
    private const double E = 11e9;
    private static readonly LathProfile Flat = new LathProfile(0.1, 0.02, false);

    private static Vec3d[] Line(Vec3d a, Vec3d b, int n)
    {
        var p = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) p[i] = a + (double)i / n * (b - a);
        return p;
    }

    private static int NodeAt(FrameAnalysis.Result r, Vec3d p) => Array.FindIndex(r.Nodes, q => (q - p).Length < 1e-9);

    // A: one-element cantilever along x, clamped at its root, axis on the
    // surface (h = 0). B: lath along y hanging on A's tip, axis e above the
    // surface. Load q per unit length on every lath.
    private static (Vec3d[] a, Vec3d[] b, MeshData mesh) LFrame(double L, double b, int mB)
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        var a = Line(new Vec3d(2, 3, 0), new Vec3d(2 + L, 3, 0), 1);
        var bb = Line(new Vec3d(2 + L, 3, 0), new Vec3d(2 + L, 3 + b, 0), mB);
        return (a, bb, mesh);
    }

    [Fact]
    public void UpperLath_PullingAlongItsAxis_TwistsTheLowerLathByEccentricityTimesForce()
    {
        const double L = 4.0, b = 2.0, q = 300.0, e = 0.05;
        var (a, bb, mesh) = LFrame(L, b, 8);
        var sec = Flat.SectionProperties();
        double A = sec.Area, Iz = sec.IB, J = sec.J, G = E / 2.6;
        var load = new Vec3d(0, q, 0); // horizontal, across A and along B

        var r = FrameAnalysis.Compute(mesh, new[] { a, bb }, new[] { bb[0] }, new[] { a[0] }, Flat, load,
            new FrameAnalysis.Options { E = E, AxisOffsets = new[] { 0.0, e } });
        Assert.InRange(r.Eccentricity, e * (1 - 1e-12), e * (1 + 1e-12));
        Assert.Equal(1, r.JointCount);

        // B is in pure tension (its load runs along its axis): N = q·b at the
        // joint, handed to A's tip e above A's axis → torque e·q·b in A, plus
        // the transverse tip force ½qL + qb that bends A in its plane
        double P = 0.5 * q * L + q * b, T = e * q * b;
        double v = P * L * L * L / (3 * E * Iz);        // A's tip deflection along y
        double thetaZ = P * L * L / (2 * E * Iz);       // A's tip rotation about z
        double phi = T * L / (G * J);                   // A's tip twist about x
        double dB = q * b * b / (2 * E * A);            // B's elongation under its own load

        var aElems = Enumerable.Range(0, r.ElementSource.Length).Where(i => r.ElementSource[i].Curve == 0).ToArray();
        var bElems = Enumerable.Range(0, r.ElementSource.Length).Where(i => r.ElementSource[i].Curve == 1).ToArray();
        Assert.Single(aElems);
        Assert.InRange(r.Torsion[aElems[0]] / T, 1 - 1e-9, 1 + 1e-9);
        Assert.All(bElems, i => Assert.InRange(r.Torsion[i], 0, 1e-9 * T));
        Assert.All(bElems, i => Assert.InRange(r.BendingY[i] + r.BendingZ[i], 0, 1e-9 * P * L));
        Assert.InRange(r.AxialForce[bElems[0]] / (q * b * (1 - 0.5 / 8)), 1 - 1e-9, 1 + 1e-9); // first element of B: q b (1 − ½/m)

        int tip = NodeAt(r, a[^1]), end = NodeAt(r, bb[^1]);
        Assert.InRange(r.Displacements[tip].Y / v, 1 - 1e-9, 1 + 1e-9);
        // B's far node on the surface: A's tip deflection + B's stretch (the
        // twist moves B's axis by −φ·e and its node back by +φ·e); along x
        // the tip rotation about z swings B by −θz·b
        Assert.InRange(r.Displacements[end].Y / (v + dB), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(r.Displacements[end].X / (-thetaZ * b), 1 - 1e-9, 1 + 1e-9);

        // reactions: force and moment balance the loads about the origin
        int root = r.SupportNodes[0];
        Assert.InRange(r.Reactions[root].Y / (-q * (L + b)), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Math.Abs(r.ReactionMoments[root].X) / T, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(r.EquilibriumError, 0, 1e-10);
        Assert.InRange(r.MomentEquilibriumError, 0, 1e-10);

        // without the stack there is no torque and no twist
        var flat = FrameAnalysis.Compute(mesh, new[] { a, bb }, new[] { bb[0] }, new[] { a[0] }, Flat, load,
            new FrameAnalysis.Options { E = E });
        Assert.Equal(0.0, flat.Eccentricity);
        Assert.InRange(flat.Torsion.Max(), 0, 1e-9 * T);
        Assert.InRange(flat.Displacements[NodeAt(flat, a[^1])].Y / v, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(phi, 1e-6, 1); // the twist is not a rounding effect at this size
    }

    [Fact]
    public void Offsets_ParallelToTheLoad_LeaveTheForcesOfADeterminateFrameUnchanged()
    {
        const double L = 4.0, b = 2.0, q = 300.0, e = 0.05;
        var (a, bb, mesh) = LFrame(L, b, 8);
        var sec = Flat.SectionProperties();
        double Iy = sec.IA;
        var load = new Vec3d(0, 0, -q); // vertical, along the normal like the offsets

        var r0 = FrameAnalysis.Compute(mesh, new[] { a, bb }, new[] { bb[0] }, new[] { a[0] }, Flat, load,
            new FrameAnalysis.Options { E = E });
        var r1 = FrameAnalysis.Compute(mesh, new[] { a, bb }, new[] { bb[0] }, new[] { a[0] }, Flat, load,
            new FrameAnalysis.Options { E = E, AxisOffsets = new[] { 0.0, e } });
        Assert.Equal(r0.Utilization.Length, r1.Utilization.Length);
        double scale = r0.BendingY.Max();
        for (int i = 0; i < r0.Utilization.Length; i++)
        {
            Assert.InRange(Math.Abs(r1.AxialForce[i] - r0.AxialForce[i]), 0, 1e-9 * scale);
            Assert.InRange(Math.Abs(r1.BendingY[i] - r0.BendingY[i]), 0, 1e-9 * scale);
            Assert.InRange(Math.Abs(r1.BendingZ[i] - r0.BendingZ[i]), 0, 1e-9 * scale);
            Assert.InRange(Math.Abs(r1.Torsion[i] - r0.Torsion[i]), 0, 1e-9 * scale);
            Assert.InRange(Math.Abs(r1.Utilization[i] - r0.Utilization[i]), 0, 1e-9 * r0.MaxUtilization);
        }
        int root = r0.SupportNodes[0];
        Assert.InRange((r1.Reactions[root] - r0.Reactions[root]).Length, 0, 1e-9 * q * (L + b));
        Assert.InRange((r1.ReactionMoments[root] - r0.ReactionMoments[root]).Length, 0, 1e-9 * q * (L + b) * L);

        // kinematics: the surface nodes move as before — the upper lath's axis
        // is carried sideways by A's tip rotation θy times the stack, and its
        // surface node comes back by the same θy·e through its own link
        int end0 = NodeAt(r0, bb[^1]), end1 = NodeAt(r1, bb[^1]);
        double wz = Math.Abs(r0.Displacements[end0].Z);
        Assert.InRange(Math.Abs(r1.Displacements[end1].Z - r0.Displacements[end0].Z), 0, 1e-9 * wz);
        double P = 0.5 * q * L + q * b, thetaY = P * L * L / (2 * E * Iy);
        Assert.InRange(thetaY * e, 1e-9 * wz, wz); // the cancelled term is well above the tolerance
        Assert.InRange(Math.Abs(r0.Displacements[end0].X), 0, 1e-9 * wz);
        Assert.InRange(Math.Abs(r1.Displacements[end1].X), 0, 1e-9 * wz);
        Assert.InRange(r1.MomentEquilibriumError, 0, 1e-10);
    }

    [Fact]
    public void RigidLinkTransfer_IsCongruent_AndCostsRigidMotionsNothing()
    {
        // a symmetric positive semi-definite "stiffness" with the six rigid
        // modes in its null space stays so under Hᵀ K H, and a rigid motion of
        // the nodes (translation + rotation × arm) still costs nothing
        var rnd = new Random(7);
        var B = new double[12, 12];
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) B[i, j] = rnd.NextDouble() - 0.5;
        var pA = new Vec3d(0, 0, 0); var pB = new Vec3d(1.3, 0.2, -0.4);
        // rigid modes at the beam ends: 3 translations, 3 rotations about the origin
        var modes = new List<double[]>();
        for (int d = 0; d < 3; d++) { var m = new double[12]; m[d] = 1; m[6 + d] = 1; modes.Add(m); }
        for (int d = 0; d < 3; d++)
        {
            var w = new Vec3d(d == 0 ? 1 : 0, d == 1 ? 1 : 0, d == 2 ? 1 : 0);
            var m = new double[12];
            var ua = Vec3d.Cross(w, pA); var ub = Vec3d.Cross(w, pB);
            m[0] = ua.X; m[1] = ua.Y; m[2] = ua.Z; m[3] = w.X; m[4] = w.Y; m[5] = w.Z;
            m[6] = ub.X; m[7] = ub.Y; m[8] = ub.Z; m[9] = w.X; m[10] = w.Y; m[11] = w.Z;
            modes.Add(m);
        }
        // K = Pᵀ BᵀB P with P the projector onto the complement of the rigid modes
        var Q = Orthonormal(modes);
        var P = new double[12, 12];
        for (int i = 0; i < 12; i++) { P[i, i] = 1; foreach (var qv in Q) for (int j = 0; j < 12; j++) P[i, j] -= qv[i] * qv[j]; }
        var K = Mul(Transpose(P), Mul(Mul(Transpose(B), B), P));

        var rA = new Vec3d(0, 0, 0.3); var rB = new Vec3d(0.1, -0.2, 0.25);
        var Kh = FrameAnalysis.OffsetStiffness(K, rA, rB);
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) Assert.InRange(Math.Abs(Kh[i, j] - Kh[j, i]), 0, 1e-12);
        // a rigid motion of the nodes (whose beam ends sit at p + r): zero force
        var nA = pA - rA; var nB = pB - rB;
        foreach (var w in new[] { new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1), new Vec3d(0.3, -0.7, 0.2) })
        {
            var u = new double[12];
            var ua = Vec3d.Cross(w, nA); var ub = Vec3d.Cross(w, nB);
            u[0] = ua.X + 0.5; u[1] = ua.Y - 0.2; u[2] = ua.Z + 0.1; u[3] = w.X; u[4] = w.Y; u[5] = w.Z;
            u[6] = ub.X + 0.5; u[7] = ub.Y - 0.2; u[8] = ub.Z + 0.1; u[9] = w.X; u[10] = w.Y; u[11] = w.Z;
            for (int i = 0; i < 12; i++)
            {
                double f = 0; for (int j = 0; j < 12; j++) f += Kh[i, j] * u[j];
                Assert.InRange(Math.Abs(f), 0, 1e-10);
            }
        }
        // and a non-rigid motion still costs: the quadratic form is positive
        var v = new double[12]; v[7] = 1;
        double energy = 0; for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) energy += v[i] * Kh[i, j] * v[j];
        Assert.True(energy > 1e-3);
        // zero arms: identity
        var K0 = FrameAnalysis.OffsetStiffness(K, Vec3d.Zero, Vec3d.Zero);
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) Assert.Equal(K[i, j], K0[i, j], 14);
    }

    [Fact]
    public void HyperboloidRulingGrid_InTwoLayers_BalancesForcesAndMoments()
    {
        // the two ruling families of x² + y² − z² = 1 as straight laths,
        // family + on the surface, family − one lath depth above it; every
        // crossing is a joint, every rim end a fixed support
        const int n = 16, m = 10;
        var mesh = TestMeshes.CreateHyperboloid(1, 1, 1, 96, 48);
        var profile = new LathProfile(0.06, 0.012, true); // upright: depth = width
        double depth = profile.NormalHigh - profile.NormalLow;
        var laths = new List<Vec3d[]>();
        for (int k = 0; k < n; k++)
        {
            double th = 2 * Math.PI * k / n;
            laths.Add(Line(new Vec3d(Math.Cos(th) + Math.Sin(th), Math.Sin(th) - Math.Cos(th), -1), new Vec3d(Math.Cos(th) - Math.Sin(th), Math.Sin(th) + Math.Cos(th), 1), m));
        }
        for (int k = 0; k < n; k++)
        {
            double th = 2 * Math.PI * k / n;
            laths.Add(Line(new Vec3d(Math.Cos(th) - Math.Sin(th), Math.Sin(th) + Math.Cos(th), -1), new Vec3d(Math.Cos(th) + Math.Sin(th), Math.Sin(th) - Math.Cos(th), 1), m));
        }
        foreach (var l in laths) foreach (var p in l) Assert.InRange(Math.Abs(p.X * p.X + p.Y * p.Y - p.Z * p.Z - 1), 0, 1e-12);
        // crossings: ruling + from θ1 meets ruling − from θ2 at t = tan((θ2 − θ1)/2)
        var joints = new List<Vec3d>();
        for (int k1 = 0; k1 < n; k1++)
            for (int k2 = 0; k2 < n; k2++)
            {
                double d = 2 * Math.PI * (k2 - k1) / n;
                d = Math.Atan2(Math.Sin(d), Math.Cos(d));
                double t = Math.Tan(0.5 * d);
                if (Math.Abs(t) > 1 - 1e-9) continue;
                double th = 2 * Math.PI * k1 / n;
                joints.Add(new Vec3d(Math.Cos(th) - t * Math.Sin(th), Math.Sin(th) + t * Math.Cos(th), t));
            }
        var supports = laths.SelectMany(l => new[] { l[0], l[^1] }).ToList();
        var offsets = Enumerable.Range(0, 2 * n).Select(c => c < n ? 0.0 : depth).ToList();

        foreach (var load in new[] { new Vec3d(1000, 0, 0), new Vec3d(0, 0, -1000) })
        {
            var r0 = FrameAnalysis.Compute(mesh, laths, joints, supports, profile, load, new FrameAnalysis.Options { E = E });
            var r1 = FrameAnalysis.Compute(mesh, laths, joints, supports, profile, load, new FrameAnalysis.Options { E = E, AxisOffsets = offsets });
            Assert.Equal(r0.JointCount, r1.JointCount);
            Assert.True(r1.JointCount >= 0.95 * joints.Count, $"{r1.JointCount} joints of {joints.Count}");
            Assert.InRange(r1.Eccentricity / depth, 1 - 1e-9, 1 + 1e-9);
            Assert.InRange(r1.EquilibriumError, 0, 1e-9);
            Assert.InRange(r1.MomentEquilibriumError, 0, 1e-9);
            Assert.InRange(r0.MomentEquilibriumError, 0, 1e-9);
            Assert.True(r1.MaxUtilization > 0 && !double.IsNaN(r1.MaxUtilization));
            // the stack is a real change (torsion in the lower layer) but not a wild one
            Assert.InRange(r1.MaxUtilization / r0.MaxUtilization, 0.5, 2.0);
            Assert.InRange(r1.MaxDisplacement / r0.MaxDisplacement, 0.5, 2.0);
        }
    }

    [Fact]
    public void AxisOffsets_OneValueForAll_And_BadCounts()
    {
        var (a, bb, mesh) = LFrame(4, 2, 4);
        var one = FrameAnalysis.Compute(mesh, new[] { a, bb }, new[] { bb[0] }, new[] { a[0] }, Flat, new Vec3d(0, 200, 0),
            new FrameAnalysis.Options { E = E, AxisOffsets = new[] { 0.03 } });
        Assert.Equal(0.0, one.Eccentricity); // every axis lifted alike: no lever arm between the laths
        Assert.All(one.AxisOffsets, h => Assert.Equal(0.03, h));
        Assert.InRange(one.MomentEquilibriumError, 0, 1e-10);
        Assert.Throws<ArgumentException>(() => FrameAnalysis.Compute(mesh, new[] { a, bb }, new[] { bb[0] }, new[] { a[0] }, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, AxisOffsets = new[] { 0.0, 0.1, 0.2 } }));
    }

    private static List<double[]> Orthonormal(List<double[]> vs)
    {
        var q = new List<double[]>();
        foreach (var v in vs)
        {
            var w = (double[])v.Clone();
            foreach (var u in q) { double d = 0; for (int i = 0; i < 12; i++) d += u[i] * w[i]; for (int i = 0; i < 12; i++) w[i] -= d * u[i]; }
            double nrm = Math.Sqrt(w.Sum(x => x * x));
            for (int i = 0; i < 12; i++) w[i] /= nrm;
            q.Add(w);
        }
        return q;
    }

    private static double[,] Transpose(double[,] a) { var t = new double[12, 12]; for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) t[j, i] = a[i, j]; return t; }
    private static double[,] Mul(double[,] a, double[,] b)
    {
        var c = new double[12, 12];
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) { double s = 0; for (int k = 0; k < 12; k++) s += a[i, k] * b[k, j]; c[i, j] = s; }
        return c;
    }
}
