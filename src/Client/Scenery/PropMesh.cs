using System.Globalization;
using System.Numerics;
using System.Text;
using KTable.Client.Geometry;
using static KTable.Client.Scenery.PropAtlas;

namespace KTable.Client.Scenery;

public sealed class PropMesh
{
    internal readonly PerlinNoise Noise;

    public PropMesh(PerlinNoise noise) => Noise = noise;

    public readonly List<(Vector3 p, Vector3 n, Vector2 t)> V = new();
    public float[] Shade = Array.Empty<float>();
    public int Ground;

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Vector3? outward = null, Vector3? smooth = null)
    {
        var n = (b - a).Cross(c - a);
        if (n.NormSquared() < 1e-14f) return;
        n = n.Normalized();
        if (outward is { } o && n.Dot(o) < 0) { n = -n; (b, c) = (c, b); (tb, tc) = (tc, tb); }
        V.Add((a, n, ta)); V.Add((b, n, tb)); V.Add((c, n, tc));
    }

    public void TriN(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ta, Vector2 tb, Vector2 tc)
    {
        var n = (b - a).Cross(c - a);
        if (n.NormSquared() < 1e-14f) return;
        if (n.Dot(na + nb + nc) < 0) { (b, c) = (c, b); (nb, nc) = (nc, nb); (tb, tc) = (tc, tb); }
        V.Add((a, na, ta)); V.Add((b, nb, tb)); V.Add((c, nc, tc));
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, M m, Vector3 inside, float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1)
    {
        var o = (a + b + c + d) / 4 - inside;
        Tri(a, b, c, Uv(m, u0, v0), Uv(m, u1, v0), Uv(m, u1, v1), o);
        Tri(a, c, d, Uv(m, u0, v0), Uv(m, u1, v1), Uv(m, u0, v1), o);
    }

    public void Sheet(Vector3 a, Vector3 b, Vector3 c, Vector3 d, M m, Vector3 inside, float sag, int nu = 6, int nv = 4, float ru = 1, Func<float, float, Vector3>? bump = null)
    {
        Vector3 P(float u, float v)
        {
            var top = a.Lerp(b, u); var bot = d.Lerp(c, u);
            var p = top.Lerp(bot, v);
            p += Axis.Down * sag * Mathf.Sin(Mathf.Pi * u) * (0.35f + 0.65f * Mathf.Sin(Mathf.Pi * Mathf.Min(1, v * 1.2f)));
            if (bump != null) p += bump(u, v);
            return p;
        }
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                float u0 = i / (float)nu, u1 = (i + 1f) / nu, v0 = j / (float)nv, v1 = (j + 1f) / nv;
                var p00 = P(u0, v0); var p10 = P(u1, v0); var p11 = P(u1, v1); var p01 = P(u0, v1);
                var o = (p00 + p11) / 2 - inside;
                Tri(p00, p10, p11, Uv(m, u0, v0), Uv(m, u1, v0), Uv(m, u1, v1), o);
                Tri(p00, p11, p01, Uv(m, u0, v0), Uv(m, u1, v1), Uv(m, u0, v1), o);
            }
    }

    public void Box(Vector3 c, Vector3 size, Basis rot, M m, M? top = null)
    {
        var h = size / 2;
        Vector3 P(float x, float y, float z) => c + rot * new Vector3(x * h.X, y * h.Y, z * h.Z);
        var faces = new[]
        {
            (new[] { P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1) }, top ?? m),
            (new[] { P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1) }, m),
            (new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1) }, m),
            (new[] { P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) }, m),
            (new[] { P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1) }, m),
            (new[] { P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1) }, m),
        };
        foreach (var (q, mm) in faces) Quad(q[0], q[1], q[2], q[3], mm, c);
    }

    public void Cylinder(Vector3 a, Vector3 b, float r, M m, int seg = 9, M? ends = null, float r2 = -1, bool capA = true, bool capB = true)
    {
        if (r2 < 0) r2 = r;
        var axis = (b - a).Normalized();
        var side = Mathf.Abs(axis.Y) < 0.9f ? axis.Cross(Axis.Up).Normalized() : axis.Cross(Axis.Right).Normalized();
        var up = side.Cross(axis);
        Vector3 Dir(int i) { float t = i * Mathf.Tau / seg; return side * Mathf.Cos(t) + up * Mathf.Sin(t); }
        float len = (b - a).Norm();
        for (int i = 0; i < seg; i++)
        {
            var d0 = Dir(i); var d1 = Dir(i + 1);
            float u0 = i / (float)seg, u1 = (i + 1f) / seg, vl = Mathf.Min(1, len * 2);
            TriN(a + d0 * r, b + d0 * r2, b + d1 * r2, d0, d0, d1, Uv(m, u0, 0), Uv(m, u0, vl), Uv(m, u1, vl));
            TriN(a + d0 * r, b + d1 * r2, a + d1 * r, d0, d1, d1, Uv(m, u0, 0), Uv(m, u1, vl), Uv(m, u1, 0));
            var e = ends ?? m;
            Vector2 EU(Vector3 d) => Uv(e, 0.5f + 0.45f * d.Dot(side), 0.5f + 0.45f * d.Dot(up));
            if (capA) Tri(a, a + d1 * r, a + d0 * r, Uv(e, 0.5f, 0.5f), EU(d1), EU(d0), -axis);
            if (capB) Tri(b, b + d0 * r2, b + d1 * r2, Uv(e, 0.5f, 0.5f), EU(d0), EU(d1), axis);
        }
    }

    public void Lump(Vector3 at, Vector3 size, Basis rot, M m, int seed, float rough = 0.18f, float boxy = 0)
    {
        var noise = Noise(seed, 1.8f);
        float t = (1 + Mathf.Sqrt(5)) / 2;
        var vs = new List<Vector3> { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
        for (int i = 0; i < vs.Count; i++) vs[i] = vs[i].Normalized();
        var fs = new List<(int, int, int)> { (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1) };
        var midCache = new Dictionary<(int, int), int>();
        int Mid(int a, int b)
        {
            var k = a < b ? (a, b) : (b, a);
            if (midCache.TryGetValue(k, out var i)) return i;
            vs.Add(((vs[a] + vs[b]) / 2).Normalized());
            return midCache[k] = vs.Count - 1;
        }
        var fs2 = new List<(int, int, int)>();
        foreach (var (a, b, c) in fs) { int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a); fs2.Add((a, ab, ca)); fs2.Add((b, bc, ab)); fs2.Add((c, ca, bc)); fs2.Add((ab, bc, ca)); }
        Vector3 Shape(Vector3 d)
        {
            var q = d;
            if (boxy > 0) q = new Vector3(Mathf.Sign(d.X) * Mathf.Pow(Mathf.Abs(d.X), 1 - boxy), Mathf.Sign(d.Y) * Mathf.Pow(Mathf.Abs(d.Y), 1 - boxy), Mathf.Sign(d.Z) * Mathf.Pow(Mathf.Abs(d.Z), 1 - boxy));
            float k = 1 + rough * noise.GetNoise3D(d.X, d.Y, d.Z);
            var p = at + rot * new Vector3(q.X * size.X, q.Y * size.Y, q.Z * size.Z) / 2 * k;
            if (p.Y < 0) p.Y = 0;
            return p;
        }
        var pts = vs.Select(Shape).ToArray();
        var nrm = new Vector3[pts.Length];
        foreach (var (a, b, c) in fs2) { var n = (pts[b] - pts[a]).Cross(pts[c] - pts[a]); if (n.Dot(pts[a] - at) < 0) n = -n; nrm[a] += n; nrm[b] += n; nrm[c] += n; }
        for (int i = 0; i < nrm.Length; i++) nrm[i] = nrm[i].NormSquared() > 0 ? nrm[i].Normalized() : Axis.Up;
        Vector2 T(Vector3 d) => Uv(m, 0.5f + 0.48f * d.X, 0.5f + 0.48f * (d.Z * 0.7f + d.Y * 0.3f));
        foreach (var (a, b, c) in fs2) TriN(pts[a], pts[b], pts[c], nrm[a], nrm[b], nrm[c], T(vs[a]), T(vs[b]), T(vs[c]));
    }

    public void GroundPatch(float hx, float hz, int n = 36)
    {
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float u0 = i / (float)n, u1 = (i + 1f) / n, v0 = j / (float)n, v1 = (j + 1f) / n;
                float cu = (u0 + u1) - 1, cv = (v0 + v1) - 1;
                if (cu * cu + cv * cv > 1.15f) continue;
                Vector3 P(float u, float v) => new((u * 2 - 1) * hx, 0.004f, (v * 2 - 1) * hz);
                Tri(P(u0, v0), P(u1, v0), P(u1, v1), GroundUv(u0, v0), GroundUv(u1, v0), GroundUv(u1, v1), Axis.Up);
                Tri(P(u0, v0), P(u1, v1), P(u0, v1), GroundUv(u0, v0), GroundUv(u1, v1), GroundUv(u0, v1), Axis.Up);
            }
        Ground = V.Count;
    }

    public Box3 Bounds()
    {
        var lo = V[0].p; var hi = V[0].p;
        foreach (var (p, _, _) in V) { lo = lo.Min(p); hi = hi.Max(p); }
        return new Box3(lo, hi - lo);
    }

    public Vector3 Settle()
    {
        var b = Bounds();
        var shift = new Vector3(-(b.Position.X + b.Size.X / 2), -b.Position.Y, -(b.Position.Z + b.Size.Z / 2));
        for (int i = 0; i < V.Count; i++) V[i] = (V[i].p + shift, V[i].n, V[i].t);
        return shift;
    }

    public void Bake()
    {
        var b = Bounds();
        const int G = 128;
        var hmap = new float[G * G];
        float cell = Mathf.Max(b.Size.X, b.Size.Z) / (G - 1);
        int Gx(float x) => Math.Clamp((int)((x - b.Position.X) / cell), 0, G - 1);
        int Gz(float z) => Math.Clamp((int)((z - b.Position.Z) / cell), 0, G - 1);
        for (int i = 0; i < V.Count; i += 3)
        {
            var p0 = V[i].p; var p1 = V[i + 1].p; var p2 = V[i + 2].p;
            int steps = Math.Clamp((int)(Mathf.Max((p1 - p0).Norm(), (p2 - p0).Norm()) / cell) + 1, 1, 40);
            for (int s = 0; s <= steps; s++)
                for (int t = 0; t <= steps - s; t++)
                {
                    var p = p0 + (p1 - p0) * (s / (float)steps) + (p2 - p0) * (t / (float)steps);
                    int k = Gz(p.Z) * G + Gx(p.X);
                    if (p.Y > hmap[k]) hmap[k] = p.Y;
                }
        }
        float H(float x, float z) => x < b.Position.X || z < b.Position.Z || x > b.End.X || z > b.End.Z ? 0 : hmap[Gz(z) * G + Gx(x)];
        var light = new Vector3(-0.55f, 0.7f, 0.45f).Normalized();
        Shade = new float[V.Count];
        for (int i = 0; i < V.Count; i++)
        {
            var (p, n, _) = V[i];
            float occl = 0;
            for (int d = 0; d < 8; d++)
            {
                float ang = d * Mathf.Tau / 8;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float worst = 0;
                for (int s = 1; s <= 5; s++)
                {
                    float dist = 0.035f * s * s * 0.5f + 0.02f;
                    float rise = H(p.X + dir.X * dist, p.Z + dir.Y * dist) - (p.Y + 0.005f);
                    if (rise > 0) worst = Mathf.Max(worst, rise / Mathf.Sqrt(rise * rise + dist * dist));
                }
                occl += worst;
            }
            float sky = 1 - occl / 8 * 0.9f;
            float contact = i < Ground || n.Y > 0.7f ? 1 : 0.6f + 0.4f * Sm(0, 0.07f, p.Y);
            float key = 0.8f + 0.28f * Mathf.Max(0, n.Dot(light)) - 0.08f * Mathf.Max(0, -n.Y);
            Shade[i] = Mathf.Clamp(sky * contact * key, 0.22f, 1.08f);
        }
    }

    public string Obj()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("# Firme's Effects and Props: made by the app (Props.cs). v x y z then the baked shade\n");
        for (int i = 0; i < V.Count; i++)
        {
            var p = V[i].p; var s = Shade.Length == V.Count ? Shade[i] : 1;
            string sh = s.ToString("0.###", inv);
            sb.Append("v ").Append(p.X.ToString("0.####", inv)).Append(' ').Append(p.Y.ToString("0.####", inv)).Append(' ').Append(p.Z.ToString("0.####", inv))
                .Append(' ').Append(sh).Append(' ').Append(sh).Append(' ').Append(sh).Append('\n');
        }
        foreach (var (_, _, t) in V) sb.Append("vt ").Append(t.X.ToString("0.#####", inv)).Append(' ').Append((1 - t.Y).ToString("0.#####", inv)).Append('\n');
        foreach (var (_, n, _) in V) sb.Append("vn ").Append(n.X.ToString("0.###", inv)).Append(' ').Append(n.Y.ToString("0.###", inv)).Append(' ').Append(n.Z.ToString("0.###", inv)).Append('\n');
        for (int i = 0; i < V.Count; i += 3) sb.Append($"f {i + 1}/{i + 1}/{i + 1} {i + 2}/{i + 2}/{i + 2} {i + 3}/{i + 3}/{i + 3}\n");
        return sb.ToString();
    }
}

public readonly record struct Box3(Vector3 Position, Vector3 Size)
{
    public Vector3 End => Position + Size;
}
