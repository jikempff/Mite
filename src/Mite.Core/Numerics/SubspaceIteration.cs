using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Numerics;

/// <summary>
/// Smallest positive eigenvalues of the pencil K φ = λ G φ with K symmetric
/// positive definite and G symmetric but possibly indefinite (linearised
/// buckling: G = −K_g, the negated geometric stiffness of the reference
/// load, so λ is the factor on that load at which the structure buckles).
///
/// Subspace iteration after Bathe &amp; Wilson (1972) and Bathe (2013, "The
/// subspace iteration method — revisited", Computers &amp; Structures 126):
/// q = max(2p, p + 8) vectors, inverse iteration K X̄ = G X, Rayleigh–Ritz on
/// the projected pencil, X = X̄ Q. Two changes for an indefinite G:
/// - the reduced problem is solved as G_r z = μ K_r z with μ = 1/λ (K_r is
///   positive definite, G_r is not), through the eigen-decomposition of K_r
///   so that dependent iteration vectors (G of low rank, e.g. a net with few
///   compressed members) are dropped and replaced instead of breaking a
///   Cholesky factor;
/// - the iteration converges to the eigenvalues of largest |μ|, i.e. the
///   smallest |λ| of either sign, so the wanted (positive) ones are picked
///   among the Ritz values and a Sturm sequence check (negative pivots of
///   K − σG, see <see cref="SparseSymmetricSolver.NegativePivots"/>) tells
///   whether any positive eigenvalue below the first one was missed.
/// </summary>
public static class SubspaceIteration
{
    public sealed class Result
    {
        /// <summary>The smallest positive eigenvalues found, ascending.</summary>
        public double[] Values { get; }

        /// <summary>Eigenvectors, K-normalised (φᵀKφ = 1), one per value.</summary>
        public double[][] Vectors { get; }

        public int Iterations { get; }

        public bool Converged { get; }

        /// <summary>Largest relative residual ‖Kφ − λGφ‖ / ‖Kφ‖ over the returned pairs.</summary>
        public double MaxResidual { get; }

        /// <summary>
        /// Sturm count below the first value: the number of eigenvalues in
        /// (0, λ₁(1 − 1e-6)). 0 means none was missed; −1 when the check
        /// could not be made (the shifted matrix was singular to working
        /// precision).
        /// </summary>
        public int SturmBelowFirst { get; }

        /// <summary>
        /// Sturm count up to the last value: eigenvalues in (0, λ_p(1 + 1e-6)).
        /// Equal to the number of returned values when the lowest p were all
        /// found; −1 when the check could not be made.
        /// </summary>
        public int SturmUpToLast { get; }

        public Result(double[] values, double[][] vectors, int iterations, bool converged,
            double maxResidual, int sturmBelowFirst, int sturmUpToLast)
        {
            Values = values; Vectors = vectors; Iterations = iterations; Converged = converged;
            MaxResidual = maxResidual; SturmBelowFirst = sturmBelowFirst; SturmUpToLast = sturmUpToLast;
        }

        /// <summary>True when the Sturm counts confirm that the returned values are the lowest positive ones.</summary>
        public bool SturmConfirmed => Values.Length > 0 && SturmBelowFirst == 0 && SturmUpToLast == Values.Length;
    }

