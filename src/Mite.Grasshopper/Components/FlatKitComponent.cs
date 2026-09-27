using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

/// <summary>
/// The fabrication kit of a deployable asymptotic gridshell: the net pressed
/// flat (Net Kinetics' Flatten) is the assembly drawing, every lath one strip
/// with its joints at their spacing, and the motion read backwards is the
/// deployment. See <see cref="FlatKit"/> and <see cref="KitExport"/>.
/// </summary>
public class FlatKitComponent : MiteComponent
{
    public FlatKitComponent()
        : base("Flat Kit", "Kit",
            "Cut, assemble flat, deploy: presses an asymptotic net flat as a scissor mechanism (Net Kinetics' Flatten) and turns the flat state into a kit. " +
            "Every lath becomes one straight strip with its joints at their spacing — the length the scissor joints keep through the whole motion — " +
            "so the strips fit the flat grid and the curved one alike. Crossings of upright laths are halving slots (A from the outer edge, B from the inner edge, " +
            "as wide as the other strip at the smallest angle the joint reaches), crossings of flat strips pin holes, and a node of three or more laths " +
            "(the flat point of a 3-fold Enneper web) a hub. Outputs the flat assembly (joints numbered, laths labelled), the strips laid out on sheets, " +
            "the deployment states, a cut list, and — with Write — SVG (red cut, blue engrave), DXF and CSV files in millimetres.",
            "Fabrication", "FlatKit") { }

