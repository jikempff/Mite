using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

namespace Mite.Core.Geometry;

public class MeshData
{
    public Vec3d[] Vertices { get; }
    public int[][] Faces { get; }

    public int VertexCount => Vertices.Length;
    public int FaceCount => Faces.Length;

    public MeshData(Vec3d[] vertices, int[][] faces)
    {
        Vertices = vertices;
        Faces = faces;
    }

    public MeshData(double[] flatVertices, int[][] faces)
    {
        int nv = flatVertices.Length / 3;
        Vertices = new Vec3d[nv];
        for (int i = 0; i < nv; i++)
            Vertices[i] = new Vec3d(flatVertices[i * 3], flatVertices[i * 3 + 1], flatVertices[i * 3 + 2]);
        Faces = faces;
    }

    public bool IsTriangulated
    {
        get
        {
            for (int i = 0; i < Faces.Length; i++)
                if (Faces[i].Length != 3) return false;
            return true;
        }
    }

    public int[][] Triangulate()
    {
        var tris = new List<int[]>();
        foreach (var face in Faces)
        {
            for (int i = 1; i < face.Length - 1; i++)
                tris.Add(new[] { face[0], face[i], face[i + 1] });
        }
        return tris.ToArray();
    }

    public MeshData ToTriangulated()
    {
        if (IsTriangulated) return this;
        return new MeshData(Vertices, Triangulate());
    }

    public int[][] BuildVertexFaces()
    {
        var vf = new List<int>[VertexCount];
        for (int i = 0; i < VertexCount; i++) vf[i] = new List<int>();
        for (int fi = 0; fi < FaceCount; fi++)
            foreach (int vi in Faces[fi])
                vf[vi].Add(fi);
        var result = new int[VertexCount][];
        for (int i = 0; i < VertexCount; i++) result[i] = vf[i].ToArray();
        return result;
    }

    public (int v0, int v1)[] BuildEdges()
    {
        var edgeSet = new HashSet<(int, int)>();
        foreach (var face in Faces)
        {
            for (int i = 0; i < face.Length; i++)
            {
                int a = face[i], b = face[(i + 1) % face.Length];
                edgeSet.Add(a < b ? (a, b) : (b, a));
            }
        }
        var edges = new (int, int)[edgeSet.Count];
        edgeSet.CopyTo(edges);
        return edges;
    }

    public int[][] BuildVertexNeighbors()
    {
        var neighbors = new HashSet<int>[VertexCount];
        for (int i = 0; i < VertexCount; i++) neighbors[i] = new HashSet<int>();
        foreach (var face in Faces)
        {
            for (int i = 0; i < face.Length; i++)
            {
                int a = face[i], b = face[(i + 1) % face.Length];
                neighbors[a].Add(b);
                neighbors[b].Add(a);
            }
        }
        var result = new int[VertexCount][];
        for (int i = 0; i < VertexCount; i++) result[i] = neighbors[i].ToArray();
        return result;
    }

    /// <summary>
    /// Flags vertices lying on a mesh boundary. An edge is a boundary edge when
    /// it belongs to exactly one face; both its vertices are boundary vertices.
    /// </summary>
    public bool[] BuildBoundaryVertexFlags()
    {
        var edgeFaceCount = new Dictionary<(int, int), int>();
        foreach (var face in Faces)
        {
            for (int i = 0; i < face.Length; i++)
            {
                int a = face[i], b = face[(i + 1) % face.Length];
                var key = a < b ? (a, b) : (b, a);
                edgeFaceCount.TryGetValue(key, out int c);
                edgeFaceCount[key] = c + 1;
            }
        }

        var boundary = new bool[VertexCount];
        foreach (var kv in edgeFaceCount)
        {
            if (kv.Value == 1)
            {
                boundary[kv.Key.Item1] = true;
                boundary[kv.Key.Item2] = true;
            }
        }
        return boundary;
    }

    public Vec3d[] ComputeFaceNormals()
    {
        var normals = new Vec3d[FaceCount];
        for (int fi = 0; fi < FaceCount; fi++)
        {
            var f = Faces[fi];
            Vec3d e1 = Vertices[f[1]] - Vertices[f[0]];
            Vec3d e2 = Vertices[f[2]] - Vertices[f[0]];
            normals[fi] = Vec3d.Cross(e1, e2).Normalized();
        }
        return normals;
    }

    public Vec3d[] ComputeVertexNormals()
    {
        var faceNormals = ComputeFaceNormals();
        var normals = new Vec3d[VertexCount];
        for (int fi = 0; fi < FaceCount; fi++)
        {
            var f = Faces[fi];
            for (int i = 0; i < f.Length; i++)
            {
                int prev = f[(i + f.Length - 1) % f.Length];
                int curr = f[i];
                int next = f[(i + 1) % f.Length];
                Vec3d e1 = (Vertices[prev] - Vertices[curr]).Normalized();
                Vec3d e2 = (Vertices[next] - Vertices[curr]).Normalized();
                double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(e1, e2))));
                normals[curr] = normals[curr] + angle * faceNormals[fi];
            }
        }
        for (int i = 0; i < VertexCount; i++)
            normals[i] = normals[i].Normalized();
        return normals;
    }

    /// <summary>
    /// Vertex normals with Nelson Max's weights (face normal / product of the
    /// squared lengths of the two adjacent edges). Exact for vertices lying on
    /// a sphere, and markedly more accurate than angle weighting for curvature
    /// estimation. Faces are fanned from their first vertex if not triangular.
    /// </summary>
    public Vec3d[] ComputeVertexNormalsMax()
    {
        var normals = new Vec3d[VertexCount];
        foreach (var face in Faces)
        {
            for (int i = 1; i < face.Length - 1; i++)
            {
                int i0 = face[0], i1 = face[i], i2 = face[i + 1];
                Vec3d a = Vertices[i0] - Vertices[i1];
                Vec3d b = Vertices[i1] - Vertices[i2];
                Vec3d c = Vertices[i2] - Vertices[i0];
                double l2a = a.LengthSquared, l2b = b.LengthSquared, l2c = c.LengthSquared;
                if (l2a < 1e-30 || l2b < 1e-30 || l2c < 1e-30) continue;

                Vec3d fn = Vec3d.Cross(a, b);
                normals[i0] = normals[i0] + (1.0 / (l2a * l2c)) * fn;
                normals[i1] = normals[i1] + (1.0 / (l2b * l2a)) * fn;
                normals[i2] = normals[i2] + (1.0 / (l2c * l2b)) * fn;
            }
        }
        for (int i = 0; i < VertexCount; i++)
            normals[i] = normals[i].Normalized();
        return normals;
    }

    public static MeshData LoadObj(string path)
    {
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("v "))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                verts.Add(new Vec3d(
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture),
                    double.Parse(parts[3], CultureInfo.InvariantCulture)));
            }
            else if (line.StartsWith("f "))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var indices = new int[parts.Length - 1];
                for (int i = 1; i < parts.Length; i++)
                {
                    string idx = parts[i].Split('/')[0];
                    indices[i - 1] = int.Parse(idx) - 1;
                }
                faces.Add(indices);
            }
        }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }
}
