using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Geometry;

/// <summary>
/// Triangulated meshes of analytic surfaces with known curvature, used as
/// test typologies (tests, the bench page and the web app): a sphere and an
/// ellipsoid (K &gt; 0, four umbilics), a torus (mixed sign), saddles and a
/// hyperboloid of one sheet (K &lt; 0, ruled), a monkey saddle (flat umbilic
/// with three asymptotic directions), a catenoid and an Enneper patch
/// (H = 0), a barrel vault and a cone (K = 0, developable), an annulus
/// (inner boundary), and the catalogue of the Studio X submission: n-fold
/// Enneper surfaces (Weierstrass g = w^(n−1); asymptotic directions from the
/// Hopf differential (n−1) w^(n−2) dw²), a skew bilinear "ruled" patch (doubly
/// ruled by its iso-lines, which are its asymptotic curves) and patches of
/// the Schwarz D and gyroid triply periodic minimal surfaces, cut from their
/// nodal approximations by marching tetrahedra and relaxed with Minimal
/// Surface so H ≈ 0 (Schling's asymptotic pavilion sits on a Schwarz D).
///
/// Every shape is also available as an <see cref="AnalyticSurface"/> through
/// <see cref="Surface"/>: the same parametrisation with its closed-form first
/// and second derivatives (or, for the nodal surfaces, the implicit function
/// with gradient and Hessian), so the exact K, H, k1, k2, principal and
/// asymptotic directions are known at every vertex of the mesh that
/// <see cref="Build"/> returns. The meshes of the parametric shapes are built
/// from that surface, so the mesh and its truth cannot drift apart.
/// </summary>
public static class AnalyticShapes
{
    /// <summary>Names accepted by <see cref="Build"/>.</summary>
    public static readonly string[] Names =
    {
        "saddle", "sphere", "torus", "hyperboloid", "monkey", "ellipsoid", "catenoid",
        "enneper", "vault", "cone", "dome", "annulus", "wave",
        "enneper3", "ruled", "schwarzd", "gyroid"
    };

    /// <summary>
    /// Builds a shape by name with up to three parameters (defaults when 0)
    /// and a resolution (segments along the longer direction).
    /// </summary>
    public static MeshData Build(string name, double p1 = 0, double p2 = 0, double p3 = 0, int resolution = 40)
    {
        int n = Math.Max(4, resolution);
        switch (name.ToLowerInvariant())
        {
            case "schwarzd":
            case "gyroid":
            {
                double extent = P(p1, 1.0) * Math.PI;
                int cells = Math.Max(8, Math.Min(48, (int)Math.Round(P(p2, n))));
                Func<Vec3d, double> fn = name.ToLowerInvariant() == "gyroid" ? ImplicitSurface.Gyroid : ImplicitSurface.SchwarzD;
                var nodal = ImplicitSurface.MarchingTetrahedra(fn, 0, extent, cells);
                int iterations = (int)Math.Round(p3 > 0 ? p3 : 30);
                if (iterations <= 0) return nodal;
                var fixedFlags = nodal.BuildBoundaryVertexFlags();
                var relaxed = FormFinding.MinimalSurface.Compute(nodal, fixedFlags, new FormFinding.MinimalSurface.Options { MaxIterations = iterations });
                return new MeshData(relaxed.Vertices, nodal.Faces);
            }
            default:
                return Surface(name, p1, p2, p3).Mesh(n);
        }
    }

