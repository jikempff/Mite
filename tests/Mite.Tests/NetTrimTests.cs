using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Cookie-cutting nets to a boundary (NetTrim), checked against closed forms:
/// the asymptotic lines x ∓ y = c of the saddle z = x² − y² are straight, so
/// their pieces inside a circle, an annulus or a non-convex loop have exact
/// end points and lengths; the rulings of the hyperboloid between two
/// parallels have length 2h√2.
/// </summary>
public class NetTrimTests
{
    private const double Tol = 2e-3;   // the boundary lies on the facets (sagitta e²κ/8 ≈ 2.4e-4 at edge 0.031)

    private static MeshData Saddle() => TestMeshes.CreateSaddle(96, 3.0);

    /// <summary>Asymptotic line x − y = c of z = x² − y², sampled exactly on the surface inside the square.</summary>
    private static Vec3d[] SaddleLineA(double c, double half = 1.5, int n = 240)
    {
        double x0 = Math.Max(-half, c - half), x1 = Math.Min(half, c + half);
        var pts = new Vec3d[n];
        for (int i = 0; i < n; i++)
        {
            double x = x0 + (x1 - x0) * i / (n - 1), y = x - c;
            pts[i] = new Vec3d(x, y, x * x - y * y);
        }
        return pts;
    }

    /// <summary>Asymptotic line x + y = c.</summary>
    private static Vec3d[] SaddleLineB(double c, double half = 1.5, int n = 240)
    {
        double x0 = Math.Max(-half, c - half), x1 = Math.Min(half, c + half);
        var pts = new Vec3d[n];
        for (int i = 0; i < n; i++)
        {
            double x = x0 + (x1 - x0) * i / (n - 1), y = c - x;
            pts[i] = new Vec3d(x, y, x * x - y * y);
        }
        return pts;
    }

    private static Vec3d OnSaddle(double x, double y) => new(x, y, x * x - y * y);

