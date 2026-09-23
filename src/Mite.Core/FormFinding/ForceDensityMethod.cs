using System;
using System.Collections.Generic;
using Mite.Core.Numerics;
using Mite.Core.Geometry;

namespace Mite.Core.FormFinding;

public static class ForceDensityMethod
{
    public readonly struct Result
    {
        public readonly Vec3d[] Vertices;

        public Result(Vec3d[] vertices) { Vertices = vertices; }
    }

    /// <summary>
    /// Solves for equilibrium positions using the Force Density Method.
    /// [Cn^T * Q * Cn] * xn = pn - [Cn^T * Q * Cf] * xf
    /// where n = free nodes, f = fixed nodes, Q = diagonal force densities.
    /// Free vertices not connected to any edge with a non-zero force density
    /// are unconstrained; they are held in place (treated as fixed). Throws
    /// when the system has no anchors or the solve is inconsistent, instead of
    /// silently returning a collapsed solution.
    /// </summary>
    public static Result Compute(
        MeshData mesh,
        double[] forceDensities,
        Vec3d[] loads,
        bool[] fixedVertices)
    {
        var edges = mesh.BuildEdges();
        int ne = edges.Length;
        int nv = mesh.VertexCount;

        if (ne == 0)
            throw new ArgumentException("ForceDensityMethod requires a mesh with at least one edge.", nameof(mesh));
        if (fixedVertices == null || fixedVertices.Length != nv)
            throw new ArgumentException("fixedVertices must provide one flag per vertex.", nameof(fixedVertices));

        // Fold unconstrained free vertices into the fixed set
        var effectiveFixed = (bool[])fixedVertices.Clone();
        var hasAnchorEdge = new bool[nv];
        for (int e = 0; e < ne; e++)
        {
            double q = e < forceDensities.Length ? forceDensities[e] : 1.0;
            if (q == 0.0) continue;
            hasAnchorEdge[edges[e].v0] = true;
            hasAnchorEdge[edges[e].v1] = true;
        }
        for (int i = 0; i < nv; i++)
            if (!effectiveFixed[i] && !hasAnchorEdge[i])
                effectiveFixed[i] = true;

        int fixedCount = 0;
        for (int i = 0; i < nv; i++) if (effectiveFixed[i]) fixedCount++;

        if (fixedCount == 0)
            throw new ArgumentException(
                "ForceDensityMethod requires at least one fixed vertex; " +
                "without anchors the equilibrium system is singular.", nameof(fixedVertices));
        if (fixedCount == nv)
        {
            var copy = new Vec3d[nv];
            Array.Copy(mesh.Vertices, copy, nv);
            return new Result(copy);
        }

        var freeIndices = new List<int>();
        var fixedIndices = new List<int>();
        var freeMap = new int[nv];
        var fixedMap = new int[nv];

        for (int i = 0; i < nv; i++)
        {
            if (effectiveFixed[i])
            {
                fixedMap[i] = fixedIndices.Count;
                freeMap[i] = -1;
                fixedIndices.Add(i);
            }
            else
            {
                freeMap[i] = freeIndices.Count;
                fixedMap[i] = -1;
                freeIndices.Add(i);
            }
        }

        int nn = freeIndices.Count;
        int nf = fixedIndices.Count;

        // Assemble D = Cn^T Q Cn (free-free) and the fixed-node contribution
        // Df xf = Cn^T Q Cf xf directly from the edge list
        var D = new SparseSymmetricSolver.Builder(nn);
        var bx = new double[nn];
        var by = new double[nn];
        var bz = new double[nn];

        for (int e = 0; e < ne; e++)
        {
            int v0 = edges[e].v0, v1 = edges[e].v1;
            double q = e < forceDensities.Length ? forceDensities[e] : 1.0;
            if (q == 0.0) continue;
            int f0 = freeMap[v0], f1 = freeMap[v1];

            if (f0 >= 0) D.Add(f0, f0, q);
            if (f1 >= 0) D.Add(f1, f1, q);

            if (f0 >= 0 && f1 >= 0)
            {
                D.Add(f0, f1, -q);
            }
            else if (f0 >= 0)
            {
                Vec3d p = mesh.Vertices[v1];
                bx[f0] += q * p.X; by[f0] += q * p.Y; bz[f0] += q * p.Z;
            }
            else if (f1 >= 0)
            {
                Vec3d p = mesh.Vertices[v0];
                bx[f1] += q * p.X; by[f1] += q * p.Y; bz[f1] += q * p.Z;
            }
        }

        for (int i = 0; i < nn; i++)
        {
            int vi = freeIndices[i];
            if (loads != null && vi < loads.Length)
            {
                bx[i] += loads[vi].X;
                by[i] += loads[vi].Y;
                bz[i] += loads[vi].Z;
            }
        }

        var solver = SparseSymmetricSolver.Factor(D, 1e-12, allowIndefinite: true);
        if (solver == null)
            throw new InvalidOperationException(
                "The force-density system is singular. " +
                "Check that force densities are positive and that every free vertex is anchored through the net.");

        double[] solvedX = solver.Solve(bx);
        double[] solvedY = solver.Solve(by);
        double[] solvedZ = solver.Solve(bz);

        // Verify the solve against the assembled matrix
        double res = Math.Max(Residual(D, solvedX, bx), Math.Max(Residual(D, solvedY, by), Residual(D, solvedZ, bz)));
        double scale = Math.Max(1e-300, D.InfinityNorm() * Math.Max(Math.Max(MaxAbs(solvedX), MaxAbs(solvedY)), MaxAbs(solvedZ))
            + Math.Max(Math.Max(MaxAbs(bx), MaxAbs(by)), MaxAbs(bz)));
        if (double.IsNaN(res) || res > 1e-8 * scale)
            throw new InvalidOperationException(
                $"The force-density system is ill-conditioned (relative residual {res / scale:E2}). " +
                "Check that force densities are not all zero and that every free vertex is anchored through the net.");

        var result = new Vec3d[nv];
        for (int i = 0; i < nn; i++)
            result[freeIndices[i]] = new Vec3d(solvedX[i], solvedY[i], solvedZ[i]);
        for (int i = 0; i < nf; i++)
            result[fixedIndices[i]] = mesh.Vertices[fixedIndices[i]];

        return new Result(result);
    }

    private static double Residual(SparseSymmetricSolver.Builder a, double[] x, double[] b)
    {
        var ax = a.Multiply(x);
        double max = 0;
        for (int i = 0; i < b.Length; i++) max = Math.Max(max, Math.Abs(ax[i] - b[i]));
        return max;
    }

    private static double MaxAbs(double[] v)
    {
        double max = 0;
        foreach (double x in v) max = Math.Max(max, Math.Abs(x));
        return max;
    }
}
