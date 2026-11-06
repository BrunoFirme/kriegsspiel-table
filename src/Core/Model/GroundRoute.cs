namespace KTable.Core.Model;

public static class GroundRoute
{
    const double SlopeWeight = 8, Impassable = 0.7, WaterCost = 40, EasiestGround = 0.25;
    const int Margin = 24, SmoothReach = 3, PullReach = 150;
    const double PullSlack = 1.05;
    const double RiverGrowOverMetres = 5000, RiverGrowth = 3, PondDepthMetres = 1, PondRim = 0.25, PondCellRadius = 0.75;
    static readonly (int dx, int dy)[] Eight = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    public static double GroundFactor(byte kind) => GroundKinds.Name(kind) switch
    {
        "road" or "bridge" => EasiestGround,
        "town" => 0.5,
        "wood" => 2,
        "sand" => 3,
        "snow" => 4,
        "marsh" => 6,
        "rock" => 10,
        "water" => WaterCost,
        _ => 1,
    };

    public static CellBox Lay(ReliefGrid g, Brush brush, IReadOnlyList<(double u, double v)> waypoints, double radiusCells, double cellMetres, double metresPerHeightUnit)
    {
        if (waypoints.Count == 0) return CellBox.None;
        if (brush.Kind != GroundKinds.Water) return ReliefBrush.Stroke(g, Road(g, g.H, cellMetres / metresPerHeightUnit, waypoints), radiusCells, brush);
        var path = River(g, g.H, waypoints[0], out var pond, PondDepthMetres / metresPerHeightUnit);
        var box = CellBox.None;
        double metres = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            box = box.Union(ReliefBrush.Stroke(g, [path[i], path[i + 1]], RiverRadius(radiusCells, metres), brush));
            double dx = (path[i + 1].u - path[i].u) * (g.Cols - 1), dy = (path[i + 1].v - path[i].v) * (g.Rows - 1);
            metres += Math.Sqrt(dx * dx + dy * dy) * cellMetres;
        }
        foreach (int c in pond)
        {
            var (u, v) = Uv(g, (c % g.Cols, c / g.Cols));
            box = box.Union(ReliefBrush.Dab(g, u, v, PondCellRadius, brush));
        }
        return box;
    }

    public static List<(double u, double v)> Road(ReliefGrid g, float[] heights, double heightUnitsPerCell, IReadOnlyList<(double u, double v)> waypoints)
    {
        var path = new List<(double u, double v)>();
        if (waypoints.Count == 0) return path;
        var cells = waypoints.Select(w => Cell(g, w)).ToList();
        path.Add(Uv(g, cells[0]));
        for (int i = 1; i < cells.Count; i++)
        {
            var leg = Between(g, heights, heightUnitsPerCell, cells[i - 1], cells[i]);
            if (leg == null) { path.Add(Uv(g, cells[i])); continue; }
            foreach (var c in Pulled(g, heights, heightUnitsPerCell, leg).Skip(1)) path.Add(Uv(g, c));
        }
        return path;
    }

    static List<(int x, int y)> Pulled(ReliefGrid g, float[] heights, double heightUnitsPerCell, List<(int x, int y)> leg)
    {
        var along = new double[leg.Count];
        for (int k = 1; k < leg.Count; k++) along[k] = along[k - 1] + StepCost(g, heights, heightUnitsPerCell, leg[k - 1], leg[k]);
        var o = new List<(int x, int y)> { leg[0] };
        int i = 0;
        while (i < leg.Count - 1)
        {
            int j = i + 1;
            while (j + 1 < leg.Count && j + 1 - i <= PullReach && StraightEnough(g, heights, heightUnitsPerCell, leg[i], leg[j + 1], along[j + 1] - along[i])) j++;
            o.Add(leg[j]);
            i = j;
        }
        return o;
    }

    static bool StraightEnough(ReliefGrid g, float[] heights, double heightUnitsPerCell, (int x, int y) a, (int x, int y) b, double alongCost) =>
        Straight(g, heights, heightUnitsPerCell, a, b) is { } straight && straight <= alongCost * PullSlack;

    static double StepCost(ReliefGrid g, float[] heights, double heightUnitsPerCell, (int x, int y) a, (int x, int y) b)
    {
        double dx = b.x - a.x, dy = b.y - a.y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-9) return 0;
        double slope = Math.Abs(heights[b.y * g.Cols + b.x] - heights[a.y * g.Cols + a.x]) / (dist * heightUnitsPerCell);
        return slope > Impassable ? double.PositiveInfinity : dist * (1 + SlopeWeight * slope * slope) * GroundFactor(g.Kind[b.y * g.Cols + b.x]);
    }

    static double? Straight(ReliefGrid g, float[] heights, double heightUnitsPerCell, (int x, int y) a, (int x, int y) b)
    {
        double dx = b.x - a.x, dy = b.y - a.y;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) * 2));
        double total = 0;
        var prev = a;
        for (int s = 1; s <= steps; s++)
        {
            (int x, int y) c = ((int)Math.Round(a.x + dx * s / steps), (int)Math.Round(a.y + dy * s / steps));
            if (c == prev) continue;
            double step = StepCost(g, heights, heightUnitsPerCell, prev, c);
            if (double.IsPositiveInfinity(step)) return null;
            total += step;
            prev = c;
        }
        return total;
    }

    static List<(double u, double v)> Smoothed(ReliefGrid g, List<(double u, double v)> path)
    {
        if (path.Count < 3) return path;
        var o = new List<(double u, double v)>(path.Count) { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            int a = Math.Max(0, i - SmoothReach), b = Math.Min(path.Count - 1, i + SmoothReach);
            double u = 0, v = 0;
            for (int j = a; j <= b; j++) { u += path[j].u; v += path[j].v; }
            var rounded = (u / (b - a + 1), v / (b - a + 1));
            bool worse = GroundFactor(g.Kind[Index(g, Cell(g, rounded))]) > GroundFactor(g.Kind[Index(g, Cell(g, path[i]))]);
            o.Add(worse ? path[i] : rounded);
        }
        o.Add(path[^1]);
        return o;
    }

    static int Index(ReliefGrid g, (int x, int y) c) => c.y * g.Cols + c.x;

    public static List<(double u, double v)> River(ReliefGrid g, float[] heights, (double u, double v) spring, out List<int> pond, double pitDepth)
    {
        var filled = GroundStreams.Filled(heights, g.Cols, g.Rows, i => g.Kind[i] == GroundKinds.Water);
        var path = new List<(double u, double v)>();
        var (x, y) = Cell(g, spring);
        pond = [];
        while (true)
        {
            int i = y * g.Cols + x;
            path.Add(Uv(g, (x, y)));
            if (g.Kind[i] == GroundKinds.Water) break;
            if (filled[i] - heights[i] > pitDepth) { pond = Pond(g, heights, filled, i, pitDepth * PondRim); break; }
            int bx = -1, by = -1;
            float best = filled[i];
            foreach (var (dx, dy) in Eight)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= g.Cols || ny >= g.Rows) continue;
                float h = filled[ny * g.Cols + nx];
                if (h >= best) continue;
                best = h;
                bx = nx;
                by = ny;
            }
            if (bx < 0) break;
            x = bx;
            y = by;
            if (x == 0 || y == 0 || x == g.Cols - 1 || y == g.Rows - 1) { path.Add(Uv(g, (x, y))); break; }
        }
        return Smoothed(g, path);
    }

    public static double RiverRadius(double radiusCells, double metresAlong) => radiusCells * (1 + (RiverGrowth - 1) * Math.Min(1, metresAlong / RiverGrowOverMetres));

    static List<int> Pond(ReliefGrid g, float[] heights, float[] filled, int from, double rim)
    {
        var pond = new List<int>();
        var queue = new Queue<int>();
        var seen = new HashSet<int> { from };
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            pond.Add(i);
            int x = i % g.Cols, y = i / g.Cols;
            foreach (var (dx, dy) in Eight)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= g.Cols || ny >= g.Rows) continue;
                int j = ny * g.Cols + nx;
                if (filled[j] - heights[j] <= rim || !seen.Add(j)) continue;
                queue.Enqueue(j);
            }
        }
        return pond;
    }

    static List<(int x, int y)>? Between(ReliefGrid g, float[] heights, double heightUnitsPerCell, (int x, int y) from, (int x, int y) to)
    {
        int x0 = Math.Max(0, Math.Min(from.x, to.x) - Margin), x1 = Math.Min(g.Cols - 1, Math.Max(from.x, to.x) + Margin);
        int y0 = Math.Max(0, Math.Min(from.y, to.y) - Margin), y1 = Math.Min(g.Rows - 1, Math.Max(from.y, to.y) + Margin);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var cost = new double[w * h];
        var came = new int[w * h];
        Array.Fill(cost, double.MaxValue);
        Array.Fill(came, -1);
        int Local(int x, int y) => (y - y0) * w + (x - x0);
        int start = Local(from.x, from.y), goal = Local(to.x, to.y);
        cost[start] = 0;
        var open = new PriorityQueue<int, double>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out int cur, out double pri))
        {
            if (cur == goal) break;
            if (pri > cost[cur] + Heuristic(cur, goal, w)) continue;
            int cx = cur % w + x0, cy = cur / w + y0;
            int ci = cy * g.Cols + cx;
            foreach (var (dx, dy) in Eight)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                int ni = ny * g.Cols + nx;
                double dist = dx != 0 && dy != 0 ? 1.41421356 : 1;
                double slope = Math.Abs(heights[ni] - heights[ci]) / (dist * heightUnitsPerCell);
                if (slope > Impassable) continue;
                double step = dist * (1 + SlopeWeight * slope * slope) * GroundFactor(g.Kind[ni]);
                int next = Local(nx, ny);
                double total = cost[cur] + step;
                if (total >= cost[next]) continue;
                cost[next] = total;
                came[next] = cur;
                open.Enqueue(next, total + Heuristic(next, goal, w));
            }
        }
        if (came[goal] < 0 && goal != start) return null;
        var path = new List<(int x, int y)>();
        for (int at = goal; at >= 0; at = came[at]) path.Add((at % w + x0, at / w + y0));
        path.Reverse();
        return path;
    }

    static double Heuristic(int a, int b, int w)
    {
        int dx = Math.Abs(a % w - b % w), dy = Math.Abs(a / w - b / w);
        return EasiestGround * (Math.Max(dx, dy) + 0.41421356 * Math.Min(dx, dy));
    }

    static (int x, int y) Cell(ReliefGrid g, (double u, double v) p) => ((int)Math.Round(Math.Clamp(p.u, 0, 1) * (g.Cols - 1)), (int)Math.Round(Math.Clamp(p.v, 0, 1) * (g.Rows - 1)));

    static (double u, double v) Uv(ReliefGrid g, (int x, int y) c) => (c.x / (double)(g.Cols - 1), c.y / (double)(g.Rows - 1));
}
