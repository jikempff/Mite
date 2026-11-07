// Block LDLᵀ with 3 × 3 blocks for the Gauss–Newton / Levenberg–Marquardt
// systems of the kinetic solve (three position and three normal unknowns per
// node). The algorithm is the up-looking sparse LDLᵀ of T. A. Davis,
// "Algorithm 849: A concise sparse Cholesky factorization package" (ACM TOMS
// 31(4), 2005; Direct Methods for Sparse Linear Systems, SIAM 2006, ch. 4)
// with every scalar replaced by a 3 × 3 block: row k of L comes from the block
// triangular solve L(1:k−1) Y = A(1:k−1, k)ᵀ, L(k, i) = Y_i D_i⁻¹ and
// D_k = A_kk − Σ L(k, i) Y_iᵀ; the pattern of row k is the union of the paths
// from the nonzero rows of column k to the root of the block elimination tree
// (Liu 1990, "The role of elimination trees in sparse factorization"). The
// blocks make the factor a block-structured one in the sense of Ng & Peyton
// 1993 ("Block sparse Cholesky algorithms on advanced uniprocessor
// computers", SIAM J. Sci. Comput. 14(5)): the index work is done once per
// block and the inner loops are dense 3 × 3 products. The fill-reducing
// ordering is the nested dissection of SparseLdl (George 1973) applied to
// the block graph, so a node's three unknowns stay together. The symbolic
// analysis (ordering, elimination tree, column counts, the map from the
// matrix to the permuted columns) is done once; refactoring with new values
// and a new diagonal shift reuses it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Mite.Core.Numerics;

/// <summary>
/// Symmetric sparse matrix stored as 3 × 3 blocks: the upper block triangle
/// (block column ≥ block row) in block compressed rows, the diagonal blocks
/// in full. The block pattern is fixed at construction; values are added
/// scalar by scalar through <see cref="Add"/> or at the positions returned by
/// <see cref="Positions"/>.
/// </summary>
public sealed class BlockSymmetricMatrix3
{
    /// <summary>Number of 3 × 3 block rows (the matrix is 3 Blocks square).</summary>
    public int Blocks { get; }

    /// <summary>Scalar order of the matrix.</summary>
    public int N => Blocks * 3;

    internal readonly int[] RowStart;
    internal readonly int[] Cols;

    /// <summary>
    /// Block values, 9 per stored block, row-major within the block:
    /// Values[9 s + 3 i + j] = A[3 br + i, 3 bc + j] for the block s = (br, bc).
    /// </summary>
    public double[] Values { get; }

    /// <summary>Number of stored blocks (upper block triangle including the diagonal).</summary>
    public int StoredBlocks => Cols.Length;

    /// <summary>
    /// Builds the pattern from the block pairs that can be nonzero (either
    /// order; the diagonal blocks are always stored).
    /// </summary>
    public BlockSymmetricMatrix3(int blocks, IEnumerable<(int Row, int Col)> blockPairs)
    {
        if (blocks < 0) throw new ArgumentOutOfRangeException(nameof(blocks));
        Blocks = blocks;
        var sets = new HashSet<int>[blocks];
        for (int i = 0; i < blocks; i++) sets[i] = new HashSet<int> { i };
        foreach (var (row, col) in blockPairs)
        {
            int r = Math.Min(row, col), c = Math.Max(row, col);
            if (r < 0 || c >= blocks) throw new ArgumentOutOfRangeException(nameof(blockPairs));
            sets[r].Add(c);
        }
        RowStart = new int[blocks + 1];
        for (int i = 0; i < blocks; i++) RowStart[i + 1] = RowStart[i] + sets[i].Count;
        Cols = new int[RowStart[blocks]];
        for (int i = 0; i < blocks; i++)
        {
            var cs = new int[sets[i].Count];
            sets[i].CopyTo(cs);
            Array.Sort(cs);
            Array.Copy(cs, 0, Cols, RowStart[i], cs.Length);
        }
        Values = new double[9L * Cols.Length > int.MaxValue ? throw new InvalidOperationException("Matrix too large.") : 9 * Cols.Length];
    }

