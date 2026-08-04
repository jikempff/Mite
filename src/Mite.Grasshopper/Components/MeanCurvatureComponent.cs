using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class MeanCurvatureComponent : GH_Component
{
    public MeanCurvatureComponent()
        : base("Mean Curvature", "MeanCurv",
            "Computes per-vertex mean curvature via cotangent Laplacian.",
            "Mite", "Curvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE3");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.MeanCurvature.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("H", "H", "Mean curvature per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("HN", "HN", "Mean curvature normal per vertex", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;

        var data = MeshConvert.ToMeshData(mesh);
        var result = MeanCurvature.Compute(data);

        DA.SetDataList(0, result.Values);

        var normals = new List<Vector3d>(result.Normals.Length);
        for (int i = 0; i < result.Normals.Length; i++)
            normals.Add(MeshConvert.ToRhinoVector(result.Normals[i]));
        DA.SetDataList(1, normals);
    }
}