    /// <summary>
    /// Finds the p smallest positive eigenvalues of K φ = λ G φ. kFactor is
    /// the LDLᵀ factor of K (from the static solve); start (optional) is a
    /// first iteration vector, e.g. the static displacement. Converged when
    /// every wanted value changed by less than tolerance (relative) in the
    /// last iteration and every wanted pair has a relative residual
    /// ‖Kφ − λGφ‖ / ‖Kφ‖ below residualTolerance. With shift, the
    /// iteration moves to (K − σG)⁻¹G after four steps, σ = 0.8 λ₁ (halved
    /// until K − σG factors as positive definite).
    /// </summary>
    public static Result SmallestPositive(SparseSymmetricSolver kFactor,
        SparseSymmetricSolver.Builder k, SparseSymmetricSolver.Builder g, int p,
        double[]? start = null, int maxIterations = 300, double tolerance = 1e-10, double residualTolerance = 1e-6, bool shift = true)
    {
        int n = k.N;
        if (p < 1) throw new ArgumentOutOfRangeException(nameof(p));
        if (n == 0) return new Result(Array.Empty<double>(), Array.Empty<double[]>(), 0, true, 0, -1, -1);
        int q = Math.Min(n, Math.Max(2 * p, p + 8));

        var rng = new Random(12345);
        double[] RandomVector()
        {
            var v = new double[n];
            for (int i = 0; i < n; i++) v[i] = rng.NextDouble() - 0.5;
            return v;
        }
        var G = new Csr(g);
        var factor = kFactor;
        double sigma = 0;

        // X and G X are carried together: after the Ritz step both are the
        // same combinations of X̄ and G X̄, so G is applied once per vector
        // and iteration, and K X̄ = G X gives the residuals for free.
        var X = new List<double[]>(q);
        var GX = new List<double[]>(q);
        void AddVector(double[] v) { X.Add(v); GX.Add(G.Multiply(v)); }
        if (start != null && start.Length == n && start.Any(x => x != 0)) AddVector((double[])start.Clone());
        while (X.Count < q) AddVector(RandomVector());

        double[] previous = Array.Empty<double>();
        double[] values = Array.Empty<double>();
        double[][] vectors = Array.Empty<double[]>();
        double[] residuals = Array.Empty<double>();
        bool converged = false;
        int it = 0;
        for (it = 1; it <= maxIterations; it++)
        {
            int m = X.Count;
            var Xb = new double[m][];
            var GXb = new double[m][];
            for (int j = 0; j < m; j++)
            {
                Xb[j] = factor.Solve(GX[j]);
                GXb[j] = G.Multiply(Xb[j]);
            }
            // projected pencil: (K − σG) X̄ = G X, so K_r = X̄ᵀ K X̄ = X̄ᵀ G X + σ G_r,
            // G_r = X̄ᵀ G X̄
            var Kr = new double[m, m];
            var Gr = new double[m, m];
            for (int i = 0; i < m; i++)
                for (int j = i; j < m; j++)
                {
                    double gij = 0.5 * (Dot(Xb[i], GXb[j]) + Dot(Xb[j], GXb[i]));
                    double kij = 0.5 * (Dot(Xb[i], GX[j]) + Dot(Xb[j], GX[i])) + sigma * gij;
                    Kr[i, j] = Kr[j, i] = kij;
                    Gr[i, j] = Gr[j, i] = gij;
                }

            // K_r = V S Vᵀ; basis B = V S^(-1/2) on the well-conditioned part
            var (s, V) = Jacobi(Kr);
            double sMax = s.Max();
            var keep = Enumerable.Range(0, m).Where(i => s[i] > 1e-12 * sMax && s[i] > 0).ToArray();
            int r = keep.Length;
            if (r == 0) break; // G X = 0: nothing is compressed
            var B = new double[m, r];
            for (int c = 0; c < r; c++)
            {
                double f = 1.0 / Math.Sqrt(s[keep[c]]);
                for (int i = 0; i < m; i++) B[i, c] = V[i, keep[c]] * f;
            }
            var C = new double[r, r];
            for (int a = 0; a < r; a++)
                for (int b = a; b < r; b++)
                {
                    double sum = 0;
                    for (int i = 0; i < m; i++)
                    {
                        if (B[i, a] == 0) continue;
                        for (int j = 0; j < m; j++) sum += B[i, a] * Gr[i, j] * B[j, b];
                    }
                    C[a, b] = C[b, a] = sum;
                }
            var (mu, Z) = Jacobi(C);

            // Ritz vectors x = X̄ B z (K-orthonormal), with G x and K x = G X B z;
            // the largest |μ| first
            var order = Enumerable.Range(0, r).OrderByDescending(i => Math.Abs(mu[i])).ToArray();
            var ritz = new List<double[]>(r);
            var ritzG = new List<double[]>(r);
            var ritzK = new List<double[]>(r);
            var ritzMu = new List<double>(r);
            var gxArr = GX.ToArray();
            foreach (int o in order)
            {
                var coef = new double[m];
                for (int i = 0; i < m; i++)
                {
                    double sum = 0;
                    for (int c = 0; c < r; c++) sum += B[i, c] * Z[c, o];
                    coef[i] = sum;
                }
                ritz.Add(Combine(Xb, coef, n));
                ritzG.Add(Combine(GXb, coef, n));
                var kx = Combine(gxArr, coef, n); // K x = G X B z + σ G x
                var gxr = ritzG[ritzG.Count - 1];
                if (sigma != 0) for (int t = 0; t < n; t++) kx[t] += sigma * gxr[t];
                ritzK.Add(kx);
                ritzMu.Add(mu[o]);
            }

            // wanted: positive μ, largest first (smallest positive λ)
            var pos = Enumerable.Range(0, ritz.Count).Where(i => ritzMu[i] > 0)
                .OrderByDescending(i => ritzMu[i]).Take(p).ToArray();
            values = pos.Select(i => 1.0 / ritzMu[i]).ToArray();
            vectors = pos.Select(i => ritz[i]).ToArray();
            residuals = pos.Select(i => Residual(ritzK[i], ritzG[i], 1.0 / ritzMu[i])).ToArray();

            if (values.Length == p && previous.Length == p)
            {
                bool ok = true;
                for (int i = 0; i < p; i++)
                    if (Math.Abs(values[i] - previous[i]) > tolerance * Math.Abs(values[i])) { ok = false; break; }
                // the vectors converge as the square root of the values: ask
                // the residual too, so mode shapes are usable as imperfections
                if (ok && residuals.Max() > residualTolerance) ok = false;
                if (ok) { converged = true; break; }
            }
            previous = values;

            // shift once the lowest factor is roughly known: iterate with
            // (K − σG)⁻¹G, σ below λ₁, which keeps K − σG positive definite
            // (no eigenvalue in (0, σ)), damps the negative eigenvalues and
            // turns the convergence rate λ_i/λ_{q+1} into
            // (λ_i − σ)/(λ_{q+1} − σ) (Bathe 2013, shifting)
            if (shift && sigma == 0 && values.Length > 0 && (it >= 4 || it >= maxIterations / 2))
            {
                double target = 0.8 * values[0]; // Ritz values bound λ₁ from above
                for (int attempt = 0; attempt < 4 && sigma == 0; attempt++, target *= 0.5)
                {
                    var f = SparseSymmetricSolver.Factor(SparseSymmetricSolver.Combine(k, 1.0, g, -target), 1e-14);
                    if (f != null) { factor = f; sigma = target; }
                }
                if (sigma != 0) previous = Array.Empty<double>();
            }

            X = ritz; GX = ritzG;
            while (X.Count < q) AddVector(RandomVector());
        }
        if (it > maxIterations) it = maxIterations;

        double maxRes = residuals.Length > 0 ? residuals.Max() : 0;

        int below = -1, upTo = -1;
        if (values.Length > 0)
        {
            below = SturmCount(k, g, values[0] * (1 - 1e-6));
            upTo = SturmCount(k, g, values[values.Length - 1] * (1 + 1e-6));
        }
        return new Result(values, vectors, it, converged, maxRes, below, upTo);
    }

