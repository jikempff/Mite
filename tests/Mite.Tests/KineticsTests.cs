using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;

namespace Mite.Tests;

/// <summary>
/// Kinetic scissor nets (roadmap E) against exact mechanisms:
///  - the doubly ruled hyperboloid grid of Schikore, Schling, Oberbichler &amp;
///    Bauer 2020 (AAG 2020, "Kinetics and Design of Semi-Compliant Grid
///    Mechanisms", rigid-body transformation of doubly ruled grids): n straight
///    rods per family tangent to a circle of radius ρ, pinned where they
///    cross; tilting them by β gives a hyperboloid with waist ρ cos β whose
///    joints sit at fixed rod positions s_m = ρ tan(π m / n) — so joint spacing
///    is constant and the grid is a one-parameter mechanism
///    (<see cref="ScissorNet.HyperboloidMechanism"/>);
///  - the planar rhombic lattice (lazy tongs): equal rods pinned at the
///    crossings shear in their plane, nodes at (i + j cos γ, j sin γ, 0).
/// The solver (Wan, Crolla &amp; Schling 2025 energies, Levenberg–Marquardt)
/// must reproduce every intermediate state to solver tolerance.
/// </summary>
public class KineticsTests
{
    private const int N = 12;       // rods per family
    private const int Levels = 2;   // joints per rod each side of the waist (5 per rod)
    private const double Rho = 1.0;

    private static double Deg(double rad) => rad * 180 / Math.PI;

    /// <summary>Largest difference between the pairwise node distances of two configurations (rigid-motion invariant).</summary>
    private static double ShapeError(Vec3d[] a, Vec3d[] b)
    {
        double e = 0;
        for (int i = 0; i < a.Length; i++)
            for (int j = i + 1; j < a.Length; j++)
                e = Math.Max(e, Math.Abs((a[i] - a[j]).Length - (b[i] - b[j]).Length));
        return e;
    }