    /// <summary>Index of the stored block (row ≤ col), or −1 when the pair is not in the pattern.</summary>
    public int Slot(int blockRow, int blockCol)
    {
        if (blockRow > blockCol) (blockRow, blockCol) = (blockCol, blockRow);
        if (blockRow < 0 || blockCol >= Blocks) return -1;
        int lo = RowStart[blockRow], hi = RowStart[blockRow + 1] - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = Cols[mid];
            if (c == blockCol) return mid;
            if (c < blockCol) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>
    /// Positions in <see cref="Values"/> of the scalar entry (row, col) — the
    /// stored one and, for an off-diagonal entry of a diagonal block, its
    /// mirror (−1 otherwise). Both are −1 when the block is not in the pattern.
    /// </summary>
    public (int Position, int Mirror) Positions(int row, int col)
    {
        if (row > col) (row, col) = (col, row);
        int br = row / 3, bc = col / 3;
        int s = Slot(br, bc);
        if (s < 0) return (-1, -1);
        int i = row - 3 * br, j = col - 3 * bc;
        int pos = 9 * s + 3 * i + j;
        int mirror = br == bc && i != j ? 9 * s + 3 * j + i : -1;
        return (pos, mirror);
    }

    /// <summary>Sets every value to zero (the pattern stays).</summary>
    public void Clear() => Array.Clear(Values, 0, Values.Length);

    /// <summary>Adds a value to the symmetric entry (row, col) (and (col, row)).</summary>
    public void Add(int row, int col, double value)
    {
        var (pos, mirror) = Positions(row, col);
        if (pos < 0) throw new ArgumentException($"Block ({row / 3}, {col / 3}) is not in the pattern.");
        Values[pos] += value;
        if (mirror >= 0) Values[mirror] += value;
    }

    /// <summary>The scalar diagonal entry.</summary>
    public double Diagonal(int index)
    {
        int b = index / 3, i = index - 3 * b;
        int s = Slot(b, b);
        return Values[9 * s + 4 * i];
    }

    /// <summary>y = A x for the full symmetric matrix.</summary>
    public double[] Multiply(double[] x)
    {
        if (x.Length != N) throw new ArgumentException("Vector length mismatch.", nameof(x));
        var y = new double[N];
        for (int br = 0; br < Blocks; br++)
            for (int p = RowStart[br]; p < RowStart[br + 1]; p++)
            {
                int bc = Cols[p];
                int v = 9 * p, xr = 3 * br, xc = 3 * bc;
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        double a = Values[v + 3 * i + j];
                        y[xr + i] += a * x[xc + j];
                        if (bc != br) y[xc + j] += a * x[xr + i];
                    }
            }
        return y;
    }
}

/// <summary>
/// Block LDLᵀ factorization (3 × 3 blocks) of a <see cref="BlockSymmetricMatrix3"/>
/// whose pattern is analysed once; <see cref="Factor"/> refactors the current
/// values, optionally with a diagonal shift (the Levenberg–Marquardt
/// damping), and <see cref="Solve"/> uses the last successful factorization.
/// </summary>
public sealed class BlockLdl3
{
    private readonly BlockSymmetricMatrix3 _a;
    private readonly int _nb;
    private readonly int[] _perm, _inv;              // block ordering: new → old, old → new
    private readonly int[] _ap, _ai, _aslot;         // permuted upper block triangle by columns (rows ≤ column)
    private readonly bool[] _adirect;                // stored block equals A(k, i) as is (else transposed)
    private readonly int[] _parent, _lp, _lnz, _li, _flag, _pattern;
    private readonly double[] _lx;                   // 9 per block of L (strictly lower block part), by columns
    private readonly double[] _dinv;                 // 9 per diagonal block: the LDLᵀ of D_k (d1, d2, d3, l10, l20, l21)
    private readonly double[] _y;                    // 9 per block work row
    private bool _factored;

    /// <summary>Stored blocks of L (strictly lower block triangle).</summary>
    public long FactorBlocks => _lp[_nb];

    /// <summary>Scalar order.</summary>
    public int N => 3 * _nb;

