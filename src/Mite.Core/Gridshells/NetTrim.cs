using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Cookie-cutting a net: trace on a larger surface, then cut the curves to a
/// boundary drawn on (or above) it.
///
/// Why. Schling's asymptotic gridshells are designed on a reference surface
/// that is "larger than the actual structure" (Schling, Kilian, Wang, Schikore
/// &amp; Pottmann 2018, Design and construction of curved support structures
/// with repetitive parameters, AAG 2018, §2.2), because the curve network
/// deteriorates towards the border ("strip quality deteriorates when
/// approaching the boundary", §2.3) and because the border of the structure
/// is a design decision: the Asymptotic Gridshell's outline follows the site
/// ("three curved horizontal supports nestle well along the complex site
/// boundaries", §3.1), the AAG timber roof of Schling, Wang, Hoyer &amp;
/// Pottmann 2022 (Designing asymptotic geodesic hybrid gridshells, CAD 152) is
/// "a quarter portion of a rotational surface, that is cropped by a 4x4 m
/// square boundary" (§4.2) and the steel facade "was finally cropped on one
/// side to fit the original facade" (§4.3); §3.3 of the same paper starts the
/// design by selecting "just a part of the initial AAG-web". Mite's tracers
/// stop at the mesh border, so the boundary of the mesh was the boundary of
/// the net; this class separates the two.
///
/// Method.
/// 1. The boundary polylines are pulled onto the mesh: resampled at half a
///    mesh edge and projected either by exact closest point
///    (<see cref="MeshProjection.ClosestPointGlobal"/>) or, with a Direction,
///    along that line (nearest ray–triangle hit, Möller–Trumbore 1997); samples
///    are added until consecutive ones lie in the same or in edge-adjacent
///    faces, so the chain of faces the boundary crosses is complete.
/// 2. Those faces are the wall. The mesh vertices that are not on a wall face
///    are flooded through mesh edges into regions; the boundary is a Jordan
///    curve on the surface and separates it. Components on the same side of a
///    loop are one region (the band itself can isolate vertices), regions on
///    opposite sides are neighbours in a graph (one hop = one loop crossed), and
///    every region gets the
///    BFS depth from the regions that touch the mesh border: odd depth is
///    inside the cookie — the ring of a one-loop boundary, a cookie with a
///    hole when a second loop lies inside the first. Reference points override
///    this (their regions are inside), Invert swaps the two.
/// 3. Points in a non-wall face take their face's region. Points in the wall
///    band are classified by the side of the nearest boundary segment, the
///    sign of ((t × (p − b)) · n) with t the segment direction, b the foot and
///    n the face normal (the pseudonormal side test of signed-distance
///    point-in-polygon; Haines 1994, Point in polygon strategies, Graphics
///    Gems IV, for the crossing count it is equivalent to); which side of each
///    loop is inside is voted from the regions next to its wall faces.
/// 4. A curve is cut where consecutive vertices change side: the crossing is
///    the 2D intersection of the curve segment with the nearest boundary
///    segments in the plane of the local face normal, half-open on the
///    boundary parameter so a crossing exactly at a sample counts once
///    (Haines' "vertex infinitesimally above the ray"), with a bisection on
///    the side test as the fallback. Pieces keep their order along the curve;
///    pieces shorter than MinLength are dropped.
///
/// Assumptions: the mesh is a manifold surface; the curves lie on it (they are
/// Mite's traced polylines) and are sampled finer than the cookie's features
/// (a curve segment that enters and leaves the cookie between two of its
/// vertices is not cut); a loop that is narrower than two faces is lost in the
/// wall band. The pulled boundary lies on the facets, so a boundary drawn off
/// the surface lands up to d·tan(dihedral/2) off the smooth foot.
/// </summary>
public static class NetTrim
{
    public class Options
    {
        /// <summary>
        /// Projection direction for the boundary. Zero (default) pulls each
        /// boundary point to its closest point on the mesh; otherwise the point
        /// is projected along ±Direction onto the nearest hit (a plan curve
        /// drawn above the surface with Direction = −Z).
        /// </summary>
        public Vec3d Direction { get; set; } = Vec3d.Zero;

