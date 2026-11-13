namespace KTable.Core.Model;

public static class BuiltThings
{
    public static readonly IReadOnlyList<string> Blocks = ["house", "church", "farm", "barn", "mill", "public"];
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

    public static BuiltThing Remade(BuiltSite site, BuiltThing old, int seed)
    {
        var given = BuiltGeometry.Points(old.Outline).Select(Spot.Of).ToList();
        BuiltThing fresh = old.Kind switch
        {
            BuiltKinds.Fort when given.Count >= 2 && Camps.Is(old) => Camps.Lay(site, old.Sheet, given[0], (given[1] - given[0]).Length, seed,
                Math.Atan2(given[1].Y - given[0].Y, given[1].X - given[0].X)),
            BuiltKinds.Settlement when given.Count >= 2 => Settlements.Lay(site, old.Sheet, given[0], (given[1] - given[0]).Length, seed,
                old.Setting(Settlements.Houses, DefaultHouses), old.Setting(Settlements.Walls, 0), old.Setting(Settlements.Plan, 0), old.Setting(Settlements.Old, 0) >= 1),
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
        if (site.UnitsPerMetre > 0) fresh.Settings[Scale] = site.UnitsPerMetre;
        fresh.Parts.AddRange(old.Parts.Where(p => p.Kept).Select(p => p.Clone()));
        return fresh;
    }

    public static BuiltThing Building(BuiltSite site, string sheet, string cls, Spot at, Spot along, double length = 0, double depth = 0)
    {
        var thing = new BuiltThing { Sheet = sheet, Kind = BuiltKinds.Building, Outline = BuiltGeometry.Flat([at.Stored, (at + along.Unit).Stored]) };
        thing.Parts.Add(BuiltSite.Block(cls, at, along, length > 0 ? length : site.M(BlockLongM), depth > 0 ? depth : site.M(BlockDeepM)));
        if (cls == "church") thing.Parts.Add(BuiltSite.Sign("cross", at - new Spot(0, depth > 0 ? depth : site.M(BlockDeepM))));
        if (cls == "mill") thing.Parts.Add(BuiltSite.Sign("mill", at));
        return thing;
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

    public const string Quarter = "quarter", Scale = "scale";
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
