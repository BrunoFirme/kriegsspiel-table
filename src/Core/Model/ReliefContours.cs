namespace KTable.Core.Model;

public sealed record ContourLine(IReadOnlyList<(double u, double v)> Points, double Height);

public static class ReliefContours
{
    const int LeastMargin = 8, Coarsest = 33, Sweeps = 48;
    const double MarginShare = 0.25, StepsPerCell = 2, Eagerness = 1.6;

    public static CellBox Raise(ReliefGrid g, IReadOnlyList<ContourLine> lines)
    {
        var drawn = lines.Where(l => l.Points.Count >= 2).ToList();
        if (drawn.Count == 0) return CellBox.None;
        var cells = drawn.SelectMany(l => l.Points).Select(p => (X: p.u * (g.Cols - 1), Y: p.v * (g.Rows - 1))).ToList();
        double x0 = cells.Min(c => c.X), x1 = cells.Max(c => c.X), y0 = cells.Min(c => c.Y), y1 = cells.Max(c => c.Y);
        int margin = Math.Max(LeastMargin, (int)Math.Ceiling(Math.Max(x1 - x0, y1 - y0) * MarginShare));
        var box = new CellBox((int)Math.Floor(x0) - margin, (int)Math.Floor(y0) - margin, (int)Math.Ceiling(x1) + margin, (int)Math.Ceiling(y1) + margin).Clip(g.Cols, g.Rows);
        int w = box.Width, h = box.Height;
        var value = new float[w * h];
        var held = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int gx = box.X0 + x, gy = box.Y0 + y;
                value[y * w + x] = g.H[gy * g.Cols + gx];
                bool rim = x == 0 || y == 0 || x == w - 1 || y == h - 1, sheetEdge = gx == 0 || gy == 0 || gx == g.Cols - 1 || gy == g.Rows - 1;
                held[y * w + x] = rim && !sheetEdge;
            }
        foreach (var line in drawn) Trace(g, line, box, value, held);
        Solve(value, held, w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) g.H[(box.Y0 + y) * g.Cols + box.X0 + x] = Math.Max(0, value[y * w + x]);
        return box;
    }

    static void Trace(ReliefGrid g, ContourLine line, CellBox box, float[] value, bool[] held)
    {
        int w = box.Width;
        for (int i = 0; i + 1 < line.Points.Count; i++)
        {
            double ax = line.Points[i].u * (g.Cols - 1), ay = line.Points[i].v * (g.Rows - 1), bx = line.Points[i + 1].u * (g.Cols - 1), by = line.Points[i + 1].v * (g.Rows - 1);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) * StepsPerCell));
            for (int s = 0; s <= steps; s++)
            {
                int x = (int)Math.Round(ax + (bx - ax) * s / steps) - box.X0, y = (int)Math.Round(ay + (by - ay) * s / steps) - box.Y0;
                if (x < 0 || y < 0 || x >= w || y >= box.Height) continue;
                value[y * w + x] = (float)line.Height;
                held[y * w + x] = true;
            }
        }
    }

    static void Solve(float[] value, bool[] held, int w, int h)
    {
        if (Math.Min(w, h) > Coarsest) Seed(value, held, w, h);
        for (int sweep = 0; sweep < Sweeps; sweep++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (held[i]) continue;
                    float sum = 0;
                    int n = 0;
                    if (x > 0) { sum += value[i - 1]; n++; }
                    if (x < w - 1) { sum += value[i + 1]; n++; }
                    if (y > 0) { sum += value[i - w]; n++; }
                    if (y < h - 1) { sum += value[i + w]; n++; }
                    value[i] += (float)((sum / n - value[i]) * Eagerness);
                }
    }

    static void Seed(float[] value, bool[] held, int w, int h)
    {
        int cw = (w + 1) / 2, ch = (h + 1) / 2;
        var sums = new double[cw * ch];
        var counts = new int[cw * ch];
        var coarse = new float[cw * ch];
        var coarseHeld = new bool[cw * ch];
        for (int pass = 0; pass < 2; pass++)
        {
            Array.Clear(sums);
            Array.Clear(counts);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int c = y / 2 * cw + x / 2;
                    if (held[y * w + x] != (pass == 0) || (pass == 1 && coarseHeld[c])) continue;
                    sums[c] += value[y * w + x];
                    counts[c]++;
                }
            for (int c = 0; c < coarse.Length; c++)
            {
                if (counts[c] == 0) continue;
                coarse[c] = (float)(sums[c] / counts[c]);
                if (pass == 0) coarseHeld[c] = true;
            }
        }
        Solve(coarse, coarseHeld, cw, ch);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (!held[y * w + x]) value[y * w + x] = coarse[y / 2 * cw + x / 2];
    }
}
