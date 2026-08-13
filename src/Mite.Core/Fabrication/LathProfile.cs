namespace Mite.Core.Fabrication;

/// <summary>
/// Rectangular cross-section of a gridshell lath swept along an on-surface
/// curve. The profile is always surface-aligned: in Flat mode the strip lies
/// on the surface (width across it in the tangent plane, thickness through it
/// along the normal — geodesic gridshells); in Upright mode the strip stands
/// on edge (width along the normal — asymptotic gridshells).
/// </summary>
public readonly struct LathProfile
{
    /// <summary>Strip dimension across the curve (tangent plane in Flat mode, normal direction in Upright mode).</summary>
    public double Width { get; }

    /// <summary>Strip dimension through the strip (normal direction in Flat mode, tangent plane in Upright mode).</summary>
    public double Thickness { get; }

    /// <summary>False: strip lies flat on the surface. True: strip stands upright on edge.</summary>
    public bool Upright { get; }

    /// <summary>Gap between the surface and the nearest strip face (0 = strip touches the surface).</summary>
    public double Offset { get; }

    public LathProfile(double width, double thickness, bool upright = false, double offset = 0.0)
    {
        Width = width;
        Thickness = thickness;
        Upright = upright;
        Offset = offset;
    }

    /// <summary>Depth of the profile measured along the surface normal.</summary>
    public double NormalDepth => Upright ? Width : Thickness;
}
