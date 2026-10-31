using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Numerics;

namespace Mite.Core.Gridshells;

/// <summary>
/// Asymptotic–asymptotic–geodesic (AAG) webs after Schling, Wang, Hoyer &amp;
/// Pottmann 2022 ("Designing asymptotic geodesic hybrid gridshells",
/// Computer-Aided Design 152, §3.2–3.3): the quad net of an
/// <see cref="AsymptoticWeb"/> plus one family of its node diagonals, all
/// three families built from straight flat slats — the asymptotic ones
/// standing upright on the surface, the geodesic diagonals lying flat on it.
/// <para>
/// Discrete model, solved by a regularised Gauss–Newton method (the paper's
/// guided projection) with auxiliary normals n and binormals m:
/// <list type="bullet">
/// <item>A-net: every regular node and its four neighbours are coplanar —
/// n · (vᵢ − v) = 0, |n|² = 1 (eqs. 6, 7);</item>
/// <item>geodesic diagonal: the normal lies in the diagonal's osculating
/// plane — n · m = 0 with m = (v_a − v) × (v_c − v) (eq. 13);</item>
/// <item>proximity to the reference surface through the tangent plane at the
/// closest point, (v − p) · n_p = 0, and a fairness term of second
/// differences along the three families (eq. 14).</item>
/// </list>
/// An AAG web exists only on special surfaces (all negatively curved
/// surfaces of revolution carry one: asymptotic curves plus meridians — the
/// rotational web of <see cref="AsymptoticWeb"/> is already exact), so on a
/// general design surface the optimisation trades a small change of the
/// surface for the geodesic property; the result reports both.
/// </para>
/// <pa/// Tri-hex variant (<see cref="Options.Midpoint"/>): the geodesic family runs
/// through the midpoints of the asymptotic lath edges instead of the nodes,
/// so every crossing joins two laths and the cells alternate triangles and
/// hexagons — the layout built as the AGH timber gridshell (Schling, Wan,
/// Wang &amp; D'Acunto 2023, AAG 2023; Wan, D'Acunto &amp; Schling 2024,
/// Engineering Structures 311: "the flat geodesic planks are interlaced at
/// the midpoint of the asymptotic beams to create a tri-hex pattern", which
/// "lowers the buckling length"; Schling et al. 2022 §4.2 built the first
/// module this way, "shifted by half a grid"). Discrete model: the web is
/// refined by one vertex per lath edge, so every lath is a polyline through
/// all its crossings — A × B nodes and A × G (or B × G) midpoint vertices
/// alternate along it — and the two discrete conditions of Schling et al.
/// 2022 are taken at every vertex with its own auxiliary normal: at a node
/// the planar star n · (m − v) = 0 towards its edge midpoints (eq. 6), at a
/// midpoint vertex m of the edge p q the two asymptotic half-edges
/// n_m · (p − m) = n_m · (q − m) = 0 and the geodesic condition
/// n_m · ((m₋ − m) × (m₊ − m)) = 0 along the mid chain (eq. 13). The
/// chord midpoints themselves are only the start: on quads that are not
/// parallelograms they alternate sides of the smooth mid curve by
/// O(spacing²), so a polyline through them turns sideways at every vertex
/// and is far from a discrete geodesic (finding in the tests: 39° on the
/// catenoid, where the exact G curves are the meridians half a step between
/// the node meridians). The optimiser moves each midpoint vertex within the
/// surface by that O(spacing²) amount; a fairness term keeps it central on
/// its lath (<see cref="Result.MaxMidpointShift"/> reports how far it went).
/// </para>
/// <para>
/// Constant crossing angle (<see cref="Options.CrossingAngle"/>): the AGH
/// gridshell was optimised for "constant 60° intersection angles between
/// asymptotic curves" (Wan et al. 2024 §2.2.1). The asymptotic directions of
/// a surface with principal-curvature ratio a = k₁/k₂ &lt; 0 cross at
/// 2γ with tan²γ = −a (Wang &amp; Pottmann 2022, "Characteristic
/// parameterizations of surfaces with a constant ratio of principal
/// curvatures", CAGD 93): a constant angle therefore turns the surface into a
/// CRPC surface (90° is the minimal surface, a = −1; 60° is a = −1/3). The
/// soft residual at every regular node is t_A · t_B − cos θ with the chord
/// directions of the two laths through the node, sign-aligned per iteration.
/// </para>
/// </summary>
public static class AagWeb
{
    public class Options
    {
        /// <summary>Gauss–Newton iterations.</summary>
        public int Iterations { get; set; } = 16;

