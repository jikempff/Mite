using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Evenly-spaced curve placement on a mesh, adapted from the Jobard-Lefer
/// streamline algorithm: trace one curve, spawn candidate seeds offset one
/// spacing to each side, keep candidates that are far enough from every
/// accepted curve, and stop traces when they close in on an existing curve.
/// Produces a complete, uniformly dense family from a single starting seed.
/// </summary>
public static class EvenlySpacedNet
{
    public class Options
    {
        /// <summary>Target distance between adjacent curves (0 = automatic, bounding box / 30).</summary>
        public double Spacing { get; set; } = 0.0;

        /// <summary>Integration step (0 = automatic: Spacing / 10, capped at half a mesh edge).</summary>
        public double StepSize { get; set; } = 0.0;

        /// <summary>Steps per half-curve (0 = automatic, from the mesh size).</summary>
        public int MaxSteps { get; set; } = 0;

        /// <summary>Safety cap on the number of curves.</summary>
        public int MaxCurves { get; set; } = 200;

        /// <summary>
        /// Continuous curves (default): a trace runs to the mesh border, the
        /// region border or back onto itself even where it comes closer than
        /// the spacing to a neighbour; it only stops when it has practically
        /// merged with an existing curve (within MergeFactor * Spacing). Off,
        /// traces stop as soon as they come within TestFactor * Spacing of an
        /// accepted curve (classic evenly-spaced streamlines), which keeps the
        /// spacing more even but ends laths in the middle of the surface.
        /// Either way an end that stops at a neighbour is snapped onto it so
        /// it forms a clean T-junction instead of floating beside it.
        /// </summary>
        public bool Continuous { get; set; } = true;

        /// <summary>Non-continuous mode: traces stop when closer than TestFactor * Spacing to an accepted curve.</summary>
        public double TestFactor { get; set; } = 0.4;

        /// <summary>Continuous mode: traces stop when closer than MergeFactor * Spacing to an accepted curve.</summary>
        public double MergeFactor { get; set; } = 0.15;

        /// <summary>On-surface Laplacian fairing passes applied to each traced curve (0 disables).</summary>
        public int SmoothingPasses { get; set; } = 10;

        /// <summary>
        /// Field traces stop where the blended field magnitude drops below this
        /// fraction of unit length (0..1); only used by TraceField.
        /// </summary>
        public double MinFieldMagnitude { get; set; } = 0.3;

        /// <summary>
        /// Curves shorter than this are discarded (0 = automatic: 2 * Spacing).
        /// Prevents the stub curves that dense seeding otherwise leaves near
        /// existing curves and region borders.
        /// </summary>
        public double MinCurveLength { get; set; } = 0.0;

        /// <summary>
        /// Optional cancellation probe checked between curves; return true to
        /// stop tracing and keep the curves produced so far (e.g. wire this to
        /// the host's Esc-key check so long solves stay interruptible).
        /// </summary>
        public Func<bool>? ShouldCancel { get; set; }

        /// <summary>Set after a run: true when the MaxCurves cap stopped growth with candidates left.</summary>
        public bool ReachedMaxCurves { get; internal set; }

        /// <summary>Set after a run: true when ShouldCancel interrupted growth.</summary>
        public bool Cancelled { get; internal set; }

        /// <summary>Set after a run: the spacing / step / step count actually used (after automatic defaults).</summary>
        public double ResolvedSpacing { get; internal set; }
        public double ResolvedStepSize { get; internal set; }
        public int ResolvedMaxSteps { get; internal set; }
    }

    private static double ArcLength(Vec3d[] line)
    {
        double len = 0;
        for (int i = 1; i < line.Length; i++) len += (line[i] - line[i - 1]).Length;
        return len;
    }

