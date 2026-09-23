using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class NetTopologyComponent : MiteComponent
{
    public NetTopologyComponent()
        : base("Net Topology", "NetTopo",
            "Turns a two-family lath net into a structural graph: unique nodes at the crossings " +
            "and one member per lath piece between consecutive crossings (free tails included). " +
            "Feed Nodes / Members to a structural package (e.g. Karamba) or use the node " +
            "schedule for fabrication.",
            "Fabrication", "NetTopology") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDFD");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "First lath family", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Second lath family", GH_ParamAccess.list);
        pManager.AddNumberParameter("Tolerance", "X", "Maximum gap accepted as a crossing (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic: 1/500 of the net size)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("MinLength", "Ml", "Drop members (e.g. free tails) shorter than this (default 0)", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Nodes", "N", "Unique crossing nodes", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Valence", "V", "Members meeting at each node", GH_ParamAccess.list);
        pManager.AddCurveParameter("Members", "M", "Lath pieces between nodes (polylines)", GH_ParamAccess.list);
        pManager.AddLineParameter("Lines", "Ln", "Straight member axes (node to node)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Start", "S", "Node index at each member start (-1 = free end)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("End", "E", "Node index at each member end (-1 = free end)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Lath", "L", "Input lath of each member: family A indices, then family B continuing", GH_ParamAccess.list);
        pManager.AddNumberParameter("Lengths", "Le", "Member lengths", GH_ParamAccess.list);
        pManager.AddIntegerParameter("NodeLaths", "Nl", "Per node: the two lath indices crossing there", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        double tolerance = 0.0, sampling = 0.0, minLength = 0.0;
        if (!DA.GetDataList(0, curvesA)) return;
        if (!DA.GetDataList(1, curvesB)) return;
        DA.GetData(2, ref tolerance);
        DA.GetData(3, ref sampling);
        DA.GetData(4, ref minLength);

        if (sampling <= 0)
        {
            var bbox = BoundingBox.Empty;
            foreach (var c in curvesA) if (c != null) bbox.Union(c.GetBoundingBox(false));
            foreach (var c in curvesB) if (c != null) bbox.Union(c.GetBoundingBox(false));
            sampling = bbox.IsValid ? Math.Max(bbox.Diagonal.Length / 500.0, 1e-9) : 0.01;
        }

        var familyA = NetJointsComponent.SampleAligned(curvesA, sampling);
        var familyB = NetJointsComponent.SampleAligned(curvesB, sampling);
        var crossings = NetIntersections.Find(familyA, familyB, tolerance);
        if (crossings.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No crossings found between the families.");

        var topo = NetTopology.Build(familyA, familyB, crossings, Math.Max(0.0, minLength));

        var nodes = new List<Point3d>(topo.Nodes.Length);
        foreach (var n in topo.Nodes) nodes.Add(MeshConvert.ToRhinoPoint(n));

        var members = new List<Curve>();
        var lines = new List<Line>();
        var starts = new List<int>();
        var ends = new List<int>();
        var laths = new List<int>();
        var lengths = new List<double>();
        foreach (var m in topo.Members)
        {
            members.Add(MeshConvert.ToPolylineCurve(m.Points));
            lines.Add(new Line(MeshConvert.ToRhinoPoint(m.Points[0]), MeshConvert.ToRhinoPoint(m.Points[m.Points.Length - 1])));
            starts.Add(m.NodeStart);
            ends.Add(m.NodeEnd);
            laths.Add(m.Lath);
            lengths.Add(m.Length);
        }

        var nodeLaths = new DataTree<int>();
        for (int i = 0; i < topo.Crossings.Length; i++)
        {
            var path = BranchPath(DA, i);
            nodeLaths.Add(topo.Crossings[i].CurveA, path);
            nodeLaths.Add(curvesA.Count + topo.Crossings[i].CurveB, path);
        }

        DA.SetDataList(0, nodes);
        DA.SetDataList(1, topo.Valence);
        DA.SetDataList(2, members);
        DA.SetDataList(3, lines);
        DA.SetDataList(4, starts);
        DA.SetDataList(5, ends);
        DA.SetDataList(6, laths);
        DA.SetDataList(7, lengths);
        DA.SetDataTree(8, nodeLaths);
    }
}
