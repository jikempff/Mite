using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Numerics;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Core.Analysis;

/// <summary>
/// Linear static analysis of a lath network as a 3D frame: each polyline
/// segment becomes a 12-DOF Euler-Bernoulli beam with the lath's section,
/// laths are coupled at net crossings (rigidly, or as scissor hinges about the
/// surface normal with an optional rotational stiffness), and support points
/// are fixed or pinned. A first-order sanity check for gridshells — not a
/// replacement for a full FE package, but it runs inside the definition and
/// reports per-lath axial/bending utilization against an allowable stress.
///
/// Joint eccentricity (2026-11-02). Laths of a gridshell are stacked: at a
/// crossing the axis of the upper lath lies a lath depth above the axis of the
/// lower one, and the bolt through both is a short, stiff bar. With
/// <see cref="Options.AxisOffsets"/> every lath keeps its own axis at a signed
/// height h_c along the surface normal, while the frame's nodes stay on the
/// surface (the reference points, where joints, supports and point loads are
/// given). Each element end is tied to its reference node by a rigid link
/// r = h_c · n: u_axis = u_node + θ × r, θ_axis = θ (small rotations), i.e.
/// the 6 × 6 transfer T = [[I, −[r]×], [0, I]]; the element stiffness at the
/// nodes is Tᵀ K T, loads lumped at the axis go to the node as Tᵀ f (the force
/// itself plus the moment r × f), and the axis displacements for the force
/// recovery are T u. This is the rigid end offset of the frame programs
/// (Oasys GSA theory, "Offsets": "u_pseudo = T u_node", "f_node = Tᵀ f_pseudo",
/// with the same T; Midas "Beam End Offsets", Karamba3D "Eccentricity on
/// Beam"), the model D'Amico, Kermani &amp; Zhang 2014 (Form finding and
/// structural analysis of actively bent timber grid shells, Eng. Struct. 81,
/// §4.3.1) describe as placing the two lath layers "on staggered levels"
/// linked at the nodes — "faithful to the real geometry" — and the layering
/// Schling, Kilian, Wang, Schikore &amp; Pottmann 2018 (AAG 2018, §3.2)
/// report for the timber asymptotic gridshell, whose lamella layers are
/// "arranged on two levels". Forces transferred between the layers at a
/// crossing therefore act with the lever arm of the stack: a horizontal force
/// handed from the upper lath to the lower one twists the lower lath by
/// e · F, which the single-node model of 1.3.x could not see. Erkmen &amp;
/// Saleh 2012 (Eccentricity effects in the finite element modelling of
/// composite beams, Adv. Eng. Softw. 52) note the limit of rigid-bar
/// coupling for two *parallel* beams (discrete links over-constrain the slip);
/// crossing laths are coupled at isolated points, where the link is exact.

/// Linear (Euler) buckling, <see cref="Options.BucklingModes"/>: the axial
/// forces N of the linear solve build the consistent geometric stiffness
/// k_g = (N/L) · [u: 1, −1; v–θz and w–θy: 6/5, ±L/10, 2L²/15, −L²/30;
/// θx: I_p/A] of every element (cubic Hermite beam, Przemieniecki 1968 ch. 15;
/// McGuire, Gallagher &amp; Ziemian 2000, Matrix Structural Analysis 2nd ed.
/// eq. 9.19, with the axial P/L term and the Wagner torsion term I_p/A;
/// Gavin 2012, Geometric stiffness effects in 2D and 3D frames, Duke CEE 421L,
/// for the Timoshenko form that reduces to it). The load factor λ solves
/// (K_e + λ K_g) φ = 0: the applied load pattern times λ_1 (smallest positive
/// eigenvalue) is the elastic critical load of the first mode (Timoshenko &amp;
/// Gere 1961, Theory of Elastic Stability; Euler column P_cr = π²EI/(K L)²
/// with K = 2 fixed–free, 1 pinned, 0.5 fixed–fixed as the exact checks).
/// Solved by subspace iteration on K_e φ = λ (−K_g) φ (Bathe &amp; Wilson 1973)
/// and verified by the Sturm sequence count of K_e − μ(−K_g) at μ just below
/// λ_1 (no eigenvalue may lie there). Negative eigenvalues (buckling under the reversed load) are left out.
/// </summary>
public static class FrameAnalysis
{

    /// <summary>How a support point holds its node.</summary>
    public enum SupportKind
    {
        /// <summary>All six degrees of freedom fixed (clamped lath end).</summary>
        Fixed = 0,
        /// <summary>Translations fixed, rotations free (pin / ball joint).</summary>
        Pinned = 1,
    }

    public class Options
    {
        /// <summary>Young's modulus (Pa). Default 11e9 ≈ timber along grain.</summary>
        public double E { get; set; } = 11e9;

        /// <summary>Shear modulus (Pa). 0 = automatic (isotropic: E / 2.6).</summary>
        public double G { get; set; } = 0.0;

        /// <summary>Allowable combined stress (Pa). Default 20e6 ≈ structural timber.</summary>
        public double AllowableStress { get; set; } = 20e6;

        /// <summary>
        /// Distance within which a support point, a point load or a joint
        /// point is attached to a lath (0 = automatic: support points and
        /// point loads within 55% of the average polyline segment, joints
        /// within 25% of it). Joints are inserted into every lath passing
        /// that close, at the exact position, so neighbouring joints never
        /// merge into one another.
        /// </summary>
        public double SnapTolerance { get; set; } = 0.0;

        /// <summary>
        /// Couple lath ends that lie on another lath (T-junctions, e.g. where a
        /// traced curve merged into its neighbour) to that lath automatically,
        /// as if a joint point had been given there. Default true.
        /// </summary>
        public bool CoupleEndsOnLaths { get; set; } = true;

        /// <summary>Support condition applied at every support point. Default fixed.</summary>
        public SupportKind Support { get; set; } = SupportKind.Fixed;

        /// <summary>
        /// Rotational stiffness of the lath-to-lath joints about the surface
        /// normal, in N·m/rad. +∞ (default) = rigid joint (the laths share all
        /// six degrees of freedom); 0 = scissor hinge (a bolt along the normal:
        /// translations and the two tangent-plane rotations are shared, the
        /// rotation about the bolt is free); values in between model a
        /// semi-rigid bolted joint.
        /// </summary>
        public double JointRotationalStiffness { get; set; } = double.PositiveInfinity;

        /// <summary>Material density in kg/m³ for self-weight (0 = no self-weight). Timber ≈ 450, steel 7850.</summary>
        public double Density { get; set; } = 0.0;

        /// <summary>Gravitational acceleration (m/s²), downward along −Z.</summary>
        public double Gravity { get; set; } = 9.81;

        /// <summary>
        /// Vertical area load in N/m² (snow, cladding, live load) acting on
        /// <see cref="LoadedArea"/> and shared by the laths in proportion to
        /// their length (each metre of lath carries LoadedArea / total lath
        /// length square metres). 0 = none.
        /// </summary>
        public double AreaLoad { get; set; } = 0.0;

        /// <summary>Area the <see cref="AreaLoad"/> acts on, m² (0 = the reference mesh's area).</summary>
        public double LoadedArea { get; set; } = 0.0;

        /// <summary>Concentrated forces (N) attached to the nearest node.</summary>
        public IReadOnlyList<(Vec3d Point, Vec3d Force)>? PointLoads { get; set; }

