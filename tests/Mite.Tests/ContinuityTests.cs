using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Continuity of traced nets: curves run border to border (or end on a
/// neighbour as a T-junction), never float in the interior, meet the border
/// along their own direction, and the contacts feed topology and analysis.
/// </summary>
public class ContinuityTests
{
    private static double ArcLength(Vec3d[] line)
    {
        double s = 0;
        for (int i = 1; i < line.Length; i++) s += (line[i] - line[i - 1]).Length;
        return s;
    }

    private static double DistToSegment(Vec3d p, Vec3d a, Vec3d b)
    {
        var ab = b - a;
        double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - a, ab) / Math.Max(ab.LengthSquared, 1e-30)));
        return (a + t * ab - p).Length;
    }

    private static double DistToOtherCurves(Vec3d p, Vec3d[] self, IEnumerable<Vec3d[]> all)
    {
        double best = double.MaxValue;
        foreach (var m in all)
        {
            if (ReferenceEquals(m, self)) continue;
            for (int i = 0; i + 1 < m.Length; i++) best = Math.Min(best, DistToSegment(p, m[i], m[i + 1]));
        }
        return best;
    }

    private static double EndTurnDegrees(Vec3d[] l, bool atEnd)
    {
        if (l.Length < 3) return 0;
        Vec3d u = atEnd ? l[^2] - l[^3] : l[1] - l[0];
        Vec3d v = atEnd ? l[^1] - l[^2] : l[2] - l[1];
        if (u.Length < 1e-12 || v.Length < 1e-12) return 0;
        return Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(u, v) / (u.Length * v.Length)))) * 180 / Math.PI;
    }

    /// <summary>Quad mesh of the saddle trimmed to a disk: a staircase border, as subdivided/trimmed meshes have.</summary>
    private static MeshData QuadDisk(int div, double size, double radius)
    {
        var verts = new List<Vec3d>();
        var map = new Dictionary<(int, int), int>();
        var faces = new List<int[]>();
        int V(int i, int j)
        {
            if (!map.TryGetValue((i, j), out int k))
            {
                double x = size * (i / (double)div - 0.5), y = size * (j / (double)div - 0.5);
                k = verts.Count;
                verts.Add(new Vec3d(x, y, x * x - y * y));
                map[(i, j)] = k;
            }
            return k;
        }
        for (int j = 0; j < div; j++)
            for (int i = 0; i < div; i++)
            {
                double cx = size * ((i + 0.5) / div - 0.5), cy = size * ((j + 0.5) / div - 0.5);
                if (cx * cx + cy * cy > radius * radius) continue;
                faces.Add(new[] { V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1) });
            }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    private static (List<Vec3d[]> A, List<Vec3d[]> B) SaddleAsymptotic(MeshData saddle, bool continuous, double spacing = 0.15)
    {
        var pc = PrincipalCurvature.Compute(saddle.ToTriangulated());
        var field = AsymptoticCurves.ComputeDirections(pc);
        var a = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1,
            new EvenlySpacedNet.Options { Spacing = spacing, Continuous = continuous }, field.Family2);
        var b = EvenlySpacedNet.TraceField(saddle, field.Family2, field.Exists, -1,
            new EvenlySpacedNet.Options { Spacing = spacing, Continuous = continuous }, field.Family1);
        return (a, b);
    }

    [Fact]
    public void ContinuousAsymptoticNet_EveryEndIsOnTheBorder()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var (a, b) = SaddleAsymptotic(saddle, continuous: true);
        var all = a.Concat(b).ToList();
        Assert.True(a.Count > 15 && b.Count > 15, $"{a.Count} + {b.Count} curves");

        foreach (var l in all)
        {
            foreach (var p in new[] { l[0], l[^1] })
            {
                double toBorder = 1.0 - Math.Max(Math.Abs(p.X), Math.Abs(p.Y));
                Assert.True(toBorder < 1e-3, $"curve end ({p.X:F3}, {p.Y:F3}) is {toBorder:F3} inside the border");
            }
            Assert.True(ArcLength(l) >= 0.3, "no stubs");
        }
    }

    [Fact]
    public void ClassicMode_EndsOnNeighbourNeverFloating()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var (a, b) = SaddleAsymptotic(saddle, continuous: false);
        var all = a.Concat(b).ToList();
        int tEnds = 0;
        foreach (var l in all)
            foreach (var p in new[] { l[0], l[^1] })
            {
                if (1.0 - Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < 1e-3) continue;
                double d = DistToOtherCurves(p, l, all);
                Assert.True(d < 1e-6, $"interior end floats {d:E2} away from the nearest curve");
                tEnds++;
            }
        // The saddle's straight asymptotic lines converge in 3D spacing toward the centre,
        // so classic mode must stop some of them: those ends are T-junctions
        Assert.True(tEnds > 0, "expected some T-junction ends in classic mode");
    }

    [Fact]
    public void Traces_MeetTheBorderWithoutHook()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var (a, b) = SaddleAsymptotic(saddle, continuous: true);
        double worst = 0;
        foreach (var l in a.Concat(b))
        {
            worst = Math.Max(worst, EndTurnDegrees(l, false));
            worst = Math.Max(worst, EndTurnDegrees(l, true));
        }
        // Before the boundary-exit fix the perpendicular clamp produced 35° hooks
        Assert.True(worst < 5.0, $"last segment turns {worst:F1}°");
    }

    [Fact]
    public void QuadMeshWithStaircaseBorder_CurvesReachTheBorderCleanly()
    {
        var disk = QuadDisk(40, 2.4, 1.1);
        var proj = new MeshProjection(disk);
        var (a, b) = SaddleAsymptotic(disk, continuous: true);
        var all = a.Concat(b).ToList();
        Assert.True(all.Count > 30, $"{all.Count} curves");

        double worst = 0;
        foreach (var l in all)
        {
            foreach (var (p, atEnd) in new[] { (l[0], false), (l[^1], true) })
            {
                var h = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                Assert.True((h.Point - p).Length < 1e-6, "end is on the mesh");
                Assert.True(proj.IsOnBoundary(h, 1e-4), $"end ({p.X:F3}, {p.Y:F3}) is not on the border");
                worst = Math.Max(worst, EndTurnDegrees(l, atEnd));
            }
        }
        Assert.True(worst < 6.0, $"last segment turns {worst:F1}° on the staircase border");
    }

    [Fact]
    public void ConjugateNet_EveryEndIsOnTheBorder()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var conj = ConjugateNet.Trace(saddle, -1, new EvenlySpacedNet.Options { Spacing = 0.2 });
        foreach (var l in conj.FamilyA.Concat(conj.FamilyB))
            foreach (var p in new[] { l[0], l[^1] })
                Assert.True(1.0 - Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < 1e-3, $"end ({p.X:F3}, {p.Y:F3}) inside");
    }

    [Fact]
    public void GeodesicNet_NoFloatingEnds()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var g = EvenlySpacedNet.TraceGeodesics(saddle, 20 * 41 + 20, new Vec3d(1, 0.35, 0),
            new EvenlySpacedNet.Options { Spacing = 0.2 });
        Assert.True(g.Count > 10);
        foreach (var l in g)
            foreach (var p in new[] { l[0], l[^1] })
            {
                if (1.0 - Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < 1e-3) continue;
                Assert.True(DistToOtherCurves(p, l, g) < 1e-6, "geodesic end floats");
            }
    }

    [Fact]
    public void NetIntersections_ReportsAnEndTouchingAnotherCurve()
    {
        var a = new[] { new Vec3d(0, 0, 0), new Vec3d(0.5, 0, 0), new Vec3d(1, 0, 0) };
        var b = new[] { new Vec3d(0.3, -1, 0), new Vec3d(0.3, -0.5, 0), new Vec3d(0.3, 0, 0) };

        var cross = NetIntersections.Find(new[] { a }, new[] { b });
        Assert.Single(cross);
        Assert.True(cross[0].IsTJunction);
        Assert.Equal(0, cross[0].FamilyOfA);
        Assert.Equal(1, cross[0].FamilyOfB);
        Assert.True((cross[0].Point - new Vec3d(0.3, 0, 0)).Length < 1e-12);

        // Same family: found by the self-mode search as well
        var self = NetIntersections.Find(new[] { a, b }, null);
        Assert.Single(self);
        Assert.True(self[0].IsTJunction);
        Assert.Equal(0, self[0].FamilyOfB);
    }

    [Fact]
    public void NetTopology_TwoEndsOnOnePointMakeOneNode()
    {
        var a = new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(2, 0, 0) };
        var b1 = new[] { new Vec3d(1, -1, 0), new Vec3d(1, -0.5, 0), new Vec3d(1, 0, 0) };
        var b2 = new[] { new Vec3d(1, 1, 0), new Vec3d(1, 0.5, 0), new Vec3d(1, 0, 0) };
        var A = new List<Vec3d[]> { a };
        var B = new List<Vec3d[]> { b1, b2 };

        var contacts = NetIntersections.FindAll(A, B);
        Assert.True(contacts.Count >= 2, $"{contacts.Count} contacts");
        var topo = NetTopology.Build(A, B, contacts);
        Assert.Single(topo.Nodes);
        Assert.Equal(4, topo.Valence[0]);
        Assert.Equal(4, topo.Members.Count);
        // every member runs from the node to a free lath end
        Assert.All(topo.Members, m => Assert.True((m.NodeStart == 0) != (m.NodeEnd == 0)));
    }

    [Fact]
    public void FrameAnalysis_CouplesAnEndRestingOnAnotherLath()
    {
        var mesh = TestMeshes.CreateQuadGrid(2, 2, 10.0);
        // A: 10 m beam along x at y = 5, supported at both ends
        var a = new Vec3d[21];
        for (int i = 0; i <= 20; i++) a[i] = new Vec3d(i * 0.5, 5, 0);
        // B: 5 m beam along y from y = 0 (supported) ending on A between two of its vertices
        var b = new Vec3d[11];
        for (int i = 0; i <= 10; i++) b[i] = new Vec3d(5.25, i * 0.5, 0);
        var laths = new List<Vec3d[]> { a, b };
        var supports = new List<Vec3d> { a[0], a[^1], b[0] };
        var profile = new LathProfile(0.1, 0.05, false);
        var load = new Vec3d(0, 0, -100);

        var coupled = FrameAnalysis.Compute(mesh, laths, null, supports, profile, load);
        var loose = FrameAnalysis.Compute(mesh, laths, null, supports, profile, load,
            new FrameAnalysis.Options { CoupleEndsOnLaths = false });

        // Uncoupled, B is a 5 m cantilever; coupled, its tip rests on A
        Assert.True(coupled.Nodes.Length < loose.Nodes.Length, "coupling merges B's end into A");
        Assert.True(coupled.MaxDisplacement < 0.5 * loose.MaxDisplacement,
            $"coupled {coupled.MaxDisplacement:E3} vs loose {loose.MaxDisplacement:E3}");
    }

    [Fact]
    public void MeshProjection_KnowsItsBoundary()
    {
        var saddle = TestMeshes.CreateSaddle(10, 2.0);
        var proj = new MeshProjection(saddle);
        var inside = proj.ClosestPoint(new Vec3d(0.05, 0.05, 0), proj.NearestVertexGlobal(Vec3d.Zero));
        Assert.False(proj.IsOnBoundary(inside));
        var outside = proj.ClosestPoint(new Vec3d(1.2, 0.13, 0.98), proj.NearestVertexGlobal(new Vec3d(1, 0.13, 0.98)));
        Assert.True(proj.IsOnBoundary(outside));
        Assert.True(Math.Abs(outside.Point.X - 1.0) < 1e-9);

        // Exit along the step, not the perpendicular foot
        Vec3d from = new Vec3d(0.9, 0.0, 0.81), to = new Vec3d(1.1, 0.2, 1.1);
        Assert.True(proj.TryBoundaryExit(from, to, outside, out var exit));
        Assert.True(Math.Abs(exit.X - 1.0) < 1e-6, $"exit x {exit.X}");
        Assert.True(Math.Abs(exit.Y - 0.1) < 0.02, $"exit y {exit.Y}: must lie on the step direction");
    }
}
