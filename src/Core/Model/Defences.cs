namespace KTable.Core.Model;

public static class Defences
{
    public const string Camp = "camp", Hillfort = "hillfort", Fortress = "fortress", Star = "star", Castle = "castle", Strongpoint = "strongpoint", Redoubt = "redoubt", Battery = "battery";
    public const string None = "none", Wall = "wall", Bastions = "bastions", Palisade = "palisade", Lines = "lines";
    public const int Hill = 2, Keep = 3, Hedgehog = 4, Guns = 5;

    public sealed record Kind(string Id, string Name, string Tip, bool Starred = false, bool Outworks = false);

    static readonly Kind Camped = new(Camp, "Camp", "A playing-card camp: rampart, ditch, four gates, rows of barracks");
    static readonly Kind Hilled = new(Hillfort, "Hillfort", "Banks and ditches ringing a height, a staggered way in, round houses inside");
    static readonly Kind Walled = new(Fortress, "Fortress", "A square of thick walls with close-set towers behind a dry ditch");
    static readonly Kind Starred = new(Star, "Star fort", "A bastioned star with its ditch; ravelins and glacis as outworks", true, true);
    static readonly Kind Kept = new(Castle, "Castle", "A keep on its stone base inside a second enclosure, ringed by a wide moat");
    static readonly Kind Boxed = new(Strongpoint, "Strongpoint", "Trenches all round behind wire, bunkers, gun pits and a minefield");
    static readonly Kind Fielded = new(Redoubt, "Redoubt", "A plain square earthwork with its ditch", false, true);
    static readonly Kind Gunned = new(Battery, "Battery", "A row of gun pits with their shelters and a trench behind");

    public static Kind Fort(int manner) => Manners.Named(manner).Epoch switch
    {
        Manners.Modern => Boxed,
        Manners.Later => manner == Manners.Japanese ? Kept : Starred,
        _ => manner == Manners.Roman ? Camped : manner == Manners.Tribal ? Hilled : Walled,
    };

    public static Kind? Fieldwork(int manner) => Manners.Named(manner).Epoch switch { Manners.Modern => Gunned, Manners.Later => Fielded, _ => null };

    public static bool Bastioned(int epoch) => epoch == Manners.Later;

    public static IReadOnlyList<(string Key, string Text, string Tip)> Rings(int epoch) => epoch switch
    {
        Manners.Modern => [(None, "None", "An open place"), (Lines, "Defended", "Trenches and wire round the place, pillboxes and blocks on the roads in")],
        Manners.Later => [(None, "None", "An open place"), (Wall, "Walled", "A wall round what is built: a palisade for a village, towers, gates and a ditch for a town"),
            (Bastions, "Bastioned", "A bastioned trace fitted round the town, with its ditch, ravelins and glacis")],
        _ => [(None, "None", "An open place"), (Wall, "Walled", "A wall round what is built: a palisade for a village, towers, gates and a ditch for a town")],
    };

    public static IReadOnlyList<string> Routes(int epoch) => epoch switch
    {
        Manners.Modern => ["track", "lane", "street", "road", "highway", "railway", "trench", "wire", "stream", "river"],
        Manners.Later => ["track", "lane", "street", "road", "chaussee", "stream", "river"],
        _ => ["track", "lane", "street", "road", "stream", "river"],
    };

    const double BayM = 7, TraverseM = 3.5, StepM = 2.6, WireOffM = 24, GapM = 9, BunkerM = 6, ArcsApartM = 150, CommsStepM = 14, CommsSwingM = 5, PitsAt = 0.42, WireAt = 1, TrenchAt = 0.7;
    const double CommandLongM = 9, CommandDeepM = 7, MineLongM = 60, MineDeepM = 26, BeltAt = 0.88;
    const int RingSteps = 28, MostPits = 6;

