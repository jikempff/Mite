// Intrinsic triangulation with signposts.
//
// Sources:
//   Sharp, Soliman & Crane 2019, "Navigating intrinsic triangulations", ACM TOG 38(4) 55 —
//     edge lengths as the only geometry, corner angles from the law of cosines, angle sums Θ_i,
//     signposts φ_ij (direction of halfedge ij in i's polar frame, rescaled by 2π/Θ_i), the flip
//     update φ_ik = φ_ij + 2π/Θ_i · θ_i^jk, and tracing an intrinsic edge across the input mesh by
//     unfolding triangles into the plane.
//   Sharp & Crane 2020, "You can find geodesic paths in triangle meshes by just flipping edges",
//     ACM TOG 39(6) 249 — an edge is flippable iff both endpoints have degree > 1 and the two
//     triangles form a convex quadrilateral; boundary wedges have angle ∞.
//   Polthier & Schmies 1998, "Straightest geodesics on polyhedral surfaces" — a trace that meets
//     a vertex leaves it at half the total angle on either side (the straightest continuation).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Geometry;

/// <summary>
/// A manifold triangle mesh whose geometry is given by edge lengths only, so its edges can be
/// flipped intrinsically (the surface never changes, only how it is cut into triangles). Every
/// halfedge carries a signpost (its direction at its origin vertex), which lets any intrinsic
/// edge be traced back onto the input mesh as a polyline lying exactly on its faces.
/// Border edges get "ghost" halfedges (face −1) so every edge has two halfedges.
/// </summary>
public sealed class IntrinsicTriangulation
{
    // ---- intrinsic (mutable) connectivity and geometry ----
    private readonly int[] _next, _twin, _origin, _face;
    private readonly double[] _len, _sign;
    private readonly int[] _vertexHe, _faceHe;
    private readonly double[] _theta;     // total angle per vertex (invariant under flips)
    private readonly bool[] _border;

    // ---- input mesh, frozen at construction (the trace target) ----
    private readonly int[] _inNext, _inTwin, _inOrigin, _inFace;
    private readonly double[] _inLen, _inSign;
    private readonly int[][] _inFan;      // outgoing input halfedges per vertex, CCW
    private readonly int[] _inVertexHe, _inFaceHe;
    private readonly Vec3d[] _pos;

    public int VertexCount => _vertexHe.Length;
    public int HalfedgeCount => _next.Length;
    public int FlipCount { get; private set; }

    /// <summary>Vertices added by splitting non-manifold vertices (one per extra fan); their positions repeat the original's.</summary>
    public int SplitVertices { get; private set; }
    public IReadOnlyList<Vec3d> Positions => _pos;

    private IntrinsicTriangulation(Vec3d[] pos, int[] next, int[] twin, int[] origin, int[] face, double[] len,
        int[] vertexHe, int[] faceHe, bool[] border)
    {
        _pos = pos; _next = next; _twin = twin; _origin = origin; _face = face; _len = len;
        _vertexHe = vertexHe; _faceHe = faceHe; _border = border;
        _sign = new double[next.Length];
        _theta = new double[pos.Length];
        _inNext = next; _inTwin = twin; _inOrigin = origin; _inFace = face; _inLen = len; _inSign = _sign;
        _inFan = Array.Empty<int[]>();
        _inVertexHe = vertexHe; _inFaceHe = faceHe;
    }

    private IntrinsicTriangulation(IntrinsicTriangulation s)
    {
        // working copy for flips; the input arrays of s stay frozen
        _pos = s._pos; _border = s._border; _theta = s._theta;
        _next = (int[])s._next.Clone(); _twin = (int[])s._twin.Clone(); _origin = (int[])s._origin.Clone();
        _face = (int[])s._face.Clone(); _len = (double[])s._len.Clone(); _sign = (double[])s._sign.Clone();
        _vertexHe = (int[])s._vertexHe.Clone(); _faceHe = (int[])s._faceHe.Clone();
        _inNext = s._next; _inTwin = s._twin; _inOrigin = s._origin; _inFace = s._face;
        _inLen = s._len; _inSign = s._sign;
        _inVertexHe = s._vertexHe; _inFaceHe = s._faceHe;
        _inFan = new int[_pos.Length][];
        for (int v = 0; v < _pos.Length; v++)
            _inFan[v] = s._vertexHe[v] < 0 ? Array.Empty<int>() : s.FanOf(v)!.ToArray();
    }

