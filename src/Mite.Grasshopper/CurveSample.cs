using System.Collections.Generic;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper;

internal static class CurveSample
{
    /// <summary>
    /// Converts a Rhino curve to a polyline for the core algorithms: adaptive
    /// sampling by chord deviation, falling back to uniform division when the
    /// curve cannot be polyline-approximated.
    /// </summary>
    internal static Vec3d[]? ToPolyline(Curve curve, double tolerance)
    {
        Polyline? pl = null;
        try
        {
            if (curve.TryGetPolyline(out var direct))
            {
                pl = direct;
            }
            else
            {
                var plc = curve.ToPolyline(tolerance, 0.5, tolerance, 0.0);
                if (plc != null) pl = plc.ToPolyline();
            }
        }
        catch
        {
            pl = null;
        }

        if (pl == null || pl.Count < 2)
        {
            curve.DivideByCount(64, true, out Point3d[]? divPts);
            if (divPts == null || divPts.Length < 2) return null;
            pl = new Polyline(divPts);
        }

        var result = new Vec3d[pl.Count];
        for (int i = 0; i < pl.Count; i++)
            result[i] = new Vec3d(pl[i].X, pl[i].Y, pl[i].Z);
        return result;
    }

    internal static List<Vec3d[]> ToPolylines(IEnumerable<Curve> curves, double tolerance)
    {
        var lines = new List<Vec3d[]>();
        foreach (var c in curves)
        {
            var line = ToPolyline(c, tolerance);
            if (line != null) lines.Add(line);
        }
        return lines;
    }
}
