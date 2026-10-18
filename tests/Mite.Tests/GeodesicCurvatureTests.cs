using System;
using System.Linq;
using Xunit;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Geodesic curvature read by Lath Analysis against closed forms, and the
/// loop closure of the tracers (2026-10-18). Truths: a latitude circle at
/// latitude φ on the unit sphere has kg = tan φ, kn = 1; the top circle of a
/// torus (R, r) has kg = 1 / R, kn = 0; every great circle and every helix is
/// a geodesic, kg = 0; a flat lath along a great circle of the unit sphere
/// with thickness t has strain kn · t / 2 and nothing else.
/// </summary>
public class GeodesicCurvatureTests
{
    private static Vec3d[] Circle(double radius, double z, int count, double sweep)
    {
        var pts = new Vec3d[count];
        for (int i = 0; i < count; i++)
        {
            double th = sweep * i / (count - 1);
            pts[i] = new Vec3d(radius * Math.Cos(th), radius * Math.Sin(th), z);
        }
        if (Math.Abs(sweep - 2 * Math.PI) < 1e-12) pts[count - 1] = pts[0];
        return pts;
    }

    private static double MaxAbsError(double[] values, double truth, int skip)
    {
        int n = values.Length;
        return Enumerable.Range(skip, n - 2 * skip).Max(i => Math.Abs(Math.Abs(values[i]) - truth));
    }

    /// <summary>Largest in-plane (about the smooth normal) kink of a polyline, in degrees.</summary>
    internal static double MaxInPlaneKinkDegrees(MeshProjection proj, Vec3d[] line)
    {
        int hint = proj.NearestVertexGlobal(line[0]);
        double max = 0;
        for (int i = 1; i < line.Length - 1; i++)
        {
            var hit = proj.ClosestPoint(line[i], hint);
            hint = hit.NearestVertex;
            Vec3d n = hit.SmoothNormal;
            Vec3d a = line[i] - line[i - 1]; a = a - Vec3d.Dot(a, n) * n;
            Vec3d b = line[i + 1] - line[i]; b = b - Vec3d.Dot(b, n) * n;
            if (a.LengthSquared < 1e-30 || b.LengthSquared < 1e-30) continue;
            double phi = Math.Atan2(Vec3d.Dot(n, Vec3d.Cross(a, b)), Vec3d.Dot(a, b));
            max = Math.Max(max, Math.Abs(phi) * 180.0 / Math.PI);
        }
        return max;
    }

    [Theory]
    [InlineData(24, 40, 0.005)]
    [InlineData(48, 40, 0.005)]
    [InlineData(48, 12, 0.02)]
    public void LatitudeCircle_CoarseSampling_GeodesicCurvatureIsTanLatitude(int divisions, int points, double tolerance)
    {
        // The window (2 mesh edges) is shorter than the polyline's spacing for
        // 40 points on the 48-sphere (0.093 vs 0.125): the old two-chord
        // estimator read 1.36 instead of 1.00 there, and 1.11 on 24 divisions.
        // At 12 points (spacing 0.30, five mesh edges) the turning between the
        // projected chords is that of the inscribed geodesic polygon, which
        // differs from the circle's by the Gaussian curvature of the slivers
        // between them, K·l³κ/12 per side ≈ 0.75 %: 0.011 here, second order
        // in the spacing.
        var sphere = TestMeshes.CreateUnitSphere(divisions);
        var proj = new MeshProjection(sphere);
        double lat = Math.PI / 4;
        var pts = Circle(Math.Cos(lat), Math.Sin(lat), points, 1.5 * Math.PI);

        var r = LathAnalysis.Analyze(proj, pts);
        double err = MaxAbsError(r.GeodesicCurvature, Math.Tan(lat), 2);
        Assert.True(err < tolerance, $"kg of the 45° latitude circle should be 1 at any sampling, max error {err:F4}");
        double knErr = MaxAbsError(r.NormalCurvature, 1.0, 2);
        Assert.True(knErr < 3 * tolerance, $"kn of a sphere curve is 1, max error {knErr:F4} (the normal is interpolated along chords {points} points apart)");
    }

