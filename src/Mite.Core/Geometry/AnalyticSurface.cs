using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Geometry;

/// <summary>
/// A smooth surface with closed-form derivatives, so that curvature, principal
/// and asymptotic directions are known exactly at every point — the ground
/// truth the tests, the bench page and the web app compare the discrete
/// estimators against. Two kinds:
///
/// Parametric x(u, v) with first and second partial derivatives (do Carmo 1976,
/// Differential Geometry of Curves and Surfaces, §3-3): with E = x_u·x_u,
/// F = x_u·x_v, G = x_v·x_v, n = x_u × x_v / |x_u × x_v|, e = x_uu·n,
/// f = x_uv·n, g = x_vv·n,
///   K = (e g − f²) / (E G − F²),
///   H = (e G − 2 f F + g E) / (2 (E G − F²))   (sign against n, see below),
///   k1, k2 = H ± √(H² − K),
/// the shape operator in the basis (x_u, x_v) is S = I⁻¹ II, its eigenvectors
/// (a, b) give the principal directions a x_u + b x_v, and the asymptotic
/// directions solve II(d, d) = e a² + 2 f a b + g b² = 0 (two real lines
/// where K &lt; 0, one where K = 0, none where K &gt; 0).
///
/// Implicit F(x, y, z) = 0 with gradient ∇F and Hessian H(F) (Goldman 2005,
/// Curvature formulas for implicit curves and surfaces, CAGD 22, eqs. 4.1–4.3):
///   K = ∇F · adj(H(F)) · ∇F / |∇F|⁴,
///   H = (∇F · H(F) · ∇F − |∇F|² tr H(F)) / (2 |∇F|³)   (relative to n = ∇F / |∇F|),
/// used for the nodal approximations of the Schwarz D and gyroid surfaces.
///
/// Sign of H: Mite reports a sphere with outward normals as H = +1/R
/// (MeanCurvature), which is the opposite of do Carmo's e = x_uu·n with the
/// same n. The mesh built by <see cref="Mesh"/> winds its faces so that the
/// vertex normal is x_u × x_v, so the truth returned here is
/// H = −(e G − 2 f F + g E) / (2 (E G − F²)) and k1 ≥ k2 follow; K is
/// independent of the orientation. The implicit H is likewise reported as
/// −(Goldman's K_M), i.e. +1/R on a sphere F = |x|² − R² whose gradient
/// points outward.
///
/// Closed forms used as independent checks of this machinery in the tests:
/// sphere K = 1/R², torus K = cos v / (r (R + r cos v)), hyperboloid of one
/// sheet K = −c² / (c² + (a² + c²) u²)², catenoid K = −1/(c² cosh⁴(z/c)) with
/// H = 0, Enneper K = −4 / (1 + u² + v²)⁴ with H = 0 (Weisstein, MathWorld),
/// monkey saddle K = −36 (x² + y²) / (1 + 9 (x² + y²)²)² and
/// H = 27 x (−x⁴ + 2 x² y² + 3 y⁴) / (1 + 9 (x² + y²)²)^{3/2} (MathWorld, up
/// to the sign convention), ellipsoid K = 1 / (a² b² c² (x²/a⁴ + y²/b⁴ + z²/c⁴)²)
/// with its four umbilics at (±a √((a² − b²)/(a² − c²)), 0, ±c √((b² − c²)/(a² − c²)))
/// for a &gt; b &gt; c, K = 0 with k2 = 0 on the cylinder (vault) and the cone, and
/// the two rulings of the bilinear patch as its asymptotic directions.
/// </summary>
public sealed class AnalyticSurface
{
    /// <summary>Exact differential geometry at one point of the surface.</summary>
    public readonly struct Curvature
    {
        public readonly Vec3d Point;
        /// <summary>Unit normal: x_u × x_v for a parametric surface, ∇F / |∇F| for an implicit one.</summary>
        public readonly Vec3d Normal;
        /// <summary>Gaussian curvature.</summary>
        public readonly double K;
        /// <summary>Mean curvature in Mite's sign convention (a sphere with outward normals: +1/R).</summary>
        public readonly double H;
        /// <summary>Principal curvatures, K1 ≥ K2.</summary>
        public readonly double K1, K2;
        /// <summary>Unit principal directions (lines: the sign is arbitrary); undefined at an umbilic.</summary>
        public readonly Vec3d D1, D2;
        /// <summary>Unit asymptotic directions where K &lt; 0 (both the ruling where K = 0; zero where K &gt; 0).</summary>
        public readonly Vec3d A1, A2;
        /// <summary>True where K &lt; 0 (two asymptotic directions) or K = 0 with k1 ≠ k2 (one).</summary>
        public readonly bool HasAsymptotic;
        /// <summary>True where the chart is singular (E G − F² = 0) or ∇F = 0 and nothing can be said.</summary>
        public readonly bool Degenerate;

        public Curvature(Vec3d point, Vec3d normal, double k, double h, double k1, double k2, Vec3d d1, Vec3d d2, Vec3d a1, Vec3d a2, bool hasAsymptotic, bool degenerate)
        {
            Point = point; Normal = normal; K = k; H = h; K1 = k1; K2 = k2; D1 = d1; D2 = d2; A1 = a1; A2 = a2; HasAsymptotic = hasAsymptotic; Degenerate = degenerate;
        }

        /// <summary>|k1 − k2| relative to max(|k1|, |k2|): 0 at an umbilic.</summary>
        public double UmbilicMeasure => Math.Abs(K1 - K2) / Math.Max(1e-300, Math.Max(Math.Abs(K1), Math.Abs(K2)));
    }

