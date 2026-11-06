using System.Numerics;

namespace KTable.Client.Geometry;

public static class VectorOps
{
    public static float Dot(this Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3 Cross(this Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static float NormSquared(this Vector3 v)
    {
        float x = v.X * v.X, y = v.Y * v.Y, z = v.Z * v.Z;
        return x + y + z;
    }

    public static float Norm(this Vector3 v) => Mathf.Sqrt(v.NormSquared());

    public static Vector3 Normalized(this Vector3 v)
    {
        float l2 = v.NormSquared();
        if (l2 == 0f) return Vector3.Zero;
        float l = Mathf.Sqrt(l2);
        return new Vector3(v.X / l, v.Y / l, v.Z / l);
    }

    public static Vector3 Lerp(this Vector3 a, Vector3 b, float t) => new(Mathf.Lerp(a.X, b.X, t), Mathf.Lerp(a.Y, b.Y, t), Mathf.Lerp(a.Z, b.Z, t));
    public static Vector3 Min(this Vector3 a, Vector3 b) => new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
    public static Vector3 Max(this Vector3 a, Vector3 b) => new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));

    public static float Dot(this Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
    public static float NormSquared(this Vector2 v) => v.X * v.X + v.Y * v.Y;
    public static float Norm(this Vector2 v) => Mathf.Sqrt(v.X * v.X + v.Y * v.Y);
    public static Vector2 Abs(this Vector2 v) => new(Mathf.Abs(v.X), Mathf.Abs(v.Y));
    public static float Angle(this Vector2 v) => Mathf.Atan2(v.Y, v.X);
}

public static class Axis
{
    public static readonly Vector3 Up = new(0, 1, 0);
    public static readonly Vector3 Down = new(0, -1, 0);
    public static readonly Vector3 Right = new(1, 0, 0);
    public static readonly Vector3 Back = new(0, 0, 1);
}
