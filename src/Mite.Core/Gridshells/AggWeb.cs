using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Numerics;

namespace Mite.Core.Gridshells;

/// <summary>
/// Asymptotic–geodesic–geodesic (AGG) webs after Schling, Wang, Hoyer &amp;
/// Pottmann 2022 ("Designing asymptotic geodesic hybrid gridshells",
/// Computer-Aided Design 152, 103378, §2.3, §3.2–3.3): a quad net whose two
/// lath families are geodesic (flat slats lying on the surface) and one
/// family of whose node diagonals is asymptotic (upright slats) — the web
/// with more freedom than the AAG web, because the geodesics of a surface
/// form a two-parameter family while its asymptotic curves are fixed.
/// <para>
/// Discrete model (the paper's Table 1: the vertices v are the only unknowns;
/// the edge norms are frozen at the current iterate so every constraint is
/// quadratic — a regularised Gauss–Newton method, their "guided projection"
/// after Tang et al. 2014):
/// <list type="bullet">
/// <item>G-net, Sauer's equal opposite angles at every regular node with unit
/// edge directions gᵢ = (vᵢ − v)/‖vᵢ − v‖ (eq. 8): g₁·g₂ = g₃·g₄ and
/// g₂·g₃ = g₄·g₁ (eq. 9), g₁, g₃ along one lath and g₂, g₄ along the other;
/// equivalently the two laths' principal normals g₁ + g₃ and g₂ + g₄ coincide,
/// which is the surface normal (eq. 10);</item>
/// <item>asymptotic diagonal: both diagonal edges at the node lie in that
/// tangent plane, n̄·(v_a − v) = 0 and n̄·(v_c − v) = 0 with n̄ = g₁ + g₃
/// (eq. 12) — here imposed with both bisectors, which agree once (9) holds;</item>
/// <item>soft terms: proximity to the reference surface through the tangent
/// plane at the closest point, fairness as second differences along the
/// three web polylines (eq. 14), switched off for the final iterations as in
/// the paper, and a weak hold of the border nodes at their start.</item>
/// </list>
/// Exact AGG webs live on developable surfaces: a hexagonal 3-web of
/// straight lines in the development (Graf &amp; Sauer 1924: the tangents of
/// a curve of class 3, the three parallel pencils being the simplest case)
/// rolled onto the surface, with the rulings as the asymptotic family. The
/// cylinder web of <see cref="QuadNet.Cylinder"/> is that case and satisfies
/// every discrete condition to rounding, because the rotation by π about the
/// cylinder normal at a node maps each equally spaced helix onto itself.
/// Wang, Almaskin &amp; Pottmann 2024 ("Computational design of asymptotic
/// geodesic hybrid gridshells via propagation algorithms") use the same
/// discrete conditions row by row; their normal of an AGG node is the
/// binormal of the asymptotic polyline, which degenerates on the straight
/// rulings of the exact case, so the bisector form of eq. (10) is used here.
/// </para>
/// </summary>
public static class AggWeb
{
    /// <summary>
    /// A quad net of two lath families (at most two neighbours per family at a
    /// node) with its quads and the two families of node diagonals, as the AGG
    /// optimisation and its quality measures need it. Built from a traced net
    /// (<see cref="FromTopology"/>), an <see cref="AsymptoticWeb"/>
    /// (<see cref="FromWeb"/>), a lattice function or the exact cylinder web.
    /// </summary>
    public sealed class QuadNet
    {
        public List<Vec3d> Nodes { get; } = new List<Vec3d>();
        public int[][] NeighborsA { get; private set; } = Array.Empty<int[]>();
        public int[][] NeighborsB { get; private set; } = Array.Empty<int[]>();
        /// <summary>Quads as (v, a, d, b): v–a and d–b are A edges, a–d and b–v are B edges.</summary>
        public List<int[]> Quads { get; } = new List<int[]>();
        /// <summary>Node diagonals as node chains, with their family (0 / 1) in <see cref="DiagonalFamily"/>.</summary>
        public List<int[]> DiagonalChains { get; } = new List<int[]>();
        public List<int> DiagonalFamily { get; } = new List<int>();
        /// <summary>Nodes with two neighbours in each family (where the web conditions live).</summary>
        public int RegularNodes { get; private set; }
        /// <summary>Nodes where the two-colouring of the diagonals failed (0 on a regular quad patch).</summary>
        public int LabelConflicts { get; private set; }
        /// <summary>Mean lath edge length (the unit of the optimisation).</summary>
        public double Spacing { get; private set; }

        internal bool[] Regular = Array.Empty<bool>();
        /// <summary>(centre, A neighbour, B neighbour) → the fourth corner of their quad.</summary>
        internal Dictionary<(int, int, int), int> Fourth = new Dictionary<(int, int, int), int>();

        public static QuadNet FromNeighbors(IList<Vec3d> nodes, int[][] neighborsA, int[][] neighborsB)
        {
            var net = new QuadNet();
            net.Nodes.AddRange(nodes);
            net.NeighborsA = neighborsA.Select(a => a.Distinct().ToArray()).ToArray();
            net.NeighborsB = neighborsB.Select(b => b.Distinct().ToArray()).ToArray();
            net.Finish();
            return net;
        }

        /// <summary>The quad net of an asymptotic web (its own diagonal labels are rebuilt here).</summary>
        public static QuadNet FromWeb(AsymptoticWeb.Result web) => FromNeighbors(web.Nodes, web.NeighborsA, web.NeighborsB);

