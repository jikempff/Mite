using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Numerics;

/// <summary>
/// Accumulates JᵀJ and Jᵀr row by row for a sparse Gauss–Newton /
/// Levenberg–Marquardt step and solves the damped normal equations with the
/// envelope LDLᵀ of <see cref="SparseSymmetricSolver"/>. The same accumulator
/// as the one inside <c>AagWeb</c>, shared by the newer web optimisers.
/// </summary>
internal sealed class NormalEquations
{
    private readonly int _n;
    private readonly Dictionary<long, double> _jtj = new Dictionary<long, double>();
    private readonly double[] _jtr;

    public NormalEquations(int n) { _n = n; _jtr = new double[n]; }

    /// <summary>Σ w² r² over the rows added so far.</summary>
    public double Cost { get; private set; }

    public int Rows { get; private set; }

    /// <summary>Half the gradient of <see cref="Cost"/>: Jᵀ W r.</summary>
    public double[] HalfGradient => _jtr;

    public void Row(double weight, double residual, params (int Col, double Val)[] terms)
    {
        double w2 = weight * weight;
        Cost += w2 * residual * residual;
        Rows++;
        for (int a = 0; a < terms.Length; a++)
        {
            var (ca, va) = terms[a];
            if (va == 0) continue;
            _jtr[ca] += w2 * va * residual;
            for (int b = 0; b < terms.Length; b++)
            {
                var (cb, vb) = terms[b];
                if (vb == 0 || cb < ca) continue;
                long key = (long)ca * _n + cb;
                _jtj.TryGetValue(key, out double cur);
                _jtj[key] = cur + w2 * va * vb;
            }
        }
    }

    /// <summary>
    /// Solves (JᵀJ + λ(I + diag JᵀJ)) dx = −Jᵀr; null when the factorisation
    /// fails (a system without any row on some unknown and λ = 0).
    /// </summary>
    public double[]? Solve(double damping)
    {
        var rows = new List<(int Col, double Val)>[_n];
        for (int i = 0; i < _n; i++) rows[i] = new List<(int, double)>(16);
        var diag = new double[_n];
        foreach (var kv in _jtj)
        {
            int r = (int)(kv.Key / _n), c = (int)(kv.Key % _n);
            if (r == c) diag[r] += kv.Value;
            else { rows[r].Add((c, kv.Value)); rows[c].Add((r, kv.Value)); }
        }
        for (int i = 0; i < _n; i++) diag[i] += damping * (1.0 + diag[i]);
        var rhs = new double[_n];
        for (int i = 0; i < _n; i++) rhs[i] = -_jtr[i];

        var b = new SparseSymmetricSolver.Builder(_n);
        for (int i = 0; i < _n; i++)
        {
            b.Add(i, i, diag[i]);
            foreach (var (c, v) in rows[i]) if (c > i) b.Add(i, c, v);
        }
        var solver = SparseSymmetricSolver.Factor(b, 1e-18);
        return solver?.Solve(rhs);
    }
}
