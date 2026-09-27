using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// The Mite Net value (roadmap B): one object carrying surface, both lath
/// families and their profiles. Checked against nets whose joints, ends and
/// topology are known exactly.
/// </summary>
public class LathNetTests
{
    private readonly ITestOutputHelper _out;
    public LathNetTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Straight rulings of x² + y² − z² = 1 through the waist points at angle
    /// φ (family A, direction (−sin φ, cos φ, 1)) and ψ (family B, direction
    /// (sin ψ, −cos ψ, 1)). At height z a ruling sits at polar angle φ + atan z
    /// (A) or ψ − atan z (B), so A_φ and B_ψ meet exactly once, at
    /// z = tan((ψ − φ)/2) with ψ − φ wrapped to (−π, π].
    /// </summary>
    private static Vec3d[] Ruling(double angle, bool familyA, int pieces = 20)
    {
        var line = new Vec3d[pieces + 1];
        double s = familyA ? 1 : -1;
        for (int k = 0; k <= pieces; k++)
        {
            double z = -1 + 2.0 * k / pieces;
            line[k] = new Vec3d(Math.Cos(angle) - s * z * Math.Sin(angle), Math.Sin(angle) + s * z * Math.Cos(angle), z);
        }
        return line;
    }

    private static LathNet HyperboloidRulingNet(int n, out int expectedJoints)
    {
        // 64 segments: rim vertices every 5.625°, so with n = 16 every ruling
        // ends exactly on a rim vertex (φ ± 45° at z = ±1)
        var mesh = TestMeshes.CreateHyperboloid(1, 1, 1, 64, 32);
        var a = new List<Vec3d[]>(); var b = new List<Vec3d[]>();
        var phis = Enumerable.Range(0, n).Select(i => 2 * Math.PI * i / n).ToArray();
        var psis = Enumerable.Range(0, n).Select(j => 2 * Math.PI * (j + 0.5) / n).ToArray();
        foreach (double p in phis) a.Add(Ruling(p, true));
        foreach (double q in psis) b.Add(Ruling(q, false));
        expectedJoints = 0;
        foreach (double p in phis)
            foreach (double q in psis)
            {
                double d = Math.IEEERemainder(q - p, 2 * Math.PI);
                if (Math.Abs(Math.Tan(d / 2)) <= 1) expectedJoints++;
            }
        return new LathNet(mesh, a, b, NetKind.Asymptotic, 0.3);
    }

    [Fact]
    public void HyperboloidRulings_JointsEndsAndTopologyAreExact()
    {
        var net = HyperboloidRulingNet(16, out int expected);
        Assert.Equal(128, expected); // 8 of the 16 B rulings meet each A ruling inside |z| ≤ 1
        Assert.Equal(16, net.CountA);
        Assert.Equal(16, net.CountB);

        var joints = net.Joints();
        Assert.Equal(expected, joints.Count);
        foreach (var j in joints)
        {
            // the crossing lies on the hyperboloid at z = tan((ψ − φ)/2)
            double phi = 2 * Math.PI * j.CurveA / 16, psi = 2 * Math.PI * (j.CurveB + 0.5) / 16;
            double z = Math.Tan(Math.IEEERemainder(psi - phi, 2 * Math.PI) / 2);
            Assert.True(Math.Abs(j.Point.Z - z) < 1e-12, $"joint z {j.Point.Z} vs {z}");
            Assert.True(Math.Abs(j.Point.X * j.Point.X + j.Point.Y * j.Point.Y - z * z - 1) < 1e-12);
            Assert.True(j.Gap < 1e-12);
            Assert.False(j.IsTJunction);
        }

        var ends = net.Ends();
        Assert.Equal(64, ends.Count);
        Assert.All(ends, e => Assert.Equal(LathEndKind.Border, e.Kind));
        Assert.Equal((64, 0, 0, 0), net.EndCounts());

        // every lath: 8 joints → 7 members between them + 2 tails to the rim
        var topo = net.Topology();
        Assert.Equal(128, topo.Nodes.Length);
        Assert.All(topo.Valence, v => Assert.Equal(4, v));
        Assert.Equal(32 * 9, topo.Members.Count);
        Assert.Equal(64, topo.Members.Count(m => m.NodeStart < 0 || m.NodeEnd < 0));

        // lengths: every ruling from z = −1 to 1 has length 2√2
        Assert.Equal(16 * 2 * Math.Sqrt(2), net.TotalLengthA, 10);
        Assert.Equal(16 * 2 * Math.Sqrt(2), net.TotalLengthB, 10);
        _out.WriteLine(net.Summary());
    }

