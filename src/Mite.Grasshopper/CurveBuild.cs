using System.Collections.Generic;
using Rhino.Geometry;

namespace Mite.Grasshopper;

internal static class CurveBuild
{
    /// <summary>
    /// Builds a smooth degree-3 interpolated curve through traced on-surface
    /// points (periodic when the trace closed into a loop). Points on a mesh
    /// polyline necessarily kink at facet crossings; interpolation recovers the
    /// smooth curve of the underlying surface. Falls back to a polyline if
    /// interpolation fails.
    /// </summary>
    internal static Curve? Interpolated(List<Point3d> pts)
    {
        if (pts.Count < 2) return null;
        if (pts.Count == 2) return new LineCurve(pts[0], pts[1]);

        bool closed = pts[0].DistanceToSquared(pts[pts.Count - 1]) < 1e-18;
        Curve? curve;
        if (closed && pts.Count > 4)
        {
            var open = new List<Point3d>(pts.Count - 1);
            for (int i = 0; i < pts.Count - 1; i++) open.Add(pts[i]);
            curve = Curve.CreateInterpolatedCurve(open, 3, CurveKnotStyle.ChordPeriodic);
        }
        else
        {
            curve = Curve.CreateInterpolatedCurve(pts, 3, CurveKnotStyle.Chord);
        }

        return curve ?? new PolylineCurve(pts);
    }
}
