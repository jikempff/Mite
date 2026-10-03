// Refinement of AAG webs after Schling, Wang, Hoyer & Pottmann 2022,
// "Designing asymptotic geodesic hybrid gridshells" (Computer-Aided Design
// 152, 103378), §3.4 "Refinement through subdivision" (Fig. 7 B–C): bilinear
// subdivision of the web's quads, then the refined mesh is optimised so that
// the original polylines, now refined, stay discrete asymptotic (planar
// vertex stars, eq. 6) or discrete geodesic (eq. 13), with the vertex normal
// defined as a discrete normal of the other mesh polyline through the vertex,
// (v⁻ − v⁺) · n = 0. Solver: the regularised Gauss–Newton "guided
// projection" of Tang, Sun, Gomes, Wallner & Pottmann 2014 ("Form-finding
// with polyhedral meshes made simple", ACM TOG 33(4)), with proximity to the
// reference surface through its tangent plane at the closest point.
using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Smooth laths for an AAG web. <see cref="AagWeb.Optimize"/> returns the
/// joints of the web; its laths are straight chords from joint to joint, so
/// an asymptotic lath of a coarse web kinks by the full turning of one cell
/// at every joint. This step subdivides every quad bilinearly (2, 4 or 8
/// pieces per edge) and re-optimises the fine mesh so that the refined
/// polylines of the two asymptotic families keep planar vertex stars
/// (osculating plane = tangent plane: upright strips) and the refined
/// geodesic diagonals keep the normal in their osculating plane (flat
/// strips), while the joints stay where the AAG solve put them and the fine
/// vertices stay on the surface the joints describe.
/// <para>
/// Only the original polylines carry the asymptotic / geodesic conditions;
/// the new grid lines inside each quad only define the fine surface (its
/// normals) and are kept fair. The surface the joints describe is the
/// reference surface shifted by the joints' own distances to it, linearly
/// interpolated inside each quad — so a joint that the AAG solve moved off
/// the reference is not pulled back, and the fine vertices next to it follow.
/// </para>
/// </summary>
public static class AagRefinement
{
    public class Options
    {
        /// <summary>Subdivision levels: every web edge becomes 2^Levels pieces (Schling et al. use 2; 1 is four times cheaper).</summary>
        public int Levels { get; set; } = 1;

        /// <summary>Gauss–Newton iterations.</summary>
        public int Iterations { get; set; } = 12;

        /// <summary>Weight of the proximity to the surface the joints describe.</summary>
        public double Proximity { get; set; } = 0.3;

        /// <summary>Weight that keeps the joints at their AAG positions (in units of the web spacing).</summary>
        public double JointWeight { get; set; } = 3.0;

        /// <summary>
        /// Fairness weight during the first phase: second differences along the
        /// fine grid lines inside the quads, and eight times this weight on
        /// third differences along the laths (zero on circular arcs, so the
        /// laths are not pulled toward their chords). The asymptotic and
        /// geodesic conditions do not forbid a zigzag inside the tangent plane;
        /// fairness is what keeps the fine laths smooth.
        /// </summary>
        public double Fairness { get; set; } = 0.2;

        /// <summary>Fairness in the second half of the iterations, as a fraction of <see cref="Fairness"/>.</summary>
        public double FinalFairness { get; set; } = 0.01;

        /// <summary>Levenberg damping.</summary>
        public double Damping { get; set; } = 1e-3;

        public Func<bool>? ShouldCancel { get; set; }
    }

