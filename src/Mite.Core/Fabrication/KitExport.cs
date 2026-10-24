using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// Drawings of a <see cref="FlatKit"/> in millimetres, as SVG (laser
/// convention: red hairlines cut, blue engraves) and as DXF R12 (layers CUT
/// and ENGRAVE for the strips; A, B, JOINTS and LABELS for the assembly):
///  - strip sheets: every strip nested on sheets of the laser bed (see
///    <see cref="Nest"/>), with its slots or holes and its labels;
///  - the assembly drawing: the flat grid at 1 : 1 with every joint numbered
///    and every lath labelled — print it or lay the strips on it;
///  - the deployment sequence: the grid from flat to curved in a few
///    axonometric frames.
///
/// Nesting. Strips are rectangles of one or two widths, so the sheets are
/// packed by levels (shelves) in the finite-bin form of Berkey &amp; Wang
/// 1987 (Two-dimensional finite bin-packing algorithms, J. Oper. Res. Soc.
/// 38, "finite first-fit"): strips sorted by width, then length,
/// decreasing; each goes on the first shelf of the first sheet where it
/// fits, else on a new shelf of the first sheet with room, else on a new
/// sheet. With one strip width every shelf is a one-dimensional bin and the
/// rule is first-fit decreasing: FFD ≤ 11/9 OPT + 6/9 sheets-worth of
/// shelves (Johnson 1973; Dósa 2007, tight), against the asymptotic 1.691
/// of the next-fit decreasing rule used up to 1.3.2 (Baker &amp; Coffman
/// 1981); for shelves of mixed heights FFDH ≤ 1.7 OPT + 1 against 2 OPT + 1
/// for NFDH (Coffman, Garey, Johnson &amp; Tarjan 1980). A strip longer than
/// the bed is laid along the bed's diagonal when it fits there (a lane at
/// perpendicular offset d from the centre line is the chord of the sheet
/// rectangle at that offset, clipped by both long edges of the strip) and
/// otherwise split into pieces that fit a shelf, lapped over a splice of
/// <see cref="Options.Splice"/> mm with two pin holes each, the cut pulled
/// away from every joint by <see cref="LathSegmentation.Segment"/> (as the
/// finger and 1 : 7 scarf joints of the Downland and Savill gridshells sit
/// between the nodes; Tang, Chilton &amp; Beccarelli 2013). Material use is
/// the strips' outline area over the sheets' area.
/// </summary>
public static class KitExport
{
    public sealed class Options
    {
        /// <summary>Millimetres per model unit.</summary>
        public double ToMm { get; set; } = 1000;
        /// <summary>Sheet (laser bed) size in mm.</summary>
        public double SheetWidth { get; set; } = 600;
        public double SheetHeight { get; set; } = 400;
        /// <summary>Gap between strips and to the sheet edge, mm.</summary>
        public double Gap { get; set; } = 4;
        /// <summary>
        /// Lap splice length in mm for strips longer than the bed (after the diagonal is tried):
        /// 0 = automatic (three times the strip width, at least 20 mm); negative = never split,
        /// such strips go on a drawing of their own to be cut on a longer bed.
        /// </summary>
        public double Splice { get; set; }
        /// <summary>Lay strips longer than the bed along its diagonal when they fit there, whole, before splitting them.</summary>
        public bool Diagonal { get; set; } = true;
    }

    /// <summary>Where one strip lies on its sheet: its lower-left corner (mm, y up) and its angle (radians, anticlockwise).</summary>
    public sealed class Placement
    {
        public FlatKit.Strip Strip = null!;
        /// <summary>Index into <see cref="Nesting.Sheets"/>.</summary>
        public int Sheet;
        public double X, Y, Angle;
    }

    /// <summary>The strips nested on sheets, with the numbers of the nesting.</summary>
    public sealed class Nesting
    {
        /// <summary>The sheets (title "sheet k of n …"), then — only without splitting — the drawing of the strips longer than the bed.</summary>
        public List<Drawing> Sheets = new List<Drawing>();
        /// <summary>The strips to cut, in the kit's order: pieces replace the strips that were split.</summary>
        public List<FlatKit.Strip> Strips = new List<FlatKit.Strip>();
        public List<Placement> Placements = new List<Placement>();
        public double SheetWidth, SheetHeight;
        /// <summary>Sheets of the bed size (the overflow drawing not counted).</summary>
        public int SheetCount;
        /// <summary>Shelves (rows) over all sheets.</summary>
        public int Rows;
        /// <summary>Sheets laid out as diagonal lanes, and strips lying on a diagonal.</summary>
        public int DiagonalSheets, DiagonalStrips;
        /// <summary>Strips split into lapped pieces, and the pieces they became.</summary>
        public int SplitStrips, Pieces;
        /// <summary>Lap splice length used, mm (0 when nothing was split): the longest, and the shortest where the joints sit closer.</summary>
        public double Splice, SpliceMin;
        /// <summary>Laps that reach a joint mark: the joints were too close for a joint-free lap of <see cref="Splice"/> (shorten Splice, or raise the scale).</summary>
        public int LapClashes;
        /// <summary>Strips longer than the bed that were neither laid diagonally nor split (on the overflow drawing).</summary>
        public int TooLong;
        /// <summary>Outline area of the placed strips, mm².</summary>
        public double StripArea;
        /// <summary>Area of the sheets used, mm².</summary>
        public double SheetArea => SheetCount * SheetWidth * SheetHeight;
        /// <summary>Material that becomes strips: <see cref="StripArea"/> over <see cref="SheetArea"/>.</summary>
        public double Utilisation => SheetArea > 0 ? StripArea / SheetArea : 0;
        /// <summary>The area bound: no nesting of these strips takes fewer sheets.</summary>
        public int AreaBound => SheetWidth * SheetHeight > 0 ? (int)Math.Ceiling(StripArea / (SheetWidth * SheetHeight) - 1e-9) : 0;
    }

    // ------------------------------------------------------------------ primitives

    public enum Layer { Cut, Engrave, A, B, Joints, Labels, Guide }

    public abstract class Shape { public Layer Layer; }
    public sealed class Poly : Shape { public List<(double X, double Y)> Points = new(); public bool Closed; public double Width; }
    public sealed class Circle : Shape { public double X, Y, R; public bool Filled; }
    public sealed class Text : Shape { public double X, Y, Height; public string Value = ""; public bool Centre = true; /// <summary>Rotation, degrees anticlockwise about (X, Y).</summary>
        public double Angle; }

    public sealed class Drawing
    {
        public string Title = "";
        public double Width, Height;
        public List<Shape> Shapes = new();
        public void Line(Layer layer, IEnumerable<(double, double)> pts, bool closed = false, double width = 0) =>
            Shapes.Add(new Poly { Layer = layer, Points = pts.ToList(), Closed = closed, Width = width });
        public void Hole(Layer layer, double x, double y, double r, bool filled = false) => Shapes.Add(new Circle { Layer = layer, X = x, Y = y, R = r, Filled = filled });
        public void Label(Layer layer, double x, double y, double h, string s, bool centre = true, double angle = 0) => Shapes.Add(new Text { Layer = layer, X = x, Y = y, Height = h, Value = s, Centre = centre, Angle = angle });
    }

