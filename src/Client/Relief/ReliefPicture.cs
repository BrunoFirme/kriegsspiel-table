using System.Numerics;

namespace KTable.Client.Relief;

public static class ReliefPicture
{
    public const int MostSide = 8192;
    const int DilatePasses = 6;
    const int Channels = 4;

    public static bool AsItIs(ReliefSource src) =>
        src.Triangles.Count == 0 || src.Fit is PictureFit.None || (src.Fit == PictureFit.Direct && !src.FlipU && !src.FlipV && !src.Swap);

    public static (byte[] Rgba, int W, int H) Fitted(ReliefSource src, byte[] rgba, int w, int h) =>
        src.Fit == PictureFit.Direct ? Oriented(src, rgba, w, h) : Resampled(src, rgba, w, h);

    static (byte[], int, int) Oriented(ReliefSource src, byte[] from, int tw, int th)
    {
        int w = src.Swap ? th : tw, h = src.Swap ? tw : th;
        var to = new byte[w * h * Channels];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int u = src.FlipU ? w - 1 - x : x, v = src.FlipV ? h - 1 - y : y;
                var (sx, sy) = src.Swap ? (v, u) : (u, v);
                Buffer.BlockCopy(from, (sy * tw + sx) * Channels, to, (y * w + x) * Channels, Channels);
            }
        return (to, w, h);
    }

    static (byte[], int, int) Resampled(ReliefSource src, byte[] from, int tw, int th)
    {
        int w = Math.Min(MostSide, Math.Max(tw, th));
        int h = Math.Max(1, (int)Math.Round(w * src.Depth / src.Width));
        if (h > MostSide) { w = Math.Max(1, w * MostSide / h); h = MostSide; }
        var to = new byte[w * h * Channels];
        var filled = new bool[w * h];
        foreach (var t in src.Triangles)
        {
            var a = new Vector2(t.A.X * w, t.A.Y * h); var b = new Vector2(t.B.X * w, t.B.Y * h); var c = new Vector2(t.C.X * w, t.C.Y * h);
            float area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            if (Math.Abs(area) < 1e-9f) continue;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((b.X - px) * (c.Y - py) - (c.X - px) * (b.Y - py)) / area;
                    float w1 = ((c.X - px) * (a.Y - py) - (a.X - px) * (c.Y - py)) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    float u = w0 * t.Ua.X + w1 * t.Ub.X + w2 * t.Uc.X, v = w0 * t.Ua.Y + w1 * t.Ub.Y + w2 * t.Uc.Y;
                    int sx = Math.Clamp((int)((u - MathF.Floor(u)) * tw), 0, tw - 1), sy = Math.Clamp((int)((v - MathF.Floor(v)) * th), 0, th - 1);
                    Buffer.BlockCopy(from, (sy * tw + sx) * Channels, to, (y * w + x) * Channels, Channels);
                    filled[y * w + x] = true;
                }
        }
        Dilate(to, filled, w, h);
        return (to, w, h);
    }

    static void Dilate(byte[] px, bool[] filled, int w, int h)
    {
        for (int pass = 0; pass < DilatePasses; pass++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (filled[i]) continue;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !filled[ny * w + nx]) continue;
                        Buffer.BlockCopy(px, (ny * w + nx) * Channels, px, i * Channels, Channels);
                        next[i] = true;
                        break;
                    }
                }
            Array.Copy(next, filled, filled.Length);
        }
    }
}
