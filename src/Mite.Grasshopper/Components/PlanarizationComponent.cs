using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.FormFinding;

namespace Mite.Grasshopper.Components;

public class PlanarizationComponent : MiteComponent
{
    public PlanarizationComponent()
        : base("Planarize Mesh", "Planarize",
            "Iterative quad planarization by projecting vertices onto per-face best-fit planes. " +
            "Set Strength to 0 to only measure planarity without moving anything.",
            "Form Finding", "Planarization") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE5");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input quad mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Iterations", "I", "Max iterations (default 100)", GH_ParamAccess.item, 100);
        pManager.AddNumberParameter("Tolerance", "T", "Convergence tolerance on the max face deviation, model units (default 1e-6)", GH_ParamAccess.item, 1e-6);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags (one per input mesh vertex)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Strength", "S", "Step strength per iteration 0..1 (default 1; 0 = measure only)", GH_ParamAccess.item, 1.0);
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Planarized mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Deviation", "D", "Planarity deviation per face (max distance of a corner from the best-fit plane)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Iterations performed", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        var input = LoadMesh(DA, 0, keepQuads: true);
        if (input == null) return;

        int maxIter = 100;
        double tol = 1e-6, strength = 1.0;
        var fixedList = new List<bool>();
        DA.GetData(1, ref maxIter);
        DA.GetData(2, ref tol);
        DA.GetDataList(3, fixedList);
        DA.GetData(4, ref strength);

        var data = input.Data;
        if (data.FaceCount != mesh.Faces.Count)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{mesh.Faces.Count - data.FaceCount} fully collapsed face(s) were dropped; the Deviation list follows the remaining faces.");
        if (fixedList.Count != 0 && fixedList.Count != input.RhinoVertexCount)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Fixed needs one flag per vertex: got {fixedList.Count}, mesh has {input.RhinoVertexCount}.");
            return;
        }
        var fixedVerts = input.Collapse(fixedList);

        if (strength <= 0)
        {
            DA.SetData(0, mesh);
            DA.SetDataList(1, Planarization.ComputeDeviation(data));
            DA.SetData(2, 0);
            return;
        }

        var opts = new Planarization.Options
        {
            MaxIterations = Math.Max(0, maxIter), Tolerance = tol, Strength = Math.Min(1.0, strength)
        };
        var result = Planarization.Compute(data, fixedVerts, opts);

        DA.SetData(0, input.WithVertices(mesh, result.Vertices));
        DA.SetDataList(1, result.FaceDeviations);
        DA.SetData(2, result.Iterations);
    }
}