    // ------------------------------------------------------------------ strips

    /// <summary>The outline of one strip in its own frame (mm): x along the strip, y across, outer edge at y = width.</summary>
    public static List<(double X, double Y)> StripOutline(FlatKit.Strip s, double mm)
    {
        double L = s.Length * mm, W = s.Width * mm;
        var pts = new List<(double, double)> { (0, 0) };
        foreach (var m in s.Marks.Where(m => m.Kind == FlatKit.MarkKind.SlotInner).OrderBy(m => m.At))
        {
            double x = m.At * mm, h = 0.5 * m.Size * mm, d = Math.Min(m.Depth * mm, W);
            pts.Add((x - h, 0)); pts.Add((x - h, d)); pts.Add((x + h, d)); pts.Add((x + h, 0));
        }
        pts.Add((L, 0)); pts.Add((L, W));
        foreach (var m in s.Marks.Where(m => m.Kind == FlatKit.MarkKind.SlotOuter).OrderByDescending(m => m.At))
        {
            double x = m.At * mm, h = 0.5 * m.Size * mm, d = Math.Min(m.Depth * mm, W);
            pts.Add((x + h, W)); pts.Add((x + h, W - d)); pts.Add((x - h, W - d)); pts.Add((x - h, W));
        }
        pts.Add((0, W));
        // a notch that reaches a strip end (hub plates) repeats the corner: keep every point once
        var clean = new List<(double, double)>(pts.Count);
        foreach (var q in pts)
            if (clean.Count == 0 || Math.Abs(clean[clean.Count - 1].Item1 - q.Item1) > 1e-9 || Math.Abs(clean[clean.Count - 1].Item2 - q.Item2) > 1e-9) clean.Add(q);
        if (clean.Count > 1 && Math.Abs(clean[0].Item1 - clean[clean.Count - 1].Item1) < 1e-9 && Math.Abs(clean[0].Item2 - clean[clean.Count - 1].Item2) < 1e-9) clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    /// <summary>
    /// Draws one strip with its lower-left corner at (x0, y0), y up, outer edge
    /// on top, turned by angle (radians, anticlockwise) about that corner. Each
    /// joint is labelled with the lath it joins, beside its slot (in the
    /// slotted half, where the material between slots is free) or beside its
    /// hole; the strip's own label goes in the other half, in its longest free
    /// stretch, with an arrow to the outer edge.
    /// </summary>
    private static void DrawStrip(Drawing d, FlatKit.Strip s, double mm, double x0, double y0, double angle = 0)
    {
        double W = s.Width * mm, L = s.Length * mm;
        double ca = Math.Cos(angle), sa = Math.Sin(angle), deg = angle * 180 / Math.PI;
        (double, double) T(double x, double y) => (x0 + x * ca - y * sa, y0 + x * sa + y * ca);
        void Label(double x, double y, double h, string text, bool centre)
        {
            var (px, py) = T(x, y);
            d.Label(Layer.Engrave, px, py, h, text, centre, deg);
        }
        d.Line(Layer.Cut, StripOutline(s, mm).Select(p => T(p.X, p.Y)), closed: true);
        var xs = s.Marks.Select(m => m.At * mm).OrderBy(v => v).ToList();
        double pitch = double.MaxValue;
        for (int i = 0; i + 1 < xs.Count; i++) pitch = Math.Min(pitch, xs[i + 1] - xs[i]);
        double th = Math.Max(1.2, Math.Min(Math.Min(5, 0.24 * W), pitch < double.MaxValue ? 0.3 * pitch : 5));
        bool outer = s.Marks.Any(m => m.Kind == FlatKit.MarkKind.SlotOuter), inner = s.Marks.Any(m => m.Kind == FlatKit.MarkKind.SlotInner);
        foreach (var m in s.Marks)
        {
            double x = m.At * mm, half = 0.5 * m.Size * mm;
            switch (m.Kind)
            {
                case FlatKit.MarkKind.Hole:
                case FlatKit.MarkKind.Hub:
                case FlatKit.MarkKind.Splice:
                {
                    var (cx, cy) = T(x, 0.5 * W);
                    d.Hole(Layer.Cut, cx, cy, half);
                    if (m.Kind == FlatKit.MarkKind.Splice) break; // the piece label names the splice
                    Label(x + half + 0.3 * th, 0.5 * W - 0.35 * th, th, m.Kind == FlatKit.MarkKind.Hub ? "hub" : m.Label, false);
                    break;
                }
                case FlatKit.MarkKind.SlotOuter:
                    Label(x + half + 0.3 * th, W - Math.Min(m.Depth * mm, W) + 0.15 * th, th, m.Label, false);
                    break;
                case FlatKit.MarkKind.SlotInner:
                    Label(x + half + 0.3 * th, Math.Min(m.Depth * mm, W) - 1.15 * th, th, m.Label, false);
                    break;
            }
        }
        // the strip's own label in the longest free stretch of the half without slots
        var gaps = new List<double> { 0 }; gaps.AddRange(xs); gaps.Add(L);
        double bx = 0.5 * L, gap = 0;
        for (int i = 0; i + 1 < gaps.Count; i++) if (gaps[i + 1] - gaps[i] > gap) { gap = gaps[i + 1] - gaps[i]; bx = 0.5 * (gaps[i] + gaps[i + 1]); }
        double lh = Math.Max(1.5, Math.Min(Math.Min(10, 0.3 * W), 0.18 * gap));
        double ly = outer && !inner ? 0.25 * W : inner && !outer ? 0.75 * W : 0.5 * W;
        if (!outer && !inner) { bx = 0.5 * L; ly = 0.2 * W; lh = Math.Min(lh, 0.25 * W); }
        var splices = s.Marks.Where(m => m.Kind == FlatKit.MarkKind.Splice).Select(m => m.Label).Distinct().ToList();
        string text = s.Label + " ↑out" + (splices.Count > 0 ? " · lap " + string.Join(", ", splices) : "");
        lh = Math.Max(1.5, Math.Min(lh, gap / (0.62 * text.Length))); // Helvetica averages 0.55–0.6 em per character: keep the label inside its stretch
        Label(bx, ly - 0.35 * lh, lh, text, true);
    }

    /// <summary>Area of a strip's outline (its length × width less the slots), mm².</summary>
    public static double StripArea(FlatKit.Strip s, double mm)
    {
        var p = StripOutline(s, mm);
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.X * r.Y - r.X * q.Y; }
        return 0.5 * Math.Abs(a);
    }

