using System;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// An axis-aligned box in a local orthonormal frame, used as a boolean solid
/// for cutting lath notches at net crossings.
/// </summary>
public readonly struct NotchSolid
{
    public readonly Vec3d Center;
    public readonly Vec3d AxisX, AxisY, AxisZ;
    public readonly double HalfX, HalfY, HalfZ;

    public NotchSolid(Vec3d center, Vec3d axisX, Vec3d axisY, Vec3d axisZ,
        double halfX, double halfY, double halfZ)
    {
        Center = center;
        AxisX = axisX; AxisY = axisY; AxisZ = axisZ;
        HalfX = halfX; HalfY = halfY; HalfZ = halfZ;
    }

    /// <summary>True when the solid has no volume: that lath is not cut at this crossing.</summary>
    public bool IsEmpty => HalfX <= 0 || HalfY <= 0 || HalfZ <= 0;

    /// <summary>Volume of the box (0 when empty).</summary>
    public double Volume => IsEmpty ? 0.0 : 8.0 * HalfX * HalfY * HalfZ;
}

/// <summary>
/// What a crossing's joint is made of.
/// </summary>
public enum JointKind
{
    /// <summary>Both laths have the same orientation: a half-lap (flat) or egg-crate slot pair (upright), each lath notched over the other's footprint.</summary>
    Lap,
    /// <summary>Mixed orientation: lath A stands upright and the flat lath B threads through a slot (or a mortise) in A; B is not cut.</summary>
    SlotInA,
    /// <summary>Mixed orientation: lath B stands upright and the flat lath A threads through a slot (or a mortise) in B; A is not cut.</summary>
    SlotInB,
    /// <summary>Mixed orientation and the two laths do not overlap along the surface normal: nothing is cut, the laths pass each other and need a block or bolt.</summary>
    None,
}

/// <summary>
/// Builds lap-joint notch solids for lath crossings. Two laths crossing on a
/// surface interpenetrate; a lap joint removes the top half of the lower lath
/// and the bottom half of the upper one so they mate. For upright (egg-crate)
/// laths the notch becomes a slot cut from the far edge of one lath and the
/// near edge of the other. The solids are meant to be boolean-subtracted from
/// the swept laths on the host side.
///
/// A crossing of an upright lath with a flat one — the asymptotic–geodesic
/// hybrid (AGH) node: asymptotic lamellas stand normal to the surface, geodesic
/// slats lie tangent to it — is a threaded joint instead: the flat slat passes
/// through the upright lamella and is not cut itself. Literature: Schling,
/// Wang, Hoyer &amp; Pottmann 2022, "Designing asymptotic geodesic hybrid
/// gridshells", Computer-Aided Design 152 — §2.2 (geodesic strips with their
/// osculating planes normal to the surface, asymptotic strips with them
/// tangent), §4.2 (timber AGG web: the geodesic slats are "interwoven" between
/// the top and bottom asymptotic lamellas and the families are connected by
/// bolts threaded through the gaps; the geodesics connect to the asymptotic
/// slats at mid points between two nodes), §1.2 (the earlier steel web was
/// "notched and assembled in a single layer"); Schling, Wan, Wang &amp;
/// D'Acunto 2023, "Asymptotic Geodesic Hybrid Timber Gridshell", AAG 2023
/// ("the flat geodesic planks are interlaced at the midpoint of the asymptotic
/// beams"); Hoyer, D'Acunto &amp; Schling 2024, "Timber gridshells: challenges
/// and strategies in fabrication and assembly", IASS 2024 (12 × 100 mm ash
/// lamellas, the geodesic slats "slide through the asymptotic grid" during the
/// kinetic forming, blocks between the twin lamellas). In Mite a lath is one
/// strip, so the threading becomes a slot through the upright strip over the
/// flat strip's footprint and over the normal interval the two sections share:
/// open on an edge when the flat strip reaches that edge, a mortise when it
/// runs through the middle. The flat strip keeps its full section (it carries
/// the in-surface stiffness of the hybrid and slides through during erection);
/// Lap has no meaning there.
/// </summary>
public static class JointGeometry
{
    /// <summary>
    /// Builds the notch pair for one crossing. point lies on the surface at the
    /// crossing; tangents are the lath centerline directions; surfaceNormal
    /// orients "up" (away from the surface). lap is the notched fraction of the
    /// profile depth (0.5 = half-lap); clearance loosens the fit on each side.
    /// Returns false when the laths are too parallel to form a joint.
    /// </summary>
    public static bool TryBuildLapNotches(
        Vec3d point, Vec3d tangentA, Vec3d tangentB, Vec3d surfaceNormal,
        LathProfile profileA, LathProfile profileB,
        double lap, double clearance,
        out NotchSolid notchA, out NotchSolid notchB)
        => TryBuildNotches(point, tangentA, tangentB, surfaceNormal, profileA, profileB, lap, clearance,
            out notchA, out notchB, out _);