    /// <summary>Symbolic analysis of the matrix's block pattern (ordering, elimination tree, column counts).</summary>
    public BlockLdl3(BlockSymmetricMatrix3 a)
    {
        _a = a ?? throw new ArgumentNullException(nameof(a));
        int nb = _nb = a.Blocks;
        var adj = new List<int>[nb];
        for (int i = 0; i < nb; i++) adj[i] = new List<int>();
        for (int br = 0; br < nb; br++)
            for (int p = a.RowStart[br]; p < a.RowStart[br + 1]; p++)
            {
                int bc = a.Cols[p];
                if (bc == br) continue;
                adj[br].Add(bc); adj[bc].Add(br);
            }
        _perm = Ordering(adj);
        _inv = new int[nb];
        for (int k = 0; k < nb; k++) _inv[_perm[k]] = k;

        // permuted upper block triangle by columns: column c holds block rows r ≤ c
        var cnt = new int[nb + 1];
        for (int br = 0; br < nb; br++)
            for (int p = a.RowStart[br]; p < a.RowStart[br + 1]; p++)
            {
                int r = _inv[br], c = _inv[a.Cols[p]];
                cnt[Math.Max(r, c) + 1]++;
            }
        for (int k = 0; k < nb; k++) cnt[k + 1] += cnt[k];
        _ap = (int[])cnt.Clone();
        _ai = new int[_ap[nb]];
        _aslot = new int[_ap[nb]];
        _adirect = new bool[_ap[nb]];
        var fill = (int[])cnt.Clone();
        for (int br = 0; br < nb; br++)
            for (int p = a.RowStart[br]; p < a.RowStart[br + 1]; p++)
            {
                int bc = a.Cols[p];
                int r = _inv[br], c = _inv[bc];
                // stored block S = A(br, bc). Y_r = A_perm(c, r) is needed: for r ≤ c that is A(bc, br) = Sᵀ,
                // for r > c (the entry lives in column r, row c) it is A(br, bc) = S.
                bool direct = r > c;
                if (r > c) (r, c) = (c, r);
                int q = fill[c]++;
                _ai[q] = r; _aslot[q] = p; _adirect[q] = direct;
            }

        // elimination tree and block column counts of L (Davis 2005, ldl_symbolic)
        _parent = new int[nb];
        _flag = new int[nb];
        _lnz = new int[nb];
        for (int k = 0; k < nb; k++)
        {
            _parent[k] = -1; _flag[k] = k; _lnz[k] = 0;
            for (int p = _ap[k]; p < _ap[k + 1]; p++)
            {
                int i = _ai[p];
                if (i >= k) continue;
                for (; _flag[i] != k; i = _parent[i])
                {
                    if (_parent[i] == -1) _parent[i] = k;
                    _lnz[i]++;
                    _flag[i] = k;
                }
            }
        }
        _lp = new int[nb + 1];
        for (int k = 0; k < nb; k++)
        {
            long next = (long)_lp[k] + _lnz[k];
            if (9 * next > int.MaxValue - 16) throw new InvalidOperationException("System too large for the block factorization.");
            _lp[k + 1] = (int)next;
        }
        _li = new int[_lp[nb]];
        _lx = new double[9 * _lp[nb]];
        _dinv = new double[9 * nb];
        _y = new double[9 * nb];
        _pattern = new int[nb];
    }

    /// <summary>
    /// Block graphs up to this many vertices are ordered by minimum degree on
    /// the explicit elimination graph; larger ones by nested dissection with
    /// minimum degree inside the leaves (the ordering's cost grows with the
    /// cliques, the factorization's with the fill — measured on the kinetic
    /// systems of hyperboloid grids: minimum degree halves the fill of the
    /// plain nested dissection up to 3 200 blocks and costs well under a
    /// second there).
    /// </summary>
    public const int MinimumDegreeLimit = 8192;

    /// <summary>Nested-dissection leaf size (block vertices) above <see cref="MinimumDegreeLimit"/>.</summary>
    public const int DissectionLeaf = 1024;

    /// <summary>Fill-reducing ordering of a block graph (new index → old index).</summary>
    public static int[] Ordering(List<int>[] adjacency)
    {
        int nb = adjacency.Length;
        if (nb == 0) return Array.Empty<int>();
        return nb <= MinimumDegreeLimit ? MinimumDegree(adjacency) : SparseLdl.NestedDissection(adjacency, DissectionLeaf, MinimumDegreeLeaf);
    }