        /// <summary>
        /// Reference points: the regions containing them are the cookie. Empty
        /// (default): regions an odd number of loops away from the mesh border.
        /// </summary>
        public IReadOnlyList<Vec3d>? Inside { get; set; }

        /// <summary>Keep what is outside the boundary instead (cut a hole).</summary>
        public bool Invert { get; set; } = false;

        /// <summary>Boundary sampling step (0 = half the mean mesh edge).</summary>
        public double Tolerance { get; set; } = 0.0;

        /// <summary>Pieces shorter than this are dropped (default 0: only degenerate pieces).</summary>
        public double MinLength { get; set; } = 0.0;
    }

    public class Result
    {
        /// <summary>Per input family, the pieces inside the cookie, in curve order.</summary>
        public List<Vec3d[]>[] Families { get; }

        /// <summary>Per piece, the index of the input curve it came from.</summary>
        public List<int>[] Source { get; }

        /// <summary>The ends made by the cut, in the order they were made.</summary>
        public List<Vec3d> Cuts { get; }

        /// <summary>The boundary as pulled onto the mesh (closed loops repeat their first point).</summary>
        public List<Vec3d[]> Boundary { get; }

        public int Regions { get; internal set; }
        public int InsideRegions { get; internal set; }
        public int BoundarySamples { get; internal set; }
        public int BoundaryOffMesh { get; internal set; }
        public int CurvesIn { get; internal set; }
        public int Pieces { get; internal set; }
        public int RemovedWhole { get; internal set; }
        public int CutCurves { get; internal set; }
        public int DroppedShort { get; internal set; }
        public string Report { get; internal set; } = "";

        internal Result(int families)
        {
            Families = new List<Vec3d[]>[families];
            Source = new List<int>[families];
            for (int i = 0; i < families; i++) { Families[i] = new List<Vec3d[]>(); Source[i] = new List<int>(); }
            Cuts = new List<Vec3d>();
            Boundary = new List<Vec3d[]>();
        }
    }

    /// <summary>
    /// Pulls the boundary, partitions the mesh and trims every family. The
    /// boundary polylines are closed when their first and last points
    /// coincide (within the sampling step); open polylines cut the surface
    /// like a knife and separate it only when both ends reach the mesh border.
    /// </summary>
    public static Result Trim(MeshProjection proj, IReadOnlyList<IReadOnlyList<Vec3d[]>> families,
        IReadOnlyList<Vec3d[]> boundary, Options? options = null)
    {
        options ??= new Options();
        var region = Region.Build(proj, boundary, options);
        var result = new Result(families.Count);
        foreach (var l in region.PulledLoops) result.Boundary.Add(l);
        result.Regions = region.RegionCount;
        result.InsideRegions = region.InsideRegionCount;
        result.BoundarySamples = region.SampleCount;
        result.BoundaryOffMesh = region.OffMesh;

        var sb = new StringBuilder();
        sb.Append(boundary.Count).Append(boundary.Count == 1 ? " boundary loop" : " boundary loops").Append(" pulled to the mesh (")
          .Append(region.SampleCount).Append(" samples");
        if (region.OffMesh > 0) sb.Append(", ").Append(region.OffMesh).Append(" off the mesh");
        sb.Append("); ").Append(region.RegionCount).Append(region.RegionCount == 1 ? " region, " : " regions, ")
          .Append(region.InsideRegionCount).Append(" inside");
        if (region.InsideRegionCount == 0) sb.Append(" — the boundary does not enclose anything on this mesh");
        for (int f = 0; f < families.Count; f++)
        {
            var stats = region.TrimFamily(families[f], options.MinLength, result.Families[f], result.Source[f], result.Cuts);
            result.CurvesIn += families[f].Count;
            result.Pieces += result.Families[f].Count;
            result.RemovedWhole += stats.removed;
            result.CutCurves += stats.cut;
            result.DroppedShort += stats.dropped;
            sb.Append("; family ").Append((char)('A' + f)).Append(": ").Append(families[f].Count).Append(" curves → ")
              .Append(result.Families[f].Count).Append(" pieces (").Append(stats.cut).Append(" cut, ").Append(stats.removed).Append(" removed");
            if (stats.dropped > 0) sb.Append(", ").Append(stats.dropped).Append(" short pieces dropped");
            sb.Append(')');
        }
        result.Report = sb.ToString();
        return result;
    }

