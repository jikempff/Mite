using System;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Dynamics;

/// <summary>
/// Dynamic relaxation form finding with kinetic damping, after Day 1965 and
/// Barnes 1977 / 1999 ("Form finding and analysis of tension structures by
/// dynamic relaxation", Int. J. Space Structures 14(2)); the sources read for
/// this version are Barnes' 1977 thesis (City University, ch. 2: the
/// central-difference recurrence, the stability limit Δt_crit = √(2M/S) with
/// S the direct stiffness of a node, slack links), Veenendaal &amp; Block 2011
/// (DR with lumped stiffness; the force density Q = T/L as its geometric
/// stiffness), D'Amico, Kermani &amp; Zhang 2014 (bending-active DR of timber
/// gridshells, residual tolerance) and the Oasys GSA theory manual (fictitious
/// mass from the summed nodal stiffness, the kinetic-energy peak located by a
/// parabola through the last three values).
///
/// The scheme, per free node i with residual R_i (springs, gravity, loads,
/// soap-film tension, smoothing, lath bending):
///   v_{t+Δt/2} = v_{t−Δt/2} + Δt R_t / M_i,   x_{t+Δt} = x_t + Δt v_{t+Δt/2}
/// started (and restarted) from rest with the half step v_{Δt/2} = ½Δt R/M.
/// Kinetic damping: the kinetic energy Σ ½ M v² is read at every half step;
/// when it falls, the peak lay inside the last step. Barnes takes it at
/// t − Δt/2 and moves the nodes back to that position,
///   x* = x_{t+Δt} − (3Δt/2) v_{t+Δt/2} + (Δt²/2) R_t / M
/// (the central-difference trajectory evaluated at t − Δt/2); with
/// <see cref="Options.PeakFit"/> the peak time is the vertex of the parabola
/// through the last three kinetic energies, δ ∈ [−Δt/2, Δt/2] off t − Δt/2,
/// and x* = x_{t−Δt/2} + δ v_{t−Δt/2} + ½ δ² R_t / M. Velocities are then
/// zeroed and the recurrence restarts from x*.
/// Fictitious masses: M_i = λ Δt² S_i / 2 with S_i the direct stiffness of
/// node i — the Gerschgorin bound Δt ≤ √(2 M_i / S_i) of the explicit
/// scheme turned into a mass, so every node has the same nominal frequency
/// and Δt is nominally 1. S_i sums, over the links at i, the spring stiffness
/// k plus the geometric stiffness |T| / L of the current tension, the
/// smoothing strength, the soap-film cotangent weights and the lath bending
/// bound (<see cref="LathBending.AddDirectStiffness"/>); it is refreshed at
/// every kinetic reset. λ = <see cref="Options.MassFactor"/>.
/// </summary>
public static class DynamicRelaxation
{
    public class Options
    {
        /// <summary>Edge spring stiffness (force per unit stretch).</summary>
        public double Stiffness { get; set; } = 1.0;

        /// <summary>Rest-length factor: 1 keeps edge lengths, &lt; 1 pre-tensions the net (0 = force densities k).</summary>
        public double RestScale { get; set; } = 1.0;

        /// <summary>Uniform acceleration applied to every free vertex (e.g. (0,0,-9.81) for gravity).</summary>
        public Vec3d Gravity { get; set; } = Vec3d.Zero;

        /// <summary>Soap-film tension (area minimization strength, 0 disables).</summary>
        public double AreaTension { get; set; } = 0.0;

        /// <summary>Laplacian smoothing strength (0 disables).</summary>
        public double Smoothness { get; set; } = 0.0;

        /// <summary>Optional per-vertex point loads (force).</summary>
        public Vec3d[]? Loads { get; set; }

        /// <summary>Maximum integration steps (residual evaluations).</summary>
        public int MaxIterations { get; set; } = 2000;

        /// <summary>
        /// Time step. With fictitious masses (the default) it only scales the
        /// masses and the motion is the same for any value, so 0 means 1.
        /// With <see cref="FictitiousMasses"/> off, 0 = automatic
        /// 0.5 √(1 / k_eff) for unit masses (the 1.3.2 behaviour).
        /// </summary>
        public double TimeStep { get; set; } = 0.0;

        /// <summary>Stops when the largest residual force on a free vertex falls below this fraction of the largest applied force.</summary>
        public double Tolerance { get; set; } = 1e-4;