    /// <summary>
    /// Minimum-degree ordering on the explicit elimination graph (Tinney &amp;
    /// Walker 1967, "Direct solutions of sparse network equations by
    /// optimally ordered triangular factorization"; Rose 1972; George &amp;
    /// Liu 1989, "The evolution of the minimum degree ordering algorithm",
    /// SIAM Review 31(1)): the vertex of least degree is eliminated and its
    /// neighbours made a clique, ties broken by the lowest index.
    /// </summary>
    public static int[] MinimumDegree(List<int>[] adj)
    {
        int n = adj.Length;
        var g = new HashSet<int>[n];
        for (int i = 0; i < n; i++) g[i] = new HashSet<int>(adj[i]);
        var order = new int[n];
        var done = new bool[n];
        // bucket by degree
        var deg = new int[n];
        for (int i = 0; i < n; i++) deg[i] = g[i].Count;
        var buckets = new SortedDictionary<int, SortedSet<int>>();
        void Put(int v) { if (!buckets.TryGetValue(deg[v], out var b)) buckets[deg[v]] = b = new SortedSet<int>(); b.Add(v); }
        void Take(int v) { var b = buckets[deg[v]]; b.Remove(v); if (b.Count == 0) buckets.Remove(deg[v]); }
        for (int i = 0; i < n; i++) Put(i);
        var nb = new List<int>();
        for (int k = 0; k < n; k++)
        {
            var first = buckets.First().Value;
            int v = first.Min;
            Take(v);
            done[v] = true;
            order[k] = v;
            nb.Clear(); nb.AddRange(g[v]);
            foreach (int u in nb) g[u].Remove(v);
            for (int a = 0; a < nb.Count; a++)
                for (int b = a + 1; b < nb.Count; b++)
                {
                    g[nb[a]].Add(nb[b]); g[nb[b]].Add(nb[a]);
                }
            foreach (int u in nb)
            {
                int d = g[u].Count;
                if (d != deg[u]) { Take(u); deg[u] = d; Put(u); }
            }
            g[v].Clear();
        }
        return order;
    }

    /// <summary>
    /// Minimum-degree ordering of one leaf part of the nested dissection: the
    /// vertices of the part are eliminated in minimum-degree order on the
    /// elimination graph of the part, the neighbours outside it (separators,
    /// eliminated later) counting in the degrees but never eliminated.
    /// </summary>
    internal static List<int> MinimumDegreeLeaf(List<int>[] adj, int[] part, int id, List<int> verts)
    {
        int m = verts.Count;
        var local = new Dictionary<int, int>(m);
        for (int i = 0; i < m; i++) local[verts[i]] = i;
        var g = new HashSet<int>[m];       // local elimination graph (local indices)
        var ext = new int[m];              // outside neighbours
        for (int i = 0; i < m; i++)
        {
            g[i] = new HashSet<int>();
            foreach (int u in adj[verts[i]])
            {
                if (local.TryGetValue(u, out int j)) g[i].Add(j);
                else if (part[u] != id) ext[i]++;
            }
        }
        var deg = new int[m];
        for (int i = 0; i < m; i++) deg[i] = g[i].Count + ext[i];
        var buckets = new SortedDictionary<int, SortedSet<int>>();
        void Put(int v) { if (!buckets.TryGetValue(deg[v], out var b)) buckets[deg[v]] = b = new SortedSet<int>(); b.Add(v); }
        void Take(int v) { var b = buckets[deg[v]]; b.Remove(v); if (b.Count == 0) buckets.Remove(deg[v]); }
        for (int i = 0; i < m; i++) Put(i);
        var order = new List<int>(m);
        var nb = new List<int>();
        for (int k = 0; k < m; k++)
        {
            int v = buckets.First().Value.Min;
            Take(v);
            order.Add(verts[v]);
            nb.Clear(); nb.AddRange(g[v]);
            foreach (int u in nb) g[u].Remove(v);
            for (int a = 0; a < nb.Count; a++)
                for (int b = a + 1; b < nb.Count; b++) { g[nb[a]].Add(nb[b]); g[nb[b]].Add(nb[a]); }
            foreach (int u in nb)
            {
                // an eliminated vertex's outside neighbours become outside neighbours of the clique (approximately: the count is kept as an upper bound)
                int d = g[u].Count + ext[u];
                if (d != deg[u]) { Take(u); deg[u] = d; Put(u); }
            }
            g[v].Clear();
        }
        return order;
    }

