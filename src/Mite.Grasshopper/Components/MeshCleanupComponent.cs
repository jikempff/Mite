using System;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class MeshCleanupComponent : MiteComponent
{
    public MeshCleanupComponent()
        : base("Mesh Cleanup", "Cleanup",
            "Heals a mesh for analysis: welds coincident vertices (within a tolerance), removes " +
            "degenerate and duplicate faces, reduces collapsed faces, and unifies face winding. " +
            "Mite components weld exactly coincident vertices on their own; run this first on " +
            "meshes with near-coincident vertices, slivers or mixed winding (joins, booleans, imports).",
            "Util", "MeshCleanup") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF0");

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
        pManager.AddIntegerParameter("Map", "I", "Cleaned vertex index for each input vertex", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        double tolerance = 0.0;
        bool unify = true;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref tolerance);
        DA.GetData(2, ref unify);
        if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The mesh has no faces.");
            return;
        }

        // Raw (unwelded) intake: cleanup is the one place that must see the duplicates
        var verts = new Vec3d[mesh.Vertices.Count];
        for (int i = 0; i < verts.Length; i++) verts[i] = MeshConvert.ToVec3d(mesh.Vertices.Point3dAt(i));
        var faces = new int[mesh.Faces.Count][];
        for (int i = 0; i < faces.Length; i++)
        {
            var f = mesh.Faces[i];
            faces[i] = f.IsQuad ? new[] { f.A, f.B, f.C, f.D } : new[] { f.A, f.B, f.C };
        }
        var data = new MeshData(verts, faces);

        var result = MeshCleanup.Compute(data, tolerance, unify);

        int removed = result.RemovedDegenerateFaces + result.RemovedDuplicateFaces;
        if (result.WeldedVertices > 0 || removed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Welded {result.WeldedVertices} vertices, removed {removed} faces.");
        if (result.Mesh.FaceCount == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Every face was degenerate; check the weld tolerance.");

        DA.SetData(0, MeshConvert.ToRhinoMesh(result.Mesh));
        DA.SetData(1, result.WeldedVertices);
        DA.SetData(2, removed);
        DA.SetDataList(3, result.VertexMap);
    }
}
