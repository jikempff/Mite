using System;
using System.Collections.Generic;

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
            case "saddle":
            {
                double size = P(p1, 2.0), a = P(p2, 1.0), b = P(p3, 1.0);
                return Grid((x, y) => new Vec3d(x, y, a * x * x - b * y * y), -size / 2, size / 2, -size / 2, size / 2, n, n);
            }
            case "monkey":
            {
                double size = P(p1, 2.0), k = P(p2, 1.0);
                return Grid((x, y) => new Vec3d(x, y, k * (x * x * x - 3 * x * y * y)), -size / 2, size / 2, -size / 2, size / 2, n, n);
            }
            case "wave":
            {
                double size = P(p1, 2.0), amp = P(p2, 0.3), freq = P(p3, 2.0);
                return Grid((x, y) => new Vec3d(x, y, amp * Math.Sin(freq * x) * Math.Cos(freq * y)), -size / 2, size / 2, -size / 2, size / 2, n, n);
            }
            case "sphere":
            {
                double r = P(p1, 1.0);
                return Revolution(u => r * Math.Sin(u), u => -r * Math.Cos(u), 0, Math.PI, n / 2, n, true, true);
            }
            case "dome":
            {
                double r = P(p1, 1.0), half = P(p2, 60.0) * Math.PI / 180.0;
                return Revolution(u => r * Math.Sin(u), u => r * Math.Cos(u), 0, half, Math.Max(3, n / 3), n, true, false);
            }
            case "ellipsoid":
            {
                double a = P(p1, 1.5), b = P(p2, 1.0), c = P(p3, 0.7);
                return Parametric((u, v) => new Vec3d(a * Math.Sin(v) * Math.Cos(u), b * Math.Sin(v) * Math.Sin(u), -c * Math.Cos(v)),
                    0, 2 * Math.PI, 0, Math.PI, n, n / 2, true, false, true, true);
            }
            case "torus":
            {
                double R = P(p1, 3.0), r = P(p2, 1.0);
                return Parametric((u, v) => new Vec3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)),
                    0, 2 * Math.PI, 0, 2 * Math.PI, n, Math.Max(8, n / 2), true, true, false, false);
            }
            case "hyperboloid":
            {
                double a = P(p1, 1.0), c = P(p2, 1.0), h = P(p3, 1.0);
                return Parametric((u, v) => new Vec3d(a * Math.Cosh(v) * Math.Cos(u), a * Math.Cosh(v) * Math.Sin(u), c * Math.Sinh(v)),
                    0, 2 * Math.PI, -h, h, n, Math.Max(6, n / 2), true, false, false, false);
            }
            case "catenoid":
            {
                double c = P(p1, 1.0), h = P(p2, 1.2);
                return Parametric((u, v) => new Vec3d(c * Math.Cosh(v / c) * Math.Cos(u), c * Math.Cosh(v / c) * Math.Sin(u), v),
                    0, 2 * Math.PI, -h, h, n, Math.Max(6, n / 2), true, false, false, false);
            }
            case "enneper":
            {
                double r = P(p1, 1.2);
                return Parametric((u, v) => new Vec3d(u - u * u * u / 3 + u * v * v, v - v * v * v / 3 + v * u * u, u * u - v * v),
                    -r, r, -r, r, n, n, false, false, false, false);
            }
            case "vault":
            {
                double R = P(p1, 1.5), width = P(p2, 2.0), length = P(p3, 4.0);
                double half = Math.Min(width / 2, R * 0.999);
                return Grid((x, y) => new Vec3d(x, y, Math.Sqrt(Math.Max(0, R * R - x * x)) - R), -half, half, 0, length, n / 2, n);
            }
            case "cone":
            {
                double r = P(p1, 1.0), h = P(p2, 1.5), rTop = P(p3, 0.15);
                return Parametric((u, v) => new Vec3d((r + (rTop - r) * v) * Math.Cos(u), (r + (rTop - r) * v) * Math.Sin(u), h * v),
                    0, 2 * Math.PI, 0, 1, n, Math.Max(6, n / 2), true, false, false, false);
            }
            case "annulus":
            {
                double rin = P(p1, 0.4), rout = P(p2, 1.2), k = P(p3, 1.0);
                return Parametric((u, v) => { double x = v * Math.Cos(u), y = v * Math.Sin(u); return new Vec3d(x, y, k * (x * x - y * y)); },
                    0, 2 * Math.PI, rin, rout, n, Math.Max(6, n / 3), true, false, false, false);
            }
            case "enneper3":
            {
                int folds = (int)Math.Round(P(p1, 3.0));
                double radius = P(p2, folds == 2 ? 1.0 : 0.9);
                return Parametric((phi, r) => EnneperPoint(folds, r, phi), 0, 2 * Math.PI, 0, radius, n, Math.Max(6, n / 2), true, false, true, false);
            }
            case "ruled":
            {
                // skew bilinear patch: corners of a size×size square lifted by ±twist, one corner pushed sideways by skew
                double size = P(p1, 2.0), twist = P(p2, 0.8), skew = p3;
                Vec3d p00 = new Vec3d(-size / 2, -size / 2, 0), p10 = new Vec3d(size / 2, -size / 2, twist);
                Vec3d p01 = new Vec3d(-size / 2 + skew, size / 2, twist * 0.75), p11 = new Vec3d(size / 2 + skew, size / 2, -twist * 0.6);
                return Grid((u, v) => (1 - u) * (1 - v) * p00 + u * (1 - v) * p10 + (1 - u) * v * p01 + u * v * p11, 0, 1, 0, 1, n, n);
            }
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
                throw new ArgumentException($"Unknown shape '{name}'. Known: {string.Join(", ", Names)}", nameof(name));
        }
    }

    private static double P(double v, double dflt) => v > 0 ? v : dflt;

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
        nu = Math.Max(3, nu);
        nv = Math.Max(2, nv);
        int cu = closedU ? nu : nu + 1;   // distinct columns
        int cv = closedV ? nv : nv + 1;   // distinct rows
        var verts = new List<Vec3d>();
        var index = new int[cv, cu];

        for (int j = 0; j < cv; j++)
        {
            double v = v0 + (v1 - v0) * j / nv;
            bool pole = (collapseVStart && j == 0) || (collapseVEnd && j == cv - 1);
            if (pole)
            {
                var p = f(u0, v);
                int k = verts.Count;
                verts.Add(p);
                for (int i = 0; i < cu; i++) index[j, i] = k;
                continue;
            }
            for (int i = 0; i < cu; i++)
            {
                double u = u0 + (u1 - u0) * i / nu;
                index[j, i] = verts.Count;
                verts.Add(f(u, v));
            }
        }

        var faces = new List<int[]>();
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
            }
        }
        return new MeshData(verts.ToArray(), faces.ToArray());
    }

    private static void Add(List<int[]> faces, int a, int b, int c)
    {
        if (a == b || b == c || a == c) return;
        faces.Add(new[] { a, b, c });
    }
}
