using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Lateral–torsional buckling of deep thin strips against the closed forms of
/// Timoshenko &amp; Gere 1961 (Theory of Elastic Stability, ch. 6) and the
/// coupled equations of Trahair 1993 (Flexural-Torsional Buckling of
/// Structures): a cantilever strip under a tip load buckles at P_cr = γ₁
/// √(E I G J) / L² with γ₁ = 4.0126 (eq. 6-30; the first zero of J_{−1/4}
/// solved to 13 digits), under a uniform load at (q L)_cr = 12.8538 √(E I G J)
/// / L² (eq. 6-33), and a span under uniform moment whose ends are held
/// against twist and lateral rotation at M_cr = 2π √(E I G J) / L (warping
/// neglected: E I_w = 0 for the thin rectangle). I is the weak-axis second
/// moment, J the Saint-Venant constant of the profile. The flexural–torsional
/// terms of the geometric stiffness are checked against the second-order
/// work ∫ (M_y v″ + M_z w″) φ dx by quadrature, and the Euler results of
/// <see cref="BucklingTests"/> stay bit-identical (no moments there).
/// </summary>
public class LateralTorsionalBucklingTests
{
    private readonly ITestOutputHelper _out;
    public LateralTorsionalBucklingTests(ITestOutputHelper output) { _out = output; }

    const double E = 11e9;
    const double G = E / 2.6; // the analysis default

    /// <summary>√(E I_weak G J) of a profile — the lateral–torsional rigidity of Timoshenko's formulas.</summary>
    static double Rigidity(LathProfile profile)
    {
        var s = profile.SectionProperties();
        return Math.Sqrt(E * Math.Min(s.IA, s.IB) * G * s.J);
    }

    // An upright strip along x over a flat mesh, clamped at x = x0, with a
    // vertical tip load P (N) or a uniform vertical load q (N/m).
    static FrameAnalysis.Result Cantilever(int elements, double L, LathProfile profile, double tipLoad, double q, bool lateral = true, int modes = 1)
    {
        var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        double x0 = 2, y0 = 5, z = 0.01;
        var lath = new Vec3d[elements + 1];
        for (int i = 0; i <= elements; i++) lath[i] = new Vec3d(x0 + L * i / elements, y0, z);
        var loads = new List<(Vec3d, Vec3d)>();
        if (tipLoad != 0) loads.Add((new Vec3d(x0 + L, y0, z), new Vec3d(0, 0, tipLoad)));
        return FrameAnalysis.Compute(mesh, new[] { lath }, null, new[] { new Vec3d(x0, y0, z) },
            profile, new Vec3d(0, 0, q),
            new FrameAnalysis.Options
            {
                E = E, BucklingModes = modes, BucklingTolerance = 1e-10, LateralTorsional = lateral,
                PointLoads = loads.Count > 0 ? loads : null,
            });
    }

    [Fact]
    public void CantileverStrip_TipLoad_BucklesAt4Point0126()
    {
        double L = 3.0, P = 1.0;
        var profile = new LathProfile(0.1, 0.01, true);
        double exact = 4.0125993435793 * Rigidity(profile) / (L * L);
        double prevErr = double.NaN;
        foreach (int n in new[] { 4, 8, 16, 32 })
        {
            var r = Cantilever(n, L, profile, -P, 0);
            Assert.True(r.BucklingConverged, $"{n} elements: not converged after {r.BucklingIterations} iterations");
            Assert.Single(r.BucklingFactors);
            Assert.True(Math.Abs(r.AxialForce.Max()) < 1e-9 && Math.Abs(r.AxialForce.Min()) < 1e-9, "a vertical load on a straight horizontal strip puts no axial force in it");
            double err = Math.Abs(r.BucklingFactors[0] * P / exact - 1);
            _out.WriteLine($"{n} elements: λ P = {r.BucklingFactors[0] * P:F5} N vs 4.0126 √(EIGJ)/L² = {exact:F5} N, error {err:E2}, Sturm missed {r.BucklingMissed}, {r.BucklingIterations} iterations");
            Assert.True(err < new Dictionary<int, double> { [4] = 2e-2, [8] = 5e-3, [16] = 1.5e-3, [32] = 4e-4 }[n], $"{n} elements: error {err:E2}");
            if (!double.IsNaN(prevErr)) Assert.True(prevErr / err > 3.5, $"convergence ratio {prevErr / err:F1} (expect ~4 for the linear twist)");
            prevErr = err;
            Assert.Equal(0, r.BucklingMissed);
            // the mode: sideways (in the surface, local weak axis) with the tip free, nothing vertical
            var mode = r.BucklingModes[0];
            double maxY = mode.Max(m => Math.Abs(m.Y)), maxZ = mode.Max(m => Math.Abs(m.Z));
            Assert.Equal(1.0, maxY, 12);
            Assert.True(maxZ < 1e-6, $"vertical components {maxZ:E2} in the lateral mode");
        }
    }

