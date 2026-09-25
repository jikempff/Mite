using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Tests;

/// <summary>
/// Coverage of the web layouts: after the border (or the seed cross), further
/// seed crosses are laid through the farthest uncovered point, so the whole
/// usable region ends up within 0.75 Spacing of a curve — while every curve
/// still runs to the border or the K = 0 line and none stops on a neighbour.
/// </summary>
public class WebCoverageTests
{
    private readonly ITestOutputHelper _out;
    public WebCoverageTests(ITestOutputHelper o) { _out = o; }

    internal static (double uncovered, int interiorEnds, int ends) Coverage(MeshData mesh, bool[] mask, List<Vec3d[]> curves, double s)
    {
        var proj = new MeshProjection(mesh);
        var pts = curves.SelectMany(c => c).ToArray();
        // grid hash
        double cell = s;
        var grid = new Dictionary<(int, int, int), List<Vec3d>>();
        foreach (var p in pts) { var k = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell)); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vec3d>(); l.Add(p); }
        int n = 0, un = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (!mask[i]) continue;
            n++;
            var v = mesh.Vertices[i];
            double best = double.MaxValue;
            int cx = (int)Math.Floor(v.X / cell), cy = (int)Math.Floor(v.Y / cell), cz = (int)Math.Floor(v.Z / cell);
            for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)
                if (grid.TryGetValue((cx + a, cy + b, cz + c), out var l)) foreach (var p in l) best = Math.Min(best, (p - v).Length);
            if (best > 0.75 * s) un++;
        }
        int ends = 0, interior = 0;
        foreach (var c in curves)
        {
            if ((c[0] - c[^1]).LengthSquared < 1e-18) continue;
            foreach (var e in new[] { c[0], c[^1] })
            {
                ends++;
                var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e));
                if (!proj.IsOnBoundary(h, 1e-4)) interior++;
            }
        }
        return (n > 0 ? (double)un / n : 0, interior, ends);
    }

    [Theory]
    [InlineData("saddle")]
    [InlineData("monkey")]
    [InlineData("hyperboloid")]
    [InlineData("catenoid")]
    [InlineData("enneper3")]
    [InlineData("schwarzd")]
    [InlineData("torus")]
    public void Webs_CoverTheRegion_WithoutTJunctions(string name)
    {
        var mesh = AnalyticShapes.Build(name, 0, 0, 0, 40);
        var proj = new MeshProjection(mesh);
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
        double s = mesh.BoundingBoxDiagonal() / 25.0;
        var nbrs = mesh.BuildVertexNeighbors();
        var border = mesh.BuildBoundaryVertexFlags();
        foreach (var layout in new[] { NetLayout.WebBorder, NetLayout.WebCross })
        {
            var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family2);
            var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family1);
            var ca = Coverage(mesh, field.Exists, a, s); var cb = Coverage(mesh, field.Exists, b, s);
            _out.WriteLine($"{name} {layout}: {a.Count}+{b.Count} curves, uncovered {ca.uncovered:P1} / {cb.uncovered:P1}");
            // before gap filling a single seed cross left 20–94 % of the region uncovered
            Assert.True(ca.uncovered < 0.012 && cb.uncovered < 0.012, $"{name} {layout}: {ca.uncovered:P1} / {cb.uncovered:P1} of the region farther than 0.75 Spacing from a curve");
            // a web never stops a curve on a neighbour: every end is on the border or at the
            // edge of the usable region (K = 0 line / minimum crossing angle)
            int contacts = 0;
            foreach (var (fam, c) in a.Select(c => (a, c)).Concat(b.Select(c => (b, c))))
            {
                if ((c[0] - c[^1]).LengthSquared < 1e-18) continue;
                foreach (var e in new[] { c[0], c[^1] })
                {
                    var h = proj.ClosestPoint(e, proj.NearestVertexGlobal(e));
                    // jagged marching-tetrahedra borders: an end within a fraction of an edge of the border is on it
                    if (proj.IsOnBoundary(h, 1e-3)) continue;
                    if (Enumerable.Range(0, mesh.VertexCount).Any(i => border[i] && (mesh.Vertices[i] - e).Length < 0.2 * proj.AverageEdgeLength)) continue;
                    var face = mesh.Faces[h.Face];
                    bool nearRegionEdge = false;
                    foreach (int v in face) { if (!field.Exists[v]) nearRegionEdge = true; foreach (int w in nbrs[v]) if (!field.Exists[w]) nearRegionEdge = true; }
                    if (nearRegionEdge) continue;
                    // the one legitimate interior end: two curves of one family converge until
                    // they would touch (closer than the merge distance) — a lath cannot run
                    // through its neighbour of the same layer
                    bool contact = fam.Any(o => !ReferenceEquals(o, c) && o.Any(q => (q - e).Length < 0.16 * s));
                    Assert.True(contact, $"{name} {layout}: a curve ends inside the region at ({e.X:F3}, {e.Y:F3}, {e.Z:F3}) without touching its own family");
                    contacts++;
                }
            }
            int allEnds = a.Concat(b).Count(c => (c[0] - c[^1]).LengthSquared >= 1e-18) * 2;
            _out.WriteLine($"  {contacts} of {allEnds} ends where the family converges onto itself");
            // near a flat point the curves on either side of a separatrix leave it together
            // (monkey saddle, three separatrices: 5–7 of ~150 ends); elsewhere 0–1
            Assert.True(contacts <= Math.Max(4, allEnds / 20), $"{contacts} of {allEnds} contact ends — a web should have almost none");
        }
    }

    [Fact(Skip = "diagnostic table: coverage of all layouts on ten surfaces")]
    public void Table()
    {
        foreach (var name in new[] { "saddle", "monkey", "wave", "hyperboloid", "catenoid", "enneper", "enneper3", "ruled", "schwarzd", "torus" })
        {
            var mesh = AnalyticShapes.Build(name, 0, 0, 0, 40);
            var pc = PrincipalCurvature.Compute(mesh, 2);
            var field = AsymptoticCurves.ComputeDirections(pc, mesh, 15.0);
            double s = mesh.BoundingBoxDiagonal() / 25.0;
            foreach (var layout in new[] { NetLayout.Fill, NetLayout.WebBorder, NetLayout.WebCross })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var oa = new EvenlySpacedNet.Options { Spacing = s, Layout = layout };
                var a = EvenlySpacedNet.TraceField(mesh, field.Family1, field.Exists, -1, oa, field.Family2);
                var b = EvenlySpacedNet.TraceField(mesh, field.Family2, field.Exists, -1, new EvenlySpacedNet.Options { Spacing = s, Layout = layout }, field.Family1);
                sw.Stop();
                var ca = Coverage(mesh, field.Exists, a, s); var cb = Coverage(mesh, field.Exists, b, s);
                _out.WriteLine($"{name,-11} {layout,-9} A {a.Count,3} B {b.Count,3}  uncovered A {ca.uncovered:P1} B {cb.uncovered:P1}  interior ends {ca.interiorEnds + cb.interiorEnds}/{ca.ends + cb.ends}  {sw.ElapsedMilliseconds} ms");
            }
        }
    }
}
