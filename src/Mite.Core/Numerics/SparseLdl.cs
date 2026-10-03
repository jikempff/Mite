// Sparse LDLᵀ after T. A. Davis, "Algorithm 849: A concise sparse Cholesky
// factorization package" (ACM TOMS 31(4), 2005): elimination tree, row
// counts and an up-looking numeric factorization. Fill-reducing ordering by
// automatic nested dissection on BFS level structures (A. George, "Nested
// dissection of a regular finite element mesh", SIAM J. Numer. Anal. 10,
// 1973; George & Liu, "Computer Solution of Large Sparse Positive Definite
// Systems", 1981, ch. 8).
using System;
using System.Collections.Generic;

namespace Mite.Core.Numerics;

/// <summary>
/// General sparse LDLᵀ for symmetric positive-definite systems whose graph
/// is a surface mesh with several unknowns per vertex (the Gauss–Newton
/// systems of the AAG optimisation and its refinement). Unlike the envelope
/// factorisation of <see cref="SparseSymmetricSolver"/>, whose cost grows
/// with the square of the reordered bandwidth (the perimeter of a disc-like
/// net), nested dissection keeps the fill near n log n and the work near
/// n^1.5 on such graphs.
/// </summary>
public sealed class SparseLdl
{
    private readonly int _n;
    private readonly int[] _perm, _invPerm;
    private readonly int[] _lp, _li;
    private readonly double[] _lx, _d;

    /// <summary>Stored entries of L (strictly lower part).</summary>
    public long FactorNonZeros => _lx.LongLength;

    private SparseLdl(int n, int[] perm, int[] invPerm, int[] lp, int[] li, double[] lx, double[] d)
    {
        _n = n; _perm = perm; _invPerm = invPerm; _lp = lp; _li = li; _lx = lx; _d = d;
    }

    /// <summary>
    /// Factors the matrix held by <paramref name="a"/> (upper triangle). Returns
    /// null when a pivot is not positive (or below minPivotRatio × the largest
    /// diagonal entry).
    /// </summary>
    public static SparseLdl? Factor(SparseSymmetricSolver.Builder a, double minPivotRatio = 1e-12)
    {
        int n = a.N;
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();
        double maxDiag = 0;
        for (int i = 0; i < n; i++)
            foreach (var kv in a.Rows[i])
            {
                if (kv.Key == i) { maxDiag = Math.Max(maxDiag, Math.Abs(kv.Value)); continue; }
                adj[i].Add(kv.Key); adj[kv.Key].Add(i);
            }
        if (n == 0) return new SparseLdl(0, Array.Empty<int>(), Array.Empty<int>(), new[] { 0 }, Array.Empty<int>(), Array.Empty<double>(), Array.Empty<double>());
        if (maxDiag <= 0) return null;
        int[] perm = NestedDissection(adj);
        var inv = new int[n];
        for (int k = 0; k < n; k++) inv[perm[k]] = k;

        // permuted upper triangle by columns: column c holds rows r ≤ c
        var cnt = new int[n + 1];
        for (int i = 0; i < n; i++)
            foreach (var kv in a.Rows[i])
            {
                int r = inv[i], c = inv[kv.Key];
                cnt[Math.Max(r, c) + 1]++;
            }
        for (int k = 0; k < n; k++) cnt[k + 1] += cnt[k];
        var ap = (int[])cnt.Clone();
        var ai = new int[ap[n]];
        var ax = new double[ap[n]];
        var fill = (int[])cnt.Clone();
        for (int i = 0; i < n; i++)
            foreach (var kv in a.Rows[i])
            {
                int r = inv[i], c = inv[kv.Key];
                if (r > c) (r, c) = (c, r);
                int p = fill[c]++;
                ai[p] = r; ax[p] = kv.Value;
            }

        // symbolic: elimination tree and column counts of L
        var parent = new int[n];
        var flag = new int[n];
        var lnz = new int[n];
        for (int k = 0; k < n; k++)
        {
            parent[k] = -1; flag[k] = k; lnz[k] = 0;
            for (int p = ap[k]; p < ap[k + 1]; p++)
            {
                int i = ai[p];
                if (i >= k) continue;
                for (; flag[i] != k; i = parent[i])
                {
                    if (parent[i] == -1) parent[i] = k;
                    lnz[i]++;
                    flag[i] = k;
                }
            }
        }
        var lp = new int[n + 1];
        for (int k = 0; k < n; k++)
        {
            long next = (long)lp[k] + lnz[k];
            if (next > int.MaxValue - 8) throw new InvalidOperationException("System too large for the sparse factorization.");
            lp[k + 1] = (int)next;
        }
        var li = new int[lp[n]];
        var lx = new double[lp[n]];
        var d = new double[n];

        // numeric: up-looking, row k of L from a sparse triangular solve
        var y = new double[n];
        var pattern = new int[n];
        double minPivot = minPivotRatio * maxDiag;
        for (int k = 0; k < n; k++)
        {
            y[k] = 0.0;
            int top = n;
            flag[k] = k;
            lnz[k] = 0;
            for (int p = ap[k]; p < ap[k + 1]; p++)
            {
                int i = ai[p];
                y[i] += ax[p];
                int len;
                for (len = 0; flag[i] != k; i = parent[i])
                {
                    pattern[len++] = i;
                    flag[i] = k;
                }
                while (len > 0) pattern[--top] = pattern[--len];
            }
            d[k] = y[k];
            y[k] = 0.0;
            for (; top < n; top++)
            {
                int i = pattern[top];
                double yi = y[i];
                y[i] = 0.0;
                int p2 = lp[i] + lnz[i];
                for (int p = lp[i]; p < p2; p++) y[li[p]] -= lx[p] * yi;
                double lki = yi / d[i];
                d[k] -= lki * yi;
                li[p2] = k;
                lx[p2] = lki;
                lnz[i]++;
            }
            if (double.IsNaN(d[k]) || d[k] <= minPivot) return null;
        }
        return new SparseLdl(n, perm, inv, lp, li, lx, d);
    }

