using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// NetFaces: the panels of a net and their planarity, checked against closed
/// forms. Planar-quad meshes are the discrete conjugate nets (Sauer 1970; Liu
/// et al. 2006): a translation surface's grid and a rotational surface's
/// curvature-line net are planar exactly, the curvature-line net of a general
/// surface is planar to second order (δ ∝ h²), and an asymptotic net — the
/// self-conjugate case — only to first order (δ ∝ h).
/// </summary>
public class NetFacesTests
{
    private readonly ITestOutputHelper _out;
    public NetFacesTests(ITestOutputHelper output) { _out = output; }

    // ---- helpers -----------------------------------------------------------------

    private static NetFaces.Result Faces(List<Vec3d[]> a, List<Vec3d[]> b, MeshData? reference = null, double tol = 0)
    {
        var xs = NetIntersections.FindAll(a, b, tol);
        var topo = NetTopology.Build(a, b, xs);
        return NetFaces.Build(topo, reference);
    }

    private static Vec3d[] Sample(Func<double, Vec3d> f, double t0, double t1, int n)
    {
        var pts = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) pts[i] = f(t0 + (t1 - t0) * i / n);
        return pts;
    }

    /// <summary>A graph surface z = f(x, y) with its derivatives, for exact direction fields.</summary>
    private sealed class Graph
    {
        public Func<double, double, double> F = null!, Fx = null!, Fy = null!, Fxx = null!, Fxy = null!, Fyy = null!;
        public Vec3d P(double x, double y) => new Vec3d(x, y, F(x, y));

        /// <summary>Unit (in the xy chart) direction of the max-curvature line (principal = true) or the first asymptotic direction.</summary>
        public (double, double) Dir(double x, double y, bool principal, int which)
        {
            double fx = Fx(x, y), fy = Fy(x, y), fxx = Fxx(x, y), fxy = Fxy(x, y), fyy = Fyy(x, y);
            if (principal)
            {
                // shape operator S = I⁻¹ II in the chart basis
                double e = 1 + fx * fx, f = fx * fy, g = 1 + fy * fy;
                double w = Math.Sqrt(1 + fx * fx + fy * fy);
                double l = fxx / w, m = fxy / w, n = fyy / w;
                double det = e * g - f * f;
                double s00 = (g * l - f * m) / det, s01 = (g * m - f * n) / det;
                double s10 = (e * m - f * l) / det, s11 = (e * n - f * m) / det;
                double tr = s00 + s11, dt = s00 * s11 - s01 * s10;
                double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - dt));
                double k = which == 0 ? tr / 2 + disc : tr / 2 - disc; // k1 (max) or k2
                // eigenvector of (S − kI)
                double vx, vy;
                if (Math.Abs(s01) > 1e-14) { vx = s01; vy = k - s00; }
                else if (Math.Abs(s10) > 1e-14) { vx = k - s11; vy = s10; }
                else { vx = which == 0 ? (s00 >= s11 ? 1 : 0) : (s00 >= s11 ? 0 : 1); vy = 1 - vx; }
                double len = Math.Sqrt(vx * vx + vy * vy);
                return (vx / len, vy / len);
            }
            else
            {
                // II(t, t) = 0: fxx tx² + 2 fxy tx ty + fyy ty² = 0
                double a = fxx, b = fxy, c = fyy;
                double disc = Math.Sqrt(Math.Max(0, b * b - a * c));
                double tx, ty;
                if (Math.Abs(a) > 1e-12) { ty = 1; tx = (-b + (which == 0 ? disc : -disc)) / a; }
                else { tx = 1; ty = Math.Abs(c) > 1e-12 ? (-2 * b * (which == 0 ? 1 : 0)) / c : 0; if (which == 1 && Math.Abs(c) < 1e-12) { tx = 0; ty = 1; } }
                double len = Math.Sqrt(tx * tx + ty * ty);
                return (tx / len, ty / len);
            }
        }

        /// <summary>RK4 trace of a direction field from (x0, y0) both ways, inside |x|, |y| ≤ box, as 3D points.</summary>
        public Vec3d[] Trace(double x0, double y0, bool principal, int which, double step, double box)
        {
            var fwd = Half(x0, y0, principal, which, step, box, +1);
            var bwd = Half(x0, y0, principal, which, step, box, -1);
            bwd.Reverse();
            bwd.RemoveAt(bwd.Count - 1);
            bwd.AddRange(fwd);
            return bwd.ToArray();
        }

        private List<Vec3d> Half(double x0, double y0, bool principal, int which, double step, double box, int sign)
        {
            var pts = new List<Vec3d> { P(x0, y0) };
            double x = x0, y = y0;
            var (dx, dy) = Dir(x, y, principal, which); dx *= sign; dy *= sign;
            for (int i = 0; i < 100000; i++)
            {
                (double, double) D(double px, double py)
                {
                    var (ex, ey) = Dir(px, py, principal, which);
                    if (ex * dx + ey * dy < 0) { ex = -ex; ey = -ey; }
                    return (ex, ey);
                }
                var (k1x, k1y) = D(x, y);
                var (k2x, k2y) = D(x + 0.5 * step * k1x, y + 0.5 * step * k1y);
                var (k3x, k3y) = D(x + 0.5 * step * k2x, y + 0.5 * step * k2y);
                var (k4x, k4y) = D(x + step * k3x, y + step * k3y);
                double nx = x + step * (k1x + 2 * k2x + 2 * k3x + k4x) / 6, ny = y + step * (k1y + 2 * k2y + 2 * k3y + k4y) / 6;
                dx = nx - x; dy = ny - y;
                if (Math.Abs(nx) > box || Math.Abs(ny) > box)
                {
                    // clip the last step to the box so every curve ends on it
                    double t = 1;
                    if (Math.Abs(nx) > box) t = Math.Min(t, (box * Math.Sign(nx) - x) / (nx - x));
                    if (Math.Abs(ny) > box) t = Math.Min(t, (box * Math.Sign(ny) - y) / (ny - y));
                    pts.Add(P(x + t * (nx - x), y + t * (ny - y)));
                    break;
                }
                x = nx; y = ny;
                pts.Add(P(x, y));
            }
            return pts;
        }
    }

    // elliptic graph without an umbilic in |x|, |y| ≤ 0.6 (k₁ along x stays the larger curvature), so the
    // max- and min-curvature lines are a plain two-family net there
    private static Graph Elliptic() => new Graph
    {
        F = (x, y) => x * x + 0.2 * y * y + 0.15 * x * y * y + 0.1 * x * x * y,
        Fx = (x, y) => 2 * x + 0.15 * y * y + 0.2 * x * y,
        Fy = (x, y) => 0.4 * y + 0.3 * x * y + 0.1 * x * x,
        Fxx = (x, y) => 2 + 0.2 * y,
        Fxy = (x, y) => 0.3 * y + 0.2 * x,
        Fyy = (x, y) => 0.4 + 0.3 * x,
    };

    private static Graph Hyperbolic() => new Graph
    {
        F = (x, y) => x * x - y * y + 0.3 * x * y * y + 0.2 * x * x * y,
        Fx = (x, y) => 2 * x + 0.3 * y * y + 0.4 * x * y,
        Fy = (x, y) => -2 * y + 0.6 * x * y + 0.2 * x * x,
        Fxx = (x, y) => 2 + 0.4 * y,
        Fxy = (x, y) => 0.6 * y + 0.4 * x,
        Fyy = (x, y) => -2 + 0.6 * x,
    };

    private static (List<Vec3d[]> a, List<Vec3d[]> b) GraphNet(Graph g, bool principal, double h, double box, double step = 0.004)
    {
        var a = new List<Vec3d[]>(); var b = new List<Vec3d[]>();
        int n = (int)Math.Round(2 * box / h);
        for (int i = 0; i <= n; i++)
        {
            double s = -box + 2 * box * i / n;
            if (principal)
            {
                a.Add(g.Trace(0, s, true, 0, step, box)); // max-curvature lines seeded across x = 0
                b.Add(g.Trace(s, 0, true, 1, step, box)); // min-curvature lines seeded along y = 0
            }
            else
            {
                a.Add(g.Trace(s, 0, false, 0, step, box));
                b.Add(g.Trace(s, 0, false, 1, step, box));
            }
        }
        return (a, b);
    }

    // ---- exact cases -----------------------------------------------------------------

    [Fact]
    public void DiagonalDistance_LiftedCorner_ClosedForm()
    {
        // unit square with one corner lifted by δ: the diagonals (0,0,0)–(1,1,δ) and (1,0,0)–(0,1,0) are δ / (2 √(1 + δ²/2)) apart
        double delta = 0.3;
        var a = new Vec3d(0, 0, 0); var b = new Vec3d(1, 0, 0); var c = new Vec3d(1, 1, delta); var d = new Vec3d(0, 1, 0);
        double dist = NetFaces.DiagonalDistance(a, b, c, d);
        double truth = delta / (2 * Math.Sqrt(1 + delta * delta / 2));
        Assert.True(Math.Abs(dist - truth) < 1e-14, $"diagonal distance {dist} vs {truth}");
        Assert.True(Math.Abs(NetFaces.DiagonalDistance(a, b, new Vec3d(1, 1, 0), d)) < 1e-15, "a planar quad has distance 0");
        // the face measure: deviation of the lifted quad from its Newell plane, normalised planarity
        var face = NetFaces.Measure(new[] { a, b, c, d });
        Assert.Equal(4, face.Sides);
        Assert.True(Math.Abs(face.DiagonalDistance - truth) < 1e-14);
        double meanDiag = 0.5 * (Math.Sqrt(2 + delta * delta) + Math.Sqrt(2));
        Assert.True(Math.Abs(face.Planarity - truth / meanDiag) < 1e-14, $"planarity {face.Planarity}");
        Assert.True(face.Deviation > 0.07 && face.Deviation < 0.08, $"deviation {face.Deviation} (the lifted corner sits δ/4 … δ/2 off the mean plane)");
    }

    [Fact]
    public void TranslationSurfaceGrid_IsPlanarExactly_AndOrientedAlongTheSurface()
    {
        // z = x² − y² is a translation surface: its x = const and y = const lines are a conjugate net
        // (f_xy = 0) and every panel closes up exactly (Glymph et al. 2004 translational meshes)
        int n = 12; double box = 1.5;
        var a = new List<Vec3d[]>(); var b = new List<Vec3d[]>();
        for (int i = 0; i <= n; i++)
        {
            double c = -box + 2 * box * i / n;
            a.Add(Sample(t => new Vec3d(c, t, c * c - t * t), -box, box, 24));
            b.Add(Sample(t => new Vec3d(t, c, t * t - c * c), -box, box, 24));
        }
        var saddle = TestMeshes.CreateSaddle(40, 2 * box);
        var r = Faces(a, b, saddle);
        Assert.Equal(n * n, r.Faces.Count);
        Assert.Equal(n * n, r.Quads);
        Assert.Equal(1, r.OuterLoops);
        Assert.Equal(0, r.Tails);
        Assert.True(r.MaxDeviation < 1e-12, $"max deviation {r.MaxDeviation}");
        Assert.True(r.MaxPlanarity < 1e-12, $"max planarity {r.MaxPlanarity}");
        // counter-clockwise about the surface normal (−2x, 2y, 1) at the centroid, and the areas add up to the patch
        double area = 0;
        foreach (var f in r.Faces)
        {
            var c = f.Centroid;
            var sn = new Vec3d(-2 * c.X, 2 * c.Y, 1).Normalized();
            Assert.True(Vec3d.Dot(f.Normal, sn) > 0.95, $"face normal {f.Normal} vs surface {sn}");
            area += f.Area;
        }
        // ∫∫ √(1 + 4x² + 4y²) over the box, by midpoint rule
        double truth = 0; int m = 400;
        for (int i = 0; i < m; i++) for (int j = 0; j < m; j++)
        {
            double x = -box + 2 * box * (i + 0.5) / m, y = -box + 2 * box * (j + 0.5) / m;
            truth += Math.Sqrt(1 + 4 * x * x + 4 * y * y) * (2 * box / m) * (2 * box / m);
        }
        Assert.True(Math.Abs(area - truth) / truth < 0.01, $"area {area} vs {truth}");
        // without a reference mesh the majority rule gives the same orientation
        var r2 = Faces(a, b, null);
        Assert.Equal(r.Faces.Count, r2.Faces.Count);
        foreach (var f in r2.Faces)
            Assert.True(Vec3d.Dot(f.Normal, new Vec3d(-2 * f.Centroid.X, 2 * f.Centroid.Y, 1).Normalized()) > 0.95, "majority rule orientation");
        // the mesh carries every node that bounds a face
        var mesh = r.ToMesh(out var nodeOf);
        Assert.Equal((n + 1) * (n + 1), mesh.VertexCount);
        Assert.Equal(n * n, mesh.FaceCount);
        Assert.Equal(mesh.VertexCount, nodeOf.Distinct().Count());
        var pl = NetFaces.Planarity(mesh);
        Assert.True(pl.Max() < 1e-12);
        Assert.True(Planarization.ComputeDeviation(mesh).Max() < 1e-12);
    }

    [Fact]
    public void RotationalCurvatureLines_ClosedParallels_PlanarTrapezoids()
    {
        // catenoid r = cosh z: meridians and (closed) parallels are its curvature lines; every panel is an
        // isosceles trapezoid, planar exactly; the parallel's seam is fused so the face across it exists
        int nTheta = 24, nZ = 7; double h = 0.9;
        var a = new List<Vec3d[]>(); var b = new List<Vec3d[]>();
        for (int i = 0; i < nTheta; i++)
        {
            double th = 2 * Math.PI * i / nTheta;
            a.Add(Sample(z => new Vec3d(Math.Cosh(z) * Math.Cos(th), Math.Cosh(z) * Math.Sin(th), z), -h, h, 30));
        }
        for (int j = 0; j < nZ; j++)
        {
            double z = -h + 2 * h * j / (nZ - 1);
            b.Add(Sample(t => new Vec3d(Math.Cosh(z) * Math.Cos(t), Math.Cosh(z) * Math.Sin(t), z), 0, 2 * Math.PI, 96)); // closed: first = last
        }
        var cat = TestMeshes.CreateCatenoid(1.0, 2 * h, 64, 32);
        var r = Faces(a, b, cat);
        Assert.Equal(nTheta * (nZ - 1), r.Faces.Count);
        Assert.Equal(r.Faces.Count, r.Quads);
        Assert.Equal(2, r.OuterLoops); // the two rims
        Assert.True(r.MaxPlanarity < 1e-12, $"max planarity {r.MaxPlanarity}");
        Assert.True(r.MaxDeviation < 1e-12, $"max deviation {r.MaxDeviation}");
        Assert.Equal(nTheta * nZ, r.NodeCount);
        // Euler on the annulus: V − E + F = 0
        Assert.Equal(0, r.NodeCount - r.EdgeCount + r.Faces.Count);
        // outward normals (the mesh orients them)
        foreach (var f in r.Faces)
        {
            var c = f.Centroid; var radial = new Vec3d(c.X, c.Y, 0).Normalized();
            Assert.True(Vec3d.Dot(f.Normal, radial) > 0.5, $"normal {f.Normal} should point outward at {c}");
        }
    }

    [Fact]
    public void HyperboloidRulings_SkewQuads_FirstOrder_vs_CurvatureLinesExact()
    {
        // x² + y² − z² = 1: the rulings are the asymptotic net (self-conjugate), the meridian/parallel net the
        // conjugate one. The ruling panels are skew with δ_PQ,n ∝ h (ratio 2 per halving of the spacing);
        // the curvature-line panels are planar exactly.
        double zMax = 0.8;
        List<Vec3d[]> Rulings(int n, int sign)
        {
            var list = new List<Vec3d[]>();
            for (int i = 0; i < n; i++)
            {
                double th = 2 * Math.PI * i / n;
                list.Add(Sample(z => new Vec3d(Math.Cos(th) - sign * z * Math.Sin(th), Math.Sin(th) + sign * z * Math.Cos(th), z), -zMax, zMax, 2));
            }
            return list;
        }
        var hyp = TestMeshes.CreateHyperboloid(1, 1, zMax, 64, 32);
        var coarse = Faces(Rulings(24, 1), Rulings(24, -1), hyp);
        var fine = Faces(Rulings(48, 1), Rulings(48, -1), hyp);
        _out.WriteLine($"rulings 24: {coarse.Faces.Count} faces ({coarse.Quads} quads), planarity max {coarse.MaxPlanarity:E3} mean {coarse.MeanPlanarity:E3}; 48: {fine.Faces.Count} faces, max {fine.MaxPlanarity:E3} mean {fine.MeanPlanarity:E3}");
        Assert.True(coarse.Quads > 100 && fine.Quads > 400);
        Assert.True(coarse.MaxPlanarity > 0.02, $"ruling quads are skew: {coarse.MaxPlanarity}");
        double ratio = coarse.MeanPlanarity / fine.MeanPlanarity;
        Assert.True(ratio > 1.8 && ratio < 2.25, $"δ_PQ,n ∝ h on the asymptotic net: ratio {ratio}");
        // leading order at the throat: diagonal distance = ½ h² |II(t₁, t₂)| with h the panel's edge and
        // |II(t₁, t₂)| = 1 there (k1 = 1 along the circle, k2 = −1 along the meridian, rulings at ±45°)
        var fineMesh = fine.ToMesh(out var fineNodes);
        var throat = fine.Faces.Where(f => Math.Abs(f.Centroid.Z) < 0.05).ToList();
        Assert.True(throat.Count >= 40, $"{throat.Count} throat panels");
        double worst = 0;
        foreach (var f in throat)
        {
            var p = f.Nodes.Select(i => fineMesh.Vertices[Array.IndexOf(fineNodes, i)]).ToArray();
            double h = 0; for (int k = 0; k < 4; k++) h += (p[(k + 1) % 4] - p[k]).Length; h /= 4;
            double predicted = 0.5 * h * h;
            worst = Math.Max(worst, Math.Abs(f.DiagonalDistance / predicted - 1));
        }
        _out.WriteLine($"throat panels: diagonal distance within {worst:P1} of ½h²");
        Assert.True(worst < 0.1, $"leading-order prediction off by {worst:P1}");

        var meridians = new List<Vec3d[]>(); var parallels = new List<Vec3d[]>();
        for (int i = 0; i < 24; i++)
        {
            double th = 2 * Math.PI * i / 24;
            meridians.Add(Sample(z => new Vec3d(Math.Sqrt(1 + z * z) * Math.Cos(th), Math.Sqrt(1 + z * z) * Math.Sin(th), z), -zMax, zMax, 40));
        }
        for (int j = 0; j < 7; j++)
        {
            double z = -zMax + 2 * zMax * j / 6, rr = Math.Sqrt(1 + z * z);
            parallels.Add(Sample(t => new Vec3d(rr * Math.Cos(t), rr * Math.Sin(t), z), 0, 2 * Math.PI, 96));
        }
        var conj = Faces(meridians, parallels, hyp);
        Assert.Equal(24 * 6, conj.Quads);
        Assert.True(conj.MaxPlanarity < 1e-12, $"curvature-line panels planar: {conj.MaxPlanarity}");
    }

    [Fact]
    public void GeneralSurface_CurvatureLineNet_SecondOrder_AsymptoticNet_FirstOrder()
    {
        // exact (RK4 on the analytic field) curvature lines of an elliptic graph and asymptotic lines of a
        // hyperbolic graph; δ_PQ,n of the conjugate net falls by 4 per halving of the spacing, of the
        // asymptotic net by 2 (Liu et al. 2006 §2; Taylor: diagonal distance = ½h²|II(t₁,t₂)| + O(h³))
        double box = 0.6;
        var ell = Elliptic(); var hyp = Hyperbolic();
        var (ca, cb) = GraphNet(ell, true, 0.2, box);
        var (fa, fb) = GraphNet(ell, true, 0.1, box);
        var conj1 = Faces(ca, cb); var conj2 = Faces(fa, fb);
        var (aa, ab) = GraphNet(hyp, false, 0.2, box);
        var (ba, bb) = GraphNet(hyp, false, 0.1, box);
        var asym1 = Faces(aa, ab); var asym2 = Faces(ba, bb);
        _out.WriteLine($"conjugate h=0.2: {conj1.Faces.Count} faces, δ mean {conj1.MeanPlanarity:E3} max {conj1.MaxPlanarity:E3}; h=0.1: {conj2.Faces.Count}, mean {conj2.MeanPlanarity:E3} max {conj2.MaxPlanarity:E3}");
        _out.WriteLine($"asymptotic h=0.2: {asym1.Faces.Count} faces, δ mean {asym1.MeanPlanarity:E3} max {asym1.MaxPlanarity:E3}; h=0.1: {asym2.Faces.Count}, mean {asym2.MeanPlanarity:E3} max {asym2.MaxPlanarity:E3}");
        Assert.True(conj1.Quads >= 20 && conj2.Quads >= 100, $"{conj1.Quads} / {conj2.Quads} conjugate quads");
        Assert.True(asym1.Quads >= 15 && asym2.Quads >= 80, $"{asym1.Quads} / {asym2.Quads} asymptotic quads");
        double rc = conj1.MeanPlanarity / conj2.MeanPlanarity, ra = asym1.MeanPlanarity / asym2.MeanPlanarity;
        Assert.True(rc > 3.2 && rc < 6.5, $"conjugate net: at least second order expected (ratio ≥ 4 up to higher-order terms), got {rc}");
        Assert.True(ra > 1.8 && ra < 2.3, $"asymptotic net: first order expected (ratio ≈ 2), got {ra}");
        // the same comparison over the panels of one region (centroid within 0.25 of the origin)
        double rcc = conj1.Faces.Where(f => f.Centroid.X * f.Centroid.X + f.Centroid.Y * f.Centroid.Y < 0.0625).Average(f => f.Planarity)
                   / conj2.Faces.Where(f => f.Centroid.X * f.Centroid.X + f.Centroid.Y * f.Centroid.Y < 0.0625).Average(f => f.Planarity);
        double rac = asym1.Faces.Where(f => f.Centroid.X * f.Centroid.X + f.Centroid.Y * f.Centroid.Y < 0.0625).Average(f => f.Planarity)
                   / asym2.Faces.Where(f => f.Centroid.X * f.Centroid.X + f.Centroid.Y * f.Centroid.Y < 0.0625).Average(f => f.Planarity);
        _out.WriteLine($"central region: conjugate ratio {rcc:F2}, asymptotic ratio {rac:F2}");
        Assert.True(rcc > 3.2 && rac > 1.8 && rac < 2.3, $"central region ratios {rcc} / {rac}");
        Assert.True(conj2.MeanPlanarity < 0.1 * asym2.MeanPlanarity, "the conjugate net is far more planar at the same spacing");
        // the asymptotic panels nearest the origin: diagonal distance = ½ h² |II(t₁, t₂)| with the Hessian
        // diag(2, −2) and the directions (1, ±1)/√2: |II| = |2·½ − 2·(−½)| = 2, so ½h²·2 = h²
        var mesh2 = asym2.ToMesh(out var nodes2);
        var centre = asym2.Faces.OrderBy(f => f.Centroid.X * f.Centroid.X + f.Centroid.Y * f.Centroid.Y).Take(4).ToList();
        foreach (var f in centre)
        {
            var p = f.Nodes.Select(i => mesh2.Vertices[Array.IndexOf(nodes2, i)]).ToArray();
            double h = 0; for (int k = 0; k < 4; k++) h += (p[(k + 1) % 4] - p[k]).Length; h /= 4;
            double rel = f.DiagonalDistance / (h * h);
            _out.WriteLine($"central asymptotic panel at {f.Centroid}: diagonal distance / h² = {rel:F3} (1 at the origin)");
            Assert.True(rel > 0.8 && rel < 1.25, $"leading order ½h²|II| = h² expected, got {rel} h²");
        }
    }

    [Fact]
    public void TracedClassicNet_Euler_Orientation_TJunctionPolygons()
    {
        // a real traced net with T-junctions (classic fill): faces are a disk's — V − E + F = 1 — every face
        // counter-clockwise about the mesh normal, and the T-junctions make pentagons
        var saddle = TestMeshes.CreateSaddle(48, 3.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var field = AsymptoticCurves.ComputeDirections(pc, saddle, 15.0);
        var opts = new EvenlySpacedNet.Options { Spacing = 0.3, Continuous = false, MaxCurves = 60 };
        var a = EvenlySpacedNet.TraceField(saddle, field.Family1, field.Exists, -1, opts, field.Family2);
        var b = EvenlySpacedNet.TraceField(saddle, field.Family2, field.Exists, -1, opts, field.Family1);
        Assert.True(a.Count > 5 && b.Count > 5, $"{a.Count} + {b.Count} curves");
        var xs = NetIntersections.FindAll(a, b);
        var topo = NetTopology.Build(a, b, xs);
        var r = NetFaces.Build(topo, saddle);
        _out.WriteLine($"traced: {r.NodeCount} nodes, {r.EdgeCount} edges, {r.Faces.Count} faces ({r.Triangles} tri, {r.Quads} quad, {r.Polygons} poly), {r.OuterLoops} outer loops, {r.Tails} tails; planarity mean {r.MeanPlanarity:F4} max {r.MaxPlanarity:F4}");
        Assert.True(r.Faces.Count > 50, $"{r.Faces.Count} faces");
        Assert.Equal(1, r.NodeCount - r.EdgeCount + r.Faces.Count + r.Lenses);
        Assert.True(r.Polygons > 0, "T-junctions make pentagons");
        Assert.True(r.Faces.All(f => f.Nodes.Distinct().Count() == f.Nodes.Length), "no face visits a node twice");
        int tj = xs.Count(x => x.IsTJunction);
        Assert.True(tj > 0);
        foreach (var f in r.Faces)
        {
            var c = f.Centroid;
            var sn = new Vec3d(-2 * c.X, 2 * c.Y, 1).Normalized();
            Assert.True(Vec3d.Dot(f.Normal, sn) > 0.8, $"face normal {f.Normal} vs surface {sn}");
            Assert.True(f.Members.All(m => m >= 0));
        }
        // asymptotic panels on a saddle are skew: a visible planarity defect
        Assert.True(r.MeanPlanarity > 0.005, $"mean planarity {r.MeanPlanarity}");
    }

    [Fact]
    public void Planarize_ConjugatePanelsStay_AsymptoticPanelsMove()
    {
        // Planarize Mesh on the two net meshes of one surface: the curvature-line mesh is already planar
        // (nothing moves); the asymptotic mesh reaches planarity only by leaving the surface
        double zMax = 0.8; int n = 24;
        var meridians = new List<Vec3d[]>(); var parallels = new List<Vec3d[]>();
        for (int i = 0; i < n; i++)
        {
            double th = 2 * Math.PI * i / n;
            meridians.Add(Sample(z => new Vec3d(Math.Sqrt(1 + z * z) * Math.Cos(th), Math.Sqrt(1 + z * z) * Math.Sin(th), z), -zMax, zMax, 40));
        }
        for (int j = 0; j < 7; j++)
        {
            double z = -zMax + 2 * zMax * j / 6, rr = Math.Sqrt(1 + z * z);
            parallels.Add(Sample(t => new Vec3d(rr * Math.Cos(t), rr * Math.Sin(t), z), 0, 2 * Math.PI, 96));
        }
        var hyp = TestMeshes.CreateHyperboloid(1, 1, zMax, 64, 32);
        var conj = Faces(meridians, parallels, hyp).ToMesh(out _);
        var pr = Planarization.Compute(conj, new bool[conj.VertexCount], new Planarization.Options { Tolerance = 1e-9 });
        double moved = 0; for (int i = 0; i < conj.VertexCount; i++) moved = Math.Max(moved, (pr.Vertices[i] - conj.Vertices[i]).Length);
        Assert.True(moved < 1e-9, $"a planar mesh does not move: {moved}");

        List<Vec3d[]> Rulings(int sign)
        {
            var list = new List<Vec3d[]>();
            for (int i = 0; i < n; i++)
            {
                double th = 2 * Math.PI * i / n;
                list.Add(Sample(z => new Vec3d(Math.Cos(th) - sign * z * Math.Sin(th), Math.Sin(th) + sign * z * Math.Cos(th), z), -zMax, zMax, 2));
            }
            return list;
        }
        var asym = Faces(Rulings(1), Rulings(-1), hyp).ToMesh(out _);
        double before = NetFaces.Planarity(asym).Max();
        var pa = Planarization.Compute(asym, new bool[asym.VertexCount], new Planarization.Options { MaxIterations = 2000, Tolerance = 1e-8 });
        var after = new MeshData(pa.Vertices, asym.Faces);
        double devAfter = Planarization.ComputeDeviation(after).Max();
        double movedA = 0, off = 0;
        for (int i = 0; i < asym.VertexCount; i++)
        {
            movedA = Math.Max(movedA, (pa.Vertices[i] - asym.Vertices[i]).Length);
            var p = pa.Vertices[i];
            off = Math.Max(off, Math.Abs(p.X * p.X + p.Y * p.Y - p.Z * p.Z - 1));
        }
        _out.WriteLine($"asymptotic mesh: planarity {before:E2} → deviation {devAfter:E2} after {pa.Iterations} iterations; vertices moved up to {movedA:F4}, off the hyperboloid by up to {off:F4}");
        Assert.True(devAfter < 1e-4, $"planarized to {devAfter}");
        Assert.True(movedA > 0.01, $"the skew panels can only flatten by leaving the surface: moved {movedA}");
        // with the outermost nodes held, planarity cannot be reached: the deviation stays
        var fixedRims = new bool[asym.VertexCount];
        for (int i = 0; i < asym.VertexCount; i++) fixedRims[i] = Math.Abs(asym.Vertices[i].Z) > 0.55;
        Assert.True(fixedRims.Count(f => f) > 10);
        var pf = Planarization.Compute(asym, fixedRims, new Planarization.Options { MaxIterations = 300, Tolerance = 1e-8 });
        double devFixed = pf.FaceDeviations.Max();
        _out.WriteLine($"rims fixed: deviation {Planarization.ComputeDeviation(asym).Max():E2} → {devFixed:E2}");
        Assert.True(devFixed > 1e-4, "doubly curved panels with a fixed boundary keep a residual");
    }
}
