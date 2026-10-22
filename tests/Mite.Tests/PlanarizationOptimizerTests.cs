using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;

namespace Mite.Tests;

/// <summary>
/// Guided-projection planarizer (Tang et al. 2014; Liu et al. 2006;
/// Zadravec et al. 2010) against meshes whose planarity is known exactly.
/// </summary>
public class PlanarizationOptimizerTests
{
    private readonly ITestOutputHelper _out;
    public PlanarizationOptimizerTests(ITestOutputHelper output) { _out = output; }

    private static double MaxMove(MeshData a, Vec3d[] b) => a.Vertices.Zip(b, (p, q) => (p - q).Length).Max();

    // ---------- tests ----------

    [Fact]
    public void TranslationalGrid_IsFixedPoint()
    {
        var m = TestMeshes.TranslationalGrid();
        Assert.True(Planarization.ComputeDeviation(m).Max() < 1e-12);
        var r = PlanarizationOptimizer.Optimize(m, null, null, new PlanarizationOptimizer.Options { Iterations = 10 });
        _out.WriteLine($"translational: iterations {r.Iterations}, max move {r.MaxDisplacement:E2}, deviation {r.MaxDeviation:E2}");
        Assert.True(r.Converged);
        Assert.True(r.MaxDisplacement < 1e-12, $"a planar mesh must not move: {r.MaxDisplacement:E3}");
        Assert.Equal(m.FaceCount, r.PolygonCount);
    }

    [Fact]
    public void HyperboloidRevolve_IsFixedPoint()
    {
        var m = TestMeshes.HyperboloidQuadRevolve();
        Assert.True(Planarization.ComputeDeviation(m).Max() < 1e-12);
        var r = PlanarizationOptimizer.Optimize(m, null);
        Assert.True(r.MaxDisplacement < 1e-12, $"planar trapezoids must not move: {r.MaxDisplacement:E3}");
        // the auxiliary normals are the face normals, outward on a surface of revolution
        for (int f = 0; f < m.FaceCount; f++)
        {
            var c = Vec3d.Zero; foreach (int i in m.Faces[f]) c += m.Vertices[i]; c /= 4;
            Assert.True(Math.Abs(Vec3d.Dot(r.FaceNormals[f], new Vec3d(c.X, c.Y, 0).Normalized())) > 0.6);
            Assert.True(Math.Abs(r.FaceNormals[f].Length - 1) < 1e-12);
        }
    }