    public string Name { get; }

    // ---- parametric data ----
    public Func<double, double, Vec3d>? X { get; }
    public Func<double, double, Vec3d>? Xu { get; }
    public Func<double, double, Vec3d>? Xv { get; }
    public Func<double, double, Vec3d>? Xuu { get; }
    public Func<double, double, Vec3d>? Xuv { get; }
    public Func<double, double, Vec3d>? Xvv { get; }
    public double U0 { get; }
    public double U1 { get; }
    public double V0 { get; }
    public double V1 { get; }
    public bool ClosedU { get; }
    public bool ClosedV { get; }
    public bool CollapseV0 { get; }
    public bool CollapseV1 { get; }
    /// <summary>Grid counts (nu, nv) for a given resolution — the same rule <see cref="AnalyticShapes.Build"/> uses.</summary>
    public Func<int, (int nu, int nv)>? Grid { get; }

    // ---- implicit data ----
    public Func<Vec3d, double>? F { get; }
    public Func<Vec3d, Vec3d>? Gradient { get; }
    public Func<Vec3d, Matrix3d>? Hessian { get; }

    public bool IsParametric => X != null;
    public bool IsImplicit => F != null;

    public AnalyticSurface(string name,
        Func<double, double, Vec3d> x, Func<double, double, Vec3d> xu, Func<double, double, Vec3d> xv,
        Func<double, double, Vec3d> xuu, Func<double, double, Vec3d> xuv, Func<double, double, Vec3d> xvv,
        double u0, double u1, double v0, double v1, Func<int, (int nu, int nv)> grid,
        bool closedU = false, bool closedV = false, bool collapseV0 = false, bool collapseV1 = false)
    {
        Name = name; X = x; Xu = xu; Xv = xv; Xuu = xuu; Xuv = xuv; Xvv = xvv;
        U0 = u0; U1 = u1; V0 = v0; V1 = v1; Grid = grid;
        ClosedU = closedU; ClosedV = closedV; CollapseV0 = collapseV0; CollapseV1 = collapseV1;
    }

    public AnalyticSurface(string name, Func<Vec3d, double> f, Func<Vec3d, Vec3d> gradient, Func<Vec3d, Matrix3d> hessian)
    {
        Name = name; F = f; Gradient = gradient; Hessian = hessian;
    }

