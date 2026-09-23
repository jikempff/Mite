using System;
using System.Collections.Generic;

namespace Mite.Core.Numerics;

/// <summary>
/// Direct solver for sparse symmetric positive-definite systems (cotangent
/// Laplacians, force-density matrices, frame stiffness matrices): reverse
/// Cuthill–McKee reordering followed by an envelope (skyline) LDLᵀ
/// factorization. Factor once, solve for as many right-hand sides as needed.
///
/// A generic dense LU on these matrices is O(n³) and makes anything beyond a
/// thousand unknowns unusable; the envelope factorization is O(n·b²) with b
/// the reordered bandwidth (~√n for surface-like meshes), so 20 000 unknowns
/// take seconds rather than hours.
/// </summary>
public sealed class SparseSymmetricSolver
{
    private readonly int _n;
    private readonly int[] _perm;      // new index -> original index
    private readonly int[] _invPerm;   // original index -> new index
    private readonly int[] _colStart;  // envelope column offsets
    private readonly int[] _first;     // first stored row of each column
    private readonly double[] _env;    // strictly-upper envelope entries (L^T by columns)
    private readonly double[] _diag;   // D of LDL^T

    /// <summary>Smallest pivot relative to the largest diagonal entry; below it the matrix is treated as singular.</summary>
    public double MinPivotRatio { get; }

    /// <summary>Number of stored envelope entries (memory footprint of the factor).</summary>
    public long EnvelopeSize => _env.LongLength;

    /// <summary>
    /// Builder for symmetric matrices; only the upper triangle (row ≤ col)
    /// needs to be added, but adding both halves is tolerated (the lower one
    /// is ignored). Repeated (row, col) entries accumulate.
    /// </summary>
    public sealed class Builder
    {
        internal readonly int N;
        internal readonly Dictionary<int, double>[] Rows;

        public Builder(int n)
        {
            N = n;
            Rows = new Dictionary<int, double>[n];
            for (int i = 0; i < n; i++) Rows[i] = new Dictionary<int, double>();
        }

        public void Add(int row, int col, double value)
        {
            if (row > col) (row, col) = (col, row);
            var r = Rows[row];
            r.TryGetValue(col, out double v);
            r[col] = v + value;
        }

        public double Get(int row, int col)
        {
            if (row > col) (row, col) = (col, row);
            return Rows[row].TryGetValue(col, out double v) ? v : 0.0;
        }

        /// <summary>y = A x for the full symmetric matrix.</summary>
        public double[] Multiply(double[] x)
        {
            var y = new double[N];
            for (int i = 0; i < N; i++)
            {
                foreach (var kv in Rows[i])
                {
                    int j = kv.Key;
                    y[i] += kv.Value * x[j];
                    if (j != i) y[j] += kv.Value * x[i];
                }
            }
            return y;
        }

        public double InfinityNorm()
        {
            var rowSum = new double[N];
            for (int i = 0; i < N; i++)
                foreach (var kv in Rows[i])
                {
                    double a = Math.Abs(kv.Value);
                    rowSum[i] += a;
                    if (kv.Key != i) rowSum[kv.Key] += a;
                }
            double max = 0;
            foreach (double s in rowSum) max = Math.Max(max, s);
            return max;
        }
    }

    private SparseSymmetricSolver(int n, int[] perm, int[] invPerm, int[] colStart, int[] first,
        double[] env, double[] diag, double minPivotRatio)
    {
        _n = n; _perm = perm; _invPerm = invPerm; _colStart = colStart; _first = first;
        _env = env; _diag = diag; MinPivotRatio = minPivotRatio;
    }

