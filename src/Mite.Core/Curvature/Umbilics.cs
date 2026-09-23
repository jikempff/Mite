using System;
using System.Collections.Generic;

namespace Mite.Core.Curvature;

/// <summary>
/// Finds umbilical vertices: points where the two principal curvatures are
/// (nearly) equal and the principal directions are therefore undefined.
/// Direction fields rotate wildly around umbilics, which is what makes traced
/// curve networks chaotic there — flagging them lets users place net seeds
/// deliberately or mask the region.
/// </summary>
public static class Umbilics
{
    /// <summary>
    /// Returns the indices of umbilical vertices: those where
    /// |k1 - k2| &lt;= tolerance * max(|k1|, |k2|). Vertices with negligible
    /// curvature (flat) are not reported — the directions are meaningless but
    /// harmless there.
    /// </summary>
    public static int[] Find(PrincipalCurvature.Result curvature, double tolerance = 0.05)
    {
        var result = new List<int>();

        // Flatness is judged relative to the mesh's curvature range so the
        // test does not depend on model units
        double maxK = 0;
        for (int i = 0; i < curvature.K1.Length; i++)
            maxK = Math.Max(maxK, Math.Max(Math.Abs(curvature.K1[i]), Math.Abs(curvature.K2[i])));
        double flat = 1e-3 * maxK;

        for (int i = 0; i < curvature.K1.Length; i++)
        {
            double scale = Math.Max(Math.Abs(curvature.K1[i]), Math.Abs(curvature.K2[i]));
            if (scale <= flat) continue; // flat, not umbilical in any meaningful sense
            if (Math.Abs(curvature.K1[i] - curvature.K2[i]) <= tolerance * scale)
                result.Add(i);
        }
        return result.ToArray();
    }
}
