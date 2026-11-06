using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Studio;

public readonly record struct Affine2(Vector2 X, Vector2 Y, Vector2 Origin)
{
    public static Affine2 Identity { get; } = new(new Vector2(1, 0), new Vector2(0, 1), Vector2.Zero);

    public static Affine2 Turn(float rotation, Vector2 origin)
    {
        var (sin, cos) = Mathf.SinCos(rotation);
        return new Affine2(new Vector2(cos, sin), new Vector2(-sin, cos), origin);
    }

    float Tdotx(Vector2 v) => X.X * v.X + Y.X * v.Y;
    float Tdoty(Vector2 v) => X.Y * v.X + Y.Y * v.Y;

    public static Vector2 operator *(Affine2 t, Vector2 v) => new Vector2(t.Tdotx(v), t.Tdoty(v)) + t.Origin;

    public static Affine2 operator *(Affine2 l, Affine2 r) =>
        new(new Vector2(l.Tdotx(r.X), l.Tdoty(r.X)), new Vector2(l.Tdotx(r.Y), l.Tdoty(r.Y)), l * r.Origin);
}