    private static long Key(int a, int b) => ((long)a << 32) | (uint)b;

    // long.GetHashCode folds the two halves with XOR, so (a, b) and (b, a) — and every pair with the
    // same a ^ b — collide; on a 20 k-vertex mesh that made the build take 12 s instead of 0.1 s
    private sealed class MixedKey : IEqualityComparer<long>
    {
        public static readonly MixedKey Instance = new();
        public bool Equals(long x, long y) => x == y;
        public int GetHashCode(long x)
        {
            ulong z = unchecked((ulong)x * 0x9E3779B97F4A7C15UL);
            z ^= z >> 29;
            return unchecked((int)(z ^ (z >> 32)));
        }
    }

    /// <summary>
    /// Builds the intrinsic triangulation of a triangle mesh (initially identical to it).
    /// Returns null, with a reason, when the mesh is not an oriented 2-manifold.
    /// </summary>
    public static IntrinsicTriangulation? Build(IReadOnlyList<Vec3d> vertices, IReadOnlyList<int[]> triangles, out string? reason)
    {
        reason = null;
        int nv = vertices.Count;
        var pos = vertices.ToArray();
        var tris = new List<int[]>();
        foreach (var t in triangles)
        {
            if (t.Length != 3) { reason = "faces must be triangles"; return null; }
            if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) continue;
            tris.Add(t);
        }
        var map = new Dictionary<long, int>(3 * triangles.Count + 16, MixedKey.Instance);
        var org = new List<int>();
        foreach (var t in tris)
            for (int c = 0; c < 3; c++)
            {
                long k = Key(t[c], t[(c + 1) % 3]);
                if (map.ContainsKey(k)) { reason = "the mesh is non-manifold or its faces are not consistently oriented (an edge is used twice in the same direction)"; return null; }
                map[k] = org.Count; org.Add(t[c]);
            }
        int nInterior = org.Count;
        var ghostKeys = new List<(int a, int b)>();
        for (int f = 0; f < tris.Count; f++)
        {
            var t = tris[f];
            for (int c = 0; c < 3; c++)
            {
                int a = t[c], b = t[(c + 1) % 3];
                if (!map.ContainsKey(Key(b, a))) { map[Key(b, a)] = org.Count; org.Add(b); ghostKeys.Add((b, a)); }
            }
        }
        int nh = org.Count;
        var next = new int[nh]; var twin = new int[nh]; var origin = org.ToArray(); var face = new int[nh];
        var len = new double[nh]; var vertexHe = Enumerable.Repeat(-1, nv).ToArray(); var faceHe = new int[tris.Count];
        var border = new bool[nv];
        for (int h = 0; h < nh; h++) { next[h] = -1; face[h] = -1; }
        for (int f = 0; f < tris.Count; f++)
        {
            var t = tris[f];
            for (int c = 0; c < 3; c++)
            {
                int a = t[c], b = t[(c + 1) % 3], cc = t[(c + 2) % 3];
                int h = map[Key(a, b)];
                face[h] = f;
                next[h] = map[Key(b, cc)];
                twin[h] = map[Key(b, a)];
                len[h] = (pos[b] - pos[a]).Length;
                if (vertexHe[a] < 0) vertexHe[a] = h;
            }
            faceHe[f] = map[Key(t[0], t[1])];
        }
        foreach (var (a, b) in ghostKeys)
        {
            int h = map[Key(a, b)];
            twin[h] = map[Key(b, a)];
            len[h] = len[twin[h]];
        }