    /// <summary>Solves A x = b (b is not modified).</summary>
    public double[] Solve(double[] b)
    {
        if (b.Length != _n) throw new ArgumentException("Right-hand side length mismatch.", nameof(b));
        var x = new double[_n];
        for (int k = 0; k < _n; k++) x[k] = b[_perm[k]];
        for (int j = 0; j < _n; j++)
        {
            double xj = x[j];
            for (int p = _lp[j]; p < _lp[j + 1]; p++) x[_li[p]] -= _lx[p] * xj;
        }
        for (int j = 0; j < _n; j++) x[j] /= _d[j];
        for (int j = _n - 1; j >= 0; j--)
        {
            double s = x[j];
            for (int p = _lp[j]; p < _lp[j + 1]; p++) s -= _lx[p] * x[_li[p]];
            x[j] = s;
        }
        var o = new double[_n];
        for (int k = 0; k < _n; k++) o[_perm[k]] = x[k];
        return o;
    }

    /// <summary>
    /// Nested dissection ordering (new index → old index): each connected
    /// part is split by the middle level of a breadth-first level structure
    /// rooted at a pseudo-peripheral vertex; the two halves are ordered first
    /// (recursively), the separator last. Parts below 64 vertices keep their
    /// breadth-first order.
    /// </summary>
    internal static int[] NestedDissection(List<int>[] adj)
    {
        int n = adj.Length;
        var order = new int[n];
        int next = 0;
        var part = new int[n];           // current part id of every vertex
        var level = new int[n];
        int partCounter = 0;
        for (int i = 0; i < n; i++) part[i] = 0;
        var stack = new Stack<(int Id, List<int> Vertices, bool Emit)>();
        var all = new List<int>(n);
        for (int i = 0; i < n; i++) all.Add(i);
        stack.Push((0, all, false));
        var queue = new List<int>();
        while (stack.Count > 0)
        {
            var (id, verts, emit) = stack.Pop();
            if (emit || verts.Count <= 64)
            {
                if (!emit && verts.Count > 1) verts = BfsOrder(adj, part, id, verts, level, queue);
                foreach (int v in verts) order[next++] = v;
                continue;
            }
            // connected components of the part
            var comps = new List<List<int>>();
            var seen = new HashSet<int>();
            foreach (int s in verts)
            {
                if (seen.Contains(s)) continue;
                var comp = new List<int> { s };
                seen.Add(s);
                for (int h = 0; h < comp.Count; h++)
                    foreach (int u in adj[comp[h]])
                        if (part[u] == id && seen.Add(u)) comp.Add(u);
                comps.Add(comp);
            }
            if (comps.Count > 1)
            {
                foreach (var comp in comps)
                {
                    int cid = ++partCounter;
                    foreach (int v in comp) part[v] = cid;
                    stack.Push((cid, comp, false));
                }
                continue;
            }
            // level structure from a pseudo-peripheral vertex
            int root = verts[0];
            int depth = Levels(adj, part, id, root, level, queue);
            for (int pass = 0; pass < 4; pass++)
            {
                int far = queue[queue.Count - 1];
                foreach (int v in queue) if (level[v] == depth && adj[v].Count < adj[far].Count) far = v;
                int d2 = Levels(adj, part, id, far, level, queue);
                if (d2 <= depth) { if (d2 == depth) root = far; break; }
                depth = d2; root = far;
            }
            depth = Levels(adj, part, id, root, level, queue);
            if (depth < 2)
            {
                stack.Push((id, verts, true));
                continue;
            }
            var count = new int[depth + 1];
            foreach (int v in queue) count[level[v]]++;
            int half = verts.Count / 2, acc = 0, mid = 1;
            for (int l = 0; l <= depth; l++) { acc += count[l]; if (acc >= half) { mid = l; break; } }
            mid = Math.Max(1, Math.Min(depth - 1, mid));
            var lo = new List<int>(); var hi = new List<int>(); var sep = new List<int>();
            foreach (int v in queue)
            {
                if (level[v] < mid) lo.Add(v);
                else if (level[v] > mid) hi.Add(v);
                else sep.Add(v);
            }
            // a separator vertex with no neighbour beyond the separator joins the lower half
            var sepKeep = new List<int>();
            foreach (int v in sep)
            {
                bool touchesHi = false;
                foreach (int u in adj[v]) if (part[u] == id && level[u] == mid + 1) { touchesHi = true; break; }
                if (touchesHi) sepKeep.Add(v); else lo.Add(v);
            }
            int idLo = ++partCounter, idHi = ++partCounter, idSep = ++partCounter;
            foreach (int v in lo) part[v] = idLo;
            foreach (int v in hi) part[v] = idHi;
            foreach (int v in sepKeep) part[v] = idSep;
            // stack: separator emitted last → pushed first
            stack.Push((idSep, sepKeep, true));
            stack.Push((idHi, hi, false));
            stack.Push((idLo, lo, false));
        }
        return order;
    }

    private static int Levels(List<int>[] adj, int[] part, int id, int root, int[] level, List<int> queue)
    {
        queue.Clear();
        queue.Add(root);
        var mark = new HashSet<int> { root };
        level[root] = 0;
        int depth = 0;
        for (int h = 0; h < queue.Count; h++)
        {
            int v = queue[h];
            foreach (int u in adj[v])
            {
                if (part[u] != id || !mark.Add(u)) continue;
                level[u] = level[v] + 1;
                depth = Math.Max(depth, level[u]);
                queue.Add(u);
            }
        }
        return depth;
    }

    private static List<int> BfsOrder(List<int>[] adj, int[] part, int id, List<int> verts, int[] level, List<int> queue)
    {
        var o = new List<int>(verts.Count);
        var mark = new HashSet<int>();
        foreach (int s in verts)
        {
            if (mark.Contains(s)) continue;
            Levels(adj, part, id, s, level, queue);
            foreach (int v in queue) if (mark.Add(v)) o.Add(v);
        }
        return o;
    }
}
