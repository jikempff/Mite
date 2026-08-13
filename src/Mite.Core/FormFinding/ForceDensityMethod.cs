using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
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

        var Cn = Matrix<double>.Build.Sparse(ne, nn);
        var Cf = Matrix<double>.Build.Sparse(ne, nf);
        var Q = Matrix<double>.Build.SparseDiagonal(ne, ne, 0);

        for (int e = 0; e < ne; e++)
        {
            int v0 = edges[e].v0, v1 = edges[e].v1;
            double q = e < forceDensities.Length ? forceDensities[e] : 1.0;
            Q[e, e] = q;

            if (freeMap[v0] >= 0) Cn[e, freeMap[v0]] = 1;
            else Cf[e, fixedMap[v0]] = 1;

            if (freeMap[v1] >= 0) Cn[e, freeMap[v1]] = -1;
            else Cf[e, fixedMap[v1]] = -1;
        }

        var CtQCn = Cn.Transpose() * Q * Cn;
        var CtQCf = Cn.Transpose() * Q * Cf;

        var xf = Vector<double>.Build.Dense(nf);
        var yf = Vector<double>.Build.Dense(nf);
        var zf = Vector<double>.Build.Dense(nf);
        for (int i = 0; i < nf; i++)
        {
            int vi = fixedIndices[i];
            xf[i] = mesh.Vertices[vi].X;
            yf[i] = mesh.Vertices[vi].Y;
            zf[i] = mesh.Vertices[vi].Z;
        }

        var px = Vector<double>.Build.Dense(nn);
        var py = Vector<double>.Build.Dense(nn);
        var pz = Vector<double>.Build.Dense(nn);
        for (int i = 0; i < nn; i++)
        {
            int vi = freeIndices[i];
            if (loads != null && vi < loads.Length)
            {
                px[i] = loads[vi].X;
                py[i] = loads[vi].Y;
                pz[i] = loads[vi].Z;
            }
        }

        var rhsX = px - CtQCf * xf;
        var rhsY = py - CtQCf * yf;
        var rhsZ = pz - CtQCf * zf;

        var solvedX = CtQCn.Solve(rhsX);
        var solvedY = CtQCn.Solve(rhsY);
        var solvedZ = CtQCn.Solve(rhsZ);

        // Verify the solve: MathNet's sparse factorization does not always
        // signal a singular system and can return a collapsed (e.g. all-zero)
        // solution without complaint
        double res = Math.Max(
            (CtQCn * solvedX - rhsX).InfinityNorm(),
            Math.Max((CtQCn * solvedY - rhsY).InfinityNorm(), (CtQCn * solvedZ - rhsZ).InfinityNorm()));
        double rhsScale = Math.Max(1.0, Math.Max(rhsX.InfinityNorm(),
            Math.Max(rhsY.InfinityNorm(), rhsZ.InfinityNorm())));
        if (double.IsNaN(res) || res > 1e-6 * rhsScale)
            throw new InvalidOperationException(
                $"The force-density system is singular or ill-conditioned (relative residual {res / rhsScale:E2}). " +
                "Check that force densities are not all zero and that every free vertex is anchored through the net.");

        var result = new Vec3d[nv];
        for (int i = 0; i < nn; i++)
            result[freeIndices[i]] = new Vec3d(solvedX[i], solvedY[i], solvedZ[i]);
        for (int i = 0; i < nf; i++)
            result[fixedIndices[i]] = mesh.Vertices[fixedIndices[i]];

        return new Result(result);
    }
}
