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
public enum SectionKind
{
    /// <summary>Width × Thickness rectangle.</summary>
    Rectangle,
    /// <summary>Solid round bar of diameter Width.</summary>
    Round,
    /// <summary>Arbitrary closed polygon.</summary>
    Custom,
}

/// <summary>
/// Geometric section properties of a <see cref="LathProfile"/> in its own
/// (first axis A, second axis B) plane, about the centroid. IA is the second
/// moment for bending about the A axis (fibres at distance B — "through the
/// thickness" of a flat strip), IB for bending about the B axis (fibres at
/// distance A — "across the width"). J is the Saint-Venant torsion constant.
/// TwistLength is the fibre length that turns the twist rate τ into the peak
/// shear strain of the section, γ_max = τ · TwistLength (t for a thin strip,
/// d/2 for a round bar).
/// </summary>
public readonly struct SectionProperties
{
    public double Area { get; }
    public double IA { get; }
    public double IB { get; }
    public double J { get; }
    /// <summary>Largest fibre distance from the centroid along the A axis.</summary>
    public double ExtentA { get; }
    /// <summary>Largest fibre distance from the centroid along the B axis.</summary>
    public double ExtentB { get; }
    public double TwistLength { get; }

    public SectionProperties(double area, double iA, double iB, double j, double extentA, double extentB, double twistLength)
    {
        Area = area; IA = iA; IB = iB; J = j; ExtentA = extentA; ExtentB = extentB; TwistLength = twistLength;
    }
}

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

    /// <summary>Which kind of section this is (rectangle, round bar, custom polygon).</summary>
    public SectionKind Kind { get; }

    public LathProfile(double width, double thickness, bool upright = false, double offset = 0.0)
        : this(width, thickness, upright, offset, null, SectionKind.Rectangle) { }

    private LathProfile(double width, double thickness, bool upright, double offset, IReadOnlyList<(double, double)>? section, SectionKind kind)
    {
        Width = width;
        Thickness = thickness;
        Upright = upright;
        Offset = offset;
        Section = section;
        Kind = kind;
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
        return new LathProfile(diameter, diameter, false, offset, pts, SectionKind.Round);
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
        return new LathProfile(maxA - minA, maxB - minB, upright, offset, centred, SectionKind.Custom);
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

    /// <summary>
    /// Section properties about the centroid in the profile plane. Rectangle
    /// and round bar use closed forms (rectangle torsion after Timoshenko &amp;
    /// Goodier, Theory of Elasticity, 3rd ed. 1970, §109: J = b t³ (1/3 −
    /// 0.21 t/b (1 − t⁴/12b⁴)) and γ_max = k(b/t) · τ · t with k from 0.675
    /// for a square to 1 for a thin strip; round bar J = π d⁴/32, γ_max = τ d/2).
    /// A custom polygon gets exact area and second moments (Green's theorem)
    /// and the Saint-Venant estimates J ≈ A⁴ / (40 I_p) (Roark's Formulas
    /// for Stress and Strain, solid sections) and the thin-rectangle twist
    /// rule on its bounding box, which is conservative for compact shapes.
    /// </summary>
    public SectionProperties SectionProperties()
    {
        if (Kind == SectionKind.Rectangle)
        {
            double w = Width, t = Thickness;
            double b = Math.Max(w, t), h = Math.Min(w, t); // b ≥ h
            double j = b * h * h * h * (1.0 / 3.0 - 0.21 * (h / b) * (1.0 - Math.Pow(h / b, 4) / 12.0));
            return new SectionProperties(w * t, w * t * t * t / 12.0, t * w * w * w / 12.0, j, 0.5 * w, 0.5 * t, RectangleTwistFactor(b / h) * h);
        }
        if (Kind == SectionKind.Round)
        {
            double d = Width, r = 0.5 * d;
            double i = Math.PI * Math.Pow(d, 4) / 64.0;
            return new SectionProperties(Math.PI * r * r, i, i, 2.0 * i, r, r, r);
        }

        // Custom polygon: Green's theorem for area, centroid and second moments
        var pts = SectionPoints();
        int n = pts.Length;
        double a2 = 0, cxA = 0, cxB = 0, iaa = 0, ibb = 0;
        for (int k = 0; k < n; k++)
        {
            var p = pts[k]; var q = pts[(k + 1) % n];
            double cross = p.A * q.B - q.A * p.B;
            a2 += cross;
            cxA += (p.A + q.A) * cross;
            cxB += (p.B + q.B) * cross;
            ibb += (p.A * p.A + p.A * q.A + q.A * q.A) * cross; // ∫A² dA
            iaa += (p.B * p.B + p.B * q.B + q.B * q.B) * cross; // ∫B² dA
        }
        double area = 0.5 * a2;
        double ca = cxA / (6.0 * area), cb = cxB / (6.0 * area);
        double iA = iaa / 12.0 - area * cb * cb; // about the centroidal A axis
        double iB = ibb / 12.0 - area * ca * ca; // about the centroidal B axis
        double extA = 0, extB = 0;
        foreach (var p in pts) { extA = Math.Max(extA, Math.Abs(p.A - ca)); extB = Math.Max(extB, Math.Abs(p.B - cb)); }
        double ip = iA + iB;
        double jEst = ip > 0 ? Math.Pow(area, 4) / (40.0 * ip) : 0.0;
        double bMax = 2.0 * Math.Max(extA, extB), hMin = 2.0 * Math.Min(extA, extB);
        return new SectionProperties(Math.Abs(area), iA, iB, jEst, extA, extB, RectangleTwistFactor(bMax / Math.Max(hMin, 1e-300)) * hMin);
    }

    /// <summary>
    /// γ_max / (τ · t) for a b × t rectangle in Saint-Venant torsion: 0.675 at
    /// b/t = 1, 0.930 at 2, 0.985 at 3, → 1 (Timoshenko &amp; Goodier 1970, table
    /// in §109); fitted as 1 − 0.325 (t/b)^2.5, within 4 % of the table.
    /// </summary>
    private static double RectangleTwistFactor(double aspect)
    {
        if (aspect < 1) aspect = 1;
        return 1.0 - 0.325 * Math.Pow(1.0 / aspect, 2.5);
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