    /// <summary>
    /// Numeric factorization of the matrix's current values plus
    /// diagonalShift (per scalar unknown, may be null). Returns false — and
    /// leaves no usable factor — when a block pivot is not positive definite
    /// (a scalar pivot of its own LDLᵀ at or below minPivotRatio × the largest
    /// diagonal entry), which is what a mechanism's singular system or an
    /// indefinite one does.
    /// </summary>
    public bool Factor(double[]? diagonalShift = null, double minPivotRatio = 1e-12)
    {
        int nb = _nb;
        _factored = false;
        if (diagonalShift != null && diagonalShift.Length != N) throw new ArgumentException("Shift length mismatch.", nameof(diagonalShift));
        var av = _a.Values;
        double maxDiag = 0;
        for (int c = 0; c < N; c++)
        {
            double d = _a.Diagonal(c) + (diagonalShift?[c] ?? 0.0);
            maxDiag = Math.Max(maxDiag, Math.Abs(d));
        }
        if (nb == 0) { _factored = true; return true; }
        if (maxDiag <= 0) return false;
        double minPivot = minPivotRatio * maxDiag;

        var y = _y; var lx = _lx; var li = _li; var lp = _lp; var lnz = _lnz;
        var flag = _flag; var parent = _parent; var pattern = _pattern; var dinv = _dinv;
        Array.Clear(y, 0, y.Length);
        var dk = new double[9];
        var yi = new double[9];
        var lki = new double[9];
        for (int k = 0; k < nb; k++)
        {
            int top = nb;
            flag[k] = k;
            lnz[k] = 0;
            // scatter column k of the permuted matrix: Y_i = A_perm(k, i) for i ≤ k, and the pattern of row k
            for (int p = _ap[k]; p < _ap[k + 1]; p++)
            {
                int i = _ai[p];
                int s = 9 * _aslot[p], yo = 9 * i;
                if (_adirect[p] || i == k)
                    for (int e = 0; e < 9; e++) y[yo + e] += av[s + e];
                else
                {
                    y[yo] += av[s]; y[yo + 1] += av[s + 3]; y[yo + 2] += av[s + 6];
                    y[yo + 3] += av[s + 1]; y[yo + 4] += av[s + 4]; y[yo + 5] += av[s + 7];
                    y[yo + 6] += av[s + 2]; y[yo + 7] += av[s + 5]; y[yo + 8] += av[s + 8];
                }
                if (i >= k) continue;
                int len;
                for (len = 0; flag[i] != k; i = parent[i])
                {
                    pattern[len++] = i;
                    flag[i] = k;
                }
                while (len > 0) pattern[--top] = pattern[--len];
            }
            // D_k starts as A_kk (+ shift)
            int yk = 9 * k;
            for (int e = 0; e < 9; e++) { dk[e] = y[yk + e]; y[yk + e] = 0.0; }
            if (diagonalShift != null)
            {
                int b0 = 3 * _perm[k];
                dk[0] += diagonalShift[b0]; dk[4] += diagonalShift[b0 + 1]; dk[8] += diagonalShift[b0 + 2];
            }
            // block triangular solve along the pattern (topological order of the elimination tree)
            for (; top < nb; top++)
            {
                int i = pattern[top];
                int yo = 9 * i;
                for (int e = 0; e < 9; e++) { yi[e] = y[yo + e]; y[yo + e] = 0.0; }
                int p2 = lp[i] + lnz[i];
                for (int p = lp[i]; p < p2; p++) SubtractProductTransposed(y, 9 * li[p], yi, lx, 9 * p);   // Y_r −= Y_i L_riᵀ
                SolveRight(yi, dinv, 9 * i, lki);                                                           // L_ki = Y_i D_i⁻¹
                SubtractProductTransposed(dk, 0, lki, yi, 0);                                               // D_k −= L_ki Y_iᵀ
                li[p2] = k;
                Array.Copy(lki, 0, lx, 9 * p2, 9);
                lnz[i]++;
            }
            // D_k symmetric by construction up to round-off; its inverse through its own LDLᵀ (the pivot check)
            double s01 = 0.5 * (dk[1] + dk[3]), s02 = 0.5 * (dk[2] + dk[6]), s12 = 0.5 * (dk[5] + dk[7]);
            double a00 = dk[0], a11 = dk[4], a22 = dk[8];
            double d1 = a00;
            if (double.IsNaN(d1) || d1 <= minPivot) return false;
            double l10 = s01 / d1, l20 = s02 / d1;
            double d2 = a11 - l10 * s01;
            if (double.IsNaN(d2) || d2 <= minPivot) return false;
            double l21 = (s12 - l20 * s01) / d2;
            double d3 = a22 - l20 * s02 - l21 * (s12 - l20 * s01);
            if (double.IsNaN(d3) || d3 <= minPivot) return false;
            // D_k = Λ Δ Λᵀ (unit lower Λ, diagonal Δ) kept as the six numbers; solves go through the factors
            // rather than an explicit inverse (backward stable: Higham 2002 ch. 14)
            int o = 9 * k;
            dinv[o] = d1; dinv[o + 1] = d2; dinv[o + 2] = d3; dinv[o + 3] = l10; dinv[o + 4] = l20; dinv[o + 5] = l21;
        }
        _factored = true;
        return true;
    }

