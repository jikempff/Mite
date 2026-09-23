using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Dynamics;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class DynamicRelaxationComponent : MiteComponent
{
    public DynamicRelaxationComponent()
        : base("Dynamic Relaxation", "DynRelax",
            "Form finding by dynamic relaxation with kinetic damping: the mesh edges act as " +
            "springs, optionally under gravity / point loads, soap-film tension and smoothing, " +
            "and the system is integrated until it rests in equilibrium. Hanging models, " +
            "pre-tensioned cable nets and quick minimal-surface-like relaxations of a gridshell mesh.",
            "Form Finding", "DynamicRelaxation") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF8");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh (edges become springs)", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags, one per input mesh vertex (empty = fix the boundary)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Stiffness", "K", "Edge spring stiffness (default 1)", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("RestScale", "R", "Rest length factor: 1 keeps edge lengths, < 1 pre-tensions the net (default 1)", GH_ParamAccess.item, 1.0);
        pManager.AddVectorParameter("Gravity", "G", "Acceleration applied to every free vertex (default none; e.g. 0,0,-1 for a hanging model)", GH_ParamAccess.item, Vector3d.Zero);
        pManager.AddVectorParameter("Loads", "L", "Point load per input mesh vertex (one vector broadcasts)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Tension", "Te", "Soap-film (area minimization) strength (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Smoothness", "Sm", "Laplacian smoothing strength (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Iterations", "I", "Maximum integration steps (default 2000)", GH_ParamAccess.item, 2000);
        pManager.AddNumberParameter("Tolerance", "T", "Convergence: residual force / applied force (default 1e-4)", GH_ParamAccess.item, 1e-4);
        pManager[1].Optional = true;
        pManager[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Relaxed mesh", GH_ParamAccess.item);
        pManager.AddPointParameter("Points", "P", "Relaxed vertex positions (one per input mesh vertex)", GH_ParamAccess.list);
        pManager.AddLineParameter("Edges", "E", "Springs in the order of the Forces output", GH_ParamAccess.list);
        pManager.AddNumberParameter("Forces", "N", "Axial force per edge (tension positive)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Steps performed", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Converged", "C", "True when the residual dropped below the tolerance", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        var input = LoadMesh(DA, 0, keepQuads: true);
        if (input == null) return;

        var fixedList = new List<bool>();
        var loadList = new List<Vector3d>();
        double stiffness = 1.0, restScale = 1.0, tension = 0.0, smooth = 0.0, tol = 1e-4;
        var gravity = Vector3d.Zero;
        int iterations = 2000;
        DA.GetDataList(1, fixedList);
        DA.GetData(2, ref stiffness);
        DA.GetData(3, ref restScale);
        DA.GetData(4, ref gravity);
        DA.GetDataList(5, loadList);
        DA.GetData(6, ref tension);
        DA.GetData(7, ref smooth);
        DA.GetData(8, ref iterations);
        DA.GetData(9, ref tol);

        if (stiffness <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Stiffness must be positive.");
            return;
        }

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

        Vec3d[]? loads = null;
        if (loadList.Count == 1)
        {
            loads = new Vec3d[data.VertexCount];
            var l = MeshConvert.ToVec3d(loadList[0]);
            for (int i = 0; i < loads.Length; i++) loads[i] = l;
        }
        else if (loadList.Count > 0)
        {
            if (loadList.Count != input.RhinoVertexCount)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Loads count ({loadList.Count}) does not match the vertex count ({input.RhinoVertexCount}); missing entries are zero.");
            loads = input.CollapseAverage(loadList);
        }

        var opts = new DynamicRelaxation.Options
        {
            Stiffness = stiffness,
            RestScale = Math.Max(0.0, restScale),
            Gravity = MeshConvert.ToVec3d(gravity),
            Loads = loads,
            AreaTension = Math.Max(0.0, tension),
            Smoothness = Math.Max(0.0, smooth),
            MaxIterations = Math.Max(1, iterations),
            Tolerance = Math.Max(0.0, tol),
            ShouldCancel = Cancelled
        };

        DynamicRelaxation.Result result;
        try
        {
            result = DynamicRelaxation.Compute(data, fixedVerts, opts);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        if (Cancelled())
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, result is not converged.");
        else if (result.Iterations == 0 && result.Converged)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No forces act on the mesh (no gravity, loads, tension or pre-tension): nothing moved.");
        else if (!result.Converged)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Not converged after {result.Iterations} steps (residual {result.Residual:G3}); raise Iterations or Tolerance.");

        var expanded = input.Expand(result.Vertices);
        var points = new List<Point3d>(expanded.Length);
        foreach (var v in expanded) points.Add(MeshConvert.ToRhinoPoint(v));

        var edges = data.BuildEdges();
        var lines = new List<Line>(edges.Length);
        for (int e = 0; e < edges.Length; e++)
            lines.Add(new Line(MeshConvert.ToRhinoPoint(result.Vertices[edges[e].v0]),
                MeshConvert.ToRhinoPoint(result.Vertices[edges[e].v1])));

        DA.SetData(0, input.WithVertices(mesh, result.Vertices));
        DA.SetDataList(1, points);
        DA.SetDataList(2, lines);
        DA.SetDataList(3, result.EdgeForces);
        DA.SetData(4, result.Iterations);
        DA.SetData(5, result.Converged);
    }
}
