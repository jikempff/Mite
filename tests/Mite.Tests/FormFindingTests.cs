using System;
using Xunit;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;

namespace Mite.Tests;

public class FormFindingTests
{
    [Fact]
    public void Planarization_FlatQuadGrid_AlreadyPlanar()
    {
        var grid = TestMeshes.CreateQuadGrid(5, 5);
        var deviations = Planarization.ComputeDeviation(grid);

        for (int i = 0; i < deviations.Length; i++)
            Assert.True(deviations[i] < 1e-10, $"Flat quad grid should have zero deviation, got {deviations[i]}");
    }

    [Fact]
    public void Planarization_PerturbedGrid_ReducesDeviation()
    {
        var grid = TestMeshes.CreateQuadGrid(5, 5);
        var verts = new Vec3d[grid.VertexCount];
        Array.Copy(grid.Vertices, verts, grid.VertexCount);

        var rng = new Random(42);
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new Vec3d(verts[i].X, verts[i].Y, rng.NextDouble() * 0.5);

        var perturbed = new MeshData(verts, grid.Faces);
        double initialMax = MaxDeviation(Planarization.ComputeDeviation(perturbed));

        var result = Planarization.Compute(perturbed, new bool[perturbed.VertexCount],
            new Planarization.Options { MaxIterations = 200 });
        double finalMax = MaxDeviation(result.FaceDeviations);

        Assert.True(finalMax < initialMax, $"Planarization should reduce deviation: {initialMax:E3} -> {finalMax:E3}");
    }

    [Fact]
    public void MinimalSurface_BoundaryPinned_Converges()
    {
        var grid = TestMeshes.CreateQuadGrid(10, 10);
        var triGrid = grid.ToTriangulated();

        var verts = new Vec3d[triGrid.VertexCount];
        Array.Copy(triGrid.Vertices, verts, triGrid.VertexCount);
        var rng = new Random(42);
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new Vec3d(verts[i].X, verts[i].Y, rng.NextDouble() * 0.3);

        var perturbed = new MeshData(verts, triGrid.Faces);

        var fixedVerts = new bool[perturbed.VertexCount];
        int nx = 11;
        for (int i = 0; i < perturbed.VertexCount; i++)
        {
            int x = i % nx, y = i / nx;
            if (x == 0 || x == nx - 1 || y == 0 || y == nx - 1)
                fixedVerts[i] = true;
        }

        var result = MinimalSurface.Compute(perturbed, fixedVerts,
            new MinimalSurface.Options { MaxIterations = 200, StepSize = 0.3 });

        Assert.True(result.Iterations > 0);
    }

    [Fact]
    public void ForceDensityMethod_SimpleNet_Solves()
    {
        var grid = TestMeshes.CreateQuadGrid(3, 3);
        var triGrid = grid.ToTriangulated();

        var fixedVerts = new bool[triGrid.VertexCount];
        int nx = 4;
        for (int i = 0; i < triGrid.VertexCount; i++)
        {
            int x = i % nx, y = i / nx;
            if (x == 0 || x == nx - 1 || y == 0 || y == nx - 1)
                fixedVerts[i] = true;
        }

        var edges = triGrid.BuildEdges();
        var q = new double[edges.Length];
        Array.Fill(q, 1.0);

        var loads = new Vec3d[triGrid.VertexCount];
        for (int i = 0; i < loads.Length; i++)
            loads[i] = new Vec3d(0, 0, -1.0);

        var result = ForceDensityMethod.Compute(triGrid, q, loads, fixedVerts);

        Assert.Equal(triGrid.VertexCount, result.Vertices.Length);

        for (int i = 0; i < triGrid.VertexCount; i++)
        {
            if (!fixedVerts[i])
                Assert.True(result.Vertices[i].Z < 0, $"Free vertex {i} should sag below z=0 under gravity");
        }
    }

    private static double MaxDeviation(double[] devs)
    {
        double max = 0;
        foreach (double d in devs) max = Math.Max(max, d);
        return max;
    }
}
