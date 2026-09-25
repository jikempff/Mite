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
}
