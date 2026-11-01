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
            "swept laths (Mesh Difference) to fabricate the joints. Family B may have its own " +
            "profile (WidthB, ThicknessB, UprightB, OffsetB): an upright family crossed by a flat one " +
            "— the asymptotic–geodesic hybrid — gets threaded joints, a slot through the upright " +
            "lamella for the flat slat to pass, the slat itself uncut.",
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
        pManager.AddIntegerParameter("Align", "Al", "Where the lath sits along the surface normal: 0 grows from the surface out (default), 1 centred on the surface (its centre line on the curve), 2 grows from the surface in. Offset is the gap to the near face, or the shift of the centre when centred", GH_ParamAccess.item, 0);
        RegisterNetInput(pManager, "Its profiles (per family) shape the notches unless W / T / U / O are wired"); // 12
        pManager[0].Optional = true;
        pManager[1].Optional = true;
        // Family B's own profile (appended 2026-10-27, after Net): unset = as family A
        pManager.AddNumberParameter("WidthB", "Wb", "Profile width of family B (unset: as Width)", GH_ParamAccess.item);
        pManager.AddNumberParameter("ThicknessB", "Tb", "Profile thickness of family B (unset: as Thickness)", GH_ParamAccess.item);
        pManager.AddBooleanParameter("UprightB", "Ub", "Orientation of family B when it differs from A (unset: as Upright). Upright A with flat B is the asymptotic–geodesic hybrid: the flat slat threads through a slot in the upright lamella (Lap does not apply there)", GH_ParamAccess.item);
        pManager.AddNumberParameter("OffsetB", "Ob", "Gap between the surface and the nearest face of family B (unset: as Offset); with Align 1 the shift of B's centre. Lift a flat B to the level where it should pass through the upright A", GH_ParamAccess.item);
        pManager[2].Optional = true;
        pManager[13].Optional = true;
        pManager[14].Optional = true;
        pManager[15].Optional = true;
        pManager[16].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Crossing points", GH_ParamAccess.list);
        pManager.AddPlaneParameter("Planes", "F", "Joint frame per crossing (X along family A, Z along the surface normal)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Angles", "An", "Crossing angle between the laths, in degrees", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesA", "Na", "Notch solids to subtract from family A laths (null at a threaded joint where A is the flat slat that passes through B)", GH_ParamAccess.list);
        pManager.AddBoxParameter("NotchesB", "Nb", "Notch solids to subtract from family B laths (null at a threaded joint where B is the flat slat that passes through A)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("IndexA", "Ia", "Index of the family A curve at each crossing", GH_ParamAccess.list);
        pManager.AddIntegerParameter("IndexB", "Ib", "Index of the family B curve at each crossing (a family A index when CurvesB is empty)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var netIn = GetNet(DA, 12);
        var input = LoadMeshOrNet(DA, 0, netIn);
        if (input == null) return;
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        if (!ReadCurvesOrNet(DA, 1, netIn, curvesA, 0)) return;
        ReadCurvesOrNet(DA, 2, netIn, curvesB, 1, required: false);
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
        int align = 0;
        DA.GetData(11, ref align);
        double widthB = width, thicknessB = thickness, offsetB = offset;
        bool uprightB = upright;
        bool ownB = false;
        if (DA.GetData(13, ref widthB)) ownB = true; else widthB = width;
        if (DA.GetData(14, ref thicknessB)) ownB = true; else thicknessB = thickness;
        if (DA.GetData(15, ref uprightB)) ownB = true; else uprightB = upright;
        if (DA.GetData(16, ref offsetB)) ownB = true; else offsetB = offset;

        if (width <= 0 || thickness <= 0 || widthB <= 0 || thicknessB <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Thickness (and WidthB / ThicknessB) must be positive.");
            return;
        }
        CheckSection(width, thickness, System.Linq.Enumerable.Concat(curvesA, curvesB));

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        var profile = new LathProfile(width, thickness, upright, offset).WithAlign(AlignOf(align));
        // a net's own profiles (e.g. A upright, B flat) unless the section inputs are wired;
        // WidthB / ThicknessB / UprightB / OffsetB give family B its own profile either way
        var profileA = NetProfile(netIn, 0, 3, 4, 5, 6) ?? profile;
        var profileB = ownB ? new LathProfile(widthB, thicknessB, uprightB, offsetB).WithAlign(AlignOf(align))
            : netIn != null ? NetProfile(netIn, netIn.CountA, 3, 4, 5, 6) ?? profile : profile;
        profile = profileA;

        // Keep curve indices aligned with the inputs: null / degenerate curves become empty polylines
        var familyA = SampleAligned(curvesA, chord);
        var familyB = SampleAligned(curvesB, chord);
        bool selfMode = curvesB.Count == 0;
        if (selfMode) profileB = profile; // crossings within one family: one profile
        bool mixed = profile.Upright != profileB.Upright;

        var crossings = NetIntersections.Find(familyA, selfMode ? null : familyB, tolerance);

        var points = new List<Point3d>();
        var planes = new List<Plane>();
        var angles = new List<double>();
        var notchesA = new List<Box?>();
        var notchesB = new List<Box?>();
        var indexA = new List<int>();
        var indexB = new List<int>();
        int parallel = 0, threadedA = 0, threadedB = 0, apart = 0, lapCount = 0;
        double apartGap = 0;

        int crossingsDone = 0;
        foreach (var crossing in crossings)
        {
            if (Cancelled()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial."); break; }
            ReportProgress("joints", crossingsDone++, crossings.Count);

            Vec3d ta = NetIntersections.TangentAt(familyA, crossing, false);
            Vec3d tb = selfMode
                ? NetIntersections.TangentAt(familyA, crossing, true)
                : NetIntersections.TangentAt(familyB, crossing, true);

            var hit = proj.ClosestPoint(crossing.Point, proj.NearestVertexGlobal(crossing.Point));
            Vec3d n = hit.SmoothNormal;

            if (!JointGeometry.TryBuildNotches(crossing.Point, ta, tb, n,
                    profile, selfMode ? profile : profileB, lap, clearance, out NotchSolid notchA, out NotchSolid notchB, out JointKind kind))
            {
                parallel++;
                continue;
            }
            switch (kind)
            {
                case JointKind.SlotInA: threadedA++; break;
                case JointKind.SlotInB: threadedB++; break;
                case JointKind.None:
                    apart++;
                    apartGap = Math.Max(apartGap, Math.Max(profile.FaceLow, profileB.FaceLow) - Math.Min(profile.FaceLow + profile.NormalDepth, profileB.FaceLow + profileB.NormalDepth));
                    break;
                default: lapCount++; break;
            }

            Vec3d g = Vec3d.Cross(n, ta);
            var plane = new Plane(MeshConvert.ToRhinoPoint(crossing.Point),
                MeshConvert.ToRhinoVector(ta), MeshConvert.ToRhinoVector(g));

            points.Add(MeshConvert.ToRhinoPoint(crossing.Point));
            planes.Add(plane);
            angles.Add(Rhino.RhinoMath.ToDegrees(
                Vector3d.VectorAngle(MeshConvert.ToRhinoVector(ta), MeshConvert.ToRhinoVector(tb))));
            notchesA.Add(notchA.IsEmpty ? null : ToRhinoBox(notchA));
            notchesB.Add(notchB.IsEmpty ? null : ToRhinoBox(notchB));
            indexA.Add(crossing.CurveA);
            indexB.Add(crossing.CurveB);
        }

        if (crossings.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No crossings found. Check that the families actually cross, or raise Tolerance.");
        if (parallel > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{parallel} crossing(s) skipped: the laths meet nearly tangentially, no lap joint is possible there.");
        if (mixed && (threadedA + threadedB) > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Hybrid net: {threadedA + threadedB} threaded joint(s) — the flat family passes through a slot in the upright one " +
                $"(slot in {(threadedA > 0 ? "A" : "B")}, the flat lath is not cut, Lap does not apply; the {(threadedA > 0 ? "NotchesB" : "NotchesA")} entries are null there){(lapCount > 0 ? $"; {lapCount} lap joint(s)" : "")}.");
        if (apart > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{apart} crossing(s) where the two families do not touch (they pass each other by up to {apartGap:G3} along the normal): " +
                "nothing is cut there — lower OffsetB (or Offset) so the flat slat runs through the upright lamella, or connect them with a block and bolt.");
        if (ownB && !mixed && (Math.Abs(widthB - width) > 1e-12 || Math.Abs(thicknessB - thickness) > 1e-12 || Math.Abs(offsetB - offset) > 1e-12))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Family B notched with its own profile {widthB:G4} × {thicknessB:G4}{(Math.Abs(offsetB - offset) > 1e-12 ? $", offset {offsetB:G4}" : "")}; the lap plane follows each lath's own depth.");

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
