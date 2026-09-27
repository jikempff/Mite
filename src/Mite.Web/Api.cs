using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;
using Mite.Core.Streamlines;

namespace Mite.Web;

// ---------------------------------------------------------------------------
// JSON payloads (source-generated serializer: trimming/AOT safe)
// ---------------------------------------------------------------------------

public class MeshStats
{
    public int Vertices { get; set; }
    public int Faces { get; set; }
    public double[] Min { get; set; } = new double[3];
    public double[] Max { get; set; } = new double[3];
    public double AvgEdge { get; set; }
    public int Welded { get; set; }
    public int RemovedFaces { get; set; }
    public int BoundaryVertices { get; set; }
    public long Ms { get; set; }
}

public class CurvaturePayload
{
    public double[] K1 { get; set; } = Array.Empty<double>();
    public double[] K2 { get; set; } = Array.Empty<double>();
    public double[] K { get; set; } = Array.Empty<double>();
    public double[] H { get; set; } = Array.Empty<double>();
    public double[] D1 { get; set; } = Array.Empty<double>();
    public double[] D2 { get; set; } = Array.Empty<double>();
    public double[] Normals { get; set; } = Array.Empty<double>();
    public int[] Umbilics { get; set; } = Array.Empty<int>();
    public int Anticlastic { get; set; }
    public long Ms { get; set; }
}

public class NetOptions
{
    public double Spacing { get; set; }
    public double Step { get; set; }
    public bool Continuous { get; set; } = true;
    public int MaxCurves { get; set; } = 200;
    public int Seed { get; set; } = -1;
    public double[] Direction { get; set; } = { 1, 0, 0 };
    public double EdgeLength { get; set; }
    public int Count { get; set; } = 8;
    public double AngleDeg { get; set; } = 90;
    public int Levels { get; set; } = 10;
    public string Field { get; set; } = "K";
    public double MinAngle { get; set; } = 15;
    public bool FromBorder { get; set; } = false;
    public double BorderAngle { get; set; } = 0;
    public bool Jacobi { get; set; } = true;
    /// <summary>0 = evenly spaced fill (T-junctions), 1 = web from the border, 2 = web from the seed cross, 3 = symmetric web (Schling: node spacing along the seed curves).</summary>
    public int Layout { get; set; } = 0;
    /// <summary>Symmetric web: −1 detect, 0 none, n forced n-fold rotation about the seed normal.</summary>
    public int Symmetry { get; set; } = -1;
}

public class WebInfo
{
    public int Nodes { get; set; }
    public int Quads { get; set; }
    public int Rays { get; set; }
    public bool Singular { get; set; }
    public int Symmetry { get; set; }
    public bool Rotational { get; set; }
    public double SymmetryError { get; set; }
    public double[] DiagonalError { get; set; } = new double[2];
    public double[] NodePoints { get; set; } = Array.Empty<double>();
    public double[] Seed { get; set; } = new double[3];
    public string[] Notes { get; set; } = Array.Empty<string>();
}

public class AagOptions
{
    public int Iterations { get; set; } = 16;
    public double Proximity { get; set; } = 0.3;
    public int Family { get; set; } = 0;
}

public class AagPayload
{
    public double[][][] A { get; set; } = Array.Empty<double[][]>();
    public double[][][] B { get; set; } = Array.Empty<double[][]>();
    public double[][][] G { get; set; } = Array.Empty<double[][]>();
    public double StarError { get; set; }
    public double InitialStarError { get; set; }
    public double GeodesicError { get; set; }
    public double InitialGeodesicError { get; set; }
    public double MaxDeviation { get; set; }
    public double MeanDeviation { get; set; }
    public int Iterations { get; set; }
    public string? Error { get; set; }
    public long Ms { get; set; }
}

public class LathAllPayload
{
    public double[] Utilization { get; set; } = Array.Empty<double>(); // A, then B, then G
    public double[] Length { get; set; } = Array.Empty<double>();
    public double TotalLength { get; set; }
    public double MaxUtilization { get; set; }
    public int Buildable { get; set; }
    public double[] SweepVertices { get; set; } = Array.Empty<double>();
    public int[] SweepFaces { get; set; } = Array.Empty<int>();
    public long Ms { get; set; }
}

public class FrameOptions
{
    public double Width { get; set; }
    public double Thickness { get; set; }
    public int Shape { get; set; }
    /// <summary>Metres per model unit.</summary>
    public double ToMetres { get; set; } = 1.0;
    /// <summary>Line load along every lath, N/m (downward).</summary>
    public double LineLoad { get; set; } = 0.0;
    /// <summary>Area load on the surface, N/m² (downward).</summary>
    public double AreaLoad { get; set; } = 0.0;
    /// <summary>Density for self-weight, kg/m³.</summary>
    public double Density { get; set; } = 0.0;
    /// <summary>"border" (every lath end on the border), "lowest" (border ends within LowestBand of the lowest point), "picked".</summary>
    public string Supports { get; set; } = "border";
    /// <summary>Fraction of the height counted as "lowest" (0.05 = the bottom 5 %).</summary>
    public double LowestBand { get; set; } = 0.05;
    /// <summary>Picked support points (model units), flat xyz.</summary>
    public double[] Picked { get; set; } = Array.Empty<double>();
    /// <summary>0 fixed, 1 pinned.</summary>
    public int SupportType { get; set; } = 0;
    /// <summary>Joint rotational stiffness about the normal, N·m/rad: −1 rigid, 0 scissor hinge.</summary>
    public double JointStiffness { get; set; } = -1;
    public double E { get; set; } = 11e9;
    public double Allowable { get; set; } = 20e6;
}

public class KineticsOptions
{
    /// <summary>"scissor" (rotate the laths through the seed about its normal), "lift" (raise the seed node, lath ends slide on the ground), "spread" (rim ends pulled radially, the seed held).</summary>
    public string Drive { get; set; } = "scissor";
    /// <summary>−1…1: scissor ±45°, lift ±40 % of the size, spread ±40 %.</summary>
    public double Amplitude { get; set; } = 0.6;
    public double Stiffness { get; set; } = 0.2;
    public int Steps { get; set; } = 10;
    public double Width { get; set; }
    public double Thickness { get; set; }
    public double MaxStrain { get; set; } = 0.005;
    public double ToMetres { get; set; } = 1.0;
}

public class KineticState
{
    public double Fold { get; set; }
    public double[][][] A { get; set; } = Array.Empty<double[][]>();
    public double[][][] B { get; set; } = Array.Empty<double[][]>();
    public double Drift { get; set; }
    public double Asymptotic { get; set; }
    public double Miss { get; set; }
    public double MinAngle { get; set; }
    public double MaxAngle { get; set; }
    public double Height { get; set; }
    public double Span { get; set; }
    public double Utilization { get; set; }
    public double Energy { get; set; }
    public bool Converged { get; set; }
}

