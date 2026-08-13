using System;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

public class GridshellTests
{
    [Fact]
    public void AsymptoticDirections_Sphere_NoneExist()
    {
        // K > 0 everywhere on a sphere: no asymptotic directions anywhere
        var sphere = TestMeshes.CreateUnitSphere(16);
        var curvature = PrincipalCurvature.Compute(sphere);
        var field = AsymptoticCurves.ComputeDirections(curvature);

        int existCount = 0;
        foreach (bool e in field.Exists)
            if (e) existCount++;

        Assert.True(existCount == 0, $"Sphere should have no asymptotic directions, found {existCount}");
    }

    [Fact]
    public void AsymptoticDirections_Saddle_ExistAtFortyFiveDegrees()
    {
        // On z = x^2 - y^2 the asymptotic directions at the origin are at +/-45
        // degrees to the principal axes, i.e. along (1,1,0) and (1,-1,0).
        var saddle = TestMeshes.CreateSaddle(20, 2.0);
        var curvature = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(curvature);

        int center = (20 / 2) * 21 + 20 / 2;
        Assert.True(field.Exists[center], "Asymptotic directions should exist at the saddle center");

        // Normal curvature along each asymptotic direction should vanish
        double kMax = Math.Max(Math.Abs(curvature.K1[center]), Math.Abs(curvature.K2[center]));
        foreach (var dir in new[] { field.Family1[center], field.Family2[center] })
        {
            double c = Vec3d.Dot(dir, curvature.D1[center]);
            double s = Vec3d.Dot(dir, curvature.D2[center]);
            double kn = curvature.K1[center] * c * c + curvature.K2[center] * s * s;
            Assert.True(Math.Abs(kn) < 0.15 * kMax,
                $"Normal curvature along asymptotic direction should be ~0, got {kn:F4} (kMax {kMax:F4})");
        }

        // Geometric check: each family lies near one of the analytic diagonals
        var diag1 = new Vec3d(1, 1, 0).Normalized();
        var diag2 = new Vec3d(1, -1, 0).Normalized();
        foreach (var dir in new[] { field.Family1[center], field.Family2[center] })
        {
            double a1 = Math.Abs(Vec3d.Dot(dir, diag1));
            double a2 = Math.Abs(Vec3d.Dot(dir, diag2));
            Assert.True(Math.Max(a1, a2) > 0.9,
                $"Asymptotic direction {dir} should align with a diagonal (dots {a1:F3}, {a2:F3})");
        }
    }

    [Fact]
    public void AsymptoticTrace_Saddle_StaysOnSurface()
    {
        var saddle = TestMeshes.CreateSaddle(20, 2.0);
        var curvature = PrincipalCurvature.Compute(saddle);
        int center = (20 / 2) * 21 + 20 / 2;

        var opts = new AsymptoticCurves.Options { StepSize = 0.05, MaxSteps = 200 };
        var familyA = AsymptoticCurves.Trace(saddle, new[] { center }, curvature, false, opts);
        var familyB = AsymptoticCurves.Trace(saddle, new[] { center }, curvature, true, opts);

        Assert.True(familyA.Count == 1, "Family A should produce one curve from the center seed");
        Assert.True(familyB.Count == 1, "Family B should produce one curve from the center seed");
        Assert.True(familyA[0].Length > 5, "Family A curve should have several points");

        foreach (var line in new[] { familyA[0], familyB[0] })
        {
            foreach (var p in line)
            {
                double zSurface = p.X * p.X - p.Y * p.Y;
                Assert.True(Math.Abs(p.Z - zSurface) < 0.1,
                    $"Traced point should stay near the saddle surface, offset {Math.Abs(p.Z - zSurface):F4}");
            }
        }
    }

    [Fact]
    public void Geodesic_Plane_IsStraight()
    {
        var grid = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        int center = 5 * 11 + 5;
        Vec3d start = grid.Vertices[center];
        var dir = new Vec3d(1, 1, 0).Normalized();

        var opts = new GeodesicCurves.Options { StepSize = 0.1, MaxSteps = 200 };
        var lines = GeodesicCurves.Trace(grid, new[] { center }, new[] { dir }, opts);

        Assert.True(lines.Count == 1, "Should trace one geodesic");
        Assert.True(lines[0].Length > 5, "Geodesic should have several points");

        foreach (var p in lines[0])
        {
            Assert.True(Math.Abs(p.Z) < 1e-9, "Geodesic on a flat grid should stay in the plane");
            Vec3d offset = p - start;
            double cross = Math.Abs(offset.X * dir.Y - offset.Y * dir.X);
            Assert.True(cross < 0.05, $"Geodesic on a plane should be straight, deviation {cross:F4}");
        }
    }

    [Fact]
    public void Geodesic_Sphere_ClosesAfterOneLoop()
    {
        // A great-circle geodesic must be detected as a closed loop after one
        // revolution. Midpoint integration drifts by several step sizes over a
        // full loop, so a point-capture radius of one step can never trigger —
        // the trace used to wrap around the sphere until MaxSteps.
        var sphere = TestMeshes.CreateUnitSphere(24);
        int equatorVertex = 1 + 11 * 48;

        var opts = new GeodesicCurves.Options { StepSize = 0.05, MaxSteps = 2000, SmoothingPasses = 0 };
        var lines = GeodesicCurves.Trace(sphere, new[] { equatorVertex }, new[] { new Vec3d(0, 1, 0) }, opts);

        Assert.True(lines.Count == 1, "Should trace one geodesic");
        double circumference = 2.0 * Math.PI;
        double arc = 0;
        for (int i = 1; i < lines[0].Length; i++)
            arc += (lines[0][i] - lines[0][i - 1]).Length;

        Assert.True(arc < 1.5 * circumference,
            $"Closed geodesic should stop near one circumference ({circumference:F2}), traveled {arc:F2}");
        double gap = (lines[0][^1] - lines[0][0]).Length;
        Assert.True(gap < 0.25,
            $"Closed geodesic should end near its start, gap {gap:F3}");
    }

    [Fact]
    public void Geodesic_Sphere_FollowsGreatCircle()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        // First vertex of the equator row (lat = 12 of 24): position (1, 0, 0)
        int equatorVertex = 1 + 11 * 48;
        Vec3d p0 = sphere.Vertices[equatorVertex];
        Assert.True(Math.Abs(p0.Z) < 1e-9, "Chosen seed should lie on the equator");

        var opts = new GeodesicCurves.Options { StepSize = 0.05, MaxSteps = 200 };
        var lines = GeodesicCurves.Trace(sphere, new[] { equatorVertex }, new[] { new Vec3d(0, 1, 0) }, opts);

        Assert.True(lines.Count == 1, "Should trace one geodesic");
        Assert.True(lines[0].Length > 20, "Geodesic should travel around the sphere");

        foreach (var p in lines[0])
        {
            Assert.True(Math.Abs(p.Z) < 0.1,
                $"Great circle from the equator heading east should stay near the equator, |z| = {Math.Abs(p.Z):F4}");
            Assert.True(p.Length > 0.9 && p.Length < 1.05,
                $"Geodesic should stay on the unit sphere, radius {p.Length:F4}");
        }
    }
}
