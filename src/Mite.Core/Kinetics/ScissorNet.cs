using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Numerics;

namespace Mite.Core.Kinetics;

/// <summary>
/// Kinetic simulation of a scissor-jointed lath net (a semi-compliant grid
/// mechanism). Laths are inextensible polylines through their joints; every
/// joint is a scissor hinge, so the distance between consecutive joints along
/// a lath never changes, and the laths stay asymptotic: every lath segment is
/// perpendicular to the (unknown) surface normal at both of its ends, which is
/// what keeps an upright lamella straight-unrollable through the motion.
///
/// Discrete model and energies after Wan, Crolla &amp; Schling 2025,
/// "Geometry-driven development of semi-compliant kinetic asymptotic
/// structures", Advanced Engineering Informatics 68, 103762 (§3): unknowns are
/// the joint positions v_i and a unit normal n_i per joint; the residuals are
///   asymptotic   n_a·(v_a − v_b) = 0 and n_b·(v_a − v_b) = 0 per segment,
///   unit normal  n_i·n_i − 1 = 0,
///   length       |v_a − v_b| − l_ab = 0 (constant joint spacing = scissor joint),
///   bending      change of (v_{i+1} − v_i)/l_{i+1} − (v_i − v_{i−1})/l_i, the turning
///                of the lath at a node, against its rest value carried in the
///                lath frame (Wan et al. use the plain second difference
///                2 v_i − v_{i−1} − v_{i+1}, which also penalises uneven joint
///                spacing on a straight lath and straightens curved ones —
///                inadmissible for a net that is given, not designed),
///   boundary     v_i − target_i (soft) and cable lengths |v_p − v_q| − d,
/// minimised in the least-squares sense; Wan et al. use a trust-region
/// reflective solver, here it is Levenberg–Marquardt on the sparse normal
/// equations (envelope LDLᵀ). The mechanism reading — rigid shearing at the
/// hinges plus compliant bending/twisting of the strips, motion governed by
/// geometry alone — is that of Schikore, Schling, Oberbichler &amp; Bauer 2020,
/// "Kinetics and Design of Semi-Compliant Grid Mechanisms" (AAG 2020), whose
/// doubly ruled grids (hyperboloid, hyperbolic paraboloid) are the exact test
/// cases: see <see cref="HyperboloidMechanism"/>.
///
/// Length and asymptotic residuals are dimensionless (divided by the segment
/// rest length), bending is a difference of unit vectors, targets and cables
/// are divided by the mean segment length, so the weights are comparable
/// across model scales.
/// </summary>
public sealed class ScissorNet
{
    /// <summary>Joint / lath-node positions of the rest state.</summary>
    public Vec3d[] Nodes { get; }

    /// <summary>Per lath: node indices in order along the lath.</summary>
    public IReadOnlyList<int[]> Laths { get; }

    /// <summary>Number of laths of family A (laths [0, CountA) are A, the rest B).</summary>
    public int CountA { get; }

    /// <summary>Rest length of every segment (per lath, per consecutive pair).</summary>
    public IReadOnlyList<double[]> RestLengths { get; }

    /// <summary>Initial unit normals per node (from the crossing laths or a mesh).</summary>
    public Vec3d[] Normals { get; }

    /// <summary>Per node: true when it is a joint (two laths meet), false for a plain lath node or free end.</summary>
    public bool[] IsJoint { get; }

    /// <summary>Per node: the laths passing through it.</summary>
    public IReadOnlyList<int[]> NodeLaths { get; }

    /// <summary>Mean segment rest length (the length scale of the residuals).</summary>
    public double Scale { get; }

    public sealed class Options
    {
        /// <summary>
        /// Weight of the hinge constraints (segment length, asymptotic
        /// perpendicularity, unit normal) relative to bending, targets and
        /// cables at weight 1: at the default a 1 % joint-spacing error costs
        /// as much as a 0.1 rad kink or a target miss of a tenth of a mean
        /// segment, so the mechanism stays a mechanism when the drivers ask for
        /// something it cannot do. Much larger values slow Levenberg–Marquardt
        /// down badly (the diagonal damping then over-damps the soft
        /// directions: 100 needed ~300 iterations where 10 needs 15 on the
        /// subdivided hyperboloid grid); the exact mechanisms are reached with
        /// any value, since all residuals vanish there.
        /// </summary>
        public double HingeWeight { get; set; } = 10.0;

        /// <summary>Nodes held at their rest position (removed from the unknowns).</summary>
        public IReadOnlyList<int> Fixed { get; set; } = Array.Empty<int>();