    /// <summary>
    /// The partition of a mesh by a boundary: build once, then classify points
    /// and trim as many curve families as needed.
    /// </summary>
    public sealed class Region
    {
        private readonly MeshProjection _proj;
        private readonly MeshData _mesh;
        private readonly int[][] _vertexFaces;
        private readonly int[][] _vertexNeighbors;
        private readonly bool[] _wallVertex;
        private readonly int[] _component;           // per vertex, -1 on the wall
        private readonly bool[] _componentInside;     // per component
        private readonly List<(int loop, int sample)>[] _faceSamples;  // per face, the boundary samples in it (null = not a wall face)
        private readonly List<Sample[]> _loops = new();
        private readonly bool[] _loopClosed;
        private readonly bool?[] _insideIsLeft;   // null: no evidence, the band counts as outside

        public IReadOnlyList<Vec3d[]> PulledLoops { get; }
        public int RegionCount { get; }
        public int InsideRegionCount { get; }
        public int SampleCount { get; }
        public int OffMesh { get; }
        public double Step { get; }

        private struct Sample
        {
            public Vec3d Point;
            public int Face;      // -1 when the projection missed the mesh
        }

        private Region(MeshProjection proj, IReadOnlyList<Vec3d[]> boundary, Options options)
        {
            _proj = proj;
            _mesh = proj.Mesh;
            _vertexFaces = _mesh.BuildVertexFaces();
            _vertexNeighbors = _mesh.BuildVertexNeighbors();
            Step = options.Tolerance > 0 ? options.Tolerance : 0.5 * proj.AverageEdgeLength;
            var faceNeighbors = BuildFaceNeighbors(_mesh);
            var faceNormals = _mesh.ComputeFaceNormals();

            // 1. pull the boundary
            _loopClosed = new bool[boundary.Count];
            int samples = 0, off = 0;
            var pulled = new List<Vec3d[]>();
            for (int li = 0; li < boundary.Count; li++)
            {
                var poly = boundary[li];
                var pts = new List<Vec3d>();
                foreach (var p in poly) if (pts.Count == 0 || (p - pts[pts.Count - 1]).LengthSquared > 1e-24) pts.Add(p);
                bool closed = pts.Count > 2 && (pts[0] - pts[pts.Count - 1]).Length <= Step;
                if (closed && (pts[0] - pts[pts.Count - 1]).LengthSquared <= 1e-24) pts.RemoveAt(pts.Count - 1);
                _loopClosed[li] = closed;
                var list = new List<Sample>();
                if (pts.Count >= 2)
                {
                    int segs = closed ? pts.Count : pts.Count - 1;
                    var first = Pull(pts[0], options.Direction);
                    list.Add(first);
                    for (int s = 0; s < segs; s++)
                    {
                        Vec3d a = pts[s], b = pts[(s + 1) % pts.Count];
                        var hb = (s == segs - 1 && closed) ? first : Pull(b, options.Direction);
                        Subdivide(a, b, list[list.Count - 1], hb, 0, list, faceNeighbors, options.Direction);
                        if (!(s == segs - 1 && closed)) list.Add(hb);
                    }
                }
                _loops.Add(list.ToArray());
                samples += list.Count;
                off += list.Count(x => x.Face < 0);
                var on = list.Where(x => x.Face >= 0).Select(x => x.Point).ToList();
                if (closed && on.Count > 0) on.Add(on[0]);
                pulled.Add(on.ToArray());
            }
            PulledLoops = pulled;
            SampleCount = samples;
            OffMesh = off;

            // 2. wall faces and vertices
            _faceSamples = new List<(int, int)>[_mesh.FaceCount];
            for (int li = 0; li < _loops.Count; li++)
                for (int k = 0; k < _loops[li].Length; k++)
                {
                    int f = _loops[li][k].Face;
                    if (f < 0) continue;
                    (_faceSamples[f] ??= new List<(int, int)>()).Add((li, k));
                }
            _wallVertex = new bool[_mesh.VertexCount];
            for (int f = 0; f < _mesh.FaceCount; f++)
                if (_faceSamples[f] != null) foreach (int v in _mesh.Faces[f]) _wallVertex[v] = true;

            // flood the rest into regions
            _component = new int[_mesh.VertexCount];
            for (int v = 0; v < _mesh.VertexCount; v++) _component[v] = -1;
            int comps = 0;
            var stack = new Stack<int>();
            for (int v = 0; v < _mesh.VertexCount; v++)
            {
                if (_wallVertex[v] || _component[v] >= 0) continue;
                _component[v] = comps;
                stack.Push(v);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (int w in _vertexNeighbors[u])
                        if (!_wallVertex[w] && _component[w] < 0) { _component[w] = comps; stack.Push(w); }
                }
                comps++;
            }
            RegionCount = comps;

