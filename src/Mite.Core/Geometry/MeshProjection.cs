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

    public MeshData Mesh => _mesh;

    public MeshProjection(MeshData mesh)
    {
        _mesh = mesh.ToTriangulated();
        _vertexFaces = _mesh.BuildVertexFaces();
        _vertexNeighbors = _mesh.BuildVertexNeighbors();
        _faceNormals = _mesh.ComputeFaceNormals();
    }

    public readonly struct Hit
    {
        public readonly Vec3d Point;
        public readonly Vec3d Normal;
        public readonly int NearestVertex;

        public Hit(Vec3d point, Vec3d normal, int nearestVertex)
        {
            Point = point;
            Normal = normal;
            NearestVertex = nearestVertex;
        }
    }

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
            return new Hit(_mesh.Vertices[v], Vec3d.Zero, v);

        // Nearest vertex of the winning face, used as the next query hint
        var face = _mesh.Faces[bestFace];
        int nearest = face[0];
        double nd = (_mesh.Vertices[face[0]] - bestPoint).LengthSquared;
        for (int j = 1; j < 3; j++)
        {
            double d = (_mesh.Vertices[face[j]] - bestPoint).LengthSquared;
            if (d < nd) { nd = d; nearest = face[j]; }
        }

        return new Hit(bestPoint, _faceNormals[bestFace], nearest);
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
