using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Streamlines;

namespace Mite.Tests;

/// <summary>
/// Analytic ruled-surface typologies (roadmap A): the cylinder (developable,
/// K = 0) and the hyperboloid of one sheet (doubly ruled, K &lt; 0). Ground
/// truth used here:
///  - Cylinder R: k1 = 1/R along the circles, k2 = 0 along the rulings,
///    K = 0, H = 1/(2R); geodesics are helices, rulings and circles (Clairaut;
///    do Carmo 1976 §4-4); a helix at angle α from the axis rises by
///    R cot α per unit of unwrapped angle and has kn = sin²α / R,
///    τg = ±sin α cos α / R, kg = 0.
///  - Hyperboloid x² + y² − z²/c² = a²: K(u) = −c² / (c² + (a² + c²) u²)²
///    with u = z/c (Weisstein, MathWorld "One-Sheeted Hyperboloid"); H = 0 on
///    the throat when a = c; the asymptotic curves are the two families of
///    straight rulings (do Carmo 1976 §3-2, rulings have zero normal
///    curvature); along an asymptotic curve τg² = −K with opposite signs for
///    the two families (Beltrami–Enneper theorem, Encyclopedia of Mathematics).
/// </summary>
public class RuledSurfaceTests
{
    private const int Segments = 64;
    private const int Rows = 32;

    private static double ArcLength(Vec3d[] line)
    {
        double s = 0;
        for (int i = 1; i < line.Length; i++) s += (line[i] - line[i - 1]).Length;
        return s;
    }

    /// <summary>Largest distance of any point from the chord between the ends.</summary>
    private static double ChordDeviation(Vec3d[] line)
    {
        Vec3d a = line[0];
        Vec3d d = line[line.Length - 1] - a;
        double len = d.Length;
        if (len < 1e-15) return double.PositiveInfinity;
        d = d / len;
        double worst = 0;
        foreach (var p in line)
        {
            Vec3d v = p - a;
            worst = Math.Max(worst, (v - Vec3d.Dot(v, d) * d).Length);
        }
        return worst;
    }

    private static double AngleDeg(Vec3d a, Vec3d b) =>
        Math.Acos(Math.Min(1.0, Math.Abs(Vec3d.Dot(a.Normalized(), b.Normalized())))) * 180.0 / Math.PI;

    // Three rows in from the border: the border row and the two next to it are
    // refitted with an osculating jet (exact to ~1e-4 rather than to rounding)
    private static IEnumerable<int> InteriorVertices(int margin = 3)
    {
        for (int j = margin; j <= Rows - margin; j++)
            for (int i = 0; i < Segments; i++)
                yield return j * Segments + i;
    }

    private static int MiddleSeed => (Rows / 2) * Segments;

    // ------------------------------------------------------------ cylinder

    [Fact]
    public void Cylinder_Curvature_IsDevelopable()
    {
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(cyl);
        var K = GaussianCurvature.Compute(cyl);
        var H = MeanCurvature.Compute(cyl).Values;

        foreach (int v in InteriorVertices())
        {
            Assert.True(Math.Abs(pc.K1[v] - 1.0) < 0.01, $"k1 should be 1/R = 1, got {pc.K1[v]:F4} at {v}");
            Assert.True(Math.Abs(pc.K2[v]) < 1e-5, $"k2 should be 0 on a cylinder, got {pc.K2[v]:E2} at {v}"); // row 3 averages in the jet-fitted row 2 (~4e-6)
            Assert.True(Math.Abs(K[v]) < 1e-9, $"K should be 0 on a cylinder, got {K[v]:E2} at {v}");
            Assert.True(Math.Abs(H[v] - 0.5) < 1e-6, $"H should be 1/(2R) = 0.5, got {H[v]:F6} at {v}");
            Assert.True(Math.Abs(pc.D1[v].Z) < 1e-6, "Max-curvature direction should follow the circles (no axial component)");
            Assert.True(Math.Abs(Math.Abs(pc.D2[v].Z) - 1.0) < 1e-6, "Min-curvature direction should be the ruling (axial)");
        }
    }