    [Fact]
    public void LatitudeCircle_SteepLatitude_GeodesicCurvatureIsTanLatitude()
    {
        // 60° latitude: kg = √3, a small circle of radius 0.5 sampled at 24
        // points over 270° (spacing 0.10 against a window of 0.25)
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        double lat = Math.PI / 3;
        var pts = Circle(Math.Cos(lat), Math.Sin(lat), 24, 1.5 * Math.PI);
        var r = LathAnalysis.Analyze(proj, pts);
        double err = MaxAbsError(r.GeodesicCurvature, Math.Tan(lat), 2);
        Assert.True(err < 0.02, $"kg of the 60° latitude circle should be √3 = 1.732, max error {err:F4}");
    }

    [Fact]
    public void TorusTopCircle_ClosedCoarsePolyline_GeodesicCurvatureIsOneOverR()
    {
        // Closed 30-point circle on the crown of the torus: kg = 1/3, kn = 0,
        // measured through the wrap-around window. The old estimator gave 0.58.
        var torus = TestMeshes.CreateTorus(3, 1, 48, 24);
        var proj = new MeshProjection(torus);
        var pts = Circle(3, 1, 31, 2 * Math.PI).Select(p => proj.ClosestPoint(p, proj.NearestVertexGlobal(p)).Point).ToArray();
        pts[pts.Length - 1] = pts[0];

        var r = LathAnalysis.Analyze(proj, pts);
        double err = r.GeodesicCurvature.Max(k => Math.Abs(Math.Abs(k) - 1.0 / 3.0));
        Assert.True(err < 0.003, $"kg of the torus crown circle should be 1/3 at every vertex, max error {err:F4}");
        Assert.True(r.NormalCurvature.Max(Math.Abs) < 0.01, "kn of the crown circle is 0");
        Assert.Equal(r.GeodesicCurvature[0], r.GeodesicCurvature[^1]);
    }

    [Fact]
    public void GeodesicCurvature_Sign_PointsTowardsTheTurn()
    {
        // kg > 0 when the lath turns towards g = N × T. On the 45° latitude
        // circle traversed counter-clockwise (seen from +z) the turn is towards
        // the pole: g = N × T with N radial and T along +θ points up the
        // sphere, so kg is positive; the reverse traversal flips it.
        var sphere = TestMeshes.CreateUnitSphere(24);
        var proj = new MeshProjection(sphere);
        double lat = Math.PI / 4;
        var ccw = Circle(Math.Cos(lat), Math.Sin(lat), 40, 1.5 * Math.PI);
        var cw = ccw.Reverse().ToArray();
        var a = LathAnalysis.Analyze(proj, ccw);
        var b = LathAnalysis.Analyze(proj, cw);
        for (int i = 3; i < 37; i++)
        {
            Assert.True(a.GeodesicCurvature[i] > 0.9, $"ccw kg should be +1, got {a.GeodesicCurvature[i]:F3}");
            Assert.True(b.GeodesicCurvature[i] < -0.9, $"cw kg should be −1, got {b.GeodesicCurvature[i]:F3}");
        }
    }

