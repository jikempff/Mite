using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class MeanCurvatureComponent : MiteComponent
{
    public MeanCurvatureComponent()
        : base("Mean Curvature", "MeanCurv",
            "Computes per-vertex mean curvature via the cotangent Laplacian (mixed Voronoi areas, " +
            "consistent with Gaussian Curvature). Boundary vertices are zero. One value per input mesh vertex.",
            "Curvature", "MeanCurvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE3");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("H", "H", "Signed mean curvature per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("HN", "HN", "Mean curvature normal vector per vertex (length |H|)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;

        var result = MeanCurvature.Compute(input.Data);
        DA.SetDataList(0, input.Expand(result.Values));

        var hn = input.Expand(result.CurvatureNormals);
        var normals = new List<Vector3d>(hn.Length);
        foreach (var v in hn) normals.Add(MeshConvert.ToRhinoVector(v));
        DA.SetDataList(1, normals);
    }
}
