using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Curvature;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Asymptotic webs after Schling: a quad net of asymptotic curves fixed by
/// the node spacing along the curves through one seed point, with the
/// surface's symmetry carried into the net.
/// <para>
/// An asymptotic parameterisation of a negatively curved surface is unique
/// up to u = f(ū), v = g(v̄) (Schling, Wang, Hoyer &amp; Pottmann 2022, §2.3):
/// once the two asymptotic curves through a seed are chosen and nodes are
/// placed along them, every other node is the crossing of the curve of one
/// family through a node of the first seed curve with the curve of the other
/// family through a node of the second. "Alternately drawing each curve and
/// using their intersections as new starting points" (Schling, Hitrec &amp;
/// Barthel 2017) builds exactly this net; the node distance along the seed
/// curves is the one design variable. There is no evenly-spaced insertion,
/// so no curve stops on a neighbour, no short stubs appear between
/// neighbours, and every lath runs border to border.
/// </para>
/// <para>
/// Seeds at a flat point (the centre of a 3-fold Enneper surface, a monkey
/// saddle) are singular vertices of the net: 2N asymptotic rays leave the
/// point (N = 3 on a monkey saddle — six rays, the "N ≥ 6 edges" of a
/// singular A-net vertex), the families swap across them, and each sector
/// between two rays is a regular quad web spanned by its two bounding rays.
/// A regular seed is the case of four rays.
/// </para>
/// <para>
/// Symmetry: when the mesh is invariant under a rotation about the seed
/// normal, only one fundamental set of rays is traced and the rest is
/// rotated, so the net is exactly symmetric. On a surface of revolution
/// (catenoid, hyperboloid) the net is built from rotated copies of the two
/// asymptotic curves through the seed, N = round(2πρ / (√2·spacing)) of each,
/// and the node diagonals along the meridians are planar geodesics: the web
/// is a rotational asymptotic-asymptotic-geodesic (AAG) web, the exact case
/// used to initialise AAG designs by Schling et al. 2022 (§3.3).
/// </para>
/// </summary>
public static class AsymptoticWeb
{
    public class Options
    {
        /// <summary>Node distance along the seed curves (0 = 4% of the mesh diagonal).</summary>
        public double Spacing { get; set; } = 0.0;

        /// <summary>Integration step (0 = automatic).</summary>
        public double StepSize { get; set; } = 0.0;

        /// <summary>Steps per half-curve (0 = automatic).</summary>
        public int MaxSteps { get; set; } = 0;

        /// <summary>Families crossing at less than this angle (degrees) count as outside the usable region.</summary>
        public double MinCrossingAngle { get; set; } = 10.0;

        /// <summary>
        /// Symmetry: −1 = detect (rotation about the seed normal, or a surface
        /// of revolution); 0 = none; n ≥ 2 = force n-fold rotation about the
        /// seed normal.
        /// </summary>
        public int Symmetry { get; set; } = -1;

        /// <summary>
        /// Build the rotational (AAG) web when the mesh is a surface of
        /// revolution. Default true.
        /// </summary>
        public bool Rotational { get; set; } = true;

        /// <summary>Trim lath tails beyond the last node shorter than this fraction of the spacing (0 keeps every tail).</summary>
        public double TrimTails { get; set; } = 0.35;

        /// <summary>On-surface fairing passes per traced curve.</summary>
        public int SmoothingPasses { get; set; } = 6;

        public double MinFieldMagnitude { get; set; } = 0.3;

        public Func<bool>? ShouldCancel { get; set; }
    }

    public sealed class Result
    {
        /// <summary>Laths of the first label (upright strips).</summary>
        public List<Vec3d[]> A { get; } = new List<Vec3d[]>();
        /// <summary>Laths of the second label.</summary>
        public List<Vec3d[]> B { get; } = new List<Vec3d[]>();
        /// <summary>Node positions (A × B crossings).</summary>
        public List<Vec3d> Nodes { get; } = new List<Vec3d>();
        /// <summary>Quad faces of the net (node indices, cyclic).</summary>
        public List<int[]> Quads { get; } = new List<int[]>();
        /// <summary>
        /// Node diagonals of the quads, two families: <see cref="DiagonalFamily"/>
        /// says which. On a rotational web family 0 are the meridians (geodesics).
        /// </summary>
        public List<Vec3d[]> Diagonals { get; } = new List<Vec3d[]>();
        public List<int> DiagonalFamily { get; } = new List<int>();
        /// <summary>The diagonals as node index chains (parallel to <see cref="Diagonals"/>).</summary>
        public List<int[]> DiagonalNodes { get; } = new List<int[]>();
        /// <summary>Per node: its neighbours along the A laths and along the B laths.</summary>
        public int[][] NeighborsA { get; set; } = Array.Empty<int[]>();
        public int[][] NeighborsB { get; set; } = Array.Empty<int[]>();
        /// <summary>Per diagonal family: mean |kg| · spacing along its polylines (0 = geodesic).</summary>
        public double[] DiagonalGeodesicError { get; set; } = new double[2];
        public Vec3d Seed { get; set; }
        public int Rays { get; set; }
        public bool Singular { get; set; }
        /// <summary>Rotational order used (1 = none, int.MaxValue = surface of revolution).</summary>
        public int SymmetryOrder { get; set; } = 1;
        public bool RotationalWeb { get; set; }
        public double Spacing { get; set; }
        public double Step { get; set; }
        /// <summary>Largest distance between a node and the image of another node under the symmetry (0 when none).</summary>
        public double SymmetryError { get; set; }
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Laths that start at a singular seed (the rays); diagonals break where they cross them.</summary>
        internal List<Vec3d[]> RayLaths { get; } = new List<Vec3d[]>();
    }

