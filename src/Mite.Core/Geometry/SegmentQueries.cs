using System;

namespace Mite.Core.Geometry;

internal static class SegmentQueries
{
    /// <summary>
    /// Closest points between two segments (Ericson, Real-Time Collision
    /// Detection 5.1.9). Returns the distance; s and t are the parameters of
    /// the closest points on [p1,q1] and [p2,q2].
    /// </summary>
    internal static double SegmentSegment(
        Vec3d p1, Vec3d q1, Vec3d p2, Vec3d q2,
        out double s, out double t, out Vec3d c1, out Vec3d c2)
    {
        Vec3d d1 = q1 - p1;
        Vec3d d2 = q2 - p2;
        Vec3d r = p1 - p2;
        double a = Vec3d.Dot(d1, d1);
        double e = Vec3d.Dot(d2, d2);
        double f = Vec3d.Dot(d2, r);

        const double eps = 1e-15;
        if (a <= eps && e <= eps)
        {
            s = t = 0.0;
            c1 = p1;
            c2 = p2;
            return (c1 - c2).Length;
        }
        if (a <= eps)
        {
            s = 0.0;
            t = Math.Max(0.0, Math.Min(1.0, f / e));
        }
        else
        {
            double c = Vec3d.Dot(d1, r);
            if (e <= eps)
            {
                t = 0.0;
                s = Math.Max(0.0, Math.Min(1.0, -c / a));
            }
            else
            {
                double b = Vec3d.Dot(d1, d2);
                double denom = a * e - b * b;
                s = denom > eps ? Math.Max(0.0, Math.Min(1.0, (b * f - c * e) / denom)) : 0.0;
                t = (b * s + f) / e;
                if (t < 0.0)
                {
                    t = 0.0;
                    s = Math.Max(0.0, Math.Min(1.0, -c / a));
                }
                else if (t > 1.0)
                {
                    t = 1.0;
                    s = Math.Max(0.0, Math.Min(1.0, (b - c) / a));
                }
            }
        }

        c1 = p1 + s * d1;
        c2 = p2 + t * d2;
        return (c1 - c2).Length;
    }
}
