using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Numerics;

namespace Mite.Core.FormFinding;

/// <summary>
/// Planarization of quad-dominant meshes by guided projection — the
/// regularised Gauss–Newton iteration of Tang, Sun, Gomes, Wallner &amp;
/// Pottmann 2014 ("Form-finding with polyhedral meshes made simple", ACM
/// TOG 33(4), §3–4) with the objective terms of Liu, Pottmann, Wallner, Yang
/// &amp; Wang 2006 ("Geometric modeling with conical meshes and developable
/// surfaces", SIGGRAPH, §3.2: fairness + closeness, soft weights decaying
/// towards zero) and Zadravec, Schiftner &amp; Wallner 2010 ("Designing
/// quad-dominant meshes with planar faces", CGF 29(5), eq. 8: fairness
/// relative to the input's second differences, proximity through the tangent
/// plane at the closest point).
/// <para>
/// Unknowns: the free vertices v and one auxiliary normal n_f per face with
/// four or more corners. Hard constraints (weight 1), both bilinear:
/// <list type="bullet">
/// <item>n_f · (v_i − v_j) = 0 for every edge v_i v_j of face f (planarity;
/// Tang et al. use all n edges of an n-gon "for reasons of symmetry");</item>
/// <item>n_f · n_f = 1 (unit normal).</item>
/// </list>
/// The planarity residual is divided by the edge's rest length (the sine of
/// the edge's angle to the face plane): the raw bilinear form scales with the
/// edge, and a least-squares path would lower it by shrinking the quad.
/// Soft terms, each weight times <see cref="Options.Decay"/> per iteration:
/// <list type="bullet">
/// <item>fairness ‖Δ²(v) − Δ²₀(v)‖² along the mesh polylines (for an edge
/// v a, the opposite edge v c is the one sharing no face with it; irregular
/// vertices fall back to the graph Laplacian), so the net keeps its shape,
/// the input is cost-free and quads do not crumple;</item>
/// <item>closeness ((v − p) · n_p)² to the tangent plane of the reference
/// surface at the closest point p — the Gauss–Newton approximation of the
/// squared point-to-surface distance, exact to second order;</item>
/// <item>anchor: the displacement from the start projected onto the
/// reference's tangent plane, so nodes slide along the surface only as much
/// as planarity needs (the closeness alone leaves sliding free);</item>
/// <item>optionally the edge lengths (<see cref="Options.Stretch"/>, off);</item>
/// <item>ε² ‖Δx‖² with ε = 10⁻³ (Tang et al.'s proximity to the current
/// iterate), which makes the normal matrix definite on the manifold's
/// tangent directions.</item>
/// </list>
/// Everything is measured in units of the mean edge length of the constrained
/// faces, so the weights mean the same on every model. One step linearises the
/// constraints at the iterate x_k and solves (JᵀJ + (ε² + λ)I) Δ = −Jᵀr, the
/// face normals eliminated first by a 3 × 3 Schur complement per face and the
/// vertex system factored by the envelope LDLᵀ of <see cref="SparseSymmetricSolver"/>;
/// λ is Levenberg–Marquardt damping on the gain ratio (Nielsen's update), a
/// step that drops a face below a quarter of its area is refused (the folded
/// quad is the degenerate planar solution), and near the manifold a step that
/// halves the hard cost is taken even when the soft cost rises slightly.
/// Because the constraints are bilinear the violation after a step is of
/// second order in the step, so the planarity residual converges quadratically
/// once the soft terms are small; with constant weights the iteration stops at
/// a compromise between planarity and closeness (Hᵀφ = −Kᵀ(Kx − s) at the
/// fixed point), which is why Liu et al. let the soft weights tend to zero —
/// <see cref="Options.Decay"/> &lt; 1 does that here, so a conjugate net stays
/// on its surface and an asymptotic net, which has no planar realisation on
/// it, either stalls (<see cref="Result.Stalled"/>) or becomes planar by
/// leaving it; the result reports how far.
/// </para>
/// </summary>
public static class PlanarizationOptimizer
{
    public class Options
    {
        /// <summary>Gauss–Newton iterations (Tang et al.: "at most about 10"; the loop stops earlier at Tolerance).</summary>
        public int Iterations { get; set; } = 30;

        /// <summary>Stop when the largest corner distance from its face's best-fit plane (model units) falls below this.</summary>
        public double Tolerance { get; set; } = 1e-6;

        /// <summary>
        /// Weight of the relative fairness term against the hard constraints (weight 1). It is what keeps the
        /// quads from crumpling where planarity is hard to reach: at 0.1 a noisy 24 × 24 grid with its border
        /// held folds quads into the fold guard and stalls, at 0.5 and above it planarizes with every quad
        /// above 0.39 (0.5) / 0.55 (1.0) of its area; the price is that a jittered input keeps more of its
        /// jitter (a conjugate net knocked 0.02 off its surface ends 0.009 / 0.011 / 0.014 off at 0.3 / 0.5 / 1.0).
        /// </summary>
        public double Fairness { get; set; } = 1.0;

