using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Pieces into stock lengths (first-fit decreasing with its 11/9 guarantee)
/// and the balanced segmentation of a lath — checked against exact optima
/// (branch and bound) and against closed forms.
/// </summary>
public class CuttingStockTests
{
    private static Vec3d[] StraightLath(double length, int n = 40)
    {
        var p = new Vec3d[n + 1];
        for (int i = 0; i <= n; i++) p[i] = new Vec3d(length * i / n, 0, 0);
        return p;
    }

    private static double Arc(Vec3d[] l) { double s = 0; for (int i = 1; i < l.Length; i++) s += (l[i] - l[i - 1]).Length; return s; }

    [Fact]
    public void Ffd_PacksTheTextbookInstanceOptimally_AndReportsTheMaterial()
    {
        // 7 pieces into 10-long boards: {7, 3}, {6, 4}, {5, 2, 2}... FFD: 7 | 6 | 5 | then 4 → with 6, 3 → with 7, 2 → with 5, 2 → with 5: 3 boards = Σ 29 / 10 → LB 3
        var r = CuttingStock.Pack(new double[] { 7, 3, 6, 4, 5, 2, 2 }, 10);
        Assert.Equal(3, r.Boards.Count);
        Assert.Equal(3, r.LowerBound);
        Assert.Equal(0, r.TooLong);
        Assert.Equal(29.0, r.PackedLength, 12);
        Assert.Equal(29.0 / 30.0, r.Material, 12);
        Assert.Equal(1.0, r.Waste, 12);
        Assert.Equal(r.Boards.Count, r.Offcuts.Length);
        Assert.Equal(1.0, r.Offcuts.Sum(), 12);
        // every piece on exactly one board, no board over capacity
        var seen = new HashSet<int>();
        for (int b = 0; b < r.Boards.Count; b++)
        {
            double load = 0;
            foreach (int i in r.Boards[b]) { Assert.True(seen.Add(i)); Assert.Equal(b, r.BoardOf[i]); load += new double[] { 7, 3, 6, 4, 5, 2, 2 }[i]; }
            Assert.InRange(load, 0, 10 + 1e-12);
            Assert.Equal(10 - load, r.Offcuts[b], 12);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void Kerf_CostsOneSawCutBetweenPieces_NotAfterTheLast()
    {
        // two 4.9 pieces fit a 10 board without kerf; with a 0.3 kerf they need 4.9 + 0.3 + 4.9 = 10.1 > 10
        Assert.Single(CuttingStock.Pack(new[] { 4.9, 4.9 }, 10).Boards);
        var r = CuttingStock.Pack(new[] { 4.9, 4.9 }, 10, 0.3);
        Assert.Equal(2, r.Boards.Count);
        Assert.Equal(2, r.LowerBound);           // 2 × 4.9 + 0.3 > 10: each is a "big" piece
        Assert.Equal(5.1, r.Offcuts[0], 12);     // one piece per board, no kerf spent
        // three 3 m pieces with a 0.5 kerf: 3 + 0.5 + 3 + 0.5 + 3 = 10 exactly
        var r3 = CuttingStock.Pack(new double[] { 3, 3, 3 }, 10, 0.5);
        Assert.Single(r3.Boards);
        Assert.Equal(0.0, r3.Offcuts[0], 12);
        // Martello–Toth L2 sees what the material bound misses: three pieces over half the stock need three boards
        Assert.Equal(3, CuttingStock.LowerBound(new[] { 0.51, 0.51, 0.51, 0.1 }, 1.0));
        Assert.Equal(2, (int)Math.Ceiling(1.63 - 1e-9));
        Assert.Equal(3, CuttingStock.Pack(new[] { 0.51, 0.51, 0.51, 0.1 }, 1.0).Boards.Count);
        // a piece longer than the stock is reported, not packed
        var r4 = CuttingStock.Pack(new double[] { 12, 3 }, 10);
        Assert.Equal(1, r4.TooLong);
        Assert.Equal(-1, r4.BoardOf[0]);
        Assert.Single(r4.Boards);
    }

    [Fact]
    public void Ffd_WorstCase_IsElevenNinthsOfTheOptimum_Dosa2007()
    {
        // Dósa's family at k = 0, capacity 1, ε small:
        //   4 × (1/2 + ε), 4 × (1/4 + 2ε), 4 × (1/4 + ε), 8 × (1/4 − 2ε)
        // optimum 6 boards: 4 × {1/2+ε, 1/4+ε, 1/4−2ε} + 2 × {2 × (1/4+2ε), 2 × (1/4−2ε)}; FFD takes 8.
        const double e = 1e-3;
        var items = new List<double>();
        for (int i = 0; i < 4; i++) items.Add(0.5 + e);
        for (int i = 0; i < 4; i++) items.Add(0.25 + 2 * e);
        for (int i = 0; i < 4; i++) items.Add(0.25 + e);
        for (int i = 0; i < 8; i++) items.Add(0.25 - 2 * e);
        var r = CuttingStock.Pack(items, 1.0);
        Assert.Equal(8, r.Boards.Count);
        int opt = CuttingStock.Optimal(items, 1.0, 0, out bool exact);
        Assert.True(exact);
        Assert.Equal(6, opt);
        Assert.Equal(6, r.LowerBound);                          // Σ = 6 exactly
        Assert.True(r.Boards.Count <= 11.0 / 9.0 * opt + 6.0 / 9.0 + 1e-9);
        Assert.InRange(r.GuaranteedOptimumAtLeast, 6, 6);     // ⌈(8 − 6/9) · 9/11⌉ = 6
    }

    [Fact]
    public void Ffd_IsWithinTheGuarantee_OnRandomLathPieces_AndUsuallyOptimal()
    {
        var rng = new Random(20261031);
        int equal = 0, trials = 60;
        for (int t = 0; t < trials; t++)
        {
            int n = 6 + rng.Next(10);
            var pieces = new List<double>();
            for (int i = 0; i < n; i++) pieces.Add(0.5 + 5.5 * rng.NextDouble()); // lath pieces up to a 6 m stock
            double kerf = rng.Next(2) == 0 ? 0 : 0.005;
            var r = CuttingStock.Pack(pieces, 6.0, kerf);
            int opt = CuttingStock.Optimal(pieces, 6.0, kerf, out bool exact);
            Assert.True(exact);
            Assert.InRange(opt, r.LowerBound, r.Boards.Count);
            Assert.True(r.Boards.Count <= 11.0 / 9.0 * opt + 6.0 / 9.0 + 1e-9, $"FFD {r.Boards.Count} vs OPT {opt}");
            Assert.True(r.GuaranteedOptimumAtLeast <= opt);
            if (r.Boards.Count == opt) equal++;
            // conservation: every packed length lands once, kerfs between pieces only
            foreach (var board in r.Boards)
            {
                double load = board.Sum(i => pieces[i]) + kerf * (board.Count - 1);
                Assert.InRange(load, 0, 6.0 + 1e-9);
            }
            Assert.Equal(pieces.Sum(), r.PackedLength, 9);
        }
        Assert.True(equal >= trials * 0.9, $"FFD optimal in {equal} of {trials}");
    }

    [Fact]
    public void BalancedSegmentation_GivesEqualPieces_WhereGreedyLeavesARemainder()
    {
        // 6.4 lath, 6 stock: greedy 6 + 0.4; balanced 3.2 + 3.2
        var lath = StraightLath(6.4);
        var greedy = LathSegmentation.Segment(lath, 6.0, 0.0);
        Assert.Equal(2, greedy.Segments.Count);
        Assert.Equal(6.0, Arc(greedy.Segments[0]), 9);
        Assert.Equal(0.4, Arc(greedy.Segments[1]), 9);
        var bal = LathSegmentation.Segment(lath, 6.0, 0.0, null, 0.0, true);
        Assert.Equal(2, bal.Segments.Count);
        Assert.Equal(3.2, Arc(bal.Segments[0]), 9);
        Assert.Equal(3.2, Arc(bal.Segments[1]), 9);
        Assert.Equal(3.2, bal.CutArcLengths[0], 9);
        // with a 0.3 splice: 13 lath, stock 6 → n = ⌈(13 − 0.3) / 5.7⌉ = 3 pieces, each (13 + 2 × 0.3) / 3 = 4.5333
        var lath13 = StraightLath(13.0);
        var b3 = LathSegmentation.Segment(lath13, 6.0, 0.0, null, 0.3, true);
        Assert.Equal(3, b3.Segments.Count);
        double P = 13.6 / 3;
        Assert.Equal(P - 0.15, b3.CutArcLengths[0], 9);
        Assert.Equal(P - 0.15 + (P - 0.3), b3.CutArcLengths[1], 9);
        foreach (var s in b3.Segments) Assert.Equal(P, Arc(s), 9);
        // a lath just over the stock plus the splice allowance: 6.25 on 6 with 0.3 splice → 2 pieces of 3.275 (greedy: 5.85 + 0.70, lapping 0.3)
        var b2 = LathSegmentation.Segment(StraightLath(6.25), 6.0, 0.0, null, 0.3, true);
        Assert.Equal(2, b2.Segments.Count);
        foreach (var s in b2.Segments) Assert.Equal(6.55 / 2, Arc(s), 9);
        var g2 = LathSegmentation.Segment(StraightLath(6.25), 6.0, 0.0, null, 0.3);
        Assert.Equal(2, g2.Segments.Count);
        Assert.Equal(5.85, Arc(g2.Segments[0]), 9);
        Assert.Equal(0.70, Arc(g2.Segments[1]), 9);
        foreach (var s in b3.Segments) Assert.InRange(Arc(s), 0, 6.0 + 1e-9);
        // the pieces reassemble into the lath: consecutive pieces overlap by the splice
        for (int i = 1; i < b3.Segments.Count; i++)
            Assert.Equal(0.3, b3.Segments[i - 1][^1].X - b3.Segments[i][0].X, 9);
        // the same count of pieces as the greedy cut (never more boards)
        var g3 = LathSegmentation.Segment(lath13, 6.0, 0.0, null, 0.3);
        Assert.Equal(g3.Segments.Count, b3.Segments.Count);
    }

    [Fact]
    public void BalancedSegmentation_MovesCutsOffJoints_AndFallsBackWhenItCannot()
    {
        // joints every 1.0 on a 12 lath, stock 5: 3 equal pieces of 4 would cut on joints at 4 and 8
        var lath = StraightLath(12.0, 120);
        var joints = Enumerable.Range(1, 11).Select(i => (double)i).ToList();
        var bal = LathSegmentation.Segment(lath, 5.0, 0.2, joints, 0.0, true);
        Assert.Equal(3, bal.Segments.Count);
        Assert.Equal(2, bal.CutArcLengths.Length);
        foreach (double c in bal.CutArcLengths)
        {
            double clear = joints.Min(j => Math.Abs(j - c));
            Assert.InRange(clear, 0.2 - 1e-9, double.MaxValue);
            Assert.InRange(Math.Abs(c - Math.Round(c / 4) * 4), 0.2 - 1e-9, 0.2 + 1e-9); // nearest clear position: joint ± margin
        }
        foreach (var s in bal.Segments) Assert.InRange(Arc(s), 0, 5.0 + 1e-9);
        // greedy for comparison: 5 → pulled to 4.8, then 9.6, remainder 2.4 — three pieces, unequal
        var greedy = LathSegmentation.Segment(lath, 5.0, 0.2, joints, 0.0);
        Assert.Equal(3, greedy.Segments.Count);
        Assert.Equal(4.8, greedy.CutArcLengths[0], 9);
        // when no cut clears the margin anywhere (joints every 0.3, margin 0.2) the balanced
        // request returns the greedy cuts (same count, same positions)
        var dense = Enumerable.Range(1, 39).Select(i => 0.3 * i).ToList();
        var bd = LathSegmentation.Segment(lath, 5.0, 0.2, dense, 0.0, true);
        var gd = LathSegmentation.Segment(lath, 5.0, 0.2, dense, 0.0);
        Assert.Equal(gd.CutArcLengths, bd.CutArcLengths);
        // a lath within the stock is one piece either way
        Assert.Single(LathSegmentation.Segment(StraightLath(4.0), 5.0, 0.2, joints, 0.3, true).Segments);
    }

    [Fact]
    public void LathPieces_PackIntoBoards_BalancedCutsCostBoardsOnEqualLaths()
    {
        // 9 laths of 9 on 6 stock. Greedy: 6 + 3 per lath → nine full boards + nine 3s packed two per
        // board = 9 + 5 = 14 boards, 81 / 84 = 96.4 % material. Balanced: 4.5 + 4.5 → eighteen pieces
        // that cannot share a board (4.5 + 4.5 > 6) = 18 boards, 75 %. Equal pieces buy splices away
        // from the lath ends with offcuts; on equal laths a piece of L/n with n = ⌈L/S⌉ is always
        // longer than S/2, so the balanced pieces never pair up — only mixed lath lengths let them.
        var lengths = Enumerable.Range(0, 9).Select(_ => 9.0).ToList();
        var greedyPieces = lengths.SelectMany(L => LathSegmentation.Segment(StraightLath(L), 6.0, 0.0).Segments.Select(Arc)).ToList();
        var balPieces = lengths.SelectMany(L => LathSegmentation.Segment(StraightLath(L), 6.0, 0.0, null, 0.0, true).Segments.Select(Arc)).ToList();
        Assert.Equal(18, greedyPieces.Count);
        Assert.Equal(18, balPieces.Count);
        var g = CuttingStock.Pack(greedyPieces, 6.0);
        var b = CuttingStock.Pack(balPieces, 6.0);
        Assert.Equal(14, g.Boards.Count);
        Assert.Equal(14, g.LowerBound);
        Assert.Equal(81.0 / 84.0, g.Material, 9);
        Assert.Equal(18, b.Boards.Count);
        Assert.Equal(18, b.LowerBound);
        Assert.Equal(0.75, b.Material, 9);
        // mixed lengths: the packer's count agrees with the exact optimum
        var mixed = new[] { 9.0, 7.5, 4.6, 2.2, 7.0, 13.0, 1.1 }.SelectMany(L => LathSegmentation.Segment(StraightLath(L), 6.0, 0.0, null, 0.0, true).Segments.Select(Arc)).ToList();
        var m = CuttingStock.Pack(mixed, 6.0);
        int opt = CuttingStock.Optimal(mixed, 6.0, 0, out bool exact);
        Assert.True(exact);
        Assert.Equal(opt, m.Boards.Count);
        Assert.Equal(mixed.Sum(), m.PackedLength, 9);
    }
}
