using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

internal static class PaintCarry
{
    const float Reach = 0.08f;
    const int CellsAside = 48;

    public static bool Reshaped(MeshData from, byte[] fromPaint, int fw, int fh, MeshData to, byte[] toPaint, int tw, int th)
    {
        var a = from.Bounds();
        var b = to.Bounds();
        var k = new Vector3(b.Size.X / Mathf.Max(1e-6f, a.Size.X), b.Size.Y / Mathf.Max(1e-6f, a.Size.Y), b.Size.Z / Mathf.Max(1e-6f, a.Size.Z));
        var fitted = new MeshData { V = from.V.Select(v => b.Position + (v - a.Position) * k).ToList(), UV = from.UV };
        return Carry(fitted, fromPaint, fw, fh, to, toPaint, tw, th);
    }

    public static bool Carry(MeshData from, byte[] fromPaint, int fw, int fh, MeshData to, byte[] toPaint, int tw, int th)
    {
        if (from.Tris == 0 || to.Tris == 0 || fromPaint.Length != fw * fh * 4 || toPaint.Length != tw * th * 4) return false;
        if (!fromPaint.AsSpan().ContainsAnyExcept((byte)0)) return false;
        var grid = new TriGrid(to);
        float far = Mathf.Max(1e-5f, grid.Span * Reach);
        bool any = false;
        var scale = new Vector2(fw, fh);
        var clip = new Rect2I(0, 0, fw - 1, fh - 1);
        for (int t = 0; t < from.Tris; t++)
        {
            int k = t * 3;
            var (a, b, c) = (from.V[k], from.V[k + 1], from.V[k + 2]);
            ModelImport.Raster(from.UV[k] * scale, from.UV[k + 1] * scale, from.UV[k + 2] * scale, Math.Max(fw, fh), clip, (x, y, w0, w1, w2) =>
            {
                if (x >= fw || y >= fh) return;
                int o = (y * fw + x) * 4;
                if (fromPaint[o + 3] == 0) return;
                var p = a * w0 + b * w1 + c * w2;
                if (grid.Nearest(p, far) is not var (tri, u, v, wt)) return;
                int q = tri * 3;
                var uv = to.UV[q] * u + to.UV[q + 1] * v + to.UV[q + 2] * wt;
                int tx = Math.Clamp((int)(uv.X * tw), 0, tw - 1), ty = Math.Clamp((int)(uv.Y * th), 0, th - 1);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = tx + dx, sy = ty + dy;
                        if (sx < 0 || sy < 0 || sx >= tw || sy >= th) continue;
                        int d = (sy * tw + sx) * 4;
                        if ((dx != 0 || dy != 0) && toPaint[d + 3] >= fromPaint[o + 3]) continue;
                        toPaint[d] = fromPaint[o];
                        toPaint[d + 1] = fromPaint[o + 1];
                        toPaint[d + 2] = fromPaint[o + 2];
                        toPaint[d + 3] = fromPaint[o + 3];
                    }
                any = true;
            });
        }
        return any;
    }

    sealed class TriGrid
    {
        readonly MeshData _m;
        readonly Dictionary<(int, int, int), List<int>> _cells = new();
        readonly Vector3 _lo;
        readonly float _cell;
        public readonly float Span;

        public TriGrid(MeshData m)
        {
            _m = m;
            var b = m.Bounds();
            Span = Mathf.Max(1e-5f, Mathf.Max(b.Size.X, Mathf.Max(b.Size.Y, b.Size.Z)));
            _cell = Span / CellsAside;
            _lo = b.Position;
            for (int t = 0; t < m.Tris; t++)
            {
                var lo = m.V[t * 3].Min(m.V[t * 3 + 1]).Min(m.V[t * 3 + 2]);
                var hi = m.V[t * 3].Max(m.V[t * 3 + 1]).Max(m.V[t * 3 + 2]);
                var (i0, j0, k0) = Key(lo);
                var (i1, j1, k1) = Key(hi);
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                        for (int k = k0; k <= k1; k++)
                        {
                            if (!_cells.TryGetValue((i, j, k), out var l)) _cells[(i, j, k)] = l = new List<int>();
                            l.Add(t);
                        }
            }
        }

        (int, int, int) Key(Vector3 p) => ((int)Mathf.Floor((p.X - _lo.X) / _cell), (int)Mathf.Floor((p.Y - _lo.Y) / _cell), (int)Mathf.Floor((p.Z - _lo.Z) / _cell));

        public (int tri, float u, float v, float w)? Nearest(Vector3 p, float far)
        {
            var (ci, cj, ck) = Key(p);
            int rings = Math.Max(1, (int)Mathf.Ceil(far / _cell));
            (int, float, float, float)? best = null;
            float bestD = far * far;
            for (int r = 0; r <= rings; r++)
            {
                if (best != null && (r - 1) * _cell > Mathf.Sqrt(bestD)) break;
                for (int i = ci - r; i <= ci + r; i++)
                    for (int j = cj - r; j <= cj + r; j++)
                        for (int k = ck - r; k <= ck + r; k++)
                        {
                            if (Math.Max(Math.Abs(i - ci), Math.Max(Math.Abs(j - cj), Math.Abs(k - ck))) != r || !_cells.TryGetValue((i, j, k), out var l)) continue;
                            foreach (var t in l)
                            {
                                var (q, u, v, w) = Closest(p, _m.V[t * 3], _m.V[t * 3 + 1], _m.V[t * 3 + 2]);
                                float d = (q - p).LengthSquared();
                                if (d < bestD) { bestD = d; best = (t, u, v, w); }
                            }
                        }
            }
            return best;
        }
    }

    static (Vector3, float, float, float) Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return (a, 1, 0, 0);
        var bp = p - b;
        float d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return (b, 0, 1, 0);
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            float v = d1 / (d1 - d3);
            return (a + ab * v, 1 - v, v, 0);
        }
        var cp = p - c;
        float d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return (c, 0, 0, 1);
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            float w = d2 / (d2 - d6);
            return (a + ac * w, 1 - w, 0, w);
        }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            float w = (d4 - d3) / (d4 - d3 + (d5 - d6));
            return (b + (c - b) * w, 0, 1 - w, w);
        }
        float den = 1 / (va + vb + vc);
        float vv = vb * den, ww = vc * den;
        return (a + ab * vv + ac * ww, 1 - vv - ww, vv, ww);
    }
}
