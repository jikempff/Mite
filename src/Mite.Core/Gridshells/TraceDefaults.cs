using System;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Scale-aware defaults for curve tracing. Absolute defaults (a step of 0.01
/// with 1000 steps) only suit metre-scale models: on a millimetre model they
/// trace 10 mm and every curve is discarded as a stub. Passing 0 for a step
/// or step count selects a value derived from the mesh itself.
/// </summary>
public static class TraceDefaults
{
    /// <summary>Hard cap on automatic step counts.</summary>
    public const int MaxAutoSteps = 200_000;

    /// <summary>
    /// Step length: the given value when positive; otherwise a tenth of the
    /// spacing (when a spacing is known), never coarser than half an average
    /// mesh edge, and a quarter edge when no spacing is known.
    /// </summary>
    public static double ResolveStep(double step, double spacing, MeshProjection proj)
    {
        if (step > 0) return step;
        double edge = Math.Max(proj.AverageEdgeLength, 1e-12);
        if (spacing > 0) return Math.Min(0.1 * spacing, 0.5 * edge);
        return 0.25 * edge;
    }

    /// <summary>
    /// Step count: the given value when positive; otherwise enough steps to
    /// cross the mesh's bounding-box diagonal three times.
    /// </summary>
    public static int ResolveMaxSteps(int maxSteps, double step, MeshProjection proj)
    {
        if (maxSteps > 0) return maxSteps;
        double diag = Math.Max(proj.Mesh.BoundingBoxDiagonal(), step);
        double n = Math.Ceiling(3.0 * diag / Math.Max(step, 1e-300));
        return (int)Math.Min(MaxAutoSteps, Math.Max(100, n));
    }

    /// <summary>Spacing: the given value when positive; otherwise the bounding-box diagonal / 30.</summary>
    public static double ResolveSpacing(double spacing, MeshProjection proj)
    {
        if (spacing > 0) return spacing;
        return Math.Max(proj.Mesh.BoundingBoxDiagonal() / 30.0, 4.0 * proj.AverageEdgeLength);
    }
}
