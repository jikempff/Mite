using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// AGG webs (Schling, Wang, Hoyer &amp; Pottmann 2022, §2.3) against the one
/// case with exact ground truth: the cylinder web — three families of
/// parallel lines in the development, the rulings as the asymptotic family
/// (Fig. 9 of the paper; Graf &amp; Sauer 1924) — which satisfies Sauer's
/// equal-opposite-angle condition and the asymptotic diagonal condition to
/// rounding because every equally spaced helix is mapped onto itself by the
/// half-turn about the cylinder normal at any of its nodes. Then the
/// optimiser against the same truth (a perturbed web must come back to it),
/// against a cone (the asymptotic family must come out as the generators
/// through the apex) and on a saddle where no exact web is known.
/// </summary>
public class AggWebTests
{
    private readonly ITestOutputHelper _out;
    public AggWebTests(ITestOutputHelper output) { _out = output; }

    private static MeshData CylinderMesh(double radius = 1.0, double height = 3.0, int segments = 256, int rows = 48)
        => TestMeshes.CreateCylinder(radius, height, segments, rows);

    private static double Radius(Vec3d p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);

    private static string Fmt(AggWeb.Quality q) =>
        $"G {q.GeodesicError:G4}° (mean {q.MeanGeodesicError:G3}) · bisectors {q.BisectorError:G4}° · A {q.AsymptoticError:G4}° (mean {q.MeanAsymptoticError:G3}, {q.AsymptoticEdges} edges) · " +
        $"vs surface: normal {q.SurfaceNormalError:G4}°, asym {q.SurfaceAsymptoticError:G4}° · deviation {q.MaxDeviation:G3} max / {q.MeanDeviation:G3} mean";

    [Theory]
    [InlineData(50.0, 50.0)]
    [InlineData(40.0, 65.0)]
    public void CylinderWeb_IsAnExactAggWeb(double angleA, double angleB)
    {
        int nu = 12, nv = 10;
        var net = AggWeb.QuadNet.Cylinder(1.0, angleA, 0.15, nu, nv, angleB);
        Assert.Equal(nu * nv, net.Nodes.Count);
        Assert.Equal((nu - 1) * (nv - 1), net.Quads.Count);
        Assert.Equal((nu - 2) * (nv - 2), net.RegularNodes);
        Assert.Equal(0, net.LabelConflicts);
        Assert.Equal(nu + nv - 3, net.DiagonalFamily.Count(f => f == 0));
        Assert.Equal(nu + nv - 3, net.DiagonalFamily.Count(f => f == 1));

        // every node on the cylinder, A edges of the given length and angle
        foreach (var p in net.Nodes) Assert.InRange(Radius(p), 1 - 1e-12, 1 + 1e-12);
        foreach (var ch in net.Chains(true))
            for (int k = 0; k + 1 < ch.Length; k++)
            {
                var e = net.Nodes[ch[k + 1]] - net.Nodes[ch[k]];
                double chord = 2 * Math.Sin(0.5 * 0.15 * Math.Sin(angleA * Math.PI / 180));   // chord of the arc step on the unit circle
                Assert.InRange(e.Length, Math.Sqrt(chord * chord + Math.Pow(0.15 * Math.Cos(angleA * Math.PI / 180), 2)) - 1e-12, Math.Sqrt(chord * chord + Math.Pow(0.15 * Math.Cos(angleA * Math.PI / 180), 2)) + 1e-12);
            }

        int rulings = net.RulingFamily;
        var q = AggWeb.Measure(net, rulings);
        _out.WriteLine($"cylinder {angleA}/{angleB}: family {rulings} · {Fmt(q)}");
        Assert.InRange(q.GeodesicError, 0, 1e-9);
        Assert.InRange(q.BisectorError, 0, 1e-9);
        Assert.InRange(q.AsymptoticError, 0, 1e-9);
        Assert.True(q.AsymptoticEdges > 0);

        // the asymptotic diagonals are the rulings (x, y constant), the others circles (z constant)
        foreach (var line in net.DiagonalPolylines(rulings))
            foreach (var p in line) { Assert.InRange(p.X - line[0].X, -1e-12, 1e-12); Assert.InRange(p.Y - line[0].Y, -1e-12, 1e-12); }
        foreach (var line in net.DiagonalPolylines(1 - rulings))
            for (int k = 1; k < line.Length; k++)
            {
                // the other diagonal: circles for a symmetric web (z constant), helices of a constant z step otherwise
                double dz = line[k].Z - line[k - 1].Z;
                if (angleA == angleB) Assert.InRange(dz, -1e-12, 1e-12);
                else Assert.InRange(dz - (line[1].Z - line[0].Z), -1e-12, 1e-12);
            }

        // the other diagonal family is not asymptotic: circles leave the tangent plane along the rulings? no — they lie in it;
        // but as diagonals of helices crossing at unequal angles they are not in the plane of the two bisectors only when the web is asymmetric
        var other = AggWeb.Measure(net, 1 - rulings);
        _out.WriteLine($"  other family as 'asymptotic': A {other.AsymptoticError:G4}°");
    }

