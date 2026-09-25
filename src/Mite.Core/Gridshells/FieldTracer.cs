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
        double stepSize, int maxSteps, bool reverse, Func<Vec3d, bool>? stopNear, out bool closedLoop,
        double minFieldMagnitude = 0.3, Vec3d? startDir = null)
    {
        return Trace(proj, startPos, startHint, dirsA, dirsB, mask, stepSize, maxSteps, reverse, stopNear,
            out closedLoop, out _, minFieldMagnitude, startDir);
    }

    /// <param name="stoppedNear">Set when the trace ended because stopNear fired (it ran into an existing curve).</param>
    internal static List<Vec3d> Trace(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d[] dirsA, Vec3d[]? dirsB, bool[]? mask,
        double stepSize, int maxSteps, bool reverse, Func<Vec3d, bool>? stopNear, out bool closedLoop,
        out bool stoppedNear, double minFieldMagnitude = 0.3, Vec3d? startDir = null)
    {
        closedLoop = false;
        stoppedNear = false;
        var points = new List<Vec3d>();

        var hit = proj.ClosestPoint(startPos, startHint);
        Vec3d pos = hit.Point;
        points.Add(pos);

        if (hit.Face < 0) return points;

        // Starting direction. With an explicit initial direction (e.g. the
        // tangent of the curve a candidate seed was spawned from) the best
        // aligned candidate of both families is used, so a seed never starts
        // in the wrong family. Otherwise the primary field alone is sampled
        // around the nearest vertex's direction, which fixes the family.
        Vec3d prevDir;
        if (startDir.HasValue && startDir.Value.LengthSquared > 1e-20)
        {
            prevDir = SampleLineField(proj, hit, dirsA, dirsB, startDir.Value.Normalized());
        }
        else
        {
            Vec3d reference = dirsA[hit.NearestVertex];
            if (reference.LengthSquared < 1e-20) return points;
            prevDir = SampleLineField(proj, hit, dirsA, null, reference.Normalized());
            if (prevDir.LengthSquared < 1e-20) prevDir = reference;
        }
        if (prevDir.LengthSquared < 1e-20) return points;
        prevDir = (reverse ? -1.0 : 1.0) * prevDir.Normalized();

        Vec3d startNormal = hit.SmoothNormal;
        Vec3d initialDir = prevDir;
        // Capture radius scaled to the mesh: integration drift over a full loop
        // is a fraction of the edge length (measured ~0.4x on a coarse sphere),
        // far more than one step. The direction match in TryCloseLoop guards
        // against snapping shut on geodesics that merely pass near the start.
        double captureRadius = Math.Max(0.75 * stepSize, 0.5 * proj.AverageEdgeLength);
        double leaveRadius = Math.Max(8.0 * stepSize, 2.0 * captureRadius);
        double maxStartDist = 0.0;
        double pathLength = 0.0;

        for (int step = 0; step < maxSteps; step++)
        {
            if (mask != null && !mask[hit.NearestVertex]) break;

            // Midpoint scheme: sample at the current point, walk half a step,
            // sample again, take the full step with the midpoint direction.
            // The blended field magnitude doubles as a support measure: masked
            // (non-existent) corners contribute zero, so the trace fades out
            // smoothly at region borders instead of ending on a ragged stub.
            Vec3d d0 = SampleLineField(proj, hit, dirsA, dirsB, prevDir);
            if (d0.Length < minFieldMagnitude) break;
            d0 = d0.Normalized();

            var midHit = proj.ClosestPoint(pos + 0.5 * stepSize * d0, hit.NearestVertex);
            Vec3d d1 = midHit.Face >= 0 ? SampleLineField(proj, midHit, dirsA, dirsB, d0) : d0;
            if (d1.LengthSquared < 1e-12) d1 = d0;
            d1 = d1.Normalized();

            Vec3d intended = pos + stepSize * d1;
            var newHit = proj.ClosestPoint(intended, midHit.NearestVertex);
            Vec3d newPos = newHit.Point;

            // Left the mesh: end exactly where the step crosses the border
            if (LeftMesh(proj, pos, intended, newHit, stepSize, out Vec3d exit))
            {
                // Only an exit that lies ahead, within 37° of the travel
                // direction, ends the curve; a start on the border whose
                // outward step "exits" at a point along the border edge would
                // otherwise add a crawl along the border as the first segment
                Vec3d travel = exit - pos;
                if (travel.LengthSquared > 1e-6 * stepSize * stepSize && Vec3d.Dot(travel.Normalized(), d1) > 0.8)
                    points.Add(exit);
                break;
            }

            // Stalled against a boundary
            if ((newPos - pos).LengthSquared < 0.01 * stepSize * stepSize) break;

            // Ran into an already-traced curve
            if (stopNear != null && stopNear(newPos)) { stoppedNear = true; break; }

            prevDir = (newPos - pos).Normalized();
            pathLength += (newPos - pos).Length;
            pos = newPos;
            hit = newHit;
            points.Add(pos);

            maxStartDist = Math.Max(maxStartDist, (pos - points[0]).Length);

            // Closed loop: returned to the start after traveling away
            if (step > 4 && TryCloseLoop(points, pos, startNormal, initialDir, prevDir,
                    stepSize, captureRadius, leaveRadius, maxStartDist, pathLength, out Vec3d closing))
            {
                CloseSmoothly(proj, points, closing);
                closedLoop = true;
                break;
            }
        }

        return points;
    }

    /// <summary>
    /// Detects a step that leaves the mesh and computes the exit point on the
    /// border along the step. Two signs of leaving: the projection clamped the
    /// step far from the intended target (a clear fall-off), or the projected
    /// point sits on a boundary edge while the intended point was pulled
    /// sideways onto it within the face plane (an oblique exit, which would
    /// otherwise turn into a crawl along the border followed by a hook).
    /// Steps that merely follow curvature project along the normal, which the
    /// in-plane test ignores.
    /// </summary>
    internal static bool LeftMesh(MeshProjection proj, Vec3d pos, Vec3d intended, in MeshProjection.Hit newHit,
        double stepSize, out Vec3d exit)
    {
        exit = newHit.Point;
        Vec3d disp = newHit.Point - intended;
        bool fellOff = disp.Length > 0.5 * stepSize;
        bool oblique = false;
        if (!fellOff && newHit.Face >= 0 && proj.IsOnBoundary(newHit))
        {
            Vec3d n = newHit.Normal;
            Vec3d inPlane = disp - Vec3d.Dot(disp, n) * n;
            oblique = inPlane.Length > 0.02 * stepSize;
        }
        if (!fellOff && !oblique) return false;

        // A start on the border exits at the start itself (exit ≈ pos): keep
        // that rather than falling back to the clamped point, which lies a
        // step along the border
        if (!proj.TryBoundaryExit(pos, intended, newHit, out exit) ||
            Vec3d.Dot(exit - pos, intended - pos) < -1e-9 * stepSize * stepSize)
            exit = newHit.Point;
        return true;
    }

    /// <summary>
    /// Tests whether the latest position closes the trace into a loop. Three
    /// tests, in increasing order of drift tolerance:
    /// 1. landing within a step of the exact start point (unambiguous, always armed);
    /// 2. once the trace has left the start neighborhood (leaveRadius), passing
    ///    within the capture radius of the start while heading along the initial
    ///    direction — catches loops that drift past the start point by more than
    ///    a step, which midpoint integration does over a full loop on coarse meshes;
    /// 3. crossing the first segment of the trace in the start's tangent plane
    ///    while staying within the capture radius of it in 3D.
    /// Without tests 2–3, closed geodesics/streamlines on closed surfaces wrap
    /// around repeatedly until the step budget is exhausted.
    /// </summary>
    internal static bool TryCloseLoop(
        IReadOnlyList<Vec3d> points, Vec3d cur, Vec3d startNormal, Vec3d initialDir, Vec3d travelDir,
        double stepSize, double captureRadius, double leaveRadius, double maxStartDist, double pathLength,
        out Vec3d closingPoint)
    {
        closingPoint = default;
        Vec3d p0 = points[0];

        // 1. Exact return to the start point
        if ((cur - p0).Length < 0.75 * stepSize)
        {
            closingPoint = p0;
            return true;
        }

        if (points.Count < 2 || maxStartDist < leaveRadius) return false;

        // Long loops drift: on a faceted mesh the projection walk carries a
        // small systematic bias per step (measured ~0.8% of the loop length
        // on a coarse torus), so the capture radius grows with the distance
        // travelled, up to a few edge lengths. The drift itself is spread
        // back along the loop by CloseSmoothly.
        captureRadius = Math.Max(captureRadius, Math.Min(0.015 * pathLength, 4.0 * captureRadius));

        // 2. Drifted past the start point, still heading the way the loop left
        if ((cur - p0).Length < captureRadius && Vec3d.Dot(travelDir, initialDir) > 0.5)
        {
            closingPoint = p0;
            return true;
        }

        // 3. Crossing the first segment in the start tangent plane
        Vec3d p1 = points[1];
        Vec3d axis = p1 - p0;
        double segLen = axis.Length;
        if (segLen < 1e-15 || startNormal.LengthSquared < 1e-20) return false;
        axis = axis / segLen;
        Vec3d up = Vec3d.Cross(startNormal, axis);

        Vec3d prev = points[points.Count - 2];
        double a0v = Vec3d.Dot(prev - p0, up);
        double a1v = Vec3d.Dot(cur - p0, up);
        if (a0v * a1v > 0 || Math.Abs(a0v) + Math.Abs(a1v) < 1e-15) return false;

        double t = a0v / (a0v - a1v);
        double s = Vec3d.Dot(prev - p0, axis) + t * Vec3d.Dot(cur - prev, axis);
        if (s < -captureRadius || s > segLen + captureRadius) return false;

        Vec3d cross3 = prev + t * (cur - prev);
        double sClamped = Math.Max(0.0, Math.Min(segLen, s));
        Vec3d c3 = p0 + sClamped * axis;
        if ((cross3 - c3).Length > captureRadius) return false;

        // Close on the exact start point so downstream exact-equality closure
        // checks (fairing, sweeping, unrolling) see a true loop; the crossing
        // lies within one step of p0 so the backtrack is negligible.
        closingPoint = p0;
        return true;
    }

    /// <summary>
    /// Closes a traced loop onto its start point by spreading the closure gap
    /// (current end → closing point) linearly along the whole loop and
    /// reprojecting, so the seam carries no kink and the loop returns exactly
    /// to its first point.
    /// </summary>
    internal static void CloseSmoothly(MeshProjection proj, List<Vec3d> points, Vec3d closingPoint)
    {
        int n = points.Count;
        Vec3d gap = closingPoint - points[n - 1];
        if (n > 3 && gap.LengthSquared > 1e-24)
        {
            var arc = new double[n];
            for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + (points[i] - points[i - 1]).Length;
            double total = arc[n - 1];
            if (total > 1e-15)
            {
                int hint = proj.NearestVertexGlobal(points[0]);
                for (int i = 1; i < n; i++)
                {
                    var h = proj.ClosestPoint(points[i] + (arc[i] / total) * gap, hint);
                    points[i] = h.Point;
                    hint = h.NearestVertex;
                }
            }
        }
        points.Add(closingPoint);
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
        double stepSize, int maxSteps, Func<Vec3d, bool>? stopNear, double minFieldMagnitude = 0.3,
        Vec3d? initialDir = null)
    {
        return TraceBoth(proj, startPos, startHint, dirsA, dirsB, mask, stepSize, maxSteps, stopNear,
            out _, out _, minFieldMagnitude, initialDir);
    }

    /// <param name="startNear">The backward half (start of the returned line) ended on an existing curve.</param>
    /// <param name="endNear">The forward half (end of the returned line) ended on an existing curve.</param>
    internal static Vec3d[] TraceBoth(
        MeshProjection proj, Vec3d startPos, int startHint, Vec3d[] dirsA, Vec3d[]? dirsB, bool[]? mask,
        double stepSize, int maxSteps, Func<Vec3d, bool>? stopNear, out bool startNear, out bool endNear,
        double minFieldMagnitude = 0.3, Vec3d? initialDir = null)
    {
        startNear = false;
        var forward = Trace(proj, startPos, startHint, dirsA, dirsB, mask,
            stepSize, maxSteps, false, stopNear, out bool closed, out endNear, minFieldMagnitude, initialDir);
        if (closed) return forward.ToArray();

        var backward = Trace(proj, startPos, startHint, dirsA, dirsB, mask,
            stepSize, maxSteps, true, stopNear, out _, out startNear, minFieldMagnitude, initialDir);
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
