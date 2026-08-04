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
}
