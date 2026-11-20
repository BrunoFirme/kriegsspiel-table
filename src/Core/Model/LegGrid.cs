namespace KTable.Core.Model;

sealed class LegGrid
{
    const int Across = 64;
    readonly List<(Spot A, Spot B)> _legs;
    readonly List<int>[] _cells;
    readonly double _cell;
    readonly int _cols, _rows;

    public LegGrid(IEnumerable<(Spot A, Spot B)> legs, double width, double height)
    {
        _legs = legs.ToList();
        _cell = Math.Max(Math.Max(width, height) / Across, 1e-9);
        _cols = Math.Max(1, (int)Math.Ceiling(width / _cell));
        _rows = Math.Max(1, (int)Math.Ceiling(height / _cell));
        _cells = new List<int>[_cols * _rows];
        for (int i = 0; i < _legs.Count; i++)
        {
            var (a, b) = _legs[i];
            for (int y = Row(Math.Min(a.Y, b.Y)); y <= Row(Math.Max(a.Y, b.Y)); y++)
                for (int x = Col(Math.Min(a.X, b.X)); x <= Col(Math.Max(a.X, b.X)); x++)
                    (_cells[y * _cols + x] ??= []).Add(i);
        }
    }

    int Col(double v) => Math.Clamp((int)Math.Floor(v / _cell), 0, _cols - 1);
    int Row(double v) => Math.Clamp((int)Math.Floor(v / _cell), 0, _rows - 1);

    static Spot Closest(Spot p, Spot a, Spot b)
    {
        var ab = b - a;
        return a + ab * Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
    }

    public IEnumerable<int> Along(Spot a, Spot b)
    {
        var found = new SortedSet<int>();
        for (int y = Row(Math.Min(a.Y, b.Y)); y <= Row(Math.Max(a.Y, b.Y)); y++)
            for (int x = Col(Math.Min(a.X, b.X)); x <= Col(Math.Max(a.X, b.X)); x++)
                if (_cells[y * _cols + x] is { } list) found.UnionWith(list);
        return found;
    }

    public (Spot At, double Reach)? Nearest(Spot p)
    {
        if (_legs.Count == 0) return null;
        (Spot At, double Reach)? best = null;
        void Try(int i)
        {
            var near = Closest(p, _legs[i].A, _legs[i].B);
            double reach = (near - p).Length;
            if (best is not { } known || reach < known.Reach) best = (near, reach);
        }
        if (p.X < 0 || p.Y < 0 || p.X > _cols * _cell || p.Y > _rows * _cell)
        {
            for (int i = 0; i < _legs.Count; i++) Try(i);
            return best;
        }
        int cx = Col(p.X), cy = Row(p.Y), most = Math.Max(_cols, _rows);
        var seen = new HashSet<int>();
        for (int ring = 0; ring <= most; ring++)
        {
            for (int y = cy - ring; y <= cy + ring; y++)
                for (int x = cx - ring; x <= cx + ring; x++)
                {
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring || x < 0 || y < 0 || x >= _cols || y >= _rows || _cells[y * _cols + x] is not { } list) continue;
                    foreach (int i in list) if (seen.Add(i)) Try(i);
                }
            if (best is { } found && found.Reach <= ring * _cell) break;
        }
        return best;
    }
}
