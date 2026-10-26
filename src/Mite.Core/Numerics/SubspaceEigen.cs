using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Numerics;

/// <summary>
/// Subspace iteration for the lowest eigenpairs of the generalized symmetric
/// eigenproblem K φ = λ B φ with K positive definite and B symmetric but not
/// necessarily definite — the form of the linear buckling problem
/// (K_e + λ K_g) φ = 0 with B = −K_g, where members in tension make B
/// indefinite and λ may come out negative (buckling under the reversed load).
///
/// Method (Bathe &amp; Wilson 1973, "Solution methods for eigenvalue problems in
/// structural mechanics", Int. J. Numer. Meth. Eng. 6; Bathe 1996, Finite
/// Element Procedures §11.6): iterate on q ≥ p vectors X,
///   K Θ = B X,  K_r = Θᵀ K Θ = Θᵀ B X,  B_r = Θᵀ B Θ,
///   K_r Q = B_r Q Λ  (q × q, solved exactly),  X ← Θ Q (K-orthonormal),
/// so the iteration vectors converge to the eigenvectors of the q eigenvalues
/// of smallest |λ| (largest |1/λ|), the i-th at the rate |λ_i / λ_{q+1}|.
/// The reduced problem is turned into a standard symmetric one through the
/// Cholesky factor of K_r (C⁻¹ B_r C⁻ᵀ y = (1/λ) y) and solved by cyclic
/// Jacobi, which copes with an indefinite B_r. K is factored once; every
/// iteration costs q solves and q products with B.
///
/// Verification: for K positive definite the number of negative pivots of
/// the LDLᵀ factorization of K − μ B equals the number of eigenvalues in
/// (0, μ) (Sylvester's law of inertia — Bathe's Sturm sequence check), see
/// <see cref="CountBelow"/>.
/// </summary>
public static class SubspaceEigen
{
    public sealed class Result
    {
        /// <summary>Converged eigenvalues sorted by increasing |λ| (both signs).</summary>
        public double[] Eigenvalues = Array.Empty<double>();

        /// <summary>Eigenvectors matching <see cref="Eigenvalues"/>, K-orthonormal (φᵀ K φ = 1).</summary>
        public double[][] Eigenvectors = Array.Empty<double[]>();

        /// <summary>True when the requested eigenpairs met the tolerance within the iteration budget.</summary>
        public bool Converged;

        public int Iterations;
    }

