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
