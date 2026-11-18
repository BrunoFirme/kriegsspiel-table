namespace KTable.Core.Model;

public static class BuiltThings
{
    public static readonly IReadOnlyList<string> Blocks = Catalogue.All.Where(e => e.Block).Select(e => e.Id).ToList();
    const double DefaultHouses = 0.5, BlockLongM = 14, BlockDeepM = 9;

    public static bool Remakes(BuiltThing thing) => thing.Kind is BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Details or BuiltKinds.Road or BuiltKinds.Water or BuiltKinds.Works;

    public static bool Named(BuiltThing thing) => thing.Kind is BuiltKinds.Settlement or BuiltKinds.Fort;

    public static (Spot At, double Reach) Round(BuiltThing thing)
    {
        var given = BuiltGeometry.Points(thing.Outline).Select(Spot.Of).ToList();
        return given.Count < 2 ? (default, 0) : (given[0], (given[1] - given[0]).Length * (thing.Kind == BuiltKinds.Fort ? Forts.Reach : 1));
    }

    public static void Stamp(BuiltThing thing, double? scale)
    {
        thing.Name = PlaceNames.Tidy(thing.Name);
        if (scale is > 0 and var laidAt) thing.Settings.TryAdd(Scale, laidAt);
    }

    public static bool Rerolls(BuiltThing thing) => thing.Kind is BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Details;

    public static bool Fixed(BuiltThing thing) => thing.Setting(Whole, 0) >= 1;

    public static BuiltThing WithKept(BuiltThing thing, int part, bool kept) => WithKept(thing, part >= 0 ? [part] : [], kept);

    public static BuiltThing WithKept(BuiltThing thing, IReadOnlyList<int> parts, bool kept)
    {
        var set = thing.Clone();
        foreach (int part in parts) set.Parts[part].Kept = kept;
        if (parts.Count > 0) return set;
        if (kept) set.Settings[Whole] = 1;
        else set.Settings.Remove(Whole);
        return set;
    }

    static Spot At(BuiltPart sign) => Spot.Of(BuiltGeometry.Points(sign.At)[0]);

    static void Cleared(BuiltThing fresh, List<BuiltPart> kept)
    {
        var stands = kept.Where(k => k.Kind == PartKinds.Block).Select(k => (Part: k, Round: BuiltSite.Round(k))).ToList();
        var beds = kept.Where(k => k.Kind == PartKinds.Area && k.Class is "garden" or "orchard").ToList();
        var marks = kept.Where(k => k.Kind == PartKinds.Sign && k.At.Count >= 2).Select(At).ToList();
        bool Covered(BuiltPart made) => made.Kind switch
        {
            PartKinds.Block when BuiltSite.Round(made) is var (at, _) =>
                stands.Any(s => Reach(s.Part, at) <= 0 || Reach(made, s.Round.At) <= 0) || beds.Any(b => Reach(b, at) <= 0) || marks.Any(m => Reach(made, m) <= 0),
            PartKinds.Sign => made.Class is not ("tower" or "bridge" or "ford") && made.At.Count >= 2 && stands.Any(s => Reach(s.Part, At(made)) <= 0),
            _ => false,
        };
        fresh.Parts.RemoveAll(Covered);
    }