    [Fact]
    public void Cylinder_HasNoAsymptoticDirections_AndAsymptoticNetIsEmpty()
    {
        // K = 0 everywhere: the single asymptotic direction is the ruling, a
        // degenerate (double) family. Asymptotic Net must not invent a second
        // family from curvature noise.
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(cyl);
        var field = AsymptoticCurves.ComputeDirections(pc);

        int existing = field.Exists.Count(e => e);
        Assert.True(existing == 0, $"No vertex of a cylinder should carry asymptotic directions, got {existing}");

        var curves = EvenlySpacedNet.TraceField(cyl, field.Family1, field.Exists, -1,
            new EvenlySpacedNet.Options { Spacing = 0.3 }, field.Family2);
        Assert.Empty(curves);
    }

    [Fact]
    public void Cylinder_CurvatureLines_AreCirclesAndRulings()
    {
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(cyl);

        var circle = CurvatureStreamlines.Trace(cyl, new[] { MiddleSeed }, pc,
            new CurvatureStreamlines.Options { UseMaxCurvature = true })[0];
        bool closed = (circle[0] - circle[circle.Length - 1]).Length < 1e-9;
        double polygonPerimeter = Segments * 2.0 * Math.Sin(Math.PI / Segments); // 6.2796 on the 64-gon
        Assert.True(closed, "Max-curvature line on a cylinder should close into a circle");
        Assert.True(Math.Abs(ArcLength(circle) - polygonPerimeter) < 0.005 * polygonPerimeter,
            $"Circle length {ArcLength(circle):F4} should match the 64-gon perimeter {polygonPerimeter:F4}");
        Assert.True(circle.Max(p => p.Z) - circle.Min(p => p.Z) < 1e-6, "Circle should stay in one plane");

        var ruling = CurvatureStreamlines.Trace(cyl, new[] { MiddleSeed }, pc,
            new CurvatureStreamlines.Options { UseMaxCurvature = false })[0];
        Assert.True(ChordDeviation(ruling) < 1e-3, $"Min-curvature line should be a straight ruling, deviation {ChordDeviation(ruling):E2} (border rows are jet-fitted, ~1e-4)");
        Assert.True(Math.Abs(ArcLength(ruling) - 2.0) < 0.01, $"Ruling should span the full height 2, got {ArcLength(ruling):F4}");
    }

