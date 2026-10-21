using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Dynamics;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;

namespace Mite.Tests;

/// <summary>
/// Form finding against closed forms: the force density method (Schek 1974)
/// on nets whose equilibrium is known exactly, dynamic relaxation (Barnes
/// 1999) on an elastic chain whose equilibrium is a one-parameter shooting
/// problem, the equivalence of the two at RestScale 0, soap films, and the
/// lath bending element on the Euler elastica.
/// </summary>
public class DynamicRelaxationTests
{
    private readonly ITestOutputHelper _out;
    public DynamicRelaxationTests(ITestOutputHelper output) { _out = output; }

    // ---------------------------------------------------------------- helpers

    /// <summary>Chain of n springs from a to b as 2-vertex faces (edges only).</summary>
    private static MeshData Chain(Vec3d a, Vec3d b, int n)
    {
        var verts = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) verts[i] = a + (i / (double)n) * (b - a);
        var faces = new int[n][];
        for (int i = 0; i < n; i++) faces[i] = new[] { i, i + 1 };
        return new MeshData(verts, faces);
    }

    private static bool[] Ends(int nv)
    {
        var f = new bool[nv];
        f[0] = f[nv - 1] = true;
        return f;
    }

    private static bool[] Border(int nx, int ny)
    {
        var f = new bool[(nx + 1) * (ny + 1)];
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                f[j * (nx + 1) + i] = i == 0 || i == nx || j == 0 || j == ny;
        return f;
    }

    private static MeshData WithZ(MeshData grid, Func<double, double, double> z)
    {
        var v = new Vec3d[grid.VertexCount];
        for (int i = 0; i < v.Length; i++) v[i] = new Vec3d(grid.Vertices[i].X, grid.Vertices[i].Y, z(grid.Vertices[i].X, grid.Vertices[i].Y));
        return new MeshData(v, grid.Faces);
    }

    /// <summary>
    /// Exact equilibrium of a chain of n linear springs (stiffness k, rest
    /// length l0) between (0,0,0) and (D,0,0) with weight w at every free
    /// node: the horizontal force H is the one root of the span equation,
    /// Σ_j L_j H / T_j = D with V_j = w((n−1)/2 − j), T_j = √(H² + V_j²),
    /// L_j = l0 + T_j / k (bisection to 1e-15 relative).
    /// </summary>
    private static Vec3d[] ElasticChain(int n, double D, double k, double l0, double w, out double H)
    {
        double Span(double h)
        {
            double s = 0;
            for (int j = 0; j < n; j++)
            {
                double vj = w * ((n - 1) / 2.0 - j);
                double t = Math.Sqrt(h * h + vj * vj);
                s += (l0 + t / k) * h / t;
            }
            return s;
        }
        double lo = 0, hi = 1;
        while (Span(hi) < D) hi *= 2;
        for (int it = 0; it < 200; it++)
        {
            double mid = 0.5 * (lo + hi);
            if (Span(mid) < D) lo = mid; else hi = mid;
            if (hi - lo < 1e-16 * hi) break;
        }
        H = 0.5 * (lo + hi);
        var p = new Vec3d[n + 1];
        double x = 0, z = 0;
        p[0] = Vec3d.Zero;
        for (int j = 0; j < n; j++)
        {
            double vj = w * ((n - 1) / 2.0 - j);
            double t = Math.Sqrt(H * H + vj * vj);
            double L = l0 + t / k;
            x += L * H / t;
            z -= L * vj / t;
            p[j + 1] = new Vec3d(x, 0, z);
        }
        return p;
    }

    private static double MaxDistance(Vec3d[] a, Vec3d[] b)
    {
        double m = 0;
        for (int i = 0; i < a.Length; i++) m = Math.Max(m, (a[i] - b[i]).Length);
        return m;
    }

    // ------------------------------------------------------ force density

    [Fact]
    public void ForceDensity_SaddleBoundary_IsDiscreteHarmonic()
    {
        // z = x² − y² is harmonic for the 5-point Laplacian (Δ_h(x²) = 2, Δ_h(y²) = 2),
        // so with uniform q and no load every free node sits exactly on the saddle
        int n = 12;
        var grid = WithZ(TestMeshes.CreateQuadGrid(n, n, 1.0), (x, y) => x * x - y * y);
        var fixedV = Border(n, n);
        var start = new Vec3d[grid.VertexCount];
        for (int i = 0; i < start.Length; i++) start[i] = fixedV[i] ? grid.Vertices[i] : new Vec3d(grid.Vertices[i].X + 3, -7, 50);
        var edges = grid.BuildEdges();
        var q = new double[edges.Length];
        Array.Fill(q, 2.5);
        var r = ForceDensityMethod.Compute(new MeshData(start, grid.Faces), q, new Vec3d[grid.VertexCount], fixedV);
        double worst = MaxDistance(r.Vertices, grid.Vertices);
        _out.WriteLine($"saddle: worst node error {worst:E2}");
        Assert.True(worst < 1e-10, $"free nodes must lie on z = x² − y²: {worst:E2}");
    }

    [Fact]
    public void ForceDensity_HangingChain_IsParabola()
    {
        // n equal force densities q, load P at every free node: x uniform,
        // z_i = −P i (n − i) / (2 q)
        int n = 9; double D = 4.5, q = 3.0, P = 0.7;
        var chain = Chain(Vec3d.Zero, new Vec3d(D, 0, 0), n);
        // scramble the start: FDM is independent of the initial free positions
        var start = (Vec3d[])chain.Vertices.Clone();
        var rng = new Random(5);
        for (int i = 1; i < n; i++) start[i] = new Vec3d(rng.NextDouble() * 9 - 2, rng.NextDouble(), rng.NextDouble() * 4);
        var loads = new Vec3d[n + 1];
        for (int i = 0; i < loads.Length; i++) loads[i] = new Vec3d(0, 0, -P);
        var qs = new double[n]; Array.Fill(qs, q);
        var r = ForceDensityMethod.Compute(new MeshData(start, chain.Faces), qs, loads, Ends(n + 1));
        double worst = 0;
        for (int i = 0; i <= n; i++)
        {
            var exact = new Vec3d(i * D / n, 0, -P * i * (n - i) / (2 * q));
            worst = Math.Max(worst, (r.Vertices[i] - exact).Length);
        }
        _out.WriteLine($"chain parabola: worst {worst:E2}");
        Assert.True(worst < 1e-12, $"nodes must lie on the discrete parabola: {worst:E2}");
    }

    // --------------------------------------------------- dynamic relaxation

    [Fact]
    public void DynamicRelaxation_ElasticChain_MatchesShooting()
    {
        int n = 16; double D = 8, k = 20, g = 1.0;
        var chain = Chain(Vec3d.Zero, new Vec3d(D, 0, 0), n);
        var exact = ElasticChain(n, D, k, D / n, g, out double H);
        var opts = new DynamicRelaxation.Options { Stiffness = k, Gravity = new Vec3d(0, 0, -g), Tolerance = 1e-9, MaxIterations = 20000 };
        var r = DynamicRelaxation.Compute(chain, Ends(n + 1), opts);
        double worst = MaxDistance(r.Vertices, exact);
        _out.WriteLine($"elastic chain: H = {H:F6}, {r.Iterations} steps, {r.Resets} resets, residual {r.Residual:E2}, worst node {worst:E2}");
        Assert.True(r.Converged, "converges");
        Assert.True(worst < 1e-7, $"worst node error {worst:E2}");
        // the horizontal force is the x component of every edge force
        for (int e = 0; e < n; e++)
        {
            Vec3d d = (r.Vertices[e + 1] - r.Vertices[e]).Normalized();
            Assert.True(Math.Abs(r.EdgeForces[e] * d.X - H) < 1e-6, $"edge {e}: H {r.EdgeForces[e] * d.X} vs {H}");
        }
    }

    [Fact]
    public void DynamicRelaxation_SchemesAgree_BarnesConvergesFaster()
    {
        // the 1.3.2 integrator (unit masses, velocity reset) and Barnes' scheme
        // (fictitious masses, reset to the peak) must find the same equilibrium;
        // the step counts are the measurement of the session
        int n = 20;
        var grid = TestMeshes.CreateQuadGrid(n, n, 1.0);
        var b = Border(n, n);
        var cases = new (string name, DynamicRelaxation.Options o)[]
        {
            ("1.3.2 (unit masses, velocity reset)", new DynamicRelaxation.Options { FictitiousMasses = false, ResetToPeak = false }),
            ("fictitious masses, velocity reset", new DynamicRelaxation.Options { ResetToPeak = false }),
            ("fictitious masses, reset to peak (Barnes, default)", new DynamicRelaxation.Options()),
            ("fictitious masses, reset to fitted peak", new DynamicRelaxation.Options { PeakFit = true }),
        };
        var results = new List<DynamicRelaxation.Result>();
        foreach (var (name, o) in cases)
        {
            o.Stiffness = 10; o.Gravity = new Vec3d(0, 0, -1); o.MaxIterations = 20000; o.Tolerance = 1e-6;
            var r = DynamicRelaxation.Compute(grid, b, o);
            results.Add(r);
            _out.WriteLine($"{name}: {r.Iterations} steps, {r.Resets} resets, residual {r.Residual:E2}, converged {r.Converged}");
            Assert.True(r.Converged, name);
        }
        for (int i = 1; i < results.Count; i++)
        {
            double d = MaxDistance(results[i].Vertices, results[0].Vertices);
            _out.WriteLine($"  case {i} vs 1.3.2: {d:E2}");
            Assert.True(d < 2e-4, $"case {i} differs from the 1.3.2 equilibrium by {d:E2}");
        }
        Assert.True(results[2].Iterations * 2 < results[0].Iterations,
            $"Barnes' scheme ({results[2].Iterations}) should need less than half the steps of 1.3.2 ({results[0].Iterations})");
    }

    [Fact]
    public void DynamicRelaxation_RestScaleZero_IsForceDensity()
    {
        // a spring with rest length 0 is a force density q = k: the dynamic
        // result must equal Schek's linear solution
        int n = 10;
        var grid = WithZ(TestMeshes.CreateQuadGrid(n, n, 1.0), (x, y) => 0.3 * Math.Sin(x) * Math.Cos(y));
        var b = Border(n, n);
        var loads = new Vec3d[grid.VertexCount];
        for (int i = 0; i < loads.Length; i++) loads[i] = new Vec3d(0.1, 0, -1);
        var edges = grid.BuildEdges();
        var q = new double[edges.Length]; Array.Fill(q, 4.0);
        var fd = ForceDensityMethod.Compute(grid, q, loads, b);
        var dr = DynamicRelaxation.Compute(grid, b, new DynamicRelaxation.Options { Stiffness = 4.0, RestScale = 0, Loads = loads, Tolerance = 1e-10, MaxIterations = 20000 });
        double worst = MaxDistance(dr.Vertices, fd.Vertices);
        _out.WriteLine($"RestScale 0 vs FDM: {worst:E2} after {dr.Iterations} steps ({dr.Resets} resets)");
        Assert.True(dr.Converged);
        Assert.True(worst < 1e-8, $"DR at RestScale 0 must equal the force density solution: {worst:E2}");
    }

    [Fact]
    public void DynamicRelaxation_SoapFilm_FlatBoundaryIsExact_RingsGiveCatenoid()
    {
        // flat boundary: the film is the plane, to round-off
        int n = 10;
        var grid = TestMeshes.CreateQuadGrid(n, n, 1.0);
        var rng = new Random(2);
        var v = (Vec3d[])grid.Vertices.Clone();
        var b = Border(n, n);
        for (int i = 0; i < v.Length; i++) if (!b[i]) v[i] = new Vec3d(v[i].X, v[i].Y, rng.NextDouble() - 0.5);
        var r = DynamicRelaxation.Compute(new MeshData(v, grid.Faces), b, new DynamicRelaxation.Options { Stiffness = 0, AreaTension = 1, Tolerance = 1e-9, MaxIterations = 20000 });
        double worstZ = 0;
        for (int i = 0; i < v.Length; i++) worstZ = Math.Max(worstZ, Math.Abs(r.Vertices[i].Z));
        _out.WriteLine($"flat film: |z| ≤ {worstZ:E2} after {r.Iterations} steps");
        Assert.True(r.Converged);
        Assert.True(worstZ < 1e-7, $"a soap film on a flat boundary is flat: {worstZ:E2}");

        // two rings z = ±h of radius cosh h: the catenoid r = cosh z, waist 1
        foreach (int seg in new[] { 32, 64 })
        {
            int rows = seg / 4; double h = 0.6;
            var verts = new List<Vec3d>(); var faces = new List<int[]>();
            for (int j = 0; j <= rows; j++)
            {
                double z = -h + 2 * h * j / rows;
                for (int i = 0; i < seg; i++)
                {
                    double th = 2 * Math.PI * i / seg;
                    double rad = Math.Cosh(h); // start on the cylinder, not the answer
                    verts.Add(new Vec3d(rad * Math.Cos(th), rad * Math.Sin(th), z));
                }
            }
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * seg + i, bb = j * seg + (i + 1) % seg;
                    faces.Add(new[] { a, bb, bb + seg, a + seg });
                }
            var mesh = new MeshData(verts.ToArray(), faces.ToArray());
            var fixedV = new bool[mesh.VertexCount];
            for (int i = 0; i < seg; i++) { fixedV[i] = true; fixedV[rows * seg + i] = true; }
            var film = DynamicRelaxation.Compute(mesh, fixedV, new DynamicRelaxation.Options { Stiffness = 0, AreaTension = 1, Tolerance = 1e-8, MaxIterations = 40000 });
            double worst = 0;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = film.Vertices[i];
                double rad = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                worst = Math.Max(worst, Math.Abs(rad - Math.Cosh(p.Z)));
            }
            double edge = 2 * Math.PI / seg;
            _out.WriteLine($"catenoid {seg} × {rows}: worst |r − cosh z| = {worst:E2} ({worst / (edge * edge):F2} edge²), {film.Iterations} steps, {film.Resets} resets");
            Assert.True(film.Converged, "catenoid converges");
            Assert.True(worst < 0.5 * edge * edge, $"film within ½ edge² of the catenoid: {worst:E2} vs {0.5 * edge * edge:E2}");
        }
    }

    // ------------------------------------------------------------ bending

    [Fact]
    public void Bending_ForcesAreMinusGradientOfEnergy()
    {
        var rng = new Random(11);
        Vec3d R() => new Vec3d(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
        var x = new Vec3d[14];
        for (int i = 0; i < x.Length; i++) x[i] = new Vec3d(i * 0.5, 0, 0) + 0.3 * R();
        // an open lath of 7 and a closed ring of 7 (first vertex repeated)
        var laths = new[] { new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { 7, 8, 9, 10, 11, 12, 13, 7 } };
        double ei = 1.7;
        var forces = new Vec3d[x.Length];
        LathBending.AddForces(x, laths, ei, forces);
        double worst = 0, scale = 0;
        const double hstep = 1e-6;
        for (int i = 0; i < x.Length; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                Vec3d d = c == 0 ? new Vec3d(hstep, 0, 0) : c == 1 ? new Vec3d(0, hstep, 0) : new Vec3d(0, 0, hstep);
                var xp = (Vec3d[])x.Clone(); xp[i] = x[i] + d;
                var xm = (Vec3d[])x.Clone(); xm[i] = x[i] - d;
                double fd = -(LathBending.Energy(xp, laths, ei) - LathBending.Energy(xm, laths, ei)) / (2 * hstep);
                worst = Math.Max(worst, Math.Abs(fd - forces[i][c]));
                scale = Math.Max(scale, Math.Abs(forces[i][c]));
            }
        }
        _out.WriteLine($"bending gradient: worst |F + ∇E| = {worst:E2} against forces up to {scale:F3}");
        Assert.True(worst < 1e-6 * Math.Max(1, scale), $"forces must be −∇E: {worst:E2}");
        // self-equilibrated per lath: Σ F = 0 and Σ x × F = 0
        foreach (var lath in laths)
        {
            Vec3d sum = Vec3d.Zero, moment = Vec3d.Zero;
            foreach (int i in lath.Distinct()) { sum += forces[i]; moment += Vec3d.Cross(x[i], forces[i]); }
            Assert.True(sum.Length < 1e-10 && moment.Length < 1e-10, $"lath forces balance: {sum.Length:E2} / {moment.Length:E2}");
        }
        // the curvature is the reciprocal circumradius: three points on a circle of radius 2.5
        double rad = 2.5;
        double k = LathBending.Curvature(new Vec3d(rad, 0, 0), new Vec3d(rad * Math.Cos(0.7), rad * Math.Sin(0.7), 0), new Vec3d(rad * Math.Cos(1.9), rad * Math.Sin(1.9), 0));
        Assert.True(Math.Abs(k - 1 / rad) < 1e-13, $"κ = 1/R: {k} vs {1 / rad}");
    }

    [Fact]
    public void Bending_LathsRunStraightThroughQuadMeshes()
    {
        var grid = TestMeshes.CreateQuadGrid(5, 3, 1.0); // 6 × 4 vertices
        var laths = LathBending.FromQuadMesh(grid);
        Assert.Equal(6 + 4, laths.Length); // 4 rows of 6 and 6 columns of 4
        Assert.Equal(4, laths.Count(l => l.Length == 6));
        Assert.Equal(6, laths.Count(l => l.Length == 4));
        foreach (var l in laths)
        {
            // every lath is a straight line of the grid
            var p = l.Select(i => grid.Vertices[i]).ToArray();
            for (int i = 1; i < p.Length - 1; i++)
                Assert.True(LathBending.Curvature(p[i - 1], p[i], p[i + 1]) < 1e-12);
        }
        // a quad cylinder: the rings are closed laths, the generators open
        int seg = 12, rows = 3;
        var verts = new List<Vec3d>(); var faces = new List<int[]>();
        for (int j = 0; j <= rows; j++)
            for (int i = 0; i < seg; i++)
                verts.Add(new Vec3d(Math.Cos(2 * Math.PI * i / seg), Math.Sin(2 * Math.PI * i / seg), j));
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < seg; i++)
                faces.Add(new[] { j * seg + i, j * seg + (i + 1) % seg, (j + 1) * seg + (i + 1) % seg, (j + 1) * seg + i });
        var cyl = LathBending.FromQuadMesh(new MeshData(verts.ToArray(), faces.ToArray()));
        Assert.Equal(rows + 1 + seg, cyl.Length);
        Assert.Equal(rows + 1, cyl.Count(l => l.Length == seg + 1 && l[0] == l[seg]));
        Assert.Equal(seg, cyl.Count(l => l.Length == rows + 1 && l[0] != l[rows]));
        // triangles carry no laths
        Assert.Empty(LathBending.FromQuadMesh(grid.ToTriangulated()));
    }

    [Fact]
    public void Bending_Cantilever_SmallDeflection_ConvergesAsHSquared()
    {
        // tip load P on a cantilever: w = P L³ / 3 EI. The first two nodes are
        // fixed; the first hinge sits at node 1 and its lumped curvature covers
        // [h/2, 3h/2], so the clamp acts at x = h/2 and the beam length is
        // L − h/2 (the O(h) part of a node-based beam); the rest is O(h²)
        double L = 1, ei = 1, P = 0.01;
        var errors = new List<double>();
        foreach (int n in new[] { 10, 20, 40 })
        {
            var chain = Chain(Vec3d.Zero, new Vec3d(L, 0, 0), n);
            var fixedV = new bool[n + 1]; fixedV[0] = fixedV[1] = true;
            var loads = new Vec3d[n + 1]; loads[n] = new Vec3d(0, 0, -P);
            var r = DynamicRelaxation.Compute(chain, fixedV, new DynamicRelaxation.Options
            {
                Stiffness = 1e3 * n, Bending = ei, Laths = new[] { Enumerable.Range(0, n + 1).ToArray() }, // EA = 1e3: the axial force is P sin θ ≈ 0
                Loads = loads, Tolerance = 1e-7, MaxIterations = 200000
            });
            double h = L / n, le = L - 0.5 * h;
            double w = -r.Vertices[n].Z, exact = P * le * le * le / (3 * ei);
            double err = Math.Abs(w - exact) / exact;
            errors.Add(err);
            _out.WriteLine($"cantilever n = {n}: tip {w:E6} vs {exact:E6} ({err * 100:F3} %), {r.Iterations} steps, {r.Resets} resets");
            Assert.True(r.Converged, $"n = {n} converges");
        }
        Assert.True(errors[2] < 0.01, $"1 % at n = 40: {errors[2]:E2}");
        Assert.True(errors[1] < 0.35 * errors[0] && errors[2] < 0.35 * errors[1], $"second-order convergence: {string.Join(", ", errors.Select(e => e.ToString("E2")))}");
    }

    /// <summary>
    /// Pinned–pinned Euler elastica with end shortening: with k = sin(θ₀/2),
    /// the half length is K(k) / √(P/EI) and the chord is L (2 E(k)/K(k) − 1),
    /// so d / L = 2 E(k) / K(k) − 1 fixes k; the midspan rise is
    /// 2 k / √(P/EI) = 2 k L / (2 K(k)) = k L / K(k) (Love 1944 §263; the
    /// standard elliptic-integral solution of the elastica).
    /// </summary>
    private static (double rise, double theta0) ElasticaPinned(double L, double ratio)
    {
        double K(double k) => Integrate(t => 1 / Math.Sqrt(1 - k * k * Math.Sin(t) * Math.Sin(t)));
        double E(double k) => Integrate(t => Math.Sqrt(1 - k * k * Math.Sin(t) * Math.Sin(t)));
        double lo = 1e-9, hi = 1 - 1e-9;
        for (int it = 0; it < 200; it++)
        {
            double k = 0.5 * (lo + hi);
            double f = 2 * E(k) / K(k) - 1 - ratio; // decreasing in k
            if (f > 0) lo = k; else hi = k;
        }
        double kk = 0.5 * (lo + hi);
        return (kk * L / K(kk), 2 * Math.Asin(kk));
    }

    private static double Integrate(Func<double, double> f)
    {
        // composite Simpson on [0, π/2]
        int n = 4000; double h = Math.PI / 2 / n, s = f(0) + f(Math.PI / 2);
        for (int i = 1; i < n; i++) s += (i % 2 == 1 ? 4 : 2) * f(i * h);
        return s * h / 3;
    }

    [Fact]
    public void Bending_QuadGrid_ColumnLathsBuckleAsElasticas()
    {
        // an elastic gridshell from a quad mesh: 6 column laths of length L
        // pushed to a chord 0.8 L with their ends fixed, cross laths of pitch
        // 0.2 connecting them; by symmetry every column is the pinned elastica
        // and every cross lath stays straight. Laths come from the quad faces.
        int nx = 5, ny = 32; double L = 1, ratio = 0.8, pitch = 0.2;
        var (riseExact, _) = ElasticaPinned(L, ratio);
        var grid = TestMeshes.CreateQuadGrid(nx, ny, 1.0);
        var verts = new Vec3d[grid.VertexCount];
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                double s = j / (double)ny;
                verts[j * (nx + 1) + i] = new Vec3d(i * pitch, s * ratio * L, 0.05 * Math.Sin(Math.PI * s));
            }
        var mesh = new MeshData(verts, grid.Faces);
        var edges = mesh.BuildEdges();
        var rest = new double[edges.Length];
        for (int e = 0; e < edges.Length; e++)
            rest[e] = (edges[e].v1 - edges[e].v0 == 1) ? pitch : L / ny; // row edge : column edge
        var fixedV = new bool[mesh.VertexCount];
        for (int i = 0; i <= nx; i++) { fixedV[i] = true; fixedV[ny * (nx + 1) + i] = true; }
        var r = DynamicRelaxation.Compute(mesh, fixedV, new DynamicRelaxation.Options
        {
            Stiffness = 1e6 * ny, Bending = 1, RestLengths = rest, Tolerance = 1e-9, MaxIterations = 400000
        });
        Assert.True(r.Converged, "gridshell converges");
        Assert.Equal(nx + 1 + ny + 1, r.Laths.Length);
        double worstRise = 0, worstRow = 0, worstCurv = 0;
        for (int i = 0; i <= nx; i++)
        {
            double rise = 0;
            for (int j = 0; j <= ny; j++) rise = Math.Max(rise, r.Vertices[j * (nx + 1) + i].Z);
            worstRise = Math.Max(worstRise, Math.Abs(rise - riseExact) / riseExact);
        }
        for (int j = 1; j < ny; j++)
            for (int i = 1; i <= nx; i++)
                worstRow = Math.Max(worstRow, (r.Vertices[j * (nx + 1) + i] - r.Vertices[j * (nx + 1) + i - 1] - new Vec3d(pitch, 0, 0)).Length);
        for (int l = 0; l < r.Laths.Length; l++)
            if (r.Laths[l].Length == nx + 1) worstCurv = Math.Max(worstCurv, r.Curvatures[l].Max(Math.Abs));
        _out.WriteLine($"gridshell: rise error {worstRise * 100:F3} %, cross laths off a straight pitch by {worstRow:E2}, cross curvature {worstCurv:E2}, {r.Iterations} steps, {r.Resets} resets");
        Assert.True(worstRise < 1e-3, $"columns are elasticas: {worstRise:E2}");
        Assert.True(worstRow < 1e-6, $"cross laths stay straight: {worstRow:E2}");
        Assert.True(worstCurv < 1e-6, $"cross laths carry no curvature: {worstCurv:E2}");
    }

    [Fact]
    public void Bending_Elastica_EndShortening_MatchesEllipticIntegrals()
    {
        // a straight lath of length L pushed together to a chord 0.8 L (ends
        // pinned: only the end nodes fixed) buckles into the Euler elastica;
        // the springs are stiff so the arc length stays L
        double L = 1, ratio = 0.8, ei = 1;
        var (riseExact, theta0) = ElasticaPinned(L, ratio);
        _out.WriteLine($"elastica d/L = {ratio}: rise {riseExact:F6}, end angle {theta0 * 180 / Math.PI:F3}°");
        var errs = new List<double>();
        foreach (int n in new[] { 16, 32, 64 })
        {
            // start on a shallow arc so the buckling direction is set
            var verts = new Vec3d[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double s = i / (double)n;
                verts[i] = new Vec3d(s * ratio * L, 0, 0.05 * Math.Sin(Math.PI * s));
            }
            // the lath is cut to length L: rest lengths L / n on the shortened chord
            var faces = Chain(Vec3d.Zero, new Vec3d(L, 0, 0), n).Faces;
            var fixedV = Ends(n + 1);
            var restLengths = new double[n]; Array.Fill(restLengths, L / n);
            var opts = new DynamicRelaxation.Options
            {
                Stiffness = 1e6 * n, Bending = ei, Laths = new[] { Enumerable.Range(0, n + 1).ToArray() }, // EA = 1e6
                RestLengths = restLengths, Tolerance = 1e-9, MaxIterations = 400000
            };
            var r = DynamicRelaxation.Compute(new MeshData(verts, faces), fixedV, opts);
            double rise = r.Vertices.Max(p => p.Z);
            double arc = 0;
            for (int i = 0; i < n; i++) arc += (r.Vertices[i + 1] - r.Vertices[i]).Length;
            double err = Math.Abs(rise - riseExact) / riseExact;
            errs.Add(err);
            _out.WriteLine($"n = {n}: rise {rise:F6} ({err * 100:F3} %), arc {arc:F6}, {r.Iterations} steps, {r.Resets} resets, residual {r.Residual:E2}");
            Assert.True(r.Converged, $"n = {n} converges");
            Assert.True(Math.Abs(arc - L) < 1e-4, $"inextensible to 1e-4: {arc}");
        }
        Assert.True(errs[2] < 2e-3, $"0.2 % at n = 64: {errs[2]:E2}");
        Assert.True(errs[2] < 0.4 * errs[1], $"second order: {string.Join(", ", errs.Select(e => e.ToString("E2")))}");
    }
}
