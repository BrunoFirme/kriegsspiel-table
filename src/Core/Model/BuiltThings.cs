namespace KTable.Core.Model;

public static class BuiltThings
{
    public static readonly IReadOnlyList<string> Blocks = Catalogue.All.Where(e => e.Block).Select(e => e.Id).ToList();
    const double DefaultHouses = 0.5, BlockLongM = 14, BlockDeepM = 9;

    public static bool Remakes(BuiltThing thing) => thing.Kind is BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Details or BuiltKinds.Road or BuiltKinds.Water;

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

    public static BuiltThing WithKept(BuiltThing thing, int part, bool kept)
    {
        var set = thing.Clone();
        if (part >= 0) set.Parts[part].Kept = kept;
        else if (kept) set.Settings[Whole] = 1;
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
        if (Fixed(old)) return old.Clone();
        site = site.In(Manners.Of(old));
        double walls = old.Setting(Settlements.Walls, 0);
        var byHand = walls >= Settlements.WalledFrom && walls < Settlements.BastionedFrom ? Walls.ByHand(old) : null;
        if (byHand != null && seed != old.Seed) byHand = byHand with { Gates = [] };
        var staying = (old.Kind == BuiltKinds.Settlement ? Walls.Without(old) : old).Parts.Where(p => p.Kept).Select(p => p.Clone()).ToList();
        var given = BuiltGeometry.Points(old.Outline).Select(Spot.Of).ToList();
        BuiltThing fresh = old.Kind switch
        {
            BuiltKinds.Fort when given.Count >= 2 && Camps.Is(old) => Camps.Lay(site, old.Sheet, given[0], (given[1] - given[0]).Length, seed,
                Math.Atan2(given[1].Y - given[0].Y, given[1].X - given[0].X)),
            BuiltKinds.Settlement when given.Count >= 2 => Settlements.Lay(site, old.Sheet, given[0], (given[1] - given[0]).Length, seed,
                old.Setting(Settlements.Houses, DefaultHouses), walls, old.Setting(Settlements.Plan, 0), old.Setting(Settlements.Old, 0) >= 1, byHand),
            BuiltKinds.Fort when given.Count >= 2 => Forts.Lay(site, old.Sheet, given[0], (given[1] - given[0]).Length, seed,
                (int)old.Setting(Forts.Bastions, Forts.FewestBastions + 1), old.Setting(Forts.Outworks, 0), Math.Atan2(given[1].Y - given[0].Y, given[1].X - given[0].X)),
            BuiltKinds.Details => BuiltDetails.Lay(site, old.Sheet, given, old.Setting(BuiltDetails.Radius, 1), seed, old.Setting(BuiltDetails.Plots, 0), old.Setting(BuiltDetails.Buildings, 0)),
            BuiltKinds.Road or BuiltKinds.Water when old.Parts.FirstOrDefault(p => p.Kind == PartKinds.Line) is { } line =>
                BuiltRoads.Lay(site, old.Sheet, given, line.Class, old.Setting(BuiltRoads.FindsWay, 0) > 0),
            _ => old.Clone(),
        };
        fresh.Id = old.Id;
        fresh.Name = old.Name;
        foreach (var (name, value) in old.Settings) fresh.Settings.TryAdd(name, value);
        if (old.Kind is not (BuiltKinds.Road or BuiltKinds.Water)) fresh.Settings[Manners.Setting] = site.Manner;
        if (site.UnitsPerMetre > 0) fresh.Settings[Scale] = site.UnitsPerMetre;
        Cleared(fresh, staying);
        fresh.Parts.AddRange(staying);
        return fresh;
    }

