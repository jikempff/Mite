using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;

namespace Mite.Grasshopper;

/// <summary>
/// The shared "Shape / Section" inputs of Lath Sweep, Lath Analysis and
/// Gridshell Analysis: 0 = rectangle W × T, 1 = round bar of diameter W,
/// 2 = custom closed planar Section curve (its plane X = across the curve,
/// Y = surface normal; Upright swaps them). One profile applies to every
/// curve, and the same profile is what the strain check and the beam frame
/// are computed on (LathProfile.SectionProperties).
/// </summary>
internal static class SectionInput
{
    public const string ShapeDescription =
        "Section shape applied to every curve: 0 = rectangle W × T, 1 = round bar of diameter W, " +
        "2 = custom closed planar Section curve (its plane X = across / Y = surface normal; Upright swaps them)";

    public const string SectionDescription =
        "Closed planar section curve for Shape = 2 (drawn around the origin of its own plane, any units of the model)";

    /// <summary>
    /// Builds the profile. <paramref name="scale"/> multiplies every length
    /// (Gridshell Analysis works in metres). Returns false after reporting
    /// an error on the component.
    /// </summary>
    public static bool TryBuild(GH_Component component, int shape, Curve? section,
        double width, double thickness, bool upright, double offset, double scale, int curveCount, out LathProfile profile)
    {
        profile = default;
        switch (shape)
        {
            case 1:
                profile = LathProfile.Round(width * scale, 24, offset * scale);
                if (upright)
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Round sections have no upright/flat distinction; Upright is ignored.");
                return true;
            case 2:
                if (section == null || !section.IsClosed || !section.TryGetPlane(out Plane sectionPlane))
                {
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Shape 2 needs a closed planar Section curve.");
                    return false;
                }
                var poly = section.ToPolyline(0, 0, 0.05, 0.0, 0.0, Math.Max(1e-6, 0.01 * section.GetLength()), 0.0, 0.0, true)?.ToPolyline();
                if (poly == null || poly.Count < 4)
                {
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section curve could not be converted to a polygon.");
                    return false;
                }
                var pts2d = new List<(double, double)>();
                foreach (var pt in poly)
                {
                    sectionPlane.ClosestParameter(pt, out double sx, out double sy);
                    pts2d.Add((sx * scale, sy * scale));
                }
                try { profile = LathProfile.Custom(pts2d, upright, offset * scale); }
                catch (ArgumentException ex) { component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return false; }
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Custom section {profile.Width / scale:G4} × {profile.Thickness / scale:G4} (plane X × plane Y) applied to all {curveCount} curves.");
                return true;
            default:
                profile = new LathProfile(width * scale, thickness * scale, upright, offset * scale);
                return true;
        }
    }
}
