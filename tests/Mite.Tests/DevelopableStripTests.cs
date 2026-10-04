using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Developable lath strips (Tang et al. 2016; Schling et al. 2022 §3.4;
/// Starostin &amp; van der Heijden 2007) against closed forms.
/// </summary>
public class DevelopableStripTests
{
    private readonly ITestOutputHelper _out;
    public DevelopableStripTests(ITestOutputHelper output) { _out = output; }

    private static (Vec3d[] pts, Vec3d[] nrm) CatenoidLath(int count, double v0 = -1, double v1 = 1)
    {
        var pts = new Vec3d[count];
        var nrm = new Vec3d[count];
        for (int i = 0; i < count; i++)
        {
            double v = v0 + (v1 - v0) * i / (count - 1);
            pts[i] = TestMeshes.CatenoidAsymptoticPoint(v);
            nrm[i] = TestMeshes.CatenoidNormal(pts[i]);
        }
        return (pts, nrm);
    }

    private static (Vec3d[] pts, Vec3d[] nrm) Helix(double alphaDeg, int count, double R = 1, double z0 = -1, double z1 = 1)
    {
        double al = alphaDeg * Math.PI / 180;   // angle to the axis
        var pts = new Vec3d[count]; var nrm = new Vec3d[count];
        for (int i = 0; i < count; i++)
        {
            double z = z0 + (z1 - z0) * i / (count - 1);
            double phi = (z - z0) * Math.Tan(al) / R;
            pts[i] = new Vec3d(R * Math.Cos(phi), R * Math.Sin(phi), z);
            nrm[i] = new Vec3d(Math.Cos(phi), Math.Sin(phi), 0);
        }
        return (pts, nrm);
    }

    [Fact]
    public void Helix60_FlatStrip_IsTheCylinder()
    {
        // A helix at 60° to the axis of a unit cylinder is a geodesic with κn = sin²60°,
        // τg = sin60° cos60°: η = τ/κ = cot 60°, so the tangent developable's rulings
        // run parallel to the axis (the cylinder itself) and the band develops straight.
        var (pts, nrm) = Helix(60, 121);
        var r = DevelopableStrip.Build(pts, nrm, new DevelopableStrip.Options { Width = 0.1, Upright = false })!;
        Assert.True(r.Developable && r.DevelopableFraction == 1.0, r.Note);
        var z = new Vec3d(0, 0, 1);
        double par = r.Rulings.Max(v => Vec3d.Cross(v, z).Length);
        Assert.True(par < 1e-9, $"rulings off the axis by {par:E2}");
        // the discrete ruling angle is the chord's: tan θ = 2 sin(Δφ/2) / Δz
        double dz = 2.0 / 120, dphi = dz * Math.Tan(Math.PI / 3);
        double chordDeg = Math.Atan2(2 * Math.Sin(dphi / 2), dz) * 180 / Math.PI;
        double ang = r.RulingAngle.Max(x => Math.Abs(Math.Min(x, 180 - x) - chordDeg));
        Assert.True(ang < 1e-9, $"ruling angle off the chord's {chordDeg}° by {ang:E2}");
        Assert.True(Math.Abs(chordDeg - 60) < 1e-3);
        Assert.True(r.Bow < 1e-12 && r.MaxDevelopedCurvature < 1e-9, $"bow {r.Bow:E2}, curvature {r.MaxDevelopedCurvature:E2}");
        Assert.True(r.MaxPlanarity < 1e-12 && r.EdgeStrain < 1e-12, $"planarity {r.MaxPlanarity:E2}, edge strain {r.EdgeStrain:E2}");
        // Flat band: both boundaries exactly W/2 from the developed centreline
        Vec3d o = r.FlatCentre[0], d = (r.FlatCentre[^1] - o).Normalized();
        double wErr = 0;
        foreach (var (f, sgn) in r.FlatA.Select(f => (f, -1.0)).Concat(r.FlatB.Select(f => (f, 1.0))))
            wErr = Math.Max(wErr, Math.Abs(sgn * Vec3d.Cross(d, f - o).Z - 0.05));
        Assert.True(wErr < 1e-12, $"band half-width off by {wErr:E2}");
        double flatLen = (r.FlatCentre[^1] - r.FlatCentre[0]).Length;
        Assert.True(Math.Abs(flatLen - r.Length) < 1e-12, $"flat length {flatLen} vs {r.Length}");
        _out.WriteLine($"helix 60°: rulings ∥ axis to {par:E1}, bow {r.Bow:E1}, width error {wErr:E1}");
    }

    [Fact]
    public void GreatCircle_ClosedFlatStrip_IsACylinderBand()
    {
        int n = 96;
        var pts = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) { double a = 2 * Math.PI * (i % n) / n; pts[i] = new Vec3d(Math.Cos(a), Math.Sin(a), 0); }
        var r = DevelopableStrip.Build(pts, pts.ToArray(), new DevelopableStrip.Options { Width = 0.2, Upright = false })!;
        Assert.True(r.Closed && r.Developable, r.Note);
        Assert.Equal(n + 1, r.Centre.Length);
        Assert.True(r.Rulings.Max(v => Vec3d.Cross(v, new Vec3d(0, 0, 1)).Length) < 1e-12);
        Assert.True(r.EdgeA.Max(p => Math.Abs(Math.Abs(p.Z) - 0.1)) < 1e-12 && r.EdgeB.Max(p => Math.Abs(Math.Abs(p.Z) - 0.1)) < 1e-12);
        Assert.True(r.Bow < 1e-12, $"bow {r.Bow:E2}");
        Assert.True(Math.Abs((r.FlatCentre[^1] - r.FlatCentre[0]).Length - n * 2 * Math.Sin(Math.PI / n)) < 1e-12);
    }

    [Fact]
    public void LatitudeCircle_FlatStrip_IsTheTangentCone()
    {
        // Flat lath on the latitude φ0 = 60° of the unit sphere (τg = 0, κg = cot φ0): the
        // tangent developable is the tangent cone, which unrolls to a circular arc of radius
        // tan φ0. The discrete strip is a pyramid whose apex sits on the axis at
        // z* = (sin²φ0 cos²(Δ/2) + cos²φ0)/cos φ0, so the developed polygon has
        // circumradius |apex − c| exactly.
        double phi0 = Math.PI / 3;
        foreach (int count in new[] { 61, 181 })
        {
            double sweep = 1.5 * Math.PI, step = sweep / (count - 1);
            var pts = new Vec3d[count];
            for (int i = 0; i < count; i++) { double a = step * i; pts[i] = new Vec3d(Math.Sin(phi0) * Math.Cos(a), Math.Sin(phi0) * Math.Sin(a), Math.Cos(phi0)); }
            var r = DevelopableStrip.Build(pts, pts.ToArray(), new DevelopableStrip.Options { Width = 0.1, Upright = false })!;
            Assert.True(r.Developable, r.Note);
            double zs = (Math.Pow(Math.Sin(phi0) * Math.Cos(step / 2), 2) + Math.Pow(Math.Cos(phi0), 2)) / Math.Cos(phi0);
            double rhoDiscrete = (new Vec3d(0, 0, zs) - pts[0]).Length;
            double worst = 0;
            for (int k = 1; k + 1 < count; k++)
            {
                Vec3d d0 = r.FlatCentre[k] - r.FlatCentre[k - 1], d1 = r.FlatCentre[k + 1] - r.FlatCentre[k];
                double turn = Math.Abs(Math.Atan2(d0.X * d1.Y - d0.Y * d1.X, d0.X * d1.X + d0.Y * d1.Y));
                worst = Math.Max(worst, Math.Abs(d1.Length / (2 * Math.Sin(turn / 2)) - rhoDiscrete));
            }
            Assert.True(worst < 1e-9, $"developed circumradius off the pyramid's by {worst:E2}");
            double rel = Math.Abs(rhoDiscrete - Math.Tan(phi0)) / Math.Tan(phi0);
            Assert.True(rel < (count < 100 ? 2e-3 : 2e-4), $"pyramid radius {rhoDiscrete} vs tan φ0 {Math.Tan(phi0)}");
            Assert.True(Math.Abs(r.MaxDevelopedCurvature * rhoDiscrete - 1) < 1e-3, $"developed curvature {r.MaxDevelopedCurvature} vs {1 / rhoDiscrete}");
            _out.WriteLine($"latitude 60°, {count} stations: developed radius {rhoDiscrete:F6} (tan φ0 = {Math.Tan(phi0):F6}, rel {rel:E1}), polygon error {worst:E1}");
        }
    }

    [Fact]
    public void Catenoid_AsymptoticUpright_RulingsConvergeToTheDarbouxVector()
    {
        // v ∈ [1.2, 2.2]: |τg| < |κg| (η = τ/κ from −0.95 to −0.28), so the rectifying
        // developable is the strip; rulings must converge to κg n + τg t (Tang et al. 2016)
        // at second order, every quad planar, the band straight.
        double prev = double.NaN;
        foreach (int count in new[] { 41, 81, 161 })
        {
            var (pts, nrm) = CatenoidLath(count, 1.2, 2.2);
            var r = DevelopableStrip.Build(pts, nrm, new DevelopableStrip.Options { Width = 0.1, Upright = true })!;
            Assert.True(r.Developable, r.Note);
            double worst = 0;
            for (int i = 1; i < count - 1; i++)
            {
                double v = 1.2 + 1.0 * i / (count - 1);
                worst = Math.Max(worst, Math.Abs(r.RulingAngle[i] - TestMeshes.CatenoidAsymptoticDarboux(v).thetaDeg));
            }
            Assert.True(r.MaxPlanarity < 1e-13 && r.EdgeStrain < 1e-10, $"planarity {r.MaxPlanarity:E2}, strain {r.EdgeStrain:E2}");
            if (!double.IsNaN(prev)) Assert.True(worst < prev / 3.5, $"ruling error {worst:E2} after {prev:E2}: not second order");
            Assert.True(r.Bow < 2e-4 * Math.Pow(41.0 / count, 2), $"bow {r.Bow:E2}");
            _out.WriteLine($"catenoid v ∈ [1.2, 2.2], {count} stations: ruling angle error {worst:E2}°, bow {r.Bow:E2}, planarity {r.MaxPlanarity:E1}");
            prev = worst;
        }
    }

    [Fact]
    public void Catenoid_ThroatIsTheTwistRegime()
    {
        // Across the throat the asymptotic line inflects (κ = 0, τ = 1): no developable.
        // The developable share must match the closed-form share of |τ| ≤ |κ|, and the
        // edge strain at the throat is that of a helicoid, (W/2)² τ² / 2.
        double share = TestMeshes.CatenoidDevelopableShare(-2, 2);
        var (pts, nrm) = CatenoidLath(161, -2, 2);
        double w = 0.08;
        var r = DevelopableStrip.Build(pts, nrm, new DevelopableStrip.Options { Width = w, Upright = true })!;
        Assert.False(r.Developable);
        Assert.True(Math.Abs(r.DevelopableFraction - share) < 0.02, $"developable share {r.DevelopableFraction:F3} vs closed form {share:F3}");
        double strainTheory = Math.Sqrt(1 + w * w / 4) - 1;
        Assert.True(Math.Abs(r.EdgeStrain - strainTheory) < 0.01 * strainTheory, $"edge strain {r.EdgeStrain:E3} vs {strainTheory:E3}");
        Assert.True(r.MinAdvance > 0.5 && r.LeanUsed == 45, $"advance {r.MinAdvance}, lean {r.LeanUsed}");
        Assert.Contains("κ = 0", r.Note);
        // The twisted stretch is the one around the throat
        for (int k = 0; k < r.Centre.Length; k++)
            if (Math.Abs(r.Centre[k].Z) < 0.9) Assert.False(r.OnDevelopable[k], $"station at z = {r.Centre[k].Z:F2} should twist");
        _out.WriteLine($"catenoid v ∈ [−2, 2], W = {w}: developable {r.DevelopableFraction:P1} (closed form {share:P1}), edge strain {r.EdgeStrain:E3} (helicoid {strainTheory:E3})");
    }

    [Fact]
    public void HyperboloidRuling_IsATwistedHelicoid()
    {
        // The ruling (1, s, s)/… of x² + y² − z² = 1 is straight: κ = 0, τg = 1/(1 + 2z²).
        // No developable exists; the strip is the helicoid with edge strain √(1 + (Wτ/2)²) − 1.
        int count = 101; double w = 0.1;
        var pts = new Vec3d[count]; var nrm = new Vec3d[count];
        for (int i = 0; i < count; i++) { double z = -1 + 2.0 * i / (count - 1); pts[i] = new Vec3d(1, z, z); nrm[i] = new Vec3d(1, -z, z).Normalized(); }
        var r = DevelopableStrip.Build(pts, nrm, new DevelopableStrip.Options { Width = w, Upright = true })!;
        Assert.False(r.Developable);
        Assert.Equal(0.0, r.DevelopableFraction);
        Assert.Equal(90.0, r.MaxDevelopableLean, 6);
        double theory = Math.Sqrt(1 + w * w / 4) - 1;
        Assert.True(Math.Abs(r.EdgeStrain - theory) < 0.005 * theory, $"edge strain {r.EdgeStrain:E4} vs {theory:E4}");
        double square = r.Rulings.Select((v, k) => Math.Abs(Vec3d.Dot(v, (pts[Math.Min(k + 1, count - 1)] - pts[Math.Max(k - 1, 0)]).Normalized()))).Max();
        Assert.True(square < 1e-9, $"helicoid rulings not square to the lath: {square:E2}");
        Assert.True(r.MaxTwistGap > 1e-4);
    }

    [Fact]
    public void Helix30_FlatStrip_TwistsBeyondFortyFiveDegrees()
    {
        // At 30° to the axis η = τ/κ = cot 30° > 1: Freddi et al.'s twist regime. Rulings are
        // mirrored to cot θ = 1/η, so the edge strain is √(1 + (W/2)²(τ − κ²/τ)²) − 1.
        var (pts, nrm) = Helix(30, 121);
        double w = 0.1;
        var r = DevelopableStrip.Build(pts, nrm, new DevelopableStrip.Options { Width = w, Upright = false })!;
        Assert.Equal(0.0, r.DevelopableFraction);
        double al = Math.PI / 6, kn = Math.Sin(al) * Math.Sin(al), tg = Math.Sin(al) * Math.Cos(al);
        double theory = Math.Sqrt(1 + w * w / 4 * Math.Pow(tg - kn * kn / tg, 2)) - 1;
        Assert.True(Math.Abs(r.EdgeStrain - theory) < 0.01 * theory, $"edge strain {r.EdgeStrain:E4} vs {theory:E4}");
        double dz = 2.0 / 120, dphi = dz * Math.Tan(al);
        Assert.Equal(90 - Math.Atan2(2 * Math.Sin(dphi / 2), dz) * 180 / Math.PI, r.MaxDevelopableLean, 9);
        // with the limit opened to 90° the developable (the cylinder) is accepted again
        var open = DevelopableStrip.Build(pts, nrm, new DevelopableStrip.Options { Width = w, Upright = false, MaxLean = 90 })!;
        Assert.True(open.Developable && open.EdgeStrain < 1e-12 && open.Rulings.Max(v => Vec3d.Cross(v, new Vec3d(0, 0, 1)).Length) < 1e-9);
    }

    [Fact]
    public void TracedCatenoidNet_MatchesTheClosedForm()
    {
        // Asymptotic Net on a 64 × 64 catenoid mesh (z ∈ [−2, 2]): rulings from the mesh's
        // smooth normals over a window of four mesh edges either side. Every lath crosses the throat.
        var mesh = TestMeshes.CreateCatenoid(1.0, 4.0, 64, 64);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc);
        var A = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.3 }, field.Family2);
        var proj = new MeshProjection(mesh);
        double share = TestMeshes.CatenoidDevelopableShare(-2, 2), w = 0.08;
        double strainTheory = Math.Sqrt(1 + w * w / 4) - 1;
        double fracMin = 1, fracMax = 0, strainMax = 0, bowMax = 0, planarMax = 0;
        int full = 0;
        foreach (var l in A)
        {
            if (Math.Abs(l[0].Z - l[^1].Z) < 3.9) continue;   // rim to rim only
            full++;
            var r = DevelopableStrip.Build(proj, l, new DevelopableStrip.Options { Width = w, Upright = true })!;
            fracMin = Math.Min(fracMin, r.DevelopableFraction); fracMax = Math.Max(fracMax, r.DevelopableFraction);
            strainMax = Math.Max(strainMax, r.EdgeStrain); bowMax = Math.Max(bowMax, r.Bow); planarMax = Math.Max(planarMax, r.MaxPlanarity);
        }
        Assert.True(full >= 15, $"{full} rim-to-rim laths");
        Assert.True(fracMin > share - 0.03 && fracMax < share + 0.03, $"developable share {fracMin:F3}…{fracMax:F3} vs {share:F3}");
        // the four-edge window averages the twist across the throat's peak: a few % low
        Assert.True(Math.Abs(strainMax - strainTheory) < 0.06 * strainTheory, $"edge strain {strainMax:E3} vs {strainTheory:E3}");
        // The bow is the traced polyline's own: on this triangulated mesh one asymptotic family
        // runs along the triangle diagonals and the other across them, so the polylines carry a
        // normal curvature of a few 1e-2 that the exact curve does not have (bow 2e-3 there).
        Assert.True(bowMax < 0.06, $"bow {bowMax}");
        Assert.True(planarMax < 1e-3 * w, $"planarity {planarMax:E2}");
        _out.WriteLine($"traced catenoid: {full} laths, developable {fracMin:P1}…{fracMax:P1} (closed form {share:P1}), edge strain ≤ {strainMax:E3} ({strainTheory:E3}), bow ≤ {bowMax:E2}, planarity ≤ {planarMax:E1}");
    }

    [Fact]
    public void TracedHelices_OnACylinderMesh()
    {
        // Geodesic Net helices on a 64-gon cylinder, flat 100 mm planks, the default window:
        // at 60° the strip is the cylinder (rulings along the axis, no edge strain), at 30°
        // it twists with the closed-form edge strain of the exact helix.
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 64, 32);
        var proj = new MeshProjection(cyl);
        foreach (double deg in new[] { 60.0, 30.0 })
        {
            double al = deg * Math.PI / 180;
            var g = GeodesicCurves.Trace(cyl, new[] { 16 * 64 }, new[] { new Vec3d(0, Math.Sin(al), Math.Cos(al)) }, new GeodesicCurves.Options { StepSize = 0.02, MaxSteps = 2000 })[0];
            var r = DevelopableStrip.Build(proj, g, new DevelopableStrip.Options { Width = 0.1, Upright = false })!;
            int ns = r.Centre.Length;
            double axis = Enumerable.Range(ns / 8, 3 * ns / 4).Max(k => Math.Acos(Math.Min(1, Math.Abs(r.Rulings[k].Z))) * 180 / Math.PI);
            if (deg > 45)
            {
                Assert.True(r.Developable, r.Note);
                Assert.True(axis < 2.0 && r.EdgeStrain < 5e-5 && r.MaxPlanarity < 2e-3 * 0.1, $"60°: rulings {axis:F2}° off the axis, edge strain {r.EdgeStrain:E2}, planarity {r.MaxPlanarity:E2}");
            }
            else
            {
                double kn = Math.Sin(al) * Math.Sin(al), tg = Math.Sin(al) * Math.Cos(al);
                double theory = Math.Sqrt(1 + 0.0025 * Math.Pow(tg - kn * kn / tg, 2)) - 1;
                Assert.Equal(0.0, r.DevelopableFraction);
                Assert.True(Math.Abs(r.EdgeStrain - theory) < 0.06 * theory, $"30°: edge strain {r.EdgeStrain:E3} vs {theory:E3}");
            }
            _out.WriteLine($"helix {deg}° on the mesh: developable {r.DevelopableFraction:P0}, rulings ≤ {axis:F2}° from the axis (mid 3/4), edge strain {r.EdgeStrain:E2}, bow {r.Bow:E1}");
        }
    }

    [Fact]
    public void TooWide_ReportsTheEdgeOfRegression()
    {
        // Flat lath on the latitude φ0 = 10° (developed radius tan 10° = 0.176): a 0.5 wide band
        // would wrap past the cone's apex, so its inner boundary runs backwards.
        double phi0 = 10 * Math.PI / 180;
        var pts = Enumerable.Range(0, 41).Select(i => { double a = Math.PI * i / 40; return new Vec3d(Math.Sin(phi0) * Math.Cos(a), Math.Sin(phi0) * Math.Sin(a), Math.Cos(phi0)); }).ToArray();
        var r = DevelopableStrip.Build(pts, pts.ToArray(), new DevelopableStrip.Options { Width = 0.5, Upright = false })!;
        Assert.True(r.MinAdvance < 0, $"advance {r.MinAdvance}");
        Assert.Contains("backwards", r.Note);
        var ok = DevelopableStrip.Build(pts, pts.ToArray(), new DevelopableStrip.Options { Width = 0.2, Upright = false })!;
        Assert.True(ok.Developable && ok.MinAdvance > 0, ok.Note);
    }

    [Fact]
    public void TwoPoints_AndCompareWithLathUnroll()
    {
        var two = DevelopableStrip.Build(new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0) }, new[] { new Vec3d(0, 0, 1), new Vec3d(0, 0, 1) }, new DevelopableStrip.Options { Width = 0.1, Upright = true })!;
        Assert.True(two.Developable);
        Assert.Equal(0.05, two.EdgeB[0].Z, 12);
        Assert.Equal(-0.05, two.EdgeA[1].Z, 12);
        Assert.Null(DevelopableStrip.Build(new[] { new Vec3d(0, 0, 0), new Vec3d(0, 0, 0) }, new[] { new Vec3d(0, 0, 1), new Vec3d(0, 0, 1) }));
    }
}
