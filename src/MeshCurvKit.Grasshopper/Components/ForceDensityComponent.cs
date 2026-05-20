using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using MeshCurvKit.Core.FormFinding;
using MeshCurvKit.Core.Geometry;

namespace MeshCurvKit.Grasshopper.Components;

public class ForceDensityComponent : GH_Component
{
    public ForceDensityComponent()
        : base("Force Density Method", "FDM",
            "Solves for equilibrium using the Force Density Method.",
            "MeshCurvKit", "FormFinding") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE7");

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

        Vec3d[]? loads = null;
        if (loadList.Count > 0)
        {
            loads = new Vec3d[data.VertexCount];
            for (int i = 0; i < Math.Min(loadList.Count, data.VertexCount); i++)
                loads[i] = new Vec3d(loadList[i].X, loadList[i].Y, loadList[i].Z);
        }

        var fixedVerts = fixedList.Count == data.VertexCount
            ? fixedList.ToArray()
            : new bool[data.VertexCount];

        var result = ForceDensityMethod.Compute(data, qList.ToArray(), loads ?? new Vec3d[data.VertexCount], fixedVerts);

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
