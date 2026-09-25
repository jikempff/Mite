using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mite.Core.Analysis;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
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
        _famA = new(); _famB = new();
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

        EvenlySpacedNet.Options Opts() => new EvenlySpacedNet.Options
        {
            Spacing = o.Spacing, StepSize = o.Step, Continuous = o.Continuous, MaxCurves = Math.Max(1, o.MaxCurves),
            JacobiSeeding = o.Jacobi, FromBorder = o.FromBorder, BorderAngle = o.BorderAngle
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

        _famA = a; _famB = b;
        var all = a.Concat(b).ToList();
        bool[]? regionMask = kind == "asymptotic" ? AsymptoticCurves.ComputeDirections(Pc(2), _mesh, o.MinAngle).Exists : null;
        var payload = new NetPayload
        {
            Kind = kind, A = ToJagged(a), B = ToJagged(b), CountA = a.Count, CountB = b.Count,
            MinLength = all.Count > 0 ? all.Min(ArcLength) : 0, MaxLength = all.Count > 0 ? all.Max(ArcLength) : 0,
            ResolvedSpacing = resolvedSpacing, ResolvedStep = resolvedStep,
            Warnings = warnings.ToArray()
        };
        payload.Ends = EndStatistics(all, regionMask, out var endPts, out var endCls);
        payload.EndPoints = endPts; payload.EndClasses = endCls;
        payload.Widths = StripWidths(a, b, resolvedSpacing > 0 ? resolvedSpacing : o.Spacing);
        if (crossFamilies && a.Count > 0 && b.Count > 0)
        {
            var xs = NetIntersections.Find(a, b);
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
                TJunctions = NetIntersections.FindAll(a, b).Count(x => x.IsTJunction)
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

    // ---- Frame analysis of the last net -------------------------------------

    [JSExport]
    public static string Frame(double width, double thickness, bool upright, double loadPerMetre, double modelToMetres, double sampling, int shape)
    {
        var sw = Stopwatch.StartNew();
        var p = new FramePayload();
        if (_mesh == null || _proj == null || (_famA.Count + _famB.Count) == 0) { p.Error = "Trace a net first."; return JsonSerializer.Serialize(p, MiteJson.Default.FramePayload); }
        try
        {
            double s = modelToMetres > 0 ? modelToMetres : 1.0;
            var lathsModel = _famA.Concat(_famB).Select(l => ShortestPath.Resample(l, sampling > 0 ? sampling : 2 * _proj.AverageEdgeLength)).ToList();
            var laths = lathsModel.Select(l => l.Select(q => q * s).ToArray()).ToList();
            var meshM = new MeshData(_mesh.Vertices.Select(q => q * s).ToArray(), _mesh.Faces);
            var xs = NetIntersections.Find(lathsModel.Take(_famA.Count).ToList(), lathsModel.Skip(_famA.Count).ToList());
            var joints = xs.Select(x => x.Point * s).ToList();
            // supports: lath ends on the mesh border
            var sup = new List<Vec3d>();
            foreach (var l in lathsModel)
                foreach (var e in new[] { l[0], l[^1] })
                {
                    var h = _proj.ClosestPoint(e, _proj.NearestVertexGlobal(e));
                    if (_proj.IsOnBoundary(h, 1e-4) || (h.Point - e).Length > 1e-6 * _proj.AverageEdgeLength) sup.Add(e * s);
                }
            if (sup.Count == 0) { p.Error = "No lath end lies on the mesh border, so there is nothing to support (closed surface?)."; return JsonSerializer.Serialize(p, MiteJson.Default.FramePayload); }
            var fr = FrameAnalysis.Compute(meshM, laths, joints, sup, shape == 1 ? LathProfile.Round(width * s, 24) : new LathProfile(width * s, thickness * s, upright), new Vec3d(0, 0, -loadPerMetre));
            var util = new double[laths.Count];
            for (int e = 0; e < fr.ElementSource.Length; e++) { int c = fr.ElementSource[e].Curve; util[c] = Math.Max(util[c], fr.Utilization[e]); }
            var nodeOf = new Dictionary<(int, int), int>();
            for (int n = 0; n < fr.NodeMap.Length; n++) foreach (var (c, i) in fr.NodeMap[n]) nodeOf.TryAdd((c, i), n);
            var deformed = new double[laths.Count][][];
            var disp = new List<double>();
            for (int c = 0; c < laths.Count; c++)
            {
                deformed[c] = new double[laths[c].Length][];
                for (int i = 0; i < laths[c].Length; i++)
                {
                    var d = nodeOf.TryGetValue((c, i), out int n) ? fr.Displacements[n] : Vec3d.Zero;
                    deformed[c][i] = new[] { d.X / s, d.Y / s, d.Z / s };
                    disp.Add(d.Length);
                }
            }
            p.Nodes = fr.Nodes.Length; p.Elements = fr.Utilization.Length; p.Supports = fr.SupportNodeCount;
            p.MaxDisplacement = fr.MaxDisplacement; p.MaxUtilization = fr.MaxUtilization; p.LathUtilization = util;
            p.Deformed = deformed; p.Displacements = disp.ToArray();
        }
        catch (Exception ex) { p.Error = ex.Message; }
        p.Ms = sw.ElapsedMilliseconds;
        return JsonSerializer.Serialize(p, MiteJson.Default.FramePayload);
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