        /// <summary>Weight of the closeness to the reference surface (0 = none).</summary>
        public double Closeness { get; set; } = 1.0;

        /// <summary>
        /// Weight of the tangential anchor: the displacement from the start position projected onto the
        /// reference's tangent plane (two rows per vertex). The closeness term leaves sliding along the
        /// surface free, and a net far from any planar realisation then planarizes by letting its quads
        /// slide into thin ones (Liu et al. 2006 §3.2 on thin quads); the anchor makes the result the
        /// nearest planar mesh instead. Decays with the other soft weights.
        /// </summary>
        public double Anchor { get; set; } = 0.3;

        /// <summary>
        /// Weight of the edge-length term (|v_i − v_j|² − l₀²) / (2 l₀) ≈ |v_i − v_j| − l₀ on every edge of
        /// a constrained face: keeps the quads from thinning out where planarity is hard to reach (the
        /// rim quads of an asymptotic net), as Liu et al.'s determinant constraints do for strips. Decays
        /// with the other soft weights.
        /// </summary>
        public double Stretch { get; set; } = 0.3;

        /// <summary>
        /// Factor applied to the soft weights after every iteration. Liu et al. 2006 halve them; 0.4 reaches
        /// planarity three or four factorisations sooner with the same results on the test meshes. 1 keeps them
        /// constant as in Tang et al. 2014 and Zadravec et al. 2010, which ends at a compromise between planarity
        /// and closeness instead of exact planarity.
        /// </summary>
        public double Decay { get; set; } = 0.4;

        /// <summary>Tang et al.'s ε: Tikhonov weight on the step, in units of the mean edge length.</summary>
        public double Regularisation { get; set; } = 1e-3;

        public Func<bool>? ShouldCancel { get; set; }

        /// <summary>Receives one line per iteration (deviation, residual, step, weights) for diagnostics.</summary>
        public Action<string>? Log { get; set; }
    }

    public sealed class Result
    {
        public Vec3d[] Vertices { get; set; } = Array.Empty<Vec3d>();
        /// <summary>Largest corner distance from the best-fit plane, per face (model units; 0 for triangles).</summary>
        public double[] FaceDeviations { get; set; } = Array.Empty<double>();
        /// <summary>Unit normal of every face (the auxiliary variable; Newell normal for triangles).</summary>
        public Vec3d[] FaceNormals { get; set; } = Array.Empty<Vec3d>();
        public int Iterations { get; set; }
        /// <summary>Largest |n_f · (v_i − v_j)| over all planarity constraints, model units.</summary>
        public double MaxResidual { get; set; }
        public double MaxDeviation { get; set; }
        public double MaxDisplacement { get; set; }
        public double MeanDisplacement { get; set; }
        /// <summary>Largest distance of a free vertex from the reference surface (NaN without a reference).</summary>
        public double MaxSurfaceDistance { get; set; } = double.NaN;
        public double MeanSurfaceDistance { get; set; } = double.NaN;
        /// <summary>True when MaxDeviation fell below the tolerance.</summary>
        public bool Converged { get; set; }
        /// <summary>
        /// True when the iteration stopped because five consecutive steps improved the deviation by less than
        /// 1 % (or every damped step was refused): the mesh has no planar realisation nearby that keeps its
        /// polygons from folding — an asymptotic net, for instance.
        /// </summary>
        public bool Stalled { get; set; }
        /// <summary>Mean edge length of the constrained faces, model units (to read the displacement against).</summary>
        public double MeanEdge { get; set; }
        /// <summary>Number of faces with four or more corners (the constrained ones).</summary>
        public int PolygonCount { get; set; }
        public int FreeVertexCount { get; set; }
    }