    /// <summary>
    /// Fills the surface with evenly-spaced curves following a per-vertex
    /// direction field (principal or asymptotic directions). Pass firstSeed -1
    /// to start from the masked vertex nearest the mesh centroid. A secondary
    /// field (the other asymptotic family) may be supplied so traces keep
    /// continuity across per-vertex family label swaps.
    /// </summary>
    public static List<Vec3d[]> TraceField(
        MeshData mesh, Vec3d[] dirs, bool[]? mask, int firstSeed, Options? options = null,
        Vec3d[]? secondaryDirs = null)
    {
        options ??= new Options();
        options.ReachedMaxCurves = false;
        options.Cancelled = false;
        var proj = new MeshProjection(mesh);
        var results = new List<Vec3d[]>();

        int seed = firstSeed >= 0 && firstSeed < proj.Mesh.VertexCount
            ? firstSeed
            : DefaultSeed(proj.Mesh, mask);
        if (seed < 0 || (mask != null && !mask[seed])) return results;

        Resolve(options, proj, out double spacing, out double step, out int maxSteps);
        var registry = new PointRegistry(spacing);
        double dtest = StopDistance(options, spacing);
        Func<Vec3d, bool> stop = p => registry.HasPointWithin(p, dtest);

        Vec3d[] TraceFrom(Vec3d pos, int hint, Vec3d? initialDir)
        {
            var line = FieldTracer.TraceBoth(proj, pos, hint, dirs, secondaryDirs, mask,
                step, maxSteps, stop, out bool startNear, out bool endNear, options.MinFieldMagnitude, initialDir);
            line = SnapEnds(proj, registry, line, startNear, endNear, spacing);
            return line.Length > 1 ? CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses) : line;
        }

        bool CandidateBlocked(MeshProjection.Hit chit) =>
            mask != null && !mask[chit.NearestVertex];

        Grow(proj, results, registry, options, spacing,
            TraceFrom, CandidateBlocked, null,
            proj.Mesh.Vertices[seed], seed);

