using System;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;

namespace Mite.Tests;

public class CurvatureTests
{
    [Fact]
    public void GaussianCurvature_Sphere_ReturnsPositive()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var K = GaussianCurvature.Compute(sphere);

        int interiorCount = 0;
        double sum = 0;
        for (int i = 0; i < K.Length; i++)
        {
            if (Math.Abs(K[i]) > 0.01)
            {
                Assert.True(K[i] > 0, $"Gaussian curvature should be positive on sphere, got {K[i]} at vertex {i}");
                interiorCount++;
                sum += K[i];
            }
        }
        Assert.True(interiorCount > sphere.VertexCount / 2, "Most vertices should have measurable curvature");
    }

    [Fact]
    public void GaussianCurvature_Sphere_ApproximatesAnalytic()
    {
        // K = 1/r^2 = 1.0 for unit sphere
        var sphere = TestMeshes.CreateUnitSphere(64);
        var K = GaussianCurvature.Compute(sphere);

        // Check interior vertices (skip poles which have discretization artifacts)
        int goodCount = 0;
        for (int i = 1; i < K.Length - 1; i++)
        {
            if (Math.Abs(K[i] - 1.0) < 0.3)
                goodCount++;
        }
        double ratio = (double)goodCount / (K.Length - 2);
        Assert.True(ratio > 0.7, $"At least 70% of vertices should approximate K=1, got {ratio:P0}");
    }

    [Fact]
    public void MeanCurvature_Sphere_ReturnsPositive()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var result = MeanCurvature.Compute(sphere);

        int positiveCount = 0;
        for (int i = 0; i < result.Values.Length; i++)
        {
            if (result.Values[i] > 0.1) positiveCount++;
        }
        Assert.True(positiveCount > sphere.VertexCount / 2, "Most mean curvature values should be positive on sphere");
    }

    [Fact]
    public void PrincipalCurvature_Sphere_K1EqualsK2()
    {
        // On a sphere, k1 = k2 = 1/r = 1.0
        var sphere = TestMeshes.CreateUnitSphere(32);
        var result = PrincipalCurvature.Compute(sphere);

        int goodCount = 0;
        for (int i = 1; i < result.K1.Length - 1; i++)
        {
            double diff = Math.Abs(result.K1[i] - result.K2[i]);
            if (diff < 0.5) goodCount++;
        }
        double ratio = (double)goodCount / (result.K1.Length - 2);
        Assert.True(ratio > 0.6, $"On a sphere, k1 ≈ k2 for most vertices, got {ratio:P0} within tolerance");
    }

    [Fact]
    public void PrincipalCurvature_DirectionsAreOrthogonal()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var result = PrincipalCurvature.Compute(sphere);

        for (int i = 0; i < result.D1.Length; i++)
        {
            if (result.D1[i].LengthSquared < 0.01 || result.D2[i].LengthSquared < 0.01) continue;
            double dot = Math.Abs(Vec3d.Dot(result.D1[i], result.D2[i]));
            Assert.True(dot < 0.3, $"Principal directions should be roughly orthogonal, got dot={dot} at vertex {i}");
        }
    }

    [Fact]
    public void GaussianCurvature_FlatGrid_InteriorIsZero()
    {
        int nx = 10, ny = 10;
        var grid = TestMeshes.CreateQuadGrid(nx, ny);
        var triGrid = grid.ToTriangulated();
        var K = GaussianCurvature.Compute(new MeshData(triGrid.Vertices, triGrid.Faces));

        for (int i = 0; i < K.Length; i++)
        {
            int x = i % (nx + 1), y = i / (nx + 1);
            bool isBoundary = x == 0 || x == nx || y == 0 || y == ny;
            if (isBoundary) continue;
            Assert.True(Math.Abs(K[i]) < 0.1, $"Interior flat grid vertex should have K≈0, got {K[i]} at vertex {i}");
        }
    }

    [Fact]
    public void Torus_GaussianCurvature_HasBothSigns()
    {
        var torus = TestMeshes.CreateTorus(3.0, 1.0, 64, 32);
        var K = GaussianCurvature.Compute(torus);

        bool hasPositive = false, hasNegative = false;
        for (int i = 0; i < K.Length; i++)
        {
            if (K[i] > 0.01) hasPositive = true;
            if (K[i] < -0.01) hasNegative = true;
        }
        Assert.True(hasPositive, "Torus outer ring should have positive Gaussian curvature");
        Assert.True(hasNegative, "Torus inner ring should have negative Gaussian curvature");
    }
}
