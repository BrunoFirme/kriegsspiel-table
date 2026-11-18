using System.Numerics;

namespace KTable.Core.Model;

sealed class Ring
{
    public float Y, A, B, X, Z, Ao = 1;
    public float? Th0, Th1;
    public Func<float, float>? RAt, YAt;

    public Ring(float y, float a, float b, float x = 0, float z = 0, float ao = 1) => (Y, A, B, X, Z, Ao) = (y, a, b, x, z, ao);

    public Ring Open(float th0, float th1)
    {
        (Th0, Th1) = (th0, th1);
        return this;
    }
}

sealed class Piece
{
    public readonly List<Vector3> P = new();
    public readonly List<int> T = new();
    public readonly List<float> Ao = new();

    public Piece Each(Func<Vector3, Vector3> move)
    {
        for (int i = 0; i < P.Count; i++) P[i] = move(P[i]);
        return this;
    }

    public Piece Shift(float x, float y, float z) => Each(p => p + new Vector3(x, y, z));

    public Piece TurnX(float angle, float pivotY, float pivotZ = 0) => Each(p => Loft.TurnX(p, angle, pivotY, pivotZ));

    public Piece TurnZ(float angle, float pivotX, float pivotY) => Each(p =>
    {
        float x = p.X - pivotX, y = p.Y - pivotY, c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector3(x * c - y * s + pivotX, x * s + y * c + pivotY, p.Z);
    });

    public Piece Unpose(float shoulder, float elbow, float yShoulder, float yElbow) => TurnX(-shoulder, yShoulder).TurnX(-elbow, yElbow);

    public List<Vector3> Normals()
    {
        var n = new Vector3[P.Count];
        for (int i = 0; i + 2 < T.Count; i += 3)
        {
            var f = Vector3.Cross(P[T[i + 1]] - P[T[i]], P[T[i + 2]] - P[T[i]]);
            n[T[i]] += f;
            n[T[i + 1]] += f;
            n[T[i + 2]] += f;
        }
        return n.Select(v => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : Vector3.UnitY).ToList();
    }
}

static class Loft
{
    public static Vector3 TurnX(Vector3 p, float angle, float pivotY, float pivotZ = 0)
    {
        float y = p.Y - pivotY, z = p.Z - pivotZ, c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector3(p.X, y * c - z * s + pivotY, y * s + z * c + pivotZ);
    }