    /// <summary>
    /// Finds eigenpairs of K φ = λ B φ. p = number of eigenvalues wanted
    /// (counted among the positive ones when positiveOnly is set, so that
    /// tension-dominated negative eigenvalues of smaller magnitude do not
    /// use up the budget); the subspace holds q = min(n, max(2p, p + 8))
    /// vectors. Returns every converged eigenpair in the subspace, so the
    /// caller may see more than p (and negative ones). Null when K is not
    /// positive definite.
    /// </summary>
    public static Result? Solve(SparseSymmetricSolver.Builder K, SparseSymmetricSolver.Builder B, int p,
        bool positiveOnly = true, double tolerance = 1e-9, int maxIterations = 200, Random? random = null, int extraVectors = 8,
        IReadOnlyList<double[]>? startVectors = null)
    {
        int n = K.N;
        if (n == 0 || p <= 0) return new Result { Converged = true };
        var solver = SparseSymmetricSolver.Factor(K, 1e-14);
        if (solver == null) return null;
        p = Math.Min(p, n);
        int q = Math.Min(n, Math.Max(2 * p, p + Math.Max(1, extraVectors)));
        random ??= new Random(12345);

        // Starting vectors (Bathe 1996 §11.6.3): the diagonal ratio b_ii / k_ii
        // as the first column, unit vectors at the largest ratios, one random.
        var X = new double[q][];
        var ratio = new double[n];
        for (int i = 0; i < n; i++)
        {
            double kii = K.Get(i, i), bii = B.Get(i, i);
            ratio[i] = kii > 0 ? Math.Abs(bii) / kii : 0.0;
        }
        X[0] = new double[n];
        for (int i = 0; i < n; i++) X[0][i] = ratio[i] > 0 ? ratio[i] : 1e-3 * (ratio.Max() + 1e-300);
        var order = Enumerable.Range(0, n).OrderByDescending(i => ratio[i]).ToArray();
        for (int k = 1; k < q - 1; k++) { X[k] = new double[n]; X[k][order[(k - 1) % n]] = 1.0; }
        if (q > 1) { X[q - 1] = new double[n]; for (int i = 0; i < n; i++) X[q - 1][i] = random.NextDouble() - 0.5; }
        if (q == 1) for (int i = 0; i < n; i++) X[0][i] += 1e-6 * (random.NextDouble() - 0.5);
        // caller-supplied start vectors (e.g. the static deflection, which often resembles the first mode) replace the unit vectors
        if (startVectors != null)
            for (int k = 0; k < startVectors.Count && 1 + k < Math.Max(1, q - 1); k++)
                if (startVectors[k].Length == n) X[1 + k] = (double[])startVectors[k].Clone();

        var Bcsr = new Csr(B);
        var lambdaPrev = new double[q];
        for (int k = 0; k < q; k++) lambdaPrev[k] = double.NaN;
        var result = new Result();
        var converged = new bool[q];
        for (int it = 1; it <= maxIterations; it++)
        {
            result.Iterations = it;
            // Θ = K⁻¹ B X
            var BX = new double[q][];
            var Theta = new double[q][];
            for (int k = 0; k < q; k++) { BX[k] = Bcsr.Multiply(X[k]); Theta[k] = solver.Solve(BX[k]); }
            // projections
            var Kr = new double[q, q];
            var Br = new double[q, q];
            var BTheta = new double[q][];
            for (int k = 0; k < q; k++) BTheta[k] = Bcsr.Multiply(Theta[k]);
            for (int a = 0; a < q; a++)
                for (int b = a; b < q; b++)
                {
                    double sk = 0, sb = 0;
                    var ta = Theta[a];
                    var bx = BX[b];
                    var bt = BTheta[b];
                    for (int i = 0; i < n; i++) { sk += ta[i] * bx[i]; sb += ta[i] * bt[i]; }
                    Kr[a, b] = Kr[b, a] = sk;
                    Br[a, b] = Br[b, a] = sb;
                }
            // Kr = C Cᵀ (Cholesky); S = C⁻¹ Br C⁻ᵀ; S y = κ y; Q = C⁻ᵀ Y
            var C = Cholesky(Kr, q);
            if (C == null)
            {
                // the subspace lost rank (vectors collapsed); restart the collapsed ones randomly
                for (int k = 0; k < q; k++) if (converged[k]) continue; else for (int i = 0; i < n; i++) X[k][i] = random.NextDouble() - 0.5;
                continue;
            }
            var Cinv = LowerInverse(C, q);
            var S = new double[q, q];
            for (int a = 0; a < q; a++)
                for (int b = 0; b < q; b++)
                {
                    double s = 0;
                    for (int i = 0; i < q; i++)
                        for (int j = 0; j < q; j++) s += Cinv[a, i] * Br[i, j] * Cinv[b, j];
                    S[a, b] = s;
                }
            var (kappa, Y) = Jacobi(S, q);
            // order by |κ| descending = |λ| ascending
            var idx = Enumerable.Range(0, q).OrderByDescending(k => Math.Abs(kappa[k])).ToArray();
            var newX = new double[q][];
            var lambda = new double[q];
            for (int r = 0; r < q; r++)
            {
                int k = idx[r];
                lambda[r] = Math.Abs(kappa[k]) > 1e-300 ? 1.0 / kappa[k] : double.PositiveInfinity;
                // Q column k = C⁻ᵀ y_k
                var qcol = new double[q];
                for (int a = 0; a < q; a++) { double s = 0; for (int i = 0; i < q; i++) s += Cinv[i, a] * Y[i, k]; qcol[a] = s; }
                var x = new double[n];
                for (int a = 0; a < q; a++) { double w = qcol[a]; if (w == 0) continue; var t = Theta[a]; for (int i = 0; i < n; i++) x[i] += w * t[i]; }
                newX[r] = x;
            }
            X = newX;

            // convergence on the wanted eigenvalues
            int wanted = 0; bool allOk = true;
            for (int r = 0; r < q; r++)
            {
                bool ok = !double.IsNaN(lambdaPrev[r]) && !double.IsInfinity(lambda[r]) &&
                          Math.Abs(lambda[r] - lambdaPrev[r]) <= tolerance * Math.Max(Math.Abs(lambda[r]), 1e-300);
                converged[r] = ok;
                if (positiveOnly && lambda[r] <= 0) continue;
                if (wanted < p) { wanted++; if (!ok) allOk = false; }
            }
            lambdaPrev = lambda;
            if (allOk && wanted == Math.Min(p, q))
            {
                result.Converged = true;
                Collect(result, lambda, X, converged);
                return result;
            }
            if (it == maxIterations)
            {
                Collect(result, lambda, X, converged);
                return result;
            }
        }
        return result;
    }

    private static void Collect(Result result, double[] lambda, double[][] X, bool[] converged)
    {
        var ev = new List<double>(); var vec = new List<double[]>();
        for (int r = 0; r < lambda.Length; r++)
            if (converged[r] && !double.IsInfinity(lambda[r])) { ev.Add(lambda[r]); vec.Add(X[r]); }
        result.Eigenvalues = ev.ToArray();
        result.Eigenvectors = vec.ToArray();
    }

