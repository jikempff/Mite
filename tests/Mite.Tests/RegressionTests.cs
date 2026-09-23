using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Dynamics;
using Mite.Core.Fabrication;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Numerics;

namespace Mite.Tests;

/// <summary>Regression tests for the 1.2.0 review fixes and the new algorithms.</summary>
public class RegressionTests
{
    private static double ArcLength(Vec3d[] line)
    {
        double s = 0;
        for (int i = 1; i < line.Length; i++) s += (line[i] - line[i - 1]).Length;
        return s;
    }

    // ---------- Curvature ----------

    [Fact]
    public void PrincipalCurvature_FramesAreRightHanded()
    {
        var saddle = TestMeshes.CreateSaddle(30, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        for (int i = 0; i < saddle.VertexCount; i++)
        {
            double h = Vec3d.Dot(Vec3d.Cross(pc.D1[i], pc.D2[i]), pc.Normals[i]);
            Assert.True(h > 0.99, $"vertex {i}: (D1, D2, N) handedness {h}");
            Assert.True(Math.Abs(Vec3d.Dot(pc.D1[i], pc.Normals[i])) < 1e-9, "D1 must be tangent");
            Assert.True(Math.Abs(Vec3d.Dot(pc.D2[i], pc.Normals[i])) < 1e-9, "D2 must be tangent");
        }
    }

    [Fact]
    public void AsymptoticNet_AutoSpace_StaysInOneFamily()
    {
        // With consistent frames every AutoSpace curve of family A must align
        // with the Family1 field, never with Family2 (mixed families produced
        // the short stubs users saw)
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(pc);
        var opts = new EvenlySpacedNet.Options { Spacing = 0.15, StepSize = 0.015 };
        var famA = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts, field.Family2);
        Assert.True(famA.Count >= 10, $"only {famA.Count} curves");

        var proj = new MeshProjection(saddle);
        int wrong = 0, total = 0;
        foreach (var c in famA)
        {
            Assert.True(ArcLength(c) >= 2.0 * 0.15 - 1e-9, "no stubs shorter than 2 * spacing");
            for (int i = 1; i < c.Length; i += 5)
            {
                var t = (c[i] - c[i - 1]).Normalized();
                int vi = proj.NearestVertexGlobal(c[i]);
                if (!field.Exists[vi]) continue;
                double a = Math.Abs(Vec3d.Dot(t, field.Family1[vi]));
                double b = Math.Abs(Vec3d.Dot(t, field.Family2[vi]));
                total++;
                if (b > a + 0.2) wrong++;
            }
        }
        Assert.True(wrong == 0, $"{wrong}/{total} samples follow the other family");
    }

    [Fact]
    public void MeanAndGaussian_IgnoreDegenerateTriangles()
    {
        // A collinear sliver (vertex 1 on the edge 0-2) must not blow up H
        var v = new[]
        {
            new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(2, 0, 0), new Vec3d(1, 1, 0.1),
            new Vec3d(0, 1, 0), new Vec3d(2, 1, 0), new Vec3d(1, -1, 0)
        };
        var f = new[]
        {
            new[] { 0, 1, 3 }, new[] { 1, 2, 3 }, new[] { 0, 3, 4 }, new[] { 2, 5, 3 },
            new[] { 0, 2, 1 }, new[] { 0, 6, 1 }, new[] { 1, 6, 2 }
        };
        var mesh = new MeshData(v, f);
        var H = MeanCurvature.Compute(mesh).Values;
        var K = GaussianCurvature.Compute(mesh);
        Assert.True(Math.Abs(H[1]) < 1.0, $"H at the sliver vertex = {H[1]}");
        Assert.True(Math.Abs(K[1]) < 1.0, $"K at the sliver vertex = {K[1]}");
        foreach (double h in H) Assert.False(double.IsNaN(h) || double.IsInfinity(h));
    }