    public static Piece Rings(IReadOnlyList<Ring> unsorted, int segs, bool top = false, bool bottom = false, float rot = 0, Func<float, float>? fold = null, float k = 0,
        float phase = 0)
    {
        var rings = unsorted.OrderBy(r => r.Y).ToList();
        int n = rings.Count;
        bool open = rings[0].Th0 != null;
        int cols = open ? segs + 1 : segs;
        var g = new Piece();
        for (int i = 0; i < n; i++)
        {
            var r = rings[i];
            float t = n > 1 ? (float)i / (n - 1) : 0;
            for (int j = 0; j < cols; j++)
            {
                float th = open ? r.Th0!.Value + (r.Th1!.Value - r.Th0.Value) * j / segs : rot + (float)j / segs * MathF.Tau;
                float m = 1, fs = 0, amp = 0;
                if (fold != null)
                {
                    amp = fold(t);
                    fs = MathF.Sin(th * k + phase + t * 0.6f);
                    m = 1 + amp * fs;
                }
                float ra = r.RAt?.Invoke(th) ?? 1;
                g.P.Add(new Vector3(r.X + r.A * ra * MathF.Cos(th) * m, r.YAt?.Invoke(th) ?? r.Y, r.Z + r.B * ra * MathF.Sin(th) * m));
                g.Ao.Add(MathF.Max(0.45f, r.Ao * (1 - MathF.Max(0, -fs) * amp * 5)));
            }
        }
        for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < segs; j++)
            {
                int j1 = open ? j + 1 : (j + 1) % segs, a = i * cols + j, b = i * cols + j1, c = (i + 1) * cols + j, d = (i + 1) * cols + j1;
                g.T.AddRange([a, c, b, b, c, d]);
            }
        void Cap(int i, bool up)
        {
            var r = rings[i];
            int ci = g.P.Count;
            g.P.Add(new Vector3(r.X, r.Y, r.Z));
            g.Ao.Add(r.Ao);
            for (int j = 0; j < segs; j++)
            {
                int j1 = open ? j + 1 : (j + 1) % segs, a = i * cols + j, b = i * cols + j1;
                g.T.AddRange(up ? [ci, b, a] : [ci, a, b]);
            }
        }
        if (top) Cap(n - 1, true);
        if (bottom) Cap(0, false);
        return g;
    }

    public static List<Vector3> Bez(Vector3 p0, Vector3 p1, Vector3 p2, int n)
    {
        var o = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1), u = 1 - t;
            o.Add(p0 * u * u + p1 * (2 * u * t) + p2 * (t * t));
        }
        return o;
    }

    public static Piece Sweep(IReadOnlyList<Vector3> pts, IReadOnlyList<(float A, float B)> secs, int segs, Vector3? up = null, IReadOnlyList<Vector3>? ups = null, bool caps = false,
        float rot = 0, float ao = 1)
    {
        int n = pts.Count;
        var g = new Piece();
        for (int i = 0; i < n; i++)
        {
            var tangent = Vector3.Normalize(pts[Math.Min(i + 1, n - 1)] - pts[Math.Max(i - 1, 0)]);
            var u = ups?[i] ?? up ?? Vector3.UnitY;
            if (MathF.Abs(Vector3.Dot(u, tangent)) > 0.98f) u = MathF.Abs(tangent.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;
            var side = Vector3.Normalize(Vector3.Cross(tangent, u));
            var up2 = Vector3.Normalize(Vector3.Cross(side, tangent));
            for (int j = 0; j < segs; j++)
            {
                float th = rot + (float)j / segs * MathF.Tau, c = MathF.Cos(th) * secs[i].A, s = MathF.Sin(th) * secs[i].B;
                g.P.Add(pts[i] + side * c + up2 * s);
                g.Ao.Add(ao);
            }
        }
        for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < segs; j++)
            {
                int j1 = (j + 1) % segs, a = i * segs + j, b = i * segs + j1, c = (i + 1) * segs + j, d = (i + 1) * segs + j1;
                g.T.AddRange([a, c, b, b, c, d]);
            }
        if (caps)
        {
            void Cap(int i, bool end)
            {
                int ci = g.P.Count;
                g.P.Add(pts[i]);
                g.Ao.Add(ao);
                for (int j = 0; j < segs; j++)
                {
                    int j1 = (j + 1) % segs, a = i * segs + j, b = i * segs + j1;
                    g.T.AddRange(end ? [ci, b, a] : [ci, a, b]);
                }
            }
            Cap(0, false);
            Cap(n - 1, true);
        }
        return g;
    }

    static Piece Sphere(int ws, int hs, float thetaStart = 0, float thetaLength = MathF.PI)
    {
        var g = new Piece();
        float thetaEnd = MathF.Min(thetaStart + thetaLength, MathF.PI);
        for (int iy = 0; iy <= hs; iy++)
        {
            float v = (float)iy / hs, th = thetaStart + v * thetaLength;
            for (int ix = 0; ix <= ws; ix++)
            {
                float u = (float)ix / ws, ph = u * MathF.Tau;
                g.P.Add(new Vector3(-MathF.Cos(ph) * MathF.Sin(th), MathF.Cos(th), MathF.Sin(ph) * MathF.Sin(th)));
                g.Ao.Add(1);
            }
        }
        int Grid(int iy, int ix) => iy * (ws + 1) + ix;
        for (int iy = 0; iy < hs; iy++)
            for (int ix = 0; ix < ws; ix++)
            {
                int a = Grid(iy, ix + 1), b = Grid(iy, ix), c = Grid(iy + 1, ix), d = Grid(iy + 1, ix + 1);
                if (iy != 0 || thetaStart > 0) g.T.AddRange([a, b, d]);
                if (iy != hs - 1 || thetaEnd < MathF.PI) g.T.AddRange([b, c, d]);
            }
        return g;
    }

    public static Piece Ellipsoid(float cx, float cy, float cz, float a, float b, float c, int ws, int hs, float ao = 1) =>
        Shaded(Sphere(ws, hs).Each(p => new Vector3(p.X * a + cx, p.Y * b + cy, p.Z * c + cz)), ao);

    public static Piece Shell(float cx, float cy, float cz, float a, float b, float c, float thMax, float tilt, int ws, int hs, float ao = 1, float thStart = 0) =>
        Shaded(Sphere(ws, hs, thStart, thMax).TurnX(tilt, 0).Each(p => new Vector3(p.X * a + cx, p.Y * b + cy, p.Z * c + cz)), ao);

    public static Piece Box(float w, float h, float d, float x, float y, float z, float ao = 1)
    {
        var g = new Piece();
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 e)
        {
            int first = g.P.Count;
            g.P.AddRange([a, b, c, e]);
            g.Ao.AddRange([ao, ao, ao, ao]);
            g.T.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }
        Vector3 P(int sx, int sy, int sz) => new(x + sx * w / 2, y + sy * h / 2, z + sz * d / 2);
        Face(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        Face(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
        Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
        Face(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
        Face(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
        Face(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        return g;
    }

    public static Piece Rod(Vector3 from, Vector3 to, float r, int segs) =>
        Sweep([from, to], [(r, r), (r, r)], segs, caps: true);

    public static Piece Cone(Vector3 bottom, float r, float h, int segs) =>
        Rings([new Ring(bottom.Y, r, r, bottom.X, bottom.Z), new Ring(bottom.Y + h, 0.0001f, 0.0001f, bottom.X, bottom.Z)], segs, bottom: true);

    static Piece Shaded(Piece g, float ao)
    {
        for (int i = 0; i < g.Ao.Count; i++) g.Ao[i] = ao;
        return g;
    }
}
