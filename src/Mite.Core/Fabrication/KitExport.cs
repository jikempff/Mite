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
///  - strip sheets: every strip laid out on sheets of the laser bed, longest
///    first, with its slots or holes and its labels;
///  - the assembly drawing: the flat grid at 1 : 1 with every joint numbered
///    and every lath labelled — print it or lay the strips on it;
///  - the deployment sequence: the grid from flat to curved in a few
///    axonometric frames.
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
    }

    // ------------------------------------------------------------------ primitives

    public enum Layer { Cut, Engrave, A, B, Joints, Labels, Guide }

    public abstract class Shape { public Layer Layer; }
    public sealed class Poly : Shape { public List<(double X, double Y)> Points = new(); public bool Closed; public double Width; }
    public sealed class Circle : Shape { public double X, Y, R; public bool Filled; }
    public sealed class Text : Shape { public double X, Y, Height; public string Value = ""; public bool Centre = true; }

    public sealed class Drawing
    {
        public string Title = "";
        public double Width, Height;
        public List<Shape> Shapes = new();
        public void Line(Layer layer, IEnumerable<(double, double)> pts, bool closed = false, double width = 0) =>
            Shapes.Add(new Poly { Layer = layer, Points = pts.ToList(), Closed = closed, Width = width });
        public void Hole(Layer layer, double x, double y, double r, bool filled = false) => Shapes.Add(new Circle { Layer = layer, X = x, Y = y, R = r, Filled = filled });
        public void Label(Layer layer, double x, double y, double h, string s, bool centre = true) => Shapes.Add(new Text { Layer = layer, X = x, Y = y, Height = h, Value = s, Centre = centre });
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
        return pts;
    }

    /// <summary>
    /// Draws one strip with its lower-left corner at (x0, y0), y up, outer edge
    /// on top. Each joint is labelled with the lath it joins, beside its slot
    /// (in the slotted half, where the material between slots is free) or
    /// beside its hole; the strip's own label goes in the other half, in its
    /// longest free stretch, with an arrow to the outer edge.
    /// </summary>
    private static void DrawStrip(Drawing d, FlatKit.Strip s, double mm, double x0, double y0)
    {
        double W = s.Width * mm, L = s.Length * mm;
        d.Line(Layer.Cut, StripOutline(s, mm).Select(p => (x0 + p.X, y0 + p.Y)), closed: true);
        var xs = s.Marks.Select(m => m.At * mm).OrderBy(v => v).ToList();
        double pitch = double.MaxValue;
        for (int i = 0; i + 1 < xs.Count; i++) pitch = Math.Min(pitch, xs[i + 1] - xs[i]);
        double th = Math.Max(1.2, Math.Min(Math.Min(5, 0.24 * W), pitch < double.MaxValue ? 0.3 * pitch : 5));
        bool outer = s.Marks.Any(m => m.Kind == FlatKit.MarkKind.SlotOuter), inner = s.Marks.Any(m => m.Kind == FlatKit.MarkKind.SlotInner);
        foreach (var m in s.Marks)
        {
            double x = x0 + m.At * mm, half = 0.5 * m.Size * mm;
            switch (m.Kind)
            {
                case FlatKit.MarkKind.Hole:
                case FlatKit.MarkKind.Hub:
                    d.Hole(Layer.Cut, x, y0 + 0.5 * W, half);
                    d.Label(Layer.Engrave, x + half + 0.3 * th, y0 + 0.5 * W - 0.35 * th, th, m.Kind == FlatKit.MarkKind.Hub ? "hub" : m.Label, centre: false);
                    break;
                case FlatKit.MarkKind.SlotOuter:
                    d.Label(Layer.Engrave, x + half + 0.3 * th, y0 + W - Math.Min(m.Depth * mm, W) + 0.15 * th, th, m.Label, centre: false);
                    break;
                case FlatKit.MarkKind.SlotInner:
                    d.Label(Layer.Engrave, x + half + 0.3 * th, y0 + Math.Min(m.Depth * mm, W) - 1.15 * th, th, m.Label, centre: false);
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
        d.Label(Layer.Engrave, x0 + bx, y0 + ly - 0.35 * lh, lh, s.Label + " ↑out");
    }

    /// <summary>Strips nested on sheets (shelves, longest first). Strips longer than the sheet get a sheet of their own length and are listed in the title.</summary>
    public static List<Drawing> StripSheets(FlatKit.Result kit, Options? options = null)
    {
        var o = options ?? new Options();
        double mm = o.ToMm, G = o.Gap;
        var sheets = new List<Drawing>();
        Drawing? cur = null; double y = 0, x = 0, rowH = 0;
        var order = kit.Strips.OrderByDescending(s => s.Length).ThenBy(s => s.Label).ToList();
        var tooLong = order.Where(s => s.Length * mm + 2 * G > o.SheetWidth).ToList();
        foreach (var s in order.Except(tooLong))
        {
            double L = s.Length * mm, W = s.Width * mm;
            if (cur == null || x + L + G > o.SheetWidth)
            {
                // new row
                if (cur != null) { y += rowH + G; x = G; rowH = 0; }
                if (cur == null || y + W + G > o.SheetHeight)
                {
                    cur = new Drawing { Width = o.SheetWidth, Height = o.SheetHeight };
                    sheets.Add(cur); y = G; x = G; rowH = 0;
                }
            }
            DrawStrip(cur, s, mm, x, y);
            x += L + G; rowH = Math.Max(rowH, W);
        }
        if (tooLong.Count > 0)
        {
            // one drawing of every strip longer than the bed, stacked: cut on a longer bed or split them (Lath Segment)
            double wMax = tooLong.Max(s => s.Length) * mm + 2 * G;
            var d = new Drawing { Width = wMax, Height = tooLong.Sum(s => s.Width * mm + G) + G };
            double yy = G;
            foreach (var s in tooLong) { DrawStrip(d, s, mm, G, yy); yy += s.Width * mm + G; }
            d.Title = $"{tooLong.Count} strips longer than the {o.SheetWidth:0} mm sheet (up to {wMax - 2 * G:0} mm): cut on a longer bed or split them with Lath Segment";
            sheets.Add(d);
        }
        int k = 0;
        int nSheets = sheets.Count(d => d.Title.Length == 0);
        foreach (var d in sheets) if (d.Title.Length == 0) { k++; d.Title = $"sheet {k} of {nSheets} — {d.Width:0} × {d.Height:0} mm"; }
        return sheets;
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

    /// <summary>Cut list, one row per strip: label, family, length and width in mm, then every joint as position:kind:lath.</summary>
    public static string CutList(FlatKit.Result kit, Options? options = null)
    {
        var o = options ?? new Options();
        var sb = new StringBuilder("strip,family,length_mm,width_mm,extend_start_mm,extend_end_mm,joints (position_mm:kind:joins:joint)\n");
        foreach (var s in kit.Strips)
        {
            sb.Append($"{s.Label},{(s.FamilyA ? "A" : "B")},{F(s.Length * o.ToMm)},{F(s.Width * o.ToMm)},{F(s.ExtendStart * o.ToMm)},{F(s.ExtendEnd * o.ToMm)},");
            sb.Append(string.Join(" ", s.Marks.Select(m => $"{F(m.At * o.ToMm)}:{m.Kind switch { FlatKit.MarkKind.SlotOuter => "slot-out", FlatKit.MarkKind.SlotInner => "slot-in", FlatKit.MarkKind.Hub => "hub", _ => "hole" }}:{m.Label}:j{m.Joint}")));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Every file of the kit in one zip: strips-N.svg per sheet, strips.dxf,
    /// assembly.svg / .dxf, deployment.svg, cutlist.csv and a README with the
    /// assembly sequence.
    /// </summary>
    public static byte[] Zip(FlatKit.Result kit, List<Drawing> sheets, Drawing assembly, Drawing deployment, Options? options = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in Files(kit, sheets, assembly, deployment, options))
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        }
        return ms.ToArray();
    }

    /// <summary>The kit's files as (name, text) pairs (what <see cref="Zip"/> packs).</summary>
    public static List<(string Name, string Text)> Files(FlatKit.Result kit, List<Drawing> sheets, Drawing assembly, Drawing deployment, Options? options = null)
    {
        var o = options ?? new Options();
        var files = new List<(string, string)>();
        for (int i = 0; i < sheets.Count; i++) files.Add(($"strips-{i + 1:00}.svg", ToSvg(sheets[i])));
        files.Add(("strips.dxf", ToDxf(sheets)));
        files.Add(("assembly.svg", ToSvg(assembly)));
        files.Add(("assembly.dxf", ToDxf(new[] { assembly })));
        files.Add(("deployment.svg", ToSvg(deployment)));
        files.Add(("cutlist.csv", CutList(kit, o)));
        files.Add(("README.txt", Readme(kit, sheets, o)));
        return files;
    }

    private static string Readme(FlatKit.Result kit, List<Drawing> sheets, Options o)
    {
        int hubs = kit.Joints.Count(j => j.Hub);
        var slots = kit.Strips.SelectMany(s => s.Marks).Where(m => m.Kind == FlatKit.MarkKind.SlotOuter || m.Kind == FlatKit.MarkKind.SlotInner).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Mite flat kit — cut, assemble flat, deploy");
        sb.AppendLine();
        sb.AppendLine($"{kit.Strips.Count} strips ({kit.Net.CountA} A + {kit.Strips.Count - kit.Net.CountA} B), {kit.Joints.Count} joints ({hubs} hub{(hubs == 1 ? "" : "s")}), {F(kit.TotalLength * o.ToMm / 1000)} m of strip.");
        sb.AppendLine($"Units: millimetres. Sheets {F(o.SheetWidth)} x {F(o.SheetHeight)} mm. Red = cut, blue = engrave.");
        sb.AppendLine();
        sb.AppendLine("Files");
        sb.AppendLine("  strips-NN.svg / strips.dxf   every strip with its slots or holes, labelled; strips longer than the sheet are on the last drawing");
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
        if (hubs > 0) sb.AppendLine("  The hub (where three or more strips meet, the flat point) takes one common pin or a plate through the holes marked 'hub'.");
        sb.AppendLine();
        sb.AppendLine("Sequence");
        sb.AppendLine("  1. Cut and engrave the strips. Each slot or hole is labelled with the strip it joins.");
        sb.AppendLine("  2. Lay the assembly drawing on the table (print at 100 %; check the 100 mm bar).");
        sb.AppendLine("  3. Place the A strips on it, outer edge up, then slot the B strips over them joint by joint; pin the hub.");
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
                        sb.Append($"<text x=\"{F(t.X)}\" y=\"{F(H - t.Y)}\" font-size=\"{F(t.Height)}\" font-family=\"Helvetica, Arial, sans-serif\" fill=\"{c}\" stroke=\"none\"{(t.Centre ? " text-anchor=\"middle\"" : "")}>{Esc(t.Value)}</text>\n");
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
