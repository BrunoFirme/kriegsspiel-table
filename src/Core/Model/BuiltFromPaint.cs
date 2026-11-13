namespace KTable.Core.Model;

public static class BuiltFromPaint
{
    const byte Grass = 1, Field = 2;
    const double TrackBelow = 0.33, RoadBelow = 0.66, KeepWithinCells = 0.75, LeastTownCells = 4;
    static readonly (int Dx, int Dy)[] Around = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    static bool Laid(byte kind) => kind is GroundKinds.Road or GroundKinds.Bridge or GroundKinds.Town;

    static bool Settled(ReliefGrid g, int i) => g.Kind[i] is Grass or Field && g.Detail[i] != 0;

    public static bool Needed(ReliefGrid g)
    {
        for (int i = 0; i < g.Kind.Length; i++)
            if (Laid(g.Kind[i]) || Settled(g, i)) return true;
        return false;
    }

    public static (List<BuiltThing> Things, ReliefGrid Ground) Convert(ReliefGrid old, BuiltSite site, string sheet)
    {
        var ground = Cleared(old);
        var things = new List<BuiltThing>();
        BuiltSite Here() => site.With(ground, site.Others.Concat(things).ToList());
        foreach (var (path, level) in Roads(old))
        {
            var road = BuiltRoads.Lay(Here(), sheet, path.Select(c => SpotOf(old, site, c.X, c.Y)).ToList(), level < TrackBelow ? "track" : level < RoadBelow ? "road" : "chaussee", false);
            if (road.Parts.Count > 0) things.Add(road);
        }
        foreach (var town in Patches(old, i => old.Kind[i] == GroundKinds.Town))
        {
            if (town.Count < LeastTownCells) continue;
            double cx = town.Average(i => i % old.Cols), cy = town.Average(i => i / old.Cols);
            int middle = town.MinBy(i => Math.Pow(i % old.Cols - cx, 2) + Math.Pow(i / old.Cols - cy, 2));
            var (houses, walls) = GroundKinds.LevelsOf(old.Detail[middle]);
            double cell = site.Width / Math.Max(1, old.Cols - 1);
            var place = Settlements.Lay(Here(), sheet, SpotOf(old, site, cx, cy), Math.Sqrt(town.Count / Math.PI) * cell, 1 + old.Salt[middle] + middle, houses / 100.0, walls / 100.0, 0);
            if (place.Parts.Count > 0) things.Add(place);
        }
        foreach (byte detail in Enumerable.Range(0, old.Kind.Length).Where(i => Settled(old, i)).Select(i => old.Detail[i]).Distinct().OrderBy(d => d))
        {
            var (plots, buildings) = GroundKinds.LevelsOf(detail);
            var cells = Enumerable.Range(0, old.Kind.Length).Where(i => Settled(old, i) && old.Detail[i] == detail).ToList();
            var low = SpotOf(old, site, cells.Min(i => i % old.Cols), cells.Min(i => i / old.Cols));
            var high = SpotOf(old, site, cells.Max(i => i % old.Cols), cells.Max(i => i / old.Cols));
            bool Passed(Spot p) => site.Inside(p) && Settled(old, CellOf(old, site, p)) && old.Detail[CellOf(old, site, p)] == detail;
            var country = BuiltDetails.Over(Here(), sheet, Passed, (low.X, low.Y, high.X, high.Y), 1 + detail + old.Salt[cells[0]], plots / 100.0, buildings / 100.0);
            if (country.Parts.Count > 0) things.Add(country);
        }
        return (things, ground);
    }

    static Spot SpotOf(ReliefGrid g, BuiltSite site, double x, double y) => new(x / Math.Max(1, g.Cols - 1) * site.Width, y / Math.Max(1, g.Rows - 1) * site.Height);

    static int CellOf(ReliefGrid g, BuiltSite site, Spot p) =>
        (int)Math.Round(Math.Clamp(p.Y / site.Height, 0, 1) * (g.Rows - 1)) * g.Cols + (int)Math.Round(Math.Clamp(p.X / site.Width, 0, 1) * (g.Cols - 1));

    static ReliefGrid Cleared(ReliefGrid old)
    {
        var g = old.Copy();
        var open = new Queue<int>();
        for (int i = 0; i < g.Kind.Length; i++)
        {
            if (g.Kind[i] == GroundKinds.Bridge) g.Kind[i] = GroundKinds.Water;
            if (g.Kind[i] is Grass or Field or GroundKinds.Water) g.Detail[i] = 0;
            if (!Laid(g.Kind[i])) open.Enqueue(i);
        }
        if (open.Count == 0) Array.Fill(g.Kind, Grass);
        while (open.TryDequeue(out int from))
        {
            int x = from % g.Cols, y = from / g.Cols;
            foreach (var (dx, dy) in Around)
            {
                int nx = x + dx, ny = y + dy, to = ny * g.Cols + nx;
                if (nx < 0 || ny < 0 || nx >= g.Cols || ny >= g.Rows || !Laid(g.Kind[to])) continue;
                g.Kind[to] = g.Kind[from];
                g.Salt[to] = g.Salt[from];
                g.Detail[to] = g.Detail[from];
                open.Enqueue(to);
            }
        }
        return g;
    }

