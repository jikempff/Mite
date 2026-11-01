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
        options ??= new EvenlySpacedNet.Options();
        var curvature = PrincipalCurvature.Compute(mesh);
        // progress: family A is the first half, family B the second
        var whole = new ProgressScope(options.Progress, "tracing");
        var progress = options.Progress;
        var scopeA = whole.Sub("tracing A", 0, 0.5);
        var scopeB = whole.Sub("tracing B", 0.5, 1);
        if (progress != null) options.Progress = (f, _) => scopeA.Report(f);
        var familyA = EvenlySpacedNet.TraceField(mesh, curvature.D1, null, firstSeed, options);
        bool capA = options.ReachedMaxCurves, cancelA = options.Cancelled;
        if (progress != null) options.Progress = (f, _) => scopeB.Report(f);
        var familyB = EvenlySpacedNet.TraceField(mesh, curvature.D2, null, firstSeed, options);
        options.Progress = progress;
        // Report the run status over both families
        options.ReachedMaxCurves |= capA;
        options.Cancelled |= cancelA;
        return new Result(familyA, familyB);
    }
}
