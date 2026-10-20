using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Nesting the strips of a flat kit on the laser bed: first-fit decreasing
/// shelves (Berkey &amp; Wang 1987 finite first-fit; Dósa 2007 for the FFD
/// bound), strips longer than the bed along its diagonal or split into
/// lapped pieces between the joints, and the material use that results.
/// Checked where the answer is exact: synthetic strip lists with known
/// optimal packings, the closed form of the diagonal lane, pieces that
/// reassemble to the strip, and the 3-fold Enneper kit against the area
/// bound and against the next-fit rule of 1.3.2.
/// </summary>
public class KitNestingTests
{
    private static FlatKit.Result Kit(params (double length, double width)[] strips)
    {
        var kit = new FlatKit.Result();
        for (int i = 0; i < strips.Length; i++)
            kit.Strips.Add(new FlatKit.Strip { Label = "A" + (i + 1), Lath = i, FamilyA = true, Length = strips[i].length, Width = strips[i].width });
        return kit;
    }

    private static FlatKit.Result Enneper3Kit()
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var w = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options());
        var proj = new MeshProjection(mesh);
        var (net, motion, anchor) = FlatKit.Press(w.A, w.B, w.Seed, new Vec3d(0, 0, 1), q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
        return FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), new FlatKit.Options { Width = 0.1, Thickness = 0.01, Upright = true });
    }

    /// <summary>The shelf rule of 1.3.2: longest first, a new row whenever the strip does not fit the current one (next-fit decreasing).</summary>
    private static int NextFitSheets(IEnumerable<FlatKit.Strip> strips, double mm, double SW, double SH, double G)
    {
        int sheets = 0; double x = 0, y = 0, rowH = 0; bool open = false;
        foreach (var s in strips.Where(s => s.Length * mm + 2 * G <= SW).OrderByDescending(s => s.Length))
        {
            double L = s.Length * mm, W = s.Width * mm;
            if (!open || x + L + G > SW)
            {
                if (open) { y += rowH + G; x = G; rowH = 0; }
                if (!open || y + W + G > SH) { sheets++; open = true; y = G; x = G; rowH = 0; }
            }
            x += L + G; rowH = Math.Max(rowH, W);
        }
        return sheets;
    }

    /// <summary>Corners of a placed strip's bounding rectangle (its outline's box), mm.</summary>
    private static (double x, double y)[] Corners(KitExport.Placement p, double mm)
    {
        double L = p.Strip.Length * mm, W = p.Strip.Width * mm, c = Math.Cos(p.Angle), s = Math.Sin(p.Angle);
        (double, double) T(double x, double y) => (p.X + x * c - y * s, p.Y + x * s + y * c);
        return new[] { T(0, 0), T(L, 0), T(L, W), T(0, W) };
    }

    /// <summary>Separation of two convex polygons along their edge normals (positive = the gap between them, negative = they overlap).</summary>
    private static double Separation((double x, double y)[] a, (double x, double y)[] b)
    {
        double best = double.NegativeInfinity;
        foreach (var poly in new[] { a, b })
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length];
                double nx = q.y - p.y, ny = -(q.x - p.x), len = Math.Sqrt(nx * nx + ny * ny); nx /= len; ny /= len;
                double amin = a.Min(v => v.x * nx + v.y * ny), amax = a.Max(v => v.x * nx + v.y * ny);
                double bmin = b.Min(v => v.x * nx + v.y * ny), bmax = b.Max(v => v.x * nx + v.y * ny);
                best = Math.Max(best, Math.Max(bmin - amax, amin - bmax));
            }
        return best;
    }

    private static void AssertWellPlaced(KitExport.Nesting n, double mm)
    {
        double G = 4;
        foreach (var p in n.Placements)
        {
            Assert.InRange(p.Sheet, 0, n.SheetCount - 1);
            foreach (var (x, y) in Corners(p, mm))
            {
                Assert.InRange(x, G - 1e-6, n.SheetWidth - G + 1e-6);
                Assert.InRange(y, G - 1e-6, n.SheetHeight - G + 1e-6);
            }
        }
        for (int i = 0; i < n.SheetCount; i++)
        {
            var on = n.Placements.Where(p => p.Sheet == i).ToList();
            for (int a = 0; a < on.Count; a++)
                for (int b = a + 1; b < on.Count; b++)
                    Assert.InRange(Separation(Corners(on[a], mm), Corners(on[b], mm)), G - 1e-6, double.MaxValue);
        }
        // every strip to cut is placed exactly once, and the area adds up
        Assert.Equal(n.Strips.Count - n.TooLong, n.Placements.Count);
        Assert.Equal(n.Placements.Select(p => p.Strip).Distinct().Count(), n.Placements.Count);
        Assert.Equal(n.Placements.Sum(p => KitExport.StripArea(p.Strip, mm)), n.StripArea, 6);
    }

    [Fact]
    public void FirstFit_BeatsNextFit_OnTheClassicList_AndFillsTheRowExactly()
    {
        // 0.6, 0.5, 0.4, 0.3, 0.2 of the row: next-fit decreasing takes 3 rows ([0.6], [0.5, 0.4], [0.3, 0.2]),
        // first-fit decreasing 2 ([0.6, 0.4], [0.5, 0.3, 0.2]), both rows full to the millimetre
        var kit = Kit((360, 50), (300, 50), (240, 50), (180, 50), (120, 50));
        var o = new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 100, Gap = 0 };
        var n = KitExport.Nest(kit, o);
        Assert.Equal(2, n.Rows);
        Assert.Equal(1, n.SheetCount);
        Assert.Equal(2, NextFitSheets(kit.Strips, 1, 600, 100, 0));
        Assert.Equal(1.0, n.Utilisation, 12);
        Assert.Equal(1, n.AreaBound);
        var rows = n.Placements.GroupBy(p => p.Y).OrderBy(g => g.Key).Select(g => g.OrderBy(p => p.X).Select(p => p.Strip.Length).ToArray()).ToList();
        Assert.Equal(new[] { 360.0, 240 }, rows[0]);
        Assert.Equal(new[] { 300.0, 180, 120 }, rows[1]);
        Assert.All(n.Placements, p => Assert.Equal(0, p.Angle));
    }

    [Fact]
    public void MixedWidths_ShelvesByWidthThenLength_NarrowStripsFillTheWideShelves()
    {
        var kit = Kit((400, 50), (400, 50), (150, 30), (150, 30), (300, 30), (300, 30));
        var o = new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 200, Gap = 0 };
        var n = KitExport.Nest(kit, o);
        // two 50-high shelves take the 400s; the 300s need their own 30-high shelf; the 150s fit beside the 400s
        Assert.Equal(3, n.Rows);
        Assert.Equal(1, n.SheetCount);
        var shelves = n.Placements.GroupBy(p => p.Y).OrderBy(g => g.Key).ToList();
        Assert.Equal(new[] { 0.0, 50, 100 }, shelves.Select(g => g.Key).ToArray());
        Assert.Equal(new[] { 400.0, 150 }, shelves[0].OrderBy(p => p.X).Select(p => p.Strip.Length).ToArray());
        Assert.Equal(new[] { 400.0, 150 }, shelves[1].OrderBy(p => p.X).Select(p => p.Strip.Length).ToArray());
        Assert.Equal(new[] { 300.0, 300 }, shelves[2].OrderBy(p => p.X).Select(p => p.Strip.Length).ToArray());
        Assert.Equal((2 * 400 * 50 + 2 * 150 * 30 + 2 * 300 * 30) / (600.0 * 200), n.Utilisation, 12);
    }

    [Fact]
    public void Gap_IsKeptToTheSheetEdge_AndBetweenStrips()
    {
        var kit = Kit((285, 20), (285, 20), (285, 20), (285, 20));
        var o = new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 60, Gap = 10 };
        var n = KitExport.Nest(kit, o);
        // 10 + 285 + 10 + 285 + 10 = 600: two per row to the millimetre; a second row would end at 10 + 20 + 10 + 20 + 10 = 70 > 60 → a second sheet
        Assert.Equal(2, n.SheetCount);
        Assert.Equal(2, n.Rows);
        Assert.All(n.Placements, p => Assert.Equal(10, p.Y));
        Assert.Equal(new[] { 10.0, 305 }, n.Placements.Where(p => p.Sheet == 0).Select(p => p.X).OrderBy(v => v).ToArray());
        AssertWellPlaced(n, 1);
        Assert.Equal(2 * 2 * 285 * 20 / (2 * 600.0 * 60), n.Utilisation, 12);
        Assert.Contains("32", n.Sheets[0].Title);
        // one millimetre more and only one fits a row
        var n2 = KitExport.Nest(Kit((286, 20), (286, 20)), o);
        Assert.Equal(2, n2.Rows);
    }

    [Fact]
    public void LaneLength_MatchesTheClosedForm_OnTheCentreLine_AndIsMaximalOffIt()
    {
        double SW = 600, SH = 400, G = 0, w = 30;
        // without a gap the lane runs along the diagonal itself: a line parallel to it at offset e meets the
        // long edge and the short edge, its chord is len − e·len²/(W H); the strip's two edges at ±w/2 clip its
        // start and its end by (w/2)·W/H each (the larger of W/H and H/W), so the strip gets len − w·W/H
        double len = Math.Sqrt(SW * SW + SH * SH);
        Assert.Equal(len - w * SW / SH, KitExport.LaneLength(SW, SH, G, w, 0, out double t0), 9);
        Assert.Equal(-0.5 * len + 0.5 * w * SW / SH, t0, 9);
        Assert.Equal(len, KitExport.LaneLength(SW, SH, 0, 0, 0, out _), 9);
        // 650 mm strips of 30 mm on a 600 × 400 bed with a 4 mm gap fit the centre lane (661.7 mm), 700 mm ones do not
        Assert.InRange(KitExport.LaneLength(600, 400, 4, 30, 0, out _), 661, 662);
        // with the gap, and off the centre line: the strip's corners lie inside the sheet minus the gap, and 1 µm more would not
        G = 4;
        foreach (double d in new[] { 0.0, 40, -40, 90, -120 })
        {
            double L = KitExport.LaneLength(SW, SH, G, w, d, out double t);
            Assert.True(L > 0);
            double th = Math.Atan2(SH, SW), ux = Math.Cos(th), uy = Math.Sin(th), nx = -uy, ny = ux;
            bool Inside(double x, double y, double tol) => x >= G - tol && x <= SW - G + tol && y >= G - tol && y <= SH - G + tol;
            foreach (double e in new[] { d - 0.5 * w, d + 0.5 * w })
                foreach (double s in new[] { t, t + L })
                {
                    double x = 0.5 * SW + e * nx + s * ux, y = 0.5 * SH + e * ny + s * uy;
                    Assert.True(Inside(x, y, 1e-9));
                }
            bool grown = false;
            foreach (double e in new[] { d - 0.5 * w, d + 0.5 * w })
                foreach (double s in new[] { t - 1e-3, t + L + 1e-3 })
                    if (!Inside(0.5 * SW + e * nx + s * ux, 0.5 * SH + e * ny + s * uy, 1e-9)) grown = true;
            Assert.True(grown, "the lane is not maximal");
        }
    }

    [Fact]
    public void StripsLongerThanTheBed_LieOnTheDiagonal_WhenThatCostsNoSheet()
    {
        // a 650 mm strip on a 600 mm bed: the centre lane holds 661.7 mm for a 30 mm strip; the short strips
        // take the lanes beside it (the lane at ±34 mm holds 587 mm: two 290s end to end)
        var kit = Kit((650, 30), (290, 30), (290, 30), (290, 30), (200, 30));
        var o = new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 400 };
        var n = KitExport.Nest(kit, o);
        Assert.Equal(1, n.DiagonalStrips);
        Assert.Equal(0, n.SplitStrips);
        Assert.Equal(0, n.TooLong);
        Assert.Equal(1, n.SheetCount);
        Assert.Equal(1, n.DiagonalSheets);
        Assert.Equal(5, n.Placements.Count);
        Assert.Equal(Math.Atan2(400, 600), n.Placements.First(p => p.Strip.Length == 650).Angle, 12);
        var lanes = n.Placements.GroupBy(p => Math.Round((p.Y - 200) * Math.Cos(p.Angle) - (p.X - 300) * Math.Sin(p.Angle) + 15, 6)).OrderBy(g => Math.Abs(g.Key)).ToList();
        Assert.Equal(0, lanes[0].Key, 6);
        Assert.Equal(650, lanes[0].Single().Strip.Length);
        Assert.Equal(34, Math.Abs(lanes[1].Key), 6);
        Assert.Equal(new[] { 290.0, 290 }, lanes[1].Select(p => p.Strip.Length).OrderBy(v => v).ToArray());
        AssertWellPlaced(n, 1);
        // the lanes also take the short strips: nothing is left for a shelf
        Assert.All(n.Placements, p => Assert.Equal(Math.Atan2(400, 600), p.Angle, 12));
        Assert.Contains("diagonal", n.Sheets[0].Title);
        // the drawing turns its labels with the strips, in SVG and in DXF
        string svg = KitExport.ToSvg(n.Sheets[0]);
        Assert.Contains("rotate(-33.69", svg);
        Assert.Equal("svg", XDocument.Parse(svg).Root!.Name.LocalName);
        Assert.Contains("\n50\n33.69", KitExport.ToDxf(n.Sheets));
        // without the diagonal they are split instead
        var plain = KitExport.Nest(kit, new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 400, Diagonal = false });
        Assert.Equal(1, plain.SplitStrips); Assert.Equal(2, plain.Pieces); Assert.Equal(0, plain.DiagonalStrips);
        Assert.Equal(1, plain.SheetCount);
    }

    [Fact]
    public void Diagonal_IsNotTaken_WhenItCostsASheet_ButBeatsALongerBed()
    {
        // one 650 mm strip and fourteen 290s: the diagonal sheet holds the long strip and twelve short ones in its
        // lanes, so the last two would need a second sheet; split, everything packs on one (9 shelves of 11)
        var strips = new List<(double, double)> { (650, 30) };
        for (int i = 0; i < 14; i++) strips.Add((290, 30));
        var kit = Kit(strips.ToArray());
        var o = new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 400 };
        var n = KitExport.Nest(kit, o);
        Assert.Equal(1, n.SheetCount);
        Assert.Equal(1, n.SplitStrips);
        Assert.Equal(0, n.DiagonalStrips);
        Assert.Equal(9, n.Rows);
        AssertWellPlaced(n, 1);
        // with ten 290s the diagonal sheet takes them all: whole strips win the tie
        var ten = KitExport.Nest(Kit(strips.Take(11).ToArray()), o);
        Assert.Equal(1, ten.SheetCount); Assert.Equal(1, ten.DiagonalStrips); Assert.Equal(0, ten.SplitStrips);
        AssertWellPlaced(ten, 1);
        // never split (Splice < 0): the diagonal is taken even at the price of a sheet, since the other choice is a longer bed
        var keep = KitExport.Nest(kit, new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 400, Splice = -1 });
        Assert.Equal(0, keep.TooLong); Assert.Equal(1, keep.DiagonalStrips); Assert.Equal(2, keep.SheetCount);
        var none = KitExport.Nest(kit, new KitExport.Options { ToMm = 1, SheetWidth = 600, SheetHeight = 400, Splice = -1, Diagonal = false });
        Assert.Equal(1, none.TooLong); Assert.Equal(1, none.SheetCount); Assert.Equal(2, none.Sheets.Count);
    }

    [Fact]
    public void Split_PiecesReassembleToTheStrip_EveryJointOnOnePiece_LapsBetweenJoints()
    {
        // a 1500 mm strip with slots every 100 mm on a 600 mm bed, 60 mm laps
        var s = new FlatKit.Strip { Label = "A7", Lath = 6, FamilyA = true, Length = 1500, Width = 40, ExtendStart = 10, ExtendEnd = 20 };
        for (int k = 1; k <= 14; k++) s.Marks.Add(new FlatKit.Mark(100 * k, FlatKit.MarkKind.SlotOuter, 10, 20, "B" + k, k));
        var pieces = KitExport.Split(s, 592, 60, 1, 4, out int clashes);
        Assert.Equal(0, clashes);
        Assert.True(pieces.Count >= 3);
        Assert.All(pieces, p => Assert.InRange(p.Length, 0, 592));
        Assert.Equal(new[] { "A7/1", "A7/2", "A7/3" }, pieces.Take(3).Select(p => p.Label).ToArray());
        // lengths: the strip plus one lap per cut
        Assert.Equal(1500 + 60 * (pieces.Count - 1), pieces.Sum(p => p.Length), 9);
        Assert.Equal(10, pieces[0].ExtendStart); Assert.Equal(0, pieces[0].ExtendEnd);
        Assert.Equal(20, pieces[pieces.Count - 1].ExtendEnd); Assert.Equal(0, pieces[pieces.Count - 1].ExtendStart);
        // every joint mark lands on exactly one piece, at its place on the strip
        double offset = 0;
        var seen = new List<int>();
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            foreach (var m in p.Marks)
            {
                Assert.InRange(m.At, 0, p.Length);
                if (m.Kind == FlatKit.MarkKind.Splice) continue;
                Assert.Equal(100 * m.Joint, m.At + offset, 9);
                seen.Add(m.Joint);
            }
            offset += p.Length - 60;
        }
        Assert.Equal(Enumerable.Range(1, 14), seen.OrderBy(v => v));
        // laps: two splice holes per cut on both pieces, at the quarter points of the lap, matching across the cut,
        // and no joint mark within the lap (half the lap plus half the slot from the cut)
        for (int i = 0; i + 1 < pieces.Count; i++)
        {
            var a = pieces[i]; var b = pieces[i + 1];
            var ha = a.Marks.Where(m => m.Kind == FlatKit.MarkKind.Splice && m.Label == b.Label).Select(m => m.At).OrderBy(v => v).ToArray();
            var hb = b.Marks.Where(m => m.Kind == FlatKit.MarkKind.Splice && m.Label == a.Label).Select(m => m.At).OrderBy(v => v).ToArray();
            Assert.Equal(2, ha.Length); Assert.Equal(2, hb.Length);
            Assert.Equal(30, ha[1] - ha[0], 9); Assert.Equal(15, hb[0], 9); Assert.Equal(45, hb[1], 9);
            Assert.Equal(a.Length - 45, ha[0], 9);
            double cut = a.Length - 30;
            Assert.DoesNotContain(a.Marks, m => m.Kind != FlatKit.MarkKind.Splice && Math.Abs(m.At - cut) < 30 + 5);
            Assert.DoesNotContain(b.Marks, m => m.Kind != FlatKit.MarkKind.Splice && Math.Abs(m.At - 30) < 30 + 5);
            Assert.All(ha.Concat(hb), h => Assert.True(h > 0 && h < a.Length));
        }
        // a strip that fits is returned as is; joints too dense for the lap are counted, not hidden
        Assert.Same(s, KitExport.Split(s, 2000, 60, 1, 4, out _)[0]);
        var dense = new FlatKit.Strip { Label = "B1", Length = 1000, Width = 40 };
        for (int k = 1; k < 100; k++) dense.Marks.Add(new FlatKit.Mark(10 * k, FlatKit.MarkKind.Hole, 6, 0, "A" + k, k));
        KitExport.Split(dense, 592, 60, 1, 4, out int dc);
        Assert.True(dc > 0);
    }

    [Fact]
    public void Enneper3Kit_At1To5_TakesTheAreaBound_NextFitTookOneMore()
    {
        var kit = Enneper3Kit();
        var o = new KitExport.Options { ToMm = 200, SheetWidth = 600, SheetHeight = 400 };
        var n = KitExport.Nest(kit, o);
        Assert.Equal(54, kit.Strips.Count);
        Assert.Equal(0, n.TooLong); Assert.Equal(0, n.SplitStrips); Assert.Equal(0, n.DiagonalStrips);
        Assert.Equal(2, n.SheetCount);
        Assert.Equal(n.AreaBound, n.SheetCount);
        Assert.Equal(3, NextFitSheets(kit.Strips, 200, 600, 400, 4));
        Assert.InRange(n.Utilisation, 0.60, 0.65);
        AssertWellPlaced(n, 200);
        Assert.Equal(n.Sheets.Count, n.SheetCount);
        Assert.Equal(2, n.Sheets.Count(d => d.Title.StartsWith("sheet")));
        Assert.Equal(kit.Strips.Count + 1, KitExport.CutList(kit, o).Trim().Split('\n').Length);
    }

    [Fact]
    public void Enneper3Kit_At1To2_5_EveryStripOnTheBed_LapsClearOfTheSlots()
    {
        var kit = Enneper3Kit();
        var o = new KitExport.Options { ToMm = 400, SheetWidth = 600, SheetHeight = 400 };
        int longer = kit.Strips.Count(s => s.Length * 400 + 8 > 600);
        Assert.Equal(30, longer);
        var n = KitExport.Nest(kit, o);
        Assert.Equal(0, n.TooLong);
        Assert.Equal(30, n.SplitStrips);
        Assert.Equal(60, n.Pieces);
        Assert.Equal(0, n.LapClashes);
        Assert.Equal(84, n.Strips.Count);
        Assert.InRange(n.Splice, 20, 3 * 40);
        Assert.InRange(n.SheetCount, n.AreaBound, n.AreaBound + 2);
        Assert.InRange(n.Utilisation, 0.65, 0.80);
        AssertWellPlaced(n, 400);
        // the laps never reach a slot (the doubled thickness would not pass the other strip's slot)
        foreach (var p in n.Strips.Where(s => s.Label.Contains("/")))
        {
            var holes = p.Marks.Where(m => m.Kind == FlatKit.MarkKind.Splice).ToList();
            Assert.True(holes.Count == 2 || holes.Count == 4);
            foreach (var g in holes.GroupBy(h => h.Label))
            {
                double lo = g.Min(h => h.At) - 0.25 * n.Splice / 400, hi = g.Max(h => h.At) + 0.25 * n.Splice / 400;
                Assert.DoesNotContain(p.Marks, m => m.Kind != FlatKit.MarkKind.Splice && m.At + 0.5 * m.Size > lo && m.At - 0.5 * m.Size < hi);
            }
        }
        // the pieces of a strip carry its joints once and reassemble to its length
        foreach (var s in kit.Strips.Where(s => s.Length * 400 + 8 > 600))
        {
            var ps = n.Strips.Where(p => p.Label.StartsWith(s.Label + "/")).ToList();
            Assert.Equal(2, ps.Count);
            Assert.Equal(s.Marks.Count, ps.Sum(p => p.Marks.Count(m => m.Kind != FlatKit.MarkKind.Splice)));
            Assert.Equal(s.Marks.Select(m => m.Joint).OrderBy(v => v), ps.SelectMany(p => p.Marks).Where(m => m.Kind != FlatKit.MarkKind.Splice).Select(m => m.Joint).OrderBy(v => v));
            Assert.Equal(s.Length + (ps.Count - 1) * n.Splice / 400, ps.Sum(p => p.Length), 6);
        }
        // the files follow the nesting: cut list rows = pieces + whole strips, one SVG per sheet, README names the laps
        var files = KitExport.Files(kit, n, KitExport.Assembly(kit, 0.01, o), KitExport.Deployment(kit), o);
        Assert.Equal(n.Strips.Count + 1, files.First(f => f.Name == "cutlist.csv").Text.Trim().Split('\n').Length);
        Assert.Equal(n.SheetCount, files.Count(f => f.Name.StartsWith("strips-")));
        Assert.Contains("lap over", files.First(f => f.Name == "README.txt").Text);
        Assert.Contains("splice", files.First(f => f.Name == "cutlist.csv").Text);
        // without splitting, the 1.3.2 behaviour: an overflow drawing
        var keep = KitExport.Nest(kit, new KitExport.Options { ToMm = 400, Splice = -1 });
        Assert.Equal(12, keep.DiagonalStrips); // the 600–624 mm strips fit the 661.7 mm centre lane
        Assert.Equal(18, keep.TooLong);
        Assert.Equal(0, keep.SplitStrips);
        Assert.Equal(keep.SheetCount + 1, keep.Sheets.Count);
        Assert.Equal(54, keep.Strips.Count);
    }

    [Fact]
    public void SpliceMark_IsAppendedToTheEnum()
    {
        Assert.Equal(4, (int)FlatKit.MarkKind.Splice);
        Assert.Equal(new[] { "SlotOuter", "SlotInner", "Hole", "Hub", "Splice" }, Enum.GetNames(typeof(FlatKit.MarkKind)));
    }
}
