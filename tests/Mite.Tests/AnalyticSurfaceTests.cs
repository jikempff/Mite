using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;

namespace Mite.Tests;

/// <summary>
/// The analytic ground truth itself (AnalyticSurface) checked against closed forms
/// from the literature, the two shape generators (AnalyticShapes, TestMeshes) checked
/// against each other, and the discrete curvature estimators measured against the
/// truth on the catalogue shapes that had no analytic test yet (dome, ellipsoid,
/// monkey saddle, vault, cone, annulus, wave) — mean absolute error over the
/// interior vertices and its convergence with resolution, the protocol of Magid,
/// Soldea &amp; Rivlin 2007 (A comparison of Gaussian and mean curvature estimation
/// methods on triangular meshes).
/// </summary>
public class AnalyticSurfaceTests
{
    private static readonly string[] Parametric =
        { "saddle", "sphere", "torus", "hyperboloid", "monkey", "ellipsoid", "catenoid", "enneper", "vault", "cone", "dome", "annulus", "wave", "enneper3", "ruled" };

    /// <summary>Angle between two lines in degrees (atan2 form: exact down to 1e-14°, where acos floors at 1e-6°).</summary>
    private static double Deg(Vec3d a, Vec3d b)
    {
        a = a.Normalized(); b = b.Normalized();
        return Math.Atan2(Vec3d.Cross(a, b).Length, Math.Abs(Vec3d.Dot(a, b))) * 180 / Math.PI;
    }

    [Fact]
    public void EveryParametricShapeHasConsistentDerivatives()
    {
        // the hand-written x_u, x_v, x_uu, x_uv, x_vv against central differences of x
        var rnd = new Random(1);
        foreach (var name in Parametric)
        {
            var s = AnalyticShapes.Surface(name);
            Assert.True(s.IsParametric);
            double worst = 0;
            for (int k = 0; k < 200; k++)
            {
                double u = s.U0 + (s.U1 - s.U0) * (0.05 + 0.9 * rnd.NextDouble());
                double v = s.V0 + (s.V1 - s.V0) * (0.05 + 0.9 * rnd.NextDouble());
                worst = Math.Max(worst, s.DerivativeError(u, v));
            }
            Assert.True(worst < 1e-6, $"{name}: derivative error {worst:E2}");
        }
    }

    [Fact]
    public void ImplicitShapesHaveConsistentGradientAndHessian()
    {
        var rnd = new Random(2);
        foreach (var name in new[] { "schwarzd", "gyroid" })
        {
            var s = AnalyticShapes.Surface(name);
            Assert.True(s.IsImplicit);
            double h = 1e-4, worst = 0;
            for (int k = 0; k < 200; k++)
            {
                var p = new Vec3d(rnd.NextDouble() * Math.PI, rnd.NextDouble() * Math.PI, rnd.NextDouble() * Math.PI);
                Vec3d ex = new Vec3d(h, 0, 0), ey = new Vec3d(0, h, 0), ez = new Vec3d(0, 0, h);
                var g = new Vec3d((s.F!(p + ex) - s.F(p - ex)) / (2 * h), (s.F(p + ey) - s.F(p - ey)) / (2 * h), (s.F(p + ez) - s.F(p - ez)) / (2 * h));
                worst = Math.Max(worst, (g - s.Gradient!(p)).Length);
                var H = s.Hessian!(p);
                var gx = (s.Gradient(p + ex) - s.Gradient(p - ex)) / (2 * h);
                var gy = (s.Gradient(p + ey) - s.Gradient(p - ey)) / (2 * h);
                var gz = (s.Gradient(p + ez) - s.Gradient(p - ez)) / (2 * h);
                worst = Math.Max(worst, Math.Abs(gx.X - H[0, 0]) + Math.Abs(gx.Y - H[0, 1]) + Math.Abs(gx.Z - H[0, 2]) + Math.Abs(gy.Y - H[1, 1]) + Math.Abs(gy.Z - H[1, 2]) + Math.Abs(gz.Z - H[2, 2]));
            }
            Assert.True(worst < 1e-6, $"{name}: {worst:E2}");
        }
    }

