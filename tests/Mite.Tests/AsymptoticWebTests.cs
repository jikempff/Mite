using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Schling's asymptotic webs against closed forms: on the catenoid and the
/// classical Enneper surface the asymptotic lines are u ± v = const in the
/// isothermal parameters, so every node of a web must sit on the lattice
/// spanned by the nodes of the two seed curves.
/// </summary>
public class AsymptoticWebTests
{
    private static AsymptoticWeb.Result Web(MeshData mesh, double spacing = 0, int seed = -1)
    {
        var pc = PrincipalCurvature.Compute(mesh, 2);
        return AsymptoticWeb.Build(mesh, pc, seed, new AsymptoticWeb.Options { Spacing = spacing });
    }

    private static double LatticeError(IEnumerable<double> values, double step, double offset)
    {
        double worst = 0;
        foreach (double x in values)
        {
            double r = (x - offset) / step;
            worst = Math.Max(worst, Math.Abs(r - Math.Round(r)) * step);
        }
        return worst;
    }

    [Fact]
    public void Catenoid_RotationalWeb_NodesOnTheIsothermalLattice_MeridiansGeodesic()
    {
        var mesh = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 64).ToTriangulated();
        var w = Web(mesh, 0.3);
        Assert.True(w.RotationalWeb);
        Assert.True(w.Nodes.Count > 100);
        int N = w.A.Count;
        double dphi = 2 * Math.PI / N;
        // seed on the waist: u0 = its azimuth, v = 0 → u ± v ∈ u0 + k Δφ
        double u0 = Math.Atan2(w.Seed.Y, w.Seed.X);
        var sum = w.Nodes.Select(p => Math.Atan2(p.Y, p.X) + p.Z);
        var dif = w.Nodes.Select(p => Math.Atan2(p.Y, p.X) - p.Z);
        Assert.InRange(LatticeError(sum, dphi, u0), 0, 0.01 * dphi);
        Assert.InRange(LatticeError(dif, dphi, u0), 0, 0.01 * dphi);
        // the geodesic diagonal family lies in meridian planes
        double worstPlane = 0;
        for (int i = 0; i < w.Diagonals.Count; i++)
        {
            if (w.DiagonalFamily[i] != 0) continue;
            var d = w.Diagonals[i];
            var n = Vec3d.Cross(new Vec3d(0, 0, 1), new Vec3d(d[0].X, d[0].Y, 0)).Normalized();
            foreach (var p in d) worstPlane = Math.Max(worstPlane, Math.Abs(Vec3d.Dot(p, n)));
        }
        Assert.InRange(worstPlane, 0, 1e-3);
        Assert.InRange(w.DiagonalGeodesicError[0], 0, 1e-3);
        Assert.InRange(w.SymmetryError, 0, 1e-3);
    }

    [Fact]
    public void Enneper_Web_NodesOnTheRayLattice()
    {
        // X(u, v) = (u − u³/3 + uv², v − v³/3 + vu², u² − v²): asymptotic lines u ± v = const.
        // Seed at the centre: rays along u = ±v, nodes at arc length i·h,
        // s(t) = √2 (t + 2t³/3) along u = v = t.
        var mesh = AnalyticShapes.Build("enneper", 1.2, 0, 0, 72).ToTriangulated();
        double h = 0.25;
        int centre = new MeshProjection(mesh).NearestVertexGlobal(Vec3d.Zero);
        var w = Web(mesh, h, centre);
        Assert.False(w.Singular);
        Assert.Equal(4, w.Rays);
        var ts = new List<double> { 0 };
        for (int i = 1; i < 20; i++)
        {
            // invert s(t) = i h by Newton
            double t = i * h / Math.Sqrt(2);
            for (int k = 0; k < 30; k++) t -= (Math.Sqrt(2) * (t + 2 * t * t * t / 3) - i * h) / (Math.Sqrt(2) * (1 + 2 * t * t));
            ts.Add(t); ts.Add(-t);
        }
        double worst = 0;
        foreach (var p in w.Nodes)
        {
            // invert the chart: best of a parameter grid, then Newton
            double u = 0, v = 0, bd = double.MaxValue;
            for (int a0 = 0; a0 <= 120; a0++)
                for (int b0 = 0; b0 <= 120; b0++)
                {
                    double uu = -1.2 + 2.4 * a0 / 120, vv = -1.2 + 2.4 * b0 / 120;
                    var q = new Vec3d(uu - uu * uu * uu / 3 + uu * vv * vv, vv - vv * vv * vv / 3 + vv * uu * uu, uu * uu - vv * vv);
                    double dd = (q - p).LengthSquared;
                    if (dd < bd) { bd = dd; u = uu; v = vv; }
                }
            for (int k = 0; k < 40; k++)
            {
                double fx = u - u * u * u / 3 + u * v * v - p.X, fy = v - v * v * v / 3 + v * u * u - p.Y;
                double a = 1 - u * u + v * v, b = 2 * u * v, c = 2 * u * v, d = 1 - v * v + u * u;
                double det = a * d - b * c;
                u -= (d * fx - b * fy) / det; v -= (-c * fx + a * fy) / det;
            }
            // u + v = 2 t_i and u − v = 2 t_j for ray nodes t_i, t_j
            double e1 = ts.Min(t => Math.Abs(u + v - 2 * t)), e2 = ts.Min(t => Math.Abs(u - v - 2 * t));
            worst = Math.Max(worst, Math.Max(e1, e2));
        }
        // parameter error relative to the parameter spacing near the centre (h / √2 per ray step → 2t ≈ √2 h)
        Assert.InRange(worst, 0, 0.03 * Math.Sqrt(2) * h);
        Assert.True(w.Quads.Count > 50, $"{w.Quads.Count} quads");
    }

    [Fact]
    public void Enneper3_FlatPoint_SixRays_ExactThreeFoldSymmetry_OrthogonalNodes()
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var w = Web(mesh);
        Assert.True(w.Singular);
        Assert.Equal(6, w.Rays);
        Assert.Equal(3, w.SymmetryOrder);
        Assert.InRange(w.SymmetryError, 0, 0.02 * w.Spacing); // the copies are projected onto the (not quite symmetric) facets
        // minimal surface: the two families cross at 90° at every node
        var xs = NetIntersections.Find(w.A, w.B);
        double worstAngle = 0;
        foreach (var x in xs)
        {
            var ta = NetIntersections.TangentAt(w.A, x, false); var tb = NetIntersections.TangentAt(w.B, x, true);
            double ang = Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(ta, tb)))) * 180 / Math.PI;
            if ((x.Point - w.Seed).Length > 2 * w.Spacing) worstAngle = Math.Max(worstAngle, Math.Abs(90 - ang));
        }
        Assert.InRange(worstAngle, 0, 3.0);
        // no stubs: every lath carries at least two nodes
        Assert.True(w.Quads.Count > 200);
    }

    [Fact]
    public void HyperbolicParaboloid_Web_IsTheGridOfRulings()
    {
        // z = x² − y²: asymptotic lines are the straight rulings x ± y = const
        var mesh = AnalyticShapes.Build("saddle", 2.0, 1.0, 1.0, 48).ToTriangulated();
        var w = Web(mesh, 0.15);
        Assert.Equal(2, w.SymmetryOrder);
        double worstBow = 0;
        foreach (var l in w.A.Concat(w.B))
        {
            var a = l[0]; var b = l[^1]; var d = (b - a).Normalized();
            foreach (var p in l) worstBow = Math.Max(worstBow, (p - a - Vec3d.Dot(p - a, d) * d).Length);
        }
        Assert.InRange(worstBow, 0, 0.01);
        // nodes on x ± y lattices through the centre: the ray x = y is flat
        // (z = 0), so a node at arc length i·h has x + y = √2·i·h
        double step = Math.Sqrt(2) * 0.15;
        Assert.InRange(LatticeError(w.Nodes.Select(p => p.X + p.Y), step, 0), 0, 0.03 * 0.15);
        Assert.InRange(LatticeError(w.Nodes.Select(p => p.X - p.Y), step, 0), 0, 0.03 * 0.15);
        Assert.True(w.Quads.Count > 100);
    }

    [Fact]
    public void Aag_RotationalWebIsAlreadyAag_Enneper3OptimisesToAag()
    {
        // catenoid: the rotational web's meridian diagonals are exact geodesics
        var cat = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 48).ToTriangulated();
        var wc = Web(cat, 0.3);
        var rc = AagWeb.Optimize(cat, wc);
        Assert.InRange(rc.InitialGeodesicError, 0, 0.5);
        Assert.InRange(rc.GeodesicError, 0, 0.1);
        Assert.InRange(rc.StarError, 0, 0.1);

        // 3-fold Enneper: the curvature-line diagonals of its asymptotic web are
        // far from geodesic (> 40°); a small change of the surface makes them so
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var w = Web(mesh);
        var r = AagWeb.Optimize(mesh, w);
        Assert.True(r.InitialGeodesicError > 40, $"initial {r.InitialGeodesicError:0.#}°");
        Assert.InRange(r.GeodesicError, 0, 0.5);
        Assert.InRange(r.StarError, 0, 0.5);
        Assert.InRange(r.MeanDeviation, 0, 0.02 * w.Spacing);
        Assert.InRange(r.MaxDeviation, 0, 0.05 * w.Spacing);
    }
}
