using System;
using System.Collections.Generic;

namespace Mite.Core.Geometry;

/// <summary>
/// Projects points onto a triangulated mesh using a local closest-point search.
/// Build once per mesh, then query repeatedly with a vertex hint for fast tracing.
/// </summary>
public class MeshProjection
{
    private readonly MeshData _mesh;
    private readonly int[][] _vertexFaces;
    private readonly int[][] _vertexNeighbors;
    private readonly Vec3d[] _faceNormals;
    private readonly Vec3d[] _vertexNormals;
    private readonly double _averageEdgeLength;
    private readonly VertexKdTree _kdTree;

    public MeshData Mesh => _mesh;

    /// <summary>
    /// Mean triangle edge length of the mesh. A natural length scale for
    /// tolerances (e.g. loop-closure capture radii in curve tracing).
    /// </summary>
    public double AverageEdgeLength => _averageEdgeLength;

    public MeshProjection(MeshData mesh)
    {
        _mesh = mesh.ToTriangulated();
        _vertexFaces = _mesh.BuildVertexFaces();
        _vertexNeighbors = _mesh.BuildVertexNeighbors();
        _faceNormals = _mesh.ComputeFaceNormals();
        _vertexNormals = _mesh.ComputeVertexNormals();
        _kdTree = VertexKdTree.Build(_mesh.Vertices);

        double sum = 0;
        int count = 0;
        foreach (var f in _mesh.Faces)
        {
            for (int i = 0; i < 3; i++)
            {
                sum += (_mesh.Vertices[f[(i + 1) % 3]] - _mesh.Vertices[f[i]]).Length;
                count++;
            }
        }
        _averageEdgeLength = count > 0 ? sum / count : 1.0;
    }

    public readonly struct Hit
    {
        public readonly Vec3d Point;

        /// <summary>Flat normal of the face containing the hit.</summary>
        public readonly Vec3d Normal;

        /// <summary>
        /// Barycentric-interpolated vertex normal: varies continuously across
        /// the surface, so it can be finite-differenced (e.g. for torsion).
        /// </summary>
        public readonly Vec3d SmoothNormal;

        public readonly int NearestVertex;

        /// <summary>Index of the face containing the hit, -1 if degenerate.</summary>
        public readonly int Face;

        /// <summary>Barycentric coordinates of the hit within Face.</summary>
        public readonly Vec3d Bary;

        public Hit(Vec3d point, Vec3d normal, Vec3d smoothNormal, int nearestVertex,
            int face, Vec3d bary)
        {
            Point = point;
            Normal = normal;
            SmoothNormal = smoothNormal;
            NearestVertex = nearestVertex;
            Face = face;
            Bary = bary;
        }
    }

    /// <summary>
    /// Globally nearest vertex (exact kd-tree query). Use once to seed hints.
    /// Returns -1 on an empty mesh.
    /// </summary>
    public int NearestVertexGlobal(Vec3d p) => _kdTree.Nearest(p);

    /// <summary>
    /// Finds the closest point on the mesh near the given vertex hint.
    /// Walks vertex-to-vertex toward the query point, then projects onto the
    /// triangles incident to the local neighborhood.
    /// </summary>
    public Hit ClosestPoint(Vec3d p, int hint)
    {
        int v = hint >= 0 && hint < _mesh.VertexCount ? hint : 0;

        // Greedy descent to the locally closest vertex
        double bestDist = (_mesh.Vertices[v] - p).LengthSquared;
        for (int iter = 0; iter < 64; iter++)
        {
            int next = -1;
            foreach (int n in _vertexNeighbors[v])
            {
                double d = (_mesh.Vertices[n] - p).LengthSquared;
                if (d < bestDist) { bestDist = d; next = n; }
            }
            if (next < 0) break;
            v = next;
        }

        // Project onto triangles around v and its 1-ring
        var candidateFaces = new HashSet<int>();
        foreach (int f in _vertexFaces[v]) candidateFaces.Add(f);
        foreach (int n in _vertexNeighbors[v])
            foreach (int f in _vertexFaces[n]) candidateFaces.Add(f);

        Vec3d bestPoint = _mesh.Vertices[v];
        int bestFace = -1;
        double bestProjDist = double.MaxValue;

        foreach (int fi in candidateFaces)
        {
            var f = _mesh.Faces[fi];
            Vec3d q = ClosestPointOnTriangle(p, _mesh.Vertices[f[0]], _mesh.Vertices[f[1]], _mesh.Vertices[f[2]]);
            double d = (q - p).LengthSquared;
            if (d < bestProjDist)
            {
                bestProjDist = d;
                bestPoint = q;
                bestFace = fi;
            }
        }

        if (bestFace < 0)
            return new Hit(_mesh.Vertices[v], Vec3d.Zero, Vec3d.Zero, v, -1, new Vec3d(1, 0, 0));

        // Nearest vertex of the winning face, used as the next query hint
        var face = _mesh.Faces[bestFace];
        int nearest = face[0];
        double nd = (_mesh.Vertices[face[0]] - bestPoint).LengthSquared;
        for (int j = 1; j < 3; j++)
        {
            double d = (_mesh.Vertices[face[j]] - bestPoint).LengthSquared;
            if (d < nd) { nd = d; nearest = face[j]; }
        }

        Vec3d bary = Barycentric(bestPoint, face);
        Vec3d smooth = (bary.X * _vertexNormals[face[0]] +
                        bary.Y * _vertexNormals[face[1]] +
                        bary.Z * _vertexNormals[face[2]]).Normalized();
        return new Hit(bestPoint, _faceNormals[bestFace], smooth, nearest, bestFace, bary);
    }

    private Vec3d Barycentric(Vec3d p, int[] face)
    {
        Vec3d a = _mesh.Vertices[face[0]], b = _mesh.Vertices[face[1]], c = _mesh.Vertices[face[2]];
        Vec3d v0 = b - a, v1 = c - a, v2 = p - a;

        double d00 = Vec3d.Dot(v0, v0), d01 = Vec3d.Dot(v0, v1), d11 = Vec3d.Dot(v1, v1);
        double d20 = Vec3d.Dot(v2, v0), d21 = Vec3d.Dot(v2, v1);
        double denom = d00 * d11 - d01 * d01;
        if (Math.Abs(denom) < 1e-20)
            return new Vec3d(1, 0, 0);

        double bv = (d11 * d20 - d01 * d21) / denom;
        double bw = (d00 * d21 - d01 * d20) / denom;
        return new Vec3d(1.0 - bv - bw, bv, bw);
    }

    // Ericson, "Real-Time Collision Detection", closest point on triangle
    private static Vec3d ClosestPointOnTriangle(Vec3d p, Vec3d a, Vec3d b, Vec3d c)
    {
        Vec3d ab = b - a, ac = c - a, ap = p - a;
        double d1 = Vec3d.Dot(ab, ap), d2 = Vec3d.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Vec3d bp = p - b;
        double d3 = Vec3d.Dot(ab, bp), d4 = Vec3d.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double v = d1 / (d1 - d3);
            return a + v * ab;
        }

        Vec3d cp = p - c;
        double d5 = Vec3d.Dot(ab, cp), d6 = Vec3d.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double w = d2 / (d2 - d6);
            return a + w * ac;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        double denom = 1.0 / (va + vb + vc);
        double v2 = vb * denom, w2 = vc * denom;
        return a + v2 * ab + w2 * ac;
    }
}
