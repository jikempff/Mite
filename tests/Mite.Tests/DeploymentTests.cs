using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;

namespace Mite.Tests;

/// <summary>
/// Deployment of scissor nets from their flat (assembled) state — roadmap E,
/// José's priority 3 ("the deployment driven from the flat grid by pulling
/// the singular node"). Exact ground truth:
///  - the doubly ruled hyperboloid grid (Schikore et al. 2020) opened from its
///    fully collapsed bundle (β = 0, every A rod on top of its B twin — the
///    kinetic umbrella closed) by lifting the top ring: every state must be
///    the closed form (<see cref="ScissorNet.HyperboloidMechanism"/>);
///  - the exact mechanism is the softest mode of the Gauss–Newton matrix with
///    the rigid motions deflated (Pellegrino 1993: the null space of the
///    compatibility matrix), to round-off, and well separated from the next.
/// Measured on the 3-fold Enneper web (not an exact mechanism: the press keeps
/// the joint spacing to 1e-3):
///  - the flat hexagon is a bifurcation (pitchfork): its softest non-rigid
///    mode is the deployment (|correlation| with the design heights > 0.99),
///    a Gauss–Newton solver held flat never leaves the plane, and opening
///    every scissor to the right angle of the minimal surface brings the
///    design back within a tenth of the joint spacing (pairwise distances);
///  - pulling the singular node does not deploy the grid (the mode does not
///    move it relative to the lath ends): the pull is resisted and what gives
///    is a strained bump, not the saddle.
/// </summary>
public class DeploymentTests
{
    private const int N = 12, Levels = 2;
    private const double Rho = 1.0;

    private static double ShapeError(Vec3d[] a, Vec3d[] b)
    {
        double e = 0;
        for (int i = 0; i < a.Length; i++)
            for (int j = i + 1; j < a.Length; j++)
                e = Math.Max(e, Math.Abs((a[i] - a[j]).Length - (b[i] - b[j]).Length));
        return e;
    }

    private static double RmsShapeError(Vec3d[] a, Vec3d[] b)
    {
        double e = 0; long n = 0;
        for (int i = 0; i < a.Length; i++)
            for (int j = i + 1; j < a.Length; j++) { double d = (a[i] - a[j]).Length - (b[i] - b[j]).Length; e += d * d; n++; }
        return Math.Sqrt(e / n);
    }

    private static double Correlation(double[] a, double[] b)
    {
        double num = 0, d1 = 0, d2 = 0;
        for (int k = 0; k < a.Length; k++) { num += a[k] * b[k]; d1 += a[k] * a[k]; d2 += b[k] * b[k]; }
        return num / Math.Sqrt(d1 * d2);
    }

