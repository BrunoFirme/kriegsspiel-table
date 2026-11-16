namespace KTable.Core.Model;

sealed class TownGround
{
    const double CellM = 7, SlopeDear = 7, ClimbDear = 8, SteepestCounted = 0.45, WoodDear = 7, RoughDear = 1.5, SpanDear = 5, SpanWidestM = 90, LineRoom = 0.8, RoughAcross = 0.55, RoughLeastM = 70;
    public const double Filled = 0.6, FilledHuddled = 0.48, FilledStraggling = 0.7;
    const double ShoreCheap = 0.25, ShoreCheapM = 90, WayDearOpen = 0.78, WayDearSpread = 0.3, StartWithin = 0.6, SpanFromM = 170, SpanChance = 0.45, StrandedShare = 0.12, ShoreFarCells = 1e6;
    const double WoodedBelow = 0.25, RoomFilled = 0.72;
    const int LeastAcross = 16, MostAcross = 112;
    const byte Shut = 0, Open = 1, Wet = 2, Span = 3;
    static readonly (int X, int Y)[] Around = [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    readonly BuiltSite _site;
    readonly int _n, _seed;
    readonly double _cell, _reach, _stretch, _rough;
    readonly Spot _low, _axis;
    readonly byte[] _kind;
    readonly bool[] _wood, _way, _held;
    readonly float[] _dear, _cost, _shore, _fallX, _fallY;
    readonly int[] _from;
    readonly Landforms? _land;
    double _farthest = 1;

    public Spot Centre { get; private set; }
    public bool Any { get; private set; }
    public double Cell => _cell;
    public List<int> Spans { get; } = new();

    public TownGround(BuiltSite site, Spot centre, double reach, int seed, double spread, double stretch, Spot axis, double shoreward = 0, double filled = Filled,
        Landforms? land = null)
    {
        (_site, _reach, _seed, _stretch, _axis, Centre, _land) = (site, reach, seed, Math.Max(1, stretch), axis.Unit, centre, land);
        _n = Math.Clamp((int)Math.Ceiling(2 * reach / Math.Max(1e-9, site.M(CellM))), LeastAcross, MostAcross);
        _cell = 2 * reach / _n;
        _low = centre - new Spot(reach, reach);
        _rough = Math.Max(site.M(RoughLeastM), reach * RoughAcross);
        (_kind, _wood, _way, _held) = (new byte[_n * _n], new bool[_n * _n], new bool[_n * _n], new bool[_n * _n]);
        (_dear, _cost, _shore, _from) = (new float[_n * _n], new float[_n * _n], new float[_n * _n], new int[_n * _n]);
        (_fallX, _fallY) = (new float[_n * _n], new float[_n * _n]);
        Read();
        Shores();
        Price(spread, shoreward);
        if (Start() is not { } start) return;
        Flood(start);
        if (Bridged(new Dice(seed, 77))) Flood(start);
        Hold(filled);
    }

    public Spot Mid(int i) => _low + new Spot((i % _n + 0.5) * _cell, (i / _n + 0.5) * _cell);

    public int At(Spot p)
    {
        int x = (int)Math.Floor((p.X - _low.X) / _cell), y = (int)Math.Floor((p.Y - _low.Y) / _cell);
        return x < 0 || y < 0 || x >= _n || y >= _n ? -1 : y * _n + x;
    }

    public bool Held(Spot p) => At(p) is >= 0 and var i && _held[i];

    public bool Passable(Spot p) => At(p) is >= 0 and var i && _kind[i] is Open or Span;

    public bool Spanned(Spot p) => At(p) is >= 0 and var i && _kind[i] == Span;

    public bool Wooded(Spot p) => At(p) is >= 0 and var i && _wood[i];

    public double Central(Spot p) => At(p) is >= 0 and var i && _held[i] ? Math.Clamp(_cost[i] / _farthest, 0, 1) : 1;

    public double ShoreAway(Spot p) => At(p) is >= 0 and var i ? _shore[i] * _cell : double.MaxValue;

    public Spot ShoreWay(Spot p)
    {
        if (At(p) is not (>= 0 and var i)) return new Spot(0, 0);
        int x = i % _n, y = i / _n;
        double Far(int cx, int cy) => _shore[Math.Clamp(cy, 0, _n - 1) * _n + Math.Clamp(cx, 0, _n - 1)];
        var rise = new Spot(Far(x + 1, y) - Far(x - 1, y), Far(x, y + 1) - Far(x, y - 1));
        return rise.Length < 1e-6 ? new Spot(0, 0) : rise.Unit.Left;
    }

    public static double Rough(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        (fx, fy) = (fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy));
        double a = Grain(x0, y0, seed), b = Grain(x0 + 1, y0, seed), c = Grain(x0, y0 + 1, seed), d = Grain(x0 + 1, y0 + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    static double Grain(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    void Lined(Spot a, Spot b, Action<int> mark)
    {
        int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / (_cell * 0.5)));
        for (int s = 0; s <= steps; s++)
        {
            var p = a + (b - a) * (s / (double)steps);
            foreach (var (dx, dy) in Around.Append((0, 0)))
                if (At(p + new Spot(dx * _cell, dy * _cell)) is >= 0 and var i && (Mid(i) - p).Length < _cell * LineRoom) mark(i);
        }
    }

    void Read()
    {
        for (int i = 0; i < _kind.Length; i++)
        {
            var p = Mid(i);
            byte kind = _site.KindAt(p);
            bool within = (p - Centre).Length <= _reach;
            _kind[i] = kind == GroundKinds.Water ? Wet : within && _site.Buildable(p) && !_site.InPlace(p) ? Open : Shut;
            _wood[i] = kind == GroundKinds.Wood;
        }
        int open = _kind.Count(k => k == Open), wooded = Enumerable.Range(0, _kind.Length).Count(i => _kind[i] == Open && _wood[i]);
        bool clearing = wooded > open * (1 - WoodedBelow);
        for (int i = 0; i < _kind.Length && !clearing; i++)
            if (_kind[i] == Open && _wood[i]) _kind[i] = Shut;
        foreach (var (a, b, _) in _site.Waters) Lined(a, b, i => _kind[i] = Wet);
        foreach (var (a, b) in _site.RoadSegments())
            Lined(a, b, i =>
            {
                _way[i] = true;
                if (_kind[i] == Wet && (Mid(i) - Centre).Length <= _reach) _kind[i] = Span;
                if (_kind[i] == Shut && _wood[i] && (Mid(i) - Centre).Length <= _reach && _site.Buildable(Mid(i))) _kind[i] = Open;
            });
    }

    void Price(double spread, double shoreward)
    {
        double wayDear = WayDearOpen + (WayDearSpread - WayDearOpen) * Math.Clamp(spread, 0, 1);
        for (int i = 0; i < _kind.Length; i++)
        {
            var p = Mid(i);
            var fall = _site.Downhill(p);
            (_fallX[i], _fallY[i]) = ((float)fall.X, (float)fall.Y);
            double steep = Math.Min(SteepestCounted, fall.Length), rough = Rough(p.X / _rough, p.Y / _rough, _seed) + 0.5 * Rough(2.3 * p.X / _rough, 2.3 * p.Y / _rough, _seed + 5);
            double dear = 1 + SlopeDear * steep + (_wood[i] ? WoodDear : 0) + RoughDear * (0.35 + spread) * rough;
            double shore = shoreward * Math.Max(0, 1 - _shore[i] * _cell / _site.M(ShoreCheapM));
            double sited = _land == null || _kind[i] != Open ? 1 : Siting.Dear(_site.Manner, _land.At(p), fall);
            _dear[i] = (float)(dear * sited * (_way[i] ? wayDear : 1) * (_kind[i] == Span && !_way[i] ? SpanDear : 1) * (1 + (ShoreCheap - 1) * shore));
        }
    }

    int? Start()
    {
        if (At(Centre) is >= 0 and var mid && _kind[mid] == Open) return mid;
        int best = -1;
        for (int i = 0; i < _kind.Length; i++)
            if (_kind[i] == Open && (Mid(i) - Centre).Length < _reach * StartWithin && (best < 0 || (Mid(i) - Centre).Length < (Mid(best) - Centre).Length)) best = i;
        if (best < 0) return null;
        Centre = Mid(best);
        return best;
    }

    double Stride(int dx, int dy)
    {
        var step = new Spot(dx, dy);
        double along = step.Dot(_axis) / _stretch, across = step.Dot(_axis.Left);
        return Math.Sqrt(along * along + across * across) * _cell;
    }

    void Flood(int start)
    {
        Array.Fill(_cost, float.MaxValue);
        Array.Fill(_from, -1);
        var open = new PriorityQueue<int, float>();
        _cost[start] = 0;
        open.Enqueue(start, 0);
        while (open.TryDequeue(out int i, out float known))
        {
            if (known > _cost[i]) continue;
            int x = i % _n, y = i / _n;
            foreach (var (dx, dy) in Around)
            {
                int nx = x + dx, ny = y + dy, j = ny * _n + nx;
                if (nx < 0 || ny < 0 || nx >= _n || ny >= _n || _kind[j] is not (Open or Span)) continue;
                if (dx != 0 && dy != 0 && (_kind[y * _n + nx] == Wet || _kind[ny * _n + x] == Wet)) continue;
                double climb = Math.Abs(_fallX[j] * dx + _fallY[j] * dy) / Math.Sqrt(dx * dx + dy * dy);
                float reached = known + (float)(Stride(dx, dy) * (_dear[i] + _dear[j]) / 2 * (1 + ClimbDear * Math.Min(SteepestCounted, climb)));
                if (reached >= _cost[j]) continue;
                (_cost[j], _from[j]) = (reached, i);
                open.Enqueue(j, reached);
            }
        }
    }

    bool Bridged(Dice dice)
    {
        int open = _kind.Count(k => k == Open), stranded = Enumerable.Range(0, _kind.Length).Count(i => _kind[i] == Open && _cost[i] == float.MaxValue);
        if (_kind.Contains(Span) || stranded < open * StrandedShare || _reach < _site.M(SpanFromM) && !dice.Chance(SpanChance)) return false;
        int most = (int)(_site.M(SpanWidestM) / _cell);
        (double Score, List<int> Over)? best = null;
        for (int i = 0; i < _kind.Length; i++)
        {
            if (_kind[i] != Open || _cost[i] == float.MaxValue) continue;
            foreach (var (dx, dy) in Around)
            {
                var over = new List<int>();
                int x = i % _n + dx, y = i / _n + dy;
                while (x >= 0 && y >= 0 && x < _n && y < _n && _kind[y * _n + x] == Wet && over.Count <= most)
                {
                    over.Add(y * _n + x);
                    (x, y) = (x + dx, y + dy);
                }
                if (over.Count == 0 || over.Count > most || x < 0 || y < 0 || x >= _n || y >= _n || _kind[y * _n + x] != Open || _cost[y * _n + x] != float.MaxValue) continue;
                double score = _cost[i] + over.Count * Stride(dx, dy) * SpanDear * 3;
                if (best is not { } known || score < known.Score) best = (score, over);
            }
        }
        if (best is not { } found) return false;
        foreach (int i in found.Over) _kind[i] = Span;
        Spans.Add(found.Over[found.Over.Count / 2]);
        return true;
    }

    void Hold(double filled)
    {
        var reached = Enumerable.Range(0, _kind.Length).Where(i => _kind[i] == Open && _cost[i] < float.MaxValue).OrderBy(i => _cost[i]).ToList();
        int clear = reached.Count(i => !_wood[i]), room = clear >= reached.Count * WoodedBelow ? clear : reached.Count;
        int wanted = Math.Min(reached.Count, (int)(filled * Math.Min(Math.PI * _n * _n / 4, room / RoomFilled)));
        foreach (int i in reached.Take(wanted)) _held[i] = true;
        Any = wanted > 0;
        if (Any) _farthest = Math.Max(1e-9, _cost[reached[wanted - 1]]);
    }

    void Shores()
    {
        Array.Fill(_shore, (float)ShoreFarCells);
        var open = new PriorityQueue<int, float>();
        for (int i = 0; i < _kind.Length; i++)
            if (_kind[i] is Wet or Span)
            {
                _shore[i] = 0;
                open.Enqueue(i, 0);
            }
        while (open.TryDequeue(out int i, out float known))
        {
            if (known > _shore[i]) continue;
            foreach (var (dx, dy) in Around)
            {
                int nx = i % _n + dx, ny = i / _n + dy, j = ny * _n + nx;
                float reached = known + (dx != 0 && dy != 0 ? 1.4142f : 1);
                if (nx < 0 || ny < 0 || nx >= _n || ny >= _n || reached >= _shore[j]) continue;
                _shore[j] = reached;
                open.Enqueue(j, reached);
            }
        }
    }

    public IEnumerable<int> HeldCells => Enumerable.Range(0, _held.Length).Where(i => _held[i]);

    public double Cost(int i) => _cost[i];

    public bool Over(int i, int span)
    {
        for (int at = i, guard = 0; at >= 0 && guard < _kind.Length; at = _from[at], guard++)
            if (at == span) return true;
        return false;
    }

    public List<int> Home(int from)
    {
        var path = new List<int>();
        for (int at = from, guard = 0; at >= 0 && guard < _kind.Length; at = _from[at], guard++) path.Add(at);
        return path;
    }

    public double Fare(Spot a, Spot b)
    {
        int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / (_cell * 0.5)));
        double fare = 0, along = (b - a).Dot(_axis) / _stretch, across = (b - a).Dot(_axis.Left), stride = Math.Sqrt(along * along + across * across) / steps;
        for (int s = 0; s < steps; s++)
        {
            if (At(a + (b - a) * ((s + 0.5) / steps)) is not (>= 0 and var i) || _kind[i] is not (Open or Span)) return double.MaxValue;
            fare += stride * _dear[i];
        }
        return fare;
    }
}
