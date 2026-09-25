using System;
using System.Collections.Generic;
using Xunit;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Tests;

public class FabricationTests
{
    private static MeshProjection PlaneProj()
    {
        var grid = TestMeshes.CreateQuadGrid(20, 20, 1.0).ToTriangulated();
        return new MeshProjection(grid);
    }

    [Fact]
    public void StripSweep_Flat_CorrectDimensions()
    {
        var proj = PlaneProj();
        var line = new Vec3d[11];
        for (int i = 0; i <= 10; i++) line[i] = new Vec3d(5 + i, 10, 0);

        var profile = new LathProfile(0.4, 0.1, upright: false, offset: 0.0);
        var result = StripSweep.Sweep(proj, line, profile);

        Assert.NotNull(result);
        var r = result!.Value;
        Assert.Equal(4 * 11, r.Mesh.Vertices.Length);
        Assert.Equal(4 * 10 + 2, r.Mesh.Faces.Length); // 4 sides per segment + 2 caps

        // Flat strip: width across (y), thickness through (z), inner face on the surface
        double minY = double.MaxValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var v in r.Mesh.Vertices)
        {
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
            minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z);
        }
        Assert.Equal(0.4, maxY - minY, 6);
        Assert.Equal(0.0, minZ, 6);
        Assert.Equal(0.1, maxZ, 6);
    }

    [Fact]
    public void StripSweep_Upright_WidthAlongNormal()
    {
        var proj = PlaneProj();
        var line = new Vec3d[11];
        for (int i = 0; i <= 10; i++) line[i] = new Vec3d(5 + i, 10, 0);

        var profile = new LathProfile(0.5, 0.1, upright: true, offset: 0.2);
        var r = StripSweep.Sweep(proj, line, profile)!.Value;

        double minZ = double.MaxValue, maxZ = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var v in r.Mesh.Vertices)
        {
            minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
        }
        // Upright: width stands along the normal above the offset, thickness across
        Assert.Equal(0.2, minZ, 6);
        Assert.Equal(0.7, maxZ, 6);
        Assert.Equal(0.1, maxY - minY, 6);
    }

    private static double SignedVolume(MeshData m)
    {
        double vol = 0;
        foreach (var f in m.Faces)
            for (int i = 1; i + 1 < f.Length; i++)
                vol += Vec3d.Dot(m.Vertices[f[0]], Vec3d.Cross(m.Vertices[f[i]], m.Vertices[f[i + 1]])) / 6.0;
        return vol;
    }

    [Fact]
    public void StripSweep_RoundSection_IsATubeOfTheRightVolume()
    {
        var proj = PlaneProj();
        var line = new Vec3d[11];
        for (int i = 0; i <= 10; i++) line[i] = new Vec3d(5 + i, 10, 0);

        var profile = LathProfile.Round(0.3, segments: 32, offset: 0.05);
        var r = StripSweep.Sweep(proj, line, profile)!.Value;

        Assert.Equal(32 * 11, r.Mesh.Vertices.Length);
        Assert.Equal(32 * 10 + 2 * 30, r.Mesh.Faces.Length); // quad sides + two fan caps
        double minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var v in r.Mesh.Vertices) { minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z); }
        Assert.Equal(0.05, minZ, 6);      // bar rests Offset above the surface
        Assert.Equal(0.35, maxZ, 6);      // diameter 0.3 along the normal

        double expected = profile.SectionArea() * 10.0; // 32-gon area × length
        Assert.True(Math.Abs(profile.SectionArea() - Math.PI * 0.15 * 0.15) < 0.01 * Math.PI * 0.15 * 0.15, "32-gon area within 1% of the circle");
        Assert.True(Math.Abs(SignedVolume(r.Mesh) - expected) < 1e-6 * expected,
            $"Tube volume {SignedVolume(r.Mesh):F6} should equal section area × length {expected:F6} (positive: outward winding)");
    }

    [Fact]
    public void StripSweep_CustomTriangleSection_AppliesToEveryCurve()
    {
        var proj = PlaneProj();
        // A triangle given clockwise with a duplicate closing point: re-oriented and centred on its box
        var tri = new (double, double)[] { (0, 0), (0, 0.2), (0.4, 0), (0, 0) };
        var profile = LathProfile.Custom(tri, upright: false, offset: 0.0);
        Assert.Equal(0.4, profile.Width, 9);
        Assert.Equal(0.2, profile.Thickness, 9);
        Assert.Equal(0.5 * 0.4 * 0.2, profile.SectionArea(), 9);

        var lines = new List<Vec3d[]>();
        for (int k = 0; k < 3; k++)
        {
            var line = new Vec3d[11];
            for (int i = 0; i <= 10; i++) line[i] = new Vec3d(5 + i, 6 + 4 * k, 0);
            lines.Add(line);
        }
        var results = StripSweep.SweepAll(proj, lines, profile);
        Assert.Equal(3, results.Count);
        foreach (var r in results)
        {
            Assert.Equal(3 * 11, r.Mesh.Vertices.Length);
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var v in r.Mesh.Vertices) { minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z); }
            Assert.Equal(0.0, minZ, 6);
            Assert.Equal(0.2, maxZ, 6);
            Assert.True(Math.Abs(SignedVolume(r.Mesh) - 0.04 * 10.0) < 1e-9, $"Prism volume {SignedVolume(r.Mesh):F6} should be 0.4");
        }

        // Upright custom section: the first section axis follows the normal
        var up = LathProfile.Custom(tri, upright: true, offset: 0.1);
        var ru = StripSweep.Sweep(proj, lines[0], up)!.Value;
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var v in ru.Mesh.Vertices) { lo = Math.Min(lo, v.Z); hi = Math.Max(hi, v.Z); }
        Assert.Equal(0.1, lo, 6);
        Assert.Equal(0.5, hi, 6);   // 0.4 extent along the normal above the 0.1 offset
    }

    [Fact]
    public void StripSweep_ClosedLoop_WrapsWithoutCaps()
    {
        var proj = PlaneProj();
        // Square loop, first == last
        var line = new Vec3d[]
        {
            new(5, 5, 0), new(15, 5, 0), new(15, 15, 0), new(5, 15, 0), new(5, 5, 0)
        };

        var r = StripSweep.Sweep(proj, line, new LathProfile(0.2, 0.1))!.Value;

        Assert.Equal(4 * 4, r.Mesh.Vertices.Length);   // seam station not duplicated
        Assert.Equal(4 * 4, r.Mesh.Faces.Length);       // wrapped sides, no caps
    }

    [Fact]
    public void NetIntersections_TwoFamilies_FindsAllCrossings()
    {
        // 3 vertical x 4 horizontal straight laths on a plane
        var familyA = new List<Vec3d[]>();
        for (int i = 0; i < 3; i++)
            familyA.Add(new[] { new Vec3d(i + 1.0, 0, 0), new Vec3d(i + 1.0, 5, 0) });
        var familyB = new List<Vec3d[]>();
        for (int j = 0; j < 4; j++)
            familyB.Add(new[] { new Vec3d(0, j + 0.5, 0), new Vec3d(5, j + 0.5, 0) });

        var crossings = NetIntersections.Find(familyA, familyB, 1e-6);

        Assert.Equal(12, crossings.Count);
        foreach (var c in crossings)
        {
            Assert.True(c.Gap < 1e-9, $"On-plane crossing gap should be ~0, got {c.Gap}");
            double ex = c.CurveA + 1.0, ey = c.CurveB + 0.5;
            Assert.True(Math.Abs(c.Point.X - ex) < 1e-9 && Math.Abs(c.Point.Y - ey) < 1e-9,
                $"Crossing at wrong location: {c.Point}");
        }
    }

    [Fact]
    public void NetIntersections_SelfMode_SkipsNeighborsAndSeam()
    {
        // One closed square loop: no genuine self-crossings
        var loop = new Vec3d[]
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 0, 0)
        };
        var crossings = NetIntersections.Find(new[] { loop }, null, 1e-6);
        Assert.Empty(crossings);

        // A figure-eight polyline crosses itself once
        var eight = new Vec3d[]
        {
            new(0, 0, 0), new(2, 2, 0), new(2, 0, 0), new(0, 2, 0)
        };
        crossings = NetIntersections.Find(new[] { eight }, null, 1e-6);
        Assert.Single(crossings);
        Assert.True((crossings[0].Point - new Vec3d(1, 1, 0)).Length < 1e-9,
            $"Self-crossing should be at (1,1,0), got {crossings[0].Point}");
    }

    [Fact]
    public void JointGeometry_FlatOrthogonal_HalfLapDims()
    {
        // A along x, B along y, crossing at origin on the z=0 plane
        var ok = JointGeometry.TryBuildLapNotches(
            Vec3d.Zero, new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1),
            new LathProfile(0.4, 0.1), new LathProfile(0.4, 0.1),
            lap: 0.5, clearance: 0.0,
            out NotchSolid na, out NotchSolid nb);

        Assert.True(ok);
        // Notch A is aligned with B (it removes B's footprint): across B's
        // width, long enough to span A's width; it opens the top half of A
        // and bleeds slightly past A's top face so the boolean is not coplanar
        Assert.Equal(0.2, na.HalfX, 6);
        Assert.Equal(0.2, na.HalfY, 6);
        Assert.Equal(1.0, Math.Abs(Vec3d.Dot(na.AxisX, new Vec3d(0, 1, 0))), 6);
        Assert.Equal(0.05, na.Center.Z - na.HalfZ, 6);   // cut starts at mid-depth
        Assert.True(na.Center.Z + na.HalfZ > 0.1);        // and clears the top face
        Assert.Equal(0.05, nb.Center.Z + nb.HalfZ, 6);   // bottom half of [0, 0.1]
        Assert.True(nb.Center.Z - nb.HalfZ < 0.0);
    }

    [Fact]
    public void JointGeometry_SkewedCrossing_NotchCoversWidth()
    {
        // 60-degree crossing: notch length must cover width / sin(60)
        double c = Math.Cos(Math.PI / 3), s = Math.Sin(Math.PI / 3);
        var ok = JointGeometry.TryBuildLapNotches(
            Vec3d.Zero, new Vec3d(1, 0, 0), new Vec3d(c, s, 0), new Vec3d(0, 0, 1),
            new LathProfile(0.4, 0.1), new LathProfile(0.4, 0.1),
            lap: 0.5, clearance: 0.0,
            out NotchSolid na, out _);

        Assert.True(ok);
        // The overlap of two 0.4-wide strips at 60° is a parallelogram whose
        // extent along either lath is w/sinθ + w·cotθ; the notch must span it
        double expected = 0.4 / Math.Sin(Math.PI / 3) + 0.4 * Math.Cos(Math.PI / 3) / Math.Sin(Math.PI / 3);
        Assert.Equal(expected / 2, na.HalfX, 6);

        // Every corner of A's footprint inside B's strip must fall inside notch A
        Vec3d tb = new Vec3d(c, s, 0), gb = new Vec3d(-s, c, 0);
        foreach (double ya in new[] { -0.2, 0.2 })
        {
            // Points on A's edge (y = ya) that lie on B's edges: |gb·p| = 0.2
            foreach (double side in new[] { -0.2, 0.2 })
            {
                // gb·(x, ya) = -s x + c ya = side  ->  x = (c ya - side) / s
                var p = new Vec3d((c * ya - side) / s, ya, 0);
                double along = Math.Abs(Vec3d.Dot(p - na.Center, na.AxisX));
                double across = Math.Abs(Vec3d.Dot(p - na.Center, na.AxisY));
                Assert.True(along <= na.HalfX + 1e-9, $"corner {p} outside notch along ({along} > {na.HalfX})");
                Assert.True(across <= na.HalfY + 1e-9, $"corner {p} outside notch across");
            }
        }
    }

    [Fact]
    public void JointGeometry_Parallel_Fails()
    {
        var ok = JointGeometry.TryBuildLapNotches(
            Vec3d.Zero, new Vec3d(1, 0, 0), new Vec3d(1, 0.001, 0), new Vec3d(0, 0, 1),
            new LathProfile(0.4, 0.1), new LathProfile(0.4, 0.1),
            0.5, 0.0, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Sweep_GeodesicsOnCylinder_ProducesValidStrips()
    {
        // Integration: trace geodesics, sweep them, check the strips are solid
        var proj = new MeshProjection(Cylinder(24, 12));
        var lines = new List<Vec3d[]>();
        for (int k = 0; k < 4; k++)
        {
            double th = 2 * Math.PI * k / 4;
            var line = new Vec3d[13];
            for (int i = 0; i <= 12; i++)
                line[i] = new Vec3d(5 * Math.Cos(th + 0.3 * i), 5 * Math.Sin(th + 0.3 * i), i * 0.5);
            lines.Add(line);
        }

        var results = StripSweep.SweepAll(proj, lines, new LathProfile(0.3, 0.05));
        Assert.Equal(4, results.Count);
        foreach (var r in results)
        {
            Assert.Equal(4 * 13, r.Mesh.Vertices.Length);
            foreach (var v in r.Mesh.Vertices)
            {
                double radius = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                Assert.True(radius > 4.9 && radius < 5.2,
                    $"Strip vertex should hug the cylinder, radius {radius:F3}");
            }
        }
    }

    private static MeshData Cylinder(int seg, int rows)
    {
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j < seg; j++)
            {
                double th = 2 * Math.PI * j / seg;
                verts.Add(new Vec3d(5 * Math.Cos(th), 5 * Math.Sin(th), i * 0.5));
            }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * seg + j, b = i * seg + (j + 1) % seg;
                int c = a + seg, d = b + seg;
                faces.Add(new[] { a, b, d });
                faces.Add(new[] { a, d, c });
            }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }
}