    public static Result Build(MeshData mesh, PrincipalCurvature.Result curvature, int seedVertex, Options? options = null)
    {
        options ??= new Options();
        var res = new Result();
        var proj = new MeshProjection(mesh);
        var field = AsymptoticCurves.ComputeDirections(curvature, mesh, options.MinCrossingAngle);
        var f1 = field.Family1; var f2 = field.Family2; var exists = field.Exists;
        var (mn, mx) = mesh.BoundingBox();
        double diag = (mx - mn).Length;
        double h = options.Spacing > 0 ? options.Spacing : 0.04 * diag;
        double step = TraceDefaults.ResolveStep(options.StepSize, h, proj);
        int maxSteps = TraceDefaults.ResolveMaxSteps(options.MaxSteps, step, proj);
        res.Spacing = h; res.Step = step;

        int seed = seedVertex >= 0 && seedVertex < mesh.VertexCount ? seedVertex : proj.NearestVertexGlobal(0.5 * (mn + mx));
        var seedHit = proj.ClosestPoint(mesh.Vertices[seed], seed);
        Vec3d P = seedHit.Point, nP = seedHit.SmoothNormal.Normalized();
        res.Seed = P;

        // ---- symmetry -----------------------------------------------------
        double symTol = 0.3 * proj.AverageEdgeLength;
        if (options.Rotational && options.Symmetry != 0)
        {
            foreach (var (c, axis) in AxisCandidates(mesh))
            {
                if (IsRotationallySymmetric(proj, mesh, c, axis, 7, symTol) && IsRotationallySymmetric(proj, mesh, c, axis, 11, symTol)
                    && IsRotationallySymmetric(proj, mesh, c, axis, 13, symTol))
                {
                    if (BuildRotational(proj, f1, f2, exists, seed, c, axis, h, step, maxSteps, options, res)) return res;
                    break;
                }
            }
        }
        int order = 1;
        if (options.Symmetry >= 2) order = options.Symmetry;
        else if (options.Symmetry < 0)
            for (int n = 12; n >= 2; n--)
                if (IsRotationallySymmetric(proj, mesh, P, nP, n, symTol)) { order = n; break; }

        // ---- rays -----------------------------------------------------------
        Vec3d e1 = AnyPerpendicular(nP), e2 = Vec3d.Cross(nP, e1);
        var rays = new List<(double Angle, Vec3d Start, Vec3d Dir)>();
        bool regular = exists[seed] && f1[seed].LengthSquared > 1e-20 && f2[seed].LengthSquared > 1e-20;
        if (regular)
        {
            foreach (var d in new[] { f1[seed], -f1[seed], f2[seed], -f2[seed] })
            {
                var t = (d - Vec3d.Dot(d, nP) * nP).Normalized();
                rays.Add((Math.Atan2(Vec3d.Dot(t, e2), Vec3d.Dot(t, e1)), P, t));
            }
        }
        else
        {
            // flat point: the asymptotic rays are where the field points
            // radially on a small circle around the seed
            double r = Math.Max(3.0 * proj.AverageEdgeLength, 0.3 * h);
            const int M = 720;
            var score = new double[M];
            var at = new (Vec3d P, Vec3d U, int V)[M];
            int hint = seed;
            for (int m = 0; m < M; m++)
            {
                double th = 2 * Math.PI * m / M;
                var q = P + r * (Math.Cos(th) * e1 + Math.Sin(th) * e2);
                var hq = proj.ClosestPoint(q, hint); hint = hq.NearestVertex;
                var u = hq.Point - P; u = u - Vec3d.Dot(u, hq.SmoothNormal) * hq.SmoothNormal;
                if (u.LengthSquared < 1e-24) continue;
                u = u.Normalized();
                var d = FieldTracer.SampleLineField(proj, hq, f1, f2, u);
                double mag = d.Length;
                score[m] = mag > 1e-9 ? Math.Abs(Vec3d.Dot(d / mag, u)) * Math.Min(1.0, mag) : 0.0;
                at[m] = (hq.Point, u, hq.NearestVertex);
            }
            const int win = 24; // ±12°
            for (int m = 0; m < M; m++)
            {
                if (score[m] < 0.9) continue;
                bool isMax = true;
                for (int k = -win; k <= win && isMax; k++)
                {
                    if (k == 0) continue;
                    int j = ((m + k) % M + M) % M;
                    if (score[j] > score[m] || (score[j] == score[m] && j < m)) isMax = false;
                }
                if (!isMax) continue;
                double th = 2 * Math.PI * m / M;
                rays.Add((th, at[m].P, at[m].U));
            }
            res.Singular = true;
            res.Notes.Add($"Seed at a flat point: {rays.Count} asymptotic rays leave it (a singular node of valence {rays.Count}).");
        }
        rays.Sort((x, y) => x.Angle.CompareTo(y.Angle));
        int R = rays.Count;
        res.Rays = R;
        if (R < 4 && seedVertex < 0)
        {
            // automatic seed on a parabolic or elliptic spot: take the best
            // conditioned anticlastic vertex nearest to the centre instead
            int alt = -1; double bestD = double.MaxValue, maxS = 0;
            for (int i = 0; i < mesh.VertexCount; i++) if (exists[i]) maxS = Math.Max(maxS, Vec3d.Cross(f1[i].Normalized(), f2[i].Normalized()).Length);
            var centre = 0.5 * (mn + mx);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (!exists[i] || Vec3d.Cross(f1[i].Normalized(), f2[i].Normalized()).Length < 0.85 * maxS) continue;
                double d = (mesh.Vertices[i] - centre).LengthSquared;
                if (d < bestD) { bestD = d; alt = i; }
            }
            if (alt >= 0 && alt != seed) return Build(mesh, curvature, alt, options);
        }
        if (R < 4 || R % 2 != 0)
        {
            res.Notes.Add(R < 4 ? "No asymptotic directions at the seed: pick a seed where K < 0." : $"Odd number of rays ({R}) at the seed: move the seed off the flat point.");
            if (R < 4) return res;
        }
        if (order > 1 && (R % order != 0 || !RaysMatchRotation(rays, order)))
        {
            res.Notes.Add($"The mesh is {order}-fold symmetric about the seed normal, but its {R} rays do not repeat with it: symmetry not used.");
            order = 1;
        }
        res.SymmetryOrder = order;
        int fund = R / order; // rays traced; the rest are rotated copies

