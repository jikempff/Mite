using System;

namespace Mite.Core.Geometry;

public readonly struct Vec3d : IEquatable<Vec3d>
{
    public readonly double X, Y, Z;

    public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    public Vec3d Normalized()
    {
        double len = Length;
        return len < 1e-15 ? Zero : new Vec3d(X / len, Y / len, Z / len);
    }

    public static Vec3d Zero => new(0, 0, 0);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3d operator *(double s, Vec3d v) => new(s * v.X, s * v.Y, s * v.Z);
    public static Vec3d operator *(Vec3d v, double s) => new(s * v.X, s * v.Y, s * v.Z);
    public static Vec3d operator /(Vec3d v, double s) => new(v.X / s, v.Y / s, v.Z / s);

    public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3d Cross(Vec3d a, Vec3d b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public double this[int i] => i switch { 0 => X, 1 => Y, 2 => Z, _ => throw new IndexOutOfRangeException() };

    public bool Equals(Vec3d other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vec3d v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";

    public static bool operator ==(Vec3d a, Vec3d b) => a.Equals(b);
    public static bool operator !=(Vec3d a, Vec3d b) => !a.Equals(b);
}