    public static List<Spot> Crenellated(IReadOnlyList<Spot> path, double bay, double traverse, double step)
    {
        var line = new List<Spot>();
        bool forward = false;
        double carried = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Spot a = path[i], b = path[i + 1], along = (b - a).Unit, aside = along.Left;
            double length = (b - a).Length, at = -carried;
            if (line.Count == 0) line.Add(a);
            while (true)
            {
                double next = at + (forward ? traverse : bay);
                if (next >= length) break;
                at = next;
                line.Add(a + along * at + aside * (forward ? step : 0));
                forward = !forward;
                line.Add(a + along * at + aside * (forward ? step : 0));
            }
            carried = length - at;
            line.Add(b + aside * (forward ? step : 0));
        }
        return line;
    }

    static List<Spot> Zigzag(Spot from, Spot to, double step, double swing)
    {
        var line = new List<Spot> { from };
        var along = (to - from).Unit;
        int steps = Math.Max(1, (int)((to - from).Length / step));
        for (int i = 1; i < steps; i++) line.Add(from + (to - from) * (i / (double)steps) + along.Left * (i % 2 == 0 ? swing : -swing));
        line.Add(to);
        return line;
    }

    static List<Spot> Wobbled(Dice dice, Spot centre, double radius, double play, double squash, double turn)
    {
        var u = Spot.Toward(turn);
        return Enumerable.Range(0, RingSteps).Select(i => i * 2 * Math.PI / RingSteps).Select(a =>
            centre + (u * Math.Cos(a) + u.Left * (Math.Sin(a) * squash)) * (radius * (1 + dice.Between(-play, play)))).ToList();
    }

    static IEnumerable<List<Spot>> Arcs(BuiltSite site, List<Spot> ring, IReadOnlyList<Spot> gaps, double gap)
    {
        var run = new List<Spot>();
        int first = Enumerable.Range(0, ring.Count).FirstOrDefault(i => gaps.Any(g => (g - ring[i]).Length < gap), 0);
        for (int k = 1; k <= ring.Count + 1; k++)
        {
            var p = ring[(first + k) % ring.Count];
            bool open = gaps.Any(g => (g - p).Length < gap) || !site.Buildable(p);
            if (!open) run.Add(p);
            if (!open && k <= ring.Count) continue;
            if (run.Count > 1) yield return run;
            run = new();
        }
    }

    static List<Spot> Stepped(IReadOnlyList<Spot> ring, double step)
    {
        var stepped = new List<Spot>();
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
            for (int s = 0; s < steps; s++) stepped.Add(a + (b - a) * (s / (double)steps));
        }
        return stepped;
    }

    internal static void Perimeter(BuiltSite site, Dice dice, IReadOnlyList<Spot> ring, IEnumerable<Settlements.Street> streets, List<BuiltPart> parts)
    {
        if (ring.Count < 3) return;
        var middle = Enclosures.Middle(ring);
        var gaps = new List<Spot>();
        foreach (var street in streets.Where(s => s.Class != "lane"))
            for (int i = 0; i + 1 < street.Points.Count; i++) gaps.AddRange(Enclosures.Hits(ring, street.Points[i], street.Points[i + 1]));
        foreach (var (a, b) in site.RoadSegments()) gaps.AddRange(Enclosures.Hits(ring, a, b));
        var stepped = Stepped(ring, site.M(BayM));
        foreach (var arc in Arcs(site, stepped, gaps, site.M(GapM))) parts.Add(BuiltSite.Line("trench", Crenellated(arc, site.M(BayM), site.M(TraverseM), site.M(StepM))));
        var wire = stepped.Select(p => p + (p - middle).Unit * site.M(WireOffM)).ToList();
        foreach (var arc in Arcs(site, wire, gaps.Select(g => g + (g - middle).Unit * site.M(WireOffM)).ToList(), site.M(GapM))) parts.Add(BuiltSite.Line("wire", arc));
        foreach (var gap in gaps)
        {
            var (_, along, _) = Enclosures.Nearest(ring, gap);
            if (site.Free(gap + along * site.M(GapM + BunkerM), 0)) parts.Add(BuiltSite.Block("bunker", gap + along * site.M(GapM + BunkerM), along, site.M(BunkerM), site.M(BunkerM)));
            parts.Add(BuiltSite.Sign("roadblock", gap + (gap - middle).Unit * site.M(WireOffM), Math.Atan2(along.Y, along.X)));
        }
    }

    static BuiltThing Thing(BuiltSite site, string sheet, Spot centre, double radius, int seed, double turn, int form) => new()
    {
        Sheet = sheet, Kind = BuiltKinds.Fort, Seed = seed, Settings = { [Forts.Bastions] = 0, [Forts.Outworks] = 0, ["turn"] = turn, [Forts.Form] = form, [Manners.Setting] = site.Manner },
        Outline = BuiltGeometry.Flat([centre.Stored, (centre + Spot.Toward(turn) * radius).Stored]),
    };

    public static BuiltThing LayStrongpoint(BuiltSite site, string sheet, Spot asked, double radius, int seed, double turn)
    {
        var centre = site.DryNear(asked, radius * TrenchAt, radius * Forts.ShiftReach) ?? asked;
        var thing = Thing(site, sheet, centre, radius, seed, turn, Hedgehog);
        if (!site.Free(centre, 0)) return thing;
        var dice = new Dice(seed);
        var parts = thing.Parts;
        var road = site.NearestRoad(centre) is var (to, off) && off < radius * Forts.LinkReach ? to : (Spot?)null;
        double entry = road is { } toward ? Math.Atan2(toward.Y - centre.Y, toward.X - centre.X) : turn + Math.PI;
        var rings = site.Manner == Manners.Soviet ? new[] { TrenchAt, TrenchAt * 0.6 } : [TrenchAt];
        foreach (double at in rings)
        {
            var ring = Stepped(Wobbled(dice, centre, radius * at, 0.08, 0.86, turn), site.M(BayM));
            var gaps = Enumerable.Range(0, Math.Max(2, (int)(2 * Math.PI * radius * at / site.M(ArcsApartM)))).Select(i => centre + Spot.Toward(entry + i * 2 * Math.PI
                / Math.Max(2, (int)(2 * Math.PI * radius * at / site.M(ArcsApartM)))) * (radius * at)).ToList();
            foreach (var arc in Arcs(site, ring, gaps, site.M(GapM)))
            {
                parts.Add(BuiltSite.Line("trench", Crenellated(arc, site.M(BayM), site.M(TraverseM), site.M(StepM))));
                var salient = arc[arc.Count / 2];
                if (at == TrenchAt
                    && site.Free(salient, 0)) parts.Add(BuiltSite.Block("bunker", salient + (salient - centre).Unit * site.M(BunkerM * 0.4), (salient - centre).Unit.Left, site.M(BunkerM), site.M(BunkerM)));
                parts.Add(BuiltSite.Line("trench", Zigzag(arc[arc.Count / 3], centre + (arc[arc.Count / 3] - centre) * 0.25, site.M(CommsStepM), site.M(CommsSwingM))));
            }
        }
        var wire = Wobbled(dice, centre, radius * WireAt, 0.05, 0.86, turn);
        foreach (var arc in Arcs(site, Stepped(wire, site.M(BayM * 2)), [centre + Spot.Toward(entry) * (radius * WireAt)], site.M(GapM))) parts.Add(BuiltSite.Line("wire", arc));
        if (site.Manner is Manners.NorthAfrican or Manners.Soviet)
        {
            var belt = Wobbled(dice, centre, radius * BeltAt, 0.05, 0.86, turn);
            foreach (var arc in Arcs(site, Stepped(belt, site.M(BayM * 2)), [centre + Spot.Toward(entry) * (radius * BeltAt)], site.M(GapM * 2))) parts.Add(BuiltSite.Line("wire", arc));
        }
        parts.Add(BuiltSite.Block("bunker", centre, Spot.Toward(turn), site.M(CommandLongM), site.M(CommandDeepM)));
        int pits = Math.Clamp((int)(radius / site.M(40)), 3, MostPits);
        for (int i = 0; i < pits; i++)
        {
            double bearing = entry + Math.PI + (i - (pits - 1) / 2.0) * 0.55;
            parts.Add(BuiltSite.Sign("gunpit", centre + Spot.Toward(bearing) * (radius * PitsAt), bearing));
        }
        var mines = centre + Spot.Toward(entry + Math.PI) * (radius * WireAt + site.M(MineDeepM));
        Spot along = Spot.Toward(entry + Math.PI).Left * (site.M(MineLongM) / 2), deep = Spot.Toward(entry + Math.PI) * (site.M(MineDeepM) / 2);
        if (site.Free(mines, 0)) parts.Add(BuiltSite.Area("minefield", [mines - along - deep, mines + along - deep, mines + along + deep, mines - along + deep]));
        parts.Add(BuiltSite.Sign("roadblock", centre + Spot.Toward(entry) * (radius * WireAt), entry + Math.PI / 2));
        if (road is { } joined) parts.Add(BuiltSite.Line("track", [centre + Spot.Toward(entry) * (radius * 0.2), joined]));
        return thing;
    }

    const double PitsApartM = 22, ShelterBackM = 16, ShelterM = 5;

    public static BuiltThing LayBattery(BuiltSite site, string sheet, Spot asked, double radius, int seed, double turn)
    {
        var thing = Thing(site, sheet, asked, radius, seed, turn, Guns);
        if (!site.Free(asked, 0)) return thing;
        Spot facing = Spot.Toward(turn), along = facing.Left;
        int guns = Math.Clamp((int)(2 * radius / site.M(PitsApartM)), 2, 8);
        for (int i = 0; i < guns; i++)
        {
            var at = asked + along * ((i - (guns - 1) / 2.0) * site.M(PitsApartM));
            thing.Parts.Add(BuiltSite.Sign("gunpit", at, turn));
            if (i % 2 == 0 && site.Free(at - facing * site.M(ShelterBackM), 0)) thing.Parts.Add(BuiltSite.Block("bunker", at - facing * site.M(ShelterBackM), along, site.M(ShelterM), site.M(ShelterM)));
        }
        var behind = asked - facing * site.M(ShelterBackM * 0.5);
        thing.Parts.Add(BuiltSite.Line("trench", Crenellated([behind - along * radius, behind + along * radius], site.M(BayM), site.M(TraverseM), site.M(StepM))));
        thing.Parts.Add(BuiltSite.Line("wire", [asked + facing * site.M(WireOffM * 1.4) - along * (radius * 1.1), asked + facing * site.M(WireOffM * 1.4) + along * (radius * 1.1)]));
        return thing;
    }

    const double RingsFrom = 0.62, RingsApart = 0.19, HillSquash = 0.74, StaggerM = 16, HousesApartM = 34, FosseOffM = 9;
    const int HillTries = 4;

    public static BuiltThing LayHillfort(BuiltSite site, string sheet, Spot asked, double radius, int seed, double turn)
    {
        var centre = site.DryNear(asked, radius * RingsFrom, radius * Forts.ShiftReach) ?? asked;
        var thing = Thing(site, sheet, centre, radius, seed, turn, Hill);
        if (!site.Free(centre, 0)) return thing;
        var dice = new Dice(seed);
        var fall = site.Downhill(centre);
        double lie = fall.Length > 0.02 ? Math.Atan2(fall.Left.Y, fall.Left.X) : turn;
        int rings = radius >= site.M(110) ? 3 : radius >= site.M(60) ? 2 : 1;
        for (int r = 0; r < rings; r++)
        {
            double at = radius * (RingsFrom + r * RingsApart);
            var ring = Wobbled(new Dice(seed, r), centre, at, 0.05, HillSquash, lie);
            var gate = centre + Spot.Toward(lie + r * site.M(StaggerM) / at) * at;
            foreach (var arc in Arcs(site, Stepped(ring, site.M(6)), [gate], site.M(7))) thing.Parts.Add(BuiltSite.Line("rampart", arc));
            var fosse = ring.Select(p => p + (p - centre).Unit * site.M(FosseOffM)).ToList();
            foreach (var arc in Arcs(site, Stepped(fosse, site.M(6)), [gate + (gate - centre).Unit * site.M(FosseOffM)], site.M(8))) thing.Parts.Add(BuiltSite.Line("fosse", arc));
        }
        double inside = radius * RingsFrom * 0.82, apart = site.M(HousesApartM);
        var taken = new List<(Spot At, double Reach)>();
        int wanted = (int)(Math.PI * inside * inside * HillSquash / (apart * apart));
        for (int tries = 0, laid = 0; laid < wanted && tries < wanted * HillTries; tries++)
        {
            double a = dice.Between(0, 2 * Math.PI), far = inside * Math.Sqrt(dice.Next()), size = site.M(dice.Between(6, 10));
            var at = centre + (Spot.Toward(lie) * Math.Cos(a) + Spot.Toward(lie).Left * (Math.Sin(a) * HillSquash)) * far;
            if (!site.Buildable(at) || taken.Any(o => (o.At - at).Length < o.Reach + size)) continue;
            thing.Parts.Add(BuiltSite.Block("roundhouse", at, Spot.Toward(a), size, size));
            if (dice.Chance(0.5)) thing.Parts.Add(BuiltSite.Block("granary", at + Spot.Toward(a + 1.3) * (size * 0.5 + site.M(4)), Spot.Toward(a), site.M(3), site.M(3)));
            taken.Add((at, size * 0.6 + site.M(5)));
            laid++;
        }
        thing.Parts.Add(BuiltSite.Line("track", [centre, centre + Spot.Toward(lie) * (radius * (RingsFrom + (rings - 1) * RingsApart) + site.M(20))]));
        return thing;
    }

    const double OuterLong = 0.95, OuterWide = 0.72, InnerLong = 0.4, InnerWide = 0.34, InnerAt = 0.3, KeepM = 22, TurretM = 8, HallLongM = 30, HallDeepM = 16;

    static List<Spot> Bent(Spot centre, Spot u, double halfLong, double halfWide, double notch)
    {
        Spot v = u.Left;
        return
        [
            centre - u * halfLong - v * halfWide, centre + u * (halfLong - notch) - v * halfWide, centre + u * (halfLong - notch) - v * (halfWide - notch), centre
                + u * halfLong - v * (halfWide - notch),
            centre + u * halfLong + v * halfWide, centre - u * (halfLong - notch) + v * halfWide, centre - u * (halfLong - notch) + v * (halfWide - notch), centre - u * halfLong
                + v * (halfWide - notch),
        ];
    }

    public static BuiltThing LayCastle(BuiltSite site, string sheet, Spot asked, double radius, int seed, double turn)
    {
        double perMetre = site.UnitsPerMetre > 0 ? site.UnitsPerMetre : 1;
        var shape = Forts.Raised(radius / perMetre, false).In(perMetre);
        var centre = site.DryNear(asked, radius + shape.Brink, radius * Forts.ShiftReach) ?? asked;
        var thing = Thing(site, sheet, centre, radius, seed, turn, Keep);
        if (!site.Dry(centre, radius + shape.Brink)) return thing;
        Spot u = Spot.Toward(turn), v = u.Left;
        var outer = Bent(centre, u, radius * OuterLong, radius * OuterWide, radius * 0.16);
        var core = centre + u * (radius * InnerAt) - v * (radius * 0.12);
        var inner = Bent(core, u, radius * InnerLong, radius * InnerWide, radius * 0.1);
        var body = new Forts.Body { Trace = outer, Ravelins = [], Gates = [], P = shape, Level = true, Blend = 0, GateHalf = 0, Whole = true };
        var parts = thing.Parts;
        parts.Add(BuiltSite.Area(Forts.Ditch, body.Ring(shape.Wide + shape.Scarp / 2, site.M(3))));
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        parts.Add(BuiltSite.Area(Forts.Platform, inner));
        parts.Add(BuiltSite.Line("wall", outer.Append(outer[0])));
        parts.Add(BuiltSite.Line("wall", inner.Append(inner[0])));
        foreach (var corner in outer.Where((_, i) => i is 0 or 3 or 4 or 7)) parts.Add(BuiltSite.Block("watchtower", corner + (centre - corner).Unit * site.M(TurretM * 0.6), u, site.M(TurretM), site.M(TurretM)));
        parts.Add(BuiltSite.Block("keep", core + u * (radius * InnerLong * 0.45) - v * (radius * InnerWide * 0.3), u, site.M(KeepM), site.M(KeepM * 0.85)));
        parts.Add(BuiltSite.Block("public", core - u * (radius * InnerLong * 0.35) + v * (radius * InnerWide * 0.2), u, Math.Min(site.M(HallLongM), radius * 0.3), site.M(HallDeepM)));
        var court = centre - u * (radius * 0.45);
        parts.Add(BuiltSite.Block("public", court + v * (radius * 0.2), u, Math.Min(site.M(HallLongM * 1.2), radius * 0.36), site.M(HallDeepM)));
        parts.Add(BuiltSite.Block("barracks", court - v * (radius * 0.32), u, Math.Min(site.M(44), radius * 0.4), site.M(8)));
        parts.Add(BuiltSite.Area("garden", [court + v * (radius * 0.38) - u * (radius * 0.2), court + v * (radius * 0.38) + u * (radius * 0.2), court + v * (radius * 0.58) + u * (radius * 0.2), court
            + v * (radius * 0.58) - u * (radius * 0.2)]));
        foreach (double t in new[] { -0.12, 0.06, 0.16 }) parts.Add(BuiltSite.Sign("tree", court + v * (radius * 0.48) + u * (radius * t)));
        var gate = Forts.Gate(site, outer, centre, turn + Math.PI, false, true);
        parts.Add(BuiltSite.Sign("bridge", gate, turn + Math.PI));
        parts.Add(BuiltSite.Block("gatehouse", gate + u * (shape.Wide / 2 + shape.Foot + site.M(4)), v, site.M(11), site.M(7)));
        var road = site.NearestRoad(centre) is var (to, off) && off > radius && off < radius * Forts.LinkReach ? to : (Spot?)null;
        var beyond = gate - u * (shape.Wide / 2 + shape.Scarp + site.M(14));
        parts.Add(BuiltSite.Line("road", road is { } joined ? [gate + u * (shape.Wide / 2 + shape.Foot + site.M(12)), beyond, joined] : [gate + u * (shape.Wide / 2 + shape.Foot + site.M(12)), beyond]));
        return thing;
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, string kind, Spot asked, double radius, int seed, int bastions, double outworks, double turn) => kind switch
    {
        Camp or Fortress => Camps.Lay(site, sheet, asked, radius, seed, turn),
        Hillfort => LayHillfort(site, sheet, asked, radius, seed, turn),
        Castle => LayCastle(site, sheet, asked, radius, seed, turn),
        Strongpoint => LayStrongpoint(site, sheet, asked, radius, seed, turn),
        Battery => LayBattery(site, sheet, asked, radius, seed, turn),
        Redoubt => Forts.Lay(site, sheet, asked, radius, seed, 0, outworks, turn),
        _ => Forts.Lay(site, sheet, asked, radius, seed, Math.Max(bastions, Forts.FewestBastions), outworks, turn),
    };

    public static Kind Named(BuiltThing fort) => new[] { Camped, Hilled, Walled, Starred, Kept, Boxed, Fielded, Gunned }.First(kind => kind.Id == KindOf(fort));

    public static string KindOf(BuiltThing fort) => (int)fort.Setting(Forts.Form, 0) switch
    {
        Hill => Hillfort, Keep => Castle, Hedgehog => Strongpoint, Guns => Battery,
        1 => Manners.Of(fort) == Manners.Roman ? Camp : Fortress,
        _ => fort.Setting(Forts.Bastions, 0) < Forts.FewestBastions ? Redoubt : Star,
    };

    public static BuiltThing Again(BuiltSite site, BuiltThing old, Spot centre, double radius, int seed, double turn) =>
        Lay(site, old.Sheet, KindOf(old), centre, radius, seed, (int)old.Setting(Forts.Bastions, Forts.FewestBastions + 1), old.Setting(Forts.Outworks, 0), turn);
}
