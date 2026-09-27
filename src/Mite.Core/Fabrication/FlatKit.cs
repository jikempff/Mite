using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;

namespace Mite.Core.Fabrication;

/// <summary>
/// The fabrication kit of a deployable asymptotic gridshell: the net is
/// pressed flat as a scissor mechanism (<see cref="ScissorNet.PressFlat"/>),
/// and the flat state is the assembly drawing — every lath a straight strip
/// cut from sheet, every crossing a joint at its place along the strip — so
/// the grid is cut, assembled flat on the table and pushed into shape
/// (Schling's flat-assembled lamella grids; Schikore, Schling, Oberbichler
/// &amp; Bauer 2020). Read the motion backwards for the deployment sequence.
///
/// Every lath is one strip. Its joint marks sit at the lath's joint spacing,
/// the rest length the scissor joints keep through the whole motion, so
/// the strips fit the flat grid and the curved one alike. A crossing of two
/// upright laths is a halving slot — the two strips slot into each other and
/// turn about the surface normal, which is the scissor axis: family A is
/// slotted from its outer edge, family B from its inner edge (as Net Joints),
/// half the depth each, and the slot is as wide as the other strip's
/// footprint at the smallest crossing angle the motion reaches. A crossing
/// of flat strips is a pin hole on the centre line. A node where three or
/// more laths meet (the flat point of a 3-fold Enneper web: six rays) is a
/// hub: every strip gets a hole there for a common pin or plate.
/// </summary>
public static class FlatKit
{
    public enum MarkKind { SlotOuter, SlotInner, Hole, Hub }

    /// <summary>How crossings of two laths are joined.</summary>
    public enum JointKind { Auto, Slot, Hole }

    public sealed class Options
    {
        /// <summary>Strip width (across the strip: along the surface normal for upright laths).</summary>
        public double Width { get; set; } = 0.06;
        /// <summary>Strip thickness (the sheet thickness).</summary>
        public double Thickness { get; set; } = 0.006;
        /// <summary>Laths stand upright on the surface (asymptotic gridshells).</summary>
        public bool Upright { get; set; } = true;
        /// <summary>Auto: slots for upright laths, holes for flat ones.</summary>
        public JointKind Joint { get; set; } = JointKind.Auto;
        /// <summary>Pin hole diameter (0 = a quarter of the width).</summary>
        public double HoleDiameter { get; set; }
        /// <summary>Fit clearance added to slot widths and depths.</summary>
        public double Clearance { get; set; }
    }

    public readonly struct Mark
    {
        /// <summary>Position along the strip from its start (after the start extension).</summary>
        public readonly double At;
        public readonly MarkKind Kind;
        /// <summary>Slot width across the strip's length, or hole diameter.</summary>
        public readonly double Size;
        /// <summary>Slot depth from the strip edge (0 for holes).</summary>
        public readonly double Depth;
        /// <summary>The lath(s) joined here, e.g. "B7", or "hub".</summary>
        public readonly string Label;
        /// <summary>Joint number (1-based, as on the assembly drawing).</summary>
        public readonly int Joint;

        public Mark(double at, MarkKind kind, double size, double depth, string label, int joint)
        {
            At = at; Kind = kind; Size = size; Depth = depth; Label = label; Joint = joint;
        }
    }

    public sealed class Strip
    {
        public string Label = "";
        public int Lath;
        public bool FamilyA;
        /// <summary>Cut length including the end extensions.</summary>
        public double Length;
        public double Width;
        /// <summary>Material added before the first and after the last lath point so end joints are not cut open.</summary>
        public double ExtendStart, ExtendEnd;
        public List<Mark> Marks = new List<Mark>();
    }

    public sealed class Joint
    {
        public int Id;
        public int Node;
        /// <summary>Flat position in the assembly plane (u, v).</summary>
        public double U, V;
        public int[] Laths = Array.Empty<int>();
        public bool Hub;
        /// <summary>Smallest crossing angle (degrees) over the motion.</summary>
        public double MinAngle;
    }

