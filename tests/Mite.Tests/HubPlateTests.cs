using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// The hub plate of a flat kit: the six rays of the 3-fold Enneper web end on
/// a laser-cut plate (see <see cref="HubPlate"/>). Exact expectations: the rays
/// of the flat point are the three straight asymptotic lines of the monkey
/// saddle at 60°, the plate's geometry follows the rules of the class summary
/// in closed form, and the hub keeps its angles through the whole motion.
/// </summary>
public class HubPlateTests
{
    private sealed class Pressed
    {
        public ScissorNet Net = null!; public ScissorNet.Result Motion = null!; public int Anchor; public AsymptoticWeb.Result Web = null!;
    }

    private static readonly Lazy<Pressed> Enneper = new Lazy<Pressed>(() =>
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var w = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options());
        var proj = new MeshProjection(mesh);
        var (net, motion, anchor) = FlatKit.Press(w.A, w.B, w.Seed, new Vec3d(0, 0, 1), q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
        return new Pressed { Net = net, Motion = motion, Anchor = anchor, Web = w };
    });

    private static FlatKit.Result Kit(FlatKit.Options o)
    {
        var p = Enneper.Value;
        return FlatKit.Build(p.Net, p.Motion, p.Anchor, new Vec3d(0, 0, 1), o);
    }

    private static readonly FlatKit.Options TwoPlates = new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, Clearance = 0.0002, HubPlates = 2 };
    // a 2-unit web with 20 × 2 strips and 0.2 clearance (a 1 : 1 kit in metres: 20 × 2 mm strips, joints ≈ 98 mm apart)

    [Fact]
    public void Enneper3_SixRaysAt60Degrees_AnglesKeptThroughTheMotion()
    {
        var kit = Kit(TwoPlates);
        var plate = Assert.Single(kit.Plates);
        Assert.Empty(kit.PlatelessHubs);
        Assert.Equal(6, plate.Rays.Count);
        Assert.Equal(kit.Joints.Single(j => j.Hub).Id, plate.Joint);
        Assert.InRange(Math.Sqrt(plate.U * plate.U + plate.V * plate.V), 0, 1e-9); // the hub is the anchor: the origin of the assembly plane
        // the three straight asymptotic lines of the flat point: rays 60° apart (the web is built with its 3-fold symmetry)
        Assert.InRange(plate.AngleDeviation, 0, 1e-4);
        // the scissor mechanism keeps the hub's angles through the press: a rigid plate fits every state (measured 6e-6°)
        Assert.InRange(plate.AngleDrift, 0, 1e-3);
        Console.WriteLine($"hub: rays at 60° ± {plate.AngleDeviation:E2}°, drift over the motion {plate.AngleDrift:E2}° over {Enneper.Value.Motion.States.Count} states, first joint {plate.Rays.Min(r => r.FirstJoint):0.0000}…{plate.Rays.Max(r => r.FirstJoint):0.0000}, r_end {plate.EndRadius:0.00000}, r_in {plate.InnerRadius:0.00000}, R {plate.Radius:0.00000}, bridge {plate.MinBridge:0.00000}");
        // the ray directions in the flat state are the rays' first segments
        var sorted = plate.Rays.Select(r => r.Angle).OrderBy(a => a).ToArray();
        for (int k = 0; k < 6; k++)
        {
            double gap = (k == 5 ? sorted[0] + 2 * Math.PI : sorted[k + 1]) - sorted[k];
            Assert.InRange(gap * 180 / Math.PI, 59.4, 60.6);
        }
        // the rays are alternately A and B laths? no — the web labels every ray; all six are lath ends
        Assert.All(plate.Rays, r => Assert.True(r.FirstJoint > 0.5 * Enneper.Value.Web.Spacing));
    }

    [Fact]
    public void Radii_FollowTheRules_InClosedForm()
    {
        var o = TwoPlates;
        var kit = Kit(o);
        var p = kit.Plates[0];
        double s = o.Thickness + o.Clearance, tp = o.Thickness, w = o.Width;
        double alpha = p.Rays.Select((r, k) => { var sorted = p.Rays.Select(x => x.Angle).OrderBy(a => a).ToArray(); return k == 5 ? sorted[0] + 2 * Math.PI - sorted[5] : sorted[k + 1] - sorted[k]; }).Min();
        Assert.Equal(s, p.SlotWidth, 12);
        Assert.Equal(tp, p.Thickness, 12);
        Assert.Equal(2, p.Count);
        Assert.Equal(0.25 * w, p.Bolt, 12);                       // two plates: the kit's pin hole as the bolt
        double rEndClash = s / (2 * Math.Sin(0.5 * alpha));       // ≈ s for 60°
        Assert.InRange(rEndClash, 0.00218, 0.00223);                // ≈ s at 60°
        Assert.Equal(Math.Max(rEndClash, 0.5 * p.Bolt + tp), p.EndRadius, 12);
        Assert.Equal(Math.Max(Math.Max(1.5 * s, 2 * tp), 0.1 * w), p.Key, 12);
        Assert.Equal(tp + o.Clearance, p.NotchDepth, 12);
        Assert.Equal(Math.Max(p.EndRadius + p.Key, (s + tp) / (2 * Math.Sin(0.5 * alpha))), p.InnerRadius, 12);
        Assert.True(p.MinBridge >= tp - 1e-12, $"bridge {p.MinBridge} < plate thickness {tp}");
        Assert.True(p.Radius - p.InnerRadius >= w - 1e-12 || p.Cramped);
        Assert.True(p.Radius <= p.FreeRadius + 1e-12 || p.Cramped, "the plate reaches the first joint");
        Assert.False(p.Cramped);
        Assert.Equal(w - 2 * p.NotchDepth, p.StripWeb(w), 12);
    }

    [Fact]
    public void Outline_IsASimpleStarWithTheExactArea()
    {
        var kit = Kit(TwoPlates);
        var p = kit.Plates[0];
        var poly = p.Outline(Math.PI / 720);
        // counter-clockwise, simple: positive area equal to the disc less six slots (and the bolt hole counted separately)
        double area = HubPlate.Area(poly);
        double exact = p.ExactArea() + 0.25 * Math.PI * p.Bolt * p.Bolt;
        Assert.InRange(Math.Abs(area - exact) / exact, 0, 2e-5);
        // every vertex inside the rim, slot points at r_in or on the rim
        foreach (var (x, y) in poly) Assert.InRange(Math.Sqrt(x * x + y * y), p.InnerRadius - 1e-9, p.Radius + 1e-9);
        // no self-intersection: every pair of non-adjacent edges is disjoint
        for (int i = 0; i < poly.Count; i++)
            for (int j = i + 2; j < poly.Count; j++)
            {
                if (i == 0 && j == poly.Count - 1) continue;
                Assert.False(Cross(poly[i], poly[(i + 1) % poly.Count], poly[j], poly[(j + 1) % poly.Count]), $"edges {i} and {j} cross");
            }
        // one slot per ray, each centred on its ray and s wide
        var rays = p.Rays.OrderBy(r => r.Angle).ToList();
        for (int k = 0; k < rays.Count; k++)
        {
            double ca = Math.Cos(rays[k].Angle), sa = Math.Sin(rays[k].Angle);
            var inner = poly.Where(q => Math.Abs(q.X * ca + q.Y * sa - p.InnerRadius) < 1e-9 && Math.Abs(-q.X * sa + q.Y * ca) < p.SlotWidth).ToList();
            Assert.Equal(2, inner.Count);
            var (ax, ay) = inner[0]; var (bx, by) = inner[1];
            Assert.Equal(p.SlotWidth, Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by)), 9);
            double mx = 0.5 * (ax + bx), my = 0.5 * (ay + by);
            Assert.Equal(rays[k].Angle, Math.Atan2(my, mx), 9);
        }
    }

    private static bool Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        double O(( double X, double Y) p, (double X, double Y) q, (double X, double Y) r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

    [Fact]
    public void Strips_StopShortOfTheHub_AndCarryTheNotches()
    {
        var o = TwoPlates;
        var kit = Kit(o);
        var p = kit.Plates[0];
        var hubStrips = kit.Strips.Where(s => s.Marks.Any(m => m.Label == "hub")).ToList();
        Assert.Equal(6, hubStrips.Count);
        Assert.DoesNotContain(kit.Strips.SelectMany(s => s.Marks), m => m.Kind == FlatKit.MarkKind.Hub); // no common pin hole any more
        foreach (var s in hubStrips)
        {
            var ray = p.Rays.Single(r => r.Lath == s.Lath);
            var lath = kit.Net.Laths[s.Lath];
            double arc = kit.Net.RestArcs[s.Lath].Sum();
            // the strip is the lath's arc less r_end at the hub end (plus the usual extension at the other end)
            double trim = ray.AtStart ? s.ExtendStart : s.ExtendEnd;
            Assert.Equal(-p.EndRadius, trim, 12);
            Assert.Equal(arc + s.ExtendStart + s.ExtendEnd, s.Length, 12);
            // two notches (inner and outer edge) over the key length at the hub end, t_p + c deep
            var notches = s.Marks.Where(m => m.Label == "hub").ToList();
            Assert.Equal(2, notches.Count);
            Assert.Contains(notches, m => m.Kind == FlatKit.MarkKind.SlotInner);
            Assert.Contains(notches, m => m.Kind == FlatKit.MarkKind.SlotOuter);
            foreach (var m in notches)
            {
                Assert.Equal(p.Key + o.Clearance, m.Size, 12);
                Assert.Equal(p.NotchDepth, m.Depth, 12);
                Assert.Equal(p.Joint, m.Joint);
                double at = ray.AtStart ? 0.5 * m.Size : s.Length - 0.5 * m.Size;
                Assert.Equal(at, m.At, 12);
            }
            // the first joint's slot clears the notch and the plate
            var first = s.Marks.Where(m => m.Label != "hub").Select(m => ray.AtStart ? m.At : s.Length - m.At).Min();
            Assert.True(first - 0.5 * s.Marks.Where(m => m.Label != "hub").First().Size > p.Radius - p.EndRadius, "the plate reaches the first slot");
            // the outline removes exactly the notches and the slots
            var outline = KitExport.StripOutline(s, 1000.0);
            double area = Math.Abs(HubPlate.Area(outline)) * 1e-6;
            double expect = s.Length * s.Width - s.Marks.Where(m => m.Kind != FlatKit.MarkKind.Hole).Sum(m => m.Size * m.Depth);
            Assert.Equal(expect, area, 9);
        }
        // the other strips are untouched by the plate: same lengths as the kit without plates
        var plain = Kit(new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, Clearance = 0.0002 });
        foreach (var s in kit.Strips.Except(hubStrips))
            Assert.Equal(plain.Strips[kit.Strips.IndexOf(s)].Length, s.Length, 12);
        // the strips meeting at the hub do not clash: at the strip ends the centre lines are ≥ t + c apart
        var rays = p.Rays.OrderBy(r => r.Angle).ToList();
        for (int k = 0; k < rays.Count; k++)
        {
            double a = rays[k].Angle, b = rays[(k + 1) % rays.Count].Angle;
            double gap = 2 * p.EndRadius * Math.Sin(0.5 * Math.Abs(((b - a) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI)));
            Assert.True(gap >= o.Thickness + o.Clearance - 1e-12);
        }
    }

    [Fact]
    public void OnePlate_NotchesTheInnerEdgeOnly_NoBolt()
    {
        var kit = Kit(new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, HubPlates = 1 });
        var p = Assert.Single(kit.Plates);
        Assert.Equal(1, p.Count);
        Assert.Equal(0, p.Bolt);
        Assert.Equal(0.002 / (2 * Math.Sin(Math.PI / 6)), p.EndRadius, 4); // no bolt: the clash radius alone (≈ t at 60°)
        var notches = kit.Strips.SelectMany(s => s.Marks).Where(m => m.Label == "hub").ToList();
        Assert.Equal(6, notches.Count);
        Assert.All(notches, m => Assert.Equal(FlatKit.MarkKind.SlotInner, m.Kind));
        Assert.Equal(0.02 - p.NotchDepth, p.StripWeb(0.02), 12);
    }

    [Fact]
    public void FlatStrips_KeepThePinHub_Default_IsUnchanged()
    {
        var flat = Kit(new FlatKit.Options { Width = 0.1, Thickness = 0.01, Upright = false, HubPlates = 2 });
        Assert.Empty(flat.Plates);
        Assert.Equal(6, flat.Strips.SelectMany(s => s.Marks).Count(m => m.Kind == FlatKit.MarkKind.Hub));
        var plain = Kit(new FlatKit.Options { Width = 0.1, Thickness = 0.01, Upright = true });
        Assert.Empty(plain.Plates);
        Assert.Equal(6, plain.Strips.SelectMany(s => s.Marks).Count(m => m.Kind == FlatKit.MarkKind.Hub));
        Assert.All(plain.Strips, s => Assert.True(s.ExtendStart >= 0 && s.ExtendEnd >= 0));
    }

    [Fact]
    public void GivenRadius_AndCrampedWarning()
    {
        var p = Enneper.Value;
        double first = Kit(TwoPlates).Plates[0].Rays.Min(r => r.FirstJoint);
        var big = Kit(new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, HubPlates = 1, HubRadius = 0.95 * first });
        Assert.Equal(0.95 * first, big.Plates[0].Radius, 12);
        Assert.True(big.Plates[0].Cramped);
        var wide = Kit(new FlatKit.Options { Width = 0.1, Thickness = 0.01, Upright = true, HubPlates = 1 });
        Assert.True(wide.Plates[0].Cramped);                          // strips as wide as the joint spacing: no room for a slot a width long
        Assert.True(wide.Plates[0].Radius <= wide.Plates[0].FreeRadius + 1e-12 || wide.Plates[0].Radius >= wide.Plates[0].InnerRadius + 0.05);
        var thin = Kit(new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, HubPlates = 1, HubThickness = 0.001 });
        Assert.Equal(0.001, thin.Plates[0].Thickness, 12);
        Assert.Equal(0.001, thin.Plates[0].NotchDepth, 12);
    }

    /// <summary>
    /// The mesh decides how exactly the rays sit at 60°: the n-fold Enneper chart
    /// at resolution 40 is not 3-fold symmetric (40 / 3 is not an integer), the
    /// traced rays leave 60° by about 1°, and then nothing in the mechanism holds
    /// a singular node's angles — the press turns the rays by a fraction of a
    /// degree. The plate follows the measured flat rays and reports both numbers.
    /// </summary>
    [Fact]
    public void CoarseAsymmetricMesh_PlateFollowsTheMeasuredRays_AndReportsTheDrift()
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 40).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var (mn, mx) = mesh.BoundingBox();
        var w = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options { Spacing = 0.08 * (mx - mn).Length });   // the web app's "coarse" preset
        var proj = new MeshProjection(mesh);
        var (net, motion, anchor) = FlatKit.Press(w.A, w.B, w.Seed, new Vec3d(0, 0, 1), q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal, 8, 0.3, 40);
        var kit = FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), new FlatKit.Options { Width = 0.02, Thickness = 0.002, Upright = true, HubPlates = 2 });
        var p = Assert.Single(kit.Plates);
        Assert.Equal(6, p.Rays.Count);
        Console.WriteLine($"coarse hub: rays at 60° ± {p.AngleDeviation:0.00}°, drift {p.AngleDrift:0.000}° over {motion.States.Count} states, {kit.Strips.Count} strips");
        Assert.InRange(p.AngleDeviation, 0.5, 3.0);                  // measured 1.45°
        Assert.InRange(p.AngleDrift, 0.01, 1.0);                     // measured 0.40°: not held, not large
        // the slots sit on the measured rays of the flat state, whatever the deviation
        var flat = motion.Last;
        foreach (var r in p.Rays)
        {
            var lath = net.Laths[r.Lath];
            var d = flat.Nodes[r.AtStart ? lath[1] : lath[lath.Length - 2]] - flat.Nodes[p.Node];
            Assert.Equal(Math.Atan2(d.Y, d.X), r.Angle, 9);          // the assembly plane is the XY plane here (normal Z, anchor the hub)
        }
        // and the outline is still a simple star with the exact area
        var poly = p.Outline(Math.PI / 720);
        Assert.InRange(Math.Abs(HubPlate.Area(poly) - (p.ExactArea() + 0.25 * Math.PI * p.Bolt * p.Bolt)) / p.ExactArea(), 0, 2e-5);
    }

    /// <summary>
    /// A planar star: six rays from one hub at uneven angles, each crossed once at
    /// radius 1 by a short bar — the smoke test's case (there with regular angles),
    /// and an irregular hub whose plate must follow the measured ray directions.
    /// </summary>
    [Fact]
    public void IrregularStar_PlateFollowsTheRays_SmokeCaseCounts()
    {
        double[] deg = { 0, 55, 125, 180, 240, 300 };
        var rays = new List<Vec3d[]>(); var bars = new List<Vec3d[]>();
        foreach (double dg in deg)
        {
            double a = dg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            rays.Add(new[] { Vec3d.Zero, new Vec3d(ca, sa, 0), new Vec3d(2 * ca, 2 * sa, 0) });
            bars.Add(new[] { new Vec3d(ca + 0.5 * sa, sa - 0.5 * ca, 0), new Vec3d(ca, sa, 0), new Vec3d(ca - 0.5 * sa, sa + 0.5 * ca, 0) });
        }
        var (net, motion, anchor) = FlatKit.Press(rays, bars, Vec3d.Zero, new Vec3d(0, 0, 1));
        var o = new FlatKit.Options { Width = 0.05, Thickness = 0.005, Upright = true, HubPlates = 2 };
        var kit = FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), o);
        Assert.Equal(12, kit.Strips.Count);
        Assert.Equal(7, kit.Joints.Count);
        var p = Assert.Single(kit.Plates);
        Assert.Equal(6, p.Rays.Count);
        // the plate's slots sit on the measured rays, not on a regular hexagon
        var got = p.Rays.Select(r => ((r.Angle * 180 / Math.PI) % 360 + 360) % 360).OrderBy(x => x).ToArray();
        for (int k = 0; k < 6; k++) Assert.Equal(deg[k], got[k], 6);
        Assert.Equal(10, p.AngleDeviation, 6);                       // gaps 55, 70, 55, 60, 60, 60: the 70° gap is 10° off
        Assert.InRange(p.AngleDrift, 0, 1e-9);                       // a flat net pressed flat does not move
        // the smallest gap (55°) sets the clash radius and the bridge
        double s = o.Thickness, alpha = 55 * Math.PI / 180;
        Assert.Equal(Math.Max(s / (2 * Math.Sin(alpha / 2)), 0.5 * 0.25 * o.Width + o.Thickness), p.EndRadius, 12);
        Assert.True(2 * p.InnerRadius * Math.Sin(alpha / 2) - s >= o.Thickness - 1e-12);
        // first joints at radius 1 → automatic radius 0.45
        Assert.All(p.Rays, r => Assert.Equal(1.0, r.FirstJoint, 9));
        Assert.Equal(0.45, p.Radius, 9);
        Assert.False(p.Cramped);
        // the sheets: 12 strip outlines, then the plate sheet with 2 outlines and 2 bolt holes (what the smoke test counts: 16 cut curves)
        var sheets = KitExport.StripSheets(kit, new KitExport.Options { ToMm = 1000, SheetWidth = 3000, SheetHeight = 1000 });
        int cut = sheets.Sum(d => d.Shapes.Count(x => x.Layer == KitExport.Layer.Cut));
        Assert.Equal(16, cut);
        Assert.Equal(12, sheets.Take(sheets.Count - 1).Sum(d => d.Shapes.OfType<KitExport.Poly>().Count(x => x.Layer == KitExport.Layer.Cut)));
        // simple outline with the exact area also for uneven gaps
        var poly = p.Outline(Math.PI / 720);
        Assert.InRange(Math.Abs(HubPlate.Area(poly) - (p.ExactArea() + 0.25 * Math.PI * p.Bolt * p.Bolt)) / p.ExactArea(), 0, 2e-5);
    }

    [Fact]
    public void Export_PlateSheet_CutList_Readme_CarryThePlates()
    {
        var kit = Kit(TwoPlates);
        var eo = new KitExport.Options { ToMm = 1000, SheetWidth = 600, SheetHeight = 400 };
        var sheets = KitExport.StripSheets(kit, eo);
        var plateSheet = sheets.Last();
        Assert.StartsWith("hub plate", plateSheet.Title);
        // two identical plates: two closed cut outlines with the exact area, two bolt holes, slot labels for every ray
        var outlines = plateSheet.Shapes.OfType<KitExport.Poly>().Where(s => s.Layer == KitExport.Layer.Cut && s.Closed).ToList();
        Assert.Equal(2, outlines.Count);
        double exact = (kit.Plates[0].ExactArea() + 0.25 * Math.PI * kit.Plates[0].Bolt * kit.Plates[0].Bolt) * 1e6;
        foreach (var o in outlines) Assert.InRange(Math.Abs(HubPlate.Area(o.Points) - exact) / exact, 0, 1e-3);
        Assert.Equal(2, plateSheet.Shapes.OfType<KitExport.Circle>().Count(c => c.Layer == KitExport.Layer.Cut));
        var labels = plateSheet.Shapes.OfType<KitExport.Text>().Select(t => t.Value).ToList();
        foreach (var r in kit.Plates[0].Rays) Assert.Equal(2, labels.Count(l => l == r.Label));
        // the strips are still on the earlier sheets, every strip exactly once
        var stripLabels = sheets.Take(sheets.Count - 1).SelectMany(d => d.Shapes.OfType<KitExport.Text>()).Select(t => t.Value).Where(v => v.Contains("↑out")).ToList();
        // (as nested: strips longer than the bed are split into lapped pieces since 2026-10-20, so count the pieces)
        Assert.Equal(KitExport.Nest(kit, eo).Strips.Count, stripLabels.Count);
        Assert.True(stripLabels.Count >= kit.Strips.Count);
        // cut list: one row per plate after the strips, with the rays
        var csv = KitExport.CutList(kit, eo);
        var rows = csv.Split('\n').Where(l => l.StartsWith("hub")).ToList();
        Assert.Equal(2, rows.Count);
        foreach (var r in kit.Plates[0].Rays) Assert.Contains(r.Label, rows[0]);
        Assert.Contains((kit.Plates[0].Radius * 2000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), rows[0]);
        // files and README
        var files = KitExport.Files(kit, sheets, KitExport.Assembly(kit, 0.01, eo), KitExport.Deployment(kit), eo);
        var readme = files.Single(f => f.Name == "README.txt").Text;
        Assert.Contains("hub plate", readme);
        Assert.Contains("bolt", readme);
        var dxf = files.Single(f => f.Name == "strips.dxf").Text;
        Assert.Contains("hub", dxf);
        // every sheet is a valid SVG
        foreach (var f in files.Where(f => f.Name.EndsWith(".svg"))) System.Xml.Linq.XDocument.Parse(f.Text);
    }
}