    [Fact]
    public void CantileverStrip_UniformLoad_BucklesAt12Point854()
    {
        double L = 3.0, q = 1.0;
        var profile = new LathProfile(0.1, 0.01, true);
        double exact = 12.853763321383 * Rigidity(profile) / (L * L); // (q L)_cr
        double prevErr = double.NaN;
        foreach (int n in new[] { 8, 16, 32 })
        {
            var r = Cantilever(n, L, profile, 0, -q);
            Assert.True(r.BucklingConverged);
            Assert.Single(r.BucklingFactors);
            double err = Math.Abs(r.BucklingFactors[0] * q * L / exact - 1);
            _out.WriteLine($"{n} elements: λ q L = {r.BucklingFactors[0] * q * L:F5} N vs 12.854 √(EIGJ)/L² = {exact:F5} N, error {err:E2}, Sturm missed {r.BucklingMissed}");
            Assert.True(err < new Dictionary<int, double> { [8] = 1e-2, [16] = 3e-3, [32] = 8e-4 }[n], $"{n} elements: error {err:E2}");
            if (!double.IsNaN(prevErr)) Assert.True(prevErr / err > 3.0, $"convergence ratio {prevErr / err:F1}");
            prevErr = err;
            Assert.Equal(0, r.BucklingMissed);
        }
    }

    [Fact]
    public void Span_UniformMoment_EndsHeldAgainstTwistAndLateralRotation_BucklesAt2Pi()
    {
        // A strip from −a to L + a with a stiff round cross bar at x = 0 and
        // x = L, each pinned at both ends: the strip's node there cannot
        // translate, twist or turn sideways (the bar bends), but turns freely
        // in the vertical plane (the bar twists on its pins). Equal loads P at
        // the overhang tips put the uniform hogging moment P a on the span.
        // The coupled equations E I u″ = −M φ, G J φ″ = M u″ with u = u′ = φ = 0
        // at both ends give 2 − 2 cos kL − kL sin kL = 0, k = M / √(E I G J):
        // the first root is kL = 2π. The overhangs, laterally clamped at the
        // bars, would buckle at 4.01 √(E I G J) / a² — far above.
        double L = 3.0, a = 0.3, c = 0.1, P = 1.0;
        var strip = new LathProfile(0.1, 0.01, true);
        var bar = LathProfile.Round(0.2);
        double exact = 2 * Math.PI * Rigidity(strip) / L; // M_cr
        double prevErr = double.NaN;
        foreach (int n in new[] { 10, 20, 40 })
        {
            var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
            double x0 = 2, y0 = 5, z = 0.01, h = L / n;
            int m = (int)Math.Round((L + 2 * a) / h);
            var lath = new Vec3d[m + 1];
            for (int i = 0; i <= m; i++) lath[i] = new Vec3d(x0 - a + (L + 2 * a) * i / m, y0, z);
            var left = new[] { new Vec3d(x0, y0 - c, z), new Vec3d(x0, y0, z), new Vec3d(x0, y0 + c, z) };
            var right = new[] { new Vec3d(x0 + L, y0 - c, z), new Vec3d(x0 + L, y0, z), new Vec3d(x0 + L, y0 + c, z) };
            var r = FrameAnalysis.Compute(mesh, new[] { lath, left, right },
                new[] { new Vec3d(x0, y0, z), new Vec3d(x0 + L, y0, z) },
                new[] { left[0], left[2], right[0], right[2] },
                new[] { strip, bar, bar }, Vec3d.Zero,
                new FrameAnalysis.Options
                {
                    E = E, BucklingModes = 1, BucklingTolerance = 1e-10, Support = FrameAnalysis.SupportKind.Pinned,
                    PointLoads = new List<(Vec3d, Vec3d)> { (lath[0], new Vec3d(0, 0, -P)), (lath[m], new Vec3d(0, 0, -P)) },
                });
            Assert.True(r.BucklingConverged);
            Assert.Single(r.BucklingFactors);
            Assert.Equal(2, r.JointCount);
            Assert.Equal(4, r.SupportNodes.Length);
            double err = Math.Abs(r.BucklingFactors[0] * P * a / exact - 1);
            _out.WriteLine($"{n} elements on the span: λ P a = {r.BucklingFactors[0] * P * a:F5} N·m vs 2π √(EIGJ)/L = {exact:F5} N·m, error {err:E2}, Sturm missed {r.BucklingMissed}, {r.BucklingIterations} iterations");
            Assert.True(err < new Dictionary<int, double> { [10] = 2e-2, [20] = 5e-3, [40] = 1.5e-3 }[n], $"{n} elements: error {err:E2}");
            if (!double.IsNaN(prevErr)) Assert.True(prevErr / err > 3.0, $"convergence ratio {prevErr / err:F1}");
            prevErr = err;
            Assert.Equal(0, r.BucklingMissed);
            // the mode lives on the span, sideways; the bar ends and the strip's nodes at the bars stay put
            var mode = r.BucklingModes[0];
            for (int i = 0; i < r.Nodes.Length; i++)
            {
                double x = r.Nodes[i].X - x0;
                if (Math.Abs(x) < 1e-9 || Math.Abs(x - L) < 1e-9) Assert.True(mode[i].Length < 1e-6, $"node at the bar moves {mode[i].Length:E2}");
                Assert.True(Math.Abs(mode[i].Z) < 1e-6, $"vertical component {mode[i].Z:E2}");
            }
            int mid = Array.FindIndex(r.Nodes, p => Math.Abs(p.X - x0 - L / 2) < 1e-9 && Math.Abs(p.Y - y0) < 1e-9);
            Assert.True(mid >= 0 && Math.Abs(mode[mid].Y) > 0.99, "the span's midpoint moves sideways the most");
        }
    }

