namespace KTable.Client.Studio;

public readonly record struct RegionSeed(int Tri, float U, float V);

public sealed class UvSurface
{
    const float Reach = 0.75f, Inward = 0.5f, Weld = 1e-5f;

    readonly IReadOnlyList<float> _uv;
    readonly int[] _vid;
    readonly (float x, float y, float z)[] _normal;
    readonly List<(int a, int b)> _edges = new();
    readonly Dictionary<int, List<int>> _links = new();

    public int W { get; }
    public int H { get; }
    public int Tris { get; }
    public int[] Owner { get; }

    public UvSurface(IReadOnlyList<float> pos, IReadOnlyList<float> uv, int w, int h)
    {
        W = w;
        H = h;
        _uv = uv;
        Tris = Math.Min(pos.Count / 9, uv.Count / 6);
        _vid = Weld3(pos, Tris * 3);
        _normal = new (float, float, float)[Tris];
        for (int t = 0; t < Tris; t++) _normal[t] = Normal(pos, t);
        Owner = new int[w * h];
        Array.Fill(Owner, -1);
        for (int t = 0; t < Tris; t++) Cover(t, k => { if (Owner[k] < 0) Owner[k] = t; });
        Join();
    }

    public int TexelOf(RegionSeed s)
    {
        int i = s.Tri * 6;
        float w2 = 1 - s.U - s.V;
        float u = _uv[i] * w2 + _uv[i + 2] * s.U + _uv[i + 4] * s.V, v = _uv[i + 1] * w2 + _uv[i + 3] * s.U + _uv[i + 5] * s.V;
        int x = Math.Clamp((int)MathF.Floor((u - MathF.Floor(u)) * W), 0, W - 1), y = Math.Clamp((int)MathF.Floor((v - MathF.Floor(v)) * H), 0, H - 1);
        return y * W + x;
    }

    public HashSet<int> Within(int seedTri, float bendDegrees)
    {
        var inside = new HashSet<int> { seedTri };
        if (bendDegrees >= 180)
        {
            for (int t = 0; t < Tris; t++) inside.Add(t);
            return inside;
        }
        float least = MathF.Cos(bendDegrees * MathF.PI / 180);
        var next = new Dictionary<int, List<int>>();
        foreach (var (a, b) in _edges)
        {
            if (Dot(_normal[a], _normal[b]) < least) continue;
            (next.TryGetValue(a, out var la) ? la : next[a] = new()).Add(b);
            (next.TryGetValue(b, out var lb) ? lb : next[b] = new()).Add(a);
        }
        var queue = new Queue<int>();
        queue.Enqueue(seedTri);
        while (queue.Count > 0)
            if (next.TryGetValue(queue.Dequeue(), out var on))
                foreach (var t in on)
                    if (inside.Add(t)) queue.Enqueue(t);
        return inside;
    }

    public bool Touching(int a, int b)
    {
        if (a == b) return true;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (_vid[a * 3 + i] == _vid[b * 3 + j]) return true;
        return false;
    }

    public IReadOnlyList<int> LinksOf(int texel) => _links.TryGetValue(texel, out var l) ? l : Array.Empty<int>();