    public sealed class Result
    {
        /// <summary>Refined laths of the two asymptotic families and the geodesic diagonals (same order as the AAG result).</summary>
        public List<Vec3d[]> A { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> B { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> G { get; } = new List<Vec3d[]>();
        /// <summary>Unit vertex normals along each refined lath (parallel to <see cref="A"/>, <see cref="B"/>, <see cref="G"/>).</summary>
        public List<Vec3d[]> NormalsA { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> NormalsB { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> NormalsG { get; } = new List<Vec3d[]>();
        /// <summary>The refined quad mesh (the web's surface at the fine resolution).</summary>
        public Vec3d[] Vertices { get; set; } = Array.Empty<Vec3d>();
        public List<int[]> Quads { get; } = new List<int[]>();
        /// <summary>Pieces per web edge (2^Levels).</summary>
        public int Subdivisions { get; set; }
        /// <summary>
        /// Largest angle (degrees) between an edge at a vertex of a refined
        /// asymptotic lath (its two lath neighbours and the cross direction of
        /// the fine mesh) and the vertex's tangent plane — 0 for an exact
        /// discrete asymptotic polyline on the fine mesh. Initial: after the
        /// bilinear subdivision and the projection, before the optimisation.
        /// </summary>
        public double StarError { get; set; }
        public double InitialStarError { get; set; }
        /// <summary>Largest angle (degrees) between a refined geodesic diagonal's osculating plane and the vertex normal (as <see cref="AagWeb.Result.GeodesicError"/>).</summary>
        public double GeodesicError { get; set; }
        public double InitialGeodesicError { get; set; }
        /// <summary>Largest |normal curvature| × web spacing along the refined asymptotic laths (0 = asymptotic).</summary>
        public double MaxNormalCurvature { get; set; }
        /// <summary>Largest |geodesic curvature| × web spacing along the refined geodesic laths (0 = geodesic).</summary>
        public double MaxGeodesicCurvature { get; set; }
        /// <summary>Largest turning angle (degrees) between consecutive segments of any lath: before (chords of the AAG result) and after.</summary>
        public double InitialMaxTurning { get; set; }
        public double MaxTurning { get; set; }
        /// <summary>Largest distance a joint moved during the refinement.</summary>
        public double JointShift { get; set; }
        /// <summary>Largest and mean distance from the fine vertices to the reference surface.</summary>
        public double MaxDeviation { get; set; }
        public double MeanDeviation { get; set; }
        public int Iterations { get; set; }
    }

    /// <summary>Refines the result of <see cref="AagWeb.Optimize"/> on the same web.</summary>
    public static Result Refine(MeshData reference, AsymptoticWeb.Result web, AagWeb.Result aag, Options? options = null) =>
        Refine(reference, web, aag.Nodes, aag.Normals, aag.GeodesicFamily, options);

    /// <summary>
    /// Refines a web given its joint positions and normals (the AAG result, or
    /// the web's own nodes for a plain A-net). <paramref name="geodesicFamily"/>
    /// is the diagonal family kept geodesic (0 or 1), or −1 for none.
    /// </summary>
    public static Result Refine(MeshData reference, AsymptoticWeb.Result web, IReadOnlyList<Vec3d> joints, IReadOnlyList<Vec3d>? jointNormals,
        int geodesicFamily, Options? options = null)
    {
        options ??= new Options();
        var res = new Result();
        int N = web.Nodes.Count;
        if (N == 0 || joints.Count != N) return res;
        int levels = Math.Max(1, Math.Min(4, options.Levels));
        int m = 1 << levels;
        res.Subdivisions = m;
        var proj = new MeshProjection(reference);
        double h = web.Spacing > 0 ? web.Spacing : 1.0;
        double inv = 1.0 / h;

        // ---- fine vertices ------------------------------------------------
        var pos = new List<Vec3d>(joints);             // bilinear positions (world units)
        var edgeIds = new Dictionary<(int, int), int[]>(); // (lo, hi) → ids from lo to hi
        int[] Edge(int a, int b)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edgeIds.TryGetValue(key, out var ids))
            {
                ids = new int[m + 1];
                ids[0] = key.Item1; ids[m] = key.Item2;
                for (int k = 1; k < m; k++)
                {
                    double s = k / (double)m;
                    ids[k] = pos.Count;
                    pos.Add((1 - s) * joints[key.Item1] + s * joints[key.Item2]);
                }
                edgeIds[key] = ids;
            }
            if (a == key.Item1) return ids;
            var rev = new int[m + 1];
            for (int k = 0; k <= m; k++) rev[k] = ids[m - k];
            return rev;
        }

        var quads = web.Quads.Where(q => q.Length == 4 && q.Distinct().Count() == 4).ToList();
        var grids = new int[quads.Count][,];
        for (int qi = 0; qi < quads.Count; qi++)
        {
            var q = quads[qi];
            var g = new int[m + 1, m + 1];
            // corners (0,0)=q0, (m,0)=q1, (m,m)=q2, (0,m)=q3
            var e01 = Edge(q[0], q[1]); var e12 = Edge(q[1], q[2]); var e32 = Edge(q[3], q[2]); var e03 = Edge(q[0], q[3]);
            for (int k = 0; k <= m; k++) { g[k, 0] = e01[k]; g[m, k] = e12[k]; g[k, m] = e32[k]; g[0, k] = e03[k]; }
            for (int s = 1; s < m; s++)
                for (int t = 1; t < m; t++)
                {
                    double a = s / (double)m, b = t / (double)m;
                    g[s, t] = pos.Count;
                    pos.Add((1 - a) * (1 - b) * joints[q[0]] + a * (1 - b) * joints[q[1]] + a * b * joints[q[2]] + (1 - a) * b * joints[q[3]]);
                }
            grids[qi] = g;
        }

        // ---- refined laths ------------------------------------------------
        var chainsA = AagWeb.Chains(web.NeighborsA, web.Nodes);
        var chainsB = AagWeb.Chains(web.NeighborsB, web.Nodes);
        var diag = new Dictionary<(int, int), (int Quad, bool Main)>();
        for (int qi = 0; qi < quads.Count; qi++)
        {
            var q = quads[qi];
            diag[(Math.Min(q[0], q[2]), Math.Max(q[0], q[2]))] = (qi, true);
            diag[(Math.Min(q[1], q[3]), Math.Max(q[1], q[3]))] = (qi, false);
        }
        int[] DiagonalIds(int a, int b)
        {
            var ids = new int[m + 1];
            if (!diag.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var d))
            {
                // no quad: a straight fine diagonal (does not happen on a web)
                ids[0] = a; ids[m] = b;
                for (int k = 1; k < m; k++) { ids[k] = pos.Count; pos.Add(joints[a] + k / (double)m * (joints[b] - joints[a])); }
                return ids;
            }
            var g = grids[d.Quad]; var q = quads[d.Quad];
            for (int k = 0; k <= m; k++) ids[k] = d.Main ? g[k, k] : g[m - k, k];
            int start = d.Main ? q[0] : q[1];
            if (start != a) Array.Reverse(ids);
            return ids;
        }
        List<int> Refined(int[] chain, bool diagonal)
        {
            var o = new List<int> { chain[0] };
            for (int k = 0; k + 1 < chain.Length; k++)
            {
                var ids = diagonal ? DiagonalIds(chain[k], chain[k + 1]) : Edge(chain[k], chain[k + 1]);
                for (int j = 1; j <= m; j++) o.Add(ids[j]);
            }
            return o;
        }
        var fineA = chainsA.Select(c => Refined(c, false)).ToList();
        var fineB = chainsB.Select(c => Refined(c, false)).ToList();
        var chainsG = new List<int[]>();
        if (geodesicFamily == 0 || geodesicFamily == 1)
            for (int i = 0; i < web.DiagonalNodes.Count; i++)
                if (web.DiagonalFamily[i] == geodesicFamily && web.DiagonalNodes[i].Length >= 2) chainsG.Add(web.DiagonalNodes[i]);
        var fineG = chainsG.Select(c => Refined(c, true)).ToList();
        int V = pos.Count;

        // ---- fine grid lines: triples for fairness and the cross directions ----
        var fair = new HashSet<(int, int, int)>();
        void Fair(int a, int v, int c) { if (a != c) fair.Add(a < c ? (a, v, c) : (c, v, a)); }
        // per fine vertex: the fine grid lines through it (as (a, c) neighbour pairs)
        var lines = new List<(int A, int C)>[V];
        for (int i = 0; i < V; i++) lines[i] = new List<(int, int)>(2);
        // edge points: their inner neighbours on each side
        var inner = new Dictionary<int, List<int>>();
        for (int qi = 0; qi < quads.Count; qi++)
        {
            var g = grids[qi];
            for (int t = 0; t <= m; t++)
                for (int s = 1; s < m; s++) Fair(g[s - 1, t], g[s, t], g[s + 1, t]);
            for (int s = 0; s <= m; s++)
                for (int t = 1; t < m; t++) Fair(g[s, t - 1], g[s, t], g[s, t + 1]);
            for (int s = 1; s < m; s++)
                for (int t = 1; t < m; t++)
                {
                    lines[g[s, t]].Add((g[s - 1, t], g[s + 1, t]));
                    lines[g[s, t]].Add((g[s, t - 1], g[s, t + 1]));
                }
            for (int k = 1; k < m; k++)
            {
                AddInner(g[k, 0], g[k, 1]); AddInner(g[k, m], g[k, m - 1]);
                AddInner(g[0, k], g[1, k]); AddInner(g[m, k], g[m - 1, k]);
            }
        }
        void AddInner(int edgePoint, int neighbour)
        {
            if (!inner.TryGetValue(edgePoint, out var l)) inner[edgePoint] = l = new List<int>(2);
            if (!l.Contains(neighbour)) l.Add(neighbour);
        }
        foreach (var kv in inner)
            if (kv.Value.Count == 2) Fair(kv.Value[0], kv.Key, kv.Value[1]);
        // along the laths themselves: third differences, which vanish on circular
        // arcs — second differences would pull every lath toward its chords
        // (measured on the catenoid: 0.0014 off the exact asymptotic curve
        // against 0.0001 with third differences, h = 0.16)
        var fair3List = new List<(int, int, int, int)>();
        foreach (var ch in fineA.Concat(fineB).Concat(fineG))
        {
            for (int k = 1; k + 2 < ch.Count; k++) fair3List.Add((ch[k - 1], ch[k], ch[k + 1], ch[k + 2]));
            for (int k = 1; k + 1 < ch.Count; k++) fair.Remove(ch[k - 1] < ch[k + 1] ? (ch[k - 1], ch[k], ch[k + 1]) : (ch[k + 1], ch[k], ch[k - 1]));
        }
        var fairList = fair.ToList();
        const double lathFairness = 8.0;

        // ---- which vertices carry a normal, and their conditions -------------
        bool regularJoint(int i) => web.NeighborsA[i].Length <= 2 && web.NeighborsB[i].Length <= 2 && web.NeighborsA[i].Length + web.NeighborsB[i].Length >= 3;
        var nIndex = new int[V];
        for (int i = 0; i < V; i++) nIndex[i] = -1;
        int nn = 0;
        void Carry(int v) { if (nIndex[v] < 0) nIndex[v] = nn++; }
        foreach (var ch in fineA.Concat(fineB).Concat(fineG)) foreach (int v in ch) Carry(v);

        // star rows: vertex v, neighbour u — n_v · (u − v) = 0
        var starRows = new HashSet<(int V, int U)>();
        // cross rows: n_v · (c − a) = 0 (a two-sided fine grid line through v)
        var crossRows = new HashSet<(int V, int A, int C)>();
        void Lath(List<int> ch)
        {
            for (int k = 0; k < ch.Count; k++)
            {
                int v = ch[k];
                if (v < N)
                {
                    // a joint: the A-net star of the AAG web, now with fine neighbours
                    if (!regularJoint(v))
                    {
                        // border corner or singular seed: only a lath that runs through it
                        if (k == 0 || k + 1 == ch.Count) continue;
                    }
                    if (k > 0) starRows.Add((v, ch[k - 1]));
                    if (k + 1 < ch.Count) starRows.Add((v, ch[k + 1]));
                    continue;
                }
                if (k > 0) starRows.Add((v, ch[k - 1]));
                if (k + 1 < ch.Count) starRows.Add((v, ch[k + 1]));
                // the cross direction of the fine mesh at an edge point
                if (inner.TryGetValue(v, out var l))
                {
                    if (l.Count == 2) crossRows.Add((v, l[0], l[1]));
                    else if (l.Count == 1) starRows.Add((v, l[0]));
                }
            }
        }
        foreach (var ch in fineA) Lath(ch);
        foreach (var ch in fineB) Lath(ch);
        var geoTriples = new List<(int A, int V, int C)>();
        foreach (var ch in fineG)
            for (int k = 1; k + 1 < ch.Count; k++)
            {
                int v = ch[k];
                if (v < N && !regularJoint(v)) continue;
                if (ch[k - 1] == ch[k + 1]) continue;
                geoTriples.Add((ch[k - 1], v, ch[k + 1]));
                // inside a quad the normal is the fine mesh normal: both grid lines through v
                if (v >= N) foreach (var (a, c) in lines[v]) crossRows.Add((v, a, c));
            }
        // joints that carry no star condition keep their AAG normal softly (gauge)
        var softNormal = new List<int>();
        for (int i = 0; i < N; i++) if (nIndex[i] >= 0 && !regularJoint(i)) softNormal.Add(i);

        // ---- the surface the joints describe: reference + interpolated offsets ----
        var offset = new double[V];
        var hint = new int[V];
        {
            var jointOffset = new double[N];
            for (int i = 0; i < N; i++)
            {
                var hit = proj.ClosestPoint(joints[i], proj.NearestVertexGlobal(joints[i]));
                jointOffset[i] = Vec3d.Dot(joints[i] - hit.Point, hit.SmoothNormal.Normalized());
                hint[i] = hit.NearestVertex;
            }
            foreach (var kv in edgeIds)
            {
                var ids = kv.Value;
                for (int k = 1; k < m; k++) { double s = k / (double)m; offset[ids[k]] = (1 - s) * jointOffset[kv.Key.Item1] + s * jointOffset[kv.Key.Item2]; }
            }
            for (int qi = 0; qi < quads.Count; qi++)
            {
                var g = grids[qi]; var q = quads[qi];
                for (int s = 1; s < m; s++)
                    for (int t = 1; t < m; t++)
                    {
                        double a = s / (double)m, b = t / (double)m;
                        offset[g[s, t]] = (1 - a) * (1 - b) * jointOffset[q[0]] + a * (1 - b) * jointOffset[q[1]] + a * b * jointOffset[q[2]] + (1 - a) * b * jointOffset[q[3]];
                    }
            }
            for (int i = 0; i < N; i++) offset[i] = jointOffset[i];
            for (int i = N; i < V; i++) hint[i] = proj.NearestVertexGlobal(pos[i]);
        }

        // ---- unknowns (units of h): positions 3V, normals 3nn ---------------
        int nv = 3 * V + 3 * nn;
        var x = new double[nv];
        var refNormal = new Vec3d[V];
        for (int i = 0; i < V; i++)
        {
            var hit = proj.ClosestPoint(pos[i], hint[i]);
            hint[i] = hit.NearestVertex;
            var np = hit.SmoothNormal.Normalized();
            refNormal[i] = np;
            // fine vertices start on the joints' surface, joints where they are
            var p = i < N ? joints[i] : hit.Point + offset[i] * np;
            x[3 * i] = p.X * inv; x[3 * i + 1] = p.Y * inv; x[3 * i + 2] = p.Z * inv;
        }
        for (int i = 0; i < V; i++)
        {
            if (nIndex[i] < 0) continue;
            // joints keep their AAG normal, fine vertices start from the reference normal
            var n = i < N && jointNormals != null && jointNormals.Count == N && jointNormals[i].LengthSquared > 1e-20
                ? jointNormals[i].Normalized() : refNormal[i];
            int o = 3 * V + 3 * nIndex[i];
            x[o] = n.X; x[o + 1] = n.Y; x[o + 2] = n.Z;
        }
        var jointStart = new Vec3d[N];
        for (int i = 0; i < N; i++) jointStart[i] = joints[i] * inv;
        var jointN0 = new Vec3d[N];
        for (int i = 0; i < N; i++) { int o = nIndex[i] >= 0 ? 3 * V + 3 * nIndex[i] : -1; jointN0[i] = o >= 0 ? new Vec3d(x[o], x[o + 1], x[o + 2]) : Vec3d.Zero; }

        Vec3d P(int i) => new Vec3d(x[3 * i], x[3 * i + 1], x[3 * i + 2]);
        int NO(int i) => 3 * V + 3 * nIndex[i];
        Vec3d Nr(int i) { int o = NO(i); return new Vec3d(x[o], x[o + 1], x[o + 2]); }

        var isGeo = new bool[V];
        foreach (var (_, v, _) in geoTriples) if (v >= N) isGeo[v] = true;
        bool IsGeodesicVertex(int v) => isGeo[v];

        res.InitialMaxTurning = MaxTurning(chainsA.Concat(chainsB).Concat(chainsG).Select(c => c.Select(i => joints[i]).ToArray()));
        (res.InitialStarError, res.InitialGeodesicError) = Errors();

        (double star, double geo) Errors()
        {
            double s = 0, g = 0;
            foreach (var (v, u) in starRows)
            {
                var e = P(u) - P(v); double l = e.Length;
                if (l > 1e-14) s = Math.Max(s, Math.Abs(Vec3d.Dot(Nr(v).Normalized(), e)) / l);
            }
            foreach (var (v, a, c) in crossRows)
            {
                if (IsGeodesicVertex(v)) continue;
                var e = P(c) - P(a); double l = e.Length;
                if (l > 1e-14) s = Math.Max(s, Math.Abs(Vec3d.Dot(Nr(v).Normalized(), e)) / l);
            }
            foreach (var (a, v, c) in geoTriples)
            {
                var mm = Vec3d.Cross(P(a) - P(v), P(c) - P(v)); double l = mm.Length;
                if (l > 1e-14) g = Math.Max(g, Math.Abs(Vec3d.Dot(Nr(v).Normalized(), mm)) / l);
            }
            return (Math.Asin(Math.Min(1, s)) * 180 / Math.PI, Math.Asin(Math.Min(1, g)) * 180 / Math.PI);
        }


        double fineScale = m; // second differences read in units of the fine spacing h/m

        AagWeb.NormalEquations Assemble(double wf)
        {
            var sys = new AagWeb.NormalEquations(nv);
            // star rows, normalised by the (frozen) edge length so they read as angles
            foreach (var (v, u) in starRows)
            {
                var e = P(u) - P(v); var n = Nr(v); int o = NO(v);
                double sc = 1.0 / Math.Max(1e-12, e.Length);
                sys.Row(1.0, Vec3d.Dot(n, e) * sc,
                    (o, e.X * sc), (o + 1, e.Y * sc), (o + 2, e.Z * sc),
                    (3 * u, n.X * sc), (3 * u + 1, n.Y * sc), (3 * u + 2, n.Z * sc),
                    (3 * v, -n.X * sc), (3 * v + 1, -n.Y * sc), (3 * v + 2, -n.Z * sc));
            }
            foreach (var (v, a, c) in crossRows)
            {
                var e = P(c) - P(a); var n = Nr(v); int o = NO(v);
                double sc = 1.0 / Math.Max(1e-12, e.Length);
                sys.Row(1.0, Vec3d.Dot(n, e) * sc,
                    (o, e.X * sc), (o + 1, e.Y * sc), (o + 2, e.Z * sc),
                    (3 * c, n.X * sc), (3 * c + 1, n.Y * sc), (3 * c + 2, n.Z * sc),
                    (3 * a, -n.X * sc), (3 * a + 1, -n.Y * sc), (3 * a + 2, -n.Z * sc));
            }
            // unit normals
            for (int i = 0; i < V; i++)
            {
                if (nIndex[i] < 0) continue;
                var n = Nr(i); int o = NO(i);
                sys.Row(1.0, Vec3d.Dot(n, n) - 1, (o, 2 * n.X), (o + 1, 2 * n.Y), (o + 2, 2 * n.Z));
            }
            foreach (int i in softNormal)
            {
                var d = Nr(i) - jointN0[i]; int o = NO(i);
                for (int k = 0; k < 3; k++) sys.Row(0.1, Comp(d, k), (o + k, 1.0));
            }
            // geodesic diagonals: n · ((va − v) × (vc − v)) = 0, normalised by |va − v||vc − v|
            foreach (var (a, v, c) in geoTriples)
            {
                Vec3d p = P(a) - P(v), q = P(c) - P(v), n = Nr(v); int o = NO(v);
                double sc = 1.0 / Math.Max(1e-12, p.Length * q.Length);
                var gn = Vec3d.Cross(p, q) * sc;
                var ga = Vec3d.Cross(q, n) * sc;
                var gc = Vec3d.Cross(n, p) * sc;
                var gv = -1.0 * (ga + gc);
                sys.Row(1.0, Vec3d.Dot(n, Vec3d.Cross(p, q)) * sc,
                    (o, gn.X), (o + 1, gn.Y), (o + 2, gn.Z),
                    (3 * a, ga.X), (3 * a + 1, ga.Y), (3 * a + 2, ga.Z),
                    (3 * c, gc.X), (3 * c + 1, gc.Y), (3 * c + 2, gc.Z),
                    (3 * v, gv.X), (3 * v + 1, gv.Y), (3 * v + 2, gv.Z));
            }
            // proximity to the joints' surface through the reference tangent plane
            for (int i = 0; i < V; i++)
            {
                var pw = P(i) * h;
                var hit = proj.ClosestPoint(pw, hint[i]);
                hint[i] = hit.NearestVertex;
                var np = hit.SmoothNormal.Normalized();
                if (options.Proximity > 0)
                    sys.Row(options.Proximity, Vec3d.Dot(P(i) - hit.Point * inv, np) - offset[i] * inv, (3 * i, np.X), (3 * i + 1, np.Y), (3 * i + 2, np.Z));
                if (i < N && options.JointWeight > 0)
                {
                    var d = P(i) - jointStart[i];
                    for (int k = 0; k < 3; k++) sys.Row(options.JointWeight, Comp(d, k), (3 * i + k, 1.0));
                }
            }
            if (wf > 0)
            {
                double w = wf * fineScale;
                foreach (var (a, v, c) in fairList)
                {
                    var d = P(a) - 2 * P(v) + P(c);
                    for (int k = 0; k < 3; k++) sys.Row(w, Comp(d, k), (3 * a + k, 1.0), (3 * v + k, -2.0), (3 * c + k, 1.0));
                }
                foreach (var (a, b, c, e) in fair3List)
                {
                    var d = P(a) - 3 * P(b) + 3 * P(c) - P(e);
                    for (int k = 0; k < 3; k++) sys.Row(w * lathFairness, Comp(d, k), (3 * a + k, 1.0), (3 * b + k, -3.0), (3 * c + k, 3.0), (3 * e + k, -1.0));
                }
            }
            return sys;
        }

        // two phases as in Schling et al. (§3.3): fair first, then the
        // constraints with a fraction of the fairness; a phase ends after half
        // the iterations or when its steps stall
        int it;
        double lambda = options.Damping, prevCost = double.MaxValue;
        var prevX = (double[])x.Clone();
        AagWeb.NormalEquations? prevSys = null;
        double finalWf = options.FinalFairness * options.Fairness;
        int iters = Math.Max(1, options.Iterations);
        bool final = false;
        int rejected = 0;
        for (it = 0; it < iters; it++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            if (!final && it >= iters / 2) { final = true; prevCost = double.MaxValue; prevSys = null; rejected = 0; }
            double wf = final ? finalWf : options.Fairness;
            var sys = Assemble(wf);
            if (sys.Cost > prevCost && prevSys != null)
            {
                Array.Copy(prevX, x, nv);
                lambda = Math.Min(lambda * 10, 1e6);
                sys = prevSys;
                rejected++;
            }
            else
            {
                prevCost = sys.Cost; prevSys = sys;
                Array.Copy(x, prevX, nv);
                lambda = Math.Max(lambda / 3, 1e-7);
                rejected = 0;
            }
            var dx = sys.Solve(lambda);
            double step = 0;
            if (dx != null)
                for (int k = 0; k < nv; k++) { x[k] += dx[k]; step = Math.Max(step, Math.Abs(dx[k])); }
            if (dx == null || step < 1e-11 || rejected >= 4)
            {
                Array.Copy(prevX, x, nv);
                if (final) { it++; break; }
                final = true; prevCost = double.MaxValue; prevSys = null; rejected = 0;
            }
        }
        if (prevSys != null && Assemble(finalWf).Cost > prevCost) Array.Copy(prevX, x, nv);
        res.Iterations = it;
        (res.StarError, res.GeodesicError) = Errors();

        // ---- output ---------------------------------------------------------
        var fine = new Vec3d[V];
        for (int i = 0; i < V; i++) fine[i] = P(i) * h;
        res.Vertices = fine;
        foreach (var g in grids)
            for (int s = 0; s < m; s++)
                for (int t = 0; t < m; t++) res.Quads.Add(new[] { g[s, t], g[s + 1, t], g[s + 1, t + 1], g[s, t + 1] });
        Vec3d Normal(int i) => Nr(i).Normalized();
        foreach (var ch in fineA) { res.A.Add(ch.Select(i => fine[i]).ToArray()); res.NormalsA.Add(ch.Select(Normal).ToArray()); }
        foreach (var ch in fineB) { res.B.Add(ch.Select(i => fine[i]).ToArray()); res.NormalsB.Add(ch.Select(Normal).ToArray()); }
        foreach (var ch in fineG) { res.G.Add(ch.Select(i => fine[i]).ToArray()); res.NormalsG.Add(ch.Select(Normal).ToArray()); }
        res.MaxTurning = MaxTurning(res.A.Concat(res.B).Concat(res.G));
        double kn = 0, kg = 0;
        for (int l = 0; l < res.A.Count; l++) kn = Math.Max(kn, MaxCurvature(res.A[l], res.NormalsA[l], normalPart: true));
        for (int l = 0; l < res.B.Count; l++) kn = Math.Max(kn, MaxCurvature(res.B[l], res.NormalsB[l], normalPart: true));
        for (int l = 0; l < res.G.Count; l++) kg = Math.Max(kg, MaxCurvature(res.G[l], res.NormalsG[l], normalPart: false));
        res.MaxNormalCurvature = kn * h;
        res.MaxGeodesicCurvature = kg * h;
        double js = 0;
        for (int i = 0; i < N; i++) js = Math.Max(js, (fine[i] - joints[i]).Length);
        res.JointShift = js;
        double maxDev = 0, sumDev = 0;
        for (int i = 0; i < V; i++)
        {
            var hit = proj.ClosestPoint(fine[i], hint[i]);
            double d = (hit.Point - fine[i]).Length;
            maxDev = Math.Max(maxDev, d); sumDev += d;
        }
        res.MaxDeviation = maxDev; res.MeanDeviation = V > 0 ? sumDev / V : 0;
        return res;
    }