public class KineticsPayload
{
    public int Nodes { get; set; }
    public int Joints { get; set; }
    public int Laths { get; set; }
    public int Drivers { get; set; }
    public KineticState[] States { get; set; } = Array.Empty<KineticState>();
    public int Natural { get; set; } = -1;
    public string? Error { get; set; }
    public long Ms { get; set; }
}

public class WidthStats
{
    public double Min { get; set; }
    public double Mean { get; set; }
    public double Max { get; set; }
    public double Cv { get; set; }
    public int[] Histogram { get; set; } = Array.Empty<int>(); // 12 bins over 0..2 spacing
}

public class EndStats
{
    public int Ends { get; set; }
    public int Border { get; set; }
    public int RegionEdge { get; set; }
    public int OnCurve { get; set; }
    public int Floating { get; set; }
    public int Closed { get; set; }
}

public class CrossingStats
{
    public int Count { get; set; }
    public double MinAngle { get; set; }
    public double MaxAngle { get; set; }
    public double MaxGap { get; set; }
    public double[] Points { get; set; } = Array.Empty<double>();
    public int TJunctions { get; set; }
}

public class NetPayload
{
    public string Kind { get; set; } = "";
    public double[][][] A { get; set; } = Array.Empty<double[][]>();
    public double[][][] B { get; set; } = Array.Empty<double[][]>();
    public int CountA { get; set; }
    public int CountB { get; set; }
    public double MinLength { get; set; }
    public double MaxLength { get; set; }
    public double ResolvedSpacing { get; set; }
    public double ResolvedStep { get; set; }
    public EndStats Ends { get; set; } = new EndStats();
    /// <summary>Flat xyz of every open end followed by its class: 0 border, 1 region edge, 2 on a neighbour, 3 floating.</summary>
    public double[] EndPoints { get; set; } = Array.Empty<double>();
    public int[] EndClasses { get; set; } = Array.Empty<int>();
    public WidthStats? Widths { get; set; }
    public int[] AngleHistogram { get; set; } = Array.Empty<int>(); // 9 bins of 10° from 0 to 90
    public CrossingStats? Crossings { get; set; }
    public string[] Warnings { get; set; } = Array.Empty<string>();
    /// <summary>Symmetric web only: the node diagonals of the geodesic candidate family and the web statistics.</summary>
    public double[][][] G { get; set; } = Array.Empty<double[][]>();
    public WebInfo? Web { get; set; }
    public long Ms { get; set; }
}

public class LathPayload
{
    public double[] Arc { get; set; } = Array.Empty<double>();
    public double[] Kn { get; set; } = Array.Empty<double>();
    public double[] Kg { get; set; } = Array.Empty<double>();
    public double[] Tg { get; set; } = Array.Empty<double>();
    public double[] Utilization { get; set; } = Array.Empty<double>();
    public double MaxUtilization { get; set; }
    public bool Buildable { get; set; }
    public double Length { get; set; }
    public double[][] UnrollA { get; set; } = Array.Empty<double[]>();
    public double[][] UnrollB { get; set; } = Array.Empty<double[]>();
    public double[][] UnrollCenter { get; set; } = Array.Empty<double[]>();
    public double FlatLength { get; set; }
    public double Bow { get; set; }
    public double[] SweepVertices { get; set; } = Array.Empty<double>();
    public int[] SweepFaces { get; set; } = Array.Empty<int>();
    public long Ms { get; set; }
}

public class FramePayload
{
    public int Nodes { get; set; }
    public int Elements { get; set; }
    public int Supports { get; set; }
    public double MaxDisplacement { get; set; }
    public double MaxUtilization { get; set; }
    public double[] LathUtilization { get; set; } = Array.Empty<double>();
    public double[][][] Deformed { get; set; } = Array.Empty<double[][]>();
    public double[] Displacements { get; set; } = Array.Empty<double>();
    public double[] SupportPoints { get; set; } = Array.Empty<double>();
    public double TotalLoad { get; set; }
    public double ReactionSum { get; set; }
    public double EquilibriumError { get; set; }
    public int Joints { get; set; }
    public double MaxTorsion { get; set; }
    public int FloatingLaths { get; set; }
    public string? Error { get; set; }
    public long Ms { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MeshStats))]
[JsonSerializable(typeof(CurvaturePayload))]
[JsonSerializable(typeof(NetOptions))]
[JsonSerializable(typeof(NetPayload))]
[JsonSerializable(typeof(WidthStats))]
[JsonSerializable(typeof(LathPayload))]
[JsonSerializable(typeof(FramePayload))]
[JsonSerializable(typeof(WebInfo))]
[JsonSerializable(typeof(AagOptions))]
[JsonSerializable(typeof(AagPayload))]
[JsonSerializable(typeof(LathAllPayload))]
[JsonSerializable(typeof(FrameOptions))]
[JsonSerializable(typeof(KineticsOptions))]
[JsonSerializable(typeof(KineticsPayload))]
internal partial class MiteJson : JsonSerializerContext { }

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

public static partial class MiteApi
{
    private static MeshData? _mesh;
    private static MeshProjection? _proj;
    private static PrincipalCurvature.Result? _pc;
    private static int _pcRadius = -1;
    private static List<Vec3d[]> _famA = new();
    private static List<Vec3d[]> _famB = new();
    private static List<Vec3d[]> _famG = new();
    private static AsymptoticWeb.Result? _web;

    [JSExport]
    public static string Version() => "Mite.Core " + typeof(MeshData).Assembly.GetName().Version;

    // ---- Mesh ------------------------------------------------------------

    [JSExport]
    public static string Shape(string name, double p1, double p2, double p3, int resolution)
    {
        var sw = Stopwatch.StartNew();
        var mesh = AnalyticShapes.Build(name, p1, p2, p3, resolution);
        return SetMesh(mesh, 0, 0, sw);
    }