    static IEnumerable<List<int>> Patches(ReliefGrid g, Func<int, bool> of)
    {
        var seen = new bool[g.Kind.Length];
        for (int start = 0; start < seen.Length; start++)
        {
            if (seen[start] || !of(start)) continue;
            var patch = new List<int>();
            var open = new Queue<int>([start]);
            seen[start] = true;
            while (open.TryDequeue(out int at))
            {
                patch.Add(at);
                foreach (var (dx, dy) in Around)
                {
                    int nx = at % g.Cols + dx, ny = at / g.Cols + dy, to = ny * g.Cols + nx;
                    if (nx < 0 || ny < 0 || nx >= g.Cols || ny >= g.Rows || seen[to] || !of(to)) continue;
                    seen[to] = true;
                    open.Enqueue(to);
                }
            }
            yield return patch;
        }
    }

    static IEnumerable<(List<(double X, double Y)> Path, double Level)> Roads(ReliefGrid g)
    {
        var road = new bool[g.Kind.Length];
        for (int i = 0; i < road.Length; i++) road[i] = g.Kind[i] is GroundKinds.Road or GroundKinds.Bridge;
        Thinned(road, g.Cols, g.Rows);
        int Degree(int i) => Around.Count(d => On(road, g, i % g.Cols + d.Dx, i / g.Cols + d.Dy));
        var walked = new HashSet<(int, int)>();
        var nodes = Enumerable.Range(0, road.Length).Where(i => road[i] && Degree(i) != 2).ToList();
        foreach (int from in nodes.Concat(Enumerable.Range(0, road.Length).Where(i => road[i])))
            foreach (var (dx, dy) in Around)
            {
                int nx = from % g.Cols + dx, ny = from / g.Cols + dy;
                if (!On(road, g, nx, ny) || walked.Contains((from, ny * g.Cols + nx))) continue;
                var cells = new List<int> { from };
                int before = from, at = ny * g.Cols + nx;
                while (true)
                {
                    walked.Add((before, at));
                    walked.Add((at, before));
                    cells.Add(at);
                    if (Degree(at) != 2 || at == from) break;
                    int next = Beside(road, g, at).FirstOrDefault(n => n != before, -1);
                    if (next < 0) break;
                    before = at;
                    at = next;
                }
                if (cells.Count < 2) continue;
                int mid = cells[cells.Count / 2];
                yield return (Kept(cells.Select(c => ((double)(c % g.Cols), (double)(c / g.Cols))).ToList()), GroundKinds.LevelsOf(g.Detail[mid]).First / 100.0);
            }
    }

    static IEnumerable<int> Beside(bool[] mask, ReliefGrid g, int at) =>
        Around.Select(d => (X: at % g.Cols + d.Dx, Y: at / g.Cols + d.Dy)).Where(n => On(mask, g, n.X, n.Y)).Select(n => n.Y * g.Cols + n.X);

    static bool On(bool[] mask, ReliefGrid g, int x, int y) => x >= 0 && y >= 0 && x < g.Cols && y < g.Rows && mask[y * g.Cols + x];

    static List<(double X, double Y)> Kept(List<(double X, double Y)> path)
    {
        if (path.Count < 3) return path;
        var kept = new List<(double X, double Y)> { path[0] };
        for (int i = 1; i + 1 < path.Count; i++)
            if (new Spot(path[i].X, path[i].Y).FromSegment(new Spot(kept[^1].X, kept[^1].Y), new Spot(path[i + 1].X, path[i + 1].Y)) > KeepWithinCells) kept.Add(path[i]);
        kept.Add(path[^1]);
        return kept;
    }

    static void Thinned(bool[] mask, int cols, int rows)
    {
        bool At(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows && mask[y * cols + x];
        var gone = new List<int>();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int pass = 0; pass < 2; pass++)
            {
                gone.Clear();
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        if (!mask[y * cols + x]) continue;
                        var p = Around.Select(d => At(x + d.Dx, y + d.Dy)).ToArray();
                        int count = p.Count(v => v), turns = Enumerable.Range(0, 8).Count(k => !p[k] && p[(k + 1) % 8]);
                        if (count < 2 || count > 6 || turns != 1) continue;
                        bool first = pass == 0 ? !(p[0] && p[2] && p[4]) && !(p[2] && p[4] && p[6]) : !(p[0] && p[2] && p[6]) && !(p[0] && p[4] && p[6]);
                        if (first) gone.Add(y * cols + x);
                    }
                foreach (int i in gone) mask[i] = false;
                changed |= gone.Count > 0;
            }
        }
    }
}