            // The wall band splits what is really one region into pieces (a
            // vertex whose whole one-ring is on wall faces is a region of its
            // own), so the flooded components are merged by side: at every
            // boundary sample, the non-wall vertices around its wall face are
            // sorted left / right of the loop by ((t × (w − s)) · n), all left
            // components are one region, all right components another, and the
            // two are neighbours across this loop. Depth is then the BFS
            // distance from the regions touching the mesh border, one hop per
            // loop crossed.
            var parent = new int[comps];
            for (int c = 0; c < comps; c++) parent[c] = c;
            int Find(int c) { while (parent[c] != c) { parent[c] = parent[parent[c]]; c = parent[c]; } return c; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            var across = new List<(int left, int right, int loop)>();
            var left = new List<int>();
            var right = new List<int>();
            for (int f = 0; f < _mesh.FaceCount; f++)
            {
                if (_faceSamples[f] == null) continue;
                Vec3d n = faceNormals[f];
                foreach (var (li, k) in _faceSamples[f])
                {
                    if (!TangentAt(li, k, out Vec3d s, out Vec3d t)) continue;
                    left.Clear(); right.Clear();
                    foreach (int v in _mesh.Faces[f])
                        foreach (int w in _vertexNeighbors[v])
                        {
                            int c = _component[w];
                            if (c < 0) continue;
                            double side = Vec3d.Dot(Vec3d.Cross(t, _mesh.Vertices[w] - s), n);
                            if (Math.Abs(side) < 1e-14) continue;
                            (side > 0 ? left : right).Add(c);
                        }
                    for (int i = 1; i < left.Count; i++) Union(left[0], left[i]);
                    for (int i = 1; i < right.Count; i++) Union(right[0], right[i]);
                    if (left.Count > 0 && right.Count > 0) across.Add((left[0], right[0], li));
                }
            }
            var groupOf = new int[comps];
            var groupIndex = new Dictionary<int, int>();
            for (int c = 0; c < comps; c++)
            {
                int root = Find(c);
                if (!groupIndex.TryGetValue(root, out int g)) { g = groupIndex.Count; groupIndex[root] = g; }
                groupOf[c] = g;
            }
            int groups = groupIndex.Count;
            for (int v = 0; v < _mesh.VertexCount; v++) if (_component[v] >= 0) _component[v] = groupOf[_component[v]];
            RegionCount = groups;

            var touchesBorder = new bool[groups];
            for (int v = 0; v < _mesh.VertexCount; v++)
                if (_component[v] >= 0 && proj.IsBoundaryVertex(v)) touchesBorder[_component[v]] = true;
            var adjacency = new HashSet<int>[groups];
            for (int g = 0; g < groups; g++) adjacency[g] = new HashSet<int>();
            foreach (var e in across)
            {
                int a = groupOf[e.left], b = groupOf[e.right];
                if (a != b) { adjacency[a].Add(b); adjacency[b].Add(a); }
            }
            var depth = new int[groups];
            for (int g = 0; g < groups; g++) depth[g] = -1;
            var queue = new Queue<int>();
            for (int g = 0; g < groups; g++) if (touchesBorder[g]) { depth[g] = 0; queue.Enqueue(g); }
            if (queue.Count == 0 && groups > 0)
            {
                // closed surface: the largest region is the outside
                var size = new int[groups];
                for (int v = 0; v < _mesh.VertexCount; v++) if (_component[v] >= 0) size[_component[v]]++;
                int big = Array.IndexOf(size, size.Max());
                depth[big] = 0; queue.Enqueue(big);
            }
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                foreach (int d in adjacency[g]) if (depth[d] < 0) { depth[d] = depth[g] + 1; queue.Enqueue(d); }
            }
            _componentInside = new bool[groups];
            if (options.Inside != null && options.Inside.Count > 0)
            {
                foreach (var p in options.Inside)
                {
                    int g = ComponentAt(Pull(p, Vec3d.Zero).Face);
                    if (g >= 0) _componentInside[g] = true;
                }
            }
            else
                for (int g = 0; g < groups; g++) _componentInside[g] = depth[g] > 0 && (depth[g] & 1) == 1;
            if (options.Invert) for (int g = 0; g < groups; g++) _componentInside[g] = !_componentInside[g];
            InsideRegionCount = _componentInside.Count(x => x);

