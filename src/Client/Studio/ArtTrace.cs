using System.Numerics;

namespace KTable.Client.Studio;

public static class ArtTrace
{
    public static (List<Vector2> pts, float cell) Outline(byte[] d, int w, int h, int grid = 192)
    {
        float k = Math.Max(w, h) / (float)grid;
        int gw = Math.Max(2, (int)Math.Ceiling(w / k)) + 2, gh = Math.Max(2, (int)Math.Ceiling(h / k)) + 2;
        var solid = new bool[gw * gh];
        for (int gy = 1; gy < gh - 1; gy++)
            for (int gx = 1; gx < gw - 1; gx++)
            {
                int px = Math.Min(w - 1, (int)((gx - 1 + 0.5f) * k)), py = Math.Min(h - 1, (int)((gy - 1 + 0.5f) * k));
                solid[gy * gw + gx] = d[(py * w + px) * 4 + 3] > 96;
            }
        List<Vector2> best = new();
        var seen = new bool[gw * gh];
        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 }, dy = { 0, 1, 1, 1, 0, -1, -1, -1 };
        for (int y = 1; y < gh - 1; y++)
            for (int x = 1; x < gw - 1; x++)
            {
                if (!solid[y * gw + x] || solid[y * gw + x - 1] || seen[y * gw + x]) continue;
                var pts = new List<Vector2>();
                int cx = x, cy = y, dir = 4, guard = gw * gh * 4;
                do
                {
                    seen[cy * gw + cx] = true;
                    pts.Add(new Vector2((cx - 1 + 0.5f) * k, (cy - 1 + 0.5f) * k));
                    int start = (dir + 6) % 8, i;
                    for (i = 0; i < 8; i++)
                    {
                        int nd = (start + i) % 8, nx = cx + dx[nd], ny = cy + dy[nd];
                        if (solid[ny * gw + nx])
                        {
                            cx = nx;
                            cy = ny;
                            dir = nd;
                            break;
                        }
                    }
                    if (i == 8) break;
                } while ((cx != x || cy != y) && --guard > 0);
                if (pts.Count > best.Count) best = pts;
            }
        return (best, k);
    }
}
