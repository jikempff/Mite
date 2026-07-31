using System;
using Xunit;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

public class LathAnalysisTests
{
    private static Vec3d[] CirclePoints(double radius, double z, int count, double sweep = 1.5 * Math.PI)
    {
        var pts = new Vec3d[count];
        for (int i = 0; i < count; i++)
        {
            double theta = sweep * i / (count - 1);
            pts[i] = new Vec3d(radius * Math.Cos(theta), radius * Math.Sin(theta), z);
        }
        return pts;
    }

    [Fact]
    public void StraightLine_OnPlane_NoBending()
    {
        var grid = TestMeshes.CreateQuadGrid(10, 10, 1.0).ToTriangulated();
        var proj = new MeshProjection(grid);

        var pts = new Vec3d[20];
        for (int i = 0; i < 20; i++)
            pts[i] = new Vec3d(0.5 + 9.0 * i / 19.0, 5.0, 0);

        var result = LathAnalysis.Analyze(proj, pts);

        Assert.True(result.Buildable);
        Assert.True(result.MaxUtilization < 0.05,
            $"Straight lath on a plane should have ~zero utilization, got {result.MaxUtilization:F4}");
    }

    [Fact]
    public void GreatCircle_OnSphere_PureNormalCurvature()
    {
        // The equator is a geodesic: kappa_n = 1, kappa_g = 0, tau_g = 0
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        var pts = CirclePoints(1.0, 0.0, 40);

        var result = LathAnalysis.Analyze(proj, pts);

        for (int i = 2; i < pts.Length - 2; i++)
        {
            Assert.True(Math.Abs(Math.Abs(result.NormalCurvature[i]) - 1.0) < 0.15,
                $"Equator normal curvature should be ~1, got {result.NormalCurvature[i]:F4} at {i}");
            Assert.True(Math.Abs(result.GeodesicCurvature[i]) < 0.15,
                $"Equator geodesic curvature should be ~0, got {result.GeodesicCurvature[i]:F4} at {i}");
            Assert.True(Math.Abs(result.GeodesicTorsion[i]) < 0.15,
                $"Sphere geodesic torsion should be ~0, got {result.GeodesicTorsion[i]:F4} at {i}");
        }
    }

    [Fact]
    public void LatitudeCircle_OnSphere_HasGeodesicCurvature()
    {
        // A latitude circle at 45 degrees has kappa_g = tan(45) = 1 and kappa_n = 1
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        double lat = Math.PI / 4;
        var pts = CirclePoints(Math.Cos(lat), Math.Sin(lat), 40);

        var result = LathAnalysis.Analyze(proj, pts);

        for (int i = 2; i < pts.Length - 2; i++)
        {
            Assert.True(Math.Abs(Math.Abs(result.GeodesicCurvature[i]) - 1.0) < 0.2,
                $"45-degree latitude geodesic curvature should be ~1, got {result.GeodesicCurvature[i]:F4} at {i}");
            Assert.True(Math.Abs(Math.Abs(result.NormalCurvature[i]) - 1.0) < 0.2,
                $"Sphere normal curvature should be ~1, got {result.NormalCurvature[i]:F4} at {i}");
        }
    }

    [Fact]
    public void Utilization_FlatStrip_ScalesWithThickness()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        var pts = CirclePoints(1.0, 0.0, 40);

        // Flat strip on the equator bends only about its weak axis: strain = kn * t/2
        var thin = LathAnalysis.Analyze(proj, pts,
            new LathAnalysis.Options { Width = 0.05, Thickness = 0.004, MaxStrain = 0.005 });
        Assert.True(thin.Buildable, $"Thin strip should be buildable, utilization {thin.MaxUtilization:F2}");
        Assert.True(Math.Abs(thin.MaxUtilization - 0.4) < 0.15,
            $"Thin strip utilization should be ~0.4, got {thin.MaxUtilization:F2}");

        var thick = LathAnalysis.Analyze(proj, pts,
            new LathAnalysis.Options { Width = 0.05, Thickness = 0.04, MaxStrain = 0.005 });
        Assert.False(thick.Buildable, $"Thick strip should fail, utilization {thick.MaxUtilization:F2}");
    }

    [Fact]
    public void Utilization_UprightStrip_FailsOffAsymptoticCurve()
    {
        // Upright strips bend about their strong axis wherever kappa_n != 0.
        // The equator has kappa_n = 1 everywhere, so an upright strip must fail.
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        var pts = CirclePoints(1.0, 0.0, 40);

        var result = LathAnalysis.Analyze(proj, pts,
            new LathAnalysis.Options { Upright = true, Width = 0.05, Thickness = 0.004, MaxStrain = 0.005 });

        Assert.False(result.Buildable,
            $"Upright strip on a non-asymptotic curve should fail, utilization {result.MaxUtilization:F2}");
        Assert.True(result.MaxUtilization > 3.0,
            $"Upright strip should be far over the limit, got {result.MaxUtilization:F2}");
    }
}
