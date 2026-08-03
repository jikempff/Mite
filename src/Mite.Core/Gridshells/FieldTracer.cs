using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Follows a tangent line field across a mesh with a midpoint (RK2) scheme,
/// projecting each step back onto the surface.
///
/// The field is sampled with barycentric interpolation inside the containing
/// face. Line fields carry two ambiguities that both cause kinked traces if
/// ignored: each vertex vector has an arbitrary sign, and for two-family
/// fields (e.g. asymptotic directions) the family labels themselves can swap
/// from vertex to vertex, because they are derived from principal directions
/// whose own orientation is arbitrary. Both are resolved per sample by picking,
/// at every face corner, the candidate direction (±primary, ±secondary) best
/// aligned with the current travel direction before blending.
/// </summary>
internal static class FieldTracer
{
    internal static List<Vec3d> Trace(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d[] dirsA, Vec3d[]? dirsB, bool[]? mask,
        double stepSize, int maxSteps, bool reverse, Func<Vec3d, bool>? stopNear, out bool closedLoop)
    {
        closedLoop = false;
        var points = new List<Vec3d>();

        var hit = proj.ClosestPoint(startPos, startHint);
        Vec3d pos = hit.Point;
        points.Add(pos);

        if (hit.Face < 0) return points;
        Vec3d prevDir = (reverse ? -1.0 : 1.0) * dirsA[hit.NearestVertex];
        if (prevDir.LengthSquared < 1e-20) return points;
        prevDir = prevDir.Normalized();

        for (int step = 0; step < maxSteps; step++)
        {
            if (mask != null && !mask[hit.NearestVertex]) break;

            // Midpoint scheme: sample at the current point, walk half a step,
            // sample again, take the full step with the midpoint direction.
            // The blended field magnitude doubles as a support measure: masked
            // (non-existent) corners contribute zero, so the trace fades out
            // smoothly at region borders instead of ending on a ragged stub.
            Vec3d d0 = SampleLineField(proj, hit, dirsA, dirsB, prevDir);
            if (d0.LengthSquared < 0.09) break;
            d0 = d0.Normalized();

            var midHit = proj.ClosestPoint(pos + 0.5 * stepSize * d0, hit.NearestVertex);
            Vec3d d1 = midHit.Face >= 0 ? SampleLineField(proj, midHit, dirsA, dirsB, d0) : d0;
            if (d1.LengthSquared < 1e-12) d1 = d0;
            d1 = d1.Normalized();

            Vec3d intended = pos + stepSize * d1;
            var newHit = proj.ClosestPoint(intended, midHit.NearestVertex);
            Vec3d newPos = newHit.Point;

            // Fell off the mesh: the projection clamped the step to a boundary
            // far from the intended target. End cleanly at the edge instead of
            // letting the trace crawl along the boundary as a "thread".
            if ((newPos - intended).Length > 0.5 * stepSize)
            {
                Vec3d travel = newPos - pos;
                if (Vec3d.Dot(travel, d1) > 0 && travel.LengthSquared > 0.01 * stepSize * stepSize)
                    points.Add(newPos);
                break;
            }

            // Stalled against a boundary
            if ((newPos - pos).LengthSquared < 0.01 * stepSize * stepSize) break;

            // Ran into an already-traced curve
            if (stopNear != null && stopNear(newPos)) break;

            prevDir = (newPos - pos).Normalized();
            pos = newPos;
            hit = newHit;
            points.Add(pos);

            // Closed loop: returned to the start after traveling away
            if (step > 4 && (pos - startPos).Length < 0.75 * stepSize)
            {
                points.Add(startPos);
                closedLoop = true;
                break;
            }
        }

        return points;
    }

    /// <summary>
    /// Samples the line field at a hit point: at each corner of the containing
    /// face, the candidate among ±dirsA (and ±dirsB when given) best aligned
    /// with the reference direction is chosen, then the picks are blended with
    /// the hit's barycentric weights and flattened into the local tangent plane.
    /// </summary>
    private static Vec3d SampleLineField(
        MeshProjection proj, in MeshProjection.Hit hit, Vec3d[] dirsA, Vec3d[]? dirsB, Vec3d reference)
    {
        var face = proj.Mesh.Faces[hit.Face];
        Vec3d sum = Vec3d.Zero;

        for (int k = 0; k < 3; k++)
        {
            double w = hit.Bary[k];
            if (w <= 0) continue;
            int vi = face[k];

            Vec3d best = Vec3d.Zero;
            double bestDot = 0;

            AlignCandidate(dirsA[vi], reference, ref best, ref bestDot);
            if (dirsB != null)
                AlignCandidate(dirsB[vi], reference, ref best, ref bestDot);

            sum = sum + w * best;
        }

        // Flatten into the tangent plane so the step follows the surface
        return sum - Vec3d.Dot(sum, hit.SmoothNormal) * hit.SmoothNormal;
    }

    private static void AlignCandidate(Vec3d candidate, Vec3d reference, ref Vec3d best, ref double bestDot)
    {
        if (candidate.LengthSquared < 1e-20) return;
        double d = Vec3d.Dot(candidate, reference);
        if (Math.Abs(d) > Math.Abs(bestDot))
        {
            bestDot = d;
            best = d >= 0 ? candidate : -candidate;
        }
    }

    /// <summary>
    /// Traces both ways through a point and joins the halves into one polyline.
    /// When the forward half closes into a loop, it is returned directly instead
    /// of tracing backward over the same cycle again.
    /// </summary>
    internal static Vec3d[] TraceBoth(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d[] dirsA, Vec3d[]? dirsB, bool[]? mask,
        double stepSize, int maxSteps, Func<Vec3d, bool>? stopNear)
    {
        var forward = Trace(proj, startPos, startHint, dirsA, dirsB, mask,
            stepSize, maxSteps, false, stopNear, out bool closed);
        if (closed) return forward.ToArray();

        var backward = Trace(proj, startPos, startHint, dirsA, dirsB, mask,
            stepSize, maxSteps, true, stopNear, out _);
        return Join(backward, forward);
    }

    /// <summary>Joins a backward and forward trace into one polyline through the seed.</summary>
    internal static Vec3d[] Join(List<Vec3d> backward, List<Vec3d> forward)
    {
        var line = new List<Vec3d>();
        for (int i = backward.Count - 1; i > 0; i--)
            line.Add(backward[i]);
        line.AddRange(forward);
        return line.ToArray();
    }
}