    /// <summary>
    /// Flat 4 × 4 plate with hand-placed laths: border ends, a T-junction on
    /// the other family, a T-junction on the own family and two free ends.
    /// </summary>
    [Fact]
    public void PlanarNet_ClassifiesEveryEnd()
    {
        var mesh = TestMeshes.CreateQuadGrid(4, 4, 0.25); // [0,1]²
        Vec3d P(double x, double y) => new Vec3d(x, y, 0);
        var a = new List<Vec3d[]>
        {
            new[] { P(0, 0.25), P(0.5, 0.25), P(1, 0.25) },   // A0 border–border
            new[] { P(0, 0.5), P(1, 0.5) },                   // A1 border–border
            new[] { P(0, 0.75), P(1, 0.75) },                 // A2 border–border
            new[] { P(0.6, 0.75), P(0.6, 0.9) },              // A3 starts on A2 (own family), ends free
        };
        var b = new List<Vec3d[]>
        {
            new[] { P(0.5, 0), P(0.5, 1) },                   // B0 border–border, crosses A0–A2
            new[] { P(0.25, 0.5), P(0.25, 1) },               // B1 starts on A1 (T-junction), ends on the border
            new[] { P(0.75, 0.3), P(0.75, 0.6) },             // B2 free–free, crosses A1
        };
        var net = new LathNet(mesh, a, b);
        var ends = net.Ends();
        Assert.Equal(14, ends.Count);
        LathEnd End(int lath, bool atEnd) => ends.Single(e => e.Lath == lath && e.AtEnd == atEnd);

        for (int i = 0; i < 3; i++) { Assert.Equal(LathEndKind.Border, End(i, false).Kind); Assert.Equal(LathEndKind.Border, End(i, true).Kind); }
        Assert.Equal(LathEndKind.OnLath, End(3, false).Kind);
        Assert.Equal(2, End(3, false).Other);                      // A3 sits on A2
        Assert.Equal(LathEndKind.Free, End(3, true).Kind);
        Assert.Equal(LathEndKind.Border, End(4, false).Kind);
        Assert.Equal(LathEndKind.Border, End(4, true).Kind);
        Assert.Equal(LathEndKind.OnLath, End(5, false).Kind);
        Assert.Equal(1, End(5, false).Other);                      // B1 sits on A1 (global index 1)
        Assert.Equal(0.0, End(5, false).Distance, 15);
        Assert.Equal(LathEndKind.Border, End(5, true).Kind);
        Assert.Equal(LathEndKind.Free, End(6, false).Kind);
        Assert.Equal(LathEndKind.Free, End(6, true).Kind);
        Assert.Equal((9, 2, 3, 0), net.EndCounts());

        // joints A × B: B0 × A0, A1, A2; B1 × A1 (its end: a T-junction) and A2; B2 × A1
        var joints = net.Joints();
        Assert.Equal(6, joints.Count);
        Assert.Equal(1, joints.Count(j => j.IsTJunction));
        // contacts add A3 on A2 (same family, reported with family 0 on both sides)
        var contacts = net.Contacts();
        var own = contacts.Where(c => c.FamilyOfA == c.FamilyOfB).ToList();
        Assert.Single(own);
        Assert.Equal(new[] { 2, 3 }, new[] { net.GlobalIndex(own[0].FamilyOfA, own[0].CurveA), net.GlobalIndex(own[0].FamilyOfB, own[0].CurveB) }.OrderBy(x => x));
        Assert.Equal(5, net.GlobalIndex(1, 1));

        // without a surface nothing can be on the border; the lath contacts stay
        var bare = net.WithSurface(null);
        Assert.Equal((0, 2, 12, 0), bare.EndCounts());
    }

