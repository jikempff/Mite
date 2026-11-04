using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Kinetics;

/// <summary>
/// How a scissor-jointed net can move before any driver is set: the softest
/// motions of the net at its current state, each with its first-order cost,
/// split into mechanisms (motions that cost nothing: the hinges, the joint
/// spacing and the asymptotic condition all allow them) and stiff modes.
///
/// A two-family lath grid with scissor joints is a mechanism whose mobility is
/// decided by geometry, supports and singularities (Schikore, Schling,
/// Oberbichler &amp; Bauer 2020; Schling &amp; Schikore 2023): a doubly ruled
/// grid has exactly one rigid-body mechanism, holding its joints removes it,
/// and a flat-assembled grid has its deployment branches as out-of-plane
/// modes. The modes are the null space of the compatibility matrix
/// (Pellegrino 1993) read through the Gauss–Newton matrix JᵀJ of
/// <see cref="ScissorNet"/>'s residuals; rigid-body motions of the whole net
/// are deflated (<see cref="ScissorNet.SoftestModes"/>).
/// </summary>
public static class MechanismModes
{
    public sealed class Options
    {
        /// <summary>Number of modes to compute, softest first (default 3).</summary>
        public int Count { get; set; } = 3;
        /// <summary>
        /// A mode is a mechanism when its stiffness is below this fraction of the
        /// mean stiffness of the net (default 1e-6; the exact hyperboloid mechanism
        /// reads ~1e-12 of it, the next mode ~1e-2).
        /// </summary>
        public double MechanismTolerance { get; set; } = 1e-6;
        /// <summary>Inverse-iteration sweeps per mode (default 60).</summary>
        public int Iterations { get; set; } = 60;
    }

    public sealed class Mode
    {
        /// <summary>Displacement per node, scaled so that the largest is 1.</summary>
        public Vec3d[] Displacements = Array.Empty<Vec3d>();
        /// <summary>Change of each node's normal, on the same scale.</summary>
        public Vec3d[] NormalChanges = Array.Empty<Vec3d>();
        /// <summary>First-order cost ‖J v‖² of the unit mode.</summary>
        public double Stiffness;
        /// <summary><see cref="Stiffness"/> / the net's mean stiffness.</summary>
        public double RelativeStiffness;
        /// <summary>True when the mode costs nothing at first order.</summary>
        public bool IsMechanism;
        /// <summary>Share of the motion out of the tangent planes (0 = in plane, 1 = along the normals), weighted by the displacements.</summary>
        public double OutOfPlane;
    }

    public sealed class Result
    {
        public List<Mode> Modes = new List<Mode>();
        /// <summary>Number of mechanisms among the computed modes (a lower bound when every computed mode is one).</summary>
        public int Mechanisms;
        /// <summary>True when every computed mode is a mechanism: compute more to count them all.</summary>
        public bool MoreMayExist;
        /// <summary>Mean stiffness of the net (trace of JᵀJ / unknowns).</summary>
        public double MeanStiffness;
        /// <summary>Stiffness of the softest non-mechanism over the stiffest mechanism (∞ when one side is empty): how clearly the two are separated.</summary>
        public double Gap = double.PositiveInfinity;
    }

    /// <summary>
    /// The softest modes of <paramref name="net"/> under the supports of
    /// <paramref name="supports"/> (its Fixed, Driven and Sliding nodes; the
    /// drivers' targets are not used — the modes are the motions available
    /// before driving).
    /// </summary>
    public static Result Compute(ScissorNet net, ScissorNet.Options? supports = null, Options? options = null)
    {
        if (net == null) throw new ArgumentNullException(nameof(net));
        var o = options ?? new Options();
        var s = supports ?? new ScissorNet.Options();
        var res = new Result { MeanStiffness = net.MeanStiffness(s) };
        var raw = net.SoftestModes(s, Math.Max(1, o.Count), Math.Max(1, o.Iterations));
        double reference = res.MeanStiffness > 0 ? res.MeanStiffness : 1.0;
        foreach (var (pos, nrm, k) in raw)
        {
            var m = new Mode
            {
                Displacements = pos,
                NormalChanges = nrm,
                Stiffness = k,
                RelativeStiffness = Math.Abs(k) / reference,
            };
            m.IsMechanism = m.RelativeStiffness <= o.MechanismTolerance;
            double outW = 0, all = 0;
            for (int i = 0; i < pos.Length; i++)
            {
                double l = pos[i].Length; if (l == 0) continue;
                outW += Math.Abs(Vec3d.Dot(pos[i], net.Normals[i].Normalized())); all += l;
            }
            m.OutOfPlane = all > 0 ? outW / all : 0;
            res.Modes.Add(m);
        }
        res.Mechanisms = res.Modes.Count(x => x.IsMechanism);
        res.MoreMayExist = res.Modes.Count > 0 && res.Mechanisms == res.Modes.Count;
        var soft = res.Modes.Where(x => x.IsMechanism).Select(x => x.Stiffness).DefaultIfEmpty(double.NaN).Max();
        var stiff = res.Modes.Where(x => !x.IsMechanism).Select(x => x.Stiffness).DefaultIfEmpty(double.NaN).Min();
        if (!double.IsNaN(soft) && !double.IsNaN(stiff)) res.Gap = stiff / Math.Max(Math.Abs(soft), 1e-300);
        return res;
    }

    /// <summary>
    /// The net moved along a mode by <paramref name="amplitude"/> (model units for
    /// the largest node displacement), normals re-normalised: for previewing a
    /// mode, or as the imperfection that starts a motion on that branch.
    /// </summary>
    public static ScissorNet.State Displaced(ScissorNet net, Mode mode, double amplitude) =>
        net.Kick((mode.Displacements, mode.NormalChanges, mode.Stiffness), amplitude);
}
