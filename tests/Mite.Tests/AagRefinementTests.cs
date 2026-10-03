using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Refinement of AAG webs (Schling, Wang, Hoyer &amp; Pottmann 2022, §3.4)
/// against closed forms. The joints are put exactly on the analytic surface
/// — at the crossings of the catenoid's asymptotic lines φ ± z = const, and
/// at the crossings of the hyperboloid's rulings — so the refined laths can
/// be compared with the exact curves between them.
/// </summary>
public class AagRefinementTests
{
    private readonly ITestOutputHelper _out;
    public AagRefinementTests(ITestOutputHelper output) => _out = output;

    private static Vec3d[] Oriented(MeshData mesh, IList<Vec3d> nodes, Func<Vec3d, Vec3d> normal)
    {
        var proj = new MeshProjection(mesh);
        return nodes.Select(p =>
        {
            var n = normal(p).Normalized();
            var hit = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
            return Vec3d.Dot(hit.SmoothNormal, n) < 0 ? -1.0 * n : n;
        }).ToArray();
    }

    /// <summary>The coarse laths as polylines subdivided m times along their chords (what the refinement replaces).</summary>
    private static Vec3d[] Chords(Vec3d[] l, int m)
    {
        var o = new List<Vec3d>();
        for (int k = 0; k + 1 < l.Length; k++)
            for (int j = 0; j < m; j++) o.Add(l[k] + (j / (double)m) * (l[k + 1] - l[k]));
        o.Add(l[^1]);
        return o.ToArray();
    }

