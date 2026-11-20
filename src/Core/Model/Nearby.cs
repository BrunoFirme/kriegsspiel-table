namespace KTable.Core.Model;

sealed class Nearby
{
    const double MostCellsAcross = 64;
    readonly double _cell;
    readonly Dictionary<(long X, long Y), List<int>> _cells = new();
    readonly List<int> _wide = new();
    int[] _seen = [];
    int _stamp, _items;

    public Nearby(double cell) => _cell = cell > 0 && double.IsFinite(cell) ? cell : 1;

    long Key(double v) => (long)Math.Floor(v / _cell);

    bool Wide(double x0, double y0, double x1, double y1) =>
        !double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1) || (x1 - x0) / _cell >= MostCellsAcross || (y1 - y0) / _cell >= MostCellsAcross;

    public void Add(int item, double x0, double y0, double x1, double y1)
    {
        _items = Math.Max(_items, item + 1);
        if (Wide(x0, y0, x1, y1))
        {
            _wide.Add(item);
            return;
        }
        for (long y = Key(y0); y <= Key(y1); y++)
            for (long x = Key(x0); x <= Key(x1); x++)
            {
                if (!_cells.TryGetValue((x, y), out var list)) _cells[(x, y)] = list = [];
                list.Add(item);
            }
    }

    public void Among(double x0, double y0, double x1, double y1, List<int> into)
    {
        into.Clear();
        if (_seen.Length < _items) Array.Resize(ref _seen, Math.Max(_items, _seen.Length * 2));
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }
        foreach (int i in _wide) Take(i, into);
        if (Wide(x0, y0, x1, y1) || ((x1 - x0) / _cell + 1) * ((y1 - y0) / _cell + 1) > _cells.Count)
        {
            foreach (var list in _cells.Values)
                foreach (int i in list) Take(i, into);
            return;
        }
        for (long y = Key(y0); y <= Key(y1); y++)
            for (long x = Key(x0); x <= Key(x1); x++)
                if (_cells.TryGetValue((x, y), out var list))
                    foreach (int i in list) Take(i, into);
    }

    public bool Any(double x0, double y0, double x1, double y1, Func<int, bool> test)
    {
        foreach (int i in _wide)
            if (test(i)) return true;
        if (Wide(x0, y0, x1, y1) || ((x1 - x0) / _cell + 1) * ((y1 - y0) / _cell + 1) > _cells.Count)
        {
            foreach (var list in _cells.Values)
                foreach (int i in list)
                    if (test(i)) return true;
            return false;
        }
        for (long y = Key(y0); y <= Key(y1); y++)
            for (long x = Key(x0); x <= Key(x1); x++)
                if (_cells.TryGetValue((x, y), out var list))
                    foreach (int i in list)
                        if (test(i)) return true;
        return false;
    }

    void Take(int i, List<int> into)
    {
        if (_seen[i] == _stamp) return;
        _seen[i] = _stamp;
        into.Add(i);
    }
}

sealed class StreetsNear
{
    readonly List<(Settlements.Street Street, Spot A, Spot B)> _legs = new();
    readonly Nearby _grid;
    readonly List<int> _found = new();

    public StreetsNear(IEnumerable<Settlements.Street> streets, double cell)
    {
        _grid = new Nearby(cell);
        foreach (var street in streets)
            for (int i = 0; i + 1 < street.Points.Count; i++)
            {
                Spot a = street.Points[i], b = street.Points[i + 1];
                _grid.Add(_legs.Count, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                _legs.Add((street, a, b));
            }
    }

    public double From(Spot p, double within, Settlements.Street? but = null)
    {
        _grid.Among(p.X - within, p.Y - within, p.X + within, p.Y + within, _found);
        double least = double.MaxValue;
        foreach (int i in _found)
        {
            var leg = _legs[i];
            if (leg.Street == but) continue;
            least = Math.Min(least, p.FromSegment(leg.A, leg.B));
        }
        return least;
    }
}

sealed class TakenNear
{
    readonly List<(Spot At, double Reach)> _taken;
    readonly double _cell;
    readonly List<int> _found = new();
    Nearby _grid;
    int _indexed;
    double _most;

    public TakenNear(List<(Spot At, double Reach)> taken, double cell)
    {
        _taken = taken;
        _cell = cell;
        _grid = new Nearby(cell);
    }

    void Caught()
    {
        if (_taken.Count < _indexed)
        {
            _grid = new Nearby(_cell);
            _indexed = 0;
            _most = 0;
        }
        for (; _indexed < _taken.Count; _indexed++)
        {
            var (at, reach) = _taken[_indexed];
            _grid.Add(_indexed, at.X, at.Y, at.X, at.Y);
            _most = Math.Max(_most, reach);
        }
    }

    public bool Any(Spot p, double reach, double share = 1, int upTo = int.MaxValue)
    {
        Caught();
        double r = (_most + reach) * share;
        _grid.Among(p.X - r, p.Y - r, p.X + r, p.Y + r, _found);
        foreach (int i in _found)
        {
            if (i >= upTo) continue;
            var o = _taken[i];
            if ((o.At - p).Length < (o.Reach + reach) * share) return true;
        }
        return false;
    }
}
