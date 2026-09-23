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
}

/// <summary>
/// Builds lap-joint notch solids for lath crossings. Two laths crossing on a
/// surface interpenetrate; a lap joint removes the top half of the lower lath
/// and the bottom half of the upper one so they mate. For upright (egg-crate)
/// laths the notch becomes a slot cut from the far edge of one lath and the
/// near edge of the other. The solids are meant to be boolean-subtracted from
/// the swept laths on the host side.
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
    {
        notchA = default;
        notchB = default;

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
            return false; // Mixed flat/upright crossings are not a standard joint

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
        Vec3d cA = point + (profileA.Offset + stackA + bleedA - 0.5 * (depthA + bleedA)) * n;
        notchA = new NotchSolid(cA, tb, gb, n,
            0.5 * spanA + clearance, 0.5 * footB + clearance, 0.5 * (depthA + bleedA));

        // Notch B: cut from the bottom (near) face of B over the footprint of A
        Vec3d cB = point + (profileB.Offset - bleedB + 0.5 * (depthB + bleedB)) * n;
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

        Vec3d cEnd = cutPoint + (profile.Offset + stack + bleed - 0.5 * (depth + bleed)) * n;
        Vec3d cStart = cutPoint + (profile.Offset - bleed + 0.5 * (depth + bleed)) * n;

        endNotch = new NotchSolid(cEnd, t, g, n,
            0.5 * spliceLength + clearance, halfAcross, 0.5 * (depth + bleed));
        startNotch = new NotchSolid(cStart, t, g, n,
            0.5 * spliceLength + clearance, halfAcross, 0.5 * (depth + bleed));
        return true;
    }
}
