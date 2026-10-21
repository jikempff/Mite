using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Dynamics;

/// <summary>
/// Elastic laths for dynamic relaxation: chains of mesh vertices that resist
/// bending, so a quad mesh relaxes as an elastic gridshell instead of a cable
/// net (Adriaenssens &amp; Barnes 2001, "Tensegrity spline beam and grid shell
/// structures", Eng. Struct. 23; D'Amico, Kermani &amp; Zhang 2014, "Form finding
/// and structural analysis of actively bent timber grid shells", Eng. Struct.
/// 81; Lienhard 2014, "Bending-active structures").
///
/// Curvature at an interior lath node is the spline-beam curvature of
/// Adriaenssens &amp; Barnes: the reciprocal of the radius of the circle through
/// the node and its two neighbours, κ_i = 1 / R_i = 2 sin φ_i / |x_{i+1} − x_{i−1}|
/// with φ_i the turning angle, i.e. κ_i = 2 |e₁ × e₂| / (|e₁| |e₂| |e₁ + e₂|),
/// e₁ = x_i − x_{i−1}, e₂ = x_{i+1} − x_i. The bending moment is M_i = EI κ_i.
/// Instead of converting the moments into shears by statics on each chord,
/// the nodal forces are the exact negative gradient of the discrete bending
/// energy E = Σ_i ½ EI κ_i² ℓ̄_i, ℓ̄_i = ½(|e₁| + |e₂|) (the energy form of the
/// discrete elastic rod, Bergou et al. 2008), so they are self-equilibrated
/// on every lath (Σ F = 0, Σ x × F = 0) by construction and the relaxation
/// minimises Σ E + the spring and load potentials.
///
/// Laths of a quad mesh are the chains that run straight through its
/// vertices: at a vertex the two edges that share no quad continue each
/// other (an interior vertex of valence 4 has two such pairs, a border vertex
/// of valence 3 one, a corner none), so the rows and columns of a quad grid
/// are the laths and the border row is a lath too. Faces that are not quads
/// contribute no laths.
/// </summary>
public static class LathBending
{
    /// <summary>
    /// Vertex chains that continue straight through the quad mesh (see the
    /// class summary). Closed chains (rings) have their first vertex repeated
    /// at the end.
    /// </summary>
    public static int[][] FromQuadMesh(MeshData mesh)
    {
        int nv = mesh.VertexCount;
        // edges at each vertex and the quads each edge belongs to
        var vertexEdges = new List<int>[nv];
        for (int i = 0; i < nv; i++) vertexEdges[i] = new List<int>();
        var edges = mesh.BuildEdges();
        var edgeIndex = new Dictionary<(int, int), int>();
        for (int e = 0; e < edges.Length; e++)
        {
            edgeIndex[edges[e]] = e;
            vertexEdges[edges[e].v0].Add(e);
            vertexEdges[edges[e].v1].Add(e);
        }
        var edgeFaces = new List<int>[edges.Length];
        for (int e = 0; e < edges.Length; e++) edgeFaces[e] = new List<int>();
        var vertexAllQuads = new bool[nv];
        for (int i = 0; i < nv; i++) vertexAllQuads[i] = true;
        for (int f = 0; f < mesh.FaceCount; f++)
        {
            var face = mesh.Faces[f];
            if (face.Length != 4)
            {
                foreach (int v in face) vertexAllQuads[v] = false;
                continue;
            }
            for (int i = 0; i < 4; i++)
            {
                int a = face[i], b = face[(i + 1) % 4];
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                edgeFaces[edgeIndex[key]].Add(f);
            }
        }

        // continuation: for every (vertex, edge) the edge that continues it, or -1
        var next = new Dictionary<(int v, int e), int>();
        for (int v = 0; v < nv; v++)
        {
            if (!vertexAllQuads[v]) continue;
            var ve = vertexEdges[v];
            if (ve.Count != 3 && ve.Count != 4) continue;
            for (int i = 0; i < ve.Count; i++)
            {
                int partner = -1, partners = 0;
                for (int j = 0; j < ve.Count; j++)
                {
                    if (i == j) continue;
                    bool share = false;
                    foreach (int fa in edgeFaces[ve[i]])
                        if (edgeFaces[ve[j]].Contains(fa)) { share = true; break; }
                    if (!share) { partner = ve[j]; partners++; }
                }
                if (partners == 1) next[(v, ve[i])] = partner;
            }
        }

        int Other(int e, int v) => edges[e].v0 == v ? edges[e].v1 : edges[e].v0;

        var used = new bool[edges.Length];
        var laths = new List<int[]>();
        for (int e0 = 0; e0 < edges.Length; e0++)
        {
            if (used[e0]) continue;
            // walk backwards from v0 to find the start of the chain
            int e = e0, v = edges[e0].v0;
            var seen = new HashSet<int> { e0 };
            bool closed = false;
            while (next.TryGetValue((v, e), out int prev))
            {
                if (prev == e0) { closed = true; break; }
                if (!seen.Add(prev)) break;
                v = Other(prev, v);
                e = prev;
            }
            // now (v, e) is the first edge, v its far end; walk forward
            var chain = new List<int>();
            int start = closed ? edges[e0].v0 : v;
            int cur = closed ? e0 : e;
            int at = start;
            chain.Add(at);
            while (true)
            {
                used[cur] = true;
                at = Other(cur, at);
                chain.Add(at);
                if (!next.TryGetValue((at, cur), out int nx) || used[nx]) break;
                cur = nx;
            }
            if (chain.Count >= 3) laths.Add(chain.ToArray()); // a single edge bends nothing
        }
        return laths.ToArray();
    }