    /// <summary>
    /// Length available to a strip of width w centred on the lane at
    /// perpendicular offset d from the sheet's diagonal (mm, positive towards
    /// the upper-left corner), the gap kept to the sheet edge; t0 is the
    /// lane parameter where the strip starts. Negative when nothing fits.
    /// </summary>
    public static double LaneLength(double sheetWidth, double sheetHeight, double gap, double w, double d, out double t0)
    {
        double len = Math.Sqrt(sheetWidth * sheetWidth + sheetHeight * sheetHeight);
        double ux = sheetWidth / len, uy = sheetHeight / len, nx = -uy, ny = ux;
        double cx = 0.5 * sheetWidth, cy = 0.5 * sheetHeight;
        double tmin = double.NegativeInfinity, tmax = double.PositiveInfinity;
        foreach (double e in new[] { d - 0.5 * w, d + 0.5 * w })
        {
            double px = cx + e * nx, py = cy + e * ny;
            tmin = Math.Max(tmin, Math.Max((gap - px) / ux, (gap - py) / uy));
            tmax = Math.Min(tmax, Math.Min((sheetWidth - gap - px) / ux, (sheetHeight - gap - py) / uy));
        }
        t0 = tmin;
        return tmax - tmin;
    }

    /// <summary>
    /// Splits a strip longer than stock (mm) into pieces that fit it, lapped
    /// over splice (mm) at every cut with two pin holes each (quarter points
    /// of the lap), the cuts kept clear of the joints (the lap must not reach a
    /// slot, where the doubled thickness would not pass). Pieces are labelled
    /// "A3/1", "A3/2", …; every joint mark lands on exactly one piece.
    /// </summary>
    public static List<FlatKit.Strip> Split(FlatKit.Strip s, double stock, double splice, double mm, double gap) => Split(s, stock, splice, mm, gap, out _);

    /// <summary>As <see cref="Split(FlatKit.Strip, double, double, double, double)"/>; clashes counts the laps that reach a joint mark because no joint-free stretch of the lap's length was available.</summary>
    public static List<FlatKit.Strip> Split(FlatKit.Strip s, double stock, double splice, double mm, double gap, out int clashes)
    {
        clashes = 0;
        double stockU = stock / mm, spliceU = splice / mm;
        if (s.Length <= stockU || spliceU <= 0 || spliceU >= stockU) return new List<FlatKit.Strip> { s };
        double maxMark = s.Marks.Count > 0 ? s.Marks.Max(m => m.Size) : 0;
        double margin = 0.5 * spliceU + 0.5 * maxMark + gap / mm;
        var line = new[] { Vec3d.Zero, new Vec3d(s.Length, 0, 0) };
        var seg = LathSegmentation.Segment(line, stockU, margin, s.Marks.Select(m => m.At).ToList(), spliceU);
        var cuts = seg.CutArcLengths;
        if (cuts.Length == 0) return new List<FlatKit.Strip> { s };
        foreach (double c in cuts)
            if (s.Marks.Any(m => Math.Abs(m.At - c) < 0.5 * spliceU + 0.5 * m.Size - 1e-9)) clashes++;
        double hole = Math.Min(0.25 * s.Width, 0.2 * spliceU);
        var pieces = new List<FlatKit.Strip>();
        for (int c = 0; c <= cuts.Length; c++)
        {
            double a = c == 0 ? 0 : cuts[c - 1] - 0.5 * spliceU;
            double b = c == cuts.Length ? s.Length : cuts[c] + 0.5 * spliceU;
            a = Math.Max(0, a); b = Math.Min(s.Length, b);
            var p = new FlatKit.Strip
            {
                Label = s.Label + "/" + (c + 1), Lath = s.Lath, FamilyA = s.FamilyA, Width = s.Width, Length = b - a,
                ExtendStart = c == 0 ? s.ExtendStart : 0, ExtendEnd = c == cuts.Length ? s.ExtendEnd : 0,
            };
            double lo = c == 0 ? double.NegativeInfinity : cuts[c - 1], hi = c == cuts.Length ? double.PositiveInfinity : cuts[c];
            foreach (var m in s.Marks.Where(m => m.At >= lo && m.At < hi).OrderBy(m => m.At))
                p.Marks.Add(new FlatKit.Mark(m.At - a, m.Kind, m.Size, m.Depth, m.Label, m.Joint));
            if (c > 0) AddSplice(p, cuts[c - 1] - a, spliceU, hole, s.Label + "/" + c);
            if (c < cuts.Length) AddSplice(p, cuts[c] - a, spliceU, hole, s.Label + "/" + (c + 2));
            p.Marks.Sort((u, v) => u.At.CompareTo(v.At));
            pieces.Add(p);
        }
        return pieces;
    }

    private static void AddSplice(FlatKit.Strip p, double cut, double splice, double hole, string other)
    {
        p.Marks.Add(new FlatKit.Mark(cut - 0.25 * splice, FlatKit.MarkKind.Splice, hole, 0, other, 0));
        p.Marks.Add(new FlatKit.Mark(cut + 0.25 * splice, FlatKit.MarkKind.Splice, hole, 0, other, 0));
    }

    /// <summary>Strips nested on sheets: <see cref="Nest"/>'s drawings (kept for 1.3.1 callers).</summary>
    public static List<Drawing> StripSheets(FlatKit.Result kit, Options? options = null) => Nest(kit, options).Sheets;

    /// <summary>
    /// Nests every strip on sheets of the bed size (see the class notes):
    /// strips longer than the bed go along the diagonal when they fit there,
    /// are split into lapped pieces otherwise (<see cref="Options.Splice"/> ≥ 0),
    /// and the rest are packed on shelves, first-fit decreasing.
    /// </summary>
    public static Nesting Nest(FlatKit.Result kit, Options? options = null)
    {
        var o = options ?? new Options();
        var plain = NestCore(kit, o, false);
        if (!o.Diagonal) return plain;
        // the diagonal keeps long strips whole but its sheets are poorly filled (the corners are lost):
        // take it only when it does not cost a sheet; then it also saves the splices
        var diag = NestCore(kit, o, true);
        if (diag.DiagonalStrips == 0) return plain;
        if (diag.TooLong != plain.TooLong) return diag.TooLong < plain.TooLong ? diag : plain;
        if (diag.LapClashes != plain.LapClashes) return diag.LapClashes < plain.LapClashes ? diag : plain;
        return diag.SheetCount <= plain.SheetCount ? diag : plain;
    }