    [Fact]
    public void NoisyGrid_WithPlaneReference_ReturnsToThePlane()
    {
        // the planar grid is the unique zero of planarity + closeness, so the result must be exactly flat
        var grid = TestMeshes.CreateQuadGrid(12, 12, 2.0);
        var noisy = TestMeshes.Jitter(grid, 0.1, 5);
        var border = grid.BuildBoundaryVertexFlags();
        double dev0 = Planarization.ComputeDeviation(noisy).Max();
        var r = PlanarizationOptimizer.Optimize(noisy, border, grid, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-10 });
        double maxZ = r.Vertices.Max(p => Math.Abs(p.Z));
        _out.WriteLine($"noisy grid: deviation {dev0:F4} → {r.MaxDeviation:E2} in {r.Iterations} iterations, max |z| {maxZ:E2}, max move {r.MaxDisplacement:F4}");
        Assert.True(r.Converged);
        Assert.True(maxZ < 1e-8, $"max |z| {maxZ:E3}");
        for (int i = 0; i < grid.VertexCount; i++)
            if (border[i]) Assert.Equal(noisy.Vertices[i], r.Vertices[i]); // fixed vertices do not move at all
        // the projection method does not get there
        var p = Planarization.Compute(noisy, border, new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-10 });
        _out.WriteLine($"projection: {p.FaceDeviations.Max():E2} after {p.Iterations} iterations, max |z| {p.Vertices.Max(q => Math.Abs(q.Z)):E2}");
        Assert.True(p.FaceDeviations.Max() > 1e-6);
    }

    [Fact]
    public void PerturbedConjugateNet_PlanarizesBackOntoTheSurface()
    {
        // meridians × parallels of the hyperboloid, every node jittered by up to 0.02 in each coordinate
        var exact = TestMeshes.HyperboloidQuadRevolve(32, 10, 1.0);
        var noisy = TestMeshes.Jitter(exact, 0.02, 7, interiorOnly: false);
        var reference = TestMeshes.CreateHyperboloid(1, 1, 1.3, 96, 48);
        double dev0 = Planarization.ComputeDeviation(noisy).Max();
        var r = PlanarizationOptimizer.Optimize(noisy, null, reference, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-9 });
        _out.WriteLine($"perturbed conjugate net: deviation {dev0:F4} → {r.MaxDeviation:E2} in {r.Iterations} iterations; moved max {r.MaxDisplacement:F4}; off the surface max {r.MaxSurfaceDistance:E3} mean {r.MeanSurfaceDistance:E3}; from the exact mesh {MaxMove(exact, r.Vertices):F4}");
        Assert.True(r.Converged && r.Iterations <= 20);
        Assert.True(r.MaxSurfaceDistance < 0.02, $"off the surface {r.MaxSurfaceDistance:E3} (the input was knocked 0.02 off)");
        Assert.True(r.MaxDisplacement < 0.06, $"moved {r.MaxDisplacement:F4}");
        var p = Planarization.Compute(noisy, new bool[exact.VertexCount], new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-9 });
        var refProj = new MeshProjection(reference);
        double off = p.Vertices.Max(q => (refProj.ClosestPointGlobal(q).Point - q).Length);
        _out.WriteLine($"projection: {p.FaceDeviations.Max():E2} after {p.Iterations} iterations, off the surface {off:E3}");
        Assert.True(off > r.MaxSurfaceDistance, "the projection loop leaves the mesh farther from the surface");
    }

    [Fact]
    public void HyperboloidRulingNet_HasNoPlanarRealisationOnTheSurface()
    {
        // the rulings are the asymptotic curves: self-conjugate, so no planar quad net with this combinatorics
        // lies on the hyperboloid (Pottmann et al. 2007 §3.4). The optimiser either stalls with its quads intact
        // (the fold guard refuses the folded quads that are the only planar meshes close by) or finds a planar
        // mesh well off the surface, an edge length away; it never hands back a folded quad, and it says which.
        var m = TestMeshes.HyperboloidRulingNet(24, 1.0, out int rows);
        var dev0 = Planarization.ComputeDeviation(m);
        var reference = TestMeshes.CreateHyperboloid(1, 1, 1.3, 96, 48);
        var r = PlanarizationOptimizer.Optimize(m, null, reference, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-9 });
        var (minEdge0, minArea0) = Smallest(m.Vertices, m.Faces);
        var (minEdge, minArea) = Smallest(r.Vertices, m.Faces);
        _out.WriteLine($"ruling net {m.VertexCount} nodes / {m.FaceCount} quads / {rows} rows, mean edge {r.MeanEdge:F4}: deviation max {dev0.Max():F4} mean {dev0.Average():F4} → {r.MaxDeviation:E2} after {r.Iterations} iterations (converged {r.Converged}, stalled {r.Stalled}); moved max {r.MaxDisplacement:F4} mean {r.MeanDisplacement:F4}; off the surface max {r.MaxSurfaceDistance:F4} mean {r.MeanSurfaceDistance:F4}; shortest edge {minEdge0:F4} → {minEdge:F4}, smallest area {minArea0:F4} → {minArea:F4}");
        Assert.True(r.Converged != r.Stalled, "either planar or stalled, never silent");
        if (r.Converged)
            Assert.True(r.MaxSurfaceDistance > 0.05, $"planar only well off the surface: {r.MaxSurfaceDistance:F3} (a conjugate net knocked 0.02 off ends within 0.015)");
        else
            Assert.True(r.MaxDeviation < 0.8 * dev0.Max());
        Assert.True(minArea >= 0.25 * minArea0 - 1e-12, "the fold guard keeps every quad above a quarter of its area");
        Assert.True(r.MaxDisplacement > 0.5 * r.MeanEdge, "the nodes have moved a good part of an edge");
        // the projection loop: 2000 iterations, still not planar, nodes moved twice as far
        var p = Planarization.Compute(m, new bool[m.VertexCount], new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-9 });
        var (pe, pa) = Smallest(p.Vertices, m.Faces);
        _out.WriteLine($"projection: {p.FaceDeviations.Max():E2} after {p.Iterations} iterations, moved max {MaxMove(m, p.Vertices):F4}, shortest edge {pe:F4}, smallest area {pa:F4}");
        Assert.True(p.FaceDeviations.Max() > 1e-6);
    }

    private static (double minEdge, double minArea) Smallest(Vec3d[] v, int[][] faces)
    {
        double me = double.MaxValue, ma = double.MaxValue;
        foreach (var f in faces)
        {
            for (int e = 0; e < f.Length; e++) me = Math.Min(me, (v[f[e]] - v[f[(e + 1) % f.Length]]).Length);
            ma = Math.Min(ma, Area(v, f));
        }
        return (me, ma);
    }

    private static double Area(Vec3d[] v, int[] f)
    {
        var n = Vec3d.Zero;
        for (int i = 0; i < f.Length; i++) n += Vec3d.Cross(v[f[i]], v[f[(i + 1) % f.Length]]);
        return 0.5 * n.Length;
    }

    [Fact]
    public void ScaleAndPositionInvariant()
    {
        // the same mesh in millimetres, 2 km from the origin: the result is the same to round-off (bounding-box normalisation)
        var noisy = TestMeshes.Jitter(TestMeshes.HyperboloidQuadRevolve(16, 6, 1.0), 0.02, 11, interiorOnly: false);
        var reference = TestMeshes.CreateHyperboloid(1, 1, 1.3, 64, 32);
        var opts = new PlanarizationOptimizer.Options { Iterations = 20, Tolerance = 1e-9 };
        var r1 = PlanarizationOptimizer.Optimize(noisy, null, reference, opts);
        var off = new Vec3d(2000e3, -1200e3, 500e3);
        Vec3d[] S(Vec3d[] a) => a.Select(q => q * 1000 + off).ToArray();
        var r2 = PlanarizationOptimizer.Optimize(new MeshData(S(noisy.Vertices), noisy.Faces), null, new MeshData(S(reference.Vertices), reference.Faces),
            new PlanarizationOptimizer.Options { Iterations = 20, Tolerance = 1e-6 });
        double diff = 0;
        for (int i = 0; i < noisy.VertexCount; i++) diff = Math.Max(diff, ((r2.Vertices[i] - off) / 1000 - r1.Vertices[i]).Length);
        _out.WriteLine($"scaled copy: iterations {r1.Iterations} / {r2.Iterations}, largest difference {diff:E2}");
        Assert.True(r1.Converged && r2.Converged);
        Assert.True(diff < 1e-7, $"difference {diff:E3}");
    }

    [Fact]
    public void QuadDominant_TrianglesAreFreeAndPolygonsPlanarize()
    {
        // a jittered grid with one quad split into two triangles and two quads merged into a hexagon
        var grid = TestMeshes.CreateQuadGrid(6, 6, 2.0);
        var faces = grid.Faces.ToList();
        var q0 = faces[14]; faces.RemoveAt(14);
        faces.Add(new[] { q0[0], q0[1], q0[2] }); faces.Add(new[] { q0[0], q0[2], q0[3] });
        var qa = faces[20]; var qb = faces[21]; // neighbours in a row: qa = (a, a+1, a+8, a+7), qb = (a+1, a+2, a+9, a+8)
        faces.RemoveAt(21); faces.RemoveAt(20);
        faces.Add(new[] { qa[0], qa[1], qb[1], qb[2], qb[3], qa[3] });
        var mesh = TestMeshes.Jitter(new MeshData(grid.Vertices, faces.ToArray()), 0.08, 3);
        var r = PlanarizationOptimizer.Optimize(mesh, mesh.BuildBoundaryVertexFlags(), null, new PlanarizationOptimizer.Options { Tolerance = 1e-9 });
        _out.WriteLine($"quad-dominant: {r.PolygonCount} polygons, deviation {r.MaxDeviation:E2} in {r.Iterations} iterations, moved {r.MaxDisplacement:F4}");
        Assert.Equal(faces.Count - 2, r.PolygonCount);
        Assert.True(r.Converged);
        int hex = faces.Count - 1;
        Assert.True(r.FaceDeviations[hex] < 1e-9, "the hexagon is planar too");
        Assert.Equal(0.0, r.FaceDeviations[hex - 1]); // triangles report 0
    }

    [Fact]
    public void Fairness_PairsOppositeEdgesAlongPolylines()
    {
        var grid = TestMeshes.CreateQuadGrid(5, 5, 1.0); // 6 × 6 vertices
        var triples = new List<(int a, int v, int c)>();
        var stars = new List<int[]>();
        PlanarizationOptimizer.BuildFairness(grid, triples, stars);
        // interior 4 × 4 vertices: 2 triples each; boundary non-corner 4 × 4: 1 each; corners: none
        Assert.Equal(16 * 2 + 16, triples.Count);
        Assert.Empty(stars);
        foreach (var (a, v, c) in triples)
        {
            var d1 = grid.Vertices[a] - grid.Vertices[v]; var d2 = grid.Vertices[c] - grid.Vertices[v];
            Assert.True((d1 + d2).Length < 1e-12, "opposite edges continue each other on the grid");
        }
        // an irregular vertex (valence 3 at the apex of a triangle fan) gets a Laplacian star
        var fan = TestMeshes.CreateSaddleFan(3, 0.4);
        triples.Clear(); stars.Clear();
        PlanarizationOptimizer.BuildFairness(fan, triples, stars);
        Assert.True(stars.Count >= 1);
    }

    [Fact]
    public void Options_ConstantWeightsCompromise_DecayReachesPlanarity()
    {
        // Tang et al. 2014 keep the weights (a compromise between planarity and closeness at the fixed point);
        // Liu et al. 2006 let them decay (planarity exact). Both behaviours are exposed through Decay.
        var grid = TestMeshes.CreateQuadGrid(10, 10, 2.0);
        var noisy = TestMeshes.Jitter(grid, 0.1, 9);
        var border = grid.BuildBoundaryVertexFlags();
        var constant = PlanarizationOptimizer.Optimize(noisy, border, grid, new PlanarizationOptimizer.Options { Iterations = 25, Tolerance = 1e-10, Decay = 1.0 });
        var decayed = PlanarizationOptimizer.Optimize(noisy, border, grid, new PlanarizationOptimizer.Options { Iterations = 25, Tolerance = 1e-10, Decay = 0.5 });
        _out.WriteLine($"constant weights: deviation {constant.MaxDeviation:E2}, off {constant.MaxSurfaceDistance:E2}; decayed: {decayed.MaxDeviation:E2}, off {decayed.MaxSurfaceDistance:E2}");
        Assert.True(decayed.Converged && !constant.Converged);
        Assert.True(constant.MaxDeviation > 1e-5 && constant.MaxDeviation < 0.1);
    }

    [Fact]
    public void LargerMesh_ConvergesInFewIterations()
    {
        // 1 600 nodes, 1 536 quads: the Gauss–Newton count stays at a handful; the time is the envelope factorisation
        var exact = TestMeshes.HyperboloidQuadRevolve(64, 24, 1.0);
        var noisy = TestMeshes.Jitter(exact, 0.01, 7, interiorOnly: false);
        var reference = TestMeshes.CreateHyperboloid(1, 1, 1.3, 96, 48);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = PlanarizationOptimizer.Optimize(noisy, null, reference, new PlanarizationOptimizer.Options { Iterations = 30, Tolerance = 1e-8 });
        sw.Stop();
        _out.WriteLine($"{noisy.VertexCount} vertices / {noisy.FaceCount} quads: {r.Iterations} iterations, {sw.ElapsedMilliseconds} ms, deviation {r.MaxDeviation:E1}, off the surface {r.MaxSurfaceDistance:E2}");
        Assert.True(r.Converged && r.Iterations <= 15);
        Assert.True(r.MaxSurfaceDistance < 0.01);
    }

    [Fact]
    public void BumpyGrid_NoReference_NoFixed_StillPlanarizes()
    {
        // the component's defaults: the mesh is its own reference and nothing is held — the smoke test's grid
        var rng = new Random(42);
        int nx = 10, ny = 10;
        var v = new Vec3d[(nx + 1) * (ny + 1)];
        for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) v[j * (nx + 1) + i] = new Vec3d(i, j, rng.NextDouble() * 0.3);
        var mesh = new MeshData(v, TestMeshes.GridFaces(nx, ny));
        double dev0 = Planarization.ComputeDeviation(mesh).Max();
        var r = PlanarizationOptimizer.Optimize(mesh, null, null, new PlanarizationOptimizer.Options { Iterations = 50, Tolerance = 1e-6 });
        _out.WriteLine($"bumpy grid: deviation {dev0:F3} → {r.MaxDeviation:E2} in {r.Iterations} iterations, moved max {r.MaxDisplacement:F3} mean {r.MeanDisplacement:F3}, off its own surface {r.MaxSurfaceDistance:F3}");
        Assert.True(r.Converged && r.Iterations <= 30, $"{r.Iterations} iterations, {r.MaxDeviation:E2}");
        Assert.True(r.MaxDisplacement < 0.3, "moves less than the bump amplitude");
    }
}
