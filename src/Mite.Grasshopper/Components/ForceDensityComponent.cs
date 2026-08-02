using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class ForceDensityComponent : GH_Component
{
    public ForceDensityComponent()
        : base("Force Density Method", "FDM",
            "Solves for equilibrium using the Force Density Method.",
            "Mite", "FormFinding") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE7");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.ForceDensity.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh (edges define the cable net)", GH_ParamAccess.item);
        pManager.AddNumberParameter("ForceDensity", "Q", "Force density per edge", GH_ParamAccess.list);
        pManager.AddVectorParameter("Loads", "L", "Load vector per vertex", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags", GH_ParamAccess.list);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Equilibrium mesh", GH_ParamAccess.item);
        pManager.AddPointParameter("Points", "P", "Equilibrium vertex positions", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var qList = new List<double>();
        var loadList = new List<Vector3d>();
        var fixedList = new List<bool>();

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, qList)) return;
        DA.GetDataList(2, loadList);
        if (!DA.GetDataList(3, fixedList)) return;

        var data = MeshConvert.ToMeshDataKeepQuads(mesh);
        int edgeCount = data.BuildEdges().Length;

        // Force densities: single value broadcasts to all edges
        var q = new double[edgeCount];
        if (qList.Count == 1)
        {
            for (int i = 0; i < edgeCount; i++) q[i] = qList[0];
        }
        else if (qList.Count == edgeCount)
        {
            qList.CopyTo(q);
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"ForceDensity count ({qList.Count}) does not match edge count ({edgeCount}); missing entries use the last value.");
            for (int i = 0; i < edgeCount; i++)
                q[i] = qList[Math.Min(i, qList.Count - 1)];
        }

        // Loads: single vector broadcasts to all vertices
        var loads = new Vec3d[data.VertexCount];
        if (loadList.Count == 1)
        {
            var l = new Vec3d(loadList[0].X, loadList[0].Y, loadList[0].Z);
            for (int i = 0; i < data.VertexCount; i++) loads[i] = l;
        }
        else if (loadList.Count > 0)
        {
            if (loadList.Count != data.VertexCount)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Loads count ({loadList.Count}) does not match vertex count ({data.VertexCount}); missing entries are zero.");
            for (int i = 0; i < Math.Min(loadList.Count, data.VertexCount); i++)
                loads[i] = new Vec3d(loadList[i].X, loadList[i].Y, loadList[i].Z);
        }

        // Fixed flags: must match the vertex count, and something must be fixed,
        // otherwise the equilibrium system is singular and the solve returns NaNs
        bool[] fixedVerts;
        if (fixedList.Count == data.VertexCount)
        {
            fixedVerts = fixedList.ToArray();
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Fixed needs one flag per vertex: got {fixedList.Count}, mesh has {data.VertexCount}. " +
                "Tip: use vertex indices with a 'Member Index' pattern or supply a full boolean list.");
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

        var result = ForceDensityMethod.Compute(data, q, loads, fixedVerts);

        var outMesh = mesh.DuplicateMesh();
        var points = new List<Point3d>();
        for (int i = 0; i < result.Vertices.Length; i++)
        {
            var pt = MeshConvert.ToRhinoPoint(result.Vertices[i]);
            outMesh.Vertices.SetVertex(i, pt);
            points.Add(pt);
        }
        outMesh.Normals.ComputeNormals();

        DA.SetData(0, outMesh);
        DA.SetDataList(1, points);
    }
}
