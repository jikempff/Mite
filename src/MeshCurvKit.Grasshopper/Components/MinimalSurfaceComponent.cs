using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using MeshCurvKit.Core.FormFinding;

namespace MeshCurvKit.Grasshopper.Components;

public class MinimalSurfaceComponent : GH_Component
{
    public MinimalSurfaceComponent()
        : base("Minimal Surface", "MinSrf",
            "Finds a minimal surface by cotangent Laplacian flow with fixed boundaries.",
            "MeshCurvKit", "FormFinding") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE6");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Max iterations (default 100)", GH_ParamAccess.item, 100);
        pManager.AddNumberParameter("StepSize", "S", "Step size (default 0.5)", GH_ParamAccess.item, 0.5);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Resulting minimal surface mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Iterations", "I", "Iterations performed", GH_ParamAccess.item);
        pManager.AddNumberParameter("Residual", "R", "Final residual", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var fixedList = new List<bool>();
        int maxIter = 100;
        double stepSize = 0.5;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, fixedList)) return;
        DA.GetData(2, ref maxIter);
        DA.GetData(3, ref stepSize);

        var data = MeshConvert.ToMeshData(mesh);
        var fixedVerts = fixedList.Count == data.VertexCount
            ? fixedList.ToArray()
            : new bool[data.VertexCount];

        var opts = new MinimalSurface.Options { MaxIterations = maxIter, StepSize = stepSize };
        var result = MinimalSurface.Compute(data, fixedVerts, opts);

        var outMesh = mesh.DuplicateMesh();
        for (int i = 0; i < result.Vertices.Length; i++)
            outMesh.Vertices.SetVertex(i, MeshConvert.ToRhinoPoint(result.Vertices[i]));
        outMesh.Normals.ComputeNormals();

        DA.SetData(0, outMesh);
        DA.SetData(1, result.Iterations);
        DA.SetData(2, result.Residual);
    }
}
