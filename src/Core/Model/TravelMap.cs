namespace KTable.Core.Model;

public sealed record Route(IReadOnlyList<Vec2> Points, IReadOnlyList<double> Seconds)
{
    public double Total => Seconds.Sum();
}

public sealed partial class TravelMap
{
    public const int MostCellsAcross = 320;
    const double LeastCellM = 4, WayStepM = 10, JoinShare = 0.6, OverlayM = 1.5, CrossingReachM = 120, FerryReachM = 600, Corner = 0.70710678;
    const double RiverHalfM = 7.5, StreamHalfM = 1.8, MoatHalfM = 7.5;
    static readonly string[] Walls = ["wall", "wardwall", "palisade", "rampart", "drymoat"];
    static readonly string[] WaterAreas = ["pond", "moat"];

    readonly record struct Edge(int A, int B, int Class, double Length, double Rise);

    readonly SheetDef _sheet;
    readonly ReliefGrid? _relief;
    readonly double _width, _height, _perMetre, _cell, _heightUnit, _step;
    readonly int _cols, _rows;
    readonly float[] _h = [];
    readonly float[] _speed = [];
    readonly List<Spot> _at = new();
    readonly List<int> _line = new();
    readonly List<Edge> _edges = new();
    readonly List<string> _classes = new();
    readonly Dictionary<int, List<int>> _cellNodes = new();
    readonly Dictionary<(long, long), List<int>> _buckets = new();
    List<(int To, int Edge, bool Forward)>[] _links = [];
    int[] _nodeCell = [];

    public int Cells => _cols * _rows;
    public int WayNodes => _at.Count;

    public static TravelMap? For(GameState state, string sheetId, ReliefGrid? relief)
    {
        if (state.Sheets.FirstOrDefault(s => s.Id == sheetId) is not { } sheet || state.Calibration.UnitsPerMeter is not { } perMetre || !(perMetre > 0)) return null;
        var parts = state.Built.Where(b => b.Sheet == sheetId).SelectMany(b => b.Parts).ToList();
        if (relief == null && !parts.Any(IsWay)) return null;
        return new TravelMap(sheet, perMetre, relief, parts);
    }

    TravelMap(SheetDef sheet, double perMetre, ReliefGrid? relief, List<BuiltPart> parts)
    {
        _sheet = sheet;
        _relief = relief;
        _perMetre = perMetre;
        _width = Ground.FullWidth(sheet);
        _height = BuiltGeometry.FullHeight(sheet);
        _heightUnit = _width * (sheet.Relief is { Length: > 0 } ? sheet.Height : 1);
        _cell = Math.Max(Math.Max(_width, _height) / MostCellsAcross, LeastCellM * perMetre);
        _step = relief != null ? _cell : WayStepM * perMetre;
        if (relief != null)
        {
            _cols = Math.Max(1, (int)Math.Ceiling(_width / _cell));
            _rows = Math.Max(1, (int)Math.Ceiling(_height / _cell));
            _h = new float[_cols * _rows];
            _speed = Laid(relief, parts);
        }
        LayWays(parts);
        if (relief != null)
            foreach (var sign in parts.Where(p => p.Kind == PartKinds.Sign && CrossingOf(p.Class) != null && p.At.Count >= 2)) Crossing(sign);
        Knit();
    }

    static bool IsWay(BuiltPart part) => part.Kind == PartKinds.Line && Travellers.WayClasses.Contains(part.Class) && part.At.Count >= 4;

    static string? CrossingOf(string cls) => cls switch { "bridge" => Travellers.Bridge, "ford" => Travellers.Ford, "ferryboat" => Travellers.Ferry, _ => null };

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    double HeightAt(Spot p) => _relief == null ? 0 : _relief.HeightAt(p.X / _width, p.Y / _height) * _heightUnit;

    int CellOf(Spot p)
    {
        double fx = Math.Floor(p.X / _cell), fy = Math.Floor(p.Y / _cell);
        return fx < 0 || fy < 0 || fx >= _cols || fy >= _rows ? -1 : (int)fy * _cols + (int)fx;
    }

    int CellAt(int x, int y) => x < 0 || y < 0 || x >= _cols || y >= _rows ? -1 : y * _cols + x;

    bool Free(int x, int y) => CellAt(x, y) is >= 0 and var c && _speed[c] > 0;

    Spot Centre(int c) => new((c % _cols + 0.5) * _cell, (c / _cols + 0.5) * _cell);

    Spot OnSheet(Vec2 world)
    {
        var (u, v) = Ground.UvOf(_sheet, world);
        return new Spot(u * _width, v * _height);
    }

