using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper;

/// <summary>
/// A Rhino mesh converted for the core algorithms. The core links faces only
/// through shared vertex indices, so a mesh whose faces sit on coincident but
/// separate vertices (STL imports, Brep meshes with seams, unwelded joins)
/// would look like a pile of islands: curvature 0 along every seam, traces
/// and closest-point walks stopping at them. Vertices are therefore welded
/// through Rhino's topology vertices, with maps both ways so per-vertex
/// results line up with the original mesh again (Mesh Colours, Deconstruct
/// Mesh) and seed indices from the original mesh keep working.
/// </summary>
internal sealed class MeshInput
{
    /// <summary>Welded mesh (double-precision vertices, quads split when requested).</summary>
    public MeshData Data { get; }

    /// <summary>Welded (topology) vertex index for each original mesh vertex.</summary>
    public int[] TopoOfVertex { get; }

    /// <summary>Original vertex indices merged into each welded vertex.</summary>
    public int[][] VerticesOfTopo { get; }

    public int RhinoVertexCount => TopoOfVertex.Length;

    /// <summary>True when welding merged vertices (the input had seams / duplicate vertices).</summary>
    public bool Welded => Data.VertexCount < RhinoVertexCount;

    public MeshInput(MeshData data, int[] topoOfVertex, int[][] verticesOfTopo)
    {
        Data = data;
        TopoOfVertex = topoOfVertex;
        VerticesOfTopo = verticesOfTopo;
    }

    /// <summary>Original vertex index → welded index (-1 when out of range).</summary>
    public int ToTopo(int rhinoVertex) =>
        rhinoVertex >= 0 && rhinoVertex < TopoOfVertex.Length ? TopoOfVertex[rhinoVertex] : -1;

    /// <summary>Per-welded-vertex values expanded to one per original vertex.</summary>
    public double[] Expand(double[] perTopo)
    {
        var result = new double[RhinoVertexCount];
        for (int i = 0; i < result.Length; i++) result[i] = perTopo[TopoOfVertex[i]];
        return result;
    }

    public Vec3d[] Expand(Vec3d[] perTopo)
    {
        var result = new Vec3d[RhinoVertexCount];
        for (int i = 0; i < result.Length; i++) result[i] = perTopo[TopoOfVertex[i]];
        return result;
    }

    /// <summary>Per-original-vertex flags collapsed to welded vertices (true if any merged vertex is true).</summary>
    public bool[] Collapse(IReadOnlyList<bool> perRhino)
    {
        var result = new bool[Data.VertexCount];
        for (int i = 0; i < Math.Min(perRhino.Count, RhinoVertexCount); i++)
            if (perRhino[i]) result[TopoOfVertex[i]] = true;
        return result;
    }

    /// <summary>
    /// Per-original-vertex vectors collapsed onto welded vertices by averaging
    /// (a load list is usually one vector per vertex, so the duplicates at a
    /// seam carry the same value and must not be summed).
    /// </summary>
    public Vec3d[] CollapseAverage(IReadOnlyList<Vector3d> perRhino)
    {
        var result = new Vec3d[Data.VertexCount];
        var counts = new int[Data.VertexCount];
        for (int i = 0; i < Math.Min(perRhino.Count, RhinoVertexCount); i++)
        {
            var v = perRhino[i];
            int t = TopoOfVertex[i];
            result[t] = result[t] + new Vec3d(v.X, v.Y, v.Z);
            counts[t]++;
        }
        for (int t = 0; t < result.Length; t++)
            if (counts[t] > 1) result[t] = result[t] / counts[t];
        return result;
    }

    /// <summary>
    /// Copy of the original Rhino mesh with its vertices moved to new welded
    /// positions (keeps face structure, ngons, texture coordinates).
    /// </summary>
    public Mesh WithVertices(Mesh original, Vec3d[] perTopo)
    {
        var mesh = original.DuplicateMesh();
        mesh.Vertices.UseDoublePrecisionVertices = true;
        for (int i = 0; i < RhinoVertexCount && i < mesh.Vertices.Count; i++)
        {
            var p = perTopo[TopoOfVertex[i]];
            mesh.Vertices.SetVertex(i, p.X, p.Y, p.Z);
        }
        mesh.Normals.ComputeNormals();
        mesh.FaceNormals.ComputeFaceNormals();
        return mesh;
    }
}

