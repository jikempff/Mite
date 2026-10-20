using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Geometry;

namespace Mite.Core.Curvature;

/// <summary>
/// Umbilics: points where the two principal curvatures are equal and the
/// principal directions are therefore undefined. Direction fields rotate
/// around umbilics, which is what makes traced curve networks chaotic there —
/// flagging them lets users place net seeds deliberately or mask the region.
///
/// Two readings are offered. <see cref="Find"/> flags the *vertices* whose
/// curvatures are nearly equal (a region, thresholded). <see cref="Locate"/>
/// finds the *isolated* umbilics as the degenerate points of the principal
/// direction field, following the tensor-field topology of Delmarcelle &amp;
/// Hesselink 1994 (The topology of symmetric, second-order tensor fields,
/// IEEE Visualization) and Tricoche, Scheuermann &amp; Hagen 2001 (Topology-based
/// visualization of 2D symmetric tensor fields): the shape operator is split
/// into its isotropic part and its deviator (α, β) = (½(S₁₁ − S₂₂), S₁₂),
/// which vanishes exactly at an umbilic; the deviator is interpolated
/// linearly over every triangle (the three corner tensors transported into the
/// face plane), so a triangle holds at most one degenerate point, where α = β
/// = 0; its type follows from δ = ∂α/∂x ∂β/∂y − ∂α/∂y ∂β/∂x: δ &gt; 0 is a wedge
/// (Darboux's lemon or monstar, index +½), δ &lt; 0 a trisector (star, index
/// −½). Because the deviator is a doubled-angle vector of the principal
/// direction, the winding of (α, β) round the triangle is twice the index of
/// the line field, so the sum of the indices over a closed surface is its
/// Euler characteristic (Hopf 1926; Porteous 2001 §11): a surface of sphere
/// type carries index 2, a triaxial ellipsoid exactly four lemons.
/// </summary>
public static class Umbilics
{
    /// <summary>
    /// Returns the indices of umbilical vertices: those where
    /// |k1 - k2| &lt;= tolerance * max(|k1|, |k2|). Vertices with negligible
    /// curvature (flat) are not reported — the directions are meaningless but
    /// harmless there.
    /// </summary>
    public static int[] Find(PrincipalCurvature.Result curvature, double tolerance = 0.05)
    {
        var result = new List<int>();

        // Flatness is judged relative to the mesh's curvature range so the
        // test does not depend on model units
        double maxK = 0;
        for (int i = 0; i < curvature.K1.Length; i++)
            maxK = Math.Max(maxK, Math.Max(Math.Abs(curvature.K1[i]), Math.Abs(curvature.K2[i])));
        double flat = 1e-3 * maxK;

        for (int i = 0; i < curvature.K1.Length; i++)
        {
            double scale = Math.Max(Math.Abs(curvature.K1[i]), Math.Abs(curvature.K2[i]));
            if (scale <= flat) continue; // flat, not umbilical in any meaningful sense
            if (Math.Abs(curvature.K1[i] - curvature.K2[i]) <= tolerance * scale)
                result.Add(i);
        }
        return result.ToArray();
    }

    /// <summary>An isolated umbilic: a degenerate point of the principal direction field.</summary>
    public readonly struct Degenerate
    {
        /// <summary>Position on the mesh (inside a triangle of the triangulated mesh).</summary>
        public readonly Vec3d Point;
        /// <summary>Face of the triangulated mesh that holds it.</summary>
        public readonly int Face;
        /// <summary>Index of the principal direction field round the face: +0.5 (wedge: lemon or monstar), −0.5 (trisector: star); ±1 for a non-generic umbilic resolved inside one face.</summary>
        public readonly double Index;
        /// <summary>Delmarcelle's δ = det ∂(α, β)/∂(x, y) of the linear deviator over the face (δ &gt; 0 wedge, δ &lt; 0 trisector).</summary>
        public readonly double Delta;
        /// <summary>Largest |k1 − k2| at the corners of the face: how sharply the field is defined round the point.</summary>
        public readonly double Contrast;

        public Degenerate(Vec3d point, int face, double index, double delta, double contrast)
        {
            Point = point; Face = face; Index = index; Delta = delta; Contrast = contrast;
        }
    }

