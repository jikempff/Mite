using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Linear buckling of lath frames against closed forms: Euler's column
/// (Timoshenko &amp; Gere 1961, Theory of Elastic Stability §2.2: P_cr =
/// π²EI/(4L²) fixed–free), the double eigenvalue of a round bar, the
/// orientation of the weak axis, tension (no buckling), and a fixed-base
/// portal frame whose exact sway load follows from the beam-column stability
/// functions (Przemieniecki 1968; Chen &amp; Lui 1987, Structural Stability,
/// ch. 5: the exact Euler–Bernoulli beam-column stiffness in compression),
/// plus the Sturm sequence count of the subspace solver.
/// </summary>
public class BucklingTests
{
    private readonly ITestOutputHelper _out;
    public BucklingTests(ITestOutputHelper output) { _out = output; }

    const double E = 11e9;

    // A straight lath along x at height 0.01 over a flat mesh, fixed at x = 0,
    // loaded axially at the tip with P (negative = compression).
    static FrameAnalysis.Result Column(int elements, double L, LathProfile profile, double P, int modes = 1, double tol = 1e-10)
    {
        var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        var lath = new Vec3d[elements + 1];
        for (int i = 0; i <= elements; i++) lath[i] = new Vec3d(L * i / elements, 5, 0.01);
        return FrameAnalysis.Compute(mesh, new[] { lath }, null, new[] { new Vec3d(0, 5, 0.01) },
            profile, Vec3d.Zero,
            new FrameAnalysis.Options
            {
                E = E, BucklingModes = modes, BucklingTolerance = tol,
                PointLoads = new List<(Vec3d, Vec3d)> { (new Vec3d(L, 5, 0.01), new Vec3d(P, 0, 0)) },
            });
    }

    [Fact]
    public void EulerCantilever_ConvergesToPiSquaredEIOver4L2()
    {
        double L = 3.0, w = 0.1, t = 0.01, P = 1.0;
        double Imin = w * t * t * t / 12;
        double exact = Math.PI * Math.PI * E * Imin / (4 * L * L);
        double prevErr = double.NaN;
        foreach (int n in new[] { 2, 4, 8, 16 })
        {
            var r = Column(n, L, new LathProfile(w, t, false), -P);
            Assert.True(r.BucklingConverged, $"{n} elements: not converged after {r.BucklingIterations} iterations");
            Assert.Single(r.BucklingFactors);
            double err = r.BucklingFactors[0] * P / exact - 1;
            _out.WriteLine($"{n} elements: λ P = {r.BucklingFactors[0] * P:F6} N vs π²EI/4L² = {exact:F6} N, error {err:E2}, Sturm missed {r.BucklingMissed}, {r.BucklingIterations} iterations");
            Assert.True(err > 0, "the consistent cubic element must bound the Euler load from above");
            Assert.True(err < new Dictionary<int, double> { [2] = 1e-2, [4] = 1e-3, [8] = 1e-4, [16] = 1e-5 }[n], $"{n} elements: error {err:E2}");
            if (!double.IsNaN(prevErr)) Assert.True(prevErr / err > 8, $"convergence ratio {prevErr / err:F1} (expect ~16 for an O(h⁴) element)");
            prevErr = err;
            Assert.Equal(0, r.BucklingMissed); // the Sturm count agrees: nothing below λ₁ was missed
        }
    }