    [Theory]
    [InlineData(24, 0.0, 1.0, 0.0)]
    [InlineData(24, 0.0, 0.6, 0.8)]
    [InlineData(48, 0.0, 1.0, 0.0)]
    [InlineData(48, 0.0, 0.6, 0.8)]
    [InlineData(48, 0.0, 0.3, 0.954)]
    public void TracedGreatCircle_ClosedLoop_IsGeodesicAfterClosure(int divisions, double dx, double dy, double dz)
    {
        // The loop closure used to spread the closure gap as a constant 3D
        // vector; half-way round the sphere that vector points into the
        // surface and the reprojection landed each point on whichever facet
        // beside the edge was nearer: ±1.2e-3 lateral zigzag, in-plane kinks
        // of 9°, |kg| up to 1.12 on a geodesic (48 divisions, tilted circle),
        // and a flat lath reported at utilization 11 where the truth is 1.
        var sphere = TestMeshes.CreateUnitSphere(divisions);
        var proj = new MeshProjection(sphere);
        int seed = proj.NearestVertexGlobal(new Vec3d(1, 0, 0));
        var dir = new Vec3d(dx, dy, dz);
        var line = GeodesicCurves.Trace(sphere, new[] { seed }, new[] { dir })[0];

        Assert.True((line[0] - line[^1]).LengthSquared < 1e-24, "the great circle closes");
        Vec3d planeNormal = Vec3d.Cross(new Vec3d(1, 0, 0), dir).Normalized();
        double offPlane = line.Max(p => Math.Abs(Vec3d.Dot(p, planeNormal)));
        Assert.True(offPlane < 0.11 * proj.AverageEdgeLength, $"trace stays within a tenth of an edge of its plane, got {offPlane:E2} (edge {proj.AverageEdgeLength:F4})");

        double kink = MaxInPlaneKinkDegrees(proj, line);
        Assert.True(kink < 0.6, $"in-plane kinks of a traced great circle should be below 0.6°, got {kink:F2}°");

        var la = LathAnalysis.Analyze(proj, line, new LathAnalysis.Options { Width = 0.1, Thickness = 0.01, MaxStrain = 0.005 });
        double kgMax = la.GeodesicCurvature.Max(Math.Abs);
        Assert.True(kgMax < 0.05, $"kg of a traced great circle should be 0, got up to {kgMax:F4}");
        Assert.True(MaxAbsError(la.NormalCurvature, 1.0, 3) < 0.01, "kn = 1 along the great circle");
        // flat lath: kn · t/2 = 0.005 = MaxStrain exactly → utilization 1
        Assert.True(Math.Abs(la.MaxUtilization - 1.0) < 0.02, $"flat lath along a great circle: utilization 1.00, got {la.MaxUtilization:F3}");
    }

    [Fact]
    public void TracedGreatCircle_Unfaired_HasNoClosureKink()
    {
        // Without fairing the last traced point used to sit a round-off short
        // of the closing point, leaving a segment of 1e-4 that read as a 12°
        // kink; that point is now dropped and the loop closes with a whole step.
        var sphere = TestMeshes.CreateUnitSphere(48);
        var proj = new MeshProjection(sphere);
        int seed = proj.NearestVertexGlobal(new Vec3d(1, 0, 0));
        var line = GeodesicCurves.Trace(sphere, new[] { seed }, new[] { new Vec3d(0, 0.6, 0.8) },
            new GeodesicCurves.Options { SmoothingPasses = 0 })[0];
        double kink = MaxInPlaneKinkDegrees(proj, line);
        Assert.True(kink < 2.0, $"unfaired closed trace: in-plane kinks below 2°, got {kink:F2}°");
        var segs = Enumerable.Range(1, line.Length - 1).Select(i => (line[i] - line[i - 1]).Length).ToArray();
        Assert.True(segs.Min() > 0.3 * segs.Average(), $"no stub segment at the seam: shortest {segs.Min():E2} vs mean {segs.Average():E2}");
    }