    public static BuiltThing Building(BuiltSite site, string sheet, string cls, Spot at, Spot along, double length = 0, double depth = 0)
    {
        var thing = new BuiltThing { Sheet = sheet, Kind = BuiltKinds.Building, Outline = BuiltGeometry.Flat([at.Stored, (at + along.Unit).Stored]), Settings = { [Manners.Setting] = site.Manner } };
        var entry = Catalogue.Find(cls);
        double usual = site.M(entry?.LongM ?? BlockLongM), deep = depth > 0 ? depth : site.M(entry?.DeepM ?? BlockDeepM) * (length > 0 ? Math.Clamp(length / usual, 0.7, 1.6) : 1);
        thing.Parts.Add(BuiltSite.Block(entry?.Laid ?? cls, at, along, length > 0 ? length : usual, deep));
        if (cls == "church") thing.Parts.Add(BuiltSite.Sign("cross", at - new Spot(0, deep)));
        if (entry?.With is { } with) thing.Parts.Add(BuiltSite.Sign(with, with == "wheel" ? at + along.Unit.Left * (deep / 2 + site.M(1.5)) : at));
        return thing;
    }

    public static readonly IReadOnlyList<string> Addable = Catalogue.All.Select(e => e.Id).ToList();

    const int TurnsAround = 3600;

    public static List<BuiltPart> Made(BuiltSite site, string id, Spot at)
    {
        if (Catalogue.Find(id) is not { } entry) return [BuiltSite.Sign(id, at)];
        Spot along = new Spot(site.M(entry.LongM) / 2, 0), deep = new Spot(0, site.M(entry.DeepM) / 2);
        if (entry.Shape == Catalogue.Run) return [BuiltSite.Line(entry.Laid, [at - along, at + along])];
        if (entry.Shape == Catalogue.Ground) return [BuiltSite.Area(entry.Laid, [at - along - deep, at + along - deep, at + along + deep, at - along + deep])];
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

    static void Turn(BuiltPart part, double x, double y, double radians)
    {
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        About(part.At, x, y, (dx, dy) => (dx * cos - dy * sin, dx * sin + dy * cos));
        if (part.Kind == PartKinds.Sign) part.Turn = (((part.Turn + (int)Math.Round(radians * 1800 / Math.PI)) % TurnsAround) + TurnsAround) % TurnsAround;
    }

    public static BuiltThing WithPartTurned(BuiltThing thing, int part, double radians)
    {
        var turned = thing.Clone();
        var (x, y) = Middle(turned.Parts[part].At);
        Turn(turned.Parts[part], x, y, radians);
        turned.Parts[part].Kept = true;
        return turned;
    }

    public static BuiltThing WithPartSized(BuiltThing thing, int part, double by)
    {
        var sized = thing.Clone();
        var (x, y) = Middle(sized.Parts[part].At);
        About(sized.Parts[part].At, x, y, (dx, dy) => (dx * by, dy * by));
        sized.Parts[part].Kept = true;
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

    public static BuiltThing WithoutPart(BuiltThing thing, int part)
    {
        var less = thing.Clone();
        less.Parts.RemoveAt(part);
        return less;
    }

    public static BuiltThing WithPartMoved(BuiltThing thing, int part, int dx, int dy)
    {
        var moved = thing.Clone();
        var at = moved.Parts[part].At;
        for (int i = 0; i + 1 < at.Count; i += 2)
        {
            at[i] += dx;
            at[i + 1] += dy;
        }
        moved.Parts[part].Kept = true;
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
    const double BehindEveryPart = 1e6;

    public static bool StandsOver(BuiltThing thing, Spot p) => thing.Parts.Any(part => part.Class == Quarter && Reach(part, p) <= 0);

    public static (int Part, double Reach) Nearest(BuiltThing thing, Spot p)
    {
        int best = -1;
        double least = double.MaxValue;
        for (int i = 0; i < thing.Parts.Count; i++)
        {
            double reach = Reach(thing.Parts[i], p) + (thing.Parts[i].Class == Quarter ? BehindEveryPart : 0);
            if (reach >= least) continue;
            least = reach;
            best = i;
        }
        return (best, least);
    }
}
