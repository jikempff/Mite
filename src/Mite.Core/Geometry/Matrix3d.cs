using System;

namespace Mite.Core.Geometry;

public readonly struct Matrix3d
{
    private readonly double _m00, _m01, _m02;
    private readonly double _m10, _m11, _m12;
    private readonly double _m20, _m21, _m22;

    public Matrix3d(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22)
    {
        _m00 = m00; _m01 = m01; _m02 = m02;
        _m10 = m10; _m11 = m11; _m12 = m12;
        _m20 = m20; _m21 = m21; _m22 = m22;
    }

    public double this[int r, int c] => (r, c) switch
    {
        (0, 0) => _m00, (0, 1) => _m01, (0, 2) => _m02,
        (1, 0) => _m10, (1, 1) => _m11, (1, 2) => _m12,
        (2, 0) => _m20, (2, 1) => _m21, (2, 2) => _m22,
        _ => throw new IndexOutOfRangeException()
    };

    public static Matrix3d Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Vec3d operator *(Matrix3d m, Vec3d v) =>
        new(m._m00 * v.X + m._m01 * v.Y + m._m02 * v.Z,
            m._m10 * v.X + m._m11 * v.Y + m._m12 * v.Z,
            m._m20 * v.X + m._m21 * v.Y + m._m22 * v.Z);

    public static Matrix3d operator +(Matrix3d a, Matrix3d b) =>
        new(a._m00 + b._m00, a._m01 + b._m01, a._m02 + b._m02,
            a._m10 + b._m10, a._m11 + b._m11, a._m12 + b._m12,
            a._m20 + b._m20, a._m21 + b._m21, a._m22 + b._m22);

    public static Matrix3d operator *(double s, Matrix3d m) =>
        new(s * m._m00, s * m._m01, s * m._m02,
            s * m._m10, s * m._m11, s * m._m12,
            s * m._m20, s * m._m21, s * m._m22);

    public Matrix3d Transpose() =>
        new(_m00, _m10, _m20, _m01, _m11, _m21, _m02, _m12, _m22);

    public static Matrix3d OuterProduct(Vec3d a, Vec3d b) =>
        new(a.X * b.X, a.X * b.Y, a.X * b.Z,
            a.Y * b.X, a.Y * b.Y, a.Y * b.Z,
            a.Z * b.X, a.Z * b.Y, a.Z * b.Z);

    /// <summary>
    /// Eigendecomposition of a symmetric matrix by cyclic Jacobi iteration.
    /// Returns eigenvalues in ascending order with matching orthonormal
    /// eigenvectors. Used to average shape-operator tensors across vertex
    /// neighborhoods (smoothing curvature values and directions consistently).
    /// </summary>
    public static void EigenSymmetric(Matrix3d m, out double[] values, out Vec3d[] vectors)
    {
        var a = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                a[r, c] = m[r, c];

        var v = new double[3, 3];
        v[0, 0] = v[1, 1] = v[2, 2] = 1.0;

        double diag = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
        for (int sweep = 0; sweep < 64; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (off < 1e-15 * (1.0 + diag)) break;

            for (int p = 0; p < 3; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    double apq = a[p, q];
                    if (Math.Abs(apq) < 1e-300) continue;

                    double theta = (a[q, q] - a[p, p]) / (2.0 * apq);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                    if (theta == 0.0) t = 1.0;
                    double cos = 1.0 / Math.Sqrt(t * t + 1.0);
                    double sin = t * cos;

                    for (int r = 0; r < 3; r++)
                    {
                        if (r == p || r == q) continue;
                        double arp = a[r, p], arq = a[r, q];
                        a[r, p] = cos * arp - sin * arq;
                        a[p, r] = a[r, p];
                        a[r, q] = sin * arp + cos * arq;
                        a[q, r] = a[r, q];
                    }
                    double app = a[p, p], aqq = a[q, q];
                    a[p, p] = app - t * apq;
                    a[q, q] = aqq + t * apq;
                    a[p, q] = a[q, p] = 0.0;

                    for (int r = 0; r < 3; r++)
                    {
                        double vrp = v[r, p], vrq = v[r, q];
                        v[r, p] = cos * vrp - sin * vrq;
                        v[r, q] = sin * vrp + cos * vrq;
                    }
                }
            }
        }

        // Sort ascending by eigenvalue
        var idx = new[] { 0, 1, 2 };
        System.Array.Sort(idx, (x, y) => a[x, x].CompareTo(a[y, y]));

        values = new[] { a[idx[0], idx[0]], a[idx[1], idx[1]], a[idx[2], idx[2]] };
        vectors = new[]
        {
            new Vec3d(v[0, idx[0]], v[1, idx[0]], v[2, idx[0]]),
            new Vec3d(v[0, idx[1]], v[1, idx[1]], v[2, idx[1]]),
            new Vec3d(v[0, idx[2]], v[1, idx[2]], v[2, idx[2]])
        };
    }
}