    [Fact]
    public void Serialisation_RoundTripsExactly()
    {
        var net0 = HyperboloidRulingNet(16, out _);
        var quadMesh = TestMeshes.CreateQuadGrid(3, 2, 0.5);
        var custom = LathProfile.Custom(new[] { (0.0, 0.0), (0.12, 0.0), (0.12, 0.02), (0.06, 0.05), (0.0, 0.02) }, true, 0.003);
        var nets = new[]
        {
            net0.WithProfiles(LathProfile.Rectangle(0.1, 0.012, true, 0.005), LathProfile.Round(0.04, 18, 0.001)),
            new LathNet(quadMesh, new[] { new[] { new Vec3d(0, 0.5, 0), new Vec3d(1.5, 0.5, 0) } }, null, NetKind.Geodesic, 0.0, custom),
            new LathNet(null, Array.Empty<Vec3d[]>(), Array.Empty<Vec3d[]>()),
        };
        foreach (var net in nets)
        {
            var bytes = net.ToBytes();
            var back = LathNet.FromBytes(bytes);
            Assert.Equal(bytes, back.ToBytes());
            Assert.Equal(net.Kind, back.Kind);
            Assert.Equal(net.Spacing, back.Spacing);
            Assert.Equal(net.CountA, back.CountA);
            Assert.Equal(net.CountB, back.CountB);
            for (int i = 0; i < net.LathCount; i++) Assert.Equal(net.Lath(i), back.Lath(i));
            Assert.Equal(net.Surface == null, back.Surface == null);
            if (net.Surface != null)
            {
                Assert.Equal(net.Surface.Vertices, back.Surface!.Vertices);
                Assert.Equal(net.Surface.Faces.Select(f => string.Join(",", f)), back.Surface.Faces.Select(f => string.Join(",", f)));
            }
            foreach (var (p, q) in new[] { (net.ProfileA, back.ProfileA), (net.ProfileB, back.ProfileB) })
            {
                Assert.Equal(p == null, q == null);
                if (p == null) continue;
                Assert.Equal(p.Value.Kind, q!.Value.Kind);
                Assert.Equal(p.Value.Width, q.Value.Width, 15);
                Assert.Equal(p.Value.Thickness, q.Value.Thickness, 15);
                Assert.Equal(p.Value.Upright, q.Value.Upright);
                Assert.Equal(p.Value.Offset, q.Value.Offset);
                Assert.Equal(p.Value.SectionArea(), q.Value.SectionArea(), 15);
            }
        }
        // the round bar keeps its polygon: 18 sides of a 0.04 circle
        var round = LathNet.FromBytes(nets[0].ToBytes()).ProfileB!.Value;
        Assert.Equal(18, round.SectionPoints().Length);
        Assert.Equal(0.5 * 18 * 0.02 * 0.02 * Math.Sin(2 * Math.PI / 18), round.SectionArea(), 14);
        Assert.Throws<System.IO.InvalidDataException>(() => LathNet.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Fact]
    public void Profiles_PerFamily_AndCopiesDoNotAlias()
    {
        var line = new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0) };
        var net = new LathNet(null, new[] { line, new[] { new Vec3d(0, 0, 0) } }, new[] { line });
        Assert.Equal(1, net.CountA); // the one-point polyline is dropped
        line[1] = new Vec3d(5, 5, 5);
        Assert.Equal(new Vec3d(1, 0, 0), net.Lath(0)[1]);
        Assert.Null(net.ProfileOf(0));