    private static Nesting NestCore(FlatKit.Result kit, Options o, bool diagonal)
    {
        double mm = o.ToMm, G = o.Gap, SW = o.SheetWidth, SH = o.SheetHeight;
        double rowMax = SW - 2 * G;
        var n = new Nesting { SheetWidth = SW, SheetHeight = SH };
        var order = kit.Strips.OrderByDescending(s => s.Width).ThenByDescending(s => s.Length).ThenBy(s => s.Label, StringComparer.Ordinal).ToList();
        double wMax = order.Count > 0 ? order[0].Width * mm : 0;
        // the lap must sit between two joints: three strip widths when the joints allow it, else what the free stretch
        // between neighbouring joint marks (its median over the kit) leaves after the gap on either side, 20 mm at least
        var free = new List<double>();
        foreach (var s in kit.Strips)
        {
            var ms = s.Marks.OrderBy(m => m.At).ToList();
            for (int i = 0; i + 1 < ms.Count; i++) free.Add((ms[i + 1].At - ms[i].At - 0.5 * (ms[i].Size + ms[i + 1].Size)) * mm);
        }
        double freeMedian = free.Count > 0 ? free.OrderBy(v => v).ElementAt(free.Count / 2) : double.PositiveInfinity;
        double splice = o.Splice > 0 ? o.Splice : Math.Max(20, Math.Min(3 * wMax, freeMedian - 2 * G));

        // 1. sort the strips: shelves, diagonal lanes, pieces, overflow
        var shelf = new List<FlatKit.Strip>();
        var diag = new List<FlatKit.Strip>();
        var overflow = new List<FlatKit.Strip>();
        var pieces = new Dictionary<FlatKit.Strip, List<FlatKit.Strip>>();
        foreach (var s in order)
        {
            double L = s.Length * mm, W = s.Width * mm;
            if (L <= rowMax + 1e-9) { shelf.Add(s); continue; }
            if (diagonal && L <= LaneLength(SW, SH, G, W, 0, out _) + 1e-9) { diag.Add(s); continue; }
            if (o.Splice >= 0)
            {
                double sp = splice;
                var ps = Split(s, rowMax, sp, mm, G, out int clashes);
                if (clashes > 0 && o.Splice <= 0)
                {
                    // joints closer than the kit's median here (near a hub): shorter laps until one sits between joints, 20 mm at least
                    for (double sp2 = 0.75 * sp; clashes > 0 && sp2 >= 20; sp2 *= 0.75)
                    {
                        var ps2 = Split(s, rowMax, sp2, mm, G, out int c2);
                        if (c2 < clashes) { ps = ps2; clashes = c2; sp = sp2; }
                    }
                }
                if (ps.Count > 1 && ps.All(p => p.Length * mm <= rowMax + 1e-9))
                {
                    pieces[s] = ps; n.SplitStrips++; n.Pieces += ps.Count; n.LapClashes += clashes;
                    n.Splice = Math.Max(n.Splice, sp); n.SpliceMin = n.SpliceMin == 0 ? sp : Math.Min(n.SpliceMin, sp);
                    shelf.AddRange(ps);
                    continue;
                }
            }
            overflow.Add(s);
        }
        foreach (var s in kit.Strips) { if (pieces.TryGetValue(s, out var ps)) n.Strips.AddRange(ps); else n.Strips.Add(s); }
        shelf = shelf.OrderByDescending(s => s.Width).ThenByDescending(s => s.Length).ThenBy(s => s.Label, StringComparer.Ordinal).ToList();

        void Place(Drawing d, FlatKit.Strip s, double x, double y, double angle)
        {
            DrawStrip(d, s, mm, x, y, angle);
            n.Placements.Add(new Placement { Strip = s, Sheet = n.Sheets.IndexOf(d), X = x, Y = y, Angle = angle });
            n.StripArea += StripArea(s, mm);
        }

        // 2. diagonal sheets: lanes outward from the centre line, each packed end to end like a shelf, the
        //    longest strip that fits first; once no long strip fits, shelf strips take what is left of the lanes
        double theta = Math.Atan2(SH, SW);
        double ux = Math.Cos(theta), uy = Math.Sin(theta), nx = -uy, ny = ux;
        double cx = 0.5 * SW, cy = 0.5 * SH;
        while (diag.Count > 0)
        {
            var d = new Drawing { Width = SW, Height = SH };
            n.Sheets.Add(d); n.DiagonalSheets++;
            double edgePos = 0, edgeNeg = 0; bool first = true; int side = 0, idle = 0;
            while (idle < 2)
            {
                double sign = first ? 0 : (side == 0 ? 1 : -1);
                double edge = side == 0 ? edgePos : edgeNeg;
                // open the lane with the first candidate that fits it: that fixes the lane's width and offset
                FlatKit.Strip? pick = null; double dd = 0, wl = 0, t = 0, tmax = 0;
                foreach (var cand in diag.Concat(shelf))
                {
                    double W = cand.Width * mm, L = cand.Length * mm;
                    double at = first ? 0 : sign * (edge + G + 0.5 * W);
                    double avail = LaneLength(SW, SH, G, W, at, out double t0);
                    if (L <= avail + 1e-9) { pick = cand; dd = at; wl = W; t = t0; tmax = t0 + avail; break; }
                }
                if (pick == null)
                {
                    if (first) { overflow.AddRange(diag); diag.Clear(); n.Sheets.Remove(d); n.DiagonalSheets--; break; } // guard: the centre lane was checked already
                    idle++; side = 1 - side; continue;
                }
                // fill the lane end to end: strips no wider than the lane, longest first, centred across it
                while (pick != null)
                {
                    double w = pick.Width * mm, L = pick.Length * mm;
                    double off = dd - 0.5 * w;
                    Place(d, pick, cx + off * nx + t * ux, cy + off * ny + t * uy, theta);
                    if (diag.Remove(pick)) n.DiagonalStrips++; else shelf.Remove(pick);
                    t += L + G;
                    pick = diag.Concat(shelf).FirstOrDefault(c => c.Width * mm <= wl + 1e-9 && c.Length * mm <= tmax - t + 1e-9);
                }
                if (first) { edgePos = 0.5 * wl; edgeNeg = 0.5 * wl; first = false; }
                else if (side == 0) edgePos = Math.Abs(dd) + 0.5 * wl; else edgeNeg = Math.Abs(dd) + 0.5 * wl;
                idle = 0; side = 1 - side;
            }
        }

        // 3. shelves: finite first-fit (Berkey & Wang 1987), strips by width then length decreasing
        var shelves = new List<(Drawing sheet, double y, double h, double x)>();
        var top = new Dictionary<Drawing, double>();
        foreach (var s in shelf)
        {
            double L = s.Length * mm, W = s.Width * mm;
            int k = -1;
            for (int i = 0; i < shelves.Count; i++)
                if (shelves[i].h >= W - 1e-9 && shelves[i].x + L + G <= SW + 1e-9) { k = i; break; }
            if (k < 0)
            {
                Drawing? sheet = null;
                foreach (var kv in top) if (kv.Value + W + G <= SH + 1e-9) { sheet = kv.Key; break; }
                if (sheet == null) { sheet = new Drawing { Width = SW, Height = SH }; n.Sheets.Add(sheet); top[sheet] = G; }
                shelves.Add((sheet, top[sheet], W, G)); k = shelves.Count - 1;
                top[sheet] = top[sheet] + W + G; n.Rows++;
            }
            var sh = shelves[k];
            Place(sh.sheet, s, sh.x, sh.y, 0);
            shelves[k] = (sh.sheet, sh.y, sh.h, sh.x + L + G);
        }
        n.SheetCount = n.Sheets.Count;

        // 4. the overflow drawing: strips longer than the bed, neither diagonal nor split
        if (overflow.Count > 0)
        {
            double wMaxL = overflow.Max(s => s.Length) * mm + 2 * G;
            var d = new Drawing { Width = wMaxL, Height = overflow.Sum(s => s.Width * mm + G) + G };
            double yy = G;
            foreach (var s in overflow) { DrawStrip(d, s, mm, G, yy); yy += s.Width * mm + G; }
            d.Title = $"{overflow.Count} strips longer than the {SW:0} mm sheet (up to {wMaxL - 2 * G:0} mm): cut on a longer bed, or set Splice ≥ 0 to split them";
            n.Sheets.Add(d); n.TooLong = overflow.Count;
        }
        for (int i = 0; i < n.SheetCount; i++)
        {
            var d = n.Sheets[i];
            double used = n.Placements.Where(p => p.Sheet == i).Sum(p => StripArea(p.Strip, mm)) / (SW * SH);
            d.Title = $"sheet {i + 1} of {n.SheetCount} — {SW:0} × {SH:0} mm, {used:P0} used{(i < n.DiagonalSheets ? ", diagonal lanes" : "")}";
        }
        var plates = PlateSheet(kit, o);
        if (plates != null) n.Sheets.Add(plates);
        return n;
    }