        /// <summary>Nodes pulled toward a target; targets per state come from <see cref="TargetsAt"/>.</summary>
        public IReadOnlyList<int> Driven { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Target positions of the driven nodes at fold parameter t ∈ [0, 1]
        /// (one per driven node). Default: none.
        /// </summary>
        public Func<double, Vec3d[]>? TargetsAt { get; set; }

        /// <summary>
        /// Sliding supports: nodes kept on the plane through their rest position
        /// with normal <see cref="SlideNormal"/> (Wan et al.'s ground nodes,
        /// E = Σ v_z²), free to move in it.
        /// </summary>
        public IReadOnlyList<int> Sliding { get; set; } = Array.Empty<int>();

        /// <summary>Normal of the sliding planes (default Z: nodes stay on the ground).</summary>
        public Vec3d SlideNormal { get; set; } = new Vec3d(0, 0, 1);

        /// <summary>Actuator cables (node pairs) whose length is prescribed by <see cref="CableLengthsAt"/>.</summary>
        public IReadOnlyList<(int p, int q)> Cables { get; set; } = Array.Empty<(int, int)>();

        /// <summary>Cable lengths at fold parameter t (one per cable).</summary>
        public Func<double, double[]>? CableLengthsAt { get; set; }

        /// <summary>Weight of the target residual (v − target)/scale; 1 = as strong as a length constraint.</summary>
        public double TargetWeight { get; set; } = 1.0;

        /// <summary>Weight of the cable residual (|v_p − v_q| − d)/scale.</summary>
        public double CableWeight { get; set; } = 1.0;

        /// <summary>
        /// Bending stiffness of the laths: weight of the change of the turning
        /// at every interior lath node (difference of consecutive unit segment
        /// directions, ≈ l·κ) relative to the rest state, the rest turning
        /// being carried along in the lath's moving frame. Straight rods stay
        /// straight, pre-bent laths keep their geodesic curvature, and the rest
        /// geometry is stress-free. 0 lets laths kink freely at every node (the
        /// discrete grid then has many more mechanisms than the smooth one).
        /// This replaces the spacing-dependent absolute second difference of
        /// Wan et al., which would straighten curved laths and penalise uneven
        /// joint spacing on straight ones.
        /// </summary>
        public double Fairness { get; set; } = 1.0;

        /// <summary>Number of states after the rest state: fold parameters k / Steps, k = 0..Steps.</summary>
        public int Steps { get; set; } = 10;

        /// <summary>Largest fold parameter to reach (states stop at Fold).</summary>
        public double Fold { get; set; } = 1.0;

        /// <summary>Levenberg–Marquardt iterations per state.</summary>
        public int MaxIterations { get; set; } = 60;

        /// <summary>Convergence: largest absolute weighted residual below this (dimensionless; hinge residuals carry <see cref="HardWeight"/>).</summary>
        public double Tolerance { get; set; } = 1e-9;

        /// <summary>Polled between iterations; true stops the solve (partial states are returned).</summary>
        public Func<bool>? Cancel { get; set; }

        /// <summary>Optional per-iteration trace (cost, damping) for debugging.</summary>
        public Action<string>? Log { get; set; }
    }

    /// <summary>One configuration of the mechanism.</summary>
    public sealed class State
    {
        public double Fold;
        public Vec3d[] Nodes = Array.Empty<Vec3d>();
        public Vec3d[] Normals = Array.Empty<Vec3d>();
        /// <summary>Largest |Δlength| / rest length over all segments.</summary>
        public double LengthDrift;
        /// <summary>Largest angle (degrees) between a segment and the tangent plane at its ends.</summary>
        public double AsymptoticDeviation;
        /// <summary>Largest distance of a driven node from its target (model units).</summary>
        public double TargetMiss;
        /// <summary>Largest |cable length − prescribed| (model units).</summary>
        public double CableMiss;
        /// <summary>Largest distance of a sliding node from its plane (model units).</summary>
        public double SlideMiss;
        /// <summary>Largest absolute weighted residual at the end of the solve.</summary>
        public double Residual;
        public int Iterations;
        public bool Converged;
    }

    public sealed class Result
    {
        public List<State> States = new List<State>();
        public bool Cancelled;
        public State Last => States[States.Count - 1];
    }

    // ------------------------------------------------------------------ construction