    /// <summary>Number of eigenvalues of K φ = λ G φ in (0, σ): negative pivots of K − σG (−1 if singular at σ).</summary>
    public static int SturmCount(SparseSymmetricSolver.Builder k, SparseSymmetricSolver.Builder g, double sigma)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            double sg = sigma * (1 - 1e-7 * attempt);
            var f = SparseSymmetricSolver.Factor(SparseSymmetricSolver.Combine(k, 1.0, g, -sg), 1e-15, allowIndefinite: true);
            if (f != null) return f.NegativePivots;
        }
        return -1;
    }

    /// <summary>‖Kφ − λGφ‖ / ‖Kφ‖ from Kφ and Gφ.</summary>
    private static double Residual(double[] kx, double[] gx, double lambda)
    {
        double num = 0, den = 0;
        for (int t = 0; t < kx.Length; t++) { double d = kx[t] - lambda * gx[t]; num += d * d; den += kx[t] * kx[t]; }
        return den > 0 ? Math.Sqrt(num / den) : 0;
    }

    private static double[] Combine(double[][] basis, double[] coef, int n)
    {
        var x = new double[n];
        for (int i = 0; i < coef.Length; i++)
        {
            double ci = coef[i];
            if (ci == 0) continue;
            var b = basis[i];
            for (int t = 0; t < n; t++) x[t] += ci * b[t];
        }
        return x;
    }

    /// <summary>Compressed sparse rows of a full symmetric matrix, for fast repeated products.</summary>
    private sealed class Csr
    {
        private readonly int[] _start, _col;
        private readonly double[] _val;

        public Csr(SparseSymmetricSolver.Builder a)
        {
            int n = a.N;
            var count = new int[n];
            for (int i = 0; i < n; i++)
                foreach (var kv in a.Rows[i]) { count[i]++; if (kv.Key != i) count[kv.Key]++; }
            _start = new int[n + 1];
            for (int i = 0; i < n; i++) _start[i + 1] = _start[i] + count[i];
            _col = new int[_start[n]];
            _val = new double[_start[n]];
            var fill = (int[])_start.Clone();
            for (int i = 0; i < n; i++)
                foreach (var kv in a.Rows[i])
                {
                    _col[fill[i]] = kv.Key; _val[fill[i]++] = kv.Value;
                    if (kv.Key != i) { _col[fill[kv.Key]] = i; _val[fill[kv.Key]++] = kv.Value; }
                }
        }

        public double[] Multiply(double[] x)
        {
            int n = _start.Length - 1;
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int k = _start[i]; k < _start[i + 1]; k++) s += _val[k] * x[_col[k]];
                y[i] = s;
            }
            return y;
        }
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    /// <summary>Cyclic Jacobi eigen-decomposition of a small symmetric matrix: values and column eigenvectors.</summary>
    internal static (double[] Values, double[,] Vectors) Jacobi(double[,] a0)
    {
        int n = a0.GetLength(0);
        var a = (double[,])a0.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1;
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0, diag = 0;
            for (int i = 0; i < n; i++)
            {
                diag += a[i, i] * a[i, i];
                for (int j = i + 1; j < n; j++) off += a[i, j] * a[i, j];
            }
            if (off <= 1e-30 * Math.Max(diag, 1e-300)) break;
            for (int pp = 0; pp < n - 1; pp++)
                for (int qq = pp + 1; qq < n; qq++)
                {
                    double apq = a[pp, qq];
                    if (apq == 0) continue;
                    double theta = (a[qq, qq] - a[pp, pp]) / (2 * apq);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k, pp], akq = a[k, qq];
                        a[k, pp] = c * akp - s * akq;
                        a[k, qq] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[pp, k], aqk = a[qq, k];
                        a[pp, k] = c * apk - s * aqk;
                        a[qq, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k, pp], vkq = v[k, qq];
                        v[k, pp] = c * vkp - s * vkq;
                        v[k, qq] = s * vkp + c * vkq;
                    }
                }
        }
        var values = new double[n];
        for (int i = 0; i < n; i++) values[i] = a[i, i];
        return (values, v);
    }
}
