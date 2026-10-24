using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;

namespace Mite.Core.Fabrication;

/// <summary>
/// The hub plate of a flat kit: the laser-cut connector of a singular node,
/// where three or more laths of an asymptotic web end at one point (the six
/// rays of the flat point of a 3-fold Enneper web; Schling, Wang, Hoyer &amp;
/// Pottmann 2022 call them singular vertices of valence N ≥ 6, at flat points,
/// where the diagonals break at the rays). Two upright strips cross in a
/// halving slot; six cannot — every strip would have to pass through the same
/// point — so the strips stop short of the centre and a plate holds them.
///
/// Design, in the assembly plane of the hub (model units; n rays with flat
/// directions e_k, gaps α_k between neighbouring rays, strip thickness t,
/// width w, fit clearance c, plate thickness t_p):
///
///  1. Slot width s = t + c. The hub's angles do not move: at a singular node
///     the scissor mechanism keeps the ray angles (the plate is a rigid body
///     through the whole deployment — <see cref="AngleDrift"/> measures how
///     well the kinematic model agrees), so the slot needs no 1/sin α widening
///     as the scissor slots do (Cichocka 2024 sizes those for a 90° → 75°
///     rotation plus the kerf).
///  2. Strip ends: two strips at angle α clash where their centre lines are
///     closer than t, i.e. inside r = (t + c) / (2 sin(α/2)) (two strips 60°
///     apart: r = t + c); with a bolt through the centre the strips also stay a
///     bridge away from the bolt hole. <see cref="EndRadius"/> is the larger.
///  3. Key: every strip is notched from the edge the plate sits on, t_p + c
///     deep over the key length k = max(1.5 s, 2 t_p, w / 10) from its end;
///     the plate's central disc enters that notch when the strip is dropped
///     into its radial slot, so the strip is locked radially (the step at r_in
///     bears on the slot's inner end) and sits on the plate — the press-fit
///     practice of laser-cut kits (Fab Academy: parametric slots and notches,
///     measured kerf, a test cut). The plate's slots run from the rim to
///     r_in = r_end + k, or farther out when neighbouring slots would leave
///     less than a bridge of t_p between them (SendCutSend: bridges ≥ 1× the
///     material thickness; slots ≥ 1.5× their width from other features).
///  4. Radius R: given, or 0.45 of the distance to the first joint on the
///     rays, but at least r_in + w so the slot guides the strip over its own
///     width; <see cref="Cramped"/> when the plate would reach the first
///     joint's slot (a larger Spacing or a narrower strip fixes it).
///  5. One plate sits flush with the inner edge of the strips (notch from the
///     inner edge: as the B slots of the kit); two plates make a sandwich,
///     one flush with each edge, bolted through the centre, which is what
///     locks the strips in every direction (Schling, Hitrec &amp; Barthel 2017
///     hold their steel nodes with a bolt and star-shaped washers for the same
///     reason: the slotted strips alone fix angle and position, not the
///     stacking). The two plates are identical.
///
/// Hub plates exist for upright strips only; flat strips (pin holes) keep the
/// common pin hole of <see cref="FlatKit.MarkKind.Hub"/>, since flat strips
/// converging on one point overlap and need a stacked hub instead.
///
/// Literature: Schling, Hitrec &amp; Barthel 2017, Designing Grid Structures
/// Using Asymptotic Curve Networks (singularities of valence ≠ 4; double slots,
/// star washers and a bolt at every node of the steel prototype); Schling,
/// Wang, Hoyer &amp; Pottmann 2022, Designing asymptotic geodesic hybrid
/// gridshells, CAD 152 (singular vertices at flat points); Schling &amp;
/// Schikore 2022, Morphology of Kinetic Asymptotic Grids (singularities
/// constrain the motion); Cichocka 2024, Stiffness-Scaled Models for
/// Asymptotic Gridshells, IASS 2024 (slot tolerances and kerf for laser-cut
/// plywood kits); Fab Academy, Press-fit construction; SendCutSend, Basic
/// tolerances and cut feature relationships (bridges, slot distances).
/// </summary>
public sealed class HubPlate
{
    public sealed class Ray
    {
        /// <summary>Lath index in the kit's net and its strip label.</summary>
        public int Lath;
        public string Label = "";
        /// <summary>Direction of the ray in the assembly plane (radians, from axis U) in the flat state.</summary>
        public double Angle;
        /// <summary>Arc length from the hub to the first joint along this lath (model units).</summary>
        public double FirstJoint;
        /// <summary>The hub is the lath's start node (else its end).</summary>
        public bool AtStart;
    }