    /// <summary>Spline-beam curvature 1 / R of the circle through three points (0 when collinear).</summary>
    public static double Curvature(Vec3d a, Vec3d b, Vec3d c)
    {
        Vec3d e1 = b - a, e2 = c - b;
        double g = e1.Length * e2.Length * (e1 + e2).Length;
        return g < 1e-300 ? 0.0 : 2.0 * Vec3d.Cross(e1, e2).Length / g;
    }

    /// <summary>Curvature at every node of every lath (0 at open ends).</summary>
    public static double[][] Curvatures(Vec3d[] x, int[][] laths)
    {
        var result = new double[laths.Length][];
        for (int l = 0; l < laths.Length; l++)
        {
            var lath = laths[l];
            bool closed = lath.Length > 3 && lath[0] == lath[lath.Length - 1];
            int n = closed ? lath.Length - 1 : lath.Length;
            var k = new double[n];
            for (int i = 0; i < n; i++)
            {
                int ip = i - 1, inx = i + 1;
                if (closed) { ip = (ip + n) % n; inx %= n; }
                else if (ip < 0 || inx >= n) continue;
                k[i] = Curvature(x[lath[ip]], x[lath[i]], x[lath[inx]]);
            }
            result[l] = k;
        }
        return result;
    }

    /// <summary>Discrete bending energy Σ ½ EI κ_i² ℓ̄_i over all interior lath nodes.</summary>
    public static double Energy(Vec3d[] x, int[][] laths, double stiffness)
    {
        double energy = 0;
        ForEachNode(laths, (a, b, c) =>
        {
            Vec3d e1 = x[b] - x[a], e2 = x[c] - x[b];
            double k = Curvature(x[a], x[b], x[c]);
            energy += 0.5 * stiffness * k * k * 0.5 * (e1.Length + e2.Length);
        });
        return energy;
    }

