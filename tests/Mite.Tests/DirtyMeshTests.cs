using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Dirty meshes for Mesh Cleanup, each made from a clean mesh with a known
/// defect so the repaired mesh can be compared with the original exactly:
/// polygon soups (unwelded seams), duplicate faces in both windings, flipped
/// faces (including face 0), isolated vertices, millimetre scale far from the
/// origin, needles (an edge split a hair from its end), caps (a vertex a hair
/// from an edge), non-manifold fins and a Möbius strip.
/// </summary>
public class DirtyMeshTests
{
    // ---------------------------------------------------------------- helpers

    private static MeshData Soup(MeshData m, Vec3d offset = default, double scale = 1.0)
    {
        var v = new List<Vec3d>();
        var f = new List<int[]>();
        foreach (var face in m.Faces)
        {
            var g = new int[face.Length];
            for (int i = 0; i < face.Length; i++) { g[i] = v.Count; v.Add(scale * m.Vertices[face[i]] + offset); }
            f.Add(g);
        }
        return new MeshData(v.ToArray(), f.ToArray());
    }

    private static double SignedVolume(MeshData m)
    {
        double vol = 0;
        foreach (var f in m.Faces)
            for (int i = 1; i < f.Length - 1; i++)
                vol += Vec3d.Dot(m.Vertices[f[0]], Vec3d.Cross(m.Vertices[f[i]], m.Vertices[f[i + 1]])) / 6.0;
        return vol;
    }

    private static double Area(MeshData m) => m.Faces.Sum(f =>
    {
        Vec3d n = Vec3d.Zero;
        for (int i = 1; i < f.Length - 1; i++)
            n = n + Vec3d.Cross(m.Vertices[f[i]] - m.Vertices[f[0]], m.Vertices[f[i + 1]] - m.Vertices[f[0]]);
        return 0.5 * n.Length;
    });

    private static MeshData Reversed(MeshData m) =>
        new(m.Vertices, m.Faces.Select(f => f.Reverse().ToArray()).ToArray());

    /// <summary>
    /// Faces of <paramref name="got"/> rewritten in the vertex indices of
    /// <paramref name="reference"/> (matched by position after undoing
    /// scale/offset), as canonical directed cycles; and the number of output
    /// vertices that match no reference vertex.
    /// </summary>
    private static (HashSet<string> faces, int unmatched) InReferenceIndices(MeshData got, MeshData reference,
        Vec3d offset = default, double scale = 1.0, double tol = 1e-9)
    {
        var idx = new int[got.VertexCount];
        int unmatched = 0;
        for (int i = 0; i < got.VertexCount; i++)
        {
            Vec3d p = (got.Vertices[i] - offset) / scale;
            int best = -1; double bd = double.MaxValue;
            for (int j = 0; j < reference.VertexCount; j++)
            {
                double d = (reference.Vertices[j] - p).LengthSquared;
                if (d < bd) { bd = d; best = j; }
            }
            if (Math.Sqrt(bd) > tol) { unmatched++; best = -1 - i; }
            idx[i] = best;
        }
        return (new HashSet<string>(got.Faces.Select(f => Canon(f.Select(x => idx[x]).ToArray()))), unmatched);
    }

    private static HashSet<string> Canonical(MeshData m) => new(m.Faces.Select(Canon));

    /// <summary>Rotation of the cycle that starts at its smallest index (direction kept).</summary>
    private static string Canon(int[] f)
    {
        int s = 0;
        for (int i = 1; i < f.Length; i++) if (f[i] < f[s]) s = i;
        return string.Join(",", Enumerable.Range(0, f.Length).Select(i => f[(s + i) % f.Length]));
    }

    private static double MinAngleDeg(MeshData m)
    {
        double min = 180;
        foreach (var f in m.Faces)
        {
            if (f.Length != 3) continue;
            for (int i = 0; i < 3; i++)
            {
                Vec3d a = m.Vertices[f[i]], b = m.Vertices[f[(i + 1) % 3]], c = m.Vertices[f[(i + 2) % 3]];
                Vec3d u = b - a, v = c - a;
                min = Math.Min(min, Math.Atan2(Vec3d.Cross(u, v).Length, Vec3d.Dot(u, v)) * 180 / Math.PI);
            }
        }
        return min;
    }

