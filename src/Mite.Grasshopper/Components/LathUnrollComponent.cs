using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class LathUnrollComponent : GH_Component
{
    public LathUnrollComponent()
        : base("Lath Unroll", "Unroll",
            "Unrolls lath strips to flat 2D cutting patterns. Each pattern is isometric to the " +
            "swept strip (exact per triangle), so it is ready for CNC/laser cutting. Patterns " +
            "are laid out in a row with the given gap.",
            "Mite", "Fabrication") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF4");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.LathUnroll.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the curves lie on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines on the surface", GH_ParamAccess.list);
        pManager.AddNumberParameter("Width", "W", "Strip width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddBooleanParameter("Upright", "U", "Lath orientation, as in Lath Sweep", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Gap", "G", "Gap between laid-out patterns (default 0.05)", GH_ParamAccess.item, 0.05);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (default 0.01)", GH_ParamAccess.item, 0.01);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Patterns", "P", "Closed flat cutting patterns, laid out in a row", GH_ParamAccess.list);
        pManager.AddCurveParameter("Centerlines", "C", "Flat centerlines (reference/labeling)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Lengths", "L", "3D cutting length per lath", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var curves = new List<Curve>();
        double width = 0.1, gap = 0.05, sampling = 0.01;
        bool upright = false;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, curves)) return;
        DA.GetData(2, ref width);
        DA.GetData(3, ref upright);
        DA.GetData(4, ref gap);
        DA.GetData(5, ref sampling);

        if (width <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var proj = new MeshProjection(data);
        var profile = new LathProfile(width, 0.01, upright);

        var patterns = new List<Curve>();
        var centerlines = new List<Curve>();
        var lengths = new List<double>();

        double cursor = 0;
        int failed = 0;
        foreach (var curve in curves)
        {
            var line = CurveSample.ToPolyline(curve, sampling);
            var r = line != null ? StripUnroll.Unroll(proj, line, profile) : null;
            if (r == null) { failed++; continue; }
            var res = r.Value;

            // Lay out patterns side by side along +x
            var move = new Vec3d(cursor, 0, 0);
            cursor += PatternWidth(res) + gap;

            patterns.Add(ClosedPattern(res, move));
            centerlines.Add(ToPolyline(res.Centerline, move, false));
            lengths.Add(res.Length);
        }

        if (failed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{failed} curve(s) could not be unrolled (degenerate or too short).");

        DA.SetDataList(0, patterns);
        DA.SetDataList(1, centerlines);
        DA.SetDataList(2, lengths);
    }

    private static double PatternWidth(StripUnroll.Result r)
    {
        double maxX = double.MinValue;
        foreach (var p in r.EdgeA) maxX = Math.Max(maxX, p.X);
        foreach (var p in r.EdgeB) maxX = Math.Max(maxX, p.X);
        return maxX;
    }

    private static Curve ClosedPattern(StripUnroll.Result r, Vec3d move)
    {
        var pts = new List<Point3d>();
        foreach (var p in r.EdgeA) pts.Add(MeshConvert.ToRhinoPoint(p + move));
        for (int i = r.EdgeB.Length - 1; i >= 0; i--)
            pts.Add(MeshConvert.ToRhinoPoint(r.EdgeB[i] + move));
        pts.Add(pts[0]); // close
        return new PolylineCurve(pts);
    }

    private static Curve ToPolyline(Vec3d[] pts, Vec3d move, bool closed)
    {
        var list = new List<Point3d>();
        foreach (var p in pts) list.Add(MeshConvert.ToRhinoPoint(p + move));
        if (closed && list.Count > 0) list.Add(list[0]);
        return new PolylineCurve(list);
    }
}
