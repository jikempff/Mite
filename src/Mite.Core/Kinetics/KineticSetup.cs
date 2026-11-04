using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Kinetics;

/// <summary>
/// How a scissor net is held and driven, as geometry: support points (held,
/// or sliding on a plane) and drivers (points moved to targets, cables
/// shortened or lengthened). The points snap to the nearest nodes of whichever
/// net they are applied to (<see cref="Resolve"/>), so one setup can be wired
/// into Mechanism Modes (which motions are left free) and Net Kinetics (the
/// motion under the drivers) alike. Setups combine with <see cref="Merge"/>.
/// </summary>
public sealed class KineticSetup
{
    public IReadOnlyList<Vec3d> Fixed { get; }
    public IReadOnlyList<Vec3d> Slide { get; }
    /// <summary>Normal of the sliding plane; null = not set (the user of the setup decides).</summary>
    public Vec3d? SlideNormal { get; }
    public IReadOnlyList<Vec3d> Move { get; }
    /// <summary>Target of each Move point at Fold = 1 (one per point; the last repeats).</summary>
    public IReadOnlyList<Vec3d> To { get; }
    public IReadOnlyList<(Vec3d A, Vec3d B)> Cables { get; }
    /// <summary>Cable length at Fold = 1 for each cable (the last repeats).</summary>
    public IReadOnlyList<double> Lengths { get; }

    public KineticSetup(IEnumerable<Vec3d>? fixedPoints = null, IEnumerable<Vec3d>? slide = null, Vec3d? slideNormal = null,
        IEnumerable<Vec3d>? move = null, IEnumerable<Vec3d>? to = null,
        IEnumerable<(Vec3d, Vec3d)>? cables = null, IEnumerable<double>? lengths = null)
    {
        Fixed = (fixedPoints ?? Enumerable.Empty<Vec3d>()).ToList();
        Slide = (slide ?? Enumerable.Empty<Vec3d>()).ToList();
        SlideNormal = slideNormal.HasValue && slideNormal.Value.Length > 1e-12 ? slideNormal.Value.Normalized() : (Vec3d?)null;
        Move = (move ?? Enumerable.Empty<Vec3d>()).ToList();
        To = (to ?? Enumerable.Empty<Vec3d>()).ToList();
        Cables = (cables ?? Enumerable.Empty<(Vec3d, Vec3d)>()).ToList();
        Lengths = (lengths ?? Enumerable.Empty<double>()).ToList();
    }

    public bool HasSupports => Fixed.Count > 0 || Slide.Count > 0;
    public bool HasDrivers => Move.Count > 0 || Cables.Count > 0;

    /// <summary>
    /// What is wrong with the setup, as messages a user can act on (empty when it
    /// is usable): Move points without targets, cables without lengths, lengths
    /// that are not positive.
    /// </summary>
    public List<string> Problems()
    {
        var p = new List<string>();
        if (Move.Count > 0 && To.Count == 0) p.Add($"{Move.Count} Move point(s) but no To target: give one target per point (the last repeats).");
        if (To.Count > Move.Count && Move.Count > 0) p.Add($"{To.Count} To targets for {Move.Count} Move points: the extra targets are ignored.");
        if (Cables.Count > 0 && Lengths.Count == 0) p.Add($"{Cables.Count} cable(s) but no Lengths: give the length each cable reaches at Fold = 1.");
        foreach (var l in Lengths) if (!(l > 0)) { p.Add($"Cable length {l:G4} is not positive."); break; }
        foreach (var (a, b) in Cables) if ((a - b).Length < 1e-12) { p.Add("A cable has both ends at one point."); break; }
        return p;
    }

    /// <summary>
    /// The union of two setups: points and cables of both, in order; the slide
    /// normal of <paramref name="a"/> unless it has none. Move / To and Cables /
    /// Lengths stay paired because each list is padded to its partner's count
    /// with the partner's last value before joining.
    /// </summary>
    public static KineticSetup Merge(KineticSetup? a, KineticSetup? b)
    {
        if (a == null) return b ?? new KineticSetup();
        if (b == null) return a;
        static List<T> Pad<T>(IReadOnlyList<T> v, int n) { var l = v.ToList(); while (l.Count < n && l.Count > 0) l.Add(l[l.Count - 1]); return l; }
        var to = Pad(a.To, a.Move.Count).Concat(b.To).ToList();
        var lengths = Pad(a.Lengths, a.Cables.Count).Concat(b.Lengths).ToList();
        return new KineticSetup(a.Fixed.Concat(b.Fixed), a.Slide.Concat(b.Slide), a.SlideNormal ?? b.SlideNormal,
            a.Move.Concat(b.Move), to, a.Cables.Concat(b.Cables), lengths);
    }

    /// <summary>A setup's points snapped to a net's nodes.</summary>
    public sealed class Resolved
    {
        public List<int> Fixed = new List<int>();
        public List<int> Sliding = new List<int>();
        public List<int> Driven = new List<int>();
        public List<Vec3d> TargetEnd = new List<Vec3d>();
        public List<(int p, int q)> Cables = new List<(int, int)>();
        public List<double> CableRest = new List<double>();
        public List<double> CableEnd = new List<double>();
        /// <summary>Largest distance from a Fixed / Slide / Move point to the node it snapped to.</summary>
        public double LargestSnap;
        /// <summary>Cables dropped because both ends snapped to one node.</summary>
        public int DroppedCables;
    }

    /// <summary>
    /// Snaps the setup to <paramref name="net"/>: each point to its nearest node;
    /// a node held by Fixed is not also sliding or moved, a node is moved once,
    /// a cable whose ends meet one node is dropped. The same rules Net Kinetics
    /// applies to its own inputs.
    /// </summary>
    public Resolved Resolve(ScissorNet net)
    {
        var r = new Resolved();
        double snap = 0;
        int Near(Vec3d p) { int i = net.NearestNode(p); snap = Math.Max(snap, (net.Nodes[i] - p).Length); return i; }
        foreach (var p in Fixed) { int i = Near(p); if (!r.Fixed.Contains(i)) r.Fixed.Add(i); }
        foreach (var p in Slide) { int i = Near(p); if (!r.Fixed.Contains(i) && !r.Sliding.Contains(i)) r.Sliding.Add(i); }
        for (int k = 0; k < Move.Count; k++)
        {
            int i = Near(Move[k]);
            if (To.Count == 0 || r.Fixed.Contains(i) || r.Driven.Contains(i)) continue;
            r.Driven.Add(i);
            r.TargetEnd.Add(To[Math.Min(k, To.Count - 1)]);
        }
        for (int k = 0; k < Cables.Count; k++)
        {
            int p = net.NearestNode(Cables[k].A), q = net.NearestNode(Cables[k].B);
            if (p == q || Lengths.Count == 0) { if (p == q) r.DroppedCables++; continue; }
            r.Cables.Add((p, q));
            r.CableRest.Add((net.Nodes[p] - net.Nodes[q]).Length);
            r.CableEnd.Add(Lengths[Math.Min(k, Lengths.Count - 1)]);
        }
        r.LargestSnap = snap;
        return r;
    }
}
