using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class ForceDensityComponent : MiteComponent
{
    public ForceDensityComponent()
        : base("Force Density Method", "FDM",
            "Solves the equilibrium shape of a cable / strut net with the Force Density Method " +
            "(linear, exact). Positive force densities give tension nets, negative ones compression shells. " +
            "The Edges output lists the edge order used for per-edge force densities.",
            "Form Finding", "ForceDensity") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE7");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh (edges define the net)", GH_ParamAccess.item);
        pManager.AddNumberParameter("ForceDensity", "Q", "Force density per edge (one value broadcasts; per-edge lists follow the Edges output order)", GH_ParamAccess.list, 1.0);
        pManager.AddVectorParameter("Loads", "L", "Load vector per input mesh vertex (one vector broadcasts; coincident vertices share one load)", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags, one per input mesh vertex (empty = fix the boundary)", GH_ParamAccess.list);
        pManager[2].Optional = true;
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Equilibrium mesh", GH_ParamAccess.item);
        pManager.AddPointParameter("Points", "P", "Equilibrium vertex positions (one per input mesh vertex)", GH_ParamAccess.list);
        pManager.AddLineParameter("Edges", "E", "Net edges in the order per-edge force densities are applied", GH_ParamAccess.list);
        pManager.AddNumberParameter("Forces", "N", "Axial force per edge (q * length; tension positive)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        var input = LoadMesh(DA, 0, keepQuads: true);
        if (input == null) return;

        var qList = new List<double>();
        var loadList = new List<Vector3d>();
        var fixedList = new List<bool>();
        DA.GetDataList(1, qList);
        DA.GetDataList(2, loadList);
        DA.GetDataList(3, fixedList);

        var data = input.Data;
        var edges = data.BuildEdges();
        int edgeCount = edges.Length;

        // Force densities: single value broadcasts to all edges
        var q = new double[edgeCount];
        if (qList.Count == 0) { for (int i = 0; i < edgeCount; i++) q[i] = 1.0; }
        else if (qList.Count == 1) { for (int i = 0; i < edgeCount; i++) q[i] = qList[0]; }
        else if (qList.Count == edgeCount) { qList.CopyTo(q); }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"ForceDensity count ({qList.Count}) does not match the edge count ({edgeCount}); " +
                (qList.Count < edgeCount ? "missing entries use the last value." : "extra entries are ignored.") +
                " Wire the Edges output to see the expected order.");
            for (int i = 0; i < edgeCount; i++) q[i] = qList[Math.Min(i, qList.Count - 1)];
        }

        // Loads: single vector broadcasts to all vertices; per-vertex lists are summed onto welded vertices
        Vec3d[] loads;
        if (loadList.Count == 1)
        {
            loads = new Vec3d[data.VertexCount];
            var l = MeshConvert.ToVec3d(loadList[0]);
            for (int i = 0; i < loads.Length; i++) loads[i] = l;
        }
        else
        {
            if (loadList.Count > 0 && loadList.Count != input.RhinoVertexCount)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Loads count ({loadList.Count}) does not match the vertex count ({input.RhinoVertexCount}); missing entries are zero.");
            loads = input.CollapseAverage(loadList);
        }

        bool[] fixedVerts;
        if (fixedList.Count == 0)
        {
            fixedVerts = data.BuildBoundaryVertexFlags();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No Fixed flags given: the mesh boundary is fixed.");
        }
        else if (fixedList.Count == input.RhinoVertexCount)
        {
            fixedVerts = input.Collapse(fixedList);
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Fixed needs one flag per vertex: got {fixedList.Count}, mesh has {input.RhinoVertexCount}. " +
                "Tip: build it from vertex indices with 'Member Index' or supply a full boolean list.");
            return;
        }

        bool anyFixed = false;
        foreach (bool b in fixedVerts) if (b) { anyFixed = true; break; }
        if (!anyFixed)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one vertex must be fixed, or the net has no anchors and no equilibrium exists.");
            return;
        }

        ForceDensityMethod.Result result;
        try
        {
            result = ForceDensityMethod.Compute(data, q, loads, fixedVerts);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var expanded = input.Expand(result.Vertices);
        var points = new List<Point3d>(expanded.Length);
        foreach (var v in expanded) points.Add(MeshConvert.ToRhinoPoint(v));

        var lines = new List<Line>(edgeCount);
        var forces = new List<double>(edgeCount);
        for (int e = 0; e < edgeCount; e++)
        {
            var a = result.Vertices[edges[e].v0];
            var b = result.Vertices[edges[e].v1];
            lines.Add(new Line(MeshConvert.ToRhinoPoint(a), MeshConvert.ToRhinoPoint(b)));
            forces.Add(q[e] * (b - a).Length);
        }

        DA.SetData(0, input.WithVertices(mesh, result.Vertices));
        DA.SetDataList(1, points);
        DA.SetDataList(2, lines);
        DA.SetDataList(3, forces);
    }
}