    /// <summary>Largest turning angle (degrees) between consecutive segments over a set of polylines.</summary>
    public static double MaxTurning(IEnumerable<Vec3d[]> polylines)
    {
        double worst = 0;
        foreach (var l in polylines)
            for (int k = 1; k + 1 < l.Length; k++)
            {
                var a = l[k] - l[k - 1]; var b = l[k + 1] - l[k];
                if (a.LengthSquared < 1e-24 || b.LengthSquared < 1e-24) continue;
                double c = Vec3d.Dot(a.Normalized(), b.Normalized());
                worst = Math.Max(worst, Math.Acos(Math.Max(-1, Math.Min(1, c))) * 180 / Math.PI);
            }
        return worst;
    }

    /// <summary>
    /// Largest normal (or geodesic) curvature of a polyline with vertex
    /// normals: the turning vector (t₊ − t₋) / ½(l₋ + l₊) split along the
    /// normal (normal part) or along n × t (geodesic part).
    /// </summary>
    public static double MaxCurvature(Vec3d[] l, Vec3d[] normals, bool normalPart)
    {
        double worst = 0;
        for (int k = 1; k + 1 < l.Length; k++)
        {
            var a = l[k] - l[k - 1]; var b = l[k + 1] - l[k];
            double la = a.Length, lb = b.Length;
            if (la < 1e-14 || lb < 1e-14) continue;
            var turn = (b / lb - a / la) / (0.5 * (la + lb));
            var n = normals[k].Normalized();
            var t = (a / la + b / lb).Normalized();
            double c = normalPart ? Vec3d.Dot(turn, n) : Vec3d.Dot(turn, Vec3d.Cross(n, t).Normalized());
            worst = Math.Max(worst, Math.Abs(c));
        }
        return worst;
    }

    private static double Comp(Vec3d v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
}
