// Mite Net: one value for a gridshell net (surface + two lath families +
// per-family profile), so the downstream steps take one wire.
// Pattern after the "model" objects of structural plug-ins (Karamba3D's
// Assemble → Model → Disassemble; McNeel, Grasshopper SDK "Simple Data Types"
// and "Simple Parameters": GH_Goo<T> wrapping an immutable core value,
// GH_Param<T> carrying it). The end classification unifies the counts the
// bench (tools/webgen) and the web app computed separately: an end is on
// the border when its closest surface point lies on a boundary edge, on a
// lath when it lies within tolerance of another lath (a T-junction), and
// free otherwise (Schling, Hitrec & Barthel 2017: every lath should end on
// a support or on another lath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>Which algorithm produced a <see cref="LathNet"/> (informative; nothing depends on it).</summary>
public enum NetKind
{
    Custom = 0,
    Asymptotic = 1,
    Geodesic = 2,
    Conjugate = 3,
    Chebyshev = 4,
    Kinetic = 5,
}

/// <summary>How a lath end is held.</summary>
public enum LathEndKind
{
    /// <summary>On the surface border (a support in a gridshell).</summary>
    Border = 0,
    /// <summary>On another lath (a T-junction).</summary>
    OnLath = 1,
    /// <summary>Neither: a floating end.</summary>
    Free = 2,
}

/// <summary>One end of an open lath.</summary>
public readonly struct LathEnd
{
    /// <summary>Global lath index (family A first, then B).</summary>
    public int Lath { get; }
    /// <summary>False: the start of the polyline, true: its last point.</summary>
    public bool AtEnd { get; }
    public Vec3d Point { get; }
    public LathEndKind Kind { get; }
    /// <summary>For <see cref="LathEndKind.OnLath"/>: the global index of the lath it ends on; otherwise −1.</summary>
    public int Other { get; }
    /// <summary>Distance to that lath (OnLath) or to the border (Border); NaN for free ends.</summary>
    public double Distance { get; }

    public LathEnd(int lath, bool atEnd, Vec3d point, LathEndKind kind, int other, double distance)
    {
        Lath = lath; AtEnd = atEnd; Point = point; Kind = kind; Other = other; Distance = distance;
    }
}

/// <summary>
/// A gridshell net as one immutable value: the reference surface (optional),
/// the two lath families as polylines, the profile of each family (optional:
/// null means "use the component's own section inputs"), the generating
/// algorithm and the spacing it was traced at. Crossings, contacts,
/// topology and the classification of lath ends are computed on demand.
/// Global lath indices number family A first, then family B, as Net
/// Topology and Gridshell Analysis do.
/// </summary>
public sealed class LathNet
{
    private readonly Vec3d[][] _a, _b;
    private MeshProjection? _proj;

    public MeshData? Surface { get; }
    public IReadOnlyList<Vec3d[]> FamilyA => _a;
    public IReadOnlyList<Vec3d[]> FamilyB => _b;
    public NetKind Kind { get; }
    /// <summary>Spacing the net was traced at (0 = unknown).</summary>
    public double Spacing { get; }
    public LathProfile? ProfileA { get; }
    public LathProfile? ProfileB { get; }

    public LathNet(MeshData? surface, IEnumerable<Vec3d[]> familyA, IEnumerable<Vec3d[]>? familyB,
        NetKind kind = NetKind.Custom, double spacing = 0.0,
        LathProfile? profileA = null, LathProfile? profileB = null)
    {
        Surface = surface;
        // Degenerate polylines (fewer than two points) are dropped so indices
        // stay meaningful; every array is copied, the net never aliases input.
        _a = (familyA ?? Enumerable.Empty<Vec3d[]>()).Where(l => l != null && l.Length >= 2).Select(l => (Vec3d[])l.Clone()).ToArray();
        _b = (familyB ?? Enumerable.Empty<Vec3d[]>()).Where(l => l != null && l.Length >= 2).Select(l => (Vec3d[])l.Clone()).ToArray();
        Kind = kind;
        Spacing = spacing > 0 && !double.IsNaN(spacing) && !double.IsInfinity(spacing) ? spacing : 0.0;
        ProfileA = profileA;
        ProfileB = profileB;
    }

    // ---------- copies with one part replaced ----------

    /// <summary>Same net, new profiles (B null → B uses A's profile).</summary>
    public LathNet WithProfiles(LathProfile? profileA, LathProfile? profileB = null) =>
        new LathNet(Surface, _a, _b, Kind, Spacing, profileA, profileB ?? profileA);

    /// <summary>Same surface, profiles and kind; new lath families (e.g. a kinetic state).</summary>
    public LathNet WithFamilies(IEnumerable<Vec3d[]> familyA, IEnumerable<Vec3d[]>? familyB, NetKind? kind = null) =>
        new LathNet(Surface, familyA, familyB, kind ?? Kind, Spacing, ProfileA, ProfileB);