    public static BuiltThing Remade(BuiltSite site, BuiltThing old, int seed)
    {
        if (Fixed(old) || site.UnitsPerMetre <= 0) return old.Clone();
        if (old.Kind == BuiltKinds.Works) return Works.Again(site, old);
        site = site.In(Manners.Of(old));
        double walls = old.Setting(Settlements.Walls, 0);
        var byHand = walls >= Settlements.WalledFrom && walls < Settlements.BastionedFrom ? Walls.ByHand(old) : null;
        if (byHand != null && seed != old.Seed) byHand = byHand with { Gates = [] };
        var staying = (old.Kind == BuiltKinds.Settlement ? Walls.Without(old) : old).Parts.Where(p => p.Kept).Select(p => p.Clone()).ToList();
        var given = BuiltGeometry.Points(old.Outline).Select(Spot.Of).ToList();
        BuiltThing fresh = old.Kind switch
        {
            BuiltKinds.Settlement when given.Count >= 2 => Settlements.Lay(site, old.Sheet, given[0], (given[1] - given[0]).Length, seed,
                old.Setting(Settlements.Houses, DefaultHouses), walls, old.Setting(Settlements.Plan, 0), old.Setting(Settlements.Old, 0) >= 1, byHand, Settlements.Shape.Of(old)),
            BuiltKinds.Fort when given.Count >= 2 => Defences.Again(site, old, given[0], (given[1] - given[0]).Length, seed, Math.Atan2(given[1].Y - given[0].Y, given[1].X - given[0].X)),
            BuiltKinds.Details when given.Count == 0 => old.Clone(),
            BuiltKinds.Details => BuiltDetails.Lay(site, old.Sheet, given, old.Setting(BuiltDetails.Radius, 1), seed, old.Setting(BuiltDetails.Plots, 0), old.Setting(BuiltDetails.Buildings, 0),
                old.Setting(BuiltDetails.Shares, 0) > 0),
            BuiltKinds.Road or BuiltKinds.Water when old.Parts.FirstOrDefault(p => p.Kind == PartKinds.Line) is { } line =>
                BuiltRoads.Lay(site, old.Sheet, given, line.Class, old.Setting(BuiltRoads.FindsWay, 0) > 0, line.Surface),
            _ => old.Clone(),
        };
        fresh.Id = old.Id;
        fresh.Name = old.Name;
        foreach (var (name, value) in old.Settings) fresh.Settings.TryAdd(name, value);
        if (old.Kind is not (BuiltKinds.Road or BuiltKinds.Water)) fresh.Settings[Manners.Setting] = site.Manner;
        else if (old.Settings.TryGetValue(Manners.Setting, out double kept)) fresh.Settings[Manners.Setting] = kept;
        else fresh.Settings.Remove(Manners.Setting);
        if (site.UnitsPerMetre > 0) fresh.Settings[Scale] = site.UnitsPerMetre;
        Cleared(fresh, staying);
        foreach (var works in site.Others.Where(o => o.Kind == BuiltKinds.Works))
            if (Works.Cleared(fresh, works, site.UnitsPerMetre) is { } clear) fresh.Parts = clear.Parts;
        Groups.Apart(fresh.Parts, staying);
        fresh.Parts.AddRange(staying);
        return fresh;
    }

    public static BuiltThing Building(BuiltSite site, string sheet, string cls, Spot at, Spot along, double length = 0, double depth = 0)
    {
        var thing =
            new BuiltThing { Sheet = sheet, Kind = BuiltKinds.Building, Outline = BuiltGeometry.Flat([at.Stored, (at + along.Unit).Stored]),
                Settings = { [Manners.Setting] = site.Manner, [Footings.Mark] = 1 } };
        var entry = Catalogue.Find(cls);
        double usual = site.M(entry?.LongM ?? BlockLongM), deep = depth > 0 ? depth : site.M(entry?.DeepM ?? BlockDeepM) * (length > 0 ? Math.Clamp(length / usual, 0.7, 1.6) : 1);
        thing.Parts.Add(BuiltSite.Block(entry?.Laid ?? cls, at, along, length > 0 ? length : usual, deep));
        if (cls == "church" && site.Manner is not (Manners.Ottoman or Manners.NorthAfrican or Manners.Japanese or Manners.Nippon)) thing.Parts.Add(BuiltSite.Sign("cross", at - new Spot(0, deep)));
        if (entry?.With is { } with) thing.Parts.Add(BuiltSite.Sign(with, with == "wheel" ? at + along.Unit.Left * (deep / 2 + site.M(1.5)) : at));
        return thing;
    }

    public static readonly IReadOnlyList<string> Addable = Catalogue.All.Select(e => e.Id).ToList();

    const int TurnsAround = 3600;

