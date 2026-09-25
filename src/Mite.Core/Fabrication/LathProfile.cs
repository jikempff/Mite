using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Fabrication;

/// <summary>
/// Cross-section of a gridshell lath swept along an on-surface curve. The
/// profile is always surface-aligned: in Flat mode the section plane's first
/// axis lies across the curve in the tangent plane and its second axis along
/// the surface normal (geodesic gridshells, strips lie on the surface); in
/// Upright mode the first axis is the surface normal and the second the
/// in-surface across direction (asymptotic gridshells, strips stand on edge
/// — Schling, Hitrec &amp; Barthel 2017). The default section is a Width ×
/// Thickness rectangle; <see cref="Round"/> and <see cref="Custom"/> give a
/// circular or an arbitrary closed polygon section in the same frame, so one
/// profile applies to every curve of a net.
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

    /// <summary>
    /// Closed section polygon in the (first axis, second axis) plane, counter-
    /// clockwise, centred on the curve; null means the Width × Thickness
    /// rectangle. For a custom polygon Width and Thickness are its extents.
    /// </summary>
    public IReadOnlyList<(double A, double B)>? Section { get; }

    public LathProfile(double width, double thickness, bool upright = false, double offset = 0.0)
        : this(width, thickness, upright, offset, null) { }

    private LathProfile(double width, double thickness, bool upright, double offset, IReadOnlyList<(double, double)>? section)
    {
        Width = width;
        Thickness = thickness;
        Upright = upright;
        Offset = offset;
        Section = section;
    }

    /// <summary>The Width × Thickness rectangle (the default).</summary>
    public static LathProfile Rectangle(double width, double thickness, bool upright = false, double offset = 0.0) =>
        new LathProfile(width, thickness, upright, offset);

    /// <summary>A round bar of the given diameter (a regular polygon with <paramref name="segments"/> sides).</summary>
    public static LathProfile Round(double diameter, int segments = 24, double offset = 0.0)
    {
        if (diameter <= 0) throw new ArgumentException("Diameter must be positive.", nameof(diameter));
        segments = Math.Max(6, segments);
        var pts = new (double, double)[segments];
        double r = 0.5 * diameter;
        for (int i = 0; i < segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments;
            pts[i] = (r * Math.Cos(a), r * Math.Sin(a));
        }
        return new LathProfile(diameter, diameter, false, offset, pts);
    }

    /// <summary>
    /// An arbitrary closed section polygon given in the profile plane (first
    /// axis, second axis), e.g. the points of a planar Rhino curve mapped to
    /// its plane's X and Y. The polygon is re-centred on its bounding box and
    /// oriented counter-clockwise; a duplicate closing point is dropped.
    /// </summary>
    public static LathProfile Custom(IEnumerable<(double A, double B)> polygon, bool upright = false, double offset = 0.0)
    {
        var pts = polygon.ToList();
        if (pts.Count > 1 && Math.Abs(pts[0].A - pts[pts.Count - 1].A) < 1e-12 && Math.Abs(pts[0].B - pts[pts.Count - 1].B) < 1e-12)
            pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 3) throw new ArgumentException("A section needs at least three points.", nameof(polygon));

        double area2 = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Count];
            area2 += p.A * q.B - q.A * p.B;
        }
        if (Math.Abs(area2) < 1e-18) throw new ArgumentException("Section polygon is degenerate.", nameof(polygon));
        if (area2 < 0) pts.Reverse();

        double minA = pts.Min(p => p.A), maxA = pts.Max(p => p.A);
        double minB = pts.Min(p => p.B), maxB = pts.Max(p => p.B);
        double ca = 0.5 * (minA + maxA), cb = 0.5 * (minB + maxB);
        var centred = pts.Select(p => (p.A - ca, p.B - cb)).ToArray();
        return new LathProfile(maxA - minA, maxB - minB, upright, offset, centred);
    }

    /// <summary>Section vertices in the (first axis, second axis) plane, counter-clockwise.</summary>
    public (double A, double B)[] SectionPoints()
    {
        if (Section != null) return Section.ToArray();
        double hw = 0.5 * Width, ht = 0.5 * Thickness;
        return new[] { (-hw, -ht), (hw, -ht), (hw, ht), (-hw, ht) };
    }

    /// <summary>Section area (shoelace).</summary>
    public double SectionArea()
    {
        var pts = SectionPoints();
        double a2 = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Length];
            a2 += p.A * q.B - q.A * p.B;
        }
        return 0.5 * Math.Abs(a2);
    }

    /// <summary>Depth of the profile measured along the surface normal.</summary>
    public double NormalDepth => Upright ? Width : Thickness;

    /// <summary>
    /// Lowest section coordinate along the surface normal (negative), so
    /// that Offset − NormalLow lifts the section until it just clears the
    /// surface: −Thickness/2 for a flat rectangle, −Width/2 for an upright one.
    /// </summary>
    public double NormalLow
    {
        get
        {
            var pts = SectionPoints();
            // Flat: normal is the second axis (B). Upright: the first axis (A).
            return Upright ? pts.Min(p => p.A) : pts.Min(p => p.B);
        }
    }
}