        /// <summary>
        /// Signed height of each lath's axis above the surface along its
        /// normal (model units), one value per lath or a single value for
        /// all; null or empty = every axis on its curve (the laths of a
        /// crossing share one point, as before). With offsets the frame nodes
        /// stay on the curves and every element end hangs on a rigid link
        /// h · n from its node, so two laths stacked at a crossing are joined
        /// through the lever arm of the stack (see the class remarks). For
        /// laths of a <see cref="LathProfile"/> in layers, layer k has
        /// h = profile.Lift + k · depth (<see cref="LathProfile.NormalHigh"/>
        /// − <see cref="LathProfile.NormalLow"/>), outward.
        /// </summary>
        public IReadOnlyList<double>? AxisOffsets { get; set; }
        /// Number of linear buckling modes to compute from the axial forces of
        /// the solve (0 = none, the default). The first positive factor λ_1
        /// times the applied load is the Euler critical load of the net; see
        /// <see cref="Result.BucklingFactors"/> / <see cref="Result.BucklingModes"/>.
        /// </summary>
        public int BucklingModes { get; set; } = 0;

        /// <summary>
        /// Relative change of a factor between subspace iterations below which
        /// it counts as converged (default 1e-6; the true error is a few times
        /// the change when the spectrum is dense).
        /// </summary>
        public double BucklingTolerance { get; set; } = 1e-6;
    }

    public readonly struct Result
    {
        /// <summary>Unique node positions after merging (original, undeformed).</summary>
        public readonly Vec3d[] Nodes;

        /// <summary>
        /// Per unique node: the (curve, pointIndex) locations merged into it.
        /// Indices refer to the input polylines; points inserted at joints
        /// that were not input samples carry a negative index.
        /// </summary>
        public readonly List<(int Curve, int Index)>[] NodeMap;

        /// <summary>Displacement vector per unique node.</summary>
        public readonly Vec3d[] Displacements;

        public readonly double MaxDisplacement;

        /// <summary>Per element: (curve, startIndex) it was built from.</summary>
        public readonly (int Curve, int Index)[] ElementSource;

        /// <summary>Per element: axial force N (tension positive).</summary>
        public readonly double[] AxialForce;

        /// <summary>Per element: max |bending moment| about each section axis at the ends.</summary>
        public readonly double[] BendingY;
        public readonly double[] BendingZ;

        /// <summary>Per element: combined stress / allowable stress (over 1 fails).</summary>
        public readonly double[] Utilization;

        public readonly double MaxUtilization;

        /// <summary>Number of nodes that were fixed by a support point.</summary>
        public readonly int SupportNodeCount;

        /// <summary>Per element: |torsional moment| (N·m).</summary>
        public readonly double[] Torsion;

        /// <summary>Indices (into Nodes) of the supported nodes.</summary>
        public readonly int[] SupportNodes;

        /// <summary>Reaction force per node (zero at free nodes), N.</summary>
        public readonly Vec3d[] Reactions;

        /// <summary>Sum of all applied loads, N.</summary>
        public readonly Vec3d TotalLoad;

        /// <summary>
        /// |Σ reactions + Σ loads| / |Σ loads|: global equilibrium of the
        /// solution, ≈ 1e-12 for a well-posed frame (a check of the solve,
        /// not of the model).
        /// </summary>
        public readonly double EquilibriumError;

        /// <summary>The lath polylines the frame was built from: the input with the joint points inserted.</summary>
        public readonly Vec3d[][] Laths;

        /// <summary>Number of joint nodes (where two or more laths meet).</summary>
        public readonly int JointCount;

        /// <summary>Laths (by index) that no support reaches through the net; they are left out of the solve (zero displacement, zero utilization).</summary>
        public readonly int[] FloatingLaths;

        /// <summary>Reaction moment per node (zero at free and pinned nodes), N·m, about the node.</summary>
        public readonly Vec3d[] ReactionMoments;

        /// <summary>
        /// Largest lever arm between the axes of two laths meeting at a joint
        /// (|h_i − h_j|, model units); 0 when every axis lies on its curve.
        /// </summary>
        public readonly double Eccentricity;

        /// <summary>Signed axis height per lath along the surface normal: the beams of lath c run through Laths[c][i] + AxisOffsets[c] · n.</summary>
        public readonly double[] AxisOffsets;

        /// <summary>Moment of all applied loads about the origin (each lumped load at the beam axis where it acts), N·m.</summary>
        public readonly Vec3d TotalMoment;

        /// <summary>
        /// |Σ (node × reaction + reaction moment) + TotalMoment| relative to the
        /// applied moment: global moment equilibrium of the solution (≈ 1e-12
        /// for a well-posed frame) — with axis offsets it checks that the
        /// lever arms of the stacked laths are carried into the supports.
        /// </summary>
        public readonly double MomentEquilibriumError;
        /// <summary>
        /// Positive linear buckling load factors, ascending (empty when
        /// <see cref="Options.BucklingModes"/> is 0 or no positive factor was
        /// found): the applied loads times BucklingFactors[0] is the elastic
        /// critical load of the first mode. Only factors the iteration
        /// converged on are listed.
        /// </summary>
        public readonly double[] BucklingFactors;

        /// <summary>Per factor: the mode's node translations, normalised so the largest is 1 (shape only, no amplitude).</summary>
        public readonly Vec3d[][] BucklingModes;

        /// <summary>
        /// Sturm sequence check: the number of eigenvalues of (K_e + λ K_g) φ = 0
        /// in (0, λ₁(1 − 20·tolerance)), i.e. buckling modes below the first
        /// factor found that the iteration missed — 0 when λ₁ really is the
        /// lowest; −1 when not computed (no factor, or the shifted matrix was
        /// singular).
        /// </summary>
        public readonly int BucklingMissed;

        /// <summary>Subspace iterations spent on the buckling problem (0 = not requested).</summary>
        public readonly int BucklingIterations;

        /// <summary>True when the requested number of buckling factors converged to <see cref="Options.BucklingTolerance"/>.</summary>
        public readonly bool BucklingConverged;

        public Result(Vec3d[] nodes, List<(int, int)>[] nodeMap, Vec3d[] displacements,
            double maxDisplacement, (int, int)[] elementSource,
            double[] axial, double[] bendY, double[] bendZ, double[] utilization, double maxUtilization,
            int supportNodeCount = 0)
            : this(nodes, nodeMap, displacements, maxDisplacement, elementSource, axial, bendY, bendZ,
                utilization, maxUtilization, supportNodeCount, new double[axial.Length], Array.Empty<int>(),
                new Vec3d[nodes.Length], Vec3d.Zero, 0.0, Array.Empty<Vec3d[]>(), 0, Array.Empty<int>()) { }

        public Result(Vec3d[] nodes, List<(int, int)>[] nodeMap, Vec3d[] displacements,
            double maxDisplacement, (int, int)[] elementSource,
            double[] axial, double[] bendY, double[] bendZ, double[] utilization, double maxUtilization,
            int supportNodeCount, double[] torsion, int[] supportNodes, Vec3d[] reactions,
            Vec3d totalLoad, double equilibriumError, Vec3d[][] laths, int jointCount, int[] floatingLaths)
            : this(nodes, nodeMap, displacements, maxDisplacement, elementSource, axial, bendY, bendZ,
                utilization, maxUtilization, supportNodeCount, torsion, supportNodes, reactions, totalLoad,
                equilibriumError, laths, jointCount, floatingLaths, new Vec3d[nodes.Length], 0.0, new double[laths.Length], Vec3d.Zero, 0.0,
                Array.Empty<double>(), Array.Empty<Vec3d[]>(), -1, 0, false) { }

        public Result(Vec3d[] nodes, List<(int, int)>[] nodeMap, Vec3d[] displacements,
            double maxDisplacement, (int, int)[] elementSource,
            double[] axial, double[] bendY, double[] bendZ, double[] utilization, double maxUtilization,
            int supportNodeCount, double[] torsion, int[] supportNodes, Vec3d[] reactions,
            Vec3d totalLoad, double equilibriumError, Vec3d[][] laths, int jointCount, int[] floatingLaths,
            Vec3d[] reactionMoments, double eccentricity, double[] axisOffsets, Vec3d totalMoment, double momentEquilibriumError,
            double[] bucklingFactors, Vec3d[][] bucklingModes, int bucklingMissed, int bucklingIterations, bool bucklingConverged)
        {
            BucklingFactors = bucklingFactors;
            BucklingModes = bucklingModes;
            BucklingMissed = bucklingMissed;
            BucklingIterations = bucklingIterations;
            BucklingConverged = bucklingConverged;
            TotalMoment = totalMoment;
            MomentEquilibriumError = momentEquilibriumError;
            ReactionMoments = reactionMoments;
            Eccentricity = eccentricity;
            AxisOffsets = axisOffsets;
            FloatingLaths = floatingLaths;
            SupportNodeCount = supportNodeCount;
            Nodes = nodes;
            NodeMap = nodeMap;
            Displacements = displacements;
            MaxDisplacement = maxDisplacement;
            ElementSource = elementSource;
            AxialForce = axial;
            BendingY = bendY;
            BendingZ = bendZ;
            Utilization = utilization;
            MaxUtilization = maxUtilization;
            Torsion = torsion;
            SupportNodes = supportNodes;
            Reactions = reactions;
            TotalLoad = totalLoad;
            EquilibriumError = equilibriumError;
            Laths = laths;
            JointCount = jointCount;
        }
    }