    [Fact]
    public void Cylinder_Geodesics_AreRulingCircleAndHelix()
    {
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, Segments, Rows);
        var opts = new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 };

        // Along the axis: a straight ruling of length 2
        var ruling = GeodesicCurves.Trace(cyl, new[] { MiddleSeed }, new[] { new Vec3d(0, 0, 1) }, opts)[0];
        Assert.True(ChordDeviation(ruling) < 1e-6, "Axial geodesic should be a straight ruling");
        Assert.True(Math.Abs(ArcLength(ruling) - 2.0) < 0.01, $"Ruling length {ArcLength(ruling):F4} should be 2");

        // Across the axis: a closed circle
        var circle = GeodesicCurves.Trace(cyl, new[] { MiddleSeed }, new[] { new Vec3d(0, 1, 0) }, opts)[0];
        double polygonPerimeter = Segments * 2.0 * Math.Sin(Math.PI / Segments);
        Assert.True((circle[0] - circle[circle.Length - 1]).Length < 1e-9, "Circumferential geodesic should close");
        Assert.True(Math.Abs(ArcLength(circle) - polygonPerimeter) < 0.005 * polygonPerimeter,
            $"Circle length {ArcLength(circle):F4} vs {polygonPerimeter:F4}");

        // At 45° from the axis: a helix, z = z0 + R cot α · Δθ, staying on the cylinder
        double alpha = Math.PI / 4;
        var helix = GeodesicCurves.Trace(cyl, new[] { MiddleSeed },
            new[] { new Vec3d(0, Math.Sin(alpha), Math.Cos(alpha)) }, opts)[0];
        double theta = Math.Atan2(helix[0].Y, helix[0].X), unwrapped = 0, worstRise = 0, worstRadius = 0;
        for (int i = 0; i < helix.Length; i++)
        {
            double t = Math.Atan2(helix[i].Y, helix[i].X);
            double d = t - theta;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            unwrapped += d;
            theta = t;
            worstRise = Math.Max(worstRise, Math.Abs(helix[i].Z - (helix[0].Z + unwrapped / Math.Tan(alpha))));
            worstRadius = Math.Max(worstRadius, Math.Abs(Math.Sqrt(helix[i].X * helix[i].X + helix[i].Y * helix[i].Y) - 1.0));
        }
        Assert.True(helix.Min(p => p.Z) < -0.999 && helix.Max(p => p.Z) > 0.999, "Helix should run rim to rim");
        Assert.True(worstRadius < 0.002, $"Helix should stay on the cylinder, radius error {worstRadius:E2}");
        // With the direction re-derived from the projected travel the rise was
        // off by 0.024 (helix angle 44.65°) at step 0.02 and by 0.19 (42.2°) at
        // step 0.005 — a drift that grew with the number of steps; with
        // parallel transport it is 0.0008 (45.01°) and 0.0002 (45.00°)
        Assert.True(worstRise < 0.005, $"Helix rise should follow R cot α · Δθ, worst deviation {worstRise:F4}");
        double helixAngleDeg = Math.Atan(unwrapped / (helix[helix.Length - 1].Z - helix[0].Z)) * 180 / Math.PI;
        Assert.True(Math.Abs(helixAngleDeg - 45.0) < 0.1, $"Helix angle {helixAngleDeg:F3}° should be 45°");

        // Refining the step must not make it worse (it did: 8× more drift at a quarter of the step)
        var fine = GeodesicCurves.Trace(cyl, new[] { MiddleSeed },
            new[] { new Vec3d(0, Math.Sin(alpha), Math.Cos(alpha)) },
            new GeodesicCurves.Options { StepSize = 0.005, MaxSteps = 8000 })[0];
        double fineRise = 0; theta = Math.Atan2(fine[0].Y, fine[0].X); unwrapped = 0;
        for (int i = 0; i < fine.Length; i++)
        {
            double t = Math.Atan2(fine[i].Y, fine[i].X);
            double d = t - theta;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            unwrapped += d;
            theta = t;
            fineRise = Math.Max(fineRise, Math.Abs(fine[i].Z - (fine[0].Z + unwrapped / Math.Tan(alpha))));
        }
        Assert.True(fineRise < 0.002, $"Helix at step 0.005 should be at least as accurate, worst deviation {fineRise:F4}");
    }

    [Fact]
    public void Cylinder_LathAnalysis_HelixAndCircleMatchDarbouxTheory()
    {
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, Segments, Rows);
        var proj = new MeshProjection(cyl);
        var opts = new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 };

        // Helix at α = 45°: kn = sin²α = 0.5, |τg| = sin α cos α = 0.5, kg = 0
        double alpha = Math.PI / 4;
        var helix = GeodesicCurves.Trace(cyl, new[] { MiddleSeed },
            new[] { new Vec3d(0, Math.Sin(alpha), Math.Cos(alpha)) }, opts)[0];
        var la = LathAnalysis.Analyze(proj, helix);
        int n = helix.Length;
        var middle = Enumerable.Range(n / 4, n / 2).ToArray();
        double knErr = middle.Max(i => Math.Abs(Math.Abs(la.NormalCurvature[i]) - 0.5));
        double tgErr = middle.Max(i => Math.Abs(Math.Abs(la.GeodesicTorsion[i]) - 0.5));
        double kgMax = middle.Max(i => Math.Abs(la.GeodesicCurvature[i]));
        Assert.True(knErr < 0.02, $"Helix kn should be sin²α = 0.5, worst error {knErr:F4} (was 0.25 with the polyline form)");
        Assert.True(tgErr < 0.02, $"Helix τg should be sin α cos α = 0.5, worst error {tgErr:F4}");
        Assert.True(kgMax < 0.02, $"Helix is a geodesic: kg should be 0, got up to {kgMax:F4}");

        // Circle: kn = 1/R = 1, τg = 0, kg = 0
        var circle = GeodesicCurves.Trace(cyl, new[] { MiddleSeed }, new[] { new Vec3d(0, 1, 0) }, opts)[0];
        var lc = LathAnalysis.Analyze(proj, circle);
        double circleKnErr = lc.NormalCurvature.Max(k => Math.Abs(Math.Abs(k) - 1.0));
        Assert.True(circleKnErr < 0.01, $"Circle kn should be 1/R = 1, worst error {circleKnErr:F4} (was 0.11 with the polyline form)");
        Assert.True(lc.GeodesicTorsion.Max(Math.Abs) < 1e-3, "Circle on a cylinder has no geodesic torsion");
        Assert.True(lc.GeodesicCurvature.Max(Math.Abs) < 1e-3, "Circle on a cylinder is a geodesic");
    }

    // --------------------------------------------------------- hyperboloid

    [Fact]
    public void Hyperboloid_GaussianCurvature_MatchesClosedForm()
    {
        var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(hyp);
        var K = GaussianCurvature.Compute(hyp);
        var H = MeanCurvature.Compute(hyp).Values;

        double worst = 0;
        foreach (int v in InteriorVertices())
        {
            double truth = TestMeshes.HyperboloidGaussianCurvature(hyp.Vertices[v].Z);
            Assert.True(truth < 0, "Hyperboloid of one sheet has K < 0 everywhere");
            worst = Math.Max(worst, Math.Abs(K[v] - truth));
        }
        Assert.True(worst < 0.005, $"Angle-deficit K should match −1/(1 + 2u²)², worst error {worst:F4} (K ranges from −1 to −1/9)");

        // Throat (z = 0): H = 0 when a = c, k1 = 1 (parallel), k2 = −1 (meridian)
        for (int i = 0; i < Segments; i++)
        {
            int v = (Rows / 2) * Segments + i;
            Assert.True(Math.Abs(H[v]) < 0.005, $"Throat H should be 0, got {H[v]:F4}");
            Assert.True(Math.Abs(pc.K1[v] - 1.0) < 0.02, $"Throat k1 should be 1, got {pc.K1[v]:F4}");
            Assert.True(Math.Abs(pc.K2[v] + 1.0) < 0.03, $"Throat k2 should be −1, got {pc.K2[v]:F4}");
        }
    }

    [Fact]
    public void Hyperboloid_AsymptoticDirections_AreTheRulings()
    {
        var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(hyp);
        var field = AsymptoticCurves.ComputeDirections(pc);

        double worst = 0, sum = 0;
        int count = 0;
        foreach (int v in InteriorVertices())
        {
            Assert.True(field.Exists[v], $"Asymptotic directions should exist everywhere on the hyperboloid, missing at {v}");
            var (plus, minus) = TestMeshes.HyperboloidRulings(hyp.Vertices[v]);
            double a1 = Math.Min(AngleDeg(field.Family1[v], plus), AngleDeg(field.Family1[v], minus));
            double a2 = Math.Min(AngleDeg(field.Family2[v], plus), AngleDeg(field.Family2[v], minus));
            double a = Math.Max(a1, a2);
            worst = Math.Max(worst, a);
            sum += a;
            count++;
        }
        Assert.True(worst < 1.5, $"Asymptotic directions should coincide with the rulings, worst deviation {worst:F3}°");
        Assert.True(sum / count < 0.3, $"Mean deviation from the rulings should be tiny, got {sum / count:F3}°");
    }

    [Fact]
    public void Hyperboloid_AsymptoticTrace_IsAStraightRuling()
    {
        var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(hyp);
        var (plus, minus) = TestMeshes.HyperboloidRulings(hyp.Vertices[MiddleSeed]);
        double expectedLength = 2.0 * Math.Sqrt(2.0); // throat to both rims along a 45° ruling

        foreach (bool secondFamily in new[] { false, true })
        {
            var line = AsymptoticCurves.Trace(hyp, new[] { MiddleSeed }, pc, secondFamily,
                new AsymptoticCurves.Options { StepSize = 0.02 })[0];
            Vec3d chord = line[line.Length - 1] - line[0];
            double align = Math.Min(AngleDeg(chord, plus), AngleDeg(chord, minus));
            double dev = ChordDeviation(line);

            Assert.True(dev < 0.02, $"Asymptotic curve should be a straight ruling, chord deviation {dev:F4} over length {ArcLength(line):F3}");
            Assert.True(align < 0.5, $"Ruling direction off by {align:F3}°");
            Assert.True(Math.Abs(ArcLength(line) - expectedLength) < 0.01 * expectedLength,
                $"Ruling length {ArcLength(line):F4} should be 2√2 = {expectedLength:F4}");
            Assert.True(line.Min(p => p.Z) < -0.999 && line.Max(p => p.Z) > 0.999, "Ruling should reach both rims");
        }
    }

    [Fact]
    public void Hyperboloid_AsymptoticNet_IsTheRulingNet()
    {
        var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(hyp);
        var field = AsymptoticCurves.ComputeDirections(pc);
        var proj = new MeshProjection(hyp);

        var famA = EvenlySpacedNet.TraceField(hyp, field.Family1, field.Exists, -1,
            new EvenlySpacedNet.Options { Spacing = 0.25 }, field.Family2);
        var famB = EvenlySpacedNet.TraceField(hyp, field.Family2, field.Exists, -1,
            new EvenlySpacedNet.Options { Spacing = 0.25 }, field.Family1);

        Assert.True(famA.Count >= 20 && famB.Count >= 20, $"Both families should fill the hyperboloid, got {famA.Count} + {famB.Count}");
        Assert.True(Math.Abs(famA.Count - famB.Count) <= 2, $"Families should be balanced, got {famA.Count} vs {famB.Count}");

        int wrong = 0, samples = 0;
        foreach (var (family, self, other) in new[] { (famA, field.Family1, field.Family2), (famB, field.Family2, field.Family1) })
        {
            foreach (var c in family)
            {
                Assert.True(ChordDeviation(c) < 0.02, $"Every net curve should be a straight ruling, deviation {ChordDeviation(c):F4}");
                Assert.True(Math.Abs(Math.Abs(c[0].Z) - 1.0) < 1e-3 && Math.Abs(Math.Abs(c[c.Length - 1].Z) - 1.0) < 1e-3,
                    $"Every ruling should run rim to rim, ends at z = {c[0].Z:F3} and {c[c.Length - 1].Z:F3}");
                for (int i = 1; i < c.Length; i += 5)
                {
                    Vec3d t = (c[i] - c[i - 1]).Normalized();
                    int vi = proj.NearestVertexGlobal(c[i]);
                    if (!field.Exists[vi]) continue;
                    samples++;
                    if (Math.Abs(Vec3d.Dot(t, other[vi])) > Math.Abs(Vec3d.Dot(t, self[vi])) + 0.2) wrong++;
                }
            }
        }
        Assert.True(wrong == 0, $"{wrong} of {samples} samples follow the other family");
    }

    [Fact]
    public void Hyperboloid_LathAnalysis_RulingsHaveZeroNormalCurvatureAndTorsionSqrtMinusK()
    {
        var hyp = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, Segments, Rows);
        var pc = PrincipalCurvature.Compute(hyp);
        var proj = new MeshProjection(hyp);

        var signs = new List<int>();
        foreach (bool secondFamily in new[] { false, true })
        {
            var line = AsymptoticCurves.Trace(hyp, new[] { MiddleSeed }, pc, secondFamily,
                new AsymptoticCurves.Options { StepSize = 0.02 })[0];
            var la = LathAnalysis.Analyze(proj, line, new LathAnalysis.Options { Upright = true, Width = 0.1, Thickness = 0.01, MaxStrain = 0.005 });

            int n = line.Length;
            var middle = Enumerable.Range(n / 4, n / 2).ToArray();
            double knMax = middle.Max(i => Math.Abs(la.NormalCurvature[i]));
            double kgMax = middle.Max(i => Math.Abs(la.GeodesicCurvature[i]));
            double tgErr = middle.Max(i => Math.Abs(Math.Abs(la.GeodesicTorsion[i]) - Math.Sqrt(-TestMeshes.HyperboloidGaussianCurvature(line[i].Z))));

            // Polyline second differences gave |kn| up to 0.18 here (facet noise);
            // the normal-rotation form gives ≤ 0.004
            Assert.True(knMax < 0.02, $"Ruling normal curvature should be 0, got up to {knMax:F4}");
            Assert.True(kgMax < 0.03, $"Ruling geodesic curvature should be 0, got up to {kgMax:F4}");
            Assert.True(tgErr < 0.02, $"Ruling torsion should be √−K (Beltrami–Enneper), worst error {tgErr:F4}");
            signs.Add(Math.Sign(middle.Sum(i => la.GeodesicTorsion[i])));

            // Upright 100 × 10 mm lath at the throat (K = −1, τg = 1): twist strain
            // τ·t/√3 = 0.01 / 1.732 = 0.00577 → utilization 1.155 at MaxStrain 0.005,
            // with no contribution from kn (which the polyline form put at ~0.18,
            // i.e. a spurious +1.8 of hard-axis utilization)
            int throat = Enumerable.Range(0, n).OrderBy(i => Math.Abs(line[i].Z)).First();
            double expected = (1.0 * 0.01 / Math.Sqrt(3.0)) / 0.005;
            Assert.True(Math.Abs(la.Utilization[throat] - expected) < 0.05,
                $"Throat utilization should be twist-only ≈ {expected:F3}, got {la.Utilization[throat]:F3}");
        }
        Assert.True(signs[0] * signs[1] < 0, "The two ruling families must twist in opposite senses");
    }
}