    Vec2 World(Spot p) => Ground.FromUv(_sheet, p.X / _width, p.Y / _height);

    static double Slowing(byte kind) => 1 / Math.Max(1, GroundRoute.GroundFactor(kind));

    float[] Laid(ReliefGrid relief, List<BuiltPart> parts)
    {
        int n = _cols * _rows;
        var sum = new double[n];
        var count = new int[n];
        var wet = new bool[n];
        var solid = new bool[n];
        void Take(int c, byte kind)
        {
            if (kind == GroundKinds.Water) wet[c] = true;
            sum[c] += Slowing(kind);
            count[c]++;
        }
        for (int c = 0; c < n; c++)
        {
            var p = Centre(c);
            _h[c] = (float)HeightAt(p);
            Take(c, relief.KindAt(p.X / _width, p.Y / _height));
        }
        for (int y = 0; y < relief.Rows; y++)
            for (int x = 0; x < relief.Cols; x++)
                if (CellOf(new Spot(x / (double)(relief.Cols - 1) * _width, y / (double)(relief.Rows - 1) * _height)) is >= 0 and var c) Take(c, relief.Kind[y * relief.Cols + x]);
        Barriers(parts, wet, solid);
        var crop = _sheet.Crop ?? SheetGeometry.Whole;
        var speed = new float[n];
        for (int c = 0; c < n; c++)
        {
            var p = Centre(c);
            double u = p.X / _width, v = p.Y / _height;
            bool shown = u >= crop.X && u <= crop.X + crop.W && v >= crop.Y && v <= crop.Y + crop.H;
            speed[c] = wet[c] || solid[c] || !shown ? 0 : (float)(sum[c] / count[c]);
        }
        return speed;
    }

    internal static double WaterHalfM(string cls) => cls switch { "river" => RiverHalfM, "stream" => StreamHalfM, "moat" => MoatHalfM, _ => -1 };

    void Barriers(List<BuiltPart> parts, bool[] wet, bool[] solid)
    {
        double edge = _cell * Corner;
        foreach (var part in parts)
        {
            var points = Points(part);
            if (part.Kind == PartKinds.Line && WaterHalfM(part.Class) >= 0)
                Mark(wet, points, WaterHalfM(part.Class) * _perMetre + edge, true);
            else if (part.Kind == PartKinds.Area && WaterAreas.Contains(part.Class) && points.Count >= 3)
                Fill(wet, points);
            else if (part.Kind == PartKinds.Line && Walls.Contains(part.Class))
                Mark(solid, points, edge, true);
            else if (part.Kind == PartKinds.Block && points.Count >= 3)
                Fill(solid, points);
        }
        foreach (var way in parts.Where(IsWay)) Mark(solid, Points(way), Grades.HalfM(way.Class) * _perMetre + edge, false);
    }