    void Cover(int t, Action<int> texel)
    {
        int i = t * 6;
        float ax = _uv[i] * W, ay = _uv[i + 1] * H, bx = _uv[i + 2] * W, by = _uv[i + 3] * H, cx = _uv[i + 4] * W, cy = _uv[i + 5] * H;
        if (!float.IsFinite(ax + ay + bx + by + cx + cy)) return;
        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (MathF.Abs(area) < 1e-9f) return;
        float sign = area > 0 ? 1 : -1;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx)) - 1)), x1 = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx)) + 1));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy)) - 1)), y1 = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy)) + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                if (Edge(ax, ay, bx, by, px, py, sign) < -Reach || Edge(bx, by, cx, cy, px, py, sign) < -Reach || Edge(cx, cy, ax, ay, px, py, sign) < -Reach) continue;
                texel(y * W + x);
            }
    }

    void Join()
    {
        var sides = new Dictionary<(int, int), List<(int t, int i, int j)>>();
        for (int t = 0; t < Tris; t++)
            for (int c = 0; c < 3; c++)
            {
                int i = t * 3 + c, j = t * 3 + (c + 1) % 3;
                if (_vid[i] == _vid[j]) continue;
                var key = _vid[i] < _vid[j] ? (_vid[i], _vid[j]) : (_vid[j], _vid[i]);
                (sides.TryGetValue(key, out var l) ? l : sides[key] = new()).Add((t, i, j));
            }
        foreach (var on in sides.Values)
            for (int p = 0; p < on.Count; p++)
                for (int q = p + 1; q < on.Count; q++)
                {
                    var (ta, ia, ja) = on[p];
                    var (tb, ib, jb) = on[q];
                    if (_vid[ia] != _vid[ib]) (ib, jb) = (jb, ib);
                    _edges.Add((ta, tb));
                    if (Same(ia, ib) && Same(ja, jb)) continue;
                    Stitch(ta, ia, ja, tb, ib, jb);
                }
    }

    bool Same(int a, int b) => MathF.Abs(_uv[a * 2] - _uv[b * 2]) * W < 0.5f && MathF.Abs(_uv[a * 2 + 1] - _uv[b * 2 + 1]) * H < 0.5f;

    void Stitch(int ta, int ia, int ja, int tb, int ib, int jb)
    {
        float Len(int i, int j) => MathF.Sqrt(Sq((_uv[i * 2] - _uv[j * 2]) * W) + Sq((_uv[i * 2 + 1] - _uv[j * 2 + 1]) * H));
        int steps = Math.Clamp((int)MathF.Ceiling(MathF.Max(Len(ia, ja), Len(ib, jb))), 1, 4 * Math.Max(W, H));
        for (int s = 0; s <= steps; s++)
        {
            float f = s / (float)steps;
            int a = Along(ta, ia, ja, f), b = Along(tb, ib, jb, f);
            if (a < 0 || b < 0 || a == b) continue;
            (_links.TryGetValue(a, out var la) ? la : _links[a] = new()).Add(b);
            (_links.TryGetValue(b, out var lb) ? lb : _links[b] = new()).Add(a);
        }
    }

    int Along(int t, int i, int j, float f)
    {
        float x = (_uv[i * 2] + (_uv[j * 2] - _uv[i * 2]) * f) * W, y = (_uv[i * 2 + 1] + (_uv[j * 2 + 1] - _uv[i * 2 + 1]) * f) * H;
        int k = t * 6;
        float cx = (_uv[k] + _uv[k + 2] + _uv[k + 4]) / 3 * W - x, cy = (_uv[k + 1] + _uv[k + 3] + _uv[k + 5]) / 3 * H - y;
        float len = MathF.Sqrt(cx * cx + cy * cy);
        if (len > Inward) { x += cx / len * Inward; y += cy / len * Inward; }
        int tx = (int)MathF.Floor(x), ty = (int)MathF.Floor(y);
        return tx < 0 || ty < 0 || tx >= W || ty >= H ? -1 : ty * W + tx;
    }

    static int[] Weld3(IReadOnlyList<float> pos, int corners)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < corners * 3; i++) { lo = MathF.Min(lo, pos[i]); hi = MathF.Max(hi, pos[i]); }
        float step = MathF.Max(1e-12f, (hi - lo) * Weld);
        var ids = new Dictionary<(long, long, long), int>();
        var vid = new int[corners];
        for (int c = 0; c < corners; c++)
        {
            var key = ((long)MathF.Round(pos[c * 3] / step), (long)MathF.Round(pos[c * 3 + 1] / step), (long)MathF.Round(pos[c * 3 + 2] / step));
            if (!ids.TryGetValue(key, out var id)) ids[key] = id = ids.Count;
            vid[c] = id;
        }
        return vid;
    }

    static (float, float, float) Normal(IReadOnlyList<float> p, int t)
    {
        int o = t * 9;
        float ux = p[o + 3] - p[o], uy = p[o + 4] - p[o + 1], uz = p[o + 5] - p[o + 2];
        float vx = p[o + 6] - p[o], vy = p[o + 7] - p[o + 1], vz = p[o + 8] - p[o + 2];
        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        float l = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        return l < 1e-20f ? (0, 0, 0) : (nx / l, ny / l, nz / l);
    }

    static float Dot((float x, float y, float z) a, (float x, float y, float z) b) => a.x * b.x + a.y * b.y + a.z * b.z;

    static float Sq(float x) => x * x;

    static float Edge(float x0, float y0, float x1, float y1, float px, float py, float sign)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9f) return 0;
        return sign * (dx * (py - y0) - dy * (px - x0)) / len;
    }
}

public static class UvRegion
{
    public static int Fill(UvSurface s, byte[] paint, byte[]? look, RegionSeed seed, float match, float bendDegrees, byte r, byte g, byte b, byte a)
    {
        if (seed.Tri < 0 || seed.Tri >= s.Tris) return 0;
        int start = s.TexelOf(seed);
        if (s.Owner[start] < 0) s.Owner[start] = seed.Tri;
        var inside = s.Within(seed.Tri, bendDegrees);
        var want = Shown(paint, look, start);
        int limit = (int)MathF.Round(Math.Clamp(match, 0, 1) * 255);
        bool Alike(int k)
        {
            var c = Shown(paint, look, k);
            return Math.Abs(c.r - want.r) <= limit && Math.Abs(c.g - want.g) <= limit && Math.Abs(c.b - want.b) <= limit;
        }
        var seen = new bool[s.W * s.H];
        var region = new List<int> { start };
        seen[start] = true;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        void Try(int from, int k, bool linked)
        {
            if (seen[k]) return;
            int owner = s.Owner[k];
            if (owner < 0 || !inside.Contains(owner) || (!linked && !s.Touching(s.Owner[from], owner)) || !Alike(k)) return;
            seen[k] = true;
            region.Add(k);
            queue.Enqueue(k);
        }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue();
            int x = k % s.W, y = k / s.W;
            if (x > 0) Try(k, k - 1, false);
            if (x < s.W - 1) Try(k, k + 1, false);
            if (y > 0) Try(k, k - s.W, false);
            if (y < s.H - 1) Try(k, k + s.W, false);
            foreach (var l in s.LinksOf(k)) Try(k, l, true);
        }
        foreach (var k in region)
        {
            int o = k * 4;
            paint[o] = r;
            paint[o + 1] = g;
            paint[o + 2] = b;
            paint[o + 3] = a;
        }
        return region.Count;
    }

    static (int r, int g, int b) Shown(byte[] paint, byte[]? look, int k)
    {
        int o = k * 4;
        int keep = 255 - paint[o + 3];
        if (look == null || look.Length < o + 4) return (paint[o], paint[o + 1], paint[o + 2]);
        return (paint[o] + look[o] * keep / 255, paint[o + 1] + look[o + 1] * keep / 255, paint[o + 2] + look[o + 2] * keep / 255);
    }
}
