using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Global labelling of the two asymptotic families by the sign of their
/// geodesic torsion (Beltrami–Enneper: the asymptotic directions through a
/// point have torsions +√−K and −√−K; τg = (k2 − k1) sin α cos α). Checked
/// against closed forms: a right-handed helix on a cylinder, the rulings of a
/// hyperboloid, the monkey saddle z = x³ − 3xy² (Hessian known exactly) and
/// the 3-fold Enneper surface (Hopf differential w dw²), whose flat points
/// the old breadth-first combing could not cross.
/// </summary>
public class AsymptoticFamilyTests
{
    private readonly ITestOutputHelper _out;
    public AsymptoticFamilyTests(ITestOutputHelper output) => _out = output;

    private static double AngleDeg(Vec3d a, Vec3d b) =>
        Math.Acos(Math.Min(1.0, Math.Abs(Vec3d.Dot(a.Normalized(), b.Normalized())))) * 180.0 / Math.PI;

    /// <summary>
    /// Exact geodesic torsion of the monkey saddle z = x³ − 3xy² (textbook
    /// sign: II = ⟨x'', n⟩ with the upward normal, τg = II(t, n × t)).
    /// </summary>
    private static double MonkeyTorsion(Vec3d p, Vec3d t)
    {
        double fx = 3 * p.X * p.X - 3 * p.Y * p.Y, fy = -6 * p.X * p.Y;
        double w = Math.Sqrt(1 + fx * fx + fy * fy);
        var n = new Vec3d(-fx, -fy, 1) / w;
        var u = Vec3d.Cross(n, t);
        double hxx = 6 * p.X, hxy = -6 * p.Y, hyy = -6 * p.X;
        return (t.X * hxx * u.X + t.X * hxy * u.Y + t.Y * hxy * u.X + t.Y * hyy * u.Y) / w;
    }

    /// <summary>The breadth-first combing of Mite 1.2.2–1.3.0, kept here as the reference the new labels are compared with.</summary>
    private static void LegacyComb(MeshData mesh, Vec3d[] f1, Vec3d[] f2, bool[] exists)
    {
        int nv = mesh.VertexCount;
        var nbrs = mesh.BuildVertexNeighbors();
        var done = new bool[nv];
        var K = new double[nv];
        for (int i = 0; i < nv; i++) K[i] = exists[i] ? -Math.Abs(Vec3d.Dot(f1[i], f2[i])) : 1;
        for (int start = 0; start < nv; start++)
        {
            if (done[start] || !exists[start]) continue;
            int best = start;
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>(); stack.Push(start);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                if (K[v] < K[best]) best = v;
                foreach (int n in nbrs[v]) if (exists[n] && !done[n] && seen.Add(n)) stack.Push(n);
            }
            done[best] = true;
            var queue = new Queue<int>(); queue.Enqueue(best);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int n in nbrs[v])
                {
                    if (done[n] || !exists[n]) continue;
                    if (Math.Abs(Vec3d.Dot(f1[v], f2[n])) > Math.Abs(Vec3d.Dot(f1[v], f1[n]))) (f1[n], f2[n]) = (f2[n], f1[n]);
                    done[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
    }

    [Fact]
    public void GeodesicTorsion_OfHelicesOnACylinder_IsTheirFrenetTorsion()
    {
        // helix at angle θ to the parallels on a cylinder of radius R:
        // τg = sin θ cos θ / R, positive when right-handed
        const double R = 1.5;
        var mesh = TestMeshes.CreateCylinder(R, 3.0, 96, 48);
        var pc = PrincipalCurvature.Compute(mesh);
        var boundary = mesh.BuildBoundaryVertexFlags();
        double worst = 0;
        foreach (double deg in new[] { 15.0, 45.0, 70.0 })
        {
            double th = deg * Math.PI / 180, truth = Math.Sin(th) * Math.Cos(th) / R;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (boundary[v]) continue;
                var p = mesh.Vertices[v];
                var par = new Vec3d(-p.Y, p.X, 0).Normalized();
                var right = (Math.Cos(th) * par + Math.Sin(th) * new Vec3d(0, 0, 1)).Normalized();
                var left = (Math.Cos(th) * par - Math.Sin(th) * new Vec3d(0, 0, 1)).Normalized();
                worst = Math.Max(worst, Math.Abs(AsymptoticCurves.GeodesicTorsion(pc, v, right) - truth) / truth);
                worst = Math.Max(worst, Math.Abs(AsymptoticCurves.GeodesicTorsion(pc, v, left) + truth) / truth);
                // independent of the direction's sign
                Assert.Equal(AsymptoticCurves.GeodesicTorsion(pc, v, right), AsymptoticCurves.GeodesicTorsion(pc, v, -1.0 * right), 12);
            }
        }
        _out.WriteLine($"cylinder helices: worst relative τg error {worst:P3}");
        Assert.True(worst < 0.01, $"τg of helices should be ±sin θ cos θ / R, worst relative error {worst:P2}");
    }

