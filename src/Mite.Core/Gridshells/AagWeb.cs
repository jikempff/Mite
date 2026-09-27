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

        /// <summary>Fairness weight during the first half of the iterations (then a tenth of it).</summary>
        public double Fairness { get; set; } = 0.05;

        /// <summary>Levenberg damping (the paper's ε closeness to the current iterate).</summary>
        public double Damping { get; set; } = 1e-3;

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
        public int Iterations { get; set; }
    }

    public static Result Optimize(MeshData reference, AsymptoticWeb.Result web, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(reference);
        int N = web.Nodes.Count;
        var res = new Result();
        if (N == 0) return res;
        double h = web.Spacing > 0 ? web.Spacing : 1.0;
        double inv = 1.0 / h;

        // node polylines of the families
        var chainsA = Chains(web.NeighborsA, web.Nodes);
        var chainsB = Chains(web.NeighborsB, web.Nodes);
        var chainsG = new List<int[]>();
        int famG = options.GeodesicFamily == 1 ? 1 : 0;
        for (int i = 0; i < web.DiagonalNodes.Count; i++) if (web.DiagonalFamily[i] == famG && web.DiagonalNodes[i].Length >= 2) chainsG.Add(web.DiagonalNodes[i]);

        // regular nodes carry the A-net star; the singular seed does not
        var regular = new bool[N];
        for (int i = 0; i < N; i++)
            regular[i] = web.NeighborsA[i].Length <= 2 && web.NeighborsB[i].Length <= 2 && web.NeighborsA[i].Length + web.NeighborsB[i].Length >= 3;
        // geodesic triples (a, v, c) along the diagonals, interior nodes only
        var triples = new List<(int A, int V, int C)>();
        foreach (var ch in chainsG)
            for (int k = 1; k + 1 < ch.Length; k++)
                if (regular[ch[k]] && ch[k - 1] != ch[k + 1]) triples.Add((ch[k - 1], ch[k], ch[k + 1]));

        // unknowns (in units of h): v (3N), n (3N)
        int nv = 6 * N;
        var x = new double[nv];
        for (int i = 0; i < N; i++)
        {
            var p = web.Nodes[i] * inv;
            var hit = proj.ClosestPoint(web.Nodes[i], proj.NearestVertexGlobal(web.Nodes[i]));
            var n = hit.SmoothNormal.Normalized();
            x[6 * i] = p.X; x[6 * i + 1] = p.Y; x[6 * i + 2] = p.Z;
            x[6 * i + 3] = n.X; x[6 * i + 4] = n.Y; x[6 * i + 5] = n.Z;
        }
        Vec3d V(int i) => new Vec3d(x[6 * i], x[6 * i + 1], x[6 * i + 2]);
        var start = new Vec3d[N];
        for (int i = 0; i < N; i++) start[i] = web.Nodes[i] * inv;
        Vec3d Nrm(int i) => new Vec3d(x[6 * i + 3], x[6 * i + 4], x[6 * i + 5]);

        (double star, double geo) Errors()
        {
            double s = 0, g = 0;
            for (int i = 0; i < N; i++)
            {
                if (!regular[i]) continue;
                var n = Nrm(i).Normalized();
                foreach (int j in web.NeighborsA[i].Concat(web.NeighborsB[i]))
                {
                    var e = V(j) - V(i); double l = e.Length;
                    if (l > 1e-12) s = Math.Max(s, Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(n, e)) / l)));
                }
            }
            foreach (var (a, v, c) in triples)
            {
                var m = Vec3d.Cross(V(a) - V(v), V(c) - V(v)); double l = m.Length;
                if (l > 1e-12) g = Math.Max(g, Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(Nrm(v).Normalized(), m)) / l)));
            }
            return (s * 180 / Math.PI, g * 180 / Math.PI);
        }
        (res.InitialStarError, res.InitialGeodesicError) = Errors();

        var fairTriples = new List<(int, int, int)>();
        foreach (var ch in chainsA.Concat(chainsB).Concat(chainsG))
            for (int k = 1; k + 1 < ch.Length; k++) fairTriples.Add((ch[k - 1], ch[k], ch[k + 1]));

        NormalEquations Assemble(double wf)
        {
            var sys = new NormalEquations(nv);
            // A-net stars and unit normals
            for (int i = 0; i < N; i++)
            {
                var n = Nrm(i);
                if (regular[i])
                {
                    foreach (int j in web.NeighborsA[i].Concat(web.NeighborsB[i]))
                    {
                        var e = V(j) - V(i);
                        sys.Row(1.0, Vec3d.Dot(n, e),
                            (6 * i + 3, e.X), (6 * i + 4, e.Y), (6 * i + 5, e.Z),
                            (6 * j, n.X), (6 * j + 1, n.Y), (6 * j + 2, n.Z),
                            (6 * i, -n.X), (6 * i + 1, -n.Y), (6 * i + 2, -n.Z));
                    }
                }
                sys.Row(1.0, Vec3d.Dot(n, n) - 1, (6 * i + 3, 2 * n.X), (6 * i + 4, 2 * n.Y), (6 * i + 5, 2 * n.Z));
                // proximity through the tangent plane at the closest point
                if (options.Proximity > 0)
                {
                    var p = V(i) * h;
                    var hit = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                    var np = hit.SmoothNormal.Normalized();
                    sys.Row(options.Proximity, Vec3d.Dot(V(i) - hit.Point * inv, np), (6 * i, np.X), (6 * i + 1, np.Y), (6 * i + 2, np.Z));
                }
                // border nodes (fewer than four neighbours) are held near their start
                // so they cannot slide off the reference surface along its tangent plane
                if (web.NeighborsA[i].Length + web.NeighborsB[i].Length < 4)
                {
                    var d0 = V(i) - start[i];
                    for (int k = 0; k < 3; k++) sys.Row(0.1, Comp(d0, k), (6 * i + k, 1.0));
                }
            }
            // geodesic diagonals: n · ((va − v) × (vc − v)) = 0, normalised by
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
        (res.StarError, res.GeodesicError) = Errors();

        res.Nodes = new Vec3d[N];
        res.Normals = new Vec3d[N];
        double maxDev = 0, sumDev = 0;
        for (int i = 0; i < N; i++)
        {
            res.Nodes[i] = V(i) * h;
            res.Normals[i] = Nrm(i).Normalized();
            var hit = proj.ClosestPoint(res.Nodes[i], proj.NearestVertexGlobal(res.Nodes[i]));
            double d = (hit.Point - res.Nodes[i]).Length;
            maxDev = Math.Max(maxDev, d); sumDev += d;
        }
        res.MaxDeviation = maxDev; res.MeanDeviation = sumDev / N;
        foreach (var ch in chainsA) res.A.Add(ch.Select(i => res.Nodes[i]).ToArray());
        foreach (var ch in chainsB) res.B.Add(ch.Select(i => res.Nodes[i]).ToArray());
        foreach (var ch in chainsG) res.G.Add(ch.Select(i => res.Nodes[i]).ToArray());
        return res;
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
    private sealed class NormalEquations
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
            var solver = SparseSymmetricSolver.Factor(b, 1e-18);
            return solver?.Solve(rhs);
        }
    }
}
