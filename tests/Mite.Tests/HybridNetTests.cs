using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Per-family profiles: the asymptotic–geodesic hybrid (AGH) of Schling,
/// Wang, Hoyer &amp; Pottmann 2022 / Schling, Wan, Wang &amp; D'Acunto 2023 —
/// asymptotic lamellas upright, geodesic slats flat. Two things follow from
/// it in the core: the threaded joint of Net Joints (the flat slat passes
/// through a slot in the upright lamella, exact box geometry) and one
/// section per lath in Gridshell Analysis (closed-form load sharing of a
/// cross of two unequal beams, self-weight per lath). The last test is the
/// roadmap's bench check: on the catenoid the asymptotic family has kn = 0
/// and the meridian geodesics kg = 0 with kn = 1/cosh² z, and every crossing
/// between them is a threaded joint.
/// </summary>
public class HybridNetTests
{
    private readonly ITestOutputHelper _out;
    public HybridNetTests(ITestOutputHelper output) { _out = output; }

    private static readonly Vec3d O = new Vec3d(3, 4, 0.5);
    private static readonly Vec3d Ta = new Vec3d(1, 0, 0);
    private static readonly Vec3d N = new Vec3d(0, 0, 1);

    private static Vec3d Tb(double degrees) => new Vec3d(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180), 0);

    // ---------------------------------------------------------------- joints

    [Theory]
    [InlineData(90.0, 0.0)]
    [InlineData(90.0, 0.001)]
    [InlineData(60.0, 0.0)]
    [InlineData(60.0, 0.0005)]
    public void ThreadedJoint_UprightA_FlatB_SlotOpensOnTheNearEdge(double angle, double clearance)
    {
        // A upright 100 × 12 mm standing on the surface (normal interval [0, 0.1]),
        // B flat 80 × 12 mm lying on it ([0, 0.012]): B threads through A's
        // bottom edge over its own thickness; B is not cut.
        var pa = new LathProfile(0.1, 0.012, upright: true);
        var pb = new LathProfile(0.08, 0.012, upright: false);
        Vec3d tb = Tb(angle);
        Assert.True(JointGeometry.TryBuildNotches(O, Ta, tb, N, pa, pb, 0.5, clearance, out var na, out var nb, out var kind));
        Assert.Equal(JointKind.SlotInA, kind);
        Assert.True(nb.IsEmpty);
        Assert.False(na.IsEmpty);

        double s = Math.Sin(angle * Math.PI / 180), c = Math.Abs(Math.Cos(angle * Math.PI / 180));
        double halfAlong = 0.5 * (0.012 / s + 0.08 * c / s) + clearance; // the parallelogram B cuts through A
        double halfAcross = 0.04 + clearance;
        double bleed = 0.05 * 0.1 + clearance;                           // opens on A's bottom edge
        double zLo = -bleed, zHi = 0.012 + clearance;                     // mortise side gets only the clearance
        Assert.InRange(na.HalfX, halfAlong - 1e-12, halfAlong + 1e-12);
        Assert.InRange(na.HalfY, halfAcross - 1e-12, halfAcross + 1e-12);
        Assert.InRange(na.HalfZ, 0.5 * (zHi - zLo) - 1e-12, 0.5 * (zHi - zLo) + 1e-12);
        Assert.InRange(Vec3d.Dot(na.Center - O, N), 0.5 * (zHi + zLo) - 1e-12, 0.5 * (zHi + zLo) + 1e-12);
        Assert.InRange((na.Center - O - Vec3d.Dot(na.Center - O, N) * N).Length, 0, 1e-12); // centred on the crossing in the surface
        // the slot is aligned with the flat lath B (it is B's footprint)
        Assert.InRange(Math.Abs(Vec3d.Dot(na.AxisX, tb)), 1 - 1e-12, 1 + 1e-12);
        Assert.InRange(Math.Abs(Vec3d.Dot(na.AxisZ, N)), 1 - 1e-12, 1 + 1e-12);
        // volume = footprint parallelogram × (overlap + bleed), a closed form
        double vol = 8 * halfAlong * halfAcross * 0.5 * (zHi - zLo);
        Assert.InRange(na.Volume, vol - 1e-15, vol + 1e-15);
        _out.WriteLine($"{angle}° clearance {clearance}: slot {2 * na.HalfX:F5} along B × {2 * na.HalfY:F5} across × {2 * na.HalfZ:F5} deep, volume {na.Volume:E3}");
    }

    [Fact]
    public void ThreadedJoint_SymmetricWhenBIsTheUprightOne()
    {
        var up = new LathProfile(0.1, 0.012, upright: true);
        var flat = new LathProfile(0.08, 0.012, upright: false);
        Assert.True(JointGeometry.TryBuildNotches(O, Ta, Tb(70), N, up, flat, 0.5, 0.0, out var na1, out var nb1, out var k1));
        Assert.True(JointGeometry.TryBuildNotches(O, Tb(70), Ta, N, flat, up, 0.5, 0.0, out var na2, out var nb2, out var k2));
        Assert.Equal(JointKind.SlotInA, k1);
        Assert.Equal(JointKind.SlotInB, k2);
        Assert.True(nb1.IsEmpty); Assert.True(na2.IsEmpty);
        Assert.Equal(na1.HalfX, nb2.HalfX, 12); Assert.Equal(na1.HalfY, nb2.HalfY, 12); Assert.Equal(na1.HalfZ, nb2.HalfZ, 12);
        Assert.InRange((na1.Center - nb2.Center).Length, 0, 1e-12);
    }

    [Fact]
    public void ThreadedJoint_CentredLamella_IsAMortise()
    {
        // A upright and centred on the surface ([−0.05, 0.05]); B flat on the
        // surface ([0, 0.012]) runs through the middle of A: a mortise, no
        // bleed on either side, only the clearance.
        var pa = new LathProfile(0.1, 0.012, upright: true).WithAlign(LathAlign.Centred);
        var pb = new LathProfile(0.08, 0.012, upright: false);
        const double c = 0.0004;
        Assert.True(JointGeometry.TryBuildNotches(O, Ta, Tb(90), N, pa, pb, 0.5, c, out var na, out var nb, out var kind));
        Assert.Equal(JointKind.SlotInA, kind);
        Assert.True(nb.IsEmpty);
        Assert.InRange(na.HalfZ, 0.006 + c - 1e-12, 0.006 + c + 1e-12);
        Assert.InRange(Vec3d.Dot(na.Center - O, N), 0.006 - 1e-12, 0.006 + 1e-12);
        // lifted B (Offset 0.088 → [0.088, 0.1]) reaches A's top edge: the slot opens there, bleed on top only
        var pbTop = new LathProfile(0.08, 0.012, upright: false, offset: 0.038);
        Assert.True(JointGeometry.TryBuildNotches(O, Ta, Tb(90), N, pa, pbTop, 0.5, 0.0, out var nt, out _, out var kt));
        Assert.Equal(JointKind.SlotInA, kt);
        double bleed = 0.005;
        Assert.InRange(nt.HalfZ, 0.5 * (0.012 + bleed) - 1e-12, 0.5 * (0.012 + bleed) + 1e-12);
        Assert.InRange(Vec3d.Dot(nt.Center - O, N) + nt.HalfZ, 0.05 + bleed - 1e-12, 0.05 + bleed + 1e-12);
    }

    [Fact]
    public void ThreadedJoint_NoOverlap_CutsNothing()
    {
        // B lifted above A's top edge: the laths pass each other, nothing to cut
        var pa = new LathProfile(0.1, 0.012, upright: true);
        var pb = new LathProfile(0.08, 0.012, upright: false, offset: 0.15);
        Assert.True(JointGeometry.TryBuildNotches(O, Ta, Tb(90), N, pa, pb, 0.5, 0.0, out var na, out var nb, out var kind));
        Assert.Equal(JointKind.None, kind);
        Assert.True(na.IsEmpty); Assert.True(nb.IsEmpty);
        // touching exactly at A's top face (B at offset 0.1) counts as no overlap too
        var pbTouch = new LathProfile(0.08, 0.012, upright: false, offset: 0.1);
        Assert.True(JointGeometry.TryBuildNotches(O, Ta, Tb(90), N, pa, pbTouch, 0.5, 0.0, out _, out _, out var kTouch));
        Assert.Equal(JointKind.None, kTouch);
    }

    [Fact]
    public void SameOrientation_IsTheLapJointAsBefore()
    {
        var flat = new LathProfile(0.1, 0.02, upright: false);
        var up = new LathProfile(0.1, 0.02, upright: true);
        foreach (var (pa, pb) in new[] { (flat, flat), (up, up), (flat, new LathProfile(0.06, 0.02)) })
        {
            Assert.True(JointGeometry.TryBuildLapNotches(O, Ta, Tb(55), N, pa, pb, 0.4, 0.001, out var oa, out var ob));
            Assert.True(JointGeometry.TryBuildNotches(O, Ta, Tb(55), N, pa, pb, 0.4, 0.001, out var na, out var nb, out var kind));
            Assert.Equal(JointKind.Lap, kind);
            Assert.False(na.IsEmpty); Assert.False(nb.IsEmpty);
            Assert.Equal(oa.Volume, na.Volume); Assert.Equal(ob.Volume, nb.Volume);
            Assert.InRange((oa.Center - na.Center).Length + (ob.Center - nb.Center).Length, 0, 0);
        }
        // the parallel case is still refused
        Assert.False(JointGeometry.TryBuildNotches(O, Ta, Ta, N, up, flat, 0.5, 0.0, out _, out _, out _));
    }

    // ----------------------------------------------------------------- frame

    private const double E = 11e9;

    private static Vec3d[] Line(Vec3d a, Vec3d b, int n)
    {
        var p = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) p[i] = a + (double)i / n * (b - a);
        return p;
    }

    [Fact]
    public void HybridCross_SharesTheLoadByStiffness_AndRatesEachLathWithItsOwnSection()
    {
        // Two simply supported beams crossing at mid-span, pinned ends, P at
        // the crossing: A upright (I_A = t w³/12), B flat (I_B = w t³/12, 25×
        // less). Both mid-span slopes are zero by symmetry, so the rigid joint
        // induces no torsion and the beams share P by stiffness:
        // δ = P / (k_A + k_B), k = 48 E I / L³; P_i = k_i δ; M_i = P_i L / 4;
        // σ_i = M_i / W_i with W_A = I_A / (w/2), W_B = I_B / (t/2).
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 8.0, P = 400.0, w = 0.1, t = 0.02, allow = 20e6;
        var a = Line(new Vec3d(1, 5, 0), new Vec3d(1 + L, 5, 0), 16);
        var b = Line(new Vec3d(5, 1, 0), new Vec3d(5, 1 + L, 0), 16);
        var pa = new LathProfile(w, t, upright: true);
        var pb = new LathProfile(w, t, upright: false);
        var r = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { new Vec3d(5, 5, 0) }, new[] { a[0], a[^1], b[0], b[^1] },
            new[] { pa, pb }, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, AllowableStress = allow, Support = FrameAnalysis.SupportKind.Pinned, PointLoads = new[] { (new Vec3d(5, 5, 0), new Vec3d(0, 0, -P)) } });
        double IA = t * w * w * w / 12, IB = w * t * t * t / 12;
        double kA = 48 * E * IA / (L * L * L), kB = 48 * E * IB / (L * L * L);
        double delta = P / (kA + kB);
        double PA = kA * delta, PB = kB * delta;
        Assert.Equal(1, r.JointCount);
        Assert.InRange(r.MaxDisplacement / delta, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(r.Torsion.Max(), 0, 1e-9 * P * L);
        // reactions: A's ends carry P_A/2 each, B's ends P_B/2
        var byLath = new Dictionary<int, double>();
        foreach (int n in r.SupportNodes)
        {
            int lath = r.NodeMap[n][0].Curve;
            byLath[lath] = byLath.GetValueOrDefault(lath) + r.Reactions[n].Z;
        }
        Assert.InRange(byLath[0] / PA, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(byLath[1] / PB, 1 - 1e-9, 1 + 1e-9);
        // utilization per lath: the elements next to the joint carry M_i = P_i L / 4
        double uA = 0, uB = 0;
        for (int e = 0; e < r.Utilization.Length; e++)
            if (r.ElementSource[e].Curve == 0) uA = Math.Max(uA, r.Utilization[e]); else uB = Math.Max(uB, r.Utilization[e]);
        double expA = (PA * L / 4) / (IA / (w / 2)) / allow, expB = (PB * L / 4) / (IB / (t / 2)) / allow;
        Assert.InRange(uA / expA, 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(uB / expB, 1 - 1e-9, 1 + 1e-9);
        _out.WriteLine($"δ = {delta * 1000:F3} mm; A (upright) takes {PA / P * 100:F2} % of P, utilization {uA:F4}; B (flat) {PB / P * 100:F2} %, utilization {uB:F4}");
        // the same cross with both laths upright shares it half–half
        var rU = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { new Vec3d(5, 5, 0) }, new[] { a[0], a[^1], b[0], b[^1] }, pa, Vec3d.Zero,
            new FrameAnalysis.Options { E = E, AllowableStress = allow, Support = FrameAnalysis.SupportKind.Pinned, PointLoads = new[] { (new Vec3d(5, 5, 0), new Vec3d(0, 0, -P)) } });
        Assert.InRange(rU.MaxDisplacement / (P / (2 * kA)), 1 - 1e-9, 1 + 1e-9);
    }

    [Fact]
    public void EqualProfiles_AreBitIdenticalToTheSingleProfileCall()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 8.0;
        var a = Line(new Vec3d(1, 5, 0), new Vec3d(1 + L, 5, 0), 16);
        var b = Line(new Vec3d(5, 1, 0), new Vec3d(5, 1 + L, 0), 16);
        var p = new LathProfile(0.08, 0.012, upright: true);
        var opts = new FrameAnalysis.Options { E = E, Density = 500, JointRotationalStiffness = 50.0, PointLoads = new[] { (new Vec3d(3, 5, 0), new Vec3d(10, 0, -300)) } };
        var one = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { new Vec3d(5, 5, 0) }, new[] { a[0], a[^1], b[0], b[^1] }, p, new Vec3d(0, 0, -1000), opts);
        var per = FrameAnalysis.Compute(mesh, new[] { a, b }, new[] { new Vec3d(5, 5, 0) }, new[] { a[0], a[^1], b[0], b[^1] }, new[] { p, p }, new Vec3d(0, 0, -1000), opts);
        Assert.Equal(one.Displacements.Length, per.Displacements.Length);
        for (int i = 0; i < one.Displacements.Length; i++) Assert.Equal(one.Displacements[i], per.Displacements[i]);
        Assert.Equal(one.Utilization, per.Utilization);
        Assert.Equal(one.TotalLoad, per.TotalLoad);
    }

    [Fact]
    public void SelfWeight_UsesEachLathsOwnArea()
    {
        // two cantilevers, nothing but self-weight: δ_tip = ρ g A L⁴ / (8 E I)
        // with each lath's own A and I; each equals its single-lath run exactly
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        const double L = 4.0, rho = 500.0, g = 9.81;
        var a = Line(new Vec3d(1, 3, 0), new Vec3d(1 + L, 3, 0), 40);
        var b = Line(new Vec3d(1, 7, 0), new Vec3d(1 + L, 7, 0), 40);
        var pa = new LathProfile(0.1, 0.02, upright: false);     // A = 2e-3, I = w t³/12
        var pb = new LathProfile(0.05, 0.01, upright: false);    // A = 5e-4, I = w t³/12 (16× less)
        var opts = new FrameAnalysis.Options { E = E, Density = rho, Gravity = g };
        var both = FrameAnalysis.Compute(mesh, new[] { a, b }, null, new[] { a[0], b[0] }, new[] { pa, pb }, Vec3d.Zero, opts);
        var onlyA = FrameAnalysis.Compute(mesh, new[] { a }, null, new[] { a[0] }, pa, Vec3d.Zero, opts);
        var onlyB = FrameAnalysis.Compute(mesh, new[] { b }, null, new[] { b[0] }, pb, Vec3d.Zero, opts);
        double tipA = 0, tipB = 0;
        for (int n = 0; n < both.Nodes.Length; n++)
        {
            var (c, i) = both.NodeMap[n][0];
            if (i == 40) { if (c == 0) tipA = -both.Displacements[n].Z; else tipB = -both.Displacements[n].Z; }
        }
        Assert.InRange(tipA / onlyA.MaxDisplacement, 1 - 1e-12, 1 + 1e-12);
        Assert.InRange(tipB / onlyB.MaxDisplacement, 1 - 1e-12, 1 + 1e-12);
        double exactA = rho * g * pa.SectionArea() * Math.Pow(L, 4) / (8 * E * (0.1 * Math.Pow(0.02, 3) / 12));
        double exactB = rho * g * pb.SectionArea() * Math.Pow(L, 4) / (8 * E * (0.05 * Math.Pow(0.01, 3) / 12));
        Assert.InRange(tipA / exactA, 0.99, 1.01); // lumped nodal loads: O(h²)
        Assert.InRange(tipB / exactB, 0.99, 1.01);
        double wA = rho * g * pa.SectionArea() * L, wB = rho * g * pb.SectionArea() * L;
        Assert.InRange(-both.TotalLoad.Z / (wA + wB), 1 - 1e-12, 1 + 1e-12);
        _out.WriteLine($"tips {tipA * 1000:F3} / {tipB * 1000:F3} mm vs {exactA * 1000:F3} / {exactB * 1000:F3} (ρgAL⁴/8EI)");
    }

    [Fact]
    public void ProfileCountMismatch_IsAnError()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 10.0);
        var a = Line(new Vec3d(1, 5, 0), new Vec3d(5, 5, 0), 4);
        Assert.Throws<ArgumentException>(() => FrameAnalysis.Compute(mesh, new[] { a }, null, new[] { a[0] }, new LathProfile[0], Vec3d.Zero, new FrameAnalysis.Options { E = E }));
    }

    // ------------------------------------------------------------- catenoid

    /// <summary>
    /// The AGH net of the roadmap: asymptotic family A (upright) crossed by
    /// the meridians (geodesics, flat). Checks: kn(A) ≈ 0, kg(B) ≈ 0, kn(B) =
    /// 1/cosh² z (the meridian curvature of r = cosh z), every A × B crossing a
    /// threaded joint with the slot in A.
    /// </summary>
    [Fact]
    public void AghNetOnTheCatenoid_UprightAsymptoticLamellas_FlatGeodesicSlats()
    {
        const int seg = 96, rows = 48;
        var mesh = TestMeshes.CreateCatenoid(1.0, 2.0, seg, rows);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        var famA = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, field.Family2);
        // meridians: geodesics seeded every 6th vertex of the waist, heading up the axis
        int waist = (rows / 2) * seg;
        var seeds = Enumerable.Range(0, seg / 6).Select(i => waist + 6 * i).ToArray();
        var dirs = seeds.Select(_ => new Vec3d(0, 0, 1)).ToArray();
        var famB = GeodesicCurves.Trace(mesh, seeds, dirs, new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 4000 });
        Assert.True(famA.Count >= 15, $"{famA.Count} asymptotic laths");
        Assert.Equal(seg / 6, famB.Count);

        var pa = new LathProfile(0.1, 0.012, upright: true);
        var pb = new LathProfile(0.1, 0.012, upright: false);
        var la = LathAnalysis.Analyze(mesh, famA, new LathAnalysis.Options { Profile = pa, Window = 0.1 });
        var lb = LathAnalysis.Analyze(mesh, famB, new LathAnalysis.Options { Profile = pb, Window = 0.1 });
        // the rim samples are left out (the border exit turns by up to 1°, a
        // known tracer effect noted in the roadmap); the interior is the lamella
        static double InteriorMax(double[] v) => v.Length > 10 ? v.Skip(5).Take(v.Length - 10).Select(Math.Abs).Max() : v.Select(Math.Abs).Max();
        double knA = la.Max(r => InteriorMax(r.NormalCurvature));
        double knAMean = la.Average(r => r.NormalCurvature.Select(Math.Abs).Average());
        double kgB = lb.Max(r => InteriorMax(r.GeodesicCurvature));
        // kn of a meridian = 1/cosh² z, compared point by point (window ends excluded)
        double knErr = 0, knMean = 0; int cnt = 0;
        for (int c = 0; c < famB.Count; c++)
        {
            var line = famB[c];
            for (int i = 5; i + 5 < line.Length; i++)
            {
                double truth = 1.0 / Math.Pow(Math.Cosh(line[i].Z), 2);
                knErr = Math.Max(knErr, Math.Abs(Math.Abs(lb[c].NormalCurvature[i]) - truth));
                knMean += Math.Abs(lb[c].NormalCurvature[i]); cnt++;
            }
        }
        knMean /= cnt;
        _out.WriteLine($"A: {famA.Count} asymptotic laths, interior max |kn| {knA:F4} (mean {knAMean:F4}); B: {famB.Count} meridians, interior max |kg| {kgB:F4}, mean |kn| {knMean:F4}, max |kn − 1/cosh² z| {knErr:F4}");
        Assert.True(knA < 0.05, $"asymptotic laths bend out of the surface: max |kn| {knA:F4}");
        Assert.True(knAMean < 0.01, $"asymptotic laths bend out of the surface: mean |kn| {knAMean:F4}");
        Assert.True(kgB < 0.05, $"meridians turn in the surface: max |kg| {kgB:F4}");
        Assert.True(knErr < 0.05, $"meridian kn off 1/cosh² z by {knErr:F4}");
        Assert.True(knMean > 0.4, $"meridians are curved (mean |kn| {knMean:F3})");

        // every crossing is a threaded joint with the slot in the upright lamella
        var xs = NetIntersections.Find(famA, famB);
        Assert.True(xs.Count >= 100, $"{xs.Count} crossings");
        int threaded = 0, lap = 0, none = 0, refused = 0;
        double slotDepth = 0;
        foreach (var x in xs)
        {
            var ta = NetIntersections.TangentAt(famA, x, false);
            var tb = NetIntersections.TangentAt(famB, x, true);
            var n = proj.ClosestPoint(x.Point, proj.NearestVertexGlobal(x.Point)).SmoothNormal;
            if (!JointGeometry.TryBuildNotches(x.Point, ta, tb, n, pa, pb, 0.5, 0.0, out var na, out var nb, out var kind)) { refused++; continue; }
            if (kind == JointKind.SlotInA) { threaded++; Assert.True(nb.IsEmpty); slotDepth = Math.Max(slotDepth, 2 * na.HalfZ); }
            else if (kind == JointKind.Lap) lap++;
            else none++;
        }
        _out.WriteLine($"{xs.Count} crossings: {threaded} threaded (slot in A), {lap} lap, {none} without contact, {refused} refused; deepest slot {slotDepth:F4} (0.012 + 0.005 bleed)");
        Assert.Equal(xs.Count, threaded);
        Assert.InRange(slotDepth, 0.017 - 1e-9, 0.017 + 1e-9);
    }
}
