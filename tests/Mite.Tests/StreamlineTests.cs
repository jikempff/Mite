using System;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Streamlines;

namespace Mite.Tests;

public class StreamlineTests
{
    [Fact]
    public void Streamlines_Sphere_StayOnSurface()
    {
        var sphere = TestMeshes.CreateUnitSphere(32);
        var curvature = PrincipalCurvature.Compute(sphere);
        var opts = new CurvatureStreamlines.Options { StepSize = 0.05, MaxSteps = 200 };

        int seed = sphere.VertexCount / 2; // equator-ish
        var lines = CurvatureStreamlines.Trace(sphere, new[] { seed }, curvature, opts);

        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            Assert.True(line.Length > 5, "Streamline should make progress");
            foreach (var p in line)
            {
                double r = p.Length;
                Assert.True(Math.Abs(r - 1.0) < 0.05,
                    $"Streamline point should stay on the unit sphere, got radius {r:F4}");
            }
        }
    }

    [Fact]
    public void Streamlines_InvalidSeeds_AreSkippedWithoutCrashing()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        var curvature = PrincipalCurvature.Compute(sphere);

        var lines = CurvatureStreamlines.Trace(
            sphere, new[] { -5, sphere.VertexCount + 100 }, curvature);

        Assert.Empty(lines);
    }

    [Fact]
    public void Streamlines_ManySeeds_CompleteQuickly()
    {
        // Regression: adjacency was rebuilt on every integration substep,
        // making moderate meshes hang Grasshopper
        var sphere = TestMeshes.CreateUnitSphere(48);
        var curvature = PrincipalCurvature.Compute(sphere);
        var opts = new CurvatureStreamlines.Options { StepSize = 0.02, MaxSteps = 500 };

        var seeds = new int[20];
        for (int i = 0; i < seeds.Length; i++)
            seeds[i] = 1 + i * (sphere.VertexCount - 2) / seeds.Length;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lines = CurvatureStreamlines.Trace(sphere, seeds, curvature, opts);
        sw.Stop();

        Assert.NotEmpty(lines);
        Assert.True(sw.ElapsedMilliseconds < 10_000,
            $"20 streamlines on a 4.6k-vertex mesh should be fast, took {sw.ElapsedMilliseconds}ms");
    }
}