    private static (int[] top, int[] bottom) RingEnds(int[][] laths)
    {
        int rows = 2 * Levels + 1;
        // A rod k: nodes k*rows + 0..rows-1 along +s (z = s sin β grows); B rod j: its node 0 is the joint with m = +Levels (top)
        var top = Enumerable.Range(0, N).Select(k => k * rows + 2 * Levels).Concat(Enumerable.Range(0, N).Select(j => laths[N + j][0])).Distinct().ToArray();
        var bottom = Enumerable.Range(0, N).Select(k => k * rows).Concat(Enumerable.Range(0, N).Select(j => laths[N + j][rows - 1])).Distinct().ToArray();
        return (top, bottom);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    public void HyperboloidBundle_OpensFromTheCollapsedState_ByLiftingTheTopRing_Exactly(double beta0Deg)
    {
        // The kinetic umbrella closed: at β = 0 every rod lies in the plane z = 0 and A rod k coincides with
        // B rod k (the two joints of each rod pair sit on one point). Lifting the top ring's sliding planes by
        // 2 s_max sin β while the bottom ring stays on the ground must open the exact hyperboloid at every β —
        // through the bifurcation at β = 0, where the planes' first move decides which rod of each pair goes up.
        double beta0 = beta0Deg * Math.PI / 180, beta1 = 60 * Math.PI / 180;
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0);
        var net = new ScissorNet(nodes, laths, N, normals);
        var (top, bottom) = RingEnds(laths);
        Assert.Equal(N, top.Length); Assert.Equal(N, bottom.Length);   // every top joint ends one A rod and one B rod
        double smax = Rho * Math.Tan(Math.PI * Levels / N);
        var slide = bottom.Concat(top).ToArray();
        var topSet = new HashSet<int>(top);
        int steps = 8;
        var opt = new ScissorNet.Options
        {
            Sliding = slide,
            SlideOffsetsAt = t =>
            {
                double beta = beta0 + t * (beta1 - beta0);
                double dz = 2 * smax * (Math.Sin(beta) - Math.Sin(beta0));
                return slide.Select(i => topSet.Contains(i) ? dz : 0.0).ToArray();
            },
            Steps = steps, Tolerance = 1e-11, MaxIterations = 200,
        };
        var res = net.Solve(opt);
        Assert.Equal(steps + 1, res.States.Count);
        double worstShape = 0, worstDrift = 0, worstAsym = 0, worstSlide = 0;
        for (int k = 0; k <= steps; k++)
        {
            var st = res.States[k];
            double beta = beta0 + (beta1 - beta0) * k / steps;
            var (truth, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta);
            Assert.True(st.Converged, $"state {k}: residual {st.Residual:E2} after {st.Iterations} iterations");
            worstShape = Math.Max(worstShape, ShapeError(st.Nodes, truth));
            worstDrift = Math.Max(worstDrift, st.LengthDrift);
            worstAsym = Math.Max(worstAsym, st.AsymptoticDeviation);
            worstSlide = Math.Max(worstSlide, st.SlideMiss);
            // waist radius ρ cos β, read off the first waist joint
            var w = st.Nodes[Levels];
            Assert.InRange(Math.Sqrt(w.X * w.X + w.Y * w.Y), Rho * Math.Cos(beta) - 1e-9, Rho * Math.Cos(beta) + 1e-9);
        }
        Assert.InRange(worstShape, 0, 1e-9);
        Assert.InRange(worstDrift, 0, 1e-11);
        Assert.InRange(worstAsym, 0, 1e-9);
        Assert.InRange(worstSlide, 0, 1e-9);
        // the open umbrella: rods cross at 2β = 120° at the waist (folded to 60° by CrossingAngles)
        var ang = net.CrossingAngles(res.Last);
        for (int i = 0; i < N; i++) Assert.InRange(ang[i * (2 * Levels + 1) + Levels], 60 - 1e-6, 60 + 1e-6);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(10.0)]
    public void HyperboloidMechanism_IsTheSoftestMode_OfTheGaussNewtonMatrix(double betaDeg)
    {
        double beta = betaDeg * Math.PI / 180, eps = 1e-6;
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta);
        var net = new ScissorNet(nodes, laths, N, normals);
        int waist0 = Levels;
        var opt = new ScissorNet.Options { Fixed = new[] { waist0 } };
        // d/dβ of the closed form, with the held waist joint's own motion taken out (a translation)
        var (np, _, nnp) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta + eps);
        var (nm, _, nnm) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta - eps);
        var dPos = nodes.Select((p, i) => (np[i] - nm[i]) / (2 * eps) - (np[waist0] - nm[waist0]) / (2 * eps)).ToArray();
        var dNrm = nodes.Select((p, i) => (nnp[i] - nnm[i]) / (2 * eps)).ToArray();
        double norm2 = dPos.Sum(q => q.LengthSquared) + dNrm.Sum(q => q.LengthSquared);
        // the exact mechanism costs nothing at first order
        Assert.InRange(Math.Abs(net.Stiffness(dPos, dNrm, opt)) / norm2, 0, 1e-10);
        var modes = net.SoftestModes(opt, 3);
        Assert.Equal(3, modes.Count);
        Assert.InRange(Math.Abs(modes[0].stiffness), 0, 1e-9);        // one exact mechanism …
        Assert.InRange(modes[1].stiffness, 0.05, double.MaxValue);    // … and nothing else soft
        Assert.InRange(modes[2].stiffness, modes[1].stiffness, double.MaxValue);
        double num = 0, a = 0, b = 0;
        for (int i = 0; i < nodes.Length; i++) { num += Vec3d.Dot(modes[0].positions[i], dPos[i]); a += modes[0].positions[i].LengthSquared; b += dPos[i].LengthSquared; }
        Assert.InRange(Math.Abs(num) / Math.Sqrt(a * b), 0.9999, 1.0000001);
    }

    [Fact]
    public void PlanarLattice_OnAOneSidedGround_LiftsOffButNeverSinks()
    {
        // a rhombic lattice standing on the ground line y = 0 with one corner held: pulling the far top corner
        // up turns the lattice about the held corner and the bottom row lifts off the one-sided ground (no cost,
        // target met); pushing it down by the same amount is stopped by the ground (the row never sinks, the
        // target is missed); with the ground pulled down by its weight the shear of the lazy tongs is exact
        int n = 4;
        Vec3d Node(int i, int j, double gamma) => new Vec3d(i + j * Math.Cos(gamma), j * Math.Sin(gamma), 0);
        double g0 = Math.PI / 2, g1 = Math.PI / 3;
        var nodes = new Vec3d[(n + 1) * (n + 1)];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) nodes[i * (n + 1) + j] = Node(i, j, g0);
        var laths = new List<int[]>();
        for (int j = 0; j <= n; j++) laths.Add(Enumerable.Range(0, n + 1).Select(i => i * (n + 1) + j).ToArray());
        for (int i = 0; i <= n; i++) laths.Add(Enumerable.Range(0, n + 1).Select(j => i * (n + 1) + j).ToArray());
        var net = new ScissorNet(nodes, laths, n + 1);
        var ground = Enumerable.Range(1, n).Select(i => i * (n + 1)).ToArray();   // j = 0 row without the held corner
        int farTop = n * (n + 1) + n, farBottom = n * (n + 1);
        foreach (double dy in new[] { 0.5, -0.5 })
        {
            var opt = new ScissorNet.Options
            {
                Fixed = new[] { 0 }, Ground = ground, SlideNormal = new Vec3d(0, 1, 0),
                Driven = new[] { farTop }, TargetsAt = t => new[] { nodes[farTop] + new Vec3d(0, dy * t, 0) },
                Steps = 2, Tolerance = 1e-11,
            };
            var last = net.Solve(opt).Last;
            Assert.True(last.Converged);
            Assert.InRange(last.GroundMiss, 0, 0.02 * Math.Abs(dy));   // a penalty contact, as stiff as a hinge
            if (dy > 0)
            {
                Assert.InRange(last.LengthDrift, 0, 1e-9);
                Assert.InRange(last.TargetMiss, 0, 1e-8);
                Assert.True(last.Nodes[farBottom].Y > 0.3, "the far bottom corner rises with the corner above it");
                Assert.True(last.GroundContacts < n, $"contacts {last.GroundContacts}");
            }
            else
            {
                Assert.True(last.TargetMiss > 0.3, $"the ground should stop the push: miss {last.TargetMiss:0.000}");
                Assert.InRange(last.Nodes[farBottom].Y, -0.02 * Math.Abs(dy), 1e-6);
                Assert.InRange(last.LengthDrift, 0, 0.01);   // an impossible drive: the joints give a little (the component warns)
            }
        }
        // the weight keeps the row down and the shear of the lazy tongs is the exact lattice
        var shear = new ScissorNet.Options
        {
            Fixed = new[] { 0 }, Ground = ground, GroundWeight = 1.0, SlideNormal = new Vec3d(0, 1, 0),
            Driven = new[] { n }, TargetsAt = t => new[] { Node(0, n, g0 + t * (g1 - g0)) },
            Steps = 4, Tolerance = 1e-11,
        };
        var res = net.Solve(shear);
        for (int k = 0; k < res.States.Count; k++)
        {
            var st = res.States[k];
            double gamma = g0 + (g1 - g0) * k / 4;
            Assert.True(st.Converged);
            Assert.InRange(st.GroundMiss, 0, 1e-9);
            Assert.Equal(n, st.GroundContacts);
            for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++)
                Assert.InRange((st.Nodes[i * (n + 1) + j] - Node(i, j, gamma)).Length, 0, 1e-7);
        }
    }

    // ---------------------------------------------------------------- the 3-fold Enneper web, pressed flat

    private sealed class FlatWeb
    {
        public ScissorNet Design = null!;
        public ScissorNet Flat = null!;
        public ScissorNet.State FlatState = null!;
        public int Centre;
        public double[] DesignHeights = Array.Empty<double>();
        public int[] LowEnds = Array.Empty<int>();
    }

    private static readonly Lazy<FlatWeb> Web = new Lazy<FlatWeb>(() =>
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var w = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options());
        var topo = NetTopology.Build(w.A, w.B, NetIntersections.FindAll(w.A, w.B, 0));
        var proj = new MeshProjection(mesh);
        var net = ScissorNet.FromTopology(topo, w.A.Count + w.B.Count, w.A.Count, 0, q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
        int centre = net.NearestNode(w.Seed);
        var opt = new ScissorNet.Options { Fairness = 0.3, Steps = 8, MaxIterations = 60 };
        net.PressFlat(opt, centre, new Vec3d(0, 0, 1));
        var res = net.Solve(opt);
        var pressed = res.Last;
        // the kit as cut: exactly flat (the press leaves 3e-5 of height), every normal exactly the table normal
        var flat = new ScissorNet.State
        {
            Fold = pressed.Fold, Nodes = pressed.Nodes.Select(p => new Vec3d(p.X, p.Y, 0)).ToArray(),
            Normals = pressed.Nodes.Select(_ => new Vec3d(0, 0, 1)).ToArray(), LengthDrift = pressed.LengthDrift,
        };
        double zc = net.Nodes[centre].Z;
        return new FlatWeb
        {
            Design = net, Flat = net.AtState(flat), FlatState = flat, Centre = centre,
            DesignHeights = net.Nodes.Select(p => p.Z - zc).ToArray(),
            LowEnds = net.LathEnds().Where(i => net.Nodes[i].Z - zc < -1e-6).ToArray(),
        };
    });

    [Fact]
    public void AtState_KeepsTheRestLengthsAndIsFlat()
    {
        var web = Web.Value;
        Assert.False(web.Design.IsFlat());
        Assert.True(web.Flat.IsFlat(1.0));
        Assert.Equal(web.Design.Laths.Count, web.Flat.Laths.Count);
        for (int l = 0; l < web.Design.Laths.Count; l++)
        {
            Assert.Equal(web.Design.Laths[l], web.Flat.Laths[l]);
            Assert.Equal(web.Design.RestLengths[l], web.Flat.RestLengths[l]);
        }
        Assert.Equal(web.Design.Scale, web.Flat.Scale);
        Assert.Equal(web.Design.SourceLaths, web.Flat.SourceLaths);
        Assert.InRange(web.FlatState.Nodes.Max(p => Math.Abs(p.Z)), 0, 1e-3 * web.Design.Scale);
        // the press kept the joint spacing to 1e-3 — the Enneper web is a mechanism only to that accuracy
        Assert.InRange(web.FlatState.LengthDrift, 0, 3e-3);
    }

    [Fact]
    public void FlatEnneperWeb_SoftestMode_IsTheDeployment()
    {
        var web = Web.Value;
        var modes = web.Flat.SoftestModes(new ScissorNet.Options { Fairness = 0.3, Fixed = new[] { web.Centre } }, 3);
        Assert.Equal(3, modes.Count);
        var m0 = modes[0];
        double outOfPlane = Math.Sqrt(m0.positions.Sum(p => p.Z * p.Z) / m0.positions.Sum(p => p.LengthSquared));
        Assert.InRange(outOfPlane, 0.999, 1.0);
        double corr = Correlation(web.DesignHeights, m0.positions.Select(p => p.Z).ToArray());
        Assert.InRange(Math.Abs(corr), 0.99, 1.0);
        // well separated from the in-plane (lazy-tongs) modes, which cost bending
        Assert.True(modes[1].stiffness > 50 * m0.stiffness, $"mode gap {m0.stiffness:E2} vs {modes[1].stiffness:E2}");
        // the mode is a near-mechanism of the pressed web: its stiffness is of the order of the press's own 1e-3 drift
        Assert.InRange(m0.stiffness, 0, 1e-3);
    }

    [Fact]
    public void FlatEnneperWeb_HeldFlat_DoesNotLeaveThePlane_WhenItsScissorsAreOpened()
    {
        // the bifurcation: with the centre held on the table and no kick, a Gauss–Newton solver driven in the
        // plane (open the scissors) stays in the plane — the coupling lives in the second derivative it drops
        var web = Web.Value;
        var opt = new ScissorNet.Options { Fairness = 0.3, Steps = 4, MaxIterations = 60, Fixed = new[] { web.Centre } };
        var start = web.Flat.DeployFromFlat(opt, web.Centre, new Vec3d(0, 0, 1), Math.PI / 2, 0.0, out var mode);
        Assert.Null(mode);
        Assert.Same(web.Flat, start);
        opt.Ground = Array.Empty<int>();   // no table either: nothing breaks the symmetry
        var res = start.Solve(opt);
        var last = res.Last;
        Assert.InRange(last.Nodes.Max(p => Math.Abs(p.Z)), 0, 1e-6 * web.Design.Scale);
        Assert.True(last.ScissorMiss > 30, $"the scissors should not open in the plane: miss {last.ScissorMiss:0.#}°");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(-0.5)]
    public void FlatEnneperWeb_DeploysIntoTheDesign_ByOpeningEveryScissorToARightAngle(double kick)
    {
        var web = Web.Value;
        var opt = new ScissorNet.Options { Fairness = 0.3, Steps = 10, MaxIterations = 80 };
        var start = web.Flat.DeployFromFlat(opt, web.Centre, new Vec3d(0, 0, 1), Math.PI / 2, kick, out var mode);
        Assert.NotNull(mode);
        Assert.Equal(web.Design.LathEnds().Length, opt.Ground.Count);
        Assert.True(opt.Scissors.Count > 300);
        var res = start.Solve(opt);
        foreach (var st in res.States)
        {
            Assert.True(st.Converged, $"fold {st.Fold}");
            Assert.InRange(st.LengthDrift, 0, 2e-3);
            Assert.InRange(st.AsymptoticDeviation, 0, 0.3);
            Assert.InRange(st.GroundMiss, 0, 1e-6);
        }
        var last = res.Last;
        double h = web.Design.Scale;
        // the design is back: pairwise distances within a tenth of the joint spacing (rms), half a spacing at worst
        Assert.InRange(RmsShapeError(last.Nodes, web.Design.Nodes), 0, 0.1 * h);
        Assert.InRange(ShapeError(last.Nodes, web.Design.Nodes), 0, 0.5 * h);
        // and it is a saddle standing on the table: heights correlate with the design's (sign = the branch)
        double corr = Correlation(web.DesignHeights, last.Nodes.Select(p => p.Z - last.Nodes[web.Centre].Z).ToArray());
        Assert.InRange(Math.Abs(corr), 0.99, 1.0);
        double rise = last.Nodes.Max(p => p.Z) - last.Nodes.Min(p => p.Z);
        double designRise = web.DesignHeights.Max() - web.DesignHeights.Min();
        Assert.InRange(rise / designRise, 0.9, 1.1);
        Assert.True(last.GroundContacts >= 3 && last.GroundContacts < opt.Ground.Count / 2, $"lath ends on the table: {last.GroundContacts}");
        // the scissors cannot all reach 90° (the design's own crossings run from 60° at the rays up): the miss is the design's
        Assert.InRange(last.ScissorMiss, 0, 10);
    }

    [Fact]
    public void FlatEnneperWeb_PulledAtTheSingularNode_DoesNotDeploy()
    {
        // José's Kangaroo sequence in the kinematic model: the three low lobes held on the table, the singular node
        // pulled up to its design height. The deployment mode does not move the singular node relative to the
        // lath ends, so the pull is resisted: the node rises less than half of what is asked, the shape stays
        // far from the design, and the asymptotic condition starts to give (the laths bend the hard way).
        var web = Web.Value;
        double rise = -web.DesignHeights.Min();   // the centre above the lowest ends
        var opt = new ScissorNet.Options { Fairness = 0.3, Steps = 10, MaxIterations = 80, Sliding = web.LowEnds };
        var start = web.Flat.Lift(opt, web.Centre, new Vec3d(0, 0, 1), rise, 0.5);
        Assert.Contains(web.Centre, opt.Driven);
        var res = start.Solve(opt);
        var last = res.Last;
        double lifted = last.Nodes[web.Centre].Z - start.Nodes[web.Centre].Z;
        Assert.True(lifted < 0.5 * rise, $"the centre rose {lifted:0.000} of {rise:0.000}");
        Assert.True(last.TargetMiss > 0.5 * rise);
        Assert.True(RmsShapeError(last.Nodes, web.Design.Nodes) > 0.08 * web.Design.Scale);
        Assert.True(last.AsymptoticDeviation > 0.5, $"asymptotic deviation {last.AsymptoticDeviation:0.00}°");
        // without anything held and without weight the pull lifts the grid as a rigid body: the table is one-sided
        var free = new ScissorNet.Options { Fairness = 0.3, Steps = 4, MaxIterations = 60 };
        var start2 = web.Flat.Lift(free, web.Centre, new Vec3d(0, 0, 1), rise);
        var rigid = start2.Solve(free).Last;
        Assert.InRange(rigid.TargetMiss, 0, 1e-6);
        Assert.Equal(0, rigid.GroundContacts);
        Assert.InRange(rigid.Nodes.Max(p => p.Z) - rigid.Nodes.Min(p => p.Z), 0, 0.5 * web.Design.Scale);
    }

    [Fact]
    public void FlatEnneperWeb_ReversePress_ReturnsToTheDesign()
    {
        // the press read backwards from the re-based flat kit: every lath end rises to its design height
        var web = Web.Value;
        var ends = web.Design.LathEnds();
        double zc = web.Design.Nodes[web.Centre].Z;
        var height = ends.Select(i => web.Design.Nodes[i].Z - zc).ToArray();
        var opt = new ScissorNet.Options
        {
            Fairness = 0.3, Steps = 8, MaxIterations = 80, Fixed = new[] { web.Centre },
            Sliding = ends, SlideOffsetsAt = t => height.Select(x => t * x).ToArray(),
        };
        var res = web.Flat.Solve(opt);
        var last = res.Last;
        Assert.True(last.Converged);
        Assert.InRange(last.LengthDrift, 0, 3e-3);
        Assert.InRange(last.AsymptoticDeviation, 0, 0.2);
        double h = web.Design.Scale;
        Assert.InRange(RmsShapeError(last.Nodes, web.Design.Nodes), 0, 0.1 * h);
        Assert.InRange(ShapeError(last.Nodes, web.Design.Nodes), 0, 0.5 * h);
        Assert.InRange(Math.Abs(Correlation(web.DesignHeights, last.Nodes.Select(p => p.Z - last.Nodes[web.Centre].Z).ToArray())), 0.99, 1.0);
    }
}
