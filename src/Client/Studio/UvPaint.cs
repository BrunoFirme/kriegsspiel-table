namespace KTable.Client.Studio;

public static class UvPaint
{
    const float Reach = 0.75f;

    public static int Fill(byte[] paint, int w, int h, IReadOnlyList<float> uv, IEnumerable<int> tris, byte r, byte g, byte b, byte a)
    {
        var done = new bool[w * h];
        int painted = 0;
        foreach (var t in tris)
        {
            int i = t * 6;
            if (i + 5 >= uv.Count) continue;
            float ax = uv[i] * w, ay = uv[i + 1] * h, bx = uv[i + 2] * w, by = uv[i + 3] * h, cx = uv[i + 4] * w, cy = uv[i + 5] * h;
            if (!float.IsFinite(ax + ay + bx + by + cx + cy)) continue;
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx)) - 1)), x1 = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx)) + 1));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy)) - 1)), y1 = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy)) + 1));
            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (MathF.Abs(area) < 1e-9f) continue;
            float sign = area > 0 ? 1 : -1;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    if (Edge(ax, ay, bx, by, px, py, sign) < -Reach || Edge(bx, by, cx, cy, px, py, sign) < -Reach || Edge(cx, cy, ax, ay, px, py, sign) < -Reach) continue;
                    int k = y * w + x;
                    if (done[k]) continue;
                    done[k] = true;
                    int o = k * 4;
                    paint[o] = r; paint[o + 1] = g; paint[o + 2] = b; paint[o + 3] = a;
                    painted++;
                }
        }
        return painted;
    }

    static float Edge(float x0, float y0, float x1, float y1, float px, float py, float sign)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9f) return 0;
        return sign * (dx * (py - y0) - dy * (px - x0)) / len;
    }

    public static (byte r, byte g, byte b, byte a) Premultiplied(float r, float g, float b, float a) =>
        ((byte)Math.Round(Math.Clamp(r * a, 0, 1) * 255), (byte)Math.Round(Math.Clamp(g * a, 0, 1) * 255), (byte)Math.Round(Math.Clamp(b * a, 0, 1) * 255), (byte)Math.Round(Math.Clamp(a, 0, 1) * 255));
}