    /// <summary>
    /// Factors the matrix. Returns null when a pivot collapses (singular
    /// matrix: a mechanism, missing anchors, all-zero weights) or, unless
    /// allowIndefinite is set, when a pivot turns negative (indefinite
    /// matrix). Indefinite systems (mixed-sign force densities) are factored
    /// without pivoting, which is adequate for the well-scaled systems here.
    /// </summary>
    public static SparseSymmetricSolver? Factor(Builder a, double minPivotRatio = 1e-12, bool allowIndefinite = false)
    {
        int n = a.N;
        if (n == 0)
            return new SparseSymmetricSolver(0, Array.Empty<int>(), Array.Empty<int>(),
                new[] { 0 }, Array.Empty<int>(), Array.Empty<double>(), Array.Empty<double>(), minPivotRatio);

        // Adjacency for the reordering
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();
        for (int i = 0; i < n; i++)
            foreach (var kv in a.Rows[i])
                if (kv.Key != i) { adj[i].Add(kv.Key); adj[kv.Key].Add(i); }

        int[] perm = ReverseCuthillMcKee(adj);
        var invPerm = new int[n];
        for (int k = 0; k < n; k++) invPerm[perm[k]] = k;

        // Envelope profile: first[j] = smallest permuted row index with a nonzero in column j
        var first = new int[n];
        for (int j = 0; j < n; j++) first[j] = j;
        for (int i = 0; i < n; i++)
        {
            foreach (var kv in a.Rows[i])
            {
                int pi = invPerm[i], pj = invPerm[kv.Key];
                int r = Math.Min(pi, pj), c = Math.Max(pi, pj);
                if (r < first[c]) first[c] = r;
            }
        }

        var colStart = new int[n + 1];
        long total = 0;
        for (int j = 0; j < n; j++)
        {
            colStart[j] = (int)total;
            total += j - first[j];
            if (total > int.MaxValue - 8)
                throw new InvalidOperationException("System too large for the envelope factorization.");
        }
        colStart[n] = (int)total;

        var env = new double[total];
        var diag = new double[n];
        double maxDiag = 0;
        for (int i = 0; i < n; i++)
        {
            foreach (var kv in a.Rows[i])
            {
                int pi = invPerm[i], pj = invPerm[kv.Key];
                if (pi == pj) { diag[pi] += kv.Value; maxDiag = Math.Max(maxDiag, Math.Abs(diag[pi])); continue; }
                int r = Math.Min(pi, pj), c = Math.Max(pi, pj);
                env[colStart[c] + (r - first[c])] += kv.Value;
            }
        }
        if (maxDiag <= 0) return null;
        double minPivot = minPivotRatio * maxDiag;

        // Column-wise LDL^T on the envelope. For column j the stored entries
        // are rows first[j]..j-1. Two passes per column:
        //   W[i,j] = A[i,j] - Σ_k L[i,k] W[k,j]   (k from max(first[i], first[j]) to i-1)
        // computed for increasing i with W kept in place (L[i,k] lives in the
        // already finished column i), then
        //   L[j,i] = W[i,j] / D[i],   D[j] = A[j,j] - Σ_i L[j,i] W[i,j].
        for (int j = 0; j < n; j++)
        {
            int fj = first[j];
            int cj = colStart[j];

            for (int i = fj; i < j; i++)
            {
                int fi = first[i];
                int ci = colStart[i];
                int kStart = Math.Max(fi, fj);
                double w = env[cj + (i - fj)];
                for (int k = kStart; k < i; k++)
                    w -= env[ci + (k - fi)] * env[cj + (k - fj)];
                env[cj + (i - fj)] = w;
            }

            double dj = diag[j];
            for (int i = fj; i < j; i++)
            {
                double w = env[cj + (i - fj)];
                double l = w / diag[i];
                dj -= l * w;
                env[cj + (i - fj)] = l;
            }

            if (double.IsNaN(dj) || (allowIndefinite ? Math.Abs(dj) < minPivot : dj < minPivot)) return null;
            diag[j] = dj;
        }

        return new SparseSymmetricSolver(n, perm, invPerm, colStart, first, env, diag, minPivotRatio);
    }

    /// <summary>Solves A x = b (b is not modified).</summary>
    public double[] Solve(double[] b)
    {
        if (b.Length != _n) throw new ArgumentException("Right-hand side length mismatch.", nameof(b));
        var y = new double[_n];
        for (int k = 0; k < _n; k++) y[k] = b[_perm[k]];

        // Forward: L z = y (L unit lower, L[j,i] stored in column j)
        for (int j = 0; j < _n; j++)
        {
            int fj = _first[j], cj = _colStart[j];
            double s = y[j];
            for (int i = fj; i < j; i++) s -= _env[cj + (i - fj)] * y[i];
            y[j] = s;
        }
        // Diagonal
        for (int j = 0; j < _n; j++) y[j] /= _diag[j];
        // Backward: L^T x = z
        for (int j = _n - 1; j >= 0; j--)
        {
            int fj = _first[j], cj = _colStart[j];
            double xj = y[j];
            for (int i = fj; i < j; i++) y[i] -= _env[cj + (i - fj)] * xj;
        }

        var x = new double[_n];
        for (int k = 0; k < _n; k++) x[_perm[k]] = y[k];
        return x;
    }

    /// <summary>
    /// Reverse Cuthill–McKee ordering: BFS from a low-degree node of each
    /// component, neighbors visited in increasing degree, order reversed.
    /// </summary>
    private static int[] ReverseCuthillMcKee(List<int>[] adj)
    {
        int n = adj.Length;
        var order = new List<int>(n);
        var visited = new bool[n];
        var degree = new int[n];
        for (int i = 0; i < n; i++) degree[i] = adj[i].Count;

        var byDegree = new int[n];
        for (int i = 0; i < n; i++) byDegree[i] = i;
        Array.Sort(byDegree, (x, y) => degree[x] != degree[y] ? degree[x].CompareTo(degree[y]) : x.CompareTo(y));

        var queue = new Queue<int>();
        foreach (int start in byDegree)
        {
            if (visited[start]) continue;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                order.Add(v);
                var nb = adj[v].ToArray();
                Array.Sort(nb, (x, y) => degree[x] != degree[y] ? degree[x].CompareTo(degree[y]) : x.CompareTo(y));
                foreach (int u in nb)
                {
                    if (visited[u]) continue;
                    visited[u] = true;
                    queue.Enqueue(u);
                }
            }
        }

        order.Reverse();
        return order.ToArray();
    }
}
