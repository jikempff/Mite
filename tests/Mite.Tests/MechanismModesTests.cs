using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Mechanism Modes: the motions a scissor net allows before any driver is set.
/// Exact cases: the doubly ruled hyperboloid grid moves as a one-degree-of-
/// freedom rigid-body mechanism (its closed form, <see cref="ScissorNet.HyperboloidMechanism"/>,
/// gives the mode), holding a whole ring of it removes that mechanism, and a
/// planar rhombic lattice of straight rods shears in its plane (lazy tongs).
/// </summary>
public class MechanismModesTests
{
    private const int N = 12, Levels = 2;
    private const double Rho = 1.0;
    private readonly ITestOutputHelper _out;
    public MechanismModesTests(ITestOutputHelper output) => _out = output;

    private static double Cosine(Vec3d[] a, Vec3d[] b)
    {
        double num = 0, x = 0, y = 0;
        for (int i = 0; i < a.Length; i++) { num += Vec3d.Dot(a[i], b[i]); x += a[i].LengthSquared; y += b[i].LengthSquared; }
        return Math.Abs(num) / Math.Sqrt(x * y);
    }

    [Fact]
    public void Hyperboloid_HasExactlyOneMechanism_TheClosedFormMotion()
    {
        double beta = 30 * Math.PI / 180, eps = 1e-6;
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta);
        var net = new ScissorNet(nodes, laths, N, normals);
        int waist0 = Levels;
        var r = MechanismModes.Compute(net, new ScissorNet.Options { Fixed = new[] { waist0 } }, new MechanismModes.Options { Count = 4 });
        _out.WriteLine($"mean stiffness {r.MeanStiffness:E3}; modes {string.Join(", ", r.Modes.Select(m => $"{m.Stiffness:E2} ({m.RelativeStiffness:E2}, mech {m.IsMechanism}, out {m.OutOfPlane:F3})"))}; gap {r.Gap:E2}");
        Assert.Equal(4, r.Modes.Count);
        Assert.Equal(1, r.Mechanisms);
        Assert.False(r.MoreMayExist);
        Assert.True(r.Modes[0].IsMechanism);
        Assert.True(r.Gap > 1e6, $"mechanism and stiff modes not separated: gap {r.Gap:E2}");
        // the mechanism is d/dβ of the closed form, with the held joint's translation taken out
        var (np, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta + eps);
        var (nm, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta - eps);
        var dPos = nodes.Select((p, i) => (np[i] - nm[i]) / (2 * eps) - (np[waist0] - nm[waist0]) / (2 * eps)).ToArray();
        Assert.InRange(Cosine(r.Modes[0].Displacements, dPos), 0.9999, 1.0000001);
        // scaled so the largest node displacement is 1
        Assert.InRange(r.Modes[0].Displacements.Max(v => v.Length), 1 - 1e-12, 1 + 1e-12);
    }

    [Fact]
    public void Hyperboloid_HeldAlongAWholeRing_HasNoMechanism()
    {
        // Schling & Schikore 2023: supports decide how far a grid can move. Holding every
        // lath end of the bottom ring pins the rods' feet and the twist motion is gone.
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, 30 * Math.PI / 180);
        var net = new ScissorNet(nodes, laths, N, normals);
        int rows = 2 * Levels + 1;
        var bottom = Enumerable.Range(0, N).Select(k => k * rows).Concat(Enumerable.Range(0, N).Select(j => laths[N + j][rows - 1])).Distinct().ToArray();
        var r = MechanismModes.Compute(net, new ScissorNet.Options { Fixed = bottom }, new MechanismModes.Options { Count = 3 });
        _out.WriteLine($"held ring: modes {string.Join(", ", r.Modes.Select(m => $"{m.RelativeStiffness:E2}"))}");
        Assert.Equal(0, r.Mechanisms);
        // well clear of the mechanism tolerance (1e-6), not near it
        Assert.True(r.Modes[0].RelativeStiffness > 10 * new MechanismModes.Options().MechanismTolerance, $"softest {r.Modes[0].RelativeStiffness:E2}");
    }

    [Theory]
    [InlineData(4, 60.0)]
    [InlineData(2, 60.0)]
    [InlineData(2, 90.0)]   // the square 3 × 3 joint lattice of the Rhino smoke test
    public void PlanarLattice_ShearsInPlane_AndTwistsIntoAHypar(int n, double gammaDeg)
    {
        // a rhombic lattice of straight rods in z = 0, one corner held: two exact mechanisms —
        // the shear of the rhombi in the plane (lazy tongs) and the out-of-plane twist into a
        // hyperbolic paraboloid (a flat grid of straight rods is the flat state of a doubly
        // ruled grid); everything else is stiff
        double g = gammaDeg * Math.PI / 180;
        Vec3d Node(int i, int j, double gamma) => new Vec3d(i + j * Math.Cos(gamma), j * Math.Sin(gamma), 0);
        var nodes = new Vec3d[(n + 1) * (n + 1)];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) nodes[i * (n + 1) + j] = Node(i, j, g);
        var laths = new List<int[]>();
        for (int j = 0; j <= n; j++) laths.Add(Enumerable.Range(0, n + 1).Select(i => i * (n + 1) + j).ToArray());
        for (int i = 0; i <= n; i++) laths.Add(Enumerable.Range(0, n + 1).Select(j => i * (n + 1) + j).ToArray());
        var net = new ScissorNet(nodes, laths, n + 1);
        var r = MechanismModes.Compute(net, new ScissorNet.Options { Fixed = new[] { 0 } }, new MechanismModes.Options { Count = 6 });
        _out.WriteLine($"lattice: {string.Join(", ", r.Modes.Select(m => $"{m.RelativeStiffness:E2} mech {m.IsMechanism} out {m.OutOfPlane:F3}"))}");
        // d/dγ of the shear with node 0 held: (−j sin γ, j cos γ, 0)
        var shear = Enumerable.Range(0, nodes.Length).Select(k => { int j = k % (n + 1); return new Vec3d(-j * Math.Sin(g), j * Math.Cos(g), 0); }).ToArray();
        var mech = r.Modes.Where(m => m.IsMechanism).ToList();
        Assert.Equal(2, r.Mechanisms);
        Assert.True(r.Gap > 1e6, $"gap {r.Gap:E2}");
        // the shear lies in the span of the mechanisms plus the rigid rotations about the held
        // corner (which the modes are kept orthogonal to): exact, in-plane
        var rigid = new[] { new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1) }
            .Select(w => nodes.Select(p => Vec3d.Cross(w, p - nodes[0])).ToArray()).ToList();
        double spanned = SpanFraction(mech.Select(m => m.Displacements).Concat(rigid).ToList(), shear);
        double rigidOnly = SpanFraction(rigid, shear);
        _out.WriteLine($"fraction of the shear in mechanisms + rigid rotations {spanned:F10} (rigid rotations alone {rigidOnly:F4})");
        Assert.InRange(spanned, 1 - 1e-8, 1 + 1e-9);
        Assert.True(rigidOnly < 0.9, "the shear must not be a rigid motion");
        // the hypar twist: z = c · (u v) on the rhombus coordinates is in the span too (out of plane)
        var hypar = Enumerable.Range(0, nodes.Length).Select(k => { int i = k / (n + 1), j = k % (n + 1); return new Vec3d(0, 0, i * j); }).ToArray();
        Assert.InRange(SpanFraction(mech.Select(m => m.Displacements).Concat(rigid).ToList(), hypar), 1 - 1e-8, 1 + 1e-9);
    }

    /// <summary>|projection of v onto span(basis)|² / |v|² (Gram–Schmidt).</summary>
    private static double SpanFraction(List<Vec3d[]> basis, Vec3d[] v)
    {
        var flat = basis.Select(b => b.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray()).ToList();
        var x = v.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();
        var ortho = new List<double[]>();
        foreach (var b in flat)
        {
            var c = (double[])b.Clone();
            foreach (var o in ortho) { double d = c.Zip(o, (p, q) => p * q).Sum(); for (int k = 0; k < c.Length; k++) c[k] -= d * o[k]; }
            double nn = Math.Sqrt(c.Sum(t => t * t)); if (nn < 1e-14) continue;
            for (int k = 0; k < c.Length; k++) c[k] /= nn;
            ortho.Add(c);
        }
        double total = x.Sum(t => t * t), inSpan = 0;
        foreach (var o in ortho) { double d = x.Zip(o, (p, q) => p * q).Sum(); inSpan += d * d; }
        return inSpan / total;
    }
}
