using System.Diagnostics;
using Mite.Core;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Dynamics;
using Mite.Core.Fabrication;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;

namespace Mite.Tests;

/// <summary>
/// Progress reporting and cancellation of the long solvers (the Message line
/// of the Grasshopper components). What is checked, for every solver with a
/// Progress option: the reports are in [0, 1], never go backwards, there are
/// several of them (a percentage that jumps from 0 to 100 is a spinner), the
/// last one is exactly 1 when the solve finishes, and a cancellation stops
/// the solve at its next poll with no "finished" report. For the tracers the
/// fraction is the covered area (Σ length × spacing / area), so the unclamped
/// estimate at the end is checked against 1 on surfaces that are saturated.
/// </summary>
public class ProgressTests
{
    private sealed class Recorder
    {
        public readonly List<(double f, string stage)> Reports = new();
        public ProgressHandler Handler => (f, s) => Reports.Add((f, s));
        public double Last => Reports.Count == 0 ? double.NaN : Reports[^1].f;
        public int Count => Reports.Count;

        public void AssertWellFormed(string what, int atLeast = 3)
        {
            Assert.True(Reports.Count >= atLeast, $"{what}: only {Reports.Count} progress reports");
            for (int i = 0; i < Reports.Count; i++)
            {
                Assert.InRange(Reports[i].f, 0, 1);
                if (i > 0) Assert.True(Reports[i].f > Reports[i - 1].f, $"{what}: report {i} went from {Reports[i - 1].f} to {Reports[i].f}");
            }
            Assert.Equal(1.0, Last);
            // not a jump: something was reported below a half before the end
            Assert.True(Reports[0].f < 0.5, $"{what}: the first report is already {Reports[0].f}");
        }
    }

    // ---- the scope itself ---------------------------------------------------

    [Fact]
    public void Scope_ClampsMapsIntoItsWindowAndNeverGoesBack()
    {
        var rec = new Recorder();
        var s = new ProgressScope(rec.Handler, "tracing", 0.2, 0.6);
        Assert.True(s.Active);
        Assert.Equal(0.2, s.Report(0), 12);
        Assert.Equal(-1, s.Report(0));            // no advance → nothing sent
        Assert.Equal(-1, s.Report(-0.5));
        Assert.Equal(0.4, s.Report(0.5), 12);
        Assert.Equal(-1, s.Report(0.3));          // backwards → dropped
        Assert.Equal(-1, s.Report(double.NaN));
        Assert.Equal(0.6, s.Report(7), 12);       // clamped to 1
        s.Done();                                 // already there
        Assert.Equal(new[] { 0.2, 0.4, 0.6 }, rec.Reports.Select(r => r.f).Select(v => Math.Round(v, 12)));
        Assert.All(rec.Reports, r => Assert.Equal("tracing", r.stage));

        // a nested scope covers part of the window and reports in the parent's units
        var rec2 = new Recorder();
        var parent = new ProgressScope(rec2.Handler, "all", 0, 1);
        var a = parent.Sub("A", 0, 0.5);
        var b = parent.Sub("B", 0.5, 1);
        a.Report(0.5); a.Done(); b.Report(0.5); b.Done();
        Assert.Equal(new[] { 0.25, 0.5, 0.75, 1.0 }, rec2.Reports.Select(r => r.f));
        Assert.Equal(new[] { "A", "A", "B", "B" }, rec2.Reports.Select(r => r.stage));

        // no handler: nothing happens and nothing is counted
        var none = ProgressScope.None;
        Assert.False(none.Active);
        Assert.Equal(-1, none.Report(0.5));
        Assert.Equal(-1, none.Last);
    }