        /// <summary>Which diagonal family becomes geodesic (0 = the straighter one, 1 = the other).</summary>
        public int GeodesicFamily { get; set; } = 0;

        /// <summary>Weight of the proximity to the reference surface (0 lets the surface float).</summary>
        public double Proximity { get; set; } = 0.3;

        /// <summary>
        /// Fairness weight during the first half of the iterations (then a tenth
        /// of it). The A-net and geodesic conditions let a lath kink inside the
        /// tangent plane; at 0.05 (the 1.3.0 default) the 3-fold Enneper web
        /// kinked by 44.5° where its laths cross the rays, at 0.2 by 21° (the
        /// web's own chains turn by up to 17.6° there).
        /// </summary>
        public double Fairness { get; set; } = 0.2;

        /// <summary>Levenberg damping (the paper's ε closeness to the current iterate).</summary>
        public double Damping { get; set; } = 1e-3;

        /// <summary>
        /// Tri-hex web: the geodesic family runs through the midpoints of the
        /// asymptotic edges (<see cref="AsymptoticWeb.Result.MidChains"/>)
        /// instead of through the nodes. Default false (node diagonals).
        /// </summary>
        public bool Midpoint { get; set; }

        /// <summary>Target angle (degrees, 0–90) between the two asymptotic laths at every node; 0 leaves the angle free.</summary>
        public double CrossingAngle { get; set; }

        /// <summary>Weight of the crossing-angle residual (the A-net and geodesic residuals have weight 1).</summary>
        public double AngleWeight { get; set; } = 1.0;

        public Func<bool>? ShouldCancel { get; set; }
    }

