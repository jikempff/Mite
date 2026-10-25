using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Tests;

public static class TestMeshes
{
    public static MeshData CreateUnitSphere(int subdivisions = 16)
    {
        int latDiv = subdivisions;
        int lonDiv = subdivisions * 2;

        var verts = new List<Vec3d>();
        var faces = new List<int[]>();

        verts.Add(new Vec3d(0, 0, 1));

        for (int lat = 1; lat < latDiv; lat++)
        {
            double theta = Math.PI * lat / latDiv;
            double sinT = Math.Sin(theta), cosT = Math.Cos(theta);
            for (int lon = 0; lon < lonDiv; lon++)
            {
                double phi = 2.0 * Math.PI * lon / lonDiv;
                verts.Add(new Vec3d(sinT * Math.Cos(phi), sinT * Math.Sin(phi), cosT));
            }
        }

        verts.Add(new Vec3d(0, 0, -1));

        for (int lon = 0; lon < lonDiv; lon++)
        {
            int next = (lon + 1) % lonDiv;
            faces.Add(new[] { 0, 1 + lon, 1 + next });
        }

        for (int lat = 0; lat < latDiv - 2; lat++)
        {
            int rowStart = 1 + lat * lonDiv;
            int nextRowStart = 1 + (lat + 1) * lonDiv;
            for (int lon = 0; lon < lonDiv; lon++)
            {
                int next = (lon + 1) % lonDiv;
                faces.Add(new[] { rowStart + lon, nextRowStart + lon, nextRowStart + next });
                faces.Add(new[] { rowStart + lon, nextRowStart + next, rowStart + next });
            }
        }

        int southPole = verts.Count - 1;
        int lastRowStart = 1 + (latDiv - 2) * lonDiv;
        for (int lon = 0; lon < lonDiv; lon++)
        {
            int next = (lon + 1) % lonDiv;
            faces.Add(new[] { lastRowStart + lon, southPole, lastRowStart + next });
        }

        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    public static MeshData CreateTorus(double R = 3.0, double r = 1.0, int majorDiv = 32, int minorDiv = 16)
    {
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();

        for (int i = 0; i < majorDiv; i++)
        {
            double theta = 2.0 * Math.PI * i / majorDiv;
            for (int j = 0; j < minorDiv; j++)
            {
                double phi = 2.0 * Math.PI * j / minorDiv;
                double x = (R + r * Math.Cos(phi)) * Math.Cos(theta);
                double y = (R + r * Math.Cos(phi)) * Math.Sin(theta);
                double z = r * Math.Sin(phi);
                verts.Add(new Vec3d(x, y, z));
            }
        }

        for (int i = 0; i < majorDiv; i++)
        {
            int nextI = (i + 1) % majorDiv;
            for (int j = 0; j < minorDiv; j++)
            {
                int nextJ = (j + 1) % minorDiv;
                int a = i * minorDiv + j;
                int b = nextI * minorDiv + j;
                int c = nextI * minorDiv + nextJ;
                int d = i * minorDiv + nextJ;
                faces.Add(new[] { a, b, c });
                faces.Add(new[] { a, c, d });
            }
        }

        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    public static MeshData CreateSaddle(int div = 20, double size = 2.0)
    {
        // z = x^2 - y^2 over [-size/2, size/2]^2, triangulated
        var verts = new Vec3d[(div + 1) * (div + 1)];
        var faces = new List<int[]>();

        for (int j = 0; j <= div; j++)
        {
            for (int i = 0; i <= div; i++)
            {
                double x = size * (i / (double)div - 0.5);
                double y = size * (j / (double)div - 0.5);
                verts[j * (div + 1) + i] = new Vec3d(x, y, x * x - y * y);
            }
        }

        for (int j = 0; j < div; j++)
        {
            for (int i = 0; i < div; i++)
            {
                int a = j * (div + 1) + i;
                int b = a + 1;
                int c = b + (div + 1);
                int d = a + (div + 1);
                faces.Add(new[] { a, b, c });
                faces.Add(new[] { a, c, d });
            }
        }

        return new MeshData(verts, faces.ToArray());
    }

    /// <summary>
    /// Open circular cylinder x² + y² = R² for z in [−h/2, h/2], no caps,
    /// triangulated rows × segments. Developable: K = 0, k1 = 1/R along the
    /// circles, k2 = 0 along the rulings, H = 1/(2R). Geodesics are helices
    /// (plus rulings and circles); asymptotic directions are the rulings only.
    /// Vertex index = row * segments + segment, row 0 at z = −h/2.
    /// </summary>
    public static MeshData CreateCylinder(double radius = 1.0, double height = 2.0, int segments = 64, int rows = 32)
    {
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();

        for (int j = 0; j <= rows; j++)
        {
            double z = height * (j / (double)rows - 0.5);
            for (int i = 0; i < segments; i++)
            {
                double th = 2.0 * Math.PI * i / segments;
                verts.Add(new Vec3d(radius * Math.Cos(th), radius * Math.Sin(th), z));
            }
        }

        AddRingFaces(faces, segments, rows);
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    /// <summary>
    /// Hyperboloid of one sheet of revolution x²/a² + y²/a² − z²/c² = 1
    /// (throat radius a), parametrized x = a√(1+u²) cos v, z = c u for u in
    /// [−uMax, uMax], triangulated rows × segments with meridian/parallel
    /// edges, so the straight rulings cross the facets as they would on a
    /// Rhino revolve. Doubly ruled, K &lt; 0 everywhere:
    /// K(u) = −c² / (c² + (a² + c²) u²)² (Weisstein, MathWorld, "One-Sheeted
    /// Hyperboloid"; re-derived symbolically), H = 0 on the throat when a = c.
    /// The two asymptotic families are exactly the two families of rulings.
    /// Vertex index = row * segments + segment, row 0 at u = −uMax.
    /// </summary>
    public static MeshData CreateHyperboloid(double a = 1.0, double c = 1.0, double uMax = 1.0, int segments = 64, int rows = 32)
    {
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();

        for (int j = 0; j <= rows; j++)
        {
            double u = uMax * (2.0 * j / rows - 1.0);
            double r = a * Math.Sqrt(1.0 + u * u);
            for (int i = 0; i < segments; i++)
            {
                double th = 2.0 * Math.PI * i / segments;
                verts.Add(new Vec3d(r * Math.Cos(th), r * Math.Sin(th), c * u));
            }
        }

        AddRingFaces(faces, segments, rows);
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    /// <summary>Analytic Gaussian curvature of CreateHyperboloid(a, c) at height z.</summary>
    public static double HyperboloidGaussianCurvature(double z, double a = 1.0, double c = 1.0)
    {
        double u = z / c;
        double d = c * c + (a * a + c * c) * u * u;
        return -c * c / (d * d);
    }

    /// <summary>
    /// Unit direction of the meridian (hyperbola) through a point of
    /// CreateHyperboloid(a, c): the z axis projected into the tangent plane.
    /// </summary>
    public static Vec3d HyperboloidMeridian(Vec3d p, double a = 1.0, double c = 1.0)
    {
        Vec3d n = new Vec3d(p.X / (a * a), p.Y / (a * a), -p.Z / (c * c));
        Vec3d z = new Vec3d(0, 0, 1);
        return (z - Vec3d.Dot(z, n) / n.LengthSquared * n).Normalized();
    }

    /// <summary>
    /// The two unit ruling directions of CreateHyperboloid(a, c) through a
    /// point p on it. With Q = diag(1/a², 1/a², −1/c²), the parallel direction
    /// t1 and the meridian direction t2 (t1·Q·t2 = 0 by symmetry), a tangent
    /// direction d = cos φ t1 ± sin φ t2 is a ruling when d·Q·d = 0, i.e.
    /// tan² φ = −(t1·Q·t1) / (t2·Q·t2). For a = c = 1 this is tan² φ = 1 + 2z²
    /// (verified symbolically), so the families cross at 90° on the throat.
    /// </summary>
    public static (Vec3d plus, Vec3d minus) HyperboloidRulings(Vec3d p, double a = 1.0, double c = 1.0)
    {
        Vec3d t1 = new Vec3d(-p.Y, p.X, 0).Normalized();
        Vec3d t2 = HyperboloidMeridian(p, a, c);
        double q11 = 1.0 / (a * a);
        double q22 = (t2.X * t2.X + t2.Y * t2.Y) / (a * a) - t2.Z * t2.Z / (c * c);
        double phi = Math.Atan(Math.Sqrt(-q11 / q22));
        Vec3d dp = (Math.Cos(phi) * t1 + Math.Sin(phi) * t2).Normalized();
        Vec3d dm = (Math.Cos(phi) * t1 - Math.Sin(phi) * t2).Normalized();
        return (dp, dm);
    }

    // ------------------------------------------------------------------
    // Minimal-surface catalogue (José I. Kempff et al., "Adaptive Behaviour in
    // Asymptotic Gridshells", Studio X 2026, catalogue of geometries: concave
    // cylinder ≈ catenoid, ruled surface, 2-fold and 3-fold Enneper, Schoen's
    // Batwing) plus the Schwarz D surface of Schling's asymptotic pavilion.
    // On a minimal surface (H = 0) the two asymptotic directions are
    // orthogonal and bisect the principal directions (do Carmo 1976 §3-2).
    // ------------------------------------------------------------------

    /// <summary>
    /// n-fold Enneper surface (Weierstrass data f = 1, g = w^(n−1); Weisstein,
    /// MathWorld "Enneper's Minimal Surface"), polar parametrization
    ///   x = r cos φ − r^(2n−1) cos((2n−1)φ) / (2n−1),
    ///   y = −r sin φ − r^(2n−1) sin((2n−1)φ) / (2n−1),
    ///   z = 2 r^n cos(nφ) / n,
    /// which for n = 2 is the classic Enneper surface x = u − u³/3 + u v²,
    /// y = −(v − v³/3 + v u²), z = u² − v². H = 0 everywhere; the coordinate
    /// lines of the isothermal (u, v) chart are curvature lines and the
    /// asymptotic curves are u ± v = const, i.e. their tangents are X_u ± X_v.
    /// Disk mesh: centre vertex, then rings × segments; the parameters of each
    /// vertex are returned in <paramref name="uv"/> as (r cos φ, r sin φ).
    /// </summary>
    public static MeshData CreateEnneper(int folds, double radius, int rings, int segments, out (double u, double v)[] uv)
    {
        var verts = new List<Vec3d>();
        var prm = new List<(double, double)>();
        var faces = new List<int[]>();
        verts.Add(EnneperPoint(folds, 0, 0)); prm.Add((0, 0));
        for (int j = 1; j <= rings; j++)
        {
            double r = radius * j / rings;
            for (int i = 0; i < segments; i++)
            {
                double phi = 2.0 * Math.PI * i / segments;
                verts.Add(EnneperPoint(folds, r, phi));
                prm.Add((r * Math.Cos(phi), r * Math.Sin(phi)));
            }
        }
        for (int i = 0; i < segments; i++)
            faces.Add(new[] { 0, 1 + i, 1 + (i + 1) % segments });
        for (int j = 0; j < rings - 1; j++)
        {
            int a0 = 1 + j * segments, b0 = 1 + (j + 1) * segments;
            for (int i = 0; i < segments; i++)
            {
                int n = (i + 1) % segments;
                faces.Add(new[] { a0 + i, b0 + i, b0 + n });
                faces.Add(new[] { a0 + i, b0 + n, a0 + n });
            }
        }
        uv = prm.ToArray();
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    public static MeshData CreateEnneper(int folds = 2, double radius = 1.0, int rings = 24, int segments = 72) =>
        CreateEnneper(folds, radius, rings, segments, out _);

    public static Vec3d EnneperPoint(int n, double r, double phi) => AnalyticShapes.EnneperPoint(n, r, phi);

    /// <summary>
    /// Analytic asymptotic directions of the n-fold Enneper surface at chart
    /// parameter w = u + i v. With Weierstrass data f = 1, g = w^(n−1) the Hopf
    /// differential is f g' dw² = (n−1) w^(n−2) dw², and asymptotic directions
    /// dw = e^(iθ) satisfy Re((n−1) w^(n−2) e^(2iθ)) = 0, i.e.
    /// θ = (±π/2 − (n−2) arg w) / 2. For n = 2 this is θ = ±45°: X_u ± X_v.
    /// For n ≥ 3 the two families rotate with arg w and swap after one turn
    /// around the flat point at the origin (a monkey-saddle-like singularity).
    /// </summary>
    public static (Vec3d plus, Vec3d minus) EnneperAsymptoticDirections(int n, double u, double v)
    {
        // central differences in the (u, v) chart; the chart is isothermal so X_u ⊥ X_v, |X_u| = |X_v|
        double h = 1e-5;
        Vec3d At(double uu, double vv) => EnneperPoint(n, Math.Sqrt(uu * uu + vv * vv), Math.Atan2(vv, uu));
        Vec3d xu = (1.0 / (2 * h)) * (At(u + h, v) - At(u - h, v));
        Vec3d xv = (1.0 / (2 * h)) * (At(u, v + h) - At(u, v - h));
        double arg = Math.Atan2(v, u);
        double tp = (Math.PI / 2 - (n - 2) * arg) / 2, tm = (-Math.PI / 2 - (n - 2) * arg) / 2;
        return ((Math.Cos(tp) * xu + Math.Sin(tp) * xv).Normalized(), (Math.Cos(tm) * xu + Math.Sin(tm) * xv).Normalized());
    }

    /// <summary>
    /// Catenoid x = c cosh(z/c) cos φ, y = c cosh(z/c) sin φ for z in [−h/2, h/2]
    /// (the "concave cylinder" of the catalogue): H = 0, K = −1/(c² cosh⁴(z/c)),
    /// meridians and parallels are the curvature lines and the asymptotic
    /// directions sit at ±45° to them. Vertex index = row * segments + segment.
    /// </summary>
    public static MeshData CreateCatenoid(double c = 1.0, double height = 2.0, int segments = 64, int rows = 32)
    {
        var verts = new List<Vec3d>();
        var faces = new List<int[]>();
        for (int j = 0; j <= rows; j++)
        {
            double z = height * (j / (double)rows - 0.5);
            double r = c * Math.Cosh(z / c);
            for (int i = 0; i < segments; i++)
            {
                double th = 2.0 * Math.PI * i / segments;
                verts.Add(new Vec3d(r * Math.Cos(th), r * Math.Sin(th), z));
            }
        }
        AddRingFaces(faces, segments, rows);
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    public static double CatenoidGaussianCurvature(double z, double c = 1.0)
    {
        double ch = Math.Cosh(z / c);
        return -1.0 / (c * c * ch * ch * ch * ch);
    }

    /// <summary>
    /// Bilinear (hyperbolic-paraboloid) patch spanned by four corners, the
    /// "ruled surface" of the catalogue: P(u, v) = (1−u)(1−v) p00 + u(1−v) p10 +
    /// (1−u) v p01 + u v p11. Doubly ruled: the iso-lines u = const and
    /// v = const are straight, and they are exactly the two asymptotic
    /// families (K &lt; 0 wherever the corners are not coplanar). Vertex index
    /// = j * (nu + 1) + i.
    /// </summary>
    public static MeshData CreateBilinearPatch(Vec3d p00, Vec3d p10, Vec3d p01, Vec3d p11, int nu = 40, int nv = 40)
    {
        var verts = new Vec3d[(nu + 1) * (nv + 1)];
        var faces = new List<int[]>();
        for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
            {
                double u = i / (double)nu, v = j / (double)nv;
                verts[j * (nu + 1) + i] = (1 - u) * (1 - v) * p00 + u * (1 - v) * p10 + (1 - u) * v * p01 + u * v * p11;
            }
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = j * (nu + 1) + i, b = a + 1, c = b + nu + 1, d = a + nu + 1;
                faces.Add(new[] { a, b, c });
                faces.Add(new[] { a, c, d });
            }
        return new MeshData(verts, faces.ToArray());
    }

    /// <summary>The two unit ruling directions of the bilinear patch at parameter (u, v): ∂P/∂u and ∂P/∂v.</summary>
    public static (Vec3d du, Vec3d dv) BilinearRulings(Vec3d p00, Vec3d p10, Vec3d p01, Vec3d p11, double u, double v)
    {
        Vec3d du = (1 - v) * (p10 - p00) + v * (p11 - p01);
        Vec3d dv = (1 - u) * (p01 - p00) + u * (p11 - p10);
        return (du.Normalized(), dv.Normalized());
    }

    /// <summary>
    /// A patch of the Schwarz D (diamond) triply periodic minimal surface, the
    /// surface of Schling's asymptotic pavilion (Schling, Hitrec &amp; Barthel
    /// 2017), from its nodal approximation
    ///   sin x sin y sin z + sin x cos y cos z + cos x sin y cos z + cos x cos y sin z = 0
    /// (Schnering &amp; Nesper 1991) by marching tetrahedra on a regular grid
    /// over the box [lo, hi]³. The nodal surface is close to, not exactly,
    /// minimal (|H| small); K &lt; 0 everywhere except at flat points.
    /// </summary>
    public static MeshData CreateSchwarzD(double lo = 0.0, double hi = Math.PI, int cells = 24, int relaxIterations = 30)
    {
        var nodal = MarchingTetrahedra(ImplicitSurface.SchwarzD, lo, hi, cells);
        if (relaxIterations <= 0) return nodal;
        // Marching tetrahedra leaves slivers and an uneven vertex distribution
        // that make discrete curvature noisy; relaxing the patch to the true
        // minimal surface spanning its (fixed) boundary also fairs the mesh
        var fixedFlags = nodal.BuildBoundaryVertexFlags();
        var relaxed = Mite.Core.FormFinding.MinimalSurface.Compute(nodal, fixedFlags,
            new Mite.Core.FormFinding.MinimalSurface.Options { MaxIterations = relaxIterations });
        return new MeshData(relaxed.Vertices, nodal.Faces);
    }

    /// <summary>Zero level set of an implicit function (see <see cref="ImplicitSurface.MarchingTetrahedra"/>).</summary>
    public static MeshData MarchingTetrahedra(Func<Vec3d, double> f, double lo, double hi, int cells) =>
        ImplicitSurface.MarchingTetrahedra(f, lo, hi, cells);

    private static void AddRingFaces(List<int[]> faces, int segments, int rows)
    {
        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int a = j * segments + i;
                int b = j * segments + next;
                int c = (j + 1) * segments + next;
                int d = (j + 1) * segments + i;
                faces.Add(new[] { a, b, c });
                faces.Add(new[] { a, c, d });
            }
        }
    }

    /// <summary>
    /// Right circular cone of half-angle alpha (between axis and generator), apex at the origin,
    /// opening downwards, as a polyhedral cone: n straight generators g_i, each cut at rings
    /// (optionally jittered along the generator, so the triangles are irregular), plus the apex
    /// vertex (index 0). Every quad between two generators lies in the plane of those generators
    /// through the apex, so the mesh is intrinsically flat everywhere except at the apex: it
    /// unfolds exactly onto a plane sector of angle Ω = n θ_f, cos θ_f = sin²α cos(2π/n) + cos²α,
    /// and the apex carries the whole angle deficit 2π − Ω (→ 2π(1 − sin α) for n → ∞).
    /// Vertex index = 1 + (ring − 1) * n + i, ring 1..rings at slant S·ring/rings.
    /// </summary>
    public static MeshData CreatePolyCone(double alpha = 0.6, double slant = 2.0, int n = 48, int rings = 16, double jitter = 0.0, int seed = 1)
    {
        var rnd = new Random(seed);
        var verts = new List<Vec3d> { new Vec3d(0, 0, 0) };
        for (int j = 1; j <= rings; j++)
            for (int i = 0; i < n; i++)
            {
                double s = slant * j / rings;
                if (jitter > 0 && j < rings) s += slant / rings * jitter * (rnd.NextDouble() - 0.5);
                verts.Add(s * ConeGenerator(alpha, n, i));
            }
        var faces = new List<int[]>();
        int V(int ring, int i) => 1 + (ring - 1) * n + ((i % n) + n) % n;
        for (int i = 0; i < n; i++) faces.Add(new[] { 0, V(1, i + 1), V(1, i) });
        for (int j = 1; j < rings; j++)
            for (int i = 0; i < n; i++)
            {
                int a = V(j, i), b = V(j, i + 1), c = V(j + 1, i + 1), d = V(j + 1, i);
                if (((i + j) & 1) == 0) { faces.Add(new[] { a, b, c }); faces.Add(new[] { a, c, d }); }
                else { faces.Add(new[] { a, b, d }); faces.Add(new[] { b, c, d }); }
            }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    /// <summary>Unit generator i of the polyhedral cone (apex at the origin, opening downwards).</summary>
    public static Vec3d ConeGenerator(double alpha, int n, int i)
    {
        double ph = 2 * Math.PI * i / n;
        return new Vec3d(Math.Sin(alpha) * Math.Cos(ph), Math.Sin(alpha) * Math.Sin(ph), -Math.Cos(alpha));
    }

    /// <summary>Apex angle of one face of the polyhedral cone and the total angle Ω = n θ_f.</summary>
    public static (double faceAngle, double total) ConeAngles(double alpha, int n)
    {
        double c = Math.Sin(alpha) * Math.Sin(alpha) * Math.Cos(2 * Math.PI / n) + Math.Cos(alpha) * Math.Cos(alpha);
        double th = Math.Acos(Math.Max(-1, Math.Min(1, c)));
        return (th, n * th);
    }

    /// <summary>A point on face i of the polyhedral cone: a·g_i + b·g_{i+1}.</summary>
    public static Vec3d ConePoint(double alpha, int n, int i, double a, double b) =>
        a * ConeGenerator(alpha, n, i) + b * ConeGenerator(alpha, n, i + 1);

    /// <summary>Polar coordinates (ρ, ψ) of a point a·g_i + b·g_{i+1} in the exact development.</summary>
    public static (double rho, double psi) ConeUnfold(double alpha, int n, int i, double a, double b)
    {
        var (th, _) = ConeAngles(alpha, n);
        double x = a * Math.Cos(i * th) + b * Math.Cos((i + 1) * th);
        double y = a * Math.Sin(i * th) + b * Math.Sin((i + 1) * th);
        return (Math.Sqrt(x * x + y * y), Math.Atan2(y, x) < i * th - 1e-9 ? Math.Atan2(y, x) + 2 * Math.PI : Math.Atan2(y, x));
    }

    /// <summary>
    /// Exact shortest distance on the polyhedral cone between two developed points: the shorter
    /// way around, Δ' = min(Δ, Ω − Δ) &lt; Ω/2 &lt; π, so the chord never passes the apex.
    /// </summary>
    public static double ConeDistance(double alpha, int n, (double rho, double psi) p, (double rho, double psi) q)
    {
        var (_, om) = ConeAngles(alpha, n);
        double d = Math.Abs(p.psi - q.psi) % om;
        d = Math.Min(d, om - d);
        return Math.Sqrt(Math.Max(0, p.rho * p.rho + q.rho * q.rho - 2 * p.rho * q.rho * Math.Cos(d)));
    }

    /// <summary>
    /// Flat square [0, size]² triangulated irregularly: a jittered (nx+1)² grid whose quads are split
    /// along alternating diagonals. Geodesics are straight segments.
    /// </summary>
    public static MeshData CreateIrregularPlane(int nx = 16, double size = 2.0, double jitter = 0.35, int seed = 3)
    {
        var rnd = new Random(seed);
        var verts = new Vec3d[(nx + 1) * (nx + 1)];
        double h = size / nx;
        for (int j = 0; j <= nx; j++)
            for (int i = 0; i <= nx; i++)
            {
                double x = i * h, y = j * h;
                if (i > 0 && i < nx && j > 0 && j < nx)
                {
                    x += h * jitter * (rnd.NextDouble() - 0.5);
                    y += h * jitter * (rnd.NextDouble() - 0.5);
                }
                verts[j * (nx + 1) + i] = new Vec3d(x, y, 0);
            }
        var faces = new List<int[]>();
        for (int j = 0; j < nx; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = b + nx + 1, d = a + nx + 1;
                if (((i * 7 + j * 3) % 5) < 2) { faces.Add(new[] { a, b, c }); faces.Add(new[] { a, c, d }); }
                else { faces.Add(new[] { a, b, d }); faces.Add(new[] { b, c, d }); }
            }
        return new MeshData(verts, faces.ToArray());
    }

    /// <summary>
    /// A single saddle vertex: centre (index 0) and a ring of 2m vertices at radius 1 whose heights
    /// alternate ±h, plus an outer ring at radius 2 (same pattern) — total angle at the centre
    /// &gt; 2π. Two points close to the centre in opposite sectors are joined by a geodesic through
    /// the centre (both side angles ≥ π), of length |p| + |q| (Mitchell, Mount &amp; Papadimitriou 1987).
    /// </summary>
    public static MeshData CreateSaddleFan(int m = 4, double h = 0.6)
    {
        int k = 2 * m;
        var verts = new List<Vec3d> { new Vec3d(0, 0, 0) };
        for (int r = 1; r <= 2; r++)
            for (int i = 0; i < k; i++)
            {
                double ph = 2 * Math.PI * i / k;
                verts.Add(new Vec3d(r * Math.Cos(ph), r * Math.Sin(ph), r * h * ((i & 1) == 0 ? 1 : -1)));
            }
        var faces = new List<int[]>();
        for (int i = 0; i < k; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % k, c = 1 + k + (i + 1) % k, d = 1 + k + i;
            faces.Add(new[] { 0, a, b });
            faces.Add(new[] { a, c, b });
            faces.Add(new[] { a, d, c });
        }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    public static MeshData CreateQuadGrid(int nx = 5, int ny = 5, double size = 1.0)
    {
        var verts = new Vec3d[(nx + 1) * (ny + 1)];
        var faces = new List<int[]>();

        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                verts[j * (nx + 1) + i] = new Vec3d(i * size, j * size, 0);

        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i;
                int b = a + 1;
                int c = b + (nx + 1);
                int d = a + (nx + 1);
                faces.Add(new[] { a, b, c, d });
            }

        return new MeshData(verts, faces.ToArray());
    }

    /// <summary>
    /// A height field over the rectangle [x0, x1] × [y0, y1] with circular holes: the grid
    /// vertices inside a hole and the faces using them are dropped, and every kept vertex next to
    /// a dropped one is pulled radially onto the hole's circle in the parameter plane (and re-lifted
    /// with f), so each hole border is a polygon inscribed in its circle. Faces whose three vertices
    /// all lie on one circle (a chord triangle inside the hole) are dropped too. Returns the hole
    /// border loops as vertex index lists in <paramref name="holeLoops"/> (one per hole, ordered
    /// counter-clockwise in the parameter plane).
    /// </summary>
    public static MeshData CreatePerforated(Func<double, double, Vec3d> f, double x0, double x1, double y0, double y1,
        int nx, int ny, (double cx, double cy, double r)[] holes, out List<int>[] holeLoops)
    {
        int cols = nx + 1, rows = ny + 1;
        var xy = new (double x, double y)[cols * rows];
        var inside = new int[cols * rows]; // hole index + 1, or 0
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                double x = x0 + (x1 - x0) * i / nx, y = y0 + (y1 - y0) * j / ny;
                int v = j * cols + i;
                xy[v] = (x, y);
                for (int h = 0; h < holes.Length; h++)
                {
                    double dx = x - holes[h].cx, dy = y - holes[h].cy;
                    if (dx * dx + dy * dy < holes[h].r * holes[h].r) { inside[v] = h + 1; break; }
                }
            }
        // kept vertices adjacent (in the grid, including diagonals) to a dropped one snap to that hole's circle
        var snapTo = new int[cols * rows];
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                int v = j * cols + i;
                if (inside[v] != 0) continue;
                int best = 0; double bestD = double.MaxValue;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= cols || jj >= rows) continue;
                        int w = jj * cols + ii;
                        if (inside[w] == 0) continue;
                        var hh = holes[inside[w] - 1];
                        double d = Math.Abs(Math.Sqrt((xy[v].x - hh.cx) * (xy[v].x - hh.cx) + (xy[v].y - hh.cy) * (xy[v].y - hh.cy)) - hh.r);
                        if (d < bestD) { bestD = d; best = inside[w]; }
                    }
                snapTo[v] = best;
            }
        var pos = new Vec3d[cols * rows];
        for (int v = 0; v < pos.Length; v++)
        {
            double x = xy[v].x, y = xy[v].y;
            if (snapTo[v] != 0)
            {
                var hh = holes[snapTo[v] - 1];
                double dx = x - hh.cx, dy = y - hh.cy, d = Math.Sqrt(dx * dx + dy * dy);
                x = hh.cx + dx / d * hh.r; y = hh.cy + dy / d * hh.r;
            }
            pos[v] = f(x, y);
        }
        var faces = new List<int[]>();
        void Tri(int a, int b, int c)
        {
            if (inside[a] != 0 || inside[b] != 0 || inside[c] != 0) return;
            if (snapTo[a] != 0 && snapTo[a] == snapTo[b] && snapTo[b] == snapTo[c]) return; // chord triangle inside the hole
            faces.Add(new[] { a, b, c });
        }
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * cols + i, b = a + 1, c = b + cols, d = a + cols;
                if (((i + j) & 1) == 0) { Tri(a, b, c); Tri(a, c, d); }
                else { Tri(a, b, d); Tri(b, c, d); }
            }
        // compact
        var map = new int[pos.Length];
        for (int v = 0; v < map.Length; v++) map[v] = -1;
        var verts = new List<Vec3d>();
        foreach (var fc in faces)
            for (int k = 0; k < 3; k++)
                if (map[fc[k]] < 0) { map[fc[k]] = verts.Count; verts.Add(pos[fc[k]]); }
        foreach (var fc in faces) for (int k = 0; k < 3; k++) fc[k] = map[fc[k]];
        var mesh = new MeshData(verts.ToArray(), faces.ToArray());

