using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class PrincipalCurvatureComponent : MiteComponent
{
    public PrincipalCurvatureComponent()
        : base("Principal Curvature", "PrinCurv",
            "Computes principal curvatures and directions per vertex (Rusinkiewicz per-face " +
            "second-fundamental-form fit, tensor-smoothed over R vertex rings). " +
            "Outputs are one item per input mesh vertex, ready for Mesh Colours.",
            "Curvature", "PrincipalCurvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE1");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Radius", "R", "Smoothing radius in vertex rings (1 = none, default 2)", GH_ParamAccess.item, 2);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("K1", "K1", "Maximum principal curvature per vertex", GH_ParamAccess.list);
        pManager.AddNumberParameter("K2", "K2", "Minimum principal curvature per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("D1", "D1", "Maximum curvature direction per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("D2", "D2", "Minimum curvature direction per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("Normals", "N", "Vertex normals used for the tangent frames ((D1, D2, N) is right-handed)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        int radius = 2;
        DA.GetData(1, ref radius);
        radius = Math.Max(1, radius);

        var result = PrincipalCurvature.Compute(input.Data, radius);

        DA.SetDataList(0, input.Expand(result.K1));
        DA.SetDataList(1, input.Expand(result.K2));
        DA.SetDataList(2, Vectors(input.Expand(result.D1)));
        DA.SetDataList(3, Vectors(input.Expand(result.D2)));
        DA.SetDataList(4, Vectors(input.Expand(result.Normals)));
    }

    private static List<Vector3d> Vectors(Core.Geometry.Vec3d[] v)
    {
        var list = new List<Vector3d>(v.Length);
        foreach (var x in v) list.Add(MeshConvert.ToRhinoVector(x));
        return list;
    }
}
