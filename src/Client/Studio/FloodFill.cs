namespace KTable.Client.Studio;

public static class FloodFill
{
    public static (byte[] mask, int w, int h, int x, int y)? Region(byte[] d, int w, int h, int sx, int sy, float tol, bool[]? wall = null)
    {
        if (sx < 0 || sy < 0 || sx >= w || sy >= h || wall?[sy * w + sx] == true) return null;
        int si = (sy * w + sx) * 4;
        int r0 = d[si], g0 = d[si + 1], b0 = d[si + 2], a0 = d[si + 3];
        int lim = (int)(tol * tol * 255 * 255 * 4);
        bool Like(int k)
        {
            if (wall?[k] == true) return false;
            int i = k * 4;
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
            while (xl > 0 && !inside[y * w + xl - 1] && Like(y * w + xl - 1)) xl--;
            while (xr < w - 1 && !inside[y * w + xr + 1] && Like(y * w + xr + 1)) xr++;
            for (int xx = xl; xx <= xr; xx++)
            {
                inside[y * w + xx] = true;
                count++;
                if (y > 0 && !inside[(y - 1) * w + xx] && Like((y - 1) * w + xx)) stack.Push((y - 1) * w + xx);
                if (y < h - 1 && !inside[(y + 1) * w + xx] && Like((y + 1) * w + xx)) stack.Push((y + 1) * w + xx);
            }
            minX = Math.Min(minX, xl);
            maxX = Math.Max(maxX, xr);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        if (count == 0) return null;
        minX = Math.Max(0, minX - 1);
        minY = Math.Max(0, minY - 1);
        maxX = Math.Min(w - 1, maxX + 1);
        maxY = Math.Min(h - 1, maxY + 1);
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
                md[o] = md[o + 1] = md[o + 2] = 255;
                md[o + 3] = 255;
            }
        return (md, mw, mh, minX, minY);
    }

    public static List<List<(int X, int Y)>> Loops(byte[] mask, int w, int h, int leastHole)
    {
        int pw = w + 2, ph = h + 2;
        var solid = new bool[pw * ph];
        for (int y = 1; y <= h; y++)
            for (int x = 1; x <= w; x++) solid[y * pw + x] = mask[((y - 1) * w + x - 1) * 4 + 3] > 0;
        WashSmallHoles(solid, pw, ph, leastHole);
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < pw && y < ph && solid[y * pw + x];
        var next = new Dictionary<(int X, int Y), List<(int X, int Y)>>();
        void Side((int, int) from, (int, int) to)
        {
            if (!next.TryGetValue(from, out var list)) next[from] = list = new();
            list.Add(to);
        }
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                if (!Solid(x, y)) continue;
                if (!Solid(x, y - 1)) Side((x, y), (x + 1, y));
                if (!Solid(x + 1, y)) Side((x + 1, y), (x + 1, y + 1));
                if (!Solid(x, y + 1)) Side((x + 1, y + 1), (x, y + 1));
                if (!Solid(x - 1, y)) Side((x, y + 1), (x, y));
            }
        var loops = new List<(List<(int X, int Y)> Loop, long Twice)>();
        foreach (var start in next.Keys.OrderBy(k => k).ToList())
            while (next[start].Count > 0)
            {
                var loop = new List<(int X, int Y)> { start };
                var at = Take(next, start, null);
                for (var from = start; at != start; (from, at) = (at, Take(next, at, (at.X - from.X, at.Y - from.Y)))) loop.Add(at);
                loops.Add((Straight(loop), Twice(loop)));
            }
        var outer = loops.Where(l => l.Twice > 0).OrderByDescending(l => l.Twice).Select(l => l.Loop).FirstOrDefault();
        if (outer == null) return new();
        return new[] { outer }.Concat(loops.Where(l => l.Twice < 0).Select(l => l.Loop)).Select(l => l.Select(p => (p.X - 1, p.Y - 1)).ToList()).ToList();
    }

    static void WashSmallHoles(bool[] solid, int pw, int ph, int leastHole)
    {
        var seen = new bool[solid.Length];
        var todo = new Stack<int>();
        var part = new List<int>();
        for (int k = 0; k < solid.Length; k++)
        {
            if (solid[k] || seen[k]) continue;
            part.Clear();
            bool open = false;
            seen[k] = true;
            todo.Push(k);
            while (todo.Count > 0)
            {
                int i = todo.Pop(), x = i % pw, y = i / pw;
                part.Add(i);
                open |= x == 0 || y == 0 || x == pw - 1 || y == ph - 1;
                if (x > 0) Visit(i - 1);
                if (x < pw - 1) Visit(i + 1);
                if (y > 0) Visit(i - pw);
                if (y < ph - 1) Visit(i + pw);
            }
            if (!open && part.Count < leastHole) foreach (int i in part) solid[i] = true;
        }

        void Visit(int n)
        {
            if (solid[n] || seen[n]) return;
            seen[n] = true;
            todo.Push(n);
        }
    }

    static (int X, int Y) Take(Dictionary<(int X, int Y), List<(int X, int Y)>> next, (int X, int Y) at, (int X, int Y)? came)
    {
        var outs = next[at];
        int pick = 0;
        if (came is var (dx, dy) && outs.Count > 1)
        {
            var order = new[] { (-dy, dx), (dx, dy), (dy, -dx) };
            pick = Enumerable.Range(0, outs.Count).OrderBy(i => Array.IndexOf(order, (outs[i].X - at.X, outs[i].Y - at.Y)) is var r && r < 0 ? 9 : r).First();
        }
        var to = outs[pick];
        outs.RemoveAt(pick);
        return to;
    }

    static long Twice(List<(int X, int Y)> loop)
    {
        long sum = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var (a, b) = (loop[i], loop[(i + 1) % loop.Count]);
            sum += (long)a.X * b.Y - (long)b.X * a.Y;
        }
        return sum;
    }

    static List<(int X, int Y)> Straight(List<(int X, int Y)> loop)
    {
        var kept = new List<(int X, int Y)>();
        for (int i = 0; i < loop.Count; i++)
        {
            var (a, b, c) = (loop[(i + loop.Count - 1) % loop.Count], loop[i], loop[(i + 1) % loop.Count]);
            bool onward = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) == 0 && (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y) > 0;
            if (!onward) kept.Add(b);
        }
        return kept;
    }
}