    [Fact]
    public void TracedHelix_OnCoarseCylinder_GeodesicCurvatureZero()
    {
        // 45° helix on a 24-gon cylinder: kg = 0, kn = 0.5, τg = 0.5
        var cyl = TestMeshes.CreateCylinder(1, 4, 24, 12);
        var proj = new MeshProjection(cyl);
        int seed = proj.NearestVertexGlobal(new Vec3d(1, 0, 0.2));
        var line = GeodesicCurves.Trace(cyl, new[] { seed }, new[] { new Vec3d(0, Math.Sqrt(0.5), Math.Sqrt(0.5)) })[0];
        var la = LathAnalysis.Analyze(proj, line);
        int skip = 6;
        double kg = MaxAbsError(la.GeodesicCurvature, 0, skip);
        Assert.True(kg < 0.012, $"helix kg should be 0, got up to {kg:F4}");
        Assert.True(MaxAbsError(la.NormalCurvature, 0.5, skip) < 0.01, "helix kn = sin²45° = 0.5");
        Assert.True(MaxAbsError(la.GeodesicTorsion, 0.5, skip) < 0.01, "helix τg = sin 45° cos 45° = 0.5");
    }

    [Fact]
    public void StrainModel_FlatAndUprightLath_MatchTheStripFormulas()
    {
        // Latitude circle at 45° on the unit sphere: kg = 1, kn = 1, τg = 0.
        // Flat lath (w = 0.1, t = 0.01): kn about the width → ε = kn·t/2 =
        // 0.005; kg about the normal → ε = kg·w/2 = 0.05: the strong axis
        // governs, utilization 0.05 / MaxStrain. Upright: the two swap, same
        // peak here because kg = kn. On the equator (kg = 0) the flat lath
        // keeps 0.005 and the upright lath 0.05 (kn about its width w).
        var sphere = TestMeshes.CreateUnitSphere(48);
        var proj = new MeshProjection(sphere);
        double lat = Math.PI / 4;
        var circle = Circle(Math.Cos(lat), Math.Sin(lat), 60, 1.5 * Math.PI);
        var equator = Circle(1, 0, 60, 1.5 * Math.PI);
        var flat = new LathAnalysis.Options { Width = 0.1, Thickness = 0.01, MaxStrain = 0.01, Upright = false };
        var upright = new LathAnalysis.Options { Width = 0.1, Thickness = 0.01, MaxStrain = 0.01, Upright = true };

        Assert.True(Math.Abs(LathAnalysis.Analyze(proj, circle, flat).MaxUtilization - 5.0) < 0.1, "flat lath on the 45° circle: kg·w/2 / 0.01 = 5");
        Assert.True(Math.Abs(LathAnalysis.Analyze(proj, circle, upright).MaxUtilization - 5.0) < 0.1, "upright lath on the 45° circle: kn·w/2 / 0.01 = 5");
        Assert.True(Math.Abs(LathAnalysis.Analyze(proj, equator, flat).MaxUtilization - 0.5) < 0.02, "flat lath on the equator: kn·t/2 / 0.01 = 0.5");
        Assert.True(Math.Abs(LathAnalysis.Analyze(proj, equator, upright).MaxUtilization - 5.0) < 0.1, "upright lath on the equator: kn·w/2 / 0.01 = 5");
    }

    [Fact]
    public void ShortAndDegenerateLaths_DoNotThrow()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        var proj = new MeshProjection(sphere);
        var two = new[] { new Vec3d(1, 0, 0), new Vec3d(0.99, 0.14, 0) };
        var r2 = LathAnalysis.Analyze(proj, two);
        Assert.Equal(2, r2.GeodesicCurvature.Length);
        var three = new[] { new Vec3d(1, 0, 0), new Vec3d(0.99, 0.14, 0), new Vec3d(0.96, 0.28, 0) };
        var r3 = LathAnalysis.Analyze(proj, three);
        Assert.True(Math.Abs(r3.GeodesicCurvature[1]) < 0.05, "three points on the equator: kg ≈ 0");
        // duplicate consecutive points
        var dup = new[] { new Vec3d(1, 0, 0), new Vec3d(1, 0, 0), new Vec3d(0.99, 0.14, 0), new Vec3d(0.99, 0.14, 0), new Vec3d(0.96, 0.28, 0) };
        var rd = LathAnalysis.Analyze(proj, dup);
        Assert.True(rd.GeodesicCurvature.All(k => !double.IsNaN(k)));
    }
}
