using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class MeshCleanupComponent : MiteComponent
{
    public MeshCleanupComponent()
        : base("Mesh Cleanup", "Cleanup",
            "Heals a mesh for analysis: welds coincident vertices (within a tolerance), removes " +
            "degenerate and duplicate faces, reduces collapsed faces, drops isolated vertices, and " +
            "unifies face winding (closed parts outward, open parts keep the winding most of their " +
            "area had). Sliver > 0 collapses needles and flips caps below that angle. Reports " +
            "non-manifold edges, non-orientable parts and holes. Mite components weld exactly " +
            "coincident vertices on their own; run this first on meshes with near-coincident " +
            "vertices, slivers or mixed winding (joins, booleans, imports).",
            "Util", "MeshCleanup") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF0");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Tolerance", "T", "Vertex weld distance (0 = automatic: 1e-6 of the average edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddBooleanParameter("UnifyWinding", "U", "Make face winding consistent: closed parts face outward, open parts keep the winding most of their area had", GH_ParamAccess.item, true);
        pManager.AddNumberParameter("Sliver", "S",
            "Triangles with an angle below this (degrees) are slivers: needles (longest edge ≥ 4 × shortest) are collapsed, " +
            "caps (one angle near 180°) have their long edge flipped, border caps are dropped. No vertex is moved. " +
            "0 = leave slivers (default); 1 is a good start", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Cleaned mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Welded", "W", "Vertices welded away", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Removed", "R", "Degenerate + duplicate faces removed", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Map", "I", "Cleaned vertex index for each input vertex (a collapsed vertex gives the vertex it was collapsed onto; -1 = isolated vertex, dropped)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Flipped", "F", "Faces whose winding was reversed", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Slivers", "Sl", "Slivers removed (needles collapsed + caps flipped or dropped)", GH_ParamAccess.item);
        pManager.AddLineParameter("NonManifold", "N", "Edges shared by more than two faces (not repaired)", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "What was repaired and what is left, one finding per line", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        double tolerance = 0.0;
        bool unify = true;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref tolerance);
        DA.GetData(2, ref unify);
        double sliver = 0.0;
        DA.GetData(3, ref sliver);
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

        if (sliver < 0 || sliver >= 60)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Sliver must be between 0 and 60 degrees; {sliver:0.##} was clamped.");
            sliver = Math.Max(0.0, Math.Min(59.0, sliver));
        }

        var result = MeshCleanup.Compute(data, new MeshCleanup.Options
        {
            WeldTolerance = tolerance,
            UnifyWinding = unify,
            SliverAngle = sliver,
        });
        var info = result.Info;

        int removed = result.RemovedDegenerateFaces + result.RemovedDuplicateFaces;
        if (result.WeldedVertices > 0 || removed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Welded {result.WeldedVertices} vertices, removed {removed} faces.");
        if (result.Mesh.FaceCount == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Every face was degenerate; check the weld tolerance.");
        if (info.NonManifoldEdges.Length > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{info.NonManifoldEdges.Length} edges are shared by more than two faces (see NonManifold): split the mesh there or delete the extra faces.");
        if (unify && info.OrientationConflicts > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{info.OrientationConflicts} edges cannot be oriented consistently (a Möbius-like part): normals flip across them.");
        if (info.SliversLeft > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, sliver > 0
                ? $"{info.SliversLeft} triangles are still below {sliver:0.##}°: removing them would fold the mesh or change its topology."
                : $"{info.SliversLeft} triangles have an angle below {MeshCleanup.ReportSliverAngle:0.##}° (smallest {info.MinAngleAfter:0.###}°): set Sliver to 1 to collapse or flip them.");

        DA.SetData(0, MeshConvert.ToRhinoMesh(result.Mesh));
        DA.SetData(1, result.WeldedVertices);
        DA.SetData(2, removed);
        DA.SetDataList(3, result.VertexMap);
        DA.SetData(4, info.FlippedFaces);
        DA.SetData(5, info.NeedlesCollapsed + info.CapsFlipped + info.CapsRemoved);
        var lines = new List<Line>();
        foreach (var (a, b) in info.NonManifoldEdges)
            lines.Add(new Line(MeshConvert.ToRhinoPoint(result.Mesh.Vertices[a]), MeshConvert.ToRhinoPoint(result.Mesh.Vertices[b])));
        DA.SetDataList(6, lines);
        DA.SetData(7, info.Report);
    }
}