    /// <summary>Solves A x = b with the last successful factorization (b is not modified).</summary>
    public double[] Solve(double[] b)
    {
        if (!_factored) throw new InvalidOperationException("No factorization: Factor returned false or was not called.");
        if (b.Length != N) throw new ArgumentException("Right-hand side length mismatch.", nameof(b));
        int nb = _nb;
        var x = new double[N];
        for (int k = 0; k < nb; k++) { int o = 3 * _perm[k]; x[3 * k] = b[o]; x[3 * k + 1] = b[o + 1]; x[3 * k + 2] = b[o + 2]; }
        // forward: L z = x (unit block lower; column j holds L_rj for r > j)
        for (int j = 0; j < nb; j++)
        {
            double x0 = x[3 * j], x1 = x[3 * j + 1], x2 = x[3 * j + 2];
            for (int p = _lp[j]; p < _lp[j + 1]; p++)
            {
                int r = 3 * _li[p], l = 9 * p;
                x[r] -= _lx[l] * x0 + _lx[l + 1] * x1 + _lx[l + 2] * x2;
                x[r + 1] -= _lx[l + 3] * x0 + _lx[l + 4] * x1 + _lx[l + 5] * x2;
                x[r + 2] -= _lx[l + 6] * x0 + _lx[l + 7] * x1 + _lx[l + 8] * x2;
            }
        }
        // diagonal: x_j = D_j⁻¹ x_j through Λ Δ Λᵀ
        for (int j = 0; j < nb; j++)
        {
            int o = 9 * j, r = 3 * j;
            double d1 = _dinv[o], d2 = _dinv[o + 1], d3 = _dinv[o + 2], l10 = _dinv[o + 3], l20 = _dinv[o + 4], l21 = _dinv[o + 5];
            double z0 = x[r], z1 = x[r + 1] - l10 * z0, z2 = x[r + 2] - l20 * z0 - l21 * z1;
            z0 /= d1; z1 /= d2; z2 /= d3;
            z1 -= l21 * z2; z0 -= l10 * z1 + l20 * z2;
            x[r] = z0; x[r + 1] = z1; x[r + 2] = z2;
        }
        // backward: Lᵀ x = z
        for (int j = nb - 1; j >= 0; j--)
        {
            double s0 = x[3 * j], s1 = x[3 * j + 1], s2 = x[3 * j + 2];
            for (int p = _lp[j]; p < _lp[j + 1]; p++)
            {
                int r = 3 * _li[p], l = 9 * p;
                double v0 = x[r], v1 = x[r + 1], v2 = x[r + 2];
                s0 -= _lx[l] * v0 + _lx[l + 3] * v1 + _lx[l + 6] * v2;
                s1 -= _lx[l + 1] * v0 + _lx[l + 4] * v1 + _lx[l + 7] * v2;
                s2 -= _lx[l + 2] * v0 + _lx[l + 5] * v1 + _lx[l + 8] * v2;
            }
            x[3 * j] = s0; x[3 * j + 1] = s1; x[3 * j + 2] = s2;
        }
        var o2 = new double[N];
        for (int k = 0; k < nb; k++) { int o = 3 * _perm[k]; o2[o] = x[3 * k]; o2[o + 1] = x[3 * k + 1]; o2[o + 2] = x[3 * k + 2]; }
        return o2;
    }

    /// <summary>T(target) −= P Qᵀ for 3 × 3 row-major blocks (Q at qo in q, P at 0 in p).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SubtractProductTransposed(double[] t, int to, double[] p, double[] q, int qo)
    {
        for (int i = 0; i < 3; i++)
        {
            double p0 = p[3 * i], p1 = p[3 * i + 1], p2 = p[3 * i + 2];
            for (int j = 0; j < 3; j++)
                t[to + 3 * i + j] -= p0 * q[qo + 3 * j] + p1 * q[qo + 3 * j + 1] + p2 * q[qo + 3 * j + 2];
        }
    }

