using System;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class GaussianCurvatureComponent : GH_Component
{
    public GaussianCurvatureComponent()
        : base("Gaussian Curvature", "GaussCurv",
            "Computes per-vertex Gaussian curvature via angle deficit.",
            "Mite", "Curvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE2");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.GaussianCurvature.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("K", "K", "Gaussian curvature per vertex", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;

        var data = MeshConvert.ToMeshData(mesh);
        var K = GaussianCurvature.Compute(data);

        DA.SetDataList(0, K);
    }
}