        // a vertex where several surface sheets touch (a "bow tie": edge-manifold but with more than
        // one fan) becomes one vertex per fan, so every vertex has a single fan and angle sum
        var outs = new List<int>[nv];
        for (int h = 0; h < nh; h++) (outs[origin[h]] ??= new List<int>()).Add(h);
        var newPos = new List<Vec3d>(pos);
        var visited = new bool[nh];
        int split = 0;
        for (int v = 0; v < nv; v++)
        {
            if (outs[v] == null) continue;
            bool first = true;
            var starts = outs[v].Where(h => face[h] >= 0 && face[twin[h]] < 0).Concat(outs[v].Where(h => face[h] >= 0));
            foreach (int h0 in starts)
            {
                if (visited[h0]) continue;
                int id = v;
                if (!first) { id = newPos.Count; newPos.Add(pos[v]); split++; }
                first = false;
                int h = h0, guard = outs[v].Count + 1;
                while (guard-- > 0)
                {
                    visited[h] = true; origin[h] = id;
                    if (face[h] < 0) break;
                    h = twin[next[next[h]]];
                    if (h == h0 || visited[h]) break;
                }
            }
            if (outs[v].Any(h => !visited[h])) { reason = "the mesh has a vertex whose faces cannot be ordered around it"; return null; }
        }
        if (split > 0)
        {
            pos = newPos.ToArray();
            nv = pos.Length;
            vertexHe = Enumerable.Repeat(-1, nv).ToArray();
            for (int h = 0; h < nh; h++) if (face[h] >= 0 && vertexHe[origin[h]] < 0) vertexHe[origin[h]] = h;
            border = new bool[nv];
        }
        foreach (var (a, b) in ghostKeys)
        {
            int h = map[Key(a, b)];
            border[origin[h]] = true; border[origin[twin[h]]] = true;
        }
        // border vertices start their fan at the outgoing halfedge whose twin is a ghost
        for (int h = 0; h < nh; h++)
            if (face[h] >= 0 && face[twin[h]] < 0) vertexHe[origin[h]] = h;

