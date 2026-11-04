using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Kinetic Supports / Kinetic Drivers carry a <see cref="KineticSetup"/>: points
/// that snap to the nodes of whichever net they are wired into. A setup must
/// resolve to exactly the indices Net Kinetics derives from its own inputs, so
/// a motion driven through the setup wire is the same motion.
/// </summary>
public class KineticSetupTests
{
    private const int N = 3; // 4 × 4 joints, spacing 1, square
    private static ScissorNet Lattice()
    {
        var nodes = new Vec3d[(N + 1) * (N + 1)];
        for (int i = 0; i <= N; i++) for (int j = 0; j <= N; j++) nodes[i * (N + 1) + j] = new Vec3d(i, j, 0);
        var laths = new List<int[]>();
        for (int j = 0; j <= N; j++) laths.Add(Enumerable.Range(0, N + 1).Select(i => i * (N + 1) + j).ToArray());
        for (int i = 0; i <= N; i++) laths.Add(Enumerable.Range(0, N + 1).Select(j => i * (N + 1) + j).ToArray());
        return new ScissorNet(nodes, laths, N + 1);
    }
    private static int Id(int i, int j) => i * (N + 1) + j;

    [Fact]
    public void Problems_NameWhatIsMissing()
    {
        Assert.Empty(new KineticSetup(fixedPoints: new[] { Vec3d.Zero }).Problems());
        Assert.Contains(new KineticSetup(move: new[] { Vec3d.Zero }).Problems(), p => p.Contains("no To target"));
        Assert.Contains(new KineticSetup(cables: new[] { (Vec3d.Zero, new Vec3d(1, 0, 0)) }).Problems(), p => p.Contains("no Lengths"));
        Assert.Contains(new KineticSetup(cables: new[] { (Vec3d.Zero, new Vec3d(1, 0, 0)) }, lengths: new[] { -1.0 }).Problems(), p => p.Contains("not positive"));
        Assert.Contains(new KineticSetup(cables: new[] { (Vec3d.Zero, Vec3d.Zero) }, lengths: new[] { 1.0 }).Problems(), p => p.Contains("both ends"));
        Assert.Contains(new KineticSetup(move: new[] { Vec3d.Zero }, to: new[] { Vec3d.Zero, Vec3d.Zero }).Problems(), p => p.Contains("ignored"));
        // a zero slide normal counts as not set
        Assert.Null(new KineticSetup(slideNormal: Vec3d.Zero).SlideNormal);
    }

    [Fact]
    public void Merge_KeepsMoveToAndCableLengthPairs()
    {
        // a: two Move points sharing one target (the last repeats); b: one Move point with its own target
        var a = new KineticSetup(fixedPoints: new[] { new Vec3d(0, 0, 0) }, move: new[] { new Vec3d(1, 0, 0), new Vec3d(2, 0, 0) }, to: new[] { new Vec3d(9, 9, 9) },
            cables: new[] { (new Vec3d(0, 0, 0), new Vec3d(1, 1, 0)), (new Vec3d(0, 1, 0), new Vec3d(1, 2, 0)) }, lengths: new[] { 0.5 }, slideNormal: new Vec3d(0, 0, 2));
        var b = new KineticSetup(slide: new[] { new Vec3d(3, 3, 0) }, move: new[] { new Vec3d(3, 0, 0) }, to: new[] { new Vec3d(7, 7, 7) },
            cables: new[] { (new Vec3d(2, 2, 0), new Vec3d(3, 2, 0)) }, lengths: new[] { 0.8 }, slideNormal: new Vec3d(1, 0, 0));
        var m = KineticSetup.Merge(a, b);
        Assert.Equal(3, m.Move.Count);
        Assert.Equal(new[] { new Vec3d(9, 9, 9), new Vec3d(9, 9, 9), new Vec3d(7, 7, 7) }, m.To.ToArray());
        Assert.Equal(new[] { 0.5, 0.5, 0.8 }, m.Lengths.ToArray());
        Assert.Equal(new Vec3d(0, 0, 1), m.SlideNormal);          // a's, normalised
        Assert.Single(m.Fixed); Assert.Single(m.Slide);
        Assert.Same(a, KineticSetup.Merge(a, null));
        Assert.Same(b, KineticSetup.Merge(null, b));
    }