    [Fact]
    public void Hyperboloid_FamilyOneIsTheRightHandedRulings_WithTorsionRootMinusK()
    {
        var mesh = TestMeshes.CreateHyperboloid(1.0, 1.0, 1.0, 64, 32);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        var boundary = mesh.BuildBoundaryVertexFlags();
        int n = 0, wrong = 0;
        double worstDir = 0, worstTau = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            if (!field.Exists[v] || boundary[v]) continue;
            var p = mesh.Vertices[v];
            var (plus, minus) = TestMeshes.HyperboloidRulings(p); // plus = (0, 1, 1)/√2 at (1, 0, 0): right-handed
            n++;
            if (AngleDeg(field.Family1[v], plus) > AngleDeg(field.Family1[v], minus)) wrong++;
            worstDir = Math.Max(worstDir, AngleDeg(field.Family1[v], plus));
            double root = Math.Sqrt(-TestMeshes.HyperboloidGaussianCurvature(p.Z));
            worstTau = Math.Max(worstTau, Math.Abs(AsymptoticCurves.GeodesicTorsion(pc, v, field.Family1[v]) - root) / root);
            worstTau = Math.Max(worstTau, Math.Abs(AsymptoticCurves.GeodesicTorsion(pc, v, field.Family2[v]) + root) / root);
        }
        _out.WriteLine($"hyperboloid: {n} vertices, {wrong} mislabelled, worst ruling angle {worstDir:F3}°, worst τg vs √−K {worstTau:P2}");
        Assert.True(n > 1500);
        Assert.Equal(0, wrong);
        Assert.True(worstDir < 1.0, $"Family 1 should be the right-handed rulings, worst {worstDir:F2}°");
        Assert.True(worstTau < 0.05, $"τg should be ±√−K (Beltrami–Enneper), worst relative error {worstTau:P2}");
    }

    [Fact]
    public void MonkeySaddle_LabelsAreGlobal_WhereTheOldCombingMislabelledASector()
    {
        var mesh = AnalyticShapes.Build("monkey", 2.0, 1.0, 0, 48);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);

        var l1 = (Vec3d[])field.Family1.Clone();
        var l2 = (Vec3d[])field.Family2.Clone();
        LegacyComb(mesh, l1, l2, field.Exists);

        int n = 0, wrong = 0, legacyWrong = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            if (!field.Exists[v]) continue;
            var p = mesh.Vertices[v];
            if (p.X * p.X + p.Y * p.Y < 0.05 * 0.05) continue; // the flat point itself
            n++;
            // exact: family 1 twists right-handed (τg > 0), family 2 left-handed
            if (!(MonkeyTorsion(p, field.Family1[v].Normalized()) > 0 && MonkeyTorsion(p, field.Family2[v].Normalized()) < 0)) wrong++;
            if (MonkeyTorsion(p, l1[v].Normalized()) < 0) legacyWrong++;
        }
        _out.WriteLine($"monkey: {n} vertices, new labels wrong {wrong}, legacy combing wrong {legacyWrong}");
        Assert.True(n > 2000);
        Assert.Equal(0, wrong);
        // the 1.3.0 combing turned one family into the other behind the flat
        // point (575 of 2396 vertices on this mesh); kept as a witness
        Assert.True(legacyWrong > 0.1 * n, $"legacy combing mislabelled {legacyWrong} of {n}");
    }

    [Fact]
    public void Enneper3_FamiliesDoNotSwapAroundTheFlatPoint()
    {
        // Hopf differential w dw²: θ± = (±π/2 − arg w)/2 turns by −π per loop
        // around w = 0, which maps each line onto itself — so "plus" is one
        // family all the way round, and the field labels must follow it
        var mesh = TestMeshes.CreateEnneper(3, 0.9, 24, 72, out var uv);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        int onPlus = 0, onMinus = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            double r2 = uv[v].u * uv[v].u + uv[v].v * uv[v].v;
            if (!field.Exists[v] || r2 < 0.1 * 0.1) continue;
            var (plus, minus) = TestMeshes.EnneperAsymptoticDirections(3, uv[v].u, uv[v].v);
            if (AngleDeg(field.Family1[v], plus) < AngleDeg(field.Family1[v], minus)) onPlus++; else onMinus++;
            Assert.True(AsymptoticCurves.GeodesicTorsion(pc, v, field.Family1[v]) > 0);
        }
        _out.WriteLine($"enneper3: {onPlus} on plus, {onMinus} on minus");
        Assert.True(onPlus + onMinus > 1400);
        Assert.True(Math.Min(onPlus, onMinus) == 0, $"labels split {onPlus} / {onMinus} between the two analytic families");
    }

    [Fact]
    public void Labels_DoNotDependOnTheMeshOrientation()
    {
        var mesh = AnalyticShapes.Build("wave", 2.0, 0.3, 2.0, 40);
        var flipped = new MeshData(mesh.Vertices, mesh.Faces.Select(f => f.Reverse().ToArray()).ToArray());
        var a = AsymptoticCurves.ComputeDirections(PrincipalCurvature.Compute(mesh), mesh, 15.0);
        var b = AsymptoticCurves.ComputeDirections(PrincipalCurvature.Compute(flipped), flipped, 15.0);
        int n = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            Assert.Equal(a.Exists[v], b.Exists[v]);
            if (!a.Exists[v]) continue;
            n++;
            Assert.True(AngleDeg(a.Family1[v], b.Family1[v]) < 1e-6, $"vertex {v}: family 1 changed with the orientation");
        }
        Assert.True(n > 500);
    }

    [Fact]
    public void Wave_SeparateSaddleRegions_ShareOneLabelling()
    {
        // the wave has several anticlastic regions separated by K ≥ 0; the old
        // combing labelled each from its own root, so family A could be the
        // left-handed family in one region and the right-handed one in the next
        var wave = AnalyticShapes.Build("wave", 2.0, 0.3, 2.0, 40);
        var pc = PrincipalCurvature.Compute(wave);
        foreach (double minAngle in new[] { 0.0, 15.0 })
        {
            var f = AsymptoticCurves.ComputeDirections(pc, wave, minAngle);
            for (int v = 0; v < wave.VertexCount; v++)
            {
                if (!f.Exists[v]) continue;
                double t1 = AsymptoticCurves.GeodesicTorsion(pc, v, f.Family1[v]), t2 = AsymptoticCurves.GeodesicTorsion(pc, v, f.Family2[v]);
                Assert.True(t1 > 0 && t2 < 0, $"vertex {v}: τg {t1:G3} / {t2:G3}");
                Assert.True(Math.Abs(t1 + t2) < 1e-9 * Math.Max(1, Math.Abs(t1)), "the two asymptotic torsions are negatives of one another");
            }
        }
    }

    /// <summary>Curves per family whose samples follow the other family (by the sign of τg), and stray samples within curves.</summary>
    private static (int mislabeled, int jumps, int samples, int curves) Purity(MeshData mesh, PrincipalCurvature.Result pc,
        AsymptoticCurves.DirectionField field, List<Vec3d[]> a, List<Vec3d[]> b)
    {
        var proj = new MeshProjection(mesh);
        int mislabeled = 0, jumps = 0, samples = 0;
        foreach (var (fam, sign) in new[] { (a, 1), (b, -1) })
            foreach (var c in fam)
            {
                int pos = 0, neg = 0;
                for (int i = 1; i < c.Length; i++)
                {
                    var t = c[i] - c[i - 1];
                    if (t.LengthSquared < 1e-20) continue;
                    int v = proj.NearestVertexGlobal(0.5 * (c[i] + c[i - 1]));
                    if (!field.Exists[v]) continue;
                    double tau = AsymptoticCurves.GeodesicTorsion(pc, v, t.Normalized());
                    if (Math.Abs(tau) < 0.5 * AsymptoticCurves.GeodesicTorsion(pc, v, field.Family1[v])) continue; // too close to call
                    if (tau > 0) pos++; else neg++;
                }
                samples += pos + neg;
                jumps += Math.Min(pos, neg);
                if (pos + neg > 0 && (pos >= neg ? 1 : -1) != sign) mislabeled++;
            }
        return (mislabeled, jumps, samples, a.Count + b.Count);
    }

    [Theory]
    [InlineData("monkey", 1)]
    [InlineData("enneper3", 0)]
    [InlineData("wave", 0)]
    [InlineData("wave", 1)]
    public void Nets_KeepEveryCurveInItsFamily(string shape, int layout)
    {
        var mesh = AnalyticShapes.Build(shape, 0, 0, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double sp = 0.05 * mesh.BoundingBoxDiagonal();
        var lay = layout == 0 ? NetLayout.Fill : NetLayout.WebBorder;
        var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, Layout = lay }, field.Family2);
        var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, Layout = lay }, field.Family1);
        var r = Purity(mesh, pc, field, a, b);
        _out.WriteLine($"{shape} layout {layout}: {r.curves} curves, {r.mislabeled} mislabelled, {r.jumps}/{r.samples} jumps");
        Assert.True(r.curves >= 30, $"net too sparse: {r.curves} curves");
        Assert.True(r.mislabeled == 0, $"{r.mislabeled} of {r.curves} curves belong to the other family");
        Assert.True(r.jumps <= 0.002 * r.samples, $"{r.jumps} of {r.samples} samples jump to the other family");
    }

    [Fact]
    public void Enneper3_UnlockedTracing_JumpsFamilies_LockedDoesNot()
    {
        // the 1.3.0 tracer took the best-aligned candidate of either family at
        // every sample; next to the flat point that continues straight on into
        // the other family. Locked, every curve keeps its own family.
        var mesh = AnalyticShapes.Build("enneper3", 0, 0, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double sp = 0.05 * mesh.BoundingBoxDiagonal();
        (int mislabeled, int jumps, int samples, int curves) Run(bool locked) => Purity(mesh, pc, field,
            EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, LockFamily = locked }, field.Family2),
            EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = sp, LockFamily = locked }, field.Family1));
        var off = Run(false);
        var on = Run(true);
        _out.WriteLine($"enneper3 fill unlocked: {off.mislabeled}/{off.curves} mislabelled, {off.jumps}/{off.samples}; locked: {on.mislabeled}/{on.curves}, {on.jumps}/{on.samples}");
        Assert.True(off.mislabeled > 0, "witness: the unlocked tracer should show the old family jumps here");
        Assert.Equal(0, on.mislabeled);
        Assert.Equal(0, on.jumps);
    }

    [Fact]
    public void ManualSeeds_FollowTheirOwnFamily_PastTheFlatPoint()
    {
        var mesh = AnalyticShapes.Build("monkey", 2.0, 1.0, 0, 48);
        var pc = PrincipalCurvature.Compute(mesh);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        // seeds on a small ring around the flat point, where the cross turns fastest
        var seeds = Enumerable.Range(0, mesh.VertexCount)
            .Where(v => field.Exists[v] && mesh.Vertices[v].X * mesh.Vertices[v].X + mesh.Vertices[v].Y * mesh.Vertices[v].Y is > 0.01 and < 0.04)
            .ToArray();
        Assert.True(seeds.Length >= 8);
        var a = AsymptoticCurves.Trace(mesh, seeds, pc, false);
        var b = AsymptoticCurves.Trace(mesh, seeds, pc, true);
        var r = Purity(mesh, pc, field, a, b);
        _out.WriteLine($"monkey manual seeds: {seeds.Length} seeds, {r.curves} curves, {r.mislabeled} mislabelled, {r.jumps}/{r.samples} jumps");
        Assert.Equal(0, r.mislabeled);
        Assert.True(r.jumps <= 0.002 * r.samples, $"{r.jumps} of {r.samples} samples jump to the other family");
    }
}
