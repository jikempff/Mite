using System;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

public class CurveSmoothnessTests
{
    private static double MaxTurnDegrees(Vec3d[] line)
    {
        double maxT = 0;
        for (int i = 1; i < line.Length - 1; i++)
        {
            Vec3d a = (line[i] - line[i - 1]).Normalized();
            Vec3d b = (line[i + 1] - line[i]).Normalized();
            if (a.LengthSquared < 0.5 || b.LengthSquared < 0.5) continue;
            double d = Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(a, b)));
            maxT = Math.Max(maxT, Math.Acos(d) * 180.0 / Math.PI);
        }
        return maxT;
    }

    private static double Length(Vec3d[] line)
    {
        double len = 0;
        for (int i = 1; i < line.Length; i++) len += (line[i] - line[i - 1]).Length;
        return len;
    }

    [Fact]
    public void Geodesic_Sphere_ClosesIntoGreatCircle()
    {
        // A geodesic on a sphere is a great circle: it must close after 2πR
        // instead of winding over itself for MaxSteps
        var sphere = TestMeshes.CreateUnitSphere(32);
        int seed = sphere.VertexCount / 2;
        var lines = GeodesicCurves.Trace(sphere, new[] { seed }, new[] { new Vec3d(0, 1, 0) },
            new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 });

        Assert.Single(lines);
        var line = lines[0];
        double len = Length(line);
        Assert.True(Math.Abs(len - 2.0 * Math.PI) < 0.15,
            $"Great circle length should be ≈2π, got {len:F3}");
        Assert.True((line[0] - line[line.Length - 1]).Length < 0.05,
            "Geodesic loop should close");
    }

    [Fact]
    public void Geodesic_Sphere_TurningBoundedByMeshDihedral()
    {
        // Turning between consecutive segments should stay near the mesh's own
        // facet dihedral (~5.6° for a 32-band sphere) — no tracing artifacts
        var sphere = TestMeshes.CreateUnitSphere(32);
        int seed = sphere.VertexCount / 2;
        var lines = GeodesicCurves.Trace(sphere, new[] { seed }, new[] { new Vec3d(0, 1, 0) },
            new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 });

        double maxTurn = MaxTurnDegrees(lines[0]);
        Assert.True(maxTurn < 7.0, $"Max turning angle should be near mesh dihedral, got {maxTurn:F1}°");
    }

    [Fact]
    public void Asymptotic_Saddle_TracesStraightDiagonals()
    {
        // On z = x² − y² the asymptotic directions are ±45° in xy everywhere,
        // so both families project to straight diagonal lines
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var curvature = PrincipalCurvature.Compute(saddle);
        int center = (40 / 2) * 41 + 40 / 2;
        var opts = new AsymptoticCurves.Options { StepSize = 0.02, MaxSteps = 500 };

        foreach (bool secondFamily in new[] { false, true })
        {
            var lines = AsymptoticCurves.Trace(saddle, new[] { center }, curvature, secondFamily, opts);
            Assert.Single(lines);
            var line = lines[0];
            Assert.True(Length(line) > 2.0, "Trace should cross most of the saddle");

            // Straight in xy: |Δx| == |Δy| along the whole curve
            foreach (var p in line)
                Assert.True(Math.Abs(Math.Abs(p.X) - Math.Abs(p.Y)) < 0.08,
                    $"Asymptotic curve should follow ±45° diagonal, got ({p.X:F3}, {p.Y:F3})");
        }
    }

    [Fact]
    public void Asymptotic_Torus_IsContinuousAndSmooth()
    {
        // Family labels flip vertex-to-vertex on the torus; without candidate
        // alignment the trace zigzags between families with >10° kinks
        var torus = TestMeshes.CreateTorus(3.0, 1.0, 96, 48);
        var curvature = PrincipalCurvature.Compute(torus);
        int innerSeed = 10 * 48 + 24; // inner equator, K < 0
        var opts = new AsymptoticCurves.Options { StepSize = 0.03, MaxSteps = 1500 };

        foreach (bool secondFamily in new[] { false, true })
        {
            var lines = AsymptoticCurves.Trace(torus, new[] { innerSeed }, curvature, secondFamily, opts);
            Assert.Single(lines);
            Assert.True(Length(lines[0]) > 5.0, "Asymptotic trace should be long and unbroken");
            double maxTurn = MaxTurnDegrees(lines[0]);
            Assert.True(maxTurn < 6.0,
                $"Asymptotic curve should be smooth (family-swap kinks were >10°), got {maxTurn:F1}°");
        }
    }

    [Fact]
    public void Geodesic_PerpendicularSeedDirection_StillTraces()
    {
        // A seed direction parallel to the surface normal has no tangent
        // component; the tracer must fall back instead of returning nothing
        var sphere = TestMeshes.CreateUnitSphere(32);
        int seed = sphere.VertexCount / 2; // near (-1, 0, 0)
        var lines = GeodesicCurves.Trace(sphere, new[] { seed }, new[] { new Vec3d(1, 0, 0) },
            new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 500 });

        Assert.Single(lines);
        Assert.True(lines[0].Length > 10, "Trace should make progress from a degenerate seed direction");
    }
}

