namespace KTable.Core.Model;

static class Footprints
{
    const double InsetM = 0.4, SampleM = 1, IntoWayM = 0.5, LeastRunM = 2, StockM = 9, IndexM = 30;
    static readonly string[] Ways = ["track", "lane", "street", "road", "chaussee", "highway", "railway", "boulevard"];
    static readonly string[] Bounds = ["fence", "hedge", Settlements.GardenWall, "wardwall", Terraces.Wall];
    static readonly string[] Sheds = ["shed", "privy", "coop", "pigsty", "kennel", "outshut"];
    static readonly string[] Yard = ["tree", "hive", "rick", "stooks", "wheels", "anvil", "washing", "rock", "oven", "shed"];

    readonly record struct Way(Spot A, Spot B, double Half, string Class)
    {
        public double Into(Spot p) => Half - p.FromSegment(A, B);

        public bool Near(double x0, double y0, double x1, double y1) =>
            Math.Min(A.X, B.X) - Half <= x1 && Math.Max(A.X, B.X) + Half >= x0 && Math.Min(A.Y, B.Y) - Half <= y1 && Math.Max(A.Y, B.Y) + Half >= y0;
    }

    sealed class Index
    {
        readonly Dictionary<(int, int), List<Way>> _cells = new();
        readonly double _cell;

        public Index(IEnumerable<Way> ways, double cell)
        {
            _cell = cell;
            foreach (var way in ways)
                for (int x = Cell(Math.Min(way.A.X, way.B.X) - way.Half); x <= Cell(Math.Max(way.A.X, way.B.X) + way.Half); x++)
                    for (int y = Cell(Math.Min(way.A.Y, way.B.Y) - way.Half); y <= Cell(Math.Max(way.A.Y, way.B.Y) + way.Half); y++)
                        (_cells.TryGetValue((x, y), out var list) ? list : _cells[(x, y)] = new()).Add(way);
        }

        int Cell(double v) => (int)Math.Floor(v / _cell);