            // 3. which side of each loop is inside, voted over its crossings
            var votes = new int[_loops.Count];
            foreach (var e in across)
            {
                bool l = _componentInside[groupOf[e.left]], r = _componentInside[groupOf[e.right]];
                if (l != r) votes[e.loop] += l ? 1 : -1;
            }
            _insideIsLeft = new bool?[_loops.Count];
            for (int li = 0; li < _loops.Count; li++) _insideIsLeft[li] = votes[li] > 0 ? true : votes[li] < 0 ? false : (bool?)null;
        }

        /// <summary>Builds the partition; the boundary polylines are in model space.</summary>
        public static Region Build(MeshProjection proj, IReadOnlyList<Vec3d[]> boundary, Options? options = null) =>
            new Region(proj, boundary, options ?? new Options());

        /// <summary>True when the point (on or near the mesh) lies in the cookie.</summary>
        public bool IsInside(Vec3d p)
        {
            var hit = _proj.ClosestPointGlobal(p);
            return IsInside(hit);
        }

        /// <summary>Classifies a projected point; hit.Face must be a face of the projection's mesh.</summary>
        public bool IsInside(in MeshProjection.Hit hit)
        {
            if (hit.Face < 0) return false;
            var face = _mesh.Faces[hit.Face];
            if (_faceSamples[hit.Face] == null)
                foreach (int v in face)
                    if (_component[v] >= 0) return _componentInside[_component[v]];
            // in the wall band: side of the nearest boundary segment
            if (NearestBoundarySegment(hit.Face, hit.Point, out int li, out int k, out Vec3d b, out Vec3d t))
            {
                if (_insideIsLeft[li] is not bool insideIsLeft) return false;
                double side = Vec3d.Dot(Vec3d.Cross(t, hit.Point - b), hit.Normal);
                bool left = side >= 0;
                return left == insideIsLeft;
            }
            int c = ComponentAt(hit.Face);
            return c >= 0 && _componentInside[c];
        }

