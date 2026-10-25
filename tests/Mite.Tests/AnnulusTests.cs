using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Streamlines;

namespace Mite.Tests;

/// <summary>
/// Surfaces with holes — the "annulus / disk with holes" typology: inner
/// borders, loops around a hole, curves ending on inner borders. Ground
/// truth: on the annular saddle z = k (x² − y²) the asymptotic curves are the
/// straight lines x ± y = c, cut by the hole; on the paraboloid z = ρ²/2 the
/// curvature lines are the parallels (closed circles of length 2πρ round the
/// hole) and the meridians (length ∫√(1 + ρ²) dρ, inner rim to outer rim); on a
/// flat annulus every geodesic is a straight segment and the shortest path
/// round the hole is the taut string round its polygonal rim (the shorter arc
/// of the convex hull of the two points and the rim); on the dome, geodesics
/// seeded on the rim converge and a geodesic lath that meets a neighbour ends
/// straight, as a board does (Pirazzi &amp; Weinand 2006; Pottmann et al. 2010).
/// </summary>
public class AnnulusTests
{
    private readonly ITestOutputHelper _out;
    public AnnulusTests(ITestOutputHelper o) { _out = o; }

    static double Rho(Vec3d p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);

    static double ChordDeviation(Vec3d[] line)
    {
        Vec3d a = line[0]; Vec3d d = line[^1] - a; double len = d.Length;
        if (len < 1e-15) return 0; d = d / len; double worst = 0;
        foreach (var p in line) { Vec3d v = p - a; worst = Math.Max(worst, (v - Vec3d.Dot(v, d) * d).Length); }
        return worst;
    }

    static double ArcLength(Vec3d[] c) { double s = 0; for (int i = 1; i < c.Length; i++) s += (c[i] - c[i - 1]).Length; return s; }

    static MeshData FlatAnnulus(double rin, double rout, int n, int rows) =>
        AnalyticShapes.Parametric((u, v) => new Vec3d(v * Math.Cos(u), v * Math.Sin(u), 0), 0, 2 * Math.PI, rin, rout, n, rows, true, false, false, false);

    static MeshData ParaboloidAnnulus(double rin, double rout) =>
        AnalyticShapes.Parametric((u, v) => new Vec3d(v * Math.Cos(u), v * Math.Sin(u), 0.5 * v * v), 0, 2 * Math.PI, rin, rout, 96, 24, true, false, false, false);

    /// <summary>Classifies the ends of open curves: on a rim of radius rin / rout (within tol), on another curve, or elsewhere; counts points inside the hole.</summary>
    static (int inner, int outer, int onCurve, int other, int inHole) Ends(List<Vec3d[]> all, double rin, double rout, double tol, double spacing)
    {
        int inner = 0, outer = 0, onC = 0, other = 0, inHole = 0;
        foreach (var c in all)
        {
            foreach (var p in c) if (Rho(p) < rin - tol) inHole++;
            if ((c[0] - c[^1]).LengthSquared < 1e-18) continue;
            foreach (var e in new[] { c[0], c[^1] })
            {
                double r = Rho(e);
                if (Math.Abs(r - rin) < tol) { inner++; continue; }
                if (Math.Abs(r - rout) < tol) { outer++; continue; }
                double best = double.MaxValue;
                foreach (var m in all)
                {
                    if (ReferenceEquals(m, c)) continue;
                    for (int i = 0; i + 1 < m.Length; i++)
                    {
                        Vec3d ab = m[i + 1] - m[i]; double t = Math.Max(0, Math.Min(1, Vec3d.Dot(e - m[i], ab) / Math.Max(ab.LengthSquared, 1e-300)));
                        best = Math.Min(best, (m[i] + t * ab - e).Length);
                    }
                }
                if (best < 1e-6 * spacing) onC++; else other++;
            }
        }
        return (inner, outer, onC, other, inHole);
    }