    public sealed class Result
    {
        public ScissorNet Net = null!;
        public ScissorNet.Result Motion = null!;
        public List<Strip> Strips = new List<Strip>();
        public List<Joint> Joints = new List<Joint>();
        /// <summary>Flat lath polylines in the assembly plane (u, v), family A first.</summary>
        public List<(double U, double V)[]> Flat = new List<(double, double)[]>();
        public string[] LathLabels = Array.Empty<string>();
        /// <summary>Origin and axes of the assembly plane (the anchor and its tangent plane).</summary>
        public Vec3d Origin, AxisU, AxisV, Normal;
        /// <summary>Deployment: lath polylines per state, from the flat grid (0) to the curved net.</summary>
        public List<List<Vec3d[]>> Deployment = new List<List<Vec3d[]>>();
        /// <summary>Largest out-of-plane distance of the flat state and largest joint-spacing drift.</summary>
        public double Flatness, Drift;
        public double TotalLength => Strips.Sum(s => s.Length);
    }

    /// <summary>
    /// Builds the scissor net of two lath families (joints only: the strips
    /// keep the arc length between joints, the drawing their chords), presses it flat about the node nearest to
    /// <paramref name="anchor"/> onto the plane of normal
    /// <paramref name="normal"/>, and returns the net with its motion
    /// (state 0 the curved net, the last state flat).
    /// </summary>
    public static (ScissorNet net, ScissorNet.Result motion, int anchor) Press(IReadOnlyList<Vec3d[]> familyA, IReadOnlyList<Vec3d[]> familyB,
        Vec3d anchor, Vec3d normal, Func<Vec3d, Vec3d>? normalAt = null, int steps = 8, double stiffness = 0.3, int maxIterations = 60, Func<bool>? cancel = null)
    {
        var topo = NetTopology.Build(familyA, familyB, NetIntersections.FindAll(familyA, familyB, 0));
        var net = ScissorNet.FromTopology(topo, familyA.Count + familyB.Count, familyA.Count, 0, normalAt);
        var opt = new ScissorNet.Options { Fairness = stiffness, Steps = Math.Max(1, steps), MaxIterations = maxIterations, Cancel = cancel };
        int a = net.NearestNode(anchor);
        net.PressFlat(opt, a, normal);
        return (net, net.Solve(opt), a);
    }

