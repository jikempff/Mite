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

public class Fabrication2Tests
{
    // ---------- MeshCleanup ----------

    [Fact]
    public void MeshCleanup_WeldsAndRemovesDegenerateFaces()
    {
        // Two quads sharing an edge, but with duplicated vertices (offset by 1e-9),
        // one zero-area triangle, and one duplicate face
        var verts = new List<Vec3d>
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),       // 0-3 quad A
            new(1 + 1e-9, 0, 0), new(2, 0, 0), new(2, 1, 0), new(1, 1 + 1e-9, 0), // 4-7 quad B (dupes of 1,2)
            new(3, 0, 0), new(3.5, 0, 0), new(4, 0, 0),                   // 8-10 collinear (zero area, no welding)
        };
        var faces = new List<int[]>
        {
            new[] { 0, 1, 2, 3 },
            new[] { 4, 5, 6, 7 },
            new[] { 8, 9, 10 },       // degenerate (zero area)
            new[] { 0, 1, 2, 3 },     // duplicate of quad A (after welding)
        };
        var result = MeshCleanup.Compute(new MeshData(verts.ToArray(), faces.ToArray()), 1e-6);

        Assert.Equal(2, result.WeldedVertices);              // verts 4 and 7 welded away
        Assert.Equal(1, result.RemovedDegenerateFaces);
        Assert.Equal(1, result.RemovedDuplicateFaces);
        Assert.Equal(2, result.Mesh.Faces.Length);
        Assert.Equal(9, result.Mesh.VertexCount);            // 11 - 2 welded
    }

    [Fact]
    public void MeshCleanup_UnifiesWinding()
    {
        // Two triangles sharing an edge, second one flipped
        var verts = new[]
        {
            new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(1, 1, 0), new Vec3d(0, 1, 0)
        };
        var faces = new[] { new[] { 0, 1, 2 }, new[] { 3, 2, 1 } }; // second uses shared edge 1->2 same direction as first? check after
        var result = MeshCleanup.Compute(new MeshData(verts, faces), 0, unifyWinding: true);

        // After unification, the shared edge must be traversed in opposite directions
        var f0 = result.Mesh.Faces[0];
        var f1 = result.Mesh.Faces[1];
        Assert.Equal(3, f0.Length);
        Assert.Equal(3, f1.Length);
        // Shared edge is 1-2 (or 2-1): count directed uses
        int same = 0, opposite = 0;
        foreach (var f in new[] { f0, f1 })
            for (int i = 0; i < 3; i++)
            {
                int a = f[i], b = f[(i + 1) % 3];
                if (a == 1 && b == 2) same++;
                if (a == 2 && b == 1) opposite++;
            }
        Assert.Equal(1, same);
        Assert.Equal(1, opposite);
    }

    // ---------- KdTree ----------

    [Fact]
    public void KdTree_MatchesBruteForce()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        var proj = new MeshProjection(sphere);
        var rng = new Random(7);
        for (int k = 0; k < 50; k++)
        {
            var p = new Vec3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
            int fast = proj.NearestVertexGlobal(p);
            int slow = 0;
            double best = double.MaxValue;
            for (int i = 0; i < sphere.VertexCount; i++)
            {
                double d = (sphere.Vertices[i] - p).LengthSquared;
                if (d < best) { best = d; slow = i; }
            }
            Assert.Equal(slow, fast);
        }
    }

    // ---------- StripUnroll ----------

    [Fact]
    public void StripUnroll_StraightFlatStrip_RectanglePattern()
    {
        var grid = TestMeshes.CreateQuadGrid(20, 20, 1.0).ToTriangulated();
        var proj = new MeshProjection(grid);
        var line = new Vec3d[11];
        for (int i = 0; i <= 10; i++) line[i] = new Vec3d(5 + i, 10, 0);

        var result = StripUnroll.Unroll(proj, line, new LathProfile(0.4, 0.1));
        Assert.NotNull(result);
        var r = result!.Value;

        Assert.Equal(10.0, r.Length, 6);
        // Rectangle: edges are straight lines 10 long, 0.4 apart
        double minY = r.EdgeA.Min(p => p.Y), maxY = r.EdgeB.Max(p => p.Y);
        Assert.Equal(0.4, maxY - minY, 4);
        double spanX = Math.Max(r.EdgeA.Max(p => p.X), r.EdgeB.Max(p => p.X));
        Assert.Equal(10.0, spanX, 4);
        // Edges stay parallel: every edge point at y = 0 or 0.4
        foreach (var p in r.EdgeA) Assert.True(Math.Abs(p.Y - minY) < 1e-9);
        foreach (var p in r.EdgeB) Assert.True(Math.Abs(p.Y - maxY) < 1e-9);
    }

    [Fact]
    public void StripUnroll_CurvedStrip_PreservesEdgeLengths()
    {
        // Circular arc strip on a plane: exact pattern is an annular arc
        var grid = TestMeshes.CreateQuadGrid(30, 30, 1.0).ToTriangulated();
        var proj = new MeshProjection(grid);
        int n = 33;
        var line = new Vec3d[n];
        double R = 5.0;
        for (int i = 0; i < n; i++)
        {
            double a = Math.PI * 0.5 * i / (n - 1);
            line[i] = new Vec3d(15 + R * Math.Cos(a), 15 + R * Math.Sin(a), 0);
        }

        var r = StripUnroll.Unroll(proj, line, new LathProfile(0.4, 0.1))!.Value;

        // Every pattern edge segment must keep its 3D length (isometry)
        for (int i = 1; i < n; i++)
        {
            double l3 = (line[i] - line[i - 1]).Length;
            double l2 = (r.Centerline[i] - r.Centerline[i - 1]).Length;
            Assert.True(Math.Abs(l3 - l2) < 1e-9, $"segment {i} not isometric: {l3} vs {l2}");
        }
        // Pattern width constant
        for (int i = 0; i < n; i++)
            Assert.True(Math.Abs((r.EdgeB[i] - r.EdgeA[i]).Length - 0.4) < 1e-9);
    }

    // ---------- LathSegmentation ----------

    [Fact]
    public void Segmentation_RespectsStockLength()
    {
        var line = new Vec3d[21];
        for (int i = 0; i <= 20; i++) line[i] = new Vec3d(i * 0.5, 0, 0); // length 10

        var r = LathSegmentation.Segment(line, 3.0, 0.2);
        Assert.Equal(4, r.Segments.Count); // 3 + 3 + 3 + 1
        foreach (var seg in r.Segments)
        {
            double len = 0;
            for (int i = 1; i < seg.Length; i++) len += (seg[i] - seg[i - 1]).Length;
            Assert.True(len <= 3.0 + 1e-9, $"segment too long: {len}");
        }
        Assert.Equal(3, r.CutPoints.Length);
    }

    [Fact]
    public void Segmentation_AvoidsJoints()
    {
        var line = new Vec3d[21];
        for (int i = 0; i <= 20; i++) line[i] = new Vec3d(i * 0.5, 0, 0);

        // Joint at arc 3.05: a cut at 3.0 would clash; it must move to 3.05 - 0.2 = 2.85
        var joints = LathSegmentation.JointArcLengths(line, new[] { new Vec3d(3.05, 0, 0) });
        Assert.Equal(3.05, joints[0], 6);

        var r = LathSegmentation.Segment(line, 3.0, 0.2, joints);
        Assert.Equal(2.85, r.CutArcLengths[0], 6);
    }

    [Fact]
    public void SpliceNotches_FlatStrip_ComplementaryHalves()
    {
        var ok = JointGeometry.TryBuildSpliceNotches(
            new Vec3d(5, 0, 0), new Vec3d(1, 0, 0), new Vec3d(0, 0, 1),
            new LathProfile(0.4, 0.1), spliceLength: 0.3, clearance: 0.0,
            out NotchSolid end, out NotchSolid start);

        Assert.True(ok);
        // End notch: upstream of the cut, top half of the strip
        Assert.Equal(5.0 - 0.15, end.Center.X, 6);
        Assert.Equal(0.075, end.Center.Z, 6);
        // Start notch: downstream, bottom half
        Assert.Equal(5.0 + 0.15, start.Center.X, 6);
        Assert.Equal(0.025, start.Center.Z, 6);
        Assert.Equal(0.15, end.HalfX, 6);
    }

    // ---------- ConjugateNet + Umbilics ----------

    [Fact]
    public void ConjugateNet_Torus_TracesBothFamilies()
    {
        var torus = TestMeshes.CreateTorus(3.0, 1.0, 48, 24);
        var opts = new EvenlySpacedNet.Options { Spacing = 0.6, StepSize = 0.05, MaxSteps = 800 };
        var net = ConjugateNet.Trace(torus, -1, opts);

        Assert.True(net.FamilyA.Count >= 2, $"family A: {net.FamilyA.Count} curves");
        Assert.True(net.FamilyB.Count >= 2, $"family B: {net.FamilyB.Count} curves");

        // Curves must lie on the torus: (sqrt(x²+y²) - R)² + z² = r²
        foreach (var p in net.FamilyA.SelectMany(l => l).Take(200))
        {
            double q = Math.Sqrt(p.X * p.X + p.Y * p.Y) - 3.0;
            double err = Math.Abs(q * q + p.Z * p.Z - 1.0);
            Assert.True(err < 0.05, $"point off torus: {err:F4}");
        }
    }

    [Fact]
    public void Umbilics_Sphere_FlagsMostVertices()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        var curvature = PrincipalCurvature.Compute(sphere);
        var umbilics = Umbilics.Find(curvature, 0.1);

        Assert.True(umbilics.Length > sphere.VertexCount / 2,
            $"sphere should be mostly umbilical, got {umbilics.Length}/{sphere.VertexCount}");
    }

    [Fact]
    public void Umbilics_Saddle_CenterNotUmbilical()
    {
        var saddle = TestMeshes.CreateSaddle(20, 2.0);
        var curvature = PrincipalCurvature.Compute(saddle);
        var umbilics = Umbilics.Find(curvature, 0.05);
        int center = (20 / 2) * 21 + 20 / 2;

        Assert.DoesNotContain(center, umbilics);
    }

    // ---------- FrameAnalysis ----------

    [Fact]
    public void FrameAnalysis_Cantilever_MatchesEulerBernoulli()
    {
        // Straight lath along x, fixed at x=0, uniform vertical load w.
        // Tip deflection of a cantilever under UDL: delta = w L^4 / (8 E I)
        double L = 2.0, w = 1000.0, E = 11e9;
        double width = 0.1, thickness = 0.05;
        double I = width * Math.Pow(thickness, 3) / 12.0; // bending in the vertical plane

        var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        var lath = new Vec3d[21];
        for (int i = 0; i <= 20; i++) lath[i] = new Vec3d(L * i / 20.0, 5, 0.01);
        var laths = new List<Vec3d[]> { lath };
        var supports = new List<Vec3d> { new Vec3d(0, 5, 0.01) };

        var r = FrameAnalysis.Compute(mesh, laths, null, supports,
            new LathProfile(width, thickness, upright: false),
            new Vec3d(0, 0, -w),
            new FrameAnalysis.Options { E = E, SnapTolerance = 0.05 });

        double expected = w * Math.Pow(L, 4) / (8.0 * E * I);
        Assert.True(r.MaxDisplacement > 0);
        Assert.True(Math.Abs(r.MaxDisplacement - expected) / expected < 0.05,
            $"tip deflection {r.MaxDisplacement:E4} vs analytic {expected:E4}");

        // Fixed end carries the full shear: max bending moment w L^2 / 2
        double maxM = r.BendingY.Max();
        double expectedM = w * L * L / 2.0;
        Assert.True(Math.Abs(maxM - expectedM) / expectedM < 0.05,
            $"root moment {maxM:F1} vs analytic {expectedM:F1}");
    }

    [Fact]
    public void FrameAnalysis_JointsCoupleLaths()
    {
        // Two crossing laths coupled at the crossing share a node
        var mesh = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        var lathA = new Vec3d[11];
        var lathB = new Vec3d[11];
        for (int i = 0; i <= 10; i++)
        {
            lathA[i] = new Vec3d(i, 5, 0.01);
            lathB[i] = new Vec3d(5, i, 0.01);
        }
        var joints = new List<Vec3d> { new Vec3d(5, 5, 0.01) };
        var supports = new List<Vec3d>
        {
            new Vec3d(0, 5, 0.01), new Vec3d(10, 5, 0.01),
            new Vec3d(5, 0, 0.01), new Vec3d(5, 10, 0.01)
        };

        var r = FrameAnalysis.Compute(mesh, new List<Vec3d[]> { lathA, lathB }, joints, supports,
            new LathProfile(0.1, 0.05), new Vec3d(0, 0, -100),
            new FrameAnalysis.Options { SnapTolerance = 0.1 });

        // Some node must merge locations from both curves
        Assert.Contains(r.NodeMap, locs =>
            locs.Any(l => l.Curve == 0) && locs.Any(l => l.Curve == 1));
    }
}