    /// <summary>
    /// In-surface turning of traced curves (discrete geodesic curvature × spacing, so a lath that turns
    /// by 1 rad over one spacing reads 1) and their ends: on the mesh border, exactly on another curve
    /// (a T-junction), at the contact distance from one (a free end), or elsewhere.
    /// </summary>
    internal static (double maxKg, double meanKg, int border, int onNeighbour, int free, int other) GeodesicQuality(MeshData mesh, List<Vec3d[]> curves, double spacing, double contact)
    {
        var proj = new MeshProjection(mesh);
        double maxKg = 0, sumKg = 0; int cnt = 0;
        int border = 0, onN = 0, free = 0, other = 0;
        foreach (var c in curves)
        {
            int hint = proj.NearestVertexGlobal(c[0]);
            for (int i = 1; i + 1 < c.Length; i++)
            {
                var h = proj.ClosestPoint(c[i], hint); hint = h.NearestVertex; var nrm = h.SmoothNormal;
                Vec3d e0 = c[i] - c[i - 1], e1 = c[i + 1] - c[i];
                e0 = e0 - Vec3d.Dot(e0, nrm) * nrm; e1 = e1 - Vec3d.Dot(e1, nrm) * nrm;
                double l0 = e0.Length, l1 = e1.Length; if (l0 < 1e-12 || l1 < 1e-12) continue;
                double turn = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(e0, e1) / (l0 * l1))));
                double kg = turn / (0.5 * (l0 + l1)) * spacing;
                maxKg = Math.Max(maxKg, kg); sumKg += kg; cnt++;
            }
            if ((c[0] - c[^1]).LengthSquared < 1e-18) continue;
            foreach (var e in new[] { c[0], c[^1] })
            {
                var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e));
                if (proj.IsOnBoundary(h, 1e-4)) { border++; continue; }
                double best = double.MaxValue;
                foreach (var m in curves)
                {
                    if (ReferenceEquals(m, c)) continue;
                    for (int i = 0; i + 1 < m.Length; i++)
                    {
                        Vec3d ab = m[i + 1] - m[i]; double t = Math.Max(0, Math.Min(1, Vec3d.Dot(e - m[i], ab) / Math.Max(ab.LengthSquared, 1e-300)));
                        best = Math.Min(best, (m[i] + t * ab - e).Length);
                    }
                }
                if (best < 1e-6 * spacing) onN++;
                else if (best > 0.5 * contact && best < 1.5 * contact) free++;
                else other++;
            }
        }
        return (maxKg, cnt > 0 ? sumKg / cnt : 0, border, onN, free, other);
    }

    // ---- inner borders: curves end on the hole's rim, nothing is traced across the hole ----

    [Theory]
    [InlineData(NetLayout.Fill)]
    [InlineData(NetLayout.WebBorder)]
    [InlineData(NetLayout.WebCross)]
    public void AnnularSaddle_AsymptoticLines_EndOnEitherRim(NetLayout layout)
    {
        // z = k (x² − y²): asymptotic curves are the lines x ± y = c; the inscribed 48-gon rim is 1.2e-3 inside the circle
        double rin = 0.4, rout = 1.2, k = 0.5, s = 0.15;
        var mesh = AnalyticShapes.Build("annulus", rin, rout, k, 48);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family2);
        var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family1);
        var all = a.Concat(b).ToList();
        var e = Ends(all, rin, rout, 3e-3, s);
        double bow = all.Max(ChordDeviation), dirErr = 0;
        foreach (var c in all) { var d = c[^1] - c[0]; dirErr = Math.Max(dirErr, Math.Abs(Math.Atan2(Math.Abs(d.Y), Math.Abs(d.X)) * 180 / Math.PI - 45)); }
        var (unc, _, _) = WebCoverageTests.Coverage(mesh, field.Exists, all, s);
        _out.WriteLine($"{layout}: {a.Count}+{b.Count} curves; ends inner {e.inner} outer {e.outer} on curve {e.onCurve} other {e.other}; points in hole {e.inHole}; bow {bow:E2}; plan direction off 45° by {dirErr:F3}°; uncovered {unc:P1}");
        Assert.Equal(2, EvenlySpacedNet.BoundaryLoops(mesh).Count);
        Assert.True(a.Count >= 20 && b.Count >= 20);
        Assert.True(e.inner >= 20, $"only {e.inner} ends on the hole's rim");
        Assert.Equal(0, e.other);
        Assert.Equal(0, e.inHole);
        Assert.True(bow < 0.02, $"bow {bow}");           // facets: sagitta of the 0.157 rim cells
        Assert.True(dirErr < 0.5, $"direction {dirErr}°");
        Assert.True(unc < 0.005, $"uncovered {unc:P1}");
    }

    [Fact]
    public void PerforatedSaddle_ThreeHoles_AllLayouts()
    {
        double k = 0.5, s = 0.15;
        var holes = new[] { (-0.45, -0.3, 0.3), (0.5, 0.35, 0.25), (0.1, -0.55, 0.2) };
        var mesh = TestMeshes.CreatePerforated((x, y) => new Vec3d(x, y, k * (x * x - y * y)), -1.2, 1.2, -1.2, 1.2, 60, 60, holes, out var loops);
        Assert.Equal(4, EvenlySpacedNet.BoundaryLoops(mesh).Count);
        Assert.All(loops, l => Assert.NotNull(l));
        // every hole-rim vertex lies on its circle; the mesh stays sane (smallest corner angle)
        for (int h = 0; h < holes.Length; h++)
            foreach (int v in loops[h])
                Assert.True(Math.Abs(Math.Sqrt(Math.Pow(mesh.Vertices[v].X - holes[h].Item1, 2) + Math.Pow(mesh.Vertices[v].Y - holes[h].Item2, 2)) - holes[h].Item3) < 1e-12);
        double minAngle = 180;
        foreach (var f in mesh.Faces)
            for (int i = 0; i < 3; i++)
            {
                var a = mesh.Vertices[f[i]]; var b = mesh.Vertices[f[(i + 1) % 3]]; var c = mesh.Vertices[f[(i + 2) % 3]];
                minAngle = Math.Min(minAngle, Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot((b - a).Normalized(), (c - a).Normalized())))) * 180 / Math.PI);
            }
        Assert.True(minAngle > 3, $"smallest angle {minAngle}°");
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        foreach (var layout in new[] { NetLayout.Fill, NetLayout.WebBorder, NetLayout.WebCross })
        {
            var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family2);
            var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family1);
            var all = a.Concat(b).ToList();
            int inHole = 0, holeEnds = 0, outerEnds = 0, otherEnds = 0; double bow = 0;
            foreach (var c in all)
            {
                bow = Math.Max(bow, ChordDeviation(c));
                foreach (var p in c) foreach (var hh in holes) if (Math.Sqrt(Math.Pow(p.X - hh.Item1, 2) + Math.Pow(p.Y - hh.Item2, 2)) < hh.Item3 - 2e-3) inHole++;
                foreach (var e in new[] { c[0], c[^1] })
                {
                    bool onHole = holes.Any(hh => Math.Abs(Math.Sqrt(Math.Pow(e.X - hh.Item1, 2) + Math.Pow(e.Y - hh.Item2, 2)) - hh.Item3) < 4e-3);
                    bool onOuter = Math.Abs(Math.Abs(e.X) - 1.2) < 3e-3 || Math.Abs(Math.Abs(e.Y) - 1.2) < 3e-3;
                    if (onHole) holeEnds++; else if (onOuter) outerEnds++; else otherEnds++;
                }
            }
            var (unc, _, _) = WebCoverageTests.Coverage(mesh, field.Exists, all, s);
            _out.WriteLine($"{layout}: {a.Count}+{b.Count} curves; ends on holes {holeEnds}, outer {outerEnds}, other {otherEnds}; points in holes {inHole}; bow {bow:E2}; uncovered {unc:P1}");
            Assert.True(holeEnds >= 30, $"{holeEnds} ends on the hole rims");
            Assert.Equal(0, otherEnds);
            Assert.Equal(0, inHole);
            Assert.True(bow < 0.01, $"bow {bow}");
            Assert.True(unc < 0.005, $"uncovered {unc:P1}");
        }
    }

    // ---- loops around the hole ----

    [Fact]
    public void ParaboloidAnnulus_CurvatureLines_LoopRoundTheHole_AndMeridiansRimToRim()
    {
        double rin = 0.5, rout = 1.5;
        var mesh = ParaboloidAnnulus(rin, rout);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        // k_parallel = 1/√(1+ρ²) > k_meridian = 1/(1+ρ²)^{3/2}: D1 along the parallels, D2 along the meridians
        var seeds = Enumerable.Range(0, mesh.VertexCount).Where(i => Math.Abs(Rho(mesh.Vertices[i]) - 1.0) < 0.03).Where((v, i) => i % 8 == 0).Take(5).ToArray();
        double F(double r) => 0.5 * (r * Math.Sqrt(1 + r * r) + Math.Asinh(r));
        double meridian = F(rout) - F(rin);
        foreach (var c in CurvatureStreamlines.Trace(mesh, seeds, pc, new CurvatureStreamlines.Options { UseMaxCurvature = true }))
        {
            Assert.True((c[0] - c[^1]).LengthSquared < 1e-18, "a max-curvature line did not close round the hole");
            double r0 = Rho(c[0]), len = ArcLength(c), drift = c.Max(p => Math.Abs(Rho(p) - r0));
            _out.WriteLine($"parallel: closed, length {len:F4} vs 2πρ {2 * Math.PI * r0:F4} ({(len / (2 * Math.PI * r0) - 1):P2}), radial drift {drift:F4}");
            Assert.True(Math.Abs(len / (2 * Math.PI * r0) - 1) < 0.015, $"length {len} vs {2 * Math.PI * r0}");
            Assert.True(drift < 0.05, $"radial drift {drift}"); // the 0.25° mean tilt of the Rusinkiewicz directions, see ROADMAP D
        }
        foreach (var c in CurvatureStreamlines.Trace(mesh, seeds, pc, new CurvatureStreamlines.Options { UseMaxCurvature = false }))
        {
            double len = ArcLength(c), rMin = c.Min(Rho), rMax = c.Max(Rho);
            double phi0 = Math.Atan2(c[0].Y, c[0].X), dphi = 0;
            foreach (var p in c) { double d = Math.Atan2(p.Y, p.X) - phi0; dphi = Math.Max(dphi, Math.Abs(Math.Atan2(Math.Sin(d), Math.Cos(d)))); }
            _out.WriteLine($"meridian: ρ {rMin:F4} … {rMax:F4}, length {len:F5} vs {meridian:F5}, Δφ {dphi * 180 / Math.PI:F3}°");
            Assert.True(Math.Abs(rMin - rin) < 1e-3 && Math.Abs(rMax - rout) < 1e-3, $"meridian does not run rim to rim: ρ {rMin} … {rMax}"); // the 96-gon rims are 2.7e-4 inside their circles
            Assert.True(Math.Abs(len - meridian) < 1e-3 * meridian, $"length {len} vs {meridian}");
            Assert.True(dphi < 0.1 * Math.PI / 180, $"meridian wanders by {dphi * 180 / Math.PI}°");
        }
    }

    [Fact]
    public void ParaboloidAnnulus_IsocurvesAreSingleClosedLoops()
    {
        var mesh = ParaboloidAnnulus(0.5, 1.5);
        var z = mesh.Vertices.Select(p => p.Z).ToArray();
        var levels = new[] { 0.2, 0.5, 0.8 };
        var iso = MeshIsocurves.Compute(mesh, z, levels);
        for (int l = 0; l < levels.Length; l++)
        {
            double r = Math.Sqrt(2 * levels[l]);
            Assert.Single(iso[l]);
            var c = iso[l][0];
            Assert.True((c[0] - c[^1]).LengthSquared < 1e-18, "isocurve round the hole did not close");
            double len = ArcLength(c);
            Assert.True(Math.Abs(len - 2 * Math.PI * r) < 2e-3 * 2 * Math.PI * r, $"length {len} vs {2 * Math.PI * r}"); // 96-gon chord factor 1 − 5.4e-4
            Assert.True(c.Max(p => Math.Abs(Rho(p) - r)) < 1e-3);
        }
    }

    // ---- shortest paths round the hole ----

    [Fact]
    public void FlatAnnulus_ShortestPathRoundTheHole_IsTheTautString()
    {
        double rin = 0.4, rout = 1.5; int n = 40;
        var mesh = FlatAnnulus(rin, rout, n, 12);
        var poly = Enumerable.Range(0, n).Select(i => (rin * Math.Cos(2 * Math.PI * i / n), rin * Math.Sin(2 * Math.PI * i / n))).ToList();
        var rnd = new Random(5);
        var from = new List<Vec3d>(); var to = new List<Vec3d>(); var exact = new List<double>();
        for (int k = 0; k < 40; k++)
        {
            double r1 = rin + (rout - rin) * (0.05 + 0.9 * rnd.NextDouble()), r2 = rin + (rout - rin) * (0.05 + 0.9 * rnd.NextDouble());
            double t1 = 2 * Math.PI * rnd.NextDouble(), t2 = t1 + Math.PI * (0.6 + 0.4 * rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
            var p = new Vec3d(r1 * Math.Cos(t1), r1 * Math.Sin(t1), 0); var q = new Vec3d(r2 * Math.Cos(t2), r2 * Math.Sin(t2), 0);
            from.Add(p); to.Add(q);
            exact.Add(TestMeshes.PlanarDistanceAroundPolygon((p.X, p.Y), (q.X, q.Y), poly));
        }
        var res = FlipGeodesic.Compute(mesh, from, to, out var reason);
        Assert.True(res != null, reason);
        double worst = 0; int around = 0;
        for (int k = 0; k < from.Count; k++)
        {
            var r = res![k]!;
            Assert.True(r.Converged, $"pair {k} not converged");
            if (exact[k] > (from[k] - to[k]).Length + 1e-12) around++;
            worst = Math.Max(worst, Math.Abs(r.Length - exact[k]));
            Assert.True(r.Points.All(p => Rho(p) >= rin * Math.Cos(Math.PI / n) - 1e-9), $"pair {k} cuts through the hole"); // rim edges are chords of the circle
        }
        _out.WriteLine($"{around}/{from.Count} pairs run round the hole; max |L − exact| {worst:E2}");
        Assert.True(around >= 25);
        Assert.True(worst < 1e-10, $"max length error {worst:E2}");
    }

    // ---- geodesic laths at contacts: straight, never bent onto the neighbour ----

    [Theory]
    [InlineData(NetLayout.Fill, false)]
    [InlineData(NetLayout.Fill, true)]
    [InlineData(NetLayout.WebBorder, false)]
    [InlineData(NetLayout.WebCross, false)]
    public void FlatAnnulus_GeodesicFamilies_AreStraight_InEveryLayout(NetLayout layout, bool fromBorder)
    {
        // geodesics started on the outer rim run radially and converge towards the hole: those that
        // meet a neighbour end on it; before, the end was blended sideways onto the neighbour over
        // two spacings and the "geodesic" bowed by 0.0147 (10 % of the spacing)
        double rin = 0.4, rout = 1.5, s = 0.15;
        var mesh = FlatAnnulus(rin, rout, 64, 16);
        int seed = Enumerable.Range(0, mesh.VertexCount).First(i => Math.Abs(Rho(mesh.Vertices[i]) - 1.0) < 0.05 && Math.Abs(mesh.Vertices[i].Y) < 0.05 && mesh.Vertices[i].X > 0);
        var opt = new EvenlySpacedNet.Options { Spacing = s, Layout = layout, FromBorder = fromBorder };
        var g = EvenlySpacedNet.TraceGeodesics(mesh, seed, new Vec3d(0, 1, 0), opt);
        var e = Ends(g, rin, rout, 3e-3, s);
        double bow = g.Max(ChordDeviation);
        var mask = Enumerable.Repeat(true, mesh.VertexCount).ToArray();
        var (unc, _, _) = WebCoverageTests.Coverage(mesh, mask, g, s);
        _out.WriteLine($"{layout} fromBorder {fromBorder}: {g.Count} geodesics; ends inner {e.inner} outer {e.outer} on neighbour {e.onCurve} other {e.other}; bow {bow:E2}; in hole {e.inHole}; uncovered {unc:P1}");
        Assert.True(g.Count >= 20);
        Assert.True(bow < 2e-4, $"bow {bow:E2}: a geodesic on a flat surface is a straight line");
        Assert.Equal(0, e.other);
        Assert.Equal(0, e.inHole);
        Assert.True(e.inner > 0);
        Assert.True(unc < 0.01, $"uncovered {unc:P1}");
        if (fromBorder || layout == NetLayout.WebBorder) Assert.True(e.onCurve > 0, "radial geodesics from the rim should end on their neighbours");
    }

    [Theory]
    [InlineData("dome", NetLayout.Fill, true, 0.1, 0.004)]
    [InlineData("dome", NetLayout.WebBorder, false, 0.1, 0.004)]
    [InlineData("catenoid", NetLayout.Fill, false, 0.1, 0.006)]
    [InlineData("catenoid", NetLayout.WebCross, false, 0.1, 0.006)]
    public void GeodesicFamilies_EndStraightOnTheirNeighbours(string shape, NetLayout layout, bool fromBorder, double maxTurn, double meanTurn)
    {
        // in-surface turning per spacing of every traced geodesic: before this rule the sideways blend
        // read up to 0.30 (dome from the rim, mean 0.0134) and 0.26 (catenoid web, mean 0.0068)
        var mesh = AnalyticShapes.Build(shape, 0, 0, 0, 48);
        double s = mesh.BoundingBoxDiagonal() / 25.0;
        var opt = new EvenlySpacedNet.Options { Spacing = s, Layout = layout, FromBorder = fromBorder };
        var g = EvenlySpacedNet.TraceGeodesics(mesh, mesh.VertexCount / 2, new Vec3d(1, 0, 0), opt);
        var q = GeodesicQuality(mesh, g, s, opt.MergeFactor * s);
        _out.WriteLine($"{shape} {layout} fromBorder {fromBorder}: {g.Count} geodesics; in-surface turning × spacing max {q.maxKg:F4} mean {q.meanKg:F5}; ends border {q.border} on neighbour {q.onNeighbour} free {q.free} other {q.other}");
        Assert.True(q.maxKg < maxTurn, $"max in-surface turning {q.maxKg}");
        Assert.True(q.meanKg < meanTurn, $"mean in-surface turning {q.meanKg}");
        Assert.Equal(0, q.other);
        Assert.True(q.onNeighbour > 0, "no T-junction found");
        // T-junctions sit on the neighbour exactly: Net Topology's contact tolerance is 5 % of a segment
        var xs = Mite.Core.Fabrication.NetIntersections.Find(g);
        Assert.True(xs.Count(x => x.IsTJunction) >= q.onNeighbour, $"Net Intersections sees {xs.Count(x => x.IsTJunction)} T-junctions of {q.onNeighbour}");
    }
}
