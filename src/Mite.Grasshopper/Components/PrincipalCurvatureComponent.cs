using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class PrincipalCurvatureComponent : GH_Component
{
    public PrincipalCurvatureComponent()
        : base("Principal Curvature", "PrinCurv",
            "Computes principal curvatures and directions per vertex.",
            "Mite", "Curvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE1");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.PrincipalCurvature.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Radius", "R", "Smoothing radius (default 2)", GH_ParamAccess.item, 2);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("K1", "K1", "Maximum principal curvature per vertex", GH_ParamAccess.list);
        pManager.AddNumberParameter("K2", "K2", "Minimum principal curvature per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("D1", "D1", "Maximum curvature direction per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("D2", "D2", "Minimum curvature direction per vertex", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        int radius = 2;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref radius);

        var data = MeshConvert.ToMeshData(mesh);
        var result = PrincipalCurvature.Compute(data, radius);

        DA.SetDataList(0, result.K1);
        DA.SetDataList(1, result.K2);

        var d1 = new List<Vector3d>(result.D1.Length);
        var d2 = new List<Vector3d>(result.D2.Length);
        for (int i = 0; i < result.D1.Length; i++)
        {
            d1.Add(MeshConvert.ToRhinoVector(result.D1[i]));
            d2.Add(MeshConvert.ToRhinoVector(result.D2[i]));
        }
        DA.SetDataList(2, d1);
        DA.SetDataList(3, d2);
    }
}
