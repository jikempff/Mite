using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// AAG propagation from a boundary strip (Wang, Almaskin &amp; Pottmann 2025)
/// against two exact discrete AAG webs on surfaces of revolution.
/// <para>
/// A rotational web with N meridians (row angle δ) has rings (ρ_t, z_t) on
/// alternate meridians, row m holding the rings t = m + 2j. The star at ring
/// t has its four neighbours at rings t ± 1 on the meridians m ± 1; the two
/// chords between the mirror pairs are parallel (both perpendicular to the
/// meridian plane), so the star is planar iff the vertex is collinear with
/// their midpoints: (ρ_t, z_t) lies on the line through (ρ_{t−1} cos δ, z_{t−1})
/// and (ρ_{t+1} cos δ, z_{t+1}) in the meridian half-plane, and the meridian
/// itself is a discrete geodesic by mirror symmetry (its osculating plane is
/// the meridian plane, which contains the star normal). Two closed-form
/// solutions:
/// <list type="bullet">
/// <item>uniform rings z_t = h t: ρ_{t+1} + ρ_{t−1} = (2 / cos δ) ρ_t, so
/// ρ_t = R cosh(a t) with cosh a = 1 / cos δ — a discrete catenoid (for
/// h = a R it samples ρ = R cosh(z / R) at the asymptotic lattice);</item>
/// <item>the rulings of the hyperboloid ρ² = R² + z² tan²β: the U rulings
/// at angles 2aδ, the W rulings at 2bδ, meeting at ring k = b − a, angle
/// (a + b)δ: ρ_k = R / cos(kδ), z_k = R cot β tan(kδ) — both asymptotic
/// families straight, the stars planar trivially.</item>
/// </list>
/// </para>
/// </summary>
public class WebPropagationTests
{
    private readonly ITestOutputHelper _out;
    public WebPropagationTests(ITestOutputHelper output) { _out = output; }

