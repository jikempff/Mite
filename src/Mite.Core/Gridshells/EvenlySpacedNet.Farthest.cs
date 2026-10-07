using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

// Farthest-point seeding of an evenly spaced family (NetLayout.FillFarthest).
//
// Literature
// - Mebarki, Alliez & Devillers 2005, "Farthest point seeding for efficient
//   placement of streamlines" (IEEE Visualization 2005; INRIA RR-5524): place one
//   streamline at a time, seeded at the centre of the biggest void left by all
//   streamlines placed so far (largest empty circle of a Delaunay triangulation
//   of the streamline samples and of a frame one separating distance outside
//   the domain); integrate both ways until the new point comes closer than the
//   separating distance to the placement or leaves the domain; seed only voids
//   above "saturation x spacing" (1.6 in all their experiments); stop when no
//   such void is left. "Seeding a streamline at the farthest point favors long
//   streamlines" and spreads the residual space instead of leaving it beside the
//   last curve of a sequence.
// - CGAL Stream_lines_2 (Mebarki's implementation): seeds where the circumradius
//   is >= dSep * dRat, frame points dSep outside the domain bounding box.
// - Jobard & Lefer 1997, "Creating evenly-spaced streamlines of arbitrary
//   density" (the neighbour-offset seeding of NetLayout.Fill): seeds dsep beside
//   an existing streamline, traces stop at dtest = 0.5 dsep.
//
// Reading of the parameters. The report's prose seeds every void whose
// circumcircle *diameter* exceeds saturation x "the desired spacing"; CGAL seeds
// where the circumradius exceeds dSep x dRat, stops traces at dSep (smallest
// incident circumdiameter) and puts the frame dSep outside the domain. Both agree
// when Mite's Spacing S is the desired spacing of the prose and CGAL's dSep is
// S / 2: seeds at least ½ Saturation · S from every curve (0.8 S at 1.6), the
// frame ½ S outside the border (so no seed ever lands on the border itself), and
// a stop distance of ½ S — Mite keeps its own TestFactor (0.4 S) or, for
// continuous curves, MergeFactor (0.15 S), so the neighbour fill and this one
// differ only in where curves start.
//
// On a mesh the Delaunay voids are replaced by a distance field over the
// vertices: each curve point seeds the corners of its face with their Euclidean
// distance, Dijkstra carries the nearest curve point along the edges and
// re-measures the distance to it (so the field is the distance to the nearest
// curve point, not a graph path length), and the border — the mesh border and
// the edge of the usable region — counts as a curve ½ Spacing outside. The next
// seed is the usable vertex where min(curve distance, border distance + ½ S) is
// largest, moved to the maximum of that key along the ray from its nearest curve
// point (exact polyline distances), which is where the empty circle's centre
// lies; seeding stops when the key drops below ½ · Saturation · S, i.e. when no
// void wider than Saturation · S is left.
public static partial class EvenlySpacedNet
{
    private static List<Vec3d[]> TraceFieldFarthest(
        MeshData mesh, Vec3d[] dirs, bool[]? mask, int firstSeed, Options options, Vec3d[]? secondaryDirs)
    {
        options.ReachedMaxCurves = false;
        options.Cancelled = false;
        options.RejectedSeeds = 0;
        options.LargestGap = 0;
        var proj = new MeshProjection(mesh);
        var results = new List<Vec3d[]>();

        int seed = firstSeed >= 0 && firstSeed < proj.Mesh.VertexCount
            ? firstSeed
            : DefaultSeed(proj.Mesh, mask, dirs, secondaryDirs);
        if (seed < 0 || (mask != null && !mask[seed])) return results;

        Resolve(options, proj, out double spacing, out double step, out int maxSteps);
        var registry = new PointRegistry(spacing);
        double dtest = StopDistance(options, spacing);
        double thinGap = Math.Max(0.5 * dtest, 0.02 * spacing);
        double minLen = options.MinCurveLength > 0 ? options.MinCurveLength : 2.0 * spacing;
        Func<Vec3d, bool> stop = p => registry.HasPointWithin(p, dtest);

        Vec3d[] TraceFrom(Vec3d pos, int hint)
        {
            var line = FieldTracer.TraceBoth(proj, pos, hint, dirs, secondaryDirs, mask,
                step, maxSteps, stop, out bool startNear, out bool endNear, options.MinFieldMagnitude, null);
            line = SnapEnds(proj, registry, line, startNear, endNear, spacing);
            return line.Length > 1 ? CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses) : line;
        }

