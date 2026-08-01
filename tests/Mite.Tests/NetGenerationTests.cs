using System;
using System.Collections.Generic;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

public class NetGenerationTests
{
    [Fact]
    public void Chebyshev_Plane_RegularNet()
    {
        // On a plane the Chebyshev net degenerates to a regular grid:
        // every node valid, every edge exactly EdgeLength, everything at z = 0
        var grid = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        int center = 5 * 11 + 5;

        var opts = new ChebyshevNet.Options { EdgeLength = 0.4, CountU = 4, CountV = 4 };
        var net = ChebyshevNet.Compute(grid, center, new Vec3d(1, 0, 0), opts);

        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                Assert.True(net.Valid[i, j], $"Node ({i},{j}) should be valid on a plane");
                Assert.True(Math.Abs(net.Points[i, j].Z) < 1e-6, "Net on a plane should stay at z = 0");
            }

        AssertEdgeLengths(net, 0.4, 0.02);
    }

    [Fact]
    public void Chebyshev_Sphere_PreservesEdgeLength()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        int equatorVertex = 1 + 11 * 48;

        var opts = new ChebyshevNet.Options { EdgeLength = 0.15, CountU = 5, CountV = 5 };
        var net = ChebyshevNet.Compute(sphere, equatorVertex, new Vec3d(0, 1, 0), opts);

        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        int validCount = 0;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                if (!net.Valid[i, j]) continue;
                validCount++;
                double r = net.Points[i, j].Length;
                Assert.True(r > 0.95 && r < 1.03,
                    $"Net node should lie on the unit sphere, radius {r:F4}");
            }

        Assert.True(validCount > 0.9 * nu * nv,
            $"Most of the net should be valid on a sphere, got {validCount}/{nu * nv}");

        AssertEdgeLengths(net, 0.15, 0.1);
    }

    [Fact]
    public void EvenlySpaced_Geodesics_FillSphere()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        int equatorVertex = 1 + 11 * 48;

        var opts = new EvenlySpacedNet.Options
        {
            Spacing = 0.4,
            StepSize = 0.05,
            MaxSteps = 140,
            MaxCurves = 60
        };
        var curves = EvenlySpacedNet.TraceGeodesics(sphere, equatorVertex, new Vec3d(0, 0, 1), opts);

        Assert.True(curves.Count >= 5, $"Auto-spacing should fill the sphere, got {curves.Count} curves");
        Assert.True(curves.Count <= 60, "Curve count should respect MaxCurves");

        foreach (var line in curves)
        {
            Assert.True(line.Length > 5, "Each curve should have several points");
            foreach (var p in line)
                Assert.True(p.Length > 0.9 && p.Length < 1.05,
                    $"Curves should stay on the sphere, radius {p.Length:F4}");
        }
    }

    [Fact]
    public void EvenlySpaced_AsymptoticField_FillsSaddle()
    {
        var saddle = TestMeshes.CreateSaddle(20, 2.0);
        var curvature = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(curvature);

        var opts = new EvenlySpacedNet.Options
        {
            Spacing = 0.3,
            StepSize = 0.05,
            MaxSteps = 200,
            MaxCurves = 40
        };
        var curves = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts);

        Assert.True(curves.Count >= 2, $"Auto-spacing should place multiple curves, got {curves.Count}");

        foreach (var line in curves)
            foreach (var p in line)
                Assert.True(Math.Abs(p.Z - (p.X * p.X - p.Y * p.Y)) < 0.15,
                    "Curves should stay on the saddle surface");
    }

    private static void AssertEdgeLengths(ChebyshevNet.Result net, double L, double relTol)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                if (!net.Valid[i, j]) continue;
                if (i + 1 < nu && net.Valid[i + 1, j])
                {
                    double e = (net.Points[i + 1, j] - net.Points[i, j]).Length;
                    Assert.True(Math.Abs(e - L) < relTol * L,
                        $"U edge at ({i},{j}) should be ~{L}, got {e:F4}");
                }
                if (j + 1 < nv && net.Valid[i, j + 1])
                {
                    double e = (net.Points[i, j + 1] - net.Points[i, j]).Length;
                    Assert.True(Math.Abs(e - L) < relTol * L,
                        $"V edge at ({i},{j}) should be ~{L}, got {e:F4}");
                }
            }
    }
}