        /// <summary>
        /// The quad net of two crossing lath families from <see cref="NetTopology.Build"/>
        /// (A × B crossings only: build the crossings with <see cref="NetIntersections.Find"/>,
        /// or accept that same-family contacts make irregular nodes).
        /// </summary>
        public static QuadNet FromTopology(NetTopology.Result topo)
        {
            int n = topo.Nodes.Length;
            var nbA = new List<int>[n]; var nbB = new List<int>[n];
            for (int i = 0; i < n; i++) { nbA[i] = new List<int>(); nbB[i] = new List<int>(); }
            foreach (var m in topo.Members)
            {
                if (m.NodeStart < 0 || m.NodeEnd < 0 || m.NodeStart == m.NodeEnd) continue;
                var nb = m.FamilyA ? nbA : nbB;
                if (!nb[m.NodeStart].Contains(m.NodeEnd)) nb[m.NodeStart].Add(m.NodeEnd);
                if (!nb[m.NodeEnd].Contains(m.NodeStart)) nb[m.NodeEnd].Add(m.NodeStart);
            }
            return FromNeighbors(topo.Nodes, nbA.Select(l => l.ToArray()).ToArray(), nbB.Select(l => l.ToArray()).ToArray());
        }

        /// <summary>
        /// A lattice: node (i, j) = point(i, j), family A along i (j constant), family B along j.
        /// Its diagonals are i + j = const and i − j = const.
        /// </summary>
        public static QuadNet FromLattice(Func<int, int, Vec3d> point, int nu, int nv)
        {
            var nodes = new List<Vec3d>();
            for (int j = 0; j < nv; j++) for (int i = 0; i < nu; i++) nodes.Add(point(i, j));
            int Id(int i, int j) => j * nu + i;
            var nbA = new int[nodes.Count][]; var nbB = new int[nodes.Count][];
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    var a = new List<int>(); var b = new List<int>();
                    if (i > 0) a.Add(Id(i - 1, j));
                    if (i + 1 < nu) a.Add(Id(i + 1, j));
                    if (j > 0) b.Add(Id(i, j - 1));
                    if (j + 1 < nv) b.Add(Id(i, j + 1));
                    nbA[Id(i, j)] = a.ToArray(); nbB[Id(i, j)] = b.ToArray();
                }
            return FromNeighbors(nodes, nbA, nbB);
        }

        /// <summary>
        /// The exact AGG web of a cylinder of the given radius about the z axis
        /// (Schling et al. 2022, Fig. 9): in the development a lattice of two
        /// families of parallel lines whose node diagonals i + j = const are the
        /// rulings. Family A makes <paramref name="angleA"/> degrees with the
        /// axis, family B −<paramref name="angleB"/> (default: the same angle);
        /// A edges are <paramref name="spacing"/> long, B edges spacing · sin A / sin B,
        /// so that the diagonal is axial; nu × nv nodes centred on the x axis.
        /// Family 0 or 1 of the result's diagonals is the rulings — read it
        /// from <see cref="RulingFamily"/>.
        /// </summary>
        public static QuadNet Cylinder(double radius, double angleA, double spacing, int nu, int nv, double angleB = double.NaN)
        {
            if (double.IsNaN(angleB)) angleB = angleA;
            double a = angleA * Math.PI / 180, b = angleB * Math.PI / 180;
            double s = spacing * Math.Sin(a), la = spacing * Math.Cos(a), lb = s / Math.Tan(b);
            double sMid = 0.5 * ((nu - 1) - (nv - 1)) * s, tMid = 0.5 * ((nu - 1) * la + (nv - 1) * lb);
            return FromLattice((i, j) =>
            {
                double arc = (i - j) * s - sMid, t = i * la + j * lb - tMid;
                double th = arc / radius;
                return new Vec3d(radius * Math.Cos(th), radius * Math.Sin(th), t);
            }, nu, nv);
        }

        /// <summary>Family (0 / 1) of the diagonal chains that are straighter (the rulings of a developable web).</summary>
        public int RulingFamily
        {
            get
            {
                double[] bow = { 0, 0 };
                for (int c = 0; c < DiagonalChains.Count; c++)
                {
                    var ch = DiagonalChains[c];
                    for (int k = 1; k + 1 < ch.Length; k++)
                    {
                        Vec3d p = Nodes[ch[k - 1]] - Nodes[ch[k]], q = Nodes[ch[k + 1]] - Nodes[ch[k]];
                        double l = p.Length * q.Length;
                        if (l > 1e-300) bow[DiagonalFamily[c]] = Math.Max(bow[DiagonalFamily[c]], Vec3d.Cross(p, q).Length / l);
                    }
                }
                return bow[1] < bow[0] ? 1 : 0;
            }
        }

        /// <summary>Node chains along one family (A = true).</summary>
        public List<int[]> Chains(bool familyA) => AagWeb.Chains(familyA ? NeighborsA : NeighborsB, Nodes);

        /// <summary>Polylines of the diagonal chains of one family.</summary>
        public List<Vec3d[]> DiagonalPolylines(int family)
        {
            var list = new List<Vec3d[]>();
            for (int c = 0; c < DiagonalChains.Count; c++)
                if (DiagonalFamily[c] == family) list.Add(DiagonalChains[c].Select(i => Nodes[i]).ToArray());
            return list;
        }

