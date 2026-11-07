using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;
using Mite.Core.Numerics;

namespace Mite.Tests;

/// <summary>
/// The block LDLᵀ (3 × 3 blocks, Davis 2005 up-looking algorithm on block
/// entries, minimum-degree ordering) behind the kinetic solve, checked
/// against dense elimination and the envelope solver, with its symbolic
/// reuse, its singular / indefinite detection, and the normal-equation
/// assembly with a cached pattern map.
/// </summary>
public class BlockLdlTests
{
    /// <summary>Random sparse Jacobian rows over 3-block unknowns on a ring of blocks: each row touches two neighbouring blocks.</summary>
    private static (List<List<int>> cols, List<List<double>> vals, double[] r) RandomRows(int blocks, int rows, int seed, int reach = 2)
    {
        var rnd = new Random(seed);
        var cols = new List<List<int>>(); var vals = new List<List<double>>();
        var r = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            int b = rnd.Next(blocks);
            var c = new List<int>(); var v = new List<double>();
            foreach (int bb in new[] { b, (b + rnd.Next(1, reach + 1)) % blocks })
                for (int k = 0; k < 3; k++) if (rnd.NextDouble() < 0.8) { c.Add(3 * bb + k); v.Add(rnd.NextDouble() * 2 - 1); }
            if (c.Count == 0) { c.Add(3 * b); v.Add(1.0); }
            cols.Add(c); vals.Add(v);
            r[i] = rnd.NextDouble() * 2 - 1;
        }
        return (cols, vals, r);
    }

    private static double[,] Dense(List<List<int>> cols, List<List<double>> vals, int n, double[]? shift = null)
    {
        var a = new double[n, n];
        for (int i = 0; i < cols.Count; i++)
            for (int p = 0; p < cols[i].Count; p++)
                for (int q = 0; q < cols[i].Count; q++)
                    a[cols[i][p], cols[i][q]] += vals[i][p] * vals[i][q];
        if (shift != null) for (int c = 0; c < n; c++) a[c, c] += shift[c];
        return a;
    }

    private static double[] DenseSolve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone(); var x = (double[])b.Clone();
        for (int k = 0; k < n; k++)
        {
            int piv = k; for (int i = k + 1; i < n; i++) if (Math.Abs(m[i, k]) > Math.Abs(m[piv, k])) piv = i;
            if (piv != k) { for (int j = 0; j < n; j++) (m[k, j], m[piv, j]) = (m[piv, j], m[k, j]); (x[k], x[piv]) = (x[piv], x[k]); }
            for (int i = k + 1; i < n; i++)
            {
                double f = m[i, k] / m[k, k];
                for (int j = k; j < n; j++) m[i, j] -= f * m[k, j];
                x[i] -= f * x[k];
            }
        }
        for (int k = n - 1; k >= 0; k--) { double s = x[k]; for (int j = k + 1; j < n; j++) s -= m[k, j] * x[j]; x[k] = s / m[k, k]; }
        return x;
    }

    private static double MaxAbs(IEnumerable<double> v) => v.Select(Math.Abs).DefaultIfEmpty(0).Max();

    [Theory]
    [InlineData(5, 40, 1)]
    [InlineData(40, 400, 2)]
    [InlineData(200, 2600, 3)]
    public void FactorAndSolve_MatchDenseElimination_AndTheEnvelopeSolver(int blocks, int rows, int seed)
    {
        int n = 3 * blocks;
        var (cols, vals, r) = RandomRows(blocks, rows, seed);
        var shift = Enumerable.Range(0, n).Select(c => 0.05 + 0.01 * (c % 7)).ToArray();   // makes JᵀJ + shift positive definite
        var ne = new BlockNormalEquations3(n);
        var g = new double[n];
        double maxDiag = ne.Assemble(cols, vals, r, g);
        Assert.True(maxDiag > 0);
        Assert.True(ne.Factorize(shift, 1e-14));
        var rnd = new Random(seed + 100);
        var b = Enumerable.Range(0, n).Select(_ => rnd.NextDouble() - 0.5).ToArray();
        var x = ne.Solve(b);
        // against dense elimination of the same matrix
        var dense = Dense(cols, vals, n, shift);
        var xd = DenseSolve(dense, b);
        double scale = MaxAbs(xd);
        Assert.InRange(MaxAbs(x.Zip(xd, (p, q) => p - q)), 0, 1e-10 * scale);
        // the residual of A x = b from the block matrix's own product
        var ax = ne.Matrix!.Multiply(x);
        for (int c = 0; c < n; c++) ax[c] += shift[c] * x[c];
        Assert.InRange(MaxAbs(ax.Zip(b, (p, q) => p - q)), 0, 1e-11 * Math.Max(1, MaxAbs(b)));
        // against the envelope solver of 1.6.0 on the same system
        var builder = new SparseSymmetricSolver.Builder(n);
        for (int i = 0; i < cols.Count; i++)
            for (int p = 0; p < cols[i].Count; p++)
                for (int q = p; q < cols[i].Count; q++)
                    builder.Add(cols[i][p], cols[i][q], vals[i][p] * vals[i][q]);
        for (int c = 0; c < n; c++) builder.Add(c, c, shift[c]);
        var env = SparseSymmetricSolver.Factor(builder, 1e-14)!;
        var xe = env.Solve(b);
        Assert.InRange(MaxAbs(x.Zip(xe, (p, q) => p - q)), 0, 1e-10 * scale);
        // the gradient is Jᵀ r
        var gd = new double[n];
        for (int i = 0; i < cols.Count; i++) for (int p = 0; p < cols[i].Count; p++) gd[cols[i][p]] += vals[i][p] * r[i];
        Assert.InRange(MaxAbs(g.Zip(gd, (p, q) => p - q)), 0, 1e-13);
        Assert.Equal(1, ne.Analyses);
    }

    [Fact]
    public void DiagonalShift_IsTheSameAsShiftingTheMatrix()
    {
        int blocks = 30, n = 90;
        var (cols, vals, r) = RandomRows(blocks, 400, 7);
        var shift = Enumerable.Range(0, n).Select(c => 0.3 + 0.2 * Math.Sin(c)).ToArray();
        var ne = new BlockNormalEquations3(n);
        ne.Assemble(cols, vals, r, new double[n]);
        Assert.True(ne.Factorize(shift));
        var b = Enumerable.Range(0, n).Select(c => Math.Cos(0.3 * c)).ToArray();
        var x1 = ne.Solve(b);
        // the same shift added as rows of the Jacobian (√shift on the diagonal)
        var cols2 = cols.Select(c => new List<int>(c)).ToList(); var vals2 = vals.Select(v => new List<double>(v)).ToList();
        var r2 = r.ToList();
        for (int c = 0; c < n; c++) { cols2.Add(new List<int> { c }); vals2.Add(new List<double> { Math.Sqrt(shift[c]) }); r2.Add(0); }
        var ne2 = new BlockNormalEquations3(n);
        ne2.Assemble(cols2, vals2, r2.ToArray(), new double[n]);
        Assert.True(ne2.Factorize(null));
        var x2 = ne2.Solve(b);
        Assert.InRange(MaxAbs(x1.Zip(x2, (p, q) => p - q)), 0, 1e-12 * MaxAbs(x1));
    }

    [Fact]
    public void SingularAndIndefiniteSystems_AreRefused_AndAShiftRescuesTheSingularOne()
    {
        // fewer rows than unknowns: JᵀJ is rank deficient
        int blocks = 20, n = 60;
        var (cols, vals, r) = RandomRows(blocks, 25, 11);
        var ne = new BlockNormalEquations3(n);
        ne.Assemble(cols, vals, r, new double[n]);
        Assert.False(ne.Factorize(null, 1e-12));
        Assert.Throws<InvalidOperationException>(() => ne.Solve(new double[n]));
        var shift = Enumerable.Repeat(1e-3, n).ToArray();
        Assert.True(ne.Factorize(shift, 1e-12));
        var x = ne.Solve(Enumerable.Repeat(1.0, n).ToArray());
        var ax = ne.Matrix!.Multiply(x); for (int c = 0; c < n; c++) ax[c] += shift[c] * x[c];
        // the null directions give x ≈ b / shift ≈ 1e3: the residual scales with ‖A‖ ‖x‖ ε
        double maxDiag = Enumerable.Range(0, n).Max(c => ne.Matrix.Diagonal(c));
        Assert.True(MaxAbs(x) > 100);
        Assert.InRange(MaxAbs(ax.Select(v => v - 1)), 0, 1e-13 * maxDiag * MaxAbs(x));
        // an indefinite matrix (a negative diagonal block) is refused whatever the ordering
        var m = new BlockSymmetricMatrix3(3, new[] { (0, 1), (1, 2) });
        for (int c = 0; c < 9; c++) m.Add(c, c, c == 4 ? -1.0 : 2.0);
        m.Add(0, 3, 0.5); m.Add(4, 7, 0.5);
        var f = new BlockLdl3(m);
        Assert.False(f.Factor());
        m.Add(4, 4, 3.0);
        Assert.True(f.Factor());
    }

    [Fact]
    public void PatternMap_IsReusedWhileTheJacobianStructureLasts_AndRebuiltWhenItChanges()
    {
        int blocks = 25, n = 75;
        var (cols, vals, r) = RandomRows(blocks, 300, 5);
        var ne = new BlockNormalEquations3(n);
        var g = new double[n];
        ne.Assemble(cols, vals, r, g);
        var a0 = (double[])ne.Matrix!.Values.Clone();
        // the same structure with new values: no analysis, no remap, right values
        var rnd = new Random(99);
        for (int i = 0; i < vals.Count; i++) for (int p = 0; p < vals[i].Count; p++) vals[i][p] = rnd.NextDouble();
        ne.Assemble(cols, vals, r, g);
        Assert.Equal(1, ne.Analyses); Assert.Equal(0, ne.Remaps);
        AssertMatches(ne, cols, vals, n);
        // a row loses a column (a one-sided contact switching off): the block pattern still covers it → remap only
        var saved = (cols[3].ToList(), vals[3].ToList());
        cols[3].RemoveAt(0); vals[3].RemoveAt(0);
        ne.Assemble(cols, vals, r, g);
        Assert.Equal(1, ne.Analyses); Assert.Equal(1, ne.Remaps);
        AssertMatches(ne, cols, vals, n);
        Assert.True(ne.Factorize(Enumerable.Repeat(0.1, n).ToArray()));
        // a row couples two blocks that never met: a new analysis
        cols[3] = saved.Item1; vals[3] = saved.Item2;
        cols.Add(new List<int> { 0, 3 * (blocks / 2) + 1 }); vals.Add(new List<double> { 1.0, -1.0 });
        var r2 = r.Concat(new[] { 0.5 }).ToArray();
        ne.Assemble(cols, vals, r2, g);
        Assert.Equal(2, ne.Analyses);
        AssertMatches(ne, cols, vals, n);
        Assert.True(ne.Factorize(Enumerable.Repeat(0.1, n).ToArray()));
        var b = Enumerable.Range(0, n).Select(c => 1.0 / (1 + c)).ToArray();
        var x = ne.Solve(b);
        var xd = DenseSolve(Dense(cols, vals, n, Enumerable.Repeat(0.1, n).ToArray()), b);
        Assert.InRange(MaxAbs(x.Zip(xd, (p, q) => p - q)), 0, 1e-10 * MaxAbs(xd));
    }

    private static void AssertMatches(BlockNormalEquations3 ne, List<List<int>> cols, List<List<double>> vals, int n)
    {
        var dense = Dense(cols, vals, n);
        var m = ne.Matrix!;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var (pos, _) = m.Positions(i, j);
                double v = pos < 0 ? 0 : m.Values[pos];
                Assert.InRange(Math.Abs(v - dense[i, j]), 0, 1e-13);
            }
    }

    /// <summary>Block graph of a w × h grid with 8-neighbour coupling (what a lath net's position / normal blocks look like).</summary>
    private static List<int>[] GridGraph(int w, int h)
    {
        var adj = new List<int>[w * h];
        for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int u = x + dx, v = y + dy;
                if (u < 0 || v < 0 || u >= w || v >= h) continue;
                adj[x * h + y].Add(u * h + v);
            }
        return adj;
    }

    private static long FillOf(List<int>[] adj, int[] perm)
    {
        // nnz(L) by the elimination tree / column counts, as the symbolic phase computes it
        int n = adj.Length;
        var inv = new int[n]; for (int k = 0; k < n; k++) inv[perm[k]] = k;
        var cols = new List<int>[n]; for (int k = 0; k < n; k++) cols[k] = new List<int>();
        for (int i = 0; i < n; i++) foreach (int j in adj[i]) { int r = inv[i], c = inv[j]; if (r < c) cols[c].Add(r); }
        var parent = new int[n]; var flag = new int[n]; long lnz = 0;
        for (int k = 0; k < n; k++)
        {
            parent[k] = -1; flag[k] = k;
            foreach (int i0 in cols[k])
                for (int i = i0; flag[i] != k; i = parent[i]) { if (parent[i] == -1) parent[i] = k; lnz++; flag[i] = k; }
        }
        return lnz;
    }

    [Fact]
    public void MinimumDegree_IsAPermutation_WithLessFillThanTheBandAndTheDissection()
    {
        var adj = GridGraph(60, 60);
        var md = BlockLdl3.MinimumDegree(adj);
        Assert.Equal(adj.Length, md.Distinct().Count());
        Assert.All(md, v => Assert.InRange(v, 0, adj.Length - 1));
        long fillMd = FillOf(adj, md);
        long fillBand = FillOf(adj, Enumerable.Range(0, adj.Length).ToArray());
        long fillNd = FillOf(adj, SparseLdl.NestedDissection(adj));
        Assert.True(fillMd < 0.5 * fillBand, $"minimum degree {fillMd} vs band {fillBand}");
        Assert.True(fillMd < fillNd, $"minimum degree {fillMd} vs nested dissection {fillNd}");
        // the hybrid above the limit: dissection with minimum-degree leaves, still a permutation and better than the band
        var big = GridGraph(100, 90);   // 9000 > MinimumDegreeLimit
        var hybrid = BlockLdl3.Ordering(big);
        Assert.Equal(big.Length, hybrid.Distinct().Count());
        Assert.True(FillOf(big, hybrid) < 0.5 * FillOf(big, Enumerable.Range(0, big.Length).ToArray()));
    }

    [Fact]
    public void HybridOrdering_FactorsALargeGridSystem()
    {
        // above MinimumDegreeLimit the ordering is nested dissection with minimum-degree leaves: solve a 9000-block system
        int w = 100, h = 90, blocks = w * h, n = 3 * blocks;
        var adj = GridGraph(w, h);
        var pairs = new List<(int, int)>();
        for (int i = 0; i < blocks; i++) foreach (int j in adj[i]) if (i < j) pairs.Add((i, j));
        var m = new BlockSymmetricMatrix3(blocks, pairs);
        var rnd = new Random(3);
        for (int i = 0; i < blocks; i++) foreach (int j in adj[i]) if (i < j)
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) m.Add(3 * i + a, 3 * j + b, 0.05 * (rnd.NextDouble() - 0.5));
        for (int c = 0; c < n; c++) m.Add(c, c, 2.0);   // diagonally dominant
        var f = new BlockLdl3(m);
        Assert.True(f.Factor());
        var b2 = Enumerable.Range(0, n).Select(c => Math.Sin(c * 0.01)).ToArray();
        var x = f.Solve(b2);
        var ax = m.Multiply(x);
        Assert.InRange(MaxAbs(ax.Zip(b2, (p, q) => p - q)), 0, 1e-11);
    }

    [Fact]
    public void ScissorNet_UsesTheBlockSolver_AnalysesOnce_AndStaysExactOnTheHyperboloid()
    {
        int n = 16, levels = 3, rows = 2 * levels + 1;
        double rho = 1, b0 = 25 * Math.PI / 180, b1 = 70 * Math.PI / 180;
        var (nodes0, laths0, normals0) = ScissorNet.HyperboloidMechanism(n, levels, rho, b0);
        var net = new ScissorNet(nodes0, laths0, n, normals0);
        int top0 = 2 * levels, topOpp = (n / 2) * rows + 2 * levels, waist0 = levels;
        double TopDiameter(double beta) { var (nn, _, _) = ScissorNet.HyperboloidMechanism(n, levels, rho, beta); return (nn[top0] - nn[topOpp]).Length; }
        var res = net.Solve(new ScissorNet.Options { Fixed = new[] { waist0 }, Cables = new[] { (top0, topOpp) }, CableLengthsAt = f => new[] { TopDiameter(b0 + f * (b1 - b0)) }, Steps = 6, Tolerance = 1e-11 });
        Assert.Equal(6 * (nodes0.Length - 1) + 3, res.Unknowns);
        Assert.Equal(1, res.PatternAnalyses);
        Assert.Equal(0, res.PatternRemaps);
        Assert.True(res.Factorizations >= res.States.Sum(s => s.Iterations), $"{res.Factorizations} factorizations for {res.States.Sum(s => s.Iterations)} iterations");
        Assert.True(res.FactorBlocks > 0 && res.FactorBlocks < 40L * res.Unknowns / 3, $"fill {res.FactorBlocks} blocks");
        for (int k = 0; k < res.States.Count; k++)
        {
            var (truth, _, _) = ScissorNet.HyperboloidMechanism(n, levels, rho, b0 + (b1 - b0) * k / 6);
            var st = res.States[k];
            Assert.True(st.Converged);
            Assert.InRange(st.LengthDrift, 0, 1e-12);
            double shape = 0;
            for (int i = 0; i < truth.Length; i++) for (int j = i + 1; j < truth.Length; j++)
                shape = Math.Max(shape, Math.Abs((st.Nodes[i] - st.Nodes[j]).Length - (truth[i] - truth[j]).Length));
            Assert.InRange(shape, 0, 1e-9);
        }
    }

    [Fact]
    public void ScissorNet_GroundContactsSwitching_OnlyRemapTheRows()
    {
        // the rhombic lattice pushed into a one-sided ground: the contact rows (empty above the plane) switch on
        // where a trial step sinks a node, and the row map is rebuilt without a new pattern analysis
        int n = 4;
        Vec3d Node(int i, int j, double gamma) => new Vec3d(i + j * Math.Cos(gamma), j * Math.Sin(gamma), 0);
        var nodes = new Vec3d[(n + 1) * (n + 1)];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) nodes[i * (n + 1) + j] = Node(i, j, Math.PI / 2);
        var laths = new List<int[]>();
        for (int j = 0; j <= n; j++) laths.Add(Enumerable.Range(0, n + 1).Select(i => i * (n + 1) + j).ToArray());
        for (int i = 0; i <= n; i++) laths.Add(Enumerable.Range(0, n + 1).Select(j => i * (n + 1) + j).ToArray());
        var net = new ScissorNet(nodes, laths, n + 1);
        var ground = Enumerable.Range(1, n).Select(i => i * (n + 1)).ToArray();
        int farTop = n * (n + 1) + n;
        var opt = new ScissorNet.Options
        {
            Fixed = new[] { 0 }, Ground = ground, SlideNormal = new Vec3d(0, 1, 0),
            Driven = new[] { farTop }, TargetsAt = t => new[] { nodes[farTop] + new Vec3d(0, -0.5 * t, 0) },
            Steps = 2, Tolerance = 1e-11,
        };
        var res = net.Solve(opt);
        Assert.True(res.Last.Converged);
        Assert.True(res.Last.TargetMiss > 0.3, $"the ground should stop the push: miss {res.Last.TargetMiss:0.000}");
        Assert.InRange(res.Last.GroundMiss, 0, 0.01);
        Assert.Equal(1, res.PatternAnalyses);
        Assert.True(res.PatternRemaps >= 1, "the contact rows switched and only the map was rebuilt");
    }
}