    [Fact]
    public void CylinderWeb_FamilyChoice_AndMeasureAgainstTheMesh()
    {
        var mesh = CylinderMesh();
        var net = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 12, 10);
        var proj = new MeshProjection(mesh);
        Assert.Equal(net.RulingFamily, AggWeb.ChooseFamily(net, proj));
        var q = AggWeb.Measure(net, -1, mesh);
        _out.WriteLine($"against the 256-gon cylinder: {Fmt(q)}");
        Assert.InRange(q.SurfaceAsymptoticError, 0, 0.05);
        Assert.InRange(q.SurfaceNormalError, 0, 0.5);
        Assert.InRange(q.MaxDeviation, 0, 1e-4);
    }

    [Fact]
    public void CylinderWeb_StaysExact_WhenOptimisedOnTheCylinder()
    {
        var mesh = CylinderMesh();
        var net = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 12, 10);
        var r = AggWeb.Optimize(mesh, net, new AggWeb.Options { Iterations = 20 });
        _out.WriteLine($"start   {Fmt(r.Initial)}");
        _out.WriteLine($"result  {Fmt(r.Final)} · moved {r.MaxMove:G3} · {r.Iterations} it · cost {r.FinalCost:G3}");
        Assert.Equal(net.RulingFamily, r.AsymptoticFamily);
        Assert.InRange(r.Final.GeodesicError, 0, 0.01);
        Assert.InRange(r.Final.AsymptoticError, 0, 0.01);
        Assert.InRange(r.MaxMove, 0, 2e-4);
    }

    [Fact]
    public void PerturbedCylinderWeb_ComesBack()
    {
        var mesh = CylinderMesh();
        var exact = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 12, 10);
        // every interior node moved by up to a tenth of the spacing in a random direction; the
        // border nodes stay (they are held where the user put them, so a perturbed border
        // would be a different web)
        var rnd = new Random(7);
        var nodes = exact.Nodes.Select((p, i) => exact.NeighborsA[i].Length + exact.NeighborsB[i].Length < 4 ? p
            : p + 0.1 * 0.15 * new Vec3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1)).ToList();
        var net = AggWeb.QuadNet.FromNeighbors(nodes, exact.NeighborsA, exact.NeighborsB);
        var r = AggWeb.Optimize(mesh, net, new AggWeb.Options { Iterations = 40 });
        _out.WriteLine($"perturbed {Fmt(r.Initial)}");
        _out.WriteLine($"result    {Fmt(r.Final)} · moved {r.MaxMove:G3} · {r.Iterations} it · cost {r.FinalCost:G3}");
        Assert.True(r.Initial.GeodesicError > 2, "the perturbation should be visible");
        Assert.InRange(r.Final.GeodesicError, 0, 1e-4);
        Assert.InRange(r.Final.AsymptoticError, 0, 1e-4);
        foreach (var p in r.Nodes) Assert.InRange(Radius(p), 1 - 1e-4, 1 + 1e-4);
        // the asymptotic diagonals are rulings again
        double worst = 0;
        foreach (var line in r.Asymptotic)
            for (int k = 0; k + 1 < line.Length; k++)
            {
                var e = (line[k + 1] - line[k]).Normalized();
                worst = Math.Max(worst, Math.Acos(Math.Min(1, Math.Abs(e.Z))) * 180 / Math.PI);
            }
        _out.WriteLine($"asymptotic edges within {worst:G3}° of the axis");
        Assert.InRange(worst, 0, 0.01);
    }

    [Fact]
    public void CylinderWeb_OntoACone_AsymptoticFamilyBecomesTheGenerators()
    {
        // frustum r = 1 + 0.25 z, apex at z = −4
        var cone = AnalyticShapes.Revolution(z => 1 + 0.25 * z, z => z, -1.6, 1.6, 64, 256, false, false).ToTriangulated();
        var net = AggWeb.QuadNet.Cylinder(1.0, 50, 0.16, 14, 12);
        // the web starts 0.3 away from the cone, so the family is given (the automatic choice
        // reads the reference normals at the start and would take the circles there)
        var r = AggWeb.Optimize(cone, net, new AggWeb.Options { Iterations = 60, AsymptoticFamily = net.RulingFamily, BorderHold = 0.01 });
        _out.WriteLine($"start   {Fmt(r.Initial)}");
        _out.WriteLine($"result  {Fmt(r.Final)} · moved {r.MaxMove:G3} · {r.Iterations} it · cost {r.FinalCost:G3}");
        var apex = new Vec3d(0, 0, -4);
        double worstDir = 0, sumDir = 0; int cnt = 0;
        foreach (var line in r.Asymptotic)
            for (int k = 0; k + 1 < line.Length; k++)
            {
                var e = (line[k + 1] - line[k]).Normalized();
                var g = (line[k] - apex).Normalized();
                double a = Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(e, g)))) * 180 / Math.PI;
                worstDir = Math.Max(worstDir, a); sumDir += a; cnt++;
            }
        _out.WriteLine($"asymptotic edges {sumDir / cnt:G3}° (mean) / {worstDir:G3}° (worst) from the generator through the apex");
        // the web is carried onto the cone (0.3 → 3e-4) and is an AGG web there to a few hundredths
        // of a degree; its asymptotic edges follow the generators only to a few degrees, because on a
        // developable surface the asymptotic direction is a double root — a chord ψ off the generator
        // leaves the tangent plane by only ½ κ₁ h sin²ψ (0.03° at ψ = 4° here), which is what the
        // condition can see
        Assert.True(r.Initial.MaxDeviation > 0.25 && r.Final.MaxDeviation < 0.003, $"deviation {r.Initial.MaxDeviation} → {r.Final.MaxDeviation}");
        Assert.InRange(r.Final.GeodesicError, 0, 0.1);
        Assert.InRange(r.Final.AsymptoticError, 0, 0.05);
        Assert.InRange(r.Final.SurfaceAsymptoticError, 0, 0.1);
        Assert.InRange(sumDir / cnt, 0, 5.0);
    }

    [Fact]
    public void SaddleLattice_OptimisesTowardsAnAggWeb()
    {
        var mesh = AnalyticShapes.Grid((x, y) => new Vec3d(x, y, x * y), -2.4, 2.4, -2.4, 2.4, 120, 120).ToTriangulated();
        double s = 0.2 / Math.Sqrt(2);
        var net = AggWeb.QuadNet.FromLattice((i, j) => { double x = (i + j - 14) * s, y = (i - j) * s; return new Vec3d(x, y, x * y); }, 15, 15);
        var r = AggWeb.Optimize(mesh, net, new AggWeb.Options { Iterations = 60 });
        _out.WriteLine($"start   {Fmt(r.Initial)}");
        _out.WriteLine($"result  {Fmt(r.Final)} · moved {r.MaxMove:G3} · {r.Iterations} it · cost {r.FinalCost:G3} · family {r.AsymptoticFamily}");
        // no closed-form AGG web is known on z = xy: the ±45° parabolas are not geodesic (only the
        // two through the origin are, by mirror symmetry); the optimiser trades a small change of
        // the surface for the web conditions and the test records how much
        Assert.True(r.Initial.GeodesicError > 0.5);
        Assert.InRange(r.Initial.SurfaceAsymptoticError, 0, 1.0);   // both diagonal families are the straight rulings x = const, y = const
        Assert.True(r.Final.GeodesicError < 0.1 * r.Initial.GeodesicError, $"G {r.Initial.GeodesicError} → {r.Final.GeodesicError}");
        Assert.InRange(r.Final.GeodesicError, 0, 0.5);
        Assert.InRange(r.Final.AsymptoticError, 0, 1.0);
        Assert.InRange(r.Final.MeanAsymptoticError, 0, 0.15);
        Assert.InRange(r.Final.MaxDeviation, 0, 0.02);
    }

    [Fact]
    public void FromTopology_RebuildsTheCylinderWeb()
    {
        var exact = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 12, 10);
        var a = exact.Chains(true).Select(ch => ch.Select(i => exact.Nodes[i]).ToArray()).ToList();
        var b = exact.Chains(false).Select(ch => ch.Select(i => exact.Nodes[i]).ToArray()).ToList();
        var crossings = NetIntersections.Find(a, b);
        var topo = NetTopology.Build(a, b, crossings);
        var net = AggWeb.QuadNet.FromTopology(topo);
        Assert.Equal(exact.Nodes.Count, net.Nodes.Count);
        Assert.Equal(exact.Quads.Count, net.Quads.Count);
        Assert.Equal(exact.RegularNodes, net.RegularNodes);
        Assert.Equal(0, net.LabelConflicts);
        var q = AggWeb.Measure(net, net.RulingFamily);
        _out.WriteLine($"from topology: {Fmt(q)}");
        Assert.InRange(q.GeodesicError, 0, 1e-6);
        Assert.InRange(q.AsymptoticError, 0, 1e-6);
    }

    [Fact]
    public void TheCircleFamily_CannotBeMadeAsymptotic_AndIsNotReportedSo()
    {
        var mesh = CylinderMesh();
        var net = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 12, 10);
        var r = AggWeb.Optimize(mesh, net, new AggWeb.Options { Iterations = 20, AsymptoticFamily = 1 - net.RulingFamily });
        _out.WriteLine($"circles forced: start {Fmt(r.Initial)}");
        _out.WriteLine($"               result {Fmt(r.Final)} · moved {r.MaxMove:G3}");
        Assert.False(r.Final.AsymptoticError < 0.5 && r.Final.MaxDeviation < 0.01, "a circle of the cylinder is a curvature line, not an asymptotic curve: the web cannot stay on the cylinder and satisfy it");
    }
}