internal static class MeshConvert
{
    /// <summary>
    /// Converts a Rhino mesh, welding coincident vertices. Quads are kept as
    /// quads when keepQuads is set (planarization, force density), otherwise
    /// the core's degenerate-safe triangulation applies downstream.
    /// Returns null for meshes without faces.
    /// </summary>
    public static MeshInput? Load(Mesh rhinoMesh, bool keepQuads = false)
    {
        if (rhinoMesh == null || rhinoMesh.Vertices.Count == 0 || rhinoMesh.Faces.Count == 0)
            return null;

        var topo = rhinoMesh.TopologyVertices;
        int nv = rhinoMesh.Vertices.Count;
        int nt = topo.Count;

        var topoOfVertex = new int[nv];
        var lists = new List<int>[nt];
        for (int i = 0; i < nt; i++) lists[i] = new List<int>(1);
        for (int i = 0; i < nv; i++)
        {
            int t = topo.TopologyVertexIndex(i);
            if (t < 0 || t >= nt) t = 0;
            topoOfVertex[i] = t;
            lists[t].Add(i);
        }

        var verts = new Vec3d[nt];
        var verticesOfTopo = new int[nt][];
        for (int t = 0; t < nt; t++)
        {
            verticesOfTopo[t] = lists[t].ToArray();
            int src = lists[t].Count > 0 ? lists[t][0] : 0;
            var p = rhinoMesh.Vertices.Point3dAt(src);
            verts[t] = new Vec3d(p.X, p.Y, p.Z);
        }

        // Welding can merge two corners of a face (a quad at a sphere pole):
        // reduce such faces instead of feeding the core a face with a repeated
        // vertex, which would look like a boundary edge and a self-neighbour
        var faces = new List<int[]>(rhinoMesh.Faces.Count);
        var corners = new List<int>(4);
        for (int i = 0; i < rhinoMesh.Faces.Count; i++)
        {
            var f = rhinoMesh.Faces[i];
            corners.Clear();
            corners.Add(topoOfVertex[f.A]);
            corners.Add(topoOfVertex[f.B]);
            corners.Add(topoOfVertex[f.C]);
            if (f.IsQuad) corners.Add(topoOfVertex[f.D]);

            var reduced = new List<int>(4);
            foreach (int v in corners)
                if (reduced.Count == 0 || reduced[reduced.Count - 1] != v) reduced.Add(v);
            while (reduced.Count > 1 && reduced[0] == reduced[reduced.Count - 1]) reduced.RemoveAt(reduced.Count - 1);
            if (reduced.Count < 3) continue;
            faces.Add(reduced.ToArray());
        }

        var data = new MeshData(verts, faces.ToArray());
        if (!keepQuads) data = data.ToTriangulated(); // shorter-diagonal split, slivers dropped
        return new MeshInput(data, topoOfVertex, verticesOfTopo);
    }

    /// <summary>Welded, triangulated mesh data (per-vertex results need the returned map to expand).</summary>
    public static MeshData ToMeshData(Mesh rhinoMesh) =>
        Load(rhinoMesh)?.Data ?? new MeshData(Array.Empty<Vec3d>(), Array.Empty<int[]>());

    public static MeshData ToMeshDataKeepQuads(Mesh rhinoMesh) =>
        Load(rhinoMesh, keepQuads: true)?.Data ?? new MeshData(Array.Empty<Vec3d>(), Array.Empty<int[]>());

    public static Point3d ToRhinoPoint(Vec3d v) => new(v.X, v.Y, v.Z);
    public static Vector3d ToRhinoVector(Vec3d v) => new(v.X, v.Y, v.Z);
    public static Vec3d ToVec3d(Point3d p) => new(p.X, p.Y, p.Z);
    public static Vec3d ToVec3d(Vector3d v) => new(v.X, v.Y, v.Z);

    public static Mesh ToRhinoMesh(MeshData data)
    {
        var mesh = new Mesh();
        mesh.Vertices.UseDoublePrecisionVertices = true;
        foreach (var v in data.Vertices)
            mesh.Vertices.Add(v.X, v.Y, v.Z);
        foreach (var f in data.Faces)
        {
            if (f.Length == 4) mesh.Faces.AddFace(f[0], f[1], f[2], f[3]);
            else if (f.Length == 3) mesh.Faces.AddFace(f[0], f[1], f[2]);
            else if (f.Length > 4)
                for (int i = 1; i + 1 < f.Length; i++) mesh.Faces.AddFace(f[0], f[i], f[i + 1]);
        }
        mesh.Normals.ComputeNormals();
        mesh.FaceNormals.ComputeFaceNormals();
        return mesh;
    }

    /// <summary>Traced polylines as smooth interpolated curves (failures are skipped).</summary>
    public static List<Curve> ToCurves(IEnumerable<Vec3d[]> lines)
    {
        var curves = new List<Curve>();
        foreach (var line in lines)
        {
            var c = ToCurve(line);
            if (c != null) curves.Add(c);
        }
        return curves;
    }

    public static Curve? ToCurve(Vec3d[] line)
    {
        var pts = new List<Point3d>(line.Length);
        foreach (var p in line) pts.Add(ToRhinoPoint(p));
        return CurveBuild.Interpolated(pts);
    }

    public static PolylineCurve ToPolylineCurve(Vec3d[] line)
    {
        var pts = new List<Point3d>(line.Length);
        foreach (var p in line) pts.Add(ToRhinoPoint(p));
        return new PolylineCurve(pts);
    }

    /// <summary>Welded vertex index nearest to each point (for point seeds).</summary>
    public static List<int> NearestVertices(MeshProjection proj, IEnumerable<Point3d> points)
    {
        var result = new List<int>();
        foreach (var p in points)
            result.Add(proj.NearestVertexGlobal(ToVec3d(p)));
        return result;
    }
}
