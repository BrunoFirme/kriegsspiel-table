namespace KTable.Core.Model;

public enum BrushMode { Raise, Lower, Smooth, Flatten, Paint }

public readonly record struct CellBox(int X0, int Y0, int X1, int Y1)
{
    public bool Empty => X1 < X0 || Y1 < Y0;
    public CellBox Union(CellBox o) => Empty ? o : o.Empty ? this : new(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1));
    public CellBox Grow(int by) => new(X0 - by, Y0 - by, X1 + by, Y1 + by);
    public CellBox Clip(int cols, int rows) => new(Math.Max(0, X0), Math.Max(0, Y0), Math.Min(cols - 1, X1), Math.Min(rows - 1, Y1));
    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;
    public static readonly CellBox None = new(0, 0, -1, -1);
}

public enum BrushTip { Round, Square, Diamond }

public sealed record Brush(BrushMode Mode, double Radius, double Strength, byte Kind = GroundKinds.None, double? Level = null, double Density = 1, byte Salt = 0, byte Detail = 0,
    double? Rise = null, double? Limit = null, double Hardness = 0, BrushTip Tip = BrushTip.Round);

public static class ReliefBrush
{
    public const double RisePerRadius = 0.15;
    const double PaintEdge = 0.85;
    const double SmoothReach = 0.25, HardestBrush = 0.95;
    const double EdgeNudge = 1e-6;

    public static readonly string[] Tips = ["round", "square", "diamond"];

    public static BrushTip TipOf(string? name) => (BrushTip)Math.Max(0, Array.IndexOf(Tips, name));

    public static double Off(BrushTip tip, double dx, double dy) => tip switch
    {
        BrushTip.Square => Math.Max(Math.Abs(dx), Math.Abs(dy)),
        BrushTip.Diamond => Math.Abs(dx) + Math.Abs(dy),
        _ => Math.Sqrt(dx * dx + dy * dy),
    };