    public sealed class Result
    {
        public Vec3d[] Nodes { get; set; } = Array.Empty<Vec3d>();
        public Vec3d[] Normals { get; set; } = Array.Empty<Vec3d>();
        /// <summary>Node polylines of the two asymptotic families and the geodesic diagonals.</summary>
        public List<Vec3d[]> A { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> B { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> G { get; } = new List<Vec3d[]>();
        /// <summary>Largest angle (degrees) between a star edge and its node's tangent plane — 0 for an exact A-net.</summary>
        public double StarError { get; set; }
        public double InitialStarError { get; set; }
        /// <summary>Largest angle (degrees) between a geodesic diagonal's osculating plane and the node normal — 0 for an exact geodesic.</summary>
        public double GeodesicError { get; set; }
        public double InitialGeodesicError { get; set; }
        /// <summary>Largest and mean distance from the nodes to the reference surface.</summary>
        public double MaxDeviation { get; set; }
        public double MeanDeviation { get; set; }
        /// <summary>Largest and mean distance a node moved from where the web put it (including sliding along the surface).</summary>
        public double MaxNodeMove { get; set; }
        public double MeanNodeMove { get; set; }
        public int Iterations { get; set; }
        /// <summary>Angle (degrees, 0–90) between the two asymptotic laths at every node; NaN where a family is missing.</summary>
        public double[] CrossingAngles { get; set; } = Array.Empty<double>();
        public double AngleMin { get; set; } = double.NaN;
        public double AngleMax { get; set; } = double.NaN;
        public double AngleMean { get; set; } = double.NaN;
        public double InitialAngleMin { get; set; } = double.NaN;
        public double InitialAngleMax { get; set; } = double.NaN;
        public double InitialAngleMean { get; set; } = double.NaN;
        /// <summary>True when the geodesic family runs through the edge midpoints (tri-hex web).</summary>
        public bool Midpoint { get; set; }
        /// <summary>Tri-hex web: the midpoint vertices (A × G and B × G crossings) and the lath edge (node pair) each one sits on.</summary>
        public Vec3d[] Midpoints { get; set; } = Array.Empty<Vec3d>();
        public (int, int)[] MidpointEdges { get; set; } = Array.Empty<(int, int)>();
        /// <summary>Tri-hex web: largest angle (degrees) between a lath half-edge at a midpoint vertex and that vertex's tangent plane — 0 when the refined laths are discrete asymptotic curves.</summary>
        public double AsymptoticError { get; set; } = double.NaN;
        public double InitialAsymptoticError { get; set; } = double.NaN;
        /// <summary>Tri-hex web: how far the midpoint vertices moved from the centres of their chords, as a fraction of the spacing.</summary>
        public double MaxMidpointShift { get; set; }
        public double MeanMidpointShift { get; set; }
        /// <summary>
        /// Largest turning angle (degrees) between consecutive chords of an
        /// asymptotic lath at a node. The A-net conditions allow a lath to kink
        /// inside the tangent plane; only the fairness term keeps it smooth.
        /// </summary>
        public double MaxLathTurning { get; set; }
        /// <summary>The diagonal family (0 or 1 of <see cref="AsymptoticWeb.Result.DiagonalFamily"/>) that was made geodesic.</summary>
        public int GeodesicFamily { get; set; }
    }

    public static Result Optimize(MeshData reference, AsymptoticWeb.Result web, Options? options = null)
        => Run(new MeshProjection(reference), web, options);

    /// <summary>
    /// The same optimisation without a reference surface — for a web designed
    /// free in space (<see cref="WebPropagation"/>, the global step of Wang,
    /// Almaskin &amp; Pottmann 2025 after propagation): normals start from the
    /// node stars, the proximity term is absent and the deviations are 0.
    /// </summary>
    public static Result Optimize(AsymptoticWeb.Result web, Options? options = null)
        => Run(null, web, options);

    private static Result Run(MeshProjection? proj, AsymptoticWeb.Result web, Options? options)
    {
        options ??= new Options();
        int N = web.Nodes.Count;
        var res = new Result();
        if (N == 0) return res;
        double h = web.Spacing > 0 ? web.Spacing : 1.0;
        double inv = 1.0 / h;

        // node polylines of the families
        var chainsA = Chains(web.NeighborsA, web.Nodes);
        var chainsB = Chains(web.NeighborsB, web.Nodes);
        int famG = options.GeodesicFamily == 1 ? 1 : 0;
        bool mid = options.Midpoint;
        res.Midpoint = mid;
        res.GeodesicFamily = famG;

        // regular nodes carry the A-net star; the singular seed does not
        var regular = new bool[N];
        for (int i = 0; i < N; i++)
            regular[i] = web.NeighborsA[i].Length <= 2 && web.NeighborsB[i].Length <= 2 && web.NeighborsA[i].Length + web.NeighborsB[i].Length >= 3;

        // vertices: the N nodes, then (tri-hex) one midpoint vertex per lath edge of the quad net
        var midEdges = new List<(int, int)>();
        var midOf = new Dictionary<(int, int), int>();
        (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
        if (mid)
            foreach (var ch in web.MidChainEdges)
                foreach (var e in ch)
                {
                    var k = Key(e.Item1, e.Item2);
                    if (!midOf.ContainsKey(k)) { midOf[k] = N + midEdges.Count; midEdges.Add(k); }
                }
        int M = midEdges.Count, NV = N + M;
        bool[] midRegular = midEdges.Select(e => regular[e.Item1] && regular[e.Item2]).ToArray();

        // the geodesic family as vertex chains
        var chainsG = new List<int[]>();
        if (mid)
        {
            for (int i = 0; i < web.MidChainEdges.Count; i++)
                if (web.MidChainFamily[i] == famG && web.MidChainEdges[i].Length >= 2)
                    chainsG.Add(web.MidChainEdges[i].Select(e => midOf[Key(e.Item1, e.Item2)]).ToArray());
        }
        else
        {
            for (int i = 0; i < web.DiagonalNodes.Count; i++)
                if (web.DiagonalFamily[i] == famG && web.DiagonalNodes[i].Length >= 2)
                    chainsG.Add(web.DiagonalNodes[i]);
        }
        bool Regular(int v) => v < N ? regular[v] : midRegular[v - N];
        // geodesic triples (a, v, c) along the chains, interior vertices with constrained normals only
        var triples = new List<(int A, int V, int C)>();
        foreach (var ch in chainsG)
            for (int k = 1; k + 1 < ch.Length; k++)
                if (Regular(ch[k]) && ch[k - 1] != ch[k + 1]) triples.Add((ch[k - 1], ch[k], ch[k + 1]));

        // refined laths (tri-hex): node, midpoint, node, … along every A and B chain
        List<int[]> Refine(List<int[]> chains)
        {
            if (!mid) return chains;
            var o = new List<int[]>();
            foreach (var ch in chains)
            {
                var r = new List<int> { ch[0] };
                for (int k = 1; k < ch.Length; k++)
                {
                    if (midOf.TryGetValue(Key(ch[k - 1], ch[k]), out int mv)) r.Add(mv);
                    r.Add(ch[k]);
                }
                o.Add(r.ToArray());
            }
            return o;
        }
        var lathsA = Refine(chainsA); var lathsB = Refine(chainsB);
        // star neighbours of a node: its edge midpoints (tri-hex) or its node neighbours
        int[][] star = new int[N][];
        for (int i = 0; i < N; i++)
        {
            var nb = web.NeighborsA[i].Concat(web.NeighborsB[i]);
            star[i] = mid ? nb.Select(j => midOf.TryGetValue(Key(i, j), out int mv) ? mv : -1).Where(v => v >= 0).ToArray() : nb.ToArray();
        }

        // unknowns (in units of h): per vertex v (3), n (3)
        int nv = 6 * NV;
        var x = new double[nv];
        // without a reference surface (Web Propagation's free web) the node normals come from the A-net stars
        void Init(int i, Vec3d p, Vec3d? starNormal = null)
        {
            Vec3d n;
            if (proj != null)
            {
                var hit = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                n = hit.SmoothNormal.Normalized();
            }
            else n = starNormal ?? new Vec3d(0, 0, 1);
            x[6 * i] = p.X * inv; x[6 * i + 1] = p.Y * inv; x[6 * i + 2] = p.Z * inv;
            x[6 * i + 3] = n.X; x[6 * i + 4] = n.Y; x[6 * i + 5] = n.Z;
        }
        for (int i = 0; i < N; i++) Init(i, web.Nodes[i], proj == null ? StarNormal(web, i) : null);
        // midpoint vertices start at the chord midpoints pulled onto the surface
        for (int j = 0; j < M; j++)
        {
            var (ea, eb) = midEdges[j];
            var c = 0.5 * (web.Nodes[ea] + web.Nodes[eb]);
            if (proj != null) Init(N + j, proj.ClosestPoint(c, proj.NearestVertexGlobal(c)).Point);
            else Init(N + j, c, (StarNormal(web, ea) + StarNormal(web, eb)).Normalized());
        }
        Vec3d V(int i) => new Vec3d(x[6 * i], x[6 * i + 1], x[6 * i + 2]);
        Vec3d Nrm(int i) => new Vec3d(x[6 * i + 3], x[6 * i + 4], x[6 * i + 5]);
        var start = new Vec3d[N];
        for (int i = 0; i < N; i++) start[i] = web.Nodes[i] * inv;

        // chord directions of the two laths through a node, for the crossing angle
        bool Lath(int[] nb, int i, out int from, out int to)
        {
            from = to = -1;
            if (nb.Length == 0) return false;
            if (nb.Length >= 2) { from = nb[0]; to = nb[1]; } else { from = i; to = nb[0]; }
            return true;
        }
        double cosTarget = Math.Cos(Math.Max(0, Math.Min(90, options.CrossingAngle)) * Math.PI / 180);
        bool useAngle = options.CrossingAngle > 0 && options.AngleWeight > 0;

        double TiltDeg(Vec3d n, Vec3d e)
        {
            double l = e.Length, ln = n.Length;
            return l > 1e-12 && ln > 1e-12 ? Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(n, e)) / (l * ln))) * 180 / Math.PI : 0;
        }
        (double star, double geo, double asym) Errors()
        {
            double s = 0, g = 0, a = 0;
            for (int i = 0; i < N; i++)
            {
                if (!regular[i]) continue;
                foreach (int j in star[i]) s = Math.Max(s, TiltDeg(Nrm(i), V(j) - V(i)));
            }
            for (int j = 0; j < M; j++)
            {
                if (!midRegular[j]) continue;
                int v = N + j;
                a = Math.Max(a, Math.Max(TiltDeg(Nrm(v), V(midEdges[j].Item1) - V(v)), TiltDeg(Nrm(v), V(midEdges[j].Item2) - V(v))));
            }
            foreach (var (pa, v, pc) in triples)
            {
                var m = Vec3d.Cross(V(pa) - V(v), V(pc) - V(v));
                g = Math.Max(g, TiltDeg(Nrm(v), m));
            }
            return (s, g, mid ? a : double.NaN);
        }
        double[] Angles()
        {
            var ang = new double[N];
            for (int i = 0; i < N; i++)
            {
                ang[i] = double.NaN;
                if (!regular[i] || !Lath(web.NeighborsA[i], i, out int a0, out int a1) || !Lath(web.NeighborsB[i], i, out int b0, out int b1)) continue;
                var ta = V(a1) - V(a0); var tb = V(b1) - V(b0);
                if (ta.Length < 1e-12 || tb.Length < 1e-12) continue;
                ang[i] = Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(ta, tb)) / (ta.Length * tb.Length))) * 180 / Math.PI;
            }
            return ang;
        }
        (double min, double max, double mean) AngleStats(double[] ang)
        {
            var v = ang.Where(a => !double.IsNaN(a)).ToArray();
            return v.Length == 0 ? (double.NaN, double.NaN, double.NaN) : (v.Min(), v.Max(), v.Average());
        }
        (res.InitialStarError, res.InitialGeodesicError, res.InitialAsymptoticError) = Errors();
        (res.InitialAngleMin, res.InitialAngleMax, res.InitialAngleMean) = AngleStats(Angles());

        // fairness triples along the (refined) laths and the geodesic chains
        var fairTriples = new List<(int, int, int)>();
        foreach (var ch in lathsA.Concat(lathsB).Concat(chainsG))
            for (int k = 1; k + 1 < ch.Length; k++) fairTriples.Add((ch[k - 1], ch[k], ch[k + 1]));

        NormalEquations Assemble(double wf)
        {
            var sys = new NormalEquations(nv);
            for (int i = 0; i < NV; i++)
            {
                var n = Nrm(i);
                // A-net stars at the nodes, asymptotic half-edges at the midpoint vertices
                var edgesTo = i < N ? (regular[i] ? star[i] : Array.Empty<int>())
                    : (midRegular[i - N] ? new[] { midEdges[i - N].Item1, midEdges[i - N].Item2 } : Array.Empty<int>());
                foreach (int j in edgesTo)
                {
                    var e = V(j) - V(i);
                    sys.Row(1.0, Vec3d.Dot(n, e),
                        (6 * i + 3, e.X), (6 * i + 4, e.Y), (6 * i + 5, e.Z),
                        (6 * j, n.X), (6 * j + 1, n.Y), (6 * j + 2, n.Z),
                        (6 * i, -n.X), (6 * i + 1, -n.Y), (6 * i + 2, -n.Z));
                }
                sys.Row(1.0, Vec3d.Dot(n, n) - 1, (6 * i + 3, 2 * n.X), (6 * i + 4, 2 * n.Y), (6 * i + 5, 2 * n.Z));
                // proximity through the tangent plane at the closest point
                if (options.Proximity > 0 && proj != null)
                {
                    var p = V(i) * h;
                    var hit = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                    var np = hit.SmoothNormal.Normalized();
                    sys.Row(options.Proximity, Vec3d.Dot(V(i) - hit.Point * inv, np), (6 * i, np.X), (6 * i + 1, np.Y), (6 * i + 2, np.Z));
                }
                if (i >= N) continue;
                // border nodes (fewer than four neighbours) are held near their start
                // so they cannot slide off the reference surface along its tangent plane
                if (web.NeighborsA[i].Length + web.NeighborsB[i].Length < 4)
                {
                    var d0 = V(i) - start[i];
                    for (int k = 0; k < 3; k++) sys.Row(0.1, Comp(d0, k), (6 * i + k, 1.0));
                }
                // crossing angle: t_A · t_B / (|t_A||t_B|) − ±cos θ with the chords of the two laths
                if (useAngle && regular[i] && Lath(web.NeighborsA[i], i, out int a0, out int a1) && Lath(web.NeighborsB[i], i, out int b0, out int b1))
                {
                    Vec3d ta = V(a1) - V(a0), tb = V(b1) - V(b0);
                    double la = ta.Length, lb = tb.Length;
                    if (la > 1e-12 && lb > 1e-12)
                    {
                        Vec3d u = ta / la, w = tb / lb;
                        double c = Vec3d.Dot(u, w);
                        double sign = c < 0 ? -1 : 1;
                        var ga = (w - c * u) / la;   // ∂c/∂t_A
                        var gb = (u - c * w) / lb;   // ∂c/∂t_B
                        sys.Row(options.AngleWeight, c - sign * cosTarget,
                            (6 * a1, ga.X), (6 * a1 + 1, ga.Y), (6 * a1 + 2, ga.Z),
                            (6 * a0, -ga.X), (6 * a0 + 1, -ga.Y), (6 * a0 + 2, -ga.Z),
                            (6 * b1, gb.X), (6 * b1 + 1, gb.Y), (6 * b1 + 2, gb.Z),
                            (6 * b0, -gb.X), (6 * b0 + 1, -gb.Y), (6 * b0 + 2, -gb.Z));
                    }
                }
            }
            // geodesic chains: n · ((va − v) × (vc − v)) = 0, normalised by
            // |va − v||vc − v| (frozen per iteration) so it reads as an angle
            for (int t = 0; t < triples.Count; t++)
            {
                var (a, v, c) = triples[t];
                Vec3d p = V(a) - V(v), q = V(c) - V(v), n = Nrm(v);
                double sc = 1.0 / Math.Max(1e-12, p.Length * q.Length);
                var gn = Vec3d.Cross(p, q) * sc;   // ∂/∂n
                var ga = Vec3d.Cross(q, n) * sc;   // ∂/∂va
                var gc = Vec3d.Cross(n, p) * sc;   // ∂/∂vc
                var gv = -1.0 * (ga + gc);         // ∂/∂v
                sys.Row(1.0, Vec3d.Dot(n, Vec3d.Cross(p, q)) * sc,
                    (6 * v + 3, gn.X), (6 * v + 4, gn.Y), (6 * v + 5, gn.Z),
                    (6 * a, ga.X), (6 * a + 1, ga.Y), (6 * a + 2, ga.Z),
                    (6 * c, gc.X), (6 * c + 1, gc.Y), (6 * c + 2, gc.Z),
                    (6 * v, gv.X), (6 * v + 1, gv.Y), (6 * v + 2, gv.Z));
            }
            // fairness along all three families
            if (wf > 0)
                foreach (var (a, v, c) in fairTriples)
                {
                    var d = V(a) - 2 * V(v) + V(c);
                    for (int k = 0; k < 3; k++)
                        sys.Row(wf, Comp(d, k), (6 * a + k, 1.0), (6 * v + k, -2.0), (6 * c + k, 1.0));
                }

            return sys;
        }

        int it;
        double lambda = options.Damping, prevCost = double.MaxValue, prevWf = double.NaN;
        var prevX = (double[])x.Clone();
        NormalEquations? prevSys = null;
        double lastWf = 0.1 * options.Fairness;
        for (it = 0; it < options.Iterations; it++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            double wf = it < options.Iterations / 2 ? options.Fairness : 0.1 * options.Fairness;
            if (wf != prevWf) { prevCost = double.MaxValue; prevWf = wf; }
            var sys = Assemble(wf);

            // Levenberg–Marquardt: a step that raised the cost is undone and
            // retried from the previous iterate with more damping
            if (sys.Cost > prevCost && prevSys != null)
            {
                Array.Copy(prevX, x, nv);
                lambda = Math.Min(lambda * 10, 1e6);
                sys = prevSys;
            }
            else
            {
                prevCost = sys.Cost; prevSys = sys;
                Array.Copy(x, prevX, nv);
                lambda = Math.Max(lambda / 3, 1e-7);
            }
            var dx = sys.Solve(lambda);
            if (dx == null) break;
            double step = 0;
            for (int k = 0; k < nv; k++) { x[k] += dx[k]; step = Math.Max(step, Math.Abs(dx[k])); }
            if (step < 1e-10) { it++; break; }
        }
        // keep the better of the last step and the last accepted iterate
        if (prevSys != null && Assemble(lastWf).Cost > prevCost) Array.Copy(prevX, x, nv);
        res.Iterations = it;
        (res.StarError, res.GeodesicError, res.AsymptoticError) = Errors();
        res.CrossingAngles = Angles();
        (res.AngleMin, res.AngleMax, res.AngleMean) = AngleStats(res.CrossingAngles);

        res.Nodes = new Vec3d[N];
        res.Normals = new Vec3d[N];
        double maxDev = 0, sumDev = 0, maxMove = 0, sumMove = 0;
        for (int i = 0; i < N; i++)
        {
            res.Nodes[i] = V(i) * h;
            res.Normals[i] = Nrm(i).Normalized();
            double d = 0;
            if (proj != null)
            {
                var hit = proj.ClosestPoint(res.Nodes[i], proj.NearestVertexGlobal(res.Nodes[i]));
                d = (hit.Point - res.Nodes[i]).Length;
            }
            maxDev = Math.Max(maxDev, d); sumDev += d;
            double mv = (res.Nodes[i] - web.Nodes[i]).Length;
            maxMove = Math.Max(maxMove, mv); sumMove += mv;
        }
        res.MaxDeviation = maxDev; res.MeanDeviation = sumDev / N;
        res.MaxNodeMove = maxMove; res.MeanNodeMove = sumMove / N;
        res.Midpoints = new Vec3d[M];
        res.MidpointEdges = midEdges.ToArray();
        double maxShift = 0, sumShift = 0;
        for (int j = 0; j < M; j++)
        {
            res.Midpoints[j] = V(N + j) * h;
            double sft = (res.Midpoints[j] - 0.5 * (res.Nodes[midEdges[j].Item1] + res.Nodes[midEdges[j].Item2])).Length / h;
            maxShift = Math.Max(maxShift, sft); sumShift += sft;
        }
        res.MaxMidpointShift = maxShift; res.MeanMidpointShift = M > 0 ? sumShift / M : 0;
        Vec3d At(int v) => v < N ? res.Nodes[v] : res.Midpoints[v - N];
        foreach (var ch in lathsA) res.A.Add(ch.Select(At).ToArray());
        foreach (var ch in lathsB) res.B.Add(ch.Select(At).ToArray());
        foreach (var ch in chainsG) res.G.Add(ch.Select(At).ToArray());
        res.MaxLathTurning = AagRefinement.MaxTurning(res.A.Concat(res.B));
        return res;
    }

    /// <summary>
    /// Unit normal of a node's star from its A and B neighbours: the normal of
    /// the plane through the node and its two nearest neighbours of different
    /// families (a star without both is given the mean of its neighbours' planes).
    /// </summary>
    internal static Vec3d StarNormal(AsymptoticWeb.Result web, int i)
    {
        var v = web.Nodes[i];
        var a = web.NeighborsA[i]; var b = web.NeighborsB[i];
        var sum = Vec3d.Zero;
        foreach (int ja in a)
            foreach (int jb in b)
            {
                var c = Vec3d.Cross(web.Nodes[ja] - v, web.Nodes[jb] - v);
                if (c.Length < 1e-300) continue;
                c = c.Normalized();
                if (Vec3d.Dot(c, sum) < 0) c = -c;
                sum += c;
            }
        if (sum.Length < 1e-300)
        {
            // one family only: a plane through the node and its (up to two) neighbours, if any
            var nb = a.Concat(b).ToArray();
            if (nb.Length >= 2) sum = Vec3d.Cross(web.Nodes[nb[0]] - v, web.Nodes[nb[1]] - v);
            if (sum.Length < 1e-300) return new Vec3d(0, 0, 1);
        }
        return sum.Normalized();
    }

    /// <summary>Chains of nodes along one family: maximal paths through nodes with at most two family neighbours.</summary>
    internal static List<int[]> Chains(int[][] nb, List<Vec3d> nodes)
    {
        int n = nb.Length;
        var used = new HashSet<(int, int)>();
        var chains = new List<int[]>();
        bool Through(int v) => nb[v].Length == 2;
        void Walk(int start, int first)
        {
            if (used.Contains((Math.Min(start, first), Math.Max(start, first)))) return;
            var chain = new List<int> { start };
            int prev = start, cur = first;
            while (true)
            {
                used.Add((Math.Min(prev, cur), Math.Max(prev, cur)));
                chain.Add(cur);
                if (!Through(cur) || cur == start) break;
                int next = nb[cur][0] == prev ? nb[cur][1] : nb[cur][0];
                if (used.Contains((Math.Min(cur, next), Math.Max(cur, next)))) break;
                prev = cur; cur = next;
            }
            if (chain.Count >= 2) chains.Add(chain.ToArray());
        }
        // start at ends and branch points, then close the remaining loops
        for (int v = 0; v < n; v++) if (!Through(v)) foreach (int w in nb[v]) Walk(v, w);
        for (int v = 0; v < n; v++) foreach (int w in nb[v]) Walk(v, w);
        return chains;
    }

    private static double Comp(Vec3d v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;


    /// <summary>Accumulates JᵀJ and Jᵀr row by row for a sparse least-squares step.</summary>
    internal sealed class NormalEquations
    {
        private readonly int _n;
        private readonly Dictionary<long, double> _jtj = new Dictionary<long, double>();
        private readonly double[] _jtr;

        public NormalEquations(int n) { _n = n; _jtr = new double[n]; }

        public double Cost { get; private set; }

        public void Row(double weight, double residual, params (int Col, double Val)[] terms)
        {
            double w2 = weight * weight;
            Cost += w2 * residual * residual;
            for (int a = 0; a < terms.Length; a++)
            {
                var (ca, va) = terms[a];
                if (va == 0) continue;
                _jtr[ca] += w2 * va * residual;
                for (int b = 0; b < terms.Length; b++)
                {
                    var (cb, vb) = terms[b];
                    if (vb == 0 || cb < ca) continue;
                    long key = (long)ca * _n + cb;
                    _jtj.TryGetValue(key, out double cur);
                    _jtj[key] = cur + w2 * va * vb;
                }
            }
        }

        public double[]? Solve(double damping)
        {
            // CSR of the damped normal matrix (symmetric, both halves)
            var rows = new List<(int Col, double Val)>[_n];
            for (int i = 0; i < _n; i++) rows[i] = new List<(int, double)>(16);
            var diag = new double[_n];
            foreach (var kv in _jtj)
            {
                int r = (int)(kv.Key / _n), c = (int)(kv.Key % _n);
                if (r == c) diag[r] += kv.Value;
                else { rows[r].Add((c, kv.Value)); rows[c].Add((r, kv.Value)); }
            }
            for (int i = 0; i < _n; i++) diag[i] += damping * (1.0 + diag[i]);
            var rhs = new double[_n];
            for (int i = 0; i < _n; i++) rhs[i] = -_jtr[i];

            var b = new SparseSymmetricSolver.Builder(_n);
            for (int i = 0; i < _n; i++)
            {
                b.Add(i, i, diag[i]);
                foreach (var (c, v) in rows[i]) if (c > i) b.Add(i, c, v);
            }
            // nested-dissection LDLᵀ; the envelope solver as the fallback
            var ldl = SparseLdl.Factor(b, 1e-18);
            if (ldl != null) return ldl.Solve(rhs);
            var solver = SparseSymmetricSolver.Factor(b, 1e-18);
            return solver?.Solve(rhs);
        }
    }
}
