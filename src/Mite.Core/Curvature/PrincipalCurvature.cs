using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Curvature;

public static class PrincipalCurvature
{
    public readonly struct Result
    {
        public readonly double[] K1;
        public readonly double[] K2;
        public readonly Vec3d[] D1;
        public readonly Vec3d[] D2;

        /// <summary>Vertex normals used for the tangent frames; (D1, D2, N) is right-handed.</summary>
        public readonly Vec3d[] Normals;

        public Result(double[] k1, double[] k2, Vec3d[] d1, Vec3d[] d2, Vec3d[]? normals = null)
        {
            K1 = k1; K2 = k2; D1 = d1; D2 = d2;
            if (normals == null)
            {
                normals = new Vec3d[k1.Length];
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = Vec3d.Cross(d1[i], d2[i]).Normalized();
            }
            Normals = normals;
        }
    }

    /// <summary>
    /// Computes principal curvatures and directions via per-face shape operator averaging.
    /// Based on "Estimating Curvatures and Their Derivatives on Triangle Meshes" (Rusinkiewicz 2004):
    /// the second fundamental form is least-squares fitted per face from finite normal
    /// differences along the three edges, then rotated into each vertex's tangent frame
    /// and averaged with corner-angle weights.
    /// </summary>
    public static Result Compute(MeshData mesh, int radius = 2)
    {
        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;
        int nf = triMesh.FaceCount;

        // Max-weighted normals are exact on spheres, which keeps the normal
        // finite differences (and hence curvature magnitudes) unbiased
        var vertexNormals = triMesh.ComputeVertexNormalsMax();

        var e1Basis = new Vec3d[nv];
        var e2Basis = new Vec3d[nv];
        for (int i = 0; i < nv; i++)
            ComputeTangentBasis(vertexNormals[i], out e1Basis[i], out e2Basis[i]);

        // Accumulated second fundamental form per vertex, in the vertex frame
        var accA = new double[nv];
        var accB = new double[nv];
        var accC = new double[nv];
        var accW = new double[nv];

        for (int fi = 0; fi < nf; fi++)
        {
            if (!FaceTensor(triMesh, fi, vertexNormals, out Vec3d t, out Vec3d b, out Vec3d faceNormal, out double fa, out double fm, out double fc, out Vec3d[] edges)) continue;
            var f = triMesh.Faces[fi];
            AccumulateFace(f, edges, t, b, faceNormal, fa, fm, fc, e1Basis, e2Basis, vertexNormals, accA, accB, accC, accW, null);
        }

        // Border repair: the finite-difference tensors are one-sided at the
        // mesh border (biased vertex normals, half a 1-ring), which on a
        // catenoid puts the asymptotic directions 8° off on the border row and
        // 4° on the next — the traced curves then kink in the last rows and
        // every curve that starts at the border inherits the offset. Vertices
        // within two rings of the border are refitted with an osculating jet.
        RepairBorder(triMesh, vertexNormals, e1Basis, e2Basis, accA, accB, accC, accW);

        var k1 = new double[nv];
        var k2 = new double[nv];
        var d1 = new Vec3d[nv];
        var d2 = new Vec3d[nv];

        for (int i = 0; i < nv; i++)
        {
            double a = 0, b = 0, c = 0;
            if (accW[i] > 1e-15)
            {
                a = accA[i] / accW[i];
                b = accB[i] / accW[i];
                c = accC[i] / accW[i];
            }

            DiagonalizeShapeOperator(a, b, c, out double kappa1, out double kappa2, out double theta);

            k1[i] = kappa1;
            k2[i] = kappa2;
            d1[i] = (Math.Cos(theta) * e1Basis[i] + Math.Sin(theta) * e2Basis[i]).Normalized();
            d2[i] = (-Math.Sin(theta) * e1Basis[i] + Math.Cos(theta) * e2Basis[i]).Normalized();

            if (k1[i] < k2[i])
            {
                (k1[i], k2[i]) = (k2[i], k1[i]);
                (d1[i], d2[i]) = (d2[i], d1[i]);
            }
        }

        if (radius > 1)
            SmoothCurvature(triMesh, radius - 1, k1, k2, d1, d2, vertexNormals, e1Basis, e2Basis);

        // Enforce a right-handed (D1, D2, N) frame at every vertex. The sign of
        // each principal direction is arbitrary, but consumers that combine D1
        // and D2 into derived fields (asymptotic directions c*D1 +/- s*D2) need
        // a consistent handedness or the two derived families swap from vertex
        // to vertex, which fragments traced curves.
        for (int i = 0; i < nv; i++)
        {
            Vec3d n = vertexNormals[i];
            Vec3d cross = Vec3d.Cross(n, d1[i]);
            if (cross.LengthSquared > 1e-20)
                d2[i] = cross.Normalized();
        }

        return new Result(k1, k2, d1, d2, vertexNormals);
    }