        // hole loops: boundary loops whose vertices all lie on one hole's circle
        holeLoops = new List<int>[holes.Length];
        var loops = Mite.Core.Gridshells.EvenlySpacedNet.BoundaryLoops(mesh);
        var snappedOrig = new int[verts.Count];
        for (int v = 0; v < pos.Length; v++) if (map[v] >= 0) snappedOrig[map[v]] = snapTo[v];
        foreach (var loop in loops)
        {
            int h = snappedOrig[loop[0]];
            if (h == 0 || loop.Any(v => snappedOrig[v] != h)) continue;
            holeLoops[h - 1] = loop;
        }
        return mesh;
    }

    /// <summary>
    /// Shortest path in the plane from p to q around a convex polygon (counter-clockwise vertex
    /// list) that the segment pq crosses: the shorter of the two arcs of the convex hull of
    /// {p, q} ∪ polygon between p and q (Lee &amp; Preparata 1984, shortest paths around a convex
    /// obstacle). Returns the straight distance when the segment misses the polygon.
    /// </summary>
    public static double PlanarDistanceAroundPolygon((double x, double y) p, (double x, double y) q, IList<(double x, double y)> polygon)
    {
        // does pq cross the polygon? (proper crossing of any polygon edge, or an endpoint inside)
        bool crosses = false;
        int n = polygon.Count;
        for (int i = 0; i < n && !crosses; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % n];
            double d1 = Cross(p, q, a), d2 = Cross(p, q, b), d3 = Cross(a, b, p), d4 = Cross(a, b, q);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) crosses = true;
        }
        if (!crosses) return Math.Sqrt((p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y));
        var pts = new List<(double x, double y)>(polygon) { p, q };
        var hull = ConvexHull(pts);
        int ip = hull.FindIndex(v => v.x == p.x && v.y == p.y), iq = hull.FindIndex(v => v.x == q.x && v.y == q.y);
        if (ip < 0 || iq < 0) throw new InvalidOperationException("p or q inside the polygon");
        double arc1 = 0, arc2 = 0; int m = hull.Count;
        for (int i = ip; i != iq; i = (i + 1) % m) arc1 += Dist(hull[i], hull[(i + 1) % m]);
        for (int i = iq; i != ip; i = (i + 1) % m) arc2 += Dist(hull[i], hull[(i + 1) % m]);
        return Math.Min(arc1, arc2);
    }

    private static double Cross((double x, double y) o, (double x, double y) a, (double x, double y) b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    private static double Dist((double x, double y) a, (double x, double y) b) =>
        Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    /// <summary>Andrew's monotone chain; counter-clockwise, no collinear points.</summary>
    private static List<(double x, double y)> ConvexHull(List<(double x, double y)> pts)
    {
        var s = pts.Distinct().OrderBy(v => v.x).ThenBy(v => v.y).ToList();
        if (s.Count < 3) return s;
        var lower = new List<(double x, double y)>();
        foreach (var v in s) { while (lower.Count >= 2 && Cross(lower[^2], lower[^1], v) <= 0) lower.RemoveAt(lower.Count - 1); lower.Add(v); }
        var upper = new List<(double x, double y)>();
        for (int i = s.Count - 1; i >= 0; i--) { var v = s[i]; while (upper.Count >= 2 && Cross(upper[^2], upper[^1], v) <= 0) upper.RemoveAt(upper.Count - 1); upper.Add(v); }
        lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }
}