    [Fact]
    public void Resolve_SnapsToNodes_WithNetKineticsRules()
    {
        var net = Lattice();
        var s = new KineticSetup(
            fixedPoints: new[] { new Vec3d(0.1, -0.1, 0), new Vec3d(0, 0.05, 0) },            // both snap to node (0,0): kept once
            slide: new[] { new Vec3d(0, 0, 0), new Vec3d(3, 0, 0.2) },                        // (0,0) is held, so only (3,0) slides
            move: new[] { new Vec3d(3, 3, 0), new Vec3d(2.9, 3.1, 0), new Vec3d(0, 0, 0) },  // (3,3) twice → once; the held corner is not moved
            to: new[] { new Vec3d(3, 3, 1), new Vec3d(5, 5, 5), new Vec3d(8, 8, 8) },
            cables: new[] { (new Vec3d(0, 3, 0), new Vec3d(1, 2, 0)), (new Vec3d(2, 2, 0), new Vec3d(2.1, 1.9, 0)) },
            lengths: new[] { 1.0 });
        var r = s.Resolve(net);
        Assert.Equal(new[] { Id(0, 0) }, r.Fixed);
        Assert.Equal(new[] { Id(3, 0) }, r.Sliding);
        Assert.Equal(new[] { Id(3, 3) }, r.Driven);
        Assert.Equal(new[] { new Vec3d(3, 3, 1) }, r.TargetEnd);
        Assert.Single(r.Cables);
        Assert.Equal((Id(0, 3), Id(1, 2)), r.Cables[0]);
        Assert.Equal(Math.Sqrt(2), r.CableRest[0], 12);
        Assert.Equal(1.0, r.CableEnd[0]);
        Assert.Equal(1, r.DroppedCables);
        Assert.InRange(r.LargestSnap, 0.2 - 1e-12, 0.2 + 1e-12);
    }

    [Fact]
    public void SameMotion_ThroughTheSetupWire_AsThroughDirectIndices()
    {
        // pull the far corner of the held lattice up by 1: the motion must be bit-identical
        var setup = new KineticSetup(fixedPoints: new[] { new Vec3d(0, 0, 0) }, move: new[] { new Vec3d(3, 3, 0) }, to: new[] { new Vec3d(3, 3, 1) });
        ScissorNet.Result Run(IReadOnlyList<int> fixedIdx, IReadOnlyList<int> driven, IReadOnlyList<Vec3d> targets)
        {
            var net = Lattice();
            var start = driven.Select(i => net.Nodes[i]).ToArray();
            return net.Solve(new ScissorNet.Options
            {
                Fixed = fixedIdx, Driven = driven, Steps = 4,
                TargetsAt = t => start.Select((p, k) => p + t * (targets[k] - p)).ToArray(),
            });
        }
        var r = setup.Resolve(Lattice());
        var viaSetup = Run(r.Fixed, r.Driven, r.TargetEnd);
        var direct = Run(new[] { Id(0, 0) }, new[] { Id(3, 3) }, new[] { new Vec3d(3, 3, 1) });
        Assert.Equal(direct.States.Count, viaSetup.States.Count);
        for (int k = 0; k < direct.States.Count; k++)
            for (int v = 0; v < direct.States[k].Nodes.Length; v++)
                Assert.Equal(direct.States[k].Nodes[v], viaSetup.States[k].Nodes[v]);
        Assert.InRange(viaSetup.Last.TargetMiss, 0, 1e-6);
    }

    [Fact]
    public void Supports_FromTheSetup_GiveTheSameMechanismModes()
    {
        var net = Lattice();
        var r = new KineticSetup(fixedPoints: new[] { new Vec3d(0.02, 0, 0) }).Resolve(net);
        var viaSetup = MechanismModes.Compute(net, new ScissorNet.Options { Fixed = r.Fixed, Sliding = r.Sliding }, new MechanismModes.Options { Count = 3 });
        var direct = MechanismModes.Compute(net, new ScissorNet.Options { Fixed = new[] { 0 } }, new MechanismModes.Options { Count = 3 });
        Assert.Equal(direct.Mechanisms, viaSetup.Mechanisms);
        Assert.Equal(2, viaSetup.Mechanisms);   // square lattice of straight rods: shear and hypar twist
        for (int k = 0; k < 3; k++) Assert.Equal(direct.Modes[k].Stiffness, viaSetup.Modes[k].Stiffness, 15);
    }
}