    /// <summary>The kit from a pressed net (see <see cref="Press"/>): the last state is the flat assembly.</summary>
    public static Result Build(ScissorNet net, ScissorNet.Result motion, int anchor, Vec3d normal, Options? options = null)
    {
        var o = options ?? new Options();
        var r = new Result { Net = net, Motion = motion };
        var flat = motion.Last;
        int L = net.Laths.Count;
        int nA = net.CountA;
        r.LathLabels = Enumerable.Range(0, L).Select(l => l < nA ? $"A{l + 1}" : $"B{l - nA + 1}").ToArray();

        // assembly plane: through the anchor (the fixed node), axes from the normal
        var nz = normal.Normalized();
        r.Origin = flat.Nodes[anchor];
        r.Normal = nz;
        var ax = Math.Abs(nz.Z) < 0.9 ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
        r.AxisU = (ax - Vec3d.Dot(ax, nz) * nz).Normalized();
        if (Math.Abs(nz.Z) >= 0.9) r.AxisU = (new Vec3d(1, 0, 0) - nz.X * nz).Normalized();
        r.AxisV = Vec3d.Cross(nz, r.AxisU);
        (double, double) Uv(Vec3d p) { var d = p - r.Origin; return (Vec3d.Dot(d, r.AxisU), Vec3d.Dot(d, r.AxisV)); }
        foreach (var lath in net.Laths) r.Flat.Add(lath.Select(v => Uv(flat.Nodes[v])).ToArray());
        r.Flatness = flat.Nodes.Max(p => Math.Abs(Vec3d.Dot(p - r.Origin, nz)));
        r.Drift = flat.LengthDrift;

        // smallest crossing angle of every joint over the motion (slot width)
        var minAngle = Enumerable.Repeat(90.0, net.Nodes.Length).ToArray();
        foreach (var st in motion.States)
        {
            var a = net.CrossingAngles(st);
            for (int v = 0; v < a.Length; v++) if (!double.IsNaN(a[v])) minAngle[v] = Math.Min(minAngle[v], a[v]);
        }

        // joints: nodes on two or more laths, numbered outward from the anchor
        var jointNodes = Enumerable.Range(0, net.Nodes.Length).Where(v => net.NodeLaths[v].Length >= 2)
            .OrderBy(v => (flat.Nodes[v] - r.Origin).Length).ThenBy(v => Math.Atan2(Uv(flat.Nodes[v]).Item2, Uv(flat.Nodes[v]).Item1)).ToList();
        var jointId = new Dictionary<int, int>();
        foreach (int v in jointNodes)
        {
            var (u, w) = Uv(flat.Nodes[v]);
            var j = new Joint { Id = r.Joints.Count + 1, Node = v, U = u, V = w, Laths = net.NodeLaths[v], Hub = net.NodeLaths[v].Length >= 3, MinAngle = minAngle[v] };
            r.Joints.Add(j); jointId[v] = j.Id;
        }

        bool slots = o.Joint == JointKind.Slot || (o.Joint == JointKind.Auto && o.Upright);
        double hole = o.HoleDiameter > 0 ? o.HoleDiameter : 0.25 * o.Width;
        double depth = 0.5 * o.Width + 0.5 * o.Clearance;
        for (int l = 0; l < L; l++)
        {
            var lath = net.Laths[l];
            var strip = new Strip { Label = r.LathLabels[l], Lath = l, FamilyA = l < nA, Width = o.Width };
            double s = 0;
            var raw = new List<(double at, MarkKind kind, double size, double depth, string label, int joint)>();
            for (int i = 0; i < lath.Length; i++)
            {
                if (i > 0) s += net.RestArcs[l][i - 1];
                int v = lath[i];
                if (!jointId.TryGetValue(v, out int id)) continue;
                var others = net.NodeLaths[v].Where(x => x != l).ToArray();
                if (others.Length >= 2) { raw.Add((s, MarkKind.Hub, hole, 0, "hub", id)); continue; }
                string lbl = r.LathLabels[others[0]];
                if (slots)
                {
                    double sin = Math.Max(Math.Sin(minAngle[v] * Math.PI / 180), 0.2);
                    double wSlot = o.Thickness / sin + o.Clearance;
                    raw.Add((s, l < nA ? MarkKind.SlotOuter : MarkKind.SlotInner, wSlot, depth, lbl, id));
                }
                else raw.Add((s, MarkKind.Hole, hole, 0, lbl, id));
            }
            double len = s;
            // material beyond an end joint: half a width past a hole, a slot width past a slot
            double Need(MarkKind k, double size) => k == MarkKind.SlotOuter || k == MarkKind.SlotInner ? size : 0.5 * o.Width + 0.5 * size;
            double e0 = 0, e1 = 0;
            foreach (var m in raw)
            {
                double need = Need(m.kind, m.size);
                e0 = Math.Max(e0, need - m.at);
                e1 = Math.Max(e1, need - (len - m.at));
            }
            strip.ExtendStart = Math.Max(0, e0); strip.ExtendEnd = Math.Max(0, e1);
            strip.Length = len + strip.ExtendStart + strip.ExtendEnd;
            foreach (var m in raw) strip.Marks.Add(new Mark(m.at + strip.ExtendStart, m.kind, m.size, m.depth, m.label, m.joint));
            r.Strips.Add(strip);
        }

        // deployment: from the flat grid back to the curved net
        for (int k = motion.States.Count - 1; k >= 0; k--) r.Deployment.Add(net.LathPolylines(motion.States[k]));
        return r;
    }
}
