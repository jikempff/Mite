using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Diagnostics;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Runtime messages with the fix in them (<see cref="InputAdvice"/>): each
/// check fires on the case it names, carries the measured number and a
/// suggested value, the suggested value really fixes the case, and the
/// check stays silent on the ordinary inputs of the bench and the tests.
/// </summary>
public class InputAdviceTests
{
    // z = x² − a y² on [−h, h]²: K < 0 everywhere, asymptotic directions in the
    // plan dy/dx = ±1/√a (from f_xx dx² + 2 f_xy dx dy + f_yy dy² = 0)
    private const double A = 0.01, H = 0.3;
    private static MeshData ShallowSaddle(int n = 30) =>
        AnalyticShapes.Grid((x, y) => new Vec3d(x, y, x * x - A * y * y), -H, H, -H, H, n, n);

    /// <summary>Exact crossing angle (degrees, acute) of the asymptotic lines of z = x² − a y² at (x, y).</summary>
    private static double ExactCrossing(double x, double y)
    {
        double m = 1.0 / Math.Sqrt(A);
        var tp = new Vec3d(1, m, 2 * x - 2 * A * y * m);
        var tm = new Vec3d(1, -m, 2 * x + 2 * A * y * m);
        double c = Math.Abs(Vec3d.Dot(tp, tm)) / (tp.Length * tm.Length);
        return Math.Acos(Math.Min(1, c)) * 180 / Math.PI;
    }

    private static T WithInvariantCulture<T>(Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { return f(); } finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void CrossingAngle_MatchesClosedForm()
    {
        Assert.Equal(90.0, InputAdvice.AsymptoticCrossingAngle(1, -1), 9);
        // k1 = 2, k2 = −0.02: tan t = 10, crossing 2·atan(0.1)
        Assert.Equal(2 * Math.Atan(0.1) * 180 / Math.PI, InputAdvice.AsymptoticCrossingAngle(2, -0.02), 9);
        Assert.Equal(InputAdvice.AsymptoticCrossingAngle(-0.02, 2), InputAdvice.AsymptoticCrossingAngle(2, -0.02), 12);
        Assert.True(double.IsNaN(InputAdvice.AsymptoticCrossingAngle(1, 1)));
        Assert.True(double.IsNaN(InputAdvice.AsymptoticCrossingAngle(1, 0)));
    }

    [Fact]
    public void MinAngle_ExcludingEverything_IsNamedWithTheLargestCrossing_AndTheSuggestionWorks()
    {
        var mesh = ShallowSaddle();
        var pc = PrincipalCurvature.Compute(mesh);
        double exactMax = mesh.Vertices.Max(p => ExactCrossing(p.X, p.Y));
        Assert.InRange(exactMax, 12.5, 14.0); // 2·atan(0.1) = 11.42° at the centre, 13.3° at x = ±0.3

        // The default MinAngle 15° leaves nothing: before, the component only said "no anticlastic region"
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        Assert.Equal(0, field.Exists.Count(e => e));

        var advice = InputAdvice.SaddleRegion(pc, 15.0, mesh.BoundingBoxDiagonal());
        var a = Assert.Single(advice);
        Assert.Equal("min-angle-excludes-all", a.Code);
        Assert.Equal(AdviceLevel.Warning, a.Level);
        Assert.InRange(Math.Abs(a.Measured - exactMax), 0, 0.5);
        var census = InputAdvice.CountSaddle(pc, 15.0);
        double exactMin = mesh.Vertices.Min(p => ExactCrossing(p.X, p.Y));
        Assert.InRange(Math.Abs(census.MinCrossing - exactMin), 0, 0.5); // 11.42° at the centre
        Assert.Equal(Math.Floor(census.MedianCrossing), a.Suggested);
        Assert.Contains(InputAdvice.Format(a.Suggested) + "°", a.Message);
        Assert.Contains(InputAdvice.Format(a.Measured) + "°", a.Message);
        Assert.Contains(InputAdvice.Format(Math.Floor(census.MinCrossing)) + "° to keep all", a.Message);
        Assert.Equal(a.Suggested > Math.Floor(census.MinCrossing), a.Message.Contains("to keep at least half"));

        // Following the advice gives a net
        var fixedField = AsymptoticCurves.ComputeDirections(pc, mesh, a.Suggested);
        Assert.True(fixedField.Exists.Count(e => e) > mesh.VertexCount / 2);
        var opts = new EvenlySpacedNet.Options { Spacing = 0.06 };
        var fam = EvenlySpacedNet.TraceField(mesh, fixedField.Family1, fixedField.Exists, -1, opts, fixedField.Family2);
        Assert.True(fam.Count >= 3, $"{fam.Count} curves after lowering MinAngle");
    }

    [Fact]
    public void Census_AgreesWithTheFieldMask_AndWithTheExactAngles()
    {
        var mesh = ShallowSaddle();
        var pc = PrincipalCurvature.Compute(mesh);
        foreach (double minAngle in new[] { 0.0, 12.0, 12.5, 13.0 })
        {
            var c = InputAdvice.CountSaddle(pc, minAngle);
            var field = AsymptoticCurves.ComputeDirections(pc, mesh, minAngle);
            Assert.Equal(field.Exists.Count(e => e), c.Usable);
            Assert.Equal(mesh.VertexCount, c.Saddle);
            int exact = mesh.Vertices.Count(p => ExactCrossing(p.X, p.Y) >= minAngle);
            Assert.InRange(c.Usable, exact - mesh.VertexCount / 25, exact + mesh.VertexCount / 25);
        }
        // 12.5°: a large share of the region is left out → the Remark names the share
        var adv = Assert.Single(InputAdvice.SaddleRegion(pc, 12.5));
        Assert.Equal("min-angle-excludes-part", adv.Code);
        Assert.Equal(AdviceLevel.Remark, adv.Level);
        var cc = InputAdvice.CountSaddle(pc, 12.5);
        Assert.Equal(1.0 - cc.Usable / (double)cc.Saddle, adv.Measured, 12);
        Assert.Contains($"{cc.Saddle - cc.Usable} of {cc.Saddle}", adv.Message);
    }

    [Fact]
    public void SynclasticAndFlatMeshes_AreExplained()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        var adv = Assert.Single(InputAdvice.SaddleRegion(PrincipalCurvature.Compute(sphere), 15, sphere.BoundingBoxDiagonal()));
        Assert.Equal("no-saddle", adv.Code);
        Assert.True(adv.Measured > 0.5, $"min K = {adv.Measured} on the unit sphere");
        Assert.Contains("Geodesic Net", adv.Message);

        var plane = AnalyticShapes.Grid((x, y) => new Vec3d(x, y, 0), 0, 1, 0, 1, 10, 10);
        var flat = Assert.Single(InputAdvice.SaddleRegion(PrincipalCurvature.Compute(plane), 15, plane.BoundingBoxDiagonal()));
        Assert.Equal("mesh-flat", flat.Code);

        // a real saddle at the default MinAngle: nothing to say
        var saddle = TestMeshes.CreateSaddle(20, 2.0);
        Assert.Empty(InputAdvice.SaddleRegion(PrincipalCurvature.Compute(saddle), 15, saddle.BoundingBoxDiagonal()));
    }