        public List<Way> Near(double x0, double y0, double x1, double y1)
        {
            var found = new HashSet<Way>();
            for (int x = Cell(x0); x <= Cell(x1); x++)
                for (int y = Cell(y0); y <= Cell(y1); y++)
                    if (_cells.TryGetValue((x, y), out var list)) found.UnionWith(list.Where(w => w.Near(x0, y0, x1, y1)));
            return found.ToList();
        }
    }

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    internal static void Settle(BuiltSite site, List<BuiltPart> parts, IReadOnlySet<BuiltPart> landmarks)
    {
        bool Free(BuiltPart p) => !p.Kept && !landmarks.Contains(p);
        var ways = new Index(parts.Concat(site.Others.SelectMany(o => o.Parts)).Where(p => p.Kind == PartKinds.Line && Ways.Contains(p.Class))
            .SelectMany(p => Legs(Points(p)).Select(leg => new Way(leg.A, leg.B, site.M(Grades.HalfM(p.Class)), p.Class))), site.M(IndexM));
        var footing = Slopes.Of(site.Manner).House;
        var dropped = parts.Where(p => Free(p) && p.Kind == PartKinds.Block && p.At.Count >= 6 && p.Class != Fieldworks.Cave)
            .Where(p => !Sits(site, footing, Points(p)) || InWay(site, ways, Points(p))).ToList();
        var houses = parts.Where(p => p.Kind == PartKinds.Block && p.At.Count >= 6 && !Sheds.Contains(p.Class) && !dropped.Contains(p)).Select(Points).Select(r => (Ring: r, Box: Box(r))).ToList();
        dropped.AddRange(parts.Where(p => Free(p) && p.Kind == PartKinds.Block && Sheds.Contains(p.Class) && !dropped.Contains(p) && Shrunk(site, Points(p)) is var shed && Box(shed) is var box
            && houses.Any(h => box.X0 <= h.Box.X1 && h.Box.X0 <= box.X1 && box.Y0 <= h.Box.Y1 && h.Box.Y0 <= box.Y1 && Overlap(shed, h.Ring))).ToList());
        var gone = new HashSet<BuiltPart>(dropped, ReferenceEqualityComparer.Instance);
        var stock = parts.Where(p => Free(p) && (Kept("coop", p) || Kept("pigsty", p))).Select(p => (Part: p, At: Points(p))).ToList();
        foreach (var shed in dropped.Where(d => d.Class is "coop" or "pigsty").Select(d => (d.Class, BuiltSite.Round(d).At)))
            gone.UnionWith(stock.Where(s => Kept(shed.Class, s.Part) && s.At.All(q => (q - shed.At).Length < site.M(StockM))).Select(s => s.Part));
        parts.RemoveAll(gone.Contains);
        double into = site.M(IntoWayM);
        parts.RemoveAll(p => Free(p) && p.Kind == PartKinds.Sign && p.At.Count >= 2 && Yard.Contains(p.Class)
            && Deepest(ways, Points(p)[0]) is var (depth, cls) && depth > into && !(p.Class == "tree" && cls == Settlements.Boulevard));
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var part = parts[i];
            if (part.Kept || part.Kind != PartKinds.Line || !Bounds.Contains(part.Class)) continue;
            var runs = Outside(site, ways, Points(part));
            if (runs.Count == 1 && runs[0].Count == part.At.Count / 2) continue;
            parts.RemoveAt(i);
            parts.InsertRange(i, runs.Select(run => BuiltSite.Line(part.Class, run)));
        }
    }

    static bool Kept(string shed, BuiltPart p) => shed switch
    {
        "coop" => p.Kind == PartKinds.Sign && p.Class == "hens",
        "pigsty" => p.Kind == PartKinds.Sign && p.Class == "pigs" || p.Kind == PartKinds.Area && p.Class == "pen" || p.Kind == PartKinds.Line && p.Class == "fence",
        _ => false,
    };

    static IEnumerable<(Spot A, Spot B)> Legs(IReadOnlyList<Spot> line)
    {
        for (int i = 0; i + 1 < line.Count; i++) yield return (line[i], line[i + 1]);
    }

    static (double Depth, string Class) Deepest(Index ways, Spot p)
    {
        (double Depth, string Class) best = (double.MinValue, "");
        foreach (var way in ways.Near(p.X, p.Y, p.X, p.Y))
            if (way.Into(p) is var depth && depth > best.Depth) best = (depth, way.Class);
        return best;
    }

    internal static bool Sits(BuiltSite site, Footing footing, List<Spot> ring)
    {
        if (site.Grid == null) return true;
        double Metres(Spot p) => site.HeightAt(p) / site.UnitsPerMetre;
        var heights = Filled(ring, site.M(SampleM)).Select(Metres).ToList();
        return Footings.Sits(footing, heights.Min(), heights.Max(), (Metres(ring[0]) + Metres(ring[1])) / 2);
    }

    static (double X0, double Y0, double X1, double Y1) Box(List<Spot> ring) => (ring.Min(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.X), ring.Max(p => p.Y));

    static List<Spot> Shrunk(BuiltSite site, List<Spot> ring)
    {
        var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
        return ring.Select(p => (p - middle).Length <= site.M(InsetM) ? middle : p + (middle - p).Unit * site.M(InsetM)).ToList();
    }

    static bool Overlap(List<Spot> a, List<Spot> b) =>
        a.Any(p => Enclosures.Inside(b, p)) || b.Any(p => Enclosures.Inside(a, p)) || Legs(a.Append(a[0]).ToList()).Any(e => Legs(b.Append(b[0]).ToList()).Any(f => Crosses(e.A, e.B, f.A, f.B)));

    static bool Crosses(Spot a, Spot b, Spot c, Spot d)
    {
        static double Turn(Spot o, Spot p, Spot q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        return Turn(a, b, c) * Turn(a, b, d) < 0 && Turn(c, d, a) * Turn(c, d, b) < 0;
    }

    static bool InWay(BuiltSite site, Index ways, List<Spot> ring)
    {
        var inner = Shrunk(site, ring);
        var (x0, y0, x1, y1) = Box(inner);
        var near = ways.Near(x0, y0, x1, y1);
        if (near.Count == 0) return false;
        return near.Any(w => Enclosures.Inside(inner, w.A) || Enclosures.Inside(inner, w.B)) || Filled(inner, site.M(SampleM)).Any(q => near.Any(w => w.Into(q) > 0));
    }

    static IEnumerable<Spot> Filled(List<Spot> ring, double step)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
            for (int k = 0; k < n; k++) yield return a + (b - a) * (k / (double)n);
        }
        double x0 = ring.Min(p => p.X), y0 = ring.Min(p => p.Y), x1 = ring.Max(p => p.X), y1 = ring.Max(p => p.Y);
        for (double x = x0 + step / 2; x < x1; x += step)
            for (double y = y0 + step / 2; y < y1; y += step)
                if (Enclosures.Inside(ring, new Spot(x, y))) yield return new Spot(x, y);
    }

    static List<List<Spot>> Outside(BuiltSite site, Index ways, List<Spot> line)
    {
        double step = site.M(SampleM) / 2;
        var runs = new List<List<Spot>>();
        var run = new List<Spot>();
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Spot a = line[i], b = line[i + 1];
            var near = ways.Near(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            if (near.Count == 0)
            {
                if (run.Count == 0) run.Add(a);
                run.Add(b);
                continue;
            }
            int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
            Spot last = a;
            for (int k = 0; k <= n; k++)
            {
                var q = a + (b - a) * (k / (double)n);
                if (near.Any(w => w.Into(q) > 0))
                {
                    if (run.Count > 0 && run[^1] != last) run.Add(last);
                    if (run.Count > 1) runs.Add(run);
                    run = new();
                    continue;
                }
                if (run.Count == 0 || k == n) run.Add(q);
                last = q;
            }
        }
        if (run.Count > 1) runs.Add(run);
        return runs.Where(r => Length(r) >= site.M(LeastRunM)).ToList();
    }

    static double Length(List<Spot> run) => Legs(run).Sum(leg => (leg.B - leg.A).Length);
}
