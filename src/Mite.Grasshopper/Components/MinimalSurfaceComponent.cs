using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.FormFinding;

namespace Mite.Grasshopper.Components;

public class MinimalSurfaceComponent : GH_Component
{
    public MinimalSurfaceComponent()
        : base("Minimal Surface", "MinSrf",
            "Finds a minimal surface with fixed boundaries by iterating exact cotangent " +
            "Laplace solves (each iteration freezes the weights and solves the linear system).",
            "Mite", "FormFinding") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE6");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.MinimalSurface.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Max weight-update iterations (default 20)", GH_ParamAccess.item, 20);
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
        int maxIter = 20;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, fixedList)) return;
        DA.GetData(2, ref maxIter);

        var data = MeshConvert.ToMeshData(mesh);

        if (fixedList.Count != data.VertexCount)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Fixed needs one flag per vertex: got {fixedList.Count}, mesh has {data.VertexCount}.");
            return;
        }
        var fixedVerts = fixedList.ToArray();

        bool anyFixed = false;
        foreach (bool b in fixedVerts) if (b) { anyFixed = true; break; }
        if (!anyFixed)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "At least one vertex must be fixed (typically the boundary), or the mesh collapses to a point.");
            return;
        }

        MinimalSurface.Result result;
        try
        {
            var opts = new MinimalSurface.Options { MaxIterations = maxIter };
            result = MinimalSurface.Compute(data, fixedVerts, opts);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var outMesh = mesh.DuplicateMesh();
        for (int i = 0; i < result.Vertices.Length; i++)
            outMesh.Vertices.SetVertex(i, MeshConvert.ToRhinoPoint(result.Vertices[i]));
        outMesh.Normals.ComputeNormals();

        DA.SetData(0, outMesh);
        DA.SetData(1, result.Iterations);
        DA.SetData(2, result.Residual);
    }
}