        // the first curve is kept whatever its length (as in the neighbour fill)
        var first = TraceFrom(proj.Mesh.Vertices[seed], seed);
        if (first.Length < 2) return results;
        results.Add(first);
        registry.AddLine(first, thinGap);

        var nbrs = mesh.BuildVertexNeighbors();
        var border = RegionBorder(mesh, nbrs, mask);
        FarthestPointSeeding(proj, nbrs, border, results, registry, options, spacing,
            v =>
            {
                if ((mask != null && !mask[v]) || dirs[v].LengthSquared < 1e-20) return -1;
                if (mask != null) foreach (int w in nbrs[v]) if (!mask[w]) return -1;
                return secondaryDirs != null && secondaryDirs[v].LengthSquared > 1e-20
                    ? Vec3d.Cross(dirs[v], secondaryDirs[v]).Length / Math.Sqrt(dirs[v].LengthSquared * secondaryDirs[v].LengthSquared) : 1.0;
            },
            (hit, _, _) =>
            {
                var line = TraceFrom(hit.Point, hit.NearestVertex);
                if (line.Length < 3 || ArcLength(line) < minLen) return null;
                registry.AddLine(line, thinGap);
                return line;
            });
        return results;
    }

    /// <summary>
    /// Vertices on the border of the usable region: mesh border vertices, and
    /// usable vertices with a neighbour outside the mask (the K = 0 band of an
    /// asymptotic field).
    /// </summary>
    private static bool[] RegionBorder(MeshData mesh, int[][] nbrs, bool[]? mask)
    {
        var b = mesh.BuildBoundaryVertexFlags();
        if (mask == null) return b;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (!mask[i]) { b[i] = false; continue; }
            if (b[i]) continue;
            foreach (int w in nbrs[i]) if (!mask[w]) { b[i] = true; break; }
        }
        return b;
    }

    /// <summary>
    /// The farthest-point loop (written for any curve family through
    /// <paramref name="seedAt"/>, used by direction fields). Keeps
    /// the distance from every vertex to the nearest curve point (incremental:
    /// distances only drop as curves are added) and the tangent / normal of
    /// that curve there; while the largest gap key = min(curve distance,
    /// border distance + ½ Spacing) over the candidate vertices is at least
    /// ½ Saturation · Spacing, calls <paramref name="seedAt"/> at the farthest
    /// point (the vertex, refined to the centre of the empty circle). A seed
    /// that grows no curve retires the vertices within half a Spacing of it,
    /// so the loop always ends. Infinite keys (no curve and no border
    /// reachable) go first, best quality first.
    /// </summary>
    private static void FarthestPointSeeding(MeshProjection proj, int[][] nbrs, bool[] border, List<Vec3d[]> results,
        PointRegistry registry, Options opts, double spacing, Func<int, double> candidate, Func<MeshProjection.Hit, Vec3d, Vec3d, Vec3d[]?> seedAt)
    {
        var mesh = proj.Mesh;
        var V = mesh.Vertices;
        int nv = mesh.VertexCount;

        // border distance (Euclidean to the nearest border vertex, carried by Dijkstra)
        var dBorder = new double[nv];
        var bSrc = new int[nv];
        for (int i = 0; i < nv; i++) { dBorder[i] = double.PositiveInfinity; bSrc[i] = -1; }
        var heap = new MinHeap();
        for (int i = 0; i < nv; i++) if (border[i]) { dBorder[i] = 0; bSrc[i] = i; heap.Push(0, i); }
        while (heap.Count > 0)
        {
            var (du, u) = heap.Pop();
            if (du > dBorder[u]) continue;
            foreach (int w in nbrs[u])
            {
                double nd = (V[w] - V[bSrc[u]]).Length;
                if (nd < dBorder[w]) { dBorder[w] = nd; bSrc[w] = bSrc[u]; heap.Push(nd, w); }
            }
        }

        var dist = new double[nv];
        var src = new Vec3d[nv];
        var tan = new Vec3d[nv];
        var nrm = new Vec3d[nv];
        for (int i = 0; i < nv; i++) dist[i] = double.PositiveInfinity;
        var quality = new double[nv];
        var alive = new bool[nv];
        for (int i = 0; i < nv; i++) { quality[i] = candidate(i); alive[i] = quality[i] >= 0; }

        double sat = Math.Max(1.0, opts.Saturation);
        double threshold = 0.5 * sat * spacing;
        double pad = 0.5 * spacing;
        double Key(int i) => Math.Min(dist[i], dBorder[i] + pad);

        // The same key at any surface point, with the exact distance to the
        // curve polylines (the vertex field is only as fine as the mesh)
        // exact border: the mesh boundary polylines and the region-border vertices
        var borderReg = new PointRegistry(spacing);
        var onMeshBorder = mesh.BuildBoundaryVertexFlags();
        foreach (var loop in BoundaryLoops(mesh))
        {
            var pts = new Vec3d[loop.Count + 1];
            for (int i = 0; i < loop.Count; i++) pts[i] = V[loop[i]];
            pts[loop.Count] = pts[0];
            borderReg.AddLine(pts, 0.0);
        }
        for (int i = 0; i < nv; i++) if (border[i] && !onMeshBorder[i]) borderReg.AddLine(new[] { V[i] }, 0.0);

        double KeyAt(in MeshProjection.Hit h, double radius)
        {
            double db = borderReg.TryClosestOnCurves(h.Point, radius, out Vec3d bp) ? (bp - h.Point).Length : radius;
            double dc = registry.TryClosestOnCurves(h.Point, radius, out Vec3d cp) ? (cp - h.Point).Length : radius;
            return Math.Min(dc, db + pad);
        }

        // Mebarki et al. seed at the centre of the largest empty circle; a mesh
        // vertex is only within half an edge of it. The key (with exact curve
        // and border distances) is maximised from the vertex: first along the
        // ray from its nearest curve point (across the void), then by a compass
        // search in the tangent plane.
        MeshProjection.Hit Refine(int v, double key)
        {
            var at = proj.ClosestPoint(V[v], v);
            if (double.IsPositiveInfinity(key) || double.IsPositiveInfinity(dist[v])) return at;
            Vec3d q = src[v];
            Vec3d u = V[v] - q;
            u -= Vec3d.Dot(u, at.SmoothNormal) * at.SmoothNormal;
            if (u.LengthSquared < 1e-24) return at;
            u = u.Normalized();
            double radius = 3.0 * key + spacing;
            double best = KeyAt(at, radius);
            MeshProjection.Hit bestHit = at;
            MeshProjection.Hit Probe(double lam) => proj.ClosestPoint(q + lam * u, v);
            const int scan = 24;
            double hi = 2.0 * key;
            for (int k = 1; k <= scan; k++)
            {
                double lam = hi * k / scan;
                var h = Probe(lam);
                if (h.Face < 0 || candidate(h.NearestVertex) < 0) continue;
                double f = KeyAt(h, radius);
                if (f > best) { best = f; bestHit = h; }
            }
            // then a compass (pattern) search in the tangent plane: the key is
            // the lower envelope of distance functions, and its maximum — the
            // centre of the largest empty circle — need not lie on the ray
            double hstep = 0.25 * Math.Min(key, 2.0 * spacing);
            double hmin = 0.005 * spacing;
            for (int it = 0; it < 200 && hstep > hmin; it++)
            {
                Vec3d n = bestHit.SmoothNormal;
                Vec3d e1 = u - Vec3d.Dot(u, n) * n;
                if (e1.LengthSquared < 1e-24) break;
                e1 = e1.Normalized();
                Vec3d e2 = Vec3d.Cross(n, e1);
                bool moved = false;
                foreach (var e in new[] { e1, -e1, e2, -e2 })
                {
                    var h = proj.ClosestPoint(bestHit.Point + hstep * e, bestHit.NearestVertex);
                    if (h.Face < 0 || candidate(h.NearestVertex) < 0) continue;
                    double f = KeyAt(h, radius);
                    if (f > best + 1e-12 * spacing) { best = f; bestHit = h; moved = true; break; }
                }
                if (!moved) hstep *= 0.5;
            }
            return bestHit;
        }

        int done = 0;
        int maxIter = Math.Max(1, opts.MaxCurves) + nv;
        for (int iter = 0; iter < maxIter; iter++)
        {
            // fold the new curves into the distance field
            for (; done < results.Count; done++)
            {
                var c = results[done];
                if (c.Length < 1) continue;
                int h = proj.NearestVertexGlobal(c[0]);
                for (int k = 0; k < c.Length; k++)
                {
                    var q = c[k];
                    Vec3d t = c[Math.Min(k + 1, c.Length - 1)] - c[Math.Max(k - 1, 0)];
                    var hit = proj.ClosestPoint(q, h); h = hit.NearestVertex;
                    if (hit.Face < 0) continue;
                    foreach (int w in mesh.Faces[hit.Face])
                    {
                        double d = (V[w] - q).Length;
                        if (d < dist[w]) { dist[w] = d; src[w] = q; tan[w] = t; nrm[w] = hit.SmoothNormal; heap.Push(d, w); }
                    }
                }
            }
            while (heap.Count > 0)
            {
                var (du, u) = heap.Pop();
                if (du > dist[u]) continue;
                foreach (int w in nbrs[u])
                {
                    double nd = (V[w] - src[u]).Length;
                    if (nd < dist[w]) { dist[w] = nd; src[w] = src[u]; tan[w] = tan[u]; nrm[w] = nrm[u]; heap.Push(nd, w); }
                }
            }

            if (opts.ShouldCancel?.Invoke() == true) { opts.Cancelled = true; break; }

            int best = -1; double bestKey = threshold, bestQ = -1;
            for (int i = 0; i < nv; i++)
            {
                if (!alive[i]) continue;
                double key = Key(i);
                if (key > bestKey || (double.IsPositiveInfinity(key) && double.IsPositiveInfinity(bestKey) && quality[i] > bestQ))
                { bestKey = key; bestQ = quality[i]; best = i; }
            }
            if (best < 0) break;
            if (results.Count >= opts.MaxCurves) { opts.ReachedMaxCurves = true; break; }

            var at = Refine(best, bestKey);
            var line = seedAt(at, tan[best], nrm[best]);
            if (line != null)
            {
                results.Add(line);
                continue;
            }
            // nothing grows here: retire the neighbourhood of the vertex and of
            // the refined seed, so neither is picked again
            opts.RejectedSeeds++;
            var stack = new Stack<int>();
            Vec3d c0 = V[best], c1 = at.Point;
            bool Near(int w) => Math.Min((V[w] - c0).Length, (V[w] - c1).Length) < 0.5 * spacing;
            alive[best] = false; stack.Push(best);
            if (at.NearestVertex >= 0 && alive[at.NearestVertex]) { alive[at.NearestVertex] = false; stack.Push(at.NearestVertex); }
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (int w in nbrs[u])
                    if (alive[w] && Near(w)) { alive[w] = false; stack.Push(w); }
            }
        }

        double gap = 0;
        for (int i = 0; i < nv; i++)
            if (quality[i] >= 0) gap = Math.Max(gap, Key(i));
        opts.LargestGap = 2.0 * gap;
    }
}