    [Fact]
    public void Catenoid_LatticeOfAsymptoticLines_RefinedLathsFollowTheExactCurves()
    {
        // catenoid r = cosh z: asymptotic lines φ ± z = const; joints at
        // φ = (i + j)Δ, z = (i − j)Δ; diagonals (i + k, j − k) are meridians
        // (planar geodesics r = cosh z), (i + k, j + k) parallels
        const double D = 0.1;
        int I = 16;
        Vec3d? Node(int i, int j)
        {
            double phi = (i + j - I) * D, z = (i - j) * D;
            if (Math.Abs(z) > 1.0 + 1e-9) return null;
            return new Vec3d(Math.Cosh(z) * Math.Cos(phi), Math.Cosh(z) * Math.Sin(phi), z);
        }
        var w = TestMeshes.AsymptoticLattice(Node, I, I);
        var mesh = AnalyticShapes.Build("catenoid", 1.0, 1.15, 0, 192).ToTriangulated();
        var normals = Oriented(mesh, w.Nodes, p => new Vec3d(p.X, p.Y, -Math.Sinh(p.Z) * Math.Cosh(p.Z)));
        var f = AagRefinement.Refine(mesh, w, w.Nodes, normals, 0, new AagRefinement.Options { Levels = 1, JointWeight = 10 });
        int m = f.Subdivisions;
        Assert.Equal(2, m);

        // lateral distance of each fine point from the asymptotic line through
        // the joints at the ends of its segment: |Δ(φ ± z)| · r
        double Inv(Vec3d p, int s) => Math.Atan2(p.Y, p.X) + s * p.Z;
        int Sign(Vec3d[] l) => Math.Abs(Math.IEEERemainder(Inv(l[^1], 1) - Inv(l[0], 1), 2 * Math.PI))
            < Math.Abs(Math.IEEERemainder(Inv(l[^1], -1) - Inv(l[0], -1), 2 * Math.PI)) ? 1 : -1;
        double OffCurve(Vec3d[] l, int s)
        {
            double worst = 0;
            for (int k = 0; k + m < l.Length; k += m)
            {
                double c0 = Inv(l[k], s);
                for (int j = k + 1; j < k + m; j++)
                    worst = Math.Max(worst, Math.Abs(Math.IEEERemainder(Inv(l[j], s) - c0, 2 * Math.PI)) * Math.Cosh(l[j].Z));
            }
            return worst;
        }
        var coarse = AagWeb.Optimize(mesh, w, new AagWeb.Options { Iterations = 0 });
        double refined = f.A.Concat(f.B).Max(l => OffCurve(l, Sign(l)));
        double chords = coarse.A.Concat(coarse.B).Max(l => OffCurve(Chords(l, m), Sign(l)));
        // the geodesic diagonals: in the meridian planes and on the catenary r = cosh z
        double plane = 0, catenary = 0, chordCatenary = 0;
        foreach (var g in f.G)
        {
            var n = Vec3d.Cross(new Vec3d(0, 0, 1), new Vec3d(g[0].X, g[0].Y, 0)).Normalized();
            foreach (var p in g)
            {
                plane = Math.Max(plane, Math.Abs(Vec3d.Dot(p, n)));
                catenary = Math.Max(catenary, Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Cosh(p.Z)));
            }
        }
        foreach (var g in coarse.G)
            foreach (var p in Chords(g, m))
                chordCatenary = Math.Max(chordCatenary, Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Cosh(p.Z)));
        _out.WriteLine($"spacing {w.Spacing:G4}: A/B off the exact curve {refined:G3} (chords {chords:G3}); G off the catenary {catenary:G3} (chords {chordCatenary:G3}), off its plane {plane:G3}; " +
            $"stars {f.InitialStarError:G3}° → {f.StarError:G3}°, geodesic {f.GeodesicError:G3}°, kn·h {f.MaxNormalCurvature:G3}, kg·h {f.MaxGeodesicCurvature:G3}, turning {f.InitialMaxTurning:G3}° → {f.MaxTurning:G3}°, joints moved {f.JointShift:G3}");

        Assert.InRange(chords, 0.002, 0.004);           // the sagitta of the chords
        Assert.InRange(refined, 0, 0.1 * chords);        // measured 1.0e-4: 28× closer
        Assert.InRange(chordCatenary, 0.005, 0.01);
        Assert.InRange(catenary, 0, 0.02 * chordCatenary); // measured 4e-5 (mesh facets 2e-5 off)
        Assert.InRange(plane, 0, 1e-5);
        Assert.InRange(f.StarError, 0, 0.01);
        Assert.InRange(f.MaxNormalCurvature, 0, 5e-4);
        Assert.InRange(f.MaxGeodesicCurvature, 0, 5e-5);
        Assert.InRange(f.MaxTurning, 0, 0.55 * f.InitialMaxTurning);
        Assert.InRange(f.JointShift, 0, 1e-5);
    }

    [Fact]
    public void Hyperboloid_LatticeOfRulings_RefinedRulingsStayStraight_MeridiansOnTheHyperbola()
    {
        // x² + y² − z² = 1: rulings A(φ) = e^{iφ}(1 + i s), z = s and
        // B(ψ) = e^{iψ}(1 − i s), z = s cross at s = tan((ψ − φ)/2); the
        // diagonals (i + k, j − k) keep φ + ψ — meridian hyperbolas, planar geodesics
        const double D = 0.14;
        int I = 14;
        Vec3d? Node(int i, int j)
        {
            double phi = i * D, psi = j * D;
            double s = Math.Tan(0.5 * (psi - phi));
            if (Math.Abs(s) > 0.75) return null;
            return new Vec3d(Math.Cos(phi) - s * Math.Sin(phi), Math.Sin(phi) + s * Math.Cos(phi), s);
        }
        var w = TestMeshes.AsymptoticLattice(Node, I, I);
        var mesh = AnalyticShapes.Build("hyperboloid", 1.0, 1.0, 0.8, 192).ToTriangulated();
        var normals = Oriented(mesh, w.Nodes, p => new Vec3d(p.X, p.Y, -p.Z));
        var f = AagRefinement.Refine(mesh, w, w.Nodes, normals, 0, new AagRefinement.Options { Levels = 1, JointWeight = 10 });

        // every refined A/B lath is a ruling: on the line through its end joints
        double bow = 0, length = 0;
        foreach (var l in f.A.Concat(f.B))
        {
            var a = l[0]; var d = (l[^1] - a).Normalized();
            length = Math.Max(length, (l[^1] - a).Length);
            foreach (var p in l) { var v = p - a; bow = Math.Max(bow, (v - Vec3d.Dot(v, d) * d).Length); }
        }
        // the geodesic diagonals: meridian plane and hyperbola r² − z² = 1
        double plane = 0, hyperbola = 0, chordHyperbola = 0;
        foreach (var g in f.G)
        {
            var n = Vec3d.Cross(new Vec3d(0, 0, 1), new Vec3d(g[0].X, g[0].Y, 0)).Normalized();
            foreach (var p in g)
            {
                plane = Math.Max(plane, Math.Abs(Vec3d.Dot(p, n)));
                hyperbola = Math.Max(hyperbola, Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Sqrt(1 + p.Z * p.Z)));
            }
        }
        var coarse = AagWeb.Optimize(mesh, w, new AagWeb.Options { Iterations = 0 });
        foreach (var g in coarse.G)
            foreach (var p in Chords(g, 2))
                chordHyperbola = Math.Max(chordHyperbola, Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Sqrt(1 + p.Z * p.Z)));
        _out.WriteLine($"{w.Nodes.Count} joints, spacing {w.Spacing:G4}: rulings bow {bow:G3} over {length:G3}; G off the hyperbola {hyperbola:G3} (chords {chordHyperbola:G3}), off its plane {plane:G3}; " +
            $"stars {f.StarError:G3}°, geodesic {f.GeodesicError:G3}°, kn·h {f.MaxNormalCurvature:G3}, kg·h {f.MaxGeodesicCurvature:G3}, turning {f.InitialMaxTurning:G3}° → {f.MaxTurning:G3}° (A/B {AagRefinement.MaxTurning(f.A.Concat(f.B)):G3}°)");

        Assert.True(f.A.Count >= 8 && f.B.Count >= 8);
        Assert.InRange(bow, 0, 1e-4 * length);
        Assert.InRange(AagRefinement.MaxTurning(f.A.Concat(f.B)), 0, 0.05);
        Assert.InRange(chordHyperbola, 0.002, 0.02);
        Assert.InRange(hyperbola, 0, 0.02 * chordHyperbola);
        Assert.InRange(plane, 0, 1e-5);
        Assert.InRange(f.StarError, 0, 0.01);
        Assert.InRange(f.MaxGeodesicCurvature, 0, 5e-5);
    }

    [Fact]
    public void AagWebs_Refined_SmoothLathsThroughTheJoints()
    {
        // rotational AAG web of the catenoid (exact AAG): two and four pieces per edge
        var cat = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(cat, 2);
        var w = AsymptoticWeb.Build(cat, pc, -1, new AsymptoticWeb.Options { Spacing = 0.3 });
        var r = AagWeb.Optimize(cat, w);
        foreach (int levels in new[] { 1, 2 })
        {
            var f = AagRefinement.Refine(cat, w, r, new AagRefinement.Options { Levels = levels });
            int m = f.Subdivisions;
            _out.WriteLine($"catenoid ×{m}: {f.Vertices.Length} fine vertices, stars {f.InitialStarError:G3}° → {f.StarError:G3}°, geodesic {f.InitialGeodesicError:G3}° → {f.GeodesicError:G3}°, " +
                $"kn·h {f.MaxNormalCurvature:G3}, kg·h {f.MaxGeodesicCurvature:G3}, turning {f.InitialMaxTurning:G3}° → {f.MaxTurning:G3}°, joints moved {f.JointShift:G3}, surface {f.MeanDeviation:G3} / {f.MaxDeviation:G3}");
            Assert.Equal(r.A.Count, f.A.Count);
            Assert.Equal(r.G.Count, f.G.Count);
            for (int i = 0; i < f.A.Count; i++) Assert.Equal((r.A[i].Length - 1) * m + 1, f.A[i].Length);
            Assert.Equal(m * m * w.Quads.Count, f.Quads.Count);
            // the joints stay where AAG put them, every m-th fine vertex of a lath
            for (int k = 0; k < r.A[0].Length; k++) Assert.InRange((f.A[0][k * m] - r.A[0][k]).Length, 0, 1e-3 * w.Spacing);
            Assert.InRange(f.StarError, 0, 0.05);
            Assert.InRange(f.MaxNormalCurvature, 0, 0.005);
            Assert.InRange(f.MaxGeodesicCurvature, 0, 0.005);
            Assert.InRange(f.MaxTurning, 0, 1.15 * f.InitialMaxTurning / m);
            Assert.InRange(f.MaxDeviation, 0, 0.02 * w.Spacing);
        }

        // 3-fold Enneper: a singular web (six rays, diagonals broken at the rays)
        var en = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var we = AsymptoticWeb.Build(en, PrincipalCurvature.Compute(en, 2), -1, new AsymptoticWeb.Options());
        var re = AagWeb.Optimize(en, we);
        var fe = AagRefinement.Refine(en, we, re, new AagRefinement.Options { Levels = 1 });
        _out.WriteLine($"enneper3 ×2: AAG kink {re.MaxLathTurning:G3}°; stars {fe.InitialStarError:G3}° → {fe.StarError:G3}°, geodesic {fe.InitialGeodesicError:G3}° → {fe.GeodesicError:G3}°, " +
            $"kn·h {fe.MaxNormalCurvature:G3}, kg·h {fe.MaxGeodesicCurvature:G3}, G turning {AagRefinement.MaxTurning(re.G):G3}° → {AagRefinement.MaxTurning(fe.G):G3}°, joints moved {fe.JointShift:G3}, surface {fe.MaxDeviation:G3}");
        Assert.InRange(fe.StarError, 0, 0.3);
        Assert.InRange(fe.MaxNormalCurvature, 0, 0.02);
        Assert.InRange(fe.MaxGeodesicCurvature, 0, 0.01);
        Assert.InRange(AagRefinement.MaxTurning(fe.G), 0, 0.75 * AagRefinement.MaxTurning(re.G));
        Assert.InRange(fe.JointShift, 0, 1e-3 * we.Spacing);
        Assert.InRange(fe.MaxDeviation, 0, 0.05 * we.Spacing);
    }

    [Fact]
    public void AagWeb_DefaultFairness_KeepsLathsFromKinkingAtTheRays()
    {
        // 1.3.0 (fairness 0.05): 44.5° kinks where the laths cross the rays of
        // the 3-fold Enneper web; the web's own chains turn by up to 17.6°
        var en = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var w = AsymptoticWeb.Build(en, PrincipalCurvature.Compute(en, 2), -1, new AsymptoticWeb.Options());
        var start = AagWeb.Optimize(en, w, new AagWeb.Options { Iterations = 0 });
        var old = AagWeb.Optimize(en, w, new AagWeb.Options { Fairness = 0.05 });
        var now = AagWeb.Optimize(en, w);
        _out.WriteLine($"web chains {start.MaxLathTurning:G3}°, fairness 0.05: {old.MaxLathTurning:G3}° (geodesic {old.GeodesicError:G3}°), default: {now.MaxLathTurning:G3}° (geodesic {now.GeodesicError:G3}°, stars {now.StarError:G3}°)");
        Assert.True(old.MaxLathTurning > 40);
        Assert.InRange(now.MaxLathTurning, 0, 25);
        Assert.InRange(now.GeodesicError, 0, 0.5);
        Assert.InRange(now.StarError, 0, 0.5);
    }

    [Fact]
    public void SparseLdl_MatchesTheEnvelopeSolver_WithLessFill()
    {
        // a 40 × 40 grid with 3 coupled unknowns per vertex and 2-ring couplings
        int nx = 40, ny = 40, per = 3, n = nx * ny * per;
        var b = new SparseSymmetricSolver.Builder(n);
        var rnd = new Random(7);
        int Id(int i, int j, int c) => (j * nx + i) * per + c;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                for (int c = 0; c < per; c++)
                {
                    b.Add(Id(i, j, c), Id(i, j, c), 20.0 + rnd.NextDouble());
                    for (int c2 = c + 1; c2 < per; c2++) b.Add(Id(i, j, c), Id(i, j, c2), 0.5);
                    foreach (var (di, dj) in new[] { (1, 0), (0, 1), (2, 0), (0, 2), (1, 1) })
                    {
                        int i2 = i + di, j2 = j + dj;
                        if (i2 >= nx || j2 >= ny) continue;
                        for (int c2 = 0; c2 < per; c2++) b.Add(Id(i, j, c), Id(i2, j2, c2), -0.3 - 0.1 * rnd.NextDouble());
                    }
                }
        var rhs = Enumerable.Range(0, n).Select(k => Math.Sin(k)).ToArray();
        var env = SparseSymmetricSolver.Factor(b)!;
        var ldl = SparseLdl.Factor(b)!;
        var x1 = env.Solve(rhs); var x2 = ldl.Solve(rhs);
        double diff = x1.Zip(x2, (p, q) => Math.Abs(p - q)).Max();
        var ax = b.Multiply(x2);
        double res = ax.Zip(rhs, (p, q) => Math.Abs(p - q)).Max();
        _out.WriteLine($"n {n}: envelope {env.EnvelopeSize}, LDLᵀ {ldl.FactorNonZeros}, |x₁ − x₂| {diff:G3}, residual {res:G3}");
        Assert.InRange(diff, 0, 1e-10);
        Assert.InRange(res, 0, 1e-10);
        Assert.True(ldl.FactorNonZeros < env.EnvelopeSize);

        // indefinite → null
        var bad = new SparseSymmetricSolver.Builder(2);
        bad.Add(0, 0, 1); bad.Add(1, 1, 1); bad.Add(0, 1, 2);
        Assert.Null(SparseLdl.Factor(bad));
    }
}