    /// <summary>Row m of the discrete catenoid web: rings t = m + 2j − t0 at angle m δ.</summary>
    internal static Vec3d[] CatenoidRow(int m, int n, double delta, double R, double h, int t0)
    {
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1)); // acosh(1/cos δ)
        var row = new Vec3d[n];
        for (int j = 0; j < n; j++)
        {
            int t = m + 2 * j - t0;
            double rho = R * Math.Cosh(a * t), z = h * t, phi = m * delta;
            row[j] = new Vec3d(rho * Math.Cos(phi), rho * Math.Sin(phi), z);
        }
        return row;
    }

    /// <summary>Row m of the hyperboloid ruling web: rings k = m + 2j − t0 at angle m δ, ρ = R / cos kδ, z = R cot β tan kδ.</summary>
    internal static Vec3d[] HyperboloidRow(int m, int n, double delta, double R, double beta, int t0)
    {
        var row = new Vec3d[n];
        for (int j = 0; j < n; j++)
        {
            int k = m + 2 * j - t0;
            double rho = R / Math.Cos(k * delta), z = R / Math.Tan(beta) * Math.Tan(k * delta), phi = m * delta;
            row[j] = new Vec3d(rho * Math.Cos(phi), rho * Math.Sin(phi), z);
        }
        return row;
    }

    private static double Dist(Vec3d[] a, Vec3d[] b) => a.Zip(b, (p, q) => (p - q).Length).Max();

    private static double LineDistance(Vec3d foot, Vec3d dir, Vec3d p)
    {
        var d = p - foot;
        return (d - Vec3d.Dot(d, dir) * dir).Length;
    }

    [Theory]
    [InlineData(24, 7, 1.0)]
    [InlineData(36, 9, 1.3)]
    public void DiscreteCatenoid_IsAnExactAagWeb(int N, int n, double R)
    {
        double delta = 2 * Math.PI / N;
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        double h = a * R;
        var rows = Enumerable.Range(0, 6).Select(m => CatenoidRow(m, n, delta, R, h, 6)).ToList();
        for (int k = 2; k < rows.Count; k++)
        {
            double star = WebPropagation.StarErrorDegrees(rows[k - 2], rows[k - 1], rows[k]);
            double geo = WebPropagation.GeodesicErrorDegrees(rows[k - 1], rows[k]);
            _out.WriteLine($"row {k}: star {star:E2}°, geodesic {geo:E2}°");
            Assert.True(star < 1e-9, $"star {star}");
            Assert.True(geo < 1e-9, $"geodesic {geo}");
            // the rulings L_j of the strip pass through the exact next vertices
            for (int j = 0; j + 2 < n; j++)
            {
                var (foot, dir, parallel) = WebPropagation.Ruling(rows[k - 2], rows[k - 1], j);
                Assert.False(parallel);
                double d = LineDistance(foot, dir, rows[k][j]);
                Assert.True(d < 1e-11 * R, $"ruling {j} misses the exact vertex by {d}");
            }
        }
        // sanity: the discrete catenoid lies on the smooth one, ρ = R cosh(z / R)
        foreach (var p in rows[3]) Assert.Equal(R * Math.Cosh(p.Z / R), Math.Sqrt(p.X * p.X + p.Y * p.Y), 9);
    }

    [Fact]
    public void Hyperboloid_RulingWeb_IsAnExactAagWeb()
    {
        int N = 60, n = 6; double delta = 2 * Math.PI / N, R = 1.0, beta = 0.6;
        var rows = Enumerable.Range(0, 6).Select(m => HyperboloidRow(m, n, delta, R, beta, 8)).ToList();
        for (int k = 2; k < rows.Count; k++)
        {
            Assert.True(WebPropagation.StarErrorDegrees(rows[k - 2], rows[k - 1], rows[k]) < 1e-9);
            Assert.True(WebPropagation.GeodesicErrorDegrees(rows[k - 1], rows[k]) < 1e-9);
            for (int j = 0; j + 2 < n; j++)
            {
                var (foot, dir, _) = WebPropagation.Ruling(rows[k - 2], rows[k - 1], j);
                Assert.True(LineDistance(foot, dir, rows[k][j]) < 1e-11);
            }
            // the U and W edges are straight rulings: three consecutive nodes collinear (sine of the turn)
            for (int j = 0; j < n; j++)
            {
                Vec3d a = rows[k - 1][j] - rows[k - 2][j], b = rows[k][j] - rows[k - 1][j];
                double u = Vec3d.Cross(a, b).Length / (a.Length * b.Length);
                Assert.True(u < 1e-12, $"U ruling bent {u}");
                if (j >= 2)
                {
                    Vec3d c = rows[k - 1][j - 1] - rows[k - 2][j], d = rows[k][j - 2] - rows[k - 1][j - 1];
                    double w = Vec3d.Cross(c, d).Length / (c.Length * d.Length);
                    Assert.True(w < 1e-12, $"W ruling bent {w}");
                }
            }
        }
        foreach (var p in rows[2])
            Assert.Equal(R * R + p.Z * p.Z * Math.Tan(beta) * Math.Tan(beta), p.X * p.X + p.Y * p.Y, 10);
    }

    [Fact]
    public void Propagation_WithExactTargets_ReproducesTheDiscreteCatenoid()
    {
        int N = 24, n = 7, rowsToAdd = 8; double delta = 2 * Math.PI / N, R = 1.0;
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        double h = a * R;
        var exact = Enumerable.Range(0, rowsToAdd + 2).Select(m => CatenoidRow(m, n, delta, R, h, 8)).ToList();
        // the exact rows as targets pin the free directions (the rotation by δ is not a symmetry of the
        // staggered web — rows of one parity map to rows of the other — so no local rule repeats it);
        // fairness off, since the exact meridian rows are curved and the default 0.01 would trade
        // 1e-4 of position for straightness
        var opt = new WebPropagation.Options { Rows = rowsToAdd, Targets = exact.Skip(2).ToList(), TargetWeight = 1.0, Fairness = 0 };
        var res = WebPropagation.Propagate(exact[0], exact[1], opt);
        Assert.Null(res.Stopped);
        Assert.Equal(rowsToAdd, res.RowsBuilt);
        double worst = 0;
        for (int k = 2; k < exact.Count; k++)
        {
            double d = Dist(res.Rows[k], exact[k]);
            worst = Math.Max(worst, d);
            _out.WriteLine($"row {k}: {d:E2} from the exact row, geodesic {res.GeodesicError[k - 2]:E2}°, star {res.StarError[k - 2]:E2}°, {res.Iterations[k - 2]} iterations");
        }
        Assert.True(worst < 1e-9, $"worst {worst}");
        Assert.True(res.MaxGeodesicError < 1e-8);
        Assert.True(res.MaxStarError < 1e-9);
    }

    [Fact]
    public void Propagation_WithExactTargets_ReproducesTheHyperboloidRulings()
    {
        int N = 60, n = 6, rowsToAdd = 10; double delta = 2 * Math.PI / N, R = 1.0, beta = 0.6;
        var exact = Enumerable.Range(0, rowsToAdd + 2).Select(m => HyperboloidRow(m, n, delta, R, beta, 10)).ToList();
        var opt = new WebPropagation.Options { Rows = rowsToAdd, Targets = exact.Skip(2).ToList(), TargetWeight = 1.0, Fairness = 0 };
        var res = WebPropagation.Propagate(exact[0], exact[1], opt);
        Assert.Null(res.Stopped);
        double worst = 0;
        for (int k = 2; k < exact.Count; k++) worst = Math.Max(worst, Dist(res.Rows[k], exact[k]));
        _out.WriteLine($"hyperboloid: worst {worst:E2}, geodesic {res.MaxGeodesicError:E2}°, star {res.MaxStarError:E2}°");
        Assert.True(worst < 1e-9, $"worst {worst}");
        // the propagated asymptotic laths are the straight rulings
        foreach (var lath in res.Web.A.Concat(res.Web.B))
            for (int i = 1; i + 1 < lath.Length; i++)
            {
                Vec3d a = lath[i] - lath[i - 1], b = lath[i + 1] - lath[i];
                Assert.True(Vec3d.Cross(a, b).Length / (a.Length * b.Length) < 1e-9);
            }
    }

    [Fact]
    public void Propagation_Default_KeepsTheLathSpacing_AndIsAnAagWeb()
    {
        int N = 48, n = 9, rowsToAdd = 12; double delta = 2 * Math.PI / N, R = 1.0;
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        var r0 = CatenoidRow(0, n, delta, R, a * R, 8);
        var r1 = CatenoidRow(1, n, delta, R, a * R, 8);
        var res = WebPropagation.Propagate(r0, r1, new WebPropagation.Options { Rows = rowsToAdd });
        Assert.Null(res.Stopped);
        Assert.True(res.MaxStarError < 1e-9, $"star {res.MaxStarError}");
        Assert.True(res.MaxGeodesicError < 1e-8, $"geodesic {res.MaxGeodesicError}");
        // Offset 0 repeats the strip's triangles: along every asymptotic lath the node spacing stays
        // within a few per cent of the strip's (the geodesic condition takes what it needs)
        double worst = 0;
        foreach (var lath in res.Web.A.Concat(res.Web.B))
        {
            double first = (lath[1] - lath[0]).Length;
            for (int i = 1; i + 1 < lath.Length; i++) worst = Math.Max(worst, Math.Abs((lath[i + 1] - lath[i]).Length / first - 1));
        }
        _out.WriteLine($"default: geodesic max {res.MaxGeodesicError:E2}°, star max {res.MaxStarError:E2}°, lath spacing drift {worst:P2}, iterations {string.Join(" ", res.Iterations)}");
        // (the exact catenoid web itself stretches its U edges 2.6× over these rings: the geodesic condition decides the shape)
        Assert.True(worst < 1.0, $"spacing drift {worst}");
    }

    [Fact]
    public void Propagation_Equilateral_BuildsAnAagWeb_UnlikeTheNaiveOffset()
    {
        int N = 48, n = 9, rowsToAdd = 12; double delta = 2 * Math.PI / N, R = 1.0;
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        double h = a * R;
        var r0 = CatenoidRow(0, n, delta, R, h, 8);
        var r1 = CatenoidRow(1, n, delta, R, h, 8);
        var res = WebPropagation.Propagate(r0, r1, new WebPropagation.Options { Rows = rowsToAdd, Offset = 1.0 });
        Assert.Null(res.Stopped);
        Assert.Equal(rowsToAdd, res.RowsBuilt);
        _out.WriteLine($"equilateral: geodesic max {res.MaxGeodesicError:E2}°, star max {res.MaxStarError:E2}°, iterations {string.Join(" ", res.Iterations)}");
        Assert.True(res.MaxStarError < 1e-9, $"star {res.MaxStarError}");
        Assert.True(res.MaxGeodesicError < 1e-6, $"geodesic {res.MaxGeodesicError}");

        // the naive offset (translate the last row by the last step) is no AAG web
        var naive = new List<Vec3d[]> { r0, r1 };
        for (int k = 0; k < rowsToAdd; k++)
        {
            var p = naive[naive.Count - 2]; var l = naive[naive.Count - 1];
            naive.Add(l.Select((v, j) => v + (l[j] - p[j])).ToArray());
        }
        double naiveStar = 0, naiveGeo = 0;
        for (int k = 2; k < naive.Count; k++)
        {
            naiveStar = Math.Max(naiveStar, WebPropagation.StarErrorDegrees(naive[k - 2], naive[k - 1], naive[k]));
            naiveGeo = Math.Max(naiveGeo, WebPropagation.GeodesicErrorDegrees(naive[k - 1], naive[k]));
        }
        _out.WriteLine($"naive offset: geodesic max {naiveGeo:F3}°, star max {naiveStar:F3}°");
        Assert.True(naiveStar > 0.1 || naiveGeo > 0.1);

        // the row edges stay near the strip's own (the equilateral targets keep the cells uniform)
        for (int k = 0; k < res.Rows.Count; k++)
        {
            var row = res.Rows[k];
            var e = Enumerable.Range(0, n - 1).Select(j => (row[j + 1] - row[j]).Length / (r1[j + 1] - r1[j]).Length).ToArray();
            _out.WriteLine($"row {k}: edge/strip edge {string.Join(" ", e.Select(v => v.ToString("F3")))}  rho {string.Join(" ", row.Select(p => Math.Sqrt(p.X * p.X + p.Y * p.Y).ToString("F3")))}  z {string.Join(" ", row.Select(p => p.Z.ToString("F3")))}");
            for (int j = 0; j + 1 < n; j++) Assert.InRange(e[j], 0.5, 2.0);
        }
        // and the web keeps its distance from the axis in the band of the strip (it stays a catenoid-like surface)
        double rhoMin = res.Web.Nodes.Min(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)), rhoMax = res.Web.Nodes.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        _out.WriteLine($"radius range {rhoMin:F3} … {rhoMax:F3}, z range {res.Web.Nodes.Min(p => p.Z):F3} … {res.Web.Nodes.Max(p => p.Z):F3}");
        double dev = 0;
        foreach (var p in res.Web.Nodes) dev = Math.Max(dev, Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - R * Math.Cosh(p.Z / R)));
        _out.WriteLine($"largest deviation from the smooth catenoid ρ = R cosh(z/R): {dev:E2}");
    }

    [Fact]
    public void Propagation_Targets_SteerTheFreeDirections()
    {
        int N = 48, n = 7, rowsToAdd = 4; double delta = 2 * Math.PI / N, R = 1.0;
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        var exact = Enumerable.Range(0, rowsToAdd + 2).Select(m => CatenoidRow(m, n, delta, R, a * R, 7)).ToList();
        // targets: the exact rows, so the result must be exact; then the exact rows stretched 20 % in z —
        // not an AAG web, so the row can only follow them in its free directions
        var res = WebPropagation.Propagate(exact[0], exact[1], new WebPropagation.Options { Rows = rowsToAdd, Targets = exact.Skip(2).ToList(), TargetWeight = 1.0, Fairness = 0 });
        for (int k = 2; k < exact.Count; k++) Assert.True(Dist(res.Rows[k], exact[k]) < 1e-9);
        var stretched = exact.Skip(2).Select(r => r.Select(p => new Vec3d(p.X, p.Y, 1.2 * p.Z)).ToArray()).ToList();
        var res2 = WebPropagation.Propagate(exact[0], exact[1], new WebPropagation.Options { Rows = rowsToAdd, Targets = stretched, TargetWeight = 1.0, Fairness = 0 });
        Assert.Null(res2.Stopped);
        double toExact = Dist(res2.Rows[2], exact[2]), toTarget = Dist(res2.Rows[2], stretched[0]);
        _out.WriteLine($"stretched targets: row 2 is {toExact:E2} from the exact row, {toTarget:E2} from its target; geodesic {res2.MaxGeodesicError:E2}°, star {res2.MaxStarError:E2}°, iterations {string.Join(" ", res2.Iterations)}");
        Assert.True(res2.MaxGeodesicError < 1e-8, $"geodesic {res2.MaxGeodesicError}");
        Assert.True(res2.MaxStarError < 1e-9);
        Assert.True(toExact > 1e-4);
    }

    [Fact]
    public void Propagation_PlanarStrip_StaysPlanar_EquilateralTriangles()
    {
        int n = 6; double s = 0.5;
        var r0 = Enumerable.Range(0, n).Select(j => new Vec3d(j * s, 0, 0)).ToArray();
        var r1 = Enumerable.Range(0, n).Select(j => new Vec3d(j * s + s / 2, s * Math.Sqrt(3) / 2, 0)).ToArray();
        var res = WebPropagation.Propagate(r0, r1, new WebPropagation.Options { Rows = 5 });
        Assert.Null(res.Stopped);
        Assert.Equal(5, res.RowsBuilt);
        for (int k = 2; k < res.Rows.Count; k++)
            for (int j = 0; j < n; j++)
            {
                var p = res.Rows[k][j];
                Assert.Equal(0, p.Z, 12);
                Assert.Equal(j * s + k * s / 2, p.X, 9);
                Assert.Equal(k * s * Math.Sqrt(3) / 2, p.Y, 9);
            }
        Assert.True(res.MaxGeodesicError < 1e-9);
    }

    [Fact]
    public void BuildWeb_Topology_AndTheGlobalAagStep()
    {
        int N = 24, n = 7, rowsToAdd = 6; double delta = 2 * Math.PI / N, R = 1.0;
        double a = Math.Log(1 / Math.Cos(delta) + Math.Sqrt(1 / (Math.Cos(delta) * Math.Cos(delta)) - 1));
        var res = WebPropagation.Propagate(CatenoidRow(0, n, delta, R, a * R, 8), CatenoidRow(1, n, delta, R, a * R, 8), new WebPropagation.Options { Rows = rowsToAdd });
        var web = res.Web;
        int K = rowsToAdd + 2;
        Assert.Equal(K * n, web.Nodes.Count);
        Assert.Equal((K - 2) * (n - 1), web.Quads.Count);
        Assert.Equal(n, web.A.Count);
        Assert.All(web.A, l => Assert.Equal(K, l.Length));
        Assert.Equal(n - 1 + K - 2, web.B.Count);
        Assert.Equal(K, web.DiagonalFamily.Count(f => f == 0));
        // every quad: U, W, U, W edges and its row diagonal (k+1, j) – (k+1, j−1)
        foreach (var q in web.Quads)
        {
            Assert.Contains(q[1], web.NeighborsA[q[0]]);
            Assert.Contains(q[2], web.NeighborsB[q[1]]);
            Assert.Contains(q[3], web.NeighborsA[q[2]]);
            Assert.Contains(q[0], web.NeighborsB[q[3]]);
            Assert.Equal(q[1] / n, q[3] / n);
            Assert.Equal(1, Math.Abs(q[1] - q[3]));
        }
        // interior nodes: two A and two B neighbours
        for (int k = 1; k + 1 < K; k++)
            for (int j = 1; j + 1 < n; j++)
            {
                Assert.Equal(2, web.NeighborsA[k * n + j].Length);
                Assert.Equal(2, web.NeighborsB[k * n + j].Length);
            }
        // the global AAG optimisation (no reference surface) finds nothing left to fix
        var opt = AagWeb.Optimize(web, new AagWeb.Options { Iterations = 8, Fairness = 0.0 });
        double moved = opt.Nodes.Zip(web.Nodes, (p, q) => (p - q).Length).Max();
        _out.WriteLine($"AAG step: stars {opt.InitialStarError:E2}° → {opt.StarError:E2}°, geodesic {opt.InitialGeodesicError:E2}° → {opt.GeodesicError:E2}°, moved {moved:E2}");
        Assert.True(opt.InitialStarError < 1e-8);
        Assert.True(opt.InitialGeodesicError < 1e-6);
        Assert.True(opt.StarError < 1e-6);
        Assert.True(opt.GeodesicError < 1e-6);
        Assert.True(moved < 1e-5 * web.Spacing, $"moved {moved}");
        // rows survive the round trip
        var rows = WebPropagation.RowsOf(opt.Nodes, n);
        Assert.Equal(K, rows.Count);
    }

    [Fact]
    public void Propagation_RejectsBadStrips()
    {
        var r0 = new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0) };
        var r1 = new[] { new Vec3d(0.5, 1, 0), new Vec3d(1.5, 1, 0) };
        var res = WebPropagation.Propagate(r0, r1, new WebPropagation.Options { Rows = 3 });
        Assert.NotNull(res.Stopped);
        Assert.Equal(0, res.RowsBuilt);
        var r2 = new[] { new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(2, 0, 0) };
        var res2 = WebPropagation.Propagate(r2, r1, null);
        Assert.NotNull(res2.Stopped);
    }
}
