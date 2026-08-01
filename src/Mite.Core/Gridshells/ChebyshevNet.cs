using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Chebyshev net construction by the compass method. A Chebyshev net has the
/// same edge length in both families everywhere — the kinematics of an elastic
/// gridshell: a flat lattice of constant-length laths with rotating joints,
/// bent into shape. The two axis curves are geodesics traced from the seed;
/// every interior node is placed by intersecting constant-length edges from
/// its two parents on the surface.
/// </summary>
public static class ChebyshevNet
{
    public class Options
    {
        public double EdgeLength { get; set; } = 1.0;

        /// <summary>Nodes per side of the seed in the first family.</summary>
        public int CountU { get; set; } = 10;

        /// <summary>Nodes per side of the seed in the second family.</summary>
        public int CountV { get; set; } = 10;

        /// <summary>Angle between the two families at the seed (default 90 degrees).</summary>
        public double Angle { get; set; } = Math.PI / 2;
    }

    public readonly struct Result
    {
        /// <summary>Net nodes, indexed [u, v]. Only entries flagged in Valid are meaningful.</summary>
        public readonly Vec3d[,] Points;

        /// <summary>True where the net reached the surface; ragged at boundaries.</summary>
        public readonly bool[,] Valid;

        /// <summary>Indices of the seed node.</summary>
        public readonly int OriginU;
        public readonly int OriginV;

        public Result(Vec3d[,] points, bool[,] valid, int originU, int originV)
        {
            Points = points;
            Valid = valid;
            OriginU = originU;
            OriginV = originV;
        }
    }

    public static Result Compute(MeshData mesh, int seedVertex, Vec3d direction, Options? options = null)
    {
        options ??= new Options();
        var proj = new MeshProjection(mesh);
        double L = options.EdgeLength;

        int cu = Math.Max(1, options.CountU);
        int cv = Math.Max(1, options.CountV);
        int nu = 2 * cu + 1, nv = 2 * cv + 1;
        int ou = cu, ov = cv;

        var pts = new Vec3d[nu, nv];
        var valid = new bool[nu, nv];
        var hints = new int[nu, nv];

        if (seedVertex < 0 || seedVertex >= proj.Mesh.VertexCount || L <= 0)
            return new Result(pts, valid, ou, ov);

        var seedHit = proj.ClosestPoint(proj.Mesh.Vertices[seedVertex], seedVertex);
        Vec3d n0 = seedHit.SmoothNormal;

        // Family directions in the seed's tangent plane
        Vec3d d1 = direction - Vec3d.Dot(direction, n0) * n0;
        if (d1.LengthSquared < 1e-20)
            d1 = Vec3d.Cross(n0, Math.Abs(n0.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0));
        d1 = d1.Normalized();
        Vec3d d2 = (Math.Cos(options.Angle) * d1 +
                    Math.Sin(options.Angle) * Vec3d.Cross(n0, d1).Normalized()).Normalized();

        pts[ou, ov] = seedHit.Point;
        valid[ou, ov] = true;
        hints[ou, ov] = seedHit.NearestVertex;

        // Axis curves: geodesics resampled at exact edge length
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, d1, L, cu, ou, ov, 1, 0);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, -d1, L, cu, ou, ov, -1, 0);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, d2, L, cv, ou, ov, 0, 1);
        FillAxis(proj, pts, valid, hints, seedHit.Point, seedHit.NearestVertex, -d2, L, cv, ou, ov, 0, -1);

        // Fill the four quadrants with the compass rule:
        // guess = uParent + vParent - diagonal, then enforce both edge lengths
        foreach (int su in new[] { 1, -1 })
        {
            foreach (int sv in new[] { 1, -1 })
            {
                for (int a = 1; a <= cu; a++)
                {
                    for (int b = 1; b <= cv; b++)
                    {
                        int i = ou + su * a, j = ov + sv * b;
                        int ip = i - su, jp = j - sv;

                        if (!valid[ip, j] || !valid[i, jp] || !valid[ip, jp]) continue;

                        Vec3d pA = pts[ip, j], pB = pts[i, jp], pD = pts[ip, jp];
                        Vec3d guess = pA + pB - pD;

                        var hit = proj.ClosestPoint(guess, hints[ip, j]);
                        if ((hit.Point - guess).Length > L) continue;

                        Vec3d x = hit.Point;
                        for (int k = 0; k < 8; k++)
                        {
                            Vec3d ta = x - pA, tb = x - pB;
                            if (ta.LengthSquared < 1e-20 || tb.LengthSquared < 1e-20) break;
                            Vec3d target = 0.5 * ((pA + L * ta.Normalized()) + (pB + L * tb.Normalized()));
                            hit = proj.ClosestPoint(target, hit.NearestVertex);
                            x = hit.Point;
                        }

                        double eA = (x - pA).Length, eB = (x - pB).Length;
                        if (Math.Abs(eA - L) > 0.2 * L || Math.Abs(eB - L) > 0.2 * L) continue;

                        pts[i, j] = x;
                        valid[i, j] = true;
                        hints[i, j] = hit.NearestVertex;
                    }
                }
            }
        }

        return new Result(pts, valid, ou, ov);
    }

    private static void FillAxis(
        MeshProjection proj, Vec3d[,] pts, bool[,] valid, int[,] hints,
        Vec3d start, int startHint, Vec3d dir, double L, int count,
        int ou, int ov, int du, int dv)
    {
        double step = L / 8.0;
        int maxSteps = 16 * count + 16;
        var line = GeodesicCurves.TraceOneFrom(proj, start, startHint, dir, step, maxSteps, null);

        // Exact arc-length resampling at multiples of L
        int placed = 0;
        double acc = 0, target = L;
        int hint = startHint;
        for (int i = 1; i < line.Count && placed < count; i++)
        {
            Vec3d seg = line[i] - line[i - 1];
            double len = seg.Length;
            if (len < 1e-15) continue;

            while (acc + len >= target && placed < count)
            {
                double t = (target - acc) / len;
                Vec3d p = line[i - 1] + t * seg;
                var hit = proj.ClosestPoint(p, hint);
                hint = hit.NearestVertex;

                placed++;
                int iu = ou + du * placed, iv = ov + dv * placed;
                pts[iu, iv] = hit.Point;
                valid[iu, iv] = true;
                hints[iu, iv] = hint;

                target += L;
            }
            acc += len;
        }
    }
}
