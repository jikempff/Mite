using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Core.Analysis;

/// <summary>
/// Linear static analysis of a lath network as a 3D frame: each polyline
/// segment becomes a 12-DOF Euler-Bernoulli beam with the lath's rectangular
/// section, laths are coupled at net crossings (nodes merged), and support
/// points are fully fixed. A first-order sanity check for gridshells — not a
/// replacement for a full FE package, but it runs inside the definition and
/// reports per-lath axial/bending utilization against an allowable stress.
/// </summary>
public static class FrameAnalysis
{
    public class Options
    {
        /// <summary>Young's modulus (Pa). Default 11e9 ≈ timber along grain.</summary>
        public double E { get; set; } = 11e9;

        /// <summary>Shear modulus (Pa). 0 = automatic (isotropic: E / 2.6).</summary>
        public double G { get; set; } = 0.0;

        /// <summary>Allowable combined stress (Pa). Default 20e6 ≈ structural timber.</summary>
        public double AllowableStress { get; set; } = 20e6;

        /// <summary>
        /// Distance within which lath nodes snap to joint / support points
        /// (0 = automatic: 40% of the average polyline segment length).
        /// </summary>
        public double SnapTolerance { get; set; } = 0.0;
    }

    public readonly struct Result
    {
        /// <summary>Unique node positions after merging (original, undeformed).</summary>
        public readonly Vec3d[] Nodes;

        /// <summary>Per unique node: the (curve, pointIndex) locations merged into it.</summary>
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

        public Result(Vec3d[] nodes, List<(int, int)>[] nodeMap, Vec3d[] displacements,
            double maxDisplacement, (int, int)[] elementSource,
            double[] axial, double[] bendY, double[] bendZ, double[] utilization, double maxUtilization)
        {
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
        }
    }

    /// <summary>
    /// Solves the network. laths are centerline polylines (e.g. sampled net
    /// curves); joints are crossing points where laths are coupled (e.g. the
    /// Points output of Net Joints); supports are fully fixed points; load is
    /// a force-per-unit-length vector applied along every element.
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

        // ---- Nodes and connectivity --------------------------------------
        // Raw points: node index = running counter, with (curve, index) map
        var rawPoints = new List<Vec3d>();
        var rawMap = new List<(int Curve, int Index)>();
        for (int c = 0; c < laths.Count; c++)
            for (int i = 0; i < laths[c].Length; i++)
            {
                rawPoints.Add(laths[c][i]);
                rawMap.Add((c, i));
            }
        if (rawPoints.Count < 2)
            throw new ArgumentException("At least one lath with two points is required.", nameof(laths));

        double avgSeg = 0;
        int segCount = 0;
        for (int c = 0; c < laths.Count; c++)
            for (int i = 1; i < laths[c].Length; i++)
            {
                avgSeg += (laths[c][i] - laths[c][i - 1]).Length;
                segCount++;
            }
        avgSeg = segCount > 0 ? avgSeg / segCount : 1.0;
        double snap = options.SnapTolerance > 0 ? options.SnapTolerance : 0.4 * avgSeg;

        // Union-find merge: nodes within snap of the same joint point are one node
        var parent = new int[rawPoints.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }

        if (joints != null)
        {
            foreach (var jp in joints)
            {
                int first = -1;
                double snap2 = snap * snap;
                for (int i = 0; i < rawPoints.Count; i++)
                {
                    if ((rawPoints[i] - jp).LengthSquared <= snap2)
                    {
                        if (first < 0) first = i;
                        else Union(first, i);
                    }
                }
            }
        }