    /// <summary>
    /// Builds the net from explicit laths (node index sequences through a shared
    /// node array). countA laths belong to family A. Normals are optional; when
    /// absent they are taken from the crossing lath directions at joints and
    /// propagated along the laths elsewhere.
    /// </summary>
    public ScissorNet(Vec3d[] nodes, IReadOnlyList<int[]> laths, int countA, Vec3d[]? normals = null)
    {
        if (nodes == null || nodes.Length == 0) throw new ArgumentException("No nodes.", nameof(nodes));
        Nodes = (Vec3d[])nodes.Clone();
        Laths = laths.Select(l => (int[])l.Clone()).ToList();
        CountA = countA;

        var rest = new List<double[]>();
        double sum = 0; int count = 0;
        foreach (var lath in Laths)
        {
            var r = new double[Math.Max(lath.Length - 1, 0)];
            for (int i = 0; i + 1 < lath.Length; i++)
            {
                r[i] = (Nodes[lath[i + 1]] - Nodes[lath[i]]).Length;
                if (r[i] <= 0) throw new ArgumentException("A lath has a zero-length segment (repeated node).");
                sum += r[i]; count++;
            }
            rest.Add(r);
        }
        RestLengths = rest;
        Scale = count > 0 ? sum / count : 1.0;

        var nodeLaths = new List<int>[Nodes.Length];
        for (int i = 0; i < nodeLaths.Length; i++) nodeLaths[i] = new List<int>();
        for (int l = 0; l < Laths.Count; l++)
            foreach (int v in Laths[l])
                if (!nodeLaths[v].Contains(l)) nodeLaths[v].Add(l);
        NodeLaths = nodeLaths.Select(x => x.ToArray()).ToList();
        IsJoint = NodeLaths.Select(x => x.Length >= 2).ToArray();

        Normals = normals != null && normals.Length == Nodes.Length
            ? normals.Select(n => n.Normalized()).ToArray()
            : InitialNormals();
        // Any normal still zero (a straight isolated lath): pick something perpendicular
        for (int i = 0; i < Normals.Length; i++)
            if (Normals[i].LengthSquared < 0.5)
            {
                var t = LathTangent(i);
                var a = Math.Abs(t.Z) < 0.9 ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
                Normals[i] = Vec3d.Cross(t, a).Normalized();
            }
    }

    /// <summary>
    /// Builds the net from a <see cref="NetTopology.Result"/>: joints become
    /// nodes, free tails get an end node, and members longer than maxSegment
    /// are subdivided into equal pieces (extra lath nodes, so laths can bend
    /// between joints). maxSegment ≤ 0 keeps joints only.
    /// </summary>
    public static ScissorNet FromTopology(NetTopology.Result topo, int lathCount, int countA, double maxSegment = 0.0, Func<Vec3d, Vec3d>? normalAt = null)
    {
        var nodes = new List<Vec3d>(topo.Nodes);
        var perLath = new List<(int start, int end, Vec3d[] pts)>[lathCount];
        for (int i = 0; i < lathCount; i++) perLath[i] = new List<(int, int, Vec3d[])>();
        foreach (var m in topo.Members)
            if (m.Lath >= 0 && m.Lath < lathCount) perLath[m.Lath].Add((m.NodeStart, m.NodeEnd, m.Points));

        var laths = new List<int[]>();
        var lathIds = new List<int>();
        for (int l = 0; l < lathCount; l++)
        {
            var seq = new List<int>();
            var members = perLath[l];
            for (int k = 0; k < members.Count; k++)
            {
                var (start, end, pts) = members[k];
                int s = start;
                if (s < 0) { s = nodes.Count; nodes.Add(pts[0]); }
                if (seq.Count == 0 || seq[seq.Count - 1] != s) seq.Add(s);
                // interior subdivision nodes along the member polyline
                double len = 0; for (int i = 1; i < pts.Length; i++) len += (pts[i] - pts[i - 1]).Length;
                if (maxSegment > 0 && len > maxSegment * 1.0001)
                {
                    int pieces = (int)Math.Ceiling(len / maxSegment - 1e-9);
                    for (int p = 1; p < pieces; p++)
                    {
                        var q = PointAtArc(pts, len * p / pieces);
                        seq.Add(nodes.Count); nodes.Add(q);
                    }
                }
                int e = end;
                if (e < 0) { e = nodes.Count; nodes.Add(pts[pts.Length - 1]); }
                seq.Add(e);
            }
            if (seq.Count >= 2) { laths.Add(seq.ToArray()); lathIds.Add(l); }
        }
        int a = lathIds.Count(id => id < countA);
        Vec3d[]? normals = null;
        if (normalAt != null) normals = nodes.Select(normalAt).ToArray();
        var net = new ScissorNet(nodes.ToArray(), laths, a, normals);
        net.SourceLaths = lathIds.ToArray();
        return net;
    }

    /// <summary>For nets built by <see cref="FromTopology"/>: input lath index of every lath (family A first, then B).</summary>
    public int[]? SourceLaths { get; private set; }

    private static Vec3d PointAtArc(Vec3d[] pts, double s)
    {
        double acc = 0;
        for (int i = 1; i < pts.Length; i++)
        {
            double d = (pts[i] - pts[i - 1]).Length;
            if (acc + d >= s) { double t = d > 0 ? (s - acc) / d : 0; return pts[i - 1] + t * (pts[i] - pts[i - 1]); }
            acc += d;
        }
        return pts[pts.Length - 1];
    }

