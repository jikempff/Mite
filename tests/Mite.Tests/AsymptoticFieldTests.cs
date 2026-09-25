using System;
using System.Linq;
using Xunit;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>Combing and usable-angle mask of the asymptotic direction field.</summary>
public class AsymptoticFieldTests
{
    private static double LabelSwapFraction(MeshData mesh, AsymptoticCurves.DirectionField f)
    {
        var nbrs = mesh.BuildVertexNeighbors();
        int edges = 0, swaps = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            if (!f.Exists[v]) continue;
            foreach (int n in nbrs[v])
            {
                if (n <= v || !f.Exists[n]) continue;
                edges++;
                // family 1 at v should be closer to family 1 at n than to family 2 at n
                double same = Math.Abs(Vec3d.Dot(f.Family1[v], f.Family1[n]));
                double other = Math.Abs(Vec3d.Dot(f.Family1[v], f.Family2[n]));
                if (other > same) swaps++;
            }
        }
        return edges == 0 ? 0 : (double)swaps / edges;
    }

    [Fact]
    public void Combing_MakesFamilyLabelsConsistent_OnTheWave()
    {
        var wave = AnalyticShapes.Build("wave", 2.0, 0.3, 2.0, 40);
        var pc = PrincipalCurvature.Compute(wave);
        var raw = AsymptoticCurves.ComputeDirections(pc);
        var combed = AsymptoticCurves.ComputeDirections(pc, wave, 15.0);
        double rawSwaps = LabelSwapFraction(wave, raw);
        double combedSwaps = LabelSwapFraction(wave, combed);
        Assert.True(combedSwaps < 0.03, $"combed field still swaps labels on {combedSwaps:P1} of edges (raw {rawSwaps:P1})");
        Assert.True(combedSwaps <= rawSwaps, "combing must not increase swaps");
    }

    [Fact]
    public void Combing_IsExactOnTheSaddle()
    {
        var saddle = TestMeshes.CreateSaddle(30, 2.0);
        var pc = PrincipalCurvature.Compute(saddle);
        var f = AsymptoticCurves.ComputeDirections(pc, saddle, 0.0);
        Assert.Equal(0.0, LabelSwapFraction(saddle, f), 3);
        // on z = x² − y² the families are the lines x = ±y: family 1 must be one of them everywhere
        var d = f.Family1.Where((v, i) => f.Exists[i]).Select(v => Math.Abs(Math.Abs(v.X) - Math.Abs(v.Y))).ToArray();
        Assert.True(d.Max() < 0.15, $"family direction deviates from x = ±y by {d.Max():F3}");
        var sx = f.Family1.Where((v, i) => f.Exists[i]).Select(v => Math.Sign(v.X * v.Y)).Distinct().ToArray();
        Assert.Single(sx); // all of family 1 is the same diagonal
    }

    [Fact]
    public void MinCrossingAngle_ExcludesTheNearParabolicBand()
    {
        var wave = AnalyticShapes.Build("wave", 2.0, 0.3, 2.0, 40);
        var pc = PrincipalCurvature.Compute(wave);
        var raw = AsymptoticCurves.ComputeDirections(pc, wave, 0.0);
        var masked = AsymptoticCurves.ComputeDirections(pc, wave, 20.0);
        int nRaw = raw.Exists.Count(e => e), nMasked = masked.Exists.Count(e => e);
        Assert.True(nMasked < nRaw, "the mask must remove vertices");
        Assert.True(nMasked > 0.3 * nRaw, $"the mask removed too much ({nMasked} of {nRaw})");
        for (int i = 0; i < masked.Exists.Length; i++)
        {
            if (!masked.Exists[i]) continue;
            double ang = Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(masked.Family1[i], masked.Family2[i])))) * 180 / Math.PI;
            Assert.True(ang >= 20.0 - 1e-6, $"vertex {i} kept with crossing angle {ang:F1}°");
        }
    }

    [Fact]
    public void AsymptoticNetOnTheWave_HasRealCrossings()
    {
        var wave = AnalyticShapes.Build("wave", 2.0, 0.3, 2.0, 40);
        var pc = PrincipalCurvature.Compute(wave);
        var f = AsymptoticCurves.ComputeDirections(pc, wave, 15.0);
        var a = EvenlySpacedNet.TraceField(wave, f.Family1, f.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.12 }, f.Family2);
        var b = EvenlySpacedNet.TraceField(wave, f.Family2, f.Exists, -1, new EvenlySpacedNet.Options { Spacing = 0.12 }, f.Family1);
        var xs = Mite.Core.Fabrication.NetIntersections.Find(a, b);
        Assert.True(a.Count > 3 && b.Count > 3, $"{a.Count} + {b.Count} curves");
        // before the fix the two 'families' ran side by side: 1 crossing for 58 curves
        Assert.True(xs.Count > a.Count + b.Count, $"only {xs.Count} crossings for {a.Count + b.Count} curves");
    }
}