        var it = new IntrinsicTriangulation(pos, next, twin, origin, face, len, vertexHe, faceHe, border) { SplitVertices = split };
        var outgoing = new int[nv];
        for (int h = 0; h < nh; h++) outgoing[origin[h]]++;
        for (int v = 0; v < nv; v++)
        {
            if (vertexHe[v] < 0) continue; // unused vertex
            var fan = it.FanOf(v);
            if (fan == null || fan.Count != outgoing[v])
            {
                reason = "the mesh has a non-manifold vertex (two surface sheets meet at one point)";
                return null;
            }
            double sum = 0;
            foreach (int h in fan) if (face[h] >= 0) sum += it.Corner(h);
            if (!(sum > 0)) { reason = "the mesh has degenerate (zero-area) triangles"; return null; }
            it._theta[v] = sum;
            double phi = 0;
            foreach (int h in fan)
            {
                it._sign[h] = phi;
                if (face[h] >= 0) phi += it.Corner(h) * 2 * Math.PI / sum;
            }
        }
        return new IntrinsicTriangulation(it) { SplitVertices = split };
    }

    // ---------------------------------------------------------------- queries

    public int Next(int h) => _next[h];
    public int Prev(int h) => _next[_next[h]];
    public int Twin(int h) => _twin[h];
    public int Origin(int h) => _origin[h];
    public int Head(int h) => _origin[_twin[h]];
    public int Face(int h) => _face[h];
    public double Length(int h) => _len[h];
    public double Signpost(int h) => _sign[h];
    public double AngleSum(int v) => _theta[v];
    public bool IsBorderVertex(int v) => _border[v];
    public bool IsBorderEdge(int h) => _face[h] < 0 || _face[_twin[h]] < 0;
    public int VertexHalfedge(int v) => _vertexHe[v];

    /// <summary>Next outgoing halfedge counter-clockwise around the origin (requires a face on h).</summary>
    public int Ccw(int h) => _twin[Prev(h)];

    /// <summary>Corner angle at the origin of h inside its face (Kahan-stable half-angle formula).</summary>
    public double Corner(int h)
    {
        if (_face[h] < 0) return double.PositiveInfinity;
        return CornerFromLengths(_len[h], _len[Prev(h)], _len[_next[h]]);
    }

    /// <summary>Angle between sides a and b of a triangle whose third side is c.</summary>
    public static double CornerFromLengths(double a, double b, double c)
    {
        double s = 0.5 * (a + b + c);
        double num = (s - a) * (s - b), den = s * (s - c);
        if (num <= 0) return 0.0;
        if (den <= 0) return Math.PI;
        return 2.0 * Math.Atan2(Math.Sqrt(num), Math.Sqrt(den));
    }

    /// <summary>
    /// Outgoing halfedges of v in counter-clockwise order. Interior vertices: one full turn from
    /// the vertex's halfedge. Border vertices: from the halfedge after the outside to the ghost
    /// halfedge that leaves along the border (inclusive). Null when the fan does not close.
    /// </summary>
    public List<int>? FanOf(int v)
    {
        int start = _vertexHe[v];
        if (start < 0) return new List<int>();
        var fan = new List<int>();
        int h = start;
        int guard = _next.Length + 1;
        while (guard-- > 0)
        {
            fan.Add(h);
            if (_face[h] < 0) return fan;             // reached the border on the CCW side
            h = Ccw(h);
            if (h == start) return fan;
        }
        return null;
    }

    // ---------------------------------------------------------------- flips

    /// <summary>Undoes every flip: the triangulation is the input mesh again (signposts included).</summary>
    public void Reset()
    {
        Array.Copy(_inNext, _next, _next.Length); Array.Copy(_inTwin, _twin, _twin.Length);
        Array.Copy(_inOrigin, _origin, _origin.Length); Array.Copy(_inFace, _face, _face.Length);
        Array.Copy(_inLen, _len, _len.Length); Array.Copy(_inSign, _sign, _sign.Length);
        Array.Copy(_inVertexHe, _vertexHe, _vertexHe.Length); Array.Copy(_inFaceHe, _faceHe, _faceHe.Length);
    }

    /// <summary>
    /// Flips the edge of h intrinsically if it is an interior edge whose two triangles form a
    /// strictly convex quadrilateral (and the new edge would not join a vertex to itself).
    /// Afterwards h and its twin are the new edge (h leaves the former opposite vertex of the
    /// twin's triangle). Returns false and changes nothing otherwise.
    /// </summary>
    public bool Flip(int h)
    {
        int t = _twin[h];
        if (_face[h] < 0 || _face[t] < 0) return false;
        int h1 = _next[h], h2 = _next[h1];
        int t1 = _next[t], t2 = _next[t1];
        int i = _origin[h], j = _origin[t], k = _origin[h2], l = _origin[t2];
        if (k == l) return false;
        double angI = Corner(h) + Corner(t1);
        double angJ = Corner(t) + Corner(h1);
        const double convex = Math.PI - 1e-12;
        if (!(angI < convex) || !(angJ < convex)) return false;

        double lik = _len[h2], lil = _len[t1];
        double newLen = Math.Sqrt(Math.Max(0.0, lik * lik + lil * lil - 2 * lik * lil * Math.Cos(angI)));
        if (!(newLen > 0)) return false;

        int f1 = _face[h], f2 = _face[t];
        // face A (k, i, l): h2 k→i, t1 i→l, h l→k
        _next[h2] = t1; _next[t1] = h; _next[h] = h2;
        _face[h2] = f1; _face[t1] = f1; _face[h] = f1;
        // face B (l, j, k): t2 l→j, h1 j→k, t k→l
        _next[t2] = h1; _next[h1] = t; _next[t] = t2;
        _face[t2] = f2; _face[h1] = f2; _face[t] = f2;
        _origin[h] = l; _origin[t] = k;
        _len[h] = newLen; _len[t] = newLen;
        _faceHe[f1] = h; _faceHe[f2] = t;
        if (_vertexHe[i] == h) _vertexHe[i] = t1;
        if (_vertexHe[j] == t) _vertexHe[j] = h1;

        // signposts (Sharp et al. 2019, eq. 3)
        _sign[h] = Wrap(l, _sign[t2] + Corner(t2) * 2 * Math.PI / _theta[l]);
        _sign[t] = Wrap(k, _sign[h2] + Corner(h2) * 2 * Math.PI / _theta[k]);
        FlipCount++;
        return true;
    }

    private double Wrap(int v, double phi)
    {
        if (_border[v]) return phi;
        double tp = 2 * Math.PI;
        phi %= tp;
        if (phi < 0) phi += tp;
        return phi;
    }

    // ---------------------------------------------------------------- tracing

    /// <summary>
    /// Traces intrinsic halfedge h across the input mesh: the polyline from its origin to its
    /// head through every input-edge crossing (each segment lies inside one input face, so the
    /// polyline lies exactly on the input surface). <paramref name="miss"/> is the distance between
    /// where the trace ends and the head vertex (0 up to rounding when the triangulation is sound).
    /// </summary>
    public List<Vec3d> TraceEdge(int h, out double miss)
    {
        int v = _origin[h], w = Head(h);
        var pts = TraceFromVertex(v, _sign[h], _len[h], out var end);
        miss = (end - _pos[w]).Length;
        if (pts.Count > 0) pts[pts.Count - 1] = _pos[w];
        return pts;
    }

    /// <summary>
    /// Straightest walk on the input mesh from vertex v, leaving in signpost direction phi
    /// (rescaled polar angle at v), for the given distance. Returns the points from v to the end,
    /// including every input-edge crossing.
    /// </summary>
    public List<Vec3d> TraceFromVertex(int v, double phi, double distance, out Vec3d end)
    {
        var pts = new List<Vec3d> { _pos[v] };
        double remaining = distance;
        int guard = 4 * _inNext.Length + 100;
        end = _pos[v];
        while (remaining > 0 && guard-- > 0)
        {
            // sector of the input fan that contains phi
            var fan = _inFan[v];
            if (fan.Length == 0) break;
            double tp = 2 * Math.PI;
            if (!_border[v]) { phi %= tp; if (phi < 0) phi += tp; }
            // signposts of the frozen input fan increase from 0 counter-clockwise: take the last
            // sector whose start is not past phi
            int g = -1;
            for (int a = 0; a < fan.Length; a++)
            {
                int ga = fan[a];
                if (_inFace[ga] < 0) break;
                if (_inSign[ga] <= phi + 1e-13) g = ga; else break;
            }
            if (g < 0) break;
            double delta = Math.Max(0.0, phi - _inSign[g]) * _theta[v] / tp;
            if (delta > InCorner(g) + 1e-9) break; // outside the mesh at a border vertex

            double corner = InCorner(g);
            if (corner - delta < 1e-12)
            {
                // along the next fan edge: the halfedge after g counter-clockwise
                int gn = _inTwin[InPrev(g)];
                if (!WalkAlongEdge(gn, ref v, ref phi, ref remaining, pts, out end)) break;
                continue;
            }
            if (delta < 1e-12)
            {
                if (!WalkAlongEdge(g, ref v, ref phi, ref remaining, pts, out end)) break;
                continue;
            }

            // first face: v at the origin, head(g) on +x, the third vertex to the left
            int p = _inOrigin[_inNext[g]], q = _inOrigin[InPrev(g)];
            var V2 = new P2(0, 0);
            var Pp = new P2(_inLen[g], 0);
            var Q2 = Place(V2, Pp, _inLen[InPrev(g)], _inLen[_inNext[g]]);
            var d = new P2(Math.Cos(delta), Math.Sin(delta));
            // exit through p→q
            Intersect(V2, d, Pp, Q2, out double t0, out double s0);
            if (t0 >= remaining)
            {
                end = Map3(V2 + remaining * d, V2, Pp, Q2, _pos[v], _pos[p], _pos[q]);
                pts.Add(end);
                remaining = 0;
                break;
            }
            var X = V2 + t0 * d;
            remaining -= t0;
            if (s0 <= 1e-10 || s0 >= 1 - 1e-10)
            {
                int hitV = s0 <= 1e-10 ? p : q;
                // backward direction at the hit vertex, inside face(g)
                int hu = s0 <= 1e-10 ? _inNext[g] : InPrev(g);            // outgoing from hit vertex in this face
                var U2 = s0 <= 1e-10 ? Pp : Q2;
                var Hd = (s0 <= 1e-10 ? Q2 : V2) - U2;
                end = _pos[hitV]; pts.Add(end);
                if (!ContinueThroughVertex(hitV, hu, Hd, -1.0 * d, ref v, ref phi)) { remaining = 0; break; }
                continue;
            }
            pts.Add(_pos[p] + s0 * (_pos[q] - _pos[p]));
            // cross into the neighbour of edge p→q
            int te = _inTwin[_inNext[g]];
            var U = Q2; var W = Pp; // te = q→p
            var O = X;
            bool done = false;
            while (guard-- > 0)
            {
                if (_inFace[te] < 0) { end = pts[pts.Count - 1]; remaining = 0; done = true; break; } // left the mesh
                int h1 = _inNext[te], h2 = _inNext[h1];
                int iu = _inOrigin[te], iw = _inOrigin[h1], ir = _inOrigin[h2];
                var R = Place(U, W, _inLen[h2], _inLen[h1]);
                double o = Cross(d, R - O);
                double scale = Math.Max((R - O).Len, 1e-300);
                if (Math.Abs(o) <= 1e-12 * scale && Dot(d, R - O) > 0)
                {
                    double tr = (R - O).Len;
                    if (tr >= remaining)
                    {
                        end = Map3(O + remaining * d, U, W, R, _pos[iu], _pos[iw], _pos[ir]);
                        pts.Add(end); remaining = 0; done = true; break;
                    }
                    remaining -= tr;
                    end = _pos[ir]; pts.Add(end);
                    // backward direction at r, inside this face: corner at r is halfedge h2 (r→u)
                    if (!ContinueThroughVertex(ir, h2, U - R, -1.0 * d, ref v, ref phi)) { remaining = 0; }
                    done = true;
                    break;
                }
                int exit; P2 A, B; int ia, ib;
                if (o > 0) { exit = h1; A = W; B = R; ia = iw; ib = ir; }   // through w→r
                else { exit = h2; A = R; B = U; ia = ir; ib = iu; }          // through r→u
                Intersect(O, d, A, B, out double t, out double s);
                if (t >= remaining)
                {
                    end = Map3(O + remaining * d, U, W, R, _pos[iu], _pos[iw], _pos[ir]);
                    pts.Add(end); remaining = 0; done = true; break;
                }
                remaining -= t;
                O = O + t * d;
                if (s <= 1e-10 || s >= 1 - 1e-10)
                {
                    bool atA = s <= 1e-10;
                    int hitV = atA ? ia : ib;
                    end = _pos[hitV]; pts.Add(end);
                    // outgoing halfedge from the hit vertex inside this face, and the 2D direction of it
                    int hu; P2 Hp, Hn;
                    if (hitV == iu) { hu = te; Hp = U; Hn = W; }
                    else if (hitV == iw) { hu = h1; Hp = W; Hn = R; }
                    else { hu = h2; Hp = R; Hn = U; }
                    if (!ContinueThroughVertex(hitV, hu, Hn - Hp, -1.0 * d, ref v, ref phi)) remaining = 0;
                    done = true;
                    break;
                }
                pts.Add(_pos[ia] + s * (_pos[ib] - _pos[ia]));
                // next face across the exit edge: twin(exit) runs B→A
                te = _inTwin[exit];
                U = B; W = A;
            }
            if (done && remaining <= 0) break;
            if (!done) break;
        }
        if (pts.Count > 0) end = pts[pts.Count - 1];
        return pts;
    }

    /// <summary>Moves along input halfedge g; ends on it or continues straight through its head.</summary>
    private bool WalkAlongEdge(int g, ref int v, ref double phi, ref double remaining, List<Vec3d> pts, out Vec3d end)
    {
        int w = _inOrigin[_inTwin[g]];
        double L = _inLen[g];
        if (remaining <= L * (1 + 1e-12))
        {
            double t = Math.Min(1.0, remaining / L);
            end = _pos[v] + t * (_pos[w] - _pos[v]);
            pts.Add(end);
            remaining = 0;
            return false;
        }
        remaining -= L;
        end = _pos[w];
        pts.Add(end);
        // straightest continuation at w, measured from the way back (w→v)
        int back = _inTwin[g];
        double phiBack = _inSign[back];
        return Continue(w, phiBack, ref v, ref phi);
    }

    /// <summary>
    /// The trace reached input vertex u inside a face; hu is u's outgoing halfedge in that face and
    /// hDir its 2D direction, back the 2D direction pointing back along the trace.
    /// </summary>
    private bool ContinueThroughVertex(int u, int hu, P2 hDir, P2 back, ref int v, ref double phi)
    {
        double ang = Math.Atan2(Cross(hDir, back), Dot(hDir, back));
        if (ang < 0) ang = 0;
        double phiBack = _inSign[hu] + ang * 2 * Math.PI / _theta[u];
        return Continue(u, phiBack, ref v, ref phi);
    }

    private bool Continue(int u, double phiBack, ref int v, ref double phi)
    {
        double tp = 2 * Math.PI;
        if (!_border[u])
        {
            v = u; phi = phiBack + Math.PI; // half the total angle on either side (Polthier–Schmies)
            return true;
        }
        // border vertex: actual angles in [0, Θ]; go straight on the inside if possible
        double back = phiBack * _theta[u] / tp;
        double a1 = back + Math.PI, a2 = back - Math.PI;
        double tol = 1e-9;
        double pick;
        if (a1 <= _theta[u] + tol) pick = Math.Min(a1, _theta[u]);
        else if (a2 >= -tol) pick = Math.Max(a2, 0.0);
        else return false;
        v = u; phi = pick * tp / _theta[u];
        return true;
    }

    private int InPrev(int h) => _inNext[_inNext[h]];

    private double InCorner(int h) =>
        _inFace[h] < 0 ? double.PositiveInfinity : CornerFromLengths(_inLen[h], _inLen[InPrev(h)], _inLen[_inNext[h]]);

    // ---------------------------------------------------------------- 2D helpers

    private readonly struct P2
    {
        public readonly double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(double s, P2 a) => new P2(s * a.X, s * a.Y);
        public double Len => Math.Sqrt(X * X + Y * Y);
    }

    private static double Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;
    private static double Dot(P2 a, P2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Third vertex left of U→W with |UR| = lu and |WR| = lw.</summary>
    private static P2 Place(P2 U, P2 W, double lu, double lw)
    {
        var e = W - U;
        double L = e.Len;
        var ex = (1.0 / L) * e;
        var ey = new P2(-ex.Y, ex.X);
        double a = (lu * lu - lw * lw + L * L) / (2 * L);
        double b = Math.Sqrt(Math.Max(0.0, lu * lu - a * a));
        return U + a * ex + b * ey;
    }

    /// <summary>Ray O + t d against segment A→B: distance t along d and parameter s on the segment.</summary>
    private static void Intersect(P2 O, P2 d, P2 A, P2 B, out double t, out double s)
    {
        var e = B - A;
        double den = Cross(d, e);
        var ao = A - O;
        if (Math.Abs(den) < 1e-300) { t = double.PositiveInfinity; s = 0; return; }
        t = Cross(ao, e) / den;
        s = Cross(ao, d) / den;
        if (s < 0) s = 0; else if (s > 1) s = 1;
        if (t < 0) t = 0;
    }

    private static Vec3d Map3(P2 x, P2 a, P2 b, P2 c, Vec3d A, Vec3d B, Vec3d C)
    {
        double den = Cross(b - a, c - a);
        if (Math.Abs(den) < 1e-300) return A;
        double wb = Cross(x - a, c - a) / den;
        double wc = Cross(b - a, x - a) / den;
        double wa = 1 - wb - wc;
        return wa * A + wb * B + wc * C;
    }
}