    void Mark(bool[] flags, List<Spot> points, double reach, bool value)
    {
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Spot a = points[i], b = points[i + 1];
            var (x0, y0, x1, y1) = Box(Math.Min(a.X, b.X) - reach, Math.Min(a.Y, b.Y) - reach, Math.Max(a.X, b.X) + reach, Math.Max(a.Y, b.Y) + reach);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (Centre(y * _cols + x).FromSegment(a, b) <= reach) flags[y * _cols + x] = value;
        }
    }

    void Fill(bool[] flags, List<Spot> ring)
    {
        var (x0, y0, x1, y1) = Box(ring.Min(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.X), ring.Max(p => p.Y));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Enclosures.Inside(ring, Centre(y * _cols + x))) flags[y * _cols + x] = true;
    }

    (int X0, int Y0, int X1, int Y1) Box(double lowX, double lowY, double highX, double highY) =>
        (Math.Max(0, (int)Math.Floor(lowX / _cell)), Math.Max(0, (int)Math.Floor(lowY / _cell)), Math.Min(_cols - 1, (int)Math.Floor(highX / _cell)),
            Math.Min(_rows - 1, (int)Math.Floor(highY / _cell)));

    int ClassIndex(string cls)
    {
        int at = _classes.IndexOf(cls);
        if (at >= 0) return at;
        _classes.Add(cls);
        return _classes.Count - 1;
    }

    int Node(Spot at, int line)
    {
        _at.Add(at);
        _line.Add(line);
        return _at.Count - 1;
    }

    void Link(int a, int b, int cls) => _edges.Add(new Edge(a, b, cls, (_at[b] - _at[a]).Length, HeightAt(_at[b]) - HeightAt(_at[a])));

    void LayWays(List<BuiltPart> parts)
    {
        var overlays = parts.Where(p => p.Kind == PartKinds.Line && Grades.Pinned(p.Class)).SelectMany(p =>
        {
            var points = Points(p);
            return points.Zip(points.Skip(1), (a, b) => (A: a, B: b, p.Class));
        }).ToList();
        double overlayReach = OverlayM * _perMetre;
        string ClassAt(string cls, Spot mid) => overlays.FirstOrDefault(o => mid.FromSegment(o.A, o.B) <= overlayReach).Class ?? cls;
        int line = 0;
        foreach (var part in parts.Where(IsWay))
        {
            var points = Points(part);
            int prev = Node(points[0], line);
            for (int i = 1; i < points.Count; i++)
            {
                Spot a = points[i - 1], b = points[i];
                int pieces = Math.Max(1, (int)Math.Ceiling((b - a).Length / _step));
                for (int k = 1; k <= pieces; k++)
                {
                    var at = a + (b - a) * (k / (double)pieces);
                    int next = Node(at, line);
                    Link(prev, next, ClassIndex(ClassAt(part.Class, (_at[prev] + at) * 0.5)));
                    prev = next;
                }
            }
            line++;
        }
    }

    void Crossing(BuiltPart sign)
    {
        string cls = CrossingOf(sign.Class)!;
        var centre = Spot.Of(BuiltGeometry.Points(sign.At)[0]);
        var along = Spot.Toward(sign.Turn * Math.PI / 1800);
        double reach = (cls == Travellers.Ferry ? FerryReachM : CrossingReachM) * _perMetre;
        if (Shore(centre, along, reach) is not { } a || Shore(centre, along * -1, reach) is not { } b) return;
        int line = _line.Count == 0 ? 0 : _line.Max() + 1;
        Link(Node(a, line), Node(b, line), ClassIndex(cls));
    }

    Spot? Shore(Spot from, Spot along, double reach)
    {
        for (double t = 0; t <= reach; t += _cell / 2)
        {
            var at = from + along * t;
            int c = CellOf(at);
            if (c < 0) return null;
            if (_speed[c] > 0) return at;
        }
        return null;
    }

    (long, long) BucketOf(Spot p) => ((long)Math.Floor(p.X / _step), (long)Math.Floor(p.Y / _step));

    void Knit()
    {
        for (int i = 0; i < _at.Count; i++)
        {
            var key = BucketOf(_at[i]);
            if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = new();
            list.Add(i);
        }
        double within = _step * JoinShare;
        for (int i = 0; i < _at.Count; i++)
            foreach (int j in NodesNear(_at[i], within))
                if (j > i && _line[j] != _line[i]) Link(i, j, -1);
        _links = new List<(int To, int Edge, bool Forward)>[_at.Count];
        for (int i = 0; i < _at.Count; i++) _links[i] = new();
        for (int e = 0; e < _edges.Count; e++)
        {
            _links[_edges[e].A].Add((_edges[e].B, e, true));
            _links[_edges[e].B].Add((_edges[e].A, e, false));
        }
        _nodeCell = new int[_at.Count];
        for (int i = 0; i < _at.Count; i++)
        {
            _nodeCell[i] = CellOf(_at[i]);
            if (_nodeCell[i] < 0) continue;
            if (!_cellNodes.TryGetValue(_nodeCell[i], out var list)) _cellNodes[_nodeCell[i]] = list = new();
            list.Add(i);
        }
    }

    IEnumerable<int> NodesNear(Spot p, double reach)
    {
        var (lowX, lowY) = BucketOf(p - new Spot(reach, reach));
        var (highX, highY) = BucketOf(p + new Spot(reach, reach));
        for (long y = lowY; y <= highY; y++)
            for (long x = lowX; x <= highX; x++)
                if (_buckets.TryGetValue((x, y), out var list))
                    foreach (int i in list)
                        if ((_at[i] - p).Length <= reach) yield return i;
    }

    int? NearestFree(Spot p)
    {
        int cx = Math.Clamp((int)Math.Floor(p.X / _cell), 0, _cols - 1), cy = Math.Clamp((int)Math.Floor(p.Y / _cell), 0, _rows - 1);
        int? best = null;
        double nearest = double.MaxValue;
        for (int ring = 0; ring < Math.Max(_cols, _rows); ring++)
        {
            for (int y = cy - ring; y <= cy + ring; y++)
                for (int x = cx - ring; x <= cx + ring; x++)
                {
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring || !Free(x, y)) continue;
                    double off = (Centre(y * _cols + x) - p).Length;
                    if (off >= nearest) continue;
                    nearest = off;
                    best = y * _cols + x;
                }
            if (best != null && nearest <= ring * _cell) return best;
        }
        return best;
    }
}
