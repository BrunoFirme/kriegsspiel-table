namespace KTable.Client.Studio;

public static class FloodFill
{
    public static (byte[] mask, int w, int h, int x, int y)? Region(byte[] d, int w, int h, int sx, int sy, float tol)
    {
        if (sx < 0 || sy < 0 || sx >= w || sy >= h) return null;
        int si = (sy * w + sx) * 4;
        int r0 = d[si], g0 = d[si + 1], b0 = d[si + 2], a0 = d[si + 3];
        int lim = (int)(tol * tol * 255 * 255 * 4);
        bool Like(int i)
        {
            int dr = d[i] - r0, dg = d[i + 1] - g0, db = d[i + 2] - b0, da = d[i + 3] - a0;
            if (a0 < 8 && d[i + 3] < 8) return true;
            return dr * dr + dg * dg + db * db + da * da <= lim;
        }
        var inside = new bool[w * h];
        var stack = new Stack<int>();
        stack.Push(sy * w + sx);
        int minX = sx, maxX = sx, minY = sy, maxY = sy, count = 0;
        while (stack.Count > 0)
        {
            int k = stack.Pop();
            if (inside[k]) continue;
            int y = k / w, x = k % w;
            int xl = x, xr = x;
            while (xl > 0 && !inside[y * w + xl - 1] && Like((y * w + xl - 1) * 4)) xl--;
            while (xr < w - 1 && !inside[y * w + xr + 1] && Like((y * w + xr + 1) * 4)) xr++;
            for (int xx = xl; xx <= xr; xx++)
            {
                inside[y * w + xx] = true; count++;
                if (y > 0 && !inside[(y - 1) * w + xx] && Like(((y - 1) * w + xx) * 4)) stack.Push((y - 1) * w + xx);
                if (y < h - 1 && !inside[(y + 1) * w + xx] && Like(((y + 1) * w + xx) * 4)) stack.Push((y + 1) * w + xx);
            }
            minX = Math.Min(minX, xl); maxX = Math.Max(maxX, xr); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (count == 0) return null;
        minX = Math.Max(0, minX - 1); minY = Math.Max(0, minY - 1); maxX = Math.Min(w - 1, maxX + 1); maxY = Math.Min(h - 1, maxY + 1);
        int mw = maxX - minX + 1, mh = maxY - minY + 1;
        var md = new byte[mw * mh * 4];
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                bool on = inside[y * w + x]
                    || (x > 0 && inside[y * w + x - 1]) || (x < w - 1 && inside[y * w + x + 1])
                    || (y > 0 && inside[(y - 1) * w + x]) || (y < h - 1 && inside[(y + 1) * w + x]);
                if (!on) continue;
                int o = ((y - minY) * mw + (x - minX)) * 4;
                md[o] = md[o + 1] = md[o + 2] = 255; md[o + 3] = 255;
            }
        return (md, mw, mh, minX, minY);
    }
}
