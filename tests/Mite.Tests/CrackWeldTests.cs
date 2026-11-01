using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Cracks and T-junctions for Mesh Cleanup (Borodin, Novotni &amp; Klein 2002:
/// vertex–edge contraction in order of distance). Every dirty mesh is built
/// from patches whose seam is known exactly, so the mended mesh can be checked
/// against topology that must hold (manifold, Euler characteristic, border
/// loops, Gauss–Bonnet) and against the clean surface.
/// </summary>
public class CrackWeldTests
{
    // ---------------------------------------------------------------- helpers

    private static Dictionary<long, int> EdgeUses(MeshData m)
    {
        var d = new Dictionary<long, int>();
        foreach (var f in m.Faces)
            for (int i = 0; i < f.Length; i++)
            {
                int a = f[i], b = f[(i + 1) % f.Length];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                d.TryGetValue(key, out int n);
                d[key] = n + 1;
            }
        return d;
    }

    private static int EulerCharacteristic(MeshData m) => m.VertexCount - EdgeUses(m).Count + m.FaceCount;

    private static int BorderEdgeCount(MeshData m) => EdgeUses(m).Values.Count(n => n == 1);

    private static double Area(MeshData m) => m.Faces.Sum(f =>
    {
        Vec3d n = Vec3d.Zero;
        for (int i = 1; i < f.Length - 1; i++)
            n = n + Vec3d.Cross(m.Vertices[f[i]] - m.Vertices[f[0]], m.Vertices[f[i + 1]] - m.Vertices[f[0]]);
        return 0.5 * n.Length;
    });

    private static double SignedVolume(MeshData m)
    {
        double vol = 0;
        foreach (var f in m.Faces)
            for (int i = 1; i < f.Length - 1; i++)
                vol += Vec3d.Dot(m.Vertices[f[0]], Vec3d.Cross(m.Vertices[f[i]], m.Vertices[f[i + 1]])) / 6.0;
        return vol;
    }

    /// <summary>Σ (2π − angle sum) over the vertices of a closed triangle mesh (Gauss–Bonnet: 2πχ).</summary>
    private static double TotalAngleDeficit(MeshData m)
    {
        var t = m.ToTriangulated();
        var sum = new double[t.VertexCount];
        foreach (var f in t.Faces)
            for (int i = 0; i < 3; i++)
            {
                Vec3d u = t.Vertices[f[(i + 1) % 3]] - t.Vertices[f[i]], v = t.Vertices[f[(i + 2) % 3]] - t.Vertices[f[i]];
                sum[f[i]] += Math.Atan2(Vec3d.Cross(u, v).Length, Vec3d.Dot(u, v));
            }
        return sum.Sum(s => 2 * Math.PI - s);
    }

    /// <summary>Two meshes side by side as one vertex list (nothing shared, nothing welded).</summary>
    private static MeshData Join(MeshData a, MeshData b)
    {
        var v = a.Vertices.Concat(b.Vertices).ToArray();
        var f = a.Faces.Concat(b.Faces.Select(x => x.Select(i => i + a.VertexCount).ToArray())).ToArray();
        return new MeshData(v, f);
    }

    /// <summary>Flat triangulated rectangle [x0, x1] × [y0, y1] with nx × ny cells.</summary>
    private static MeshData Rect(double x0, double x1, double y0, double y1, int nx, int ny)
    {
        var v = new List<Vec3d>();
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                v.Add(new Vec3d(x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny, 0));
        var f = new List<int[]>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = b + nx + 1, d = a + nx + 1;
                f.Add(new[] { a, b, c }); f.Add(new[] { a, c, d });
            }
        return new MeshData(v.ToArray(), f.ToArray());
    }

    /// <summary>
    /// Band of a surface of revolution r(z) between two heights, `segments` around,
    /// `rows` rings, triangulated; optionally the angles offset by half a step.
    /// </summary>
    private static MeshData Band(Func<double, double> radius, double z0, double z1, int segments, int rows, double phase = 0)
    {
        var v = new List<Vec3d>();
        for (int j = 0; j <= rows; j++)
        {
            double z = z0 + (z1 - z0) * j / rows, r = radius(z);
            for (int i = 0; i < segments; i++)
            {
                double th = 2.0 * Math.PI * (i + phase) / segments;
                v.Add(new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z));
            }
        }
        var f = new List<int[]>();
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * segments + i, b = j * segments + (i + 1) % segments, c = b + segments, d = a + segments;
                f.Add(new[] { a, b, c }); f.Add(new[] { a, c, d });
            }
        return new MeshData(v.ToArray(), f.ToArray());
    }

    /// <summary>Sphere cap z ∈ [z0, z1] of the unit sphere with the pole closed by a fan when it is included.</summary>
    private static MeshData Hemisphere(bool upper, int segments, int rings)
    {
        var v = new List<Vec3d>();
        var f = new List<int[]>();
        // rings from the equator (ring 0) towards the pole, the pole itself a single vertex
        for (int j = 0; j < rings; j++)
        {
            double phi = 0.5 * Math.PI * j / rings; // latitude
            double z = Math.Sin(phi), r = Math.Cos(phi);
            if (!upper) z = -z;
            for (int i = 0; i < segments; i++)
            {
                double th = 2.0 * Math.PI * i / segments;
                v.Add(new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z));
            }
        }
        int pole = v.Count;
        v.Add(new Vec3d(0, 0, upper ? 1 : -1));
        for (int j = 0; j < rings - 1; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * segments + i, b = j * segments + (i + 1) % segments, c = b + segments, d = a + segments;
                if (upper) { f.Add(new[] { a, b, c }); f.Add(new[] { a, c, d }); }
                else { f.Add(new[] { a, c, b }); f.Add(new[] { a, d, c }); }
            }
        int last = (rings - 1) * segments;
        for (int i = 0; i < segments; i++)
        {
            int a = last + i, b = last + (i + 1) % segments;
            f.Add(upper ? new[] { a, b, pole } : new[] { a, pole, b });
        }
        return new MeshData(v.ToArray(), f.ToArray());
    }

    // ---------------------------------------------------------------- T-junctions on a straight seam

    [Fact]
    public void Seam_WithDoubledVertexCount_IsWeldedWithoutMovingAnything()
    {
        // left 4 × 4 cells of 0.25, right 8 × 8 cells of 0.125: the seam x = 1 has 5 vertices on
        // the left and 9 on the right, 4 of them T-vertices on the middle of a left edge
        var dirty = Join(Rect(0, 1, 0, 1, 4, 4), Rect(1, 2, 0, 1, 8, 8));

        var off = MeshCleanup.Compute(dirty);
        Assert.Equal(2, off.Info.Components);
        Assert.Equal(4, off.Info.CrackCandidates);
        Assert.True(off.Info.CrackCandidateDistance < 1e-12, $"T-vertices lie on the edge, distance {off.Info.CrackCandidateDistance}");
        Assert.Contains("set Cracks to", off.Info.Report);

        var on = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 1e-6 });
        var m = on.Mesh;
        Assert.Equal(4, on.Info.CracksClosed);
        Assert.Equal(0, on.Info.CrackVerticesMerged);                  // the 5 coincident pairs went in the weld pass
        Assert.Equal(5, on.WeldedVertices);
        Assert.True(on.Info.CrackMaxGap < 1e-12, $"largest gap {on.Info.CrackMaxGap}");
        Assert.Equal(0, on.Info.CracksLeft);
        Assert.Equal(1, on.Info.Components);
        Assert.Equal(1, on.Info.BorderLoops);
        Assert.Empty(on.Info.NonManifoldEdges);
        Assert.Equal(dirty.FaceCount + 4, m.FaceCount);                 // 4 left triangles split in two
        Assert.Equal(dirty.VertexCount - 5, m.VertexCount);
        Assert.Equal(1, EulerCharacteristic(m));                        // a disk
        Assert.Equal(2 * (4 + 8) + 4 + 8, BorderEdgeCount(m));          // only the outer rectangle is left: 12 + 12 + 4 + 8
        Assert.True(Math.Abs(Area(m) - 2.0) < 1e-12, $"area {Area(m)}");
        foreach (var f in m.Faces) Assert.Equal(3, f.Length);
        // no vertex moved: every output vertex is an input vertex
        var input = new HashSet<Vec3d>(dirty.Vertices);
        Assert.All(m.Vertices, p => Assert.Contains(p, input));
        // the seam vertices are interior now: angle sum 2π
        var tri = m.ToTriangulated();
        var sum = new double[m.VertexCount];
        foreach (var f in tri.Faces)
            for (int i = 0; i < 3; i++)
            {
                Vec3d u = m.Vertices[f[(i + 1) % 3]] - m.Vertices[f[i]], w = m.Vertices[f[(i + 2) % 3]] - m.Vertices[f[i]];
                sum[f[i]] += Math.Atan2(Vec3d.Cross(u, w).Length, Vec3d.Dot(u, w));
            }
        for (int i = 0; i < m.VertexCount; i++)
            if (Math.Abs(m.Vertices[i].X - 1) < 1e-12 && m.Vertices[i].Y > 1e-9 && m.Vertices[i].Y < 1 - 1e-9)
                Assert.True(Math.Abs(sum[i] - 2 * Math.PI) < 1e-12, $"seam vertex {i} angle sum {sum[i]}");
    }

    [Fact]
    public void Seam_OnQuads_SplitsTheQuadIntoThreeTriangles()
    {
        var left = TestMeshes.CreateQuadGrid(2, 2, 0.5);          // [0,1]², 2 × 2 quads
        var right = Rect(1, 2, 0, 1, 4, 4);                        // 4 × 4 triangles: T-vertices at y = 0.25 and 0.75
        var dirty = Join(left, right);
        var on = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 1e-6 });
        var m = on.Mesh;
        Assert.Equal(1, on.Info.Components);
        Assert.Equal(1, on.Info.BorderLoops);
        Assert.Equal(1, EulerCharacteristic(m));
        Assert.True(Math.Abs(Area(m) - 2.0) < 1e-12, $"area {Area(m)}");
        // each of the two seam quads takes one T-vertex and becomes three triangles
        Assert.Equal(2, on.Info.CracksClosed);
        Assert.Equal(3, on.WeldedVertices);
        int quads = m.Faces.Count(f => f.Length == 4), tris = m.Faces.Count(f => f.Length == 3);
        Assert.Equal(2, quads);
        Assert.Equal(32 + 6, tris);
        Assert.Equal(2 * 2 + 4 + 2 * 4 + 2, BorderEdgeCount(m));
        Assert.All(m.Vertices, p => Assert.Contains(p, new HashSet<Vec3d>(dirty.Vertices)));
    }

    // ---------------------------------------------------------------- sphere from two hemispheres

    [Fact]
    public void Sphere_FromHemispheresWithDifferentMeridianCounts_ClosesAndSatisfiesGaussBonnet()
    {
        // 24 meridians below, 48 above: every other upper equator vertex coincides with a lower
        // one, the others lie the sagitta 1 − cos 7.5° = 0.00856 outside the lower chord
        var dirty = Join(Hemisphere(false, 24, 6), Hemisphere(true, 48, 12));
        double sagitta = 1 - Math.Cos(Math.PI / 24);

        var off = MeshCleanup.Compute(dirty);
        Assert.Equal(1, off.Info.BorderLoops);                          // the two rims share the 24 welded vertices: one pinched border graph
        Assert.Equal(2, off.Info.Components);
        Assert.Equal(0, off.Info.ClosedComponents);
        Assert.Equal(24, off.Info.CrackCandidates);
        Assert.True(Math.Abs(off.Info.CrackCandidateDistance - sagitta) < 1e-9, $"candidate distance {off.Info.CrackCandidateDistance} vs sagitta {sagitta}");

        var on = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.01 });
        var m = on.Mesh;
        Assert.Equal(24, on.Info.CracksClosed);
        Assert.Equal(24, on.WeldedVertices);
        Assert.Equal(0, on.Info.CracksLeft);
        Assert.True(Math.Abs(on.Info.CrackMaxGap - sagitta) < 1e-9, $"largest gap {on.Info.CrackMaxGap}");
        Assert.Equal(0, on.Info.BorderLoops);
        Assert.Equal(1, on.Info.Components);
        Assert.Equal(1, on.Info.ClosedComponents);
        Assert.Empty(on.Info.NonManifoldEdges);
        Assert.Equal(0, on.Info.OrientationConflicts);
        Assert.Equal(2, EulerCharacteristic(m));
        Assert.True(Math.Abs(TotalAngleDeficit(m) - 4 * Math.PI) < 1e-9, $"total deficit {TotalAngleDeficit(m)} vs 4π");
        Assert.Equal(dirty.FaceCount + 24, m.FaceCount);
        // no vertex moved: every vertex is still on the unit sphere (the coarse chords bend through the fine vertices)
        Assert.All(m.Vertices, p => Assert.True(Math.Abs(p.Length - 1) < 1e-12, $"|p| = {p.Length}"));
        Assert.True(SignedVolume(m) > 0, "outward");
        // Gaussian curvature at the equator reads as 0 on the cracked mesh (border) and ≈ 1 on the mended one
        var kOff = GaussianCurvature.Compute(off.Mesh);
        var kOn = GaussianCurvature.Compute(m);
        int seamOff = Enumerable.Range(0, off.Mesh.VertexCount).Count(i => Math.Abs(off.Mesh.Vertices[i].Z) < 1e-9);
        Assert.True(seamOff == 48 && Enumerable.Range(0, off.Mesh.VertexCount).Where(i => Math.Abs(off.Mesh.Vertices[i].Z) < 1e-9).All(i => kOff[i] == 0),
            "cracked: the equator is a border, K = 0 there");
        var seamK = Enumerable.Range(0, m.VertexCount).Where(i => Math.Abs(m.Vertices[i].Z) < 1e-9).Select(i => kOn[i]).ToArray();
        Assert.Equal(48, seamK.Length);
        Assert.True(Math.Abs(seamK.Average() - 1) < 0.1 && seamK.All(k => k > 0.3 && k < 3.0),
            $"mended: K at the equator mean {seamK.Average():F3}, {seamK.Min():F3} … {seamK.Max():F3}");
    }

    [Fact]
    public void Sphere_WithOneHemisphereInsideOut_IsOrientedAcrossTheMendedSeam()
    {
        var lower = Hemisphere(false, 24, 6);
        var reversed = new MeshData(lower.Vertices, lower.Faces.Select(f => f.Reverse().ToArray()).ToArray());
        var dirty = Join(reversed, Hemisphere(true, 48, 12));
        var on = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.01 });
        Assert.Equal(1, on.Info.ClosedComponents);
        Assert.Equal(0, on.Info.OrientationConflicts);
        Assert.Equal(lower.FaceCount + 24, on.Info.FlippedFaces);       // the lower faces and the 24 pieces split off them
        Assert.True(SignedVolume(on.Mesh) > 4.0, $"volume {SignedVolume(on.Mesh)}");
    }

    // ---------------------------------------------------------------- interleaved seam: both sides snap

    [Fact]
    public void Cylinder_FromTwoBandsOffsetByHalfAStep_IsOneManifoldSheet()
    {
        // same count on both sides but rotated half a step: every seam vertex is a T-vertex of
        // the other side, the sagitta 1 − cos(π/64) = 0.0012 off its chord
        var dirty = Join(Band(_ => 1.0, -1, 0, 64, 8), Band(_ => 1.0, 0, 1, 64, 8, 0.5));
        double sagitta = 1 - Math.Cos(Math.PI / 64);
        var on = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.005 });
        var m = on.Mesh;
        Assert.Equal(128, on.Info.CracksClosed);
        Assert.Equal(0, on.Info.CrackVerticesMerged);
        Assert.Equal(0, on.Info.CracksLeft);
        Assert.True(on.Info.CrackMaxGap <= sagitta + 1e-12, $"largest gap {on.Info.CrackMaxGap} vs sagitta {sagitta}");
        Assert.Equal(1, on.Info.Components);
        Assert.Equal(2, on.Info.BorderLoops);
        Assert.Empty(on.Info.NonManifoldEdges);
        Assert.Equal(0, EulerCharacteristic(m));                       // an annulus
        Assert.Equal(dirty.FaceCount + 128, m.FaceCount);
        Assert.Equal(dirty.VertexCount, m.VertexCount);
    }

    // ---------------------------------------------------------------- what must not happen

    [Fact]
    public void TwoSheetsOneAboveTheOther_AreNotSnappedBeyondTheTolerance()
    {
        var dirty = Join(Rect(0, 1, 0, 1, 4, 4), new MeshData(Rect(0, 1, 0, 1, 8, 8).Vertices.Select(p => p + new Vec3d(0, 0, 0.01)).ToArray(), Rect(0, 1, 0, 1, 8, 8).Faces));
        var on = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.005 });
        Assert.Equal(0, on.Info.CracksClosed + on.Info.CrackVerticesMerged);
        Assert.Equal(2, on.Info.Components);
        // and within the tolerance the double wall is zipped at its rim, never through a third face:
        // at two corners the second zip would duplicate a fine triangle, so it is refused and reported
        var zip = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.02 });
        Assert.Empty(zip.Info.NonManifoldEdges);
        Assert.True(zip.Info.CrackMaxGap <= 0.02);
        Assert.True(zip.Info.CracksLeft > 0 && zip.Info.CracksLeft <= 4, $"left {zip.Info.CracksLeft}");
        Assert.Contains("third face", zip.Info.Report);
    }

    [Fact]
    public void Tolerance_BoundsEveryMove()
    {
        var dirty = Join(Hemisphere(false, 24, 6), Hemisphere(true, 48, 12));
        double sagitta = 1 - Math.Cos(Math.PI / 24);
        // below the sagitta nothing is snapped, the mesh stays two sheets, and the report says what would
        var below = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.5 * sagitta });
        Assert.Equal(0, below.Info.CracksClosed);
        Assert.Equal(2, below.Info.Components);
        Assert.Equal(0, below.Info.CracksLeft);
    }

    // ---------------------------------------------------------------- the point of it: tracers cross the seam

    [Fact]
    public void Hyperboloid_InTwoPatches_RulingsStopAtTheCrackAndRunRimToRimAfterWelding()
    {
        double a = 1.0, c = 1.0;
        Func<double, double> r = z => a * Math.Sqrt(1 + z * z / (c * c));
        var dirty = Join(Band(r, -1, 0, 64, 16), Band(r, 0, 1, 64, 16, 0.5));
        double sagitta = r(0) * (1 - Math.Cos(Math.PI / 64));

        var cracked = MeshCleanup.Compute(dirty).Mesh;
        var mended = MeshCleanup.Compute(dirty, new MeshCleanup.Options { CrackTolerance = 0.005 });
        Assert.Equal(128, mended.Info.CracksClosed);
        Assert.True(mended.Info.CrackMaxGap <= sagitta + 1e-12);

        int Through(MeshData mesh, out int curves, out double worstBow, out double worstAngle)
        {
            var pc = PrincipalCurvature.Compute(mesh);
            var field = AsymptoticCurves.ComputeDirections(pc);
            var fam = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1,
                new EvenlySpacedNet.Options { Spacing = 0.25 }, field.Family2);
            curves = fam.Count;
            worstBow = 0; worstAngle = 0;
            int rimToRim = 0;
            foreach (var line in fam)
            {
                double zMin = line.Min(p => p.Z), zMax = line.Max(p => p.Z);
                if (zMin < -0.999 && zMax > 0.999) rimToRim++;
                Vec3d chord = line[line.Length - 1] - line[0];
                double L = chord.Length;
                foreach (var p in line)
                {
                    Vec3d d = p - line[0];
                    double bow = Vec3d.Cross(d, chord).Length / L;
                    if (bow > worstBow) worstBow = bow;
                }
                var (plus, minus) = TestMeshes.HyperboloidRulings(line[line.Length / 2], a, c);
                double ang = Math.Min(Angle(chord, plus), Angle(chord, minus));
                if (ang > worstAngle) worstAngle = ang;
            }
            return rimToRim;
        }

        int throughCracked = Through(cracked, out int nCracked, out _, out _);
        int throughMended = Through(mended.Mesh, out int nMended, out double bow, out double ang);
        Assert.Equal(0, throughCracked);                                 // the seam is a border: every ruling ends on it
        Assert.True(throughMended == nMended && nMended >= 20, $"{throughMended} of {nMended} rulings rim to rim after welding");
        Assert.True(bow < 0.03, $"rulings straight through the seam, bow {bow:F4}");
        Assert.True(ang < 1.0, $"ruling direction off by {ang:F3}°");
    }

    private static double Angle(Vec3d u, Vec3d v)
    {
        double d = Math.Abs(Vec3d.Dot(u.Normalized(), v.Normalized()));
        return Math.Acos(Math.Min(1.0, d)) * 180.0 / Math.PI;
    }
}