    /// <summary>
    /// The shape as an analytic surface with exact derivatives: the same
    /// parametrisation and parameter domain that <see cref="Build"/> meshes
    /// (so <c>Surface(name).Mesh(resolution)</c> is <c>Build(name, …, resolution)</c>),
    /// or the implicit nodal function for "schwarzd" and "gyroid".
    /// </summary>
    public static AnalyticSurface Surface(string name, double p1 = 0, double p2 = 0, double p3 = 0)
    {
        switch (name.ToLowerInvariant())
        {
            case "saddle":
            {
                double size = P(p1, 2.0), a = P(p2, 1.0), b = P(p3, 1.0);
                return AnalyticSurface.Graph("saddle",
                    (x, y) => a * x * x - b * y * y, (x, y) => 2 * a * x, (x, y) => -2 * b * y,
                    (x, y) => 2 * a, (x, y) => 0.0, (x, y) => -2 * b,
                    -size / 2, size / 2, -size / 2, size / 2, n => (n, n));
            }
            case "monkey":
            {
                double size = P(p1, 2.0), k = P(p2, 1.0);
                return AnalyticSurface.Graph("monkey",
                    (x, y) => k * (x * x * x - 3 * x * y * y), (x, y) => k * (3 * x * x - 3 * y * y), (x, y) => -6 * k * x * y,
                    (x, y) => 6 * k * x, (x, y) => -6 * k * y, (x, y) => -6 * k * x,
                    -size / 2, size / 2, -size / 2, size / 2, n => (n, n));
            }
            case "wave":
            {
                double size = P(p1, 2.0), amp = P(p2, 0.3), q = P(p3, 2.0);
                return AnalyticSurface.Graph("wave",
                    (x, y) => amp * Math.Sin(q * x) * Math.Cos(q * y),
                    (x, y) => amp * q * Math.Cos(q * x) * Math.Cos(q * y),
                    (x, y) => -amp * q * Math.Sin(q * x) * Math.Sin(q * y),
                    (x, y) => -amp * q * q * Math.Sin(q * x) * Math.Cos(q * y),
                    (x, y) => -amp * q * q * Math.Cos(q * x) * Math.Sin(q * y),
                    (x, y) => -amp * q * q * Math.Sin(q * x) * Math.Cos(q * y),
                    -size / 2, size / 2, -size / 2, size / 2, n => (n, n));
            }
            case "sphere":
            {
                double r = P(p1, 1.0);
                return AnalyticSurface.Revolution("sphere",
                    u => r * Math.Sin(u), u => r * Math.Cos(u), u => -r * Math.Sin(u),
                    u => -r * Math.Cos(u), u => r * Math.Sin(u), u => r * Math.Cos(u),
                    0, Math.PI, n => (n, n / 2), collapseStart: true, collapseEnd: true);
            }
            case "dome":
            {
                double r = P(p1, 1.0), half = P(p2, 60.0) * Math.PI / 180.0;
                return AnalyticSurface.Revolution("dome",
                    u => r * Math.Sin(u), u => r * Math.Cos(u), u => -r * Math.Sin(u),
                    u => r * Math.Cos(u), u => -r * Math.Sin(u), u => -r * Math.Cos(u),
                    0, half, n => (n, Math.Max(3, n / 3)), collapseStart: true, collapseEnd: false);
            }
            case "ellipsoid":
            {
                double a = P(p1, 1.5), b = P(p2, 1.0), c = P(p3, 0.7);
                return new AnalyticSurface("ellipsoid",
                    (u, v) => new Vec3d(a * Math.Sin(v) * Math.Cos(u), b * Math.Sin(v) * Math.Sin(u), -c * Math.Cos(v)),
                    (u, v) => new Vec3d(-a * Math.Sin(v) * Math.Sin(u), b * Math.Sin(v) * Math.Cos(u), 0),
                    (u, v) => new Vec3d(a * Math.Cos(v) * Math.Cos(u), b * Math.Cos(v) * Math.Sin(u), c * Math.Sin(v)),
                    (u, v) => new Vec3d(-a * Math.Sin(v) * Math.Cos(u), -b * Math.Sin(v) * Math.Sin(u), 0),
                    (u, v) => new Vec3d(-a * Math.Cos(v) * Math.Sin(u), b * Math.Cos(v) * Math.Cos(u), 0),
                    (u, v) => new Vec3d(-a * Math.Sin(v) * Math.Cos(u), -b * Math.Sin(v) * Math.Sin(u), c * Math.Cos(v)),
                    0, 2 * Math.PI, 0, Math.PI, n => (n, n / 2), closedU: true, collapseV0: true, collapseV1: true);
            }
            case "torus":
            {
                double R = P(p1, 3.0), r = P(p2, 1.0);
                return new AnalyticSurface("torus",
                    (u, v) => new Vec3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)),
                    (u, v) => new Vec3d(-(R + r * Math.Cos(v)) * Math.Sin(u), (R + r * Math.Cos(v)) * Math.Cos(u), 0),
                    (u, v) => new Vec3d(-r * Math.Sin(v) * Math.Cos(u), -r * Math.Sin(v) * Math.Sin(u), r * Math.Cos(v)),
                    (u, v) => new Vec3d(-(R + r * Math.Cos(v)) * Math.Cos(u), -(R + r * Math.Cos(v)) * Math.Sin(u), 0),
                    (u, v) => new Vec3d(r * Math.Sin(v) * Math.Sin(u), -r * Math.Sin(v) * Math.Cos(u), 0),
                    (u, v) => new Vec3d(-r * Math.Cos(v) * Math.Cos(u), -r * Math.Cos(v) * Math.Sin(u), -r * Math.Sin(v)),
                    0, 2 * Math.PI, 0, 2 * Math.PI, n => (n, Math.Max(8, n / 2)), closedU: true, closedV: true);
            }
            case "hyperboloid":
            {
                double a = P(p1, 1.0), c = P(p2, 1.0), h = P(p3, 1.0);
                return AnalyticSurface.Revolution("hyperboloid",
                    v => a * Math.Cosh(v), v => a * Math.Sinh(v), v => a * Math.Cosh(v),
                    v => c * Math.Sinh(v), v => c * Math.Cosh(v), v => c * Math.Sinh(v),
                    -h, h, n => (n, Math.Max(6, n / 2)));
            }
            case "catenoid":
            {
                double c = P(p1, 1.0), h = P(p2, 1.2);
                return AnalyticSurface.Revolution("catenoid",
                    v => c * Math.Cosh(v / c), v => Math.Sinh(v / c), v => Math.Cosh(v / c) / c,
                    v => v, v => 1.0, v => 0.0,
                    -h, h, n => (n, Math.Max(6, n / 2)));
            }
            case "enneper":
            {
                double r = P(p1, 1.2);
                return new AnalyticSurface("enneper",
                    (u, v) => new Vec3d(u - u * u * u / 3 + u * v * v, v - v * v * v / 3 + v * u * u, u * u - v * v),
                    (u, v) => new Vec3d(1 - u * u + v * v, 2 * u * v, 2 * u),
                    (u, v) => new Vec3d(2 * u * v, 1 - v * v + u * u, -2 * v),
                    (u, v) => new Vec3d(-2 * u, 2 * v, 2),
                    (u, v) => new Vec3d(2 * v, 2 * u, 0),
                    (u, v) => new Vec3d(2 * u, -2 * v, -2),
                    -r, r, -r, r, n => (n, n));
            }
            case "vault":
            {
                double R = P(p1, 1.5), width = P(p2, 2.0), length = P(p3, 4.0);
                double half = Math.Min(width / 2, R * 0.999);
                return AnalyticSurface.Graph("vault",
                    (x, y) => Math.Sqrt(Math.Max(0, R * R - x * x)) - R,
                    (x, y) => -x / Math.Sqrt(Math.Max(1e-300, R * R - x * x)),
                    (x, y) => 0.0,
                    (x, y) => -R * R / Math.Pow(Math.Max(1e-300, R * R - x * x), 1.5),
                    (x, y) => 0.0, (x, y) => 0.0,
                    -half, half, 0, length, n => (n / 2, n));
            }
            case "cone":
            {
                double r = P(p1, 1.0), h = P(p2, 1.5), rTop = P(p3, 0.15);
                return AnalyticSurface.Revolution("cone",
                    v => r + (rTop - r) * v, v => rTop - r, v => 0.0,
                    v => h * v, v => h, v => 0.0,
                    0, 1, n => (n, Math.Max(6, n / 2)));
            }
            case "annulus":
            {
                double rin = P(p1, 0.4), rout = P(p2, 1.2), k = P(p3, 1.0);
                // (θ, ρ) → (ρ cos θ, ρ sin θ, k ρ² cos 2θ) = the saddle z = k (x² − y²) over a ring
                return new AnalyticSurface("annulus",
                    (u, v) => new Vec3d(v * Math.Cos(u), v * Math.Sin(u), k * (v * Math.Cos(u) * (v * Math.Cos(u)) - v * Math.Sin(u) * (v * Math.Sin(u)))),
                    (u, v) => new Vec3d(-v * Math.Sin(u), v * Math.Cos(u), -2 * k * v * v * Math.Sin(2 * u)),
                    (u, v) => new Vec3d(Math.Cos(u), Math.Sin(u), 2 * k * v * Math.Cos(2 * u)),
                    (u, v) => new Vec3d(-v * Math.Cos(u), -v * Math.Sin(u), -4 * k * v * v * Math.Cos(2 * u)),
                    (u, v) => new Vec3d(-Math.Sin(u), Math.Cos(u), -4 * k * v * Math.Sin(2 * u)),
                    (u, v) => new Vec3d(0, 0, 2 * k * Math.Cos(2 * u)),
                    0, 2 * Math.PI, rin, rout, n => (n, Math.Max(6, n / 3)), closedU: true);
            }
            case "enneper3":
            {
                int folds = (int)Math.Round(P(p1, 3.0));
                double radius = P(p2, folds == 2 ? 1.0 : 0.9);
                int nn = folds, m = 2 * folds - 1;
                // polar chart (φ, r): x = r cos φ − r^m cos(mφ)/m, y = −r sin φ − r^m sin(mφ)/m, z = 2 r^n cos(nφ)/n
                return new AnalyticSurface("enneper3",
                    (phi, r) => EnneperPoint(folds, r, phi),
                    (phi, r) => new Vec3d(-r * Math.Sin(phi) + Math.Pow(r, m) * Math.Sin(m * phi), -r * Math.Cos(phi) - Math.Pow(r, m) * Math.Cos(m * phi), -2 * Math.Pow(r, nn) * Math.Sin(nn * phi)),
                    (phi, r) => new Vec3d(Math.Cos(phi) - Math.Pow(r, m - 1) * Math.Cos(m * phi), -Math.Sin(phi) - Math.Pow(r, m - 1) * Math.Sin(m * phi), 2 * Math.Pow(r, nn - 1) * Math.Cos(nn * phi)),
                    (phi, r) => new Vec3d(-r * Math.Cos(phi) + m * Math.Pow(r, m) * Math.Cos(m * phi), r * Math.Sin(phi) + m * Math.Pow(r, m) * Math.Sin(m * phi), -2 * nn * Math.Pow(r, nn) * Math.Cos(nn * phi)),
                    (phi, r) => new Vec3d(-Math.Sin(phi) + m * Math.Pow(r, m - 1) * Math.Sin(m * phi), -Math.Cos(phi) - m * Math.Pow(r, m - 1) * Math.Cos(m * phi), -2 * nn * Math.Pow(r, nn - 1) * Math.Sin(nn * phi)),
                    (phi, r) => new Vec3d(-(m - 1) * Math.Pow(r, m - 2) * Math.Cos(m * phi), -(m - 1) * Math.Pow(r, m - 2) * Math.Sin(m * phi), 2 * (nn - 1) * (nn >= 2 ? Math.Pow(r, nn - 2) : 0) * Math.Cos(nn * phi)),
                    0, 2 * Math.PI, 0, radius, n => (n, Math.Max(6, n / 2)), closedU: true, collapseV0: true);
            }
            case "ruled":
            {
                // skew bilinear patch: corners of a size×size square lifted by ±twist, one corner pushed sideways by skew
                double size = P(p1, 2.0), twist = P(p2, 0.8), skew = p3;
                Vec3d p00 = new Vec3d(-size / 2, -size / 2, 0), p10 = new Vec3d(size / 2, -size / 2, twist);
                Vec3d p01 = new Vec3d(-size / 2 + skew, size / 2, twist * 0.75), p11 = new Vec3d(size / 2 + skew, size / 2, -twist * 0.6);
                Vec3d mixed = p00 - p10 - p01 + p11;
                return new AnalyticSurface("ruled",
                    (u, v) => (1 - u) * (1 - v) * p00 + u * (1 - v) * p10 + (1 - u) * v * p01 + u * v * p11,
                    (u, v) => (1 - v) * (p10 - p00) + v * (p11 - p01),
                    (u, v) => (1 - u) * (p01 - p00) + u * (p11 - p10),
                    (u, v) => Vec3d.Zero,
                    (u, v) => mixed,
                    (u, v) => Vec3d.Zero,
                    0, 1, 0, 1, n => (n, n));
            }
            case "schwarzd":
                return new AnalyticSurface("schwarzd", ImplicitSurface.SchwarzD, SchwarzDGradient, SchwarzDHessian);
            case "gyroid":
                return new AnalyticSurface("gyroid", ImplicitSurface.Gyroid, GyroidGradient, GyroidHessian);
            default:
                throw new ArgumentException($"Unknown shape '{name}'. Known: {string.Join(", ", Names)}", nameof(name));
        }
    }

    private static double P(double v, double dflt) => v > 0 ? v : dflt;

    // Schwarz D nodal form sin x sin y sin z + sin x cos y cos z + cos x sin y cos z + cos x cos y sin z
    private static Vec3d SchwarzDGradient(Vec3d p)
    {
        double sx = Math.Sin(p.X), cx = Math.Cos(p.X), sy = Math.Sin(p.Y), cy = Math.Cos(p.Y), sz = Math.Sin(p.Z), cz = Math.Cos(p.Z);
        return new Vec3d(
            cx * sy * sz + cx * cy * cz - sx * sy * cz - sx * cy * sz,
            sx * cy * sz - sx * sy * cz + cx * cy * cz - cx * sy * sz,
            sx * sy * cz - sx * cy * sz - cx * sy * sz + cx * cy * cz);
    }

    private static Matrix3d SchwarzDHessian(Vec3d p)
    {
        double sx = Math.Sin(p.X), cx = Math.Cos(p.X), sy = Math.Sin(p.Y), cy = Math.Cos(p.Y), sz = Math.Sin(p.Z), cz = Math.Cos(p.Z);
        double F = sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
        // every term is a product of one function of each variable, so F_xx = F_yy = F_zz = −F
        double fxy = cx * cy * sz - cx * sy * cz - sx * cy * cz + sx * sy * sz;
        double fxz = cx * sy * cz - cx * cy * sz + sx * sy * sz - sx * cy * cz;
        double fyz = sx * cy * cz + sx * sy * sz - cx * cy * sz - cx * sy * cz;
        return new Matrix3d(-F, fxy, fxz, fxy, -F, fyz, fxz, fyz, -F);
    }

    // Gyroid nodal form sin x cos y + sin y cos z + sin z cos x
    private static Vec3d GyroidGradient(Vec3d p)
    {
        double sx = Math.Sin(p.X), cx = Math.Cos(p.X), sy = Math.Sin(p.Y), cy = Math.Cos(p.Y), sz = Math.Sin(p.Z), cz = Math.Cos(p.Z);
        return new Vec3d(cx * cy - sz * sx, -sx * sy + cy * cz, -sy * sz + cz * cx);
    }

    private static Matrix3d GyroidHessian(Vec3d p)
    {
        double sx = Math.Sin(p.X), cx = Math.Cos(p.X), sy = Math.Sin(p.Y), cy = Math.Cos(p.Y), sz = Math.Sin(p.Z), cz = Math.Cos(p.Z);
        double fxx = -sx * cy - sz * cx, fyy = -sy * cz - sx * cy, fzz = -sz * cx - sy * cz;
        double fxy = -cx * sy, fxz = -cz * sx, fyz = -cy * sz;
        return new Matrix3d(fxx, fxy, fxz, fxy, fyy, fyz, fxz, fyz, fzz);
    }

    /// <summary>
    /// n-fold Enneper surface in polar chart coordinates (r, φ):
    /// x = r cos φ − r^(2n−1) cos((2n−1)φ)/(2n−1), y = −r sin φ − r^(2n−1) sin((2n−1)φ)/(2n−1),
    /// z = 2 r^n cos(nφ)/n (Weisstein, MathWorld); n = 2 is the classic Enneper surface.
    /// </summary>
    public static Vec3d EnneperPoint(int n, double r, double phi)
    {
        int m = 2 * n - 1;
        double rm = Math.Pow(r, m);
        return new Vec3d(
            r * Math.Cos(phi) - rm * Math.Cos(m * phi) / m,
            -r * Math.Sin(phi) - rm * Math.Sin(m * phi) / m,
            2.0 * Math.Pow(r, n) * Math.Cos(n * phi) / n);
    }

    /// <summary>Height field over a rectangle, triangulated along the shorter diagonal.</summary>
    public static MeshData Grid(Func<double, double, Vec3d> f, double x0, double x1, double y0, double y1, int nx, int ny)
    {
        return Parametric((x, y) => f(x, y), x0, x1, y0, y1, nx, ny, false, false, false, false);
    }

    /// <summary>Surface of revolution about z: profile (radius(u), z(u)).</summary>
    public static MeshData Revolution(Func<double, double> radius, Func<double, double> height, double u0, double u1,
        int nu, int nAround, bool collapseStart, bool collapseEnd)
    {
        return Parametric((th, u) => new Vec3d(radius(u) * Math.Cos(th), radius(u) * Math.Sin(th), height(u)),
            0, 2 * Math.PI, u0, u1, nAround, nu, true, false, collapseStart, collapseEnd);
    }

    /// <summary>
    /// General parametric patch. closedU/closedV join the last row to the
    /// first; collapseVStart/End merge a degenerate row (a pole) into one vertex.
    /// </summary>
    public static MeshData Parametric(Func<double, double, Vec3d> f, double u0, double u1, double v0, double v1,
        int nu, int nv, bool closedU, bool closedV, bool collapseVStart, bool collapseVEnd)
    {
        return ParametricGrid(f, u0, u1, v0, v1, nu, nv, closedU, closedV, collapseVStart, collapseVEnd, out _, out _);
    }

    /// <summary>
    /// <see cref="Parametric"/> that also returns the chart parameters of every vertex
    /// and the quads of the parameter grid (triangles at a collapsed pole) before the
    /// split along the shorter diagonal. The vertex normal of the triangulated mesh
    /// points along x_u × x_v.
    /// </summary>
    public static MeshData ParametricGrid(Func<double, double, Vec3d> f, double u0, double u1, double v0, double v1,
        int nu, int nv, bool closedU, bool closedV, bool collapseVStart, bool collapseVEnd,
        out (double u, double v)[] uv, out int[][] quads)
    {
        nu = Math.Max(3, nu);
        nv = Math.Max(2, nv);
        int cu = closedU ? nu : nu + 1;   // distinct columns
        int cv = closedV ? nv : nv + 1;   // distinct rows
        var verts = new List<Vec3d>();
        var prm = new List<(double, double)>();
        var index = new int[cv, cu];

        for (int j = 0; j < cv; j++)
        {
            double v = v0 + (v1 - v0) * j / nv;
            bool pole = (collapseVStart && j == 0) || (collapseVEnd && j == cv - 1);
            if (pole)
            {
                var p = f(u0, v);
                int k = verts.Count;
                verts.Add(p); prm.Add((u0, v));
                for (int i = 0; i < cu; i++) index[j, i] = k;
                continue;
            }
            for (int i = 0; i < cu; i++)
            {
                double u = u0 + (u1 - u0) * i / nu;
                index[j, i] = verts.Count;
                verts.Add(f(u, v)); prm.Add((u, v));
            }
        }

        var faces = new List<int[]>();
        var cells = new List<int[]>();
        var V = verts;
        for (int j = 0; j < nv; j++)
        {
            int j1 = (j + 1) % cv;
            for (int i = 0; i < nu; i++)
            {
                int i1 = (i + 1) % cu;
                int a = index[j, i], b = index[j, i1], c = index[j1, i1], d = index[j1, i];
                // shorter diagonal
                double dac = (V[c] - V[a]).LengthSquared, dbd = (V[d] - V[b]).LengthSquared;
                if (dbd < dac) { Add(faces, a, b, d); Add(faces, b, c, d); }
                else { Add(faces, a, b, c); Add(faces, a, c, d); }
                var cell = new[] { a, b, c, d }.Distinct().ToArray();
                if (cell.Length >= 3) cells.Add(cell);
            }
        }
        uv = prm.ToArray();
        quads = cells.ToArray();
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    private static void Add(List<int[]> faces, int a, int b, int c)
    {
        if (a == b || b == c || a == c) return;
        faces.Add(new[] { a, b, c });
    }
}
