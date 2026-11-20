namespace KTable.Core.Model;

static class PlotRoom
{
    const double InsetM = 0.6;
    internal static readonly string[] Grown = ["garden", "orchard", "vineyard", "meadow", "plantation", "allotments"];
    internal static readonly string[] Held = ["pen", "churchyard", "square", "green", "pond", "threshing", "yard", "forum", "parade", "works", "arena", "theatre", Traces.Circus, "parterre", "pitch", "schoolyard"];
    static readonly string[] Ways = ["track", "lane", "street", "road", "chaussee", "causeway", "highway", "railway", "boulevard"];
    static readonly string[] Bounds = ["fence", "hedge", Settlements.GardenWall, Terraces.Wall];
    static readonly string[] Inside = ["tree", "hive", "stooks", "rick"];
    static readonly string[] Sheds = ["shed", "privy", "coop", "pigsty", "kennel", "outshut"];

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public static Box Of(IReadOnlyList<Spot> points) => new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));

        public bool Meets(Box o) => X0 <= o.X1 && o.X0 <= X1 && Y0 <= o.Y1 && o.Y0 <= Y1;
    }

    static bool Cross(Spot a, Spot b, Spot c, Spot d)
    {
        static double Turn(Spot o, Spot p, Spot q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        return Turn(a, b, c) * Turn(a, b, d) < 0 && Turn(c, d, a) * Turn(c, d, b) < 0;
    }

    static IEnumerable<(Spot A, Spot B)> Edges(IReadOnlyList<Spot> ring, bool closed = true)
    {
        for (int i = 0; i + 1 < ring.Count; i++) yield return (ring[i], ring[i + 1]);
        if (closed && ring.Count > 2) yield return (ring[^1], ring[0]);
    }

    static bool Overlap(IReadOnlyList<Spot> a, IReadOnlyList<Spot> b) =>
        a.Any(p => Enclosures.Inside(b, p)) || b.Any(p => Enclosures.Inside(a, p)) || Edges(a).Any(e => Edges(b).Any(f => Cross(e.A, e.B, f.A, f.B)));

    static bool Crossed(IReadOnlyList<Spot> ring, IReadOnlyList<Spot> line) =>
        line.Any(p => Enclosures.Inside(ring, p)) || Edges(line, false).Any(e => Edges(ring).Any(f => Cross(e.A, e.B, f.A, f.B)));

    static List<Spot> Shrunk(BuiltSite site, List<Spot> ring)
    {
        var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
        return ring.Select(p => (p - middle).Length <= site.M(InsetM) ? p : p + (middle - p).Unit * site.M(InsetM)).ToList();
    }

    sealed class Index
    {
        readonly Nearby _grid;
        readonly List<int> _found = new();
        public readonly List<(List<Spot> Ring, Box Box, BuiltPart Part)> Items = new();

        public Index(double cell) => _grid = new Nearby(cell);

        public void Add(List<Spot> ring, BuiltPart part)
        {
            var box = Box.Of(ring);
            _grid.Add(Items.Count, box.X0, box.Y0, box.X1, box.Y1);
            Items.Add((ring, box, part));
        }

        public IEnumerable<(List<Spot> Ring, Box Box, BuiltPart Part)> Meeting(Box box)
        {
            _grid.Among(box.X0, box.Y0, box.X1, box.Y1, _found);
            return _found.Select(i => Items[i]).Where(o => o.Box.Meets(box));
        }
    }

    static Index Indexed(BuiltSite site, IEnumerable<BuiltPart> parts, int least)
    {
        var index = new Index(site.M(Homes.NearCellM));
        foreach (var part in parts)
            if (Points(part) is { } ring && ring.Count >= least) index.Add(ring, part);
        return index;
    }

    internal static void Tidy(BuiltSite site, List<BuiltPart> parts)
    {
        var others = site.Others.SelectMany(o => o.Parts).ToList();
        var fixedAreas = Indexed(site, parts.Concat(others).Where(p => p.Kind == PartKinds.Area && Held.Contains(p.Class) || p.Kind == PartKinds.Block && !Sheds.Contains(p.Class)), 3);
        var ways = Indexed(site, parts.Concat(others).Where(p => p.Kind == PartKinds.Line && Ways.Contains(p.Class)), 2);
        var kept = Indexed(site, others.Where(p => p.Kind == PartKinds.Area && Grown.Contains(p.Class)), 3);
        var bounding = parts.Where(p => p.Kind == PartKinds.Line && Bounds.Contains(p.Class) || Terraces.Shapes(p)).ToList();
        var bounds = Indexed(site, bounding, 1);
        var pointless = bounding.Where(p => Points(p).Count == 0).ToList();
        var inside = Indexed(site, parts.Where(p => p.Kind == PartKinds.Sign && Inside.Contains(p.Class)), 1);
        var dropped = new HashSet<BuiltPart>();
        foreach (var plot in parts.Where(p => p.Kind == PartKinds.Area && Grown.Contains(p.Class)).ToList())
        {
            var ring = Points(plot);
            if (ring.Count < 3) continue;
            var inner = Shrunk(site, ring);
            var box = Box.Of(inner);
            if (kept.Meeting(box).Any(k => Overlap(inner, k.Ring)) || fixedAreas.Meeting(box).Any(f => Overlap(inner, f.Ring)) || ways.Meeting(box).Any(w => Crossed(inner, w.Ring)))
            {
                dropped.Add(plot);
                var outer = Box.Of(ring);
                double inset = site.M(InsetM);
                var near = outer with { X0 = outer.X0 - inset, Y0 = outer.Y0 - inset, X1 = outer.X1 + inset, Y1 = outer.Y1 + inset };
                foreach (var b in bounds.Meeting(near).ToList())
                    if (b.Ring.All(q => Enclosures.Inside(ring, q) || Edges(ring).Any(e => q.FromSegment(e.A, e.B) < inset))) dropped.Add(b.Part);
                dropped.UnionWith(pointless);
                foreach (var s in inside.Meeting(near).ToList())
                    if (Enclosures.Inside(ring, s.Ring[0])) dropped.Add(s.Part);
                continue;
            }
            kept.Add(ring, plot);
        }
        parts.RemoveAll(dropped.Contains);
    }
}
