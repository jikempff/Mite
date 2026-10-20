using System;
using System.Collections.Generic;
using System.Linq;
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
            "use this to place net seeds deliberately or to mask regions before tracing. " +
            "Points / Indices / Mask flag the region of nearly equal curvatures; Umbilics locates " +
            "the isolated umbilics themselves as the points round which the principal directions " +
            "turn, with their index (+½ lemon or monstar, −½ star). The indices sum to the Euler " +
            "characteristic of a closed mesh: a triaxial ellipsoid has exactly four lemons.",
            "Curvature", "Umbilics") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF2");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Tolerance", "T", "Relative |k1-k2| tolerance (default 0.05)", GH_ParamAccess.item, 0.05);
        pManager.AddNumberParameter("Cancel", "Cn",
            "Distance within which a +½ and a −½ umbilic cancel each other (noise in a nearly umbilical region appears as such pairs). " +
            "0 (default) keeps every located umbilic; a few mesh edges is a sensible value on noisy meshes.", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Umbilical vertex positions", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Indices", "I", "Umbilical vertex indices (of the input mesh)", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Mask", "Mk", "Per input vertex: true where umbilical", GH_ParamAccess.list);
        pManager.AddPointParameter("Umbilics", "U", "Isolated umbilics: the points round which the principal directions turn (one per mesh face at most)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Index", "Ix", "Index of each isolated umbilic: +0.5 (lemon / monstar, the generic umbilics of a convex surface), −0.5 (star, e.g. the flat point of a monkey saddle)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        double tolerance = 0.05;
        DA.GetData(1, ref tolerance);
        double cancel = 0;
        DA.GetData(2, ref cancel);

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

        var located = Umbilics.Locate(input.Data, curvature, Math.Max(0, cancel));
        var umbilics = located.Select(d => MeshConvert.ToRhinoPoint(d.Point)).ToList();
        var index = located.Select(d => d.Index).ToList();
        if (located.Length > 0)
        {
            int lemons = located.Count(d => d.Index > 0), stars = located.Length - lemons;
            string sum = Umbilics.IndexSum(located).ToString("0.#");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{located.Length} isolated umbilic(s): {lemons} of index +½, {stars} of index −½, sum {sum}" +
                (cancel <= 0 && lemons > 0 && stars > 0 ? " — opposite pairs close together are noise; set Cancel to a few mesh edges to remove them." : "."));
        }

        DA.SetDataList(0, points);
        DA.SetDataList(1, indices);
        DA.SetDataList(2, mask);
        DA.SetDataList(3, umbilics);
        DA.SetDataList(4, index);
    }
}
