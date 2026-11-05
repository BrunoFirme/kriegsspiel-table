namespace KTable.Core.Model;

public enum NameKind { Plain, Place, Water, Area }

public static class GroundNames
{
    public const double AreaLetter = 0.6, WaterLetter = 0.4, PlaceLetter = 0.5, Advance = 0.7;
    const double AreaShare = 0.6, AreaSpread = 3, WaterReach = 2, NarrowRatio = 0.35;
    const int MostPatchCells = 60000, ShoreScan = 64, Headings = 8;
    static readonly (int dx, int dy)[] Four = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    public static NameKind KindOf(byte kind) => kind switch
    {
        GroundKinds.None => NameKind.Plain,
        GroundKinds.Town => NameKind.Place,
        GroundKinds.Water => NameKind.Water,
        _ => NameKind.Area,
    };

    public static double Letter(NameKind kind) => kind switch
    {
        NameKind.Area => AreaLetter,
        NameKind.Water => WaterLetter,
        _ => PlaceLetter,
    };

    public static double Length(string text, NameKind kind) => Math.Max(1, text.Trim().Length) * Letter(kind) * Advance;

    public static (NameKind Kind, List<(double u, double v)> Baseline) Place(ReliefGrid g, double u, double v, int letters, double cellUnits)
    {
        int x = (int)Math.Round(Math.Clamp(u, 0, 1) * (g.Cols - 1)), y = (int)Math.Round(Math.Clamp(v, 0, 1) * (g.Rows - 1));
        byte kind = g.Kind[y * g.Cols + x];
        var here = Uv(g, x, y);
        if (kind is GroundKinds.None or GroundKinds.Town) return (KindOf(kind), [here]);
        double Natural(NameKind of) => Length(new string('n', letters), of) / cellUnits;
        if (kind == GroundKinds.Water && Narrowest(g, x, y, out double ax, out double ay) < Natural(NameKind.Water))
            return (NameKind.Water, Medial(g, x, y, ax, ay, Natural(NameKind.Water) * WaterReach));
        var (cx, cy, px, py, length, _) = Axis(Patch(g, x, y), g.Cols);
        double half = Math.Min(length * AreaShare, Natural(NameKind.Area) * AreaSpread) / 2;
        return (NameKind.Area, [Uv(g, cx - px * half, cy - py * half), Uv(g, cx + px * half, cy + py * half)]);
    }

    static double Narrowest(ReliefGrid g, int x, int y, out double ax, out double ay)
    {
        double best = double.MaxValue;
        ax = 1;
        ay = 0;
        for (int k = 0; k < Headings; k++)
        {
            double angle = k * Math.PI / Headings, dx = Math.Cos(angle), dy = Math.Sin(angle);
            int a = 0, b = 0;
            while (a < ShoreScan && Water(g, x + dx * (a + 1), y + dy * (a + 1))) a++;
            while (b < ShoreScan && Water(g, x - dx * (b + 1), y - dy * (b + 1))) b++;
            if (a + b + 1 >= best) continue;
            best = a + b + 1;
            ax = -dy;
            ay = dx;
        }
        return best;
    }

    static List<int> Patch(ReliefGrid g, int x, int y)
    {
        byte kind = g.Kind[y * g.Cols + x];
        var cells = new List<int>();
        var queue = new Queue<int>();
        var seen = new HashSet<int> { y * g.Cols + x };
        queue.Enqueue(y * g.Cols + x);
        while (queue.Count > 0 && cells.Count < MostPatchCells)
        {
            int i = queue.Dequeue();
            cells.Add(i);
            int cx = i % g.Cols, cy = i / g.Cols;
            foreach (var (dx, dy) in Four)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= g.Cols || ny >= g.Rows) continue;
                int j = ny * g.Cols + nx;
                if (g.Kind[j] != kind || !seen.Add(j)) continue;
                queue.Enqueue(j);
            }
        }
        return cells;
    }

    static (double cx, double cy, double ax, double ay, double length, double width) Axis(List<int> cells, int cols)
    {
        double cx = 0, cy = 0;
        foreach (int i in cells) { cx += i % cols; cy += i / cols; }
        cx /= cells.Count;
        cy /= cells.Count;
        double sxx = 0, sxy = 0, syy = 0;
        foreach (int i in cells)
        {
            double dx = i % cols - cx, dy = i / cols - cy;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }
        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        double ax = Math.Cos(angle), ay = Math.Sin(angle);
        double lo = double.MaxValue, hi = double.MinValue, wlo = double.MaxValue, whi = double.MinValue;
        foreach (int i in cells)
        {
            double dx = i % cols - cx, dy = i / cols - cy;
            double along = dx * ax + dy * ay, across = -dx * ay + dy * ax;
            lo = Math.Min(lo, along);
            hi = Math.Max(hi, along);
            wlo = Math.Min(wlo, across);
            whi = Math.Max(whi, across);
        }
        return (cx, cy, ax, ay, hi - lo + 1, whi - wlo + 1);
    }

    static List<(double u, double v)> Medial(ReliefGrid g, int x, int y, double ax, double ay, double reachCells)
    {
        var back = Walk(g, x, y, -ax, -ay, reachCells);
        var forth = Walk(g, x, y, ax, ay, reachCells);
        back.Reverse();
        var line = new List<(double u, double v)>();
        foreach (var (px, py) in back) line.Add(Uv(g, px, py));
        line.Add(Uv(g, x, y));
        foreach (var (px, py) in forth) line.Add(Uv(g, px, py));
        return line;
    }

    static List<(double x, double y)> Walk(ReliefGrid g, int x, int y, double dx, double dy, double reachCells)
    {
        var points = new List<(double x, double y)>();
        double px = x, py = y, gone = 0;
        while (gone < reachCells)
        {
            double nx = px + dx * 2, ny = py + dy * 2;
            if (!Centred(g, nx, ny, -dy, dx, out double mx, out double my)) break;
            double sx = mx - px, sy = my - py, step = Math.Sqrt(sx * sx + sy * sy);
            if (step < 0.5) break;
            dx = sx / step;
            dy = sy / step;
            px = mx;
            py = my;
            gone += step;
            points.Add((px, py));
        }
        return points;
    }

    static bool Centred(ReliefGrid g, double x, double y, double nx, double ny, out double mx, out double my)
    {
        mx = x;
        my = y;
        if (!Water(g, x, y)) return false;
        int a = 0, b = 0;
        while (a < ShoreScan && Water(g, x + nx * (a + 1), y + ny * (a + 1))) a++;
        while (b < ShoreScan && Water(g, x - nx * (b + 1), y - ny * (b + 1))) b++;
        double shift = (a - b) / 2.0;
        mx = x + nx * shift;
        my = y + ny * shift;
        return true;
    }

    static bool Water(ReliefGrid g, double x, double y)
    {
        int cx = (int)Math.Round(x), cy = (int)Math.Round(y);
        return cx >= 0 && cy >= 0 && cx < g.Cols && cy < g.Rows && g.Kind[cy * g.Cols + cx] == GroundKinds.Water;
    }

    static (double u, double v) Uv(ReliefGrid g, double x, double y) => (Math.Clamp(x / (g.Cols - 1), 0, 1), Math.Clamp(y / (g.Rows - 1), 0, 1));
}