        internal QuadNet WithNodes(Vec3d[] nodes)
        {
            var net = new QuadNet { NeighborsA = NeighborsA, NeighborsB = NeighborsB, RegularNodes = RegularNodes, LabelConflicts = LabelConflicts, Regular = Regular, Fourth = Fourth };
            net.Nodes.AddRange(nodes);
            net.Quads.AddRange(Quads); net.DiagonalChains.AddRange(DiagonalChains); net.DiagonalFamily.AddRange(DiagonalFamily);
            double sum = 0; int cnt = 0;
            for (int v = 0; v < nodes.Length; v++) foreach (int w in NeighborsA[v].Concat(NeighborsB[v])) { sum += (nodes[w] - nodes[v]).Length; cnt++; }
            net.Spacing = cnt > 0 ? sum / cnt : 1;
            return net;
        }

        private void Finish()
        {
            int n = Nodes.Count;
            Regular = new bool[n];
            double sum = 0; int cnt = 0;
            for (int v = 0; v < n; v++)
            {
                Regular[v] = NeighborsA[v].Length == 2 && NeighborsB[v].Length == 2;
                if (Regular[v]) RegularNodes++;
                foreach (int w in NeighborsA[v].Concat(NeighborsB[v])) { sum += (Nodes[w] - Nodes[v]).Length; cnt++; }
            }
            Spacing = cnt > 0 ? sum / cnt : 1;

            // quads: v with A neighbour a and B neighbour b closes when a has a B neighbour that is an A neighbour of b
            var seen = new HashSet<(int, int, int, int)>();
            for (int v = 0; v < n; v++)
                foreach (int a in NeighborsA[v])
                    foreach (int b in NeighborsB[v])
                        foreach (int d in NeighborsB[a])
                        {
                            if (d == v || !NeighborsA[b].Contains(d)) continue;
                            var key = Sorted(v, a, d, b);
                            if (!seen.Add(key)) continue;
                            Quads.Add(new[] { v, a, d, b });
                            Fourth[(v, a, b)] = d; Fourth[(d, b, a)] = v;
                            Fourth[(a, v, d)] = b; Fourth[(b, d, v)] = a;
                        }

            // diagonal chains: from v through the quad (v, a, b) to d, on through d's other neighbours
            var used = new HashSet<(int, int)>();
            bool Continues(int v, int a, int b) => Fourth.ContainsKey((v, a, b));
            (int, int) Others(int v, int a, int b)
            {
                int a2 = NeighborsA[v][0] == a ? NeighborsA[v][1] : NeighborsA[v][0];
                int b2 = NeighborsB[v][0] == b ? NeighborsB[v][1] : NeighborsB[v][0];
                return (a2, b2);
            }
            void Walk(int v, int a, int b)
            {
                var chain = new List<int> { v };
                while (Fourth.TryGetValue((v, a, b), out int d))
                {
                    var e = (Math.Min(v, d), Math.Max(v, d));
                    if (used.Contains(e)) break;
                    used.Add(e); chain.Add(d);
                    if (!Regular[d]) break;
                    // arrived at d along A neighbour b and B neighbour a; leave through its other two
                    var (b2, a2) = Others(d, b, a);
                    v = d; a = b2; b = a2;
                    if (chain.Count > 4 && v == chain[0]) break;
                }
                if (chain.Count >= 2) DiagonalChains.Add(chain.ToArray());
            }
            for (int pass = 0; pass < 2; pass++)
                for (int v = 0; v < n; v++)
                    foreach (int a in NeighborsA[v])
                        foreach (int b in NeighborsB[v])
                        {
                            if (!Continues(v, a, b)) continue;
                            if (pass == 0 && Regular[v])
                            {
                                var (a2, b2) = Others(v, a, b);
                                if (Continues(v, a2, b2)) continue;   // interior of a chain: started elsewhere
                            }
                            Walk(v, a, b);
                        }

            // two-colour the chains: the two diagonals through a node differ, and so
            // do the two diagonals of one quad (the node rule alone leaves the two
            // parity classes of a lattice unrelated)
            int m = DiagonalChains.Count;
            var adjacent = new List<int>[m];
            for (int c = 0; c < m; c++) adjacent[c] = new List<int>();
            var at = new List<int>[n];
            for (int v = 0; v < n; v++) at[v] = new List<int>();
            var chainOf = new Dictionary<(int, int), int>();
            for (int c = 0; c < m; c++)
            {
                foreach (int v in DiagonalChains[c].Distinct()) at[v].Add(c);
                var ch = DiagonalChains[c];
                for (int k = 0; k + 1 < ch.Length; k++) chainOf[(Math.Min(ch[k], ch[k + 1]), Math.Max(ch[k], ch[k + 1]))] = c;
            }
            for (int v = 0; v < n; v++)
                for (int i = 0; i < at[v].Count; i++)
                    for (int j = i + 1; j < at[v].Count; j++) { adjacent[at[v][i]].Add(at[v][j]); adjacent[at[v][j]].Add(at[v][i]); }
            foreach (var q in Quads)
                if (chainOf.TryGetValue((Math.Min(q[0], q[2]), Math.Max(q[0], q[2])), out int c1) &&
                    chainOf.TryGetValue((Math.Min(q[1], q[3]), Math.Max(q[1], q[3])), out int c2) && c1 != c2)
                { adjacent[c1].Add(c2); adjacent[c2].Add(c1); }
            var label = Enumerable.Repeat(-1, m).ToArray();
            var queue = new Queue<int>();
            for (int seed = 0; seed < m; seed++)
            {
                if (label[seed] >= 0) continue;
                label[seed] = 0; queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    foreach (int o in adjacent[c])
                    {
                        if (label[o] < 0) { label[o] = 1 - label[c]; queue.Enqueue(o); }
                        else if (label[o] == label[c]) LabelConflicts++;
                    }
                }
            }
            DiagonalFamily.AddRange(label);
        }

        private static (int, int, int, int) Sorted(int a, int b, int c, int d)
        {
            var s = new[] { a, b, c, d }; Array.Sort(s);
            return (s[0], s[1], s[2], s[3]);
        }
    }

    public class Options
    {
        /// <summary>Gauss–Newton iterations (the last third without fairness, as in the paper).</summary>
        public int Iterations { get; set; } = 40;

        /// <summary>
        /// Which diagonal family becomes asymptotic: −1 = the one whose edges lie nearer the
        /// reference tangent planes at the start (right for a web traced on the reference; a
        /// web started far from it, like the cylinder web on another surface, should say), 0 or 1.
        /// </summary>
        public int AsymptoticFamily { get; set; } = -1;

        /// <summary>Weight of the proximity to the reference surface (0 lets the surface float).</summary>
        public double Proximity { get; set; } = 0.3;

        /// <summary>Fairness weight (second differences along the three families) during the first two thirds of the iterations.</summary>
        public double Fairness { get; set; } = 0.02;

        /// <summary>Weight holding the border nodes near their start along the reference tangent plane (they have no web condition of their own).</summary>
        public double BorderHold { get; set; } = 0.02;

        /// <summary>
        /// Weight pulling the node normals towards the reference normals during the first two
        /// thirds of the iterations, a tenth of it afterwards: it carries a web started far
        /// from the reference (the cylinder web on another surface) towards it.
        /// </summary>
        public double Guide { get; set; } = 0.3;

        /// <summary>Levenberg damping (the paper's ε closeness to the current iterate).</summary>
        public double Damping { get; set; } = 1e-3;

        /// <summary>
        /// Largest node move per iteration, in units of the lath spacing. A web started far from
        /// the reference would otherwise be thrown onto it in one linear step and crumple;
        /// capped, it is carried over in a few steps with the web conditions tracked along the way.
        /// </summary>
        public double MaxStep { get; set; } = 0.25;

        public Func<bool>? ShouldCancel { get; set; }

        /// <summary>Test hook: receives the cost and half-gradient functions of the first iteration's system and skips the solve.</summary>
        internal Action<Func<double[], double>, Func<double[], double[]>>? Probe { get; set; }
        internal Action<string>? Trace { get; set; }
        internal bool HoldAll { get; set; }
        internal double FinalFairness { get; set; } = 0.0;
        internal double FinalGuide { get; set; } = 0.1;
        internal bool SkipGeodesicRows { get; set; }
        internal bool SkipAsymptoticRows { get; set; }
    }

    /// <summary>Quality of a quad net as an AGG web, every angle in degrees.</summary>
    public sealed class Quality
    {
        /// <summary>Largest |α₁ − α₃|, |α₂ − α₄| of the opposite angles at a regular node (0 = exact G-net).</summary>
        public double GeodesicError { get; set; }
        public double MeanGeodesicError { get; set; }
        /// <summary>Largest angle between the two laths' principal normals g₁ + g₃ and g₂ + g₄ (0 = one common surface normal).</summary>
        public double BisectorError { get; set; }
        /// <summary>Largest angle between an asymptotic diagonal edge and the node's tangent plane (0 = exact asymptotic polyline).</summary>
        public double AsymptoticError { get; set; }
        public double MeanAsymptoticError { get; set; }
        /// <summary>Against the reference mesh: largest angle between the web's normal and the mesh normal, and between an asymptotic edge and the mesh tangent plane.</summary>
        public double SurfaceNormalError { get; set; }
        public double SurfaceAsymptoticError { get; set; }
        /// <summary>Largest and mean node distance to the reference mesh.</summary>
        public double MaxDeviation { get; set; }
        public double MeanDeviation { get; set; }
        public int AsymptoticEdges { get; set; }
    }

    public sealed class Result
    {
        public QuadNet Net { get; set; } = new QuadNet();
        public Vec3d[] Nodes { get; set; } = Array.Empty<Vec3d>();
        /// <summary>Unit normals of the web at its nodes (the laths' common principal normal; the reference normal at border nodes).</summary>
        public Vec3d[] Normals { get; set; } = Array.Empty<Vec3d>();
        /// <summary>Node polylines of the two geodesic lath families.</summary>
        public List<Vec3d[]> A { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> B { get; } = new List<Vec3d[]>();
        /// <summary>Node polylines of the asymptotic diagonals and of the other diagonal family.</summary>
        public List<Vec3d[]> Asymptotic { get; } = new List<Vec3d[]>();
        public List<Vec3d[]> Other { get; } = new List<Vec3d[]>();
        public int AsymptoticFamily { get; set; }
        public Quality Initial { get; set; } = new Quality();
        public Quality Final { get; set; } = new Quality();
        /// <summary>Largest node displacement from the start, in model units.</summary>
        public double MaxMove { get; set; }
        public int Iterations { get; set; }
        public double FinalCost { get; set; }
    }

    /// <summary>
    /// Measures a quad net as an AGG web with the given asymptotic diagonal family
    /// (−1 = the family nearer the reference tangent planes).
    /// </summary>
    public static Quality Measure(QuadNet net, int asymptoticFamily = -1, MeshData? reference = null)
    {
        var proj = reference != null ? new MeshProjection(reference) : null;
        if (asymptoticFamily < 0) asymptoticFamily = ChooseFamily(net, proj);
        return Measure(net, net.Nodes.ToArray(), asymptoticFamily, proj);
    }

    /// <summary>
    /// Which diagonal family lies nearer the reference tangent planes (mean |sin| of the
    /// angle between its edges and the plane); family 0 without a reference or on a tie.
    /// </summary>
    public static int ChooseFamily(QuadNet net, MeshProjection? proj)
    {
        if (proj == null) return 0;
        double[] sum = { 0, 0 }; int[] cnt = { 0, 0 };
        for (int c = 0; c < net.DiagonalChains.Count; c++)
        {
            var ch = net.DiagonalChains[c]; int f = net.DiagonalFamily[c];
            for (int k = 0; k + 1 < ch.Length; k++)
            {
                var p = net.Nodes[ch[k]];
                var hit = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                var nm = hit.SmoothNormal.Normalized();
                var u = (net.Nodes[ch[k + 1]] - p).Normalized();
                sum[f] += Math.Abs(Vec3d.Dot(nm, u)); cnt[f]++;
            }
        }
        double m0 = cnt[0] > 0 ? sum[0] / cnt[0] : double.MaxValue, m1 = cnt[1] > 0 ? sum[1] / cnt[1] : double.MaxValue;
        return m1 < m0 - 1e-12 ? 1 : 0;
    }

    public static Result Optimize(MeshData reference, QuadNet net, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(reference);
        int N = net.Nodes.Count;
        var res = new Result { Net = net };
        if (N == 0) return res;
        int fam = options.AsymptoticFamily >= 0 ? Math.Min(1, options.AsymptoticFamily) : ChooseFamily(net, proj);
        res.AsymptoticFamily = fam;
        double h = net.Spacing > 0 ? net.Spacing : 1.0;
        double inv = 1.0 / h;

        // unknowns: the node positions in units of h (the paper's Table 1 — the normals
        // of an AGG web are the laths' bisectors, eq. 10, not auxiliary variables)
        int nv = 3 * N;
        var x = new double[nv];
        var start = new Vec3d[N];
        for (int i = 0; i < N; i++)
        {
            var p = net.Nodes[i] * inv; start[i] = p;
            x[3 * i] = p.X; x[3 * i + 1] = p.Y; x[3 * i + 2] = p.Z;
        }
        Vec3d V(int i) => new Vec3d(x[3 * i], x[3 * i + 1], x[3 * i + 2]);
        Vec3d[] Current() { var a = new Vec3d[N]; for (int i = 0; i < N; i++) a[i] = V(i) * h; return a; }

        res.Initial = Measure(net, net.Nodes.ToArray(), fam, proj);

        // asymptotic diagonal edges (v, d) at regular nodes v — both edges of every node,
        // eq. 12 — and fairness triples along A, B and the asymptotic diagonals
        var asymEdges = new List<(int V, int D)>();
        var fairTriples = new List<(int, int, int)>();
        for (int c = 0; c < net.DiagonalChains.Count; c++)
        {
            if (net.DiagonalFamily[c] != fam) continue;
            var ch = net.DiagonalChains[c];
            for (int k = 0; k + 1 < ch.Length; k++)
            {
                if (net.Regular[ch[k]]) asymEdges.Add((ch[k], ch[k + 1]));
                if (net.Regular[ch[k + 1]]) asymEdges.Add((ch[k + 1], ch[k]));
            }
            for (int k = 1; k + 1 < ch.Length; k++) fairTriples.Add((ch[k - 1], ch[k], ch[k + 1]));
        }
        foreach (var ch in net.Chains(true).Concat(net.Chains(false)))
            for (int k = 1; k + 1 < ch.Length; k++) fairTriples.Add((ch[k - 1], ch[k], ch[k + 1]));
        var border = new bool[N];
        for (int i = 0; i < N; i++) border[i] = net.NeighborsA[i].Length + net.NeighborsB[i].Length < 4;

        const double bisectorFloor = 0.05;   // |g₁ + g₃| = 2 sin(φ/2): a lath turning less than ~3° per node gives a weaker row
        var refNormal = new Vec3d[N];
        var unit = new Vec3d[4]; var len = new double[4]; var id = new int[4];
        // g₁, g₃ along A and g₂, g₄ along B at a regular node, as unit vectors with their lengths
        void Star(int v)
        {
            var pv = V(v);
            id[0] = net.NeighborsA[v][0]; id[2] = net.NeighborsA[v][1];
            id[1] = net.NeighborsB[v][0]; id[3] = net.NeighborsB[v][1];
            for (int k = 0; k < 4; k++) { var e = V(id[k]) - pv; len[k] = Math.Max(e.Length, 1e-12); unit[k] = e / len[k]; }
        }

        NormalEquations Assemble(double wf, double wg)
        {
            var sys = new NormalEquations(nv);
            for (int v = 0; v < N; v++)
            {
                var pv = V(v);
                var pm = pv * h;
                var hit = proj.ClosestPoint(pm, proj.NearestVertexGlobal(pm));
                var np = hit.SmoothNormal.Normalized();
                refNormal[v] = np;
                if (net.Regular[v])
                {
                    Star(v);
                    // equal opposite angles (eq. 9): g₁·g₂ − g₃·g₄ = 0 and g₂·g₃ − g₄·g₁ = 0,
                    // with the derivative of the unit vectors, ∂gᵢ/∂vᵢ = (I − gᵢgᵢᵀ)/ℓᵢ
                    if (!options.SkipGeodesicRows)
                    {
                        AngleRow(sys, v, id, unit, len, 0, 1, 2, 3);
                        AngleRow(sys, v, id, unit, len, 1, 2, 3, 0);
                    }
                    // reference guide: the bisectors g₁ + g₃ and g₂ + g₄ have no component
                    // along the reference tangent plane (the laths geodesic on the reference)
                    if (wg > 0)
                    {
                        var t1 = Vec3d.Cross(np, Math.Abs(np.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0)).Normalized();
                        var t2 = Vec3d.Cross(np, t1);
                        for (int pair = 0; pair < 2; pair++)
                        {
                            int i = pair == 0 ? 0 : 1, k = pair == 0 ? 2 : 3;
                            var nb = unit[i] + unit[k];
                            foreach (var t in new[] { t1, t2 })
                            {
                                var di = (t - Vec3d.Dot(unit[i], t) * unit[i]) / len[i];
                                var dk = (t - Vec3d.Dot(unit[k], t) * unit[k]) / len[k];
                                var dv = -1.0 * (di + dk);
                                sys.Row(wg, Vec3d.Dot(nb, t),
                                    (3 * id[i], di.X), (3 * id[i] + 1, di.Y), (3 * id[i] + 2, di.Z),
                                    (3 * id[k], dk.X), (3 * id[k] + 1, dk.Y), (3 * id[k] + 2, dk.Z),
                                    (3 * v, dv.X), (3 * v + 1, dv.Y), (3 * v + 2, dv.Z));
                            }
                        }
                    }
                }
                // proximity through the tangent plane at the closest point
                if (options.Proximity > 0)
                    sys.Row(options.Proximity, Vec3d.Dot(pv - hit.Point * inv, np), (3 * v, np.X), (3 * v + 1, np.Y), (3 * v + 2, np.Z));
                // border nodes are held near their start along the reference tangent plane
                // (free to reach the surface, not to slide along it)
                if ((border[v] || options.HoldAll) && options.BorderHold > 0)
                {
                    var d0 = pv - start[v];
                    var t1 = Vec3d.Cross(np, Math.Abs(np.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0)).Normalized();
                    var t2 = Vec3d.Cross(np, t1);
                    foreach (var t in new[] { t1, t2 })
                        sys.Row(options.BorderHold, Vec3d.Dot(d0, t), (3 * v, t.X), (3 * v + 1, t.Y), (3 * v + 2, t.Z));
                }
            }
            // asymptotic diagonals (eq. 12): n̄·û = 0 for the unit edge û from v to d, n̄ the
            // longer of the two bisectors at v scaled by 1/|n̄| (floored) so the residual reads
            // as sin of the angle between the edge and the tangent plane; also the reference
            // guide n_ref·û = 0
            foreach (var (v, d) in asymEdges)
            {
                var pv = V(v);
                var u = V(d) - pv; double lu = Math.Max(u.Length, 1e-12); var uh = u / lu;
                if (wg > 0)
                {
                    var np = refNormal[v];
                    var dd0 = (np - Vec3d.Dot(np, uh) * uh) / lu;
                    sys.Row(wg, Vec3d.Dot(np, uh), (3 * d, dd0.X), (3 * d + 1, dd0.Y), (3 * d + 2, dd0.Z), (3 * v, -dd0.X), (3 * v + 1, -dd0.Y), (3 * v + 2, -dd0.Z));
                }
                if (options.SkipAsymptoticRows) continue;
                Star(v);
                var nA = unit[0] + unit[2]; var nB = unit[1] + unit[3];
                int pi = nA.LengthSquared >= nB.LengthSquared ? 0 : 1, pk = pi + 2;
                var nb = pi == 0 ? nA : nB;
                int i = id[pi], k = id[pk];
                Vec3d gi = unit[pi], gk = unit[pk]; double li = len[pi], lk = len[pk];
                double nl = nb.Length;
                double s = 1.0 / Math.Max(nl, bisectorFloor);
                double r = s * Vec3d.Dot(nb, uh);
                // ∂r/∂d through û; ∂r/∂vᵢ through gᵢ and, above the floor, through 1/|n̄|:
                // (I − gᵢgᵢᵀ)(û − (n̂·û) n̂)/ℓᵢ, the projection dropping out below the floor
                var w = nl > bisectorFloor ? uh - (r / nl) * nb : uh;
                var dd = s / lu * (nb - Vec3d.Dot(nb, uh) * uh);
                var di = s / li * (w - Vec3d.Dot(gi, w) * gi);
                var dk = s / lk * (w - Vec3d.Dot(gk, w) * gk);
                var dv = -1.0 * (dd + di + dk);
                sys.Row(1.0, r,
                    (3 * d, dd.X), (3 * d + 1, dd.Y), (3 * d + 2, dd.Z),
                    (3 * i, di.X), (3 * i + 1, di.Y), (3 * i + 2, di.Z),
                    (3 * k, dk.X), (3 * k + 1, dk.Y), (3 * k + 2, dk.Z),
                    (3 * v, dv.X), (3 * v + 1, dv.Y), (3 * v + 2, dv.Z));
            }
            if (wf > 0)
                foreach (var (a, v, c) in fairTriples)
                {
                    var d2 = V(a) - 2 * V(v) + V(c);
                    for (int k = 0; k < 3; k++) sys.Row(wf, d2[k], (3 * a + k, 1.0), (3 * v + k, -2.0), (3 * c + k, 1.0));
                }
            return sys;
        }

        if (options.Probe != null)
        {
            options.Probe(xs => { Array.Copy(xs, x, nv); return Assemble(options.Fairness, options.Guide).Cost; },
                          xs => { Array.Copy(xs, x, nv); return (double[])Assemble(options.Fairness, options.Guide).HalfGradient.Clone(); });
            return res;
        }
        int it;
        int fairUntil = options.Iterations - Math.Max(1, options.Iterations / 3);
        double lambda = options.Damping, prevCost = double.MaxValue, prevWf = double.NaN;
        var prevX = (double[])x.Clone();
        NormalEquations? prevSys = null;
        double lastWf = options.FinalFairness * options.Fairness, lastWg = options.FinalGuide * options.Guide;
        for (it = 0; it < options.Iterations; it++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            double wf = it < fairUntil ? options.Fairness : options.FinalFairness * options.Fairness;
            double wg = it < fairUntil ? options.Guide : options.FinalGuide * options.Guide;
            // the soft weights change once: the costs are not comparable across the
            // change, and the weaker system is entered with fresh damping
            if (wf != prevWf) { prevCost = double.MaxValue; prevWf = wf; prevSys = null; lambda = Math.Max(lambda, options.Damping); }
            lastWf = wf; lastWg = wg;
            var sys = Assemble(wf, wg);
            // Levenberg–Marquardt: a step that raised the cost is undone and retried with more damping
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
                lambda = Math.Max(lambda / 10, 1e-9);
            }
            options.Trace?.Invoke($"it {it} wf {wf} wg {wg} cost {sys.Cost:G4} lambda {lambda:G2}");
            var dx = sys.Solve(lambda);
            // a singular system (soft terms off, a web free to slide): damp harder and go on
            for (int retry = 0; dx == null && retry < 6; retry++) { lambda = Math.Min(lambda * 100, 1e6); dx = sys.Solve(lambda); }
            if (dx == null) break;
            double step = 0;
            for (int k = 0; k < nv; k++) step = Math.Max(step, Math.Abs(dx[k]));
            double scale = options.MaxStep > 0 && step > options.MaxStep ? options.MaxStep / step : 1.0;
            for (int k = 0; k < nv; k++) x[k] += scale * dx[k];
            if (step < 1e-12)
            {
                // converged: on to the final phase without fairness, or done
                if (it < fairUntil) { it = fairUntil - 1; continue; }
                it++; break;
            }
        }
        // keep the better of the last step and the last accepted iterate, judged by the same system
        double lastCost = Assemble(lastWf, lastWg).Cost;
        if (prevSys != null && lastCost > prevCost) { Array.Copy(prevX, x, nv); res.FinalCost = prevCost; }
        else res.FinalCost = lastCost;
        res.Iterations = it;

        res.Nodes = Current();
        var outNet = net.WithNodes(res.Nodes);
        res.Final = Measure(outNet, res.Nodes, fam, proj);
        res.Normals = new Vec3d[N];
        for (int v = 0; v < N; v++)
        {
            var n = NodeNormal(outNet, res.Nodes, v, out _, out _);
            var nm = proj.ClosestPoint(res.Nodes[v], proj.NearestVertexGlobal(res.Nodes[v])).SmoothNormal.Normalized();
            if (n.Length == 0) n = nm; else if (Vec3d.Dot(n, nm) < 0) n = -n;
            res.Normals[v] = n;
        }
        double move = 0;
        for (int i = 0; i < N; i++) move = Math.Max(move, (res.Nodes[i] - net.Nodes[i]).Length);
        res.MaxMove = move;
        foreach (var ch in outNet.Chains(true)) res.A.Add(ch.Select(i => res.Nodes[i]).ToArray());
        foreach (var ch in outNet.Chains(false)) res.B.Add(ch.Select(i => res.Nodes[i]).ToArray());
        res.Asymptotic.AddRange(outNet.DiagonalPolylines(fam));
        res.Other.AddRange(outNet.DiagonalPolylines(1 - fam));
        return res;
    }

    /// <summary>One row gᵢ·gⱼ − gₖ·gₗ = 0 at node v with its four unit edges (indices into the star arrays).</summary>
    private static void AngleRow(NormalEquations sys, int v, int[] id, Vec3d[] g, double[] len, int i, int j, int k, int l)
    {
        double cij = Vec3d.Dot(g[i], g[j]), ckl = Vec3d.Dot(g[k], g[l]);
        var ti = (g[j] - cij * g[i]) / len[i];
        var tj = (g[i] - cij * g[j]) / len[j];
        var tk = -1.0 / len[k] * (g[l] - ckl * g[k]);
        var tl = -1.0 / len[l] * (g[k] - ckl * g[l]);
        var tv = -1.0 * (ti + tj + tk + tl);
        sys.Row(1.0, cij - ckl,
            (3 * id[i], ti.X), (3 * id[i] + 1, ti.Y), (3 * id[i] + 2, ti.Z),
            (3 * id[j], tj.X), (3 * id[j] + 1, tj.Y), (3 * id[j] + 2, tj.Z),
            (3 * id[k], tk.X), (3 * id[k] + 1, tk.Y), (3 * id[k] + 2, tk.Z),
            (3 * id[l], tl.X), (3 * id[l] + 1, tl.Y), (3 * id[l] + 2, tl.Z),
            (3 * v, tv.X), (3 * v + 1, tv.Y), (3 * v + 2, tv.Z));
    }

    /// <summary>The web's normal at a node: the longer of the two laths' bisectors g₁ + g₃, g₂ + g₄ (zero when both laths are straight there).</summary>
    internal static Vec3d NodeNormal(QuadNet net, Vec3d[] p, int v, out Vec3d nA, out Vec3d nB)
    {
        nA = Vec3d.Zero; nB = Vec3d.Zero;
        if (!net.Regular[v]) return Vec3d.Zero;
        nA = (p[net.NeighborsA[v][0]] - p[v]).Normalized() + (p[net.NeighborsA[v][1]] - p[v]).Normalized();
        nB = (p[net.NeighborsB[v][0]] - p[v]).Normalized() + (p[net.NeighborsB[v][1]] - p[v]).Normalized();
        var n = nA.LengthSquared >= nB.LengthSquared ? nA : nB;
        return n.Length > 1e-9 ? n.Normalized() : Vec3d.Zero;
    }

    private static Quality Measure(QuadNet net, Vec3d[] p, int fam, MeshProjection? proj)
    {
        var q = new Quality();
        int N = p.Length;
        double rad = 180 / Math.PI;
        double gSum = 0; int gCnt = 0;
        var normal = new Vec3d[N];
        for (int v = 0; v < N; v++)
        {
            if (!net.Regular[v]) continue;
            var g1 = (p[net.NeighborsA[v][0]] - p[v]).Normalized(); var g3 = (p[net.NeighborsA[v][1]] - p[v]).Normalized();
            var g2 = (p[net.NeighborsB[v][0]] - p[v]).Normalized(); var g4 = (p[net.NeighborsB[v][1]] - p[v]).Normalized();
            double a1 = Angle(g1, g2), a2 = Angle(g2, g3), a3 = Angle(g3, g4), a4 = Angle(g4, g1);
            double e = Math.Max(Math.Abs(a1 - a3), Math.Abs(a2 - a4)) * rad;
            q.GeodesicError = Math.Max(q.GeodesicError, e); gSum += e; gCnt++;
            normal[v] = NodeNormal(net, p, v, out var nA, out var nB);
            if (nA.Length > 1e-9 && nB.Length > 1e-9) { double ab = Angle(nA.Normalized(), nB.Normalized()) * rad; q.BisectorError = Math.Max(q.BisectorError, Math.Min(ab, 180 - ab)); }
            if (proj != null && normal[v].Length > 0)
            {
                var nm = proj.ClosestPoint(p[v], proj.NearestVertexGlobal(p[v])).SmoothNormal.Normalized();
                double an = Angle(normal[v], nm) * rad;
                q.SurfaceNormalError = Math.Max(q.SurfaceNormalError, Math.Min(an, 180 - an));
            }
        }
        q.MeanGeodesicError = gCnt > 0 ? gSum / gCnt : 0;
        double aSum = 0; int aCnt = 0;
        for (int c = 0; c < net.DiagonalChains.Count; c++)
        {
            if (net.DiagonalFamily[c] != fam) continue;
            var ch = net.DiagonalChains[c];
            for (int k = 0; k + 1 < ch.Length; k++)
                for (int side = 0; side < 2; side++)
                {
                    int v = side == 0 ? ch[k] : ch[k + 1], d = side == 0 ? ch[k + 1] : ch[k];
                    if (!net.Regular[v]) continue;
                    var u = (p[d] - p[v]).Normalized();
                    if (normal[v].Length > 0)
                    {
                        double e = Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(normal[v], u)))) * rad;
                        q.AsymptoticError = Math.Max(q.AsymptoticError, e); aSum += e; aCnt++;
                    }
                    if (proj != null)
                    {
                        var nm = proj.ClosestPoint(p[v], proj.NearestVertexGlobal(p[v])).SmoothNormal.Normalized();
                        q.SurfaceAsymptoticError = Math.Max(q.SurfaceAsymptoticError, Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(nm, u)))) * rad);
                    }
                }
        }
        q.MeanAsymptoticError = aCnt > 0 ? aSum / aCnt : 0;
        q.AsymptoticEdges = aCnt;
        if (proj != null)
        {
            double sum = 0;
            for (int v = 0; v < N; v++)
            {
                double d = (proj.ClosestPoint(p[v], proj.NearestVertexGlobal(p[v])).Point - p[v]).Length;
                q.MaxDeviation = Math.Max(q.MaxDeviation, d); sum += d;
            }
            q.MeanDeviation = N > 0 ? sum / N : 0;
        }
        return q;
    }

    /// <summary>Angle between two unit vectors, accurate near 0 and π (atan2 form).</summary>
    private static double Angle(Vec3d a, Vec3d b) => Math.Atan2(Vec3d.Cross(a, b).Length, Vec3d.Dot(a, b));
}
