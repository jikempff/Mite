using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.FormFinding;

namespace Mite.Grasshopper.Components;

public class MinimalSurfaceComponent : MiteComponent
{
    public MinimalSurfaceComponent()
        : base("Minimal Surface", "MinSrf",
            "Finds a minimal surface with fixed boundaries by iterating exact cotangent " +
            "Laplace solves (each iteration freezes the weights and solves the sparse linear system). " +
            "Leave Fixed empty to fix the mesh boundary.",
            "Form Finding", "MinimalSurface") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE6");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags, one per input mesh vertex (empty = fix the boundary)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Max weight-update iterations (default 20)", GH_ParamAccess.item, 20);
        pManager.AddNumberParameter("Tolerance", "T", "Convergence: max vertex move per iteration as a fraction of the mesh size (default 1e-8)", GH_ParamAccess.item, 1e-8);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Resulting minimal surface mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Iterations", "I", "Iterations performed", GH_ParamAccess.item);
        pManager.AddNumberParameter("Residual", "R", "Max vertex movement in the last iteration", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        var input = LoadMesh(DA, 0);
        if (input == null) return;

        var fixedList = new List<bool>();
        int maxIter = 20;
        double tol = 1e-8;
        DA.GetDataList(1, fixedList);
        DA.GetData(2, ref maxIter);
        DA.GetData(3, ref tol);

        var data = input.Data;
        bool[] fixedVerts;
        if (fixedList.Count == 0)
        {
            fixedVerts = data.BuildBoundaryVertexFlags();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No Fixed flags given: the mesh boundary is fixed.");
        }
        else if (fixedList.Count == input.RhinoVertexCount)
        {
            fixedVerts = input.Collapse(fixedList);
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Fixed needs one flag per vertex: got {fixedList.Count}, mesh has {input.RhinoVertexCount}.");
            return;
        }

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
            var opts = new MinimalSurface.Options { MaxIterations = Math.Max(1, maxIter), Tolerance = tol, ShouldCancel = Cancelled };
            result = MinimalSurface.Compute(data, fixedVerts, opts);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }
        if (Cancelled())
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, result is not converged.");

        DA.SetData(0, input.WithVertices(mesh, result.Vertices));
        DA.SetData(1, result.Iterations);
        DA.SetData(2, result.Residual);
    }
}
