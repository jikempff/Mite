using System;
using System.Collections.Generic;
using Mite.Core.Numerics;
using Mite.Core.Geometry;

namespace Mite.Core.FormFinding;

public static class MinimalSurface
{
    public class Options
    {
        /// <summary>
        /// Outer iterations. Each one freezes the cotangent weights at the
        /// current geometry and solves the resulting linear Laplace system
        /// exactly, so a handful of weight updates suffice (the first solve
        /// already gives the exact harmonic map for the initial weights).
        /// </summary>
        public int MaxIterations { get; set; } = 20;

        /// <summary>
        /// Convergence tolerance on the maximum vertex movement per outer
        /// iteration, as a fraction of the bounding-box diagonal.
        /// </summary>
        public double Tolerance { get; set; } = 1e-8;

        /// <summary>Optional cancellation probe checked between outer iterations.</summary>
        public Func<bool>? ShouldCancel { get; set; }
    }

    public readonly struct Result
    {
        public readonly Vec3d[] Vertices;
        public readonly int Iterations;

        /// <summary>Maximum vertex movement in the final outer iteration.</summary>
        public readonly double Residual;

        public Result(Vec3d[] vertices, int iterations, double residual)
        {
            Vertices = vertices; Iterations = iterations; Residual = residual;
        }
    }

    /// <summary>
    /// Finds a minimal surface with fixed boundary vertices by iterating the
    /// cotangent-weighted Laplace solve: freeze weights at the current
    /// geometry, solve the linear system exactly, update the weights. This
    /// replaces explicit Laplacian flow, which needs thousands of Euler steps
    /// and still does not converge to tolerance.
    /// </summary>
    public static Result Compute(MeshData mesh, bool[] fixedVertices, Options? options = null)
    {
        options ??= new Options();
        if (fixedVertices == null || fixedVertices.Length != mesh.VertexCount)
            throw new ArgumentException("fixedVertices must provide one flag per vertex.", nameof(fixedVertices));

        var triMesh = mesh.ToTriangulated();
        int nv = triMesh.VertexCount;

        var verts = new Vec3d[nv];
        Array.Copy(triMesh.Vertices, verts, nv);

        // Vertices with no incident face have a zero Laplacian stencil: hold
        // them in place instead of feeding the solver a singular row
        var hold = (bool[])fixedVertices.Clone();
        var hasFace = new bool[nv];
        foreach (var f in triMesh.Faces)
            foreach (int vi in f)
                hasFace[vi] = true;
        for (int i = 0; i < nv; i++)
            if (!hasFace[i]) hold[i] = true;

        int fixedCount = 0;
        for (int i = 0; i < nv; i++) if (hold[i]) fixedCount++;

        if (fixedCount == 0)
            throw new ArgumentException(
                "At least one vertex must be fixed (typically the boundary); " +
                "without constraints the minimal surface degenerates to a point.",
                nameof(fixedVertices));
        if (fixedCount == nv)
            return new Result(verts, 0, 0.0);

        var freeMap = new int[nv];
        var fixedMap = new int[nv];
        int nn = 0, nf = 0;
        for (int i = 0; i < nv; i++)
        {
            if (hold[i]) { fixedMap[i] = nf++; freeMap[i] = -1; }
            else { freeMap[i] = nn++; fixedMap[i] = -1; }
        }

        var edges = triMesh.BuildEdges();

        double residual = double.MaxValue;
        int iter = 0;

        // Tolerance is relative to the model size so millimetre and metre
        // models converge to the same visual accuracy
        double tolerance = options.Tolerance * Math.Max(triMesh.BoundingBoxDiagonal(), 1e-300);

        for (iter = 0; iter < options.MaxIterations; iter++)
        {
            if (options.ShouldCancel?.Invoke() == true) break;

            // Cotangent weights from the current geometry. Negative weights
            // (obtuse opposite angles) are clamped to a small positive floor:
            // they make the frozen system indefinite, and the exact solve then
            // loses the maximum principle and wanders instead of converging.
            var current = new MeshData(verts, triMesh.Faces);
            var wEdge = DiscreteOperators.CotangentEdgeWeights(current, edges, 1e-4);

            var A = new SparseSymmetricSolver.Builder(nn);
            var bx = new double[nn];
            var by = new double[nn];
            var bz = new double[nn];

            for (int e = 0; e < edges.Length; e++)
            {
                double w = wEdge[e];
                Accumulate(edges[e].v0, edges[e].v1, w, verts, freeMap, A, bx, by, bz);
                Accumulate(edges[e].v1, edges[e].v0, w, verts, freeMap, A, bx, by, bz);
            }

            var solver = SparseSymmetricSolver.Factor(A);
            if (solver == null)
                throw new InvalidOperationException(
                    "The cotangent Laplace system is singular. " +
                    "Check the mesh for degenerate faces or patches not connected to any fixed vertex.");

            double[] sx = solver.Solve(bx);
            double[] sy = solver.Solve(by);
            double[] sz = solver.Solve(bz);

            residual = 0;
            for (int i = 0; i < nv; i++)
            {
                int fi = freeMap[i];
                if (fi < 0) continue;

                var next = new Vec3d(sx[fi], sy[fi], sz[fi]);
                if (!IsFinite(next))
                    throw new InvalidOperationException(
                        "The cotangent Laplace solve returned a non-finite value. " +
                        "Check the mesh for degenerate faces or disconnected patches.");

                residual = Math.Max(residual, (next - verts[i]).Length);
                verts[i] = next;
            }

            if (residual < tolerance) { iter++; break; }
        }

        return new Result(verts, iter, residual);
    }

    /// <summary>
    /// Adds one directed cotangent contribution: for free vertex a, the term
    /// w * (x_b - x_a) of the Laplace stencil becomes diagonal +w, off-diagonal
    /// -w (free b) or right-hand side +w * x_b (fixed b).
    /// </summary>
    private static void Accumulate(
        int a, int b, double w, Vec3d[] verts, int[] freeMap,
        SparseSymmetricSolver.Builder A, double[] bx, double[] by, double[] bz)
    {
        int fa = freeMap[a];
        if (fa < 0) return;

        A.Add(fa, fa, w);
        int fb = freeMap[b];
        if (fb >= 0)
        {
            if (fa <= fb) A.Add(fa, fb, -w); // the symmetric mirror is added by the (b, a) call
        }
        else
        {
            bx[fa] += w * verts[b].X;
            by[fa] += w * verts[b].Y;
            bz[fa] += w * verts[b].Z;
        }
    }

    private static bool IsFinite(Vec3d v) =>
        !(double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z) ||
          double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z));
}