    // ------------------------------------------------------------------ hub plates

    /// <summary>The outline of a hub plate in its own frame (mm, centre at the origin): see <see cref="HubPlate.Outline"/>.</summary>
    public static List<(double X, double Y)> PlateOutline(HubPlate p, double mm) =>
        p.Outline().Select(q => (q.X * mm, q.Y * mm)).ToList();

    /// <summary>
    /// Draws one hub plate centred at (cx, cy): the star outline and the bolt hole
    /// on the cut layer, the hub number and the plate's place in the sandwich in the
    /// middle, and at every slot the label of the strip that goes into it.
    /// </summary>
    public static void DrawPlate(Drawing d, HubPlate p, int index, double mm, double cx, double cy)
    {
        double R = p.Radius * mm;
        d.Line(Layer.Cut, PlateOutline(p, mm).Select(q => (cx + q.X, cy + q.Y)), closed: true);
        if (p.Bolt > 0) d.Hole(Layer.Cut, cx, cy, 0.5 * p.Bolt * mm);
        // the plate's name in the sector between slots nearest the bottom, in two short lines clear of the slots
        double th = Math.Max(1.5, Math.Min(5, 0.07 * R));
        string where = p.Count == 2 ? (index == 0 ? "inner" : "outer") : "plate";
        var angles = p.Rays.Select(r => r.Angle).OrderBy(a => a).ToList();
        double best = -Math.PI / 2, bestDist = double.MaxValue;
        for (int k = 0; k < angles.Count; k++)
        {
            double a = angles[k], b = k + 1 < angles.Count ? angles[k + 1] : angles[0] + 2 * Math.PI, mid = 0.5 * (a + b);
            double dist = Math.Abs(Math.Atan2(Math.Sin(mid + Math.PI / 2), Math.Cos(mid + Math.PI / 2)));
            if (dist < bestDist) { bestDist = dist; best = mid; }
        }
        double lx = cx + 0.6 * R * Math.Cos(best), ly = cy + 0.6 * R * Math.Sin(best);
        d.Label(Layer.Engrave, lx, ly + 0.25 * th, th, $"hub j{p.Joint}");
        d.Label(Layer.Engrave, lx, ly - 1.0 * th, th, where);
        // strip labels beside the slots, on the counter-clockwise side, half way along the slot
        double lh = Math.Max(1.2, Math.Min(5, 0.09 * R)), rl = 0.5 * (p.InnerRadius + p.Radius) * mm;
        foreach (var r in p.Rays)
        {
            double off = 0.5 * p.SlotWidth * mm + 0.4 * lh;
            double x = cx + rl * Math.Cos(r.Angle) - off * Math.Sin(r.Angle), y = cy + rl * Math.Sin(r.Angle) + off * Math.Cos(r.Angle) - 0.35 * lh;
            d.Label(Layer.Engrave, x, y, lh, r.Label);
        }
    }

    /// <summary>All hub plates of the kit on one drawing (two per hub for a sandwich), or null when the kit has none.</summary>
    public static Drawing? PlateSheet(FlatKit.Result kit, Options? options = null)
    {
        if (kit.Plates.Count == 0) return null;
        var o = options ?? new Options();
        double mm = o.ToMm, G = o.Gap;
        var items = kit.Plates.SelectMany(p => Enumerable.Range(0, p.Count).Select(i => (p, i))).ToList();
        double x = G, y = G, rowH = 0, width = o.SheetWidth, used = 0;
        var d = new Drawing();
        foreach (var (p, i) in items)
        {
            double D = 2 * p.Radius * mm;
            if (x > G && x + D + G > width) { y += rowH + G; x = G; rowH = 0; }
            DrawPlate(d, p, i, mm, x + 0.5 * D, y + 0.5 * D);
            x += D + G; rowH = Math.Max(rowH, D); used = Math.Max(used, x);
        }
        // the drawing is as wide as the plates need (one row) or the sheet (several rows)
        d.Width = y > G ? Math.Max(width, items.Max(it => 2 * it.p.Radius * mm) + 2 * G) : used; d.Height = Math.Max(y + rowH + G, 40);
        int hubs = kit.Plates.Count;
        d.Title = $"hub plate{(items.Count == 1 ? "" : "s")} — {items.Count} for {hubs} hub{(hubs == 1 ? "" : "s")}, Ø {F(kit.Plates.Min(p => 2 * p.Radius * mm))}{(hubs > 1 ? "–" + F(kit.Plates.Max(p => 2 * p.Radius * mm)) : "")} mm, {F(kit.Plates[0].Thickness * mm)} mm sheet";
        return d;
    }

    // ------------------------------------------------------------------ assembly

    /// <summary>The flat grid at 1 : 1 (mm): laths as strips of their footprint, joints numbered, laths labelled.</summary>
    public static Drawing Assembly(FlatKit.Result kit, double foot, Options? options = null)
    {
        var o = options ?? new Options();
        double mm = o.ToMm;
        var all = kit.Flat.SelectMany(l => l).ToList();
        double minU = all.Min(p => p.U), maxU = all.Max(p => p.U), minV = all.Min(p => p.V), maxV = all.Max(p => p.V);
        double margin = 30;
        double X(double u) => margin + (u - minU) * mm;
        double Y(double v) => margin + (v - minV) * mm;
        var d = new Drawing { Width = (maxU - minU) * mm + 2 * margin, Height = (maxV - minV) * mm + 2 * margin + 20 };
        double spacing = kit.Net.Scale * mm;   // mean joint-to-joint length
        double fw = Math.Max(0.5, foot * mm);
        for (int l = 0; l < kit.Flat.Count; l++)
            d.Line(l < kit.Net.CountA ? Layer.A : Layer.B, kit.Flat[l].Select(p => (X(p.U), Y(p.V))), width: fw);
        double jr = Math.Max(1.2, 0.06 * spacing), jh = Math.Max(1.5, 0.16 * spacing);
        foreach (var j in kit.Joints)
        {
            d.Hole(Layer.Joints, X(j.U), Y(j.V), j.Hub ? 2 * jr : jr, filled: true);
            if (kit.Joints.Count <= 600) d.Label(Layer.Labels, X(j.U) + 1.4 * jr, Y(j.V) + 1.2 * jr, jh, j.Id.ToString(CultureInfo.InvariantCulture), centre: false);
        }
        double lh = Math.Max(3, 0.3 * spacing);
        for (int l = 0; l < kit.Flat.Count; l++)
        {
            var f = kit.Flat[l];
            // label at the end farther from the anchor, pushed outward along the lath
            var (a, b) = f[0].U * f[0].U + f[0].V * f[0].V > f[f.Length - 1].U * f[f.Length - 1].U + f[f.Length - 1].V * f[f.Length - 1].V ? (f[0], f[1]) : (f[f.Length - 1], f[f.Length - 2]);
            double du = a.U - b.U, dv = a.V - b.V, n = Math.Max(Math.Sqrt(du * du + dv * dv), 1e-12);
            d.Label(Layer.Labels, X(a.U) + du / n * lh * 1.2, Y(a.V) + dv / n * lh * 1.2 - 0.35 * lh, lh, kit.LathLabels[l]);
        }
        // scale bar 100 mm
        d.Line(Layer.Guide, new[] { (margin, 12.0), (margin + 100, 12.0) }, width: 0.8);
        d.Label(Layer.Guide, margin + 50, 15, 4, "100 mm");
        d.Title = $"flat assembly 1 : 1 — {kit.Strips.Count} strips, {kit.Joints.Count} joints";
        return d;
    }

