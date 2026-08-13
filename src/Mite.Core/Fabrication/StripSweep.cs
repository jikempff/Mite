using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Fabrication;

/// <summary>
/// Sweeps a rectangular lath profile along an on-surface polyline, producing
/// a closed quad strip solid. The cross-section rides in the Darboux frame of
/// the surface (tangent / surface normal / in-surface across), so a flat
/// lath hugs the surface and an upright lath stands perpendicular to it —
/// the two gridshell construction modes. Surface normals are the smooth
/// (barycentric-interpolated) ones, so the strip does not kink at facet
/// crossings.
/// </summary>
public static class StripSweep
{
    public readonly struct Result
    {
        /// <summary>Closed quad mesh of the swept strip (4 vertices per station).</summary>
        public readonly MeshData Mesh;

        /// <summary>Strip centerline per station (reference curve lifted by Offset + half the normal depth).</summary>
        public readonly Vec3d[] Centers;

        /// <summary>Curve tangent per station.</summary>
        public readonly Vec3d[] Tangents;

        /// <summary>Surface normal per station, orthonormalized against the tangent.</summary>
        public readonly Vec3d[] Normals;

        /// <summary>In-surface across direction per station: Normals x Tangents.</summary>
        public readonly Vec3d[] Sideways;

        public Result(MeshData mesh, Vec3d[] centers, Vec3d[] tangents, Vec3d[] normals, Vec3d[] sideways)
        {
            Mesh = mesh;
            Centers = centers;
            Tangents = tangents;
            Normals = normals;
            Sideways = sideways;
        }
    }

    /// <summary>
    /// Sweeps one polyline. Returns null when the polyline has fewer than two
    /// distinct points. A polyline whose ends coincide is swept as a closed
    /// band (wrapped seam, no end caps).
    /// </summary>
    public static Result? Sweep(MeshProjection proj, Vec3d[] polyline, LathProfile profile)
    {
        if (profile.Width <= 0 || profile.Thickness <= 0)
            throw new ArgumentException("Profile width and thickness must be positive.", nameof(profile));

        // Drop consecutive duplicate points (they would poison the tangents)
        var pts = new List<Vec3d>(polyline.Length);
        foreach (var p in polyline)
            if (pts.Count == 0 || (p - pts[pts.Count - 1]).LengthSquared > 1e-24)
                pts.Add(p);
        if (pts.Count < 2) return null;

        bool closed = (pts[0] - pts[pts.Count - 1]).LengthSquared < 1e-18 && pts.Count > 3;
        if (closed) pts.RemoveAt(pts.Count - 1);
        int n = pts.Count;

        // Smooth surface normals along the curve
        var normals = new Vec3d[n];
        int hint = proj.NearestVertexGlobal(pts[0]);
        for (int i = 0; i < n; i++)
        {
            var hit = proj.ClosestPoint(pts[i], hint);
            normals[i] = hit.SmoothNormal;
            hint = hit.NearestVertex;
        }

        // Station frames
        var tangents = new Vec3d[n];
        var across = new Vec3d[n];
        for (int i = 0; i < n; i++)
        {
            Vec3d t;
            if (closed || (i > 0 && i < n - 1))
            {
                Vec3d prev = pts[(i - 1 + n) % n];
                Vec3d next = pts[(i + 1) % n];
                if (!closed)
                {
                    prev = pts[i - 1];
                    next = pts[i + 1];
                }
                Vec3d t0 = (pts[i] - prev).Normalized();
                Vec3d t1 = (next - pts[i]).Normalized();
                t = t0 + t1;
                if (t.LengthSquared < 1e-20) t = t1; // hairpin: keep going
            }
            else
            {
                t = i == 0 ? pts[1] - pts[0] : pts[n - 1] - pts[n - 2];
            }
            if (t.LengthSquared < 1e-20) t = new Vec3d(1, 0, 0);
            t = t.Normalized();

            Vec3d nv = normals[i];
            if (nv.LengthSquared < 1e-20)
                nv = i > 0 ? normals[i - 1] : new Vec3d(0, 0, 1);
            nv = nv - Vec3d.Dot(nv, t) * t;
            if (nv.LengthSquared < 1e-20)
            {
                // Normal parallel to the tangent (degenerate hit): any perpendicular
                Vec3d axis = Math.Abs(t.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
                nv = Vec3d.Cross(axis, t);
            }
            nv = nv.Normalized();

            tangents[i] = t;
            normals[i] = nv;
            across[i] = Vec3d.Cross(nv, t);
        }

        // Cross-section axes: across the strip / through the strip
        double halfW = 0.5 * profile.Width;
        double halfT = 0.5 * profile.Thickness;
        double lift = profile.Offset + 0.5 * profile.NormalDepth;

        var verts = new Vec3d[4 * n];
        var centers = new Vec3d[n];
        for (int i = 0; i < n; i++)
        {
            Vec3d a = profile.Upright ? normals[i] : across[i];
            Vec3d b = profile.Upright ? across[i] : normals[i];
            Vec3d c = pts[i] + lift * normals[i];
            centers[i] = c;
            verts[4 * i + 0] = c - halfW * a - halfT * b;
            verts[4 * i + 1] = c + halfW * a - halfT * b;
            verts[4 * i + 2] = c + halfW * a + halfT * b;
            verts[4 * i + 3] = c - halfW * a + halfT * b;
        }

        var faces = new List<int[]>(4 * (closed ? n : n - 1) + (closed ? 0 : 2));
        int stationCount = closed ? n : n - 1;
        for (int i = 0; i < stationCount; i++)
        {
            int i2 = (i + 1) % n;
            for (int s = 0; s < 4; s++)
            {
                int s2 = (s + 1) % 4;
                faces.Add(new[] { 4 * i + s, 4 * i + s2, 4 * i2 + s2, 4 * i2 + s });
            }
        }
        if (!closed)
        {
            faces.Add(new[] { 3, 2, 1, 0 });
            faces.Add(new[] { 4 * (n - 1), 4 * (n - 1) + 1, 4 * (n - 1) + 2, 4 * (n - 1) + 3 });
        }

        return new Result(new MeshData(verts, faces.ToArray()), centers, tangents, normals, across);
    }

    /// <summary>Sweeps many polylines against one reference mesh.</summary>
    public static List<Result> SweepAll(MeshProjection proj, IEnumerable<Vec3d[]> polylines, LathProfile profile)
    {
        var results = new List<Result>();
        foreach (var line in polylines)
        {
            var r = Sweep(proj, line, profile);
            if (r.HasValue) results.Add(r.Value);
        }
        return results;
    }
}
