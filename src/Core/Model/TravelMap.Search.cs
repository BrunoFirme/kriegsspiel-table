namespace KTable.Core.Model;

public sealed partial class TravelMap
{
    public const double RoadsideM = 30;
    const double Diagonal = 1.41421356, Even = 1e-12, Level = 1e-9;
    const int PullReach = 64;

    sealed record End(Spot Point, List<(int Node, double Seconds)> Links);

    sealed class Search(TravelMap map, Traveller who, Spot goal)
    {
        public readonly int Cells = map._cols * map._rows;
        public readonly double[] Spent = Filled(map._cols * map._rows + map._at.Count + 1, double.PositiveInfinity);
        public readonly int[] Came = Filled(map._cols * map._rows + map._at.Count + 1, -1);
        public readonly bool[] Done = new bool[map._cols * map._rows + map._at.Count + 1];
        public readonly PriorityQueue<int, double> Open = new();
        public readonly double[] Paces = map._classes.Select(who.On).ToArray();
        public readonly Dictionary<int, double> Goals = new();
        readonly double _fastest = map._classes.Select(who.On).Append(who.Open).Append(StepPace(who)).Max();
        public int Goal => Spent.Length - 1;

        static T[] Filled<T>(int count, T value)
        {
            var all = new T[count];
            Array.Fill(all, value);
            return all;
        }

        public Spot At(int node) => node < Cells ? map.Centre(node) : node == Goal ? goal : map._at[node - Cells];

        public void Relax(int node, double spent, int from)
        {
            if (Done[node] || spent >= Spent[node]) return;
            Spent[node] = spent;
            Came[node] = from;
            Open.Enqueue(node, spent + (At(node) - goal).Length / map._perMetre / _fastest);
        }
    }

    double Seconds(Spot a, Spot b, double pace) => (b - a).Length / _perMetre / pace;

    static double StepPace(Traveller who) => who.Open > 0 ? who.Open : who.Fastest;

    End? EndAt(Spot p, Traveller who)
    {
        double step = StepPace(who);
        int c = CellOf(p);
        if (_relief == null)
        {
            int? road = NodesNear(p, RoadsideM * _perMetre).OrderBy(i => (_at[i] - p).Length).Select(i => (int?)i).FirstOrDefault();
            return road is { } r ? new End(p, [(Cells + r, Seconds(p, _at[r], step))]) : null;
        }
        if (c >= 0 && _speed[c] > 0) return new End(p, Linked(p, c, who));
        int? near = NodesNear(p, _cell).OrderBy(i => (_at[i] - p).Length).Select(i => (int?)i).FirstOrDefault();
        if (near is { } way) return new End(_at[way], [(Cells + way, 0)]);
        if (NearestFree(p) is not { } free) return null;
        return new End(Centre(free), Linked(Centre(free), free, who));
    }

    List<(int Node, double Seconds)> Linked(Spot p, int c, Traveller who)
    {
        var links = new List<(int Node, double Seconds)>();
        if (who.Open > 0) links.Add((c, Seconds(p, Centre(c), who.Open * _speed[c])));
        foreach (int i in NodesNear(p, _cell)) links.Add((Cells + i, Seconds(p, _at[i], StepPace(who))));
        return links;
    }

    public Route? Find(Vec2 from, Vec2 to, Traveller who) => Find(from, to, who, out _);

    public Route? Find(Vec2 from, Vec2 to, Traveller who, out int expanded)
    {
        expanded = 0;
        if (who.Fastest <= 0 || EndAt(OnSheet(from), who) is not { } start || EndAt(OnSheet(to), who) is not { } end) return null;
        var s = new Search(this, who, end.Point);
        foreach (var (node, seconds) in end.Links) s.Goals[node] = Math.Min(seconds, s.Goals.GetValueOrDefault(node, double.PositiveInfinity));
        foreach (var (node, seconds) in start.Links) s.Relax(node, seconds, -1);
        while (s.Open.TryDequeue(out int cur, out _))
        {
            if (s.Done[cur]) continue;
            s.Done[cur] = true;
            if (cur == s.Goal) break;
            expanded++;
            if (s.Goals.TryGetValue(cur, out double last)) s.Relax(s.Goal, s.Spent[cur] + last, cur);
            if (cur < s.Cells) FromCell(s, who, cur);
            else FromNode(s, who, cur - s.Cells);
        }
        return double.IsPositiveInfinity(s.Spent[s.Goal]) ? null : Laid(s, who, start.Point);
    }