    /// <summary>R = P D⁻¹ for a 3 × 3 row-major block P and D = Λ Δ Λᵀ stored at o in f (d1, d2, d3, l10, l20, l21): each row of P solved through the factors.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SolveRight(double[] p, double[] f, int o, double[] r)
    {
        double d1 = f[o], d2 = f[o + 1], d3 = f[o + 2], l10 = f[o + 3], l20 = f[o + 4], l21 = f[o + 5];
        for (int i = 0; i < 3; i++)
        {
            // row vector x with x D = p_i  ⇔  D xᵀ = p_iᵀ (D symmetric)
            double z0 = p[3 * i], z1 = p[3 * i + 1] - l10 * z0, z2 = p[3 * i + 2] - l20 * z0 - l21 * z1;
            z0 /= d1; z1 /= d2; z2 /= d3;
            z1 -= l21 * z2; z0 -= l10 * z1 + l20 * z2;
            r[3 * i] = z0; r[3 * i + 1] = z1; r[3 * i + 2] = z2;
        }
    }
}

/// <summary>
/// The normal equations JᵀJ δ = −Jᵀ r of a least-squares problem whose
/// unknowns come in blocks of three, assembled from the Jacobian's rows
/// (column index and value lists) into a <see cref="BlockSymmetricMatrix3"/>
/// through a map from the Jacobian pattern to the matrix positions that is
/// built once and reused while the pattern lasts; the <see cref="BlockLdl3"/>
/// of the matrix is analysed at the same time. When a row's columns change
/// (a one-sided contact that switches on or off) only the map is rebuilt if
/// the block pattern still covers it, otherwise the matrix and the factor
/// are analysed again.
/// </summary>
public sealed class BlockNormalEquations3
{
    private readonly int _n;
    private int[] _rowStart = Array.Empty<int>();
    private int[] _cols = Array.Empty<int>();
    private int[] _pos = Array.Empty<int>();
    private int[] _mirror = Array.Empty<int>();

    /// <summary>JᵀJ (valid after <see cref="Assemble"/>).</summary>
    public BlockSymmetricMatrix3? Matrix { get; private set; }

    /// <summary>The factorization object bound to <see cref="Matrix"/>'s pattern.</summary>
    public BlockLdl3? Factor { get; private set; }

    /// <summary>Number of times the block pattern was analysed.</summary>
    public int Analyses { get; private set; }

    /// <summary>Number of times the row map was rebuilt without a new analysis.</summary>
    public int Remaps { get; private set; }

    /// <summary>Numeric factorizations attempted through <see cref="Factorize"/>.</summary>
    public int Factorizations { get; private set; }

    private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

    /// <summary>Milliseconds spent assembling, factoring and solving.</summary>
    public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;

    public BlockNormalEquations3(int unknowns)
    {
        if (unknowns < 0 || unknowns % 3 != 0) throw new ArgumentException("The number of unknowns must be a multiple of 3.", nameof(unknowns));
        _n = unknowns;
    }

    /// <summary>
    /// Assembles JᵀJ into <see cref="Matrix"/> and the gradient Jᵀ r into
    /// gradient (length = unknowns, overwritten). Returns the largest
    /// diagonal entry of JᵀJ.
    /// </summary>
    public double Assemble(IReadOnlyList<List<int>> rowsCols, IReadOnlyList<List<double>> rowsVals, double[] residuals, double[] gradient)
    {
        _clock.Start();
        try { return AssembleCore(rowsCols, rowsVals, residuals, gradient); }
        finally { _clock.Stop(); }
    }

    /// <summary>Factors the assembled matrix plus the diagonal shift (see <see cref="BlockLdl3.Factor"/>).</summary>
    public bool Factorize(double[]? diagonalShift = null, double minPivotRatio = 1e-12)
    {
        if (Factor == null) throw new InvalidOperationException("Assemble first.");
        _clock.Start();
        try { Factorizations++; return Factor.Factor(diagonalShift, minPivotRatio); }
        finally { _clock.Stop(); }
    }

    /// <summary>Solves with the last successful factorization.</summary>
    public double[] Solve(double[] rhs)
    {
        if (Factor == null) throw new InvalidOperationException("Assemble first.");
        _clock.Start();
        try { return Factor.Solve(rhs); }
        finally { _clock.Stop(); }
    }

