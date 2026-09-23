using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;

namespace Mite.Grasshopper.Components;

public class UmbilicsComponent : MiteComponent
{
    public UmbilicsComponent()
        : base("Umbilics", "Umb",
            "Finds umbilical points, where the two principal curvatures are equal and the " +
            "principal directions are undefined. Curve networks rotate wildly around umbilics — " +
            "use this to place net seeds deliberately or to mask regions before tracing.",
            "Curvature", "Umbilics") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF2");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Tolerance", "T", "Relative |k1-k2| tolerance (default 0.05)", GH_ParamAccess.item, 0.05);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Umbilical vertex positions", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Indices", "I", "Umbilical vertex indices (of the input mesh)", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Mask", "Mk", "Per input vertex: true where umbilical", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        double tolerance = 0.05;
        DA.GetData(1, ref tolerance);

        var curvature = PrincipalCurvature.Compute(input.Data);
        var topoIndices = Umbilics.Find(curvature, tolerance);

        var flags = new bool[input.Data.VertexCount];
        foreach (int t in topoIndices) flags[t] = true;

        var points = new List<Point3d>();
        var indices = new List<int>();
        var mask = new bool[input.RhinoVertexCount];
        for (int i = 0; i < input.RhinoVertexCount; i++)
        {
            if (!flags[input.TopoOfVertex[i]]) continue;
            mask[i] = true;
            indices.Add(i);
            points.Add(MeshConvert.ToRhinoPoint(input.Data.Vertices[input.TopoOfVertex[i]]));
        }

        DA.SetDataList(0, points);
        DA.SetDataList(1, indices);
        DA.SetDataList(2, mask);
    }
}