    [Fact]
    public void EulerCantilever_ModeIsOneMinusCosine_InTheWeakDirection()
    {
        // flat strip: the thin direction is the surface normal (z) → the mode moves in z
        double L = 3.0;
        var r = Column(16, L, new LathProfile(0.1, 0.01, false), -1.0);
        var mode = r.BucklingModes[0];
        double maxOff = 0, maxOther = 0;
        for (int i = 0; i < r.Nodes.Length; i++)
        {
            double x = r.Nodes[i].X;
            double shape = 1 - Math.Cos(Math.PI * x / (2 * L)); // normalised to 1 at the tip
            maxOff = Math.Max(maxOff, Math.Abs(Math.Abs(mode[i].Z) - shape));
            maxOther = Math.Max(maxOther, Math.Abs(mode[i].X) + Math.Abs(mode[i].Y));
        }
        _out.WriteLine($"mode shape error {maxOff:E2}, in-plane components {maxOther:E2}");
        Assert.True(maxOff < 2e-3, $"mode shape error {maxOff:E2}");
        Assert.True(maxOther < 1e-9, $"in-plane components {maxOther:E2}");

        // upright strip: the thin direction lies in the surface → the mode moves in y, same load
        var ru = Column(16, L, new LathProfile(0.1, 0.01, true), -1.0);
        Assert.Equal(r.BucklingFactors[0], ru.BucklingFactors[0], 6);
        double maxY = ru.BucklingModes[0].Max(m => Math.Abs(m.Y)), maxZ = ru.BucklingModes[0].Max(m => Math.Abs(m.Z));
        Assert.Equal(1.0, maxY, 12);
        Assert.True(maxZ < 1e-9, $"upright mode has z components {maxZ:E2}");
    }

    [Fact]
    public void RoundBar_DoubleEigenvalue_BothFound()
    {
        double L = 2.0, d = 0.02, P = 1.0;
        double I = Math.PI * Math.Pow(d, 4) / 64;
        double exact = Math.PI * Math.PI * E * I / (4 * L * L);
        var r = Column(8, L, LathProfile.Round(d), -P, modes: 2);
        Assert.True(r.BucklingConverged);
        Assert.Equal(2, r.BucklingFactors.Length);
        _out.WriteLine($"round bar: λ₁ P = {r.BucklingFactors[0]:F6}, λ₂ P = {r.BucklingFactors[1]:F6}, exact {exact:F6}; Sturm missed {r.BucklingMissed}");
        Assert.True(Math.Abs(r.BucklingFactors[0] - r.BucklingFactors[1]) < 1e-8 * exact, "the two bending planes of a round bar buckle at the same load");
        Assert.True(Math.Abs(r.BucklingFactors[0] - exact) / exact < 1e-4);
        Assert.Equal(0, r.BucklingMissed);
    }

    [Fact]
    public void Tension_NoBuckling()
    {
        var r = Column(8, 3.0, new LathProfile(0.1, 0.01, false), +1.0);
        Assert.True(r.BucklingConverged);
        Assert.Empty(r.BucklingFactors);
        Assert.Empty(r.BucklingModes);
        // and the linear results are untouched by the request
        var r0 = Column(8, 3.0, new LathProfile(0.1, 0.01, false), +1.0, modes: 0);
        Assert.Equal(r0.MaxDisplacement, r.MaxDisplacement, 15);
        Assert.Empty(r0.BucklingFactors);
        Assert.Equal(-1, r0.BucklingMissed);
    }

