using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
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

        /// <summary>Convergence tolerance on the maximum vertex movement per outer iteration.</summary>
        public double Tolerance { get; set; } = 1e-8;
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
        var edgeIndex = new Dictionary<(int, int), int>(edges.Length);
        for (int e = 0; e < edges.Length; e++)
            edgeIndex[edges[e]] = e;

        double residual = double.MaxValue;
        int iter = 0;

        for (iter = 0; iter < options.MaxIterations; iter++)
        {
            // Cotangent weights from the current geometry: w_ab += cot(angle
            // opposite edge ab), accumulated per face corner
            var wEdge = new double[edges.Length];
            foreach (var f in triMesh.Faces)
            {
                for (int j = 0; j < 3; j++)
                {
                    // Corner at f[j]; the edge opposite it is (f[j+1], f[j+2])
                    int ia = f[(j + 1) % 3], ib = f[(j + 2) % 3], ic = f[j];

                    Vec3d e1 = verts[ia] - verts[ic];
                    Vec3d e2 = verts[ib] - verts[ic];
                    double l1 = e1.Length, l2 = e2.Length;
                    if (l1 < 1e-15 || l2 < 1e-15) continue;

                    double cosA = Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(e1, e2) / (l1 * l2)));
                    double sinA = Math.Sqrt(Math.Max(0.0, 1.0 - cosA * cosA));
                    if (sinA < 1e-15) continue;

                    wEdge[edgeIndex[ia < ib ? (ia, ib) : (ib, ia)]] += cosA / sinA;
                }
            }

            // Clamp negative weights (obtuse opposite angles) to a small
            // positive floor. Negative weights make the frozen system
            // indefinite: the exact solve loses the maximum principle and the
            // outer iteration wanders instead of converging. Cotangents are
            // dimensionless and O(1), so an absolute floor is safe; the clamp
            // only touches edges whose weight was already meaningless.
            var A = Matrix<double>.Build.Sparse(nn, nn);
            var bx = new double[nn];
            var by = new double[nn];
            var bz = new double[nn];

            for (int e = 0; e < edges.Length; e++)
            {
                double w = Math.Max(wEdge[e], 1e-4);
                Accumulate(edges[e].v0, edges[e].v1, w, verts, freeMap, fixedMap, A, bx, by, bz);
                Accumulate(edges[e].v1, edges[e].v0, w, verts, freeMap, fixedMap, A, bx, by, bz);
            }

            var rhsX = Vector<double>.Build.Dense(bx);
            var rhsY = Vector<double>.Build.Dense(by);
            var rhsZ = Vector<double>.Build.Dense(bz);

            Vector<double> sx, sy, sz;
            try
            {
                sx = A.Solve(rhsX);
                sy = A.Solve(rhsY);
                sz = A.Solve(rhsZ);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The cotangent Laplace system could not be solved. " +
                    "Check the mesh for degenerate faces or disconnected patches.", ex);
            }

            residual = 0;
            for (int i = 0; i < nv; i++)
            {
                int fi = freeMap[i];
                if (fi < 0) continue;

                var next = new Vec3d(sx[fi], sy[fi], sz[fi]);
                if (double.IsNaN(next.X) || double.IsNaN(next.Y) || double.IsNaN(next.Z))
                    throw new InvalidOperationException(
                        "The cotangent Laplace solve returned NaN. " +
                        "Check the mesh for degenerate faces or disconnected patches.");

                residual = Math.Max(residual, (next - verts[i]).Length);
                verts[i] = next;
            }

            if (residual < options.Tolerance) { iter++; break; }
        }

        return new Result(verts, iter, residual);
    }

    /// <summary>
    /// Adds one directed cotangent contribution: for free vertex a, the term
    /// w * (x_b - x_a) of the Laplace stencil becomes diagonal +w, off-diagonal
    /// -w (free b) or right-hand side +w * x_b (fixed b).
    /// </summary>
    private static void Accumulate(
        int a, int b, double w, Vec3d[] verts,
        int[] freeMap, int[] fixedMap,
        Matrix<double> A, double[] bx, double[] by, double[] bz)
    {
        int fa = freeMap[a];
        if (fa < 0) return;

        A[fa, fa] += w;
        int fb = freeMap[b];
        if (fb >= 0)
        {
            A[fa, fb] -= w;
        }
        else
        {
            bx[fa] += w * verts[b].X;
            by[fa] += w * verts[b].Y;
            bz[fa] += w * verts[b].Z;
        }
    }
}