    /// <summary>
    /// Adds the bending forces −∂E/∂x to <paramref name="forces"/>: the exact
    /// gradient of E = Σ ½ EI κ² ℓ̄ with κ = 2 f / g, f = |e₁ × e₂|,
    /// g = |e₁| |e₂| |e₁ + e₂|, ℓ̄ = ½(|e₁| + |e₂|).
    /// </summary>
    public static void AddForces(Vec3d[] x, int[][] laths, double stiffness, Vec3d[] forces)
    {
        if (stiffness <= 0) return;
        ForEachNode(laths, (ia, ib, ic) =>
        {
            Vec3d e1 = x[ib] - x[ia], e2 = x[ic] - x[ib];
            double l1 = e1.Length, l2 = e2.Length;
            if (l1 < 1e-300 || l2 < 1e-300) return;
            Vec3d c = e1 + e2;
            double lc = c.Length;
            if (lc < 1e-300) return;
            Vec3d n = Vec3d.Cross(e1, e2);
            double f = n.Length;
            if (f < 1e-300) return; // straight: E ∝ f², gradient vanishes
            double g = l1 * l2 * lc;
            double lbar = 0.5 * (l1 + l2);
            // E = 2 EI f² ℓ̄ / g²
            Vec3d nh = n / f, u1 = e1 / l1, u2 = e2 / l2, uc = c / lc;
            // ∂f/∂e₁ = e₂ × n̂, ∂f/∂e₂ = n̂ × e₁
            Vec3d df1 = Vec3d.Cross(e2, nh), df2 = Vec3d.Cross(nh, e1);
            // ∂g/∂e₁ = û₁ l₂ l_c + l₁ l₂ û_c ; ∂g/∂e₂ = l₁ û₂ l_c + l₁ l₂ û_c
            Vec3d dg1 = (l2 * lc) * u1 + (l1 * l2) * uc, dg2 = (l1 * lc) * u2 + (l1 * l2) * uc;
            Vec3d dl1 = 0.5 * u1, dl2 = 0.5 * u2;
            double g2 = g * g;
            double cf = 2 * stiffness * 2 * f * lbar / g2;      // ∂E/∂f
            double cg = -2 * stiffness * 2 * f * f * lbar / (g2 * g); // ∂E/∂g
            double cl = 2 * stiffness * f * f / g2;              // ∂E/∂ℓ̄
            Vec3d dE1 = cf * df1 + cg * dg1 + cl * dl1;
            Vec3d dE2 = cf * df2 + cg * dg2 + cl * dl2;
            // chain rule to the three nodes
            forces[ia] = forces[ia] + dE1;             // −(−dE1)
            forces[ib] = forces[ib] - dE1 + dE2;       // −(dE1 − dE2)
            forces[ic] = forces[ic] - dE2;             // −(dE2)
        });
    }

    /// <summary>
    /// Direct bending stiffness per vertex for the fictitious masses of
    /// dynamic relaxation: the transverse stiffness of a beam node against
    /// its neighbours is of the order 6 EI / ℓ³ on the diagonal with a
    /// Gerschgorin row sum of 16 EI / ℓ³ (central-difference beam operator
    /// EI/ℓ³ [1 −4 6 −4 1]); 8 EI / ℓ³ per lath with the shortest segment
    /// within two of the node is the bound used here.
    /// </summary>
    public static void AddDirectStiffness(Vec3d[] x, int[][] laths, double stiffness, double[] s)
    {
        if (stiffness <= 0) return;
        foreach (var lath in laths)
        {
            int m = lath.Length;
            var len = new double[m - 1];
            for (int i = 0; i < m - 1; i++) len[i] = Math.Max((x[lath[i + 1]] - x[lath[i]]).Length, 1e-12);
            bool closed = m > 3 && lath[0] == lath[m - 1];
            int n = closed ? m - 1 : m;
            for (int i = 0; i < n; i++)
            {
                double lmin = double.MaxValue;
                for (int d = -2; d < 2; d++)
                {
                    int seg = i + d;
                    if (closed) seg = ((seg % (m - 1)) + (m - 1)) % (m - 1);
                    else if (seg < 0 || seg >= m - 1) continue;
                    lmin = Math.Min(lmin, len[seg]);
                }
                if (lmin == double.MaxValue) continue;
                s[lath[i]] += 8.0 * stiffness / (lmin * lmin * lmin);
            }
        }
    }

    private static void ForEachNode(int[][] laths, Action<int, int, int> body)
    {
        foreach (var lath in laths)
        {
            int m = lath.Length;
            bool closed = m > 3 && lath[0] == lath[m - 1];
            if (closed)
            {
                int n = m - 1;
                for (int i = 0; i < n; i++) body(lath[(i + n - 1) % n], lath[i], lath[(i + 1) % n]);
            }
            else
            {
                for (int i = 1; i < m - 1; i++) body(lath[i - 1], lath[i], lath[i + 1]);
            }
        }
    }
}
