using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// Turns two lath families and their crossings into a structural graph:
/// unique nodes at the crossings and one member per lath piece between
/// consecutive crossings (plus the free tails before the first and after the
/// last crossing). This is the input a frame or FE package wants — beams
/// between nodes — and the basis of a node schedule for fabrication.
/// </summary>
public static class NetTopology
{
    public readonly struct Member
    {
        /// <summary>Global lath index: family A laths first, then family B.</summary>
        public readonly int Lath;

        /// <summary>True for family A.</summary>
        public readonly bool FamilyA;

        /// <summary>Node indices at the member ends (-1 for a free lath end).</summary>
        public readonly int NodeStart, NodeEnd;

        /// <summary>Polyline of the member (follows the lath between its nodes).</summary>
        public readonly Vec3d[] Points;

        public readonly double Length;

        public Member(int lath, bool familyA, int nodeStart, int nodeEnd, Vec3d[] points, double length)
        {
            Lath = lath; FamilyA = familyA; NodeStart = nodeStart; NodeEnd = nodeEnd;
            Points = points; Length = length;
        }
    }

    public readonly struct Result
    {
        public readonly Vec3d[] Nodes;

        /// <summary>Number of members meeting at each node.</summary>
        public readonly int[] Valence;

        public readonly List<Member> Members;

        /// <summary>Per node: the crossing it was built from.</summary>
        public readonly NetIntersections.Crossing[] Crossings;

        public Result(Vec3d[] nodes, int[] valence, List<Member> members, NetIntersections.Crossing[] crossings)
        {
            Nodes = nodes; Valence = valence; Members = members; Crossings = crossings;
        }
    }

    /// <summary>
    /// Builds the graph. Crossings come from <see cref="NetIntersections.Find"/>
    /// on the same two families; members shorter than minLength (e.g.
    /// free tails) are dropped.
    /// </summary>
    public static Result Build(
        IReadOnlyList<Vec3d[]> familyA, IReadOnlyList<Vec3d[]> familyB,
        IReadOnlyList<NetIntersections.Crossing> crossings, double minLength = 0.0)
    {
        int na = familyA.Count;
        var nodes = new Vec3d[crossings.Count];
        for (int i = 0; i < crossings.Count; i++) nodes[i] = crossings[i].Point;

        // Per lath: (arc length, node) list
        var perLath = new List<(double arc, int node)>[na + familyB.Count];
        for (int i = 0; i < perLath.Length; i++) perLath[i] = new List<(double, int)>();

        var arcA = CumulativeArcs(familyA);
        var arcB = CumulativeArcs(familyB);

        for (int c = 0; c < crossings.Count; c++)
        {
            var x = crossings[c];
            if (x.CurveA >= 0 && x.CurveA < na)
                perLath[x.CurveA].Add((ArcAt(arcA[x.CurveA], x.SegmentA, x.ParamA), c));
            if (x.CurveB >= 0 && x.CurveB < familyB.Count)
                perLath[na + x.CurveB].Add((ArcAt(arcB[x.CurveB], x.SegmentB, x.ParamB), c));
        }

        var members = new List<Member>();
        var valence = new int[crossings.Count];
        for (int l = 0; l < perLath.Length; l++)
        {
            bool isA = l < na;
            var line = isA ? familyA[l] : familyB[l - na];
            var arc = isA ? arcA[l] : arcB[l - na];
            if (line.Length < 2) continue;
            double total = arc[arc.Length - 1];

            var list = perLath[l];
            list.Sort((p, q) => p.arc.CompareTo(q.arc));

            // Merge crossings that coincide on this lath (two curves of the
            // other family meeting at one point)
            var stations = new List<(double arc, int node)>();
            foreach (var s in list)
                if (stations.Count == 0 || s.arc - stations[stations.Count - 1].arc > 1e-9 * Math.Max(total, 1))
                    stations.Add(s);

            double prevArc = 0;
            int prevNode = -1;
            for (int k = 0; k <= stations.Count; k++)
            {
                double curArc = k < stations.Count ? stations[k].arc : total;
                int curNode = k < stations.Count ? stations[k].node : -1;
                if (curArc - prevArc > 1e-12)
                {
                    var pts = LathSegmentation.SubPolyline(line, arc, prevArc, curArc);
                    // Snap the member ends onto the node positions
                    if (prevNode >= 0) pts[0] = nodes[prevNode];
                    if (curNode >= 0) pts[pts.Length - 1] = nodes[curNode];
                    double len = curArc - prevArc;
                    if (len >= minLength)
                    {
                        members.Add(new Member(l, isA, prevNode, curNode, pts, len));
                        if (prevNode >= 0) valence[prevNode]++;
                        if (curNode >= 0) valence[curNode]++;
                    }
                }
                prevArc = curArc;
                prevNode = curNode;
            }
        }

        var xs = new NetIntersections.Crossing[crossings.Count];
        for (int i = 0; i < xs.Length; i++) xs[i] = crossings[i];
        return new Result(nodes, valence, members, xs);
    }

    private static double[][] CumulativeArcs(IReadOnlyList<Vec3d[]> family)
    {
        var arcs = new double[family.Count][];
        for (int c = 0; c < family.Count; c++)
        {
            var line = family[c];
            var arc = new double[Math.Max(line.Length, 1)];
            for (int i = 1; i < line.Length; i++)
                arc[i] = arc[i - 1] + (line[i] - line[i - 1]).Length;
            arcs[c] = arc;
        }
        return arcs;
    }

    private static double ArcAt(double[] arc, int segment, double param)
    {
        if (segment < 0 || segment + 1 >= arc.Length) return segment < 0 ? 0 : arc[arc.Length - 1];
        return arc[segment] + param * (arc[segment + 1] - arc[segment]);
    }
}