    // ------------------------------------------------------------------ deployment

    /// <summary>The grid from flat to curved in axonometric frames (not to scale).</summary>
    public static Drawing Deployment(FlatKit.Result kit, int frames = 5)
    {
        int n = kit.Deployment.Count;
        frames = Math.Max(2, Math.Min(frames, n));
        var picks = Enumerable.Range(0, frames).Select(i => (int)Math.Round(i * (n - 1) / (double)(frames - 1))).Distinct().ToList();
        // axonometric in the kit's frame: u, v in plan, w up
        (double, double) P(Vec3d p)
        {
            var q = p - kit.Origin;
            double u = Vec3d.Dot(q, kit.AxisU), v = Vec3d.Dot(q, kit.AxisV), w = Vec3d.Dot(q, kit.Normal);
            return ((u - v) * 0.8660254, -(u + v) * 0.5 - w);
        }
        var proj = picks.Select(k => kit.Deployment[k].Select(l => l.Select(P).ToArray()).ToList()).ToList();
        var pts = proj.SelectMany(f => f.SelectMany(l => l)).ToList();
        double minX = pts.Min(p => p.Item1), maxX = pts.Max(p => p.Item1), minY = pts.Min(p => p.Item2), maxY = pts.Max(p => p.Item2);
        double cell = 220, s = (cell - 20) / Math.Max(maxX - minX, maxY - minY);
        var d = new Drawing { Width = cell * picks.Count, Height = cell + 34, Title = "deployment — from the flat grid to the curved net" };
        for (int f = 0; f < picks.Count; f++)
        {
            double ox = f * cell + 10, oy = 10;
            for (int l = 0; l < proj[f].Count; l++)
                d.Line(l < kit.Net.CountA ? Layer.A : Layer.B, proj[f][l].Select(p => (ox + (p.Item1 - minX) * s, oy + (maxY - p.Item2) * s)), width: 0.9);
            double fold = 1 - kit.Motion.States[kit.Motion.States.Count - 1 - picks[f]].Fold;
            d.Label(Layer.Labels, ox + cell / 2 - 10, cell + 6, 13, f == 0 ? "flat — assemble" : f == picks.Count - 1 ? "curved — lock the supports" : $"{fold * 100:0} % deployed");
        }
        return d;
    }

    // ------------------------------------------------------------------ cut list and package

    /// <summary>Cut list of the strips as nested (pieces for split strips), one row per strip: label, family, length and width in mm, then every joint as position:kind:lath.</summary>
    public static string CutList(FlatKit.Result kit, Options? options = null) => CutList(Nest(kit, options).Strips, options, kit);

    /// <summary>Cut list, one row per strip: label, family, length and width in mm, then every joint or splice hole as position:kind:joins:joint.</summary>
    public static string CutList(IEnumerable<FlatKit.Strip> strips, Options? options = null, FlatKit.Result? kit = null)
    {
        var o = options ?? new Options();
        var sb = new StringBuilder("strip,family,length_mm,width_mm,extend_start_mm,extend_end_mm,joints (position_mm:kind:joins:joint)\n");
        foreach (var s in strips)
        {
            sb.Append($"{s.Label},{(s.FamilyA ? "A" : "B")},{F(s.Length * o.ToMm)},{F(s.Width * o.ToMm)},{F(s.ExtendStart * o.ToMm)},{F(s.ExtendEnd * o.ToMm)},");
            sb.Append(string.Join(" ", s.Marks.Select(m => $"{F(m.At * o.ToMm)}:{m.Kind switch { FlatKit.MarkKind.SlotOuter => "slot-out", FlatKit.MarkKind.SlotInner => "slot-in", FlatKit.MarkKind.Hub => "hub", FlatKit.MarkKind.Splice => "splice", _ => "hole" }}:{m.Label}:j{m.Joint}")));
            sb.Append('\n');
        }
        // hub plates: one row each — label, "plate", diameter and thickness, inner radius and slot width, then the rays (angle:strip)
        if (kit != null) foreach (var p in kit.Plates)
            for (int i = 0; i < p.Count; i++)
            {
                sb.Append($"hub-j{p.Joint}-{(p.Count == 2 ? (i == 0 ? "inner" : "outer") : "plate")},plate,{F(2 * p.Radius * o.ToMm)},{F(p.Thickness * o.ToMm)},{F(p.InnerRadius * o.ToMm)},{F(p.SlotWidth * o.ToMm)},");
                sb.Append($"bolt:{F(p.Bolt * o.ToMm)} ");
                sb.Append(string.Join(" ", p.Rays.Select(r => $"{F(r.Angle * 180 / Math.PI)}deg:{r.Label}")));
                sb.Append('\n');
            }
        return sb.ToString();
    }

    /// <summary>
    /// Every file of the kit in one zip: strips-N.svg per sheet, strips.dxf,
    /// assembly.svg / .dxf, deployment.svg, cutlist.csv and a README with the
    /// assembly sequence.
    /// </summary>
    public static byte[] Zip(FlatKit.Result kit, List<Drawing> sheets, Drawing assembly, Drawing deployment, Options? options = null) =>
        Zip(kit, Nest(kit, options), assembly, deployment, options);