    /// <summary>
    /// Least-squares fit of the second fundamental form of one face from the
    /// finite normal differences along its three edges (Rusinkiewicz 2004):
    /// II · e ≈ Δn for each edge, 6 equations for the 3 unknowns of the
    /// symmetric tensor in the face frame (t, b).
    /// </summary>
    private static bool FaceTensor(MeshData triMesh, int fi, Vec3d[] vertexNormals,
        out Vec3d t, out Vec3d b, out Vec3d faceNormal, out double fa, out double fm, out double fc, out Vec3d[] edges)
    {
        var f = triMesh.Faces[fi];
        Vec3d p0 = triMesh.Vertices[f[0]];
        Vec3d p1 = triMesh.Vertices[f[1]];
        Vec3d p2 = triMesh.Vertices[f[2]];
        // Edge j is opposite vertex j
        edges = new[] { p2 - p1, p0 - p2, p1 - p0 };
        t = default; b = default; fa = fm = fc = 0;
        faceNormal = Vec3d.Cross(edges[2], -edges[1]);
        if (faceNormal.LengthSquared < 1e-30) return false;
        faceNormal = faceNormal.Normalized();
        t = edges[2].Normalized();
        b = Vec3d.Cross(faceNormal, t).Normalized();

        double m00 = 0, m01 = 0, m11 = 0, m12 = 0, m22 = 0;
        double r0 = 0, r1 = 0, r2 = 0;
        for (int j = 0; j < 3; j++)
        {
            Vec3d dn = vertexNormals[f[(j + 2) % 3]] - vertexNormals[f[(j + 1) % 3]];
            double u = Vec3d.Dot(edges[j], t);
            double v = Vec3d.Dot(edges[j], b);
            double dnU = Vec3d.Dot(dn, t);
            double dnV = Vec3d.Dot(dn, b);
            m00 += u * u; m01 += u * v; r0 += u * dnU;
            m11 += u * u; m12 += u * v; r2 += v * dnV;
            m11 += v * v; m22 += v * v; r1 += v * dnU + u * dnV;
        }
        Solve3x3Symmetric(m00, m01, 0, m11, m12, m22, r0, r1, r2, out fa, out fm, out fc);
        return true;
    }

    /// <summary>Distributes a face tensor to its corners (rotated into each vertex frame, corner-angle weighted); onlyVertex restricts it to one corner.</summary>
    private static void AccumulateFace(int[] f, Vec3d[] edges, Vec3d t, Vec3d b, Vec3d faceNormal, double fa, double fm, double fc,
        Vec3d[] e1Basis, Vec3d[] e2Basis, Vec3d[] vertexNormals, double[] accA, double[] accB, double[] accC, double[] accW, int? onlyVertex)
    {
        for (int j = 0; j < 3; j++)
        {
            int vi = f[j];
            if (onlyVertex.HasValue && vi != onlyVertex.Value) continue;
            Vec3d cornerE1 = -edges[(j + 1) % 3];
            Vec3d cornerE2 = edges[(j + 2) % 3];
            double w = CornerAngle(cornerE1, cornerE2);
            if (w < 1e-12) continue;
            ProjectCurvatureTensor(t, b, faceNormal, fa, fm, fc, e1Basis[vi], e2Basis[vi], vertexNormals[vi],
                out double pa, out double pm, out double pc);
            accA[vi] += w * pa; accB[vi] += w * pm; accC[vi] += w * pc; accW[vi] += w;
        }
    }