    [Fact]
    public void MinorAxisBending_AndRoundBars_DoNotBuckleLaterally()
    {
        // A flat strip loaded flatwise bends about its minor axis; a round bar has
        // no minor axis: neither buckles laterally (Trahair 1993 §7: the
        // prebuckling-curvature factor 1 − I_minor/I_major is ≤ 0), and the
        // classical coupling, which would report a factor, is left out for them.
        var flat = Cantilever(16, 3.0, new LathProfile(0.1, 0.01, false), -1.0, 0);
        Assert.True(flat.BucklingConverged);
        Assert.Empty(flat.BucklingFactors);
        var round = Cantilever(16, 3.0, LathProfile.Round(0.03), -1.0, 0);
        Assert.True(round.BucklingConverged);
        Assert.Empty(round.BucklingFactors);
        // the upright strip under the same load has a factor; turned flat it has none
        var up = Cantilever(16, 3.0, new LathProfile(0.1, 0.01, true), -1.0, 0);
        Assert.Single(up.BucklingFactors);
        _out.WriteLine($"upright strip λ = {up.BucklingFactors[0]:F3}; flat strip and round bar: no factor");
    }

    [Fact]
    public void WithoutTheMomentTerms_PureBendingDoesNotBuckle_AndEulerIsUnchanged()
    {
        var profile = new LathProfile(0.1, 0.01, true);
        var r = Cantilever(8, 3.0, profile, -1.0, 0, lateral: false);
        Assert.True(r.BucklingConverged);
        Assert.Empty(r.BucklingFactors); // 1.6.0: nothing in compression, no Euler buckling
        // an axially loaded column carries no moment: both settings agree to the bit
        var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        var lath = new Vec3d[9];
        for (int i = 0; i <= 8; i++) lath[i] = new Vec3d(2 + 3.0 * i / 8, 5, 0.01);
        FrameAnalysis.Result Column(bool lateral) => FrameAnalysis.Compute(mesh, new[] { lath }, null, new[] { lath[0] }, profile, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, BucklingModes = 1, BucklingTolerance = 1e-10, LateralTorsional = lateral,
                PointLoads = new List<(Vec3d, Vec3d)> { (lath[8], new Vec3d(-1, 0, 0)) } });
        var with = Column(true); var without = Column(false);
        Assert.Single(with.BucklingFactors);
        Assert.Equal(without.BucklingFactors[0], with.BucklingFactors[0]);
        double exact = Math.PI * Math.PI * E * profile.SectionProperties().IA / (4 * 9.0);
        Assert.True(Math.Abs(with.BucklingFactors[0] / exact - 1) < 1e-4);
    }

    [Fact]
    public void GeometricStiffness_MomentTerms_MatchTheSecondOrderWork()
    {
        // ½ qᵀ K_g q must equal ∫ [½ N (u′² + v′² + w′²) + ½ N (I_p/A) φ′² + (M_y v″ + M_z w″) φ] dx
        // for the cubic Hermite v (v₁, θz₁, v₂, θz₂), w (w₁, −θy₁, w₂, −θy₂) and the linear φ
        double N = -2.5, L = 1.7, ipa = 0.3, MyA = 1.3, MyB = -0.8, MzA = 0.6, MzB = 2.1;
        var k = FrameAnalysis.GeometricStiffness(N, L, ipa, MyA, MyB, MzA, MzB);
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) Assert.Equal(k[i, j], k[j, i], 14);
        // the axial-only overload is the same matrix with the moments off
        var k0 = FrameAnalysis.GeometricStiffness(N, L, ipa);
        var kz = FrameAnalysis.GeometricStiffness(N, L, ipa, 0, 0, 0, 0);
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) Assert.Equal(k0[i, j], kz[i, j]);
        var rnd = new Random(7);
        for (int trial = 0; trial < 5; trial++)
        {
            var q = new double[12];
            for (int i = 0; i < 12; i++) q[i] = rnd.NextDouble() * 2 - 1;
            double quad = 0;
            // 4-point Gauss on [0, L] (integrand degree ≤ 5)
            double[] gx = { -0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526 };
            double[] gw = { 0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538 };
            for (int g = 0; g < 4; g++)
            {
                double xi = 0.5 * (gx[g] + 1), x = xi * L, wgt = 0.5 * L * gw[g];
                // Hermite derivatives in x
                double h1p = (-6 * xi + 6 * xi * xi) / L, h2p = 1 - 4 * xi + 3 * xi * xi, h3p = (6 * xi - 6 * xi * xi) / L, h4p = -2 * xi + 3 * xi * xi;
                double h1pp = (-6 + 12 * xi) / (L * L), h2pp = (-4 + 6 * xi) / L, h3pp = (6 - 12 * xi) / (L * L), h4pp = (-2 + 6 * xi) / L;
                double vp = h1p * q[1] + h2p * q[5] + h3p * q[7] + h4p * q[11];
                double vpp = h1pp * q[1] + h2pp * q[5] + h3pp * q[7] + h4pp * q[11];
                double wp = h1p * q[2] - h2p * q[4] + h3p * q[8] - h4p * q[10];
                double wpp = h1pp * q[2] - h2pp * q[4] + h3pp * q[8] - h4pp * q[10];
                double phi = (1 - xi) * q[3] + xi * q[9], phip = (q[9] - q[3]) / L;
                double My = MyA * (1 - xi) + MyB * xi, Mz = MzA * (1 - xi) + MzB * xi;
                quad += wgt * (0.5 * N * (vp * vp + wp * wp) + 0.5 * N * ipa * phip * phip + (My * vpp + Mz * wpp) * phi);
            }
            quad += 0.5 * N * (q[6] - q[0]) * (q[6] - q[0]) / L; // the axial block, ½ N ∫ u′² dx with the linear u
            double matrix = 0;
            for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) matrix += 0.5 * q[i] * k[i, j] * q[j];
            Assert.Equal(quad, matrix, 12);
        }
        // the moment terms alone cost nothing on a rigid motion (no curvature): a
        // translation, a twist with a rotation about z, a rotation about y
        var km = FrameAnalysis.GeometricStiffness(0, L, ipa, MyA, MyB, MzA, MzB);
        foreach (var rigid in new[]
        {
            new double[] { 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 },
            new double[] { 0, 0, 0, 0.7, 0, 0.3, 0, 0.3 * L, 0, 0.7, 0, 0.3 },
            new double[] { 0, 0, 0, 0, 0.4, 0, 0, 0, -0.4 * L, 0, 0.4, 0 },
        })
        {
            double e2 = 0;
            for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) e2 += rigid[i] * km[i, j] * rigid[j];
            Assert.Equal(0.0, e2, 12);
        }
        // and the entries of the derivation
        Assert.Equal(-MyA / L, k[1, 3], 14); Assert.Equal(MyB / L, k[1, 9], 14); Assert.Equal(-(5 * MyA + MyB) / 6, k[3, 5], 14);
        Assert.Equal((5 * MzA + MzB) / 6, k[3, 4], 14); Assert.Equal((MzA - MzB) / 6, k[4, 9], 14); Assert.Equal(-(MzA + 5 * MzB) / 6, k[9, 10], 14);
    }
}
