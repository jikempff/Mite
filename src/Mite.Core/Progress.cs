using System;

namespace Mite.Core;

/// <summary>
/// Receives the progress of a long operation: the fraction done in [0, 1]
/// and a short word for the stage it is in ("tracing A", "solving").
/// Reports arrive on the calling thread, between units of work, and never
/// go backwards; the last report of a finished operation is 1.
/// </summary>
/// <remarks>
/// Why a percentage and not a spinner: a percent-done indicator is what
/// people prefer once a wait passes about ten seconds (Myers 1985, "The
/// importance of percent-done progress indicators for computer-human
/// interfaces", CHI '85: 86 % preferred it; Nielsen 1993, "Usability
/// Engineering", the 0.1 / 1 / 10 s limits: past 10 s show percent done,
/// under 1 s show nothing). The fraction is an estimate, and the rules for a
/// good one (Nielsen; Harrison, Amento, Kuznetsov &amp; Bell 2007, "Rethinking
/// the progress bar", UIST '07) are: never move backwards, never stall near
/// the end, prefer a late speed-up to a late slow-down. <see cref="ProgressScope"/>
/// enforces the first rule; the estimates in the solvers aim at the others
/// (coverage of the surface for the tracers, the residual's decay for the
/// iterative solvers — both reach 1 when the work does, instead of sitting
/// at the iteration cap).
/// </remarks>
public delegate void ProgressHandler(double fraction, string stage);

/// <summary>
/// A window [From, To] of a <see cref="ProgressHandler"/> for one stage of an
/// operation. <see cref="Report"/> clamps to [0, 1], drops anything that does
/// not advance, and maps into the window; <see cref="Sub"/> nests a stage
/// inside it. A scope with no handler costs nothing.
/// </summary>
public sealed class ProgressScope
{
    private readonly ProgressHandler? _handler;
    private readonly double _from, _to;
    private double _last = -1;

    public ProgressScope(ProgressHandler? handler, string stage, double from = 0.0, double to = 1.0)
    {
        _handler = handler;
        Stage = stage ?? "";
        _from = Math.Min(from, to);
        _to = Math.Max(from, to);
    }

    /// <summary>A scope that reports nowhere.</summary>
    public static ProgressScope None => new ProgressScope(null, "");

    /// <summary>The stage word sent with every report.</summary>
    public string Stage { get; set; }

    /// <summary>True when somebody is listening (skip expensive estimates otherwise).</summary>
    public bool Active => _handler != null;

    /// <summary>Last fraction reported within this scope (0..1), −1 before the first report.</summary>
    public double Last => _last;

    /// <summary>
    /// Reports <paramref name="fraction"/> of this scope's work (clamped to
    /// [0, 1]) if it advances on the last report. Returns the fraction sent
    /// to the handler (in the handler's units) or −1 when nothing was sent.
    /// </summary>
    public double Report(double fraction)
    {
        if (_handler == null) return -1;
        if (double.IsNaN(fraction)) return -1;
        double f = fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
        if (f <= _last) return -1;
        _last = f;
        double mapped = _from + (_to - _from) * f;
        _handler(mapped, Stage);
        return mapped;
    }

    /// <summary>Reports 1: the stage is finished.</summary>
    public void Done() => Report(1.0);

    /// <summary>The text a host shows for a report: "tracing A 42 %" (the percentage rounded down, never 100 before the end).</summary>
    public static string Text(double fraction, string? stage)
    {
        double f = double.IsNaN(fraction) ? 0 : fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
        int pct = (int)Math.Floor(100.0 * f + 1e-9);
        return string.IsNullOrEmpty(stage) ? pct + " %" : stage + " " + pct + " %";
    }

    /// <summary>
    /// A nested scope covering [from, to] of this one (both in 0..1 of this
    /// scope). Its reports start where this scope's window starts, never
    /// below what has been reported here already.
    /// </summary>
    public ProgressScope Sub(string stage, double from, double to)
    {
        double a = _from + (_to - _from) * Math.Max(0, Math.Min(1, from));
        double b = _from + (_to - _from) * Math.Max(0, Math.Min(1, to));
        return new ProgressScope(_handler, stage, a, b);
    }

    /// <summary>
    /// Progress of an iterative solve that stops at a tolerance or at an
    /// iteration cap, whichever comes first: the larger of the iteration
    /// fraction and the residual's decay on a log scale from its first value
    /// to the tolerance. The log scale is the natural one for the linear and
    /// superlinear convergence of relaxation and Newton-type solvers (the
    /// residual of a linearly convergent method falls by a constant factor per
    /// iteration, so log r is linear in the iteration count). Returns 0..1.
    /// </summary>
    public static double Converging(double firstResidual, double residual, double tolerance, int iteration, int maxIterations)
    {
        double byIter = maxIterations > 0 ? (double)iteration / maxIterations : 0;
        double byResidual = 0;
        if (firstResidual > 0 && residual > 0 && tolerance > 0 && firstResidual > tolerance && !double.IsInfinity(firstResidual))
            byResidual = Math.Log(firstResidual / Math.Max(residual, tolerance)) / Math.Log(firstResidual / tolerance);
        double f = Math.Max(byIter, byResidual);
        return f < 0 ? 0 : f > 1 ? 1 : double.IsNaN(f) ? 0 : f;
    }
}