    /// <summary>
    /// Replaces the tensor, normal and tangent frame of every vertex within
    /// two rings of a border by an osculating-jet fit (Cazals &amp; Pouget 2005,
    /// "Estimating differential quantities using polynomial fitting of
    /// osculating jets", CAGD 22): a degree-2 Monge patch z = ½(A x² + 2B xy +
    /// C y²) + D x + E y + F least-squares fitted to the vertex's 2- or 3-ring
    /// in a frame that is re-aligned with the fitted normal and refitted, so
    /// the residual slope D, E is negligible and (A, B, C) is the second
    /// fundamental form in an orthonormal tangent frame. The fit is unbiased on
    /// a half-disc neighbourhood, which the normal-difference scheme is not.
    /// </summary>
    private static void RepairBorder(MeshData mesh, Vec3d[] normals, Vec3d[] e1, Vec3d[] e2,
        double[] accA, double[] accB, double[] accC, double[] accW)
    {
        var onBorder = mesh.BuildBoundaryVertexFlags();
        bool any = false;
        foreach (bool b in onBorder) if (b) { any = true; break; }
        if (!any) return;

        var nbrs = mesh.BuildVertexNeighbors();
        int nv = mesh.VertexCount;
        // graph distance to the border, up to 2
        var dist = new int[nv];
        for (int i = 0; i < nv; i++) dist[i] = onBorder[i] ? 0 : int.MaxValue;
        for (int ring = 1; ring <= 2; ring++)
            for (int i = 0; i < nv; i++)
                if (dist[i] == int.MaxValue)
                    foreach (int j in nbrs[i]) if (dist[j] == ring - 1) { dist[i] = ring; break; }

        // Rings 1 and 2: jet fits (their neighbourhoods reach two rings into
        // the interior, enough for a well-conditioned fit)
        var fitted = new bool[nv];
        for (int i = 0; i < nv; i++)
        {
            if (dist[i] != 1 && dist[i] != 2) continue;
            if (FitJet(mesh, nbrs, i, normals[i], out Vec3d n, out Vec3d t1, out Vec3d t2, out double a, out double b, out double c))
            {
                normals[i] = n; e1[i] = t1; e2[i] = t2;
                accA[i] = a; accB[i] = b; accC[i] = c; accW[i] = 1.0;
                fitted[i] = true;
            }
        }

        // The border row itself: a polynomial fitted to a strictly one-sided
        // neighbourhood is ill-conditioned in the inward direction (a quartic
        // put k2 = −0.37 on a cylinder), so the border row is extrapolated
        // instead: each component of the world-frame shape operator S =
        // k1 d1d1ᵀ + k2 d2d2ᵀ (and of the normal) of the fitted vertices in
        // the 2-ring is fitted with a plane over the tangent coordinates and
        // read off at the border vertex, then projected into the tangent plane
        // of the extrapolated normal. Linear extrapolation over one ring is
        // second-order accurate, the same order as the interior estimate.
        var world = new Matrix3d[nv];
        for (int i = 0; i < nv; i++)
        {
            if (!fitted[i]) continue;
            double w = accW[i] > 1e-15 ? accW[i] : 1.0;
            DiagonalizeShapeOperator(accA[i] / w, accB[i] / w, accC[i] / w, out double ka, out double kb, out double th);
            Vec3d da = Math.Cos(th) * e1[i] + Math.Sin(th) * e2[i];
            Vec3d db = -Math.Sin(th) * e1[i] + Math.Cos(th) * e2[i];
            world[i] = ka * Matrix3d.OuterProduct(da, da) + kb * Matrix3d.OuterProduct(db, db);
        }
        for (int i = 0; i < nv; i++)
        {
            if (dist[i] != 0) continue;
            // fitted vertices of the 2-ring, with local tangent coordinates in
            // the frame of their mean normal
            var ring = KRing(nbrs, i, 2);
            var pts = new List<int>();
            Vec3d nMean = Vec3d.Zero;
            foreach (int j in ring) if (fitted[j]) { pts.Add(j); nMean = nMean + normals[j]; }
            if (pts.Count == 0) continue;
            nMean = nMean.Normalized();
            ComputeTangentBasis(nMean, out Vec3d u, out Vec3d v);
            Vec3d p = mesh.Vertices[i];
            // least-squares plane f(x, y) = f0 + fx x + fy y per component; f0 is
            // the value extrapolated to the border vertex (x = y = 0)
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, s0 = pts.Count;
            var comps = new double[pts.Count, 9]; // 6 tensor + 3 normal components
            for (int q = 0; q < pts.Count; q++)
            {
                int j = pts[q];
                Vec3d d = mesh.Vertices[j] - p;
                double x = Vec3d.Dot(d, u), y = Vec3d.Dot(d, v);
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y;
                var W = world[j];
                comps[q, 0] = W[0, 0]; comps[q, 1] = W[0, 1]; comps[q, 2] = W[0, 2]; comps[q, 3] = W[1, 1]; comps[q, 4] = W[1, 2]; comps[q, 5] = W[2, 2];
                comps[q, 6] = normals[j].X; comps[q, 7] = normals[j].Y; comps[q, 8] = normals[j].Z;
            }
            var f0 = new double[9];
            bool planar = pts.Count >= 4;
            if (planar)
            {
                var M = new double[3, 3] { { s0, sx, sy }, { sx, sxx, sxy }, { sy, sxy, syy } };
                for (int comp = 0; comp < 9; comp++)
                {
                    var r = new double[3];
                    for (int q = 0; q < pts.Count; q++)
                    {
                        Vec3d d = mesh.Vertices[pts[q]] - p;
                        double x = Vec3d.Dot(d, u), y = Vec3d.Dot(d, v), val = comps[q, comp];
                        r[0] += val; r[1] += val * x; r[2] += val * y;
                    }
                    if (!SolveDense(M, r, 3, out double[] sol)) { planar = false; break; }
                    f0[comp] = sol[0];
                }
            }
            if (!planar)
                for (int comp = 0; comp < 9; comp++) { double m = 0; for (int q = 0; q < pts.Count; q++) m += comps[q, comp]; f0[comp] = m / pts.Count; }

            var S = new Matrix3d(f0[0], f0[1], f0[2], f0[1], f0[3], f0[4], f0[2], f0[4], f0[5]);
            Vec3d n = new Vec3d(f0[6], f0[7], f0[8]);
            if (n.LengthSquared < 1e-20) continue;
            n = n.Normalized();
            ComputeTangentBasis(n, out Vec3d t1, out Vec3d t2);
            Vec3d st1 = S * t1, st2 = S * t2;
            normals[i] = n; e1[i] = t1; e2[i] = t2;
            accA[i] = Vec3d.Dot(t1, st1); accB[i] = Vec3d.Dot(t1, st2); accC[i] = Vec3d.Dot(t2, st2); accW[i] = 1.0;
        }

    }