    /// <summary>
    /// Builds the notch pair for one crossing and says what kind of joint it
    /// is. Same orientation: the lap / egg-crate pair of
    /// <see cref="TryBuildLapNotches"/> (<see cref="JointKind.Lap"/>). Mixed
    /// orientation (one lath upright, the other flat — the AGH node): the
    /// upright lath gets a slot over the flat lath's footprint and over the
    /// normal interval the two sections share, the flat lath is not cut
    /// (<see cref="JointKind.SlotInA"/> / <see cref="JointKind.SlotInB"/>,
    /// the empty notch has <see cref="NotchSolid.IsEmpty"/>); when the two
    /// sections do not overlap along the normal nothing is cut
    /// (<see cref="JointKind.None"/>, both notches empty) — the laths pass
    /// each other and need a block or a bolt. Returns false only when the
    /// laths are too parallel to form a joint or the normal is degenerate.
    /// </summary>
    public static bool TryBuildNotches(
        Vec3d point, Vec3d tangentA, Vec3d tangentB, Vec3d surfaceNormal,
        LathProfile profileA, LathProfile profileB,
        double lap, double clearance,
        out NotchSolid notchA, out NotchSolid notchB, out JointKind kind)
    {
        notchA = default;
        notchB = default;
        kind = JointKind.Lap;

        if (surfaceNormal.LengthSquared < 1e-20) return false;
        Vec3d n = surfaceNormal.Normalized();

        Vec3d ta = tangentA - Vec3d.Dot(tangentA, n) * n;
        Vec3d tb = tangentB - Vec3d.Dot(tangentB, n) * n;
        if (ta.LengthSquared < 1e-20 || tb.LengthSquared < 1e-20) return false;
        ta = ta.Normalized();
        tb = tb.Normalized();

        double sinAngle = Vec3d.Cross(ta, tb).Length;
        if (sinAngle < 1e-3) return false; // too parallel to cross meaningfully

        lap = Math.Max(0.0, Math.Min(1.0, lap));
        Vec3d ga = Vec3d.Cross(n, ta);
        Vec3d gb = Vec3d.Cross(n, tb);

        if (profileA.Upright != profileB.Upright)
        {
            // Threaded joint: the flat lath passes through the upright one.
            bool slotInA = profileA.Upright;
            LathProfile up = slotInA ? profileA : profileB;     // the slotted (upright) lath
            LathProfile flat = slotInA ? profileB : profileA;   // the lath that threads through
            Vec3d tFlat = slotInA ? tb : ta;
            Vec3d gFlat = slotInA ? gb : ga;
            double cosMixed = Math.Abs(Vec3d.Dot(ta, tb));

            double upLow = up.FaceLow, upHigh = up.FaceLow + up.NormalDepth;
            double flatLow = flat.FaceLow, flatHigh = flat.FaceLow + flat.NormalDepth;
            double z0 = Math.Max(upLow, flatLow), z1 = Math.Min(upHigh, flatHigh);
            double eps = 1e-9 * Math.Max(up.NormalDepth, flat.NormalDepth);
            if (z1 <= z0 + eps)
            {
                kind = JointKind.None;
                return true;
            }

            // Along the flat lath: long enough to pass through the upright
            // lath's thickness at the crossing angle (the overlap is a
            // parallelogram of length t_up / sin θ + w_flat |cos θ| / sin θ
            // along the flat lath); across: the flat lath's width.
            double footUp = up.Thickness;     // in-surface footprint of the upright lath
            double footFlat = flat.Width;     // in-surface footprint of the flat lath
            double span = footUp / sinAngle + footFlat * cosMixed / sinAngle;
            // Bleed past the upright lath's edge where the slot opens on it;
            // only the clearance where it stays inside (a mortise).
            double bleed = 0.05 * up.NormalDepth + clearance;
            double bLow = z0 <= upLow + eps ? bleed : clearance;
            double bHigh = z1 >= upHigh - eps ? bleed : clearance;
            double zLo = z0 - bLow, zHi = z1 + bHigh;
            Vec3d c = point + 0.5 * (zLo + zHi) * n;
            var slot = new NotchSolid(c, tFlat, gFlat, n,
                0.5 * span + clearance, 0.5 * footFlat + clearance, 0.5 * (zHi - zLo));
            if (slotInA) { notchA = slot; kind = JointKind.SlotInA; }
            else { notchB = slot; kind = JointKind.SlotInB; }
            return true;
        }

        // Each notch removes exactly the volume the other lath occupies, so it
        // is aligned with the OTHER lath's frame: across it the other lath's
        // footprint (plus clearance), along it long enough to span this lath's
        // full width at the crossing angle (the overlap is a parallelogram
        // whose length along the other lath is w/sinθ + w'|cotθ|; a box
        // aligned with this lath's own tangent would leave corner slivers at
        // oblique crossings). Extra length only cuts air.
        // The notch bleeds slightly past the outer face it opens on, so the
        // boolean never has to resolve a coplanar face.
        double footA = profileA.Upright ? profileA.Thickness : profileA.Width;   // in-surface footprint
        double footB = profileB.Upright ? profileB.Thickness : profileB.Width;
        double stackA = profileA.NormalDepth;                                      // extent along the normal
        double stackB = profileB.NormalDepth;
        double cosAngle = Math.Abs(Vec3d.Dot(ta, tb));

        double spanA = footA / sinAngle + footB * cosAngle / sinAngle;  // along tb, covering A's footprint
        double spanB = footB / sinAngle + footA * cosAngle / sinAngle;  // along ta, covering B's footprint

        double depthA = lap * stackA + clearance;
        double depthB = lap * stackB + clearance;
        double bleedA = 0.05 * stackA + clearance;
        double bleedB = 0.05 * stackB + clearance;

        // Notch A: cut from the top (far) face of A over the footprint of B
        Vec3d cA = point + (profileA.FaceLow + stackA + bleedA - 0.5 * (depthA + bleedA)) * n;
        notchA = new NotchSolid(cA, tb, gb, n,
            0.5 * spanA + clearance, 0.5 * footB + clearance, 0.5 * (depthA + bleedA));

        // Notch B: cut from the bottom (near) face of B over the footprint of A
        Vec3d cB = point + (profileB.FaceLow - bleedB + 0.5 * (depthB + bleedB)) * n;
        notchB = new NotchSolid(cB, ta, ga, n,
            0.5 * spanB + clearance, 0.5 * footA + clearance, 0.5 * (depthB + bleedB));
        return true;
    }

