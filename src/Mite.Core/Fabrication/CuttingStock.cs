using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Fabrication;

/// <summary>
/// One-dimensional cutting stock: the pieces of <see cref="LathSegmentation"/>
/// (every piece at most one stock length) are cut from as few stock lengths
/// as possible, each saw cut costing a kerf.
///
/// The packing is first-fit decreasing (Johnson 1973, "Near-optimal bin
/// packing algorithms", MIT thesis): sort the pieces longest first and put
/// each into the first board with room, opening a new board when none has.
/// Its worst case is FFD ≤ 11/9 · OPT + 6/9 boards, and that bound is tight
/// (Dósa 2007, "The tight bound of first fit decreasing bin-packing algorithm
/// is FFD(I) ≤ 11/9 OPT(I) + 6/9", ESCAPE 2007); in practice it is optimal or
/// one board over on lath pieces, which are few per board. The optimum is
/// reported through a lower bound, so the material figure can be read as
/// "within n boards of the best possible" — the classic LP relaxation of the
/// cutting stock problem is Gilmore &amp; Gomory 1961 (column generation),
/// which is overkill for a few dozen laths; <see cref="Optimal"/> gives the
/// exact answer by branch and bound for small lists (tests, small kits).
///
/// Kerf: a board holding m pieces spends (m − 1) kerfs, so a piece is packed
/// as (length + kerf) into a capacity of (stock + kerf) — the standard
/// reduction.
/// </summary>
public static class CuttingStock
{
    public sealed class Result
    {
        /// <summary>Pieces per board, by index into the input list, in the order they were placed.</summary>
        public List<List<int>> Boards { get; } = new();
        /// <summary>Board index per piece; −1 for a piece longer than the stock.</summary>
        public int[] BoardOf { get; internal set; } = Array.Empty<int>();
        /// <summary>Leftover length per board after the pieces and the kerfs between them.</summary>
        public double[] Offcuts { get; internal set; } = Array.Empty<double>();
        /// <summary>Pieces longer than the stock (not packed).</summary>
        public int TooLong { get; internal set; }
        public double StockLength { get; internal set; }
        public double Kerf { get; internal set; }
        /// <summary>Lower bound on the number of boards any packing needs (see <see cref="CuttingStock.LowerBound"/>).</summary>
        public int LowerBound { get; internal set; }
        /// <summary>Σ piece lengths of the packed pieces.</summary>
        public double PackedLength { get; internal set; }
        /// <summary>Σ piece lengths / (boards × stock): the share of the bought material that ends up in the laths.</summary>
        public double Material => Boards.Count == 0 ? 0 : PackedLength / (Boards.Count * StockLength);
        /// <summary>Total offcut (the kerfs are counted as waste too: boards × stock − Σ pieces).</summary>
        public double Waste => Boards.Count * StockLength - PackedLength;
        /// <summary>The FFD guarantee for this instance: the optimum needs at least ⌈(boards − 6/9) · 9/11⌉ boards.</summary>
        public int GuaranteedOptimumAtLeast => Math.Max(LowerBound, (int)Math.Ceiling((Boards.Count - 6.0 / 9.0) * 9.0 / 11.0 - 1e-9));
    }

    /// <summary>
    /// Lower bound on the boards needed: the larger of the material bound
    /// L₁ = ⌈Σ(lᵢ + kerf) / (stock + kerf)⌉ and Martello &amp; Toth's L₂ (Knapsack
    /// Problems, 1990, §8.3): for a threshold α ≤ C/2 the pieces longer than
    /// C − α take a board each, the pieces in (C/2, C − α] take a board each
    /// too, and what is left of their boards is all the room the pieces in
    /// [α, C/2] can share — L₂(α) = |N₁| + |N₂| + ⌈(Σ_{N₃} sᵢ − (|N₂| C − Σ_{N₂} sᵢ)) / C⌉⁺,
    /// maximised over the piece sizes as α. Sizes are lᵢ + kerf against
    /// C = stock + kerf. Pieces longer than the stock are ignored.
    /// </summary>
    public static int LowerBound(IReadOnlyList<double> lengths, double stockLength, double kerf = 0.0)
    {
        if (stockLength <= 0) throw new ArgumentException("Stock length must be positive.", nameof(stockLength));
        kerf = Math.Max(0.0, kerf);
        double C = stockLength + kerf;
        var sizes = lengths.Where(l => l > 0 && l <= stockLength + 1e-12).Select(l => l + kerf).OrderByDescending(x => x).ToArray();
        if (sizes.Length == 0) return 0;
        int bound = (int)Math.Ceiling(sizes.Sum() / C - 1e-9);
        var alphas = sizes.Where(x => x <= 0.5 * C + 1e-12).Distinct().ToList();
        alphas.Add(0.0);
        foreach (double alpha in alphas)
        {
            int n1 = 0, n2 = 0; double sum2 = 0, sum3 = 0;
            foreach (double x in sizes)
            {
                if (x > C - alpha + 1e-12) n1++;
                else if (x > 0.5 * C + 1e-12) { n2++; sum2 += x; }
                else if (x >= alpha - 1e-12) sum3 += x;
            }
            double spare = Math.Max(0.0, sum3 - (n2 * C - sum2));
            int l2 = n1 + n2 + (int)Math.Ceiling(spare / C - 1e-9);
            bound = Math.Max(bound, l2);
        }
        return bound;
    }