    /// <summary>
    /// Osculating-jet fit at one vertex over its k-ring (k = 2, or 3 when the
    /// 2-ring is too small). Returns the fitted unit normal, an orthonormal
    /// tangent frame and the second fundamental form (a = II(t1,t1), b =
    /// II(t1,t2), c = II(t2,t2)) with the sign convention of the finite-
    /// difference scheme (positive where the surface bends towards −n, i.e.
    /// a sphere with outward normals has positive curvature).
    /// </summary>
    internal static bool FitJet(MeshData mesh, int[][] nbrs, int vertex, Vec3d normalGuess,
        out Vec3d normal, out Vec3d t1, out Vec3d t2, out double a, out double b, out double c)
    {
        normal = normalGuess; t1 = default; t2 = default; a = b = c = 0;
        // Degree-4 jet: the higher terms absorb the truncation of a plain
        // quadric so the second-order coefficients converge at O(h³)
        // (Cazals & Pouget 2005, Thm. 2) — a cubic still leaked ~1e-3 into
        // the zero curvature of a cylinder on a one-sided neighbourhood.
        const int NC = 15;
        var ring = KRing(nbrs, vertex, 3);
        if (ring.Count < 20) ring = KRing(nbrs, vertex, 4);
        if (ring.Count < 18) return false;
        // Gaussian weights centred on the vertex: the far points carry the
        // truncation error of the polynomial, the near ones the curvature
        double h = 0; foreach (int j in nbrs[vertex]) h += (mesh.Vertices[j] - mesh.Vertices[vertex]).Length;
        h = nbrs[vertex].Length > 0 ? h / nbrs[vertex].Length : 1.0;
        double sigma2 = 2.0 * (1.5 * h) * (1.5 * h);

        Vec3d p = mesh.Vertices[vertex];
        Vec3d n = normalGuess.LengthSquared > 1e-20 ? normalGuess.Normalized() : new Vec3d(0, 0, 1);
        double A = 0, B = 0, C = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            ComputeTangentBasis(n, out t1, out t2);
            // normal equations of z ≈ ½A x² + B xy + ½C y² + D x + E y + F
            var M = new double[NC, NC];
            var r = new double[NC];
            var row = new double[NC];
            foreach (int j in ring)
            {
                Vec3d q = mesh.Vertices[j] - p;
                double x = Vec3d.Dot(q, t1), y = Vec3d.Dot(q, t2), z = Vec3d.Dot(q, n);
                row[0] = 0.5 * x * x; row[1] = x * y; row[2] = 0.5 * y * y; row[3] = x; row[4] = y; row[5] = 1;
                row[6] = x * x * x; row[7] = x * x * y; row[8] = x * y * y; row[9] = y * y * y;
                row[10] = x * x * x * x; row[11] = x * x * x * y; row[12] = x * x * y * y; row[13] = x * y * y * y; row[14] = y * y * y * y;
                double wgt = Math.Exp(-(x * x + y * y) / sigma2);
                for (int u = 0; u < NC; u++) { r[u] += wgt * row[u] * z; for (int v = 0; v < NC; v++) M[u, v] += wgt * row[u] * row[v]; }
            }
            if (!SolveDense(M, r, NC, out double[] x6)) return false;
            A = x6[0]; B = x6[1]; C = x6[2];
            double D = x6[3], E = x6[4];
            // re-align the frame with the fitted normal and refit
            Vec3d nFit = (-D * t1 - E * t2 + n).Normalized();
            double tilt = 1.0 - Vec3d.Dot(nFit, n);
            n = nFit;
            if (tilt < 1e-12) break;
        }
        ComputeTangentBasis(n, out t1, out t2);
        normal = n;
        // In the Monge frame the surface z = ½(A x² + …) bends towards +n where
        // A > 0; the finite-difference scheme counts that as negative curvature
        // (a sphere with outward normals: the surface bends away from n → positive).
        a = -A; b = -B; c = -C;
        return true;
    }

    private static List<int> KRing(int[][] nbrs, int vertex, int k)
    {
        var seen = new HashSet<int> { vertex };
        var frontier = new List<int> { vertex };
        var ring = new List<int> { vertex };
        for (int d = 0; d < k; d++)
        {
            var next = new List<int>();
            foreach (int v in frontier)
                foreach (int w in nbrs[v])
                    if (seen.Add(w)) { next.Add(w); ring.Add(w); }
            frontier = next;
        }
        return ring;
    }

    /// <summary>Gaussian elimination with partial pivoting for a small dense system.</summary>
    private static bool SolveDense(double[,] M, double[] r, int n, out double[] x)
    {
        x = new double[n];
        var a = (double[,])M.Clone();
        var b = (double[])r.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int i = col + 1; i < n; i++) if (Math.Abs(a[i, col]) > Math.Abs(a[piv, col])) piv = i;
            if (Math.Abs(a[piv, col]) < 1e-300) return false;
            if (piv != col)
            {
                for (int k = 0; k < n; k++) { double t = a[col, k]; a[col, k] = a[piv, k]; a[piv, k] = t; }
                double tb = b[col]; b[col] = b[piv]; b[piv] = tb;
            }
            for (int i = col + 1; i < n; i++)
            {
                double f = a[i, col] / a[col, col];
                if (f == 0) continue;
                for (int k = col; k < n; k++) a[i, k] -= f * a[col, k];
                b[i] -= f * b[col];
            }
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = b[i];
            for (int k = i + 1; k < n; k++) sum -= a[i, k] * x[k];
            if (Math.Abs(a[i, i]) < 1e-300) return false;
            x[i] = sum / a[i, i];
        }
        return true;
    }

    /// <summary>
    /// Solves the symmetric 3x3 system M x = r via Cramer's rule, where
    /// M = [[m00,m01,m02],[m01,m11,m12],[m02,m12,m22]]. Returns zeros when singular
    /// (isolated vertices or degenerate 1-rings).
    /// </summary>
    private static void Solve3x3Symmetric(
        double m00, double m01, double m02, double m11, double m12, double m22,
        double r0, double r1, double r2,
        out double a, out double b, out double c)
    {
        double det =
            m00 * (m11 * m22 - m12 * m12)
            - m01 * (m01 * m22 - m12 * m02)
            + m02 * (m01 * m12 - m11 * m02);

        double scale = Math.Max(Math.Max(Math.Abs(m00), Math.Abs(m11)), Math.Abs(m22));
        if (Math.Abs(det) < 1e-12 * Math.Max(scale * scale * scale, 1e-30))
        {
            a = 0; b = 0; c = 0;
            return;
        }

        a = (r0 * (m11 * m22 - m12 * m12)
           - m01 * (r1 * m22 - m12 * r2)
           + m02 * (r1 * m12 - m11 * r2)) / det;

        b = (m00 * (r1 * m22 - r2 * m12)
           - r0 * (m01 * m22 - m12 * m02)
           + m02 * (m01 * r2 - r1 * m02)) / det;

        c = (m00 * (m11 * r2 - m12 * r1)
           - m01 * (m01 * r2 - r1 * m02)
           + r0 * (m01 * m12 - m11 * m02)) / det;
    }

    private static double CornerAngle(Vec3d e1, Vec3d e2)
    {
        double l1 = e1.Length, l2 = e2.Length;
        if (l1 < 1e-15 || l2 < 1e-15) return 0;
        double d = Vec3d.Dot(e1, e2) / (l1 * l2);
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d)));
    }

    /// <summary>
    /// Re-expresses a curvature tensor given in the (oldU, oldV) tangent frame
    /// (with normal oldN) in the (newU, newV) frame with normal newN. The new
    /// frame is first rotated so its normal coincides with the old one
    /// (Rusinkiewicz 2004, "proj_curv").
    /// </summary>
    private static void ProjectCurvatureTensor(
        Vec3d oldU, Vec3d oldV, Vec3d oldN,
        double oldA, double oldB, double oldC,
        Vec3d newU, Vec3d newV, Vec3d newN,
        out double a, out double b, out double c)
    {
        RotateCoordSys(newU, newV, oldN, out Vec3d rU, out Vec3d rV);

        double u1 = Vec3d.Dot(rU, oldU);
        double v1 = Vec3d.Dot(rU, oldV);
        double u2 = Vec3d.Dot(rV, oldU);
        double v2 = Vec3d.Dot(rV, oldV);

        a = oldA * u1 * u1 + oldB * (2.0 * u1 * v1) + oldC * v1 * v1;
        b = oldA * u1 * u2 + oldB * (u1 * v2 + u2 * v1) + oldC * v1 * v2;
        c = oldA * u2 * u2 + oldB * (2.0 * u2 * v2) + oldC * v2 * v2;
    }

    /// <summary>
    /// Rotates the coordinate system (u, v) about the axis perpendicular to its
    /// normal and the target normal, so the frame becomes perpendicular to newNorm.
    /// </summary>
    private static void RotateCoordSys(Vec3d u, Vec3d v, Vec3d newNorm, out Vec3d rU, out Vec3d rV)
    {
        rU = u;
        rV = v;
        Vec3d oldNorm = Vec3d.Cross(u, v);
        double ndot = Vec3d.Dot(oldNorm, newNorm);
        if (ndot <= -1.0)
        {
            rU = -rU;
            rV = -rV;
            return;
        }

        Vec3d perpOld = newNorm - ndot * oldNorm;
        Vec3d dperp = (1.0 / (1.0 + ndot)) * (oldNorm + newNorm);
        rU = rU - Vec3d.Dot(rU, perpOld) * dperp;
        rV = rV - Vec3d.Dot(rV, perpOld) * dperp;
    }

    private static void ComputeTangentBasis(Vec3d normal, out Vec3d e1, out Vec3d e2)
    {
        Vec3d up = Math.Abs(normal.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
        e1 = Vec3d.Cross(normal, up).Normalized();
        e2 = Vec3d.Cross(normal, e1).Normalized();
    }

    private static void DiagonalizeShapeOperator(double a, double b, double c, out double k1, out double k2, out double theta)
    {
        double diff = a - c;
        if (Math.Abs(b) < 1e-15 && Math.Abs(diff) < 1e-15)
        {
            k1 = a;
            k2 = c;
            theta = 0;
            return;
        }

        theta = 0.5 * Math.Atan2(2.0 * b, diff);
        double cos2 = Math.Cos(theta) * Math.Cos(theta);
        double sin2 = Math.Sin(theta) * Math.Sin(theta);
        double sincos = Math.Sin(theta) * Math.Cos(theta);

        k1 = a * cos2 + 2.0 * b * sincos + c * sin2;
        k2 = a * sin2 - 2.0 * b * sincos + c * cos2;
    }

    /// <summary>
    /// Smooths the curvature field by averaging the shape operator as a
    /// world-frame 3x3 tensor S = k1 d1d1^T + k2 d2d2^T over each 1-ring, then
    /// re-extracting values and directions from the average. Scalar smoothing
    /// of k1/k2 alone leaves the directions noisy, which corrupts streamline
    /// and asymptotic tracing on coarse meshes; tensor averaging keeps values
    /// and directions consistent (and handles the arbitrary per-vertex sign of
    /// the direction fields, which plain vector averaging cannot).
    /// </summary>
    private static void SmoothCurvature(
        MeshData mesh, int iterations,
        double[] k1, double[] k2,
        Vec3d[] d1, Vec3d[] d2,
        Vec3d[] normals, Vec3d[] e1Basis, Vec3d[] e2Basis)
    {
        var neighbors = mesh.BuildVertexNeighbors();
        int nv = mesh.VertexCount;

        var tensors = new Matrix3d[nv];
        for (int i = 0; i < nv; i++)
            tensors[i] = k1[i] * Matrix3d.OuterProduct(d1[i], d1[i]) +
                         k2[i] * Matrix3d.OuterProduct(d2[i], d2[i]);

        for (int iter = 0; iter < iterations; iter++)
        {
            var smoothed = new Matrix3d[nv];
            for (int i = 0; i < nv; i++)
            {
                Matrix3d sum = tensors[i];
                int count = 1;
                foreach (int j in neighbors[i])
                {
                    sum = sum + tensors[j];
                    count++;
                }
                smoothed[i] = (1.0 / count) * sum;
            }
            tensors = smoothed;
        }

        for (int i = 0; i < nv; i++)
        {
            // Project the averaged world tensor into this vertex's tangent frame.
            // Neighbouring tensors live in slightly different tangent planes, so
            // the 3x3 average has a small normal component; restricting to the
            // frame keeps the extracted directions exactly tangent and avoids
            // the ambiguity of a 3D eigensolve when k2 ~ 0 (near-developable
            // regions), where the "which eigenvector is the normal" choice is
            // ill-posed.
            Vec3d e1 = e1Basis[i], e2 = e2Basis[i];
            Vec3d se1 = tensors[i] * e1;
            Vec3d se2 = tensors[i] * e2;
            double a = Vec3d.Dot(e1, se1);
            double b = Vec3d.Dot(e1, se2);
            double c = Vec3d.Dot(e2, se2);

            // Degenerate (flat or isolated) vertex: keep the frame-derived directions
            if (Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c))) < 1e-15) continue;

            DiagonalizeShapeOperator(a, b, c, out double kappa1, out double kappa2, out double theta);
            Vec3d dir1 = (Math.Cos(theta) * e1 + Math.Sin(theta) * e2).Normalized();
            Vec3d dir2 = (-Math.Sin(theta) * e1 + Math.Cos(theta) * e2).Normalized();

            if (kappa1 >= kappa2)
            {
                k1[i] = kappa1; d1[i] = dir1;
                k2[i] = kappa2; d2[i] = dir2;
            }
            else
            {
                k1[i] = kappa2; d1[i] = dir2;
                k2[i] = kappa1; d2[i] = dir1;
            }
        }
    }
}
