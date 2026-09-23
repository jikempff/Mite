using System;
using Grasshopper.Kernel;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class GaussianCurvatureComponent : MiteComponent
{
    public GaussianCurvatureComponent()
        : base("Gaussian Curvature", "GaussCurv",
            "Computes per-vertex Gaussian curvature via angle deficit over mixed Voronoi areas. " +
            "Positive on domes, negative on saddles, zero on developable and boundary vertices. " +
            "One value per input mesh vertex.",
            "Curvature", "GaussianCurvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE2");

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
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        DA.SetDataList(0, input.Expand(GaussianCurvature.Compute(input.Data)));
    }
}