    public static List<BuiltPart> Made(BuiltSite site, string id, Spot at)
    {
        if (Catalogue.Find(id) is not { } entry) return [BuiltSite.Sign(id, at)];
        if (entry.Shape is Catalogue.Run or Catalogue.Ground) return [Traces.Footprint(entry, at, new Spot(1, 0), site.M(entry.LongM), site.M(entry.DeepM))];
        if (!entry.Block) return [BuiltSite.Sign(entry.Laid, at)];
        var made = new List<BuiltPart> { BuiltSite.Block(entry.Laid, at, new Spot(1, 0), site.M(entry.LongM), site.M(entry.DeepM)) };
        if (entry.With is { } with) made.Add(BuiltSite.Sign(with, with == "wheel" ? at + new Spot(0, site.M(entry.DeepM / 2 + 1.5)) : at));
        return made;
    }

    public static BuiltThing WithPart(BuiltThing thing, BuiltPart part) => WithParts(thing, [part]);

    public static BuiltThing WithParts(BuiltThing thing, IEnumerable<BuiltPart> parts)
    {
        var more = thing.Clone();
        foreach (var part in parts)
        {
            part.Kept = true;
            more.Parts.Add(part);
        }
        return more;
    }

    static void About(List<int> at, double x, double y, Func<double, double, (double X, double Y)> to)
    {
        for (int i = 0; i + 1 < at.Count; i += 2)
        {
            var (px, py) = to(at[i] - x, at[i + 1] - y);
            at[i] = (int)Math.Round(x + px);
            at[i + 1] = (int)Math.Round(y + py);
        }
    }

    static (double X, double Y) Middle(List<int> at) =>
        (Enumerable.Range(0, at.Count / 2).Average(i => (double)at[2 * i]), Enumerable.Range(0, at.Count / 2).Average(i => (double)at[2 * i + 1]));

    static (double X, double Y) Middle(BuiltThing thing, IReadOnlyList<int> parts) => Middle(parts.SelectMany(i => thing.Parts[i].At).ToList());

    static void Turn(BuiltPart part, double x, double y, double radians)
    {
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        About(part.At, x, y, (dx, dy) => (dx * cos - dy * sin, dx * sin + dy * cos));
        if (part.LaidAt != null) About(part.LaidAt, x, y, (dx, dy) => (dx * cos - dy * sin, dx * sin + dy * cos));
        if (part.Kind == PartKinds.Sign) part.Turn = (((part.Turn + (int)Math.Round(radians * 1800 / Math.PI)) % TurnsAround) + TurnsAround) % TurnsAround;
    }

    public static BuiltThing WithPartTurned(BuiltThing thing, int part, double radians) => WithPartsTurned(thing, [part], radians);

    public static BuiltThing WithPartsTurned(BuiltThing thing, IReadOnlyList<int> parts, double radians)
    {
        var turned = thing.Clone();
        var (x, y) = Middle(turned, parts);
        foreach (int part in parts)
        {
            Turn(turned.Parts[part], x, y, radians);
            turned.Parts[part].Kept = true;
        }
        return turned;
    }

    public static BuiltThing WithPartSized(BuiltThing thing, int part, double by) => WithPartsSized(thing, [part], by);

    public static BuiltThing WithPartsSized(BuiltThing thing, IReadOnlyList<int> parts, double by)
    {
        var sized = thing.Clone();
        var (x, y) = Middle(sized, parts);
        foreach (int part in parts)
        {
            var one = sized.Parts[part];
            one.Kept = true;
            if (one.Kind == PartKinds.Sign)
            {
                one.SizePercent = Math.Clamp((int)Math.Round(one.Size * by * BuiltPart.WholeSize), BuiltPart.LeastSizePercent, BuiltPart.MostSizePercent);
                if (one.SizePercent == BuiltPart.WholeSize) one.SizePercent = 0;
            }
            if (one.Kind != PartKinds.Sign || parts.Count > 1) About(one.At, x, y, (dx, dy) => (dx * by, dy * by));
        }
        return sized;
    }