    /// <summary>Loads a triangle/quad soup: flat xyz vertices and flat face indices (3 per face).</summary>
    [JSExport]
    public static string LoadMesh([JSMarshalAs<JSType.Array<JSType.Number>>] double[] vertices,
        [JSMarshalAs<JSType.Array<JSType.Number>>] int[] triangles, bool weld)
    {
        var sw = Stopwatch.StartNew();
        int nv = vertices.Length / 3;
        var v = new Vec3d[nv];
        for (int i = 0; i < nv; i++) v[i] = new Vec3d(vertices[3 * i], vertices[3 * i + 1], vertices[3 * i + 2]);
        int nf = triangles.Length / 3;
        var f = new int[nf][];
        for (int i = 0; i < nf; i++) f[i] = new[] { triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2] };
        var mesh = new MeshData(v, f);
        int welded = 0, removed = 0;
        if (weld)
        {
            var r = MeshCleanup.Compute(mesh);
            mesh = r.Mesh;
            welded = r.WeldedVertices;
            removed = r.RemovedDegenerateFaces + r.RemovedDuplicateFaces;
        }
        return SetMesh(mesh, welded, removed, sw);
    }

    private static string SetMesh(MeshData mesh, int welded, int removed, Stopwatch sw)
    {
        _mesh = mesh.ToTriangulated();
        _proj = new MeshProjection(_mesh);
        _pc = null; _pcRadius = -1;
        _famA = new(); _famB = new(); _famG = new(); _web = null;
        var (mn, mx) = _mesh.BoundingBox();
        int boundary = 0;
        foreach (bool b in _mesh.BuildBoundaryVertexFlags()) if (b) boundary++;
        var s = new MeshStats
        {
            Vertices = _mesh.VertexCount, Faces = _mesh.FaceCount,
            Min = new[] { mn.X, mn.Y, mn.Z }, Max = new[] { mx.X, mx.Y, mx.Z },
            AvgEdge = _proj.AverageEdgeLength, Welded = welded, RemovedFaces = removed,
            BoundaryVertices = boundary, Ms = sw.ElapsedMilliseconds
        };
        return JsonSerializer.Serialize(s, MiteJson.Default.MeshStats);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] Vertices()
    {
        if (_mesh == null) return Array.Empty<double>();
        var o = new double[_mesh.VertexCount * 3];
        for (int i = 0; i < _mesh.VertexCount; i++) { var p = _mesh.Vertices[i]; o[3 * i] = p.X; o[3 * i + 1] = p.Y; o[3 * i + 2] = p.Z; }
        return o;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static int[] Triangles()
    {
        if (_mesh == null) return Array.Empty<int>();
        var o = new int[_mesh.FaceCount * 3];
        for (int i = 0; i < _mesh.FaceCount; i++) { var f = _mesh.Faces[i]; o[3 * i] = f[0]; o[3 * i + 1] = f[1]; o[3 * i + 2] = f[2]; }
        return o;
    }

    [JSExport]
    public static int NearestVertex(double x, double y, double z) =>
        _proj == null ? -1 : _proj.NearestVertexGlobal(new Vec3d(x, y, z));

    // ---- Curvature -------------------------------------------------------

    private static PrincipalCurvature.Result Pc(int radius)
    {
        if (_mesh == null) throw new InvalidOperationException("No mesh loaded.");
        if (_pc == null || _pcRadius != radius) { _pc = PrincipalCurvature.Compute(_mesh, radius); _pcRadius = radius; }
        return _pc.Value;
    }

    [JSExport]
    public static string Curvature(int radius, double umbilicTolerance)
    {
        var sw = Stopwatch.StartNew();
        var pc = Pc(Math.Max(1, radius));
        var K = GaussianCurvature.Compute(_mesh!);
        var H = MeanCurvature.Compute(_mesh!).Values;
        var field = AsymptoticCurves.ComputeDirections(pc, _mesh, 15.0);
        int anti = 0; foreach (bool e in field.Exists) if (e) anti++;
        var p = new CurvaturePayload
        {
            K1 = pc.K1, K2 = pc.K2, K = K, H = H,
            D1 = Flat(pc.D1), D2 = Flat(pc.D2), Normals = Flat(pc.Normals),
            Umbilics = Umbilics.Find(pc, umbilicTolerance > 0 ? umbilicTolerance : 0.05),
            Anticlastic = anti, Ms = sw.ElapsedMilliseconds
        };
        return JsonSerializer.Serialize(p, MiteJson.Default.CurvaturePayload);
    }

    // ---- Nets ------------------------------------------------------------

    [JSExport]
    public static string Net(string kind, string optionsJson)
    {
        var sw = Stopwatch.StartNew();
        if (_mesh == null || _proj == null) throw new InvalidOperationException("No mesh loaded.");
        var o = JsonSerializer.Deserialize(optionsJson, MiteJson.Default.NetOptions) ?? new NetOptions();
        var warnings = new List<string>();
        var a = new List<Vec3d[]>();
        var b = new List<Vec3d[]>();
        double resolvedSpacing = 0, resolvedStep = 0;
        bool crossFamilies = true;
        WebInfo? webInfo = null;
        var diagonals = new List<Vec3d[]>();
        _web = null;

        EvenlySpacedNet.Options Opts() => new EvenlySpacedNet.Options
        {
            Spacing = o.Spacing, StepSize = o.Step, Continuous = o.Continuous, MaxCurves = Math.Max(1, o.MaxCurves),
            JacobiSeeding = o.Jacobi, FromBorder = o.FromBorder, BorderAngle = o.BorderAngle,
            Layout = o.Layout == 1 ? NetLayout.WebBorder : o.Layout == 2 ? NetLayout.WebCross : NetLayout.Fill
        };
        int seed = o.Seed >= 0 && o.Seed < _mesh.VertexCount ? o.Seed : -1;
        var dir = new Vec3d(o.Direction[0], o.Direction[1], o.Direction[2]);
        if (dir.LengthSquared < 1e-20) dir = new Vec3d(1, 0, 0);

        switch (kind)
        {
            case "asymptotic":
            {
                var pc = Pc(2);
                var field = AsymptoticCurves.ComputeDirections(pc, _mesh, o.MinAngle);
                int anti = 0; foreach (bool e in field.Exists) if (e) anti++;
                if (anti == 0) { warnings.Add(o.MinAngle > 0 ? $"No usable anticlastic region: nowhere do the asymptotic families cross at more than {o.MinAngle:0}° (lower MinAngle or pick a more saddle-shaped surface)." : "No anticlastic region (K < 0): asymptotic curves do not exist on this shape."); break; }
                int s = seed >= 0 && field.Exists[seed] ? seed : -1;
                if (o.Layout == 3)
                {
                    var w = AsymptoticWeb.Build(_mesh, pc, seed, new AsymptoticWeb.Options { Spacing = o.Spacing, StepSize = o.Step, MinCrossingAngle = Math.Min(o.MinAngle, 10), Symmetry = o.Symmetry });
                    _web = w;
                    a = w.A; b = w.B;
                    resolvedSpacing = w.Spacing; resolvedStep = w.Step;
                    webInfo = new WebInfo
                    {
                        Nodes = w.Nodes.Count, Quads = w.Quads.Count, Rays = w.Rays, Singular = w.Singular,
                        Symmetry = w.SymmetryOrder == int.MaxValue ? -1 : w.SymmetryOrder, Rotational = w.RotationalWeb,
                        SymmetryError = w.SymmetryError, DiagonalError = w.DiagonalGeodesicError,
                        NodePoints = Flat(w.Nodes.ToArray()), Seed = new[] { w.Seed.X, w.Seed.Y, w.Seed.Z }, Notes = w.Notes.ToArray()
                    };
                    for (int i = 0; i < w.Diagonals.Count; i++) if (w.DiagonalFamily[i] == 0) diagonals.Add(w.Diagonals[i]);
                    break;
                }
                var oa = Opts(); var ob = Opts();
                a = EvenlySpacedNet.TraceField(_mesh, field.Family1, field.Exists, s, oa, field.Family2);
                b = EvenlySpacedNet.TraceField(_mesh, field.Family2, field.Exists, s, ob, field.Family1);
                resolvedSpacing = oa.ResolvedSpacing; resolvedStep = oa.ResolvedStepSize;
                if (oa.ReachedMaxCurves || ob.ReachedMaxCurves) warnings.Add("MaxCurves reached; raise it or the spacing.");
                if (anti < _mesh.VertexCount) warnings.Add($"{anti} of {_mesh.VertexCount} vertices are usable anticlastic region (families crossing at ≥ {o.MinAngle:0}°); curves end where the families collapse near K = 0.");
                break;
            }
            case "conjugate":
            {
                var oa = Opts();
                var r = ConjugateNet.Trace(_mesh, seed, oa);
                a = r.FamilyA; b = r.FamilyB;
                resolvedSpacing = oa.ResolvedSpacing; resolvedStep = oa.ResolvedStepSize;
                break;
            }
            case "geodesic":
            {
                var oa = Opts();
                int s = seed >= 0 ? seed : _proj.NearestVertexGlobal(Centroid(_mesh));
                a = EvenlySpacedNet.TraceGeodesics(_mesh, s, dir, oa);
                resolvedSpacing = oa.ResolvedSpacing; resolvedStep = oa.ResolvedStepSize;
                crossFamilies = false;
                if (oa.ReachedMaxCurves) warnings.Add("MaxCurves reached; raise it or the spacing.");
                break;
            }
            case "geodesicBoth":
            {
                var oa = Opts(); var ob = Opts();
                int s = seed >= 0 ? seed : _proj.NearestVertexGlobal(Centroid(_mesh));
                var hit = _proj.ClosestPoint(_mesh.Vertices[s], s);
                var d1 = dir - Vec3d.Dot(dir, hit.SmoothNormal) * hit.SmoothNormal;
                if (d1.LengthSquared < 1e-20) d1 = Vec3d.Cross(hit.SmoothNormal, new Vec3d(0, 0, 1));
                d1 = d1.Normalized();
                double ang = o.AngleDeg * Math.PI / 180.0;
                var d2 = (Math.Cos(ang) * d1 + Math.Sin(ang) * Vec3d.Cross(hit.SmoothNormal, d1).Normalized()).Normalized();
                // seeded on the border, the two families leave it symmetrically about
                // the border angle, ± half the family angle, so they cross instead of coinciding
                if (o.FromBorder || oa.Layout == NetLayout.WebBorder) { oa.BorderAngle = o.BorderAngle + 0.5 * o.AngleDeg; ob.BorderAngle = o.BorderAngle - 0.5 * o.AngleDeg; }
                a = EvenlySpacedNet.TraceGeodesics(_mesh, s, d1, oa);
                b = EvenlySpacedNet.TraceGeodesics(_mesh, s, d2, ob);
                resolvedSpacing = oa.ResolvedSpacing; resolvedStep = oa.ResolvedStepSize;
                break;
            }
            case "streamMax":
            case "streamMin":
            {
                var pc = Pc(2);
                var oa = Opts();
                a = EvenlySpacedNet.TraceField(_mesh, kind == "streamMax" ? pc.D1 : pc.D2, null, seed, oa);
                resolvedSpacing = oa.ResolvedSpacing; resolvedStep = oa.ResolvedStepSize;
                crossFamilies = false;
                break;
            }
            case "chebyshev":
            {
                int s = seed >= 0 ? seed : _proj.NearestVertexGlobal(Centroid(_mesh));
                double L = o.EdgeLength > 0 ? o.EdgeLength : 4 * _proj.AverageEdgeLength;
                var r = ChebyshevNet.Compute(_mesh, s, dir, new ChebyshevNet.Options
                {
                    EdgeLength = L, CountU = Math.Max(1, o.Count), CountV = Math.Max(1, o.Count), Angle = o.AngleDeg * Math.PI / 180.0
                });
                int nu = r.Points.GetLength(0), nv = r.Points.GetLength(1);
                int valid = 0;
                for (int i = 0; i < nu; i++)
                {
                    var l = new List<Vec3d>();
                    for (int j = 0; j < nv; j++) if (r.Valid[i, j]) { l.Add(r.Points[i, j]); valid++; } else { if (l.Count > 1) a.Add(l.ToArray()); l.Clear(); }
                    if (l.Count > 1) a.Add(l.ToArray());
                }
                for (int j = 0; j < nv; j++)
                {
                    var l = new List<Vec3d>();
                    for (int i = 0; i < nu; i++) if (r.Valid[i, j]) l.Add(r.Points[i, j]); else { if (l.Count > 1) b.Add(l.ToArray()); l.Clear(); }
                    if (l.Count > 1) b.Add(l.ToArray());
                }
                resolvedSpacing = L;
                if (valid < nu * nv) warnings.Add($"{nu * nv - valid} of {nu * nv} nodes left the mesh (ragged border).");
                break;
            }
            case "isocurves":
            {
                var pc = Pc(2);
                double[] values = o.Field switch
                {
                    "H" => MeanCurvature.Compute(_mesh).Values,
                    "k1" => pc.K1,
                    "k2" => pc.K2,
                    "z" => _mesh.Vertices.Select(p => p.Z).ToArray(),
                    _ => GaussianCurvature.Compute(_mesh)
                };
                double lo = values.Min(), hi = values.Max();
                int n = Math.Max(1, o.Levels);
                var levels = new List<double>();
                for (int i = 1; i <= n; i++) levels.Add(lo + (hi - lo) * i / (n + 1));
                var iso = MeshIsocurves.Compute(_mesh, values, levels);
                foreach (var lvl in iso) foreach (var c in lvl) a.Add(c);
                crossFamilies = false;
                break;
            }
            default:
                throw new ArgumentException("Unknown net kind: " + kind);
        }

        _famA = a; _famB = b; _famG = new();
        var all = a.Concat(b).ToList();
        bool[]? regionMask = kind == "asymptotic" ? AsymptoticCurves.ComputeDirections(Pc(2), _mesh, o.MinAngle).Exists : null;
        var payload = new NetPayload
        {
            Kind = kind, A = ToJagged(a), B = ToJagged(b), CountA = a.Count, CountB = b.Count,
            MinLength = all.Count > 0 ? all.Min(ArcLength) : 0, MaxLength = all.Count > 0 ? all.Max(ArcLength) : 0,
            ResolvedSpacing = resolvedSpacing, ResolvedStep = resolvedStep,
            Warnings = warnings.ToArray(), G = ToJagged(diagonals), Web = webInfo
        };
        payload.Ends = EndStatistics(all, regionMask, out var endPts, out var endCls);
        if (webInfo != null)
        {
            // a web's laths end on the border, at the K = 0 line, or at their last
            // node (tails shorter than a third of the spacing are cut there)
            for (int i = 0; i < endCls.Length; i++) if (endCls[i] >= 2) endCls[i] = 0;
            payload.Ends.Border += payload.Ends.OnCurve + payload.Ends.Floating;
            payload.Ends.OnCurve = 0; payload.Ends.Floating = 0;
        }
        payload.EndPoints = endPts; payload.EndClasses = endCls;
        payload.Widths = StripWidths(a, b, resolvedSpacing > 0 ? resolvedSpacing : o.Spacing);
        if (crossFamilies && a.Count > 0 && b.Count > 0)
        {
            var xs = NetIntersections.Find(a, b, webInfo != null ? 0.3 * _proj.AverageEdgeLength : 0.0);
            // the rays of a singular web meet at the seed: not a crossing
            if (_web != null && _web.Singular) xs = xs.Where(x => (x.Point - _web.Seed).Length > 0.05 * _web.Spacing).ToList();
            var angles = new List<double>();
            foreach (var x in xs)
            {
                var ta = NetIntersections.TangentAt(a, x, false); var tb = NetIntersections.TangentAt(b, x, true);
                angles.Add(Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(ta, tb)))) * 180 / Math.PI);
            }
            var pts = new double[xs.Count * 3];
            for (int i = 0; i < xs.Count; i++) { pts[3 * i] = xs[i].Point.X; pts[3 * i + 1] = xs[i].Point.Y; pts[3 * i + 2] = xs[i].Point.Z; }
            payload.Crossings = new CrossingStats
            {
                Count = xs.Count, MinAngle = angles.Count > 0 ? angles.Min() : 0, MaxAngle = angles.Count > 0 ? angles.Max() : 0,
                MaxGap = xs.Count > 0 ? xs.Max(x => x.Gap) : 0, Points = pts,
                TJunctions = webInfo != null ? 0 : NetIntersections.FindAll(a, b).Count(x => x.IsTJunction)
            };
            var hist = new int[9];
            foreach (double ang in angles) hist[Math.Min(8, (int)Math.Floor(ang / 10.0))]++;
            payload.AngleHistogram = hist;
        }
        payload.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(payload, MiteJson.Default.NetPayload);
    }

    // ---- Lath --------------------------------------------------------------

    [JSExport]
    public static string Lath([JSMarshalAs<JSType.Array<JSType.Number>>] double[] polyline,
        double width, double thickness, bool upright, double maxStrain, bool sweep, int shape)
    {
        var sw = Stopwatch.StartNew();
        if (_proj == null) throw new InvalidOperationException("No mesh loaded.");
        var line = Unflat(polyline);
        // 0 = rectangle width × thickness, 1 = round bar of diameter width — the strain
        // check and the sweep read the same profile (unroll keeps the rectangle's band)
        var profile = shape == 1 ? LathProfile.Round(width, 24) : new LathProfile(width, thickness, upright);
        var la = LathAnalysis.Analyze(_proj, line, new LathAnalysis.Options
        {
            Profile = profile, MaxStrain = maxStrain > 0 ? maxStrain : 0.005
        });
        var arc = new double[line.Length];
        for (int i = 1; i < line.Length; i++) arc[i] = arc[i - 1] + (line[i] - line[i - 1]).Length;
        var p = new LathPayload
        {
            Arc = arc, Kn = la.NormalCurvature, Kg = la.GeodesicCurvature, Tg = la.GeodesicTorsion,
            Utilization = la.Utilization, MaxUtilization = la.MaxUtilization, Buildable = la.Buildable,
            Length = arc[arc.Length - 1]
        };
        var un = StripUnroll.Unroll(_proj, line, new LathProfile(width, thickness, upright));
        if (un.HasValue)
        {
            p.UnrollA = un.Value.EdgeA.Select(q => new[] { q.X, q.Y }).ToArray();
            p.UnrollB = un.Value.EdgeB.Select(q => new[] { q.X, q.Y }).ToArray();
            p.UnrollCenter = un.Value.Centerline.Select(q => new[] { q.X, q.Y }).ToArray();
            p.FlatLength = ArcLength(un.Value.Centerline);
            p.Bow = un.Value.Centerline.Max(q => q.Y) - un.Value.Centerline.Min(q => q.Y);
        }
        if (sweep)
        {
            var s = StripSweep.Sweep(_proj, line, profile);
            if (s.HasValue)
            {
                var m = s.Value.Mesh.ToTriangulated();
                p.SweepVertices = Flat(m.Vertices);
                var f = new int[m.FaceCount * 3];
                for (int i = 0; i < m.FaceCount; i++) { f[3 * i] = m.Faces[i][0]; f[3 * i + 1] = m.Faces[i][1]; f[3 * i + 2] = m.Faces[i][2]; }
                p.SweepFaces = f;
            }
        }
        p.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(p, MiteJson.Default.LathPayload);
    }

    // ---- AAG: geodesic diagonals by optimisation --------------------------------

    /// <summary>Includes (or drops) the geodesic diagonals of the current symmetric web as a third lath family.</summary>
    [JSExport]
    public static int UseDiagonals(bool on)
    {
        _famG = new();
        if (on && _web != null)
            for (int i = 0; i < _web.Diagonals.Count; i++) if (_web.DiagonalFamily[i] == 0) _famG.Add(_web.Diagonals[i]);
        return _famG.Count;
    }

    [JSExport]
    public static string Aag(string optionsJson)
    {
        var sw = Stopwatch.StartNew();
        var p = new AagPayload();
        if (_mesh == null || _web == null || _web.Nodes.Count == 0) { p.Error = "Trace a symmetric asymptotic web first."; return JsonSerializer.Serialize(p, MiteJson.Default.AagPayload); }
        var o = JsonSerializer.Deserialize(optionsJson, MiteJson.Default.AagOptions) ?? new AagOptions();
        try
        {
            var r = AagWeb.Optimize(_mesh, _web, new AagWeb.Options { Iterations = Math.Max(1, o.Iterations), Proximity = o.Proximity, GeodesicFamily = o.Family });
            p.A = ToJagged(r.A); p.B = ToJagged(r.B); p.G = ToJagged(r.G);
            p.StarError = r.StarError; p.InitialStarError = r.InitialStarError;
            p.GeodesicError = r.GeodesicError; p.InitialGeodesicError = r.InitialGeodesicError;
            p.MaxDeviation = r.MaxDeviation; p.MeanDeviation = r.MeanDeviation; p.Iterations = r.Iterations;
            // the optimised web becomes the current net (laths, frame)
            _famA = r.A; _famB = r.B; _famG = r.G;
        }
        catch (Exception ex) { p.Error = ex.Message; }
        p.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(p, MiteJson.Default.AagPayload);
    }

    // ---- every lath with one section ---------------------------------------------

    /// <summary>
    /// Strain check of every lath of the current net with one section (A and B
    /// upright when the net is asymptotic, flat otherwise; G flat), and
    /// optionally all laths swept as one solid.
    /// </summary>
    [JSExport]
    public static string LathAll(double width, double thickness, bool uprightAB, double maxStrain, int shape, bool sweep)
    {
        var sw = Stopwatch.StartNew();
        var p = new LathAllPayload();
        if (_proj == null) return JsonSerializer.Serialize(p, MiteJson.Default.LathAllPayload);
        var laths = _famA.Concat(_famB).Concat(_famG).ToList();
        int nAB = _famA.Count + _famB.Count;
        var util = new double[laths.Count];
        var len = new double[laths.Count];
        var sv = new List<double>(); var sf = new List<int>();
        for (int i = 0; i < laths.Count; i++)
        {
            bool upright = i < nAB ? uprightAB : false;
            var profile = shape == 1 ? LathProfile.Round(width, 24) : new LathProfile(width, thickness, upright);
            var line = laths[i];
            len[i] = ArcLength(line);
            if (line.Length < 2) continue;
            try
            {
                var la = LathAnalysis.Analyze(_proj, line, new LathAnalysis.Options { Profile = profile, MaxStrain = maxStrain > 0 ? maxStrain : 0.005 });
                util[i] = la.MaxUtilization;
            }
            catch { util[i] = double.NaN; }
            if (sweep)
            {
                var s = StripSweep.Sweep(_proj, line, profile);
                if (s.HasValue)
                {
                    var m = s.Value.Mesh.ToTriangulated();
                    int baseV = sv.Count / 3;
                    foreach (var q in m.Vertices) { sv.Add(q.X); sv.Add(q.Y); sv.Add(q.Z); }
                    foreach (var f in m.Faces) { sf.Add(baseV + f[0]); sf.Add(baseV + f[1]); sf.Add(baseV + f[2]); }
                }
            }
        }
        p.Utilization = util; p.Length = len; p.TotalLength = len.Sum();
        p.MaxUtilization = util.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        p.Buildable = util.Count(u => double.IsFinite(u) && u <= 1.0);
        p.SweepVertices = sv.ToArray(); p.SweepFaces = sf.ToArray();
        p.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(p, MiteJson.Default.LathAllPayload);
    }

    // ---- Frame analysis of the last net -------------------------------------

    [JSExport]
    public static string Frame(string optionsJson)
    {
        var sw = Stopwatch.StartNew();
        var p = new FramePayload();
        var o = JsonSerializer.Deserialize(optionsJson, MiteJson.Default.FrameOptions) ?? new FrameOptions();
        if (_mesh == null || _proj == null || (_famA.Count + _famB.Count) == 0) { p.Error = "Trace a net first."; return JsonSerializer.Serialize(p, MiteJson.Default.FramePayload); }
        try
        {
            double s = o.ToMetres > 0 ? o.ToMetres : 1.0;
            double sampling = 2 * _proj.AverageEdgeLength;
            var famsModel = _famA.Concat(_famB).Concat(_famG).Select(l => ShortestPath.Resample(l, sampling)).ToList();
            int nA = _famA.Count, nB = _famB.Count;
            var laths = famsModel.Select(l => l.Select(q => q * s).ToArray()).ToList();
            var meshM = new MeshData(_mesh.Vertices.Select(q => q * s).ToArray(), _mesh.Faces);
            // joints: every crossing between different families (A×B, A×G, B×G)
            var joints = new List<Vec3d>();
            var A = famsModel.Take(nA).ToList(); var B = famsModel.Skip(nA).Take(nB).ToList(); var G = famsModel.Skip(nA + nB).ToList();
            joints.AddRange(NetIntersections.Find(A, B).Select(x => x.Point * s));
            if (G.Count > 0)
            {
                joints.AddRange(NetIntersections.Find(A, G).Select(x => x.Point * s));
                joints.AddRange(NetIntersections.Find(B, G).Select(x => x.Point * s));
            }
            // supports
            var ends = new List<Vec3d>();
            foreach (var l in famsModel)
                foreach (var e in new[] { l[0], l[^1] })
                {
                    var h = _proj.ClosestPoint(e, _proj.NearestVertexGlobal(e));
                    if (_proj.IsOnBoundary(h, 1e-3) || (h.Point - e).Length > 1e-6 * _proj.AverageEdgeLength) ends.Add(e);
                }
            var sup = new List<Vec3d>();
            if (o.Supports == "picked")
            {
                for (int i = 0; i + 2 < o.Picked.Length; i += 3) sup.Add(new Vec3d(o.Picked[i], o.Picked[i + 1], o.Picked[i + 2]));
            }
            else if (o.Supports == "lowest")
            {
                var (mn, mx) = _mesh.BoundingBox();
                double zCut = mn.Z + Math.Max(1e-9, o.LowestBand) * (mx.Z - mn.Z);
                var endsLow = ends.Where(e => e.Z <= zCut).ToList();
                // no lath end that low: the lowest vertices of the net
                if (endsLow.Count == 0) endsLow = famsModel.SelectMany(l => l).Where(q => q.Z <= zCut).ToList();
                sup = endsLow;
            }
            else sup = ends;
            if (sup.Count == 0) { p.Error = o.Supports == "picked" ? "Pick support points first (click lath ends or nodes with “pick supports” on)." : "No lath end lies on the border or at the lowest level, so there is nothing to support (closed surface?)."; return JsonSerializer.Serialize(p, MiteJson.Default.FramePayload); }
            var profile = o.Shape == 1 ? LathProfile.Round(o.Width * s, 24) : new LathProfile(o.Width * s, o.Thickness * s, true);
            var fr = FrameAnalysis.Compute(meshM, laths, joints, sup.Select(q => q * s).ToList(), profile, new Vec3d(0, 0, -o.LineLoad),
                new FrameAnalysis.Options
                {
                    E = o.E > 0 ? o.E : 11e9, AllowableStress = o.Allowable > 0 ? o.Allowable : 20e6,
                    Support = o.SupportType == 1 ? FrameAnalysis.SupportKind.Pinned : FrameAnalysis.SupportKind.Fixed,
                    JointRotationalStiffness = o.JointStiffness < 0 ? double.PositiveInfinity : o.JointStiffness,
                    Density = Math.Max(0, o.Density), AreaLoad = Math.Max(0, o.AreaLoad),
                    SnapTolerance = 0.0,
                });
            var util = new double[laths.Count];
            for (int e = 0; e < fr.ElementSource.Length; e++) { int c = fr.ElementSource[e].Curve; util[c] = Math.Max(util[c], fr.Utilization[e]); }
            // deformed laths: the frame's own polylines (joints inserted), in model units
            var nodeOf = new Dictionary<(int, int), int>();
            for (int n = 0; n < fr.NodeMap.Length; n++) foreach (var (c, i) in fr.NodeMap[n]) nodeOf.TryAdd((c, i), n);
            var deformed = new double[laths.Count][][];
            var disp = new List<double>();
            for (int c = 0; c < laths.Count; c++)
            {
                var pl = fr.Laths[c];
                deformed[c] = new double[pl.Length][];
                // map each frame polyline point to its node by position
                for (int i = 0; i < pl.Length; i++)
                {
                    int best = NearestIndex(fr.Nodes, pl[i]);
                    var d = fr.Displacements[best];
                    deformed[c][i] = new[] { pl[i].X / s, pl[i].Y / s, pl[i].Z / s, d.X / s, d.Y / s, d.Z / s };
                    disp.Add(d.Length);
                }
            }
            p.Nodes = fr.Nodes.Length; p.Elements = fr.Utilization.Length; p.Supports = fr.SupportNodeCount;
            p.MaxDisplacement = fr.MaxDisplacement; p.MaxUtilization = fr.MaxUtilization; p.LathUtilization = util;
            p.Deformed = deformed; p.Displacements = disp.ToArray();
            p.SupportPoints = fr.SupportNodes.SelectMany(n => new[] { fr.Nodes[n].X / s, fr.Nodes[n].Y / s, fr.Nodes[n].Z / s }).ToArray();
            p.TotalLoad = -fr.TotalLoad.Z;
            p.ReactionSum = fr.SupportNodes.Sum(n => fr.Reactions[n].Z);
            p.EquilibriumError = fr.EquilibriumError;
            p.Joints = fr.JointCount;
            p.MaxTorsion = fr.Torsion.DefaultIfEmpty(0).Max();
            p.FloatingLaths = fr.FloatingLaths.Length;
        }
        catch (Exception ex) { p.Error = ex.Message; }
        p.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(p, MiteJson.Default.FramePayload);
    }

    private static int NearestIndex(Vec3d[] pts, Vec3d q)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < pts.Length; i++) { double d = (pts[i] - q).LengthSquared; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    // ---- Kinetics: the current asymptotic net as a scissor-jointed mechanism ------

    [JSExport]
    public static string Kinetics(string optionsJson)
    {
        var sw = Stopwatch.StartNew();
        var p = new KineticsPayload();
        var o = JsonSerializer.Deserialize(optionsJson, MiteJson.Default.KineticsOptions) ?? new KineticsOptions();
        if (_mesh == null || _proj == null || _famA.Count == 0 || _famB.Count == 0) { p.Error = "Trace an asymptotic net (two families) first."; return JsonSerializer.Serialize(p, MiteJson.Default.KineticsPayload); }
        try
        {
            var topo = NetTopology.Build(_famA, _famB, NetIntersections.FindAll(_famA, _famB, 0));
            var proj = _proj;
            var net = ScissorNet.FromTopology(topo, _famA.Count + _famB.Count, _famA.Count, 0, q => proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).SmoothNormal);
            if (net.Nodes.Length > 900) { p.Error = $"{net.Nodes.Length} nodes is too many for the browser; raise the spacing (coarse) and run again."; return JsonSerializer.Serialize(p, MiteJson.Default.KineticsPayload); }
            var (mn, mx) = _mesh.BoundingBox();
            double size = (mx - mn).Length;
            var seedPt = _web != null ? _web.Seed : 0.5 * (mn + mx);
            int centre = net.NearestNode(seedPt);
            var c = net.Nodes[centre];
            var nrm = proj.ClosestPoint(c, proj.NearestVertexGlobal(c)).SmoothNormal.Normalized();
            double amp = Math.Max(-1, Math.Min(1, o.Amplitude));
            var opts = new ScissorNet.Options { Fairness = Math.Max(0, o.Stiffness), Steps = Math.Max(1, Math.Min(24, o.Steps)), MaxIterations = 40 };
            var ends = Enumerable.Range(0, net.Nodes.Length).Where(i => !net.IsJoint[i]).ToArray();
            switch (o.Drive)
            {
                case "lift":
                {
                    opts.Driven = new[] { centre };
                    opts.TargetsAt = f => new[] { c + (0.4 * amp * f * size) * nrm };
                    opts.Sliding = ends;
                    p.Drivers = 1;
                    break;
                }
                case "spread":
                {
                    opts.Fixed = new[] { centre };
                    var rest = ends.Select(i => net.Nodes[i]).ToArray();
                    opts.Driven = ends;
                    opts.TargetWeight = 0.3;
                    opts.TargetsAt = f => rest.Select(q => { var v = q - c; var radial = v - Vec3d.Dot(v, nrm) * nrm; return q + (0.4 * amp * f) * radial; }).ToArray();
                    p.Drivers = ends.Length;
                    break;
                }
                default: // scissor: the laths through the seed turn about its normal, A one way, B the other
                {
                    var tips = new List<int>(); var sign = new List<double>();
                    for (int l = 0; l < net.Laths.Count; l++)
                    {
                        var lath = net.Laths[l];
                        int k = Array.IndexOf(lath, centre);
                        if (k < 0) continue;
                        foreach (int t in new[] { lath[0], lath[lath.Length - 1] })
                            if (t != centre && !tips.Contains(t)) { tips.Add(t); sign.Add(l < net.CountA ? 1.0 : -1.0); }
                    }
                    if (tips.Count == 0) { p.Error = "No lath passes through the seed node."; return JsonSerializer.Serialize(p, MiteJson.Default.KineticsPayload); }
                    var rest = tips.Select(i => net.Nodes[i]).ToArray();
                    opts.Fixed = new[] { centre };
                    opts.Driven = tips.ToArray();
                    opts.TargetsAt = f => rest.Select((q, k) => AsymptoticWeb.Rotate(q, c, nrm, sign[k] * amp * f * Math.PI / 4)).ToArray();
                    p.Drivers = tips.Count;
                    break;
                }
            }
            var res = net.Solve(opts);
            List<KineticStrain.StateResult>? elastic = null;
            if (o.Width > 0 && o.Thickness > 0)
            {
                double sc = o.ToMetres > 0 ? o.ToMetres : 1.0;
                elastic = KineticStrain.Analyze(net, res, new KineticStrain.Options { Profile = new LathProfile(o.Width, o.Thickness, true), MaxStrain = o.MaxStrain > 0 ? o.MaxStrain : 0.005, UnitScale = sc });
                p.Natural = KineticStrain.NaturalState(elastic);
            }
            var states = new List<KineticState>();
            for (int k = 0; k < res.States.Count; k++)
            {
                var st = res.States[k];
                var polys = net.LathPolylines(st);
                var ang = net.CrossingAngles(st).Where(x => !double.IsNaN(x)).ToArray();
                double zmin = st.Nodes.Min(q => q.Z), zmax = st.Nodes.Max(q => q.Z);
                double span = 0; foreach (int i in ends) foreach (int j in ends) span = Math.Max(span, (st.Nodes[i] - st.Nodes[j]).Length);
                states.Add(new KineticState
                {
                    Fold = st.Fold,
                    A = ToJagged(polys.Take(net.CountA).ToList()), B = ToJagged(polys.Skip(net.CountA).ToList()),
                    Drift = st.LengthDrift, Asymptotic = st.AsymptoticDeviation, Miss = st.TargetMiss,
                    MinAngle = ang.Length > 0 ? ang.Min() : 0, MaxAngle = ang.Length > 0 ? ang.Max() : 0,
                    Height = zmax - zmin, Span = span, Converged = st.Converged,
                    Utilization = elastic != null ? elastic[k].MaxUtilization : 0,
                    Energy = elastic != null ? elastic[k].StrainEnergy : 0,
                });
            }
            p.States = states.ToArray();
            p.Nodes = net.Nodes.Length; p.Joints = net.IsJoint.Count(j => j); p.Laths = net.Laths.Count;
        }
        catch (Exception ex) { p.Error = ex.Message; }
        p.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(p, MiteJson.Default.KineticsPayload);
    }

    // ---- helpers ---------------------------------------------------------------

    private static Vec3d Centroid(MeshData m)
    {
        Vec3d c = Vec3d.Zero;
        for (int i = 0; i < m.VertexCount; i++) c = c + m.Vertices[i];
        return c / Math.Max(1, m.VertexCount);
    }

    private static double[] Flat(Vec3d[] v)
    {
        var o = new double[v.Length * 3];
        for (int i = 0; i < v.Length; i++) { o[3 * i] = v[i].X; o[3 * i + 1] = v[i].Y; o[3 * i + 2] = v[i].Z; }
        return o;
    }

    private static Vec3d[] Unflat(double[] a)
    {
        var o = new Vec3d[a.Length / 3];
        for (int i = 0; i < o.Length; i++) o[i] = new Vec3d(a[3 * i], a[3 * i + 1], a[3 * i + 2]);
        return o;
    }

    private static double[][][] ToJagged(List<Vec3d[]> lines) =>
        lines.Select(l => l.Select(p => new[] { p.X, p.Y, p.Z }).ToArray()).ToArray();

    private static double ArcLength(Vec3d[] l)
    {
        double s = 0;
        for (int i = 1; i < l.Length; i++) s += (l[i] - l[i - 1]).Length;
        return s;
    }

    private static EndStats EndStatistics(List<Vec3d[]> all, bool[]? regionMask, out double[] endPoints, out int[] endClasses)
    {
        var st = new EndStats();
        var pts = new List<double>();
        var cls = new List<int>();
        endPoints = Array.Empty<double>(); endClasses = Array.Empty<int>();
        if (_proj == null || _mesh == null) return st;
        double tol = 1e-6 * Math.Max(_proj.AverageEdgeLength, 1e-12);
        int[][]? nbrs = regionMask != null ? _mesh.BuildVertexNeighbors() : null;
        foreach (var l in all)
        {
            if (l.Length < 2) continue;
            if ((l[0] - l[^1]).LengthSquared < 1e-24) { st.Closed++; continue; }
            foreach (var p in new[] { l[0], l[^1] })
            {
                st.Ends++;
                pts.Add(p.X); pts.Add(p.Y); pts.Add(p.Z);
                var h = _proj.ClosestPoint(p, _proj.NearestVertexGlobal(p));
                if (_proj.IsOnBoundary(h, 1e-4) || (h.Point - p).Length > tol) { st.Border++; cls.Add(0); continue; }
                if (regionMask != null && nbrs != null)
                {
                    // end at the edge of the region where the field exists (K = 0 line)
                    var f = _mesh.Faces[h.Face];
                    bool edge = false;
                    foreach (int vi in f)
                    {
                        if (!regionMask[vi]) { edge = true; break; }
                        foreach (int nb in nbrs[vi]) if (!regionMask[nb]) { edge = true; break; }
                        if (edge) break;
                    }
                    if (edge) { st.RegionEdge++; cls.Add(1); continue; }
                }
                double best = double.MaxValue;
                foreach (var m in all)
                {
                    if (ReferenceEquals(m, l)) continue;
                    for (int i = 0; i + 1 < m.Length; i++)
                    {
                        var ab = m[i + 1] - m[i];
                        double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - m[i], ab) / Math.Max(ab.LengthSquared, 1e-30)));
                        best = Math.Min(best, (m[i] + t * ab - p).Length);
                        if (best < tol) break;
                    }
                    if (best < tol) break;
                }
                if (best < tol) { st.OnCurve++; cls.Add(2); } else { st.Floating++; cls.Add(3); }
            }
        }
        endPoints = pts.ToArray(); endClasses = cls.ToArray();
        return st;
    }

    /// <summary>
    /// Strip width along the curves of each family: distance from sampled
    /// points to the nearest other curve of the same family (capped at 2×
    /// spacing, which also excludes lone curves). Even nets have a low
    /// coefficient of variation.
    /// </summary>
    private static WidthStats? StripWidths(List<Vec3d[]> a, List<Vec3d[]> b, double spacing)
    {
        if (spacing <= 0) return null;
        var widths = new List<double>();
        foreach (var fam in new[] { a, b })
        {
            if (fam.Count < 2) continue;
            // registry of the family's points for a coarse nearest lookup
            foreach (var c in fam)
            {
                int stride = Math.Max(1, c.Length / 24);
                for (int i = 0; i < c.Length; i += stride)
                {
                    double best = double.MaxValue;
                    foreach (var m in fam)
                    {
                        if (ReferenceEquals(m, c)) continue;
                        // quick reject by bounding sphere of the segment run
                        for (int k = 0; k + 1 < m.Length; k++)
                        {
                            var ab = m[k + 1] - m[k];
                            double t = Math.Max(0, Math.Min(1, Vec3d.Dot(c[i] - m[k], ab) / Math.Max(ab.LengthSquared, 1e-30)));
                            double d = (m[k] + t * ab - c[i]).Length;
                            if (d < best) best = d;
                        }
                    }
                    if (best < 2 * spacing) widths.Add(best);
                }
            }
        }
        if (widths.Count < 4) return null;
        double mean = widths.Average();
        double sd = Math.Sqrt(widths.Average(w => (w - mean) * (w - mean)));
        var hist = new int[12];
        foreach (double w in widths) hist[Math.Min(11, (int)Math.Floor(w / (2 * spacing) * 12))]++;
        return new WidthStats { Min = widths.Min(), Mean = mean, Max = widths.Max(), Cv = sd / mean, Histogram = hist };
    }
}
