using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class NetJointsComponent : MiteComponent
{
    public NetJointsComponent()
        : base("Net Joints", "Joints",
            "Finds the crossings between the two lath families of a gridshell net and builds " +
            "lap-joint notch solids for each crossing. Boolean-subtract the notches from the " +
            "swept laths (Mesh Difference) to fabricate the joints.",
            "Fabrication", "NetJoints") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDED");

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
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Crossing points", GH_ParamAccess.list);
        pManager.AddPlaneParameter("Planes", "F", "Joint frame per crossing (X along family A, Z along the surface normal)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Angles", "An", "Crossing angle between the laths, in degrees", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesA", "Na", "Notch solids to subtract from family A laths", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesB", "Nb", "Notch solids to subtract from family B laths", GH_ParamAccess.list);
        pManager.AddIntegerParameter("IndexA", "Ia", "Index of the family A curve at each crossing", GH_ParamAccess.list);
        pManager.AddIntegerParameter("IndexB", "Ib", "Index of the family B curve at each crossing (a family A index when CurvesB is empty)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        if (!DA.GetDataList(1, curvesA)) return;
        DA.GetDataList(2, curvesB);
        double width = 0.1, thickness = 0.01, offset = 0.0, lap = 0.5, clearance = 0.0, tolerance = 0.0, sampling = 0.0;
        bool upright = false;
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

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        var profile = new LathProfile(width, thickness, upright, offset);

        // Keep curve indices aligned with the inputs: null / degenerate curves become empty polylines
        var familyA = SampleAligned(curvesA, chord);
        var familyB = SampleAligned(curvesB, chord);
        bool selfMode = curvesB.Count == 0;

        var crossings = NetIntersections.Find(familyA, selfMode ? null : familyB, tolerance);

        var points = new List<Point3d>();
        var planes = new List<Plane>();
        var angles = new List<double>();
        var notchesA = new List<Box>();
        var notchesB = new List<Box>();
        var indexA = new List<int>();
        var indexB = new List<int>();
        int parallel = 0;

        foreach (var crossing in crossings)
        {
            if (Cancelled()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial."); break; }

            Vec3d ta = NetIntersections.TangentAt(familyA, crossing, false);
            Vec3d tb = selfMode
                ? NetIntersections.TangentAt(familyA, crossing, true)
                : NetIntersections.TangentAt(familyB, crossing, true);

            var hit = proj.ClosestPoint(crossing.Point, proj.NearestVertexGlobal(crossing.Point));
            Vec3d n = hit.SmoothNormal;

            if (!JointGeometry.TryBuildLapNotches(crossing.Point, ta, tb, n,
                    profile, profile, lap, clearance, out NotchSolid notchA, out NotchSolid notchB))
            {
                parallel++;
                continue;
            }

            Vec3d g = Vec3d.Cross(n, ta);
            var plane = new Plane(MeshConvert.ToRhinoPoint(crossing.Point),
                MeshConvert.ToRhinoVector(ta), MeshConvert.ToRhinoVector(g));

            points.Add(MeshConvert.ToRhinoPoint(crossing.Point));
            planes.Add(plane);
            angles.Add(Rhino.RhinoMath.ToDegrees(
                Vector3d.VectorAngle(MeshConvert.ToRhinoVector(ta), MeshConvert.ToRhinoVector(tb))));
            notchesA.Add(ToRhinoBox(notchA));
            notchesB.Add(ToRhinoBox(notchB));
            indexA.Add(crossing.CurveA);
            indexB.Add(crossing.CurveB);
        }

        if (crossings.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No crossings found. Check that the families actually cross, or raise Tolerance.");
        if (parallel > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{parallel} crossing(s) skipped: the laths meet nearly tangentially, no lap joint is possible there.");

        DA.SetDataList(0, points);
        DA.SetDataList(1, planes);
        DA.SetDataList(2, angles);
        DA.SetDataList(3, notchesA);
        DA.SetDataList(4, notchesB);
        DA.SetDataList(5, indexA);
        DA.SetDataList(6, indexB);
    }

    internal static List<Vec3d[]> SampleAligned(List<Curve> curves, double chord)
    {
        var lines = new List<Vec3d[]>(curves.Count);
        foreach (var c in curves)
        {
            var line = c == null ? null : CurveSample.ToPolyline(c, chord);
            lines.Add(line ?? Array.Empty<Vec3d>());
        }
        return lines;
    }

    internal static Box ToRhinoBox(NotchSolid notch)
    {
        var plane = new Plane(MeshConvert.ToRhinoPoint(notch.Center),
            MeshConvert.ToRhinoVector(notch.AxisX), MeshConvert.ToRhinoVector(notch.AxisY));
        return new Box(plane,
            new Interval(-notch.HalfX, notch.HalfX),
            new Interval(-notch.HalfY, notch.HalfY),
            new Interval(-notch.HalfZ, notch.HalfZ));
    }
}
