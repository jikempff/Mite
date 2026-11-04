using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Kinetics;

/// <summary>
/// A solved motion of a scissor net — the net, its states from Fold 0 to the
/// end of the run and the section the states are read with — that can be read
/// at any fold: one moment of the motion for drawing, baking or checking.
///
/// At a solved state the reading is that state exactly. Between two solved
/// states the nodes are interpolated linearly and the normals linearly then
/// re-normalised; the joint spacing then drifts by O(Δ²) of the step (a chord
/// of the motion, not the motion), which <see cref="SpacingDrift"/> reports so
/// the user can raise the number of steps when it matters.
/// </summary>
public sealed class KineticMotion
{
    public ScissorNet Net { get; }
    public IReadOnlyList<ScissorNet.State> States { get; }
    /// <summary>The section and material the states are read with (null = not read elastically).</summary>
    public KineticStrain.Options? Strain { get; }

    public KineticMotion(ScissorNet net, IReadOnlyList<ScissorNet.State> states, KineticStrain.Options? strain = null)
    {
        Net = net ?? throw new ArgumentNullException(nameof(net));
        if (states == null || states.Count == 0) throw new ArgumentException("A motion needs at least one state.", nameof(states));
        for (int k = 1; k < states.Count; k++)
            if (!(states[k].Fold >= states[k - 1].Fold)) throw new ArgumentException("States must be in increasing Fold.", nameof(states));
        States = states.ToList();
        Strain = strain;
    }

    public double FirstFold => States[0].Fold;
    public double LastFold => States[States.Count - 1].Fold;

    /// <summary>
    /// The state at <paramref name="fold"/> (clamped to the solved range).
    /// <paramref name="bracket"/> gives the solved states it lies between and
    /// <paramref name="t"/> the fraction from the first to the second (0 at a
    /// solved state).
    /// </summary>
    public ScissorNet.State At(double fold, out int bracket, out double t)
    {
        if (double.IsNaN(fold)) fold = LastFold;
        fold = Math.Max(FirstFold, Math.Min(LastFold, fold));
        int k = 0;
        while (k + 1 < States.Count && States[k + 1].Fold < fold) k++;
        bracket = k;
        if (k + 1 >= States.Count) { t = 0; return Copy(States[k], fold); }
        var a = States[k]; var b = States[k + 1];
        double span = b.Fold - a.Fold;
        t = span > 0 ? (fold - a.Fold) / span : 0;
        if (t <= 1e-12) { t = 0; return Copy(a, fold); }
        if (t >= 1 - 1e-12) { bracket = k + 1; t = 0; return Copy(b, fold); }
        double u = t;
        var st = new ScissorNet.State { Fold = fold };
        st.Nodes = a.Nodes.Select((p, i) => p + u * (b.Nodes[i] - p)).ToArray();
        st.Normals = a.Normals.Select((n, i) => (n + u * (b.Normals[i] - n)).Normalized()).ToArray();
        return st;
    }

    public ScissorNet.State At(double fold) => At(fold, out _, out _);

    /// <summary>Largest |segment length − rest length| / rest length of a state: 0 for exact scissor joints.</summary>
    public double SpacingDrift(ScissorNet.State state)
    {
        double worst = 0;
        for (int l = 0; l < Net.Laths.Count; l++)
        {
            var lath = Net.Laths[l]; var rest = Net.RestLengths[l];
            for (int i = 0; i + 1 < lath.Length; i++)
                worst = Math.Max(worst, Math.Abs((state.Nodes[lath[i + 1]] - state.Nodes[lath[i]]).Length - rest[i]) / rest[i]);
        }
        return worst;
    }

    private static ScissorNet.State Copy(ScissorNet.State s, double fold) => new ScissorNet.State
    {
        Fold = fold,
        Nodes = (Vec3d[])s.Nodes.Clone(),
        Normals = (Vec3d[])s.Normals.Clone(),
        LengthDrift = s.LengthDrift, AsymptoticDeviation = s.AsymptoticDeviation, TargetMiss = s.TargetMiss, CableMiss = s.CableMiss,
        ScissorMiss = s.ScissorMiss, SlideMiss = s.SlideMiss, GroundMiss = s.GroundMiss, GroundContacts = s.GroundContacts,
        Residual = s.Residual, Iterations = s.Iterations, Converged = s.Converged,
    };
}