    /// <summary>Flat triangulated square grid, z = 0, normals +z.</summary>
    private static MeshData FlatGrid(int n, double size = 1.0)
    {
        var v = new List<Vec3d>();
        for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) v.Add(new Vec3d(i * size / n, j * size / n, 0));
        var f = new List<int[]>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = b + n + 1, d = a + n + 1;
                f.Add(new[] { a, b, c }); f.Add(new[] { a, c, d });
            }
        return new MeshData(v.ToArray(), f.ToArray());
    }

    /// <summary>
    /// Splits interior edges a–b at p = a + ε(b − a): each split turns two
    /// triangles into four, two of them needles with the short edge a–p. The
    /// edges are chosen so no two splits share a triangle's vertex.
    /// </summary>
    private static (MeshData mesh, int splits) WithNeedles(MeshData m, double eps, int every)
    {
        var verts = m.Vertices.ToList();
        var faces = m.Faces.Select(f => (int[])f.Clone()).ToList();
        var used = new HashSet<int>();
        int splits = 0;
        var edges = m.BuildEdges();
        for (int e = 0; e < edges.Length; e += every)
        {
            var (a, b) = edges[e];
            var adj = Enumerable.Range(0, faces.Count).Where(fi => faces[fi].Contains(a) && faces[fi].Contains(b)).ToList();
            if (adj.Count != 2) continue;
            var ring = adj.SelectMany(fi => faces[fi]).Distinct().ToList();
            if (ring.Any(used.Contains)) continue;
            foreach (int x in ring) used.Add(x);
            int p = verts.Count;
            verts.Add(verts[a] + eps * (verts[b] - verts[a]));
            foreach (int fi in adj)
            {
                var f = faces[fi];
                int ia = Array.IndexOf(f, a), ib = Array.IndexOf(f, b);
                int c = f.First(x => x != a && x != b);
                // keep the face's winding: replace a→b (or b→a) by a→p→b
                bool aToB = f[(ia + 1) % 3] == b;
                if (aToB) { faces[fi] = new[] { a, p, c }; faces.Add(new[] { p, b, c }); }
                else { faces[fi] = new[] { b, p, c }; faces.Add(new[] { p, a, c }); }
                _ = ib;
            }
            splits++;
        }
        return (new MeshData(verts.ToArray(), faces.ToArray()), splits);
    }

    // ---------------------------------------------------------------- weld, duplicates, isolated

    [Fact]
    public void Soup_WeldsBackToTheCleanSphere_FaceForFace()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        Assert.True(SignedVolume(sphere) > 0); // the reference faces outward
        var r = MeshCleanup.Compute(Soup(sphere));
        Assert.Equal(sphere.VertexCount, r.Mesh.VertexCount);
        Assert.Equal(sphere.FaceCount * 3 - sphere.VertexCount, r.WeldedVertices);
        var (faces, unmatched) = InReferenceIndices(r.Mesh, sphere);
        Assert.Equal(0, unmatched);
        Assert.True(Canonical(sphere).SetEquals(faces));
        Assert.Equal(0, r.Info.FlippedFaces);
        Assert.Equal(1, r.Info.Components);
        Assert.Equal(1, r.Info.ClosedComponents);
        Assert.Equal(0, r.Info.BorderLoops);
        Assert.Empty(r.Info.NonManifoldEdges);
        Assert.Equal(0, r.Info.OrientationConflicts);
    }

    [Fact]
    public void DuplicatesInBothWindings_AndIsolatedVertices_AreRemoved()
    {
        var saddle = TestMeshes.CreateSaddle(12);
        var v = saddle.Vertices.ToList();
        var f = saddle.Faces.Select(x => (int[])x.Clone()).ToList();
        for (int i = 0; i < 10; i++) f.Add((int[])saddle.Faces[7 * i].Clone());              // same winding
        for (int i = 0; i < 6; i++) f.Add(saddle.Faces[11 * i + 3].Reverse().ToArray());       // reversed
        for (int i = 0; i < 5; i++) v.Add(new Vec3d(10 + i, 0, 0));                             // unused
        var r = MeshCleanup.Compute(new MeshData(v.ToArray(), f.ToArray()));
        Assert.Equal(16, r.RemovedDuplicateFaces);
        Assert.Equal(5, r.Info.IsolatedVertices);
        Assert.Equal(saddle.VertexCount, r.Mesh.VertexCount);
        for (int i = 0; i < 5; i++) Assert.Equal(-1, r.VertexMap[saddle.VertexCount + i]);
        var (faces, unmatched) = InReferenceIndices(r.Mesh, saddle);
        Assert.Equal(0, unmatched);
        Assert.True(Canonical(saddle).SetEquals(faces));
        Assert.Equal(1, r.Info.BorderLoops);
        Assert.Contains("isolated", r.Info.Report);

        // Kept when asked to
        var keep = MeshCleanup.Compute(new MeshData(v.ToArray(), f.ToArray()),
            new MeshCleanup.Options { RemoveIsolatedVertices = false });
        Assert.Equal(saddle.VertexCount + 5, keep.Mesh.VertexCount);
    }

    [Fact]
    public void MillimetreScaleFarFromTheOrigin_RepairsTheSame()
    {
        var sphere = TestMeshes.CreateUnitSphere(12);
        var off = new Vec3d(2.5e6, -1.2e6, 350.0);
        // 1 m sphere drawn in millimetres at a georeferenced position, exploded, 30 % flipped
        var soup = Soup(sphere, off, 1000.0);
        var rnd = new Random(5);
        var faces = soup.Faces.Select((x, i) => i == 0 || rnd.NextDouble() < 0.3 ? x.Reverse().ToArray() : x).ToArray();
        var r = MeshCleanup.Compute(new MeshData(soup.Vertices, faces));
        Assert.Equal(sphere.VertexCount, r.Mesh.VertexCount);
        var (got, unmatched) = InReferenceIndices(r.Mesh, sphere, off, 1000.0, 1e-9);
        Assert.Equal(0, unmatched);
        Assert.True(Canonical(sphere).SetEquals(got));
    }

    // ---------------------------------------------------------------- orientation

    [Fact]
    public void ClosedSphere_MostFacesFlipped_IncludingFaceZero_TurnsOutward()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        var rnd = new Random(11);
        var faces = sphere.Faces.Select((x, i) => i == 0 || rnd.NextDouble() < 0.7 ? x.Reverse().ToArray() : x).ToArray();
        int flippedIn = faces.Where((x, i) => x[0] != sphere.Faces[i][0] || x[1] != sphere.Faces[i][1]).Count();
        Assert.True(flippedIn > sphere.FaceCount / 2);

        var r = MeshCleanup.Compute(new MeshData(sphere.Vertices, faces));
        Assert.True(Canonical(sphere).SetEquals(Canonical(r.Mesh)));
        Assert.Equal(flippedIn, r.Info.FlippedFaces);
        Assert.True(SignedVolume(r.Mesh) > 0);

        // Mean curvature keeps its sign: +1/R on the outward unit sphere, exactly as on the clean mesh
        var hClean = MeanCurvature.Compute(sphere).Values;
        var hFixed = MeanCurvature.Compute(r.Mesh).Values;
        for (int i = 0; i < hClean.Length; i++) Assert.Equal(hClean[i], hFixed[i], 12);

        // A sphere given entirely inside out is turned outward too
        var inside = MeshCleanup.Compute(Reversed(sphere));
        Assert.True(Canonical(sphere).SetEquals(Canonical(inside.Mesh)));
        Assert.Equal(sphere.FaceCount, inside.Info.FlippedFaces);
    }

    [Fact]
    public void OpenSaddle_FaceZeroFlipped_KeepsTheMajorityWinding()
    {
        // The old breadth-first unification took face 0 as the reference, so one flipped
        // face 0 reversed every other face of an open mesh
        var saddle = TestMeshes.CreateSaddle(16);
        var rnd = new Random(2);
        var faces = saddle.Faces.Select((x, i) => i == 0 || rnd.NextDouble() < 0.3 ? x.Reverse().ToArray() : x).ToArray();
        int flippedIn = faces.Where((x, i) => Canon(x) != Canon(saddle.Faces[i])).Count();
        var r = MeshCleanup.Compute(new MeshData(saddle.Vertices, faces));
        Assert.True(Canonical(saddle).SetEquals(Canonical(r.Mesh)));
        Assert.Equal(flippedIn, r.Info.FlippedFaces);

        // ... and the reversed saddle stays reversed (an open sheet has no outside)
        var rev = Reversed(saddle);
        var r2 = MeshCleanup.Compute(rev);
        Assert.True(Canonical(rev).SetEquals(Canonical(r2.Mesh)));
        Assert.Equal(0, r2.Info.FlippedFaces);
    }

    [Fact]
    public void TwoSpheres_OneInsideOut_BothOutward_TwoClosedParts()
    {
        var s = TestMeshes.CreateUnitSphere(8);
        int n = s.VertexCount;
        var verts = s.Vertices.Concat(s.Vertices.Select(p => p + new Vec3d(5, 0, 0))).ToArray();
        var faces = s.Faces.Concat(s.Faces.Select(f => f.Reverse().Select(x => x + n).ToArray())).ToArray();
        var r = MeshCleanup.Compute(new MeshData(verts, faces));
        Assert.Equal(2, r.Info.Components);
        Assert.Equal(2, r.Info.ClosedComponents);
        Assert.Equal(s.FaceCount, r.Info.FlippedFaces);
        // each part encloses +4/3·π·(faceted) volume
        var a = new MeshData(r.Mesh.Vertices, r.Mesh.Faces.Take(s.FaceCount).ToArray());
        Assert.True(SignedVolume(a) > 0);
        Assert.True(SignedVolume(r.Mesh) > 1.9 * SignedVolume(s));
    }

    [Fact]
    public void Mobius_ReportsExactlyOneOrientationConflict_AndOneBorderLoop()
    {
        int n = 40;
        var v = new List<Vec3d>();
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            for (int s = -1; s <= 1; s += 2)
            {
                double w = 0.3 * s;
                v.Add(new Vec3d((2 + w * Math.Cos(t / 2)) * Math.Cos(t), (2 + w * Math.Cos(t / 2)) * Math.Sin(t), w * Math.Sin(t / 2)));
            }
        }
        var f = new List<int[]>();
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i, b = 2 * i + 1;
            int c, d;
            if (i < n - 1) { c = 2 * (i + 1); d = 2 * (i + 1) + 1; }
            else { c = 1; d = 0; } // the half twist: the rung comes back swapped
            f.Add(new[] { a, c, d }); f.Add(new[] { a, d, b });
        }
        var r = MeshCleanup.Compute(new MeshData(v.ToArray(), f.ToArray()));
        Assert.Equal(1, r.Info.OrientationConflicts);
        Assert.Equal(1, r.Info.BorderLoops);
        Assert.Equal(0, r.Info.ClosedComponents);
        Assert.Contains("Möbius", r.Info.Report);
    }

    [Fact]
    public void NonManifoldFin_IsReported_AndNotCrossedByTheWinding()
    {
        // Two flat squares meeting a vertical fin along the edge x = 1: three faces on edges of that line
        var v = new[]
        {
            new Vec3d(0, 0, 0), new Vec3d(1, 0, 0), new Vec3d(1, 1, 0), new Vec3d(0, 1, 0),
            new Vec3d(2, 0, 0), new Vec3d(2, 1, 0), new Vec3d(1, 0, 1), new Vec3d(1, 1, 1),
        };
        var f = new[]
        {
            new[] { 0, 1, 2, 3 }, new[] { 1, 4, 5, 2 }, new[] { 1, 2, 7, 6 },
        };
        var r = MeshCleanup.Compute(new MeshData(v, f));
        Assert.Single(r.Info.NonManifoldEdges);
        var (a, b) = r.Info.NonManifoldEdges[0];
        Assert.Equal(new Vec3d(1, 0, 0), r.Mesh.Vertices[a]);
        Assert.Equal(new Vec3d(1, 1, 0), r.Mesh.Vertices[b]);
        Assert.Equal(0, r.Info.FlippedFaces); // nothing is propagated across the fin
        Assert.Contains("non-manifold", r.Info.Report);
    }

    [Fact]
    public void Annulus_HasTwoBorderLoops()
    {
        int n = 32;
        var v = new List<Vec3d>();
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            v.Add(new Vec3d(Math.Cos(t), Math.Sin(t), 0));
            v.Add(new Vec3d(2 * Math.Cos(t), 2 * Math.Sin(t), 0));
        }
        var f = new List<int[]>();
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i, b = a + 1, c = (2 * i + 3) % (2 * n), d = (2 * i + 2) % (2 * n);
            f.Add(new[] { a, b, c, d });
        }
        var r = MeshCleanup.Compute(new MeshData(v.ToArray(), f.ToArray()));
        Assert.Equal(2, r.Info.BorderLoops);
        Assert.Equal("Clean: nothing to repair.", r.Info.Report);
    }

    // ---------------------------------------------------------------- slivers

    [Fact]
    public void Needles_CollapseBackToTheCleanSphere_Exactly()
    {
        var sphere = TestMeshes.CreateUnitSphere(16);
        var (dirty, splits) = WithNeedles(sphere, 1e-4, 5);
        Assert.True(splits >= 40, $"{splits} splits");
        Assert.True(MinAngleDeg(dirty) < 0.01);

        // Left alone, the needles are reported with the fix
        var report = MeshCleanup.Compute(dirty);
        Assert.Equal(2 * splits, report.Info.SliversLeft);
        Assert.Contains("set Sliver to 1", report.Info.Report);

        var r = MeshCleanup.Compute(dirty, new MeshCleanup.Options { SliverAngle = 1.0 });
        Assert.Equal(splits, r.Info.NeedlesCollapsed);
        Assert.Equal(0, r.Info.CapsFlipped);
        Assert.Equal(0, r.Info.SliversLeft);
        Assert.Equal(sphere.VertexCount, r.Mesh.VertexCount);
        var (faces, unmatched) = InReferenceIndices(r.Mesh, sphere, tol: 0.0);
        Assert.Equal(0, unmatched);                      // no vertex moved: the split points went
        Assert.True(Canonical(sphere).SetEquals(faces)); // and the original triangles came back
        // every split point maps to the end it was next to
        for (int i = sphere.VertexCount; i < dirty.VertexCount; i++)
        {
            int k = r.VertexMap[i];
            Assert.True(k >= 0);
            Assert.True((r.Mesh.Vertices[k] - dirty.Vertices[i]).Length < 1e-3);
        }

        // Mean curvature: garbage at the needles before, the clean values after
        var hClean = MeanCurvature.Compute(sphere).Values;
        var hDirty = MeanCurvature.Compute(dirty).Values;
        double errDirty = hDirty.Take(sphere.VertexCount).Select((h, i) => Math.Abs(h - hClean[i])).Max();
        Assert.True(errDirty > 0.05, $"dirty H error {errDirty}");
    }

    [Fact]
    public void Caps_FlipAway_OnAFlatGrid_AreaAndVerticesUnchanged()
    {
        int n = 8;
        var grid = FlatGrid(n);
        var v = grid.Vertices.ToList();
        var f = new List<int[]>();
        var capCells = new HashSet<int> { 9, 12, 27, 30, 45, 49 }; // interior cells, no two adjacent
        double delta = 1e-5;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = b + n + 1, d = a + n + 1;
                if (!capCells.Contains(j * n + i)) { f.Add(new[] { a, b, c }); f.Add(new[] { a, c, d }); continue; }
                // m sits δ off the diagonal a–c towards b: (a, m, c) is a cap with apex m
                Vec3d mid = 0.5 * (v[a] + v[c]);
                Vec3d toB = (v[b] - mid).Normalized();
                int m = v.Count;
                v.Add(mid + delta * toB);
                f.Add(new[] { a, b, m }); f.Add(new[] { b, c, m }); f.Add(new[] { a, m, c }); f.Add(new[] { a, c, d });
            }
        var dirty = new MeshData(v.ToArray(), f.ToArray());
        double area0 = Area(dirty);
        Assert.Equal(1.0, area0, 12);

        var r = MeshCleanup.Compute(dirty, new MeshCleanup.Options { SliverAngle = 1.0 });
        Assert.Equal(capCells.Count, r.Info.CapsFlipped);
        Assert.Equal(0, r.Info.NeedlesCollapsed);
        Assert.Equal(0, r.Info.SliversLeft);
        Assert.Equal(dirty.VertexCount, r.Mesh.VertexCount);
        Assert.Equal(dirty.FaceCount, r.Mesh.FaceCount);
        Assert.Equal(area0, Area(r.Mesh), 13);
        Assert.True(MinAngleDeg(r.Mesh) > 1.0);
        foreach (var face in r.Mesh.Faces)
        {
            Vec3d nrm = Vec3d.Cross(r.Mesh.Vertices[face[1]] - r.Mesh.Vertices[face[0]], r.Mesh.Vertices[face[2]] - r.Mesh.Vertices[face[0]]);
            Assert.True(nrm.Z > 0); // no fold, winding kept
        }
        Assert.Equal(1, r.Info.BorderLoops);
    }

    [Fact]
    public void BorderCap_IsDropped_LosingExactlyItsOwnArea()
    {
        int n = 6;
        var grid = FlatGrid(n);
        var v = grid.Vertices.ToList();
        var f = new List<int[]>();
        double delta = 1e-5;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = b + n + 1, d = a + n + 1;
                if (j != 0 || i != 2) { f.Add(new[] { a, b, c }); f.Add(new[] { a, c, d }); continue; }
                // bottom row: a–b lies on the border y = 0; m sits δ above its middle
                int m = v.Count;
                v.Add(0.5 * (v[a] + v[b]) + new Vec3d(0, delta, 0));
                f.Add(new[] { a, b, m });   // the border cap
                f.Add(new[] { b, c, m }); f.Add(new[] { m, c, a }); f.Add(new[] { a, c, d });
            }
        var dirty = new MeshData(v.ToArray(), f.ToArray());
        var r = MeshCleanup.Compute(dirty, new MeshCleanup.Options { SliverAngle = 1.0 });
        Assert.Equal(1, r.Info.CapsRemoved);
        Assert.Equal(0, r.Info.SliversLeft);
        double cell = 1.0 / n;
        Assert.Equal(0.5 * cell * delta, Area(dirty) - Area(r.Mesh), 13);
        Assert.Equal(1, r.Info.BorderLoops);
    }

    [Fact]
    public void Slivers_Off_ByDefault_MeshUntouched()
    {
        var sphere = TestMeshes.CreateUnitSphere(10);
        var (dirty, splits) = WithNeedles(sphere, 1e-4, 7);
        var r = MeshCleanup.Compute(dirty);
        Assert.Equal(dirty.VertexCount, r.Mesh.VertexCount);
        Assert.Equal(dirty.FaceCount, r.Mesh.FaceCount);
        Assert.Equal(0, r.Info.NeedlesCollapsed);
        Assert.True(splits > 0);
    }

    [Fact]
    public void CleanMeshes_ReportNothing_AndKeepEveryFace()
    {
        foreach (var m in new[] { TestMeshes.CreateUnitSphere(12), TestMeshes.CreateSaddle(10), TestMeshes.CreateTorus(), TestMeshes.CreateQuadGrid(4, 4) })
        {
            var r = MeshCleanup.Compute(m, new MeshCleanup.Options { SliverAngle = 1.0 });
            Assert.Equal(m.VertexCount, r.Mesh.VertexCount);
            Assert.True(Canonical(m).SetEquals(Canonical(r.Mesh)) || Canonical(Reversed(m)).SetEquals(Canonical(r.Mesh)));
            Assert.Equal(0, r.Info.NeedlesCollapsed + r.Info.CapsFlipped + r.Info.CapsRemoved);
        }
    }
    [Fact]
    public void LargeSoup_CleansInSeconds_NotMinutes()
    {
        // Edge keys (a << 32 | b) hash to a ^ b with long.GetHashCode; with the mixing
        // comparer this 55 k-face soup takes well under a second (a 75 k-face soup took 31.6 s before)
        var sphere = TestMeshes.CreateUnitSphere(96);
        var soup = Soup(sphere);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = MeshCleanup.Compute(soup, new MeshCleanup.Options { SliverAngle = 1.0 });
        sw.Stop();
        Assert.Equal(sphere.VertexCount, r.Mesh.VertexCount);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