    [Fact]
    public void Sphere_MeanAndGaussian_ConsistentAreas()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        var H = MeanCurvature.Compute(sphere).Values;
        var K = GaussianCurvature.Compute(sphere);
        for (int i = 0; i < sphere.VertexCount; i++)
        {
            Assert.InRange(H[i], 0.9, 1.1);
            Assert.InRange(K[i], 0.9, 1.1);
        }
    }

    // ---------- Sparse solver ----------

    [Fact]
    public void SparseSolver_MatchesDenseSolution()
    {
        var rng = new Random(7);
        int n = 60;
        var dense = new double[n, n];
        var b = new SparseSymmetricSolver.Builder(n);
        for (int i = 0; i < n; i++)
        {
            dense[i, i] = 4 + rng.NextDouble();
            b.Add(i, i, dense[i, i]);
            for (int k = 1; k <= 3; k++)
            {
                int j = (i + k * 7) % n;
                if (j <= i) continue;
                double v = -0.3 * rng.NextDouble();
                dense[i, j] += v; dense[j, i] += v;
                b.Add(i, j, v);
            }
        }
        var rhs = new double[n];
        for (int i = 0; i < n; i++) rhs[i] = rng.NextDouble() - 0.5;

        var solver = SparseSymmetricSolver.Factor(b);
        Assert.NotNull(solver);
        var x = solver!.Solve(rhs);

        var ax = b.Multiply(x);
        for (int i = 0; i < n; i++) Assert.Equal(rhs[i], ax[i], 10);
    }

    [Fact]
    public void SparseSolver_DetectsSingularMatrix()
    {
        var b = new SparseSymmetricSolver.Builder(3);
        b.Add(0, 0, 1); b.Add(0, 1, -1); b.Add(1, 1, 1); // row 2 empty -> singular
        Assert.Null(SparseSymmetricSolver.Factor(b));
    }

    [Fact]
    public void MinimalSurface_LargeMesh_SolvesQuickly()
    {
        var grid = TestMeshes.CreateQuadGrid(50, 50, 1.0);
        var b = grid.BuildBoundaryVertexFlags();
        var verts = (Vec3d[])grid.Vertices.Clone();
        for (int i = 0; i < verts.Length; i++)
            if (b[i]) verts[i] = new Vec3d(verts[i].X, verts[i].Y, 3 * Math.Sin(verts[i].X / 8.0));
        var mesh = new MeshData(verts, grid.Faces);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = MinimalSurface.Compute(mesh, b, new MinimalSurface.Options { MaxIterations = 8 });
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        // Interior stays within the boundary's z-range (maximum principle)
        foreach (var p in r.Vertices) Assert.InRange(p.Z, -3.0001, 3.0001);
    }

    [Fact]
    public void FrameAnalysis_Cantilever_MatchesBeamTheory()
    {
        var mesh = TestMeshes.CreateQuadGrid(2, 2, 10.0);
        double L = 10, w = 0.1, t = 0.05, q = 100;
        var lath = new Vec3d[21];
        for (int i = 0; i <= 20; i++) lath[i] = new Vec3d(i * L / 20, 5, 0);
        var r = FrameAnalysis.Compute(mesh, new[] { lath }, null, new[] { new Vec3d(0, 5, 0) },
            new LathProfile(w, t, false), new Vec3d(0, 0, -q));
        double I = w * t * t * t / 12;
        double tip = q * Math.Pow(L, 4) / (8 * 11e9 * I);
        Assert.Equal(tip, r.MaxDisplacement, 1);
        Assert.Equal(1, r.SupportNodeCount);
    }

    [Fact]
    public void FrameAnalysis_LargeNet_SolvesQuickly()
    {
        var mesh = TestMeshes.CreateQuadGrid(2, 2, 10.0);
        var laths = new List<Vec3d[]>();
        var joints = new List<Vec3d>();
        var sup = new List<Vec3d>();
        int N = 30; double S = 10.0 / N;
        for (int a = 0; a <= N; a++)
        {
            var l1 = new Vec3d[N + 1]; var l2 = new Vec3d[N + 1];
            for (int i = 0; i <= N; i++) { l1[i] = new Vec3d(i * S, a * S, 0); l2[i] = new Vec3d(a * S, i * S, 0); }
            laths.Add(l1); laths.Add(l2);
        }
        for (int a = 0; a <= N; a++)
            for (int i = 0; i <= N; i++)
            {
                joints.Add(new Vec3d(i * S, a * S, 0));
                if (a == 0 || a == N || i == 0 || i == N) sup.Add(new Vec3d(i * S, a * S, 0));
            }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = FrameAnalysis.Compute(mesh, laths, joints, sup, new LathProfile(0.1, 0.05), new Vec3d(0, 0, -100));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 10000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal((N + 1) * (N + 1), r.Nodes.Length); // crossings merged into single nodes
        Assert.True(r.MaxDisplacement > 0 && r.MaxDisplacement < 1.0);
    }

    // ---------- Fabrication ----------

    [Fact]
    public void Segment_KeepsStartPoints_AndOverlaps()
    {
        var lath = new Vec3d[11];
        for (int i = 0; i <= 10; i++) lath[i] = new Vec3d(i, 0, 0);
        var r = LathSegmentation.Segment(lath, 4.0, 0.5, new double[] { 3.9, 7.5 }, overlap: 0.4);

        Assert.Equal(3, r.Segments.Count);
        Assert.Equal(0.0, r.Segments[0][0].X, 9);                    // first point kept
        Assert.Equal(10.0, r.Segments[2][^1].X, 9);
        for (int k = 1; k < r.Segments.Count; k++)
            Assert.Equal(0.4, r.Segments[k - 1][^1].X - r.Segments[k][0].X, 9); // overlap
        foreach (double cut in r.CutArcLengths)
        {
            Assert.True(Math.Abs(cut - 3.9) >= 0.5 - 1e-9 && Math.Abs(cut - 7.5) >= 0.5 - 1e-9, $"cut {cut} clashes with a joint");
        }
        foreach (var seg in r.Segments)
            Assert.True(ArcLength(seg) <= 4.0 + 1e-9, "piece fits the stock");
    }

    [Fact]
    public void JointArcLengths_FiltersFarJoints()
    {
        var lath = new[] { new Vec3d(0, 0, 0), new Vec3d(10, 0, 0) };
        var joints = new[] { new Vec3d(3, 0.01, 0), new Vec3d(6, 5, 0) };
        var near = LathSegmentation.JointArcLengths(lath, joints, 0.1);
        Assert.Single(near);
        Assert.Equal(3.0, near[0], 6);
        Assert.Equal(2, LathSegmentation.JointArcLengths(lath, joints).Length);
    }

    [Fact]
    public void Unroll_ClosedLath_OpensAtSeam()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var proj = new MeshProjection(sphere);
        var circ = new Vec3d[41];
        for (int i = 0; i <= 40; i++)
        {
            double t = 2 * Math.PI * i / 40;
            circ[i] = new Vec3d(Math.Cos(t) * Math.Sin(1.0), Math.Sin(t) * Math.Sin(1.0), Math.Cos(1.0));
        }
        var r = StripUnroll.Unroll(proj, circ, new LathProfile(0.05, 0.01));
        Assert.NotNull(r);
        Assert.Equal(41, r!.Value.EdgeA.Length);
        Assert.Equal(2 * Math.PI * Math.Sin(1.0), r.Value.Length, 1);
        // The pattern is a gently curved band, not a sliver across itself:
        // consecutive centerline points advance monotonically in x
        var cl = r.Value.Centerline;
        double dev = 0;
        for (int i = 1; i < cl.Length; i++) dev = Math.Max(dev, (cl[i] - cl[i - 1]).Length);
        Assert.True(dev < 0.2, $"max centerline step {dev}");
    }

    [Fact]
    public void Sweep_Upright_WindsOutward()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var proj = new MeshProjection(sphere);
        var circ = new Vec3d[41];
        for (int i = 0; i <= 40; i++)
        {
            double t = 2 * Math.PI * i / 40;
            circ[i] = new Vec3d(Math.Cos(t) * Math.Sin(1.0), Math.Sin(t) * Math.Sin(1.0), Math.Cos(1.0));
        }
        foreach (bool upright in new[] { false, true })
        {
            var r = StripSweep.Sweep(proj, circ, new LathProfile(0.05, 0.01, upright))!.Value;
            double vol = 0;
            foreach (var f in r.Mesh.Faces)
                for (int i = 1; i + 1 < f.Length; i++)
                    vol += Vec3d.Dot(r.Mesh.Vertices[f[0]], Vec3d.Cross(r.Mesh.Vertices[f[i]], r.Mesh.Vertices[f[i + 1]])) / 6;
            Assert.True(vol > 0, $"upright={upright}: signed volume {vol}");
        }
    }

    [Fact]
    public void NetIntersections_OneJointPerCrossing_NearVertices()
    {
        // Crossing exactly at a polyline vertex of A
        var a = new[] { new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(2, 0, 0) } };
        var b = new[] { new[] { new Vec3d(1, -1, 0), new Vec3d(1, 1, 0) } };
        var xs = NetIntersections.Find(a, b);
        Assert.Single(xs);
    }

    [Fact]
    public void NetTopology_GridOfLaths()
    {
        var famA = new List<Vec3d[]>(); var famB = new List<Vec3d[]>();
        for (int i = 1; i <= 3; i++)
        {
            famA.Add(new[] { new Vec3d(0.5, i, 0), new Vec3d(2, i, 0), new Vec3d(3.5, i, 0) });
            famB.Add(new[] { new Vec3d(i, 0.5, 0), new Vec3d(i, 2, 0), new Vec3d(i, 3.5, 0) });
        }
        var xs = NetIntersections.Find(famA, famB);
        var topo = NetTopology.Build(famA, famB, xs);
        Assert.Equal(9, topo.Nodes.Length);
        Assert.Equal(24, topo.Members.Count);
        Assert.Equal(12, topo.Members.Count(m => m.NodeStart < 0 || m.NodeEnd < 0));
        Assert.Equal(4, topo.Valence.Max());
        foreach (var m in topo.Members)
            Assert.Equal(m.NodeStart < 0 || m.NodeEnd < 0 ? 0.5 : 1.0, m.Length, 9);

        var noTails = NetTopology.Build(famA, famB, xs, minLength: 0.75);
        Assert.Equal(12, noTails.Members.Count);
        Assert.All(noTails.Members, m => Assert.True(m.NodeStart >= 0 && m.NodeEnd >= 0));
    }

    // ---------- Mesh utilities ----------

    [Fact]
    public void MeshCleanup_ReducesCollapsedQuad()
    {
        var m = new MeshData(
            new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(1, 1, 0), new Vec3d(1, 1, 0) },
            new[] { new[] { 0, 1, 2, 3 } });
        var r = MeshCleanup.Compute(m);
        Assert.Equal(1, r.Mesh.FaceCount);
        Assert.Equal(3, r.Mesh.Faces[0].Length);
        Assert.Equal(1, r.WeldedVertices);
        Assert.Equal(r.VertexMap[2], r.VertexMap[3]);
    }

    [Fact]
    public void MeshCleanup_FarFromOrigin_StillWelds()
    {
        var grid = TestMeshes.CreateQuadGrid(10, 10, 1.0);
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();
        var offset = new Vec3d(1e6, 2e6, 0);
        foreach (var f in grid.Faces)
        {
            int b = verts.Count;
            foreach (int vi in f) verts.Add(grid.Vertices[vi] + offset);
            faces.Add(new[] { b, b + 1, b + 2, b + 3 });
        }
        var r = MeshCleanup.Compute(new MeshData(verts.ToArray(), faces.ToArray()));
        Assert.Equal(grid.VertexCount, r.Mesh.VertexCount);
    }

    [Fact]
    public void KdTree_PlanarGrid_BuildsFast()
    {
        var grid = TestMeshes.CreateQuadGrid(300, 300, 1.0); // 90k coplanar vertices
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var proj = new MeshProjection(grid);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(0, proj.NearestVertexGlobal(new Vec3d(-1, -1, 0)));
    }

    [Fact]
    public void Planarization_ReportsFinalDeviation()
    {
        var grid = TestMeshes.CreateQuadGrid(6, 6, 1.0);
        var verts = (Vec3d[])grid.Vertices.Clone();
        var rng = new Random(3);
        for (int i = 0; i < verts.Length; i++) verts[i] = new Vec3d(verts[i].X, verts[i].Y, 0.2 * rng.NextDouble());
        var mesh = new MeshData(verts, grid.Faces);
        var r = Planarization.Compute(mesh, new bool[mesh.VertexCount], new Planarization.Options { MaxIterations = 3 });
        var check = Planarization.ComputeDeviation(new MeshData(r.Vertices, mesh.Faces));
        for (int f = 0; f < check.Length; f++) Assert.Equal(check[f], r.FaceDeviations[f], 12);
    }

    // ---------- New algorithms ----------

    [Fact]
    public void Isocurves_TorusParabolicLines()
    {
        var torus = TestMeshes.CreateTorus(3.0, 1.0, 64, 32);
        var K = GaussianCurvature.Compute(torus);
        var iso = MeshIsocurves.Compute(torus, K, new[] { 0.0 })[0];
        Assert.Equal(2, iso.Count);
        foreach (var c in iso)
        {
            Assert.Equal(2 * Math.PI * 3.0, ArcLength(c), 1);
            Assert.True((c[0] - c[^1]).Length < 1e-12, "parabolic line is a closed loop");
        }
    }

    [Fact]
    public void ShortestPath_SphereGreatCircle()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var proj = new MeshProjection(sphere);
        var a = new Vec3d(1, 0, 0);
        var b = new Vec3d(-0.5, 0.5, 0.7071).Normalized();
        var r = ShortestPath.Compute(proj, a, b);
        Assert.NotNull(r);
        double theory = Math.Acos(Vec3d.Dot(a, b));
        Assert.InRange(r!.Value.Length, theory * 0.99, theory * 1.02);
        Assert.True((r.Value.Points[0] - a).Length < 0.05);
        Assert.True((r.Value.Points[^1] - b).Length < 0.05);
    }

    [Fact]
    public void DynamicRelaxation_HangingNet_ConvergesSymmetric()
    {
        var grid = TestMeshes.CreateQuadGrid(20, 20, 1.0);
        var b = grid.BuildBoundaryVertexFlags();
        var r = DynamicRelaxation.Compute(grid, b,
            new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000 });
        Assert.True(r.Converged, $"residual {r.Residual}");
        double center = r.Vertices[10 * 21 + 10].Z;
        Assert.True(center < -1.0, "the net sags under gravity");
        Assert.Equal(center, r.Vertices.Min(p => p.Z), 6);                 // lowest at the centre
        Assert.Equal(r.Vertices[5 * 21 + 10].Z, r.Vertices[15 * 21 + 10].Z, 6); // symmetric
        Assert.True(r.EdgeForces.Max() > 0, "edges are in tension");
    }

    [Fact]
    public void CurveFairing_DoesNotShrinkClosedLoops()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var proj = new MeshProjection(sphere);
        var circ = new Vec3d[81];
        for (int i = 0; i <= 80; i++)
        {
            double t = 2 * Math.PI * i / 80;
            circ[i] = new Vec3d(Math.Cos(t) * Math.Sin(0.8), Math.Sin(t) * Math.Sin(0.8), Math.Cos(0.8));
        }
        var faired = CurveFairing.SmoothOnSurface(proj, circ, 30);
        Assert.Equal(ArcLength(circ), ArcLength(faired), 1);
        Assert.True((faired[0] - faired[^1]).Length < 1e-12);
    }

    [Fact]
    public void TraceDefaults_ScaleWithTheMesh()
    {
        var small = TestMeshes.CreateSaddle(20, 2.0);
        var big = new MeshData(small.Vertices.Select(v => 1000.0 * v).ToArray(), small.Faces);
        var ps = new MeshProjection(small);
        var pb = new MeshProjection(big);
        Assert.Equal(1000.0, TraceDefaults.ResolveStep(0, 0, pb) / TraceDefaults.ResolveStep(0, 0, ps), 6);
        Assert.Equal(1000.0, TraceDefaults.ResolveSpacing(0, pb) / TraceDefaults.ResolveSpacing(0, ps), 6);
        int stepsSmall = TraceDefaults.ResolveMaxSteps(0, TraceDefaults.ResolveStep(0, 0, ps), ps);
        int stepsBig = TraceDefaults.ResolveMaxSteps(0, TraceDefaults.ResolveStep(0, 0, pb), pb);
        Assert.Equal(stepsSmall, stepsBig);

        // A millimetre-scale saddle traces full-length asymptotic curves with the defaults
        var pc = PrincipalCurvature.Compute(big);
        var field = AsymptoticCurves.ComputeDirections(pc);
        var curves = EvenlySpacedNet.TraceField(big, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options(), field.Family2);
        Assert.True(curves.Count >= 5, $"{curves.Count} curves");
        Assert.True(curves.Max(ArcLength) > 500, "curves span the mesh");
    }
}
