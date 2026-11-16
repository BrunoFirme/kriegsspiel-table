namespace KTable.Core.Model;

static class PlotRoom
{
    const double InsetM = 0.6;
    static readonly string[] Grown = ["garden", "orchard", "vineyard", "meadow", "plantation", "allotments"];
    static readonly string[] Held = ["pen", "churchyard", "square", "green", "pond", "threshing", "yard", "forum", "parade", "works", "arena", "theatre", Traces.Circus, "parterre", "pitch", "schoolyard"];
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

    internal static void Tidy(BuiltSite site, List<BuiltPart> parts)
    {
        var others = site.Others.SelectMany(o => o.Parts).ToList();
        var fixedAreas = parts.Concat(others).Where(p => p.Kind == PartKinds.Area && Held.Contains(p.Class) || p.Kind == PartKinds.Block && !Sheds.Contains(p.Class)).Select(Points)
            .Where(r => r.Count >= 3).Select(r => (Ring: r, Box: Box.Of(r))).ToList();
        var ways = parts.Concat(others).Where(p => p.Kind == PartKinds.Line && Ways.Contains(p.Class)).Select(Points).Where(l => l.Count >= 2).Select(l => (Ring: l, Box: Box.Of(l))).ToList();
        var kept = others.Where(p => p.Kind == PartKinds.Area && Grown.Contains(p.Class)).Select(Points).Where(r => r.Count >= 3).Select(r => (Ring: r, Box: Box.Of(r))).ToList();
        var dropped = new List<BuiltPart>();
        foreach (var plot in parts.Where(p => p.Kind == PartKinds.Area && Grown.Contains(p.Class)).ToList())
        {
            var ring = Points(plot);
            if (ring.Count < 3) continue;
            var inner = Shrunk(site, ring);
            var box = Box.Of(inner);
            if (kept.Any(k => k.Box.Meets(box) && Overlap(inner, k.Ring)) || fixedAreas.Any(f => f.Box.Meets(box) && Overlap(inner, f.Ring))
                || ways.Any(w => w.Box.Meets(box) && Crossed(inner, w.Ring)))
            {
                dropped.Add(plot);
                dropped.AddRange(parts.Where(p => (p.Kind == PartKinds.Line && Bounds.Contains(p.Class) || Terraces.Shapes(p))
                    && Points(p).All(q => Enclosures.Inside(ring, q) || Edges(ring).Any(e => q.FromSegment(e.A, e.B) < site.M(InsetM))))
                    .Concat(parts.Where(p => p.Kind == PartKinds.Sign && Inside.Contains(p.Class) && Enclosures.Inside(ring, Points(p)[0]))));
                continue;
            }
            kept.Add((ring, Box.Of(ring)));
        }
        parts.RemoveAll(dropped.Contains);
    }
}