        /// <summary>Optional cancellation probe checked every few iterations.</summary>
        public Func<bool>? ShouldCancel { get; set; }

        /// <summary>
        /// Bending stiffness EI of the laths (0 = none). Laths are the chains
        /// of edges that run straight through the quad mesh — the rows and
        /// columns of a quad grid — unless <see cref="Laths"/> is given.
        /// </summary>
        public double Bending { get; set; } = 0.0;

        /// <summary>Explicit laths as vertex chains (a closed ring repeats its first vertex at the end). Null = derive from the quad faces.</summary>
        public int[][]? Laths { get; set; }

        /// <summary>
        /// Rest length per edge (in <see cref="MeshData.BuildEdges"/> order)
        /// instead of the input edge lengths: a lath cut longer than its chord
        /// buckles, a cable cut shorter pulls. <see cref="RestScale"/> still
        /// multiplies them. Null = the input lengths.
        /// </summary>
        public double[]? RestLengths { get; set; }

        /// <summary>Per-node fictitious masses M_i = λ Δt² S_i / 2 (Barnes); off = unit masses with the stability step (1.3.2).</summary>
        public bool FictitiousMasses { get; set; } = true;

        /// <summary>Safety factor λ on the fictitious masses (1 = the Gerschgorin bound itself).</summary>
        public double MassFactor { get; set; } = 1.0;

        /// <summary>Kinetic damping moves the nodes back to the kinetic-energy peak (Barnes); off = zero the velocities only (1.3.2).</summary>
        public bool ResetToPeak { get; set; } = true;

        /// <summary>
        /// Locate the peak by a parabola through the last three kinetic
        /// energies (Oasys GSA; Alamatian 2022) instead of at t − Δt/2
        /// (Barnes). Off by default: measured on a hanging net, an elastic
        /// chain, a soap film and two bent laths the parabola was faster on two
        /// cases and slower on three.
        /// </summary>
        public bool PeakFit { get; set; } = false;
    }

    public readonly struct Result
    {
        public readonly Vec3d[] Vertices;
        public readonly int Iterations;

        /// <summary>Largest residual force on a free vertex at the end.</summary>
        public readonly double Residual;

        /// <summary>Per-edge axial force at the end (tension positive).</summary>
        public readonly double[] EdgeForces;

        public readonly bool Converged;

        /// <summary>Laths that carried bending (empty when Bending = 0).</summary>
        public readonly int[][] Laths;

        /// <summary>Curvature 1 / R at every lath node (0 at open ends), in the order of <see cref="Laths"/>.</summary>
        public readonly double[][] Curvatures;

        /// <summary>Kinetic-energy peaks met (velocity resets).</summary>
        public readonly int Resets;

        public Result(Vec3d[] vertices, int iterations, double residual, double[] edgeForces, bool converged)
            : this(vertices, iterations, residual, edgeForces, converged, Array.Empty<int[]>(), Array.Empty<double[]>(), 0) { }

        public Result(Vec3d[] vertices, int iterations, double residual, double[] edgeForces, bool converged,
            int[][] laths, double[][] curvatures, int resets)
        {
            Vertices = vertices;
            Iterations = iterations;
            Residual = residual;
            EdgeForces = edgeForces;
            Converged = converged;
            Laths = laths;
            Curvatures = curvatures;
            Resets = resets;
        }
    }

