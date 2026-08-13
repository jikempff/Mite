using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// Unrolls an on-surface lath strip to a flat 2D cutting pattern. The strip
/// mid-surface (between its two long edges) is triangulated and flattened
/// triangle by triangle — each triangle keeps its exact edge lengths, so the
/// pattern is isometric to the strip as swept. Works for both flat (geodesic)
/// and upright (asymptotic) laths; how well the developed strip lies flat on
/// the design surface depends on the geodesic torsion of the curve, which is
/// exactly what Lath Analysis reports.
/// </summary>
public static class StripUnroll
{
    public readonly struct Result
    {
        /// <summary>2D pattern of the strip's two long edges (z = 0).</summary>
        public readonly Vec3d[] EdgeA;
        public readonly Vec3d[] EdgeB;

        /// <summary>2D pattern of the strip centerline.</summary>
        public readonly Vec3d[] Centerline;

        /// <summary>Triangulated flat pattern mesh (2D vertices) for preview.</summary>
        public readonly MeshData FlatMesh;

        /// <summary>3D arc length of the centerline (cutting length).</summary>
        public readonly double Length;

        /// <summary>Strip width across the pattern.</summary>
        public readonly double Width;

        public Result(Vec3d[] edgeA, Vec3d[] edgeB, Vec3d[] centerline,
            MeshData flatMesh, double length, double width)
        {
            EdgeA = edgeA;
            EdgeB = edgeB;
            Centerline = centerline;
            FlatMesh = flatMesh;
            Length = length;
            Width = width;
        }
    }

