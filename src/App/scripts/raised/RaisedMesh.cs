using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class RaisedMesh
{
    readonly List<Vector3> _at = new();
    readonly List<Vector3> _normal = new();
    readonly List<Color> _tint = new();
    readonly List<Vector2> _metres = new();
    readonly List<float> _info = new();
    readonly List<float> _tangent = new();

    public float PerMetre { get; set; } = 1;

    public Vector3 Origin { get; init; }

    public int Count => _at.Count;

    public IReadOnlyList<Vector3> Points => _at;

    public readonly record struct Paint(Color Tint, float Code, float Length = 0, float Door = -1, float Storeys = 0);

    public void Face(Vector3[] ring, Vector3 outward, Vector3 along, Vector3 origin, Paint paint)
    {
        if (ring.Length < 3) return;
        var normal = Normal(ring, outward);
        if (normal == Vector3.Zero) return;
        var u = (along - normal * along.Dot(normal)).Normalized();
        if (u == Vector3.Zero) u = normal.Cross(Vector3.Up).Normalized();
        var v = normal.Cross(u);
        if (v.Y < 0 || v.Y == 0 && v.Dot(Vector3.Up) < 0) v = -v;
        for (int i = 1; i + 1 < ring.Length; i++) Tri(ring[0], ring[i], ring[i + 1], normal, u, v, origin, paint);
    }

    public void Wall(Vector3 a, Vector3 b, float bottom, float top, Vector3 inside, Paint paint, float topA = float.NaN, float topB = float.NaN)
    {
        float ta = float.IsNaN(topA) ? top : topA, tb = float.IsNaN(topB) ? top : topB;
        var flat = b - a;
        flat.Y = 0;
        if (flat.LengthSquared() < 1e-12f) return;
        var outward = new Vector3(flat.Z, 0, -flat.X).Normalized();
        if (outward.Dot(a - inside) < 0) outward = -outward;
        var ring = new[] { a with { Y = bottom }, b with { Y = bottom }, b with { Y = tb }, a with { Y = ta } };
        Face(ring, outward, flat, a with { Y = bottom + RaisedLooks.PlinthM * PerMetre }, paint with { Length = flat.Length() / PerMetre });
    }

    static Vector3 Normal(Vector3[] ring, Vector3 outward)
    {
        var n = Vector3.Zero;
        for (int i = 0; i < ring.Length; i++)
        {
            var p = ring[i];
            var q = ring[(i + 1) % ring.Length];
            n += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
        }
        if (n.LengthSquared() < 1e-14f) return Vector3.Zero;
        n = n.Normalized();
        return n.Dot(outward) < 0 ? -n : n;
    }

    void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector3 u, Vector3 v, Vector3 origin, Paint paint)
    {
        if ((b - a).Cross(c - a).Dot(normal) > 0) (b, c) = (c, b);
        float sign = normal.Cross(u).Dot(-v) >= 0 ? 1 : -1;
        foreach (var p in new[] { a, b, c })
        {
            _tangent.Add(u.X);
            _tangent.Add(u.Y);
            _tangent.Add(u.Z);
            _tangent.Add(sign);
            _at.Add(p - Origin);
            _normal.Add(normal);
            _tint.Add(paint.Tint);
            var d = p - origin;
            _metres.Add(new Vector2(d.Dot(u), d.Dot(v)) / PerMetre);
            _info.Add(paint.Code);
            _info.Add(paint.Length);
            _info.Add(paint.Door);
            _info.Add(paint.Storeys);
        }
    }

    public Godot.Collections.Array Arrays()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _at.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normal.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _tint.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _metres.ToArray();
        arrays[(int)Mesh.ArrayType.Custom0] = _info.ToArray();
        arrays[(int)Mesh.ArrayType.Tangent] = _tangent.ToArray();
        return arrays;
    }

    public const Mesh.ArrayFormat Format = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
}