    /// <summary>Exact curvature at chart parameters (u, v) of a parametric surface.</summary>
    public Curvature At(double u, double v)
    {
        if (!IsParametric) throw new InvalidOperationException($"{Name} is implicit; use AtPoint.");
        Vec3d p = X!(u, v), xu = Xu!(u, v), xv = Xv!(u, v);
        double E = Vec3d.Dot(xu, xu), Fm = Vec3d.Dot(xu, xv), G = Vec3d.Dot(xv, xv);
        double det = E * G - Fm * Fm;
        Vec3d cross = Vec3d.Cross(xu, xv);
        double scale = Math.Max(E, G);
        if (det <= 1e-14 * scale * scale || scale <= 0)
            return new Curvature(p, Vec3d.Zero, double.NaN, double.NaN, double.NaN, double.NaN, Vec3d.Zero, Vec3d.Zero, Vec3d.Zero, Vec3d.Zero, false, true);
        Vec3d n = cross / Math.Sqrt(det);
        double e = Vec3d.Dot(Xuu!(u, v), n), f = Vec3d.Dot(Xuv!(u, v), n), g = Vec3d.Dot(Xvv!(u, v), n);
        double K = (e * g - f * f) / det;
        double Hdc = (e * G - 2 * f * Fm + g * E) / (2 * det);   // do Carmo's sign
        // shape operator S = I⁻¹ II in the (x_u, x_v) basis; its eigenvectors are the principal directions
        double s11 = (G * e - Fm * f) / det, s12 = (G * f - Fm * g) / det;
        double s21 = (E * f - Fm * e) / det, s22 = (E * g - Fm * f) / det;
        double tr = s11 + s22, dsc = Math.Max(0, tr * tr / 4 - (s11 * s22 - s12 * s21));
        double sq = Math.Sqrt(dsc);
        double l1 = tr / 2 + sq, l2 = tr / 2 - sq;   // do Carmo's principal curvatures, l1 ≥ l2
        Vec3d d1, d2;
        if (sq <= 1e-12 * Math.Max(1e-300, Math.Abs(tr)) || sq == 0)
        {
            d1 = xu.Normalized(); d2 = Vec3d.Cross(n, d1).Normalized();   // umbilic: any orthonormal pair
        }
        else
        {
            // eigenvector of [[s11, s12],[s21, s22]] for λ: (s12, λ − s11) or (λ − s22, s21), whichever is longer
            (double a, double b) Eig(double lam)
            {
                double a1 = s12, b1 = lam - s11, a2 = lam - s22, b2 = s21;
                double n1 = a1 * a1 * E + 2 * a1 * b1 * Fm + b1 * b1 * G, n2 = a2 * a2 * E + 2 * a2 * b2 * Fm + b2 * b2 * G;
                return n1 >= n2 ? (a1, b1) : (a2, b2);
            }
            var (a1, b1) = Eig(l1); var (a2, b2) = Eig(l2);
            // Mite's k1 = −l2 belongs to the eigenvector of l2, k2 = −l1 to that of l1
            d1 = (a2 * xu + b2 * xv).Normalized();
            d2 = (a1 * xu + b1 * xv).Normalized();
            // keep the pair orthonormal against round-off
            d2 = (d2 - Vec3d.Dot(d2, d1) * d1).Normalized();
        }
        // asymptotic directions: e a² + 2 f a b + g b² = 0
        Vec3d A1 = Vec3d.Zero, A2 = Vec3d.Zero; bool hasAsym = false;
        double disc = f * f - e * g;   // = −K · det
        if (disc >= 0 && (Math.Abs(e) > 0 || Math.Abs(f) > 0 || Math.Abs(g) > 0))
        {
            // roots (a : b) of e a² + 2 f a b + g b² = 0 by the cancellation-free quadratic formula:
            // q = −(f + sign(f) √(f² − e g)), roots a/b = q/e and a/b = g/q (Press et al., Numerical Recipes §5.6)
            double r = Math.Sqrt(Math.Max(0, disc));
            double q = -(f + (f < 0 ? -1 : 1) * r);
            double a1, b1, a2, b2;
            if (q == 0)
            {
                // f = 0 and e g = 0: a double root along x_v (e ≠ 0, g = 0) or along x_u (g ≠ 0, e = 0)
                if (Math.Abs(e) > 0) { a1 = 0; b1 = 1; } else { a1 = 1; b1 = 0; }
                a2 = a1; b2 = b1;
            }
            else
            {
                if (Math.Abs(e) > 0) { a1 = q; b1 = e; } else { a1 = 1; b1 = 0; }
                a2 = g; b2 = q;
            }
            A1 = (a1 * xu + b1 * xv).Normalized();
            A2 = (a2 * xu + b2 * xv).Normalized();
            hasAsym = true;
        }
        // Mite's sign: a sphere with outward normal has H = +1/R
        double H = -Hdc, k1 = -l2, k2 = -l1;
        if (k1 < k2) { (k1, k2) = (k2, k1); (d1, d2) = (d2, d1); }
        return new Curvature(p, n, K, H, k1, k2, d1, d2, A1, A2, hasAsym, false);
    }