    /// <summary>
    /// Unrolls one lath. Uses the same surface-aligned frames as StripSweep, so
    /// patterns are consistent with the swept solids. Returns null when the
    /// polyline has fewer than two distinct points.
    /// </summary>
    public static Result? Unroll(MeshProjection proj, Vec3d[] polyline, LathProfile profile)
    {
        if (profile.Width <= 0)
            throw new ArgumentException("Profile width must be positive.", nameof(profile));

        // Drop consecutive duplicates
        var pts = new List<Vec3d>(polyline.Length);
        foreach (var p in polyline)
            if (pts.Count == 0 || (p - pts[pts.Count - 1]).LengthSquared > 1e-24)
                pts.Add(p);
        if (pts.Count < 2) return null;

        bool closed = (pts[0] - pts[pts.Count - 1]).LengthSquared < 1e-18 && pts.Count > 3;
        if (closed) pts.RemoveAt(pts.Count - 1);
        int n = pts.Count;

        // Surface normals and station frames (same construction as StripSweep)
        var normals = new Vec3d[n];
        int hint = proj.NearestVertexGlobal(pts[0]);
        if (hint < 0) return null;
        for (int i = 0; i < n; i++)
        {
            var hit = proj.ClosestPoint(pts[i], hint);
            normals[i] = hit.SmoothNormal;
            hint = hit.NearestVertex;
        }

        var across = new Vec3d[n];
        double arcLength = 0;
        for (int i = 0; i < n; i++)
        {
            Vec3d t;
            if (closed || (i > 0 && i < n - 1))
            {
                Vec3d prev = pts[(i - 1 + n) % n];
                Vec3d next = pts[(i + 1) % n];
                if (!closed) { prev = pts[i - 1]; next = pts[i + 1]; }
                Vec3d t0 = (pts[i] - prev).Normalized();
                Vec3d t1 = (next - pts[i]).Normalized();
                t = t0 + t1;
                if (t.LengthSquared < 1e-20) t = t1;
            }
            else
            {
                t = i == 0 ? pts[1] - pts[0] : pts[n - 1] - pts[n - 2];
            }
            if (t.LengthSquared < 1e-20) t = new Vec3d(1, 0, 0);
            t = t.Normalized();

            Vec3d nv = normals[i];
            if (nv.LengthSquared < 1e-20) nv = i > 0 ? normals[i - 1] : new Vec3d(0, 0, 1);
            nv = nv - Vec3d.Dot(nv, t) * t;
            if (nv.LengthSquared < 1e-20)
            {
                Vec3d axis = Math.Abs(t.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
                nv = Vec3d.Cross(axis, t);
            }
            nv = nv.Normalized();

            Vec3d g = Vec3d.Cross(nv, t);
            across[i] = profile.Upright ? nv : g;

            if (i > 0) arcLength += (pts[i] - pts[i - 1]).Length;
        }
        if (closed) arcLength += (pts[0] - pts[n - 1]).Length;

        // 3D edge polylines of the strip mid-surface
        double halfW = 0.5 * profile.Width;
        var eA = new Vec3d[n];
        var eB = new Vec3d[n];
        for (int i = 0; i < n; i++)
        {
            eA[i] = pts[i] - halfW * across[i];
            eB[i] = pts[i] + halfW * across[i];
        }

        // Triangulate the strip: (A_i, B_i, B_{i+1}) and (A_i, B_{i+1}, A_{i+1})
        int segCount = closed ? n : n - 1;
        var triVerts = new List<Vec3d>(2 * n);  // 3D reference, order: A0..An, B0..Bn
        triVerts.AddRange(eA);
        triVerts.AddRange(eB);
        var tris = new List<int[]>(2 * segCount);
        for (int i = 0; i < segCount; i++)
        {
            int i2 = (i + 1) % n;
            tris.Add(new[] { i, n + i, n + i2 });       // A_i, B_i, B_{i+1}
            tris.Add(new[] { i, n + i2, i2 });          // A_i, B_{i+1}, A_{i+1}
        }

        // Flatten by sequential trilateration along the strip
        var flat = new Vec3d[2 * n];
        flat[0] = Vec3d.Zero;
        flat[n] = new Vec3d(0, (triVerts[n] - triVerts[0]).Length, 0);

        // Forward direction estimate for resolving trilateration ambiguities
        Vec3d forward = new Vec3d(1, 0, 0);
        for (int i = 0; i < segCount; i++)
        {
            int i2 = (i + 1) % n;
            // B_{i+1} from A_i and B_i
            flat[n + i2] = PlacePoint(flat[i], flat[n + i],
                (triVerts[n + i2] - triVerts[i]).Length,
                (triVerts[n + i2] - triVerts[n + i]).Length, forward);
            // A_{i+1} from A_i and B_{i+1}
            flat[i2] = PlacePoint(flat[i], flat[n + i2],
                (triVerts[i2] - triVerts[i]).Length,
                (triVerts[i2] - triVerts[n + i2]).Length, forward);

            Vec3d step = (flat[n + i2] + flat[i2]) - (flat[n + i] + flat[i]);
            if (step.LengthSquared > 1e-24) forward = step.Normalized();
        }

        var centerline = new Vec3d[n];
        for (int i = 0; i < n; i++)
            centerline[i] = 0.5 * (flat[i] + flat[n + i]);

        // Shift the pattern so its minimum is at the origin (nice for nesting)
        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (var f in flat) { minX = Math.Min(minX, f.X); minY = Math.Min(minY, f.Y); }
        var shift = new Vec3d(-minX, -minY, 0);
        for (int i = 0; i < flat.Length; i++) flat[i] = flat[i] + shift;
        for (int i = 0; i < n; i++) centerline[i] = centerline[i] + shift;

        var edgeA2d = new Vec3d[n];
        var edgeB2d = new Vec3d[n];
        for (int i = 0; i < n; i++) { edgeA2d[i] = flat[i]; edgeB2d[i] = flat[n + i]; }

        return new Result(edgeA2d, edgeB2d, centerline,
            new MeshData(flat, tris.ToArray()), arcLength, 2.0 * halfW);
    }

    /// <summary>
    /// Places point c in the plane given distances to two known points a and b,
    /// resolving the two-way ambiguity toward the current forward direction.
    /// </summary>
    private static Vec3d PlacePoint(Vec3d a, Vec3d b, double ra, double rb, Vec3d forward)
    {
        Vec3d d = b - a;
        double len = d.Length;
        if (len < 1e-15) return a + ra * forward;

        Vec3d u = d / len;
        double x = (ra * ra - rb * rb + len * len) / (2.0 * len);
        double h2 = ra * ra - x * x;
        double h = h2 > 0 ? Math.Sqrt(h2) : 0.0;
        Vec3d perp = new Vec3d(-u.Y, u.X, 0);

        Vec3d c1 = a + x * u + h * perp;
        Vec3d c2 = a + x * u - h * perp;
        return Vec3d.Dot(c1 - a, forward) >= Vec3d.Dot(c2 - a, forward) ? c1 : c2;
    }
}