    /// <summary>Joint number of the hub on the assembly drawing and its node in the net.</summary>
    public int Joint, Node;
    /// <summary>Centre of the plate in the assembly plane (u, v).</summary>
    public double U, V;
    public List<Ray> Rays = new List<Ray>();
    /// <summary>Plate radius R.</summary>
    public double Radius;
    /// <summary>Inner end of the radial slots (the central disc).</summary>
    public double InnerRadius;
    /// <summary>Where the strips end, measured from the centre.</summary>
    public double EndRadius;
    /// <summary>Slot width s = t + c.</summary>
    public double SlotWidth;
    /// <summary>Key length k: the notch in every strip, from its end.</summary>
    public double Key;
    /// <summary>Notch depth in the strips (t_p + c).</summary>
    public double NotchDepth;
    /// <summary>Plate thickness.</summary>
    public double Thickness;
    /// <summary>One plate (inner edge) or two (sandwich, bolted).</summary>
    public int Count;
    /// <summary>Bolt hole diameter through the centre (0 = none).</summary>
    public double Bolt;
    /// <summary>Smallest material bridge in the plate: between neighbouring slots at r_in, and from the bolt hole to the slots.</summary>
    public double MinBridge;
    /// <summary>Largest deviation of a gap between neighbouring rays from the regular 2π/n, degrees, in the flat state.</summary>
    public double AngleDeviation;
    /// <summary>Largest change of any gap between neighbouring rays over the whole motion, degrees (a rigid plate needs this small).</summary>
    public double AngleDrift;
    /// <summary>True when the radius had to be cut back to clear the first joints (the slots are shorter than the strip width).</summary>
    public bool Cramped;
    /// <summary>Radius the rays allow before the plate reaches the first joint's slot.</summary>
    public double FreeRadius;

    /// <summary>Material left on a strip between its two notches (two plates) or beyond the one (one plate).</summary>
    public double StripWeb(double width) => width - Count * NotchDepth;

    /// <summary>
    /// Closed outline of the plate in its own frame (centre at the origin, model units):
    /// the rim between the slots as arcs of at most <paramref name="arcStep"/> radians,
    /// every slot as two edges and its inner end.
    /// </summary>
    public List<(double X, double Y)> Outline(double arcStep = Math.PI / 90)
    {
        // counter-clockwise: at every ray the rim meets the slot's clockwise edge first,
        // down that edge to the inner end, across, up the other edge, then the rim arc to the next slot
        var pts = new List<(double, double)>();
        var rays = Rays.OrderBy(r => r.Angle).ToList();
        int n = rays.Count;
        double R = Radius, h = 0.5 * SlotWidth, xr = Math.Sqrt(Math.Max(R * R - h * h, 0));
        double dPhi = Math.Asin(Math.Min(h / R, 1));
        for (int k = 0; k < n; k++)
        {
            double a = rays[k].Angle, b = rays[(k + 1) % n].Angle;
            if (k == n - 1) b += 2 * Math.PI;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            pts.Add((xr * ca + h * sa, xr * sa - h * ca));
            pts.Add((InnerRadius * ca + h * sa, InnerRadius * sa - h * ca));
            pts.Add((InnerRadius * ca - h * sa, InnerRadius * sa + h * ca));
            pts.Add((xr * ca - h * sa, xr * sa + h * ca));
            double from = a + dPhi, to = b - dPhi;
            if (to > from)
            {
                int steps = Math.Max(1, (int)Math.Ceiling((to - from) / arcStep));
                for (int i = 1; i < steps; i++) { double t = from + (to - from) * i / steps; pts.Add((R * Math.Cos(t), R * Math.Sin(t))); }
            }
        }
        return pts;
    }