        var upright = LathProfile.Rectangle(0.1, 0.01, true);
        var flat = LathProfile.Rectangle(0.08, 0.008, false);
        var one = net.WithProfiles(upright);
        Assert.True(one.ProfileOf(0)!.Value.Upright);
        Assert.True(one.ProfileOf(1)!.Value.Upright); // B follows A
        var agh = net.WithProfiles(upright, flat);   // the asymptotic–geodesic hybrid: A upright, B flat
        Assert.True(agh.ProfileOf(0)!.Value.Upright);
        Assert.False(agh.ProfileOf(1)!.Value.Upright);
        Assert.Equal(0.08, agh.ProfileOf(1)!.Value.Width);
        Assert.Contains("/ B", agh.Summary());
        var moved = agh.WithFamilies(new[] { new[] { new Vec3d(0, 1, 0), new Vec3d(1, 1, 0) } }, null, NetKind.Kinetic);
        Assert.Equal(NetKind.Kinetic, moved.Kind);
        Assert.Equal(0, moved.CountB);
        Assert.True(moved.ProfileOf(0)!.Value.Upright);
    }

    [Fact]
    public void ProfileScaled_ScalesEveryLength()
    {
        const double f = 0.001; // millimetres to metres
        var profiles = new[]
        {
            LathProfile.Rectangle(100, 10, true, 5),
            LathProfile.Round(40, 18, 2),
            LathProfile.Custom(new[] { (0.0, 0.0), (120.0, 0.0), (120.0, 20.0), (60.0, 50.0), (0.0, 20.0) }, false, 3),
        };
        foreach (var p in profiles)
        {
            var q = p.Scaled(f);
            Assert.Equal(p.Kind, q.Kind);
            Assert.Equal(p.Upright, q.Upright);
            Assert.Equal(p.Width * f, q.Width, 15);
            Assert.Equal(p.Thickness * f, q.Thickness, 15);
            Assert.Equal(p.Offset * f, q.Offset, 15);
            Assert.Equal(p.SectionArea() * f * f, q.SectionArea(), 15);
            var sp = p.SectionProperties(); var sq = q.SectionProperties();
            Assert.True(Math.Abs(sp.IA * Math.Pow(f, 4) - sq.IA) <= 1e-12 * sq.IA, $"IA {sq.IA} vs {sp.IA * Math.Pow(f, 4)}");
            Assert.True(Math.Abs(sp.J * Math.Pow(f, 4) - sq.J) <= 1e-12 * sq.J, $"J {sq.J} vs {sp.J * Math.Pow(f, 4)}");
            Assert.Equal(sp.TwistLength * f, sq.TwistLength, 15);
        }
        Assert.Throws<ArgumentException>(() => profiles[0].Scaled(0));
    }

    /// <summary>
    /// Traced nets: the net's own end classification agrees with the bench's
    /// independent count (up to the bench's off-mesh clause, see below), and a web traced from the border on the catenoid
    /// (K &lt; 0 everywhere) has no free end — every curve runs border to border
    /// (tools/webgen Program.cs: border if the closest point is on a boundary
    /// edge or off the mesh, else on a curve within 1e-6).
    /// </summary>
    [Theory]
    [InlineData("catenoid", NetLayout.WebBorder)]
    [InlineData("saddle", NetLayout.Fill)]
    [InlineData("hyperboloid", NetLayout.Fill)]
    public void TracedNets_EndsAgreeWithTheBenchCount(string shape, NetLayout layout)
    {
        var mesh = AnalyticShapes.Build(shape, 0, 0, 0, 40);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = mesh.BoundingBoxDiagonal() / 20.0;
        var opts = new EvenlySpacedNet.Options { Spacing = s, Layout = layout, Continuous = layout != NetLayout.Fill };
        var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, opts, field.Family2);
        var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout, Continuous = layout != NetLayout.Fill }, field.Family1);
        var net = new LathNet(mesh, a, b, NetKind.Asymptotic, s);
        var (border, onLath, free, closed) = net.EndCounts();
        _out.WriteLine(net.Summary());

        // the bench's count (before this change). Its "off the mesh by more than
        // 1e-6 → border" clause also catches interior ends whose last chord cuts
        // under the facets (saddle, classic fill: one end 1.3e-4 off the mesh
        // but 2e-18 from the lath it stops on) — those are T-junctions, and the
        // net counts them so.
        var proj = new MeshProjection(mesh);
        int bb = 0, bo = 0, bf = 0, offMeshOnLath = 0;
        var all = a.Concat(b).ToList();
        foreach (var l in all)
        {
            if ((l[0] - l[^1]).LengthSquared < 1e-24) continue;
            foreach (var p in new[] { l[0], l[^1] })
            {
                var h = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                double best = all.Where(m => !ReferenceEquals(m, l)).Min(m => LathNet.DistanceToPolyline(p, m));
                if (proj.IsOnBoundary(h, 1e-4) || (h.Point - p).Length > 1e-6)
                {
                    bb++;
                    if (!proj.IsOnBoundary(h, 1e-4) && best < 1e-6)
                    {
                        offMeshOnLath++;
                        _out.WriteLine($"interior end {(h.Point - p).Length:E2} off the mesh, {best:E2} from a lath (edge {proj.AverageEdgeLength:G3})");
                    }
                    continue;
                }
                if (best < 1e-6) bo++; else bf++;
            }
        }
        _out.WriteLine($"net: {border} border, {onLath} on a lath, {free} free, {closed} closed; bench: {bb} / {bo} / {bf}, of which {offMeshOnLath} border ends are T-junctions");
        Assert.Equal(bb - offMeshOnLath, border);
        Assert.Equal(bo + offMeshOnLath, onLath);
        Assert.Equal(bf, free);
        if (layout == NetLayout.WebBorder)
        {
            Assert.Equal(0, free);
            Assert.True(border >= 0.9 * (border + onLath), $"{onLath} of {border + onLath} ends on a lath in a border web");
        }
    }
}