    /// <summary>
    /// Exact curvature of an implicit surface at a point on it (Goldman 2005).
    /// The point is not projected: pass a point that satisfies F = 0.
    /// </summary>
    public Curvature AtPoint(Vec3d p)
    {
        if (!IsImplicit) throw new InvalidOperationException($"{Name} is parametric; use At(u, v).");
        Vec3d g = Gradient!(p); Matrix3d Hm = Hessian!(p);
        double g2 = g.LengthSquared;
        if (g2 <= 1e-28)
            return new Curvature(p, Vec3d.Zero, double.NaN, double.NaN, double.NaN, double.NaN, Vec3d.Zero, Vec3d.Zero, Vec3d.Zero, Vec3d.Zero, false, true);
        double gl = Math.Sqrt(g2);
        Vec3d n = g / gl;
        // adjugate of the symmetric Hessian
        double h11 = Hm[0, 0], h12 = Hm[0, 1], h13 = Hm[0, 2], h22 = Hm[1, 1], h23 = Hm[1, 2], h33 = Hm[2, 2];
        double a11 = h22 * h33 - h23 * h23, a12 = -(h12 * h33 - h13 * h23), a13 = h12 * h23 - h13 * h22;
        double a22 = h11 * h33 - h13 * h13, a23 = -(h11 * h23 - h12 * h13), a33 = h11 * h22 - h12 * h12;
        double gAg = g.X * (a11 * g.X + a12 * g.Y + a13 * g.Z) + g.Y * (a12 * g.X + a22 * g.Y + a23 * g.Z) + g.Z * (a13 * g.X + a23 * g.Y + a33 * g.Z);
        double K = gAg / (g2 * g2);
        Vec3d Hg = Hm * g;
        double trH = h11 + h22 + h33;
        double Km = (Vec3d.Dot(g, Hg) - g2 * trH) / (2 * g2 * gl);   // Goldman's K_M: −1/R on a sphere with outward ∇F
        double H = -Km;
        double dsc = Math.Max(0, H * H - K), sq = Math.Sqrt(dsc);
        double k1 = H + sq, k2 = H - sq;
        // principal directions: eigenvectors of the shape operator (I − n nᵀ) H(F) (I − n nᵀ) / |∇F| in the
        // tangent plane (Goldman 2005 §4; the sign is Mite's, so a sphere with outward ∇F has +1/R twice)
        Vec3d t1 = Math.Abs(n.X) < 0.9 ? Vec3d.Cross(n, new Vec3d(1, 0, 0)).Normalized() : Vec3d.Cross(n, new Vec3d(0, 1, 0)).Normalized();
        Vec3d t2 = Vec3d.Cross(n, t1);
        double s11 = Vec3d.Dot(t1, Hm * t1) / gl, s12 = Vec3d.Dot(t1, Hm * t2) / gl, s22 = Vec3d.Dot(t2, Hm * t2) / gl;
        double th = 0.5 * Math.Atan2(2 * s12, s11 - s22);
        Vec3d d1 = (Math.Cos(th) * t1 + Math.Sin(th) * t2).Normalized();
        Vec3d d2 = Vec3d.Cross(n, d1).Normalized();
        double kd1 = Math.Cos(th) * Math.Cos(th) * s11 + 2 * Math.Cos(th) * Math.Sin(th) * s12 + Math.Sin(th) * Math.Sin(th) * s22;
        if (Math.Abs(kd1 - k1) > Math.Abs(kd1 - k2)) (d1, d2) = (d2, d1);
        Vec3d A1 = Vec3d.Zero, A2 = Vec3d.Zero; bool hasAsym = false;
        if (K <= 0 && sq > 0)
        {
            // II(cos φ d1 + sin φ d2) = k1 cos² φ + k2 sin² φ = 0 → tan² φ = −k1 / k2
            double phi = Math.Atan(Math.Sqrt(Math.Max(0, -k1 / k2)));
            if (Math.Abs(k2) < 1e-300) phi = Math.PI / 2;
            A1 = (Math.Cos(phi) * d1 + Math.Sin(phi) * d2).Normalized();
            A2 = (Math.Cos(phi) * d1 - Math.Sin(phi) * d2).Normalized();
            hasAsym = true;
        }
        return new Curvature(p, n, K, H, k1, k2, d1, d2, A1, A2, hasAsym, false);
    }

    /// <summary>
    /// The mesh <see cref="AnalyticShapes.Build"/> makes for this surface, with the chart
    /// parameters of every vertex (a collapsed pole carries (U0, V0) or (U0, V1)) and the
    /// quads of the parameter grid before triangulation (3 indices at a pole).
    /// </summary>
    public MeshData Mesh(int resolution, out (double u, double v)[] uv, out int[][] quads)
    {
        if (!IsParametric) throw new InvalidOperationException($"{Name} is implicit; use AnalyticShapes.Build.");
        var (nu, nv) = Grid!(resolution);
        return AnalyticShapes.ParametricGrid(X!, U0, U1, V0, V1, nu, nv, ClosedU, ClosedV, CollapseV0, CollapseV1, out uv, out quads);
    }

    public MeshData Mesh(int resolution) => Mesh(resolution, out _, out _);