    private Vec3d LathTangent(int node)
    {
        foreach (int l in NodeLaths[node])
        {
            var lath = Laths[l];
            int k = Array.IndexOf(lath, node);
            int i0 = Math.Max(0, k - 1), i1 = Math.Min(lath.Length - 1, k + 1);
            var t = Nodes[lath[i1]] - Nodes[lath[i0]];
            if (t.LengthSquared > 0) return t.Normalized();
        }
        return new Vec3d(1, 0, 0);
    }

    private Vec3d[] InitialNormals()
    {
        var n = new Vec3d[Nodes.Length];
        // joints: cross product of the two lath tangents
        for (int v = 0; v < Nodes.Length; v++)
        {
            if (NodeLaths[v].Length < 2) continue;
            var tangents = new List<Vec3d>();
            foreach (int l in NodeLaths[v])
            {
                var lath = Laths[l]; int k = Array.IndexOf(lath, v);
                int i0 = Math.Max(0, k - 1), i1 = Math.Min(lath.Length - 1, k + 1);
                tangents.Add((Nodes[lath[i1]] - Nodes[lath[i0]]).Normalized());
            }
            var c = Vec3d.Cross(tangents[0], tangents[1]);
            if (c.Length > 1e-6) n[v] = c.Normalized();
        }
        // propagate along laths to non-joint nodes (free ends, subdivision nodes)
        for (int pass = 0; pass < 2; pass++)
            foreach (var lath in Laths)
            {
                // forward / backward fill of empty normals from the nearest known one, transported along the lath
                for (int dir = 0; dir < 2; dir++)
                {
                    Vec3d known = Vec3d.Zero; int knownIdx = -1;
                    for (int s = 0; s < lath.Length; s++)
                    {
                        int i = dir == 0 ? s : lath.Length - 1 - s;
                        int v = lath[i];
                        if (n[v].LengthSquared > 0.5) { known = n[v]; knownIdx = i; continue; }
                        if (knownIdx < 0) continue;
                        // make the transported normal perpendicular to the local tangent
                        int i0 = Math.Max(0, i - 1), i1 = Math.Min(lath.Length - 1, i + 1);
                        var t = (Nodes[lath[i1]] - Nodes[lath[i0]]).Normalized();
                        var m = known - Vec3d.Dot(known, t) * t;
                        if (m.Length > 1e-9) { n[v] = m.Normalized(); known = n[v]; knownIdx = i; }
                    }
                }
            }
        return n;
    }
    private struct Bend
    {
        public int Prev, Node, Next;
        public double LPrev, LNext;
        /// <summary>Rest turning vector components along the lath, along the normal and in the tangent plane.</summary>
        public double Along, Normal, InPlane;
    }

    // ------------------------------------------------------------------ solve

    /// <summary>
    /// Runs the motion: state 0 settles the rest geometry onto the constraint
    /// manifold (targets and cables at t = 0), then every further state moves
    /// the drivers to fold t_k = Fold · k / Steps and re-solves from the
    /// previous state.
    /// </summary>
    public Result Solve(Options? options = null)
    {
        var opt = options ?? new Options();
        int n = Nodes.Length;
        var fixedSet = new HashSet<int>(opt.Fixed);
        int freeCount = 0;
        // unknown layout per node: position (3, absent for fixed nodes) then normal (3, always free)
        var dof = new int[n];
        var ndof = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (fixedSet.Contains(i)) dof[i] = -1; else { dof[i] = freeCount; freeCount += 3; }
            ndof[i] = freeCount; freeCount += 3;
        }

        var pos = (Vec3d[])Nodes.Clone();
        var nrm = (Vec3d[])Normals.Clone();
        var result = new Result();

        // Residual list (rebuilt every iteration; structure is fixed)
        var segs = new List<(int a, int b, double l)>();
        for (int l = 0; l < Laths.Count; l++)
            for (int i = 0; i + 1 < Laths[l].Length; i++) segs.Add((Laths[l][i], Laths[l][i + 1], RestLengths[l][i]));
        var fair = new List<Bend>();
        if (opt.Fairness > 0)
            for (int l = 0; l < Laths.Count; l++)
                for (int i = 1; i + 1 < Laths[l].Length; i++)
                {
                    int p = Laths[l][i - 1], v = Laths[l][i], q = Laths[l][i + 1];
                    double lp = RestLengths[l][i - 1], lq = RestLengths[l][i];
                    // rest turning vector in the rest frame (t along the lath, n the normal, b = n × t)
                    var d0 = (Nodes[q] - Nodes[v]) / lq - (Nodes[v] - Nodes[p]) / lp;
                    var t0 = (Nodes[q] - Nodes[p]).Normalized();
                    var n0 = Normals[v];
                    n0 = (n0 - Vec3d.Dot(n0, t0) * t0).Normalized();
                    var b0 = Vec3d.Cross(n0, t0);
                    fair.Add(new Bend { Prev = p, Node = v, Next = q, LPrev = lp, LNext = lq, Along = Vec3d.Dot(d0, t0), Normal = Vec3d.Dot(d0, n0), InPlane = Vec3d.Dot(d0, b0) });
                }
        double wF = Math.Sqrt(Math.Max(0, opt.Fairness));
        double wT = opt.TargetWeight / Scale;
        double wC = opt.CableWeight / Scale;

