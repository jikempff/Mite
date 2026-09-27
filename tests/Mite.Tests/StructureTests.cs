using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Gridshell Analysis against closed-form beam theory, plus the checks that
/// caught the 1.2.8 joint-chaining bug (431 crossings on a 3-fold Enneper net
/// collapsed 556 lath samples into 62 frame nodes).
/// </summary>
public class StructureTests
{
    private const double E = 11e9;
    private static readonly LathProfile Flat = new LathProfile(0.1, 0.02, false); // I = w t³/12 for vertical bending
    private static double I => 0.1 * Math.Pow(0.02, 3) / 12.0;

    private static Vec3d[] Line(Vec3d a, Vec3d b, int n)
    {
        var p = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) p[i] = a + (double)i / n * (b - a);
        return p;
    }

    [Fact]
    public void Cantilever_TipLoad_MatchesPL3Over3EI()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 4.0, P = 50.0;
        var beam = Line(new Vec3d(1, 5, 0), new Vec3d(1 + L, 5, 0), 8);
        var r = FrameAnalysis.Compute(mesh, new[] { beam }, null, new[] { beam[0] }, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, PointLoads = new[] { (beam[^1], new Vec3d(0, 0, -P)) } });
        double exact = P * L * L * L / (3 * E * I);
        Assert.InRange(r.MaxDisplacement / exact, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(r.EquilibriumError, 0, 1e-9);
        Assert.InRange(r.Reactions[r.SupportNodes[0]].Z, P * (1 - 1e-9), P * (1 + 1e-9));
    }

    [Fact]
    public void ClampedBeam_UniformLoad_MatchesQL4Over384EI()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 6.0, q = 200.0;
        var beam = Line(new Vec3d(2, 5, 0), new Vec3d(2 + L, 5, 0), 40);
        var r = FrameAnalysis.Compute(mesh, new[] { beam }, null, new[] { beam[0], beam[^1] }, Flat, new Vec3d(0, 0, -q),
            new FrameAnalysis.Options { E = E });
        double exact = q * Math.Pow(L, 4) / (384 * E * I);
        // lumped nodal loads: O(h²) at midspan
        Assert.InRange(r.MaxDisplacement / exact, 0.995, 1.005);
        Assert.InRange(r.TotalLoad.Z, -q * L * (1 + 1e-12), -q * L * (1 - 1e-12));
        Assert.InRange(r.EquilibriumError, 0, 1e-9);
    }

    [Fact]
    public void PinnedCross_CentreLoad_EachBeamCarriesHalf()
    {
        // two simply supported beams crossing at mid-span, all ends pinned:
        // by symmetry each carries P/2 at its centre, no torsion
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 8.0, P = 400.0;
        var a = Line(new Vec3d(1, 5, 0), new Vec3d(1 + L, 5, 0), 16);
        var b = Line(new Vec3d(5, 1, 0), new Vec3d(5, 1 + L, 0), 16);
        var r = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { new Vec3d(5, 5, 0) }, new[] { a[0], a[^1], b[0], b[^1] }, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, Support = FrameAnalysis.SupportKind.Pinned, PointLoads = new[] { (new Vec3d(5, 5, 0), new Vec3d(0, 0, -P)) } });
        double exact = 0.5 * P * L * L * L / (48 * E * I);
        Assert.Equal(1, r.JointCount);
        Assert.InRange(r.MaxDisplacement / exact, 1 - 1e-9, 1 + 1e-9);
        Assert.All(r.SupportNodes, n => Assert.InRange(r.Reactions[n].Z, P / 4 * (1 - 1e-9), P / 4 * (1 + 1e-9)));
        Assert.InRange(r.Torsion.Max(), 0, 1e-6 * P * L);
    }

    [Fact]
    public void SemiRigidJoint_AddsExactlyTheSpringRotation()
    {
        // A: clamped both ends along x. B: cantilever along y from the joint at
        // A's midspan, loaded in-plane (along x) at its tip. The moment P·Lb
        // about the normal goes through the joint; a rotational spring k adds
        // a rigid rotation P·Lb/k of B, i.e. a tip displacement P·Lb²/k.
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double La = 6.0, Lb = 2.0, P = 30.0;
        var a = Line(new Vec3d(2, 5, 0), new Vec3d(2 + La, 5, 0), 12);
        var b = Line(new Vec3d(5, 5, 0), new Vec3d(5, 5 + Lb, 0), 8);
        var loads = new[] { (b[^1], new Vec3d(P, 0, 0)) };
        var sup = new[] { a[0], a[^1] };
        var rigid = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { b[0] }, sup, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, PointLoads = loads });
        const double k = 2000.0; // N·m/rad
        var spring = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { b[0] }, sup, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, PointLoads = loads, JointRotationalStiffness = k });
        int tip = Array.FindIndex(rigid.Nodes, p => (p - b[^1]).Length < 1e-9);
        int tip2 = Array.FindIndex(spring.Nodes, p => (p - b[^1]).Length < 1e-9);
        double extra = spring.Displacements[tip2].X - rigid.Displacements[tip].X;
        Assert.InRange(extra / (P * Lb * Lb / k), 1 - 1e-4, 1 + 1e-4);

        // a free scissor hinge leaves B spinning about the bolt: a mechanism
        Assert.Throws<InvalidOperationException>(() => FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { b[0] }, sup, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, PointLoads = loads, JointRotationalStiffness = 0 }));
    }

    [Fact]
    public void SelfWeight_And_AreaLoad_SumToTheirTotals()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        var a = Line(new Vec3d(1, 5, 0), new Vec3d(9, 5, 0), 16);
        var b = Line(new Vec3d(5, 1, 0), new Vec3d(5, 9, 0), 16);
        var r = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { new Vec3d(5, 5, 0) }, new[] { a[0], a[^1], b[0], b[^1] }, Flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, Density = 500, AreaLoad = 1000, LoadedArea = 20 });
        double self = 500 * 0.1 * 0.02 * 9.81 * 16.0;
        Assert.InRange(-r.TotalLoad.Z, (self + 20000) * (1 - 1e-12), (self + 20000) * (1 + 1e-12));
        Assert.InRange(r.EquilibriumError, 0, 1e-9);
    }

    [Fact]
    public void EnneperNet_KeepsEveryCrossingAsItsOwnNode()
    {
        // regression: joints within snap of each other used to chain the whole net into a few nodes
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 40).ToTriangulated();
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15);
        var (mn, mx) = mesh.BoundingBox();
        double size = (mx - mn).Length;
        var oa = new EvenlySpacedNet.Options { Spacing = 0.04 * size, Layout = NetLayout.WebBorder };
        var ob = new EvenlySpacedNet.Options { Spacing = 0.04 * size, Layout = NetLayout.WebBorder };
        var fa = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, oa, field.Family2);
        var fb = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, ob, field.Family1);
        var laths = fa.Concat(fb).Select(l => ShortestPath.Resample(l, 2 * proj.AverageEdgeLength)).ToList();
        var xs = NetIntersections.Find(laths.Take(fa.Count).ToList(), laths.Skip(fa.Count).ToList());
        var ends = laths.SelectMany(l => new[] { l[0], l[^1] }).ToList();
        var r = FrameAnalysis.Compute(mesh, laths, xs.Select(x => x.Point).ToList(), ends, new LathProfile(0.06, 0.012, true), new Vec3d(0, 0, -500));
        int samples = laths.Sum(l => l.Length);
        Assert.True(r.Nodes.Length >= samples, $"{r.Nodes.Length} nodes for {samples} samples and {xs.Count} crossings");
        Assert.True(r.JointCount >= 0.95 * xs.Count, $"{r.JointCount} joints for {xs.Count} crossings");
        Assert.InRange(r.EquilibriumError, 0, 1e-8);
    }
}
