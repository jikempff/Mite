using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Tri-hex (kagome) AAG webs — the geodesic family through the midpoints of
/// the asymptotic edges (Wan, D'Acunto &amp; Schling 2024; Schling et al. 2022
/// §4.2) — and the constant crossing angle of the AGH gridshell, against
/// exact cases: the rotational web of a surface of revolution, whose mid
/// chains lie in meridian planes, the hyperbolic paraboloid, whose mid chains
/// are plane sections, and the rotational CRPC surface k_m = −k_p / 3, whose
/// asymptotic curves cross at 60° (Wang &amp; Pottmann 2022).
/// </summary>
public class TriHexWebTests
{
    private readonly ITestOutputHelper _out;
    public TriHexWebTests(ITestOutputHelper output) { _out = output; }

    private static AsymptoticWeb.Result Web(MeshData mesh, double spacing = 0, int seed = -1)
    {
        var pc = PrincipalCurvature.Compute(mesh, 2);
        return AsymptoticWeb.Build(mesh, pc, seed, new AsymptoticWeb.Options { Spacing = spacing });
    }

    private static int Segments(AsymptoticWeb.Result w, int family)
    {
        int s = 0;
        for (int i = 0; i < w.MidChains.Count; i++) if (w.MidChainFamily[i] == family) s += w.MidChains[i].Length - 1;
        return s;
    }

