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

        /// <summary>Traces stop when closer than TestFactor * Spacing to an accepted curve.</summary>
        public double TestFactor { get; set; } = 0.4;

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
        double dtest = options.TestFactor * spacing;
        Func<Vec3d, bool> stop = p => registry.HasPointWithin(p, dtest);

        Vec3d[] TraceFrom(Vec3d pos, int hint, Vec3d? initialDir)
        {
            var line = FieldTracer.TraceBoth(proj, pos, hint, dirs, secondaryDirs, mask,
                step, maxSteps, stop, options.MinFieldMagnitude, initialDir);
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
        double dtest = options.TestFactor * spacing;
        Func<Vec3d, bool> stop = p => registry.HasPointWithin(p, dtest);

        Vec3d[] TraceFrom(Vec3d pos, int hint, Vec3d dir)
        {
            var line = GeodesicCurves.TraceBothFrom(proj, pos, hint, dir,
                step, maxSteps, stop);
            return line.Length > 1 ? CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses) : line;
        }

        Grow(proj, results, registry, options, spacing,
            null, null, TraceFrom,
            proj.Mesh.Vertices[firstSeed], firstSeed, firstDir.Normalized());

        return results;
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
        double thinGap = 0.5 * opts.TestFactor * spacing;
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
                    Vec3d cand = sample.Point + s * spacing * side;
                    var chit = proj.ClosestPoint(cand, hint);

                    // Fell off the mesh, or landed too close to an existing curve
                    if ((chit.Point - cand).Length > 0.5 * spacing) continue;
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

    /// <summary>Uniform-grid spatial hash over accepted curve points.</summary>
    private class PointRegistry
    {
        private readonly double _cell;
        private readonly Dictionary<(int, int, int), List<Vec3d>> _cells =
            new Dictionary<(int, int, int), List<Vec3d>>();

        public PointRegistry(double cellSize)
        {
            _cell = Math.Max(cellSize, 1e-9);
        }

        /// <summary>Stores line points thinned to the given arc-length gap.</summary>
        public void AddLine(Vec3d[] line, double gap)
        {
            if (line.Length == 0) return;
            Add(line[0]);
            double acc = 0;
            for (int i = 1; i < line.Length; i++)
            {
                acc += (line[i] - line[i - 1]).Length;
                if (acc >= gap || i == line.Length - 1)
                {
                    acc = 0;
                    Add(line[i]);
                }
            }
        }

        public void Add(Vec3d p)
        {
            var key = Key(p);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<Vec3d>();
                _cells[key] = list;
            }
            list.Add(p);
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
                        foreach (var q in list)
                            if ((q - p).LengthSquared < d2) return true;
                    }
            return false;
        }

        private (int, int, int) Key(Vec3d p) =>
            ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));
    }
}
