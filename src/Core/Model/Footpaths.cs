namespace KTable.Core.Model;

public static class Footpaths
{
    public const string Path = "path";
    const double GroundReach = 0.75, GridLeastM = 30, LinkM = 12, StrandedM = 20, MostM = 280, Bend = 0.12, BarrierLongM = 150, ClearShare = 0.85, EdgeInM = 1;
    const int Candidates = 8, MostPaths = 160, RingPoints = 4, Salt = 101;
    static readonly string[] Ways = ["track", "lane", "street", "road", "chaussee", "causeway", "jetty", "boulevard", Path];
    static readonly string[] Barriers = ["wall", "rampart", "wardwall", "palisade", "moat", "river", "drymoat", "glacis"];
    static readonly string[] Grounds = ["churchyard", "threshing", "arena", "theatre", "parterre", "pitch", "schoolyard"];

    static (Spot At, double Reach) Ground(BuiltPart area)
    {
        var points = BuiltGeometry.Points(area.At).Select(Spot.Of).ToList();
        var middle = new Spot(points.Average(p => p.X), points.Average(p => p.Y));
        return (middle, points.Max(p => (p - middle).Length) * GroundReach);
    }

    static readonly string[] Unvisited = ["tower", "watchtower", "gatehouse", "keep", "bastion", "bunker", "steeple", "privy", "coop", "pigsty", "kennel", "outshut"];

    sealed class Group
    {
        public List<(Spot At, double Reach)> Blocks { get; } = [];
        public Spot Anchor { get; set; }
        public double Radius { get; set; }
        public Spot? Gate { get; set; }
    }

    static IEnumerable<(Spot A, Spot B)> Legs(BuiltPart line)
    {
        var points = BuiltGeometry.Points(line.At).Select(Spot.Of).ToList();
        for (int i = 0; i + 1 < points.Count; i++) yield return (points[i], points[i + 1]);
    }

    static double Long(BuiltPart line) => Legs(line).Sum(leg => (leg.B - leg.A).Length);

    static Spot Closest(Spot p, Spot a, Spot b)
    {
        var ab = b - a;
        double t = ab.Length < 1e-9 ? 0 : Math.Clamp(((p - a).X * ab.X + (p - a).Y * ab.Y) / (ab.Length * ab.Length), 0, 1);
        return a + ab * t;
    }