    /// <summary>Signed area of a closed polygon (shoelace).</summary>
    public static double Area(IReadOnlyList<(double X, double Y)> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++) { var p = poly[i]; var q = poly[(i + 1) % poly.Count]; a += p.X * q.Y - q.X * p.Y; }
        return 0.5 * a;
    }

    /// <summary>Exact area of the plate: the disc less n slots (rectangle to the rim chord plus the circular segment beyond it).</summary>
    public double ExactArea()
    {
        double R = Radius, h = 0.5 * SlotWidth, xr = Math.Sqrt(R * R - h * h);
        double theta = 2 * Math.Asin(h / R);
        double segment = 0.5 * R * R * (theta - Math.Sin(theta));
        return Math.PI * R * R - Rays.Count * (SlotWidth * (xr - InnerRadius) + segment) - 0.25 * Math.PI * Bolt * Bolt;
    }

    /// <summary>
    /// Builds the plate of hub <paramref name="node"/> from the pressed net: ray
    /// directions and first-joint distances in the flat state, angle drift over
    /// the motion, radii after the rules in the class summary. Returns null when
    /// a lath passes through the hub instead of ending there (no plate can hold it).
    /// </summary>
    public static HubPlate? Build(ScissorNet net, ScissorNet.Result motion, int node, int jointId,
        Func<Vec3d, (double U, double V)> uv, string[] lathLabels, FlatKit.Options o)
    {
        var flat = motion.Last;
        var laths = net.NodeLaths[node];
        if (laths.Length < 3) return null;
        var plate = new HubPlate { Joint = jointId, Node = node };
        (plate.U, plate.V) = uv(flat.Nodes[node]);
        double tp = o.HubThickness > 0 ? o.HubThickness : o.Thickness;
        int count = Math.Max(1, Math.Min(2, o.HubPlates));

        foreach (int l in laths)
        {
            var lath = net.Laths[l];
            int i = Array.IndexOf(lath, node);
            if (i != 0 && i != lath.Length - 1) return null; // a through-lath: the plate cannot hold it
            bool atStart = i == 0;
            int next = atStart ? lath[1] : lath[lath.Length - 2];
            var (u0, v0) = uv(flat.Nodes[node]); var (u1, v1) = uv(flat.Nodes[next]);
            // arc length to the first joint along the lath (rest arcs: what the strip carries)
            double first = 0; var arcs = net.RestArcs[l];
            if (atStart) { for (int j = 0; j < arcs.Length; j++) { first += arcs[j]; if (net.NodeLaths[lath[j + 1]].Length >= 2) break; } }
            else { for (int j = arcs.Length - 1; j >= 0; j--) { first += arcs[j]; if (net.NodeLaths[lath[j]].Length >= 2) break; } }
            plate.Rays.Add(new Ray { Lath = l, Label = lathLabels[l], Angle = Math.Atan2(v1 - v0, u1 - u0), FirstJoint = first, AtStart = atStart });
        }
        plate.Rays = plate.Rays.OrderBy(r => r.Angle).ToList();
        int n = plate.Rays.Count;

        // gaps between neighbouring rays in the flat state and along the motion
        double[] Gaps(ScissorNet.State st)
        {
            var ang = new double[n];
            var c = st.Nodes[node];
            // the plane of the hub in this state: its normal; angles measured in the tangent plane
            var nz = st.Normals[node].Normalized();
            var ax = Math.Abs(nz.Z) < 0.9 ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
            var ex = (ax - Vec3d.Dot(ax, nz) * nz).Normalized(); var ey = Vec3d.Cross(nz, ex);
            for (int k = 0; k < n; k++)
            {
                var r = plate.Rays[k]; var lath = net.Laths[r.Lath];
                var d = st.Nodes[r.AtStart ? lath[1] : lath[lath.Length - 2]] - c;
                ang[k] = Math.Atan2(Vec3d.Dot(d, ey), Vec3d.Dot(d, ex));
            }
            Array.Sort(ang);
            var g = new double[n];
            for (int k = 0; k < n; k++) { g[k] = ang[(k + 1) % n] - ang[k]; if (k == n - 1) g[k] += 2 * Math.PI; }
            return g;
        }
        var gFlat = Gaps(flat);
        double regular = 2 * Math.PI / n;
        plate.AngleDeviation = gFlat.Max(g => Math.Abs(g - regular)) * 180 / Math.PI;
        double drift = 0;
        var sortedFlat = gFlat.OrderBy(x => x).ToArray();
        foreach (var st in motion.States)
        {
            var g = Gaps(st).OrderBy(x => x).ToArray();
            for (int k = 0; k < n; k++) drift = Math.Max(drift, Math.Abs(g[k] - sortedFlat[k]));
        }
        plate.AngleDrift = drift * 180 / Math.PI;
        double alphaMin = gFlat.Min();

        // 1. slot width, 2. strip ends, 3. key and inner radius, 4. radius
        double s = o.Thickness + o.Clearance, bridge = tp;
        plate.SlotWidth = s; plate.Thickness = tp; plate.Count = count;
        plate.Bolt = count == 2 ? (o.HubBolt > 0 ? o.HubBolt : o.HoleDiameter > 0 ? o.HoleDiameter : 0.25 * o.Width) : Math.Max(0, o.HubBolt);
        double rEnd = s / (2 * Math.Sin(0.5 * alphaMin));
        if (plate.Bolt > 0) rEnd = Math.Max(rEnd, 0.5 * plate.Bolt + bridge);
        plate.EndRadius = rEnd;
        plate.Key = Math.Max(Math.Max(1.5 * s, 2 * tp), 0.1 * o.Width);
        plate.NotchDepth = tp + o.Clearance;
        double rIn = Math.Max(rEnd + plate.Key, (s + bridge) / (2 * Math.Sin(0.5 * alphaMin)));
        plate.InnerRadius = rIn;
        double firstMin = plate.Rays.Min(r => r.FirstJoint);
        // the first joint's own slot: the other strip's thickness at its smallest angle, as the kit sizes it
        double firstSlotHalf = 0.5 * (o.Thickness / 0.2 + o.Clearance);
        plate.FreeRadius = firstMin - firstSlotHalf - bridge;
        double R = o.HubRadius > 0 ? o.HubRadius : Math.Max(0.45 * firstMin, rIn + o.Width);
        if (o.HubRadius <= 0 && R > plate.FreeRadius) { R = Math.Max(plate.FreeRadius, rIn + 0.5 * o.Width); }
        plate.Cramped = R - rIn < o.Width || R > plate.FreeRadius;
        plate.Radius = R;
        // bridges: between neighbouring slots at r_in, and from the bolt to the slot ends
        double between = 2 * rIn * Math.Sin(0.5 * alphaMin) - s;
        plate.MinBridge = plate.Bolt > 0 ? Math.Min(between, rIn - 0.5 * plate.Bolt) : between;
        return plate;
    }
}