    public static Result Compute(MeshData mesh, bool[] fixedVertices, Options? options = null)
    {
        options ??= new Options();
        if (fixedVertices == null || fixedVertices.Length != mesh.VertexCount)
            throw new ArgumentException("fixedVertices must provide one flag per vertex.", nameof(fixedVertices));

        int nv = mesh.VertexCount;
        int fixedCount = 0;
        foreach (bool f in fixedVertices) if (f) fixedCount++;
        if (fixedCount == nv || nv == 0)
            return new Result((Vec3d[])mesh.Vertices.Clone(), 0, 0.0, new double[mesh.BuildEdges().Length], true);

        var physics = new MeshPhysics(mesh, fixedVertices);
        var edges = physics.Edges;
        if (options.RestLengths != null)
        {
            if (options.RestLengths.Length != edges.Length)
                throw new ArgumentException($"RestLengths needs one value per edge ({edges.Length}), got {options.RestLengths.Length}.", nameof(options));
            for (int e = 0; e < edges.Length; e++)
                if (options.RestLengths[e] >= 0 && !double.IsNaN(options.RestLengths[e]))
                    physics.RestLengths[e] = options.RestLengths[e];
        }
        var x = physics.Positions;
        var v = physics.Velocities;

        int[][] laths = Array.Empty<int[]>();
        if (options.Bending > 0)
        {
            laths = options.Laths ?? LathBending.FromQuadMesh(mesh);
            foreach (var lath in laths)
                foreach (int i in lath)
                    if (i < 0 || i >= nv) throw new ArgumentException("Laths refer to a vertex outside the mesh.", nameof(options));
        }

        // Effective stiffness per vertex bounds the stable explicit step (unit masses)
        var valence = new int[nv];
        foreach (var e in edges) { valence[e.v0]++; valence[e.v1]++; }
        int maxValence = 1;
        foreach (int val in valence) maxValence = Math.Max(maxValence, val);
        double kEff = Math.Max(options.Stiffness, 1e-12) * maxValence
                    + Math.Max(options.Smoothness, 0) + Math.Max(options.AreaTension, 0) * maxValence;
        double dt = options.FictitiousMasses
            ? (options.TimeStep > 0 ? options.TimeStep : 1.0)
            : (options.TimeStep > 0 ? options.TimeStep : 0.5 * Math.Sqrt(1.0 / kEff));

        double appliedScale = 0;
        var gravityForces = physics.ComputeGravityForces(options.Gravity);
        for (int i = 0; i < nv; i++)
        {
            double f = gravityForces[i].Length;
            if (options.Loads != null && i < options.Loads.Length) f += options.Loads[i].Length;
            appliedScale = Math.Max(appliedScale, f);
        }
        // With no applied loads (pure spring / soap-film / bending relaxation)
        // judge convergence against the initial internal force scale instead
        double residualScale = appliedScale;

        var mass = new double[nv];
        for (int i = 0; i < nv; i++) mass[i] = 1.0;
        // At the Gerschgorin bound (λ = 1) the stiffest mode sits exactly on
        // the stability limit; the reset to the peak position damps it, a
        // velocity reset alone does not (measured: 35 000 steps against 186 on
        // a 20 × 20 net), so without the peak reset the masses are doubled.
        double lambda = Math.Max(options.MassFactor, 1e-3) * (options.ResetToPeak ? 1.0 : 2.0);

        Vec3d[] Residual()
        {
            var forces = physics.ComputeSpringForces(options.Stiffness, options.RestScale);
            if (options.Gravity.LengthSquared > 0)
                forces = MeshPhysics.CombineForces(forces, gravityForces);
            if (options.AreaTension > 0)
                forces = MeshPhysics.CombineForces(forces, physics.ComputeAreaMinimizationForces(options.AreaTension));
            if (options.Smoothness > 0)
                forces = MeshPhysics.CombineForces(forces, physics.ComputeSmoothnessForces(options.Smoothness));
            if (options.Loads != null)
                forces = MeshPhysics.CombineForces(forces, options.Loads);
            if (options.Bending > 0)
                LathBending.AddForces(x, laths, options.Bending, forces);
            for (int i = 0; i < nv; i++) if (fixedVertices[i]) forces[i] = Vec3d.Zero;
            return forces;
        }

        void UpdateMasses()
        {
            if (!options.FictitiousMasses) return;
            var s = DirectStiffness(physics, options, laths);
            double floor = 0;
            for (int i = 0; i < nv; i++) if (!fixedVertices[i]) floor = Math.Max(floor, s[i]);
            floor = Math.Max(floor * 1e-9, 1e-300);
            for (int i = 0; i < nv; i++)
                mass[i] = lambda * dt * dt * 0.5 * Math.Max(s[i], floor);
        }

        double KineticEnergy()
        {
            double e = 0;
            for (int i = 0; i < nv; i++)
                if (!fixedVertices[i]) e += 0.5 * mass[i] * v[i].LengthSquared;
            return e;
        }

        UpdateMasses();
        double kePrev = 0, kePrev2 = 0; // kinetic energy at t − Δt/2 and t − 3Δt/2
        int sinceReset = 0;
        double residual = double.MaxValue;
        int iter = 0, resets = 0;
        bool converged = false;

        for (iter = 0; iter < options.MaxIterations; iter++)
        {
            if ((iter & 15) == 0 && options.ShouldCancel?.Invoke() == true) break;

            var forces = Residual();
            residual = 0;
            for (int i = 0; i < nv; i++)
                if (!fixedVertices[i]) residual = Math.Max(residual, forces[i].Length);
            if (iter == 0 && residualScale <= 0) residualScale = residual;
            if (residualScale <= 0) { converged = true; break; } // nothing acts on the mesh: already at rest
            if (residual <= options.Tolerance * residualScale) { converged = true; break; }

            // central-difference step; a half step when starting from rest
            double vdt = sinceReset == 0 ? 0.5 * dt : dt;
            for (int i = 0; i < nv; i++)
            {
                if (fixedVertices[i]) continue;
                v[i] = v[i] + (vdt / mass[i]) * forces[i];
                x[i] = x[i] + dt * v[i];
            }
            sinceReset++;

            double ke = KineticEnergy();
            if (sinceReset >= 2 && ke < kePrev)
            {
                resets++;
                if (options.ResetToPeak)
                {
                    // δ: peak time measured from t − Δt/2 (Barnes: 0)
                    double delta = 0;
                    if (options.PeakFit && sinceReset >= 3)
                    {
                        double denom = kePrev2 - 2 * kePrev + ke;
                        if (denom < 0) delta = 0.5 * dt * (kePrev2 - ke) / denom;
                        delta = Math.Max(-0.5 * dt, Math.Min(0.5 * dt, delta));
                    }
                    for (int i = 0; i < nv; i++)
                    {
                        if (fixedVertices[i]) continue;
                        Vec3d a = forces[i] / mass[i];
                        Vec3d vPrev = v[i] - vdt * a;                       // v_{t−Δt/2}
                        Vec3d xPrev = x[i] - dt * v[i] - 0.5 * dt * vPrev;  // x_{t−Δt/2}
                        x[i] = xPrev + delta * vPrev + 0.5 * delta * delta * a;
                    }
                }
                physics.ResetVelocities();
                sinceReset = 0;
                kePrev = kePrev2 = 0;
                UpdateMasses();
            }
            else
            {
                kePrev2 = kePrev;
                kePrev = ke;
            }
        }

        var edgeForces = new double[edges.Length];
        for (int e = 0; e < edges.Length; e++)
        {
            double len = (x[edges[e].v1] - x[edges[e].v0]).Length;
            edgeForces[e] = options.Stiffness * (len - options.RestScale * physics.RestLengths[e]);
        }
        var curvatures = laths.Length > 0 ? LathBending.Curvatures(x, laths) : Array.Empty<double[]>();

        return new Result((Vec3d[])x.Clone(), iter, residual, edgeForces, converged, laths, curvatures, resets);
    }