        int steps = Math.Max(1, opt.Steps);
        for (int k = 0; k <= steps; k++)
        {
            double t = opt.Fold * k / steps;
            var targets = opt.TargetsAt != null && opt.Driven.Count > 0 ? opt.TargetsAt(t) : Array.Empty<Vec3d>();
            var cableLen = opt.CableLengthsAt != null && opt.Cables.Count > 0 ? opt.CableLengthsAt(t) : Array.Empty<double>();

            var st = SolveState(opt, t, pos, nrm, dof, ndof, freeCount, segs, fair, wF, wT, wC, targets, cableLen);
            result.States.Add(st);
            if (opt.Cancel != null && opt.Cancel()) { result.Cancelled = true; break; }
        }
        return result;
    }

    private State SolveState(Options opt, double t, Vec3d[] pos, Vec3d[] nrm, int[] dof, int[] ndof, int freeCount,
        List<(int a, int b, double l)> segs, List<Bend> fair,
        double wF, double wT, double wC, Vec3d[] targets, double[] cableLen)
    {
        // residual blocks: per seg 3 (length, asym a, asym b); per node 1 (unit);
        // per fair 3; per driven 3; per cable 1
        int nDriven = Math.Min(opt.Driven.Count, targets.Length);
        int nCable = Math.Min(opt.Cables.Count, cableLen.Length);
        int nSlide = opt.Sliding.Count;
        int m = segs.Count * 3 + Nodes.Length + fair.Count * 3 + nDriven * 3 + nCable + nSlide;
        var r = new double[m];
        // Jacobian in triplets per residual row: (col, value) lists
        var rowsCols = new List<int>[m];
        var rowsVals = new List<double>[m];
        for (int i = 0; i < m; i++) { rowsCols[i] = new List<int>(12); rowsVals[i] = new List<double>(12); }

        var slideN = opt.SlideNormal.Normalized();
        double cost = Evaluate(pos, nrm, dof, ndof, segs, fair, wF, wT, wC, opt, nDriven, nCable, targets, cableLen, slideN, r, rowsCols, rowsVals);
        double lambda = 1e-4;
        var st = new State { Fold = t };
        int it = 0, slow = 0;
        bool converged = MaxAbs(r) < opt.Tolerance;
        var newPos = new Vec3d[pos.Length];
        var newNrm = new Vec3d[nrm.Length];

        while (!converged && it < opt.MaxIterations)
        {
            it++;
            // normal equations
            var builder = new SparseSymmetricSolver.Builder(freeCount);
            var g = new double[freeCount];
            for (int i = 0; i < m; i++)
            {
                var cols = rowsCols[i]; var vals = rowsVals[i];
                double ri = r[i];
                for (int p = 0; p < cols.Count; p++)
                {
                    g[cols[p]] += vals[p] * ri;
                    for (int q = p; q < cols.Count; q++)
                        builder.Add(cols[p], cols[q], vals[p] * vals[q]);
                }
            }
            var diag = new double[freeCount];
            double maxDiag = 0;
            for (int c = 0; c < freeCount; c++) { diag[c] = builder.Get(c, c); maxDiag = Math.Max(maxDiag, diag[c]); }
            if (maxDiag <= 0) break;

            bool accepted = false;
            for (int attempt = 0; attempt < 12 && !accepted; attempt++)
            {
                // damp in place (restored below) instead of copying the matrix
                for (int c = 0; c < freeCount; c++) builder.Add(c, c, lambda * Math.Max(diag[c], 1e-12 * maxDiag) + 1e-14 * maxDiag);
                var solver = SparseSymmetricSolver.Factor(builder, 1e-16);
                for (int c = 0; c < freeCount; c++) builder.Rows[c][c] = diag[c];
                if (solver == null) { lambda *= 10; continue; }
                var rhs = new double[freeCount];
                for (int c = 0; c < freeCount; c++) rhs[c] = -g[c];
                var delta = solver.Solve(rhs);

                for (int v = 0; v < pos.Length; v++)
                {
                    int d = dof[v], nd = ndof[v];
                    newPos[v] = d < 0 ? pos[v] : pos[v] + new Vec3d(delta[d], delta[d + 1], delta[d + 2]);
                    newNrm[v] = nrm[v] + new Vec3d(delta[nd], delta[nd + 1], delta[nd + 2]);
                }
                var r2 = new double[m];
                var rc2 = new List<int>[m]; var rv2 = new List<double>[m];
                for (int i = 0; i < m; i++) { rc2[i] = new List<int>(12); rv2[i] = new List<double>(12); }
                double cost2 = Evaluate(newPos, newNrm, dof, ndof, segs, fair, wF, wT, wC, opt, nDriven, nCable, targets, cableLen, slideN, r2, rc2, rv2);
                opt.Log?.Invoke($"it {it} attempt {attempt} lambda {lambda:E1} cost {cost:E3} -> {cost2:E3} maxr {MaxAbs(r2):E2}");
                if (cost2 < cost || double.IsNaN(cost))
                {
                    accepted = true;
                    double costBefore = cost;
                    Array.Copy(newPos, pos, pos.Length); Array.Copy(newNrm, nrm, nrm.Length);
                    double stepNorm = 0; foreach (double x in delta) stepNorm += x * x;
                    cost = cost2; r = r2; rowsCols = rc2; rowsVals = rv2;
                    lambda = Math.Max(lambda / 10, 1e-15);
                    double gain = cost2 <= 0 ? 1 : (costBefore - cost2) / Math.Max(costBefore, 1e-300);
                    slow = gain < 1e-6 ? slow + 1 : 0;
                    converged = MaxAbs(r) < opt.Tolerance
                        || Math.Sqrt(stepNorm) < 1e-13 * Math.Max(1, Scale) * Math.Sqrt(freeCount)
                        || slow >= 3;   // stationary: the drivers ask for more than the mechanism allows
                }
                else lambda *= 10;
            }
            if (!accepted) { converged = true; break; }   // no descent direction at any damping: stationary point
        }

        st.Iterations = it;
        st.Converged = converged || MaxAbs(r) < opt.Tolerance;
        st.Residual = MaxAbs(r);
        st.Nodes = (Vec3d[])pos.Clone();
        st.Normals = nrm.Select(v => v.Normalized()).ToArray();
        // diagnostics in geometric terms
        double drift = 0, asym = 0;
        foreach (var (a, b, l) in segs)
        {
            var e = pos[a] - pos[b]; double len = e.Length;
            drift = Math.Max(drift, Math.Abs(len - l) / l);
            if (len > 0)
            {
                var u = e / len;
                asym = Math.Max(asym, Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(u, st.Normals[a])))));
                asym = Math.Max(asym, Math.Asin(Math.Min(1, Math.Abs(Vec3d.Dot(u, st.Normals[b])))));
            }
        }
        st.LengthDrift = drift;
        st.AsymptoticDeviation = asym * 180 / Math.PI;
        double miss = 0;
        for (int i = 0; i < nDriven; i++) miss = Math.Max(miss, (pos[opt.Driven[i]] - targets[i]).Length);
        st.TargetMiss = miss;
        double cm = 0;
        for (int i = 0; i < nCable; i++) cm = Math.Max(cm, Math.Abs((pos[opt.Cables[i].p] - pos[opt.Cables[i].q]).Length - cableLen[i]));
        st.CableMiss = cm;
        double sm = 0;
        foreach (int v in opt.Sliding) sm = Math.Max(sm, Math.Abs(Vec3d.Dot(pos[v] - Nodes[v], slideN)));
        st.SlideMiss = sm;
        return st;
    }

    private static double MaxAbs(double[] r) { double m = 0; foreach (double x in r) m = Math.Max(m, Math.Abs(x)); return m; }

    /// <summary>Fills residuals and Jacobian rows; returns ½ Σ r².</summary>
    private double Evaluate(Vec3d[] pos, Vec3d[] nrm, int[] dof, int[] ndof,
        List<(int a, int b, double l)> segs, List<Bend> fair,
        double wF, double wT, double wC, Options opt, int nDriven, int nCable, Vec3d[] targets, double[] cableLen, Vec3d slideN,
        double[] r, List<int>[] cols, List<double>[] vals)
    {
        int row = 0;
        void Add(int node, int comp, bool normal, double value)
        {
            int d = normal ? ndof[node] : dof[node]; if (d < 0) return;
            int col = d + comp;
            var rc = cols[row];
            for (int i = 0; i < rc.Count; i++) if (rc[i] == col) { vals[row][i] += value; return; }
            rc.Add(col); vals[row].Add(value);
        }
        void AddVec(int node, bool normal, Vec3d v) { Add(node, 0, normal, v.X); Add(node, 1, normal, v.Y); Add(node, 2, normal, v.Z); }

        double wH = opt.HingeWeight;
        foreach (var (a, b, l) in segs)
        {
            var e = pos[a] - pos[b]; double len = e.Length;
            double il = wH / l;
            // length (scissor joint spacing)
            r[row] = (len - l) * il;
            if (len > 1e-300) { var u = e * (il / len); AddVec(a, false, u); AddVec(b, false, -u); }
            row++;
            // asymptotic at a
            r[row] = Vec3d.Dot(nrm[a], e) * il;
            AddVec(a, false, nrm[a] * il); AddVec(b, false, -(nrm[a] * il)); AddVec(a, true, e * il);
            row++;
            // asymptotic at b
            r[row] = Vec3d.Dot(nrm[b], e) * il;
            AddVec(a, false, nrm[b] * il); AddVec(b, false, -(nrm[b] * il)); AddVec(b, true, e * il);
            row++;
        }
        for (int v = 0; v < pos.Length; v++)
        {
            r[row] = wH * (Vec3d.Dot(nrm[v], nrm[v]) - 1);
            AddVec(v, true, 2 * wH * nrm[v]);
            row++;
        }
        foreach (var bd in fair)
        {
            // turning of the lath at v (difference of the segment directions, rest lengths as constants)
            // minus the rest turning carried along in the current frame (t along the lath, n the node normal):
            // zero when the lath keeps its rest bend, so a straight rod stays straight and a pre-bent
            // lath keeps its geodesic curvature unless the hinges force otherwise
            int p = bd.Prev, v = bd.Node, q = bd.Next;
            double cp = wF / bd.LPrev, cq = wF / bd.LNext;
            var d = cq * (pos[q] - pos[v]) - cp * (pos[v] - pos[p]);
            Vec3d Rest(Vec3d vp, Vec3d vq, Vec3d nv)
            {
                var t = (vq - vp).Normalized();
                var nn = (nv - Vec3d.Dot(nv, t) * t).Normalized();
                return wF * (bd.Along * t + bd.Normal * nn + bd.InPlane * Vec3d.Cross(nn, t));
            }
            var rest = Rest(pos[p], pos[q], nrm[v]);
            var f = d - rest;
            // Jacobian: the turning is linear; the rest term's dependence on the frame (v_p, v_q, n_v) by central differences
            double hp = 1e-6 * Math.Max(bd.LPrev, bd.LNext), hn = 1e-6;
            var dRest = new Vec3d[9];
            for (int c = 0; c < 3; c++)
            {
                var e = new Vec3d(c == 0 ? 1 : 0, c == 1 ? 1 : 0, c == 2 ? 1 : 0);
                dRest[c] = (Rest(pos[p] + hp * e, pos[q], nrm[v]) - Rest(pos[p] - hp * e, pos[q], nrm[v])) / (2 * hp);
                dRest[3 + c] = (Rest(pos[p], pos[q] + hp * e, nrm[v]) - Rest(pos[p], pos[q] - hp * e, nrm[v])) / (2 * hp);
                dRest[6 + c] = (Rest(pos[p], pos[q], nrm[v] + hn * e) - Rest(pos[p], pos[q], nrm[v] - hn * e)) / (2 * hn);
            }
            for (int c = 0; c < 3; c++)
            {
                r[row] = f[c];
                Add(v, c, false, -(cq + cp)); Add(p, c, false, cp); Add(q, c, false, cq);
                for (int k = 0; k < 3; k++)
                {
                    Add(p, k, false, -dRest[k][c]);
                    Add(q, k, false, -dRest[3 + k][c]);
                    Add(v, k, true, -dRest[6 + k][c]);
                }
                row++;
            }
        }
        for (int i = 0; i < nDriven; i++)
        {
            int v = opt.Driven[i];
            var f = wT * (pos[v] - targets[i]);
            for (int c = 0; c < 3; c++) { r[row] = f[c]; Add(v, c, false, wT); row++; }
        }
        for (int i = 0; i < nCable; i++)
        {
            var (p, q) = opt.Cables[i];
            var e = pos[p] - pos[q]; double len = e.Length;
            r[row] = wC * (len - cableLen[i]);
            if (len > 1e-300) { var u = e * (wC / len); AddVec(p, false, u); AddVec(q, false, -u); }
            row++;
        }

        foreach (int v in opt.Sliding)
        {
            r[row] = wT * Vec3d.Dot(pos[v] - Nodes[v], slideN);
            AddVec(v, false, wT * slideN);
            row++;
        }
        double cost = 0; foreach (double x in r) cost += x * x;
        return 0.5 * cost;
    }

    // ------------------------------------------------------------------ read-outs

    /// <summary>Polylines of the laths in a state (family A first, then B).</summary>
    public List<Vec3d[]> LathPolylines(State s) =>
        Laths.Select(l => l.Select(v => s.Nodes[v]).ToArray()).ToList();

    /// <summary>
    /// Crossing angle (degrees, 0–90) at every joint of a state: the angle
    /// between the two lath tangents there (the scissor / rhombus angle). NaN
    /// at non-joint nodes.
    /// </summary>
    public double[] CrossingAngles(State s)
    {
        var a = new double[Nodes.Length];
        for (int v = 0; v < Nodes.Length; v++)
        {
            if (NodeLaths[v].Length < 2) { a[v] = double.NaN; continue; }
            var ts = new List<Vec3d>();
            foreach (int l in NodeLaths[v])
            {
                var lath = Laths[l]; int k = Array.IndexOf(lath, v);
                int i0 = Math.Max(0, k - 1), i1 = Math.Min(lath.Length - 1, k + 1);
                ts.Add((s.Nodes[lath[i1]] - s.Nodes[lath[i0]]).Normalized());
            }
            a[v] = Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(ts[0], ts[1])))) * 180 / Math.PI;
        }
        return a;
    }

    /// <summary>Nearest node to a point (for picking supports and drivers).</summary>
    public int NearestNode(Vec3d p)
    {
        int best = -1; double bd = double.MaxValue;
        for (int i = 0; i < Nodes.Length; i++)
        {
            double d = (Nodes[i] - p).LengthSquared;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    // ------------------------------------------------------------------ exact mechanism

    /// <summary>
    /// The doubly ruled scissor grid of Schikore et al. 2020 in closed form:
    /// n straight rods per family, all tangent to the circle of radius rho in
    /// the flat state, pinned to each other where they cross. Tilting every rod
    /// by the angle beta about its tangent point turns the flat grid into a
    /// hyperboloid of one sheet — the rods are its rulings, so the grid is an
    /// asymptotic net — with waist radius rho·cos(beta) and every joint at
    /// signed rod position s_m = rho·tan(γ_m / 2), γ_m = 2π m / n the azimuth
    /// difference of the two rods meeting there, independent of beta: a
    /// one-parameter mechanism with constant joint spacing. A node of rod k
    /// (azimuth φ = 2πk/n) at position s is
    ///   (cosβ (ρ cosφ − s sinφ), cosβ (ρ sinφ + s cosφ), s sinβ).
    /// Family B rods are the mirror images (z → −z), so the B rod through the
    /// same tangent point crosses the A rod there at the waist. levels = joints per rod
    /// on each side of the waist; returns node positions, laths (A then B,
    /// n each) and the exact normals.
    /// </summary>
    public static (Vec3d[] nodes, int[][] laths, Vec3d[] normals) HyperboloidMechanism(int n, int levels, double rho, double beta)
    {
        // joint (k, m): A rod k crosses B rod j = k + m (mod n) at s = rho tan(π m / n) on A, −s on B
        int rows = 2 * levels + 1;
        var nodes = new Vec3d[n * rows];
        var normals = new Vec3d[n * rows];
        double cb = Math.Cos(beta), sb = Math.Sin(beta);
        Vec3d Rod(int k, double s)
        {
            double phi = 2 * Math.PI * k / n;
            return new Vec3d(cb * (rho * Math.Cos(phi) - s * Math.Sin(phi)), cb * (rho * Math.Sin(phi) + s * Math.Cos(phi)), s * sb);
        }
        Vec3d RodDir(int k, double sign)
        {
            double phi = 2 * Math.PI * k / n;
            return new Vec3d(-cb * Math.Sin(phi), cb * Math.Cos(phi), sign * sb).Normalized();
        }
        for (int k = 0; k < n; k++)
            for (int m = -levels; m <= levels; m++)
            {
                double s = rho * Math.Tan(Math.PI * m / n);
                int idx = k * rows + (m + levels);
                nodes[idx] = Rod(k, s);
                int j = ((k + m) % n + n) % n;
                normals[idx] = Vec3d.Cross(RodDir(k, 1), RodDir(j, -1)).Normalized();
            }
        var laths = new int[2 * n][];
        for (int k = 0; k < n; k++)
        {
            var a = new int[rows];
            for (int m = -levels; m <= levels; m++) a[m + levels] = k * rows + (m + levels);
            laths[k] = a;
        }
        for (int j = 0; j < n; j++)
        {
            // B rod j passes through joints (k, m) with k + m ≡ j, at B-position −s_m: order by −m ascending → m descending
            var b = new int[rows];
            for (int m = levels; m >= -levels; m--)
            {
                int k = ((j - m) % n + n) % n;
                b[levels - m] = k * rows + (m + levels);
            }
            laths[n + j] = b;
        }
        return (nodes, laths, normals);
    }
}