    /// <summary>
    /// Planarizes <paramref name="mesh"/>. Vertices flagged in
    /// <paramref name="fixedVertices"/> keep their position. The closeness
    /// term pulls every free vertex towards <paramref name="reference"/>
    /// (any mesh; triangulated internally) or, when null, towards the input
    /// mesh itself.
    /// </summary>
    public static Result Optimize(MeshData mesh, bool[]? fixedVertices, MeshData? reference = null, Options? options = null)
    {
        options ??= new Options();
        int nv = mesh.VertexCount, nf = mesh.FaceCount;
        var res = new Result();
        if (nv == 0) return res;

        // --- scale to the mean edge length of the constrained faces, so every residual — planarity
        // (a corner's distance off its plane), closeness, anchor, fairness, the step — is measured in
        // edge units and ε and the weights mean the same on every model (the bounding box would
        // under-weight the soft terms by the number of edges across the mesh)
        double h = 0; int hCount = 0;
        foreach (var face in mesh.Faces)
        {
            if (face.Length < 4) continue;
            for (int e = 0; e < face.Length; e++) { h += (mesh.Vertices[face[e]] - mesh.Vertices[face[(e + 1) % face.Length]]).Length; hCount++; }
        }
        h = hCount > 0 ? h / hCount : mesh.BoundingBoxDiagonal();
        if (h <= 0) h = 1;
        var v = new Vec3d[nv];
        for (int i = 0; i < nv; i++) v[i] = mesh.Vertices[i] / h;
        var v0 = (Vec3d[])v.Clone();

        // --- unknown indices: free vertices first, then one normal per polygon
        var vIdx = new int[nv];
        int nFree = 0;
        for (int i = 0; i < nv; i++) vIdx[i] = (fixedVertices != null && i < fixedVertices.Length && fixedVertices[i]) ? -1 : nFree++;
        var polys = new List<int>();
        for (int f = 0; f < nf; f++) if (mesh.Faces[f].Length >= 4) polys.Add(f);
        var nIdx = new int[nf];
        for (int f = 0; f < nf; f++) nIdx[f] = -1;
        for (int k = 0; k < polys.Count; k++) nIdx[polys[k]] = nFree + k;
        int nUnk = 3 * (nFree + polys.Count);
        res.PolygonCount = polys.Count; res.FreeVertexCount = nFree;

        // --- rest edge lengths of the constrained faces (normalised units), for the planarity residuals
        var restLength = new double[nf][];
        double meanEdge = 0; int edgeCount = 0;
        foreach (int f in polys)
        {
            var face = mesh.Faces[f];
            restLength[f] = new double[face.Length];
            for (int e = 0; e < face.Length; e++) { double l = (v0[face[e]] - v0[face[(e + 1) % face.Length]]).Length; restLength[f][e] = l; meanEdge += l; edgeCount++; }
        }
        meanEdge = edgeCount > 0 ? meanEdge / edgeCount : 1;
        foreach (int f in polys)
            for (int e = 0; e < restLength[f].Length; e++)
                if (restLength[f][e] < 1e-6 * meanEdge) restLength[f][e] = 1e-6 * meanEdge; // a collapsed input edge: keep the row finite

        var area0 = new double[nf];
        foreach (int f in polys) area0[f] = PolygonArea(v0, mesh.Faces[f]);

        // --- face normals: Newell for every face, the auxiliary variable for polygons
        var normal = new Vec3d[nf];
        for (int f = 0; f < nf; f++) normal[f] = Newell(v, mesh.Faces[f]);

        // --- fairness triples (a, v, c) along the polylines, Laplacian stars for irregular vertices
        var triples = new List<(int a, int v, int c)>();
        var stars = new List<int[]>();
        BuildFairness(mesh, triples, stars);
        var tripleRest = triples.Select(t => v0[t.a] - 2 * v0[t.v] + v0[t.c]).ToArray();
        var starRest = stars.Select(s => { var m = Vec3d.Zero; for (int k = 1; k < s.Length; k++) m += v0[s[k]]; return v0[s[0]] - m / (s.Length - 1); }).ToArray();

        // --- reference surface for the closeness term
        MeshProjection? proj = null;
        if (options.Closeness > 0)
        {
            var refMesh = reference ?? mesh;
            var rv = new Vec3d[refMesh.VertexCount];
            for (int i = 0; i < rv.Length; i++) rv[i] = refMesh.Vertices[i] / h;
            proj = new MeshProjection(new MeshData(rv, refMesh.Faces).ToTriangulated());
        }
        var hint = new int[nv];
        for (int i = 0; i < nv; i++) hint[i] = -1;
        // tangent plane of the reference at every start position, for the anchor
        var anchorT1 = new Vec3d[nv]; var anchorT2 = new Vec3d[nv];
        if (proj != null && options.Anchor > 0)
            for (int i = 0; i < nv; i++)
            {
                if (vIdx[i] < 0) continue;
                var hit = proj.ClosestPointGlobal(v0[i]);
                var np = (hit.Normal.LengthSquared > 1e-20 ? hit.Normal : hit.SmoothNormal).Normalized();
                var axis = Math.Abs(np.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
                anchorT1[i] = Vec3d.Cross(np, axis).Normalized();
                anchorT2[i] = Vec3d.Cross(np, anchorT1[i]);
            }

        double tol = options.Tolerance / h;
        double eps2 = options.Regularisation * options.Regularisation;
        double wFair = options.Fairness, wClose = options.Closeness, wAnchor = options.Anchor, wStretch = options.Stretch;
        var edges = new List<(int a, int b, double l0)>();
        {
            var seen = new HashSet<(int, int)>();
            foreach (int f in polys)
            {
                var face = mesh.Faces[f];
                for (int e = 0; e < face.Length; e++)
                {
                    int a = face[e], b = face[(e + 1) % face.Length];
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    if (!seen.Add(key) || (vIdx[a] < 0 && vIdx[b] < 0)) continue;
                    double l0 = (v0[a] - v0[b]).Length;
                    if (l0 > 1e-12) edges.Add((a, b, l0));
                }
            }
        }
        int it = 0;
        bool converged = false;
        double maxDev = MaxDeviationOf(v, mesh.Faces);
        if (maxDev < tol) converged = true;

        // Assembles J, r at the current (v, normal): hard rows at weight 1, soft rows at the given weights;
        // the raw (unweighted) soft costs are kept so the cost can be re-weighted without reassembly.
        NormalEquations Assemble(double wf, double wc, double wa, double ws)
        {
            var sys = new NormalEquations(nUnk);
            foreach (int f in polys)
            {
                var face = mesh.Faces[f];
                int ni = nIdx[f];
                var n = normal[f];
                for (int e = 0; e < face.Length; e++)
                {
                    int a = face[e], b = face[(e + 1) % face.Length];
                    var d = v[a] - v[b];
                    // residual n · (v_a − v_b) / l₀: the sine of the edge's angle to the face plane. The raw
                    // bilinear form scales with the edge length, so a least-squares path lowers it by shrinking
                    // the quad as readily as by flattening it (Liu et al.'s thin quads); the rest length l₀
                    // takes that route away without changing the constraint's zero set.
                    double inv = 1.0 / restLength[f][e];
                    double r = Vec3d.Dot(n, d) * inv;
                    var terms = new List<(int, double)>(9);
                    for (int k = 0; k < 3; k++)
                    {
                        if (vIdx[a] >= 0) terms.Add((3 * vIdx[a] + k, n[k] * inv));
                        if (vIdx[b] >= 0) terms.Add((3 * vIdx[b] + k, -n[k] * inv));
                        terms.Add((3 * ni + k, d[k] * inv));
                    }
                    sys.Row(1.0, r, terms, 0);
                }
                sys.Row(1.0, Vec3d.Dot(n, n) - 1.0,
                    new List<(int, double)> { (3 * ni, 2 * n.X), (3 * ni + 1, 2 * n.Y), (3 * ni + 2, 2 * n.Z) }, 0);
            }
            if (wf > 0)
            {
                for (int t = 0; t < triples.Count; t++)
                {
                    var (a, b, c) = (triples[t].a, triples[t].v, triples[t].c);
                    var d = v[a] - 2 * v[b] + v[c] - tripleRest[t];
                    for (int k = 0; k < 3; k++)
                    {
                        var terms = new List<(int, double)>(3);
                        if (vIdx[a] >= 0) terms.Add((3 * vIdx[a] + k, 1.0));
                        if (vIdx[b] >= 0) terms.Add((3 * vIdx[b] + k, -2.0));
                        if (vIdx[c] >= 0) terms.Add((3 * vIdx[c] + k, 1.0));
                        if (terms.Count > 0) sys.Row(wf, d[k], terms, 1);
                    }
                }
                for (int s = 0; s < stars.Count; s++)
                {
                    var st = stars[s];
                    int m = st.Length - 1;
                    var mean = Vec3d.Zero;
                    for (int k = 1; k < st.Length; k++) mean += v[st[k]];
                    var d = v[st[0]] - mean / m - starRest[s];
                    for (int k = 0; k < 3; k++)
                    {
                        var terms = new List<(int, double)>(st.Length);
                        if (vIdx[st[0]] >= 0) terms.Add((3 * vIdx[st[0]] + k, 1.0));
                        for (int q = 1; q < st.Length; q++) if (vIdx[st[q]] >= 0) terms.Add((3 * vIdx[st[q]] + k, -1.0 / m));
                        if (terms.Count > 0) sys.Row(wf, d[k], terms, 1);
                    }
                }
            }
            if (proj != null && wc > 0)
            {
                for (int i = 0; i < nv; i++)
                {
                    if (vIdx[i] < 0) continue;
                    var hit = hint[i] < 0 ? proj.ClosestPointGlobal(v[i]) : proj.ClosestPoint(v[i], hint[i]);
                    hint[i] = hit.NearestVertex;
                    var np = hit.Normal.LengthSquared > 1e-20 ? hit.Normal.Normalized() : hit.SmoothNormal.Normalized();
                    double r = Vec3d.Dot(v[i] - hit.Point, np);
                    sys.Row(wc, r, new List<(int, double)> { (3 * vIdx[i], np.X), (3 * vIdx[i] + 1, np.Y), (3 * vIdx[i] + 2, np.Z) }, 2);
                }
            }
            if (proj != null && wa > 0)
            {
                for (int i = 0; i < nv; i++)
                {
                    if (vIdx[i] < 0) continue;
                    var d = v[i] - v0[i];
                    var t1 = anchorT1[i]; var t2 = anchorT2[i];
                    sys.Row(wa, Vec3d.Dot(d, t1), new List<(int, double)> { (3 * vIdx[i], t1.X), (3 * vIdx[i] + 1, t1.Y), (3 * vIdx[i] + 2, t1.Z) }, 3);
                    sys.Row(wa, Vec3d.Dot(d, t2), new List<(int, double)> { (3 * vIdx[i], t2.X), (3 * vIdx[i] + 1, t2.Y), (3 * vIdx[i] + 2, t2.Z) }, 3);
                }
            }
            if (ws > 0)
            {
                foreach (var (a, b, l0) in edges)
                {
                    var d = v[a] - v[b];
                    double r = (d.LengthSquared - l0 * l0) / (2 * l0);
                    var terms = new List<(int, double)>(6);
                    for (int k = 0; k < 3; k++)
                    {
                        if (vIdx[a] >= 0) terms.Add((3 * vIdx[a] + k, d[k] / l0));
                        if (vIdx[b] >= 0) terms.Add((3 * vIdx[b] + k, -d[k] / l0));
                    }
                    sys.Row(ws, r, terms, 4);
                }
            }
            return sys;
        }

        // Levenberg–Marquardt on the regularised Gauss–Newton step (Nielsen's update of the damping):
        // a step is accepted when the actual decrease of the cost — at the weights the step was computed
        // with — is at least a quarter of the decrease the linear model predicted; otherwise it is undone
        // and retried from the same iterate with more damping, so a poor linearisation (a net far from
        // any planar realisation) is followed in short steps instead of one long jump.
        double lambda = 0, nu = 2;
        var sysK = Assemble(wFair, wClose, wAnchor, wStretch);
        var vK = (Vec3d[])v.Clone(); var nK = (Vec3d[])normal.Clone();
        int rejected = 0, slow = 0;
        bool stalled = false;
        for (it = 0; it < options.Iterations && !converged && nUnk > 0; it++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            double costK = sysK.CostAt(wFair, wClose, wAnchor, wStretch);
            double[]? dx = null;
            NormalEquations? sysNext = null;
            double step = 0;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                dx = sysK.Solve(eps2 + lambda, 3 * nFree);
                if (dx == null) { lambda = lambda == 0 ? 1e-6 : lambda * 10; continue; }
                Apply(vK, nK, dx, 1.0, vIdx, nIdx, polys, v, normal);
                sysNext = Assemble(wFair, wClose, wAnchor, wStretch);
                double costNext = sysNext.CostAt(wFair, wClose, wAnchor, wStretch);
                double predicted = costK - sysK.PredictedCost(dx, wFair, wClose, wAnchor, wStretch);
                double actual = costK - costNext;
                double rho = predicted > 0 ? actual / predicted : (actual >= 0 ? 1 : -1);
                // fold guard: a polygon that lost three quarters of its area is on its way to a bow-tie
                // (the degenerate planar solution); such a step is treated like a cost increase
                bool folded = false;
                foreach (int f in polys) if (PolygonArea(v, mesh.Faces[f]) < 0.25 * area0[f]) { folded = true; break; }
                bool atGoal = sysNext.HardCost <= sysK.HardCost && MaxDeviationOf(v, mesh.Faces) < tol;
                // near the manifold the soft terms dominate the cost and their compromise with planarity sits
                // above the tolerance at the current weights; a step that halves the hard cost while raising the
                // total by at most 1 % is the projection step Liu et al. end with, and is taken — the decay of
                // the weights forgives the soft increase on the next iteration
                bool planarGain = sysNext.HardCost <= 0.5 * sysK.HardCost && costNext <= costK * 1.01;
                if (!folded && (atGoal || rho > 0.25 || planarGain))
                {
                    lambda *= Math.Max(1.0 / 3, 1 - Math.Pow(2 * rho - 1, 3));
                    if (lambda < 1e-12) lambda = 0;
                    nu = 2;
                    break;
                }
                rejected++;
                lambda = lambda == 0 ? 1e-6 : lambda * nu;
                nu *= 2;
                sysNext = null;
            }
            if (sysNext == null || dx == null) { Array.Copy(vK, v, nv); Array.Copy(nK, normal, nf); stalled = true; break; }
            double devBefore = maxDev;
            foreach (double d in dx) step = Math.Max(step, Math.Abs(d));
            Array.Copy(v, vK, nv); Array.Copy(normal, nK, nf);
            sysK = sysNext;
            maxDev = MaxDeviationOf(v, mesh.Faces);
            if (options.Log != null)
            {
                double mv = 0; for (int i = 0; i < nv; i++) mv = Math.Max(mv, (v[i] - v0[i]).Length);
                options.Log($"it {it + 1}: deviation {maxDev * h:E2}, hard cost {sysK.HardCost:E2}, step {step * h:E2}, lambda {lambda:E1}, moved {mv * h:F4}, wFair {wFair:E1}, wClose {wClose:E1}, wAnchor {wAnchor:E1}, wStretch {wStretch:E1}");
            }
            if (maxDev < tol) { converged = true; it++; break; }
            slow = maxDev > 0.99 * devBefore ? slow + 1 : 0;
            if (slow >= 5) { stalled = true; it++; break; }
            // Liu et al. 2006: the soft weights decay every iteration, so planarity is reached exactly while
            // the first steps — the ones that decide where the net goes — are taken with the closeness,
            // the anchor and the fairness at full strength
            wFair *= options.Decay; wClose *= options.Decay; wAnchor *= options.Decay; wStretch *= options.Decay;
            // a weight below a millionth of its start is set to zero outright, so the late iterations compare clean
            // hard-constraint costs (the re-projected closest points make a soft residual jitter at round-off level)
            if (wFair < 1e-6 * options.Fairness) wFair = 0;
            if (wClose < 1e-6 * options.Closeness) wClose = 0;
            if (wAnchor < 1e-6 * options.Anchor) wAnchor = 0;
            if (wStretch < 1e-6 * options.Stretch) wStretch = 0;
            sysK = sysK.Reweighted(wFair, wClose, wAnchor, wStretch);
            // the objective changed: the damping is reset rather than carried over from a rejected step
            if (options.Decay < 1) { lambda = 0; nu = 2; }
        }

        // --- report, back in model units
        res.Iterations = it;
        res.Converged = converged;
        res.Stalled = stalled && !converged;
        res.MeanEdge = meanEdge * h;
        res.Vertices = new Vec3d[nv];
        for (int i = 0; i < nv; i++) res.Vertices[i] = vIdx[i] < 0 ? mesh.Vertices[i] : v[i] * h;
        res.FaceNormals = new Vec3d[nf];
        for (int f = 0; f < nf; f++) res.FaceNormals[f] = nIdx[f] >= 0 ? normal[f].Normalized() : Newell(v, mesh.Faces[f]);
        var outMesh = new MeshData(res.Vertices, mesh.Faces);
        res.FaceDeviations = Planarization.ComputeDeviation(outMesh);
        res.MaxDeviation = res.FaceDeviations.Length > 0 ? res.FaceDeviations.Max() : 0;
        double maxRes = 0;
        foreach (int f in polys)
        {
            var face = mesh.Faces[f]; var n = res.FaceNormals[f];
            for (int e = 0; e < face.Length; e++)
                maxRes = Math.Max(maxRes, Math.Abs(Vec3d.Dot(n, res.Vertices[face[e]] - res.Vertices[face[(e + 1) % face.Length]])));
        }
        res.MaxResidual = maxRes;
        double maxMove = 0, sumMove = 0;
        for (int i = 0; i < nv; i++) { double m = (v[i] - v0[i]).Length * h; maxMove = Math.Max(maxMove, m); sumMove += m; }
        res.MaxDisplacement = maxMove; res.MeanDisplacement = sumMove / nv;
        if (proj != null)
        {
            double maxD = 0, sumD = 0; int cnt = 0;
            for (int i = 0; i < nv; i++)
            {
                if (vIdx[i] < 0) continue;
                var hit = proj.ClosestPointGlobal(v[i]);
                double d = (hit.Point - v[i]).Length * h;
                maxD = Math.Max(maxD, d); sumD += d; cnt++;
            }
            res.MaxSurfaceDistance = maxD; res.MeanSurfaceDistance = cnt > 0 ? sumD / cnt : 0;
        }
        return res;
    }