    /// <summary>Every file of the kit in one zip, from a <see cref="Nest"/>ing.</summary>
    public static byte[] Zip(FlatKit.Result kit, Nesting nest, Drawing assembly, Drawing deployment, Options? options = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in Files(kit, nest, assembly, deployment, options))
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        }
        return ms.ToArray();
    }

    /// <summary>The kit's files as (name, text) pairs (what <see cref="Zip"/> packs).</summary>
    public static List<(string Name, string Text)> Files(FlatKit.Result kit, List<Drawing> sheets, Drawing assembly, Drawing deployment, Options? options = null) =>
        Files(kit, Nest(kit, options), assembly, deployment, options);

    /// <summary>The kit's files as (name, text) pairs, from a <see cref="Nest"/>ing (its sheets and its cut list).</summary>
    public static List<(string Name, string Text)> Files(FlatKit.Result kit, Nesting nest, Drawing assembly, Drawing deployment, Options? options = null)
    {
        var o = options ?? new Options();
        var sheets = nest.Sheets;
        var files = new List<(string, string)>();
        for (int i = 0; i < sheets.Count; i++) files.Add(($"strips-{i + 1:00}.svg", ToSvg(sheets[i])));
        files.Add(("strips.dxf", ToDxf(sheets)));
        files.Add(("assembly.svg", ToSvg(assembly)));
        files.Add(("assembly.dxf", ToDxf(new[] { assembly })));
        files.Add(("deployment.svg", ToSvg(deployment)));
        files.Add(("cutlist.csv", CutList(nest.Strips, o, kit)));
        files.Add(("README.txt", Readme(kit, nest, o)));
        return files;
    }

    /// <summary>One line on the nesting for reports: sheets, material use, diagonals, splices.</summary>
    public static string NestingSummary(Nesting n)
    {
        var sb = new StringBuilder();
        sb.Append($"{n.SheetCount} sheet{(n.SheetCount == 1 ? "" : "s")} of {n.SheetWidth:0} × {n.SheetHeight:0} mm, {n.Utilisation:P0} of the material becomes strips (the area bound is {n.AreaBound})");
        if (n.DiagonalStrips > 0) sb.Append($"; {n.DiagonalStrips} strip{(n.DiagonalStrips == 1 ? "" : "s")} longer than the bed lie{(n.DiagonalStrips == 1 ? "s" : "")} along its diagonal");
        if (n.SplitStrips > 0) sb.Append($"; {n.SplitStrips} strip{(n.SplitStrips == 1 ? "" : "s")} longer than the bed split into {n.Pieces} pieces lapped over {(n.SpliceMin < n.Splice - 0.5 ? $"{n.SpliceMin:0}–{n.Splice:0}" : $"{n.Splice:0}")} mm");
        if (n.LapClashes > 0) sb.Append($"; {n.LapClashes} lap{(n.LapClashes == 1 ? "" : "s")} reach{(n.LapClashes == 1 ? "es" : "")} a joint (shorten Splice or raise the scale)");
        if (n.TooLong > 0) sb.Append($"; {n.TooLong} strip{(n.TooLong == 1 ? "" : "s")} longer than the bed on a drawing of their own (set Splice ≥ 0 to split them)");
        return sb.ToString();
    }

    private static string Readme(FlatKit.Result kit, Nesting nest, Options o)
    {
        int hubs = kit.Joints.Count(j => j.Hub);
        var slots = kit.Strips.SelectMany(s => s.Marks).Where(m => (m.Kind == FlatKit.MarkKind.SlotOuter || m.Kind == FlatKit.MarkKind.SlotInner) && m.Label != "hub").ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Mite flat kit — cut, assemble flat, deploy");
        sb.AppendLine();
        sb.AppendLine($"{kit.Strips.Count} strips ({kit.Net.CountA} A + {kit.Strips.Count - kit.Net.CountA} B), {kit.Joints.Count} joints ({hubs} hub{(hubs == 1 ? "" : "s")}), {F(kit.TotalLength * o.ToMm / 1000)} m of strip.");
        sb.AppendLine($"Units: millimetres. Sheets {F(o.SheetWidth)} x {F(o.SheetHeight)} mm. Red = cut, blue = engrave.");
        sb.AppendLine("Nesting: " + NestingSummary(nest).Replace("×", "x") + ".");
        sb.AppendLine();
        sb.AppendLine("Files");
        sb.AppendLine("  strips-NN.svg / strips.dxf   every strip with its slots or holes, labelled" + (nest.TooLong > 0 ? "; strips longer than the sheet are on the last drawing" : "") + (kit.Plates.Count > 0 ? ", the hub plates on the last one" : ""));
        sb.AppendLine("  assembly.svg / assembly.dxf  the flat grid at 1:1, joints numbered, laths labelled");
        sb.AppendLine("  deployment.svg               the grid from flat to curved");
        sb.AppendLine("  cutlist.csv                  lengths and joint positions of every strip");
        sb.AppendLine();
        sb.AppendLine("Joints");
        if (slots.Count > 0)
        {
            sb.AppendLine($"  Crossings are halving slots: A strips are slotted from the outer edge (the edge marked ^out), B strips from the inner edge, half the width deep.");
            sb.AppendLine($"  Slots are {F(slots.Min(m => m.Size) * o.ToMm)}-{F(slots.Max(m => m.Size) * o.ToMm)} mm wide: the other strip's thickness at the smallest angle that joint reaches while deploying (down to {kit.Joints.Min(j => j.MinAngle):0}°), so the scissors can turn.");
        }
        else sb.AppendLine("  Crossings are pin holes on the centre line: one pin per joint, free to turn.");
        if (hubs > 0 && kit.Plates.Count == 0) sb.AppendLine("  The hub (where three or more strips meet, the flat point) takes one common pin or a plate through the holes marked 'hub'.");
        foreach (var p in kit.Plates)
        {
            sb.AppendLine($"  Hub j{p.Joint}: {p.Rays.Count} strips ({string.Join(", ", p.Rays.Select(r => r.Label))}) end {F(p.EndRadius * o.ToMm)} mm short of the centre on {(p.Count == 2 ? "a sandwich of two identical hub plates" : "one hub plate")}, Ø {F(2 * p.Radius * o.ToMm)} × {F(p.Thickness * o.ToMm)} mm, with {p.Rays.Count} radial slots {F(p.SlotWidth * o.ToMm)} mm wide from the rim to {F(p.InnerRadius * o.ToMm)} mm.");
            sb.AppendLine($"    Each of those strips is notched {F(p.NotchDepth * o.ToMm)} mm deep over its last {F((p.Key + kit.Strips.SelectMany(x => x.Marks).Where(m => m.Label == "hub").Select(m => m.Size - p.Key).DefaultIfEmpty(0).First()) * o.ToMm)} mm{(p.Count == 2 ? " on both edges" : " on the inner edge")} (marked 'hub'): drop the strip into its slot (labelled with the strip) and the plate's disc enters the notch, which locks the strip radially.");
            sb.AppendLine(p.Count == 2
                ? $"    Fit the inner plate first, the strips on it, then the outer plate from above; bolt the two through the centre hole (Ø {F(p.Bolt * o.ToMm)} mm) — the bolt is what keeps the strips from lifting out."
                : "    One plate holds angle and position; glue or pin the strips to it so they cannot lift out.");
            sb.AppendLine($"    The rays sit at {F(360.0 / p.Rays.Count)}° ± {p.AngleDeviation:0.0#}° in the flat state and their angles change by at most {p.AngleDrift:0.0#}° through the deployment: the plate is rigid.{(p.Cramped ? " The plate had to be cut back to clear the first joints (slots shorter than a strip width): raise the spacing or narrow the strips." : "")}");
        }
        if (nest.SplitStrips > 0) sb.AppendLine($"  Split strips (A3/1, A3/2, ...) lap over {(nest.SpliceMin < nest.Splice - 0.5 ? F(nest.SpliceMin) + "-" + F(nest.Splice) : F(nest.Splice))} mm with two pin holes each; the laps sit between the joints, never under a slot" + (nest.LapClashes > 0 ? $" - except {nest.LapClashes} where the joints are too close: check those on the drawings" : "") + ".");
        sb.AppendLine();
        sb.AppendLine("Sequence");
        sb.AppendLine("  1. Cut and engrave the strips. Each slot or hole is labelled with the strip it joins." + (nest.SplitStrips > 0 ? " Glue or bolt the pieces of split strips into one straight strip at their splice holes (A3/1 over A3/2, outer edges up), flat on the table." : ""));
        sb.AppendLine("  2. Lay the assembly drawing on the table (print at 100 %; check the 100 mm bar).");
        sb.AppendLine(kit.Plates.Count > 0
            ? "  3. Lay the inner hub plate at the hub mark, place the A strips on the drawing, outer edge up, their hub ends in the plate's slots, then slot the B strips over them joint by joint; add the outer plate and bolt the hub."
            : "  3. Place the A strips on it, outer edge up, then slot the B strips over them joint by joint; pin the hub.");
        sb.AppendLine("  4. Deploy: hold the hub and push it up (or lower the ends) — every strip end slides; follow deployment.svg.");
        sb.AppendLine("  5. Fix the ends to their supports in the curved state.");
        sb.AppendLine();
        sb.AppendLine($"Check: the flat state is flat to {F(kit.Flatness * o.ToMm)} mm and keeps every joint spacing to {kit.Drift:P2}.");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ writers

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string SvgColour(Layer l) => l switch
    {
        Layer.Cut => "#ff0000",
        Layer.Engrave => "#0000ff",
        Layer.A => "#2a3cff",
        Layer.B => "#ff1f4f",
        Layer.Guide => "#7f7f7f",
        _ => "#000000",
    };

    /// <summary>SVG in millimetres (y down; the drawing's y up is flipped).</summary>
    public static string ToSvg(Drawing d)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(d.Width)}mm\" height=\"{F(d.Height)}mm\" viewBox=\"0 0 {F(d.Width)} {F(d.Height)}\">\n");
        sb.Append($"<title>{Esc(d.Title)}</title>\n");
        double H = d.Height;
        foreach (var layer in d.Shapes.Select(s => s.Layer).Distinct())
        {
            string c = SvgColour(layer);
            sb.Append($"<g id=\"{layer.ToString().ToLowerInvariant()}\" stroke=\"{c}\" fill=\"none\" stroke-width=\"0.1\" stroke-linecap=\"round\" stroke-linejoin=\"round\">\n");
            foreach (var s in d.Shapes.Where(s => s.Layer == layer))
            {
                switch (s)
                {
                    case Poly p:
                        string pts = string.Join(" ", p.Points.Select(q => $"{F(q.X)},{F(H - q.Y)}"));
                        string w = p.Width > 0 ? $" stroke-width=\"{F(p.Width)}\"" : "";
                        sb.Append(p.Closed ? $"<polygon points=\"{pts}\"{w}/>\n" : $"<polyline points=\"{pts}\"{w}/>\n");
                        break;
                    case Circle ci:
                        sb.Append($"<circle cx=\"{F(ci.X)}\" cy=\"{F(H - ci.Y)}\" r=\"{F(ci.R)}\"{(ci.Filled ? $" fill=\"{c}\"" : "")}/>\n");
                        break;
                    case Text t:
                        string rot = Math.Abs(t.Angle) > 1e-9 ? $" transform=\"rotate({F(-t.Angle)} {F(t.X)} {F(H - t.Y)})\"" : "";
                        sb.Append($"<text x=\"{F(t.X)}\" y=\"{F(H - t.Y)}\" font-size=\"{F(t.Height)}\" font-family=\"Helvetica, Arial, sans-serif\" fill=\"{c}\" stroke=\"none\"{(t.Centre ? " text-anchor=\"middle\"" : "")}{rot}>{Esc(t.Value)}</text>\n");
                        break;
                }
            }
            sb.Append("</g>\n");
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static int DxfColour(Layer l) => l switch { Layer.Cut => 1, Layer.Engrave => 5, Layer.A => 5, Layer.B => 1, Layer.Guide => 8, _ => 7 };

    /// <summary>
    /// DXF R12 (millimetres, y up) of one or more drawings placed side by side
    /// (20 mm apart): LINE, CIRCLE and TEXT entities only, which every laser
    /// and CAD program reads.
    /// </summary>
    public static string ToDxf(IEnumerable<Drawing> drawings)
    {
        var list = drawings.ToList();
        var sb = new StringBuilder();
        void G(int code, string v) { sb.Append(code).Append('\n').Append(v).Append('\n'); }
        void Gd(int code, double v) => G(code, F(v));
        G(0, "SECTION"); G(2, "HEADER"); G(9, "$ACADVER"); G(1, "AC1009"); G(9, "$INSUNITS"); G(70, "4"); G(0, "ENDSEC");
        var layers = list.SelectMany(dw => dw.Shapes.Select(s => s.Layer)).Distinct().ToList();
        G(0, "SECTION"); G(2, "TABLES"); G(0, "TABLE"); G(2, "LAYER"); G(70, layers.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var l in layers) { G(0, "LAYER"); G(2, l.ToString().ToUpperInvariant()); G(70, "0"); G(62, DxfColour(l).ToString(CultureInfo.InvariantCulture)); G(6, "CONTINUOUS"); }
        G(0, "ENDTAB"); G(0, "ENDSEC");
        G(0, "SECTION"); G(2, "ENTITIES");
        double ox = 0;
        foreach (var dw in list)
        {
            foreach (var s in dw.Shapes)
            {
                string layer = s.Layer.ToString().ToUpperInvariant();
                switch (s)
                {
                    case Poly p:
                        int count = p.Points.Count;
                        int segs = p.Closed ? count : count - 1;
                        for (int i = 0; i < segs; i++)
                        {
                            var a = p.Points[i]; var b = p.Points[(i + 1) % count];
                            G(0, "LINE"); G(8, layer); Gd(10, ox + a.X); Gd(20, a.Y); Gd(30, 0); Gd(11, ox + b.X); Gd(21, b.Y); Gd(31, 0);
                        }
                        break;
                    case Circle c:
                        G(0, "CIRCLE"); G(8, layer); Gd(10, ox + c.X); Gd(20, c.Y); Gd(30, 0); Gd(40, c.R);
                        break;
                    case Text t:
                        G(0, "TEXT"); G(8, layer); Gd(10, ox + t.X); Gd(20, t.Y); Gd(30, 0); Gd(40, t.Height); G(1, t.Value.Replace("↑", "^"));
                        if (Math.Abs(t.Angle) > 1e-9) Gd(50, t.Angle);
                        if (t.Centre) { G(72, "1"); Gd(11, ox + t.X); Gd(21, t.Y); Gd(31, 0); }
                        break;
                }
            }
            ox += dw.Width + 20;
        }
        G(0, "ENDSEC"); G(0, "EOF");
        return sb.ToString();
    }
}
