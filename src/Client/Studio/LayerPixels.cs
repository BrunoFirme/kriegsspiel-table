namespace KTable.Client.Studio;

public static class LayerPixels
{
    public static byte[] Over(byte[] da, float oa, byte[] db, float ob)
    {
        var o = new byte[da.Length];
        for (int k = 0; k < da.Length; k += 4)
        {
            float aa = da[k + 3] / 255f * oa, ab = db[k + 3] / 255f * ob;
            float ao = ab + aa * (1 - ab);
            if (ao <= 0) continue;
            for (int c = 0; c < 3; c++) o[k + c] = (byte)Math.Clamp((db[k + c] * ab + da[k + c] * aa * (1 - ab)) / ao, 0, 255);
            o[k + 3] = (byte)Math.Clamp(ao * 255, 0, 255);
        }
        return o;
    }

    public static int KeyOut(byte[] d, int w, int h, float tolerance)
    {
        float tol = Math.Max(0.06f, tolerance);
        int lim = (int)(tol * tol * 255 * 255 * 3);
        var gone = new bool[w * h];
        var stack = new Stack<int>();
        var seeds = new[] { 0, w - 1, (h - 1) * w, h * w - 1 }.Select(k => (d[k * 4], d[k * 4 + 1], d[k * 4 + 2])).Distinct().ToList();
        bool Like(int k)
        {
            if (d[k * 4 + 3] < 10) return true;
            foreach (var (r, g, b) in seeds)
            {
                int dr = d[k * 4] - r, dg = d[k * 4 + 1] - g, db = d[k * 4 + 2] - b;
                if (dr * dr + dg * dg + db * db <= lim) return true;
            }
            return false;
        }
        for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int k = stack.Pop();
            if (gone[k] || !Like(k)) continue;
            gone[k] = true;
            int x = k % w, y = k / w;
            if (x > 0) stack.Push(k - 1);
            if (x < w - 1) stack.Push(k + 1);
            if (y > 0) stack.Push(k - w);
            if (y < h - 1) stack.Push(k + w);
        }
        int n = 0;
        for (int k = 0; k < gone.Length; k++)
        {
            if (gone[k]) { d[k * 4 + 3] = 0; n++; continue; }
            int x = k % w, y = k / w;
            if ((x > 0 && gone[k - 1]) || (x < w - 1 && gone[k + 1]) || (y > 0 && gone[k - w]) || (y < h - 1 && gone[k + w])) d[k * 4 + 3] = (byte)(d[k * 4 + 3] / 2);
        }
        return n;
    }
}