    /// <summary>
    /// Locates the isolated umbilics of the principal direction field as the
    /// faces round which the field winds: the index of a face is the sum of
    /// the parallel-transported angle changes of the direction along its three
    /// edges plus the holonomy of the transport round the face, over 2π (Ray,
    /// Vallet, Li &amp; Lévy 2008, N-symmetry direction field design; Crane,
    /// Desbrun &amp; Schröder 2010, Trivial connections — the N = 2 case), which is
    /// exactly a multiple of ½, and the indices sum to the Euler characteristic
    /// of a closed mesh. The point inside the face is the zero of the linearly
    /// interpolated deviator (Delmarcelle &amp; Hesselink 1994) when it lies
    /// inside, else the centroid. <paramref name="cancelDistance"/> &gt; 0 removes
    /// pairs of opposite index closer than that (noise in a nearly umbilical
    /// region appears as such pairs: Tricoche, Scheuermann &amp; Hagen 2001,
    /// topology simplification); 0 keeps every point.
    /// </summary>
    public static Degenerate[] Locate(MeshData mesh, PrincipalCurvature.Result curvature, double cancelDistance = 0)
    {
        var tri = mesh.ToTriangulated();
        int nv = tri.VertexCount;
        if (curvature.K1.Length != nv) throw new ArgumentException("curvature must be per vertex of the mesh", nameof(curvature));

        // vertex frames (e1, e2, n) and the direction angle of D1 in them (mod π)
        var e1 = new Vec3d[nv]; var e2 = new Vec3d[nv]; var theta = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            Vec3d n = curvature.Normals[i];
            if (n.LengthSquared < 1e-20) n = new Vec3d(0, 0, 1);
            n = n.Normalized();
            Vec3d up = Math.Abs(n.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
            e1[i] = Vec3d.Cross(n, up).Normalized();
            e2[i] = Vec3d.Cross(n, e1[i]).Normalized();
            Vec3d d = curvature.D1[i];
            theta[i] = Math.Atan2(Vec3d.Dot(d, e2[i]), Vec3d.Dot(d, e1[i]));
        }

        // world-frame shape operators for the deviator zero inside a face
        var S = new Matrix3d[nv];
        for (int i = 0; i < nv; i++)
            S[i] = curvature.K1[i] * Matrix3d.OuterProduct(curvature.D1[i], curvature.D1[i])
                 + curvature.K2[i] * Matrix3d.OuterProduct(curvature.D2[i], curvature.D2[i]);

        // connection angle across a directed edge: the angle of e1_i transported
        // (minimal rotation n_i → n_j) in the frame of j, and the wrapped change
        // of the line direction along the edge
        double Omega(int i, int j)
        {
            Rotate(curvature.Normals[i].Normalized(), curvature.Normals[j].Normalized(), e1[i], out Vec3d r);
            return Math.Atan2(Vec3d.Dot(r, e2[j]), Vec3d.Dot(r, e1[j]));
        }
        static double WrapPi(double a) { a = Math.IEEERemainder(a, Math.PI); if (a <= -Math.PI / 2) a += Math.PI; if (a > Math.PI / 2) a -= Math.PI; return a; }
        static double Wrap2Pi(double a) { a = Math.IEEERemainder(a, 2 * Math.PI); if (a <= -Math.PI) a += 2 * Math.PI; if (a > Math.PI) a -= 2 * Math.PI; return a; }

        var found = new List<Degenerate>();
        var alpha = new double[3]; var beta = new double[3];
        for (int fi = 0; fi < tri.FaceCount; fi++)
        {
            var f = tri.Faces[fi];
            Vec3d p0 = tri.Vertices[f[0]], p1 = tri.Vertices[f[1]], p2 = tri.Vertices[f[2]];
            Vec3d nf = Vec3d.Cross(p1 - p0, p2 - p0);
            double twoA = nf.Length;
            if (twoA < 1e-30) continue;
            nf = nf / twoA;

            // winding of the line field round the face
            double holonomy = 0, turning = 0;
            for (int j = 0; j < 3; j++)
            {
                int a = f[j], b2 = f[(j + 1) % 3];
                double w = Omega(a, b2);
                holonomy += w;
                turning += WrapPi(theta[b2] - theta[a] - w);
            }
            double index = (turning + Wrap2Pi(holonomy)) / (2 * Math.PI);
            index = Math.Round(2 * index) / 2;
            if (index == 0) continue;

            // deviator at the corners in the face frame (transported) and its linear zero
            Vec3d t = (p1 - p0).Normalized();
            Vec3d b = Vec3d.Cross(nf, t).Normalized();
            double contrast = 0;
            for (int j = 0; j < 3; j++)
            {
                int v = f[j];
                Vec3d n = curvature.Normals[v].Normalized();
                Rotate(nf, n, t, out Vec3d tv); Rotate(nf, n, b, out Vec3d bv);
                Vec3d St = S[v] * tv, Sb = S[v] * bv;
                alpha[j] = 0.5 * (Vec3d.Dot(tv, St) - Vec3d.Dot(bv, Sb));
                beta[j] = Vec3d.Dot(tv, Sb);
                contrast = Math.Max(contrast, Math.Abs(curvature.K1[v] - curvature.K2[v]));
            }
            double a11 = alpha[1] - alpha[0], a12 = alpha[2] - alpha[0];
            double a21 = beta[1] - beta[0], a22 = beta[2] - beta[0];
            double det = a11 * a22 - a12 * a21;
            double scale = Math.Max(Math.Max(Math.Abs(a11), Math.Abs(a12)), Math.Max(Math.Abs(a21), Math.Abs(a22)));
            Vec3d q = (p0 + p1 + p2) / 3.0;
            double delta = 0;
            if (scale > 0 && Math.Abs(det) > 1e-12 * scale * scale)
            {
                double l1 = (-alpha[0] * a22 + a12 * beta[0]) / det;
                double l2 = (-a11 * beta[0] + a21 * alpha[0]) / det;
                double l0 = 1 - l1 - l2;
                // clamp a zero that the discontinuous per-face interpolant puts just outside
                l0 = Math.Max(0, l0); l1 = Math.Max(0, l1); l2 = Math.Max(0, l2);
                double sum = l0 + l1 + l2;
                q = (l0 * p0 + l1 * p1 + l2 * p2) / sum;
                Vec3d g0 = Vec3d.Cross(nf, p2 - p1) / twoA, g1 = Vec3d.Cross(nf, p0 - p2) / twoA, g2 = Vec3d.Cross(nf, p1 - p0) / twoA;
                Vec3d ga = alpha[0] * g0 + alpha[1] * g1 + alpha[2] * g2;
                Vec3d gb = beta[0] * g0 + beta[1] * g1 + beta[2] * g2;
                delta = Vec3d.Dot(ga, t) * Vec3d.Dot(gb, b) - Vec3d.Dot(ga, b) * Vec3d.Dot(gb, t);
            }
            found.Add(new Degenerate(q, fi, index, delta, contrast));
        }

        if (cancelDistance > 0) Cancel(found, cancelDistance);
        return found.ToArray();
    }