    [Fact]
    public void BuildIsTheSurfaceMesh()
    {
        // one generator: Build(name) and Surface(name).Mesh are the same mesh, vertex for vertex
        foreach (var name in Parametric)
        {
            var a = AnalyticShapes.Build(name, 0, 0, 0, 24);
            var b = AnalyticShapes.Surface(name).Mesh(24, out var uv, out var quads);
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.FaceCount, b.FaceCount);
            for (int i = 0; i < a.VertexCount; i++) Assert.Equal(a.Vertices[i], b.Vertices[i]);
            for (int i = 0; i < a.FaceCount; i++) Assert.Equal(a.Faces[i], b.Faces[i]);
            Assert.Equal(b.VertexCount, uv.Length);
            // the parameters really are the vertex's: X(u, v) = vertex
            var s = AnalyticShapes.Surface(name);
            for (int i = 0; i < uv.Length; i++) Assert.True((s.X!(uv[i].u, uv[i].v) - b.Vertices[i]).Length < 1e-12);
            // every triangle belongs to exactly one cell; cells cover the triangles
            Assert.Equal(b.FaceCount, quads.Sum(q => q.Length - 2));
        }
    }

    [Fact]
    public void SphereAndDome_KHAndPrincipalCurvaturesAreOneOverR()
    {
        foreach (var (name, r) in new[] { ("sphere", 1.0), ("dome", 1.0) })
        {
            var s = AnalyticShapes.Surface(name);
            for (int k = 1; k < 20; k++)
            {
                var c = s.At(0.37 * k, s.V0 + (s.V1 - s.V0) * k / 20.0);
                Assert.False(c.Degenerate);
                Assert.True(Math.Abs(c.K - 1 / (r * r)) < 1e-12);
                // Mite's sign: H = +1/R with the normal outward (the sphere's chart) and −1/R with it inward (the dome's chart runs the other way)
                double outward = Vec3d.Dot(c.Normal, c.Point) / r;
                Assert.True(Math.Abs(Math.Abs(outward) - 1) < 1e-9, "the normal is radial");
                Assert.True(Math.Abs(c.H - outward / r) < 1e-12, $"{name}: H = {c.H}, outward {outward}");
                if (name == "sphere") Assert.True(outward > 0); else Assert.True(outward < 0);
                // k1, k2 = H ± √(H² − K): the square root of the round-off at an umbilic is ~1e-8
                Assert.True(Math.Abs(c.K1 - outward / r) < 1e-7 && Math.Abs(c.K2 - outward / r) < 1e-7);
                Assert.True(c.UmbilicMeasure < 1e-7);
                Assert.False(c.HasAsymptotic);
            }
            // the pole row is a singular chart point: reported as such, not as a number
            Assert.True(s.At(0, 0).Degenerate);
        }
    }

    [Fact]
    public void Torus_ClosedForm()
    {
        // K = cos v / (r (R + r cos v)), H = (R + 2 r cos v) / (2 r (R + r cos v)) (outward normal),
        // principal directions along the parallel (u) and the meridian (v)
        double R = 3, r = 1;
        var s = AnalyticShapes.Surface("torus");
        for (int i = 0; i < 24; i++)
        {
            double u = 0.3 + i * 0.26, v = 2 * Math.PI * i / 24 + 0.05;
            var c = s.At(u, v);
            double Kt = Math.Cos(v) / (r * (R + r * Math.Cos(v))), Ht = (R + 2 * r * Math.Cos(v)) / (2 * r * (R + r * Math.Cos(v)));
            Assert.True(Math.Abs(c.K - Kt) < 1e-12 && Math.Abs(c.H - Ht) < 1e-12, $"v = {v}: K {c.K} vs {Kt}, H {c.H} vs {Ht}");
            Assert.True(Math.Abs(c.K1 - 1 / r) < 1e-12, "k1 = 1/r on the whole tube");
            Assert.True(Deg(c.D1, s.Xv!(u, v)) < 1e-6 && Deg(c.D2, s.Xu!(u, v)) < 1e-6);
            Assert.Equal(Math.Cos(v) < 0, c.HasAsymptotic);
            Assert.True(Vec3d.Dot(c.Normal, c.Point - new Vec3d(R * Math.Cos(u), R * Math.Sin(u), 0)) > 0.999, "outward");
        }
    }

    [Fact]
    public void HyperboloidAndCatenoid_AgreeWithTestMeshesClosedForms()
    {
        var hyp = AnalyticShapes.Surface("hyperboloid");   // a = c = 1, same as TestMeshes.CreateHyperboloid
        for (int i = 0; i < 30; i++)
        {
            double u = 0.2 * i, v = -1 + 2.0 * i / 29;
            var c = hyp.At(u, v);
            Assert.True(Math.Abs(c.K - TestMeshes.HyperboloidGaussianCurvature(c.Point.Z)) < 1e-12);
            var (dp, dm) = TestMeshes.HyperboloidRulings(c.Point);
            Assert.True(c.HasAsymptotic);
            double best = Math.Min(Deg(c.A1, dp) + Deg(c.A2, dm), Deg(c.A1, dm) + Deg(c.A2, dp));
            Assert.True(best < 1e-5, $"asymptotic directions are the rulings: {best}° (TestMeshes.HyperboloidRulings normalises numerically)");
        }
        Assert.True(Math.Abs(hyp.At(1, 0).H) < 1e-12, "H = 0 on the throat when a = c");

        var cat = AnalyticShapes.Surface("catenoid");
        var mesh = TestMeshes.CreateCatenoid(1, 2, 32, 16);
        foreach (var p in mesh.Vertices)
        {
            double u = Math.Atan2(p.Y, p.X), v = p.Z;
            var c = cat.At(u, v);
            Assert.True((c.Point - p).Length < 1e-12, "TestMeshes.CreateCatenoid vertices lie on AnalyticShapes' catenoid");
            Assert.True(Math.Abs(c.K - TestMeshes.CatenoidGaussianCurvature(v)) < 1e-12);
            Assert.True(Math.Abs(c.H) < 1e-12, "minimal");
            Assert.True(Math.Abs(Deg(c.A1, cat.Xv!(u, v)) - 45) < 1e-6 && Math.Abs(Deg(c.A2, cat.Xv(u, v)) - 45) < 1e-6, "asymptotic directions at 45° to the meridian");
            Assert.True(Math.Abs(Deg(c.D1, c.D2) - 90) < 1e-6);
        }
        // and the torus of TestMeshes lies on AnalyticShapes' torus
        var tor = AnalyticShapes.Surface("torus");
        foreach (var p in TestMeshes.CreateTorus(3, 1, 16, 8).Vertices)
        {
            double u = Math.Atan2(p.Y, p.X), rho = Math.Sqrt(p.X * p.X + p.Y * p.Y), v = Math.Atan2(p.Z, rho - 3);
            Assert.True((tor.At(u, v).Point - p).Length < 1e-12);
        }
    }

    [Fact]
    public void Enneper_ClosedForm_AndHopfDirections()
    {
        // classic Enneper: K = −4 / (1 + u² + v²)⁴, H = 0, asymptotic directions X_u ± X_v (MathWorld)
        var e = AnalyticShapes.Surface("enneper");
        for (int i = 0; i < 25; i++)
        {
            double u = -1.1 + 2.2 * (i % 5) / 4, v = -1.1 + 2.2 * (i / 5) / 4;
            var c = e.At(u, v);
            double q = 1 + u * u + v * v;
            Assert.True(Math.Abs(c.K + 4 / (q * q * q * q)) < 1e-12, $"K {c.K}");
            Assert.True(Math.Abs(c.H) < 1e-12);
            var xu = e.Xu!(u, v); var xv = e.Xv!(u, v);
            double best = Math.Min(Deg(c.A1, xu + xv) + Deg(c.A2, xu - xv), Deg(c.A1, xu - xv) + Deg(c.A2, xu + xv));
            Assert.True(best < 1e-7, $"{best}");
        }
        // n-fold Enneper (polar chart) against TestMeshes' Hopf-differential prediction, which was derived by hand
        foreach (int n in new[] { 2, 3, 4 })
        {
            var s = AnalyticShapes.Surface("enneper3", n, 0.9);
            for (int i = 0; i < 40; i++)
            {
                double phi = 2 * Math.PI * i / 40 + 0.01, r = 0.15 + 0.7 * (i % 7) / 6;
                var c = s.At(phi, r);
                Assert.True(Math.Abs(c.H) < 1e-10, $"n = {n}: H = {c.H}");
                Assert.True(c.K < 0);
                var (dp, dm) = TestMeshes.EnneperAsymptoticDirections(n, r * Math.Cos(phi), r * Math.Sin(phi));
                double best = Math.Min(Deg(c.A1, dp) + Deg(c.A2, dm), Deg(c.A1, dm) + Deg(c.A2, dp));
                Assert.True(best < 1e-4, $"n = {n}, φ = {phi:F2}, r = {r:F2}: {best}° (finite differences in TestMeshes)");
            }
        }
    }

    [Fact]
    public void MonkeySaddle_MathWorldFormulas()
    {
        // K = −36 (x² + y²) / (1 + 9 (x² + y²)²)², |H| = 27 |x (−x⁴ + 2 x² y² + 3 y⁴)| / (1 + 9 (x² + y²)²)^{3/2}
        var s = AnalyticShapes.Surface("monkey");
        int sign = 0;
        for (int i = 0; i < 36; i++)
        {
            double x = -0.9 + 1.8 * (i % 6) / 5, y = -0.9 + 1.8 * (i / 6) / 5;
            var c = s.At(x, y);
            double q = x * x + y * y, d = 1 + 9 * q * q;
            Assert.True(Math.Abs(c.K + 36 * q / (d * d)) < 1e-12, $"K at ({x}, {y}): {c.K}");
            double Hmw = 27 * x * (-x * x * x * x + 2 * x * x * y * y + 3 * y * y * y * y) / Math.Pow(d, 1.5);
            Assert.True(Math.Abs(Math.Abs(c.H) - Math.Abs(Hmw)) < 1e-12, $"|H| at ({x}, {y}): {c.H} vs {Hmw}");
            if (Math.Abs(Hmw) > 1e-6)
            {
                int sg = Math.Sign(c.H * Hmw);
                if (sign == 0) sign = sg; else Assert.Equal(sign, sg);   // one fixed sign convention between the two
            }
            Assert.True(q < 1e-12 || c.HasAsymptotic);
        }
        // the flat point: K = H = k1 = k2 = 0, so every direction is asymptotic and none is singled out
        var o = s.At(0, 0);
        Assert.True(Math.Abs(o.K) < 1e-15 && Math.Abs(o.H) < 1e-15 && Math.Abs(o.K1) < 1e-15 && Math.Abs(o.K2) < 1e-15);
        Assert.False(o.HasAsymptotic);
        Assert.False(o.Degenerate);
        // In polar coordinates z = r³ cos 3θ has II ∝ cos 3θ (dr² − r² dθ²) − 2 sin 3θ r dr dθ (orthonormal frame,
        // flat limit), so the asymptotic lines make the angle φ = ±45° − 3θ/2 with the ray: the line field turns by
        // −540° relative to the ray, −180° absolutely (index −½), and the three straight asymptotic lines through
        // the flat point (θ = 30°, 90°, 150°, where cos 3θ = 0) each belong to family A on one side and to
        // family B on the other — which is how the two families are one family globally (the 3-fold Enneper too).
        double worstPhi = 0;
        for (int k = 0; k < 360; k += 5)
        {
            double th = k * Math.PI / 180, x = 0.2 * Math.Cos(th), y = 0.2 * Math.Sin(th);
            var c = s.At(x, y);
            var radial = (Math.Cos(th) * s.Xu!(x, y) + Math.Sin(th) * s.Xv!(x, y)).Normalized();
            double Phi(Vec3d A) => Math.Atan2(Vec3d.Dot(Vec3d.Cross(radial, A), c.Normal), Vec3d.Dot(radial, A)) * 180 / Math.PI;
            double expect = 45 - 1.5 * k;
            double Mod(double d) { d = ((d % 180) + 180) % 180; return Math.Min(d, 180 - d); }
            double err = Math.Min(Mod(Phi(c.A1) - expect), Mod(Phi(c.A2) - expect));
            worstPhi = Math.Max(worstPhi, err);
        }
        Assert.True(worstPhi < 0.5, $"φ = 45° − 3θ/2 holds to {worstPhi:F2}° at r = 0.2 (the tangent-plane tilt is 7°)");
        // the straight line θ = 30°: an asymptotic curve, family A north-east of the flat point and family B south-west
        var ne = s.At(0.3 * Math.Cos(Math.PI / 6), 0.3 * Math.Sin(Math.PI / 6));
        var sw = s.At(-0.3 * Math.Cos(Math.PI / 6), -0.3 * Math.Sin(Math.PI / 6));
        var along = new Vec3d(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0);
        Assert.True(Math.Min(Deg(ne.A1, along), Deg(ne.A2, along)) < 1e-6 && Math.Min(Deg(sw.A1, along), Deg(sw.A2, along)) < 1e-6, "the line is asymptotic on both sides");
        // continue family A from the north-east point to the south-west point along the circle r = 0.3 and read which line it is there
        Vec3d prev = Deg(ne.A1, along) < Deg(ne.A2, along) ? ne.A1 : ne.A2;
        for (int k = 1; k <= 180; k++)
        {
            double th = Math.PI / 6 + Math.PI * k / 180;
            var c = s.At(0.3 * Math.Cos(th), 0.3 * Math.Sin(th));
            prev = Deg(c.A1, prev) < Deg(c.A2, prev) ? c.A1 : c.A2;
        }
        Assert.True(Deg(prev, along) > 89.99, $"family A arrives at the south-west point {Deg(prev, along):F2}° from the line: the line there is family B");
    }

    [Fact]
    public void Ellipsoid_KFormulaAndFourUmbilics()
    {
        double a = 1.5, b = 1.0, c = 0.7;
        var s = AnalyticShapes.Surface("ellipsoid");
        for (int i = 0; i < 30; i++)
        {
            double u = 0.21 * i, v = 0.1 + 2.9 * i / 29;
            var cv = s.At(u, v);
            var p = cv.Point;
            double w = p.X * p.X / Math.Pow(a, 4) + p.Y * p.Y / Math.Pow(b, 4) + p.Z * p.Z / Math.Pow(c, 4);
            Assert.True(Math.Abs(cv.K - 1 / (a * a * b * b * c * c * w * w)) < 1e-10, $"K {cv.K}");
            Assert.True(cv.K > 0 && !cv.HasAsymptotic);
        }
        // umbilics of the triaxial ellipsoid a > b > c: (±a √((a² − b²)/(a² − c²)), 0, ±c √((b² − c²)/(a² − c²)))
        double ux = a * Math.Sqrt((a * a - b * b) / (a * a - c * c)), uz = c * Math.Sqrt((b * b - c * c) / (a * a - c * c));
        foreach (int sx in new[] { 1, -1 })
            foreach (int sz in new[] { 1, -1 })
            {
                double v = Math.Acos(-sz * uz / c), u = Math.Acos(sx * ux / (a * Math.Sin(v)));
                var cv = s.At(u, v);
                Assert.True((cv.Point - new Vec3d(sx * ux, 0, sz * uz)).Length < 1e-12);
                Assert.True(cv.UmbilicMeasure < 1e-7, $"k1 = {cv.K1}, k2 = {cv.K2}");
            }
        // and exactly four: local minima of |k1 − k2| / max|k| on a 720 × 360 parameter grid
        int nu = 720, nv = 360; var M = new double[nu, nv + 1];
        for (int i = 0; i < nu; i++) for (int j = 0; j <= nv; j++) { var cv = s.At(2 * Math.PI * i / nu, Math.PI * j / nv); M[i, j] = cv.Degenerate ? 1 : cv.UmbilicMeasure; }
        var minima = new List<(double u, double v)>();
        for (int i = 0; i < nu; i++)
            for (int j = 1; j < nv; j++)
            {
                bool local = true;
                for (int di = -1; di <= 1 && local; di++) for (int dj = -1; dj <= 1; dj++) { if (di == 0 && dj == 0) continue; if (M[(i + di + nu) % nu, j + dj] < M[i, j]) { local = false; break; } }
                if (local && M[i, j] < 0.05) minima.Add((2 * Math.PI * i / nu, Math.PI * j / nv));
            }
        Assert.Equal(4, minima.Count);
        foreach (var (u, v) in minima)
        {
            var p = s.At(u, v).Point;
            Assert.True(Math.Abs(Math.Abs(p.X) - ux) < 0.01 && Math.Abs(p.Y) < 0.01 && Math.Abs(Math.Abs(p.Z) - uz) < 0.01);
        }
        // the poles of the chart are not umbilics (k1 = c/b², k2 = c/a²)
        var pole = s.At(0, 1e-6);
        Assert.True(Math.Abs(pole.K1 - c / (b * b)) < 1e-6 && Math.Abs(pole.K2 - c / (a * a)) < 1e-6);
    }

    [Fact]
    public void Developables_KZeroAndOneRuling()
    {
        // vault (cylinder): K = 0, k1 = 1/R, k2 = 0, the single asymptotic direction is the ruling (y)
        var vault = AnalyticShapes.Surface("vault");   // R = 1.5
        for (int i = 0; i < 10; i++)
        {
            var c = vault.At(-0.9 + 1.8 * i / 9, 0.4 * i);
            Assert.True(Math.Abs(c.K) < 1e-12 && Math.Abs(c.K1 - 1 / 1.5) < 1e-12 && Math.Abs(c.K2) < 1e-12, $"K {c.K}, k1 {c.K1}, k2 {c.K2}");
            Assert.True(c.HasAsymptotic && Deg(c.A1, new Vec3d(0, 1, 0)) < 1e-6 && Deg(c.A2, new Vec3d(0, 1, 0)) < 1e-6);
            Assert.True(Deg(c.D2, new Vec3d(0, 1, 0)) < 1e-6, "k2 = 0 along the ruling");
            Assert.True(c.Normal.Z > 0, "the vault's normal points up (outward)");
        }
        // cone: K = 0, k2 = 0 along the generator, k1 = cos α / ρ with ρ the distance to the axis
        var cone = AnalyticShapes.Surface("cone");   // r = 1, h = 1.5, rTop = 0.15
        double tanA = (1 - 0.15) / 1.5, cosA = 1 / Math.Sqrt(1 + tanA * tanA);
        for (int i = 0; i < 10; i++)
        {
            double u = 0.5 * i, v = 0.05 + 0.9 * i / 9;
            var c = cone.At(u, v);
            double rho = 1 + (0.15 - 1) * v;
            Assert.True(Math.Abs(c.K) < 1e-12 && Math.Abs(c.K2) < 1e-12 && Math.Abs(c.K1 - cosA / rho) < 1e-12, $"k1 {c.K1} vs {cosA / rho}");
            Assert.True(Deg(c.A1, cone.Xv!(u, v)) < 1e-6 && Deg(c.D2, cone.Xv(u, v)) < 1e-6);
        }
        // the bilinear patch: K < 0 and its asymptotic directions are the two rulings ∂u, ∂v
        var ruled = AnalyticShapes.Surface("ruled");
        for (int i = 0; i < 16; i++)
        {
            double u = 0.1 + 0.8 * (i % 4) / 3, v = 0.1 + 0.8 * (i / 4) / 3;
            var c = ruled.At(u, v);
            Assert.True(c.K < 0 && c.HasAsymptotic);
            double best = Math.Min(Deg(c.A1, ruled.Xu!(u, v)) + Deg(c.A2, ruled.Xv!(u, v)), Deg(c.A1, ruled.Xv(u, v)) + Deg(c.A2, ruled.Xu(u, v)));
            Assert.True(best < 1e-7, $"{best}");
        }
    }

    [Fact]
    public void Implicit_SphereAndNodalSurfaces()
    {
        // Goldman 2005 on F = |x|² − R²: K = 1/R², H = +1/R in Mite's sign for the outward gradient
        double R = 2;
        var sph = new AnalyticSurface("isphere", p => p.LengthSquared - R * R, p => 2 * p, p => 2 * Matrix3d.Identity);
        var rnd = new Random(5);
        for (int k = 0; k < 20; k++)
        {
            var p = R * new Vec3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized();
            var c = sph.AtPoint(p);
            Assert.True(Math.Abs(c.K - 1 / (R * R)) < 1e-12 && Math.Abs(c.H - 1 / R) < 1e-12 && Math.Abs(c.K1 - c.K2) < 1e-7);
            Assert.True(Deg(c.Normal, p) < 1e-9 && Math.Abs(Vec3d.Dot(c.D1, c.Normal)) < 1e-9 && Math.Abs(Vec3d.Dot(c.D2, c.Normal)) < 1e-9);
        }
        // an implicit cylinder x² + y² = 1: K = 0, k1 = 1, k2 = 0, principal direction 2 = the axis
        var cyl = new AnalyticSurface("icyl", p => p.X * p.X + p.Y * p.Y - 1, p => new Vec3d(2 * p.X, 2 * p.Y, 0), p => new Matrix3d(2, 0, 0, 0, 2, 0, 0, 0, 0));
        var cc = cyl.AtPoint(new Vec3d(Math.Cos(0.7), Math.Sin(0.7), 0.3));
        Assert.True(Math.Abs(cc.K) < 1e-12 && Math.Abs(cc.K1 - 1) < 1e-12 && Math.Abs(cc.K2) < 1e-12 && Deg(cc.D2, new Vec3d(0, 0, 1)) < 1e-6 && cc.HasAsymptotic && Deg(cc.A1, new Vec3d(0, 0, 1)) < 1e-6);
        // the Schwarz D nodal surface: at every vertex of the unrelaxed marching-tetrahedra mesh K ≤ 0 and |H| ≪ k1
        var sd = AnalyticShapes.Surface("schwarzd");
        var nodal = AnalyticShapes.Build("schwarzd", 1, 24, -1);   // p3 < 0 → no relaxation
        int positive = 0, n = 0; double sumH = 0, sumK1 = 0;
        foreach (var p in nodal.Vertices)
        {
            Assert.True(Math.Abs(sd.F!(p)) < 0.06, "marching tetrahedra vertices lie on the nodal surface up to the linear interpolation along the cell edges");
            var c = sd.AtPoint(p);
            if (c.Degenerate) continue;
            n++; if (c.K > 1e-9) positive++; sumH += Math.Abs(c.H); sumK1 += Math.Abs(c.K1);
        }
        Assert.Equal(0, positive);
        Assert.True(sumH / n < 0.1 * sumK1 / n, $"nodal approximation: mean |H| {sumH / n:F4} vs mean k1 {sumK1 / n:F4}");
    }

    public static IEnumerable<object[]> ShapesWithoutTests() =>
        new[] { "dome", "ellipsoid", "monkey", "vault", "cone", "annulus", "wave", "sphere", "torus" }.Select(s => new object[] { s });

    /// <summary>Mean absolute error of the discrete K and H over the interior vertices, relative to the largest |K| / |k| of the truth.</summary>
    private static (double k, double h, double k1, double k2, int n) MeshError(string name, int resolution)
    {
        var s = AnalyticShapes.Surface(name);
        var mesh = s.Mesh(resolution, out var uv, out _);
        var truth = s.AtVertices(uv);
        var K = GaussianCurvature.Compute(mesh); var H = MeanCurvature.Compute(mesh).Values; var pc = PrincipalCurvature.Compute(mesh);
        var bnd = mesh.BuildBoundaryVertexFlags();
        double maxK = 0, maxk = 0, eK = 0, eH = 0, e1 = 0, e2 = 0; int n = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (bnd[i] || truth[i].Degenerate) continue;
            n++;
            maxK = Math.Max(maxK, Math.Abs(truth[i].K)); maxk = Math.Max(maxk, Math.Max(Math.Abs(truth[i].K1), Math.Abs(truth[i].K2)));
            eK += Math.Abs(K[i] - truth[i].K); eH += Math.Abs(H[i] - truth[i].H);
            e1 += Math.Abs(pc.K1[i] - truth[i].K1); e2 += Math.Abs(pc.K2[i] - truth[i].K2);
        }
        maxK = Math.Max(maxK, 1e-300); maxk = Math.Max(maxk, 1e-300);
        return (eK / n / Math.Max(maxK, maxk * maxk), eH / n / maxk, e1 / n / maxk, e2 / n / maxk, n);
    }

    [Theory]
    [MemberData(nameof(ShapesWithoutTests))]
    public void DiscreteCurvatureConvergesToTheTruth(string name)
    {
        // Gauss–Bonnet K, cotangent H and the Rusinkiewicz k1, k2 against the exact values at every interior
        // vertex: the mean absolute error (relative to the shape's largest curvature) is below 2.5 % at
        // resolution 48 and falls by at least a quarter when the resolution doubles (Magid et al. 2007
        // report convergence of both schemes on analytic surfaces; the developables are exact for K)
        var c48 = MeshError(name, 48);
        var c96 = MeshError(name, 96);
        Assert.True(c48.k < 0.025 && c48.h < 0.025, $"{name} at 48: K {c48.k:E2}, H {c48.h:E2} ({c48.n} interior vertices)");
        Assert.True(c48.k1 < 0.1 && c48.k2 < 0.1, $"{name} at 48: k1 {c48.k1:E2}, k2 {c48.k2:E2}");
        bool developable = name == "vault" || name == "cone";
        if (!developable) Assert.True(c96.k < 0.75 * c48.k, $"{name}: K error {c48.k:E2} → {c96.k:E2}");
        else Assert.True(c96.k < 1e-9 && c48.k < 1e-9, $"{name}: K error {c48.k:E2} → {c96.k:E2} (exact on a developable)");
        Assert.True(c96.h < 0.75 * c48.h, $"{name}: H error {c48.h:E2} → {c96.h:E2}");
        Assert.True(c96.k1 < 0.75 * c48.k1 && c96.k2 < 0.75 * c48.k2, $"{name}: k1 {c48.k1:E2} → {c96.k1:E2}, k2 {c48.k2:E2} → {c96.k2:E2}");
    }

    [Fact]
    public void AngleDeficitIsBlindToAValenceFourStarAlongTheAsymptoticLines()
    {
        // At the saddle points of the wave z = 0.3 sin 2x cos 2y the mesh lines run along the asymptotic
        // directions and the shorter-diagonal rule leaves valence-4 stars whose four neighbours are coplanar
        // with the vertex: the piecewise-linear surface is flat there, so Gauss–Bonnet reads K = 0 where the
        // truth is −1.44 — however fine the mesh. The tensor estimator (normals of a two-ring) is not fooled.
        var s = AnalyticShapes.Surface("wave");
        var mesh = s.Mesh(48, out var uv, out _);
        var truth = s.AtVertices(uv);
        var K = GaussianCurvature.Compute(mesh); var pc = PrincipalCurvature.Compute(mesh);
        var bnd = mesh.BuildBoundaryVertexFlags();
        int worst = Enumerable.Range(0, mesh.VertexCount).Where(i => !bnd[i]).OrderByDescending(i => Math.Abs(K[i] - truth[i].K)).First();
        Assert.True(Math.Abs(K[worst]) < 1e-12 && truth[worst].K < -1.4, $"K {K[worst]} vs {truth[worst].K} at {mesh.Vertices[worst]}");
        Assert.Equal(4, mesh.Faces.Count(f => f.Contains(worst)));
        double kk = pc.K1[worst] * pc.K2[worst];
        Assert.True(Math.Abs(kk - truth[worst].K) < 0.25 * Math.Abs(truth[worst].K), $"k1 k2 = {kk} vs {truth[worst].K}");
        // the same on the bilinear patch, whose grid lines are its rulings: the shorter-diagonal rule leaves one
        // valence-4 vertex where the diagonal flips, and it reads K = 0 exactly against −0.22
        var r = AnalyticShapes.Surface("ruled");
        var rm = r.Mesh(48, out var ruv, out _);
        var rt = r.AtVertices(ruv); var rK = GaussianCurvature.Compute(rm); var rb = rm.BuildBoundaryVertexFlags();
        int four = 0;
        for (int i = 0; i < rm.VertexCount; i++)
        {
            if (rb[i] || rm.Faces.Count(f => f.Contains(i)) != 4) continue;
            four++;
            Assert.True(Math.Abs(rK[i]) < 1e-9 && rt[i].K < -0.2, $"ruled v{i}: K {rK[i]} vs {rt[i].K}");
        }
        Assert.True(four >= 1, "no valence-4 interior vertex on the bilinear patch");
    }

    [Fact]
    public void Umbilics_FindsTheFourOfTheEllipsoidAndNothingElse()
    {
        double a = 1.5, b = 1.0, c = 0.7;
        double ux = a * Math.Sqrt((a * a - b * b) / (a * a - c * c)), uz = c * Math.Sqrt((b * b - c * c) / (a * a - c * c));
        var mesh = AnalyticShapes.Build("ellipsoid", 0, 0, 0, 96);
        var pc = PrincipalCurvature.Compute(mesh);
        foreach (double tol in new[] { 0.05, 0.1, 0.2 })
        {
            var found = Umbilics.Find(pc, tol);
            var clusters = new List<List<int>>();
            foreach (var i in found)
            {
                var cl = clusters.FirstOrDefault(cl => cl.Any(j => (mesh.Vertices[i] - mesh.Vertices[j]).Length < 0.3));
                if (cl == null) { cl = new List<int>(); clusters.Add(cl); }
                cl.Add(i);
            }
            Assert.Equal(4, clusters.Count);
            foreach (var cl in clusters)
            {
                var cen = new Vec3d(cl.Average(i => mesh.Vertices[i].X), cl.Average(i => mesh.Vertices[i].Y), cl.Average(i => mesh.Vertices[i].Z));
                double d = new[] { 1, -1 }.SelectMany(sx => new[] { 1, -1 }.Select(sz => (cen - new Vec3d(sx * ux, 0, sz * uz)).Length)).Min();
                Assert.True(d < 0.05, $"tolerance {tol}: cluster of {cl.Count} at {cen}, {d:F3} from the nearest analytic umbilic");
            }
        }
    }
}