    [Fact]
    public void Scope_Converging_IsOneAtTheToleranceAndMonotoneInBothArguments()
    {
        // residual falling by a constant factor: log-linear progress
        double r0 = 1, tol = 1e-6;
        double prev = -1;
        for (int it = 0; it <= 12; it++)
        {
            double r = r0 * Math.Pow(10, -it * 0.5);
            double f = ProgressScope.Converging(r0, r, tol, it, 1000);
            Assert.True(f >= prev, $"iteration {it}: {f} < {prev}");
            Assert.InRange(f, 0, 1);
            prev = f;
        }
        Assert.Equal(0.5, ProgressScope.Converging(1, 1e-3, 1e-6, 0, 1000), 12);
        Assert.Equal(1.0, ProgressScope.Converging(1, 1e-6, 1e-6, 0, 1000), 12);
        Assert.Equal(1.0, ProgressScope.Converging(1, 1e-9, 1e-6, 0, 1000), 12);   // past the tolerance: still 1
        // the iteration cap wins when the residual stalls
        Assert.Equal(0.75, ProgressScope.Converging(1, 0.9, 1e-6, 75, 100), 12);
        // degenerate inputs are 0 .. 1, never NaN
        Assert.Equal(0.0, ProgressScope.Converging(0, 0, 0, 0, 0));
        Assert.Equal(0.0, ProgressScope.Converging(double.PositiveInfinity, 1, 1e-6, 0, 10));
        Assert.Equal(0.0, ProgressScope.Converging(1e-9, 1e-9, 1e-6, 0, 10));     // already converged at the start: iteration fraction only
        Assert.Equal("tracing A 42 %", ProgressScope.Text(0.4299, "tracing A"));
        Assert.Equal("99 %", ProgressScope.Text(0.9999, null));
        Assert.Equal("100 %", ProgressScope.Text(1, ""));
    }

    // ---- tracers: progress by coverage ---------------------------------------

    [Fact]
    public void MaskedArea_IsTheAreaOfTheMaskedFaces()
    {
        var grid = TestMeshes.CreateQuadGrid(4, 4, 2.0);        // 4 × 4 cells of 2 × 2
        Assert.Equal(64.0, EvenlySpacedNet.MaskedArea(grid, null), 9);
        var mask = new bool[grid.VertexCount];
        for (int i = 0; i < mask.Length; i++) mask[i] = grid.Vertices[i].X < 4.0 + 1e-9;   // the left half plus the middle column
        // 8 faces fully inside (x ≤ 4) and the 4 faces of the column x = 4 .. 6 cut by the mask count half
        Assert.Equal(8 * 4.0 + 4 * 4.0 * 0.5, EvenlySpacedNet.MaskedArea(grid, mask), 9);
        var sphere = TestMeshes.CreateUnitSphere(24);
        Assert.InRange(EvenlySpacedNet.MaskedArea(sphere, null), 4 * Math.PI * 0.95, 4 * Math.PI);
    }

