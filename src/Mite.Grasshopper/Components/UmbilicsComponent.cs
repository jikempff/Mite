using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class UmbilicsComponent : GH_Component
{
    public UmbilicsComponent()
        : base("Umbilics", "Umb",
            "Finds umbilical points, where the two principal curvatures are equal and the " +
            "principal directions are undefined. Curve networks rotate wildly around umbilics — " +
            "use this to place net seeds deliberately or to mask regions before tracing.",
            "Mite", "Curvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF2");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.Umbilics.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Tolerance", "T", "Relative k1-k2 tolerance (default 0.05)", GH_ParamAccess.item, 0.05);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Umbilical vertex positions", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Indices", "I", "Umbilical vertex indices", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        double tolerance = 0.05;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref tolerance);

        var data = MeshConvert.ToMeshData(mesh);
        var curvature = PrincipalCurvature.Compute(data);
        var indices = Umbilics.Find(curvature, tolerance);

        var points = new List<Point3d>();
        foreach (int i in indices)
            points.Add(MeshConvert.ToRhinoPoint(data.Vertices[i]));

        DA.SetDataList(0, points);
        DA.SetDataList(1, indices);
    }
}