    /// <summary>Exact curvature at every vertex of <see cref="Mesh"/> (NaN fields where the chart is singular).</summary>
    public Curvature[] AtVertices((double u, double v)[] uv)
    {
        var r = new Curvature[uv.Length];
        for (int i = 0; i < uv.Length; i++) r[i] = At(uv[i].u, uv[i].v);
        return r;
    }

    // ---------------------------------------------------------------
    // Builders for the common shape families
    // ---------------------------------------------------------------

    /// <summary>Height field z = f(x, y) over [x0, x1] × [y0, y1] (Monge patch): x_u = (1, 0, f_x), x_v = (0, 1, f_y).</summary>
    public static AnalyticSurface Graph(string name, Func<double, double, double> f, Func<double, double, double> fx, Func<double, double, double> fy,
        Func<double, double, double> fxx, Func<double, double, double> fxy, Func<double, double, double> fyy,
        double x0, double x1, double y0, double y1, Func<int, (int, int)> grid)
    {
        return new AnalyticSurface(name,
            (x, y) => new Vec3d(x, y, f(x, y)),
            (x, y) => new Vec3d(1, 0, fx(x, y)),
            (x, y) => new Vec3d(0, 1, fy(x, y)),
            (x, y) => new Vec3d(0, 0, fxx(x, y)),
            (x, y) => new Vec3d(0, 0, fxy(x, y)),
            (x, y) => new Vec3d(0, 0, fyy(x, y)),
            x0, x1, y0, y1, grid);
    }

    /// <summary>
    /// Surface of revolution about z with profile (r(t), z(t)), parametrised
    /// (θ, t) → (r cos θ, r sin θ, z): x_θ = (−r sin θ, r cos θ, 0), x_t = (r' cos θ, r' sin θ, z').
    /// </summary>
    public static AnalyticSurface Revolution(string name,
        Func<double, double> r, Func<double, double> r1, Func<double, double> r2,
        Func<double, double> z, Func<double, double> z1, Func<double, double> z2,
        double t0, double t1, Func<int, (int, int)> grid, bool collapseStart = false, bool collapseEnd = false)
    {
        return new AnalyticSurface(name,
            (th, t) => new Vec3d(r(t) * Math.Cos(th), r(t) * Math.Sin(th), z(t)),
            (th, t) => new Vec3d(-r(t) * Math.Sin(th), r(t) * Math.Cos(th), 0),
            (th, t) => new Vec3d(r1(t) * Math.Cos(th), r1(t) * Math.Sin(th), z1(t)),
            (th, t) => new Vec3d(-r(t) * Math.Cos(th), -r(t) * Math.Sin(th), 0),
            (th, t) => new Vec3d(-r1(t) * Math.Sin(th), r1(t) * Math.Cos(th), 0),
            (th, t) => new Vec3d(r2(t) * Math.Cos(th), r2(t) * Math.Sin(th), z2(t)),
            0, 2 * Math.PI, t0, t1, grid, closedU: true, collapseV0: collapseStart, collapseV1: collapseEnd);
    }

    /// <summary>
    /// Derivative check by central differences at (u, v): returns the largest relative
    /// error of x_u, x_v, x_uu, x_uv, x_vv against the finite differences of X (step h).
    /// </summary>
    public double DerivativeError(double u, double v, double h = 1e-4)
    {
        if (!IsParametric) throw new InvalidOperationException();
        Vec3d fu = (X!(u + h, v) - X(u - h, v)) / (2 * h), fv = (X(u, v + h) - X(u, v - h)) / (2 * h);
        Vec3d fuu = (X(u + h, v) - 2 * X(u, v) + X(u - h, v)) / (h * h);
        Vec3d fvv = (X(u, v + h) - 2 * X(u, v) + X(u, v - h)) / (h * h);
        Vec3d fuv = (X(u + h, v + h) - X(u + h, v - h) - X(u - h, v + h) + X(u - h, v - h)) / (4 * h * h);
        double scale = Math.Max(1e-9, Math.Max(Math.Max(Xu!(u, v).Length, Xv!(u, v).Length), Math.Max(Math.Max(Xuu!(u, v).Length, Xuv!(u, v).Length), Xvv!(u, v).Length)));
        double err = 0;
        err = Math.Max(err, (fu - Xu(u, v)).Length);
        err = Math.Max(err, (fv - Xv(u, v)).Length);
        err = Math.Max(err, (fuu - Xuu(u, v)).Length);
        err = Math.Max(err, (fuv - Xuv(u, v)).Length);
        err = Math.Max(err, (fvv - Xvv(u, v)).Length);
        return err / scale;
    }
}
