namespace KTable.Core.Model;

public readonly record struct GridCell(int A, int B);

public static class GridCells
{
    const double HalfRoot3 = 0.8660254037844386, KeyZPerUnit = 2 * 1.7320508075688772;
    public const int MostRings = 30;

    static readonly (int X, int Z)[] HexCorners = { (1, 1), (0, 2), (-1, 1), (-1, -1), (0, -2), (1, -1) };
    static readonly (int X, int Z)[] SquareCorners = { (0, 0), (1, 0), (1, 1), (0, 1) };

    static bool Hexes(GridDef g) => g.Kind == "hex";

    static Vec2 Turned(GridDef g, Vec2 p) => g.FlatTop && Hexes(g) ? new Vec2(p.Z, p.X) : p;

    public static GridCell CellOf(GridDef g, Vec2 p)
    {
        if (!Hexes(g)) return new((int)Math.Floor(p.X / g.Pitch), (int)Math.Floor(p.Z / g.Pitch));
        var t = Turned(g, p);
        double r = t.Z / g.Pitch / HalfRoot3, q = t.X / g.Pitch - r / 2, s = -q - r;
        double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
        double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        return new((int)rq, (int)rr);
    }

    public static Vec2 Centre(GridDef g, GridCell c) => Hexes(g)
        ? Turned(g, new Vec2((c.A + c.B / 2.0) * g.Pitch, c.B * HalfRoot3 * g.Pitch))
        : new Vec2((c.A + 0.5) * g.Pitch, (c.B + 0.5) * g.Pitch);

    public static int Steps(GridDef g, GridCell a, GridCell b)
    {
        int da = a.A - b.A, db = a.B - b.B;
        return Hexes(g) ? (Math.Abs(da) + Math.Abs(db) + Math.Abs(da + db)) / 2 : Math.Max(Math.Abs(da), Math.Abs(db));
    }

    public static IEnumerable<GridCell> Within(GridDef g, GridCell c, int rings)
    {
        for (int da = -rings; da <= rings; da++)
        {
            int lo = Hexes(g) ? Math.Max(-rings, -da - rings) : -rings, hi = Hexes(g) ? Math.Min(rings, -da + rings) : rings;
            for (int db = lo; db <= hi; db++) yield return new(c.A + da, c.B + db);
        }
    }

    public static int Count(GridDef g, int rings) => Hexes(g) ? 3 * rings * (rings + 1) + 1 : (2 * rings + 1) * (2 * rings + 1);

    public static double Farthest(GridDef g, int rings) => Hexes(g)
        ? (rings + 2 / 1.7320508075688772) * g.Pitch
        : (rings + 1) * g.Pitch * Math.Sqrt(2);

    public static List<Vec2> Corners(GridDef g, GridCell c) => Keys(g, c).Select(k => FromKey(g, k)).ToList();

    static IEnumerable<(int X, int Z)> Keys(GridDef g, GridCell c) => Hexes(g)
        ? HexCorners.Select(o => (2 * c.A + c.B + o.X, 3 * c.B + o.Z))
        : SquareCorners.Select(o => (c.A + o.X, c.B + o.Z));

    static Vec2 FromKey(GridDef g, (int X, int Z) k) => Hexes(g)
        ? Turned(g, new Vec2(k.X / 2.0 * g.Pitch, k.Z / KeyZPerUnit * g.Pitch))
        : new Vec2(k.X * g.Pitch, k.Z * g.Pitch);

    public static List<List<Vec2>> Outline(GridDef g, IEnumerable<GridCell> cells)
    {
        var sides = new HashSet<((int X, int Z) From, (int X, int Z) To)>();
        foreach (var c in cells.Distinct())
        {
            var k = Keys(g, c).ToList();
            for (int i = 0; i < k.Count; i++) sides.Add((k[i], k[(i + 1) % k.Count]));
        }
        var onward = new Dictionary<(int X, int Z), Stack<(int X, int Z)>>();
        foreach (var (from, to) in sides.Where(s => !sides.Contains((s.To, s.From))).OrderBy(s => s.From).ThenBy(s => s.To))
        {
            if (!onward.TryGetValue(from, out var next)) onward[from] = next = new();
            next.Push(to);
        }
        var loops = new List<List<Vec2>>();
        foreach (var start in onward.Keys.ToList())
            while (onward[start].Count > 0)
            {
                var loop = new List<(int X, int Z)> { start };
                for (var at = onward[start].Pop(); at != start; at = onward[at].Pop()) loop.Add(at);
                loops.Add(Straight(loop).Select(k => FromKey(g, k)).ToList());
            }
        return loops;
    }

    static List<(int X, int Z)> Straight(List<(int X, int Z)> loop)
    {
        var kept = new List<(int X, int Z)>();
        for (int i = 0; i < loop.Count; i++)
        {
            var (a, b, c) = (loop[(i + loop.Count - 1) % loop.Count], loop[i], loop[(i + 1) % loop.Count]);
            long cross = (long)(b.X - a.X) * (c.Z - b.Z) - (long)(b.Z - a.Z) * (c.X - b.X);
            if (cross != 0) kept.Add(b);
        }
        return kept;
    }

    public static List<Vec2> Joined(List<List<Vec2>> loops)
    {
        if (loops.Count == 0) return new();
        var ring = loops[0].ToList();
        foreach (var loop in loops.Skip(1))
        {
            ring.Add(loops[0][0]);
            ring.AddRange(loop);
            ring.Add(loop[0]);
        }
        return ring;
    }

    public static int JoinedCount(List<List<Vec2>> loops) => loops.Sum(l => l.Count) + 2 * Math.Max(loops.Count - 1, 0);
}