    /// <summary>
    /// Builds the half-lap splice pair for rejoining two lath segments (from
    /// LathSegmentation with overlap = spliceLength). Over the splice length
    /// centred on the cut, the upstream segment keeps its near-surface half
    /// and the downstream segment keeps its far half, so they lap into full
    /// depth.
    /// </summary>
    public static bool TryBuildSpliceNotches(
        Vec3d cutPoint, Vec3d tangent, Vec3d surfaceNormal,
        LathProfile profile, double spliceLength, double clearance,
        out NotchSolid endNotch, out NotchSolid startNotch)
    {
        endNotch = default;
        startNotch = default;

        if (surfaceNormal.LengthSquared < 1e-20 || spliceLength <= 0) return false;
        Vec3d n = surfaceNormal.Normalized();
        Vec3d t = tangent - Vec3d.Dot(tangent, n) * n;
        if (t.LengthSquared < 1e-20) return false;
        t = t.Normalized();
        Vec3d g = Vec3d.Cross(n, t);

        // Both notches are centred on the cut over the splice length: the
        // upstream piece (which ends at cut + L/2 after segmentation overlap)
        // loses its far half there, the downstream piece (starting at
        // cut - L/2) loses its near half, so they lap into full depth.
        double stack = profile.NormalDepth;
        double halfAcross = 0.5 * (profile.Upright ? profile.Thickness : profile.Width) + clearance;
        double depth = 0.5 * stack + clearance;
        double bleed = 0.05 * stack + clearance;

        Vec3d cEnd = cutPoint + (profile.FaceLow + stack + bleed - 0.5 * (depth + bleed)) * n;
        Vec3d cStart = cutPoint + (profile.FaceLow - bleed + 0.5 * (depth + bleed)) * n;

        endNotch = new NotchSolid(cEnd, t, g, n,
            0.5 * spliceLength + clearance, halfAcross, 0.5 * (depth + bleed));
        startNotch = new NotchSolid(cStart, t, g, n,
            0.5 * spliceLength + clearance, halfAcross, 0.5 * (depth + bleed));
        return true;
    }
}
