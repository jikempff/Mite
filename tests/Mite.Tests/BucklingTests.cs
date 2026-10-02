using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Linear buckling of Gridshell Analysis (FrameAnalysis.Options.BucklingModes)
/// against closed-form stability theory (Timoshenko &amp; Gere 1961): Euler's
/// cantilever, its higher modes and the strong axis, the sway mode of a
/// portal frame (alignment-chart equation, exact for this frame), and the
/// exact discrete eigenvalues of the consistent geometric stiffness.
/// </summary>
public class BucklingTests
{
    private readonly ITestOutputHelper _out;
    public BucklingTests(ITestOutputHelper output) => _out = output;

    private const double E = 11e9;

    private static Vec3d[] Line(Vec3d a, Vec3d b, int n)
    {
        var p = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) p[i] = a + (double)i / n * (b - a);
        return p;
    }

    /// <summary>The XY quad grid turned into the XZ plane (normal ±Y), for frames standing upright.</summary>
    private static MeshData VerticalPlane(double size)
    {
        var flat = TestMeshes.CreateQuadGrid(4, 4, size / 4);
        var v = flat.Vertices.Select(p => new Vec3d(p.X - size / 2, 0, p.Y - size / 2)).ToArray();
        return new MeshData(v, flat.Faces.Select(f => f.ToArray()).ToArray());
    }

    // λ / (π²EI/4L²) of a cantilever with 8 cubic elements and the consistent
    // geometric stiffness: the exact discrete eigenvalues of the 16-DOF
    // problem (an upper bound of Euler's (2n − 1)², error O(h⁴)).
    private static readonly double[] Cantilever8 = { 1.000002060211, 9.001478254004, 25.030733808184 };

    [Fact]
    public void Cantilever_WeakAxis_HigherModes_StrongAxis_MatchEuler()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 4.0, P = 100.0, w = 0.1, t = 0.025; // strong / weak = (w/t)² = 16
        var flat = new LathProfile(w, t, false);
        double Iweak = w * t * t * t / 12, Istrong = t * w * w * w / 12;
        var beam = Line(new Vec3d(1, 5, 0), new Vec3d(1 + L, 5, 0), 8);
        var r = FrameAnalysis.Compute(mesh, new[] { beam }, null, new[] { beam[0] }, flat, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, BucklingModes = 4, PointLoads = new[] { (beam[^1], new Vec3d(-P, 0, 0)) } });
        var b = r.Buckling!;
        double euler = Math.PI * Math.PI * E * Iweak / (4 * L * L);
        var ratios = b.Factors.Select(f => f * P / euler).ToArray();
        _out.WriteLine("λP / (π²EI/4L²): " + string.Join(", ", ratios.Select(x => x.ToString("F9"))) +
            $"; iterations {b.Iterations}, residual {b.MaxResidual:E1}, Sturm {b.SturmConfirmed}");
        Assert.True(b.Converged);
        Assert.True(b.SturmConfirmed);
        Assert.Equal(4, b.Factors.Length);
        // weak axis: modes 1 and 3 of Euler's cantilever, exact discrete values
        Assert.InRange(ratios[0] / Cantilever8[0], 1 - 1e-8, 1 + 1e-8);
        Assert.InRange(ratios[1] / Cantilever8[1], 1 - 1e-8, 1 + 1e-8);
        // strong axis first mode: 16 × the weak one, in the plane of the mesh
        Assert.InRange(ratios[2] / (16 * Cantilever8[0]), 1 - 1e-8, 1 + 1e-8);
        Assert.InRange(ratios[3] / Cantilever8[2], 1 - 1e-8, 1 + 1e-8);
        // against the continuum: 2e-6 low for 8 elements, from above
        Assert.InRange(ratios[0] - 1, 0, 3e-6);
        // shapes: unit tip deflection along the normal (weak) and in the plane (strong)
        int tip = Array.FindIndex(r.Nodes, p => (p - beam[^1]).Length < 1e-9);
        Assert.InRange(Math.Abs(b.Shapes[0][tip].Z), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Math.Abs(b.Shapes[0][tip].Y), 0, 1e-9);
        Assert.InRange(Math.Abs(b.Shapes[2][tip].Y), 1 - 1e-9, 1 + 1e-9);
        Assert.True(b.MaxResidual < 1e-6, $"residual {b.MaxResidual}");
        Assert.InRange(Istrong / Iweak, 16 - 1e-9, 16 + 1e-9);
    }

    [Fact]
    public void Cantilever_Converges_FromAbove_AsH4()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 4.0, P = 100.0;
        var flat = new LathProfile(0.1, 0.02, false);
        double euler = Math.PI * Math.PI * E * (0.1 * Math.Pow(0.02, 3) / 12) / (4 * L * L);
        var errors = new List<double>();
        foreach (int n in new[] { 1, 2, 4, 8 })
        {
            var beam = Line(new Vec3d(1, 5, 0), new Vec3d(1 + L, 5, 0), n);
            var r = FrameAnalysis.Compute(mesh, new[] { beam }, null, new[] { beam[0] }, flat, Vec3d.Zero,
                new FrameAnalysis.Options { E = E, BucklingModes = 1, PointLoads = new[] { (beam[^1], new Vec3d(-P, 0, 0)) } });
            errors.Add(r.Buckling!.Factors[0] * P / euler - 1);
        }
        _out.WriteLine("relative error for 1, 2, 4, 8 elements: " + string.Join(", ", errors.Select(e => e.ToString("E3"))));
        // 1 element: 0.75 % (the classic 1.2159 of the pinned column halved by symmetry)
        Assert.InRange(errors[0], 0.00752, 0.00753);
        for (int i = 1; i < errors.Count; i++)
        {
            Assert.True(errors[i] > 0 && errors[i] < errors[i - 1]);
            Assert.InRange(errors[i - 1] / errors[i], 14, 17); // O(h⁴)
        }
    }

    [Fact]
    public void PortalFrame_Sway_MatchesAlignmentChartEquation()
    {
        // fixed-base portal, columns h, beam Lb, same section; vertical loads P
        // on both column tops. Sway buckling: x / tan x = −6 / G with
        // G = (I/h) / (I/Lb), P_cr = x² EI / h² (exact for this frame: both
        // columns buckle together, the beam bends in double curvature with
        // equal end rotations and carries no axial force).
        const double h = 4.0, Lb = 4.0, P = 1000.0;
        var mesh = VerticalPlane(12.0);
        var upright = new LathProfile(0.1, 0.02, true); // stands on edge: in-plane bending is the weak one
        double I = 0.1 * Math.Pow(0.02, 3) / 12;
        var a = new Vec3d(-Lb / 2, 0, -2); var b = new Vec3d(Lb / 2, 0, -2);
        var c1 = Line(a, a + new Vec3d(0, 0, h), 16);
        var beam = Line(c1[^1], b + new Vec3d(0, 0, h), 16);
        var c2 = Line(b, b + new Vec3d(0, 0, h), 16);
        var r = FrameAnalysis.Compute(mesh, new[] { c1, beam, c2 }, null, new[] { a, b }, upright, Vec3d.Zero,
            new FrameAnalysis.Options
            {
                E = E, BucklingModes = 2,
                PointLoads = new[] { (c1[^1], new Vec3d(0, 0, -P)), (c2[^1], new Vec3d(0, 0, -P)) },
            });
        var bk = r.Buckling!;
        double G = Lb / h;
        double lo = Math.PI / 2 + 1e-12, hi = Math.PI - 1e-12;
        for (int k = 0; k < 200; k++)
        {
            double mid = 0.5 * (lo + hi);
            if (mid / Math.Tan(mid) + 6 / G > 0) lo = mid; else hi = mid;
        }
        double x = 0.5 * (lo + hi);
        double pcr = x * x * E * I / (h * h);
        double err = bk.Factors[0] * P / pcr - 1;
        _out.WriteLine($"x = {x:F9} (K = {Math.PI / x:F6}), P_cr = {pcr:F3} N, λ·P = {bk.Factors[0] * P:F3} N, error {err:E2}; λ₂/λ₁ = {bk.Factors[1] / bk.Factors[0]:F4}");
        Assert.True(bk.SturmConfirmed);
        Assert.InRange(err, -2e-4, 2e-4);
        // the sway mode moves the beam sideways: both column tops by the same x
        int t1 = Array.FindIndex(r.Nodes, p => (p - c1[^1]).Length < 1e-9);
        int t2 = Array.FindIndex(r.Nodes, p => (p - c2[^1]).Length < 1e-9);
        var s = bk.Shapes[0];
        Assert.InRange(Math.Abs(s[t1].X), 0.99, 1.0 + 1e-9);
        Assert.InRange(s[t1].X - s[t2].X, -1e-3, 1e-3);
        Assert.InRange(Math.Abs(s[t1].Y), 0, 1e-9); // in plane
    }

    [Fact]
    public void LoadFactor_ScalesExactly_WithLoadAndStiffness_OnAGridshell()
    {
        var mesh = TestMeshes.CreateSaddle(16, 2.0);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15);
        var o = new EvenlySpacedNet.Options { Spacing = 0.35, Layout = NetLayout.WebBorder };
        var fa = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, o, field.Family2);
        var fb = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, o, field.Family1);
        var laths = fa.Concat(fb).Select(l => ShortestPath.Resample(l, 2 * proj.AverageEdgeLength)).ToList();
        var xs = NetIntersections.Find(laths.Take(fa.Count).ToList(), laths.Skip(fa.Count).ToList());
        var ends = laths.SelectMany(l => new[] { l[0], l[^1] }).ToList();
        var profile = new LathProfile(0.06, 0.012, true);
        FrameAnalysis.Result Run(double e, double q) => FrameAnalysis.Compute(mesh, laths, xs.Select(x => x.Point).ToList(), ends,
            profile, new Vec3d(0, 0, -q), new FrameAnalysis.Options { E = e, BucklingModes = 3 });
        var r1 = Run(E, 1000);
        var r2 = Run(E, 2000);
        var r3 = Run(2 * E, 1000);
        var b1 = r1.Buckling!;
        _out.WriteLine($"{r1.Nodes.Length} nodes, {r1.JointCount} joints: λ = {string.Join(", ", b1.Factors.Select(f => f.ToString("G6")))}; " +
            $"{b1.Iterations} iterations, residual {b1.MaxResidual:E1}, Sturm {b1.SturmConfirmed}");
        Assert.True(b1.Converged);
        Assert.True(b1.SturmConfirmed);
        Assert.Equal(3, b1.Factors.Length);
        Assert.True(b1.Factors[0] > 0 && b1.Factors[0] <= b1.Factors[1] && b1.Factors[1] <= b1.Factors[2]);
        Assert.True(b1.MaxResidual < 1e-6, $"residual {b1.MaxResidual}");
        for (int m = 0; m < 3; m++)
        {
            Assert.InRange(r2.Buckling!.Factors[m] * 2 / b1.Factors[m], 1 - 1e-7, 1 + 1e-7);
            Assert.InRange(r3.Buckling!.Factors[m] / (2 * b1.Factors[m]), 1 - 1e-7, 1 + 1e-7);
            Assert.InRange(b1.Shapes[m].Max(v => v.Length), 1 - 1e-12, 1 + 1e-12);
        }
    }

    [Fact]
    public void TensionOnly_HasNoBucklingFactor()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        var beam = Line(new Vec3d(1, 5, 0), new Vec3d(5, 5, 0), 8);
        var r = FrameAnalysis.Compute(mesh, new[] { beam }, null, new[] { beam[0] }, new LathProfile(0.1, 0.02, false), Vec3d.Zero,
            new FrameAnalysis.Options { E = E, BucklingModes = 2, PointLoads = new[] { (beam[^1], new Vec3d(100, 0, 0)) } });
        Assert.NotNull(r.Buckling);
        Assert.Empty(r.Buckling!.Factors);
        // and without the option nothing is computed
        var r0 = FrameAnalysis.Compute(mesh, new[] { beam }, null, new[] { beam[0] }, new LathProfile(0.1, 0.02, false), Vec3d.Zero,
            new FrameAnalysis.Options { E = E, PointLoads = new[] { (beam[^1], new Vec3d(-100, 0, 0)) } });
        Assert.Null(r0.Buckling);
    }

    [Fact]
    public void SturmCount_CountsTheEigenvaluesBelowTheShift()
    {
        // 2 × 2 pencil K = diag(2, 6), G = I: eigenvalues 2 and 6
        var k = new SparseSymmetricSolver.Builder(2); k.Add(0, 0, 2); k.Add(1, 1, 6);
        var g = new SparseSymmetricSolver.Builder(2); g.Add(0, 0, 1); g.Add(1, 1, 1);
        Assert.Equal(0, SubspaceIteration.SturmCount(k, g, 1.0));
        Assert.Equal(1, SubspaceIteration.SturmCount(k, g, 3.0));
        Assert.Equal(2, SubspaceIteration.SturmCount(k, g, 7.0));
        // indefinite G: the negative eigenvalue (λ = −4 from G = −½) is not counted
        var g2 = new SparseSymmetricSolver.Builder(2); g2.Add(0, 0, 1); g2.Add(1, 1, -1.5);
        Assert.Equal(1, SubspaceIteration.SturmCount(k, g2, 10.0));
        var f = SparseSymmetricSolver.Factor(k)!;
        var res = SubspaceIteration.SmallestPositive(f, k, g2, 1);
        Assert.Single(res.Values);
        Assert.InRange(res.Values[0], 2 - 1e-12, 2 + 1e-12);
        Assert.True(res.SturmConfirmed);
    }
}