    /// <summary>First-fit decreasing packing of the pieces into stock lengths.</summary>
    public static Result Pack(IReadOnlyList<double> lengths, double stockLength, double kerf = 0.0)
    {
        if (stockLength <= 0) throw new ArgumentException("Stock length must be positive.", nameof(stockLength));
        kerf = Math.Max(0.0, kerf);
        var r = new Result { StockLength = stockLength, Kerf = kerf };
        int n = lengths.Count;
        var boardOf = new int[n];
        for (int i = 0; i < n; i++) boardOf[i] = -1;

        var order = Enumerable.Range(0, n).Where(i => lengths[i] > 0).OrderByDescending(i => lengths[i]).ThenBy(i => i).ToList();
        double capacity = stockLength + kerf;
        var free = new List<double>();
        double packed = 0;
        foreach (int i in order)
        {
            double size = lengths[i] + kerf;
            if (lengths[i] > stockLength + 1e-12) { r.TooLong++; continue; }
            int b = -1;
            for (int k = 0; k < free.Count; k++) if (free[k] >= size - 1e-12) { b = k; break; }
            if (b < 0) { free.Add(capacity); r.Boards.Add(new List<int>()); b = free.Count - 1; }
            free[b] -= size;
            r.Boards[b].Add(i);
            boardOf[i] = b;
            packed += lengths[i];
        }
        r.BoardOf = boardOf;
        // free = (S + kerf) − Σ(lᵢ + kerf) = S − Σlᵢ − (m − 1)·kerf: the board's true leftover
        r.Offcuts = free.Select(f => Math.Max(0.0, f)).ToArray();
        r.PackedLength = packed;
        r.LowerBound = LowerBound(lengths, stockLength, kerf);
        return r;
    }

    /// <summary>
    /// Exact minimum number of boards by depth-first branch and bound over the
    /// pieces in decreasing order (each piece goes into one of the open boards
    /// with room, or a new one; boards with equal remaining room are tried
    /// once). Exponential in the worst case — meant for lists of a few dozen
    /// pieces; <paramref name="maxNodes"/> caps the search and the method then
    /// returns the best packing found (an upper bound, flagged by
    /// <paramref name="exact"/> = false).
    /// </summary>
    public static int Optimal(IReadOnlyList<double> lengths, double stockLength, double kerf, out bool exact, long maxNodes = 2_000_000)
    {
        if (stockLength <= 0) throw new ArgumentException("Stock length must be positive.", nameof(stockLength));
        kerf = Math.Max(0.0, kerf);
        var items = lengths.Where(l => l > 0 && l <= stockLength + 1e-12).Select(l => l + kerf).OrderByDescending(l => l).ToArray();
        double capacity = stockLength + kerf;
        int lower = LowerBound(lengths, stockLength, kerf);
        int best = Pack(lengths, stockLength, kerf).Boards.Count;
        exact = true;
        if (best <= lower || items.Length == 0) return best;

        var free = new List<double>();
        long nodes = 0;
        bool aborted = false;
        double sumRest = items.Sum();

        void Search(int idx, double rest)
        {
            if (aborted) return;
            if (++nodes > maxNodes) { aborted = true; return; }
            if (free.Count >= best) return;
            if (idx == items.Length) { best = free.Count; return; }
            // material bound on the boards still needed
            double room = 0; foreach (double f in free) room += f;
            int need = (int)Math.Ceiling((rest - room) / capacity - 1e-9);
            if (free.Count + Math.Max(0, need) >= best) return;
            double size = items[idx];
            // existing boards, each distinct remaining room once
            var tried = new HashSet<long>();
            for (int b = 0; b < free.Count; b++)
            {
                if (free[b] < size - 1e-12) continue;
                long key = (long)Math.Round(free[b] * 1e9);
                if (!tried.Add(key)) continue;
                free[b] -= size;
                Search(idx + 1, rest - size);
                free[b] += size;
                if (aborted || best <= lower) return;
            }
            // a new board
            if (free.Count + 1 < best)
            {
                free.Add(capacity - size);
                Search(idx + 1, rest - size);
                free.RemoveAt(free.Count - 1);
            }
        }

        Search(0, sumRest);
        exact = !aborted;
        return best;
    }
}
