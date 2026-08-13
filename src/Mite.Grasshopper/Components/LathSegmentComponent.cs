using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class LathSegmentComponent : GH_Component
{
    public LathSegmentComponent()
        : base("Lath Segment", "Segment",
            "Splits laths into segments that fit stock material length, keeping cuts away from " +
            "net crossings, and builds half-lap splice notch solids for rejoining the pieces. " +
            "Subtract Ne/Ns from the swept lath ends with Solid Difference.",
            "Mite", "Fabrication") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF5");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.LathSegment.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines", GH_ParamAccess.list);
        pManager.AddNumberParameter("StockLength", "St", "Available material length", GH_ParamAccess.item, 3.0);
        pManager.AddNumberParameter("Margin", "Ma", "Minimum distance between a cut and a joint (default 0.05)", GH_ParamAccess.item, 0.05);
        pManager.AddPointParameter("Joints", "J", "Crossing points to avoid (from Net Joints)", GH_ParamAccess.list);
        pManager.AddNumberParameter("SpliceLength", "SL", "Half-lap splice overlap length (default 0.15)", GH_ParamAccess.item, 0.15);
        pManager.AddNumberParameter("Width", "W", "Lath width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Lath thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "Lath orientation, as in Lath Sweep", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Clearance", "Cl", "Fit clearance (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "Sa", "Chord deviation for curve sampling (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Segments", "S", "Lath segments", GH_ParamAccess.list);
        pManager.AddPointParameter("Cuts", "Cu", "Cut points", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesEnd", "Ne", "Splice notches for upstream segment ends", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesStart", "Ns", "Splice notches for downstream segment starts", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var curves = new List<Curve>();
        var joints = new List<Point3d>();
        double stock = 3.0, margin = 0.05, spliceLength = 0.15, width = 0.1, thickness = 0.01, clearance = 0.0, sampling = 0.01;
        bool upright = false;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, curves)) return;
        DA.GetData(2, ref stock);
        DA.GetData(3, ref margin);
        DA.GetDataList(4, joints);
        DA.GetData(5, ref spliceLength);
        DA.GetData(6, ref width);
        DA.GetData(7, ref thickness);
        DA.GetData(8, ref upright);
        DA.GetData(9, ref clearance);
        DA.GetData(10, ref sampling);

        if (stock <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "StockLength must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var proj = new MeshProjection(data);
        var profile = new LathProfile(width, thickness, upright);

        var jointVecs = new List<Vec3d>();
        foreach (var p in joints) jointVecs.Add(new Vec3d(p.X, p.Y, p.Z));

        var segments = new List<Curve>();
        var cuts = new List<Point3d>();
        var notchesEnd = new List<Box>();
        var notchesStart = new List<Box>();

        foreach (var curve in curves)
        {
            var line = CurveSample.ToPolyline(curve, sampling);
            if (line == null) continue;

            double[] jointArcs = jointVecs.Count > 0
                ? LathSegmentation.JointArcLengths(line, jointVecs)
                : null!;

            var r = LathSegmentation.Segment(line, stock, margin, jointArcs);

            foreach (var seg in r.Segments)
            {
                var pts = new List<Point3d>();
                foreach (var p in seg) pts.Add(MeshConvert.ToRhinoPoint(p));
                if (pts.Count > 1) segments.Add(new PolylineCurve(pts));
            }

            for (int k = 0; k < r.CutPoints.Length; k++)
            {
                Vec3d cut = r.CutPoints[k];
                // Tangent at the cut from the polyline
                Vec3d tangent = TangentAtArc(line, r.CutArcLengths[k]);
                var hit = proj.ClosestPoint(cut, proj.NearestVertexGlobal(cut));

                cuts.Add(MeshConvert.ToRhinoPoint(cut));

                if (JointGeometry.TryBuildSpliceNotches(cut, tangent, hit.SmoothNormal,
                        profile, spliceLength, clearance, out NotchSolid ne, out NotchSolid ns))
                {
                    notchesEnd.Add(ToRhinoBox(ne));
                    notchesStart.Add(ToRhinoBox(ns));
                }
            }
        }

        DA.SetDataList(0, segments);
        DA.SetDataList(1, cuts);
        DA.SetDataList(2, notchesEnd);
        DA.SetDataList(3, notchesStart);
    }

    private static Vec3d TangentAtArc(Vec3d[] line, double s)
    {
        double arc = 0;
        for (int i = 1; i < line.Length; i++)
        {
            double len = (line[i] - line[i - 1]).Length;
            if (arc + len >= s && len > 1e-15)
                return (line[i] - line[i - 1]) / len;
            arc += len;
        }
        return (line[line.Length - 1] - line[line.Length - 2]).Normalized();
    }

    private static Box ToRhinoBox(NotchSolid notch)
    {
        var plane = new Plane(MeshConvert.ToRhinoPoint(notch.Center),
            MeshConvert.ToRhinoVector(notch.AxisX), MeshConvert.ToRhinoVector(notch.AxisY));
        return new Box(plane,
            new Interval(-notch.HalfX, notch.HalfX),
            new Interval(-notch.HalfY, notch.HalfY),
            new Interval(-notch.HalfZ, notch.HalfZ));
    }
}
