using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper;

internal static class MeshConvert
{
    public static MeshData ToMeshData(Mesh rhinoMesh)
    {
        // Work on a copy: mutating the input mesh would corrupt upstream
        // Grasshopper document data (the user's mesh would get triangulated).
        var copy = rhinoMesh.DuplicateMesh();
        copy.Faces.ConvertQuadsToTriangles();

        var verts = new Vec3d[copy.Vertices.Count];
        for (int i = 0; i < verts.Length; i++)
        {
            var p = copy.Vertices[i];
            verts[i] = new Vec3d(p.X, p.Y, p.Z);
        }

        var faces = new int[copy.Faces.Count][];
        for (int i = 0; i < faces.Length; i++)
        {
            var f = copy.Faces[i];
            faces[i] = f.IsQuad
                ? new[] { f.A, f.B, f.C, f.D }
                : new[] { f.A, f.B, f.C };
        }

        return new MeshData(verts, faces);
    }

    public static MeshData ToMeshDataKeepQuads(Mesh rhinoMesh)
    {
        var verts = new Vec3d[rhinoMesh.Vertices.Count];
        for (int i = 0; i < verts.Length; i++)
        {
            var p = rhinoMesh.Vertices[i];
            verts[i] = new Vec3d(p.X, p.Y, p.Z);
        }

        var faces = new int[rhinoMesh.Faces.Count][];
        for (int i = 0; i < faces.Length; i++)
        {
            var f = rhinoMesh.Faces[i];
            faces[i] = f.IsQuad
                ? new[] { f.A, f.B, f.C, f.D }
                : new[] { f.A, f.B, f.C };
        }

        return new MeshData(verts, faces);
    }

    public static Point3d ToRhinoPoint(Vec3d v) => new(v.X, v.Y, v.Z);
    public static Vector3d ToRhinoVector(Vec3d v) => new(v.X, v.Y, v.Z);
}