    public LathNet WithSurface(MeshData? surface) =>
        new LathNet(surface, _a, _b, Kind, Spacing, ProfileA, ProfileB);

    // ---------- laths ----------

    public int CountA => _a.Length;
    public int CountB => _b.Length;
    public int LathCount => _a.Length + _b.Length;

    /// <summary>Global lath i (family A first).</summary>
    public Vec3d[] Lath(int i) => i < _a.Length ? _a[i] : _b[i - _a.Length];

    public bool IsFamilyA(int i) => i < _a.Length;

    /// <summary>Profile of global lath i (null when the family has none).</summary>
    public LathProfile? ProfileOf(int i) => IsFamilyA(i) ? ProfileA : ProfileB ?? ProfileA;

    /// <summary>All laths, family A then family B.</summary>
    public List<Vec3d[]> Laths()
    {
        var all = new List<Vec3d[]>(LathCount);
        all.AddRange(_a);
        all.AddRange(_b);
        return all;
    }

    /// <summary>Global index of curve <paramref name="curve"/> in family 0 (A) or 1 (B), as crossings report them.</summary>
    public int GlobalIndex(int family, int curve) => family == 0 ? curve : _a.Length + curve;

    public static double PolylineLength(Vec3d[] line)
    {
        double s = 0;
        for (int i = 0; i + 1 < line.Length; i++) s += (line[i + 1] - line[i]).Length;
        return s;
    }

    public static bool IsClosed(Vec3d[] line) =>
        line.Length > 2 && (line[0] - line[line.Length - 1]).LengthSquared <= 1e-24 * Math.Max(1.0, line[0].LengthSquared);

    public double TotalLengthA => _a.Sum(PolylineLength);
    public double TotalLengthB => _b.Sum(PolylineLength);

