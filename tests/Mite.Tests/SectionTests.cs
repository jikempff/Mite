using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// One profile for every curve — and for every check: Lath Sweep, Lath
/// Analysis and Gridshell Analysis all read the section of the same
/// LathProfile. Closed forms: rectangle and round bar (Timoshenko &amp;
/// Goodier 1970 §109 for rectangle torsion), Green's theorem for polygons,
/// Roark's J ≈ A⁴/(40 I_p) for solid custom sections.
/// </summary>
public class SectionTests
{
    [Fact]
    public void Rectangle_SectionProperties_MatchClosedForms()
    {
        var sp = new LathProfile(0.1, 0.01).SectionProperties();
        Assert.Equal(0.001, sp.Area, 12);
        Assert.Equal(0.1 * 1e-6 / 12, sp.IA, 15);        // through the thickness
        Assert.Equal(0.01 * 1e-3 / 12, sp.IB, 15);       // across the width
        Assert.Equal(0.05, sp.ExtentA, 12);
        Assert.Equal(0.005, sp.ExtentB, 12);
        // Thin strip: J → b t³/3 (within 7 % for b/t = 10), γ_max → τ·t
        Assert.InRange(sp.J / (0.1 * 1e-6 / 3), 0.93, 1.0);
        Assert.InRange(sp.TwistLength / 0.01, 0.99, 1.0);

        // Square: J = 0.1406 a⁴ and γ_max = 0.675 τ a (Timoshenko & Goodier)
        var sq = new LathProfile(0.02, 0.02).SectionProperties();
        Assert.InRange(sq.J / (0.1406 * Math.Pow(0.02, 4)), 0.99, 1.01);
        Assert.InRange(sq.TwistLength / 0.02, 0.67, 0.68);
    }

    [Fact]
    public void Round_SectionProperties_AreTheCircleFormulas()
    {
        double d = 0.03;
        var sp = LathProfile.Round(d).SectionProperties();
        Assert.Equal(Math.PI * d * d / 4, sp.Area, 12);
        Assert.Equal(Math.PI * Math.Pow(d, 4) / 64, sp.IA, 15);
        Assert.Equal(sp.IA, sp.IB, 15);
        Assert.Equal(Math.PI * Math.Pow(d, 4) / 32, sp.J, 15);
        Assert.Equal(0.5 * d, sp.ExtentA, 12);
        Assert.Equal(0.5 * d, sp.TwistLength, 12); // γ_max = τ r on a solid shaft
        Assert.Equal(SectionKind.Round, LathProfile.Round(d).Kind);
        Assert.Equal(SectionKind.Rectangle, new LathProfile(1, 1).Kind);
    }

    [Fact]
    public void Custom_Polygon_MatchesRectangleAndCircleWithinTheEstimates()
    {
        // A square given as a custom polygon: exact area and second moments,
        // Roark's J within 7 % of Timoshenko's 0.1406 a⁴
        double a = 0.02;
        var square = LathProfile.Custom(new[] { (-a / 2, -a / 2), (a / 2, -a / 2), (a / 2, a / 2), (-a / 2, a / 2), (-a / 2, -a / 2) });
        Assert.Equal(SectionKind.Custom, square.Kind);
        var sq = square.SectionProperties();
        var reference = new LathProfile(a, a).SectionProperties();
        Assert.Equal(reference.Area, sq.Area, 12);
        Assert.Equal(reference.IA, sq.IA, 15);
        Assert.Equal(reference.IB, sq.IB, 15);
        Assert.Equal(reference.ExtentA, sq.ExtentA, 12);
        Assert.InRange(sq.J / reference.J, 0.95, 1.08);
        Assert.Equal(reference.TwistLength, sq.TwistLength, 12);

        // A 48-gon given as a custom polygon vs the round bar
        double d = 0.03;
        var poly = Enumerable.Range(0, 48).Select(i => (0.5 * d * Math.Cos(2 * Math.PI * i / 48), 0.5 * d * Math.Sin(2 * Math.PI * i / 48)));
        var circ = LathProfile.Custom(poly).SectionProperties();
        var round = LathProfile.Round(d).SectionProperties();
        Assert.InRange(circ.Area / round.Area, 0.99, 1.0);
        Assert.InRange(circ.IA / round.IA, 0.98, 1.0);
        Assert.InRange(circ.J / round.J, 0.95, 1.02);

        // Off-centre polygon: moments are about the centroid, not the origin
        var shifted = LathProfile.Custom(new[] { (1.0, 1.0), (1.0 + a, 1.0), (1.0 + a, 1.0 + a), (1.0, 1.0 + a) }).SectionProperties();
        Assert.Equal(reference.IA, shifted.IA, 15);
        Assert.Equal(reference.ExtentB, shifted.ExtentB, 12);
    }