    private double AssembleCore(IReadOnlyList<List<int>> rowsCols, IReadOnlyList<List<double>> rowsVals, double[] residuals, double[] gradient)
    {
        int m = rowsCols.Count;
        if (rowsVals.Count != m || residuals.Length != m) throw new ArgumentException("Row count mismatch.");
        if (gradient.Length != _n) throw new ArgumentException("Gradient length mismatch.", nameof(gradient));
        if (!SamePattern(rowsCols)) Remap(rowsCols);
        var a = Matrix!;
        a.Clear();
        Array.Clear(gradient, 0, gradient.Length);
        var av = a.Values;
        int t = 0;
        for (int i = 0; i < m; i++)
        {
            var cols = rowsCols[i]; var vals = rowsVals[i];
            double ri = residuals[i];
            int len = cols.Count;
            for (int p = 0; p < len; p++)
            {
                double vp = vals[p];
                gradient[cols[p]] += vp * ri;
                for (int q = p; q < len; q++)
                {
                    double v = vp * vals[q];
                    av[_pos[t]] += v;
                    int mr = _mirror[t];
                    if (mr >= 0) av[mr] += v;
                    t++;
                }
            }
        }
        double maxDiag = 0;
        for (int c = 0; c < _n; c++) maxDiag = Math.Max(maxDiag, a.Diagonal(c));
        return maxDiag;
    }

    private bool SamePattern(IReadOnlyList<List<int>> rowsCols)
    {
        if (Matrix == null || _rowStart.Length != rowsCols.Count + 1) return false;
        for (int i = 0; i < rowsCols.Count; i++)
        {
            var cols = rowsCols[i];
            int s = _rowStart[i];
            if (_rowStart[i + 1] - s != cols.Count) return false;
            for (int p = 0; p < cols.Count; p++) if (_cols[s + p] != cols[p]) return false;
        }
        return true;
    }

    private void Remap(IReadOnlyList<List<int>> rowsCols)
    {
        int m = rowsCols.Count;
        _rowStart = new int[m + 1];
        long pairs = 0;
        for (int i = 0; i < m; i++)
        {
            int len = rowsCols[i].Count;
            _rowStart[i + 1] = _rowStart[i] + len;
            pairs += (long)len * (len + 1) / 2;
        }
        if (pairs > int.MaxValue - 16) throw new InvalidOperationException("Jacobian too large.");
        _cols = new int[_rowStart[m]];
        for (int i = 0; i < m; i++) rowsCols[i].CopyTo(_cols, _rowStart[i]);
        _pos = new int[pairs];
        _mirror = new int[pairs];
        // try the existing pattern first; analyse again only when a pair falls outside it
        if (Matrix == null || !TryMap(rowsCols))
        {
            var blockPairs = new HashSet<(int, int)>();
            for (int i = 0; i < m; i++)
            {
                var cols = rowsCols[i];
                for (int p = 0; p < cols.Count; p++)
                    for (int q = p; q < cols.Count; q++)
                    {
                        if (cols[p] < 0 || cols[p] >= _n || cols[q] < 0 || cols[q] >= _n) throw new ArgumentOutOfRangeException(nameof(rowsCols), "Column index out of range.");
                        int br = cols[p] / 3, bc = cols[q] / 3;
                        if (br != bc) blockPairs.Add((Math.Min(br, bc), Math.Max(br, bc)));
                    }
            }
            Matrix = new BlockSymmetricMatrix3(_n / 3, blockPairs);
            Factor = new BlockLdl3(Matrix);
            Analyses++;
            if (!TryMap(rowsCols)) throw new InvalidOperationException("Pattern analysis failed.");
        }
        else Remaps++;
    }

    private bool TryMap(IReadOnlyList<List<int>> rowsCols)
    {
        var a = Matrix!;
        int t = 0;
        for (int i = 0; i < rowsCols.Count; i++)
        {
            var cols = rowsCols[i];
            for (int p = 0; p < cols.Count; p++)
                for (int q = p; q < cols.Count; q++)
                {
                    var (pos, mirror) = a.Positions(cols[p], cols[q]);
                    if (pos < 0) return false;
                    _pos[t] = pos; _mirror[t] = mirror; t++;
                }
        }
        return true;
    }
}