public class EdgeAndStubTests
{
    private static double Length(Mite.Core.Geometry.Vec3d[] line)
    {
        double len = 0;
        for (int i = 1; i < line.Length; i++) len += (line[i] - line[i - 1]).Length;
        return len;
    }

    [Fact]
    public void Geodesic_OpenMesh_StopsAtBoundaryWithoutCrawling()
    {
        // A trace that leaves the mesh used to be clamped onto the boundary and
        // then crawl along it, producing "threads" along open edges
        var grid = TestMeshes.CreateQuadGrid(10, 10).ToTriangulated();
        int center = 5 * 11 + 5;
        var lines = Mite.Core.Gridshells.GeodesicCurves.Trace(
            grid, new[] { center }, new[] { new Mite.Core.Geometry.Vec3d(1, 0.3, 0) },
            new Mite.Core.Gridshells.GeodesicCurves.Options { StepSize = 0.1, MaxSteps = 2000, SmoothingPasses = 0 });

        Assert.Single(lines);
        var line = lines[0];

        int boundaryPoints = line.Count(p =>
            p.X > 9.999 || p.Y > 9.999 || p.X < 0.001 || p.Y < 0.001);
        Assert.True(boundaryPoints <= 2,
            $"Only the endpoints may touch the boundary, found {boundaryPoints} boundary points");

        // Both ends should reach the boundary (within a step) rather than stop short
        foreach (var end in new[] { line[0], line[line.Length - 1] })
        {
            double edgeDist = Math.Min(
                Math.Min(end.X, 10 - end.X),
                Math.Min(end.Y, 10 - end.Y));
            Assert.True(edgeDist < 0.15, $"Trace end should reach the open edge, stopped {edgeDist:F3} away");
        }
    }

    [Fact]
    public void AutoSpace_Asymptotic_ProducesNoStubCurves()
    {
        var saddle = TestMeshes.CreateSaddle(40, 2.0);
        var curvature = Mite.Core.Curvature.PrincipalCurvature.Compute(saddle);
        var field = Mite.Core.Gridshells.AsymptoticCurves.ComputeDirections(curvature);
        var opts = new Mite.Core.Gridshells.EvenlySpacedNet.Options
        {
            Spacing = 0.15,
            StepSize = 0.02,
            MaxSteps = 2000
        };

        var curves = Mite.Core.Gridshells.EvenlySpacedNet.TraceField(
            saddle, field.Family1, field.Exists, -1, opts, field.Family2);

        Assert.True(curves.Count > 10, $"Fill should produce a dense family, got {curves.Count}");
        for (int i = 1; i < curves.Count; i++) // first curve exempt by design
            Assert.True(Length(curves[i]) >= 2.0 * opts.Spacing,
                $"Curve {i} is a stub: length {Length(curves[i]):F3} < {2.0 * opts.Spacing:F3}");
    }
}