        /// <summary>
        /// Cuts one family to the cookie. Pieces and their source indices are
        /// appended to the lists; the cut points to cuts. Returns the number
        /// of curves cut (partly kept), removed whole and dropped as short.
        /// </summary>
        public (int cut, int removed, int dropped) TrimFamily(IReadOnlyList<Vec3d[]> curves, double minLength,
            List<Vec3d[]> pieces, List<int> source, List<Vec3d> cuts)
        {
            int cutCount = 0, removed = 0, dropped = 0;
            for (int ci = 0; ci < curves.Count; ci++)
            {
                var curve = curves[ci];
                if (curve == null || curve.Length < 2) { removed++; continue; }
                var pts = curve;
                bool closed = pts.Length > 3 && (pts[0] - pts[pts.Length - 1]).LengthSquared <= 1e-18;
                if (closed) pts = pts.Take(pts.Length - 1).ToArray();

                var hits = new MeshProjection.Hit[pts.Length];
                var inside = new bool[pts.Length];
                int hint = -1;
                for (int i = 0; i < pts.Length; i++)
                {
                    hits[i] = hint < 0 ? _proj.ClosestPointGlobal(pts[i]) : _proj.ClosestPoint(pts[i], hint);
                    hint = hits[i].NearestVertex;
                    inside[i] = IsInside(hits[i]);
                }
                int nIn = inside.Count(x => x);
                if (nIn == 0) { removed++; continue; }
                if (nIn == pts.Length) { pieces.Add(curve); source.Add(ci); continue; }

                if (closed)
                {
                    // start at an outside vertex so no piece wraps round the seam
                    int start = Array.IndexOf(inside, false);
                    pts = Rotate(pts, start); hits = Rotate(hits, start); inside = Rotate(inside, start);
                    var ptsL = pts.ToList(); ptsL.Add(pts[0]); pts = ptsL.ToArray();
                    var hitsL = hits.ToList(); hitsL.Add(hits[0]); hits = hitsL.ToArray();
                    var inL = inside.ToList(); inL.Add(inside[0]); inside = inL.ToArray();
                }

                int before = pieces.Count;
                var piece = new List<Vec3d>();
                for (int i = 0; i < pts.Length; i++)
                {
                    if (inside[i])
                    {
                        if (piece.Count == 0 && i > 0)
                        {
                            Vec3d c = Crossing(pts[i], hits[i], pts[i - 1], hits[i - 1]);
                            piece.Add(c); cuts.Add(c);
                        }
                        piece.Add(pts[i]);
                        if (i == pts.Length - 1) Flush(piece, ci, minLength, pieces, source, ref dropped);
                    }
                    else if (piece.Count > 0)
                    {
                        Vec3d c = Crossing(pts[i - 1], hits[i - 1], pts[i], hits[i]);
                        piece.Add(c); cuts.Add(c);
                        Flush(piece, ci, minLength, pieces, source, ref dropped);
                    }
                }
                if (pieces.Count > before) cutCount++; else removed++;
            }
            return (cutCount, removed, dropped);
        }

        private static void Flush(List<Vec3d> piece, int ci, double minLength, List<Vec3d[]> pieces, List<int> source, ref int dropped)
        {
            double len = 0;
            for (int i = 1; i < piece.Count; i++) len += (piece[i] - piece[i - 1]).Length;
            if (piece.Count >= 2 && len > Math.Max(minLength, 1e-12)) { pieces.Add(piece.ToArray()); source.Add(ci); }
            else dropped++;
            piece.Clear();
        }