    static bool Cross(Spot a, Spot b, Spot c, Spot d)
    {
        static double Turn(Spot o, Spot p, Spot q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        return Turn(a, b, c) * Turn(a, b, d) < 0 && Turn(c, d, a) * Turn(c, d, b) < 0;
    }

    static List<Group> Grouped(List<(Spot At, double Reach)> blocks, double link)
    {
        int[] root = Enumerable.Range(0, blocks.Count).ToArray();
        int Find(int i) => root[i] == i ? i : root[i] = Find(root[i]);
        double cell = link + 2 * blocks.Max(b => b.Reach);
        (long, long) Key(Spot p) => ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell));
        var buckets = blocks.Select((b, i) => (Key: Key(b.At), i)).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Select(x => x.i).ToList());
        for (int i = 0; i < blocks.Count; i++)
        {
            var (kx, ky) = Key(blocks[i].At);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (buckets.TryGetValue((kx + dx, ky + dy), out var near))
                        foreach (int j in near)
                            if (j > i && (blocks[i].At - blocks[j].At).Length - blocks[i].Reach - blocks[j].Reach < link) root[Find(i)] = Find(j);
        }
        var groups = blocks.Select((b, i) => (b, i)).GroupBy(x => Find(x.i)).Select(g =>
        {
            var group = new Group();
            group.Blocks.AddRange(g.Select(x => x.b));
            group.Anchor = new Spot(group.Blocks.Average(b => b.At.X), group.Blocks.Average(b => b.At.Y));
            group.Radius = group.Blocks.Max(b => (b.At - group.Anchor).Length + b.Reach);
            return group;
        });
        return groups.ToList();
    }

    internal static void Lay(BuiltSite site, Spot centre, List<BuiltPart> parts)
    {
        Paths(site, centre, parts);
        WayClimbs.Lay(site, parts);
    }

    static void Paths(BuiltSite site, Spot centre, List<BuiltPart> parts)
    {
        var dice = BuiltSite.Roll(centre, Salt);
        var blocks = parts.Where(p => p.Kind == PartKinds.Block && !Unvisited.Contains(p.Class)).Select(BuiltSite.Round)
            .Concat(parts.Where(p => p.Kind == PartKinds.Area && Grounds.Contains(p.Class)).Select(Ground)).ToList();
        if (blocks.Count == 0) return;
        var all = parts.Where(p => p.Kind == PartKinds.Block).Select(BuiltSite.Round).ToList();
        var network = parts.Where(p => p.Kind == PartKinds.Line && Ways.Contains(p.Class)).SelectMany(Legs).Concat(site.RoadSegments()).ToList();
        if (network.Count == 0) return;
        var barriers = parts.Where(p => p.Kind == PartKinds.Line && Barriers.Contains(p.Class) && Long(p) > site.M(BarrierLongM)).SelectMany(Legs).ToList();
        double reachMost = blocks.Max(b => b.Reach) + site.M(StrandedM), cellSize = Math.Max(reachMost, site.M(GridLeastM));
        (long, long) Cell(Spot p) => ((long)Math.Floor(p.X / cellSize), (long)Math.Floor(p.Y / cellSize));
        var legCells = new Dictionary<(long, long), List<(Spot A, Spot B)>>();
        foreach (var leg in network)
        {
            var (ax, ay) = Cell(leg.A);
            var (bx, by) = Cell(leg.B);
            for (long x = Math.Min(ax, bx); x <= Math.Max(ax, bx); x++)
                for (long y = Math.Min(ay, by); y <= Math.Max(ay, by); y++)
                    (legCells.TryGetValue((x, y), out var list) ? list : legCells[(x, y)] = []).Add(leg);
        }
        double Off(Spot p)
        {
            var (cx, cy) = Cell(p);
            double least = double.MaxValue;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (legCells.TryGetValue((cx + dx, cy + dy), out var legs))
                        foreach (var leg in legs) least = Math.Min(least, (p - Closest(p, leg.A, leg.B)).Length);
            return least;
        }
        var stranded = Grouped(blocks, site.M(LinkM)).Select(g => (Group: g, Off: g.Blocks.Min(b => Off(b.At) - b.Reach))).Where(g => g.Off > site.M(StrandedM)).OrderBy(g => g.Off)
            .ThenBy(g => (g.Group.Anchor - centre).Length)
            .Select(g => g.Group).ToList();
        var rings = parts.Where(p => p.Kind == PartKinds.Line && p.Class is "fence" or "palisade" && p.At.Count >= RingPoints * 2).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList())
            .ToList();
        var joined = new List<Group>();
        int laid = 0;
        foreach (var g in stranded)
        {
            if (laid >= MostPaths) break;
            g.Gate = Gate(g, rings);
            var to = Targets(g, network, joined).Where(t => (t.At - g.Anchor).Length - g.Radius < site.M(MostM)).OrderBy(t => (t.At - (g.Gate ?? g.Anchor)).Length).Take(Candidates)
                .Select(t => (t.At, t.Into, Start: g.Gate ?? g.Anchor + (t.At - g.Anchor).Unit * (g.Radius - site.M(EdgeInM)))).Where(t => Clear(site, t.Start, t.At, g, t.Into, all, barriers))
                .ToList();
            if (to.Count == 0) continue;
            var (a, b) = (to[0].Start, to[0].At);
            var middle = (a + b) * 0.5 + (b - a).Left * dice.Between(-Bend, Bend);
            parts.Add(BuiltSite.Line(Path, [a, middle, b]));
            network.Add((a, middle));
            network.Add((middle, b));
            joined.Add(g);
            laid++;
        }
    }

    static Spot? Gate(Group g, List<List<Spot>> rings) =>
        rings.Where(r => (r[0] - g.Anchor).Length < g.Radius * 3 && Enclosures.Inside(r, g.Anchor)).Select(r => (Spot?)((r[0] + r[^1]) * 0.5)).FirstOrDefault();

    static IEnumerable<(Spot At, Group? Into)> Targets(Group g, List<(Spot A, Spot B)> network, List<Group> joined)
    {
        foreach (var leg in network) yield return (Closest(g.Anchor, leg.A, leg.B), null);
        foreach (var other in joined) yield return (other.Anchor + (g.Anchor - other.Anchor).Unit * other.Radius, other);
    }

    static bool Clear(BuiltSite site, Spot a, Spot b, Group from, Group? into, List<(Spot At, double Reach)> blocks, List<(Spot A, Spot B)> barriers)
    {
        if ((b - a).Length < site.M(2) || site.WaterCrossing(a, b) != null || barriers.Any(w => Cross(a, b, w.A, w.B))) return false;
        return blocks.All(k => from.Blocks.Contains(k) || into != null && into.Blocks.Contains(k) || (k.At - Closest(k.At, a, b)).Length > k.Reach * ClearShare);
    }
}
