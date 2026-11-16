using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class RaisedMesh
{
    const float Snap = 0.01f, SillOutM = 0.12f, SillThickM = 0.07f, SillWiderM = 0.2f, LintelOutM = 0.08f, LintelThickM = 0.14f, LintelWiderM = 0.24f, LintelAboveM = 0.12f, LeastWallM = 2.5f;

    readonly List<Vector3> _at = new();
    readonly List<Vector3> _normal = new();
    readonly List<Color> _tint = new();
    readonly List<Vector2> _metres = new();
    readonly List<ushort> _info = new();
    readonly List<float> _tangent = new();
    readonly List<Vector2> _shift = new();
    readonly List<ushort> _extra = new();
    readonly List<int> _index = new();

    public const float Unbounded = 1e4f, LancetDoor = -3, ArcadeDoor = -4;

    public float PerMetre { get; set; } = 1;

    public Vector3 Origin { get; init; }

    public Vector2 Shift { get; set; }

    public float Weather { get; set; } = 0.5f;

    public float FootingM { get; set; } = RaisedLooks.PlinthM;

    public float FloorY { get; set; } = float.NaN;

    public float Scorch { get; set; }

    public List<Vector3>? Smokes { get; init; }

    public Func<Vector3, Vector3, float>? OpenFor { get; set; }

    public RaisedMesh? Fine { get; init; }

    public bool Plain { get; init; }

    bool Muted { get; init; }

    static readonly RaisedMesh Unseen = new() { Muted = true };

    public int Count => _at.Count;

    public int Triangles => _index.Count / 3;

    public IReadOnlyList<Vector3> Points => _at;

    public IReadOnlyList<int> Corners => _index;

    public RaisedMesh Small
    {
        get
        {
            if (Plain) return Unseen;
            if (Fine == null) return this;
            Fine.PerMetre = PerMetre;
            Fine.Shift = Shift;
            Fine.Weather = Weather;
            Fine.Scorch = Scorch;
            return Fine;
        }
    }

    public readonly record struct Paint(Color Tint, float Code, float Length = 0, float Door = -1, float Storeys = 0, float Top = Unbounded, float Open = Unbounded);

    public void Face(Vector3[] ring, Vector3 outward, Vector3 along, Vector3 origin, Paint paint)
    {
        if (ring.Length < 3 || Muted) return;
        var normal = Normal(ring, outward);
        if (normal == Vector3.Zero) return;
        var u = (along - normal * along.Dot(normal)).Normalized();
        if (u == Vector3.Zero) u = normal.Cross(Vector3.Up).Normalized();
        var v = normal.Cross(u);
        if (v.Y < 0 || v.Y == 0 && v.Dot(Vector3.Up) < 0) v = -v;
        bool turned = (ring[1] - ring[0]).Cross(ring[2] - ring[0]).Dot(normal) > 0;
        float sign = normal.Cross(u).Dot(-v) >= 0 ? 1 : -1;
        int first = _at.Count;
        foreach (var p in ring) Corner(p, normal, u, v, sign, origin, paint);
        for (int i = 1; i + 1 < ring.Length; i++)
        {
            _index.Add(first);
            _index.Add(first + (turned ? i + 1 : i));
            _index.Add(first + (turned ? i : i + 1));
        }
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
        float from = bottom + FootingM * PerMetre, open = OpenFor?.Invoke(a, b) ?? Unbounded;
        if (!float.IsNaN(FloorY)) from = Mathf.Max(from, FloorY);
        var laid = paint with { Length = flat.Length() / PerMetre, Top = (Mathf.Min(ta, tb) - from) / PerMetre, Open = open };
        Face(ring, outward, flat, a with { Y = from }, laid);
        Sills(a, flat.Normalized(), outward, from, laid);
    }

    void Sills(Vector3 a, Vector3 along, Vector3 outward, float from, Paint paint)
    {
        int glazing = (int)paint.Code / 16, storeys = (int)Math.Floor(paint.Storeys);
        bool front = paint.Door >= 0 || paint.Storeys % 1 > 0.25f;
        if (Fine == null || paint.Code >= RaisedCodes.Plinth || glazing is < 1 or > 3 || storeys < 1 || !front || paint.Top < LeastWallM) return;
        var (apart, wide, high, sill) = glazing switch { 1 => (2.6f, 0.95f, 1.6f, 0.95f), 2 => (3.2f, 0.6f, 0.7f, 1.3f), _ => (3f, 0.6f, 1.1f, 0.95f) };
        float run = (float)(System.Half)paint.Length, door = (float)(System.Half)paint.Door;
        int columns = (int)Math.Floor((run - 1) / apart + Snap);
        if (columns < 1) return;
        float start = (run - (columns - 1) * apart) / 2;
        int kind = (int)paint.Code % 16;
        bool wooden = kind is (int)Fabric.Timber or (int)Fabric.Log or (int)Fabric.Plank;
        var stone = wooden ? new Paint(RaisedColours.Timber, RaisedCodes.Timber) : new Paint(RaisedColours.Plinth.Lightened(0.25f), RaisedCodes.Plinth);
        for (int s = paint.Storeys % 1 > 0.25f ? 1 : 0; s < storeys; s++)
            for (int i = 0; i < columns; i++)
            {
                float cx = start + i * apart, low = s * RaisedLooks.StoreyM + sill;
                if (s == 0 && door >= 0 && Math.Abs(cx - door) < (wide + 1.2f) / 2 || low + high + LintelAboveM + LintelThickM > paint.Top) continue;
                var centre = a + along * (cx * PerMetre);
                Ledge(centre, along, outward, from + low * PerMetre, wide + SillWiderM, SillOutM, SillThickM, stone);
                Ledge(centre, along, outward, from + (low + high + LintelAboveM + LintelThickM) * PerMetre, wide + LintelWiderM, LintelOutM, LintelThickM, stone);
            }
    }

    void Ledge(Vector3 centre, Vector3 along, Vector3 outward, float y, float wideM, float outM, float thickM, Paint paint)
    {
        var s = Small;
        Vector3 half = along * (wideM / 2 * PerMetre), reach = outward * (outM * PerMetre);
        Vector3 l = (centre - half) with { Y = y }, r = (centre + half) with { Y = y }, down = Vector3.Down * (thickM * PerMetre);
        s.Face([l, r, r + reach, l + reach], Vector3.Up, along, l, paint);
        s.Face([l + reach, r + reach, r + reach + down, l + reach + down], outward, along, l + reach + down, paint);
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

    static ushort Half(float value) => BitConverter.HalfToUInt16Bits((System.Half)value);

    void Corner(Vector3 p, Vector3 normal, Vector3 u, Vector3 v, float sign, Vector3 origin, Paint paint)
    {
        _tangent.Add(u.X);
        _tangent.Add(u.Y);
        _tangent.Add(u.Z);
        _tangent.Add(sign);
        _shift.Add(Shift);
        _extra.Add(Half(paint.Top));
        _extra.Add(Half(paint.Open));
        _extra.Add(Half(Weather));
        _extra.Add(Half(Scorch));
        _at.Add(p - Origin);
        _normal.Add(normal);
        _tint.Add(paint.Tint);
        var d = p - origin;
        _metres.Add(new Vector2(d.Dot(u), d.Dot(v)) / PerMetre);
        _info.Add(Half(paint.Code));
        _info.Add(Half(paint.Length));
        _info.Add(Half(paint.Door));
        _info.Add(Half(paint.Storeys));
    }

    static byte[] Bytes(List<ushort> halves)
    {
        var bytes = new byte[halves.Count * sizeof(ushort)];
        for (int i = 0; i < halves.Count; i++)
        {
            bytes[i * 2] = (byte)(halves[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(halves[i] >> 8);
        }
        return bytes;
    }

    public Godot.Collections.Array Arrays()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _at.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normal.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _tint.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _metres.ToArray();
        arrays[(int)Mesh.ArrayType.Custom0] = Bytes(_info);
        arrays[(int)Mesh.ArrayType.Tangent] = _tangent.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = _shift.ToArray();
        arrays[(int)Mesh.ArrayType.Custom1] = Bytes(_extra);
        arrays[(int)Mesh.ArrayType.Index] = _index.ToArray();
        return arrays;
    }

    public const Mesh.ArrayFormat Format = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaHalf << (int)Mesh.ArrayFormat.FormatCustom0Shift
        | (long)Mesh.ArrayCustomFormat.RgbaHalf << (int)Mesh.ArrayFormat.FormatCustom1Shift);

}