    [Fact]
    public void LathAnalysis_RoundBar_UsesTheDiameterForEveryMode()
    {
        // 45° helix on the unit cylinder: kn = 0.5, |τg| = 0.5, kg = 0
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 64, 32);
        var proj = new MeshProjection(cyl);
        double alpha = Math.PI / 4;
        var helix = GeodesicCurves.Trace(cyl, new[] { 16 * 64 }, // vertex on the middle row
            new[] { new Vec3d(0, Math.Sin(alpha), Math.Cos(alpha)) }, new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 })[0];

        double d = 0.02, limit = 0.005;
        // Round bar Ø d: bending strain kn·d/2 = 0.005 whichever way it is "oriented";
        // twist τ·(d/2)/√3 = 0.0029 is smaller, so utilization = 1.0
        var flat = LathAnalysis.Analyze(proj, helix, new LathAnalysis.Options { Profile = LathProfile.Round(d), MaxStrain = limit, Upright = false });
        var up = LathAnalysis.Analyze(proj, helix, new LathAnalysis.Options { Profile = LathProfile.Round(d), MaxStrain = limit, Upright = true });
        Assert.InRange(flat.MaxUtilization, 0.97, 1.03);
        Assert.Equal(flat.MaxUtilization, up.MaxUtilization, 12); // Upright is meaningless for a round bar

        // The same helix as a flat 0.05 × 0.01 strip: twist governs (0.5·0.01·k/√3 / 0.005 ≈ 0.57);
        // upright, kn bends it the hard way: 0.5·0.025 / 0.005 = 2.5
        var strip = LathAnalysis.Analyze(proj, helix, new LathAnalysis.Options { Width = 0.05, Thickness = 0.01, MaxStrain = limit });
        var stripUp = LathAnalysis.Analyze(proj, helix, new LathAnalysis.Options { Width = 0.05, Thickness = 0.01, MaxStrain = limit, Upright = true });
        Assert.InRange(strip.MaxUtilization, 0.55, 0.60);
        Assert.InRange(stripUp.MaxUtilization, 2.4, 2.6);

        // Profile overrides Width/Thickness/Upright when given
        var viaProfile = LathAnalysis.Analyze(proj, helix, new LathAnalysis.Options { Width = 9, Thickness = 9, Profile = new LathProfile(0.05, 0.01, upright: true), MaxStrain = limit });
        Assert.Equal(stripUp.MaxUtilization, viaProfile.MaxUtilization, 12);
    }

    [Fact]
    public void FrameAnalysis_RoundBar_DeflectsWithTheCircleInertia()
    {
        // Cantilever under a UDL, δ = q L⁴ / (8 E I): I = π d⁴ / 64 for the bar,
        // w t³ / 12 for the strip — same code path, different section
        var mesh = TestMeshes.CreateQuadGrid(2, 2, 10.0);
        double L = 10, q = 100, E = 11e9, d = 0.06;
        var lath = new Vec3d[21];
        for (int i = 0; i <= 20; i++) lath[i] = new Vec3d(i * L / 20, 5, 0);
        var round = FrameAnalysis.Compute(mesh, new[] { lath }, null, new[] { new Vec3d(0, 5, 0) },
            LathProfile.Round(d), new Vec3d(0, 0, -q), new FrameAnalysis.Options { E = E });
        double tip = q * Math.Pow(L, 4) / (8 * E * Math.PI * Math.Pow(d, 4) / 64);
        Assert.InRange(round.MaxDisplacement / tip, 0.98, 1.02);

        var strip = FrameAnalysis.Compute(mesh, new[] { lath }, null, new[] { new Vec3d(0, 5, 0) },
            new LathProfile(0.1, 0.05), new Vec3d(0, 0, -q), new FrameAnalysis.Options { E = E });
        double tipStrip = q * Math.Pow(L, 4) / (8 * E * 0.1 * Math.Pow(0.05, 3) / 12);
        Assert.InRange(strip.MaxDisplacement / tipStrip, 0.98, 1.02);
    }
}