    public static BuiltThing Turned(BuiltThing thing, double radians)
    {
        var turned = thing.Clone();
        if (turned.Outline.Count < 2) return turned;
        double x = turned.Outline[0], y = turned.Outline[1], cos = Math.Cos(radians), sin = Math.Sin(radians);
        About(turned.Outline, x, y, (dx, dy) => (dx * cos - dy * sin, dx * sin + dy * cos));
        foreach (var part in turned.Parts) Turn(part, x, y, radians);
        return turned;
    }

    public static bool Stretches(BuiltPart part) => part.Kind == PartKinds.Block && part.At.Count == 8;

    public static (double Long, double Deep) Sides(BuiltPart part) => Sides(part.At);

    public static (double Long, double Deep) Sides(List<int> at)
    {
        var c = BuiltGeometry.Points(at).Select(Spot.Of).ToList();
        return ((c[1] - c[0]).Length, (c[3] - c[0]).Length);
    }

    public static BuiltThing WithPartStretched(BuiltThing thing, int part, double length, double depth)
    {
        var stretched = thing.Clone();
        var c = BuiltGeometry.Points(stretched.Parts[part].At).Select(Spot.Of).ToList();
        Spot middle = (c[0] + c[2]) * 0.5, along = (c[1] - c[0]).Unit * (length / 2), across = (c[3] - c[0]).Unit * (depth / 2);
        stretched.Parts[part].At = BuiltGeometry.Flat([(middle - along - across).Stored, (middle + along - across).Stored, (middle + along + across).Stored, (middle - along + across).Stored]);
        stretched.Parts[part].Kept = true;
        return stretched;
    }

    public static BuiltThing WithoutPart(BuiltThing thing, int part) => WithoutParts(thing, [part]);

    public static BuiltThing WithoutParts(BuiltThing thing, IReadOnlyList<int> parts)
    {
        var less = thing.Clone();
        foreach (int part in parts.OrderByDescending(i => i)) less.Parts.RemoveAt(part);
        return less;
    }

    public static BuiltThing WithPartMoved(BuiltThing thing, int part, int dx, int dy) => WithPartsMoved(thing, [part], dx, dy);

    public static BuiltThing WithPartsMoved(BuiltThing thing, IReadOnlyList<int> parts, int dx, int dy)
    {
        var moved = thing.Clone();
        foreach (int part in parts)
        {
            foreach (var at in new[] { moved.Parts[part].At, moved.Parts[part].LaidAt ?? [] })
                for (int i = 0; i + 1 < at.Count; i += 2)
                {
                    at[i] += dx;
                    at[i + 1] += dy;
                }
            moved.Parts[part].Kept = true;
        }
        return moved;
    }

    public static double Reach(BuiltPart part, Spot p)
    {
        var points = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        if (points.Count == 0) return double.MaxValue;
        if (points.Count == 1) return (p - points[0]).Length;
        bool closed = part.Kind is PartKinds.Block or PartKinds.Area;
        if (closed && Holds(points, p)) return 0;
        double least = double.MaxValue;
        for (int i = 0; i + 1 < points.Count; i++) least = Math.Min(least, p.FromSegment(points[i], points[i + 1]));
        return closed ? Math.Min(least, p.FromSegment(points[^1], points[0])) : least;
    }

    static bool Holds(List<Spot> ring, Spot p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            if (ring[i].Y > p.Y != ring[j].Y > p.Y && p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X) inside = !inside;
        return inside;
    }

    public const string Quarter = "quarter", Scale = "scale", Whole = "kept";
    const double BehindEveryPart = 1e6, BehindWhatStandsOnIt = 1e-6;

    public static bool StandsOver(BuiltThing thing, Spot p) => thing.Parts.Any(part => part.Class == Quarter && Reach(part, p) <= 0);

    public static (int Part, double Reach) Nearest(BuiltThing thing, Spot p)
    {
        int best = -1;
        double least = double.MaxValue;
        for (int i = 0; i < thing.Parts.Count; i++)
        {
            double reach = Reach(thing.Parts[i], p) + (thing.Parts[i].Class == Quarter ? BehindEveryPart : thing.Parts[i].Kind == PartKinds.Area ? BehindWhatStandsOnIt : 0);
            if (reach >= least) continue;
            least = reach;
            best = i;
        }
        return (best, least);
    }
}
