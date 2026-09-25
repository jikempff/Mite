using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Geodesic families only. Neighbouring geodesics do not stay
        /// parallel: their separation J obeys the Jacobi equation
        /// J'' + K·J = 0, so on K &gt; 0 they converge and on K &lt; 0 they
        /// diverge. With JacobiSeeding a new geodesic does not start parallel
        /// to its neighbour but at the angle that keeps the strip between
        /// them closest to the target width along its whole length (least
        /// squares over the Jacobi field; Pottmann, Huang, Deng, Schiftner,
        /// Kilian, Guibas, Wallner 2010, "Geodesic patterns", §4). Default on.
        /// </summary>
        public bool JacobiSeeding { get; set; } = true;

        /// <summary>Largest start angle (degrees) Jacobi seeding may apply.</summary>
        public double MaxSeedAngle { get; set; } = 30.0;

        /// <summary>
        /// Geodesic families only: start the family from the mesh border
        /// instead of one interior seed — geodesics leave the longest
        /// boundary loop every Spacing at BorderAngle from the inward
        /// normal; sideways growth then fills and merges as usual. This is
        /// how geodesic timber gridshells are laid out in practice (Pirazzi
        /// &amp; Weinand 2006). Ignored on meshes without a border.
        /// </summary>
        public bool FromBorder { get; set; } = false;

        /// <summary>Angle (degrees) between the border geodesics and the inward normal of the border.</summary>
        public double BorderAngle { get; set; } = 0.0;

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
            : DefaultSeed(proj.Mesh, mask, dirs, secondaryDirs);
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

        // Uncovered vertex to seed a new region from: no accepted curve within
        // one spacing, well inside the region (all neighbours masked in),
        // best conditioned (families crossing widely) first
        var nbrs = proj.Mesh.BuildVertexNeighbors();
        int Refill()
        {
            int best = -1;
            double bestQ = -1;
            var V = proj.Mesh.Vertices;
            for (int i = 0; i < V.Length; i++)
            {
                if (mask != null && !mask[i]) continue;
                if (dirs[i].LengthSquared < 1e-20) continue;
                bool interior = true;
                if (mask != null) foreach (int n in nbrs[i]) if (!mask[n]) { interior = false; break; }
                if (!interior) continue;
                if (registry.HasPointWithin(V[i], spacing)) continue;
                double q = 1;
                if (secondaryDirs != null && secondaryDirs[i].LengthSquared > 1e-20)
                    q = Vec3d.Cross(dirs[i], secondaryDirs[i]).Length / Math.Sqrt(dirs[i].LengthSquared * secondaryDirs[i].LengthSquared);
                if (q > bestQ) { bestQ = q; best = i; }
            }
            return best;
        }

        Grow(proj, results, registry, options, spacing,
            TraceFrom, CandidateBlocked, null,
            proj.Mesh.Vertices[seed], seed, default, Refill);

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

        // Start angle per candidate from the Jacobi field along the parent
        double[]? K = options.JacobiSeeding ? Curvature.GaussianCurvature.Compute(proj.Mesh) : null;
        double maxAngle = Math.Max(0.0, options.MaxSeedAngle) * Math.PI / 180.0;
        Func<Vec3d[], int, Vec3d, Vec3d, Vec3d, double>? seedAngle = K == null ? null :
            (parent, index, point, tangent, side) => JacobiStartAngle(proj, K, parent, index, spacing, maxAngle);

        var border = options.FromBorder ? BorderSeeds(proj, spacing, options.BorderAngle * Math.PI / 180.0, proj.Mesh.Vertices[firstSeed]) : null;
        if (border != null && border.Count > 0)
        {
            double thinGap = Math.Max(0.5 * dtest, 0.02 * spacing);
            var queueSeeds = new List<Vec3d[]>();
            foreach (var (pos, hint, dir) in border)
            {
                if (results.Count >= options.MaxCurves) break;
                var half = GeodesicCurves.TraceOneFrom(proj, pos, hint, dir, step, maxSteps, stop, out _, out bool endNear);
                var line = SnapEnds(proj, registry, half.ToArray(), false, endNear, spacing);
                if (line.Length < 3 || ArcLength(line) < 2.0 * spacing) continue;
                line = CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses);
                results.Add(line);
                registry.AddLine(line, thinGap);
                queueSeeds.Add(line);
            }
            if (results.Count > 0)
            {
                Grow(proj, results, registry, options, spacing, null, null, TraceFrom,
                    default, -1, default, null, seedAngle, queueSeeds);
                return results;
            }
        }

        Grow(proj, results, registry, options, spacing,
            null, null, TraceFrom,
            proj.Mesh.Vertices[firstSeed], firstSeed, firstDir.Normalized(), null, seedAngle);

        return results;
    }

    /// <summary>
    /// Start angle for a geodesic seeded one spacing beside <paramref name="parent"/>
    /// at polyline index <paramref name="index"/>: the least-squares optimum of
    /// the Jacobi field J'' + K J = 0, J(0) = w, J'(0) = φ, measured against
    /// the target width w along the parent in both directions (the backward
    /// half sees J'(0) = −φ). Positive angles turn the new geodesic away from
    /// the parent along the parent's forward direction.
    /// </summary>
    internal static double JacobiStartAngle(MeshProjection proj, double[] K, Vec3d[] parent, int index, double w, double maxAngle)
    {
        double num = 0, den = 0;
        int hint = proj.NearestVertexGlobal(parent[Math.Max(0, Math.Min(parent.Length - 1, index))]);
        void Integrate(int dirSign)
        {
            // fundamental solutions A (1,0) and B (0,1) integrated with the
            // velocity Verlet scheme along the parent polyline
            double a = 1, ad = 0, b = 0, bd = 1;
            int i = index;
            int h = hint;
            while (true)
            {
                int j = i + dirSign;
                if (j < 0 || j >= parent.Length) break;
                double ds = (parent[j] - parent[i]).Length;
                if (ds < 1e-15) { i = j; continue; }
                var hit = proj.ClosestPoint(parent[i], h);
                h = hit.NearestVertex;
                double k = K[h];
                // step
                double aAcc = -k * a, bAcc = -k * b;
                double aNew = a + ad * ds + 0.5 * aAcc * ds * ds;
                double bNew = b + bd * ds + 0.5 * bAcc * ds * ds;
                double aAccNew = -k * aNew, bAccNew = -k * bNew;
                ad += 0.5 * (aAcc + aAccNew) * ds;
                bd += 0.5 * (bAcc + bAccNew) * ds;
                a = aNew; b = bNew;
                // residual of J = w a + s φ b against w, with s the direction sign of φ
                double sgn = dirSign;
                num += ds * (w - w * a) * (sgn * b);
                den += ds * b * b;
                i = j;
            }
        }
        Integrate(+1);
        Integrate(-1);
        if (den < 1e-18) return 0.0;
        double phi = num / den; // J'(0) ≈ tan φ for small angles
        phi = Math.Atan(phi);
        return Math.Max(-maxAngle, Math.Min(maxAngle, phi));
    }

    /// <summary>
    /// Seeds along one border edge, every spacing, heading into the mesh at
    /// the given angle from the inward normal. The boundary loop is split at
    /// its corners (turning angle above 40°) and the edge nearest
    /// <paramref name="near"/> is used — seeding all around a loop sends
    /// geodesics from opposite sides into each other. A loop without corners
    /// (a dome rim) contributes the third of its length centred nearest the
    /// seed. Empty on closed meshes.
    /// </summary>
    internal static List<(Vec3d pos, int hint, Vec3d dir)> BorderSeeds(MeshProjection proj, double spacing, double angle, Vec3d near)
    {
        var mesh = proj.Mesh;
        var loops = BoundaryLoops(mesh);
        var seeds = new List<(Vec3d, int, Vec3d)>();
        if (loops.Count == 0) return seeds;

        // pick the loop containing the boundary vertex nearest 'near'
        List<int> loop = loops[0];
        double bestD = double.MaxValue;
        foreach (var l in loops)
            foreach (int v in l)
            {
                double d = (mesh.Vertices[v] - near).LengthSquared;
                if (d < bestD) { bestD = d; loop = l; }
            }
        int n = loop.Count;

        // split at corners
        var corners = new List<int>();
        for (int i = 0; i < n; i++)
        {
            Vec3d a = mesh.Vertices[loop[(i - 1 + n) % n]], b = mesh.Vertices[loop[i]], c = mesh.Vertices[loop[(i + 1) % n]];
            Vec3d e0 = (b - a), e1 = (c - b);
            if (e0.LengthSquared < 1e-30 || e1.LengthSquared < 1e-30) continue;
            double ang = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(e0, e1) / (e0.Length * e1.Length))));
            if (ang > 40.0 * Math.PI / 180.0) corners.Add(i);
        }

        // the edge (vertex index range along the loop) to seed on
        var edge = new List<int>();
        if (corners.Count >= 2)
        {
            int bestEdge = 0; bestD = double.MaxValue;
            for (int k = 0; k < corners.Count; k++)
            {
                int i0 = corners[k], i1 = corners[(k + 1) % corners.Count];
                var idx = new List<int>();
                for (int i = i0; ; i = (i + 1) % n) { idx.Add(loop[i]); if (i == i1) break; }
                double d = idx.Min(v => (mesh.Vertices[v] - near).LengthSquared);
                if (d < bestD) { bestD = d; bestEdge = k; edge = idx; }
            }
        }
        else
        {
            int i0 = 0; bestD = double.MaxValue;
            for (int i = 0; i < n; i++) { double d = (mesh.Vertices[loop[i]] - near).LengthSquared; if (d < bestD) { bestD = d; i0 = i; } }
            double total = LoopLength(mesh, loop), want = total / 3.0, acc = 0;
            var idx = new List<int>();
            for (int i = i0; acc < want / 2 && idx.Count < n; i = (i - 1 + n) % n) { idx.Insert(0, loop[i]); acc += (mesh.Vertices[loop[i]] - mesh.Vertices[loop[(i + 1) % n]]).Length; }
            acc = 0;
            for (int i = (i0 + 1) % n; acc < want / 2 && idx.Count < n; i = (i + 1) % n) { idx.Add(loop[i]); acc += (mesh.Vertices[loop[i]] - mesh.Vertices[loop[(i - 1 + n) % n]]).Length; }
            edge = idx;
        }
        if (edge.Count < 2) return seeds;

        double len = 0;
        for (int i = 0; i + 1 < edge.Count; i++) len += (mesh.Vertices[edge[i + 1]] - mesh.Vertices[edge[i]]).Length;
        int count = Math.Max(1, (int)Math.Floor(len / spacing));
        double stepArc = len / count;
        double target = 0.5 * stepArc, accum = 0;
        for (int i = 0; i + 1 < edge.Count; i++)
        {
            Vec3d a = mesh.Vertices[edge[i]], b = mesh.Vertices[edge[i + 1]];
            double l = (b - a).Length;
            if (l < 1e-15) continue;
            while (accum + l >= target && seeds.Count < count)
            {
                double t = (target - accum) / l;
                Vec3d p = a + t * (b - a);
                Vec3d tb = (b - a) / l;
                var hit = proj.ClosestPoint(p, edge[i]);
                Vec3d nrm = hit.SmoothNormal;
                Vec3d inward = Vec3d.Cross(nrm, tb);
                if (inward.LengthSquared < 1e-20) { target += stepArc; continue; }
                inward = inward.Normalized();
                double probe = 0.5 * proj.AverageEdgeLength;
                var h1 = proj.ClosestPoint(p + probe * inward, hit.NearestVertex);
                var h2 = proj.ClosestPoint(p - probe * inward, hit.NearestVertex);
                if ((h2.Point - (p - probe * inward)).Length < (h1.Point - (p + probe * inward)).Length) inward = -inward;
                Vec3d dir = Math.Cos(angle) * inward + Math.Sin(angle) * Vec3d.Cross(nrm, inward).Normalized();
                seeds.Add((hit.Point, hit.NearestVertex, dir.Normalized()));
                target += stepArc;
            }
            accum += l;
        }
        return seeds;
    }

    private static double LoopLength(MeshData mesh, List<int> loop)
    {
        double s = 0;
        for (int i = 0; i < loop.Count; i++) s += (mesh.Vertices[loop[(i + 1) % loop.Count]] - mesh.Vertices[loop[i]]).Length;
        return s;
    }

    /// <summary>Boundary edge loops as ordered vertex lists.</summary>
    public static List<List<int>> BoundaryLoops(MeshData mesh)
    {
        var use = new Dictionary<(int, int), int>();
        var directed = new Dictionary<(int, int), bool>();
        foreach (var f in mesh.Faces)
        {
            int m = f.Length;
            for (int i = 0; i < m; i++)
            {
                int a = f[i], b = f[(i + 1) % m];
                var key = a < b ? (a, b) : (b, a);
                use[key] = use.TryGetValue(key, out int c) ? c + 1 : 1;
                directed[(a, b)] = true;
            }
        }
        var next = new Dictionary<int, int>();
        foreach (var kv in use)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key;
            // boundary edge in face orientation a->b; the loop follows face orientation
            if (directed.ContainsKey((a, b))) next[a] = b; else next[b] = a;
        }
        var loops = new List<List<int>>();
        var visited = new HashSet<int>();
        foreach (int start in next.Keys)
        {
            if (visited.Contains(start)) continue;
            var loop = new List<int>();
            int v = start;
            while (!visited.Contains(v) && next.ContainsKey(v))
            {
                visited.Add(v);
                loop.Add(v);
                v = next[v];
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
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
        Vec3d firstPos, int firstHint, Vec3d firstDir = default, Func<int>? refill = null,
        Func<Vec3d[], int, Vec3d, Vec3d, Vec3d, double>? seedAngle = null, List<Vec3d[]>? initialQueue = null)
    {
        double thinGap = Math.Max(0.5 * StopDistance(opts, spacing), 0.02 * spacing);
        double minLen = opts.MinCurveLength > 0 ? opts.MinCurveLength : 2.0 * spacing;
        var queue = new Queue<Vec3d[]>();
        opts.ReachedMaxCurves = false;
        opts.Cancelled = false;

        if (initialQueue != null)
        {
            foreach (var l in initialQueue) queue.Enqueue(l);
        }
        else
        {
            // The first curve is kept regardless of length: if it is short, the
            // traceable region is simply small, and returning it beats returning nothing
            var first = traceField != null
                ? traceField(firstPos, firstHint, null)
                : traceDirected!(firstPos, firstHint, firstDir);
            if (first.Length < 2) return;

            results.Add(first);
            registry.AddLine(first, thinGap);
            queue.Enqueue(first);
        }

        while (true)
        {
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
                    if (seedAngle != null)
                    {
                        // turn the start direction away from (φ > 0) or towards the parent
                        double phi = seedAngle(source, sample.Index, chit.Point, dir, side);
                        if (Math.Abs(phi) > 1e-9)
                        {
                            Vec3d away = s * side - Vec3d.Dot(s * side, chit.SmoothNormal) * chit.SmoothNormal;
                            if (away.LengthSquared > 1e-20)
                                dir = (Math.Cos(phi) * dir + Math.Sin(phi) * away.Normalized()).Normalized();
                        }
                    }

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

        // Sideways growth only reaches the region connected to the first
        // seed. Regions the field also covers but that no curve has entered
        // (the other lobes of a wave, the far side of a surface split by a
        // K = 0 band) get their own seed at an uncovered vertex, until none
        // is left or the cap is hit.
        if (refill == null || opts.Cancelled || results.Count >= opts.MaxCurves) break;
        int v = refill();
        if (v < 0) break;
        var pos = proj.Mesh.Vertices[v];
        var extra = traceField != null ? traceField(pos, v, null) : traceDirected!(pos, v, firstDir);
        if (extra.Length > 2 && ArcLength(extra) >= minLen)
        {
            results.Add(extra);
            registry.AddLine(extra, thinGap);
            queue.Enqueue(extra);
        }
        else
        {
            registry.AddLine(new[] { pos }, thinGap); // mark as covered so it is not tried again
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

    private static IEnumerable<(Vec3d Point, Vec3d Tangent, int Index)> SampleAlong(Vec3d[] line, double spacing)
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
                yield return (line[i], seg / len, i);
            }
        }
    }

    /// <summary>
    /// Default first seed: the masked vertex nearest the centroid — but for
    /// two-family fields only among the well-conditioned vertices, where the
    /// families cross at close to their widest angle. Seeding where the
    /// families nearly coincide (K ≈ 0) starts both families on the same
    /// curve and the whole net inherits the mistake.
    /// </summary>
    private static int DefaultSeed(MeshData mesh, bool[]? mask, Vec3d[]? dirs = null, Vec3d[]? secondary = null)
    {
        Vec3d centroid = Vec3d.Zero;
        for (int i = 0; i < mesh.VertexCount; i++)
            centroid = centroid + mesh.Vertices[i];
        centroid = centroid / mesh.VertexCount;

        double[]? quality = null;
        double qMax = 0;
        if (dirs != null && secondary != null && secondary.Length == dirs.Length)
        {
            quality = new double[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (mask != null && !mask[i]) continue;
                if (dirs[i].LengthSquared < 1e-20 || secondary[i].LengthSquared < 1e-20) continue;
                // sine of the crossing angle: 1 = orthogonal families
                quality[i] = Vec3d.Cross(dirs[i], secondary[i]).Length / Math.Sqrt(dirs[i].LengthSquared * secondary[i].LengthSquared);
                qMax = Math.Max(qMax, quality[i]);
            }
        }

        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mask != null && !mask[i]) continue;
            if (quality != null && quality[i] < 0.85 * qMax) continue;
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
