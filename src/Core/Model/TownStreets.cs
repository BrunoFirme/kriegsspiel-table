namespace KTable.Core.Model;

sealed record StreetRecipe(double Block, double Long, double Wander, double Radial, double Grid, double Bearing, double Blind, bool Aligned, int Ranks, string Side, double Thinning = 0.5,
    string Lane = "lane", double Order = 0.5, bool Cranked = false)
{
    public List<(Spot At, double Reach, double Bearing)> Quarters { get; init; } = [];
}

sealed class TownStreets
{
    const double PlannedRoom = 0.5;
    const double SampleM = 5, BucketM = 24, StepM = 6, SnapShare = 0.42, OwnShare = 0.8, TurnGrown = 0.13, TurnLaid = 0.02, SkewGrown = 0.45, Inertia = 1.1, PulledWithin = 1.05, BendEveryM =
        75, BendPlayM = 7, FillFrom = 0.95, FillReach = 3.2, PullReach = 2.2, StreetPull = 2.4, SlopePull = 2.2, SlopeFull = 0.14, SlopeFrom = 0.025;
    const double ShorePull = 1.6, ShoreReachM = 75, RoughM = 230, LeastM = 26, LeastShare = 0.55, MostLongs = 7, ReachOn = 0.8, ProbeAt = 0.5, ProbeRoom = 0.38, JoinRoom = 0.55;
    const double TipAside = 0.6, TipLeast = 0.3, GateGap = 0.85, GateJitter = 0.3, JoinAfterM = 28, JoinCells = 1.4, EveryCells = 3, EdgeRoom = 0.35, SpacingPlay = 0.28, QuarterPull = 9;
    const double DenseCore = 0.8, ThinCore = 0.55, ClimbFare = 8, ClimbCounted = 0.45, WheeledWithin = 1.05;
    const int MostWays = 520, Smoothings = 2, FillRounds = 3;
    const double MostVeer = 1.75, BankRoom = 1.6, ThroughTurn = 0.4, AxisFirst = 0.4, CrankAt = 0.36, CrankM = 20, CrankFromM = 130, MeetCells = 4, ClimbApart = 8, MostClimb = 0.2, FillClimb =
        0.16, OwnTail = 2.2, QuarterAligned =
        0.4, EdgeCompact = 1.2, EdgeSprawled = 1.8, ThinCompact = 1.15, ThinSprawled = 2, YardRoomM = 2.5;

    readonly record struct Sample(Spot At, Spot Along, int Way, double S);

    sealed class Way(List<Spot> points, string cls, bool laid, int rank, int side)
    {
        public List<Spot> Points = points;
        public string Class = cls;
        public bool Laid = laid;
        public int Rank = rank, Side = side;
        public List<(double S, int Side)> Joins = new();
    }

    readonly BuiltSite _site;
    readonly TownGround _ground;
    readonly Dice _dice;
    readonly StreetRecipe _recipe;
    readonly int _seed;
    readonly double _bucket;
    readonly List<Way> _ways = new();
    readonly Dictionary<(int, int), List<Sample>> _near = new();
    List<(Spot At, double Reach)> _taken = [];
    List<BuiltPart> _yards = [];

    public TownStreets(BuiltSite site, TownGround ground, Dice dice, StreetRecipe recipe, int seed) =>
        (_site, _ground, _dice, _recipe, _seed, _bucket) = (site, ground, dice, recipe, seed, site.M(BucketM));

    public List<Settlements.Street> Streets => _ways.Select(w => new Settlements.Street(w.Points, w.Class, w.Laid, w.Side, Dense(w), w.Rank == 0 || w.Rank == int.MaxValue)).ToList();

    double Dense(Way way) => Settlements.Lerp(ThinCore, Settlements.Lerp(ThinCompact, ThinSprawled, _recipe.Thinning), _ground.Central(Along(way.Points, Long(way.Points) / 2).At));

