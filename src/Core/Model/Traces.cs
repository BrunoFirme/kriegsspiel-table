namespace KTable.Core.Model;

public static class Traces
{
    public const string Block = "cityblock", Arena = "arena", Circus = "circus";
    const int OvalSteps = 36, BendSteps = 12;
    const double LeastEdgeM = 6, InwardM = 1, RingM = 24, RingsFrom = 0.5, ParallelBelow = 1e-6;

    public static List<List<Spot>> Blocks(BuiltThing place) =>
        place.Parts.Where(IsBlock).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();

    public static bool IsBlock(BuiltPart part) => part.Kind == PartKinds.Area && part.Class == Block;

    public static BuiltThing? Holder(IEnumerable<BuiltThing> built, string sheet, Spot at) =>
        built.Where(b => b.Sheet == sheet && b.Kind == BuiltKinds.Settlement && !BuiltThings.Fixed(b))
            .Select(b => (Place: b, Round: BuiltThings.Round(b))).Where(b => (b.Round.At - at).Length <= b.Round.Reach)
            .OrderBy(b => (b.Round.At - at).Length).Select(b => b.Place).FirstOrDefault();

    public static BuiltThing WithBlock(BuiltSite site, BuiltThing place, IReadOnlyList<Spot> ring)
    {
        var traced = BuiltThings.WithPart(place, BuiltSite.Area(Block, ring));
        return BuiltThings.Remade(site, traced, traced.Seed);
    }

    public static Spot Middle(IReadOnlyList<Spot> ring) => new(ring.Average(p => p.X), ring.Average(p => p.Y));

    internal static List<(Spot At, double Reach)> Claimed(IReadOnlyList<List<Spot>>? blocks, List<(Spot At, double Reach)> taken)
    {
        var discs = (blocks ?? []).Select(block => (Middle(block), Enumerable.Range(0, block.Count).Min(i => Middle(block).FromSegment(block[i], block[(i + 1) % block.Count])))).ToList();
        taken.AddRange(discs);
        return discs;
    }

    internal static IEnumerable<BuiltPart> Yards(IReadOnlyList<List<Spot>>? blocks) => (blocks ?? []).Select(block => BuiltSite.Area(Block, block));

    static bool Inside(IReadOnlyList<List<Spot>> blocks, BuiltPart part) => blocks.Any(block => Enclosures.Inside(block, BuiltSite.Round(part).At));

    internal static void Fill(BuiltSite site, Dice dice, Spot centre, double within, IReadOnlyList<List<Spot>> blocks, List<Settlements.Street> streets, double houses,
        List<BuiltPart> parts, List<(Spot At, double Reach)> taken, int homesFrom)
    {
        for (int i = parts.Count - 1; i >= homesFrom; i--)
            if (!parts[i].Kept && Inside(blocks, parts[i])) parts.RemoveAt(i);
        int edgesFrom = parts.Count;
        foreach (var block in blocks)
            for (var ring = block; ring != null; ring = houses >= RingsFrom ? Inset(site, ring, site.M(RingM)) : null)
                for (int i = 0; i < ring.Count; i++)
                {
                    Spot a = ring[i], b = ring[(i + 1) % ring.Count];
                    if ((b - a).Length < site.M(LeastEdgeM)) continue;
                    Homes.Front(site, dice, centre, within, new Settlements.Street([a, b], "lane", false, Inward(ring, a, b, site)), streets, houses, parts, taken);
                }
        for (int i = parts.Count - 1; i >= edgesFrom; i--)
            if (!Inside(blocks, parts[i])) parts.RemoveAt(i);
    }

    static int Inward(List<Spot> ring, Spot a, Spot b, BuiltSite site) => Enclosures.Inside(ring, (a + b) * 0.5 + (b - a).Unit.Left * site.M(InwardM)) ? 1 : -1;