    [Fact]
    public void SpacingBelowEdge_NamesTheEdgeAndTheSubdivisions()
    {
        var list = WithInvariantCulture(() => InputAdvice.Spacing(0.02, true, 0.002, false, 0.05, 2.0));
        var a = Assert.Single(list);
        Assert.Equal("spacing-below-edge", a.Code);
        Assert.Equal(0.05, a.Suggested);
        Assert.Contains("Spacing 0.02 is below the mean mesh edge 0.05", a.Message);
        // 0.05 → 0.025 → 0.0125: two subdivisions bring the edge below 0.02
        Assert.Contains("subdivide the mesh 2×", a.Message);
        // automatic spacing is never below 4 edges, so it is never flagged
        Assert.Empty(InputAdvice.Spacing(0.02, false, 0.002, false, 0.05, 2.0));
        Assert.Empty(InputAdvice.Spacing(0.05, true, 0.005, false, 0.05, 2.0));
    }

    [Fact]
    public void StepChecks_SuggestTheAutomaticStep()
    {
        var coarse = Assert.Single(InputAdvice.Spacing(0.1, true, 0.05, true, 0.05, 2.0));
        Assert.Equal("step-coarse-for-spacing", coarse.Code);
        Assert.Equal(0.01, coarse.Suggested, 12); // min(Spacing/10, edge/2) = TraceDefaults' automatic step
        var mesh = TestMeshes.CreateSaddle(20, 2.0);
        var proj = new MeshProjection(mesh);
        Assert.Equal(TraceDefaults.ResolveStep(0, 0.6, proj), InputAdvice.Spacing(0.6, true, 0.3, true, proj.AverageEdgeLength, 3).Single().Suggested, 2);

        var longStep = Assert.Single(InputAdvice.Spacing(1.0, true, 0.02, true, 0.01, 5.0));
        Assert.Equal("step-longer-than-edge", longStep.Code);
        Assert.Equal(0.005, longStep.Suggested, 12);
        Assert.Empty(InputAdvice.Spacing(1.0, true, 0.005, true, 0.01, 5.0));
    }

