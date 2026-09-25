using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// The minimal-surface catalogue of the Studio X submission (Kempff, Srisuta,
/// Thanachanan, Hao 2026, "Adaptive Behaviour in Asymptotic Gridshells",
/// p. 48): concave cylinder (catenoid), ruled surface (bilinear patch), 2-fold
/// and 3-fold Enneper, plus the Schwarz D surface of Schling's asymptotic
/// pavilion. Ground truth: on a minimal surface H = 0 and the asymptotic
/// directions are orthogonal (do Carmo 1976 §3-2); Enneper's asymptotic
/// directions follow from its Hopf differential (n−1) w^(n−2) dw²; the
/// catenoid has K = −1/(c² cosh⁴(z/c)) with asymptotic directions at 45° to
/// the parallels; a bilinear patch is doubly ruled by its iso-lines.
/// </summary>
public class MinimalSurfaceCatalogueTests
{
    private static double AngleDeg(Vec3d a, Vec3d b) =>
        Math.Acos(Math.Min(1.0, Math.Abs(Vec3d.Dot(a.Normalized(), b.Normalized())))) * 180.0 / Math.PI;

    private static double ArcLength(Vec3d[] line)
    {
        double s = 0;
        for (int i = 1; i < line.Length; i++) s += (line[i] - line[i - 1]).Length;
        return s;
    }

    private static double ChordDeviation(Vec3d[] line)
    {
        Vec3d a = line[0];
        Vec3d d = line[line.Length - 1] - a;
        double len = d.Length;
        if (len < 1e-15) return 0;
        d = d / len;
        double worst = 0;
        foreach (var p in line)
        {
            Vec3d v = p - a;
            worst = Math.Max(worst, (v - Vec3d.Dot(v, d) * d).Length);
        }
        return worst;
    }