    /// <summary>Diagonal of the laths' bounding box (the surface's when there are no laths).</summary>
    public double Size()
    {
        bool any = false;
        var min = new Vec3d(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3d(double.MinValue, double.MinValue, double.MinValue);
        foreach (var l in _a.Concat(_b))
            foreach (var p in l)
            {
                any = true;
                min = new Vec3d(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3d(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        if (any) return (max - min).Length;
        return Surface != null ? Surface.BoundingBoxDiagonal() : 0.0;
    }

    /// <summary>Closest-point queries on the surface (built once, null without a surface).</summary>
    public MeshProjection? Projection
    {
        get
        {
            if (_proj == null && Surface != null && Surface.FaceCount > 0) _proj = new MeshProjection(Surface);
            return _proj;
        }
    }

    // ---------- contacts, joints, topology ----------

    /// <summary>A × B crossings (the joints); within family A when B is empty.</summary>
    public List<NetIntersections.Crossing> Joints(double tolerance = 0.0) =>
        NetIntersections.Find(_a, _b.Length > 0 ? _b : null, tolerance);

    /// <summary>All node-forming contacts: A × B crossings plus crossings and T-junctions within each family.</summary>
    public List<NetIntersections.Crossing> Contacts(double tolerance = 0.0) =>
        NetIntersections.FindAll(_a, _b, tolerance);

    /// <summary>Structural graph (nodes at every contact, members between them).</summary>
    public NetTopology.Result Topology(double tolerance = 0.0, double minLength = 0.0) =>
        NetTopology.Build(_a, _b, Contacts(tolerance), Math.Max(0.0, minLength));

    // ---------- lath ends ----------

    /// <summary>
    /// Automatic end tolerance: a millionth of the average mesh edge (traced
    /// nets end exactly on the border or on a neighbour), or of the average
    /// lath segment when there is no surface.
    /// </summary>
    public double DefaultEndTolerance()
    {
        var proj = Projection;
        if (proj != null) return 1e-6 * Math.Max(proj.AverageEdgeLength, 1e-12);
        double sum = 0; int n = 0;
        foreach (var l in _a.Concat(_b))
            for (int i = 0; i + 1 < l.Length; i++) { sum += (l[i + 1] - l[i]).Length; n++; }
        return 1e-6 * Math.Max(n > 0 ? sum / n : 1.0, 1e-12);
    }

    /// <summary>
    /// Classifies both ends of every open lath (closed laths have none):
    /// Border when the end's closest surface point lies on a boundary edge
    /// (barycentric tolerance 1e-4, as the tracer's own exit test) or within
    /// <paramref name="tolerance"/> of one; OnLath when it lies within
    /// tolerance of another lath; Free otherwise. Tolerance 0 selects
    /// <see cref="DefaultEndTolerance"/>; curves resampled from smooth Rhino
    /// curves need about the sampling chord.
    /// </summary>
    public List<LathEnd> Ends(double tolerance = 0.0)
    {
        double tol = tolerance > 0 ? tolerance : DefaultEndTolerance();
        var proj = Projection;
        var boundary = proj != null ? BoundarySegments(proj.Mesh) : new List<(Vec3d, Vec3d)>();
        var all = Laths();
        var ends = new List<LathEnd>();
        for (int li = 0; li < all.Count; li++)
        {
            var l = all[li];
            if (IsClosed(l)) continue;
            for (int e = 0; e < 2; e++)
            {
                var p = e == 0 ? l[0] : l[l.Length - 1];
                if (proj != null)
                {
                    var hit = proj.ClosestPoint(p, proj.NearestVertexGlobal(p));
                    double db = DistanceToSegments(p, boundary);
                    if (proj.IsOnBoundary(hit, 1e-4) || db <= tol)
                    {
                        ends.Add(new LathEnd(li, e == 1, p, LathEndKind.Border, -1, db));
                        continue;
                    }
                }
                int other = -1; double best = double.MaxValue;
                for (int m = 0; m < all.Count; m++)
                {
                    if (m == li) continue;
                    double d = DistanceToPolyline(p, all[m]);
                    if (d < best) { best = d; other = m; }
                }
                if (other >= 0 && best <= tol) ends.Add(new LathEnd(li, e == 1, p, LathEndKind.OnLath, other, best));
                else ends.Add(new LathEnd(li, e == 1, p, LathEndKind.Free, -1, double.NaN));
            }
        }
        return ends;
    }

    /// <summary>Counts of the end kinds: (border, on a lath, free, closed laths).</summary>
    public (int Border, int OnLath, int Free, int Closed) EndCounts(double tolerance = 0.0)
    {
        int b = 0, o = 0, f = 0;
        foreach (var e in Ends(tolerance))
            if (e.Kind == LathEndKind.Border) b++; else if (e.Kind == LathEndKind.OnLath) o++; else f++;
        int closed = _a.Count(IsClosed) + _b.Count(IsClosed);
        return (b, o, f, closed);
    }

    /// <summary>One-paragraph description (what the Grasshopper panel shows).</summary>
    public string Summary(double tolerance = 0.0)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var (b, o, f, closed) = EndCounts(tolerance);
        int joints = Joints().Count;
        string kind = Kind == NetKind.Custom ? "" : " (" + Kind.ToString().ToLowerInvariant() + ")";
        var s = string.Format(inv, "Mite Net{0}: {1} A + {2} B laths, length {3:G5} + {4:G5}, {5} joints; ends: {6} border, {7} on a lath, {8} free",
            kind, CountA, CountB, TotalLengthA, TotalLengthB, joints, b, o, f);
        if (closed > 0) s += string.Format(inv, ", {0} closed laths", closed);
        if (Spacing > 0) s += string.Format(inv, "; spacing {0:G4}", Spacing);
        s += Surface != null ? string.Format(inv, "; surface {0} vertices", Surface.VertexCount) : "; no surface";
        s += "; profile " + Describe(ProfileA) + (SameProfile(ProfileA, ProfileB) ? "" : " / B " + Describe(ProfileB));
        return s;
    }

    private static bool SameProfile(LathProfile? a, LathProfile? b) =>
        b == null || (a != null && a.Value.Kind == b.Value.Kind && a.Value.Width == b.Value.Width &&
                      a.Value.Thickness == b.Value.Thickness && a.Value.Upright == b.Value.Upright && a.Value.Offset == b.Value.Offset);

    public static string Describe(LathProfile? p)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (p == null) return "unset (components use their own section inputs)";
        var v = p.Value;
        string shape = v.Kind switch
        {
            SectionKind.Round => string.Format(inv, "round Ø{0:G4}", v.Width),
            SectionKind.Custom => string.Format(inv, "custom {0:G4} × {1:G4}", v.Width, v.Thickness),
            _ => string.Format(inv, "{0:G4} × {1:G4}", v.Width, v.Thickness),
        };
        if (v.Kind != SectionKind.Round) shape += v.Upright ? " upright" : " flat";
        if (v.Offset != 0) shape += string.Format(inv, ", offset {0:G4}", v.Offset);
        return shape;
    }

    // ---------- serialisation (Grasshopper internalised data, files) ----------

    private const int Magic = 0x54454E4D; // "MNET"
    public const int FormatVersion = 1;

    /// <summary>Compact little-endian binary form; <see cref="FromBytes"/> restores it exactly.</summary>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write((int)Kind);
            w.Write(Spacing);
            WriteProfile(w, ProfileA);
            WriteProfile(w, ProfileB);
            w.Write(Surface != null);
            if (Surface != null)
            {
                w.Write(Surface.VertexCount);
                foreach (var v in Surface.Vertices) WriteVec(w, v);
                w.Write(Surface.FaceCount);
                foreach (var f in Surface.Faces)
                {
                    w.Write(f.Length);
                    foreach (int i in f) w.Write(i);
                }
            }
            WriteFamily(w, _a);
            WriteFamily(w, _b);
        }
        return ms.ToArray();
    }