    /// <summary>
    /// Sturm sequence check: the number of eigenvalues of K φ = λ B φ in
    /// (0, μ) for μ > 0 (or in (μ, 0) for μ &lt; 0), as the number of negative
    /// pivots of K − μ B. Null when the shifted matrix is numerically singular
    /// (μ is itself an eigenvalue to rounding).
    /// </summary>
    public static int? CountBelow(SparseSymmetricSolver.Builder K, SparseSymmetricSolver.Builder B, double mu)
    {
        int n = K.N;
        var shifted = new SparseSymmetricSolver.Builder(n);
        for (int i = 0; i < n; i++)
        {
            foreach (var kv in K.Rows[i]) shifted.Add(i, kv.Key, kv.Value);
            foreach (var kv in B.Rows[i]) shifted.Add(i, kv.Key, -mu * kv.Value);
        }
        var f = SparseSymmetricSolver.Factor(shifted, 1e-14, allowIndefinite: true);
        return f?.NegativePivotCount;
    }

    /// <summary>Compressed rows of the full symmetric matrix for fast products (the Builder's dictionaries are slow to enumerate).</summary>
    private sealed class Csr
    {
        private readonly int[] _start, _col; private readonly double[] _val; private readonly int _n;
        public Csr(SparseSymmetricSolver.Builder a)
        {
            _n = a.N;
            var count = new int[_n];
            for (int i = 0; i < _n; i++) foreach (var kv in a.Rows[i]) { count[i]++; if (kv.Key != i) count[kv.Key]++; }
            _start = new int[_n + 1];
            for (int i = 0; i < _n; i++) _start[i + 1] = _start[i] + count[i];
            _col = new int[_start[_n]]; _val = new double[_start[_n]];
            var fill = (int[])_start.Clone();
            for (int i = 0; i < _n; i++)
                foreach (var kv in a.Rows[i])
                {
                    int j = kv.Key;
                    _col[fill[i]] = j; _val[fill[i]++] = kv.Value;
                    if (j != i) { _col[fill[j]] = i; _val[fill[j]++] = kv.Value; }
                }
        }
        public double[] Multiply(double[] x)
        {
            var y = new double[_n];
            for (int i = 0; i < _n; i++)
            {
                double s = 0;
                for (int k = _start[i]; k < _start[i + 1]; k++) s += _val[k] * x[_col[k]];
                y[i] = s;
            }
            return y;
        }
    }

    private static double[,]? Cholesky(double[,] A, int q)
    {
        var C = new double[q, q];
        for (int j = 0; j < q; j++)
        {
            double d = A[j, j];
            for (int k = 0; k < j; k++) d -= C[j, k] * C[j, k];
            if (!(d > 1e-300 * Math.Max(1.0, Math.Abs(A[j, j])))) return null;
            C[j, j] = Math.Sqrt(d);
            for (int i = j + 1; i < q; i++)
            {
                double s = A[i, j];
                for (int k = 0; k < j; k++) s -= C[i, k] * C[j, k];
                C[i, j] = s / C[j, j];
            }
        }
        return C;
    }

    private static double[,] LowerInverse(double[,] C, int q)
    {
        var inv = new double[q, q];
        for (int j = 0; j < q; j++)
        {
            inv[j, j] = 1.0 / C[j, j];
            for (int i = j + 1; i < q; i++)
            {
                double s = 0;
                for (int k = j; k < i; k++) s -= C[i, k] * inv[k, j];
                inv[i, j] = s / C[i, i];
            }
        }
        return inv;
    }

    /// <summary>Cyclic Jacobi for a small dense symmetric matrix: eigenvalues and the matrix of eigenvectors (columns).</summary>
    internal static (double[] Values, double[,] Vectors) Jacobi(double[,] S, int q)
    {
        var A = (double[,])S.Clone();
        var V = new double[q, q];
        for (int i = 0; i < q; i++) V[i, i] = 1.0;
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int i = 0; i < q; i++) for (int j = i + 1; j < q; j++) off += A[i, j] * A[i, j];
            if (off < 1e-30) break;
            for (int pp = 0; pp < q; pp++)
                for (int qq = pp + 1; qq < q; qq++)
                {
                    if (Math.Abs(A[pp, qq]) < 1e-300) continue;
                    double theta = 0.5 * (A[qq, qq] - A[pp, pp]) / A[pp, qq];
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1.0;
                    double c = 1.0 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < q; k++)
                    {
                        double akp = A[k, pp], akq = A[k, qq];
                        A[k, pp] = c * akp - s * akq;
                        A[k, qq] = s * akp + c * akq;
                    }
                    for (int k = 0; k < q; k++)
                    {
                        double apk = A[pp, k], aqk = A[qq, k];
                        A[pp, k] = c * apk - s * aqk;
                        A[qq, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < q; k++)
                    {
                        double vkp = V[k, pp], vkq = V[k, qq];
                        V[k, pp] = c * vkp - s * vkq;
                        V[k, qq] = s * vkp + c * vkq;
                    }
                }
        }
        var vals = new double[q];
        for (int i = 0; i < q; i++) vals[i] = A[i, i];
        return (vals, V);
    }
}
