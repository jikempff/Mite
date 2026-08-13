using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class NetJointsComponent : GH_Component
{
    public NetJointsComponent()
        : base("Net Joints", "Joints",
            "Finds the crossings between the two lath families of a gridshell net and builds " +
            "lap-joint notch solids for each crossing. Boolean-subtract the notches from the " +
            "swept laths (e.g. with Solid Difference) to fabricate the joints.",
            "Mite", "Fabrication") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDED");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.NetJoints.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the net lies on", GH_ParamAccess.item);
        pManager.AddCurveParameter("CurvesA", "A", "First lath family", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Second lath family (empty: crossings within family A)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Width", "W", "Lath profile width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Lath profile thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "False: laths lie flat (lap notch). True: laths stand upright (egg-crate slot)", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Offset", "O", "Gap between the surface and the nearest lath face (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Lap", "L", "Notched fraction of the profile depth (default 0.5 = half-lap)", GH_ParamAccess.item, 0.5);
        pManager.AddNumberParameter("Clearance", "Cl", "Fit clearance added to each notch side (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Tolerance", "X", "Maximum gap accepted as a crossing (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Crossing points", GH_ParamAccess.list);
        pManager.AddPlaneParameter("Planes", "F", "Joint frame per crossing (X along family A, Z along the surface normal)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Angles", "An", "Crossing angle between the laths, in degrees", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesA", "Na", "Notch solids to subtract from family A laths", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesB", "Nb", "Notch solids to subtract from family B laths", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        double width = 0.1, thickness = 0.01, offset = 0.0, lap = 0.5, clearance = 0.0, tolerance = 0.0, sampling = 0.01;
        bool upright = false;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, curvesA)) return;
        DA.GetDataList(2, curvesB);
        DA.GetData(3, ref width);
        DA.GetData(4, ref thickness);
        DA.GetData(5, ref upright);
        DA.GetData(6, ref offset);
        DA.GetData(7, ref lap);
        DA.GetData(8, ref clearance);
        DA.GetData(9, ref tolerance);
        DA.GetData(10, ref sampling);

        if (width <= 0 || thickness <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Thickness must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var proj = new MeshProjection(data);
        var profile = new LathProfile(width, thickness, upright, offset);

        var familyA = CurveSample.ToPolylines(curvesA, sampling);
        var familyB = CurveSample.ToPolylines(curvesB, sampling);
        bool selfMode = familyB.Count == 0;

        var crossings = NetIntersections.Find(familyA, selfMode ? null : familyB, tolerance);

        var points = new List<Point3d>();
        var planes = new List<Plane>();
        var angles = new List<double>();
        var notchesA = new List<Box>();
        var notchesB = new List<Box>();

        foreach (var crossing in crossings)
        {
            Vec3d ta = NetIntersections.TangentAt(familyA, crossing, false);
            Vec3d tb = selfMode
                ? NetIntersections.TangentAt(familyA, crossing, true)
                : NetIntersections.TangentAt(familyB, crossing, true);

            var hit = proj.ClosestPoint(crossing.Point, proj.NearestVertexGlobal(crossing.Point));
            Vec3d n = hit.SmoothNormal;

            if (!JointGeometry.TryBuildLapNotches(crossing.Point, ta, tb, n,
                    profile, profile, lap, clearance, out NotchSolid notchA, out NotchSolid notchB))
                continue;

            Vec3d g = Vec3d.Cross(n, ta);
            var plane = new Plane(MeshConvert.ToRhinoPoint(crossing.Point),
                MeshConvert.ToRhinoVector(ta), MeshConvert.ToRhinoVector(g));

            points.Add(MeshConvert.ToRhinoPoint(crossing.Point));
            planes.Add(plane);
            angles.Add(Rhino.RhinoMath.ToDegrees(
                Vector3d.VectorAngle(MeshConvert.ToRhinoVector(ta), MeshConvert.ToRhinoVector(tb))));
            notchesA.Add(ToRhinoBox(notchA));
            notchesB.Add(ToRhinoBox(notchB));
        }

        if (crossings.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No crossings found. Check that the families actually cross, or raise Tolerance.");

        DA.SetDataList(0, points);
        DA.SetDataList(1, planes);
        DA.SetDataList(2, angles);
        DA.SetDataList(3, notchesA);
        DA.SetDataList(4, notchesB);
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