    private static void Apply(Vec3d[] vFrom, Vec3d[] nFrom, double[] dx, double alpha, int[] vIdx, int[] nIdx, List<int> polys, Vec3d[] v, Vec3d[] normal)
    {
        for (int i = 0; i < v.Length; i++)
        {
            int u = vIdx[i];
            v[i] = u < 0 ? vFrom[i] : vFrom[i] + alpha * new Vec3d(dx[3 * u], dx[3 * u + 1], dx[3 * u + 2]);
        }
        Array.Copy(nFrom, normal, normal.Length);
        foreach (int f in polys)
        {
            int u = nIdx[f];
            normal[f] = nFrom[f] + alpha * new Vec3d(dx[3 * u], dx[3 * u + 1], dx[3 * u + 2]);
        }
    }

    private static double HardCost(Vec3d[] v, Vec3d[] normal, int[][] faces, List<int> polys)
    {
        double c = 0;
        foreach (int f in polys)
        {
            var face = faces[f]; var n = normal[f];
            for (int e = 0; e < face.Length; e++)
            {
                double r = Vec3d.Dot(n, v[face[e]] - v[face[(e + 1) % face.Length]]);
                c += r * r;
            }
            double u = Vec3d.Dot(n, n) - 1; c += u * u;
        }
        return c;
    }

