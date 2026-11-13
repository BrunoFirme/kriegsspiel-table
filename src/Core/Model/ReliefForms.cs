namespace KTable.Core.Model;

public static class ReliefForms
{
    public const string Round = "round", Sharp = "sharp", Flat = "flat";
    public static readonly IReadOnlyList<string> Profiles = [Round, Sharp, Flat];
    const double FlatAcross = 0.6, SlopeAcross = 0.5;

    public static double Share(double d, string profile)
    {
        if (d >= 1) return 0;
        return profile == Sharp ? 1 - d : ReliefBrush.Falloff(d, profile == Flat ? FlatAcross : 0);
    }

    static (double X, double Y) Cell(ReliefGrid g, (double u, double v) p) => (p.u * (g.Cols - 1), p.v * (g.Rows - 1));

    static CellBox Around(ReliefGrid g, IEnumerable<(double X, double Y)> points, double reach)
    {
        var all = points.ToList();
        return new CellBox((int)Math.Floor(all.Min(p => p.X) - reach), (int)Math.Floor(all.Min(p => p.Y) - reach), (int)Math.Ceiling(all.Max(p => p.X) + reach),
            (int)Math.Ceiling(all.Max(p => p.Y) + reach)).Clip(g.Cols, g.Rows);
    }

    static void Add(ReliefGrid g, int x, int y, double by) => g.H[y * g.Cols + x] = (float)Math.Max(0, g.H[y * g.Cols + x] + by);

    public static CellBox Hill(ReliefGrid g, (double u, double v) summit, (double u, double v) foot, double height, string profile)
    {
        var (cx, cy) = Cell(g, summit);
        var (fx, fy) = Cell(g, foot);
        double r = Math.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
        if (r < 1) return CellBox.None;
        var box = Around(g, [(cx, cy)], r);
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++) Add(g, x, y, height * Share(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r, profile));
        return box;
    }

    static (double Away, double Along) From((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, span = dx * dx + dy * dy;
        double t = span < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / span, 0, 1);
        double ox = p.X - a.X - dx * t, oy = p.Y - a.Y - dy * t;
        return (Math.Sqrt(ox * ox + oy * oy), t);
    }

    public static CellBox Ridge(ReliefGrid g, IReadOnlyList<(double u, double v)> line, double halfWidthCells, double height, string profile)
    {
        if (line.Count < 2 || halfWidthCells < 0.5) return CellBox.None;
        var path = line.Select(p => Cell(g, p)).ToList();
        var box = Around(g, path, halfWidthCells);
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                double least = double.MaxValue;
                for (int i = 0; i + 1 < path.Count; i++) least = Math.Min(least, From((x, y), path[i], path[i + 1]).Away);
                Add(g, x, y, height * Share(least / halfWidthCells, profile));
            }
        return box;
    }

    public static CellBox Slope(ReliefGrid g, (double u, double v) top, (double u, double v) foot, double halfWidthCells)
    {
        var a = Cell(g, top);
        var b = Cell(g, foot);
        if (halfWidthCells < 0.5 || Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 1) return CellBox.None;
        double from = g.HeightAt(top.u, top.v), to = g.HeightAt(foot.u, foot.v);
        var box = Around(g, [a, b], halfWidthCells);
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                var (away, along) = From((x, y), a, b);
                if (away >= halfWidthCells) continue;
                int i = y * g.Cols + x;
                g.H[i] += (float)((from + (to - from) * along - g.H[i]) * ReliefBrush.Falloff(away / halfWidthCells, SlopeAcross));
            }
        return box;
    }
}