    public override Guid ComponentGuid => new("4E2B8C1D-7A3F-4C59-9E0B-6D1F2A3C4B58");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "First lath family (asymptotic curves, e.g. Asymptotic Web A)", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Second lath family", GH_ParamAccess.list);
        pManager.AddMeshParameter("Mesh", "M", "Optional surface mesh: its normals start the joint normals", GH_ParamAccess.item);
        pManager[2].Optional = true;
        pManager.AddPointParameter("Anchor", "P", "The joint held while the net is pressed flat (e.g. the Asymptotic Web seed); default the joint nearest the centre", GH_ParamAccess.item);
        pManager[3].Optional = true;
        pManager.AddVectorParameter("Normal", "N", "Normal of the assembly plane (default Z)", GH_ParamAccess.item, Vector3d.ZAxis);
        pManager.AddNumberParameter("Width", "W", "Strip width (model units; along the surface normal for upright laths)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Strip thickness = sheet thickness (model units)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "True: laths stand upright on the surface (asymptotic gridshells)", GH_ParamAccess.item, true);
        pManager.AddIntegerParameter("Joint", "J", "0 automatic (slots for upright laths, pins for flat), 1 halving slots, 2 pin holes", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Hole", "H", "Pin hole diameter (model units; 0 = a quarter of the width)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Clearance", "Cl", "Fit clearance added to slot widths and depths (model units)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("SheetWidth", "Sw", "Sheet (laser bed) width in mm", GH_ParamAccess.item, 600.0);
        pManager.AddNumberParameter("SheetHeight", "Sh", "Sheet height in mm", GH_ParamAccess.item, 400.0);
        pManager.AddNumberParameter("Stiffness", "K", "Lath bending stiffness while pressing flat (as Net Kinetics)", GH_ParamAccess.item, 0.3);
        pManager.AddIntegerParameter("Steps", "St", "States of the motion (the deployment frames)", GH_ParamAccess.item, 8);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic: 1/500 of the net size)", GH_ParamAccess.item, 0.0);
        pManager.AddTextParameter("Folder", "F", "Folder for the files (strips-NN.svg, strips.dxf, assembly.svg/.dxf, deployment.svg, cutlist.csv, README.txt)", GH_ParamAccess.item);
        pManager[16].Optional = true;
        pManager.AddBooleanParameter("Write", "Wr", "Write the files to Folder", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("FlatA", "FA", "Family A laths in the flat state (in the assembly plane through the anchor)", GH_ParamAccess.list);
        pManager.AddCurveParameter("FlatB", "FB", "Family B laths in the flat state", GH_ParamAccess.list);
        pManager.AddPointParameter("Joints", "J", "Joints in the flat state, numbered as on the drawings", GH_ParamAccess.list);
        pManager.AddIntegerParameter("JointIds", "Ji", "Joint number of each joint", GH_ParamAccess.list);
        pManager.AddTextParameter("Labels", "L", "Lath labels (A1…, B1…), one per lath, at LabelPoints", GH_ParamAccess.list);
        pManager.AddPointParameter("LabelPoints", "Lp", "Outer end of each lath in the flat state", GH_ParamAccess.list);
        pManager.AddCurveParameter("Strips", "S", "Cut curves of every strip (outlines with slots, holes), laid out sheet by sheet below the assembly, model units", GH_ParamAccess.list);
        pManager.AddTextParameter("StripText", "Tx", "Engraved labels of the strips (at StripTextPoints)", GH_ParamAccess.list);
        pManager.AddPointParameter("StripTextPoints", "Tp", "Positions of the engraved labels", GH_ParamAccess.list);
        pManager.AddCurveParameter("DeployA", "DA", "Family A laths per deployment state (one branch per state, flat first)", GH_ParamAccess.tree);
        pManager.AddCurveParameter("DeployB", "DB", "Family B laths per deployment state", GH_ParamAccess.tree);
        pManager.AddTextParameter("CutList", "CL", "Cut list (CSV, mm): strip, family, length, width, joints", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Summary", GH_ParamAccess.item);
        pManager.AddTextParameter("Files", "Fi", "Files written", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var curvesA = new List<Curve>(); var curvesB = new List<Curve>();
        if (!DA.GetDataList(0, curvesA)) return;
        if (!DA.GetDataList(1, curvesB)) return;
        Mesh? mesh = null; DA.GetData(2, ref mesh);
        Point3d anchorPt = Point3d.Unset; bool hasAnchor = DA.GetData(3, ref anchorPt);
        var normal = Vector3d.ZAxis; DA.GetData(4, ref normal); if (normal.IsTiny()) normal = Vector3d.ZAxis;
        double width = 0.1, thickness = 0.01, hole = 0, clearance = 0, sheetW = 600, sheetH = 400, stiffness = 0.3, sampling = 0;
        bool upright = true, write = false; int joint = 0, steps = 8; string? folder = null;
        DA.GetData(5, ref width); DA.GetData(6, ref thickness); DA.GetData(7, ref upright); DA.GetData(8, ref joint);
        DA.GetData(9, ref hole); DA.GetData(10, ref clearance); DA.GetData(11, ref sheetW); DA.GetData(12, ref sheetH);
        DA.GetData(13, ref stiffness); DA.GetData(14, ref steps); DA.GetData(15, ref sampling); DA.GetData(16, ref folder); DA.GetData(17, ref write);
        if (width <= 0 || thickness <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Thickness must be positive."); return; }

        if (sampling <= 0)
        {
            var bbox = BoundingBox.Empty;
            foreach (var c in curvesA.Concat(curvesB)) if (c != null) bbox.Union(c.GetBoundingBox(false));
            sampling = bbox.IsValid ? Math.Max(bbox.Diagonal.Length / 500.0, 1e-9) : 0.01;
        }
        var famA = NetJointsComponent.SampleAligned(curvesA, sampling);
        var famB = NetJointsComponent.SampleAligned(curvesB, sampling);
        Func<Vec3d, Vec3d>? normalAt = null;
        if (mesh != null)
        {
            var input = MeshConvert.Load(mesh);
            if (input != null) { var proj = new MeshProjection(input.Data); normalAt = p => proj.ClosestPoint(p, -1).SmoothNormal; }
        }
        Vec3d anchor;
        if (hasAnchor) anchor = MeshConvert.ToVec3d(anchorPt);
        else
        {
            var all = famA.Concat(famB).SelectMany(l => l).ToList();
            anchor = all.Count > 0 ? all.Aggregate(Vec3d.Zero, (a, b) => a + b) / all.Count : Vec3d.Zero;
        }
        var nz = MeshConvert.ToVec3d(normal);

        FlatKit.Result kit;
        try
        {
            var (net, motion, a) = FlatKit.Press(famA, famB, anchor, nz, normalAt, Math.Max(2, steps), Math.Max(0.01, stiffness), 60, Cancelled);
            kit = FlatKit.Build(net, motion, a, nz, new FlatKit.Options
            {
                Width = width, Thickness = thickness, Upright = upright, Joint = (FlatKit.JointKind)Math.Max(0, Math.Min(2, joint)), HoleDiameter = hole, Clearance = clearance,
            });
        }
        catch (ArgumentException ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }
        if (kit.Motion.Cancelled) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc: the motion is partial and the kit is not flat.");

        double toMm = ModelToMeters() * 1000;
        var eo = new KitExport.Options { ToMm = toMm, SheetWidth = sheetW, SheetHeight = sheetH };
        var sheets = KitExport.StripSheets(kit, eo);
        var assembly = KitExport.Assembly(kit, upright ? thickness : width, eo);
        var deployment = KitExport.Deployment(kit);

        // flat assembly in the plane through the anchor
        Point3d W(double u, double v) => MeshConvert.ToRhinoPoint(kit.Origin + u * kit.AxisU + v * kit.AxisV);
        var flatA = new List<Curve>(); var flatB = new List<Curve>(); var labelPts = new List<Point3d>();
        for (int l = 0; l < kit.Flat.Count; l++)
        {
            var f = kit.Flat[l];
            var c = new PolylineCurve(f.Select(p => W(p.U, p.V)));
            (l < kit.Net.CountA ? flatA : flatB).Add(c);
            var end = f[0].U * f[0].U + f[0].V * f[0].V > f[f.Length - 1].U * f[f.Length - 1].U + f[f.Length - 1].V * f[f.Length - 1].V ? f[0] : f[f.Length - 1];
            labelPts.Add(W(end.U, end.V));
        }
        var joints = kit.Joints.Select(j => W(j.U, j.V)).ToList();

        // strips below the assembly, sheets side by side, in model units
        double minV = kit.Flat.SelectMany(f => f).Min(p => p.V), minU = kit.Flat.SelectMany(f => f).Min(p => p.U);
        double ox = minU, oy = minV - 0.1 * (kit.Flat.SelectMany(f => f).Max(p => p.V) - minV);
        var stripCurves = new List<Curve>(); var stripText = new List<string>(); var stripTextPts = new List<Point3d>();
        foreach (var sh in sheets)
        {
            double top = oy;
            Point3d S2(double x, double y) => W(ox + x / toMm, top - (sh.Height - y) / toMm);
            foreach (var s in sh.Shapes)
            {
                switch (s)
                {
                    case KitExport.Poly p when s.Layer == KitExport.Layer.Cut:
                        var pts = p.Points.Select(q => S2(q.X, q.Y)).ToList(); if (p.Closed) pts.Add(pts[0]);
                        stripCurves.Add(new PolylineCurve(pts)); break;
                    case KitExport.Circle c when s.Layer == KitExport.Layer.Cut:
                        stripCurves.Add(new ArcCurve(new Circle(new Plane(S2(c.X, c.Y), MeshConvert.ToRhinoVector(kit.AxisU), MeshConvert.ToRhinoVector(kit.AxisV)), c.R / toMm))); break;
                    case KitExport.Text t:
                        stripText.Add(t.Value); stripTextPts.Add(S2(t.X, t.Y)); break;
                }
            }
            ox += (sh.Width + 20) / toMm;
        }

        var deployA = new DataTree<Curve>(); var deployB = new DataTree<Curve>();
        for (int k = 0; k < kit.Deployment.Count; k++)
        {
            var path = BranchPath(DA, k);
            for (int l = 0; l < kit.Deployment[k].Count; l++)
                (l < kit.Net.CountA ? deployA : deployB).Add(MeshConvert.ToPolylineCurve(kit.Deployment[k][l]), path);
        }

        var files = new List<string>();
        if (write)
        {
            if (string.IsNullOrWhiteSpace(folder)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Set a Folder to write the files.");
            else
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    foreach (var (name, text) in KitExport.Files(kit, sheets, assembly, deployment, eo))
                    {
                        var path = Path.Combine(folder, name);
                        File.WriteAllText(path, text, new UTF8Encoding(false));
                        files.Add(path);
                    }
                }
                catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Writing the kit failed: " + ex.Message); }
            }
        }

        int tooLong = kit.Strips.Count(s => s.Length * toMm + 2 * eo.Gap > sheetW);
        if (tooLong > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{tooLong} strips are longer than the {sheetW:0} mm sheet: they are on their own drawing — cut on a longer bed or split them with Lath Segment.");
        if (kit.Flatness > 0.01 * kit.Net.Scale) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The pressed net is not flat ({kit.Flatness:G3} out of plane): raise Steps or lower Stiffness.");

        var slots = kit.Strips.SelectMany(s => s.Marks).Where(m => m.Kind == FlatKit.MarkKind.SlotOuter || m.Kind == FlatKit.MarkKind.SlotInner).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Flat Kit: {kit.Strips.Count} strips ({kit.Net.CountA} A + {kit.Strips.Count - kit.Net.CountA} B), {kit.Joints.Count} joints ({kit.Joints.Count(j => j.Hub)} hubs), {kit.TotalLength * toMm / 1000:0.##} m of {width * toMm:0.#} × {thickness * toMm:0.#} mm strip");
        sb.AppendLine($"Flat to {kit.Flatness * toMm:0.###} mm, joint spacing kept to {kit.Drift:P2}; longest strip {kit.Strips.Max(s => s.Length) * toMm:0} mm");
        if (slots.Count > 0) sb.AppendLine($"Halving slots {slots.Min(m => m.Size) * toMm:0.##}–{slots.Max(m => m.Size) * toMm:0.##} mm wide (joints turn down to {kit.Joints.Min(j => j.MinAngle):0}°)");
        sb.Append($"{sheets.Count(s => s.Title.StartsWith("sheet"))} sheets of {sheetW:0} × {sheetH:0} mm{(tooLong > 0 ? $" + {tooLong} long strips" : "")}; {kit.Deployment.Count} deployment states");

        DA.SetDataList(0, flatA); DA.SetDataList(1, flatB);
        DA.SetDataList(2, joints); DA.SetDataList(3, kit.Joints.Select(j => j.Id));
        DA.SetDataList(4, kit.LathLabels); DA.SetDataList(5, labelPts);
        DA.SetDataList(6, stripCurves); DA.SetDataList(7, stripText); DA.SetDataList(8, stripTextPts);
        DA.SetDataTree(9, deployA); DA.SetDataTree(10, deployB);
        DA.SetData(11, KitExport.CutList(kit, eo)); DA.SetData(12, sb.ToString()); DA.SetDataList(13, files);
    }
}
