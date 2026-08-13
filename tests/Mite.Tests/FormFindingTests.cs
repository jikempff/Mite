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
            new MinimalSurface.Options { MaxIterations = 50 });

        Assert.True(result.Iterations > 0);

        // On a white-noise-perturbed mesh the vertex-movement residual has a
        // slow tail (in-plane reparametrization), so assert on what a minimal
        // surface actually is: a small normalized discrete Laplacian
        double before = NormalizedLaplacianResidual(perturbed, perturbed.Vertices, fixedVerts);
        double after = NormalizedLaplacianResidual(perturbed, result.Vertices, fixedVerts);
        Assert.True(after < 0.1 * before,
            $"Mean-curvature residual should drop by at least 10x: {before:E3} -> {after:E3}");
    }

    /// <summary>Max over free vertices of |L x| / (sum of weights), clamped cotangent weights.</summary>
    private static double NormalizedLaplacianResidual(MeshData mesh, Vec3d[] verts, bool[] fixedVerts)
    {
        var lap = new Vec3d[verts.Length];
        var wsum = new double[verts.Length];
        foreach (var f in mesh.Faces)
        {
            for (int j = 0; j < 3; j++)
            {
                int ia = f[(j + 1) % 3], ib = f[(j + 2) % 3], ic = f[j];
                Vec3d e1 = verts[ia] - verts[ic], e2 = verts[ib] - verts[ic];
                double l1 = e1.Length, l2 = e2.Length;
                if (l1 < 1e-15 || l2 < 1e-15) continue;
                double cosA = Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(e1, e2) / (l1 * l2)));
                double sinA = Math.Sqrt(Math.Max(0.0, 1.0 - cosA * cosA));
                if (sinA < 1e-15) continue;
                double w = Math.Max(cosA / sinA, 1e-4);
                lap[ia] += w * (verts[ib] - verts[ia]);
                lap[ib] -= w * (verts[ib] - verts[ia]);
                wsum[ia] += w;
                wsum[ib] += w;
            }
        }
        double max = 0;
        for (int i = 0; i < verts.Length; i++)
            if (!fixedVerts[i] && wsum[i] > 0)
                max = Math.Max(max, (lap[i] / wsum[i]).Length);
        return max;
    }

    [Fact]
    public void MinimalSurface_FlatBoundary_FlattensInterior()
    {
        // Boundary pinned in the z=0 plane: the minimal surface is the plane,
        // so a perturbed interior must flatten exactly
        var grid = TestMeshes.CreateQuadGrid(10, 10);
        var triGrid = grid.ToTriangulated();

        var verts = new Vec3d[triGrid.VertexCount];
        Array.Copy(triGrid.Vertices, verts, triGrid.VertexCount);
        var rng = new Random(42);
        int nx = 11;
        for (int i = 0; i < verts.Length; i++)
        {
            int x = i % nx, y = i / nx;
            bool boundary = x == 0 || x == nx - 1 || y == 0 || y == nx - 1;
            if (!boundary)
                verts[i] = new Vec3d(verts[i].X, verts[i].Y, rng.NextDouble() * 0.5);
        }
        var perturbed = new MeshData(verts, triGrid.Faces);

        var fixedVerts = new bool[perturbed.VertexCount];
        for (int i = 0; i < perturbed.VertexCount; i++)
        {
            int x = i % nx, y = i / nx;
            if (x == 0 || x == nx - 1 || y == 0 || y == nx - 1)
                fixedVerts[i] = true;
        }

        var result = MinimalSurface.Compute(perturbed, fixedVerts);

        for (int i = 0; i < result.Vertices.Length; i++)
        {
            if (!fixedVerts[i])
                Assert.True(Math.Abs(result.Vertices[i].Z) < 1e-9,
                    $"Interior vertex {i} should flatten to z=0, got {result.Vertices[i].Z:E3}");
        }
    }

    [Fact]
    public void MinimalSurface_NoFixedVertices_Throws()
    {
        var grid = TestMeshes.CreateQuadGrid(3, 3).ToTriangulated();
        Assert.Throws<ArgumentException>(() =>
            MinimalSurface.Compute(grid, new bool[grid.VertexCount]));
    }

    [Fact]
    public void ForceDensityMethod_NoFixedVertices_Throws()
    {
        var grid = TestMeshes.CreateQuadGrid(3, 3).ToTriangulated();
        var edges = grid.BuildEdges();
        var q = new double[edges.Length];
        Array.Fill(q, 1.0);
        Assert.Throws<ArgumentException>(() =>
            ForceDensityMethod.Compute(grid, q, new Vec3d[grid.VertexCount], new bool[grid.VertexCount]));
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
