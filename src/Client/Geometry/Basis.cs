using System.Numerics;

namespace KTable.Client.Geometry;

public readonly struct Basis
{
    public readonly Vector3 Row0, Row1, Row2;

    public static Basis Identity { get; } = new(new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1));

    Basis(float xx, float yx, float zx, float xy, float yy, float zy, float xz, float yz, float zz)
    {
        Row0 = new Vector3(xx, yx, zx);
        Row1 = new Vector3(xy, yy, zy);
        Row2 = new Vector3(xz, yz, zz);
    }

    public Basis(Vector3 column0, Vector3 column1, Vector3 column2)
    {
        Row0 = new Vector3(column0.X, column1.X, column2.X);
        Row1 = new Vector3(column0.Y, column1.Y, column2.Y);
        Row2 = new Vector3(column0.Z, column1.Z, column2.Z);
    }

    public Basis(Vector3 axis, float angle)
    {
        var sq = new Vector3(axis.X * axis.X, axis.Y * axis.Y, axis.Z * axis.Z);
        var (sin, cos) = Mathf.SinCos(angle);
        float t = 1f - cos;
        float xyzt = axis.X * axis.Y * t, zyxs = axis.Z * sin;
        float r0y = xyzt - zyxs, r1x = xyzt + zyxs;
        xyzt = axis.X * axis.Z * t; zyxs = axis.Y * sin;
        float r0z = xyzt + zyxs, r2x = xyzt - zyxs;
        xyzt = axis.Y * axis.Z * t; zyxs = axis.X * sin;
        float r1z = xyzt - zyxs, r2y = xyzt + zyxs;
        Row0 = new Vector3(sq.X + cos * (1f - sq.X), r0y, r0z);
        Row1 = new Vector3(r1x, sq.Y + cos * (1f - sq.Y), r1z);
        Row2 = new Vector3(r2x, r2y, sq.Z + cos * (1f - sq.Z));
    }

    public static Basis Arc(Vector3 from, Vector3 to)
    {
        var a = from.Normalized(); var b = to.Normalized();
        float d = a.Dot(b), x, y, z, w;
        if (Mathf.Abs(d) > 0.99999976f)
        {
            if (d >= 0f) return Identity;
            throw new ArgumentException("Opposite directions have no single shortest turn.");
        }
        var c = a.Cross(b);
        float s = Mathf.Sqrt((1f + d) * 2f), rs = 1f / s;
        x = c.X * rs; y = c.Y * rs; z = c.Z * rs; w = s * 0.5f;
        float inv = 1f / Mathf.Sqrt(x * x + y * y + z * z + w * w);
        x *= inv; y *= inv; z *= inv; w *= inv;
        float k = 2f / (x * x + y * y + z * z + w * w);
        float xs = x * k, ys = y * k, zs = z * k;
        float wx = w * xs, wy = w * ys, wz = w * zs, xx = x * xs, xy = x * ys, xz = x * zs, yy = y * ys, yz = y * zs, zz = z * zs;
        return new Basis(1f - (yy + zz), xy - wz, xz + wy, xy + wz, 1f - (xx + zz), yz - wx, xz - wy, yz + wx, 1f - (xx + yy));
    }

    public Vector3 X => new(Row0.X, Row1.X, Row2.X);

    float Tdotx(Vector3 v) => Row0.X * v.X + Row1.X * v.Y + Row2.X * v.Z;
    float Tdoty(Vector3 v) => Row0.Y * v.X + Row1.Y * v.Y + Row2.Y * v.Z;
    float Tdotz(Vector3 v) => Row0.Z * v.X + Row1.Z * v.Y + Row2.Z * v.Z;

    public static Basis operator *(Basis l, Basis r) => new(
        r.Tdotx(l.Row0), r.Tdoty(l.Row0), r.Tdotz(l.Row0),
        r.Tdotx(l.Row1), r.Tdoty(l.Row1), r.Tdotz(l.Row1),
        r.Tdotx(l.Row2), r.Tdoty(l.Row2), r.Tdotz(l.Row2));

    public static Vector3 operator *(Basis b, Vector3 v) => new(b.Row0.Dot(v), b.Row1.Dot(v), b.Row2.Dot(v));
}