    [Fact]
    public void Catenoid_MidChains_AreMidpoints_TwoPerQuadPerFamily_Family0InMeridianPlanes()
    {
        var mesh = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 64).ToTriangulated();
        var w = Web(mesh, 0.3);
        Assert.True(w.RotationalWeb);
        int Q = w.Quads.Count;
        Assert.True(Q > 100);
        // every chain vertex is exactly the midpoint of its lath edge
        double worstMid = 0;
        for (int i = 0; i < w.MidChains.Count; i++)
            for (int k = 0; k < w.MidChains[i].Length; k++)
            {
                var (a, b) = w.MidChainEdges[i][k];
                Assert.True(w.NeighborsA[a].Contains(b) || w.NeighborsB[a].Contains(b), "a mid-chain vertex is not on a lath edge");
                worstMid = Math.Max(worstMid, (w.MidChains[i][k] - 0.5 * (w.Nodes[a] + w.Nodes[b])).Length);
            }
        Assert.Equal(0.0, worstMid);
        // two segments per quad and family: the tri-hex tiling covers every quad once
        Assert.Equal(2 * Q, Segments(w, 0));
        Assert.Equal(2 * Q, Segments(w, 1));
        // family 0 is parallel to the meridian diagonals: 2N chains (the node
        // meridians sit at kδ and (k + ½)δ, the mid chains a quarter step off
        // either), each one near a plane through the axis. Chord midpoints are
        // not in that plane: on quads that are not parallelograms they
        // alternate sides by O(spacing²), which is why the tri-hex web needs
        // the optimisation below rather than the chord midpoints themselves.
        int N = w.A.Count;
        double dphi = 2 * Math.PI / N;
        double worstPlane = 0, worstQuarter = 0;
        int chains0 = 0;
        double u0 = Math.Atan2(w.Seed.Y, w.Seed.X);
        for (int i = 0; i < w.MidChains.Count; i++)
        {
            if (w.MidChainFamily[i] != 0) continue;
            chains0++;
            var d = w.MidChains[i];
            // best plane through the axis: mean azimuth of the chain
            double sx = d.Sum(p => Math.Cos(Math.Atan2(p.Y, p.X))), sy = d.Sum(p => Math.Sin(Math.Atan2(p.Y, p.X)));
            double phi = Math.Atan2(sy, sx);
            var n = new Vec3d(-Math.Sin(phi), Math.Cos(phi), 0);
            foreach (var p in d) worstPlane = Math.Max(worstPlane, Math.Abs(Vec3d.Dot(p, n)));
            double r = (phi - u0) / dphi * 2 - 0.5;   // quarter steps: r ∈ ℤ for (k + ¼)δ and (k + ¾)δ
            worstQuarter = Math.Max(worstQuarter, Math.Abs(r - Math.Round(r)) * dphi / 2);
        }
        _out.WriteLine($"catenoid: {Q} quads, {w.MidChains.Count} mid chains ({chains0} meridian-like), distance from the meridian plane {worstPlane:E2} (spacing {w.Spacing}), quarter-step error {worstQuarter:E2}, chord-midpoint mid geodesic error {w.MidGeodesicError[0]:F4} / {w.MidGeodesicError[1]:F4}, node diagonals {w.DiagonalGeodesicError[0]:E2} / {w.DiagonalGeodesicError[1]:F4}");
        Assert.Equal(2 * N, chains0);
        Assert.InRange(worstQuarter, 0, 0.1 * dphi);
        Assert.InRange(worstPlane, 0, 0.1 * w.Spacing);
        Assert.True(worstPlane > 1e-4, "chord midpoints do zigzag about the plane (O(spacing²)) — if this ever reads 0 the measure changed");
    }

    [Fact]
    public void HyperbolicParaboloid_MidChains_ArePlaneSections()
    {
        // z = x² − y², web = the rulings x ± y = const: the mid chains of one
        // family lie in planes y = const, the other in x = const (lattice
        // coordinates u = x + y, v = x − y: chains are v − u = const + ½ step)
        var mesh = AnalyticShapes.Build("saddle", 2.0, 1.0, 1.0, 48).ToTriangulated();
        var w = Web(mesh, 0.15);
        int Q = w.Quads.Count;
        Assert.Equal(2 * Q, Segments(w, 0));
        Assert.Equal(2 * Q, Segments(w, 1));
        double worst = 0; int[] constY = new int[2], constX = new int[2];
        for (int i = 0; i < w.MidChains.Count; i++)
        {
            var d = w.MidChains[i];
            if (d.Length < 3) continue;
            double sy = d.Max(p => p.Y) - d.Min(p => p.Y), sx = d.Max(p => p.X) - d.Min(p => p.X);
            if (sy < sx) constY[w.MidChainFamily[i]]++; else constX[w.MidChainFamily[i]]++;
            worst = Math.Max(worst, Math.Min(sx, sy));
        }
        _out.WriteLine($"saddle: {Q} quads, {w.MidChains.Count} mid chains, plane error {worst:E2}, family 0: {constY[0]} y-planes / {constX[0]} x-planes, family 1: {constY[1]} / {constX[1]}");
        Assert.InRange(worst, 0, 0.01);
        // one family per plane direction
        Assert.True((constY[0] == 0 && constX[1] == 0) || (constX[0] == 0 && constY[1] == 0));
    }

    [Fact]
    public void TriHexAag_Catenoid_MidpointsMoveOntoTheHalfStepMeridians_Enneper3Optimises()
    {
        // The rotational web: the exact tri-hex G curves are the meridian planes a
        // quarter step between the node meridians. The chord midpoints alternate
        // sides of that plane (O(spacing²)), so the polyline through them is far
        // from a discrete geodesic; the optimiser moves every midpoint vertex
        // within the surface onto the plane and leaves the nodes where they are.
        var cat = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 48).ToTriangulated();
        var wc = Web(cat, 0.3);
        var rc = AagWeb.Optimize(cat, wc, new AagWeb.Options { Midpoint = true, Iterations = 24 });
        double worstPlane = 0;
        foreach (var g in rc.G)
        {
            double sx = g.Sum(p => Math.Cos(Math.Atan2(p.Y, p.X))), sy = g.Sum(p => Math.Sin(Math.Atan2(p.Y, p.X)));
            double phi = Math.Atan2(sy, sx);
            var n = new Vec3d(-Math.Sin(phi), Math.Cos(phi), 0);
            foreach (var p in g) worstPlane = Math.Max(worstPlane, Math.Abs(Vec3d.Dot(p, n)));
        }
        _out.WriteLine($"catenoid tri-hex: geodesic {rc.InitialGeodesicError:F2}° → {rc.GeodesicError:F4}°, stars {rc.InitialStarError:F3}° → {rc.StarError:F4}°, asymptotic at the midpoints {rc.InitialAsymptoticError:F3}° → {rc.AsymptoticError:F4}°, nodes off the surface {rc.MeanDeviation:E2} mean / {rc.MaxDeviation:E2} max, nodes moved {rc.MeanNodeMove:E2} / {rc.MaxNodeMove:E2}, midpoints shifted {rc.MeanMidpointShift:F4} mean / {rc.MaxMidpointShift:F4} max of the spacing, G off the meridian planes {worstPlane:E2}, angles {rc.AngleMin:F2}–{rc.AngleMax:F2} (mean {rc.AngleMean:F2}), {rc.G.Count} G chains, {rc.Midpoints.Length} midpoints, {rc.Iterations} it");
        Assert.True(rc.Midpoint);
        Assert.True(rc.InitialGeodesicError > 10, $"chord midpoints read as geodesic: {rc.InitialGeodesicError}°");
        Assert.InRange(rc.GeodesicError, 0, 0.2);
        Assert.InRange(rc.StarError, 0, 0.2);
        Assert.InRange(rc.AsymptoticError, 0, 0.2);
        Assert.InRange(rc.MeanDeviation, 0, 0.01 * wc.Spacing);
        Assert.InRange(rc.MaxNodeMove, 0, 0.1 * wc.Spacing);
        Assert.InRange(rc.MaxMidpointShift, 0, 0.1);
        Assert.InRange(worstPlane, 0, 0.02 * wc.Spacing);
        // the refined laths alternate node, midpoint, node
        Assert.All(rc.A.Concat(rc.B), l => Assert.True(l.Length % 2 == 1 && l.Length >= 3));
        Assert.Equal(wc.MidChainFamily.Count(f => f == 0), rc.G.Count);
        Assert.InRange(Math.Abs(90 - rc.AngleMean), 0, 1.5);

        // 3-fold Enneper (singular seed, six rays): the chord-midpoint chains are
        // 76° from geodesic; the tri-hex optimisation reaches the same accuracy
        // as the node-to-node AAG web with the same small change of the surface
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var w = Web(mesh);
        var r = AagWeb.Optimize(mesh, w, new AagWeb.Options { Midpoint = true });
        _out.WriteLine($"enneper3 tri-hex: geodesic {r.InitialGeodesicError:F2}° → {r.GeodesicError:F4}°, stars {r.InitialStarError:F3}° → {r.StarError:F4}°, asymptotic {r.InitialAsymptoticError:F3}° → {r.AsymptoticError:F4}°, moved {r.MeanDeviation:E2} mean / {r.MaxDeviation:E2} max, midpoints shifted {r.MeanMidpointShift:F4} mean / {r.MaxMidpointShift:F4} max, {r.Midpoints.Length} midpoints, {r.G.Count} G chains, {r.Iterations} it");
        Assert.True(r.InitialGeodesicError > 40, $"initial {r.InitialGeodesicError}");
        Assert.InRange(r.GeodesicError, 0, 0.5);
        Assert.InRange(r.StarError, 0, 0.5);
        Assert.InRange(r.AsymptoticError, 0, 0.5);
        Assert.InRange(r.MeanDeviation, 0, 0.02 * w.Spacing);
        Assert.InRange(r.MaxDeviation, 0, 0.05 * w.Spacing);
        Assert.InRange(r.MaxMidpointShift, 0, 0.25);
    }

    [Fact]
    public void TriHexAag_ClassicalEnneper_ConvergesWhereTheNodeWebDoesNot()
    {
        // The classical Enneper surface carries no node-to-node AAG web near it
        // (its curvature-line diagonals cannot all become geodesic at the mirror
        // planes — roadmap finding, 1.3.0). The tri-hex web has the midpoint
        // vertices as extra freedom and gets there.
        var mesh = AnalyticShapes.Build("enneper", 1.2, 0, 0, 72).ToTriangulated();
        int centre = new MeshProjection(mesh).NearestVertexGlobal(Vec3d.Zero);
        var w = Web(mesh, 0.25, centre);
        var tri = AagWeb.Optimize(mesh, w, new AagWeb.Options { Midpoint = true, Iterations = 48 });
        var node = AagWeb.Optimize(mesh, w, new AagWeb.Options { Iterations = 48 });
        _out.WriteLine($"enneper tri-hex: geodesic {tri.InitialGeodesicError:F2}° → {tri.GeodesicError:F4}°, stars {tri.StarError:F4}°, asymptotic {tri.AsymptoticError:F4}°, moved {tri.MeanDeviation:E2} / {tri.MaxDeviation:E2}, nodes moved {tri.MeanNodeMove:E2} / {tri.MaxNodeMove:E2}, midpoints shifted {tri.MeanMidpointShift:F3} / {tri.MaxMidpointShift:F3}, angles {tri.InitialAngleMin:F1}–{tri.InitialAngleMax:F1} → {tri.AngleMin:F1}–{tri.AngleMax:F1}, {tri.Iterations} it; node-to-node: {node.InitialGeodesicError:F2}° → {node.GeodesicError:F2}°, stars {node.StarError:F2}°, moved {node.MeanDeviation:E2} / {node.MaxDeviation:E2}");
        Assert.InRange(tri.GeodesicError, 0, 1.0);
        Assert.InRange(tri.StarError, 0, 0.5);
        Assert.InRange(tri.AsymptoticError, 0, 0.5);
        Assert.InRange(tri.MeanDeviation, 0, 0.01 * w.Spacing);
        Assert.True(node.GeodesicError > 10, $"the node web converged after all: {node.GeodesicError}° — update the roadmap");
    }

    [Fact]
    public void Crpc_Profile_CatenoidForA1_ClosedFormForAThird_AsymptoticCurvesCrossAt60()
    {
        // a = 1: r = cosh z
        var prof1 = AnalyticShapes.CrpcProfile(1.0, 1.2, 4000);
        double worst1 = 0;
        for (int k = 0; k <= 4000; k += 400) worst1 = Math.Max(worst1, Math.Abs(prof1[k] - Math.Cosh(1.2 * k / 4000)));
        // a = 1/3: 1 + r'² = r^(2/3), r = t³, z = (3/2)(t √(t² − 1) + arcosh t)
        var prof3 = AnalyticShapes.CrpcProfile(1.0 / 3.0, 1.2, 4000);
        double worst3 = 0;
        for (int k = 0; k <= 4000; k += 400)
        {
            double z = 1.2 * k / 4000, t = Math.Pow(prof3[k], 1.0 / 3.0);
            double zc = 1.5 * (t * Math.Sqrt(Math.Max(0, t * t - 1)) + Math.Log(t + Math.Sqrt(Math.Max(0, t * t - 1))));
            worst3 = Math.Max(worst3, Math.Abs(zc - z));
        }
        _out.WriteLine($"profile: catenoid error {worst1:E2}, a = 1/3 closed-form error {worst3:E2}, r(1.2) = {prof3[4000]:F5}");
        Assert.InRange(worst1, 0, 1e-9);
        Assert.InRange(worst3, 0, 1e-7);

        // the mesh: principal curvature ratio −1/3 and 60° between the asymptotic curves of its web
        var mesh = AnalyticShapes.Build("crpc", 1.0 / 3.0, 1.2, 0, 64).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var proj = new MeshProjection(mesh);
        double worstRatio = 0; int cnt = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Vertices[i];
            if (Math.Abs(p.Z) > 0.9) continue;
            double ratio = pc.K1[i] / pc.K2[i];
            if (ratio > 0) ratio = 1 / ratio;
            worstRatio = Math.Max(worstRatio, Math.Abs(Math.Min(Math.Abs(ratio), 1 / Math.Abs(ratio)) - 1.0 / 3.0)); cnt++;
        }
        var w = Web(mesh, 0.3);
        Assert.True(w.RotationalWeb);
        var r = AagWeb.Optimize(mesh, w, new AagWeb.Options { Iterations = 1 });
        _out.WriteLine($"crpc: |k1/k2| within {worstRatio:F4} of 1/3 on {cnt} vertices; web {w.A.Count} + {w.B.Count} laths, {w.Nodes.Count} nodes; crossing angles {r.InitialAngleMin:F2}–{r.InitialAngleMax:F2} (mean {r.InitialAngleMean:F2}); node diagonals geodesic {w.DiagonalGeodesicError[0]:E2}");
        Assert.InRange(worstRatio, 0, 0.02);
        Assert.InRange(Math.Abs(60 - r.InitialAngleMean), 0, 1.0);
        Assert.InRange(r.InitialAngleMax - r.InitialAngleMin, 0, 3.0);
        Assert.InRange(w.DiagonalGeodesicError[0], 0, 1e-3);
    }

    [Fact]
    public void CrossingAngle_CrpcWebAlreadyAt60_CatenoidWebDrivenTo60()
    {
        // on the CRPC surface the 60° constraint is already satisfied: nothing moves
        var crpc = AnalyticShapes.Build("crpc", 1.0 / 3.0, 1.2, 0, 48).ToTriangulated();
        var wc = Web(crpc, 0.3);
        var rc = AagWeb.Optimize(crpc, wc, new AagWeb.Options { Midpoint = true, CrossingAngle = 60 });
        _out.WriteLine($"crpc @60: angles {rc.InitialAngleMin:F2}–{rc.InitialAngleMax:F2} → {rc.AngleMin:F3}–{rc.AngleMax:F3} (mean {rc.AngleMean:F3}), geodesic {rc.InitialGeodesicError:F2}° → {rc.GeodesicError:F4}°, stars {rc.StarError:F4}°, asymptotic {rc.AsymptoticError:F4}°, moved {rc.MeanDeviation:E2} / {rc.MaxDeviation:E2}, midpoints shifted {rc.MaxMidpointShift:F4}, {rc.Iterations} it");
        Assert.InRange(Math.Abs(60 - rc.AngleMean), 0, 0.5);
        Assert.InRange(rc.AngleMax - rc.AngleMin, 0, 2.0);
        Assert.InRange(rc.GeodesicError, 0, 0.2);
        Assert.InRange(rc.StarError, 0, 0.2);
        Assert.InRange(rc.MeanDeviation, 0, 0.02 * wc.Spacing);

        // the catenoid web crosses at 90°; asking for 60° must reshape the
        // surface (a minimal surface has no 60° asymptotic net)
        var cat = AnalyticShapes.Build("catenoid", 1.0, 1.2, 0, 48).ToTriangulated();
        var w = Web(cat, 0.3);
        foreach (double prox in new[] { 0.3, 0.05 })
        {
            var r = AagWeb.Optimize(cat, w, new AagWeb.Options { Midpoint = true, CrossingAngle = 60, Proximity = prox, Iterations = 40 });
            _out.WriteLine($"catenoid @60, proximity {prox}: angles {r.InitialAngleMean:F2} → {r.AngleMin:F2}–{r.AngleMax:F2} (mean {r.AngleMean:F2}), geodesic {r.GeodesicError:F3}°, stars {r.StarError:F3}°, asymptotic {r.AsymptoticError:F3}°, off the surface {r.MeanDeviation:E2} / {r.MaxDeviation:E2}, nodes moved {r.MeanNodeMove:E2} / {r.MaxNodeMove:E2}, {r.Iterations} it");
        }
    }
}