        private static T[] Rotate<T>(T[] a, int start)
        {
            var r = new T[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[(start + i) % a.Length];
            return r;
        }

        /// <summary>
        /// The boundary crossing on the curve segment from the inside vertex
        /// pIn to the outside vertex pOut.
        /// </summary>
        private Vec3d Crossing(Vec3d pIn, in MeshProjection.Hit hIn, Vec3d pOut, in MeshProjection.Hit hOut)
        {
            Vec3d n = (hIn.Normal + hOut.Normal);
            if (n.LengthSquared < 1e-20) n = hIn.Normal;
            n = n.Normalized();
            Basis(n, out Vec3d e1, out Vec3d e2);
            double ax = Vec3d.Dot(pIn, e1), ay = Vec3d.Dot(pIn, e2);
            double bx = Vec3d.Dot(pOut, e1), by = Vec3d.Dot(pOut, e2);
            double bestU = double.MaxValue;
            var seen = new HashSet<(int, int)>();
            foreach (int f in new[] { hIn.Face, hOut.Face })
            {
                if (f < 0) continue;
                foreach (int v in _mesh.Faces[f])
                    foreach (int g in _vertexFaces[v])
                    {
                        var list = _faceSamples[g];
                        if (list == null) continue;
                        foreach (var (li, k) in list)
                        {
                            foreach (int kk in new[] { k - 1, k })
                            {
                                if (!SegmentAt(li, kk, out Vec3d s0, out Vec3d s1) || !seen.Add((li, kk))) continue;
                                double cx = Vec3d.Dot(s0, e1), cy = Vec3d.Dot(s0, e2);
                                double dx = Vec3d.Dot(s1, e1), dy = Vec3d.Dot(s1, e2);
                                // (a + u (b − a)) = (c + v (d − c))
                                double rx = bx - ax, ry = by - ay, sx = dx - cx, sy = dy - cy;
                                double den = rx * sy - ry * sx;
                                if (Math.Abs(den) < 1e-18) continue;
                                double qx = cx - ax, qy = cy - ay;
                                double u = (qx * sy - qy * sx) / den;
                                double w = (qx * ry - qy * rx) / den;
                                if (u < -1e-9 || u > 1 + 1e-9 || w < 0 || w >= 1) continue;
                                if (u < bestU) bestU = u;
                            }
                        }
                    }
            }
            if (bestU != double.MaxValue) return pIn + Math.Max(0, Math.Min(1, bestU)) * (pOut - pIn);

            // fallback: bisection on the side test along the chord
            double lo = 0, hi = 1;
            int hint = hIn.NearestVertex;
            for (int it = 0; it < 30; it++)
            {
                double m = 0.5 * (lo + hi);
                var h = _proj.ClosestPoint(pIn + m * (pOut - pIn), hint);
                hint = h.NearestVertex;
                if (IsInside(h)) lo = m; else hi = m;
            }
            return pIn + 0.5 * (lo + hi) * (pOut - pIn);
        }

        private int ComponentAt(int face)
        {
            if (face < 0) return -1;
            foreach (int v in _mesh.Faces[face]) if (_component[v] >= 0) return _component[v];
            foreach (int v in _mesh.Faces[face])
                foreach (int w in _vertexNeighbors[v]) if (_component[w] >= 0) return _component[w];
            return -1;
        }

        private bool NearestBoundarySegment(int face, Vec3d p, out int loop, out int seg, out Vec3d foot, out Vec3d tangent)
        {
            loop = -1; seg = -1; foot = tangent = Vec3d.Zero;
            double best = double.MaxValue;
            var seen = new HashSet<(int, int)>();
            // the samples in this face and in the faces around its vertices
            foreach (int v in _mesh.Faces[face])
                foreach (int g in _vertexFaces[v])
                {
                    var list = _faceSamples[g];
                    if (list == null) continue;
                    foreach (var (li, k) in list)
                        foreach (int kk in new[] { k - 1, k })
                        {
                            if (!seen.Add((li, kk)) || !SegmentAt(li, kk, out Vec3d a, out Vec3d b)) continue;
                            Vec3d d = b - a;
                            double l2 = d.LengthSquared;
                            double t = l2 > 0 ? Math.Max(0, Math.Min(1, Vec3d.Dot(p - a, d) / l2)) : 0;
                            Vec3d q = a + t * d;
                            double dist = (q - p).LengthSquared;
                            if (dist < best) { best = dist; loop = li; seg = kk; foot = q; tangent = l2 > 0 ? d / Math.Sqrt(l2) : Vec3d.Zero; }
                        }
                }
            return loop >= 0 && tangent.LengthSquared > 0;
        }

        /// <summary>Segment kk of loop li (from sample kk to kk + 1, wrapping on closed loops); false when a sample is off the mesh.</summary>
        private bool SegmentAt(int li, int kk, out Vec3d a, out Vec3d b)
        {
            var L = _loops[li];
            a = b = Vec3d.Zero;
            int n = L.Length;
            if (n < 2) return false;
            int i0 = kk, i1 = kk + 1;
            if (_loopClosed[li]) { i0 = ((kk % n) + n) % n; i1 = (i0 + 1) % n; }
            else if (i0 < 0 || i1 >= n) return false;
            if (L[i0].Face < 0 || L[i1].Face < 0) return false;
            a = L[i0].Point; b = L[i1].Point;
            return (b - a).LengthSquared > 1e-24;
        }

        private bool TangentAt(int li, int k, out Vec3d point, out Vec3d tangent)
        {
            point = _loops[li][k].Point;
            tangent = Vec3d.Zero;
            if (SegmentAt(li, k - 1, out Vec3d a0, out Vec3d b0)) tangent += (b0 - a0).Normalized();
            if (SegmentAt(li, k, out Vec3d a1, out Vec3d b1)) tangent += (b1 - a1).Normalized();
            if (tangent.LengthSquared < 1e-20) return false;
            tangent = tangent.Normalized();
            return true;
        }

        private static void Basis(Vec3d n, out Vec3d e1, out Vec3d e2)
        {
            Vec3d a = Math.Abs(n.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
            e1 = Vec3d.Cross(n, a).Normalized();
            e2 = Vec3d.Cross(n, e1);
        }

        /// <summary>
        /// Adds the samples strictly between a and b (whose pulled samples are
        /// ha and hb) so that consecutive samples lie in the same or in
        /// edge-adjacent faces and are at most Step apart.
        /// </summary>
        private void Subdivide(Vec3d a, Vec3d b, Sample ha, Sample hb, int depth, List<Sample> list, int[][] faceNeighbors, Vec3d direction)
        {
            bool adjacent = ha.Face < 0 || hb.Face < 0 || ha.Face == hb.Face || Array.IndexOf(faceNeighbors[ha.Face], hb.Face) >= 0;
            double len = (b - a).Length;
            if ((adjacent && len <= Step) || depth >= 12 || len < 1e-12) return;
            Vec3d m = 0.5 * (a + b);
            var hm = Pull(m, direction);
            Subdivide(a, m, ha, hm, depth + 1, list, faceNeighbors, direction);
            list.Add(hm);
            Subdivide(m, b, hm, hb, depth + 1, list, faceNeighbors, direction);
        }

        private Sample Pull(Vec3d p, Vec3d direction)
        {
            if (direction.LengthSquared < 1e-24)
            {
                var hit = _proj.ClosestPointGlobal(p);
                return new Sample { Point = hit.Point, Face = hit.Face };
            }
            // nearest ray–triangle hit along the line through p (both ways)
            Vec3d d = direction.Normalized();
            double bestT = double.MaxValue;
            int bestF = -1;
            Vec3d bestP = p;
            var V = _mesh.Vertices;
            for (int f = 0; f < _mesh.FaceCount; f++)
            {
                var tri = _mesh.Faces[f];
                Vec3d v0 = V[tri[0]], e1 = V[tri[1]] - v0, e2 = V[tri[2]] - v0;
                Vec3d h = Vec3d.Cross(d, e2);
                double det = Vec3d.Dot(e1, h);
                if (Math.Abs(det) < 1e-18) continue;
                double inv = 1.0 / det;
                Vec3d s = p - v0;
                double u = inv * Vec3d.Dot(s, h);
                if (u < -1e-9 || u > 1 + 1e-9) continue;
                Vec3d q = Vec3d.Cross(s, e1);
                double v = inv * Vec3d.Dot(d, q);
                if (v < -1e-9 || u + v > 1 + 1e-9) continue;
                double t = inv * Vec3d.Dot(e2, q);
                if (Math.Abs(t) < Math.Abs(bestT)) { bestT = t; bestF = f; bestP = p + t * d; }
            }
            return new Sample { Point = bestP, Face = bestF };
        }

        private static int[][] BuildFaceNeighbors(MeshData mesh)
        {
            var edgeFaces = new Dictionary<long, (int a, int b)>();
            long nv = mesh.VertexCount;
            for (int f = 0; f < mesh.FaceCount; f++)
            {
                var face = mesh.Faces[f];
                for (int i = 0; i < face.Length; i++)
                {
                    int p = face[i], q = face[(i + 1) % face.Length];
                    long key = p < q ? p * nv + q : q * nv + p;
                    if (edgeFaces.TryGetValue(key, out var pair)) edgeFaces[key] = (pair.a, pair.b < 0 ? f : pair.b);
                    else edgeFaces[key] = (f, -1);
                }
            }
            var result = new int[mesh.FaceCount][];
            var lists = new List<int>[mesh.FaceCount];
            for (int f = 0; f < mesh.FaceCount; f++) lists[f] = new List<int>(3);
            foreach (var kv in edgeFaces)
            {
                if (kv.Value.b < 0) continue;
                lists[kv.Value.a].Add(kv.Value.b);
                lists[kv.Value.b].Add(kv.Value.a);
            }
            for (int f = 0; f < mesh.FaceCount; f++) result[f] = lists[f].ToArray();
            return result;
        }
    }
}
