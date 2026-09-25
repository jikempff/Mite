using System;
using System.Collections.Generic;
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