    static List<Spot>? Inset(BuiltSite site, List<Spot> ring, double by)
    {
        int n = ring.Count;
        var lines = Enumerable.Range(0, n).Select(i =>
        {
            Spot a = ring[i], b = ring[(i + 1) % n], along = (b - a).Unit;
            return (At: a + along.Left * (by * Inward(ring, a, b, site)), Along: along);
        }).ToList();
        var inset = new List<Spot>();
        for (int i = 0; i < n; i++)
        {
            var (p, u) = lines[(i + n - 1) % n];
            var (q, v) = lines[i];
            double cross = u.X * v.Y - u.Y * v.X;
            if (Math.Abs(cross) < ParallelBelow) return null;
            double t = ((q.X - p.X) * v.Y - (q.Y - p.Y) * v.X) / cross;
            inset.Add(p + u * t);
        }
        bool fits = inset.All(p => Enclosures.Inside(ring, p)) && Enumerable.Range(0, n).All(i => (inset[(i + 1) % n] - inset[i]).Length >= site.M(LeastEdgeM));
        return fits ? inset : null;
    }

    public static List<BuiltPart> Thing(BuiltSite site, string id, Spot centre, Spot along, double length)
    {
        if (Catalogue.Find(id) is not { } entry) return [BuiltSite.Sign(id, centre)];
        if (entry.Block) return BuiltThings.Building(site, "", id, centre, along, length).Parts;
        if (entry.Shape is not (Catalogue.Run or Catalogue.Ground)) return [BuiltSite.Sign(entry.Laid, centre, length > 0 ? Math.Atan2(along.Y, along.X) : 0)];
        double usual = site.M(entry.LongM), wanted = length > 0 ? length : usual;
        return [Footprint(entry, centre, along.Unit, wanted, site.M(entry.DeepM) * wanted / Math.Max(usual, 1e-9))];
    }

    public static BuiltPart Footprint(Catalogue.Entry entry, Spot centre, Spot along, double length, double depth)
    {
        if (entry.Shape == Catalogue.Run) return BuiltSite.Line(entry.Laid, [centre - along * (length / 2), centre + along * (length / 2)]);
        if (entry.Shape != Catalogue.Ground) return BuiltSite.Block(entry.Laid, centre, along, length, depth);
        var left = along.Left;
        if (entry.Laid == Arena)
            return BuiltSite.Area(Arena, Enumerable.Range(0, OvalSteps).Select(i => i * Math.Tau / OvalSteps)
                .Select(turn => centre + along * (Math.Cos(turn) * length / 2) + left * (Math.Sin(turn) * depth / 2)));
        if (entry.Laid == Circus)
        {
            double r = Math.Min(depth, length) / 2;
            var bend = centre + along * (length / 2 - r);
            var ring = new List<Spot> { centre - along * (length / 2) + left * r };
            ring.AddRange(Enumerable.Range(0, BendSteps + 1).Select(i => bend + left.Turned(i * Math.PI / BendSteps * Turn(along, left)) * r));
            ring.Add(centre - along * (length / 2) - left * r);
            return BuiltSite.Area(Circus, ring);
        }
        Spot l = along * (length / 2), d = left * (-depth / 2);
        return BuiltSite.Area(entry.Laid, [centre - l - d, centre + l - d, centre + l + d, centre - l + d]);
    }

    static double Turn(Spot along, Spot left) => left.Turned(Math.PI / 2).Dot(along) > 0 ? 1 : -1;

    public static BuiltThing Stood(BuiltThing place, IReadOnlyList<BuiltPart> parts)
    {
        var rings = parts.Where(p => p.Kind is PartKinds.Block or PartKinds.Area).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();
        var stood = place.Clone();
        stood.Parts.RemoveAll(p => !p.Kept && p.Kind is PartKinds.Block or PartKinds.Sign && rings.Any(ring => Enclosures.Inside(ring, BuiltSite.Round(p).At)));
        return BuiltThings.WithParts(stood, parts);
    }

    public static BuiltThing Alone(BuiltSite site, string sheet, IReadOnlyList<BuiltPart> parts)
    {
        var at = BuiltSite.Round(parts[0]).At;
        var points = BuiltGeometry.Points(parts[0].At).Select(Spot.Of).ToList();
        var along = points.Count >= 2 ? (points[1] - points[0]).Unit : new Spot(1, 0);
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Building, Outline = BuiltGeometry.Flat([at.Stored, (at + along).Stored]), Settings = { [Manners.Setting] = site.Manner },
        };
        return BuiltThings.WithParts(thing, parts);
    }

    public static BuiltThing Without(BuiltSite site, BuiltThing thing, int part)
    {
        var less = BuiltThings.WithoutPart(thing, part);
        return IsBlock(thing.Parts[part]) && thing.Kind == BuiltKinds.Settlement ? BuiltThings.Remade(site, less, less.Seed) : less;
    }
}