    private static Vec3d[] SaddleCircle(double r, int n = 360, double z = double.NaN)
    {
        var pts = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double th = 2 * Math.PI * i / n, x = r * Math.Cos(th), y = r * Math.Sin(th);
            pts[i] = double.IsNaN(z) ? OnSaddle(x, y) : new Vec3d(x, y, z);
        }
        return pts;
    }

    /// <summary>
    /// Exact pieces of a parametrised curve under an inside predicate: the runs
    /// of t ∈ [t0, t1] where inside holds, their ends found by bisection.
    /// </summary>
    private static List<(double t0, double t1)> ExactPieces(Func<double, Vec3d> curve, double t0, double t1, Func<Vec3d, bool> inside, int scan = 20000)
    {
        var runs = new List<(double, double)>();
        double Root(double a, double b)
        {
            bool fa = inside(curve(a));
            for (int i = 0; i < 60; i++)
            {
                double m = 0.5 * (a + b);
                if (inside(curve(m)) == fa) a = m; else b = m;
            }
            return 0.5 * (a + b);
        }
        double? start = null;
        bool prev = inside(curve(t0));
        if (prev) start = t0;
        for (int i = 1; i <= scan; i++)
        {
            double t = t0 + (t1 - t0) * i / scan;
            bool now = inside(curve(t));
            if (now && !prev) start = Root(t0 + (t1 - t0) * (i - 1) / scan, t);
            if (!now && prev) { runs.Add((start!.Value, Root(t0 + (t1 - t0) * (i - 1) / scan, t))); start = null; }
            prev = now;
        }
        if (start != null) runs.Add((start.Value, t1));
        return runs;
    }

    private static double Length(Vec3d[] l) { double s = 0; for (int i = 1; i < l.Length; i++) s += (l[i] - l[i - 1]).Length; return s; }

    [Fact]
    public void Saddle_CircleBoundary_CutsAsymptoticLinesAtExactPoints()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        var cs = new[] { -1.4, -1.2, -1.0, -0.8, -0.6, -0.4, -0.2, 0.0, 0.2, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4 };
        var famA = cs.Select(c => SaddleLineA(c)).ToList();
        var famB = cs.Select(c => SaddleLineB(c)).ToList();
        var res = NetTrim.Trim(proj, new[] { famA, famB }, new[] { SaddleCircle(1.0) });

        Assert.Equal(2, res.Regions);
        Assert.Equal(1, res.InsideRegions);
        Assert.Equal(0, res.BoundaryOffMesh);
        // |c| < √2 → exactly one piece per line, ends at x = c/2 ± ½√(2 − c²), length √(2 − c²)·√(2 + 4c²)
        int expected = cs.Count(c => Math.Abs(c) < Math.Sqrt(2));
        Assert.Equal(expected, res.Families[0].Count);
        Assert.Equal(expected, res.Families[1].Count);
        Assert.Equal(cs.Length - expected, res.RemovedWhole / 2);
        double worstEnd = 0, worstLen = 0;
        for (int f = 0; f < 2; f++)
            for (int i = 0; i < res.Families[f].Count; i++)
            {
                double c = cs[res.Source[f][i]];
                var piece = res.Families[f][i];
                double h = 0.5 * Math.Sqrt(2 - c * c);
                double xa = c / 2 - h, xb = c / 2 + h;
                Vec3d ea = f == 0 ? OnSaddle(xa, xa - c) : OnSaddle(xa, c - xa);
                Vec3d eb = f == 0 ? OnSaddle(xb, xb - c) : OnSaddle(xb, c - xb);
                double e = Math.Min((piece[0] - ea).Length + (piece[^1] - eb).Length, (piece[0] - eb).Length + (piece[^1] - ea).Length);
                worstEnd = Math.Max(worstEnd, e);
                worstLen = Math.Max(worstLen, Math.Abs(Length(piece) - Math.Sqrt(2 - c * c) * Math.Sqrt(2 + 4 * c * c)));
                foreach (var p in piece) Assert.True(p.X * p.X + p.Y * p.Y <= 1 + Tol, $"point outside the circle: {p}");
            }
        Assert.True(worstEnd < Tol, $"worst end error {worstEnd:E2}");
        Assert.True(worstLen < 2 * Tol, $"worst length error {worstLen:E2}");
        Assert.Equal(2 * 2 * expected, res.Cuts.Count);
        foreach (var q in res.Cuts) Assert.True(Math.Abs(Math.Sqrt(q.X * q.X + q.Y * q.Y) - 1) < Tol);
        Assert.Contains("1 inside", res.Report);
    }

    [Fact]
    public void Saddle_PlanCircleProjectedDown_SameCutAsOnSurface()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        var cs = new[] { -1.0, -0.5, 0.0, 0.5, 1.0 };
        var famA = cs.Select(c => SaddleLineA(c)).ToList();
        var onSurface = NetTrim.Trim(proj, new[] { famA }, new[] { SaddleCircle(1.0) });
        var fromPlan = NetTrim.Trim(proj, new[] { famA }, new[] { SaddleCircle(1.0, 360, 5.0) },
            new NetTrim.Options { Direction = new Vec3d(0, 0, -1) });
        Assert.Equal(0, fromPlan.BoundaryOffMesh);
        Assert.Equal(onSurface.Families[0].Count, fromPlan.Families[0].Count);
        double worst = 0;
        for (int i = 0; i < famA.Count; i++)
        {
            worst = Math.Max(worst, (onSurface.Families[0][i][0] - fromPlan.Families[0][i][0]).Length);
            worst = Math.Max(worst, (onSurface.Families[0][i][^1] - fromPlan.Families[0][i][^1]).Length);
        }
        Assert.True(worst < Tol, $"plan projection differs from the surface loop by {worst:E2}");
        // the pulled loop is on the surface, not at z = 5
        foreach (var p in fromPlan.Boundary[0]) Assert.True(Math.Abs(p.Z - (p.X * p.X - p.Y * p.Y)) < 1e-3);
    }

    [Fact]
    public void Saddle_NonConvexLoop_PiecesMatchExactRootFinding()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        // peanut r(θ) = 1 + 0.45 cos 2θ (concave at the waist on the y axis)
        bool Inside(Vec3d p) { double th = Math.Atan2(p.Y, p.X); return Math.Sqrt(p.X * p.X + p.Y * p.Y) < 1 + 0.45 * Math.Cos(2 * th); }
        var loop = new Vec3d[721];
        for (int i = 0; i <= 720; i++) { double th = 2 * Math.PI * i / 720, r = 1 + 0.45 * Math.Cos(2 * th); loop[i] = OnSaddle(r * Math.Cos(th), r * Math.Sin(th)); }

        // curves y = y0 across the saddle (parabolas on the surface)
        var ys = new[] { -0.9, -0.7, -0.56, -0.3, 0.0, 0.3, 0.56, 0.7, 0.9 };
        var fam = ys.Select(y0 => Enumerable.Range(0, 301).Select(i => OnSaddle(-1.5 + 3.0 * i / 300, y0)).ToArray()).ToList();
        var res = NetTrim.Trim(proj, new[] { fam }, new[] { loop });

        int totalExact = 0;
        double worst = 0;
        for (int k = 0; k < ys.Length; k++)
        {
            double y0 = ys[k];
            var exact = ExactPieces(x => OnSaddle(x, y0), -1.5, 1.5, Inside);
            var pieces = Enumerable.Range(0, res.Families[0].Count).Where(i => res.Source[0][i] == k).Select(i => res.Families[0][i]).ToList();
            Assert.True(exact.Count == pieces.Count, $"y = {y0}: {pieces.Count} pieces, exactly {exact.Count}");
            totalExact += exact.Count;
            for (int i = 0; i < exact.Count; i++)
            {
                worst = Math.Max(worst, (pieces[i][0] - OnSaddle(exact[i].t0, y0)).Length);
                worst = Math.Max(worst, (pieces[i][^1] - OnSaddle(exact[i].t1, y0)).Length);
            }
        }
        Assert.True(totalExact >= ys.Length + 2, "the waist should split at least two of the lines in two");
        Assert.True(worst < Tol, $"worst end error {worst:E2}");
    }

    [Fact]
    public void Hyperboloid_TwoParallels_KeepTheBandBetweenThem()
    {
        var mesh = TestMeshes.CreateHyperboloid(1, 1, 1, 96, 48);
        var proj = new MeshProjection(mesh);
        double h = 0.6;
        Vec3d[] Ring(double z) => Enumerable.Range(0, 361).Select(i => { double th = 2 * Math.PI * i / 360, r = Math.Sqrt(1 + z * z); return new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z); }).ToArray();
        Vec3d[] Ruling(double th, int sign) => Enumerable.Range(0, 201).Select(i => { double t = -1 + 2.0 * i / 200; return new Vec3d(Math.Cos(th) - sign * t * Math.Sin(th), Math.Sin(th) + sign * t * Math.Cos(th), t); }).ToArray();
        var famA = Enumerable.Range(0, 24).Select(i => Ruling(2 * Math.PI * i / 24, +1)).ToList();
        var famB = Enumerable.Range(0, 24).Select(i => Ruling(2 * Math.PI * i / 24 + 0.1, -1)).ToList();
        var res = NetTrim.Trim(proj, new[] { famA, famB }, new[] { Ring(h), Ring(-h) });

        Assert.Equal(3, res.Regions);          // two caps on the rims, the band
        Assert.Equal(1, res.InsideRegions);
        Assert.Equal(24, res.Families[0].Count);
        Assert.Equal(24, res.Families[1].Count);
        double worstZ = 0, worstLen = 0;
        foreach (var fam in res.Families)
            foreach (var piece in fam)
            {
                worstZ = Math.Max(worstZ, Math.Abs(Math.Abs(piece[0].Z) - h));
                worstZ = Math.Max(worstZ, Math.Abs(Math.Abs(piece[^1].Z) - h));
                worstLen = Math.Max(worstLen, Math.Abs(Length(piece) - 2 * h * Math.Sqrt(2)));
                foreach (var p in piece) Assert.True(Math.Abs(p.Z) <= h + Tol);
            }
        Assert.True(worstZ < Tol, $"worst end height error {worstZ:E2}");
        Assert.True(worstLen < 2 * Tol, $"worst length error {worstLen:E2} against 2h√2 = {2 * h * Math.Sqrt(2):F4}");
    }

    [Fact]
    public void Saddle_Annulus_HoleIsOutsideByDepth_InsidePointAndInvertOverride()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        double R = 1.2, r = 0.5;
        var cs = new[] { -1.4, -0.9, -0.5, 0.0, 0.5, 0.9, 1.4 };
        var famA = cs.Select(c => SaddleLineA(c)).ToList();
        var loops = new[] { SaddleCircle(R), SaddleCircle(r, 180) };

        // default: the ring is one loop from the border (inside), the hole two (outside)
        var ring = NetTrim.Trim(proj, new[] { famA }, loops);
        Assert.Equal(3, ring.Regions);
        Assert.Equal(1, ring.InsideRegions);
        double worst = 0;
        for (int k = 0; k < cs.Length; k++)
        {
            double c = cs[k];
            var pieces = Enumerable.Range(0, ring.Families[0].Count).Where(i => ring.Source[0][i] == k).Select(i => ring.Families[0][i]).ToList();
            int expected = Math.Abs(c) < r * Math.Sqrt(2) ? 2 : Math.Abs(c) < R * Math.Sqrt(2) ? 1 : 0;
            Assert.True(expected == pieces.Count, $"c = {c}: {pieces.Count} pieces, expected {expected}");
            foreach (var piece in pieces)
                foreach (var end in new[] { piece[0], piece[^1] })
                {
                    double rho = Math.Sqrt(end.X * end.X + end.Y * end.Y);
                    worst = Math.Max(worst, Math.Min(Math.Abs(rho - R), Math.Abs(rho - r)));
                }
            // the ring pieces of a line through the hole: x ∈ [c/2 − ½√(2R² − c²), c/2 − ½√(2r² − c²)] and its mirror
            if (expected == 2)
            {
                double hR = 0.5 * Math.Sqrt(2 * R * R - c * c), hr = 0.5 * Math.Sqrt(2 * r * r - c * c);
                double exactLen = (hR - hr) * Math.Sqrt(2 + 4 * c * c);
                foreach (var piece in pieces) worst = Math.Max(worst, Math.Abs(Length(piece) - exactLen));
            }
        }
        Assert.True(worst < 2 * Tol, $"worst end / length error {worst:E2}");

        // a reference point in the hole keeps only the hole
        var hole = NetTrim.Trim(proj, new[] { famA }, loops, new NetTrim.Options { Inside = new[] { new Vec3d(0, 0, 0) } });
        Assert.Equal(1, hole.InsideRegions);
        Assert.Equal(cs.Count(c => Math.Abs(c) < r * Math.Sqrt(2)), hole.Families[0].Count);
        foreach (var piece in hole.Families[0]) foreach (var p in piece) Assert.True(p.X * p.X + p.Y * p.Y <= r * r + Tol);

        // invert: the outer tails and the hole chord, never the ring
        var inv = NetTrim.Trim(proj, new[] { famA }, loops, new NetTrim.Options { Invert = true });
        Assert.Equal(2, inv.InsideRegions);
        for (int k = 0; k < cs.Length; k++)
        {
            double c = cs[k];
            int n = Enumerable.Range(0, inv.Families[0].Count).Count(i => inv.Source[0][i] == k);
            int expected = Math.Abs(c) < r * Math.Sqrt(2) ? 3 : Math.Abs(c) < R * Math.Sqrt(2) ? 2 : 1;
            Assert.True(expected == n, $"inverted c = {c}: {n} pieces, expected {expected}");
        }
        foreach (var piece in inv.Families[0])
            foreach (var p in piece.Skip(1).Take(piece.Length - 2))
            {
                double rho2 = p.X * p.X + p.Y * p.Y;
                Assert.True(rho2 <= r * r + Tol || rho2 >= R * R - Tol, $"ring point kept under Invert: {p}");
            }
    }

    [Fact]
    public void Saddle_LoopCrossingTheMeshBorder_KeepsBorderEndsAndCutsTheRest()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        double R = 1.6;    // crosses the 3 × 3 square's sides (1.5 < 1.6 < 2.12)
        var cs = new[] { -1.4, -1.0, -0.8, -0.4, 0.0, 0.4, 0.8, 1.0, 1.4 };
        var famA = cs.Select(c => SaddleLineA(c)).ToList();
        var res = NetTrim.Trim(proj, new[] { famA }, new[] { SaddleCircle(R, 720) });
        Assert.Equal(1, res.InsideRegions);
        bool Inside(Vec3d p) => p.X * p.X + p.Y * p.Y < R * R;
        double worst = 0;
        int borderEnds = 0, circleEnds = 0;
        for (int k = 0; k < cs.Length; k++)
        {
            double c = cs[k];
            double x0 = Math.Max(-1.5, c - 1.5), x1 = Math.Min(1.5, c + 1.5);
            var exact = ExactPieces(x => OnSaddle(x, x - c), x0, x1, Inside);
            var pieces = Enumerable.Range(0, res.Families[0].Count).Where(i => res.Source[0][i] == k).Select(i => res.Families[0][i]).ToList();
            Assert.True(exact.Count == pieces.Count, $"c = {c}: {pieces.Count} pieces, exactly {exact.Count}");
            for (int i = 0; i < exact.Count; i++)
            {
                worst = Math.Max(worst, (pieces[i][0] - OnSaddle(exact[i].t0, exact[i].t0 - c)).Length);
                worst = Math.Max(worst, (pieces[i][^1] - OnSaddle(exact[i].t1, exact[i].t1 - c)).Length);
                foreach (var end in new[] { pieces[i][0], pieces[i][^1] })
                    if (Math.Abs(Math.Abs(end.X) - 1.5) < 1e-9 || Math.Abs(Math.Abs(end.Y) - 1.5) < 1e-9) borderEnds++; else circleEnds++;
            }
        }
        Assert.True(worst < Tol, $"worst end error {worst:E2}");
        Assert.True(borderEnds > 0 && circleEnds > 0, $"{borderEnds} border ends, {circleEnds} circle ends");
    }

    [Fact]
    public void Saddle_ClosedCurves_SplitWithoutWrappingTheSeam()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        Vec3d[] Ellipse(double a, double b, int n = 400) => Enumerable.Range(0, n + 1).Select(i => { double ph = 2 * Math.PI * i / n; return OnSaddle(a * Math.Cos(ph), b * Math.Sin(ph)); }).ToArray();
        var fam = new List<Vec3d[]> { Ellipse(1.3, 0.6), Ellipse(0.5, 0.5), Ellipse(1.2, 1.2) };
        var res = NetTrim.Trim(proj, new[] { fam }, new[] { SaddleCircle(1.0) });

        // ellipse 1.3 × 0.6: inside the unit circle where |cos φ| < √(0.64 / 1.33) → two arcs, four cuts
        var arcs = Enumerable.Range(0, res.Families[0].Count).Where(i => res.Source[0][i] == 0).Select(i => res.Families[0][i]).ToList();
        Assert.Equal(2, arcs.Count);
        double cosCut = Math.Sqrt(0.64 / 1.33);
        double worst = 0;
        foreach (var arc in arcs)
            foreach (var end in new[] { arc[0], arc[^1] })
            {
                worst = Math.Max(worst, Math.Abs(Math.Abs(end.X / 1.3) - cosCut));
                worst = Math.Max(worst, Math.Abs(Math.Sqrt(end.X * end.X + end.Y * end.Y) - 1));
            }
        Assert.True(worst < Tol, $"worst end error {worst:E2}");
        // each arc stays on one side of the y axis (no piece wraps round the seam at φ = 0)
        foreach (var arc in arcs) Assert.True(arc.All(p => p.Y > -Tol) || arc.All(p => p.Y < Tol));
        // the circle r = 0.5 is kept whole and closed, the circle r = 1.2 removed
        var small = Enumerable.Range(0, res.Families[0].Count).Where(i => res.Source[0][i] == 1).Select(i => res.Families[0][i]).ToList();
        Assert.Single(small);
        Assert.Equal(fam[1].Length, small[0].Length);
        Assert.True((small[0][0] - small[0][^1]).Length < 1e-12, "the kept circle is still closed");
        Assert.DoesNotContain(2, res.Source[0]);
        Assert.Equal(4, res.Cuts.Count);
    }

    [Fact]
    public void Saddle_TracedClassicNet_NothingOutsideEveryInsideVertexKept()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        var field = AsymptoticCurves.ComputeDirections(PrincipalCurvature.Compute(mesh));
        var opts = new EvenlySpacedNet.Options { Spacing = 0.25, Continuous = false, MaxCurves = 80 };
        var famA = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, opts);
        var famB = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, opts);
        Assert.True(famA.Count > 5 && famB.Count > 5);
        var res = NetTrim.Trim(proj, new[] { famA, famB }, new[] { SaddleCircle(1.0) });

        var kept = new HashSet<Vec3d>();
        foreach (var fam in res.Families) foreach (var piece in fam) foreach (var p in piece) kept.Add(p);
        int insideVertices = 0, insideKept = 0, outsideKept = 0;
        foreach (var fam in new[] { famA, famB })
            foreach (var curve in fam)
                foreach (var p in curve)
                {
                    double rho = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                    if (rho < 1 - Tol) { insideVertices++; if (kept.Contains(p)) insideKept++; }
                    if (rho > 1 + Tol && kept.Contains(p)) outsideKept++;
                }
        Assert.True(insideVertices > 200);
        Assert.Equal(insideVertices, insideKept);
        Assert.Equal(0, outsideKept);
        foreach (var q in res.Cuts) Assert.True(Math.Abs(Math.Sqrt(q.X * q.X + q.Y * q.Y) - 1) < Tol, $"cut off the circle: {q}");
        Assert.True(res.Cuts.Count >= 20, $"{res.Cuts.Count} cuts");
        Assert.True(res.CutCurves + res.RemovedWhole <= famA.Count + famB.Count);
    }

    [Fact]
    public void Region_IsInside_ClassifiesPointsNearAndFarFromTheLoop()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        var region = NetTrim.Region.Build(proj, new[] { SaddleCircle(1.0) });
        var rnd = new Random(7);
        int wrong = 0, near = 0, nearWrong = 0;
        for (int i = 0; i < 4000; i++)
        {
            double x = -1.45 + 2.9 * rnd.NextDouble(), y = -1.45 + 2.9 * rnd.NextDouble();
            double rho = Math.Sqrt(x * x + y * y);
            if (Math.Abs(rho - 1) < Tol) continue;    // on the pulled loop itself the side is a tie
            bool truth = rho < 1, got = region.IsInside(OnSaddle(x, y));
            if (Math.Abs(rho - 1) < 0.05) { near++; if (truth != got) nearWrong++; }
            if (truth != got) wrong++;
        }
        Assert.True(near > 50);
        Assert.True(wrong == 0, $"{wrong} of 4000 random points misclassified ({nearWrong} of {near} within 0.05 of the loop)");
    }

    [Fact]
    public void OpenBoundaryThatDoesNotSeparate_KeepsNothingAndSaysSo()
    {
        var mesh = Saddle();
        var proj = new MeshProjection(mesh);
        var arc = Enumerable.Range(0, 91).Select(i => { double th = Math.PI * i / 90; return OnSaddle(Math.Cos(th), Math.Sin(th)); }).ToArray();
        var res = NetTrim.Trim(proj, new[] { new List<Vec3d[]> { SaddleLineA(0.0) } }, new[] { arc });
        Assert.Equal(1, res.Regions);
        Assert.Equal(0, res.InsideRegions);
        Assert.Empty(res.Families[0]);
        Assert.Contains("does not enclose", res.Report);
    }
}