    private static double MaxDeviationOf(Vec3d[] v, int[][] faces)
    {
        double max = 0;
        foreach (var f in faces)
        {
            if (f.Length < 4) continue;
            var n = Newell(v, f);
            var c = Vec3d.Zero; foreach (int i in f) c += v[i]; c /= f.Length;
            foreach (int i in f) max = Math.Max(max, Math.Abs(Vec3d.Dot(v[i] - c, n)));
        }
        return max;
    }

    internal static double PolygonArea(Vec3d[] v, int[] face)
    {
        var n = Vec3d.Zero;
        for (int i = 0; i < face.Length; i++) n += Vec3d.Cross(v[face[i]], v[face[(i + 1) % face.Length]]);
        return 0.5 * n.Length;
    }

    internal static Vec3d Newell(Vec3d[] v, int[] face)
    {
        var n = Vec3d.Zero;
        for (int i = 0; i < face.Length; i++)
        {
            var a = v[face[i]]; var b = v[face[(i + 1) % face.Length]];
            n += new Vec3d((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return n.LengthSquared > 1e-30 ? n.Normalized() : new Vec3d(0, 0, 1);
    }

    /// <summary>
    /// Second-difference triples along the mesh polylines: at vertex v the edge
    /// v a continues into v c when a and c share no face with v and the pairing
    /// is unique (the straight-through continuation of a quad net; boundary
    /// vertices of a quad mesh get the one triple along the border). Vertices
    /// whose neighbours do not pair up this way (irregular, valence ≠ 4 in the
    /// interior) get a Laplacian star instead.
    /// </summary>
    public static void BuildFairness(MeshData mesh, List<(int a, int v, int c)> triples, List<int[]> stars)
    {
        int nv = mesh.VertexCount;
        var nb = mesh.BuildVertexNeighbors();
        var vf = mesh.BuildVertexFaces();
        for (int v = 0; v < nv; v++)
        {
            var ns = nb[v];
            if (ns.Length < 2) continue;
            // neighbours sharing a face with v, per neighbour
            var shared = new HashSet<(int, int)>();
            foreach (int f in vf[v])
            {
                var face = mesh.Faces[f];
                for (int i = 0; i < face.Length; i++)
                    for (int j = i + 1; j < face.Length; j++)
                        if (face[i] != v && face[j] != v) { shared.Add((face[i], face[j])); shared.Add((face[j], face[i])); }
            }
            var pairs = new List<(int, int)>();
            var used = new HashSet<int>();
            bool ok = true;
            for (int i = 0; i < ns.Length && ok; i++)
            {
                if (used.Contains(ns[i])) continue;
                var opp = new List<int>();
                for (int j = 0; j < ns.Length; j++)
                    if (j != i && !used.Contains(ns[j]) && !shared.Contains((ns[i], ns[j]))) opp.Add(ns[j]);
                if (opp.Count == 1) { pairs.Add((ns[i], opp[0])); used.Add(ns[i]); used.Add(opp[0]); }
                else if (opp.Count > 1) ok = false;
                // opp.Count == 0: a border edge with no continuation — fine
            }
            if (ok && pairs.Count > 0)
                foreach (var (a, c) in pairs) triples.Add((a, v, c));
            else if (ns.Length >= 3)
            {
                var star = new int[ns.Length + 1];
                star[0] = v; Array.Copy(ns, 0, star, 1, ns.Length);
                stars.Add(star);
            }
        }
    }

    /// <summary>
    /// Rows of the least-squares system, kept as (weight class, residual, sparse Jacobian row) so the
    /// soft weights can change without reassembly; JᵀJ and Jᵀr are formed on demand.
    /// Weight classes: 0 hard (weight 1), 1 fairness, 2 closeness, 3 anchor, 4 edge length.
    /// </summary>
    private sealed class NormalEquations
    {
        private readonly int _n;
        private readonly List<(int Kind, double Weight, double Residual, List<(int Col, double Val)> Terms)> _rows
            = new List<(int, double, double, List<(int, double)>)>();
        private readonly double[] _raw = new double[5];

        public NormalEquations(int n) { _n = n; }

        public double HardCost => _raw[0];

        public void Row(double weight, double residual, List<(int Col, double Val)> terms, int kind)
        {
            _rows.Add((kind, weight, residual, terms));
            _raw[kind] += residual * residual;
        }

        /// <summary>Σ r² (hard) + wf² Σ r² (fairness) + wc² Σ r² (closeness) + wa² Σ r² (anchor) + ws² Σ r² (length).</summary>
        public double CostAt(double wf, double wc, double wa, double ws) => _raw[0] + wf * wf * _raw[1] + wc * wc * _raw[2] + wa * wa * _raw[3] + ws * ws * _raw[4];

        private static double WeightOf(int kind, double wf, double wc, double wa, double ws) => kind == 0 ? 1.0 : kind == 1 ? wf : kind == 2 ? wc : kind == 3 ? wa : ws;

        /// <summary>Cost of the linear model at step dx: Σ w² (r + J dx)².</summary>
        public double PredictedCost(double[] dx, double wf, double wc, double wa, double ws)
        {
            double c = 0;
            foreach (var (kind, _, r, terms) in _rows)
            {
                double w = WeightOf(kind, wf, wc, wa, ws);
                if (w == 0) continue;
                double lin = r;
                foreach (var (col, val) in terms) lin += val * dx[col];
                c += w * w * lin * lin;
            }
            return c;
        }

        public NormalEquations Reweighted(double wf, double wc, double wa, double ws)
        {
            var s = new NormalEquations(_n);
            foreach (var (kind, _, r, terms) in _rows) s.Row(WeightOf(kind, wf, wc, wa, ws), r, terms, kind);
            return s;
        }

        /// <summary>Solves (JᵀJ + damping·I) Δ = −Jᵀr.</summary>
        /// <summary>
        /// Solves (JᵀJ + damping·I) Δ = −Jᵀr. The unknowns from <paramref name="nVertex"/> on are the face
        /// normals, three per face, each coupled only to its own face's corners: their 3 × 3 diagonal blocks
        /// are eliminated first (Schur complement), so the factorisation runs on the vertex unknowns alone —
        /// half the unknowns and half the bandwidth — and the normal increments follow by back-substitution.
        /// </summary>
        public double[]? Solve(double damping, int nVertex)
        {
            var jtj = new Dictionary<long, double>();
            var jtr = new double[_n];
            foreach (var (_, weight, residual, terms) in _rows)
            {
                if (weight == 0) continue;
                double w2 = weight * weight;
                for (int a = 0; a < terms.Count; a++)
                {
                    var (ca, va) = terms[a];
                    if (va == 0) continue;
                    jtr[ca] += w2 * va * residual;
                    for (int b = 0; b < terms.Count; b++)
                    {
                        var (cb, vb) = terms[b];
                        if (vb == 0 || cb < ca) continue;
                        long key = (long)ca * _n + cb;
                        jtj.TryGetValue(key, out double cur);
                        jtj[key] = cur + w2 * va * vb;
                    }
                }
            }
            int nNormal = (_n - nVertex) / 3;
            // split: vertex–vertex entries go to the builder; normal blocks and vertex–normal couplings are collected
            var bld = new SparseSymmetricSolver.Builder(nVertex);
            var blocks = new double[nNormal][]; // 3 × 3 symmetric, row-major, with damping
            var coupling = new List<(int Col, double[] Vals)>[nNormal]; // vertex column → the 3 entries against this normal
            var rhsN = new double[3 * nNormal];
            for (int k = 0; k < nNormal; k++) { blocks[k] = new double[9]; coupling[k] = new List<(int, double[])>(); }
            var couplingIndex = new Dictionary<long, int>();
            foreach (var kv in jtj)
            {
                int r = (int)(kv.Key / _n), c = (int)(kv.Key % _n); // r ≤ c
                if (c < nVertex) bld.Add(r, c, kv.Value);
                else if (r >= nVertex)
                {
                    int k = (r - nVertex) / 3;
                    int i = (r - nVertex) % 3, j = (c - nVertex) % 3;
                    if ((c - nVertex) / 3 != k) throw new InvalidOperationException("normals are coupled only within their face");
                    blocks[k][3 * i + j] += kv.Value; if (i != j) blocks[k][3 * j + i] += kv.Value;
                }
                else
                {
                    int k = (c - nVertex) / 3, j = (c - nVertex) % 3;
                    long ck = (long)k * _n + r;
                    if (!couplingIndex.TryGetValue(ck, out int idx)) { idx = coupling[k].Count; coupling[k].Add((r, new double[3])); couplingIndex[ck] = idx; }
                    coupling[k][idx].Vals[j] += kv.Value;
                }
            }
            for (int i = 0; i < nVertex; i++) bld.Add(i, i, damping);
            var rhs = new double[nVertex];
            for (int i = 0; i < nVertex; i++) rhs[i] = -jtr[i];
            for (int k = 0; k < nNormal; k++)
            {
                var B = blocks[k];
                B[0] += damping; B[4] += damping; B[8] += damping;
                var inv = Invert3(B);
                if (inv == null) return null;
                blocks[k] = inv;
                for (int j = 0; j < 3; j++) rhsN[3 * k + j] = -jtr[nVertex + 3 * k + j];
                // Schur complement: A_vv −= A_vn B⁻¹ A_nv ; rhs_v −= A_vn B⁻¹ rhs_n
                var cs = coupling[k];
                var bi = new double[cs.Count][];
                for (int a = 0; a < cs.Count; a++) bi[a] = Mul3(inv, cs[a].Vals);
                for (int a = 0; a < cs.Count; a++)
                {
                    var (ca, _) = cs[a];
                    rhs[ca] -= bi[a][0] * rhsN[3 * k] + bi[a][1] * rhsN[3 * k + 1] + bi[a][2] * rhsN[3 * k + 2];
                    for (int b = 0; b < cs.Count; b++)
                    {
                        var (cb, vb) = cs[b];
                        if (cb < ca) continue;
                        bld.Add(ca, cb, -(bi[a][0] * vb[0] + bi[a][1] * vb[1] + bi[a][2] * vb[2]));
                    }
                }
            }
            var solver = SparseSymmetricSolver.Factor(bld, 1e-18);
            var dv = solver?.Solve(rhs);
            if (dv == null) return null;
            var dx = new double[_n];
            Array.Copy(dv, dx, nVertex);
            for (int k = 0; k < nNormal; k++)
            {
                var t = new double[3];
                for (int j = 0; j < 3; j++) t[j] = rhsN[3 * k + j];
                foreach (var (col, vals) in coupling[k]) for (int j = 0; j < 3; j++) t[j] -= vals[j] * dv[col];
                var dn = Mul3(blocks[k], t);
                for (int j = 0; j < 3; j++) dx[nVertex + 3 * k + j] = dn[j];
            }
            return dx;
        }

        private static double[] Mul3(double[] m, double[] x) => new[]
        {
            m[0] * x[0] + m[1] * x[1] + m[2] * x[2],
            m[3] * x[0] + m[4] * x[1] + m[5] * x[2],
            m[6] * x[0] + m[7] * x[1] + m[8] * x[2],
        };

        private static double[]? Invert3(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (Math.Abs(det) < 1e-300) return null;
            double s = 1.0 / det;
            return new[]
            {
                (e * i - f * h) * s, (c * h - b * i) * s, (b * f - c * e) * s,
                (f * g - d * i) * s, (a * i - c * g) * s, (c * d - a * f) * s,
                (d * h - e * g) * s, (b * g - a * h) * s, (a * e - b * d) * s,
            };
        }
    }
}