    internal static double Long(List<Spot> points) => Enumerable.Range(0, points.Count - 1).Sum(i => (points[i + 1] - points[i]).Length);

    internal static (Spot At, Spot Along) Along(List<Spot> points, double s)
    {
        for (int i = 0; i + 1 < points.Count; i++)
        {
            double leg = (points[i + 1] - points[i]).Length;
            if (s <= leg || i + 2 == points.Count) return (points[i] + (points[i + 1] - points[i]).Unit * Math.Clamp(s, 0, leg), (points[i + 1] - points[i]).Unit);
            s -= leg;
        }
        return (points[0], new Spot(1, 0));
    }

    (int, int) Bucket(Spot p) => ((int)Math.Floor(p.X / _bucket), (int)Math.Floor(p.Y / _bucket));

    int Add(Way way)
    {
        int index = _ways.Count;
        _ways.Add(way);
        double length = Long(way.Points), every = _site.M(SampleM);
        for (double s = 0; s <= length; s += every)
        {
            var (at, along) = Along(way.Points, s);
            var key = Bucket(at);
            if (!_near.TryGetValue(key, out var list)) _near[key] = list = new();
            list.Add(new Sample(at, along, index, s));
        }
        return index;
    }

    Sample? Nearest(Spot p, double reach, Func<Sample, bool>? counts = null)
    {
        Sample? best = null;
        double least = reach;
        var (bx, by) = Bucket(p);
        int around = (int)Math.Ceiling(reach / _bucket);
        for (int y = by - around; y <= by + around; y++)
            for (int x = bx - around; x <= bx + around; x++)
            {
                if (!_near.TryGetValue((x, y), out var list)) continue;
                foreach (var sample in list)
                {
                    double off = (sample.At - p).Length;
                    if (off >= least || counts != null && !counts(sample)) continue;
                    (least, best) = (off, sample);
                }
            }
        return best;
    }