    [Fact]
    public void SpacingAgainstRegion_PredictsFewCurves_AndTheSuggestionFillsTheRegion()
    {
        var mesh = TestMeshes.CreateSaddle(30, 2.0);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15);
        var proj = new MeshProjection(mesh);
        double region = InputAdvice.RegionSize(mesh, field.Exists);
        Assert.InRange(region, 3.2, 3.47); // ≤ the diagonal √12 of the whole saddle

        var opts = new EvenlySpacedNet.Options { Spacing = 0.55 * region };
        var big = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, opts, field.Family2);
        var adv = InputAdvice.Spacing(opts.ResolvedSpacing, true, opts.ResolvedStepSize, false, proj.AverageEdgeLength, region);
        var a = Assert.Single(adv);
        Assert.Equal("spacing-exceeds-region", a.Code);
        Assert.InRange(big.Count, 1, 2); // "one or two curves"

        var opts2 = new EvenlySpacedNet.Options { Spacing = a.Suggested };
        var full = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, opts2, field.Family2);
        Assert.True(full.Count >= 8, $"{full.Count} curves at the suggested spacing {a.Suggested}");
        Assert.Empty(InputAdvice.Spacing(opts2.ResolvedSpacing, true, opts2.ResolvedStepSize, false, proj.AverageEdgeLength, region));
    }

    [Fact]
    public void NoFalseAlarms_OnTheBenchShapesAtTheirSpacings()
    {
        var cases = new (string name, MeshData mesh, double spacing)[]
        {
            ("saddle", TestMeshes.CreateSaddle(30, 2.0), 0.15),
            ("saddle auto", TestMeshes.CreateSaddle(30, 2.0), 0),
            ("hyperboloid", TestMeshes.CreateHyperboloid(), 0.25),
            ("catenoid", TestMeshes.CreateCatenoid(), 0.2),
            ("enneper 2", TestMeshes.CreateEnneper(2), 0),
        };
        foreach (var (name, mesh, spacing) in cases)
        {
            var pc = PrincipalCurvature.Compute(mesh);
            var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15);
            var proj = new MeshProjection(mesh);
            var opts = new EvenlySpacedNet.Options { Spacing = spacing };
            var fam = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, opts, field.Family2);
            Assert.True(fam.Count > 3, name);
            var adv = InputAdvice.Spacing(opts.ResolvedSpacing, spacing > 0, opts.ResolvedStepSize, false,
                proj.AverageEdgeLength, InputAdvice.RegionSize(mesh, field.Exists));
            adv.AddRange(InputAdvice.SaddleRegion(pc, 15, mesh.BoundingBoxDiagonal()).Where(x => x.Level == AdviceLevel.Warning));
            Assert.True(adv.Count == 0, name + ": " + string.Join(" | ", adv));
        }
    }

    [Fact]
    public void SeedPointOffTheSurface_ReportsItsExactDistance()
    {
        var plane = TestMeshes.CreateQuadGrid(30, 30, 0.1); // 3 × 3, edges 0.1
        var proj = new MeshProjection(plane);
        double diag = plane.BoundingBoxDiagonal();
        foreach (var (h, expected) in new[] { (0.12, (AdviceLevel?)null), (0.3, AdviceLevel.Remark), (1.5, AdviceLevel.Warning) })
        {
            var p = new Vec3d(1.23, 1.71, h);
            int v = proj.NearestVertexGlobal(p);
            double d = (proj.ClosestPoint(p, v).Point - p).Length;
            Assert.Equal(h, d, 9);
            var a = InputAdvice.SeedPoint(2, d, proj.AverageEdgeLength, diag);
            if (expected == null) { Assert.Null(a); continue; }
            Assert.NotNull(a);
            Assert.Equal(expected, a!.Level);
            Assert.Equal(h, a.Measured, 9);
            Assert.Contains("SeedPoint 2 is " + InputAdvice.Format(h), a.Message);
        }
    }

    [Fact]
    public void Format_ThreeSignificantDigits_NoExponentForOrdinarySizes()
    {
        WithInvariantCulture(() =>
        {
            Assert.Equal("2590", InputAdvice.Format(2589.7));
            Assert.Equal("0.0557", InputAdvice.Format(0.05574));
            Assert.Equal("0.346", InputAdvice.Format(3.4641 / 10));
            Assert.Equal("0.35", InputAdvice.Format(InputAdvice.RoundSignificant(3.4641 / 10, 2)));
            Assert.Equal(0.35, InputAdvice.RoundSignificant(0.34641, 2));
            Assert.Equal("1.5E-07", InputAdvice.Format(1.5e-7));
            Assert.Equal("0", InputAdvice.Format(0));
            return 0;
        });
    }

    [Fact]
    public void SeedIndicesOutOfRange_NameTheRange()
    {
        var a = WithInvariantCulture(() => InputAdvice.SeedIndices(new[] { 1200, 1500 }, 1089));
        Assert.NotNull(a);
        Assert.Contains("Seeds 1200, 1500 skipped: the mesh has 1089 vertices (0 to 1088)", a!.Message);
        var many = InputAdvice.SeedIndices(Enumerable.Range(2000, 8).ToArray(), 10);
        Assert.Contains("and 3 more", many!.Message);
        Assert.Null(InputAdvice.SeedIndices(Array.Empty<int>(), 10));
    }

    [Fact]
    public void SectionScale_CatchesUnitSlips_BothWays()
    {
        var lengthsM = new[] { 3.0, 4.0, 5.0, 4.5 };
        // millimetres typed into a metre model
        var mm = WithInvariantCulture(() => InputAdvice.SectionScale(50, 8, lengthsM, 1.0, "Meters"));
        Assert.NotNull(mm);
        Assert.Equal("section-larger-than-laths", mm!.Code);
        Assert.Equal(50 / 4.25, mm.Measured, 9); // median of 3, 4, 4.5, 5
        Assert.Equal(0.05, mm.Suggested, 12);
        Assert.Contains("enter Width 0.05 and Thickness 0.008", mm.Message);

        // metres typed into a millimetre model
        var lengthsMm = lengthsM.Select(l => 1000 * l).ToArray();
        var m = WithInvariantCulture(() => InputAdvice.SectionScale(0.05, 0.008, lengthsMm, 0.001, "Millimeters"));
        Assert.NotNull(m);
        Assert.Equal("section-tiny", m!.Code);
        Assert.Equal(50, m.Suggested, 9);
        Assert.Contains("enter Width 50 and Thickness 8", m.Message);

        // a section deep against the laths in a millimetre model is not a unit slip
        var deep = InputAdvice.SectionScale(2000, 50, lengthsMm, 0.001, "Millimeters");
        Assert.NotNull(deep);
        Assert.True(double.IsNaN(deep!.Suggested));
        Assert.DoesNotContain("if you meant", deep.Message);

        // the defaults (0.1 × 0.01) on metre laths, and 100 × 20 mm on millimetre laths: silent
        Assert.Null(InputAdvice.SectionScale(0.1, 0.01, lengthsM, 1.0, "Meters"));
        Assert.Null(InputAdvice.SectionScale(100, 20, lengthsMm, 0.001, "Millimeters"));
        Assert.Null(InputAdvice.SectionScale(0.1, double.NaN, lengthsM, 1.0, "Meters"));
        Assert.Null(InputAdvice.SectionScale(0.1, 0.01, Array.Empty<double>(), 1.0, "Meters"));
    }

    [Fact]
    public void Messages_AvoidBlameWords_AndCarryAFix()
    {
        var mesh = ShallowSaddle();
        var pc = PrincipalCurvature.Compute(mesh);
        var all = new List<Advice>();
        all.AddRange(InputAdvice.Spacing(0.02, true, 0.002, false, 0.05, 2.0));
        all.AddRange(InputAdvice.Spacing(2, true, 0.6, true, 0.05, 2.0));
        all.AddRange(InputAdvice.Spacing(1.0, true, 0.02, true, 0.01, 5.0));
        all.AddRange(InputAdvice.SaddleRegion(pc, 15));
        all.AddRange(InputAdvice.SaddleRegion(pc, 12.5));
        all.AddRange(InputAdvice.SaddleRegion(PrincipalCurvature.Compute(TestMeshes.CreateUnitSphere(8)), 15));
        all.Add(InputAdvice.SeedPoint(0, 5, 0.1, 3)!);
        all.Add(InputAdvice.SeedIndices(new[] { 99 }, 10)!);
        all.Add(InputAdvice.SectionScale(50, 8, new[] { 4.0 }, 1, "Meters")!);
        all.Add(InputAdvice.SectionScale(0.05, 0.008, new[] { 4000.0 }, 0.001, "Millimeters")!);
        Assert.Equal(11, all.Count);
        Assert.Equal(all.Count, all.Select(a => a.Code).Distinct().Count());
        // Shneiderman 1982 / NN/g 2023: no hostile or blaming words
        string[] blame = { "invalid", "illegal", "bad ", "error", "fatal", "wrong" };
        string[] fixVerbs = { "Raise", "Lower", "Set ", "enter", "use ", "Use ", "move", "Bend", "check", "subdivide", "keep working" };
        foreach (var a in all)
        {
            foreach (var w in blame) Assert.DoesNotContain(w, a.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(fixVerbs.Any(v => a.Message.Contains(v)), "no fix in: " + a.Message);
            Assert.Matches("[0-9]", a.Message); // numbers, not adjectives
        }
    }
}
