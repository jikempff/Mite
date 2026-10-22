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
            "Planarizes the faces of a quad-dominant mesh. Method 1 (default) is the guided projection of " +
            "Tang et al. 2014 with the fairness and closeness terms of Liu et al. 2006 and Zadravec et al. 2010: " +
            "planar in about ten iterations, nodes held on the Reference surface (the input mesh when none is " +
            "given) and anchored where they started. Method 0 is the old projection onto per-face best-fit planes. " +
            "Set Strength to 0 to only measure planarity without moving anything.",
            "Form Finding", "Planarization") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE5");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input quad mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Iterations", "I", "Max iterations (default 100; the optimisation usually needs about ten)", GH_ParamAccess.item, 100);
        pManager.AddNumberParameter("Tolerance", "T", "Convergence tolerance on the max face deviation, model units (default 1e-6)", GH_ParamAccess.item, 1e-6);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags (one per input mesh vertex)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Strength", "S", "Method 0: step strength per iteration 0..1 (default 1). Either method: 0 = measure only", GH_ParamAccess.item, 1.0);
        pManager[3].Optional = true;
        // appended 2026-10-22: the optimisation method and its terms (older definitions keep Method 0's inputs)
        pManager.AddIntegerParameter("Method", "Mt",
            "0 = projection onto per-face best-fit planes (slow, drifts off the surface); " +
            "1 = guided projection: Gauss–Newton on n·(vi − vj) = 0 with auxiliary face normals, soft fairness, " +
            "closeness to the Reference and an anchor at the start positions, weights halved every iteration (default)",
            GH_ParamAccess.item, 1);
        pManager.AddMeshParameter("Reference", "R", "Design surface the nodes are kept on (Method 1); the input mesh itself when empty", GH_ParamAccess.item);
        pManager[pManager.ParamCount - 1].Optional = true;
        pManager.AddNumberParameter("Fairness", "Fa", "Method 1: weight of the fairness term — second differences along the mesh polylines, relative to the input, so laths keep their shape and quads do not crumple (default 1; lower it to let a noisy input straighten)", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Closeness", "Cl", "Method 1: weight of the closeness to the Reference (default 1; 0 lets the mesh leave the surface freely)", GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Planarized mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Deviation", "D", "Planarity deviation per face (max distance of a corner from the best-fit plane)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Iterations performed", GH_ParamAccess.item);
        // appended 2026-10-22
        pManager.AddNumberParameter("Moved", "Mv", "Largest node displacement from the input, model units", GH_ParamAccess.item);
        pManager.AddNumberParameter("Distance", "Ds", "Largest distance of a free node from the Reference surface after planarizing (Method 1)", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        var input = LoadMesh(DA, 0, keepQuads: true);
        if (input == null) return;

        int maxIter = 100, method = 1;
        double tol = 1e-6, strength = 1.0, fairness = 1.0, closeness = 1.0;
        var fixedList = new List<bool>();
        DA.GetData(1, ref maxIter);
        DA.GetData(2, ref tol);
        DA.GetDataList(3, fixedList);
        DA.GetData(4, ref strength);
        DA.GetData(5, ref method);
        DA.GetData(7, ref fairness);
        DA.GetData(8, ref closeness);

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
            DA.SetData(3, 0.0);
            return;
        }

        if (method <= 0)
        {
            var opts = new Planarization.Options
            {
                MaxIterations = Math.Max(0, maxIter), Tolerance = tol, Strength = Math.Min(1.0, strength)
            };
            var result = Planarization.Compute(data, fixedVerts, opts);
            double moved = 0;
            for (int i = 0; i < data.VertexCount; i++) moved = Math.Max(moved, (result.Vertices[i] - data.Vertices[i]).Length);
            DA.SetData(0, input.WithVertices(mesh, result.Vertices));
            DA.SetDataList(1, result.FaceDeviations);
            DA.SetData(2, result.Iterations);
            DA.SetData(3, moved);
            return;
        }

        Mite.Core.Geometry.MeshData? reference = null;
        Mesh? refMesh = null;
        if (DA.GetData(6, ref refMesh) && refMesh != null)
        {
            var refInput = MeshConvert.Load(refMesh, keepQuads: false);
            if (refInput == null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The Reference mesh has no faces; the input mesh is used as the reference.");
            else reference = refInput.Data;
        }

        var r = PlanarizationOptimizer.Optimize(data, fixedVerts, reference, new PlanarizationOptimizer.Options
        {
            Iterations = Math.Max(0, maxIter), Tolerance = tol, Fairness = Math.Max(0, fairness), Closeness = Math.Max(0, closeness),
            ShouldCancel = Cancelled,
        });

        DA.SetData(0, input.WithVertices(mesh, r.Vertices));
        DA.SetDataList(1, r.FaceDeviations);
        DA.SetData(2, r.Iterations);
        DA.SetData(3, r.MaxDisplacement);
        if (!double.IsNaN(r.MaxSurfaceDistance)) DA.SetData(4, r.MaxSurfaceDistance);

        if (r.PolygonCount == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Every face is a triangle: nothing to planarize.");
            return;
        }
        string edgeShare = r.MeanEdge > 0 ? $" ({r.MaxDisplacement / r.MeanEdge:0.00} × the mean edge)" : "";
        string distance = double.IsNaN(r.MaxSurfaceDistance) ? "" : $", {r.MaxSurfaceDistance:G3} off the reference";
        if (r.Stalled)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"No planar mesh nearby: stalled at deviation {r.MaxDeviation:G3} after {r.Iterations} iterations, nodes moved up to " +
                $"{r.MaxDisplacement:G3}{edgeShare}{distance}. A net along asymptotic (self-conjugate) directions cannot be planarized — " +
                "use a conjugate or curvature-line net (Conjugate Net), or free the surface with Closeness 0.");
        else if (!r.Converged)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Not converged: deviation {r.MaxDeviation:G3} after {r.Iterations} iterations (tolerance {tol:G3}); raise Iterations.");
        else
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{r.PolygonCount} faces planar to {r.MaxDeviation:G3} in {r.Iterations} iterations; nodes moved up to {r.MaxDisplacement:G3}{edgeShare}{distance}.");
    }
}