    public void Given(IEnumerable<(Spot A, Spot B)> roads)
    {
        double step = _ground.Cell * 0.5;
        foreach (var (a, b) in roads)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
            Spot? from = null, last = null;
            for (int s = 0; s <= steps; s++)
            {
                var p = a + (b - a) * (s / (double)steps);
                bool held = _ground.Held(p) || _ground.Spanned(p);
                if (held) (from, last) = (from ?? p, p);
                if ((!held || s == steps) && from is { } start && last is { } end)
                {
                    if ((end - start).Length > step) Add(new Way([start, end], "road", false, 0, Settlements.BothSides));
                    (from, last) = (null, null);
                }
            }
        }
    }

    static double Apart(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

    int? Tip(double bearing, double reach)
    {
        var way = Spot.Toward(bearing);
        (int Cell, double Score)? best = null;
        foreach (int i in _ground.HeldCells)
        {
            var off = _ground.Mid(i) - _ground.Centre;
            double score = off.Dot(way) - TipAside * Math.Abs(off.Dot(way.Left));
            if (best is not { } known || score > known.Score) best = (i, score);
        }
        return best is { } found && found.Score > reach * TipLeast ? found.Cell : null;
    }

    double Climbed(Spot a, Spot b, string cls)
    {
        double length = (b - a).Length, rise = 0;
        int steps = Math.Max(1, (int)Math.Ceiling(length / (_ground.Cell * 0.5)));
        for (int k = 0; k < steps; k++) rise += Math.Abs(_site.HeightAt(a + (b - a) * ((k + 1) / (double)steps)) - _site.HeightAt(a + (b - a) * (k / (double)steps)));
        double grade = Math.Min(ClimbCounted, rise / Math.Max(length, 1e-9));
        return 1 + ClimbFare * Math.Max(0, grade - Grades.Of(_site.Manner, cls));
    }

    List<Spot> Pulled(List<int> cells, Spot last, string cls)
    {
        var kept = new List<Spot> { _ground.Mid(cells[0]) };
        Spot At(int i) => i == cells.Count - 1 ? last : _ground.Mid(cells[i]);
        for (int i = 0; i + 1 < cells.Count;)
        {
            int j = cells.Count - 1;
            while (j > i + 1 && _ground.Fare(At(i), At(j)) * Climbed(At(i), At(j), cls) > (_ground.Cost(cells[i]) - _ground.Cost(cells[j])) * PulledWithin) j--;
            var (from, to) = (At(i), At(j));
            int bends = (int)((to - from).Length / _site.M(BendEveryM));
            for (int k = 1; k <= bends; k++)
            {
                var bent = from + (to - from) * (k / (bends + 1.0))
                    + (to - from).Unit.Left * (_site.M(BendPlayM) * 2 * (1 - _recipe.Order) * (2 * TownGround.Rough(from.X * 7.1 + k, to.Y * 3.3, _seed) - 1));
                if (!Blocked(bent)) kept.Add(bent);
            }
            kept.Add(to);
            i = j;
        }
        return kept;
    }

    static List<Spot> Smooth(List<Spot> kept)
    {
        for (int pass = 0; pass < Smoothings && kept.Count > 2; pass++)
        {
            var next = new List<Spot> { kept[0] };
            for (int i = 0; i + 1 < kept.Count; i++)
            {
                if (i > 0) next.Add(kept[i] + (kept[i + 1] - kept[i]) * 0.25);
                if (i + 2 < kept.Count) next.Add(kept[i] + (kept[i + 1] - kept[i]) * 0.75);
            }
            next.Add(kept[^1]);
            kept = next;
        }
        return kept;
    }

    List<Spot> Cranked(List<Spot> line)
    {
        double length = Long(line), from = length * CrankAt, to = length * (1 - CrankAt), gone = 0;
        if (!_recipe.Cranked || length < _site.M(CrankFromM)) return line;
        var (first, way) = Along(line, from);
        var last = Along(line, to).At;
        var aside = way.Left * (_site.M(CrankM) * (_dice.Chance(0.5) ? 1 : -1));
        List<Spot> before = [line[0]], between = [], after = [];
        for (int i = 1; i < line.Count; i++)
        {
            gone += (line[i] - line[i - 1]).Length;
            (gone < from ? before : gone > to ? after : between).Add(gone < from || gone > to ? line[i] : line[i] + aside);
        }
        List<Spot> cranked = [.. before, first, first + aside, .. between, last + aside, last, .. after];
        return cranked.Any(Blocked) ? line : cranked;
    }

    bool Main(int gate, string cls)
    {
        var home = _ground.Home(gate);
        double gone = 0, join = _ground.Cell * JoinCells;
        var path = new List<int> { home[0] };
        Sample? onto = null;
        for (int i = 1; i < home.Count && onto == null; i++)
        {
            gone += (_ground.Mid(home[i]) - _ground.Mid(home[i - 1])).Length;
            onto = gone > _site.M(JoinAfterM) ? Nearest(_ground.Mid(home[i]), join) : null;
            path.Add(home[i]);
        }
        if (onto is { } met && _ways[met.Way] is { Rank: 0, Laid: true } other && (other.Points[^1] - _ground.Centre).Length < 1e-9 && (met.At - _ground.Centre).Length < _ground.Cell * MeetCells)
            (onto, path) = (null, path.Append(_ground.At(_ground.Centre)).ToList());
        if (gone < _site.M(LeastM)) return false;
        var smooth = Cranked(Smooth(Pulled(path, onto?.At ?? (_ground.Cost(path[^1]) <= 0 ? _ground.Centre : _ground.Mid(path[^1])), cls)));
        if (onto == null && _ground.Cost(path[^1]) <= 0) smooth[^1] = _ground.Centre;
        int made = Add(new Way(smooth, cls, true, 0, Settlements.BothSides));
        if (onto is { } hit) _ways[hit.Way].Joins.Add((hit.S, Math.Sign((smooth[^2] - hit.At).Dot(hit.Along.Left))));
        return made >= 0;
    }

    public void Mains(int wanted, Spot axis, double reach, string cls)
    {
        var served = new List<double>();
        foreach (var way in _ways)
            foreach (var end in new[] { way.Points[0], way.Points[^1] })
                if ((end - _ground.Centre).Length > reach * TipLeast) served.Add(Math.Atan2(end.Y - _ground.Centre.Y, end.X - _ground.Centre.X));
        if (_ways.Count > 0 && Nearest(_ground.Centre, reach) is { } road && (road.At - _ground.Centre).Length > _ground.Cell * JoinCells && _ground.At(road.At) is >= 0 and var from)
        {
            var link = Smooth(Pulled(_ground.Home(from), _ground.Centre, cls));
            if (Long(link) > _site.M(LeastM))
            {
                link[0] = road.At;
                Add(new Way(link, cls, true, 0, Settlements.BothSides));
            }
        }
        foreach (int span in _ground.Spans)
            if (_ground.HeldCells.Where(i => _ground.Over(i, span)).OrderByDescending(_ground.Cost).Cast<int?>().FirstOrDefault() is { } beyond && Main(beyond, cls))
                served.Add(Math.Atan2(_ground.Mid(beyond).Y - _ground.Centre.Y, _ground.Mid(beyond).X - _ground.Centre.X));
        double along = Math.Atan2(axis.Y, axis.X);
        double[] turns = [0, Math.PI, Math.PI / 2, -Math.PI / 2, Math.PI / 4, -3 * Math.PI / 4, 3 * Math.PI / 4, -Math.PI / 4];
        foreach (double turn in turns)
        {
            if (served.Count >= wanted) break;
            double bearing = along + turn + _dice.Between(-GateJitter, GateJitter);
            if (served.Any(known => Apart(known, bearing) < GateGap) || Tip(bearing, reach) is not { } gate) continue;
            var at = _ground.Mid(gate) - _ground.Centre;
            double actual = Math.Atan2(at.Y, at.X);
            if (served.Any(known => Apart(known, actual) < GateGap * 0.6) || !Main(gate, cls)) continue;
            served.Add(actual);
        }
        Through(axis.Unit);
    }

    void Through(Spot axis)
    {
        var ended = _ways.Where(w => w.Laid && w.Rank == 0 && w.Points.Count > 1 && (w.Points[^1] - _ground.Centre).Length < 1e-9).ToList();
        if (ended.Count < 2) return;
        Spot Way(Way w) => (w.Points[^1] - w.Points[^2]).Unit;
        var (first, second) = ended.SelectMany(a => ended.Where(b => b != a).Select(b => (a, b)))
            .MinBy(pair => Way(pair.a).Dot(Way(pair.b)) - AxisFirst * (Math.Abs(Way(pair.a).Dot(axis)) + Math.Abs(Way(pair.b).Dot(axis))));
        if (Way(first).Dot(Way(second)) > -ThroughTurn) return;
        first.Points = first.Points.Concat(Enumerable.Reverse(second.Points).Skip(1)).ToList();
        first.Joins.Clear();
        _ways.Remove(second);
        _near.Clear();
        var all = _ways.ToList();
        _ways.Clear();
        foreach (var way in all)
        {
            way.Joins.Clear();
            Add(way);
        }
    }

    double Field(Spot p, Spot going)
    {
        double x = 0, y = 0;
        void Pull(double angle, double weight) => (x, y) = (x + weight * Math.Cos(2 * angle), y + weight * Math.Sin(2 * angle));
        Pull(Math.Atan2(going.Y, going.X), Inertia * (1 - Math.Min(1, _recipe.Wander)) + 0.2);
        double reach = _recipe.Block * PullReach;
        if (Nearest(p, reach) is { } near) Pull(Math.Atan2(near.Along.Y, near.Along.X), StreetPull * Math.Pow(1 - (near.At - p).Length / reach, 2));
        var fall = _site.Downhill(p);
        if (fall.Length > SlopeFrom) Pull(Math.Atan2(fall.Left.Y, fall.Left.X), SlopePull * Math.Min(1, fall.Length / SlopeFull));
        double shore = _ground.ShoreAway(p), shoreReach = _site.M(ShoreReachM);
        if (shore < shoreReach && _ground.ShoreWay(p) is { Length: > 0 } bank) Pull(Math.Atan2(bank.Y, bank.X), ShorePull * (1 - shore / shoreReach));
        double rough = _site.M(RoughM);
        double drift = 2 * Math.PI * TownGround.Rough(p.X / rough, p.Y / rough, _seed + 31);
        var wander = new Spot(Math.Cos(2 * drift), Math.Sin(2 * drift)) * _recipe.Wander;
        (x, y) = (x + wander.X, y + wander.Y);
        var out_ = p - _ground.Centre;
        if (_recipe.Radial > 0 && out_.Length > _recipe.Block * 0.5) Pull(Math.Atan2(out_.Y, out_.X), _recipe.Radial);
        if (_recipe.Grid > 0) Pull(_recipe.Bearing, _recipe.Grid);
        foreach (var quarter in _recipe.Quarters)
            Pull(quarter.Bearing, QuarterPull * Hold(quarter, p));
        double planned = Planned(p);
        if (planned > 0) (x, y) = (x - wander.X * planned, y - wander.Y * planned);
        return 0.5 * Math.Atan2(y, x);
    }

    double Veer(Spot p, Spot dir, string cls)
    {
        double field = Field(p, dir);
        var want = Ways(p, field, cls).MaxBy(way => way.Dot(dir));
        double most = Settlements.Lerp(TurnGrown, TurnLaid, _recipe.Order);
        return Math.Clamp(Math.Atan2(dir.X * want.Y - dir.Y * want.X, dir.Dot(want)), -most, most);
    }

    IEnumerable<Spot> Ways(Spot p, double field, string cls)
    {
        var axes = Enumerable.Range(0, 4).Select(k => Spot.Toward(field + k * Math.PI / 2)).ToList();
        var fall = _site.Downhill(p);
        double grade = Grades.Of(_site.Manner, cls), slope = fall.Length;
        if (slope <= grade) return axes;
        double turn = Math.Asin(grade / slope);
        var oblique = new[] { turn, -turn, Math.PI + turn, Math.PI - turn }.Select(t => fall.Unit.Left.Turned(t)).ToList();
        if (cls == "street") axes.RemoveAll(way => Math.Abs(way.Dot(fall)) > grade * WheeledWithin);
        return axes.Concat(oblique);
    }

    bool Blocked(Spot p) => !_ground.Passable(p) || _taken.Any(o => (o.At - p).Length < o.Reach) || _yards.Any(yard => BuiltThings.Reach(yard, p) < _site.M(YardRoomM));

    double BlockAt(Spot p) => _recipe.Block * Settlements.Lerp(DenseCore, Settlements.Lerp(EdgeCompact, EdgeSprawled, _recipe.Thinning), _ground.Central(p)) * (1 + Planned(p) * PlannedRoom);

    static double Hold((Spot At, double Reach, double Bearing) quarter, Spot p) => (p - quarter.At).Length < quarter.Reach ? 1 - Math.Pow((p - quarter.At).Length / quarter.Reach, 4) : 0;

    double Planned(Spot p) => _recipe.Quarters.Select(quarter => Hold(quarter, p)).DefaultIfEmpty(0).Max();

    public void Axes(Spot axis, string cls)
    {
        double step = _site.M(StepM);
        foreach (var way in new[] { axis, axis.Left })
        {
            Spot Far(int sign)
            {
                var p = _ground.Centre;
                while (_ground.Held(p + way * (sign * step)) && !Blocked(p + way * (sign * step))) p = p + way * (sign * step);
                return p;
            }
            var (from, to) = (Far(-1), Far(1));
            string laid = Math.Abs(_site.Downhill(_ground.Centre).Dot(way)) > Grades.Of(_site.Manner, cls) ? "street" : cls;
            if ((to - from).Length > _site.M(LeastM)) Add(new Way([from, to], laid, true, 0, Settlements.BothSides));
        }
    }

    public Spot? Edge(double bearing, double reach) => Tip(bearing, reach) is { } cell ? _ground.Mid(cell) : null;

    void Branch(int parent, double s, Spot from, Spot dir, double block)
    {
        var probe = from + dir * (block * ProbeAt);
        if (_ground.ShoreAway(from) < _ground.Cell * BankRoom || !_ground.Held(probe) || Blocked(probe) || Nearest(probe, block * ProbeRoom, sample => sample.Way != parent) != null) return;
        double step = _site.M(StepM), snap = block * SnapShare, gone = 0, most = _recipe.Long * MostLongs, veered = 0;
        string cls = _ways[parent].Rank == 0 ? _recipe.Side : _recipe.Lane;
        var points = new List<Spot> { from };
        var p = from;
        Sample? onto = null;
        bool stopped = false;
        int recent = (int)(snap * OwnTail / step) + 2;
        bool Counts(Sample sample) => !(sample.Way == parent && (sample.At - from).Length < block * OwnShare);
        while (gone < most && onto == null)
        {
            double veer = Veer(p, dir, cls);
            (dir, veered) = (dir.Turned(veer).Unit, veered + veer);
            var q = p + dir * step;
            stopped = Blocked(q) || _ground.Spanned(q) || Math.Abs(_site.HeightAt(q) - _site.HeightAt(p)) > step * Grades.StairsSteepest || Math.Abs(veered) > MostVeer;
            if (stopped || !_ground.Held(q)) break;
            onto = gone + step > snap && Nearest(q, snap, Counts) is { } met && Clear(p, met.At) ? met : null;
            if (onto == null && points.Take(Math.Max(0, points.Count - recent)).Any(own => (own - q).Length < snap)) { stopped = true; break; }
            points.Add(onto?.At ?? q);
            (p, gone) = (q, gone + step);
        }
        if (onto == null && Nearest(p + dir * (block * ReachOn * 0.5), block * ReachOn, sample => Counts(sample) && (sample.At - p).Dot(dir) > 0) is { } ahead && Clear(p, ahead.At))
        {
            points.Add(ahead.At);
            onto = ahead;
        }
        double length = Long(points);
        if (length < Math.Max(_site.M(LeastM), block * LeastShare) || onto == null && stopped && !_dice.Chance(_recipe.Blind * (1 - Planned(p)))) return;
        var way = new Way(Thinned(points), cls, true, _ways[parent].Rank + 1, Settlements.BothSides);
        Add(way);
        _ways[parent].Joins.Add((s, Math.Sign(dir.Dot(Along(_ways[parent].Points, s).Along.Left)) is var side && side != 0 ? side : 1));
        if (onto is { } hit) _ways[hit.Way].Joins.Add((hit.S, Math.Sign((points[^2] - hit.At).Dot(hit.Along.Left))));
    }

    bool Clear(Spot a, Spot b)
    {
        int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / _site.M(StepM)));
        return Enumerable.Range(1, steps).All(k => !Blocked(a + (b - a) * (k / (double)steps)));
    }

    static List<Spot> Thinned(List<Spot> points)
    {
        const double Within = 0.012;
        var kept = new List<Spot> { points[0] };
        for (int i = 1; i + 1 < points.Count; i++)
            if (points[i].FromSegment(kept[^1], points[i + 1]) > Within * (points[i + 1] - kept[^1]).Length) kept.Add(points[i]);
        kept.Add(points[^1]);
        return kept;
    }

    void Seed(int index)
    {
        var parent = _ways[index];
        double length = Long(parent.Points);
        double first = _dice.Between(0.3, 0.8);
        foreach (int side in new[] { 1, -1 })
        {
            if (parent.Side != Settlements.BothSides && parent.Side != side) continue;
            bool aligned = _recipe.Aligned || Planned(Along(parent.Points, length / 2).At) > QuarterAligned;
            var (middle, heading) = Along(parent.Points, length / 2);
            double steep = Math.Min(MostClimb, Math.Abs(_site.Downhill(middle).Dot(heading.Left)));
            double s = (parent.Rank % 2 == 0 ? _recipe.Long : _recipe.Block) * (1 + steep * ClimbApart) * (aligned ? first : _dice.Between(0.3, 0.9));
            while (s < length - _recipe.Block * EdgeRoom && _ways.Count < MostWays)
            {
                var (at, along) = Along(parent.Points, s);
                double block = BlockAt(at), climb = Math.Min(MostClimb, Math.Abs(_site.Downhill(at).Dot(along.Left)));
                double every = (parent.Rank % 2 == 0 ? _recipe.Long : _recipe.Block) * block / _recipe.Block * (1 + climb * ClimbApart);
                double skew = aligned ? 0 : _dice.Between(-1, 1) * SkewGrown * Math.Pow(1 - _recipe.Order, 2);
                if (!parent.Joins.Any(join => join.Side == side && Math.Abs(join.S - s) < every * JoinRoom)) Branch(index, s, at, (along.Left * side).Turned(skew), block);
                s += every * (aligned ? 1 : _dice.Between(1 - SpacingPlay, 1 + SpacingPlay));
            }
        }
    }

    public Spot Centre => _ground.Centre;

    public void Linked(Spot joined, string cls)
    {
        var ends = _ways.Where(w => w.Laid).SelectMany(w => new[] { w.Points[0], w.Points[^1] }).ToList();
        if (ends.Count == 0) return;
        var end = ends.MinBy(p => (p - joined).Length);
        if (_site.Buildable(end + (joined - end) * 0.5)) Add(new Way([end, joined], cls, true, int.MaxValue, Settlements.BothSides));
    }

    public List<Settlements.Street> Grown(IEnumerable<Settlements.Street> added, List<(Spot At, double Reach)> taken, IEnumerable<BuiltPart> yards)
    {
        _yards = yards.ToList();
        foreach (var street in added) Add(new Way(street.Points, street.Class, street.Laid, int.MaxValue, street.Side));
        _taken = taken;
        int index = 0;
        for (int round = 0; round <= FillRounds; round++)
        {
            for (; index < _ways.Count && _ways.Count < MostWays; index++)
                if (_ways[index].Rank < _recipe.Ranks) Seed(index);
            if (round == FillRounds || _recipe.Ranks == 0 || !Filled()) break;
        }
        return Streets;
    }

    bool Filled()
    {
        int before = _ways.Count;
        var tried = new HashSet<(int, int)>();
        foreach (int cell in _ground.HeldCells.OrderBy(_ground.Cost))
        {
            var p = _ground.Mid(cell);
            double block = BlockAt(p);
            if (_ways.Count >= MostWays || Blocked(p) || Nearest(p, block * FillFrom) != null) continue;
            if (Nearest(p, block * FillReach, sample => _ways[sample.Way].Rank < int.MaxValue) is not { } near || !tried.Add((near.Way, (int)(near.S / block)))) continue;
            int side = Math.Sign((p - near.At).Dot(near.Along.Left));
            if (Math.Abs(_site.Downhill(near.At).Dot(near.Along.Left)) > FillClimb) continue;
            if (side != 0 && (_ways[near.Way].Side == Settlements.BothSides || _ways[near.Way].Side == side)) Branch(near.Way, near.S, near.At, near.Along.Left * side, block);
        }
        return _ways.Count > before;
    }
}