    /// <summary>Removes the closest opposite-index pairs within <paramref name="distance"/>, nearest first.</summary>
    private static void Cancel(List<Degenerate> pts, double distance)
    {
        while (true)
        {
            int bi = -1, bj = -1; double best = distance;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    if (pts[i].Index * pts[j].Index >= 0) continue;
                    double d = (pts[i].Point - pts[j].Point).Length;
                    if (d <= best) { best = d; bi = i; bj = j; }
                }
            if (bi < 0) return;
            pts.RemoveAt(bj); pts.RemoveAt(bi);
        }
    }

    /// <summary>Sum of the indices: the Euler characteristic of a closed surface when every umbilic is isolated.</summary>
    public static double IndexSum(IEnumerable<Degenerate> points)
    {
        double s = 0;
        foreach (var p in points) s += p.Index;
        return s;
    }

    /// <summary>Rotates v by the minimal rotation that takes unit vector from onto unit vector to.</summary>
    private static void Rotate(Vec3d from, Vec3d to, Vec3d v, out Vec3d r)
    {
        double c = Vec3d.Dot(from, to);
        if (c <= -1 + 1e-12) { r = -v; return; }
        Vec3d axis = Vec3d.Cross(from, to); // |axis| = sin θ
        // Rodrigues with the unnormalised axis: r = v c + (axis × v) + axis (axis·v)/(1 + c)
        r = c * v + Vec3d.Cross(axis, v) + (Vec3d.Dot(axis, v) / (1 + c)) * axis;
    }
}