        // ---- trace the fundamental rays and their transversals --------------
        var rayLines = new Vec3d[R][];
        var trans = new List<(int Ray, int Index, Vec3d[] Line)>();
        for (int k = 0; k < fund; k++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;
            var ray = TraceRay(proj, f1, f2, exists, P, rays[k].Start, rays[k].Dir, seed, step, maxSteps, options);
            ray = CurveFairing.SmoothOnSurface(proj, ray, options.SmoothingPasses);
            rayLines[k] = ray;
            int i = 1;
            foreach (var (x, t) in NodesAlong(ray, h))
            {
                var hx = proj.ClosestPoint(x, proj.NearestVertexGlobal(x));
                var cand = Transversal(proj, hx, f1, f2, t);
                if (cand.LengthSquared < 1e-20) { i++; continue; }
                var line = FieldTracer.TraceBoth(proj, hx.Point, hx.NearestVertex, f1, f2, exists, step, maxSteps, null,
                    options.MinFieldMagnitude, cand);
                if (line.Length >= 2) trans.Add((k, i, CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses)));
                i++;
            }
        }
        if (order > 1)
        {
            int nFund = trans.Count;
            for (int m = 1; m < order; m++)
            {
                double ang = 2 * Math.PI * m / order;
                for (int k = 0; k < fund; k++)
                    if (rayLines[k] != null) rayLines[k + m * fund] = RotateOnto(proj, rayLines[k], P, nP, ang);
                for (int q = 0; q < nFund; q++)
                    trans.Add((trans[q].Ray + m * fund, trans[q].Index, RotateOnto(proj, trans[q].Line, P, nP, ang)));
            }
        }
        else
        {
            for (int k = fund; k < R; k++)
            {
                var ray = TraceRay(proj, f1, f2, exists, P, rays[k].Start, rays[k].Dir, seed, step, maxSteps, options);
                ray = CurveFairing.SmoothOnSurface(proj, ray, options.SmoothingPasses);
                rayLines[k] = ray;
                int i = 1;
                foreach (var (x, t) in NodesAlong(ray, h))
                {
                    var hx = proj.ClosestPoint(x, proj.NearestVertexGlobal(x));
                    var cand = Transversal(proj, hx, f1, f2, t);
                    if (cand.LengthSquared < 1e-20) { i++; continue; }
                    var line = FieldTracer.TraceBoth(proj, hx.Point, hx.NearestVertex, f1, f2, exists, step, maxSteps, null,
                        options.MinFieldMagnitude, cand);
                    if (line.Length >= 2) trans.Add((k, i, CurveFairing.SmoothOnSurface(proj, line, options.SmoothingPasses)));
                    i++;
                }
            }
        }

        // ---- laths with labels: rays alternate families around the seed ------
        // Regular seed: ray k and k + 2 are the two halves of one curve. At a
        // flat point the families swap across the rays, so a curve through the
        // seed is one label on one side and the other beyond it: the rays stay
        // separate laths meeting at the singular node.
        if (!res.Singular && R == 4)
        {
            for (int k = 0; k < 2; k++)
            {
                if (rayLines[k] == null || rayLines[k + 2] == null) continue;
                var joined = rayLines[k + 2].Reverse().Concat(rayLines[k].Skip(1)).ToArray();
                (k % 2 == 0 ? res.A : res.B).Add(joined);
            }
        }
        else
        {
            for (int k = 0; k < R; k++)
                if (rayLines[k] != null && rayLines[k].Length > 1)
                {
                    (k % 2 == 0 ? res.A : res.B).Add(rayLines[k]);
                    res.RayLaths.Add(rayLines[k]);
                }
        }
        foreach (var (k, _, line) in trans) (k % 2 == 0 ? res.B : res.A).Add(line);

        FinishNet(proj, res, h, options);
        if (order > 1) res.SymmetryError = SymmetryResidual(res.Nodes, P, nP, order);
        return res;
    }

    // ---- surface of revolution: rotated copies (rotational AAG web) -------------

    private static bool BuildRotational(MeshProjection proj, Vec3d[] f1, Vec3d[] f2, bool[] exists, int seed, Vec3d c, Vec3d axis,
        double h, double step, int maxSteps, Options options, Result res)
    {
        if (!exists[seed] || f1[seed].LengthSquared < 1e-20) return false;
        var mesh = proj.Mesh;
        var P = proj.ClosestPoint(mesh.Vertices[seed], seed).Point;
        axis = axis.Normalized();
        var radial = P - c - Vec3d.Dot(P - c, axis) * axis;
        double rho = radial.Length;
        if (rho < 1e-9) return false;
        // A through the seed and its mirror image in the seed's meridian plane
        var a0 = FieldTracer.TraceBoth(proj, P, seed, f1, f2, exists, step, maxSteps, null, options.MinFieldMagnitude, f1[seed]);
        if (a0.Length < 3) return false;
        a0 = CurveFairing.SmoothOnSurface(proj, a0, options.SmoothingPasses);
        var mnormal = Vec3d.Cross(axis, radial.Normalized()).Normalized(); // normal of the meridian plane through P
        var b0 = a0.Select(p => p - 2 * Vec3d.Dot(p - P, mnormal) * mnormal).ToArray();
        b0 = b0.Select(p => proj.ClosestPoint(p, proj.NearestVertexGlobal(p)).Point).ToArray();
        int N = Math.Max(3, (int)Math.Round(2 * Math.PI * rho / (Math.Sqrt(2) * h)));
        double dphi = 2 * Math.PI / N;
        for (int k = 0; k < N; k++)
        {
            res.A.Add(RotateOnto(proj, a0, c, axis, k * dphi));
            res.B.Add(RotateOnto(proj, b0, c, axis, k * dphi));
        }
        res.RotationalWeb = true;
        res.SymmetryOrder = int.MaxValue;
        res.Rays = 4;
        res.Notes.Add($"Surface of revolution: {N} rotated copies of each asymptotic curve through the seed (Δφ = {360.0 / N:0.##}°); the node diagonals along the meridians are planar geodesics — a rotational AAG web.");
        FinishNet(proj, res, h, options);
        res.SymmetryError = SymmetryResidual(res.Nodes, c, axis, N);
        return true;
    }

    // ---- nodes, tails, quads, diagonals -----------------------------------------

    private static void FinishNet(MeshProjection proj, Result res, double h, Options options)
    {
        // rotated copies sit on the exact symmetric surface, a sagitta off the
        // facets the traced curves lie on: accept crossings within a third of an edge
        double xTol = 0.3 * proj.AverageEdgeLength;
        var xs = NetIntersections.Find(res.A, res.B, xTol);
        // tails shorter than TrimTails × h beyond the last node are cut at the node
        if (options.TrimTails > 0 && xs.Count > 0)
        {
            TrimTails(res.A, xs.Select(x => (x.CurveA, x.SegmentA + x.ParamA)).ToList(), options.TrimTails * h);
            TrimTails(res.B, xs.Select(x => (x.CurveB, x.SegmentB + x.ParamB)).ToList(), options.TrimTails * h);
            xs = NetIntersections.Find(res.A, res.B, xTol);
        }
        // nodes: crossings, merged when two crossings coincide (the seed of a regular web)
        double mergeTol = 0.05 * h;
        var nodeOf = new int[xs.Count];
        var grid = new Dictionary<(long, long, long), List<int>>();
        (long, long, long) Key(Vec3d p) => ((long)Math.Floor(p.X / mergeTol), (long)Math.Floor(p.Y / mergeTol), (long)Math.Floor(p.Z / mergeTol));
        int FindNode(Vec3d p)
        {
            var (kx, ky, kz) = Key(p);
            for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) for (long dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var l))
                            foreach (int j in l) if ((res.Nodes[j] - p).Length < mergeTol) return j;
            return -1;
        }
        int AddNode(Vec3d p)
        {
            int j = res.Nodes.Count; res.Nodes.Add(p);
            var k = Key(p);
            (grid.TryGetValue(k, out var l) ? l : grid[k] = new List<int>()).Add(j);
            return j;
        }
        for (int i = 0; i < xs.Count; i++)
        {
            int found = FindNode(xs[i].Point);
            nodeOf[i] = found >= 0 ? found : AddNode(xs[i].Point);
        }
        // flat-point seed: the rays' shared start is a node too
        if (res.Singular && FindNode(res.Seed) < 0) AddNode(res.Seed);

        // node order along every curve: (param, node)
        var alongA = new List<(double, int)>[res.A.Count];
        var alongB = new List<(double, int)>[res.B.Count];
        for (int i = 0; i < res.A.Count; i++) alongA[i] = new List<(double, int)>();
        for (int i = 0; i < res.B.Count; i++) alongB[i] = new List<(double, int)>();
        for (int i = 0; i < xs.Count; i++)
        {
            alongA[xs[i].CurveA].Add((xs[i].SegmentA + xs[i].ParamA, nodeOf[i]));
            alongB[xs[i].CurveB].Add((xs[i].SegmentB + xs[i].ParamB, nodeOf[i]));
        }
        foreach (var l in alongA) l.Sort((x, y) => x.Item1.CompareTo(y.Item1));
        foreach (var l in alongB) l.Sort((x, y) => x.Item1.CompareTo(y.Item1));

        // edges between consecutive nodes along each curve
        var nbA = new Dictionary<int, List<int>>(); var nbB = new Dictionary<int, List<int>>();
        void Link(Dictionary<int, List<int>> nb, int a, int b)
        {
            if (a == b) return;
            (nb.TryGetValue(a, out var la) ? la : nb[a] = new List<int>()).Add(b);
            (nb.TryGetValue(b, out var lb) ? lb : nb[b] = new List<int>()).Add(a);
        }
        foreach (var l in alongA) for (int i = 1; i < l.Count; i++) Link(nbA, l[i - 1].Item2, l[i].Item2);
        foreach (var l in alongB) for (int i = 1; i < l.Count; i++) Link(nbB, l[i - 1].Item2, l[i].Item2);

        res.NeighborsA = new int[res.Nodes.Count][];
        res.NeighborsB = new int[res.Nodes.Count][];
        for (int i = 0; i < res.Nodes.Count; i++)
        {
            res.NeighborsA[i] = nbA.TryGetValue(i, out var la) ? la.Distinct().ToArray() : Array.Empty<int>();
            res.NeighborsB[i] = nbB.TryGetValue(i, out var lb) ? lb.Distinct().ToArray() : Array.Empty<int>();
        }

        // quads: a → b along A, b → c along B, c → d along A, d → a along B
        var seen = new HashSet<string>();
        foreach (var a in nbA.Keys)
            foreach (int b in nbA[a])
            {
                if (!nbB.TryGetValue(b, out var bs)) continue;
                foreach (int cc in bs)
                {
                    if (cc == a || !nbA.TryGetValue(cc, out var cs)) continue;
                    foreach (int d in cs)
                    {
                        if (d == b || d == a || !nbB.TryGetValue(d, out var ds) || !ds.Contains(a)) continue;
                        var key = string.Join(",", new[] { a, b, cc, d }.OrderBy(x => x));
                        if (seen.Add(key)) res.Quads.Add(new[] { a, b, cc, d });
                    }
                }
            }

        BuildDiagonals(proj, res, h);
    }

    private static void TrimTails(List<Vec3d[]> curves, List<(int Curve, double S)> params_, double minTail)
    {
        var lo = new double[curves.Count]; var hi = new double[curves.Count];
        for (int i = 0; i < curves.Count; i++) { lo[i] = double.MaxValue; hi[i] = double.MinValue; }
        foreach (var (c, sPar) in params_) { lo[c] = Math.Min(lo[c], sPar); hi[c] = Math.Max(hi[c], sPar); }
        for (int i = 0; i < curves.Count; i++)
        {
            var l = curves[i];
            if (lo[i] > hi[i] || l.Length < 2) continue;
            if ((l[0] - l[l.Length - 1]).LengthSquared < 1e-24) continue; // closed
            double tailStart = ArcBetween(l, 0, lo[i]), tailEnd = ArcBetween(l, hi[i], l.Length - 1);
            double from = tailStart < minTail ? lo[i] : 0, to = tailEnd < minTail ? hi[i] : l.Length - 1;
            if (from == 0 && to == l.Length - 1) continue;
            if (to - from < 1e-9) continue;
            // keep the node itself: extend by a hair so the crossing is still found
            curves[i] = Sub(l, Math.Max(0, from - 0.02), Math.Min(l.Length - 1, to + 0.02));
        }
    }

    private static double ArcBetween(Vec3d[] l, double s0, double s1)
    {
        if (s1 < s0) (s0, s1) = (s1, s0);
        Vec3d At(double s) { int i = Math.Min(l.Length - 2, (int)Math.Floor(s)); double t = s - i; return l[i] + t * (l[i + 1] - l[i]); }
        double len = 0; Vec3d prev = At(s0);
        for (int i = (int)Math.Floor(s0) + 1; i < s1; i++) { len += (l[i] - prev).Length; prev = l[i]; }
        return len + (At(s1) - prev).Length;
    }

    private static Vec3d[] Sub(Vec3d[] l, double s0, double s1)
    {
        Vec3d At(double s) { int i = Math.Min(l.Length - 2, (int)Math.Floor(s)); double t = s - i; return l[i] + t * (l[i + 1] - l[i]); }
        var o = new List<Vec3d> { At(s0) };
        for (int i = (int)Math.Floor(s0) + 1; i < s1; i++) o.Add(l[i]);
        var e = At(s1);
        if ((e - o[o.Count - 1]).LengthSquared > 1e-24) o.Add(e);
        return o.ToArray();
    }

    /// <summary>
    /// Diagonal polylines through opposite quad corners, chained straight
    /// through the nodes and split into two families (the two diagonals of a
    /// quad belong to different families). The geodesic error of each family
    /// is the mean |geodesic curvature| of its polylines times the spacing,
    /// measured in the tangent plane of the surface: 0 for an AAG diagonal.
    /// </summary>
    private static void BuildDiagonals(MeshProjection proj, Result res, double h)
    {
        if (res.Quads.Count == 0) return;
        // diagonal edges with the quad they come from; edge id per quad: 2q (a–c), 2q + 1 (b–d)
        var edges = new List<(int U, int V)>();
        foreach (var q in res.Quads) { edges.Add((q[0], q[2])); edges.Add((q[1], q[3])); }
        var atNode = new Dictionary<int, List<int>>();
        for (int e = 0; e < edges.Count; e++)
        {
            (atNode.TryGetValue(edges[e].U, out var lu) ? lu : atNode[edges[e].U] = new List<int>()).Add(e);
            (atNode.TryGetValue(edges[e].V, out var lv) ? lv : atNode[edges[e].V] = new List<int>()).Add(e);
        }
        Vec3d Dir(int e, int from) { int to = edges[e].U == from ? edges[e].V : edges[e].U; return (res.Nodes[to] - res.Nodes[from]).Normalized(); }
        // pair the diagonal edges at each node through opposite quads (quads
        // that share only the node, not an edge); a node with other than four
        // quads (border, singular seed) ends its diagonals, and so does a node
        // on a ray of a singular web, where the diagonal families swap
        var onRay = new bool[res.Nodes.Count];
        foreach (var ray in res.RayLaths)
            for (int i = 0; i < res.Nodes.Count; i++)
                if (!onRay[i] && DistanceToPolyline(res.Nodes[i], ray) < 1e-3 * h) onRay[i] = true;
        var next = new Dictionary<(int Node, int Edge), int>();
        foreach (var kv in atNode)
        {
            int v = kv.Key;
            var list = kv.Value;
            if (list.Count != 4 || onRay[v]) continue;
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                {
                    int qa = list[i] / 2, qb = list[j] / 2;
                    int common = res.Quads[qa].Count(a => res.Quads[qb].Contains(a));
                    if (common == 1) { next[(v, list[i])] = list[j]; next[(v, list[j])] = list[i]; }
                }
        }
        var edgeDone = new bool[edges.Count];
        var polyOfEdge = new int[edges.Count];
        var polys = new List<List<int>>();
        for (int e0 = 0; e0 < edges.Count; e0++)
        {
            if (edgeDone[e0]) continue;
            // walk back to the start of the chain, then forward
            int e = e0, node = edges[e0].U, guard = 0;
            while (next.TryGetValue((node, e), out int pe) && pe != e0 && guard++ < edges.Count)
            {
                node = edges[pe].U == node ? edges[pe].V : edges[pe].U;
                e = pe;
            }
            // e is the first edge, node its free end
            var poly = new List<int> { node };
            int pid = polys.Count;
            guard = 0;
            while (true)
            {
                edgeDone[e] = true; polyOfEdge[e] = pid;
                int other = edges[e].U == node ? edges[e].V : edges[e].U;
                poly.Add(other);
                node = other;
                if (!next.TryGetValue((node, e), out int ne) || edgeDone[ne] || guard++ > edges.Count) break;
                e = ne;
            }
            polys.Add(poly);
        }
        // two-colour: the two diagonals of every quad differ
        var colour = Enumerable.Repeat(-1, polys.Count).ToArray();
        var adj = new List<int>[polys.Count];
        for (int i = 0; i < polys.Count; i++) adj[i] = new List<int>();
        for (int q = 0; q < res.Quads.Count; q++) { int p1 = polyOfEdge[2 * q], p2 = polyOfEdge[2 * q + 1]; if (p1 != p2) { adj[p1].Add(p2); adj[p2].Add(p1); } }
        for (int s = 0; s < polys.Count; s++)
        {
            if (colour[s] >= 0) continue;
            colour[s] = 0;
            var queue = new Queue<int>(); queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                foreach (int o in adj[p]) if (colour[o] < 0) { colour[o] = 1 - colour[p]; queue.Enqueue(o); }
            }
        }
        // components of the quad-sharing graph (one per sector of a singular web)
        var comp = Enumerable.Repeat(-1, polys.Count).ToArray();
        int nComp = 0;
        for (int s0 = 0; s0 < polys.Count; s0++)
        {
            if (comp[s0] >= 0) continue;
            var queue = new Queue<int>(); queue.Enqueue(s0); comp[s0] = nComp;
            while (queue.Count > 0) { int p = queue.Dequeue(); foreach (int o in adj[p]) if (comp[o] < 0) { comp[o] = nComp; queue.Enqueue(o); } }
            nComp++;
        }
        var kgErr = new double[polys.Count]; var kgN = new int[polys.Count];
        for (int i = 0; i < polys.Count; i++)
        {
            var pts = polys[i].Select(n => res.Nodes[n]).ToArray();
            for (int k = 1; k + 1 < pts.Length; k++)
            {
                var hit = proj.ClosestPoint(pts[k], proj.NearestVertexGlobal(pts[k]));
                var n = hit.SmoothNormal.Normalized();
                Vec3d a = pts[k] - pts[k - 1], b = pts[k + 1] - pts[k];
                double la = a.Length, lb = b.Length;
                if (la < 1e-12 || lb < 1e-12) continue;
                // turning in the tangent plane per unit length
                double kg = Vec3d.Dot(Vec3d.Cross(a / la, b / lb), n) * 2.0 / (la + lb);
                kgErr[i] += Math.Abs(kg) * h; kgN[i]++;
            }
        }
        // per component, family 0 is the straighter colour (the geodesic candidate)
        var flip = new bool[nComp];
        for (int c = 0; c < nComp; c++)
        {
            double[] e = new double[2]; int[] n = new int[2];
            for (int i = 0; i < polys.Count; i++) if (comp[i] == c) { e[colour[i]] += kgErr[i]; n[colour[i]] += kgN[i]; }
            flip[c] = n[0] > 0 && n[1] > 0 && e[1] / n[1] < e[0] / n[0];
        }
        var errSum = new double[2]; var errN = new int[2];
        for (int i = 0; i < polys.Count; i++)
        {
            if (polys[i].Count < 2) continue;
            int fam = flip[comp[i]] ? 1 - colour[i] : colour[i];
            res.Diagonals.Add(polys[i].Select(n => res.Nodes[n]).ToArray());
            res.DiagonalNodes.Add(polys[i].ToArray());
            res.DiagonalFamily.Add(fam);
            errSum[fam] += kgErr[i]; errN[fam] += kgN[i];
        }
        for (int c = 0; c < 2; c++) res.DiagonalGeodesicError[c] = errN[c] > 0 ? errSum[c] / errN[c] : double.NaN;
    }

    // ---- helpers ---------------------------------------------------------------

    private static Vec3d[] TraceRay(MeshProjection proj, Vec3d[] f1, Vec3d[] f2, bool[] exists, Vec3d P, Vec3d start, Vec3d dir,
        int seed, double step, int maxSteps, Options options)
    {
        var hs = proj.ClosestPoint(start, proj.NearestVertexGlobal(start));
        var fwd = FieldTracer.Trace(proj, hs.Point, hs.NearestVertex, f1, f2, exists, step, maxSteps, false, null, out _,
            options.MinFieldMagnitude, dir);
        var pts = new List<Vec3d>();
        double r = (start - P).Length;
        if (r > 1e-12)
        {
            // flat point: the field is undefined at the seed and unreliable in the
            // first rings around it, so the ray runs as a chord from the seed to
            // the circle point where the field points exactly along it (radially)
            int n = Math.Max(1, (int)Math.Ceiling(r / step));
            for (int i = 0; i < n; i++)
            {
                var q = P + (double)i / n * (hs.Point - P);
                pts.Add(i == 0 ? P : proj.ClosestPoint(q, proj.NearestVertexGlobal(q)).Point);
            }
        }
        pts.AddRange(fwd);
        return pts.ToArray();
    }

    /// <summary>The asymptotic direction at a node that is not the ray's own (the other family).</summary>
    private static Vec3d Transversal(MeshProjection proj, in MeshProjection.Hit hit, Vec3d[] f1, Vec3d[] f2, Vec3d rayTangent)
    {
        if (hit.Face < 0) return Vec3d.Zero;
        Vec3d best = Vec3d.Zero; double bestScore = double.MaxValue;
        // pick at the nearest corner the candidate least aligned with the ray
        int v = hit.NearestVertex;
        foreach (var c in new[] { f1[v], f2[v] })
        {
            if (c.LengthSquared < 1e-20) continue;
            double s = Math.Abs(Vec3d.Dot(c.Normalized(), rayTangent));
            if (s < bestScore) { bestScore = s; best = c.Normalized(); }
        }
        if (best.LengthSquared < 1e-20) return best;
        return FieldTracer.SampleLineField(proj, hit, f1, f2, best);
    }

    /// <summary>Points at arc length h, 2h, … from the start of a polyline, with the unit tangent there.</summary>
    private static IEnumerable<(Vec3d, Vec3d)> NodesAlong(Vec3d[] line, double h)
    {
        double acc = 0, due = h;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var seg = line[i + 1] - line[i];
            double l = seg.Length;
            if (l < 1e-15) continue;
            while (acc + l >= due)
            {
                double t = (due - acc) / l;
                yield return (line[i] + t * seg, seg / l);
                due += h;
            }
            acc += l;
        }
    }

    private static double DistanceToPolyline(Vec3d p, Vec3d[] l)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < l.Length; i++)
        {
            var ab = l[i + 1] - l[i];
            double t = Math.Max(0, Math.Min(1, Vec3d.Dot(p - l[i], ab) / Math.Max(ab.LengthSquared, 1e-30)));
            best = Math.Min(best, (l[i] + t * ab - p).Length);
        }
        return best;
    }

    private static Vec3d AnyPerpendicular(Vec3d n)
    {
        var a = Math.Abs(n.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
        return Vec3d.Cross(n, a).Normalized();
    }

    /// <summary>Rotates p about the axis through c (Rodrigues).</summary>
    public static Vec3d Rotate(Vec3d p, Vec3d c, Vec3d axis, double angle)
    {
        var k = axis.Normalized();
        var v = p - c;
        double cs = Math.Cos(angle), sn = Math.Sin(angle);
        return c + cs * v + sn * Vec3d.Cross(k, v) + (1 - cs) * Vec3d.Dot(k, v) * k;
    }

    private static Vec3d[] RotateOnto(MeshProjection proj, Vec3d[] line, Vec3d c, Vec3d axis, double angle)
    {
        var o = new Vec3d[line.Length];
        int hint = -1;
        for (int i = 0; i < line.Length; i++)
        {
            var q = Rotate(line[i], c, axis, angle);
            // projected onto the facets, so every curve lies on the mesh and the
            // copies meet the traced curves within the usual crossing tolerance
            // (the symmetry then holds to the mesh's faceting, not to rounding)
            var hq = proj.ClosestPoint(q, hint >= 0 ? hint : proj.NearestVertexGlobal(q));
            hint = hq.NearestVertex;
            o[i] = (hq.Point - q).Length < 0.5 * proj.AverageEdgeLength ? hq.Point : q;
        }
        return o;
    }

    private static IEnumerable<(Vec3d C, Vec3d Axis)> AxisCandidates(MeshData mesh)
    {
        int n = mesh.VertexCount;
        Vec3d c = Vec3d.Zero;
        for (int i = 0; i < n; i++) c = c + mesh.Vertices[i];
        c = c / Math.Max(1, n);
        // principal axes of the vertex cloud
        var m = new double[3, 3];
        for (int i = 0; i < n; i++)
        {
            var d = mesh.Vertices[i] - c;
            double[] v = { d.X, d.Y, d.Z };
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) m[a, b] += v[a] * v[b];
        }
        foreach (var ax in SymmetricEigenvectors(m)) yield return (c, ax);
        yield return (c, new Vec3d(0, 0, 1));
    }

    private static List<Vec3d> SymmetricEigenvectors(double[,] a)
    {
        // Jacobi rotations on a 3×3 symmetric matrix
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var m = (double[,])a.Clone();
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(m[0, 1]) + Math.Abs(m[0, 2]) + Math.Abs(m[1, 2]);
            if (off < 1e-14 * (Math.Abs(m[0, 0]) + Math.Abs(m[1, 1]) + Math.Abs(m[2, 2]) + 1e-300)) break;
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(m[p, q]) < 1e-300) continue;
                    double th = 0.5 * Math.Atan2(2 * m[p, q], m[q, q] - m[p, p]);
                    double c = Math.Cos(th), s = Math.Sin(th);
                    for (int k = 0; k < 3; k++)
                    {
                        double mkp = m[k, p], mkq = m[k, q];
                        m[k, p] = c * mkp - s * mkq; m[k, q] = s * mkp + c * mkq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double mpk = m[p, k], mqk = m[q, k];
                        m[p, k] = c * mpk - s * mqk; m[q, k] = s * mpk + c * mqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                    }
                }
        }
        return new List<Vec3d> { new Vec3d(v[0, 0], v[1, 0], v[2, 0]), new Vec3d(v[0, 1], v[1, 1], v[2, 1]), new Vec3d(v[0, 2], v[1, 2], v[2, 2]) };
    }

    /// <summary>True when rotating the mesh by 2π/n about the axis maps (a sample of) its vertices onto the mesh.</summary>
    internal static bool IsRotationallySymmetric(MeshProjection proj, MeshData mesh, Vec3d c, Vec3d axis, int n, double tol)
    {
        if (axis.LengthSquared < 1e-20) return false;
        int stride = Math.Max(1, mesh.VertexCount / 400);
        double ang = 2 * Math.PI / n;
        for (int i = 0; i < mesh.VertexCount; i += stride)
        {
            var q = Rotate(mesh.Vertices[i], c, axis, ang);
            var hq = proj.ClosestPoint(q, proj.NearestVertexGlobal(q));
            if ((hq.Point - q).Length > tol) return false;
        }
        return true;
    }

    private static bool RaysMatchRotation(List<(double Angle, Vec3d Start, Vec3d Dir)> rays, int order)
    {
        int R = rays.Count, shift = R / order;
        double target = 2 * Math.PI / order;
        for (int k = 0; k < R; k++)
        {
            double d = rays[(k + shift) % R].Angle - rays[k].Angle;
            d = ((d % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
            if (Math.Abs(d - target) > 8 * Math.PI / 180) return false;
        }
        return true;
    }

    private static double SymmetryResidual(List<Vec3d> nodes, Vec3d c, Vec3d axis, int order)
    {
        if (nodes.Count == 0 || order < 2) return 0;
        // RMS distance from each rotated node to its nearest node; nodes whose
        // image falls on a position the border trimmed away are ignored
        double sum = 0; int n = 0;
        double ang = 2 * Math.PI / order;
        double far = double.MaxValue;
        if (nodes.Count > 1) { far = 0; for (int i = 1; i < Math.Min(nodes.Count, 50); i++) far = Math.Max(far, (nodes[i] - nodes[0]).Length); far *= 0.05; }
        foreach (var p in nodes)
        {
            var q = Rotate(p, c, axis, ang);
            double best = double.MaxValue;
            foreach (var o in nodes) best = Math.Min(best, (o - q).LengthSquared);
            best = Math.Sqrt(best);
            if (best > far) continue;
            sum += best * best; n++;
        }
        return n > 0 ? Math.Sqrt(sum / n) : 0;
    }
}