        return results;
    }

    /// <summary>
    /// Fills the surface with evenly-spaced geodesics of one family. Each new
    /// geodesic starts one spacing beside an existing one, heading in the
    /// transported direction of its neighbor.
    /// </summary>
    public static List<Vec3d[]> TraceGeodesics(
        MeshData mesh, int firstSeed, Vec3d firstDir, Options? options = null)
    {
        options ??= new Options();
        options.ReachedMaxCurves = false;
        options.Cancelled = false;
        var proj = new MeshProjection(mesh);
        var results = new List<Vec3d[]>();

        if (firstSeed < 0 || firstSeed >= proj.Mesh.VertexCount) return results;
        if (firstDir.LengthSquared < 1e-20) return results;

        firstDir = GeodesicCurves.SeedTangent(proj, firstSeed, firstDir);
        if (firstDir.LengthSquared < 1e-20) return results;

        Resolve(options, proj, out double spacing, out double step, out int maxSteps);
        var registry = new PointRegistry(spacing);
        double dtest = StopDistance(options, spacing);
        Func<Vec3d, bool> stop = p => registry.HasPointWithin(p, dtest);

        Vec3d[] TraceFrom(Vec3d pos, int hint, Vec3d dir)
        {
            var line = GeodesicCurves.TraceBothFrom(proj, pos, hint, dir,
                step, maxSteps, stop, out bool startNear, out bool endNear);
            line = SnapEnds(proj, registry, line, startNear, endNear, spacing);
            return line.Length > 1 ? CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses) : line;
        }

        Grow(proj, results, registry, options, spacing,
            null, null, TraceFrom,
            proj.Mesh.Vertices[firstSeed], firstSeed, firstDir.Normalized());

        return results;
    }

    private static double StopDistance(Options opts, double spacing) =>
        (opts.Continuous ? Math.Max(0.0, opts.MergeFactor) : Math.Max(0.0, opts.TestFactor)) * spacing;

    /// <summary>
    /// Pulls a trace end that stopped beside an existing curve exactly onto
    /// that curve, blending the shift over the last stretch of the trace
    /// (up to two spacings) so the T-junction carries no kink. Ends that
    /// stopped for other reasons (border, region edge, closure) are untouched.
    /// </summary>
    private static Vec3d[] SnapEnds(MeshProjection proj, PointRegistry registry, Vec3d[] line,
        bool startNear, bool endNear, double spacing)
    {
        if (line.Length < 3 || (!startNear && !endNear)) return line;
        bool closed = (line[0] - line[line.Length - 1]).LengthSquared < 1e-24;
        if (closed) return line;

        var pts = new List<Vec3d>(line);
        if (endNear && registry.TryClosestOnCurves(pts[pts.Count - 1], 2.0 * spacing, out Vec3d target))
            BlendEnd(proj, pts, target, 2.0 * spacing, fromEnd: true);
        if (startNear && registry.TryClosestOnCurves(pts[0], 2.0 * spacing, out target))
            BlendEnd(proj, pts, target, 2.0 * spacing, fromEnd: false);
        return pts.ToArray();
    }

    private static void BlendEnd(MeshProjection proj, List<Vec3d> pts, Vec3d target, double blendLength, bool fromEnd)
    {
        int n = pts.Count;
        int endIdx = fromEnd ? n - 1 : 0;
        Vec3d gap = target - pts[endIdx];
        if (gap.LengthSquared < 1e-24) return;

        // Arc length measured from the moving end inward
        var arc = new double[n];
        if (fromEnd) { for (int i = n - 2; i >= 0; i--) arc[i] = arc[i + 1] + (pts[i + 1] - pts[i]).Length; }
        else { for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + (pts[i] - pts[i - 1]).Length; }
        double total = arc[fromEnd ? 0 : n - 1];
        double blend = Math.Min(blendLength, 0.5 * total);
        if (blend < 1e-15) { pts[endIdx] = target; return; }

        int hint = proj.NearestVertexGlobal(pts[endIdx]);
        for (int i = 0; i < n; i++)
        {
            if (arc[i] >= blend) continue;
            double w = 1.0 - arc[i] / blend;
            w = w * w * (3 - 2 * w); // smoothstep
            if (i == endIdx) { pts[i] = target; continue; }
            var h = proj.ClosestPoint(pts[i] + w * gap, hint);
            pts[i] = h.Point;
            hint = h.NearestVertex;
        }
    }

    private static void Resolve(Options opts, MeshProjection proj,
        out double spacing, out double step, out int maxSteps)
    {
        spacing = TraceDefaults.ResolveSpacing(opts.Spacing, proj);
        step = TraceDefaults.ResolveStep(opts.StepSize, spacing, proj);
        maxSteps = TraceDefaults.ResolveMaxSteps(opts.MaxSteps, step, proj);
        opts.ResolvedSpacing = spacing;
        opts.ResolvedStepSize = step;
        opts.ResolvedMaxSteps = maxSteps;
    }

    // Shared Jobard-Lefer loop. Field mode passes traceField; geodesic mode
    // passes traceDirected (candidates inherit the neighbor's tangent).
    private static void Grow(
        MeshProjection proj, List<Vec3d[]> results, PointRegistry registry, Options opts, double spacing,
        Func<Vec3d, int, Vec3d?, Vec3d[]>? traceField,
        Func<MeshProjection.Hit, bool>? candidateBlocked,
        Func<Vec3d, int, Vec3d, Vec3d[]>? traceDirected,
        Vec3d firstPos, int firstHint, Vec3d firstDir = default)
    {
        double thinGap = Math.Max(0.5 * StopDistance(opts, spacing), 0.02 * spacing);
        double minLen = opts.MinCurveLength > 0 ? opts.MinCurveLength : 2.0 * spacing;
        var queue = new Queue<Vec3d[]>();
        opts.ReachedMaxCurves = false;
        opts.Cancelled = false;

        // The first curve is kept regardless of length: if it is short, the
        // traceable region is simply small, and returning it beats returning nothing
        var first = traceField != null
            ? traceField(firstPos, firstHint, null)
            : traceDirected!(firstPos, firstHint, firstDir);
        if (first.Length < 2) return;

        results.Add(first);
        registry.AddLine(first, thinGap);
        queue.Enqueue(first);

        while (queue.Count > 0 && results.Count < opts.MaxCurves)
        {
            if (opts.ShouldCancel?.Invoke() == true) { opts.Cancelled = true; break; }
            var source = queue.Dequeue();
            int hint = proj.NearestVertexGlobal(source[0]);

            foreach (var sample in SampleAlong(source, spacing))
            {
                if (results.Count >= opts.MaxCurves) break;

                var hit = proj.ClosestPoint(sample.Point, hint);
                hint = hit.NearestVertex;

                Vec3d side = Vec3d.Cross(hit.SmoothNormal, sample.Tangent);
                if (side.LengthSquared < 1e-20) continue;
                side = side.Normalized();

                for (int s = -1; s <= 1; s += 2)
                {
                    // Walk one spacing sideways along the surface in a few
                    // substeps, transporting the side direction: a single
                    // tangent offset lands short of the spacing on curved
                    // surfaces (on a tube of radius r the chord is 2r sin(d/2r))
                    // and the candidate is then rejected as too close
                    if (!WalkOnSurface(proj, sample.Point, hint, s * spacing * side, 4, out var chit))
                        continue;

                    // Landed too close to an existing curve
                    if (registry.HasPointWithin(chit.Point, 0.9 * spacing)) continue;
                    if (candidateBlocked != null && candidateBlocked(chit)) continue;

                    Vec3d dir = sample.Tangent - Vec3d.Dot(sample.Tangent, chit.SmoothNormal) * chit.SmoothNormal;
                    if (dir.LengthSquared < 1e-20) continue;
                    dir = dir.Normalized();

                    // Candidates inherit the source curve's tangent so a new
                    // curve always starts in the same family as its neighbor
                    Vec3d[] line = traceField != null
                        ? traceField(chit.Point, chit.NearestVertex, dir)
                        : traceDirected!(chit.Point, chit.NearestVertex, dir);

                    if (line.Length > 2 && ArcLength(line) >= minLen)
                    {
                        results.Add(line);
                        registry.AddLine(line, thinGap);
                        queue.Enqueue(line);
                        if (results.Count >= opts.MaxCurves) break;
                    }
                }
            }
        }

        opts.ReachedMaxCurves = results.Count >= opts.MaxCurves && queue.Count > 0;
    }

    /// <summary>
    /// Moves a point along the surface by the given tangent offset in
    /// substeps, re-flattening the remaining direction into the local tangent
    /// plane after each projection. Returns false when the walk runs off the
    /// mesh (the projection clamps the step to a boundary).
    /// </summary>
    private static bool WalkOnSurface(MeshProjection proj, Vec3d start, int hint, Vec3d offset, int substeps,
        out MeshProjection.Hit hit)
    {
        double total = offset.Length;
        Vec3d dir = offset / Math.Max(total, 1e-300);
        double step = total / substeps;
        Vec3d pos = start;
        hit = proj.ClosestPoint(start, hint);
        for (int k = 0; k < substeps; k++)
        {
            Vec3d n = hit.SmoothNormal;
            Vec3d d = dir - Vec3d.Dot(dir, n) * n;
            if (d.LengthSquared < 1e-20) return false;
            d = d.Normalized();
            Vec3d intended = pos + step * d;
            hit = proj.ClosestPoint(intended, hit.NearestVertex);
            if ((hit.Point - intended).Length > 0.5 * step) return false;
            pos = hit.Point;
            dir = d;
        }
        return true;
    }

    private static IEnumerable<(Vec3d Point, Vec3d Tangent)> SampleAlong(Vec3d[] line, double spacing)
    {
        double acc = 0;
        for (int i = 1; i < line.Length; i++)
        {
            Vec3d seg = line[i] - line[i - 1];
            double len = seg.Length;
            if (len < 1e-15) continue;
            acc += len;
            if (acc >= spacing)
            {
                acc = 0;
                yield return (line[i], seg / len);
            }
        }
    }

    private static int DefaultSeed(MeshData mesh, bool[]? mask)
    {
        Vec3d centroid = Vec3d.Zero;
        for (int i = 0; i < mesh.VertexCount; i++)
            centroid = centroid + mesh.Vertices[i];
        centroid = centroid / mesh.VertexCount;

        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mask != null && !mask[i]) continue;
            double d = (mesh.Vertices[i] - centroid).LengthSquared;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Uniform-grid spatial hash over accepted curve points. Every polyline
    /// vertex is stored with its line and index so proximity tests can also
    /// return the closest point on the neighbouring polyline segments (used to
    /// snap T-junction ends exactly onto the curve they stopped at).
    /// </summary>
    private class PointRegistry
    {
        private readonly double _cell;
        private readonly Dictionary<(int, int, int), List<(Vec3d p, int line, int idx)>> _cells =
            new Dictionary<(int, int, int), List<(Vec3d, int, int)>>();
        private readonly List<Vec3d[]> _lines = new List<Vec3d[]>();

        public PointRegistry(double cellSize)
        {
            _cell = Math.Max(cellSize, 1e-9);
        }

        /// <summary>Stores a line; points are thinned to the gap for the proximity test, segments stay exact.</summary>
        public void AddLine(Vec3d[] line, double gap)
        {
            if (line.Length == 0) return;
            int li = _lines.Count;
            _lines.Add(line);
            Add(line[0], li, 0);
            double acc = 0;
            for (int i = 1; i < line.Length; i++)
            {
                acc += (line[i] - line[i - 1]).Length;
                if (acc >= gap || i == line.Length - 1)
                {
                    acc = 0;
                    Add(line[i], li, i);
                }
            }
        }

        private void Add(Vec3d p, int line, int idx)
        {
            var key = Key(p);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<(Vec3d, int, int)>();
                _cells[key] = list;
            }
            list.Add((p, line, idx));
        }

        public bool HasPointWithin(Vec3d p, double d)
        {
            int range = (int)Math.Ceiling(d / _cell);
            double d2 = d * d;
            var (kx, ky, kz) = Key(p);

            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                    for (int dz = -range; dz <= range; dz++)
                    {
                        if (!_cells.TryGetValue((kx + dx, ky + dy, kz + dz), out var list)) continue;
                        foreach (var e in list)
                            if ((e.p - p).LengthSquared < d2) return true;
                    }
            return false;
        }

        /// <summary>
        /// Closest point on any stored polyline within the search radius: the
        /// nearest stored vertices are found first, then the exact closest
        /// point on the polyline segments around each of them.
        /// </summary>
        public bool TryClosestOnCurves(Vec3d p, double radius, out Vec3d closest)
        {
            closest = p;
            int range = (int)Math.Ceiling(radius / _cell);
            double best = radius * radius;
            bool found = false;
            var (kx, ky, kz) = Key(p);

            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                    for (int dz = -range; dz <= range; dz++)
                    {
                        if (!_cells.TryGetValue((kx + dx, ky + dy, kz + dz), out var list)) continue;
                        foreach (var e in list)
                        {
                            // vertices are thinned: search the segments between this
                            // stored vertex and the neighbouring stored ones
                            var line = _lines[e.line];
                            int lo = Math.Max(0, e.idx - 16), hi = Math.Min(line.Length - 1, e.idx + 16);
                            for (int i = lo; i < hi; i++)
                            {
                                Vec3d q = ClosestOnSegment(p, line[i], line[i + 1]);
                                double d2 = (q - p).LengthSquared;
                                if (d2 < best) { best = d2; closest = q; found = true; }
                            }
                        }
                    }
            return found;
        }

        private static Vec3d ClosestOnSegment(Vec3d p, Vec3d a, Vec3d b)
        {
            Vec3d ab = b - a;
            double len2 = ab.LengthSquared;
            if (len2 < 1e-30) return a;
            double t = Math.Max(0.0, Math.Min(1.0, Vec3d.Dot(p - a, ab) / len2));
            return a + t * ab;
        }

        private (int, int, int) Key(Vec3d p) =>
            ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));
    }
}