    /// <summary>
    /// Solves the network. laths are centerline polylines (e.g. sampled net
    /// curves); joints are crossing points where laths are coupled (e.g. the
    /// Points output of Net Joints) — each is inserted at its exact position
    /// into every lath that passes through it, so the laths share a node
    /// there; supports are held as <see cref="Options.Support"/> says; load is
    /// a force-per-unit-length vector applied along every element (self-weight,
    /// area and point loads come from the options).
    /// </summary>
    public static Result Compute(
        MeshData mesh,
        IReadOnlyList<Vec3d[]> laths,
        IReadOnlyList<Vec3d>? joints,
        IReadOnlyList<Vec3d>? supports,
        LathProfile profile,
        Vec3d loadPerUnitLength,
        Options? options = null)
    {
        options ??= new Options();
        if (profile.Width <= 0 || profile.Thickness <= 0)
            throw new ArgumentException("Profile width and thickness must be positive.", nameof(profile));
        if (options.E <= 0)
            throw new ArgumentException("Young's modulus must be positive.", nameof(options));

        // ---- Lath polylines with the joints inserted ----------------------
        double avgSeg = 0, maxSeg = 0;
        int segCount = 0, pointCount = 0;
        for (int c = 0; c < laths.Count; c++)
        {
            pointCount += laths[c].Length;
            for (int i = 1; i < laths[c].Length; i++)
            {
                double l = (laths[c][i] - laths[c][i - 1]).Length;
                avgSeg += l; maxSeg = Math.Max(maxSeg, l);
                segCount++;
            }
        }
        if (pointCount < 2)
            throw new ArgumentException("At least one lath with two points is required.", nameof(laths));
        avgSeg = segCount > 0 ? avgSeg / segCount : 1.0;
        double snap = options.SnapTolerance > 0 ? options.SnapTolerance : 0.55 * avgSeg;
        double jointTol = options.SnapTolerance > 0 ? options.SnapTolerance : 0.25 * avgSeg;

        // points of each lath: (position, original index or −1)
        var work = new List<(Vec3d P, int Src)>[laths.Count];
        for (int c = 0; c < laths.Count; c++)
        {
            work[c] = new List<(Vec3d, int)>(laths[c].Length + 4);
            for (int i = 0; i < laths[c].Length; i++) work[c].Add((laths[c][i], i));
        }

        var segGrid = new SegmentGrid(laths, Math.Max(avgSeg, 1e-12));
        var allJoints = new List<Vec3d>();
        if (joints != null) allJoints.AddRange(joints);
        if (options.CoupleEndsOnLaths)
        {
            // T-junctions: a lath end sitting on another lath's segment is a joint there
            double onTol = 0.05 * avgSeg;
            for (int c = 0; c < laths.Count; c++)
            {
                var l = laths[c];
                if (l.Length < 2 || (l[0] - l[l.Length - 1]).LengthSquared < 1e-24) continue;
                foreach (var end in new[] { l[0], l[l.Length - 1] })
                {
                    bool onOther = false;
                    foreach (var (oc, k) in segGrid.Near(end, onTol))
                    {
                        if (oc == c) continue;
                        if (SegmentDistance(end, laths[oc][k], laths[oc][k + 1], out _) <= onTol) { onOther = true; break; }
                    }
                    if (onOther) allJoints.Add(end);
                }
            }
        }

        // joints closer than half the joint tolerance are one joint (three laths
        // through one node give three pairwise crossings a hair apart; inserted
        // separately they would leave near-zero elements between them)
        if (allJoints.Count > 1)
        {
            double mergeJ = 0.5 * jointTol;
            var jGrid = new PointGrid(allJoints, Math.Max(mergeJ, 1e-12));
            var jParent = new int[allJoints.Count];
            for (int i = 0; i < jParent.Length; i++) jParent[i] = i;
            int JFind(int x) { while (jParent[x] != x) { jParent[x] = jParent[jParent[x]]; x = jParent[x]; } return x; }
            for (int i = 0; i < allJoints.Count; i++)
                foreach (int j in jGrid.Within(allJoints[i], mergeJ)) if (j != i) jParent[JFind(i)] = JFind(j);
            var sum = new Dictionary<int, (Vec3d S, int N)>();
            for (int i = 0; i < allJoints.Count; i++)
            {
                int r = JFind(i);
                sum.TryGetValue(r, out var acc);
                sum[r] = (acc.S + allJoints[i], acc.N + 1);
            }
            allJoints = sum.Values.Select(v => v.S / v.N).ToList();
        }

        // insertions: per lath, (segment, t, point); each joint goes into each
        // lath at most once, at the lath's closest segment
        var inserts = new List<(int Seg, double T, Vec3d P)>[laths.Count];
        foreach (var jp in allJoints)
        {
            var best = new Dictionary<int, (int Seg, double T, double D)>();
            foreach (var (c, k) in segGrid.Near(jp, jointTol))
            {
                double d = SegmentDistance(jp, laths[c][k], laths[c][k + 1], out double t);
                if (d > jointTol) continue;
                if (!best.TryGetValue(c, out var b) || d < b.D) best[c] = (k, t, d);
            }
            foreach (var kv in best)
                (inserts[kv.Key] ??= new List<(int, double, Vec3d)>()).Add((kv.Value.Seg, kv.Value.T, jp));
        }
        for (int c = 0; c < laths.Count; c++)
        {
            if (inserts[c] == null) continue;
            var l = laths[c];
            // snap to an existing sample when the joint falls on (or next to) it
            var extra = new List<(int Seg, double T, Vec3d P)>();
            foreach (var (seg, t, p) in inserts[c])
            {
                double segLen = (l[seg + 1] - l[seg]).Length;
                double tolT = segLen > 0 ? Math.Min(0.25, 1e-3 * avgSeg / segLen + 0.02) : 0.5;
                if (t <= tolT) work[c][seg] = (p, work[c][seg].Src);
                else if (t >= 1 - tolT) work[c][seg + 1] = (p, work[c][seg + 1].Src);
                else extra.Add((seg, t, p));
            }
            extra.Sort((x, y) => x.Seg != y.Seg ? y.Seg.CompareTo(x.Seg) : y.T.CompareTo(x.T));
            foreach (var (seg, _, p) in extra) work[c].Insert(seg + 1, (p, -1));
        }
        var lathsW = new Vec3d[laths.Count][];
        for (int c = 0; c < laths.Count; c++)
        {
            // drop zero-length duplicates created by coinciding insertions
            var pts = new List<Vec3d>();
            var src = new List<int>();
            foreach (var (p, sidx) in work[c])
            {
                if (pts.Count > 0 && (p - pts[pts.Count - 1]).LengthSquared < 1e-24) { if (sidx >= 0 && src[src.Count - 1] < 0) src[src.Count - 1] = sidx; continue; }
                pts.Add(p); src.Add(sidx);
            }
            lathsW[c] = pts.ToArray();
            work[c] = new List<(Vec3d, int)>();
            for (int i = 0; i < pts.Count; i++) work[c].Add((pts[i], src[i]));
        }

        // ---- Nodes: coincident points merge ---------------------------------
        var rawPoints = new List<Vec3d>();
        var rawMap = new List<(int Curve, int Index)>();
        var rawCurve = new List<int>();
        for (int c = 0; c < laths.Count; c++)
            for (int i = 0; i < lathsW[c].Length; i++)
            {
                rawPoints.Add(lathsW[c][i]);
                rawMap.Add((c, work[c][i].Src >= 0 ? work[c][i].Src : -1 - i));
                rawCurve.Add(c);
            }

        var parent = new int[rawPoints.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }

        var grid = new PointGrid(rawPoints, Math.Max(avgSeg, 1e-12));
        double coincident = 1e-6 * avgSeg;
        for (int i = 0; i < rawPoints.Count; i++)
            foreach (int j in grid.Within(rawPoints[i], coincident))
                if (j != i) Union(i, j);

        var nodeIndex = new Dictionary<int, int>();
        var nodes = new List<Vec3d>();
        var nodeMap = new List<List<(int, int)>>();
        var rawToNode = new int[rawPoints.Count];
        for (int i = 0; i < rawPoints.Count; i++)
        {
            int root = Find(i);
            if (!nodeIndex.TryGetValue(root, out int ni))
            {
                ni = nodes.Count;
                nodeIndex[root] = ni;
                nodes.Add(rawPoints[root]);
                nodeMap.Add(new List<(int, int)>());
            }
            rawToNode[i] = ni;
            nodeMap[ni].Add(rawMap[i]);
        }

        // Elements: consecutive points of each lath (skip zero-length after merging)
        var elemA = new List<int>();
        var elemB = new List<int>();
        var elemCurve = new List<int>();
        var elemSrc = new List<(int Curve, int Index)>();
        int rawCursor = 0;
        for (int c = 0; c < laths.Count; c++)
        {
            int lastSrc = 0;
            for (int i = 1; i < lathsW[c].Length; i++)
            {
                int prevSrc = work[c][i - 1].Src;
                if (prevSrc >= 0) lastSrc = prevSrc;
                int a = rawToNode[rawCursor + i - 1];
                int b = rawToNode[rawCursor + i];
                if (a != b)
                {
                    elemA.Add(a);
                    elemB.Add(b);
                    elemCurve.Add(c);
                    elemSrc.Add((c, lastSrc));
                }
            }
            rawCursor += lathsW[c].Length;
        }
        int ne = elemA.Count;
        if (ne == 0)
            throw new ArgumentException("The laths contain no usable segments.", nameof(laths));

        // ---- Degrees of freedom ----------------------------------------------
        // Every node has 3 translations. Rotations: one block per node, or
        // with non-rigid joints one block per (node, lath) at nodes where
        // several laths meet, tied together by rotational springs.
        bool rigid = double.IsPositiveInfinity(options.JointRotationalStiffness);
        var lathsAtNode = new List<int>[nodes.Count];
        for (int e = 0; e < ne; e++)
        {
            foreach (int n in new[] { elemA[e], elemB[e] })
            {
                var list = lathsAtNode[n] ??= new List<int>(2);
                if (!list.Contains(elemCurve[e])) list.Add(elemCurve[e]);
            }
        }
        int jointCount = 0;
        for (int n = 0; n < nodes.Count; n++) if (lathsAtNode[n] != null && lathsAtNode[n].Count > 1) jointCount++;

        int ndof = 0;
        var transBase = new int[nodes.Count];
        var rotBase = new Dictionary<(int Node, int Curve), int>();
        var rotBlocks = new List<int>[nodes.Count];
        for (int n = 0; n < nodes.Count; n++)
        {
            transBase[n] = ndof; ndof += 3;
            rotBlocks[n] = new List<int>(1);
            var ls = lathsAtNode[n];
            if (!rigid && ls != null && ls.Count > 1)
            {
                foreach (int c in ls) { rotBase[(n, c)] = ndof; rotBlocks[n].Add(ndof); ndof += 3; }
            }
            else
            {
                int b = ndof; ndof += 3;
                rotBlocks[n].Add(b);
                if (ls != null) foreach (int c in ls) rotBase[(n, c)] = b;
            }
        }
        int[] ElemDofs(int e)
        {
            int a = elemA[e], b = elemB[e], c = elemCurve[e];
            int ra = rotBase[(a, c)], rb = rotBase[(b, c)];
            return new[]
            {
                transBase[a], transBase[a] + 1, transBase[a] + 2, ra, ra + 1, ra + 2,
                transBase[b], transBase[b] + 1, transBase[b] + 2, rb, rb + 1, rb + 2,
            };
        }

        // ---- Axis offsets: the beams run h_c · n above the nodes -------------
        var offsets = ResolveOffsets(options.AxisOffsets, laths.Count);
        bool eccentric = false;
        foreach (double h in offsets) if (h != 0.0) { eccentric = true; break; }
        var proj = new MeshProjection(mesh);
        var nodeNormal = new Vec3d[nodes.Count];
        double eccentricity = 0.0;
        if (eccentric)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                nodeNormal[n] = proj.ClosestPoint(nodes[n], proj.NearestVertexGlobal(nodes[n])).SmoothNormal.Normalized();
                var ls = lathsAtNode[n];
                if (ls == null || ls.Count < 2) continue;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (int c in ls) { lo = Math.Min(lo, offsets[c]); hi = Math.Max(hi, offsets[c]); }
                eccentricity = Math.Max(eccentricity, hi - lo);
            }
        }
        Vec3d Arm(int node, int curve) => eccentric ? offsets[curve] * nodeNormal[node] : Vec3d.Zero;

        // Supports: each support point holds the nearest node within snap
        var fixedDof = new bool[ndof];
        var supportNodes = new List<int>();
        if (supports != null)
        {
            var nodeGrid = new PointGrid(nodes, Math.Max(snap, 1e-12));
            foreach (var sp in supports)
            {
                int best = NearestNode(nodeGrid, nodes, sp, snap);
                if (best < 0 || supportNodes.Contains(best)) continue;
                supportNodes.Add(best);
                for (int d = 0; d < 3; d++) fixedDof[transBase[best] + d] = true;
                if (options.Support == SupportKind.Fixed)
                    foreach (int rb in rotBlocks[best]) for (int d = 0; d < 3; d++) fixedDof[rb + d] = true;
            }
        }

        // Parts of the net that no support reaches (a lath crossing nothing, a
        // patch cut off by a K = 0 line) would make the stiffness matrix
        // singular; they are left out of the solve and reported instead.
        var comp = new int[nodes.Count];
        for (int i = 0; i < comp.Length; i++) comp[i] = i;
        int CFind(int x) { while (comp[x] != x) { comp[x] = comp[comp[x]]; x = comp[x]; } return x; }
        for (int e = 0; e < ne; e++) comp[CFind(elemA[e])] = CFind(elemB[e]);
        var anchored = new HashSet<int>();
        foreach (int sn in supportNodes) anchored.Add(CFind(sn));
        var floatingCurves = new HashSet<int>();
        for (int e = 0; e < ne; e++)
            if (!anchored.Contains(CFind(elemA[e]))) floatingCurves.Add(elemCurve[e]);
        for (int n = 0; n < nodes.Count; n++)
        {
            if (anchored.Contains(CFind(n))) continue;
            for (int d = 0; d < 3; d++) fixedDof[transBase[n] + d] = true;
            foreach (int rb in rotBlocks[n]) for (int d = 0; d < 3; d++) fixedDof[rb + d] = true;
        }
        if (anchored.Count == 0)
            throw new ArgumentException("No support point lies on a lath: nothing holds the net.", nameof(supports));

        // ---- Section properties ------------------------------------------
        // From the profile's section (rectangle, round bar or custom polygon,
        // see LathProfile.SectionProperties): the same profile Lath Sweep
        // builds is what the frame is made of.
        var section = profile.SectionProperties();
        double A = section.Area;
        double Iy = section.IA; // bending through the thickness (about the first axis)
        double Iz = section.IB; // bending across the width (about the second axis)
        double J = section.J;   // Saint-Venant torsion
        double Wy = Iy / section.ExtentB;
        double Wz = Iz / section.ExtentA;
        double tauPerTorque = J > 0 ? section.TwistLength / J : 0; // τ_max = T · t_twist / J
        double E = options.E;
        double G = options.G > 0 ? options.G : options.E / 2.6;

        // ---- Loads -----------------------------------------------------------
        double totalLength = 0;
        for (int e = 0; e < ne; e++) totalLength += (nodes[elemB[e]] - nodes[elemA[e]]).Length;
        Vec3d q = loadPerUnitLength;
        if (options.Density > 0) q = q + new Vec3d(0, 0, -options.Density * A * options.Gravity);
        if (options.AreaLoad != 0 && totalLength > 0)
        {
            double area = options.LoadedArea > 0 ? options.LoadedArea : MeshArea(mesh);
            q = q + new Vec3d(0, 0, -options.AreaLoad * area / totalLength);
        }

        // ---- Assemble -----------------------------------------------------
        var K = new Dictionary<long, double>();
        var F = new double[ndof];
        void AddK(int r, int c, double v)
        {
            if (v == 0.0) return;
            long key = (long)r * ndof + c;
            K.TryGetValue(key, out double cur);
            K[key] = cur + v;
        }

        var axial = new double[ne];
        var bendY = new double[ne];
        var bendZ = new double[ne];
        var torsion = new double[ne];
        var elemLen = new double[ne];
        var elemRot = new Vec3d[ne][]; // local axes [ex, ey, ez] per element
        var elemArm = new Vec3d[ne][]; // rigid link from each end's node to the beam end (zero without offsets)
        var nodeRotStiff = new double[nodes.Count];
        Vec3d totalMoment = Vec3d.Zero;

        for (int e = 0; e < ne; e++)
        {
            Vec3d rA = Arm(elemA[e], elemCurve[e]), rB = Arm(elemB[e], elemCurve[e]);
            elemArm[e] = new[] { rA, rB };
            Vec3d p1 = nodes[elemA[e]] + rA, p2 = nodes[elemB[e]] + rB;
            Vec3d d = p2 - p1;
            double L = d.Length;
            if (L < 1e-12) continue;
            elemLen[e] = L;

            Vec3d ex = d / L;
            Vec3d mid = 0.5 * (nodes[elemA[e]] + nodes[elemB[e]]);
            Vec3d nrm = proj.ClosestPoint(mid, proj.NearestVertexGlobal(mid)).SmoothNormal;
            Vec3d ey;
            if (profile.Upright)
            {
                ey = nrm - Vec3d.Dot(nrm, ex) * ex;
                if (ey.LengthSquared < 1e-20) ey = Math.Abs(ex.Y) < 0.9
                    ? Vec3d.Cross(ex, new Vec3d(0, 1, 0)) : Vec3d.Cross(ex, new Vec3d(1, 0, 0));
            }
            else
            {
                ey = Vec3d.Cross(nrm, ex);
                if (ey.LengthSquared < 1e-20) ey = Math.Abs(ex.Y) < 0.9
                    ? Vec3d.Cross(ex, new Vec3d(0, 1, 0)) : Vec3d.Cross(ex, new Vec3d(1, 0, 0));
            }
            ey = ey.Normalized();
            Vec3d ez = Vec3d.Cross(ex, ey);
            var rot = new[] { ex, ey, ez };
            elemRot[e] = rot;

            var ke = LocalStiffness(E, G, A, Iy, Iz, J, L);
            double kr = 4 * E * Math.Max(Iy, Iz) / L;
            nodeRotStiff[elemA[e]] = Math.Max(nodeRotStiff[elemA[e]], kr);
            nodeRotStiff[elemB[e]] = Math.Max(nodeRotStiff[elemB[e]], kr);

            // K_global = Tᵀ ke T with T = diag(Q, Q, Q, Q), Q rows = local axes
            var dofs = ElemDofs(e);
            var kg = new double[12, 12];
            for (int r = 0; r < 12; r++)
            {
                int rb = r / 3 * 3, ra = r % 3;
                for (int c = 0; c < 12; c++)
                {
                    int cb = c / 3 * 3, ca = c % 3;
                    double sum = 0;
                    for (int a = 0; a < 3; a++)
                    {
                        double qa = Component(rot[a], ra);
                        if (qa == 0) continue;
                        for (int b = 0; b < 3; b++)
                            sum += qa * ke[rb + a, cb + b] * Component(rot[b], ca);
                    }
                    kg[r, c] = sum;
                }
            }
            // beam ends on rigid links from their nodes: K_nodes = Hᵀ K_axis H
            if (eccentric) kg = OffsetStiffness(kg, rA, rB);
            for (int r = 0; r < 12; r++)
                for (int c = 0; c < 12; c++)
                    AddK(dofs[r], dofs[c], kg[r, c]);

            // Distributed load -> lumped forces at the beam ends; an end on a
            // rigid link hands its node the force and the moment r × f
            Vec3d f = 0.5 * L * q;
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? elemA[e] : elemB[e];
                F[transBase[node] + 0] += f.X;
                F[transBase[node] + 1] += f.Y;
                F[transBase[node] + 2] += f.Z;
                if (anchored.Contains(CFind(node))) totalMoment = totalMoment + Vec3d.Cross(nodes[node] + elemArm[e][end], f);
                if (!eccentric) continue;
                Vec3d m = Vec3d.Cross(elemArm[e][end], f);
                int rbase = dofs[end * 6 + 3];
                F[rbase + 0] += m.X; F[rbase + 1] += m.Y; F[rbase + 2] += m.Z;
            }
        }

        // joint springs between the rotation blocks of a non-rigid joint
        if (!rigid)
        {
            double kn = Math.Max(0.0, options.JointRotationalStiffness);
            for (int n = 0; n < nodes.Count; n++)
            {
                var blocks = rotBlocks[n];
                if (blocks.Count < 2) continue;
                Vec3d nrm = proj.ClosestPoint(nodes[n], proj.NearestVertexGlobal(nodes[n])).SmoothNormal.Normalized();
                Vec3d t1 = Math.Abs(nrm.Z) < 0.9 ? Vec3d.Cross(nrm, new Vec3d(0, 0, 1)) : Vec3d.Cross(nrm, new Vec3d(1, 0, 0));
                t1 = t1.Normalized();
                Vec3d t2 = Vec3d.Cross(nrm, t1);
                double kLock = 1e3 * Math.Max(nodeRotStiff[n], 1e-12);
                for (int k = 1; k < blocks.Count; k++)
                {
                    int bi = blocks[0], bj = blocks[k];
                    foreach (var (axis, stiff) in new[] { (t1, kLock), (t2, kLock), (nrm, kn) })
                    {
                        if (stiff <= 0) continue;
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 3; c++)
                            {
                                double v = stiff * Component(axis, r) * Component(axis, c);
                                AddK(bi + r, bi + c, v); AddK(bj + r, bj + c, v);
                                AddK(bi + r, bj + c, -v); AddK(bj + r, bi + c, -v);
                            }
                    }
                }
            }
        }

        // point loads to the nearest node
        if (options.PointLoads != null && options.PointLoads.Count > 0)
        {
            var nodeGrid = new PointGrid(nodes, Math.Max(snap, 1e-12));
            foreach (var (pt, force) in options.PointLoads)
            {
                int best = NearestNode(nodeGrid, nodes, pt, snap);
                if (best < 0) best = NearestNodeBrute(nodes, pt);
                F[transBase[best] + 0] += force.X; F[transBase[best] + 1] += force.Y; F[transBase[best] + 2] += force.Z;
                if (anchored.Contains(CFind(best))) totalMoment = totalMoment + Vec3d.Cross(nodes[best], force);
            }
        }

        Vec3d totalLoad = Vec3d.Zero;
        for (int n = 0; n < nodes.Count; n++)
            if (anchored.Contains(CFind(n)))
                totalLoad = totalLoad + new Vec3d(F[transBase[n]], F[transBase[n] + 1], F[transBase[n] + 2]);

        // ---- Solve ---------------------------------------------------------
        var freeDofs = new List<int>();
        var dofMap = new int[ndof];
        for (int i = 0; i < ndof; i++)
        {
            if (fixedDof[i]) dofMap[i] = -1;
            else { dofMap[i] = freeDofs.Count; freeDofs.Add(i); }
        }

        if (freeDofs.Count == 0)
            throw new ArgumentException("Every node is fixed; nothing to solve.", nameof(supports));

        int nfree = freeDofs.Count;
        var Kff = new SparseSymmetricSolver.Builder(nfree);
        foreach (var kv in K)
        {
            int r = (int)(kv.Key / ndof);
            int c = (int)(kv.Key % ndof);
            int fr = dofMap[r], fc = dofMap[c];
            if (fr >= 0 && fc >= 0 && fr <= fc) Kff.Add(fr, fc, kv.Value);
        }
        var Ff = new double[nfree];
        for (int i = 0; i < nfree; i++) Ff[i] = F[freeDofs[i]];

        var solver = SparseSymmetricSolver.Factor(Kff, 1e-14);
        if (solver == null)
            throw new InvalidOperationException(
                "The frame stiffness matrix is singular — the structure is a mechanism " +
                "(unsupported or under-connected). Add supports at enough joints, use fixed supports, or stiffen the joints.");

        double[] u = solver.Solve(Ff);

        // Scale-aware consistency check: residual relative to |K||u| + |F|
        double res = 0, uMax = 0, fMax = 0;
        var Ku = Kff.Multiply(u);
        for (int i = 0; i < nfree; i++)
        {
            res = Math.Max(res, Math.Abs(Ku[i] - Ff[i]));
            uMax = Math.Max(uMax, Math.Abs(u[i]));
            fMax = Math.Max(fMax, Math.Abs(Ff[i]));
        }
        double scale = Kff.InfinityNorm() * uMax + fMax;
        if (double.IsNaN(res) || (scale > 0 && res > 1e-6 * scale))
            throw new InvalidOperationException(
                "The frame solve is inconsistent — the structure is likely a mechanism " +
                "(unsupported or under-connected). Add supports, use fixed supports, or stiffen the joints.");

        double U(int dof) { int m = dofMap[dof]; return m >= 0 ? u[m] : 0.0; }

        var displacements = new Vec3d[nodes.Count];
        double maxDisp = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var disp = new Vec3d(U(transBase[i]), U(transBase[i] + 1), U(transBase[i] + 2));
            displacements[i] = disp;
            maxDisp = Math.Max(maxDisp, disp.Length);
        }

        // ---- Reactions: R = K u − F on the held degrees of freedom -----------
        var reactDof = new double[ndof];
        foreach (var kv in K)
        {
            int r = (int)(kv.Key / ndof);
            if (!fixedDof[r]) continue;
            int c = (int)(kv.Key % ndof);
            reactDof[r] += kv.Value * U(c);
        }
        var reactions = new Vec3d[nodes.Count];
        var reactionMoments = new Vec3d[nodes.Count];
        Vec3d reactionSum = Vec3d.Zero;
        foreach (int n in supportNodes)
        {
            var r = new Vec3d(reactDof[transBase[n]] - F[transBase[n]], reactDof[transBase[n] + 1] - F[transBase[n] + 1], reactDof[transBase[n] + 2] - F[transBase[n] + 2]);
            reactions[n] = r;
            reactionSum = reactionSum + r;
            if (options.Support == SupportKind.Fixed)
            {
                Vec3d m = Vec3d.Zero;
                foreach (int rb in rotBlocks[n])
                    m = m + new Vec3d(reactDof[rb] - F[rb], reactDof[rb + 1] - F[rb + 1], reactDof[rb + 2] - F[rb + 2]);
                reactionMoments[n] = m;
            }
        }
        double eqErr = totalLoad.Length > 0 ? (reactionSum + totalLoad).Length / totalLoad.Length : 0.0;
        Vec3d momentSum = Vec3d.Zero;
        foreach (int n in supportNodes) momentSum = momentSum + Vec3d.Cross(nodes[n], reactions[n]) + reactionMoments[n];
        var (bbLo, bbHi) = (nodes[0], nodes[0]);
        foreach (var p in nodes)
        {
            bbLo = new Vec3d(Math.Min(bbLo.X, p.X), Math.Min(bbLo.Y, p.Y), Math.Min(bbLo.Z, p.Z));
            bbHi = new Vec3d(Math.Max(bbHi.X, p.X), Math.Max(bbHi.Y, p.Y), Math.Max(bbHi.Z, p.Z));
        }
        double momentScale = Math.Max(totalMoment.Length, totalLoad.Length * (bbHi - bbLo).Length);
        double momentErr = momentScale > 0 ? (momentSum + totalMoment).Length / momentScale : 0.0;

        // ---- Element forces and utilization --------------------------------
        var utilization = new double[ne];
        double maxUtil = 0;
        for (int e = 0; e < ne; e++)
        {
            if (elemRot[e] == null) continue;
            double L = elemLen[e];
            var dofs = ElemDofs(e);
            var rot = elemRot[e];
            // global end displacements: a beam end on a rigid link moves by u + θ × r
            var ug = new double[12];
            for (int i = 0; i < 12; i++) ug[i] = U(dofs[i]);
            if (eccentric)
                for (int end = 0; end < 2; end++)
                {
                    int t = end * 6;
                    var th = new Vec3d(ug[t + 3], ug[t + 4], ug[t + 5]);
                    Vec3d dv = Vec3d.Cross(th, elemArm[e][end]);
                    ug[t] += dv.X; ug[t + 1] += dv.Y; ug[t + 2] += dv.Z;
                }
            var ul = new double[12];
            for (int blk = 0; blk < 4; blk++)
                for (int a = 0; a < 3; a++)
                {
                    double sum = 0;
                    for (int b = 0; b < 3; b++) sum += Component(rot[a], b) * ug[blk * 3 + b];
                    ul[blk * 3 + a] = sum;
                }

            var ke = LocalStiffness(E, G, A, Iy, Iz, J, L);
            var fl = new double[12];
            for (int r = 0; r < 12; r++)
                for (int c = 0; c < 12; c++)
                    fl[r] += ke[r, c] * ul[c];

            axial[e] = -fl[0]; // tension positive at end 1
            torsion[e] = Math.Abs(fl[3]);
            bendY[e] = Math.Max(Math.Abs(fl[4]), Math.Abs(fl[10]));
            bendZ[e] = Math.Max(Math.Abs(fl[5]), Math.Abs(fl[11]));

            double sigma = Math.Abs(axial[e]) / A + bendY[e] / Wy + bendZ[e] / Wz;
            double tau = torsion[e] * tauPerTorque;
            utilization[e] = Math.Sqrt(sigma * sigma + 3 * tau * tau) / options.AllowableStress;
            if (utilization[e] > maxUtil) maxUtil = utilization[e];
        }

        // ---- Linear buckling: (K_e + λ K_g) φ = 0 ---------------------------
        var bucklingFactors = Array.Empty<double>();
        var bucklingModes = Array.Empty<Vec3d[]>();
        int bucklingMissed = -1, bucklingIterations = 0;
        bool bucklingConverged = false;
        if (options.BucklingModes > 0)
        {
            // B = −K_g on the free degrees of freedom: compression (N < 0) makes it positive
            var Bff = new SparseSymmetricSolver.Builder(nfree);
            double Ip = Iy + Iz;
            bool anyCompression = false;
            for (int e = 0; e < ne; e++)
            {
                if (elemRot[e] == null) continue;
                double N = axial[e];
                if (N == 0) continue;
                if (N < 0) anyCompression = true;
                var kg = GeometricStiffness(N, elemLen[e], Ip / A);
                var rot = elemRot[e];
                var dofs = ElemDofs(e);
                // global axes, then — like K_e — through the rigid links of the beam ends
                var kgg = new double[12, 12];
                for (int r = 0; r < 12; r++)
                {
                    int rb = r / 3 * 3, ra = r % 3;
                    for (int c = 0; c < 12; c++)
                    {
                        int cb = c / 3 * 3, ca = c % 3;
                        double sum = 0;
                        for (int a = 0; a < 3; a++)
                        {
                            double qa = Component(rot[a], ra);
                            if (qa == 0) continue;
                            for (int b = 0; b < 3; b++)
                                sum += qa * kg[rb + a, cb + b] * Component(rot[b], ca);
                        }
                        kgg[r, c] = sum;
                    }
                }
                if (eccentric) kgg = OffsetStiffness(kgg, elemArm[e][0], elemArm[e][1]);
                for (int r = 0; r < 12; r++)
                {
                    int fr = dofMap[dofs[r]];
                    if (fr < 0) continue;
                    for (int c = 0; c < 12; c++)
                    {
                        int fc = dofMap[dofs[c]];
                        if (fc < 0 || fr > fc) continue;
                        double sum = kgg[r, c];
                        if (sum != 0) Bff.Add(fr, fc, -sum);
                    }
                }
            }
            if (anyCompression)
            {
                var eig = SubspaceEigen.Solve(Kff, Bff, options.BucklingModes, positiveOnly: true,
                    tolerance: options.BucklingTolerance, maxIterations: 300, startVectors: new[] { u });
                if (eig != null)
                {
                    bucklingIterations = eig.Iterations;
                    bucklingConverged = eig.Converged;
                    var pos = new List<(double L, double[] V)>();
                    for (int k = 0; k < eig.Eigenvalues.Length; k++)
                        if (eig.Eigenvalues[k] > 0 && !double.IsInfinity(eig.Eigenvalues[k])) pos.Add((eig.Eigenvalues[k], eig.Eigenvectors[k]));
                    pos.Sort((x, y) => x.L.CompareTo(y.L));
                    if (pos.Count > options.BucklingModes) pos.RemoveRange(options.BucklingModes, pos.Count - options.BucklingModes);
                    bucklingFactors = pos.Select(x => x.L).ToArray();
                    bucklingModes = new Vec3d[pos.Count][];
                    for (int m = 0; m < pos.Count; m++)
                    {
                        var v = pos[m].V;
                        var mode = new Vec3d[nodes.Count];
                        double big = 0;
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            double Mx(int dof) { int mm = dofMap[dof]; return mm >= 0 ? v[mm] : 0.0; }
                            mode[i] = new Vec3d(Mx(transBase[i]), Mx(transBase[i] + 1), Mx(transBase[i] + 2));
                            big = Math.Max(big, mode[i].Length);
                        }
                        if (big > 0) for (int i = 0; i < nodes.Count; i++) mode[i] = mode[i] / big;
                        bucklingModes[m] = mode;
                    }
                    if (bucklingFactors.Length > 0)
                    {
                        // Sturm check just below the first factor: any eigenvalue in (0, μ) was
                        // missed. Rayleigh–Ritz estimates converge from above by a few times the
                        // last change, so the margin is a multiple of the tolerance.
                        double mu = bucklingFactors[0] * (1 - 20 * Math.Max(options.BucklingTolerance, 1e-12));
                        bucklingMissed = SubspaceEigen.CountBelow(Kff, Bff, mu) ?? -1;
                    }
                }
            }
            else bucklingConverged = true; // nothing in compression: no Euler buckling under this load
        }

        return new Result(nodes.ToArray(), nodeMap.ToArray(), displacements, maxDisp,
            elemSrc.ToArray(), axial, bendY, bendZ, utilization, maxUtil, supportNodes.Count,
            torsion, supportNodes.ToArray(), reactions, totalLoad, eqErr, lathsW, jointCount, floatingCurves.OrderBy(c => c).ToArray(),
            reactionMoments, eccentricity, offsets, totalMoment, momentErr,
            bucklingFactors, bucklingModes, bucklingMissed, bucklingIterations, bucklingConverged);
    }

    /// <summary>One axis offset per lath from the option (null / empty → zeros, one value → all laths).</summary>
    private static double[] ResolveOffsets(IReadOnlyList<double>? given, int lathCount)
    {
        var h = new double[lathCount];
        if (given == null || given.Count == 0) return h;
        if (given.Count != 1 && given.Count != lathCount)
            throw new ArgumentException($"AxisOffsets has {given.Count} values for {lathCount} laths: give one per lath or one for all.", nameof(given));
        for (int c = 0; c < lathCount; c++)
        {
            double v = given[given.Count == 1 ? 0 : c];
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("AxisOffsets must be finite.", nameof(given));
            h[c] = v;
        }
        return h;
    }

    /// <summary>
    /// Rigid end offsets: K_nodes = Hᵀ K_axis H with H = diag(H_A, H_B),
    /// H_end = [[I, −[r]×], [0, I]] so that u_axis = u_node + θ × r and
    /// θ_axis = θ (GSA theory, "Offsets"; see the class remarks).
    /// </summary>
    public static double[,] OffsetStiffness(double[,] kg, Vec3d rA, Vec3d rB)
    {
        var H = new double[12, 12];
        for (int i = 0; i < 12; i++) H[i, i] = 1.0;
        void Link(int t, Vec3d r)
        {
            // S = −[r]×: u += S θ  ⇔  u += θ × r
            H[t + 0, t + 4] = r.Z; H[t + 0, t + 5] = -r.Y;
            H[t + 1, t + 3] = -r.Z; H[t + 1, t + 5] = r.X;
            H[t + 2, t + 3] = r.Y; H[t + 2, t + 4] = -r.X;
        }
        Link(0, rA);
        Link(6, rB);
        var kh = new double[12, 12]; // kg H
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++)
            {
                double s = 0;
                for (int k = 0; k < 12; k++) s += kg[i, k] * H[k, j];
                kh[i, j] = s;
            }
        var res = new double[12, 12]; // Hᵀ (kg H)
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++)
            {
                double s = 0;
                for (int k = 0; k < 12; k++) s += H[k, i] * kh[k, j];
                res[i, j] = s;
            }
        return res;
    }

    private static int NearestNode(PointGrid grid, List<Vec3d> nodes, Vec3d p, double radius)
    {
        int best = -1;
        double bestD = radius * radius;
        foreach (int i in grid.Within(p, radius))
        {
            double d = (nodes[i] - p).LengthSquared;
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static int NearestNodeBrute(List<Vec3d> nodes, Vec3d p)
    {
        int best = 0; double bestD = double.MaxValue;
        for (int i = 0; i < nodes.Count; i++) { double d = (nodes[i] - p).LengthSquared; if (d < bestD) { bestD = d; best = i; } }
        return best;
    }

    private static double SegmentDistance(Vec3d p, Vec3d a, Vec3d b, out double t)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        t = len2 < 1e-30 ? 0.0 : Math.Max(0.0, Math.Min(1.0, Vec3d.Dot(p - a, ab) / len2));
        return (a + t * ab - p).Length;
    }

    private static double MeshArea(MeshData mesh)
    {
        double s = 0;
        foreach (var f in mesh.Faces)
            for (int k = 1; k + 1 < f.Length; k++)
                s += 0.5 * Vec3d.Cross(mesh.Vertices[f[k]] - mesh.Vertices[f[0]], mesh.Vertices[f[k + 1]] - mesh.Vertices[f[0]]).Length;
        return s;
    }

    /// <summary>Uniform grid of polyline segments (by their bounding boxes) for proximity queries.</summary>
    private sealed class SegmentGrid
    {
        private readonly double _cell;
        private readonly Dictionary<(long, long, long), List<(int, int)>> _cells = new Dictionary<(long, long, long), List<(int, int)>>();

        public SegmentGrid(IReadOnlyList<Vec3d[]> laths, double cell)
        {
            _cell = cell;
            for (int c = 0; c < laths.Count; c++)
                for (int k = 0; k + 1 < laths[c].Length; k++)
                {
                    Vec3d a = laths[c][k], b = laths[c][k + 1];
                    var lo = Key(new Vec3d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
                    var hi = Key(new Vec3d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
                    long span = (hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1);
                    if (span > 200000) { lo = hi = Key(0.5 * (a + b)); } // pathological segment: centre cell only
                    for (long x = lo.Item1; x <= hi.Item1; x++)
                        for (long y = lo.Item2; y <= hi.Item2; y++)
                            for (long z = lo.Item3; z <= hi.Item3; z++)
                            {
                                if (!_cells.TryGetValue((x, y, z), out var list)) { list = new List<(int, int)>(); _cells[(x, y, z)] = list; }
                                list.Add((c, k));
                            }
                }
        }

        public IEnumerable<(int Curve, int Segment)> Near(Vec3d p, double radius)
        {
            var seen = new HashSet<(int, int)>();
            long range = (long)Math.Ceiling(radius / _cell);
            var (kx, ky, kz) = Key(p);
            for (long dx = -range; dx <= range; dx++)
                for (long dy = -range; dy <= range; dy++)
                    for (long dz = -range; dz <= range; dz++)
                    {
                        if (!_cells.TryGetValue((kx + dx, ky + dy, kz + dz), out var list)) continue;
                        foreach (var s in list) if (seen.Add(s)) yield return s;
                    }
        }

        private (long, long, long) Key(Vec3d p) =>
            ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell), (long)Math.Floor(p.Z / _cell));
    }

    /// <summary>Uniform grid for radius queries over a fixed point set.</summary>
    private sealed class PointGrid
    {
        private readonly IReadOnlyList<Vec3d> _points;
        private readonly double _cell;
        private readonly Dictionary<(long, long, long), List<int>> _cells = new Dictionary<(long, long, long), List<int>>();

        public PointGrid(IReadOnlyList<Vec3d> points, double cell)
        {
            _points = points;
            _cell = Math.Max(cell, 1e-12);
            for (int i = 0; i < points.Count; i++)
            {
                var key = Key(points[i]);
                if (!_cells.TryGetValue(key, out var list)) { list = new List<int>(); _cells[key] = list; }
                list.Add(i);
            }
        }

        public IEnumerable<int> Within(Vec3d p, double radius)
        {
            long range = (long)Math.Ceiling(radius / _cell);
            double r2 = radius * radius;
            var (kx, ky, kz) = Key(p);
            for (long dx = -range; dx <= range; dx++)
                for (long dy = -range; dy <= range; dy++)
                    for (long dz = -range; dz <= range; dz++)
                    {
                        if (!_cells.TryGetValue((kx + dx, ky + dy, kz + dz), out var list)) continue;
                        foreach (int i in list)
                            if ((_points[i] - p).LengthSquared <= r2) yield return i;
                    }
        }

        private (long, long, long) Key(Vec3d p) =>
            ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell), (long)Math.Floor(p.Z / _cell));
    }

    private static double Component(Vec3d v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    /// <summary>
    /// Consistent 12x12 geometric stiffness of a beam element carrying the
    /// axial force N (tension positive), local coordinates, same DOF order as
    /// <see cref="LocalStiffness"/>: k_g = (N/L) · [...] with the cubic Hermite
    /// bending terms 6/5, ±L/10, 2L²/15, −L²/30 in both planes, the axial term
    /// 1, −1 and the torsional (Wagner) term I_p/A = (Iy + Iz)/A (McGuire,
    /// Gallagher &amp; Ziemian 2000 eq. 9.19; Przemieniecki 1968 ch. 15).
    /// Bending-moment coupling terms (lateral–torsional buckling of open
    /// sections) are not included.
    /// </summary>
    public static double[,] GeometricStiffness(double N, double L, double IpOverA)
    {
        var k = new double[12, 12];
        double f = N / L, L2 = L * L;
        void Set(int i, int j, double v) { k[i, j] = v; k[j, i] = v; }
        // Axial
        Set(0, 0, f); Set(0, 6, -f); Set(6, 6, f);
        // Torsion (Wagner)
        Set(3, 3, f * IpOverA); Set(3, 9, -f * IpOverA); Set(9, 9, f * IpOverA);
        // Bending about z (v, rz)
        Set(1, 1, f * 6 / 5); Set(1, 5, f * L / 10); Set(1, 7, -f * 6 / 5); Set(1, 11, f * L / 10);
        Set(5, 5, f * 2 * L2 / 15); Set(5, 7, -f * L / 10); Set(5, 11, -f * L2 / 30);
        Set(7, 7, f * 6 / 5); Set(7, 11, -f * L / 10);
        Set(11, 11, f * 2 * L2 / 15);
        // Bending about y (w, ry)
        Set(2, 2, f * 6 / 5); Set(2, 4, -f * L / 10); Set(2, 8, -f * 6 / 5); Set(2, 10, -f * L / 10);
        Set(4, 4, f * 2 * L2 / 15); Set(4, 8, f * L / 10); Set(4, 10, -f * L2 / 30);
        Set(8, 8, f * 6 / 5); Set(8, 10, f * L / 10);
        Set(10, 10, f * 2 * L2 / 15);
        return k;
    }

    /// <summary>
    /// Standard 12x12 Euler-Bernoulli beam stiffness in local coordinates.
    /// DOF order per node: u (along x), v (y), w (z), rx, ry, rz.
    /// </summary>
    private static double[,] LocalStiffness(double E, double G, double A, double Iy, double Iz, double J, double L)
    {
        var k = new double[12, 12];
        double L2 = L * L, L3 = L2 * L;

        void Set(int i, int j, double v) { k[i, j] = v; k[j, i] = v; }

        // Axial
        Set(0, 0, E * A / L); Set(0, 6, -E * A / L); Set(6, 6, E * A / L);
        // Torsion
        Set(3, 3, G * J / L); Set(3, 9, -G * J / L); Set(9, 9, G * J / L);
        // Bending about z (v, rz)
        Set(1, 1, 12 * E * Iz / L3); Set(1, 5, 6 * E * Iz / L2); Set(1, 7, -12 * E * Iz / L3); Set(1, 11, 6 * E * Iz / L2);
        Set(5, 5, 4 * E * Iz / L); Set(5, 7, -6 * E * Iz / L2); Set(5, 11, 2 * E * Iz / L);
        Set(7, 7, 12 * E * Iz / L3); Set(7, 11, -6 * E * Iz / L2);
        Set(11, 11, 4 * E * Iz / L);
        // Bending about y (w, ry)
        Set(2, 2, 12 * E * Iy / L3); Set(2, 4, -6 * E * Iy / L2); Set(2, 8, -12 * E * Iy / L3); Set(2, 10, -6 * E * Iy / L2);
        Set(4, 4, 4 * E * Iy / L); Set(4, 8, 6 * E * Iy / L2); Set(4, 10, 2 * E * Iy / L);
        Set(8, 8, 12 * E * Iy / L3); Set(8, 10, 6 * E * Iy / L2);
        Set(10, 10, 4 * E * Iy / L);

        return k;
    }
}