    /// <summary>Traces both asymptotic families and classifies every curve end: on the border, on another curve (T-junction) or floating.</summary>
    private static (List<Vec3d[]> a, List<Vec3d[]> b, int ends, int border, int onCurve, int floating, int wrongFamily, int samples)
        AsymptoticNet(MeshData mesh, PrincipalCurvature.Result pc, AsymptoticCurves.DirectionField field, double spacing)
    {
        var proj = new MeshProjection(mesh);
        var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = spacing }, field.Family2);
        var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = spacing }, field.Family1);
        var all = a.Concat(b).ToList();
        int ends = 0, border = 0, onCurve = 0, floating = 0, wrong = 0, samples = 0;
        foreach (var (family, self, other) in new[] { (a, field.Family1, field.Family2), (b, field.Family2, field.Family1) })
            foreach (var c in family)
            {
                for (int i = 1; i < c.Length; i += 5)
                {
                    Vec3d t = (c[i] - c[i - 1]).Normalized();
                    int vi = proj.NearestVertexGlobal(c[i]);
                    if (!field.Exists[vi]) continue;
                    samples++;
                    if (Math.Abs(Vec3d.Dot(t, other[vi])) > Math.Abs(Vec3d.Dot(t, self[vi])) + 0.2) wrong++;
                }
                if ((c[0] - c[c.Length - 1]).LengthSquared < 1e-24) continue;
                foreach (var e in new[] { c[0], c[c.Length - 1] })
                {
                    ends++;
                    var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e));
                    if (proj.IsOnBoundary(h, 1e-4) || (h.Point - e).Length > 1e-6) { border++; continue; }
                    double best = double.MaxValue;
                    foreach (var m in all)
                    {
                        if (ReferenceEquals(m, c)) continue;
                        for (int i = 0; i + 1 < m.Length; i++)
                        {
                            Vec3d ab = m[i + 1] - m[i];
                            double tt = Math.Max(0, Math.Min(1, Vec3d.Dot(e - m[i], ab) / Math.Max(ab.LengthSquared, 1e-30)));
                            best = Math.Min(best, (m[i] + tt * ab - e).Length);
                        }
                    }
                    if (best < 1e-6) onCurve++; else floating++;
                }
            }
        return (a, b, ends, border, onCurve, floating, wrong, samples);
    }

    [Fact]
    public void Enneper2_IsMinimal_AndAsymptoticDirectionsAreDiagonalsOfTheChart()
    {
        var mesh = TestMeshes.CreateEnneper(2, 1.0, 24, 72, out var uv);
        var pc = PrincipalCurvature.Compute(mesh);
        var H = MeanCurvature.Compute(mesh).Values;
        var field = AsymptoticCurves.ComputeDirections(pc);
        var boundary = mesh.BuildBoundaryVertexFlags();

        double worstDir = 0, worstOrth = 0, worstH = 0, kMax = 0;
        int interior = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            double r2 = uv[v].u * uv[v].u + uv[v].v * uv[v].v;
            if (boundary[v] || r2 < 0.05 * 0.05 || r2 > 0.85 * 0.85) continue;
            interior++;
            kMax = Math.Max(kMax, Math.Abs(pc.K1[v]));
            worstH = Math.Max(worstH, Math.Abs(H[v]));
            Assert.True(field.Exists[v], $"Enneper is anticlastic everywhere, asymptotic directions missing at {v}");
            var (plus, minus) = TestMeshes.EnneperAsymptoticDirections(2, uv[v].u, uv[v].v);
            double a1 = Math.Min(AngleDeg(field.Family1[v], plus), AngleDeg(field.Family1[v], minus));
            double a2 = Math.Min(AngleDeg(field.Family2[v], plus), AngleDeg(field.Family2[v], minus));
            worstDir = Math.Max(worstDir, Math.Max(a1, a2));
            worstOrth = Math.Max(worstOrth, Math.Abs(90 - AngleDeg(field.Family1[v], field.Family2[v])));
        }
        Assert.True(interior > 1000, $"Expected a dense interior, got {interior} vertices");
        Assert.True(worstH < 0.03, $"Enneper is minimal: |H| should vanish, worst {worstH:F4} against k1 up to {kMax:F2}");
        Assert.True(worstDir < 1.5, $"Asymptotic directions should be X_u ± X_v, worst deviation {worstDir:F2}°");
        Assert.True(worstOrth < 1.5, $"On a minimal surface the asymptotic families are orthogonal, worst deviation {worstOrth:F2}°");

        var net = AsymptoticNet(mesh, pc, field, 0.12);
        Assert.True(net.wrongFamily == 0, $"{net.wrongFamily} of {net.samples} samples follow the other family");
        Assert.True(net.floating == 0 && net.border == net.ends,
            $"Every Enneper asymptotic curve should run rim to rim: {net.border} border, {net.onCurve} T-junction, {net.floating} floating of {net.ends} ends");
        Assert.True(net.a.Count >= 20 && net.b.Count >= 20, $"Families should fill the disk, got {net.a.Count} + {net.b.Count}");
    }

    [Fact]
    public void Enneper3_DirectionsFollowTheHopfDifferential_FamiliesSwapAroundTheFlatPoint()
    {
        var mesh = TestMeshes.CreateEnneper(3, 0.9, 24, 72, out var uv);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc);
        var boundary = mesh.BuildBoundaryVertexFlags();

        double worstDir = 0, worstOrth = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            double r2 = uv[v].u * uv[v].u + uv[v].v * uv[v].v;
            if (boundary[v] || r2 < 0.1 * 0.1 || r2 > 0.75 * 0.75) continue;
            var (plus, minus) = TestMeshes.EnneperAsymptoticDirections(3, uv[v].u, uv[v].v);
            double a1 = Math.Min(AngleDeg(field.Family1[v], plus), AngleDeg(field.Family1[v], minus));
            double a2 = Math.Min(AngleDeg(field.Family2[v], plus), AngleDeg(field.Family2[v], minus));
            worstDir = Math.Max(worstDir, Math.Max(a1, a2));
            worstOrth = Math.Max(worstOrth, Math.Abs(90 - AngleDeg(field.Family1[v], field.Family2[v])));
        }
        Assert.True(worstDir < 2.5, $"3-fold Enneper asymptotic directions should follow θ = (±π/2 − arg w)/2, worst deviation {worstDir:F2}°");
        Assert.True(worstOrth < 3.0, $"Families should be orthogonal (H = 0), worst deviation {worstOrth:F2}°");

        // The Hopf differential has a double zero at the origin: one turn around
        // it rotates the asymptotic cross by 180°, so the two families are one
        // connected family globally and the per-vertex labels cannot separate
        // them. What the net must still guarantee is that no lath ends in the
        // middle of the surface.
        var net = AsymptoticNet(mesh, pc, field, 0.12);
        Assert.True(net.floating == 0, $"No floating ends allowed: {net.border} border, {net.onCurve} T-junction, {net.floating} floating of {net.ends} ends");
        Assert.True(net.a.Count + net.b.Count >= 40, $"Net should fill the surface, got {net.a.Count + net.b.Count} curves");
    }

    [Fact]
    public void Catenoid_IsMinimal_WithAsymptoticDirectionsAt45Degrees()
    {
        const int seg = 64, rows = 32;
        var mesh = TestMeshes.CreateCatenoid(1.0, 2.0, seg, rows);
        var pc = PrincipalCurvature.Compute(mesh);
        var H = MeanCurvature.Compute(mesh).Values;
        var K = GaussianCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc);

        double worstH = 0, worstK = 0, worst45 = 0;
        for (int j = 2; j <= rows - 2; j++)
            for (int i = 0; i < seg; i++)
            {
                int v = j * seg + i;
                Vec3d p = mesh.Vertices[v];
                worstH = Math.Max(worstH, Math.Abs(H[v]));
                worstK = Math.Max(worstK, Math.Abs(K[v] - TestMeshes.CatenoidGaussianCurvature(p.Z)));
                Assert.True(field.Exists[v], $"Catenoid is anticlastic everywhere, missing at {v}");
                Vec3d parallel = new Vec3d(-p.Y, p.X, 0).Normalized();
                worst45 = Math.Max(worst45, Math.Abs(45 - AngleDeg(field.Family1[v], parallel)));
                worst45 = Math.Max(worst45, Math.Abs(45 - AngleDeg(field.Family2[v], parallel)));
            }
        Assert.True(worstH < 0.005, $"Catenoid H should be 0, worst {worstH:F4}");
        Assert.True(worstK < 0.005, $"Catenoid K should be −1/cosh⁴ z, worst error {worstK:F4}");
        Assert.True(worst45 < 2.0, $"Asymptotic directions should bisect meridians and parallels (45°), worst deviation {worst45:F2}°");

        var net = AsymptoticNet(mesh, pc, field, 0.3);
        Assert.True(net.wrongFamily == 0, $"{net.wrongFamily} of {net.samples} samples follow the other family");
        Assert.True(net.floating == 0 && net.border == net.ends, $"Every curve should run rim to rim: {net.border}/{net.ends} on the border, {net.floating} floating");
        Assert.True(Math.Abs(net.a.Count - net.b.Count) <= 2, $"Balanced families expected, got {net.a.Count} vs {net.b.Count}");
    }

    [Fact]
    public void BilinearPatch_AsymptoticNetIsItsRulings()
    {
        var p00 = new Vec3d(0, 0, 0); var p10 = new Vec3d(2, 0, 0.8); var p01 = new Vec3d(0.3, 1.5, 0.6); var p11 = new Vec3d(2.2, 1.6, -0.5);
        const int n = 40;
        var mesh = TestMeshes.CreateBilinearPatch(p00, p10, p01, p11, n, n);
        var pc = PrincipalCurvature.Compute(mesh);
        var K = GaussianCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc);

        double worst = 0;
        for (int j = 3; j <= n - 3; j++)
            for (int i = 3; i <= n - 3; i++)
            {
                int v = j * (n + 1) + i;
                Assert.True(K[v] <= 1e-9, $"A skew bilinear patch has K < 0, got {K[v]:E2} at {v}");
                Assert.True(field.Exists[v], $"Asymptotic directions missing at {v}");
                var (du, dv) = TestMeshes.BilinearRulings(p00, p10, p01, p11, i / (double)n, j / (double)n);
                double a1 = Math.Min(AngleDeg(field.Family1[v], du), AngleDeg(field.Family1[v], dv));
                double a2 = Math.Min(AngleDeg(field.Family2[v], du), AngleDeg(field.Family2[v], dv));
                worst = Math.Max(worst, Math.Max(a1, a2));
            }
        Assert.True(worst < 0.2, $"Asymptotic directions should be the two rulings, worst deviation {worst:F3}°");

        var net = AsymptoticNet(mesh, pc, field, 0.15);
        foreach (var c in net.a.Concat(net.b))
            Assert.True(ChordDeviation(c) < 0.02, $"Every net curve should be a straight ruling, bow {ChordDeviation(c):F4} over {ArcLength(c):F2}");
        Assert.True(net.wrongFamily == 0 && net.floating == 0 && net.border == net.ends,
            $"{net.wrongFamily} family-mixing samples; {net.border}/{net.ends} ends on the border, {net.floating} floating");
    }

    [Fact]
    public void AnalyticShapes_CatalogueNames_BuildAnticlasticOrMinimalPatches()
    {
        // The web app and the bench reach the catalogue through AnalyticShapes.Build
        foreach (var (name, p1, p2, p3) in new[] { ("enneper3", 3.0, 0.9, 0.0), ("ruled", 2.0, 0.8, 0.3), ("schwarzd", 1.0, 16.0, 20.0), ("gyroid", 1.0, 16.0, 20.0) })
        {
            var mesh = AnalyticShapes.Build(name, p1, p2, p3, 40);
            Assert.True(mesh.VertexCount > 300 && mesh.FaceCount > 500, $"{name}: {mesh.VertexCount} vertices, {mesh.FaceCount} faces");
            var boundary = mesh.BuildBoundaryVertexFlags();
            var K = GaussianCurvature.Compute(mesh);
            var H = MeanCurvature.Compute(mesh).Values;
            var pc = PrincipalCurvature.Compute(mesh);
            var interior = Enumerable.Range(0, mesh.VertexCount).Where(v => !boundary[v]).ToArray();
            int positive = interior.Count(v => K[v] > 1e-6);
            Assert.True(positive == 0, $"{name}: K should be ≤ 0 everywhere, {positive} of {interior.Length} interior vertices positive");
            if (name != "ruled")
            {
                double meanH = interior.Average(v => Math.Abs(H[v])), meanK1 = interior.Average(v => Math.Abs(pc.K1[v]));
                Assert.True(meanH < 0.08 * meanK1, $"{name}: should be (near) minimal, mean |H| {meanH:F4} vs k1 {meanK1:F3}");
            }
        }
        Assert.Contains("schwarzd", AnalyticShapes.Names);
        Assert.Throws<ArgumentException>(() => AnalyticShapes.Build("batwing"));
    }

    [Fact]
    public void SchwarzD_NodalPatchRelaxesToAMinimalSurface_AndCarriesAnAsymptoticNet()
    {
        var nodal = TestMeshes.CreateSchwarzD(0, Math.PI, 24, relaxIterations: 0);
        var mesh = TestMeshes.CreateSchwarzD(0, Math.PI, 24);
        Assert.True(mesh.VertexCount > 2000 && mesh.FaceCount > 4000, $"Marching tetrahedra should produce a dense patch, got {mesh.VertexCount} vertices");
        Assert.Equal(nodal.VertexCount, mesh.VertexCount);

        var boundary = mesh.BuildBoundaryVertexFlags();
        var pc = PrincipalCurvature.Compute(mesh);
        var H = MeanCurvature.Compute(mesh).Values;
        var K = GaussianCurvature.Compute(mesh);
        var Hn = MeanCurvature.Compute(nodal).Values;
        var interior = Enumerable.Range(0, mesh.VertexCount).Where(v => !boundary[v]).ToArray();

        double meanH = interior.Average(v => Math.Abs(H[v]));
        double meanHn = interior.Average(v => Math.Abs(Hn[v]));
        double meanK1 = interior.Average(v => Math.Abs(pc.K1[v]));
        int positiveK = interior.Count(v => K[v] > 1e-6);
        Assert.True(meanH < 0.05 * meanK1, $"Relaxed Schwarz D should be minimal: mean |H| {meanH:F4} against mean k1 {meanK1:F3} (nodal mesh had {meanHn:F3})");
        Assert.True(meanH < 0.1 * meanHn, $"Relaxation should cut the nodal mesh's |H| by 10×: {meanHn:F3} → {meanH:F4}");
        Assert.True(positiveK == 0, $"K should be ≤ 0 everywhere on a minimal surface, {positiveK} interior vertices positive");

        var field = AsymptoticCurves.ComputeDirections(pc);
        var net = AsymptoticNet(mesh, pc, field, 0.25);
        Assert.True(net.a.Count >= 15 && net.b.Count >= 15, $"Both families should fill the patch, got {net.a.Count} + {net.b.Count}");
        Assert.True(net.wrongFamily == 0, $"{net.wrongFamily} of {net.samples} samples follow the other family");
        Assert.True(net.floating == 0, $"No floating ends: {net.border} border, {net.onCurve} T-junction, {net.floating} floating of {net.ends}");
    }
}
