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
    private readonly HashSet<long> _boundaryEdges;
    private readonly bool[] _boundaryVertex;

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

        // Boundary edges: edges used by exactly one face
        var edgeUse = new Dictionary<long, int>();
        foreach (var f in _mesh.Faces)
            for (int i = 0; i < 3; i++)
            {
                long k = EdgeKey(f[i], f[(i + 1) % 3]);
                edgeUse[k] = edgeUse.TryGetValue(k, out int c) ? c + 1 : 1;
            }
        _boundaryEdges = new HashSet<long>();
        _boundaryVertex = new bool[_mesh.VertexCount];
        foreach (var kv in edgeUse)
            if (kv.Value == 1)
            {
                _boundaryEdges.Add(kv.Key);
                _boundaryVertex[(int)(kv.Key / _mesh.VertexCount)] = true;
                _boundaryVertex[(int)(kv.Key % _mesh.VertexCount)] = true;
            }
    }

    private long EdgeKey(int a, int b) =>
        a < b ? (long)a * _mesh.VertexCount + b : (long)b * _mesh.VertexCount + a;

    /// <summary>True when the edge (a, b) belongs to exactly one face.</summary>
    public bool IsBoundaryEdge(int a, int b) => _boundaryEdges.Contains(EdgeKey(a, b));

    /// <summary>True when the vertex lies on a mesh boundary.</summary>
    public bool IsBoundaryVertex(int v) => v >= 0 && v < _boundaryVertex.Length && _boundaryVertex[v];

    /// <summary>
    /// True when the hit point lies on a boundary edge (or boundary vertex) of
    /// its face, i.e. the projection clamped a query point that was outside
    /// the mesh onto the mesh border.
    /// </summary>
    public bool IsOnBoundary(in Hit hit, double baryTol = 1e-6)
    {
        if (hit.Face < 0) return false;
        var f = _mesh.Faces[hit.Face];
        double[] b = { hit.Bary.X, hit.Bary.Y, hit.Bary.Z };
        for (int k = 0; k < 3; k++)
        {
            if (b[k] > baryTol) continue;
            // on the edge opposite corner k
            if (IsBoundaryEdge(f[(k + 1) % 3], f[(k + 2) % 3])) return true;
        }
        // exactly on a vertex: boundary if the vertex is
        int zeros = 0, corner = -1;
        for (int k = 0; k < 3; k++) if (b[k] <= baryTol) zeros++; else corner = k;
        if (zeros == 2 && corner >= 0 && _boundaryVertex[f[corner]]) return true;
        return false;
    }

    /// <summary>
    /// Where a step from → to leaves the mesh: the point on the boundary edges
    /// around the hit (the clamped projection of "to") closest to the step
    /// segment. Ending a trace here instead of at the perpendicular foot of
    /// the projection keeps the last segment on the curve's own direction, so
    /// curves meet the border without a hook or a crawl along it.
    /// </summary>
    public bool TryBoundaryExit(Vec3d from, Vec3d to, in Hit hit, out Vec3d exit)
    {
        exit = hit.Point;
        if (hit.Face < 0) return false;

        // The step chord from → to floats above the surface (and the boundary
        // edge is a chord under it), so the closest points of the two skew
        // segments in 3D land up to a tenth of a step beside the trajectory —
        // enough for a visible hook after fairing. Intersect them in the
        // tangent plane of the hit instead: the exit is where the projected
        // step crosses the projected edge, i.e. exactly on the curve's line
        // of travel. The 3D closest point remains the fallback for steps
        // that do not cross an edge in projection.
        Vec3d n = hit.Normal.LengthSquared > 1e-20 ? hit.Normal.Normalized() : hit.SmoothNormal.Normalized();
        Vec3d ax = to - from; ax = ax - Vec3d.Dot(ax, n) * n;
        if (ax.LengthSquared < 1e-30) return false;
        ax = ax.Normalized();
        Vec3d ay = Vec3d.Cross(n, ax);
        double stepLen = Vec3d.Dot(to - from, ax);

        var seen = new HashSet<long>();
        double best = double.MaxValue, bestT = double.MaxValue;
        bool found = false, crossed = false;
        var face = _mesh.Faces[hit.Face];
        for (int c = 0; c < 3; c++)
        {
            foreach (int fi in _vertexFaces[face[c]])
            {
                var f = _mesh.Faces[fi];
                for (int i = 0; i < 3; i++)
                {
                    int a = f[i], b = f[(i + 1) % 3];
                    long k = EdgeKey(a, b);
                    if (!_boundaryEdges.Contains(k) || !seen.Add(k)) continue;
                    Vec3d A = _mesh.Vertices[a], B = _mesh.Vertices[b];
                    // 2D intersection in the tangent plane: from + t·ax·stepLen meets A + u (B − A)
                    double ax0 = Vec3d.Dot(A - from, ax), ay0 = Vec3d.Dot(A - from, ay);
                    double bx0 = Vec3d.Dot(B - from, ax), by0 = Vec3d.Dot(B - from, ay);
                    double dy = by0 - ay0;
                    if (Math.Abs(dy) > 1e-15)
                    {
                        double u = -ay0 / dy; // where the edge crosses the travel line (ay = 0)
                        if (u >= -1e-9 && u <= 1 + 1e-9)
                        {
                            double t = (ax0 + u * (bx0 - ax0)) / Math.Max(stepLen, 1e-300);
                            // ahead of the start (allow a hair behind for a start on the edge) and within ~2 steps
                            if (t > -0.05 && t < 2.0 && t < bestT)
                            {
                                bestT = t; exit = A + Math.Max(0.0, Math.Min(1.0, u)) * (B - A); found = true; crossed = true;
                            }
                        }
                    }
                    if (crossed) continue;
                    double d = SegmentQueries.SegmentSegment(from, to, A, B, out _, out _, out _, out Vec3d q);
                    if (d < best) { best = d; exit = q; found = true; }
                }
            }
        }
        return found;
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
        if (_mesh.VertexCount == 0)
            return new Hit(p, Vec3d.Zero, Vec3d.Zero, -1, -1, new Vec3d(1, 0, 0));

        // Without a usable hint, start from the globally nearest vertex: the
        // greedy descent below only finds a local minimum, so starting at an
        // arbitrary vertex could return a point on the far side of the mesh
        int v = hint >= 0 && hint < _mesh.VertexCount ? hint : _kdTree.Nearest(p);

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