    [Fact]
    public void AsymptoticFill_OnTheSaddle_ReportsCoverageUpToOne()
    {
        var saddle = TestMeshes.CreateSaddle(30, 2.0);
        var field = AsymptoticCurves.ComputeDirections(PrincipalCurvature.Compute(saddle));
        var rec = new Recorder();
        var opts = new EvenlySpacedNet.Options { Spacing = 0.25, MaxCurves = 400, Progress = rec.Handler };
        var curves = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts, field.Family2);
        rec.AssertWellFormed("asymptotic fill", atLeast: 10);
        Assert.False(opts.Cancelled);
        Assert.All(rec.Reports, r => Assert.Equal("tracing", r.stage));
        // the coverage estimate at the end is the raw Σ L · S / A: a saturated
        // family sweeps about the whole area (curves one spacing apart)
        Assert.InRange(opts.CoverageEstimate, 0.75, 1.35);
        // and the reports are the running estimate, clamped
        Assert.True(curves.Count >= 10);
    }

    [Fact]
    public void GeodesicFill_OnTheSphere_ReportsCoverageUpToOne()
    {
        var sphere = TestMeshes.CreateUnitSphere(24);
        int equatorVertex = 1 + 11 * 48;
        var rec = new Recorder();
        var opts = new EvenlySpacedNet.Options { Spacing = 0.3, MaxCurves = 400, Progress = rec.Handler };
        var curves = EvenlySpacedNet.TraceGeodesics(sphere, equatorVertex, new Vec3d(0, 0, 1), opts);
        rec.AssertWellFormed("geodesic fill", atLeast: 8);
        Assert.InRange(opts.CoverageEstimate, 0.7, 1.35);
        Assert.True(curves.Count >= 8);
    }

    [Fact]
    public void Webs_BorderAndCross_ReportCoverageUpToOne()
    {
        var cat = TestMeshes.CreateCatenoid(1.0, 2.0, 64, 32);
        var field = AsymptoticCurves.ComputeDirections(PrincipalCurvature.Compute(cat));
        foreach (var layout in new[] { NetLayout.WebBorder, NetLayout.WebCross })
        {
            var rec = new Recorder();
            var opts = new EvenlySpacedNet.Options { Spacing = 0.3, MaxCurves = 400, Layout = layout, Progress = rec.Handler };
            var curves = EvenlySpacedNet.TraceField(cat, field.Family1, field.Exists, -1, opts, field.Family2);
            rec.AssertWellFormed($"asymptotic {layout}", atLeast: 6);
            Assert.True(curves.Count >= 6, $"{layout}: {curves.Count} curves");
            // a web's curves run rim to rim one spacing apart along the seed line, so the
            // swept strips also add up to about the area
            Assert.InRange(opts.CoverageEstimate, 0.6, 1.4);
        }
        // the geodesic web on a cylinder
        var cyl = TestMeshes.CreateCylinder(1.0, 2.0, 64, 32);
        var recG = new Recorder();
        var optsG = new EvenlySpacedNet.Options { Spacing = 0.3, MaxCurves = 400, Layout = NetLayout.WebBorder, Progress = recG.Handler };
        var helices = EvenlySpacedNet.TraceGeodesics(cyl, 0, new Vec3d(0.7, 0, 0.7), optsG);
        recG.AssertWellFormed("geodesic web", atLeast: 6);
        Assert.True(helices.Count >= 6);
        Assert.InRange(optsG.CoverageEstimate, 0.6, 1.4);
    }

    [Fact]
    public void ConjugateNet_SplitsTheProgressBetweenTheFamilies()
    {
        var torus = TestMeshes.CreateTorus(3, 1, 48, 24);
        var rec = new Recorder();
        var handler = rec.Handler;
        var opts = new EvenlySpacedNet.Options { Spacing = 0.4, MaxCurves = 300, Progress = handler };
        var net = ConjugateNet.Trace(torus, -1, opts);
        rec.AssertWellFormed("conjugate net", atLeast: 10);
        Assert.Contains(rec.Reports, r => r.stage == "tracing A" && r.f <= 0.5);
        Assert.Contains(rec.Reports, r => r.stage == "tracing B" && r.f > 0.5);
        Assert.Equal(0.5, rec.Reports.Where(r => r.stage == "tracing A").Max(r => r.f), 12);
        Assert.True(net.FamilyA.Count > 0 && net.FamilyB.Count > 0);
        Assert.Same(handler, opts.Progress); // the caller's handler is put back
    }

    [Fact]
    public void SeededTracers_ReportSeedsDoneOverSeedsGiven()
    {
        var saddle = TestMeshes.CreateSaddle(20, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var seeds = Enumerable.Range(0, 8).Select(i => 21 * (3 + 2 * i) + 10).ToArray();

        var recA = new Recorder();
        AsymptoticCurves.Trace(saddle, seeds, pc, false, new AsymptoticCurves.Options { Progress = recA.Handler });
        recA.AssertWellFormed("asymptotic seeds", atLeast: 8);
        Assert.Equal(seeds.Length + 1, recA.Count);   // one per seed, then 1

        var recG = new Recorder();
        GeodesicCurves.Trace(saddle, seeds, new[] { new Vec3d(1, 0, 0) }, new GeodesicCurves.Options { Progress = recG.Handler });
        recG.AssertWellFormed("geodesic seeds", atLeast: 8);

        var recS = new Recorder();
        Mite.Core.Streamlines.CurvatureStreamlines.Trace(saddle, seeds, pc, new Mite.Core.Streamlines.CurvatureStreamlines.Options { Progress = recS.Handler });
        recS.AssertWellFormed("streamline seeds", atLeast: 8);
    }

    // ---- iterative solvers: progress by residual decay ----------------------

    [Fact]
    public void AsymptoticWebAndAag_ReportWebThenOptimisation()
    {
        var cat = TestMeshes.CreateCatenoid(1.0, 2.0, 64, 32);
        var pc = PrincipalCurvature.Compute(cat);
        var rec = new Recorder();
        var web = AsymptoticWeb.Build(cat, pc, -1, new AsymptoticWeb.Options { Spacing = 0.35, Progress = rec.Handler });
        Assert.True(web.Nodes.Count > 0);
        // a surface of revolution is one traced curve and its rotated copies: finished at once
        Assert.True(web.RotationalWeb);
        Assert.Equal(1.0, rec.Last);
        Assert.All(rec.Reports, r => Assert.Equal("web", r.stage));

        // a saddle is not a surface of revolution: rays are traced one by one
        var saddle = TestMeshes.CreateSaddle(30, 2.0);
        var recS = new Recorder();
        var webS = AsymptoticWeb.Build(saddle, PrincipalCurvature.Compute(saddle), -1, new AsymptoticWeb.Options { Spacing = 0.25, Progress = recS.Handler });
        Assert.True(webS.Nodes.Count > 0);
        recS.AssertWellFormed("asymptotic web, saddle", atLeast: 4);

        var recO = new Recorder();
        var r = AagWeb.Optimize(cat, web, new AagWeb.Options { Iterations = 40, Progress = recO.Handler });
        Assert.True(r.Iterations > 0);
        recO.AssertWellFormed("AAG", atLeast: 4);
        Assert.All(recO.Reports, x => Assert.Equal("optimising", x.stage));
    }

    [Fact]
    public void ScissorNet_ReportsStatesAndIterations()
    {
        const int N = 12, Levels = 2; const double Rho = 1.0;
        double beta0 = 30 * Math.PI / 180, beta1 = 65 * Math.PI / 180;
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta0);
        var net = new ScissorNet(nodes, laths, N, normals);
        int rows = 2 * Levels + 1;
        int top0 = 2 * Levels, topOpp = (N / 2) * rows + 2 * Levels, waist0 = Levels;
        double TopDiameter(double beta)
        {
            var (nn, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, beta);
            return (nn[top0] - nn[topOpp]).Length;
        }
        var rec = new Recorder();
        var opt = new ScissorNet.Options
        {
            Fixed = new[] { waist0 }, Cables = new[] { (top0, topOpp) },
            CableLengthsAt = t => new[] { TopDiameter(beta0 + t * (beta1 - beta0)) },
            Steps = 6, Tolerance = 1e-11, Progress = rec.Handler,
        };
        var res = net.Solve(opt);
        Assert.Equal(7, res.States.Count);
        Assert.False(res.Cancelled);
        rec.AssertWellFormed("scissor net", atLeast: 7);
        Assert.All(rec.Reports, x => Assert.Equal("folding", x.stage));
        // the k-th state's reports lie in its own seventh of the range
        foreach (var (f, _) in rec.Reports.Take(rec.Count - 1))
            Assert.True(f < 1);
    }

    [Fact]
    public void RelaxationSolvers_ReportTheResidualDecayUpToOne()
    {
        // dynamic relaxation: a hanging net that converges before the cap
        var grid = TestMeshes.CreateQuadGrid(16, 16, 1.0);
        var recD = new Recorder();
        var r = DynamicRelaxation.Compute(grid, grid.BuildBoundaryVertexFlags(),
            new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000, Progress = recD.Handler });
        Assert.True(r.Converged);
        Assert.True(r.Iterations < 5000, "converged before the cap, so the progress must come from the residual, not the iteration count");
        recD.AssertWellFormed("dynamic relaxation", atLeast: 5);
        Assert.All(recD.Reports, x => Assert.Equal("relaxing", x.stage));

        // minimal surface: 10 × 10 perturbed grid, boundary pinned
        var tri = TestMeshes.CreateQuadGrid(10, 10).ToTriangulated();
        var verts = (Vec3d[])tri.Vertices.Clone();
        var rng = new Random(42);
        for (int i = 0; i < verts.Length; i++) verts[i] = new Vec3d(verts[i].X, verts[i].Y, rng.NextDouble() * 0.3);
        var perturbed = new MeshData(verts, tri.Faces);
        var fixedVerts = new bool[perturbed.VertexCount];
        for (int i = 0; i < fixedVerts.Length; i++) { int x = i % 11, y = i / 11; fixedVerts[i] = x == 0 || x == 10 || y == 0 || y == 10; }
        var recM = new Recorder();
        var m = MinimalSurface.Compute(perturbed, fixedVerts, new MinimalSurface.Options { MaxIterations = 50, Progress = recM.Handler });
        Assert.True(m.Iterations > 1);
        recM.AssertWellFormed("minimal surface", atLeast: 3);

        // planarization: converges to the tolerance before the cap
        var q = TestMeshes.CreateQuadGrid(5, 5);
        var qv = (Vec3d[])q.Vertices.Clone();
        for (int i = 0; i < qv.Length; i++) qv[i] = new Vec3d(qv[i].X, qv[i].Y, rng.NextDouble() * 0.5);
        var recP = new Recorder();
        var optsP = new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-6, Progress = recP.Handler };
        var p = Planarization.Compute(new MeshData(qv, q.Faces), new bool[qv.Length], optsP);
        Assert.True(p.Iterations < 2000, $"planarization hit the cap after {p.Iterations}");
        recP.AssertWellFormed("planarization", atLeast: 5);
        Assert.All(recP.Reports, x => Assert.Equal("planarizing", x.stage));
    }

    [Fact]
    public void FrameAnalysisAndGeodesicPaths_ReportTheirStages()
    {
        var mesh = TestMeshes.CreateQuadGrid(2, 2, 10.0);
        var a = new Vec3d[21];
        for (int i = 0; i <= 20; i++) a[i] = new Vec3d(i * 0.5, 5, 0);
        var b = new Vec3d[11];
        for (int i = 0; i <= 10; i++) b[i] = new Vec3d(5.25, i * 0.5, 0);
        var rec = new Recorder();
        FrameAnalysis.Compute(mesh, new List<Vec3d[]> { a, b }, null, new List<Vec3d> { a[0], a[^1], b[0] },
            new LathProfile(0.1, 0.05, false), new Vec3d(0, 0, -100), new FrameAnalysis.Options { Progress = rec.Handler });
        rec.AssertWellFormed("frame analysis", atLeast: 4);
        Assert.Equal(new[] { "joints", "assembling", "solving", "results", "results" }, rec.Reports.Select(x => x.stage));

        var sphere = TestMeshes.CreateUnitSphere(16);
        var from = new List<Vec3d>(); var to = new List<Vec3d>();
        var rng = new Random(5);
        Vec3d Rnd() { var v = new Vec3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5); return v.Normalized(); }
        for (int i = 0; i < 6; i++) { from.Add(Rnd()); to.Add(Rnd()); }
        var recF = new Recorder();
        var opts = new FlipGeodesic.Options { Progress = recF.Handler };
        var res = FlipGeodesic.Compute(sphere, from, to, out _, opts)!;
        Assert.Equal(6, res.Length);
        Assert.All(res, x => Assert.NotNull(x));
        recF.AssertWellFormed("exact geodesics", atLeast: 6);
        Assert.All(recF.Reports, x => Assert.Equal("geodesics", x.stage));
        Assert.False(opts.Cancelled);
    }

    // ---- cancellation -------------------------------------------------------

    /// <summary>A cancel probe that fires once the solver has reported progress at least <paramref name="after"/> times.</summary>
    private static (Func<bool> cancel, Recorder rec) CancelAfter(int after)
    {
        var rec = new Recorder();
        return (() => rec.Count >= after, rec);
    }

    private static void AssertCancelled(Recorder rec, string what)
    {
        Assert.True(rec.Count > 0, $"{what}: cancelled before any report");
        Assert.True(rec.Last < 1, $"{what}: a cancelled solve reported 'finished' ({rec.Last})");
    }

    [Fact]
    public void Cancellation_StopsEverySolverAtItsNextPoll()
    {
        var sw = Stopwatch.StartNew();

        // tracers
        var saddle = TestMeshes.CreateSaddle(30, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(pc);
        {
            var (cancel, rec) = CancelAfter(3);
            var opts = new EvenlySpacedNet.Options { Spacing = 0.2, MaxCurves = 400, ShouldCancel = cancel, Progress = rec.Handler };
            var curves = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts, field.Family2);
            Assert.True(opts.Cancelled);
            Assert.InRange(curves.Count, 1, 6);           // the curves accepted before the poll
            AssertCancelled(rec, "asymptotic fill");
        }
        foreach (var layout in new[] { NetLayout.WebBorder, NetLayout.WebCross })
        {
            var (cancel, rec) = CancelAfter(2);
            var opts = new EvenlySpacedNet.Options { Spacing = 0.2, MaxCurves = 400, Layout = layout, ShouldCancel = cancel, Progress = rec.Handler };
            var curves = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts, field.Family2);
            Assert.True(opts.Cancelled, layout.ToString());
            Assert.InRange(curves.Count, 1, 4);
            AssertCancelled(rec, $"asymptotic {layout}");
        }
        {
            var sphere = TestMeshes.CreateUnitSphere(24);
            var (cancel, rec) = CancelAfter(3);
            var opts = new EvenlySpacedNet.Options { Spacing = 0.2, MaxCurves = 400, ShouldCancel = cancel, Progress = rec.Handler };
            var curves = EvenlySpacedNet.TraceGeodesics(sphere, 1 + 11 * 48, new Vec3d(0, 0, 1), opts);
            Assert.True(opts.Cancelled);
            Assert.InRange(curves.Count, 1, 6);
            AssertCancelled(rec, "geodesic fill");
        }
        {
            var seeds = Enumerable.Range(0, 8).Select(i => 31 * (3 + 3 * i) + 15).ToArray();
            var (cancel, rec) = CancelAfter(3);
            var curves = AsymptoticCurves.Trace(saddle, seeds, pc, false, new AsymptoticCurves.Options { ShouldCancel = cancel, Progress = rec.Handler });
            Assert.InRange(curves.Count, 0, 3);
            AssertCancelled(rec, "asymptotic seeds");
            var (cancelG, recG) = CancelAfter(3);
            GeodesicCurves.Trace(saddle, seeds, new[] { new Vec3d(1, 0, 0) }, new GeodesicCurves.Options { ShouldCancel = cancelG, Progress = recG.Handler });
            AssertCancelled(recG, "geodesic seeds");
            var (cancelS, recS) = CancelAfter(3);
            Mite.Core.Streamlines.CurvatureStreamlines.Trace(saddle, seeds, pc, new Mite.Core.Streamlines.CurvatureStreamlines.Options { ShouldCancel = cancelS, Progress = recS.Handler });
            AssertCancelled(recS, "streamline seeds");
        }
        {
            // a skew bilinear patch has no symmetry, so all four rays are traced one by one (the
            // saddle z = x² − y² is 2-fold symmetric: two rays and their copies, finished before a third poll)
            var patch = TestMeshes.CreateBilinearPatch(new Vec3d(0, 0, 0), new Vec3d(2, 0.3, 0.4), new Vec3d(0.2, 2, -0.3), new Vec3d(2.1, 1.9, 1.0), 40, 40);
            var (cancel, rec) = CancelAfter(2);
            var web = AsymptoticWeb.Build(patch, PrincipalCurvature.Compute(patch), -1, new AsymptoticWeb.Options { Spacing = 0.2, ShouldCancel = cancel, Progress = rec.Handler });
            AssertCancelled(rec, "asymptotic web");
            Assert.True(web.A.Count + web.B.Count < 20, $"{web.A.Count + web.B.Count} laths after the cancel");
        }
        {
            var cat = TestMeshes.CreateCatenoid(1.0, 2.0, 48, 24);
            var web = AsymptoticWeb.Build(cat, PrincipalCurvature.Compute(cat), -1, new AsymptoticWeb.Options { Spacing = 0.4 });
            var (cancel, rec) = CancelAfter(3);
            var r = AagWeb.Optimize(cat, web, new AagWeb.Options { Iterations = 60, ShouldCancel = cancel, Progress = rec.Handler });
            Assert.InRange(r.Iterations, 1, 3);
            AssertCancelled(rec, "AAG");
        }
        {
            const int N = 12, Levels = 2;
            var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, 1.0, 30 * Math.PI / 180);
            var net = new ScissorNet(nodes, laths, N, normals);
            int rows = 2 * Levels + 1, top0 = 2 * Levels, topOpp = (N / 2) * rows + 2 * Levels;
            double d0 = (nodes[top0] - nodes[topOpp]).Length;
            var (cancel, rec) = CancelAfter(4);
            var res = net.Solve(new ScissorNet.Options
            {
                Fixed = new[] { Levels }, Cables = new[] { (top0, topOpp) }, CableLengthsAt = t => new[] { d0 * (1 - 0.3 * t) },
                Steps = 8, Cancel = cancel, Progress = rec.Handler,
            });
            Assert.True(res.Cancelled);
            Assert.InRange(res.States.Count, 1, 3);
            AssertCancelled(rec, "scissor net");
        }
        {
            var grid = TestMeshes.CreateQuadGrid(16, 16, 1.0);
            var (cancel, rec) = CancelAfter(3);
            var r = DynamicRelaxation.Compute(grid, grid.BuildBoundaryVertexFlags(),
                new DynamicRelaxation.Options { Stiffness = 10, Gravity = new Vec3d(0, 0, -1), MaxIterations = 5000, ShouldCancel = cancel, Progress = rec.Handler });
            Assert.False(r.Converged);
            Assert.InRange(r.Iterations, 1, 48);
            AssertCancelled(rec, "dynamic relaxation");
        }
        {
            var q = TestMeshes.CreateQuadGrid(5, 5);
            var qv = (Vec3d[])q.Vertices.Clone();
            var rng = new Random(1);
            for (int i = 0; i < qv.Length; i++) qv[i] = new Vec3d(qv[i].X, qv[i].Y, rng.NextDouble() * 0.5);
            var (cancel, rec) = CancelAfter(3);
            var opts = new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-9, ShouldCancel = cancel, Progress = rec.Handler };
            var p = Planarization.Compute(new MeshData(qv, q.Faces), new bool[qv.Length], opts);
            Assert.True(opts.Cancelled);
            Assert.InRange(p.Iterations, 1, 4);
            AssertCancelled(rec, "planarization");
        }
        {
            var sphere = TestMeshes.CreateUnitSphere(16);
            var from = new List<Vec3d>(); var to = new List<Vec3d>();
            var rng = new Random(5);
            Vec3d Rnd() { var v = new Vec3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5); return v.Normalized(); }
            for (int i = 0; i < 8; i++) { from.Add(Rnd()); to.Add(Rnd()); }
            var rec = new Recorder();
            Func<bool> cancel = () => rec.Count > 0 && rec.Last >= 0.3;   // once the pairs are under way (the preparation is the first fifth)
            var opts = new FlipGeodesic.Options { ShouldCancel = cancel, Progress = rec.Handler };
            var res = FlipGeodesic.Compute(sphere, from, to, out _, opts)!;
            Assert.True(opts.Cancelled);
            Assert.Equal(8, res.Length);
            Assert.InRange(res.Count(x => x != null), 1, 3);
            AssertCancelled(rec, "exact geodesics");
        }
        Assert.True(sw.Elapsed.TotalSeconds < 60, $"cancelled solves took {sw.Elapsed.TotalSeconds:0.0} s in all");
    }
}