    void FromCell(Search s, Traveller who, int c)
    {
        double open = who.Open;
        int x = c % _cols, y = c / _cols;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || !Free(x + dx, y + dy)) continue;
                bool slant = dx != 0 && dy != 0;
                if (slant && (!Free(x + dx, y) || !Free(x, y + dy))) continue;
                int next = (y + dy) * _cols + x + dx;
                double run = slant ? _cell * Diagonal : _cell, grade = (_h[next] - _h[c]) / run;
                if (Math.Abs(grade) > HillPace.Steepest) continue;
                double seconds = run / _perMetre / (open * HillPace.Factor(grade)) * (1 / _speed[c] + 1 / _speed[next]) / 2;
                s.Relax(next, s.Spent[c] + seconds, c);
            }
        if (!_cellNodes.TryGetValue(c, out var nodes)) return;
        foreach (int i in nodes) s.Relax(s.Cells + i, s.Spent[c] + Seconds(Centre(c), _at[i], open * _speed[c]), c);
    }

    void FromNode(Search s, Traveller who, int i)
    {
        int self = s.Cells + i;
        foreach (var (to, e, forward) in _links[i])
        {
            var edge = _edges[e];
            double pace = edge.Class < 0 ? StepPace(who) : s.Paces[edge.Class];
            if (pace <= 0) continue;
            double grade = edge.Length > Level ? (forward ? edge.Rise : -edge.Rise) / edge.Length : 0;
            s.Relax(s.Cells + to, s.Spent[self] + edge.Length / _perMetre / (pace * HillPace.Factor(grade)), self);
        }
        if (who.Open > 0 && _nodeCell[i] is >= 0 and var c && _speed[c] > 0) s.Relax(c, s.Spent[self] + Seconds(_at[i], Centre(c), who.Open * _speed[c]), self);
    }

    Route Laid(Search s, Traveller who, Spot start)
    {
        var nodes = new List<int>();
        for (int at = s.Came[s.Goal]; at >= 0; at = s.Came[at]) nodes.Add(at);
        nodes.Reverse();
        var points = new List<Spot> { start };
        var seconds = new List<double>();
        var cells = new List<bool> { false };
        double before = 0;
        foreach (int node in nodes.Append(s.Goal))
        {
            points.Add(s.At(node));
            cells.Add(node < s.Cells);
            seconds.Add(s.Spent[node] - before);
            before = s.Spent[node];
        }
        cells[^1] = false;
        Pulled(points, seconds, cells, who.Open, PullReach);
        Pulled(points, seconds, cells, who.Open, int.MaxValue);
        Merged(points, seconds);
        return new Route(points.Select(World).ToList(), seconds);
    }

    void Pulled(List<Spot> points, List<double> seconds, List<bool> cells, double open, int reach)
    {
        if (open <= 0) return;
        var keptPoints = new List<Spot> { points[0] };
        var keptSeconds = new List<double>();
        var keptCells = new List<bool> { cells[0] };
        int i = 0;
        while (i < points.Count - 1)
        {
            int j = i + 1;
            double leg = seconds[i];
            if (cells[i] && cells[j])
            {
                double along = seconds[i];
                for (int k = j + 1; k < points.Count && cells[k] && k - i <= reach; k++)
                {
                    along += seconds[k - 1];
                    if (Straight(points[i], points[k], open) is not { } straight || straight > along) break;
                    j = k;
                    leg = straight;
                }
            }
            keptPoints.Add(points[j]);
            keptSeconds.Add(leg);
            keptCells.Add(cells[j]);
            i = j;
        }
        points.Clear();
        points.AddRange(keptPoints);
        seconds.Clear();
        seconds.AddRange(keptSeconds);
        cells.Clear();
        cells.AddRange(keptCells);
    }

    static void Merged(List<Spot> points, List<double> seconds)
    {
        for (int i = points.Count - 2; i >= 1; i--)
        {
            Spot a = points[i - 1], b = points[i], c = points[i + 1];
            Spot ab = b - a, bc = c - b;
            bool same = ab.Length <= Level || bc.Length <= Level;
            bool straight = Math.Abs(ab.X * bc.Y - ab.Y * bc.X) <= Level * ab.Length * bc.Length && ab.Dot(bc) > 0;
            if (!same && !straight) continue;
            seconds[i - 1] += seconds[i];
            seconds.RemoveAt(i);
            points.RemoveAt(i);
        }
    }

    double? Straight(Spot a, Spot b, double open)
    {
        var run = b - a;
        double length = run.Length;
        if (length <= Level) return 0;
        int x = (int)Math.Floor(a.X / _cell), y = (int)Math.Floor(a.Y / _cell), sx = Math.Sign(run.X), sy = Math.Sign(run.Y);
        double nextX = sx == 0 ? double.PositiveInfinity : ((sx > 0 ? x + 1 : x) * _cell - a.X) / run.X, stepX = sx == 0 ? double.PositiveInfinity : _cell / Math.Abs(run.X);
        double nextY = sy == 0 ? double.PositiveInfinity : ((sy > 0 ? y + 1 : y) * _cell - a.Y) / run.Y, stepY = sy == 0 ? double.PositiveInfinity : _cell / Math.Abs(run.Y);
        double t = 0, total = 0;
        while (true)
        {
            if (!Free(x, y)) return null;
            double until = Math.Min(1, Math.Min(nextX, nextY));
            if (until - t > Even)
            {
                Spot p = a + run * t, q = a + run * until;
                double piece = (until - t) * length, grade = (HeightAt(q) - HeightAt(p)) / piece;
                if (Math.Abs(grade) > HillPace.Steepest) return null;
                total += piece / _perMetre / (open * HillPace.Factor(grade) * _speed[y * _cols + x]);
            }
            if (until >= 1) return total;
            t = until;
            if (Math.Abs(nextX - nextY) <= Even)
            {
                if (!Free(x + sx, y) || !Free(x, y + sy)) return null;
                (x, y, nextX, nextY) = (x + sx, y + sy, nextX + stepX, nextY + stepY);
            }
            else if (nextX < nextY) (x, nextX) = (x + sx, nextX + stepX);
            else (y, nextY) = (y + sy, nextY + stepY);
        }
    }
}