    /// <summary>
    /// Direct stiffness S_i of every node: Σ over its links of k + |T| / L,
    /// the smoothing strength, the soap-film cotangent weights and the lath
    /// bending bound.
    /// </summary>
    public static double[] DirectStiffness(MeshPhysics physics, Options options, int[][] laths)
    {
        int nv = physics.VertexCount;
        var x = physics.Positions;
        var s = new double[nv];
        var edges = physics.Edges;
        for (int e = 0; e < edges.Length; e++)
        {
            double len = (x[edges[e].v1] - x[edges[e].v0]).Length;
            double t = options.Stiffness * (len - options.RestScale * physics.RestLengths[e]);
            double k = options.Stiffness + (len > 1e-300 ? Math.Abs(t) / len : 0.0);
            s[edges[e].v0] += k;
            s[edges[e].v1] += k;
        }
        if (options.Smoothness > 0)
            for (int i = 0; i < nv; i++) s[i] += options.Smoothness;
        if (options.AreaTension > 0)
        {
            // area Hessian ~ cotangent Laplacian: diagonal ½ Σ (cot α + cot β)
            foreach (var f in physics.TriangleFaces)
            {
                for (int j = 0; j < 3; j++)
                {
                    int ic = f[j], ia = f[(j + 1) % 3], ib = f[(j + 2) % 3];
                    Vec3d e1 = x[ia] - x[ic], e2 = x[ib] - x[ic];
                    double cross = Vec3d.Cross(e1, e2).Length;
                    if (cross < 1e-300) continue;
                    double cot = Math.Abs(Vec3d.Dot(e1, e2)) / cross;
                    double w = 0.5 * options.AreaTension * cot;
                    s[ia] += w; s[ib] += w;
                }
            }
        }
        if (options.Bending > 0)
            LathBending.AddDirectStiffness(x, laths, options.Bending, s);
        return s;
    }
}