    public static LathNet FromBytes(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (r.ReadInt32() != Magic) throw new InvalidDataException("Not a Mite Net.");
        int version = r.ReadInt32();
        if (version < 1 || version > FormatVersion) throw new InvalidDataException($"Mite Net format {version} is newer than this Mite ({FormatVersion}).");
        var kind = (NetKind)r.ReadInt32();
        double spacing = r.ReadDouble();
        var pa = ReadProfile(r);
        var pb = ReadProfile(r);
        MeshData? surface = null;
        if (r.ReadBoolean())
        {
            var verts = new Vec3d[r.ReadInt32()];
            for (int i = 0; i < verts.Length; i++) verts[i] = ReadVec(r);
            var faces = new int[r.ReadInt32()][];
            for (int i = 0; i < faces.Length; i++)
            {
                var f = new int[r.ReadInt32()];
                for (int k = 0; k < f.Length; k++) f[k] = r.ReadInt32();
                faces[i] = f;
            }
            surface = new MeshData(verts, faces);
        }
        var a = ReadFamily(r);
        var b = ReadFamily(r);
        return new LathNet(surface, a, b, kind, spacing, pa, pb);
    }

    private static void WriteVec(BinaryWriter w, Vec3d v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    private static Vec3d ReadVec(BinaryReader r) => new Vec3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

    private static void WriteFamily(BinaryWriter w, Vec3d[][] family)
    {
        w.Write(family.Length);
        foreach (var l in family)
        {
            w.Write(l.Length);
            foreach (var p in l) WriteVec(w, p);
        }
    }

    private static List<Vec3d[]> ReadFamily(BinaryReader r)
    {
        int n = r.ReadInt32();
        var list = new List<Vec3d[]>(n);
        for (int i = 0; i < n; i++)
        {
            var l = new Vec3d[r.ReadInt32()];
            for (int k = 0; k < l.Length; k++) l[k] = ReadVec(r);
            list.Add(l);
        }
        return list;
    }

    private static void WriteProfile(BinaryWriter w, LathProfile? profile)
    {
        w.Write(profile != null);
        if (profile == null) return;
        var p = profile.Value;
        w.Write((int)p.Kind);
        w.Write(p.Width); w.Write(p.Thickness); w.Write(p.Upright); w.Write(p.Offset);
        var sec = p.Section;
        w.Write(sec?.Count ?? 0);
        if (sec != null) foreach (var (A, B) in sec) { w.Write(A); w.Write(B); }
    }

    private static LathProfile? ReadProfile(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        var kind = (SectionKind)r.ReadInt32();
        double width = r.ReadDouble(), thickness = r.ReadDouble();
        bool upright = r.ReadBoolean();
        double offset = r.ReadDouble();
        var sec = new (double A, double B)[r.ReadInt32()];
        for (int i = 0; i < sec.Length; i++) sec[i] = (r.ReadDouble(), r.ReadDouble());
        return kind switch
        {
            SectionKind.Round => LathProfile.Round(width, sec.Length, offset),
            SectionKind.Custom => LathProfile.Custom(sec, upright, offset),
            _ => new LathProfile(width, thickness, upright, offset),
        };
    }

    // ---------- geometry helpers ----------

    private static List<(Vec3d, Vec3d)> BoundarySegments(MeshData mesh)
    {
        var count = new Dictionary<(int, int), int>();
        foreach (var f in mesh.Faces)
            for (int k = 0; k < f.Length; k++)
            {
                int a = f[k], b = f[(k + 1) % f.Length];
                var key = a < b ? (a, b) : (b, a);
                count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        var segs = new List<(Vec3d, Vec3d)>();
        foreach (var kv in count)
            if (kv.Value == 1) segs.Add((mesh.Vertices[kv.Key.Item1], mesh.Vertices[kv.Key.Item2]));
        return segs;
    }

    private static double DistanceToSegments(Vec3d p, List<(Vec3d, Vec3d)> segs)
    {
        double best = double.MaxValue;
        foreach (var (a, b) in segs) best = Math.Min(best, DistanceToSegment(p, a, b));
        return best;
    }

    public static double DistanceToPolyline(Vec3d p, Vec3d[] line)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++) best = Math.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
        return best;
    }

    private static double DistanceToSegment(Vec3d p, Vec3d a, Vec3d b)
    {
        var ab = b - a;
        double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - a, ab) / Math.Max(ab.LengthSquared, 1e-300)));
        return (a + t * ab - p).Length;
    }
}
