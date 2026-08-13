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

        if (!profileA.Upright && !profileB.Upright)
        {
            // Flat strips: width across the surface, thickness through it.
            // Notch A is cut from the top of A, notch B from the bottom of B.
            double lenA = profileB.Width / sinAngle + 2.0 * clearance;
            double lenB = profileA.Width / sinAngle + 2.0 * clearance;
            double depthA = lap * profileA.Thickness + clearance;
            double depthB = lap * profileB.Thickness + clearance;

            Vec3d cA = point + (profileA.Offset + profileA.Thickness - 0.5 * depthA) * n;
            Vec3d cB = point + (profileB.Offset + 0.5 * depthB) * n;

            notchA = new NotchSolid(cA, ta, ga, n,
                0.5 * lenA, 0.5 * profileA.Width + clearance, 0.5 * depthA);
            notchB = new NotchSolid(cB, tb, gb, n,
                0.5 * lenB, 0.5 * profileB.Width + clearance, 0.5 * depthB);
            return true;
        }

        if (profileA.Upright && profileB.Upright)
        {
            // Upright strips (egg-crate): width stands along the normal. Notch A
            // is a slot from the top (far) edge of A, notch B from the bottom
            // (near) edge of B; both pass through the full strip thickness.
            double lenA = profileB.Thickness / sinAngle + 2.0 * clearance;
            double lenB = profileA.Thickness / sinAngle + 2.0 * clearance;
            double depthA = lap * profileA.Width + clearance;
            double depthB = lap * profileB.Width + clearance;

            Vec3d cA = point + (profileA.Offset + profileA.Width - 0.5 * depthA) * n;
            Vec3d cB = point + (profileB.Offset + 0.5 * depthB) * n;

            notchA = new NotchSolid(cA, ta, ga, n,
                0.5 * lenA, 0.5 * profileA.Thickness + clearance, 0.5 * depthA);
            notchB = new NotchSolid(cB, tb, gb, n,
                0.5 * lenB, 0.5 * profileB.Thickness + clearance, 0.5 * depthB);
            return true;
        }

        // Mixed flat/upright crossings are not a standard joint
        return false;
    }

    /// <summary>
    /// Builds the half-lap splice pair for rejoining two lath segments end to
    /// end (from LathSegmentation). The end of the upstream segment keeps its
    /// near-surface half over the splice length; the start of the downstream
    /// segment keeps its far half, so they overlap into full depth.
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

        if (!profile.Upright)
        {
            // Flat: splice overlaps over spliceLength, each side loses half the thickness
            double depth = 0.5 * profile.Thickness + clearance;
            // End of upstream segment: remove the top half over [cut - L, cut]
            Vec3d cEnd = cutPoint - 0.5 * spliceLength * t
                + (profile.Offset + profile.Thickness - 0.5 * depth) * n;
            // Start of downstream segment: remove the bottom half over [cut, cut + L]
            Vec3d cStart = cutPoint + 0.5 * spliceLength * t
                + (profile.Offset + 0.5 * depth) * n;

            double halfAcross = 0.5 * profile.Width + clearance;
            endNotch = new NotchSolid(cEnd, t, g, n,
                0.5 * spliceLength + clearance, halfAcross, 0.5 * depth);
            startNotch = new NotchSolid(cStart, t, g, n,
                0.5 * spliceLength + clearance, halfAcross, 0.5 * depth);
            return true;
        }
        else
        {
            // Upright: same idea along the standing width
            double depth = 0.5 * profile.Width + clearance;
            Vec3d cEnd = cutPoint - 0.5 * spliceLength * t
                + (profile.Offset + profile.Width - 0.5 * depth) * n;
            Vec3d cStart = cutPoint + 0.5 * spliceLength * t
                + (profile.Offset + 0.5 * depth) * n;

            double halfThick = 0.5 * profile.Thickness + clearance;
            endNotch = new NotchSolid(cEnd, t, g, n,
                0.5 * spliceLength + clearance, halfThick, 0.5 * depth);
            startNotch = new NotchSolid(cStart, t, g, n,
                0.5 * spliceLength + clearance, halfThick, 0.5 * depth);
            return true;
        }
    }
}
