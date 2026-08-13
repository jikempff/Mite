using System.Collections.Generic;
using Mite.Core.Curvature;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Conjugate curve networks from the principal direction field. Principal
/// directions are conjugate by definition (the second fundamental form
/// vanishes between them), so tracing both families with the evenly-spaced
/// seeder yields an approximate conjugate net — the layout whose quad cells
/// can be planarized into a planar-quad (PQ) panelization.
/// </summary>
public static class ConjugateNet
{
    public readonly struct Result
    {
        /// <summary>Curves following D1 (max curvature direction).</summary>
        public readonly List<Vec3d[]> FamilyA;

        /// <summary>Curves following D2 (min curvature direction).</summary>
        public readonly List<Vec3d[]> FamilyB;

        public Result(List<Vec3d[]> familyA, List<Vec3d[]> familyB)
        {
            FamilyA = familyA;
            FamilyB = familyB;
        }
    }

    /// <summary>
    /// Traces both principal families over the whole mesh. Pass firstSeed -1
    /// to start near the mesh centroid. Umbilical regions (k1 ≈ k2) have no
    /// meaningful principal direction — traces there follow numerical noise,
    /// so consider masking them out via the Umbilics component on real models.
    /// </summary>
    public static Result Trace(MeshData mesh, int firstSeed = -1, EvenlySpacedNet.Options? options = null)
    {
        var curvature = PrincipalCurvature.Compute(mesh);
        var familyA = EvenlySpacedNet.TraceField(mesh, curvature.D1, null, firstSeed, options);
        var familyB = EvenlySpacedNet.TraceField(mesh, curvature.D2, null, firstSeed, options);
        return new Result(familyA, familyB);
    }
}