        // Unique nodes
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
                nodes.Add(rawPoints[i]);
                nodeMap.Add(new List<(int, int)>());
            }
            rawToNode[i] = ni;
            nodeMap[ni].Add(rawMap[i]);
        }

        // Elements: consecutive raw points of each lath (skip zero-length after merging)
        var elemA = new List<int>();
        var elemB = new List<int>();
        var elemSrc = new List<(int Curve, int Index)>();
        int rawCursor = 0;
        for (int c = 0; c < laths.Count; c++)
        {
            for (int i = 1; i < laths[c].Length; i++)
            {
                int a = rawToNode[rawCursor + i - 1];
                int b = rawToNode[rawCursor + i];
                if (a != b)
                {
                    elemA.Add(a);
                    elemB.Add(b);
                    elemSrc.Add((c, i - 1));
                }
            }
            rawCursor += laths[c].Length;
        }
        int ne = elemA.Count;
        if (ne == 0)
            throw new ArgumentException("The laths contain no usable segments.", nameof(laths));

        // Supports
        var fixedNodes = new bool[nodes.Count];
        if (supports != null)
        {
            double snap2 = snap * snap;
            foreach (var sp in supports)
                for (int i = 0; i < nodes.Count; i++)
                    if ((nodes[i] - sp).LengthSquared <= snap2)
                        fixedNodes[i] = true;
        }

        // ---- Section properties ------------------------------------------
        double A = profile.Width * profile.Thickness;
        double Iy = profile.Width * Math.Pow(profile.Thickness, 3) / 12.0; // bending through thickness
        double Iz = profile.Thickness * Math.Pow(profile.Width, 3) / 12.0; // bending across the width
        double J = profile.Width * Math.Pow(profile.Thickness, 3) / 3.0;   // thin-rectangle torsion
        double Wy = Iy / (0.5 * profile.Thickness);
        double Wz = Iz / (0.5 * profile.Width);
        double E = options.E;
        double G = options.G > 0 ? options.G : options.E / 2.6;

        // ---- Assemble -----------------------------------------------------
        var proj = new MeshProjection(mesh);
        int ndof = nodes.Count * 6;
        var K = new Dictionary<long, double>();
        var F = new double[ndof];

        var axial = new double[ne];
        var bendY = new double[ne];
        var bendZ = new double[ne];
        var elemLen = new double[ne];
        var elemRot = new Vec3d[ne][]; // local axes [ex, ey, ez] per element

        for (int e = 0; e < ne; e++)
        {
            Vec3d p1 = nodes[elemA[e]], p2 = nodes[elemB[e]];
            Vec3d d = p2 - p1;
            double L = d.Length;
            if (L < 1e-12) continue;
            elemLen[e] = L;

            Vec3d ex = d / L;
            Vec3d nrm = proj.ClosestPoint(0.5 * (p1 + p2), proj.NearestVertexGlobal(0.5 * (p1 + p2))).SmoothNormal;
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
            elemRot[e] = new[] { ex, ey, ez };

            var ke = LocalStiffness(E, G, A, Iy, Iz, J, L);

            // Rotation: u_local = Q u_global; K_global += Q^T ke Q
            for (int bi = 0; bi < 2; bi++)
            {
                int nodeI = bi == 0 ? elemA[e] : elemB[e];
                for (int bj = 0; bj < 2; bj++)
                {
                    int nodeJ = bj == 0 ? elemA[e] : elemB[e];
                    for (int r = 0; r < 6; r++)
                    {
                        for (int c = 0; c < 6; c++)
                        {
                            // Transform 6x6 block: Kg = R^T kl R with R = diag(Q, Q)
                            double sum = 0;
                            for (int a = 0; a < 3; a++)
                                for (int b = 0; b < 3; b++)
                                {
                                    var rot = elemRot[e];
                                    double q_a_r = Component(rot[a], r % 3);
                                    double q_b_c = Component(rot[b], c % 3);
                                    sum += q_a_r * ke[bi * 6 + a + (r / 3) * 3, bj * 6 + b + (c / 3) * 3] * q_b_c;
                                }
                            if (sum != 0.0)
                            {
                                long key = (long)(nodeI * 6 + r) * ndof + (nodeJ * 6 + c);
                                K.TryGetValue(key, out double cur);
                                K[key] = cur + sum;
                            }
                        }
                    }
                }
            }

            // Distributed load -> lumped nodal forces
            Vec3d f = 0.5 * L * loadPerUnitLength;
            for (int bi = 0; bi < 2; bi++)
            {
                int node = bi == 0 ? elemA[e] : elemB[e];
                F[node * 6 + 0] += f.X;
                F[node * 6 + 1] += f.Y;
                F[node * 6 + 2] += f.Z;
            }
        }

        // ---- Solve ---------------------------------------------------------
        var freeDofs = new List<int>();
        var dofMap = new int[ndof];
        for (int i = 0; i < nodes.Count; i++)
            for (int d = 0; d < 6; d++)
            {
                if (fixedNodes[i]) { dofMap[i * 6 + d] = -1; }
                else { dofMap[i * 6 + d] = freeDofs.Count; freeDofs.Add(i * 6 + d); }
            }

        if (freeDofs.Count == 0)
            throw new ArgumentException("Every node is fixed; nothing to solve.", nameof(supports));

        int nfree = freeDofs.Count;
        var Kff = Matrix<double>.Build.Sparse(nfree, nfree);
        foreach (var kv in K)
        {
            int r = (int)(kv.Key / ndof);
            int c = (int)(kv.Key % ndof);
            int fr = dofMap[r], fc = dofMap[c];
            if (fr >= 0 && fc >= 0) Kff[fr, fc] = Kff[fr, fc] + kv.Value;
        }
        var Ff = Vector<double>.Build.Dense(nfree);
        for (int i = 0; i < nfree; i++) Ff[i] = F[freeDofs[i]];

        Vector<double> u;
        try
        {
            u = Kff.Solve(Ff);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The frame system could not be solved. Check that the network is " +
                "supported against rigid-body motion (supports at enough joints).", ex);
        }

        double res = (Kff * u - Ff).InfinityNorm();
        double fScale = Math.Max(1.0, Ff.InfinityNorm());
        if (double.IsNaN(res) || res > 1e-6 * fScale)
            throw new InvalidOperationException(
                "The frame solve is inconsistent — the structure is likely a mechanism " +
                "(unsupported or under-connected). Add supports or check the joint coupling.");

        var displacements = new Vec3d[nodes.Count];
        double maxDisp = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var disp = Vec3d.Zero;
            int d0 = dofMap[i * 6 + 0];
            if (d0 >= 0)
                disp = new Vec3d(u[d0], u[dofMap[i * 6 + 1]], u[dofMap[i * 6 + 2]]);
            displacements[i] = disp;
            maxDisp = Math.Max(maxDisp, disp.Length);
        }

        // ---- Element forces and utilization --------------------------------
        var utilization = new double[ne];
        double maxUtil = 0;
        for (int e = 0; e < ne; e++)
        {
            if (elemRot[e] == null) continue;
            double L = elemLen[e];

            var ug = new double[12];
            for (int bi = 0; bi < 2; bi++)
            {
                int node = bi == 0 ? elemA[e] : elemB[e];
                for (int d = 0; d < 6; d++)
                {
                    int fd = dofMap[node * 6 + d];
                    ug[bi * 6 + d] = fd >= 0 ? u[fd] : 0.0;
                }
            }

            // u_local = R u_global per node (6 dofs: 3 transl + 3 rot)
            var rot = elemRot[e];
            var ul = new double[12];
            for (int bi = 0; bi < 2; bi++)
                for (int half = 0; half < 2; half++) // 0: translations, 1: rotations
                    for (int a = 0; a < 3; a++)
                    {
                        double sum = 0;
                        for (int b = 0; b < 3; b++)
                            sum += Component(rot[a], b) * ug[bi * 6 + half * 3 + b];
                        ul[bi * 6 + half * 3 + a] = sum;
                    }

            var ke = LocalStiffness(E, G, A, Iy, Iz, J, L);
            var fl = new double[12];
            for (int r = 0; r < 12; r++)
                for (int c = 0; c < 12; c++)
                    fl[r] += ke[r, c] * ul[c];

            axial[e] = -fl[0]; // tension positive at end 1
            bendY[e] = Math.Max(Math.Abs(fl[4]), Math.Abs(fl[10]));
            bendZ[e] = Math.Max(Math.Abs(fl[5]), Math.Abs(fl[11]));

            double sigma = Math.Abs(axial[e]) / A + bendY[e] / Wy + bendZ[e] / Wz;
            utilization[e] = sigma / options.AllowableStress;
            if (utilization[e] > maxUtil) maxUtil = utilization[e];
        }

        return new Result(nodes.ToArray(), nodeMap.ToArray(), displacements, maxDisp,
            elemSrc.ToArray(), axial, bendY, bendZ, utilization, maxUtil);
    }

    private static double Component(Vec3d v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

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
