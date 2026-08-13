using System;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class MeshCleanupComponent : GH_Component
{
    public MeshCleanupComponent()
        : base("Mesh Cleanup", "Cleanup",
            "Heals a mesh for analysis: welds coincident vertices, removes degenerate and " +
            "duplicate faces, and unifies face winding. Most Mite components assume a clean " +
            "mesh — run this first on meshes from joins, booleans, or imports.",
            "Mite", "Util") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF0");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.MeshCleanup.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Tolerance", "T", "Vertex weld distance (0 = automatic: 1e-6 of the average edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("UnifyWinding", "U", "Make face winding consistent", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Cleaned mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Welded", "W", "Vertices welded away", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Removed", "R", "Degenerate + duplicate faces removed", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        double tolerance = 0.0;
        bool unify = true;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref tolerance);
        DA.GetData(2, ref unify);

        var data = MeshConvert.ToMeshDataKeepQuads(mesh);
        var result = MeshCleanup.Compute(data, tolerance, unify);

        int removed = result.RemovedDegenerateFaces + result.RemovedDuplicateFaces;
        if (result.WeldedVertices > 0 || removed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Welded {result.WeldedVertices} vertices, removed {removed} faces.");

        DA.SetData(0, MeshConvert.ToRhinoMesh(result.Mesh));
        DA.SetData(1, result.WeldedVertices);
        DA.SetData(2, removed);
    }
}
