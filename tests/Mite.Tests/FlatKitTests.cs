using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// The flat kit of a deployable asymptotic gridshell: the net pressed flat is
/// the assembly, every lath one strip with its joints at their spacing.
/// </summary>
public class FlatKitTests
{
    private static (FlatKit.Result kit, AsymptoticWeb.Result web) Enneper3Kit()
    {
        var mesh = AnalyticShapes.Build("enneper3", 3, 0.9, 0, 48).ToTriangulated();
        var pc = PrincipalCurvature.Compute(mesh, 2);
        var w = AsymptoticWeb.Build(mesh, pc, -1, new AsymptoticWeb.Options());
        var proj = new MeshProjection(mesh);
        var (net, motion, anchor) = FlatKit.Press(w.A, w.B, w.Seed, new Vec3d(0, 0, 1), q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
        var kit = FlatKit.Build(net, motion, anchor, new Vec3d(0, 0, 1), new FlatKit.Options { Width = 0.1, Thickness = 0.01, Upright = true });
        return (kit, w);
    }

    private static double Arc(Vec3d[] l) { double s = 0; for (int i = 1; i < l.Length; i++) s += (l[i] - l[i - 1]).Length; return s; }

    [Fact]
    public void Enneper3Kit_StripsCarryTheJointSpacing_AndFitTheFlatGrid()
    {
        var (kit, w) = Enneper3Kit();
        var net = kit.Net;
        Assert.Equal(net.Laths.Count, kit.Strips.Count);
        Assert.InRange(kit.Flatness, 0, 1e-3 * w.Spacing);
        Assert.InRange(kit.Drift, 0, 3e-3);

        // one hub: the flat point, where the six rays meet
        var hubs = kit.Joints.Where(j => j.Hub).ToList();
        Assert.Single(hubs);
        Assert.Equal(6, hubs[0].Laths.Length);
        Assert.InRange(Math.Sqrt(hubs[0].U * hubs[0].U + hubs[0].V * hubs[0].V), 0, 1e-9);

        foreach (var s in kit.Strips)
        {
            var lath = net.Laths[s.Lath];
            // marks at the arc length of their joints along the lath (+ the start extension)
            double at = s.ExtendStart; int k = 0;
            for (int i = 0; i < lath.Length; i++)
            {
                if (i > 0) at += net.RestArcs[s.Lath][i - 1];
                if (net.NodeLaths[lath[i]].Length < 2) continue;
                Assert.Equal(at, s.Marks[k].At, 9);
                k++;
            }
            Assert.Equal(k, s.Marks.Count);
            // the strip is the lath's arc length plus its end extensions, and the arc is at least the chord sum
            double arc = net.RestArcs[s.Lath].Sum(), chord = net.RestLengths[s.Lath].Sum();
            Assert.Equal(arc + s.ExtendStart + s.ExtendEnd, s.Length, 9);
            Assert.InRange(arc - chord, -1e-12, 0.01 * chord);
            // in the flat drawing consecutive joints sit their chord apart (the scissor joints keep it)
            var f = kit.Flat[s.Lath];
            for (int i = 0; i + 1 < f.Length; i++)
            {
                double d = Math.Sqrt((f[i + 1].U - f[i].U) * (f[i + 1].U - f[i].U) + (f[i + 1].V - f[i].V) * (f[i + 1].V - f[i].V));
                Assert.InRange(Math.Abs(d - net.RestLengths[s.Lath][i]) / net.RestLengths[s.Lath][i], 0, 3e-3);
            }
            // no mark closer to an end than the material it needs
            foreach (var m in s.Marks) { Assert.True(m.At > 0 && m.At < s.Length); }
        }

        // every crossing is on exactly two strips, each labelled with the other: A slotted from the outer edge, B from the inner
        var byJoint = kit.Strips.SelectMany(s => s.Marks.Select(m => (s, m))).Where(x => x.m.Kind != FlatKit.MarkKind.Hub).GroupBy(x => x.m.Joint).ToList();
        Assert.Equal(kit.Joints.Count - 1, byJoint.Count);
        foreach (var g in byJoint)
        {
            var pair = g.ToList();
            Assert.Equal(2, pair.Count);
            Assert.Equal(pair[1].s.Label, pair[0].m.Label);
            Assert.Equal(pair[0].s.Label, pair[1].m.Label);
            var a = pair.Single(x => x.s.FamilyA); var b = pair.Single(x => !x.s.FamilyA);
            Assert.Equal(FlatKit.MarkKind.SlotOuter, a.m.Kind);
            Assert.Equal(FlatKit.MarkKind.SlotInner, b.m.Kind);
            // slot = the other strip's footprint at the smallest angle the joint reaches
            var j = kit.Joints[g.Key - 1];
            Assert.Equal(0.01 / Math.Sin(j.MinAngle * Math.PI / 180), a.m.Size, 9);
            Assert.Equal(0.05, a.m.Depth, 9);
        }
        // the joints close while the net flattens: some slots are wider than the sheet thickness
        Assert.True(kit.Joints.Min(j => j.MinAngle) < 60);

        // the deployment runs from the flat grid to the curved net
        Assert.InRange(kit.Deployment[0].SelectMany(l => l).Max(p => Math.Abs(p.Z)), 0, 1e-3 * w.Spacing);
        Assert.True(kit.Deployment[kit.Deployment.Count - 1].SelectMany(l => l).Max(p => Math.Abs(p.Z)) > 2 * w.Spacing);
    }

    [Fact]
    public void Export_SvgDxfZip_AreWellFormed_AndTheStripOutlineRemovesExactlyTheSlots()
    {
        var (kit, _) = Enneper3Kit();
        var o = new KitExport.Options { ToMm = 300, SheetWidth = 600, SheetHeight = 400 };
        var sheets = KitExport.StripSheets(kit, o);
        // every strip is drawn once
        int outlines = sheets.Sum(d => d.Shapes.Count(s => s is KitExport.Poly p && p.Layer == KitExport.Layer.Cut && p.Closed));
        Assert.Equal(kit.Strips.Count, outlines);
        foreach (var d in sheets.Where(d => d.Title.StartsWith("sheet")))
            foreach (var p in d.Shapes.OfType<KitExport.Poly>())
                Assert.All(p.Points, q => { Assert.InRange(q.X, 0, o.SheetWidth); Assert.InRange(q.Y, 0, o.SheetHeight); });

        // outline area = length × width − the slots
        foreach (var s in kit.Strips)
        {
            var pts = KitExport.StripOutline(s, o.ToMm);
            double a2 = 0;
            for (int i = 0; i < pts.Count; i++) { var p = pts[i]; var q = pts[(i + 1) % pts.Count]; a2 += p.X * q.Y - q.X * p.Y; }
            double expect = s.Length * s.Width * o.ToMm * o.ToMm - s.Marks.Where(m => m.Kind != FlatKit.MarkKind.Hole && m.Kind != FlatKit.MarkKind.Hub).Sum(m => m.Size * m.Depth) * o.ToMm * o.ToMm;
            Assert.Equal(expect, 0.5 * a2, 6);
        }

        var assembly = KitExport.Assembly(kit, 0.01, o);
        var deploy = KitExport.Deployment(kit);
        foreach (var d in sheets.Append(assembly).Append(deploy))
        {
            var x = XDocument.Parse(KitExport.ToSvg(d));
            Assert.Equal("svg", x.Root!.Name.LocalName);
        }
        var dxf = KitExport.ToDxf(sheets);
        Assert.StartsWith("0\nSECTION", dxf);
        Assert.EndsWith("0\nEOF\n", dxf);
        int holes = kit.Strips.Sum(s => s.Marks.Count(m => m.Kind == FlatKit.MarkKind.Hub || m.Kind == FlatKit.MarkKind.Hole));
        Assert.Equal(holes, dxf.Split('\n').Count(l => l == "CIRCLE"));

        var zip = new ZipArchive(new MemoryStream(KitExport.Zip(kit, sheets, assembly, deploy, o)));
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Contains("assembly.svg", names); Assert.Contains("strips.dxf", names); Assert.Contains("cutlist.csv", names); Assert.Contains("README.txt", names);
        Assert.Equal(sheets.Count, names.Count(n => n.StartsWith("strips-")));
        using var r = new StreamReader(zip.GetEntry("cutlist.csv")!.Open());
        Assert.Equal(kit.Strips.Count + 1, r.ReadToEnd().Trim().Split('\n').Length);
    }
}
