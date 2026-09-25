using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Geodesic families: Jacobi-field start angles (Pottmann et al. 2010,
/// "Geodesic patterns") keep strips near constant width where parallel
/// seeding lets them converge or diverge; border seeding on a vault gives a
/// perfectly even helix pattern.
/// </summary>
public class GeodesicPatternTests
{
    private static (double cv, int merges, int curves) Measure(MeshData mesh, List<Vec3d[]> g, double spacing)
    {
        var proj = new MeshProjection(mesh);
        var widths = new List<double>();
        double Dist(Vec3d p, Vec3d[] self)
        {
            double best = double.MaxValue;
            foreach (var m in g)
            {
                if (ReferenceEquals(m, self)) continue;
                for (int k = 0; k + 1 < m.Length; k++)
                {
                    var ab = m[k + 1] - m[k];
                    double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - m[k], ab) / Math.Max(ab.LengthSquared, 1e-30)));
                    best = Math.Min(best, (m[k] + t * ab - p).Length);
                }
            }
            return best;
        }
        foreach (var c in g)
            for (int i = 0; i < c.Length; i += 4)
            {
                double d = Dist(c[i], c);
                if (d < 3 * spacing) widths.Add(d);
            }
        double mean = widths.Average();
        double sd = Math.Sqrt(widths.Average(w => (w - mean) * (w - mean)));
        int merges = 0;
        foreach (var c in g)
            foreach (var p in new[] { c[0], c[^1] })
            {
                var h = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                if (proj.IsOnBoundary(h, 1e-4)) continue;
                if (Dist(p, c) < 1e-6) merges++;
            }
        return (sd / mean, merges, g.Count);
    }

    [Theory]
    [InlineData("dome", 1.0, 60.0, 0.0, 0, 0, 1, 1, 0.3, 0)]
    [InlineData("wave", 2.0, 0.3, 2.0, 0, 0, 0, 1, 0.3, 0)]
    [InlineData("saddle", 2.0, 1.0, 1.0, -1, 0, 0, 1, 0.35, 0)]
    public void JacobiSeeding_KeepsStripsEvenAndAvoidsMerges(string shape, double p1, double p2, double p3,
        double sx, double sy, double sz, double dx, double dy, double dz)
    {
        var mesh = AnalyticShapes.Build(shape, p1, p2, p3, 40);
        var proj = new MeshProjection(mesh);
        int seed = proj.NearestVertexGlobal(new Vec3d(sx, sy, sz));
        double sp = 0.04 * mesh.BoundingBoxDiagonal();
        var dir = new Vec3d(dx, dy, dz);

        var plain = EvenlySpacedNet.TraceGeodesics(mesh, seed, dir, new EvenlySpacedNet.Options { Spacing = sp, JacobiSeeding = false });
        var jacobi = EvenlySpacedNet.TraceGeodesics(mesh, seed, dir, new EvenlySpacedNet.Options { Spacing = sp, JacobiSeeding = true });
        var a = Measure(mesh, plain, sp);
        var b = Measure(mesh, jacobi, sp);

        Assert.True(b.cv < a.cv, $"{shape}: width variation {b.cv:F2} with Jacobi vs {a.cv:F2} without");
        Assert.True(b.cv < 0.3, $"{shape}: width variation {b.cv:F2}");
        Assert.True(b.merges <= Math.Max(2, a.merges / 4), $"{shape}: {b.merges} merges with Jacobi vs {a.merges} without");
        Assert.True(b.curves >= 10, $"{shape}: only {b.curves} curves");
    }

    [Fact]
    public void Vault_IsAlreadyEven_JacobiChangesNothing()
    {
        var vault = AnalyticShapes.Build("vault", 1.5, 2.0, 4.0, 40);
        var proj = new MeshProjection(vault);
        int seed = proj.NearestVertexGlobal(new Vec3d(0, 2, 0));
        double sp = 0.04 * vault.BoundingBoxDiagonal();
        var g = EvenlySpacedNet.TraceGeodesics(vault, seed, new Vec3d(0.3, 1, 0), new EvenlySpacedNet.Options { Spacing = sp });
        var m = Measure(vault, g, sp);
        Assert.True(m.cv < 0.1 && m.merges == 0, $"cv {m.cv:F2}, merges {m.merges}");
    }

    [Fact]
    public void BorderSeeding_OnAVault_GivesAnEvenHelixPattern()
    {
        var vault = AnalyticShapes.Build("vault", 1.5, 2.0, 4.0, 40);
        var proj = new MeshProjection(vault);
        int seed = proj.NearestVertexGlobal(new Vec3d(-1, 2, 0)); // on the long straight edge x = -1
        double sp = 0.04 * vault.BoundingBoxDiagonal();
        var g = EvenlySpacedNet.TraceGeodesics(vault, seed, new Vec3d(1, 0, 0),
            new EvenlySpacedNet.Options { Spacing = sp, FromBorder = true, BorderAngle = 20 });
        Assert.True(g.Count >= 15, $"{g.Count} curves");
        var m = Measure(vault, g, sp);
        Assert.True(m.cv < 0.05, $"width variation {m.cv:F3}");
        Assert.Equal(0, m.merges);
        // every curve starts on the border
        foreach (var c in g)
        {
            bool s0 = proj.IsOnBoundary(proj.ClosestPoint(c[0], proj.NearestVertexGlobal(c[0])), 1e-4);
            bool s1 = proj.IsOnBoundary(proj.ClosestPoint(c[^1], proj.NearestVertexGlobal(c[^1])), 1e-4);
            Assert.True(s0 || s1, "curve does not touch the border");
        }
    }

    [Fact]
    public void BoundaryLoops_FindBothRimsOfAnAnnulus()
    {
        var ann = AnalyticShapes.Build("annulus", 0.4, 1.2, 1.0, 40);
        var loops = EvenlySpacedNet.BoundaryLoops(ann);
        Assert.Equal(2, loops.Count);
        Assert.Empty(EvenlySpacedNet.BoundaryLoops(AnalyticShapes.Build("torus", 3, 1, 0, 24)));
    }
}
