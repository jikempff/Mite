using System;
using Mite.Core.Geometry;

namespace Mite.Core.Dynamics;

/// <summary>
/// Dynamic relaxation form-finding (Day 1965, Barnes 1999): the mesh is
/// treated as a particle-spring system, integrated explicitly with kinetic
/// damping — whenever the total kinetic energy peaks, velocities are reset to
/// zero — so the system settles into static equilibrium instead of
/// oscillating. Springs, gravity / point loads, soap-film tension and
/// Laplacian smoothing can be mixed, which covers hanging models, tensile
/// membranes and quick minimal-surface-like relaxations of a gridshell mesh.
/// </summary>
public static class DynamicRelaxation
{
    public class Options
    {
        /// <summary>Edge spring stiffness (force per unit stretch).</summary>
        public double Stiffness { get; set; } = 1.0;

        /// <summary>Rest-length factor: 1 keeps edge lengths, &lt; 1 pre-tensions the net.</summary>
        public double RestScale { get; set; } = 1.0;

        /// <summary>Uniform acceleration applied to every free vertex (e.g. (0,0,-9.81) for gravity).</summary>
        public Vec3d Gravity { get; set; } = Vec3d.Zero;

        /// <summary>Soap-film tension (area minimization strength, 0 disables).</summary>
        public double AreaTension { get; set; } = 0.0;

        /// <summary>Laplacian smoothing strength (0 disables).</summary>
        public double Smoothness { get; set; } = 0.0;

        /// <summary>Optional per-vertex point loads (force).</summary>
        public Vec3d[]? Loads { get; set; }

        /// <summary>Maximum integration steps.</summary>
        public int MaxIterations { get; set; } = 2000;

        /// <summary>
        /// Time step (0 = automatic, from the stiffness and mass so the
        /// explicit integration is stable: dt = 0.5 * sqrt(m / k_eff)).
        /// </summary>
        public double TimeStep { get; set; } = 0.0;

        /// <summary>Stops when the largest residual force on a free vertex falls below this fraction of the largest applied force.</summary>
        public double Tolerance { get; set; } = 1e-4;

        /// <summary>Optional cancellation probe checked every few iterations.</summary>
        public Func<bool>? ShouldCancel { get; set; }
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

        public Result(Vec3d[] vertices, int iterations, double residual, double[] edgeForces, bool converged)
        {
            Vertices = vertices;
            Iterations = iterations;
            Residual = residual;
            EdgeForces = edgeForces;
            Converged = converged;
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

        // Effective stiffness per vertex bounds the stable explicit step
        var valence = new int[nv];
        foreach (var e in edges) { valence[e.v0]++; valence[e.v1]++; }
        int maxValence = 1;
        foreach (int v in valence) maxValence = Math.Max(maxValence, v);
        double kEff = Math.Max(options.Stiffness, 1e-12) * maxValence
                    + Math.Max(options.Smoothness, 0) + Math.Max(options.AreaTension, 0) * maxValence;
        double dt = options.TimeStep > 0 ? options.TimeStep : 0.5 * Math.Sqrt(1.0 / kEff);

        double appliedScale = 0;
        var gravityForces = physics.ComputeGravityForces(options.Gravity);
        for (int i = 0; i < nv; i++)
        {
            double f = gravityForces[i].Length;
            if (options.Loads != null && i < options.Loads.Length) f += options.Loads[i].Length;
            appliedScale = Math.Max(appliedScale, f);
        }
        // With no applied loads (pure spring / soap-film relaxation) judge
        // convergence against the initial internal force scale instead
        double residualScale = appliedScale;

        double prevKinetic = 0;
        double residual = double.MaxValue;
        int iter = 0;
        bool converged = false;

        for (iter = 0; iter < options.MaxIterations; iter++)
        {
            if ((iter & 15) == 0 && options.ShouldCancel?.Invoke() == true) break;

            var forces = physics.ComputeSpringForces(options.Stiffness, options.RestScale);
            if (options.Gravity.LengthSquared > 0)
                forces = MeshPhysics.CombineForces(forces, gravityForces);
            if (options.AreaTension > 0)
                forces = MeshPhysics.CombineForces(forces, physics.ComputeAreaMinimizationForces(options.AreaTension));
            if (options.Smoothness > 0)
                forces = MeshPhysics.CombineForces(forces, physics.ComputeSmoothnessForces(options.Smoothness));
            if (options.Loads != null)
                forces = MeshPhysics.CombineForces(forces, options.Loads);

            residual = 0;
            for (int i = 0; i < nv; i++)
                if (!fixedVertices[i]) residual = Math.Max(residual, forces[i].Length);
            if (iter == 0 && residualScale <= 0) residualScale = residual;
            if (residualScale <= 0) { converged = true; break; } // nothing acts on the mesh: already at rest
            if (residual <= options.Tolerance * residualScale) { converged = true; break; }

            physics.Step(dt, forces);

            // Kinetic damping: when the kinetic energy starts to fall the
            // system just passed through an equilibrium configuration
            double kinetic = physics.KineticEnergy();
            if (kinetic < prevKinetic)
            {
                physics.ResetVelocities();
                prevKinetic = 0;
            }
            else prevKinetic = kinetic;
        }

        var edgeForces = new double[edges.Length];
        for (int e = 0; e < edges.Length; e++)
        {
            double len = (physics.Positions[edges[e].v1] - physics.Positions[edges[e].v0]).Length;
            edgeForces[e] = options.Stiffness * (len - options.RestScale * physics.RestLengths[e]);
        }

        return new Result((Vec3d[])physics.Positions.Clone(), iter, residual, edgeForces, converged);
    }
}