    [Fact]
    public void FixedBasePortal_MatchesStabilityFunctions()
    {
        // Portal in the horizontal plane: columns along x from (0, y) to (h, y),
        // y = 0 and y = l; beam along y at x = h. Upright strips bend their weak
        // axis in the plane. Bases fixed, P on each column top along −x.
        double h = 3.0, l = 3.0, w = 0.1, t = 0.01, P = 1.0;
        double I = w * t * t * t / 12;
        double exact = PortalSwayLoad(E * I, h, l) ; // first root of the exact 3-DOF determinant
        _out.WriteLine($"exact first critical load (stability functions): P h²/EI = {exact * h * h / (E * I):F6}");
        double prev = double.NaN;
        foreach (int n in new[] { 2, 4, 8 })
        {
            var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
            Vec3d[] Line(Vec3d a, Vec3d b) { var pts = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) pts[i] = a + (double)i / n * (b - a); return pts; }
            double z = 0.01;
            var laths = new List<Vec3d[]>
            {
                Line(new Vec3d(0, 2, z), new Vec3d(h, 2, z)),
                Line(new Vec3d(0, 2 + l, z), new Vec3d(h, 2 + l, z)),
                Line(new Vec3d(h, 2, z), new Vec3d(h, 2 + l, z)),
            };
            var r = FrameAnalysis.Compute(mesh, laths, null, new[] { new Vec3d(0, 2, z), new Vec3d(0, 2 + l, z) },
                new LathProfile(w, t, true), Vec3d.Zero,
                new FrameAnalysis.Options
                {
                    E = E, BucklingModes = 1, BucklingTolerance = 1e-10,
                    PointLoads = new List<(Vec3d, Vec3d)> { (new Vec3d(h, 2, z), new Vec3d(-P, 0, 0)), (new Vec3d(h, 2 + l, z), new Vec3d(-P, 0, 0)) },
                });
            Assert.True(r.BucklingConverged);
            Assert.Single(r.BucklingFactors);
            double err = r.BucklingFactors[0] * P / exact - 1;
            _out.WriteLine($"{n} elements per member: λ P = {r.BucklingFactors[0]:F6} vs exact {exact:F6}, error {err:E2}; Sturm missed {r.BucklingMissed}; {r.BucklingIterations} iterations");
            Assert.True(Math.Abs(err) < new Dictionary<int, double> { [2] = 2e-2, [4] = 2e-3, [8] = 2e-4 }[n], $"{n} elements: error {err:E2}");
            if (!double.IsNaN(prev)) Assert.True(prev / Math.Abs(err) > 6, $"convergence ratio {prev / Math.Abs(err):F1}");
            prev = Math.Abs(err);
            Assert.Equal(0, r.BucklingMissed);
            // sway mode: both column tops move the same way in y, the beam translates
            var m = r.BucklingModes[0];
            int top1 = Array.FindIndex(r.Nodes, p => (p - new Vec3d(h, 2, z)).Length < 1e-9);
            int top2 = Array.FindIndex(r.Nodes, p => (p - new Vec3d(h, 2 + l, z)).Length < 1e-9);
            Assert.True(top1 >= 0 && top2 >= 0);
            Assert.True(Math.Abs(m[top1].Y - m[top2].Y) < 1e-6 && Math.Abs(m[top1].Y) > 0.9, $"sway: {m[top1].Y:F4} / {m[top2].Y:F4}");
        }
    }

    /// <summary>
    /// Exact first critical load of a symmetric portal with fixed bases:
    /// columns (EI, h) carrying P, beam (EI, l) without axial force, rigid
    /// joints, axially rigid members. Degrees of freedom: sway Δ, joint
    /// rotations θ1, θ2. Column top stiffness from the exact beam-column
    /// matrix in compression (φ = h√(P/EI), Φ = 2 − 2cos φ − φ sin φ):
    /// k_vv = 12φ₁EI/h³, k_vθ = −6φ₂EI/h², k_θθ = 4φ₃EI/h with
    /// φ₁ = φ³ sin φ/(12Φ), φ₂ = φ²(1 − cos φ)/(6Φ), φ₃ = φ(sin φ − φ cos φ)/(4Φ).
    /// The beam adds 4EI/l on each rotation and 2EI/l between them. The first
    /// zero of the 3 × 3 determinant is bracketed by scanning and bisected.
    /// </summary>
    static double PortalSwayLoad(double EI, double h, double l)
    {
        double Det(double P)
        {
            double phi = h * Math.Sqrt(P / EI);
            double Phi = 2 - 2 * Math.Cos(phi) - phi * Math.Sin(phi);
            double f1 = phi * phi * phi * Math.Sin(phi) / (12 * Phi);
            double f2 = phi * phi * (1 - Math.Cos(phi)) / (6 * Phi);
            double f3 = phi * (Math.Sin(phi) - phi * Math.Cos(phi)) / (4 * Phi);
            double kvv = 2 * 12 * f1 * EI / (h * h * h);     // two columns
            double kvt = -6 * f2 * EI / (h * h);             // per column
            double ktt = 4 * f3 * EI / h + 4 * EI / l;       // column + beam
            double k12 = 2 * EI / l;
            // | kvv  kvt  kvt |
            // | kvt  ktt  k12 |
            // | kvt  k12  ktt |
            return kvv * (ktt * ktt - k12 * k12) - kvt * (kvt * ktt - k12 * kvt) + kvt * (kvt * k12 - ktt * kvt);
        }
        // scan from a small load (det > 0: stable) to the first sign change
        double pEuler = Math.PI * Math.PI * EI / (h * h);
        double step = pEuler / 2000, a = step, fa = Det(a);
        Assert.True(fa > 0);
        double b = a;
        for (int i = 0; i < 20000; i++)
        {
            b = a + step;
            double fb = Det(b);
            if (fb <= 0 || double.IsNaN(fb)) break;
            a = b; fa = fb;
        }
        for (int i = 0; i < 200; i++)
        {
            double m = 0.5 * (a + b), fm = Det(m);
            if (fm > 0) a = m; else b = m;
        }
        return 0.5 * (a + b);
    }

    [Fact]
    public void SubspaceEigen_DiagonalProblem_PositiveEigenvaluesAndSturmCount()
    {
        // K = diag(1..6), B = diag(1, −1, 0.5, 2, −0.25, 1) → λ = 1, −2, 6, 2, −20, 6
        var K = new SparseSymmetricSolver.Builder(6);
        var B = new SparseSymmetricSolver.Builder(6);
        double[] b = { 1, -1, 0.5, 2, -0.25, 1 };
        for (int i = 0; i < 6; i++) { K.Add(i, i, i + 1); B.Add(i, i, b[i]); }
        var r = SubspaceEigen.Solve(K, B, 3, positiveOnly: true, tolerance: 1e-12)!;
        Assert.True(r.Converged);
        var pos = r.Eigenvalues.Where(v => v > 0).OrderBy(v => v).ToArray();
        Assert.True(pos.Length >= 3);
        Assert.Equal(1.0, pos[0], 10);
        Assert.Equal(2.0, pos[1], 10);
        Assert.Equal(6.0, pos[2], 10);
        var neg = r.Eigenvalues.Where(v => v < 0).OrderByDescending(v => v).ToArray();
        Assert.Equal(-2.0, neg[0], 10);
        // eigenvectors are K-orthonormal and satisfy K φ = λ B φ
        for (int k = 0; k < r.Eigenvalues.Length; k++)
        {
            var v = r.Eigenvectors[k];
            var Kv = K.Multiply(v); var Bv = B.Multiply(v);
            double res = 0, norm = 0;
            for (int i = 0; i < 6; i++) { res = Math.Max(res, Math.Abs(Kv[i] - r.Eigenvalues[k] * Bv[i])); norm += v[i] * Kv[i]; }
            Assert.True(res < 1e-9, $"residual {res:E2}");
            Assert.Equal(1.0, norm, 9);
        }
        Assert.Equal(1, SubspaceEigen.CountBelow(K, B, 1.5));
        Assert.Equal(2, SubspaceEigen.CountBelow(K, B, 2.5));
        Assert.Equal(4, SubspaceEigen.CountBelow(K, B, 6.5));
        Assert.Equal(0, SubspaceEigen.CountBelow(K, B, 0.5));
    }

    [Fact]
    public void GeometricStiffness_IsSymmetric_AndMatchesTheClosedForm()
    {
        double N = -3.0, L = 2.0, ipa = 0.7;
        var k = FrameAnalysis.GeometricStiffness(N, L, ipa);
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) Assert.Equal(k[i, j], k[j, i], 15);
        double f = N / L;
        Assert.Equal(f * 6 / 5, k[1, 1], 15); Assert.Equal(f * L / 10, k[1, 5], 15); Assert.Equal(-f * L * L / 30, k[5, 11], 15);
        Assert.Equal(f * 2 * L * L / 15, k[4, 4], 15); Assert.Equal(-f * L / 10, k[2, 4], 15); Assert.Equal(f * L / 10, k[4, 8], 15);
        Assert.Equal(f * ipa, k[3, 3], 15); Assert.Equal(f, k[0, 0], 15); Assert.Equal(-f, k[0, 6], 15);
        // rigid-body translations and rotations about the beam axis cost nothing
        var u = new double[12]; u[1] = 1; u[7] = 1;
        for (int i = 0; i < 12; i++) { double s = 0; for (int j = 0; j < 12; j++) s += k[i, j] * u[j]; Assert.Equal(0.0, s, 12); }
        // a small rigid rotation θ about z (v = θx, θz = θ) stores N ∫(v')² dx = N L θ²:
        // the work of the axial force through the end's second-order shortening (what stiffens a string)
        var rz = new double[12]; rz[5] = 1; rz[11] = 1; rz[7] = L;
        double energy = 0; for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) energy += rz[i] * k[i, j] * rz[j];
        Assert.Equal(N * L, energy, 10);
    }

    [Fact]
    public void ArchGrid_ThreeModes_HingesLowerTheCriticalLoad()
    {
        // two circular arches (R = 5, 20°…160°) joined by five straight cross
        // laths, bases fixed, gravity: compression in the arches → positive
        // factors; scissor hinges at the joints remove constraints, so the
        // hinged grid cannot buckle later than the rigid one
        double R = 5.0; int n = 24;
        Vec3d Arc(double a, double y) => new Vec3d(R * Math.Cos(a), y, R * Math.Sin(a));
        double a0 = 20 * Math.PI / 180, a1 = 160 * Math.PI / 180;
        var ys = new[] { 0.0, 2.0 };
        var laths = new List<Vec3d[]>();
        foreach (double y in ys) { var l = new Vec3d[n + 1]; for (int i = 0; i <= n; i++) l[i] = Arc(a0 + (a1 - a0) * i / n, y); laths.Add(l); }
        var joints = new List<Vec3d>();
        foreach (int k in new[] { 4, 8, 12, 16, 20 })
        {
            double a = a0 + (a1 - a0) * k / n;
            laths.Add(new[] { Arc(a, 0), Arc(a, 1), Arc(a, 2) });
            joints.Add(Arc(a, 0)); joints.Add(Arc(a, 2));
        }
        // the surface the net lies on: the cylindrical strip through the arches
        var verts = new List<Vec3d>(); var faces = new List<int[]>();
        int m = 32;
        for (int j = 0; j < 2; j++) for (int i = 0; i <= m; i++) verts.Add(Arc(a0 - 0.1 + (a1 - a0 + 0.2) * i / m, j == 0 ? -1 : 3));
        for (int i = 0; i < m; i++) faces.Add(new[] { i, i + 1, m + 2 + i, m + 1 + i });
        var mesh = new MeshData(verts.ToArray(), faces.ToArray()).ToTriangulated();
        var supports = new[] { Arc(a0, 0), Arc(a1, 0), Arc(a0, 2), Arc(a1, 2) };
        var profile = new LathProfile(0.08, 0.02, true);
        FrameAnalysis.Result Run(double jointK) => FrameAnalysis.Compute(mesh, laths, joints, supports, profile, new Vec3d(0, 0, -1000),
            new FrameAnalysis.Options { E = E, BucklingModes = 3, JointRotationalStiffness = jointK });
        var rigid = Run(double.PositiveInfinity);
        var hinged = Run(0.0);
        foreach (var (r, name) in new[] { (rigid, "rigid"), (hinged, "hinged") })
        {
            _out.WriteLine($"{name}: factors {string.Join(", ", r.BucklingFactors.Select(f => f.ToString("F4")))}; Sturm missed {r.BucklingMissed}; {r.BucklingIterations} iterations; axial min {r.AxialForce.Min():F1} N");
            Assert.True(r.BucklingConverged, $"{name} not converged");
            Assert.Equal(3, r.BucklingFactors.Length);
            Assert.True(r.BucklingFactors[0] > 0 && r.BucklingFactors[0] <= r.BucklingFactors[1] && r.BucklingFactors[1] <= r.BucklingFactors[2]);
            Assert.Equal(0, r.BucklingMissed);
            Assert.True(r.AxialForce.Min() < 0, "the arches must be in compression");
            Assert.Equal(3, r.BucklingModes.Length);
            Assert.All(r.BucklingModes, mode => Assert.Equal(1.0, mode.Max(v => v.Length), 12));
            // supported nodes do not move in any mode
            foreach (int sn in r.SupportNodes) Assert.All(r.BucklingModes, mode => Assert.True(mode[sn].Length < 1e-12));
        }
        Assert.True(hinged.BucklingFactors[0] <= rigid.BucklingFactors[0] * (1 + 1e-6),
            $"hinged {hinged.BucklingFactors[0]:F4} should not exceed rigid {rigid.BucklingFactors[0]:F4}");
    }
}