    public static CellBox Dab(ReliefGrid g, double u, double v, double radiusCells, Brush b)
    {
        double cx = u * (g.Cols - 1), cy = v * (g.Rows - 1);
        double r = Math.Max(0.75, radiusCells);
        int x0 = Math.Max(0, (int)Math.Floor(cx - r)), x1 = Math.Min(g.Cols - 1, (int)Math.Ceiling(cx + r));
        int y0 = Math.Max(0, (int)Math.Floor(cy - r)), y1 = Math.Min(g.Rows - 1, (int)Math.Ceiling(cy + r));
        if (x1 < x0 || y1 < y0) return CellBox.None;
        float rise = (float)(b.Rise ?? Math.Clamp(b.Strength, 0, 1) * RisePerRadius * r / Math.Max(1, g.Cols - 1));
        float level = (float)(b.Level ?? g.HeightAt(u, v));
        int reach = Math.Max(1, (int)Math.Round(r * SmoothReach));
        var before = b.Mode == BrushMode.Smooth ? Window.Of(g, x0, y0, x1, y1, reach) : null;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double d = Off(b.Tip, x - cx, y - cy) / r;
                if (d > 1) continue;
                float fall = (float)Falloff(d, b.Hardness);
                int i = y * g.Cols + x;
                switch (b.Mode)
                {
                    case BrushMode.Raise:
                        g.H[i] = Raised(g.H[i], rise * fall, b.Limit);
                        break;
                    case BrushMode.Lower:
                        g.H[i] = Lowered(g.H[i], rise * fall, b.Limit);
                        break;
                    case BrushMode.Flatten:
                        g.H[i] += (level - g.H[i]) * Math.Min(1, fall * (float)Math.Clamp(b.Strength * 2, 0, 1));
                        break;
                    case BrushMode.Smooth:
                        g.H[i] += (before!.Mean(x, y) - g.H[i]) * fall * (float)Math.Clamp(b.Strength * 2, 0, 1);
                        break;
                    case BrushMode.Paint:
                        if (d <= PaintEdge && GroundScatter.Takes(x, y, b.Kind, b.Density)) Mark(g, i, b);
                        break;
                }
            }
        return new CellBox(x0, y0, x1, y1);
    }

    static float Raised(float h, float by, double? upTo) => upTo is not { } top ? h + by : h >= top ? h : (float)Math.Min(top, h + by);

    static float Lowered(float h, float by, double? downTo) => Math.Max(0, downTo is not { } floor ? h - by : h <= floor ? h : (float)Math.Max(floor, h - by));

    static double Spacing(double radiusCells) => Math.Max(0.5, radiusCells * 0.35);

    public static int Dabs(ReliefGrid g, (double u, double v) from, (double u, double v) to, double radiusCells)
    {
        double du = (to.u - from.u) * (g.Cols - 1), dv = (to.v - from.v) * (g.Rows - 1);
        return 1 + Math.Max(1, (int)Math.Ceiling(Math.Sqrt(du * du + dv * dv) / Spacing(radiusCells)));
    }

    static void Mark(ReliefGrid g, int i, Brush b)
    {
        byte laid = GroundKinds.Laid(g.Kind[i], b.Kind);
        bool took = laid == b.Kind || laid == GroundKinds.Bridge;
        g.Kind[i] = laid;
        if (!took) return;
        bool bare = laid == GroundKinds.None;
        g.Salt[i] = bare ? (byte)0 : b.Salt;
        g.Detail[i] = bare ? (byte)0 : b.Detail;
    }

    public static CellBox Fill(ReliefGrid g, double u, double v, byte kind, double density) => Fill(g, u, v, new Brush(BrushMode.Paint, 0, 1, kind, Density: density));

    public static CellBox Fill(ReliefGrid g, double u, double v, Brush b)
    {
        int sx = (int)Math.Round(Math.Clamp(u, 0, 1) * (g.Cols - 1)), sy = (int)Math.Round(Math.Clamp(v, 0, 1) * (g.Rows - 1));
        byte from = g.Kind[sy * g.Cols + sx];
        var seen = new bool[g.Kind.Length];
        var todo = new Stack<int>();
        todo.Push(sy * g.Cols + sx);
        seen[sy * g.Cols + sx] = true;
        int x0 = sx, x1 = sx, y0 = sy, y1 = sy;
        bool changed = false;
        while (todo.Count > 0)
        {
            int i = todo.Pop(), x = i % g.Cols, y = i / g.Cols;
            if (Lay(g, x, y, b)) changed = true;
            x0 = Math.Min(x0, x);
            x1 = Math.Max(x1, x);
            y0 = Math.Min(y0, y);
            y1 = Math.Max(y1, y);
            if (x > 0) Visit(i - 1);
            if (x < g.Cols - 1) Visit(i + 1);
            if (y > 0) Visit(i - g.Cols);
            if (y < g.Rows - 1) Visit(i + g.Cols);
        }
        return changed ? new CellBox(x0, y0, x1, y1) : CellBox.None;

        void Visit(int n)
        {
            if (seen[n] || g.Kind[n] != from) return;
            seen[n] = true;
            todo.Push(n);
        }
    }

    public static CellBox Shape(ReliefGrid g, IReadOnlyList<(double u, double v)> outline, byte kind, double density) =>
        Shape(g, outline, new Brush(BrushMode.Paint, 0, 1, kind, Density: density));

    public static CellBox Shape(ReliefGrid g, IReadOnlyList<(double u, double v)> outline, Brush b)
    {
        if (outline.Count < 3) return CellBox.None;
        var pts = outline.Select(p => (x: p.u * (g.Cols - 1), y: p.v * (g.Rows - 1))).ToArray();
        double top = pts.Min(p => p.y), bottom = pts.Max(p => p.y);
        if (bottom - top <= 2 * EdgeNudge) return CellBox.None;
        var box = CellBox.None;
        var cross = new List<double>();
        for (int y = Math.Max(0, (int)Math.Ceiling(top - EdgeNudge)); y <= Math.Min(g.Rows - 1, (int)Math.Floor(bottom + EdgeNudge)); y++)
        {
            double sy = Math.Clamp(y, top + EdgeNudge, bottom - EdgeNudge);
            cross.Clear();
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                if (pts[j].y > sy != pts[i].y > sy) cross.Add(pts[j].x + (sy - pts[j].y) / (pts[i].y - pts[j].y) * (pts[i].x - pts[j].x));
            cross.Sort();
            for (int k = 0; k + 1 < cross.Count; k += 2)
                for (int x = Math.Max(0, (int)Math.Ceiling(cross[k] - EdgeNudge)); x <= Math.Min(g.Cols - 1, (int)Math.Floor(cross[k + 1] + EdgeNudge)); x++)
                    if (Lay(g, x, y, b)) box = box.Union(new CellBox(x, y, x, y));
        }
        return box;
    }

    static bool Lay(ReliefGrid g, int x, int y, Brush b)
    {
        int i = y * g.Cols + x;
        if (!GroundScatter.Takes(x, y, b.Kind, b.Density)) return false;
        byte kind = g.Kind[i], salt = g.Salt[i], detail = g.Detail[i];
        Mark(g, i, b);
        return g.Kind[i] != kind || g.Salt[i] != salt || g.Detail[i] != detail;
    }

    public static double Falloff(double d, double hardness = 0)
    {
        double hard = Math.Clamp(hardness, 0, HardestBrush);
        if (d <= hard) return 1;
        double t = 1 - (d - hard) / (1 - hard);
        return t * t * (3 - 2 * t);
    }

    sealed class Window
    {
        int _x0, _y0, _w, _h, _reach;
        double[] _sums = Array.Empty<double>();

        public static Window Of(ReliefGrid g, int x0, int y0, int x1, int y1, int reach)
        {
            var w = new Window { _x0 = Math.Max(0, x0 - reach), _y0 = Math.Max(0, y0 - reach), _reach = reach };
            w._w = Math.Min(g.Cols - 1, x1 + reach) - w._x0 + 1;
            w._h = Math.Min(g.Rows - 1, y1 + reach) - w._y0 + 1;
            w._sums = new double[(w._w + 1) * (w._h + 1)];
            for (int y = 0; y < w._h; y++)
            {
                double row = 0;
                for (int x = 0; x < w._w; x++)
                {
                    row += g.H[(w._y0 + y) * g.Cols + w._x0 + x];
                    w._sums[(y + 1) * (w._w + 1) + x + 1] = w._sums[y * (w._w + 1) + x + 1] + row;
                }
            }
            return w;
        }

        public float Mean(int x, int y)
        {
            int ax = Math.Max(0, x - _reach - _x0), ay = Math.Max(0, y - _reach - _y0), bx = Math.Min(_w, x + _reach + 1 - _x0), by = Math.Min(_h, y + _reach + 1 - _y0);
            int across = _w + 1;
            double sum = _sums[by * across + bx] - _sums[ay * across + bx] - _sums[by * across + ax] + _sums[ay * across + ax];
            return (float)(sum / ((bx - ax) * (by - ay)));
        }
    }

    public static CellBox Stroke(ReliefGrid g, IReadOnlyList<(double u, double v)> points, double radiusCells, Brush b)
    {
        var box = CellBox.None;
        if (points.Count == 0) return box;
        var level = b.Mode == BrushMode.Flatten && b.Level == null ? b with { Level = g.HeightAt(points[0].u, points[0].v) } : b;
        double spacing = Spacing(radiusCells);
        var last = points[0];
        box = box.Union(Dab(g, last.u, last.v, radiusCells, level));
        for (int i = 1; i < points.Count; i++)
        {
            var p = points[i];
            double du = (p.u - last.u) * (g.Cols - 1), dv = (p.v - last.v) * (g.Rows - 1);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(du * du + dv * dv) / spacing));
            for (int k = 1; k <= steps; k++)
            {
                double t = k / (double)steps;
                box = box.Union(Dab(g, last.u + (p.u - last.u) * t, last.v + (p.v - last.v) * t, radiusCells, level));
            }
            last = p;
        }
        return box;
    }
}