    [Fact]
    public void HyperboloidMechanism_IsAnExactAsymptoticNetWithConstantJointSpacing()
    {
        foreach (double betaDeg in new[] { 20.0, 45.0, 70.0 })
        {
            var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, betaDeg * Math.PI / 180);
            double cb = Math.Cos(betaDeg * Math.PI / 180);
            // waist joints (m = 0) at radius ρ cos β, all joints on the hyperboloid r² = a² + z² cot²β... checked via the rulings:
            for (int k = 0; k < N; k++)
            {
                var waist = nodes[k * (2 * Levels + 1) + Levels];
                Assert.InRange(Math.Sqrt(waist.X * waist.X + waist.Y * waist.Y), Rho * cb - 1e-12, Rho * cb + 1e-12);
                Assert.InRange(Math.Abs(waist.Z), 0, 1e-12);
            }
            // every rod is straight and its joint spacing is ρ (tan(π(m+1)/n) − tan(πm/n)) — independent of β
            foreach (var lath in laths)
            {
                var d0 = (nodes[lath[1]] - nodes[lath[0]]).Normalized();
                for (int i = 1; i + 1 < lath.Length; i++)
                    Assert.InRange(Vec3d.Cross(d0, (nodes[lath[i + 1]] - nodes[lath[i]]).Normalized()).Length, 0, 1e-12);
                for (int i = 0; i + 1 < lath.Length; i++)
                {
                    int m = i - Levels;
                    double expected = Rho * (Math.Tan(Math.PI * (m + 1) / N) - Math.Tan(Math.PI * m / N));
                    Assert.InRange((nodes[lath[i + 1]] - nodes[lath[i]]).Length, expected - 1e-12, expected + 1e-12);
                }
            }
            // asymptotic: every segment ⟂ the normal at both ends
            var net = new ScissorNet(nodes, laths, N, normals);
            foreach (var lath in laths)
                for (int i = 0; i + 1 < lath.Length; i++)
                {
                    var e = (nodes[lath[i + 1]] - nodes[lath[i]]).Normalized();
                    Assert.InRange(Math.Abs(Vec3d.Dot(e, net.Normals[lath[i]])), 0, 1e-12);
                    Assert.InRange(Math.Abs(Vec3d.Dot(e, net.Normals[lath[i + 1]])), 0, 1e-12);
                }
            // the constructor's own normals (from the crossing rods) agree with the analytic ones up to sign
            var auto = new ScissorNet(nodes, laths, N);
            for (int v = 0; v < nodes.Length; v++)
                Assert.InRange(Math.Abs(Vec3d.Dot(auto.Normals[v], normals[v])), 1 - 1e-9, 1 + 1e-9);
        }
    }

    [Fact]
    public void HyperboloidTwist_DrivenByATopRingCable_FollowsTheClosedFormThroughTheWholeMotion()
    {
        double beta0 = 30 * Math.PI / 180, beta1 = 65 * Math.PI / 180;
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0);
        var net = new ScissorNet(nodes, laths, N, normals);
        int rows = 2 * Levels + 1;
        int top0 = 0 * rows + 2 * Levels, topOpp = (N / 2) * rows + 2 * Levels;   // two opposite top joints
        int waist0 = 0 * rows + Levels;                                            // one waist joint held (translation)
        double TopDiameter(double beta)
        {
            var (nn, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta);
            return (nn[top0] - nn[topOpp]).Length;
        }
        int steps = 8;
        var opt = new ScissorNet.Options
        {
            Fixed = new[] { waist0 },
            Cables = new[] { (top0, topOpp) },
            CableLengthsAt = t => new[] { TopDiameter(beta0 + t * (beta1 - beta0)) },
            Steps = steps,
            Tolerance = 1e-11,
        };
        var res = net.Solve(opt);
        Assert.Equal(steps + 1, res.States.Count);

        double worstShape = 0, worstDrift = 0, worstAsym = 0, worstCable = 0;
        for (int k = 0; k <= steps; k++)
        {
            var st = res.States[k];
            double beta = beta0 + (beta1 - beta0) * k / steps;
            var (truth, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta);
            Assert.True(st.Converged, $"state {k} did not converge: residual {st.Residual:E2} after {st.Iterations} iterations");
            worstShape = Math.Max(worstShape, ShapeError(st.Nodes, truth));
            worstDrift = Math.Max(worstDrift, st.LengthDrift);
            worstAsym = Math.Max(worstAsym, st.AsymptoticDeviation);
            worstCable = Math.Max(worstCable, st.CableMiss);
            // the held waist joint stays; the waist shrinks to ρ cos β (opposite waist joints are 2ρ cos β apart — rigid-invariant)
            Assert.Equal(nodes[waist0], st.Nodes[waist0]);
            for (int i = 0; i < N / 2; i++)
            {
                double d = (st.Nodes[i * rows + Levels] - st.Nodes[(i + N / 2) * rows + Levels]).Length;
                Assert.InRange(d, 2 * Rho * Math.Cos(beta) - 1e-8, 2 * Rho * Math.Cos(beta) + 1e-8);
            }
        }
        Assert.InRange(worstShape, 0, 1e-7);
        Assert.InRange(worstDrift, 0, 1e-9);
        Assert.InRange(worstAsym, 0, 1e-7);
        Assert.InRange(worstCable, 0, 1e-9);
        // crossing angle at the waist: the rulings cross at 2β there (tan(β) slope each side of the tangent)
        var angles = net.CrossingAngles(res.Last);
        double expectedWaist = Deg(2 * beta1) > 90 ? 180 - Deg(2 * beta1) : Deg(2 * beta1);
        for (int i = 0; i < N; i++) Assert.InRange(angles[i * rows + Levels], expectedWaist - 1e-5, expectedWaist + 1e-5);
    }

    [Fact]
    public void HyperboloidTwist_DrivenByTargets_AndBuiltThroughNetTopology_MatchesTheClosedForm()
    {
        // Build the rods as polylines, let NetIntersections / NetTopology find the joints (the Grasshopper route)
        double beta0 = 35 * Math.PI / 180, beta1 = 55 * Math.PI / 180;
        var (nodes, laths, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0);
        var familyA = laths.Take(N).Select(l => l.Select(v => nodes[v]).ToArray()).ToList();
        var familyB = laths.Skip(N).Select(l => l.Select(v => nodes[v]).ToArray()).ToList();
        var crossings = NetIntersections.FindAll(familyA, familyB, 1e-9);
        var topo = NetTopology.Build(familyA, familyB, crossings);
        Assert.Equal(N * (2 * Levels + 1), topo.Nodes.Length);
        var net = ScissorNet.FromTopology(topo, 2 * N, N, maxSegment: 0.0);
        Assert.Equal(N * (2 * Levels + 1), net.Nodes.Length);
        Assert.Equal(2 * N, net.Laths.Count);
        Assert.All(net.Laths, l => Assert.Equal(2 * Levels + 1, l.Length));

        // drive every top joint to its closed-form position; hold nothing else
        var (truth1, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta1);
        int rows = 2 * Levels + 1;
        var topTruthIdx = Enumerable.Range(0, N).Select(k => k * rows + 2 * Levels).ToArray();
        var driven = topTruthIdx.Select(i => net.NearestNode(nodes[i])).ToArray();
        Assert.Equal(N, driven.Distinct().Count());
        int steps = 6;
        var opt = new ScissorNet.Options
        {
            Driven = driven,
            TargetsAt = t =>
            {
                var (nn, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0 + t * (beta1 - beta0));
                return topTruthIdx.Select(i => nn[i]).ToArray();
            },
            Steps = steps,
            Tolerance = 1e-11,
        };
        var res = net.Solve(opt);
        for (int k = 0; k <= steps; k++)
        {
            var st = res.States[k];
            var (truth, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0 + (beta1 - beta0) * k / steps);
            Assert.True(st.Converged, $"state {k}: residual {st.Residual:E2}");
            // node-by-node (the driven top ring fixes the rigid motion): map through the topology node order
            double worst = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                int j = net.NearestNode(nodes[i]);
                worst = Math.Max(worst, (st.Nodes[j] - truth[i]).Length);
            }
            Assert.InRange(worst, 0, 1e-7);
            Assert.InRange(st.LengthDrift, 0, 1e-9);
            Assert.InRange(st.AsymptoticDeviation, 0, 1e-7);
            Assert.InRange(st.TargetMiss, 0, 1e-8);
        }
    }

    [Fact]
    public void PlanarRhombicLattice_ShearsAsALazyTongs()
    {
        int n = 5; // (n+1)² nodes, rods along i and along j, unit spacing
        Vec3d Node(int i, int j, double gamma) => new Vec3d(i + j * Math.Cos(gamma), j * Math.Sin(gamma), 0);
        double g0 = Math.PI / 2, g1 = Math.PI / 3;
        var nodes = new Vec3d[(n + 1) * (n + 1)];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) nodes[i * (n + 1) + j] = Node(i, j, g0);
        var laths = new List<int[]>();
        for (int j = 0; j <= n; j++) laths.Add(Enumerable.Range(0, n + 1).Select(i => i * (n + 1) + j).ToArray()); // rods along i (family A)
        for (int i = 0; i <= n; i++) laths.Add(Enumerable.Range(0, n + 1).Select(j => i * (n + 1) + j).ToArray()); // rods along j (family B)
        var net = new ScissorNet(nodes, laths, n + 1);
        Assert.All(net.Normals, nm => Assert.InRange(Math.Abs(nm.Z), 1 - 1e-12, 1 + 1e-12));

        int corner00 = 0, cornerN0 = n * (n + 1), corner0N = n;
        int steps = 5;
        var opt = new ScissorNet.Options
        {
            Fixed = new[] { corner00, cornerN0 },
            Driven = new[] { corner0N },
            TargetsAt = t => new[] { Node(0, n, g0 + t * (g1 - g0)) },
            Steps = steps,
            Tolerance = 1e-11,
        };
        var res = net.Solve(opt);
        for (int k = 0; k <= steps; k++)
        {
            var st = res.States[k];
            double gamma = g0 + (g1 - g0) * k / steps;
            Assert.True(st.Converged, $"state {k}: residual {st.Residual:E2}");
            double worst = 0;
            for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) worst = Math.Max(worst, (st.Nodes[i * (n + 1) + j] - Node(i, j, gamma)).Length);
            Assert.InRange(worst, 0, 1e-7);
            Assert.InRange(st.LengthDrift, 0, 1e-9);
            var angles = net.CrossingAngles(st);
            for (int v = 0; v < angles.Length; v++) Assert.InRange(angles[v], Deg(gamma) - 1e-5, Deg(gamma) + 1e-5);
        }
    }

    [Fact]
    public void PlanarRhombicLattice_WithSlidingGroundRow_ShearsExactly()
    {
        // same lattice, bottom row free to slide along the ground line y = 0 (Wan et al.'s ground nodes), one corner held
        int n = 4;
        Vec3d Node(int i, int j, double gamma) => new Vec3d(i + j * Math.Cos(gamma), j * Math.Sin(gamma), 0);
        double g0 = Math.PI / 2, g1 = 2 * Math.PI / 5;
        var nodes = new Vec3d[(n + 1) * (n + 1)];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) nodes[i * (n + 1) + j] = Node(i, j, g0);
        var laths = new List<int[]>();
        for (int j = 0; j <= n; j++) laths.Add(Enumerable.Range(0, n + 1).Select(i => i * (n + 1) + j).ToArray());
        for (int i = 0; i <= n; i++) laths.Add(Enumerable.Range(0, n + 1).Select(j => i * (n + 1) + j).ToArray());
        var net = new ScissorNet(nodes, laths, n + 1);
        var ground = Enumerable.Range(0, n + 1).Select(i => i * (n + 1)).ToArray();   // j = 0 row
        var res = net.Solve(new ScissorNet.Options
        {
            Fixed = new[] { 0 },
            Sliding = ground,
            SlideNormal = new Vec3d(0, 1, 0),
            Driven = new[] { n },
            TargetsAt = t => new[] { Node(0, n, g0 + t * (g1 - g0)) },
            Steps = 4, Tolerance = 1e-11,
        });
        foreach (var st in res.States)
        {
            double gamma = g0 + (g1 - g0) * st.Fold;
            Assert.True(st.Converged, $"fold {st.Fold}: residual {st.Residual:E2}");
            for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) Assert.InRange((st.Nodes[i * (n + 1) + j] - Node(i, j, gamma)).Length, 0, 1e-7);
            Assert.InRange(st.SlideMiss, 0, 1e-9);
        }
    }

    [Fact]
    public void SubdividedRods_StayStraightAndInextensible()
    {
        // joints only vs. rods split into 3 pieces between joints: same motion, laths keep their length
        double beta0 = 30 * Math.PI / 180, beta1 = 50 * Math.PI / 180;
        var (nodes, laths, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0);
        var familyA = laths.Take(N).Select(l => l.Select(v => nodes[v]).ToArray()).ToList();
        var familyB = laths.Skip(N).Select(l => l.Select(v => nodes[v]).ToArray()).ToList();
        var topo = NetTopology.Build(familyA, familyB, NetIntersections.FindAll(familyA, familyB, 1e-9));
        double shortest = Rho * Math.Tan(Math.PI / N);
        var net = ScissorNet.FromTopology(topo, 2 * N, N, maxSegment: shortest / 3 + 1e-9);
        Assert.True(net.Nodes.Length > N * (2 * Levels + 1));
        int rows = 2 * Levels + 1;
        var topIdx = Enumerable.Range(0, N).Select(k => k * rows + 2 * Levels).ToArray();
        var driven = topIdx.Select(i => net.NearestNode(nodes[i])).ToArray();
        var res = net.Solve(new ScissorNet.Options
        {
            Driven = driven,
            TargetsAt = t => { var (nn, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0 + t * (beta1 - beta0)); return topIdx.Select(i => nn[i]).ToArray(); },
            Steps = 4, Tolerance = 1e-11,
        });
        var last = res.Last;
        Assert.True(last.Converged, $"residual {last.Residual:E2}");
        var (truth, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta1);
        // every original joint lands on the closed form, and every lath is straight with its rest length
        for (int i = 0; i < nodes.Length; i++) Assert.InRange((last.Nodes[net.NearestNode(nodes[i])] - truth[i]).Length, 0, 1e-6);
        foreach (var poly in net.LathPolylines(last))
        {
            double len = 0; for (int i = 1; i < poly.Length; i++) len += (poly[i] - poly[i - 1]).Length;
            double chord = (poly[poly.Length - 1] - poly[0]).Length;
            Assert.InRange(len - chord, -1e-12, 1e-6);
            Assert.InRange(len, 2 * Rho * Math.Tan(2 * Math.PI / N) - 1e-6, 2 * Rho * Math.Tan(2 * Math.PI / N) + 1e-6);
        }
        Assert.InRange(last.LengthDrift, 0, 1e-9);
    }
}
