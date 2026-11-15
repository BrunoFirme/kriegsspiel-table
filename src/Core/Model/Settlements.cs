namespace KTable.Core.Model;

public static class Settlements
{
    public const string Houses = "houses", Walls = "walls", Plan = "plan", Old = "old", Spread = "spread", Order = "order", Heading = "heading";
    public const double UsualSpread = 0.5, UsualOrder = 0.5;
    public const double WalledFrom = 1.0 / 3, BastionedFrom = 2.0 / 3;
    internal const int BothSides = 0, NoSide = 2;
    const double InsideWall = 0.88, InsideBastions = 0.55, OffCentre = 0.35, SpineTo = 0.9, StripShare = 0.7, StripMostM = 140, StripsFrom = 0.5;
    const double HeartFromM = 120, TownFromM = 260, TownHouses = 0.6, QuarterAt = 0.92;
    const double ChurchAlongM = 24, ChurchDeepM = 11, ChurchOffM = 14, SteepleM = 8, YardRoomM = 8, TownChurch = 1.45;
    const double HallAlongM = 20, HallDeepM = 13, HallOffM = 4, LindenAt = 0.28, WallRoomM = 16, FenceRoomM = 10, TraceBastionsApartM = 220, SectorReach = 1.3, LeastTip = 0.7;
    const double AshoreStepM = 8, KnollRiseM = 1.5, LinkReach = 2.6, SlopeThatSteers = 0.03, MainWobble = 0.06;
    const double GridLongs = 1.15, MazeOrder = 0.5, SpanMostM = 120, ShutWithinM = 3, ShutShort = 0.9, ShutLeastM = 12, CastleShare = 0.3, CastleLeastM = 70, CastleClear = 1.42, CastleSteps =
        6, MoatReach = 1.6;
    const double TraceGateReachM = 420, GatesApartM = 150, BeyondTraceM = 90;
    const int MostGates = 4;
    const double BlockOpen = 1.2, BlockPacked = 0.9;
    const int QuarterSteps = 20, MainSteps = 4, MazeRanks = 6, GrownRanks = 5;
    const double GridSpread = 0.3, MazeBlock = 0.5, GrownBlock = 0.72, LongLeast = 1.4, LongMost = 2.1, MazeWander = 1.7, MazeTidy = 0.7, StreetStretch = 3.4, ShoreStretch = 2, GridStretch = 0.5;
    const double WanderLeast = 0.08, WanderMost = 1.5, WanderPlay = 0.6, RadialShare = 0.7, RadialLeast = 0.3, RadialMost = 0.9, MazeBlind = 0.8, GrownBlind = 0.4, TownStreetsFrom = 0.3;
    const double PalaceLongM = 52, PalaceDeepM = 22, PorticoM = 5, ParkDeepM = 40, HamletM = 110, VillageM = 220, StretchCompact = 0.3, StretchSprawled = 1.5, StreetLong = 3.2, AlignedFrom =
        0.35, GridCoreLeast = 0.4, GridCoreMost = 1.5, AdditionShare = 0.5;
    const double ShoreSetM = 45, WalledSpread = 0.3, WalledFilled = 0.9, PlannedFrom = 0.3, PlannedMany = 0.65, QuarterOut = 0.7, QuarterLeast = 0.3, QuarterMost = 0.48, AdditionTurnLeast =
        0.35, AdditionTurnMost = 1.1;

    internal sealed record Street(List<Spot> Points, string Class, bool Laid, int Side = BothSides, double Dense = 1, bool Main = true);

    internal sealed record Cell(Spot At, Spot U, Spot V, double HalfU, double HalfV);

    internal static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    static Spot[] Rect(Spot mid, Spot along, Spot aside, double wide, double deep) =>
        [mid - along * (wide / 2) - aside * (deep / 2), mid + along * (wide / 2) - aside * (deep / 2), mid + along * (wide / 2) + aside * (deep / 2), mid - along * (wide / 2) + aside * (deep / 2)];

    public static string Form(BuiltThing place) => Plans.For(Manners.Of(place), Plans.Of(place.Setting(Plan, 0)));

    public sealed record Shape(double Spread = UsualSpread, double Order = UsualOrder, double? Heading = null)
    {
        public static Shape Of(BuiltThing place) =>
            new(place.Setting(Settlements.Spread, UsualSpread), place.Setting(Settlements.Order, UsualOrder), place.Settings.TryGetValue(Settlements.Heading, out double heading) ? heading : null);
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot centre, double radius, int seed, double houses, double walls, double plan, bool old = false,
        Walls.Trace? byHand = null, Shape? shape = null)
    {
        shape = new Shape(Math.Clamp((shape ?? new()).Spread, 0, 1), Math.Clamp((shape ?? new()).Order, 0, 1), shape?.Heading);
        var dice = new Dice(seed);
        site = site.Settled(radius, houses);
        old |= Manners.Old(site.Kin);
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Settlement, Seed = seed,
            Settings = { [Houses] = houses, [Walls] = walls, [Plan] = plan, [Manners.Setting] = site.Manner, [Spread] = shape.Spread, [Order] = shape.Order },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + new Spot(radius, 0)).Stored]),
        };
        if (shape.Heading is { } headed) thing.Settings[Heading] = headed;
        string form = Plans.For(site.Manner, Plans.Of(plan));
        bool streeted = form is not (Plans.Round or Plans.Scatter);
        double within = radius * (Traced(site, walls) ? InsideBastions : walls >= WalledFrom ? InsideWall : 1);
        var taken = new List<(Spot At, double Reach)>();
        var cells = new List<Cell>();
        bool grown = form is Plans.Grown or Plans.Maze or Plans.Grid or Plans.Street or Plans.Shore;
        var net = grown ? Network(site, dice, centre, within, seed, houses, form, shape, walls >= WalledFrom) : null;
        if (grown && net == null) return thing;
        var streets = net?.Streets ?? Streets(site, dice, centre, within, houses, form, thing.Parts, taken, cells);
        int mains = streets.Count;
        if (streeted && form != Plans.Green && radius >= site.M(HeartFromM) && streets.Count > 0) Heart(site, dice, net?.Centre ?? centre, within, streets, thing.Parts, taken);
        if (form == Plans.Wards) Villages.Wards(site, cells, streets, thing.Parts, taken);
        if (net == null) streets.RemoveAll(s => s.Class == "lane" && s.Laid && form != Plans.Wards && Crosses(s, taken));
        else streets = net.Grown(streets.Skip(mains), taken, thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is "square" or "churchyard" or "green" or "forum" or "pond"));
        if (streeted) Outskirts.Watermill(site, dice, centre, within, thing.Parts, taken);
        double strips = form == Plans.Street && site.Ways.Crofts >= StripsFrom ? Math.Min(within * StripShare, site.M(StripMostM * site.Ways.Crofts)) : 0;
        if (streeted) Callings.Works(site, centre, within, walls, streets, thing.Parts, taken);
        if (streeted) Industry.Lay(site, dice, centre, within, streets, thing.Parts, taken);
        if (streeted)
            foreach (var street in streets.Where(s => s.Side != NoSide).ToList())
                Homes.Front(site, dice, centre, within, street, streets, Math.Pow(Math.Clamp(houses, 0, 1), street.Dense), thing.Parts, taken, street.Class == "lane" ? 0 : strips);
        Callings.Trades(site, centre, thing.Parts);
        Outskirts.Lay(site, dice, centre, within, streets, houses, walls < WalledFrom, !old, thing.Parts, taken);
        Callings.Country(site, centre, within, streets, thing.Parts, taken);
        Livings.Lay(site, centre, within, streets, thing.Parts, taken);
        Environs.Lay(site, centre, within, streets, thing.Parts, taken);
        foreach (var street in streets.Where(s => s.Laid))
        {
            var runs = Ashore(site, street.Points, net != null && street.Main);
            foreach (var ashore in runs.Where(run => run.Count >= 2))
            {
                thing.Parts.Add(BuiltSite.Line(street.Class, ashore));
                for (int i = 0; i + 1 < ashore.Count; i++)
                    if (site.WaterCrossing(ashore[i], ashore[i + 1]) is { } over)
                        thing.Parts.Add(BuiltSite.Sign("bridge", over, Math.Atan2(ashore[i + 1].Y - ashore[i].Y, ashore[i + 1].X - ashore[i].X)));
            }
            for (int k = 0; k + 1 < runs.Count; k++)
                if (runs[k].Count >= 2 && runs[k + 1].Count >= 2) thing.Parts.Add(BuiltSite.Line("causeway", [runs[k][^1], runs[k + 1][0]]));
        }
        if (thing.Parts.All(p => p.Kind != PartKinds.Block))
        {
            thing.Parts.Clear();
            return thing;
        }
        thing.Parts.Add(BuiltSite.Area(BuiltThings.Quarter, Ring(centre, within * QuarterAt, QuarterSteps)));
        if (walls >= WalledFrom) Fortify(site, dice, thing.Parts, streets.Where(s => s.Main).ToList(), within, houses, walls, byHand);
        if (!old) return thing;
        thing.Settings[Old] = 1;
        thing.Parts.RemoveAll(p => p.Kind == PartKinds.Sign && p.Class is "cross" or "shrine");
        return thing;
    }

    static bool Traced(BuiltSite site, double walls) => walls >= BastionedFrom && Defences.Bastioned(site.Epoch) && site.Manner != Manners.Japanese;

    static bool Crosses(Street lane, List<(Spot At, double Reach)> taken)
    {
        for (int i = 0; i + 1 < lane.Points.Count; i++)
            if (taken.Any(o => o.At.FromSegment(lane.Points[i], lane.Points[i + 1]) < o.Reach)) return true;
        return false;
    }

    static List<Spot> Ring(Spot centre, double radius, int steps) => Enumerable.Range(0, steps).Select(i => centre + Spot.Toward(i * 2 * Math.PI / steps) * radius).ToList();

    static IEnumerable<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of);

    static void Fortify(BuiltSite site, Dice dice, List<BuiltPart> parts, List<Street> streets, double within, double houses, double walls, Walls.Trace? byHand)
    {
        bool trace = Traced(site, walls), town = trace || within >= site.M(TownFromM) || houses >= TownHouses;
        bool Held(BuiltPart p) => p.Kind == PartKinds.Block && !Industry.Outlying(p.Class)
            || p.Kind == PartKinds.Area && p.Class != BuiltThings.Quarter && (!town || p.Class is "square" or "churchyard" or "green" or "forum");
        var held = parts.Where(Held).SelectMany(Points).ToList();
        if (held.Count < 3) return;
        if (site.Modern)
        {
            Defences.Perimeter(site, dice, Enclosures.Round(site, held, site.M(WallRoomM * 2)), streets, parts);
            return;
        }
        if (site.Manner == Manners.Japanese)
        {
            Castled(site, dice, Enclosures.Middle(Enclosures.Hull(held)), within, parts);
            return;
        }
        if (!trace && byHand != null)
        {
            parts.RemoveAll(p => p.Kind != PartKinds.Line && p.Class is not (BuiltThings.Quarter or "tree" or "shrine" or "rock" or "gallows") && Points(p).Any(q => !Enclosures.Inside(byHand.Corners, q)));
            int from = parts.Count;
            Enclosures.Wall(site, byHand.Corners, streets, byHand.Masonry, parts, byHand.Gates);
            foreach (var work in parts.Skip(from).Where(p => !BuiltSite.IsRoad(p))) work.Kept = true;
            Shut(site, parts, byHand.Corners);
            return;
        }
        if (!trace)
        {
            var ring = Enclosures.Round(site, held, site.M(town ? WallRoomM : FenceRoomM));
            if (town) parts.RemoveAll(p => (p.Kind == PartKinds.Area && p.Class is "garden" or "orchard" || p.Kind == PartKinds.Line && p.Class == "fence") && Points(p).Any(q => !Enclosures.Inside(ring, q)));
            Enclosures.Wall(site, ring, streets, town, parts);
            if (town) Props.Keep(site, ring, held, parts);
            Shut(site, parts, ring);
            return;
        }
        var hull = Enclosures.Hull(held);
        var middle = Enclosures.Middle(hull);
        double around = Enumerable.Range(0, hull.Count).Sum(i => (hull[(i + 1) % hull.Count] - hull[i]).Length), turn = dice.Between(0, Math.PI);
        int bastions = Math.Clamp((int)Math.Round(around / site.M(TraceBastionsApartM)), Forts.FewestBastions + 1, Forts.MostBastions + 4);
        bool Faces(Spot p, double bearing) => Math.Abs(Math.IEEERemainder(Math.Atan2(p.Y - middle.Y, p.X - middle.X) - bearing, 2 * Math.PI)) < Math.PI / bastions * SectorReach;
        double Farthest(double bearing) => held.Where(p => Faces(p, bearing)).Select(p => (p - middle).Length).DefaultIfEmpty(0).Max();
        var reaches = Enumerable.Range(0, bastions).Select(i => (Bearing: turn + i * 2 * Math.PI / bastions, Reach: Farthest(turn + i * 2 * Math.PI / bastions))).ToList();
        double least = reaches.Max(r => r.Reach) * LeastTip;
        var tips = reaches.Select(r => (r.Bearing, (Math.Max(r.Reach, least) + site.M(WallRoomM)) / Forts.CurtainClear)).ToList();
        double outworks = (walls - BastionedFrom) / (1 - BastionedFrom);
        var star = Forts.Trace(middle, tips);
        var gates = new List<(Spot At, double Bearing, Spot From, string Class)>();
        foreach (var street in streets.Where(s => s.Laid && s.Class != "lane"))
            foreach (var (end, before) in new[] { (street.Points[0], street.Points[1]), (street.Points[^1], street.Points[^2]) })
            {
                var way = (end - before).Unit;
                if (gates.Count >= MostGates || !Enclosures.Inside(star, end) || !Enclosures.Hits(star, end, end + way * site.M(TraceGateReachM)).Any()) continue;
                var hit = Enclosures.Hits(star, end, end + way * site.M(TraceGateReachM)).MinBy(p => (p - end).Length);
                double bearing = Math.Atan2(hit.Y - middle.Y, hit.X - middle.X);
                var gate = Forts.Gate(site, star, middle, bearing, outworks >= Forts.GlacisFrom);
                if (!gates.Any(g => (g.At - gate).Length < site.M(GatesApartM))) gates.Add((gate, bearing, end, street.Class));
            }
        parts.AddRange(Forts.Around(site, middle, tips, outworks, gates.Select(g => g.At).ToList()));
        foreach (var gate in gates)
        {
            parts.Add(BuiltSite.Line(gate.Class, [gate.From, gate.At, gate.At + Spot.Toward(gate.Bearing) * site.M(BeyondTraceM)]));
            parts.Add(BuiltSite.Sign("bridge", gate.At, gate.Bearing));
        }
    }

    static void Shut(BuiltSite site, List<BuiltPart> parts, IReadOnlyList<Spot> ring)
    {
        var walls = parts.Where(p => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade").Select(p => Points(p).ToList()).ToList();
        bool Walled(Spot at) => walls.Any(run => Enumerable.Range(0, run.Count - 1).Any(i => at.FromSegment(run[i], run[i + 1]) < site.M(ShutWithinM)));
        foreach (var line in parts.Where(BuiltSite.IsRoad).ToList())
        {
            var given = Points(line).ToList();
            var kept = new List<Spot> { given[0] };
            bool cut = false;
            for (int i = 0; i + 1 < given.Count && !cut; i++)
            {
                var met = Enclosures.Hits(ring, given[i], given[i + 1]).Where(Walled).OrderBy(hit => (hit - given[i]).Length).Cast<Spot?>().FirstOrDefault();
                cut = met != null;
                kept.Add(met is { } at ? given[i] + (at - given[i]) * ShutShort : given[i + 1]);
            }
            if (!cut) continue;
            parts.Remove(line);
            bool inside = Enclosures.Inside(ring, given[0]);
            var rest = inside ? kept : given.Skip(kept.Count - 1).Prepend(given[kept.Count - 2] + (kept[^1] - given[kept.Count - 2]) * (2 - ShutShort)).ToList();
            if (inside && TownStreets.Long(rest) > site.M(ShutLeastM)) parts.Add(BuiltSite.Line(line.Class, rest));
        }
    }

    static IEnumerable<Spot> Stepped(List<Spot> line, double step)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((line[i + 1] - line[i]).Length / step));
            for (int k = i == 0 ? 0 : 1; k <= steps; k++) yield return line[i] + (line[i + 1] - line[i]) * (k / (double)steps);
        }
    }

    static void Castled(BuiltSite site, Dice dice, Spot middle, double within, List<BuiltPart> parts)
    {
        double radius = Math.Max(site.M(CastleLeastM), within * CastleShare), reach = radius * CastleClear;
        var castle = Defences.LayCastle(site, "", middle, radius, dice.Below(int.MaxValue - 1) + 1, dice.Between(0, 2 * Math.PI));
        if (castle.Parts.Count == 0) return;
        var at = Spot.Of(BuiltGeometry.Points(castle.Outline)[0]);
        parts.RemoveAll(p => p.Kind != PartKinds.Line && p.Class != BuiltThings.Quarter && (BuiltSite.Round(p).At - at).Length < reach + BuiltSite.Round(p).Reach * 0.5);
        var moat = castle.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == Forts.Ditch).Select(p => Points(p).ToList()).FirstOrDefault() ?? [];
        bool Kept(Spot p) => (p - at).Length >= reach && (moat.Count < 3 || !Enclosures.Inside(moat, p));
        var lines = parts.Where(p => p.Kind == PartKinds.Line).ToList();
        parts.RemoveAll(p => p.Kind == PartKinds.Line);
        foreach (var line in lines)
        {
            var given = Points(line).ToList();
            if (!Enumerable.Range(0, given.Count - 1).Any(i => at.FromSegment(given[i], given[i + 1]) < reach * MoatReach))
            {
                parts.Add(line);
                continue;
            }
            var run = new List<Spot>();
            foreach (var p in Stepped(given, reach / CastleSteps))
            {
                if (Kept(p)) run.Add(p);
                else if (run.Count > 1) parts.Add(BuiltSite.Line(line.Class, run));
                if (!Kept(p)) run = new();
            }
            if (run.Count > 1) parts.Add(BuiltSite.Line(line.Class, run));
        }
        parts.AddRange(castle.Parts.Where(p => p.Class != "road"));
    }

    public static BuiltThing? Fronted(BuiltSite site, BuiltThing place, BuiltThing line)
    {
        if (place.Kind != BuiltKinds.Settlement || line.Kind != BuiltKinds.Road || Form(place) is Plans.Round or Plans.Scatter || place.Setting(Walls, 0) >= WalledFrom) return null;
        if (line.Parts.FirstOrDefault(p => p.Kind == PartKinds.Line) is not { } laid || laid.Class is "track" or "railway" or "highway") return null;
        var (centre, radius) = BuiltThings.Round(place);
        site = site.In(Manners.Of(place)).Settled(radius, place.Setting(Houses, 0.5));
        var streets = place.Parts.Where(BuiltSite.IsRoad).Select(p => new Street(Points(p).ToList(), p.Class, false)).ToList();
        var taken = place.Parts.Where(p => p.Kind == PartKinds.Block || p.Kind == PartKinds.Area && p.Class != BuiltThings.Quarter).Select(BuiltSite.Round).ToList();
        var fronted = place.Clone();
        var dice = new Dice(place.Seed, laid.At.Count + laid.At[0]);
        var path = Points(laid).ToList();
        for (int i = 0; i + 1 < path.Count; i++)
        {
            if (Clipped(path[i], path[i + 1], centre, radius) is not var (a, b)) continue;
            var street = new Street([a, b], laid.Class, false);
            streets.Add(street);
            Homes.Front(site, dice, centre, radius, street, streets, place.Setting(Houses, 0.5), fronted.Parts, taken);
        }
        return fronted.Parts.Count == place.Parts.Count ? null : fronted;
    }

    static (Spot A, Spot B)? Clipped(Spot a, Spot b, Spot centre, double radius)
    {
        var d = b - a;
        var f = a - centre;
        double qa = d.Dot(d), qb = 2 * f.Dot(d), qc = f.Dot(f) - radius * radius;
        double disc = qb * qb - 4 * qa * qc;
        if (qa < 1e-18 || disc <= 0) return null;
        double root = Math.Sqrt(disc), t0 = Math.Max(0, (-qb - root) / (2 * qa)), t1 = Math.Min(1, (-qb + root) / (2 * qa));
        return t1 - t0 < 1e-6 ? null : (a + d * t0, a + d * t1);
    }

    static List<Spot> Spine(Dice dice, Spot centre, double within, double bearing)
    {
        Spot along = Spot.Toward(bearing), aside = along.Left;
        var main = new List<Spot>();
        for (int i = 0; i <= MainSteps; i++)
            main.Add(centre + along * (-within + 2 * within * i / MainSteps) + aside * (i is 0 or MainSteps ? 0 : dice.Between(-MainWobble, MainWobble) * within));
        return main;
    }

    static List<Street> Streets(BuiltSite site, Dice dice, Spot centre, double within, double houses, string form, List<BuiltPart> parts, List<(Spot At, double Reach)> taken,
        List<Cell> cells)
    {
        var streets = new List<Street>();
        foreach (var (a, b) in site.RoadSegments())
            if (Clipped(a, b, centre, within) is var (p, q)) streets.Add(new Street([p, q], "road", false));
        if (form == Plans.Round)
        {
            Spot? road = streets.Count > 0 ? Nearest(streets, centre).At : Linked(site, centre, within);
            Villages.Round(site, dice, centre, within, road, streets, parts, taken);
            return streets;
        }
        bool gridded = form is Plans.Wards or Plans.Rows;
        string main = form == Plans.Scatter ? "track" : "street";
        double bearing;
        var link = Linked(site, centre, within);
        if (streets.Count > 0)
        {
            var longest = streets.MaxBy(s => (s.Points[1] - s.Points[0]).Length)!;
            var along = longest.Points[1] - longest.Points[0];
            bearing = Math.Atan2(along.Y, along.X);
            var (near, _, off) = Nearest(streets, centre);
            var far = centre + (centre - near).Unit * (within * SpineTo);
            if (!gridded && off > within * OffCentre && Passable(site, near, centre, far)) streets.Add(new Street([near, centre, far], main, true));
        }
        else
        {
            var fall = site.Downhill(centre);
            bearing = link is { } toward ? Math.Atan2(toward.Y - centre.Y, toward.X - centre.X) : fall.Length > SlopeThatSteers ? Math.Atan2(fall.Left.Y, fall.Left.X) : dice.Between(0, Math.PI);
            if (!gridded)
            {
                var spine = Spine(dice, centre, within, bearing);
                streets.Add(new Street(spine, main, true));
                if (link is { } joined && site.Buildable(spine[^1] + (joined - spine[^1]) * 0.5)) streets.Add(new Street([spine[^1], joined], within >= site.M(TownFromM) ? "road" : "track", true));
            }
        }
        if (gridded && site.Ways.Cardinal) bearing = 0;
        if (gridded) Gridded(site, centre, within, bearing, houses, form, link, streets, cells);
        else if (form == Plans.Scatter) Villages.Scatter(site, dice, centre, within, houses, streets, parts, taken);
        else if (form == Plans.Green && Nearest(streets, centre) is var (at, axis, _)) Villages.Green(site, dice, at, axis, within, streets, parts, taken);
        return streets;
    }

    static TownStreets? Network(BuiltSite site, Dice dice, Spot centre, double within, int seed, double houses, string form, Shape shape, bool walled)
    {
        var ways = site.Ways;
        bool grid = form == Plans.Grid, maze = form == Plans.Maze, street = form == Plans.Street, shore = form == Plans.Shore;
        var roads = site.RoadSegments().Select(leg => Clipped(leg.A, leg.B, centre, within)).Where(leg => leg != null).Select(leg => leg!.Value).ToList();
        var fall = site.Downhill(centre);
        double bearing = shore && Villages.Shoreward(site, centre, within) is { } water ? water + Math.PI / 2
            : roads.Count > 0 && roads.MaxBy(leg => (leg.B - leg.A).Length) is var longest ? Math.Atan2(longest.B.Y - longest.A.Y, longest.B.X - longest.A.X)
            : shape.Heading ?? (Linked(site, centre, within) is { } toward ? Math.Atan2(toward.Y - centre.Y, toward.X - centre.X)
            : fall.Length > SlopeThatSteers ? Math.Atan2(fall.Left.Y, fall.Left.X) : dice.Between(0, Math.PI));
        if (grid && ways.Cardinal) bearing = 0;
        if (shore && Villages.Shoreward(site, centre, within) is { } seaward)
            for (double out_ = 0; out_ < within && site.KindAt(centre + Spot.Toward(seaward) * (out_ + site.M(ShoreSetM))) != GroundKinds.Water; out_ += site.M(AshoreStepM))
                centre = centre + Spot.Toward(seaward) * site.M(AshoreStepM);
        double stretch = street ? StreetStretch : shore ? ShoreStretch : grid ? 1 + dice.Between(0, GridStretch) : 1 + dice.Next() * Lerp(StretchCompact, StretchSprawled, shape.Spread);
        double spread = shape.Spread * (walled ? WalledSpread : 1);
        var ground = new TownGround(site, centre, within, seed, spread * (grid ? GridSpread : 1), walled && !street ? Math.Sqrt(stretch) : stretch,
            Spot.Toward(bearing), shore ? 1 : 0, walled && !street ? WalledFilled : street || shore ? TownGround.Filled : Lerp(TownGround.FilledHuddled, TownGround.FilledStraggling, shape.Spread));
        if (!ground.Any) return null;
        double metres = within / Math.Max(1e-9, site.UnitsPerMetre), order = shape.Order;
        double block = site.M(ways.BlockM * (grid ? 1 : maze ? MazeBlock : GrownBlock)) * Lerp(BlockOpen, BlockPacked, houses) / Math.Sqrt(ways.Lanes);
        double wander = (maze ? Lerp(MazeWander, MazeTidy, order) : Lerp(WanderMost, WanderLeast, order)) * dice.Between(WanderPlay, 1);
        double radial = grid || street || shore || !dice.Chance(Lerp(RadialShare, 0, order)) ? 0 : dice.Between(RadialLeast, RadialMost);
        var recipe = new StreetRecipe(block, block * (grid ? GridLongs : street ? StreetLong : dice.Between(LongLeast, LongMost)), wander, radial, 0, bearing,
            maze ? MazeBlind : Lerp(GrownBlind, 0, order), grid && order >= AlignedFrom, street ? 0 : shore ? 2 : maze ? MazeRanks : GrownRanks, !street
                && (site.Town > TownStreetsFrom || grid) ? "street" : "lane", spread, grid ? "street" : "lane", maze ? order * MazeOrder : order, site.Manner is Manners.Japanese or Manners.Nippon);
        var net = new TownStreets(site, ground, dice, recipe, seed);
        net.Given(roads);
        int wanted = street || shore || metres < HamletM ? 2 : metres < VillageM ? 2 + dice.Below(2) : metres < TownFromM ? 3 + dice.Below(2) : 4 + dice.Below(2);
        if (grid) recipe.Quarters.Add((ground.Centre, within * Lerp(GridCoreLeast, GridCoreMost, order), bearing));
        int planned = street || shore || maze || metres < VillageM ? 0 : grid ? (dice.Chance(AdditionShare) ? 1 : 0) : order < PlannedFrom ? 0 : dice.Below(order > PlannedMany ? 3 : 2);
        for (int i = 0; i < planned; i++)
        {
            double outward = dice.Between(0, 2 * Math.PI);
            if (net.Edge(outward, within) is not { } tip) continue;
            recipe.Quarters.Add((ground.Centre + (tip - ground.Centre) * QuarterOut, within * dice.Between(QuarterLeast, QuarterMost), grid ? bearing
                + dice.Between(AdditionTurnLeast, AdditionTurnMost) : outward));
        }
        if (grid && roads.Count == 0) net.Axes(Spot.Toward(bearing), "road");
        net.Mains(wanted, Spot.Toward(bearing), within, shore ? "track" : grid ? "road" : roads.Count > 0 && metres < VillageM ? "lane" : "street");
        if (roads.Count == 0 && Linked(site, centre, within) is { } joined) net.Linked(joined, within >= site.M(TownFromM) ? "road" : "track");
        return net;
    }

    static Spot? Linked(BuiltSite site, Spot centre, double within) => site.NearestRoad(centre) is var (at, reach) && reach > within && reach < within * LinkReach ? at : null;

    static bool Passable(BuiltSite site, params Spot[] path)
    {
        const int Samples = 4;
        for (int i = 0; i + 1 < path.Length; i++)
        {
            if (site.WaterCrossing(path[i], path[i + 1]) != null) return false;
            for (int s = 0; s <= Samples; s++)
                if (!site.Buildable(path[i] + (path[i + 1] - path[i]) * (s / (double)Samples))) return false;
        }
        return true;
    }

    internal static double FromStreets(IEnumerable<Street> streets, Spot p, Street? but = null)
    {
        double least = double.MaxValue;
        foreach (var street in streets)
        {
            if (street == but) continue;
            for (int i = 0; i + 1 < street.Points.Count; i++) least = Math.Min(least, p.FromSegment(street.Points[i], street.Points[i + 1]));
        }
        return least;
    }

    static void Gridded(BuiltSite site, Spot centre, double within, double bearing, double houses, string form, Spot? link, List<Street> streets, List<Cell> cells)
    {
        var ways = site.Ways;
        double block = site.M(ways.BlockM * Lerp(BlockOpen, BlockPacked, houses));
        Spot u = Spot.Toward(bearing), v = u.Left;
        int nu = Math.Max(1, (int)Math.Round(within * ways.GridLong / block)), nv = Math.Max(1, (int)Math.Round(within * ways.GridWide / block));
        bool wards = form == Plans.Wards, rows = form == Plans.Rows;
        string axis = wards ? "chaussee" : "road", other = rows ? "lane" : "street";
        void Run(Spot from, Spot to, string cls, int side)
        {
            if (Passable(site, from, to)) streets.Add(new Street([from, to], cls, true, wards ? NoSide : side));
        }
        for (int j = -nv; j <= nv; j++)
            Run(centre + v * (j * block) - u * (nu * block), centre + v * (j * block) + u * (nu * block), j == 0 ? axis : other, j == nv ? -1 : j == -nv ? 1 : BothSides);
        for (int i = -nu; i <= nu; i++)
            if (!rows || i == 0 || Math.Abs(i) == nu)
                Run(centre + u * (i * block) - v * (nv * block), centre + u * (i * block) + v * (nv * block), i == 0 ? axis : rows ? "street" : other, i == nu ? 1 : i == -nu ? -1 : rows ? NoSide : BothSides);
        for (int i = -nu; i < nu; i++)
            for (int j = -nv; j < nv; j++) cells.Add(new Cell(centre + u * ((i + 0.5) * block) + v * ((j + 0.5) * block), u, v, block / 2, block / 2));
        if (link is not { } joined) return;
        var gate = new[] { centre + u * (nu * block), centre - u * (nu * block), centre + v * (nv * block), centre - v * (nv * block) }.MinBy(end => (end - joined).Length);
        if (site.Buildable(gate + (joined - gate) * 0.5)) streets.Add(new Street([gate, joined], "road", true, NoSide));
    }

    internal static (Spot At, Spot Along, double Reach) Nearest(IEnumerable<Street> streets, Spot p)
    {
        Spot at = p, along = new(1, 0);
        double least = double.MaxValue;
        foreach (var street in streets)
            for (int i = 0; i + 1 < street.Points.Count; i++)
            {
                Spot a = street.Points[i], b = street.Points[i + 1];
                var ab = b - a;
                var near = a + ab * Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
                if ((near - p).Length >= least) continue;
                least = (near - p).Length;
                at = near;
                along = ab.Unit;
            }
        return (at, along, least);
    }

    static (double WideM, double DeepM, string Class) Place(Ways ways, bool town) => ways.Open switch
    {
        Ways.Forum => (town ? 62 : 38, town ? 36 : 30, "forum"),
        Ways.Maidan => (48, 36, "square"),
        Ways.Court => (56, 56, ways.Cardinal ? "square" : "green"),
        Ways.Clearing => (30, 26, "green"),
        Ways.Green => (46, 24, "green"),
        Ways.Plaza => (town ? 84 : 44, town ? 56 : 32, "square"),
        _ => (town ? 56 : 34, town ? 38 : 26, "square"),
    };

    static void Heart(BuiltSite site, Dice dice, Spot centre, double within, List<Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var ways = site.Ways;
        bool town = within >= site.M(TownFromM), open = ways.Open != Ways.NoPlace, court = ways.Open == Ways.Court;
        var (at, along, _) = Nearest(streets, centre);
        var aside = along.Left * (dice.Chance(0.5) ? 1 : -1);
        var (wideM, deepM, cls) = Place(ways, town);
        double wide = site.M(wideM), deep = site.M(deepM), rise = site.HeightAt(at + aside * (deep * 1.5)) - site.HeightAt(at - aside * (deep * 1.5));
        if (rise < -site.M(KnollRiseM)) aside = aside * -1;
        var square = at + aside * (Homes.HalfWidth(site, "street") + deep / 2);
        if (!site.Free(square, deep / 2) || (square - centre).Length > within) return;
        var edges = Rect(square, along, aside, wide, deep);
        int manner = site.Kin;
        double grand = town ? TownChurch : 1;
        if (!open)
        {
            Church(site, square, along, aside, centre, within, parts, taken, grand);
            if (town) Seat(site, square + along * (site.M(ChurchAlongM) * grand / 2 + site.M(YardRoomM + HallOffM + HallAlongM / 2)), along, aside, centre, within, parts, taken);
            return;
        }
        parts.Add(BuiltSite.Area(cls, edges));
        streets.AddRange(new[] { (edges[1], edges[2]), (edges[3], edges[0]) }.Select(side => new Street([side.Item1, side.Item2], "lane", false)));
        taken.Add((square, Math.Max(wide, deep) / 2));
        if (court)
        {
            parts.Add(BuiltSite.Block(ways.Seat, square, along, site.M(HallAlongM * 1.1), site.M(HallDeepM * 1.2)));
            if (ways.Cardinal) parts.Add(BuiltSite.Line("wardwall", edges.Append(edges[0])));
            else parts.AddRange(edges.Select(corner => BuiltSite.Sign("tree", square + (corner - square) * 0.78)));
            parts.Add(BuiltSite.Sign(ways.Cardinal ? "well" : "flag", square - aside * (deep * 0.3)));
            Church(site, square + along * (wide / 2 + site.M(ChurchAlongM / 2 + YardRoomM + 8)) * (dice.Chance(0.5) ? 1 : -1), along, aside, centre, within, parts, taken, grand);
            return;
        }
        parts.Add(BuiltSite.Sign("well", square));
        if (!town) parts.Add(BuiltSite.Sign("tree", square + along * (wide * LindenAt)));
        else Seat(site, square + along * (wide / 2 + site.M(HallOffM + HallAlongM / 2)), along, aside, centre, within, parts, taken);
        if (site.Manner == Manners.Soviet && town) Palace(site, square + aside * (deep / 2 + site.M(PalaceDeepM / 2 + 6)), along, aside, parts, taken);
        else Church(site, square + aside * (deep / 2 + site.M(ChurchOffM) * grand), along, aside, centre, within, parts, taken, grand);
        if (ways.Open != Ways.Clearing) Props.Market(site, dice, square, along, aside, wide, deep, town, parts);
        if (manner != Manners.Tribal) Props.Trades(site, at, square, along, aside, wide, centre, within, parts, taken);
        var mark = square - along * (wide * 0.36) - aside * (deep * 0.3);
        if (manner == Manners.Roman || site.Manner is Manners.Soviet or Manners.Persian) parts.Add(BuiltSite.Sign("statue", mark));
        else if (site.Manner == Manners.Continental) parts.Add(BuiltSite.Sign("marketcross", mark));
        else if (site.Manner == Manners.Western) parts.Add(BuiltSite.Sign("column", mark));
        else if (manner == Manners.Tribal) parts.Add(BuiltSite.Sign("menhir", square - along * (wide * 0.36)));
        else if (manner == Manners.Egyptian) parts.Add(BuiltSite.Sign("jars", mark));
    }

    static void Palace(BuiltSite site, Spot at, Spot along, Spot aside, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (!site.Free(at, site.M(PalaceDeepM / 2)) || taken.Any(o => (o.At - at).Length < o.Reach + site.M(PalaceDeepM / 2))) return;
        parts.Add(BuiltSite.Block("public", at, along, site.M(PalaceLongM), site.M(PalaceDeepM)));
        parts.Add(BuiltSite.Block("public", at - aside * site.M(PalaceDeepM / 2 + PorticoM / 2), along, site.M(PalaceLongM * 0.4), site.M(PorticoM)));
        parts.Add(BuiltSite.Area("green", Rect(at + aside * site.M(PalaceDeepM / 2 + ParkDeepM / 2 + 4), along, aside, site.M(PalaceLongM * 1.3), site.M(ParkDeepM))));
        foreach (int side in new[] { -1, 1 }) parts.Add(BuiltSite.Sign("flag", at - aside * site.M(PalaceDeepM / 2 + PorticoM + 3) + along * (side * site.M(PalaceLongM * 0.3))));
        taken.Add((at, site.M(PalaceLongM / 2)));
        taken.Add((at + aside * site.M(PalaceDeepM / 2 + ParkDeepM / 2 + 4), site.M(ParkDeepM / 2)));
    }

    static void Seat(BuiltSite site, Spot hall, Spot along, Spot aside, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (!site.Free(hall, site.M(HallDeepM / 2)) || (hall - centre).Length >= within || taken.Any(o => (o.At - hall).Length < o.Reach + site.M(HallDeepM / 2))) return;
        string seat = site.Ways.Seat;
        bool deep = seat is "greathouse" or "villa" or "cloister";
        parts.Add(BuiltSite.Block(seat, hall, along, site.M(HallAlongM), site.M(deep ? HallAlongM * 0.85 : HallDeepM)));
        if (site.Manner == Manners.Egyptian) parts.AddRange(Callings.Silos(site, hall + aside * site.M(HallAlongM * 0.43 + 3.5), along, 4));
        taken.Add((hall, site.M(HallAlongM) / 2));
    }

    static List<IReadOnlyList<Spot>> Ashore(BuiltSite site, IReadOnlyList<Spot> points, bool bridged)
    {
        double step = site.M(AshoreStepM);
        if (site.Grid == null || step <= 0 || points.Count < 2) return [points];
        var runs = new List<List<Spot>> { new() };
        bool wetAny = false;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((points[i + 1] - points[i]).Length / step));
            for (int k = i == 0 ? 0 : 1; k <= steps; k++)
            {
                var p = points[i] + (points[i + 1] - points[i]) * (k / (double)steps);
                if (site.KindAt(p) != GroundKinds.Water) runs[^1].Add(p);
                else if (runs[^1].Count > 0) runs.Add(new());
                wetAny |= site.KindAt(p) == GroundKinds.Water;
            }
        }
        if (!wetAny) return [points];
        var dry = runs.Where(run => run.Count >= 2).ToList();
        bool spans = bridged && dry.Count > 1 && Enumerable.Range(0, dry.Count - 1).All(k => (dry[k + 1][0] - dry[k][^1]).Length <= site.M(SpanMostM));
        return spans ? dry.Cast<IReadOnlyList<Spot>>().ToList() : [runs.MaxBy(run => run.Count)!];
    }

    internal static bool Steepled(int manner) =>
        !Manners.Old(Manners.Kin(manner)) && manner is not (Manners.Ottoman or Manners.NorthAfrican or Manners.Japanese or Manners.Nippon or Manners.Pacific);

    internal static void Chapel(BuiltSite site, Spot church, Spot along, List<BuiltPart> parts, double grand = 1)
    {
        double length = site.M(ChurchAlongM) * grand;
        parts.Add(BuiltSite.Block("church", church, along, length, site.M(ChurchDeepM) * grand));
        if (site.Kin == Manners.Roman && site.Manner != Manners.Chinese) parts.Add(BuiltSite.Sign("altar", church + along * (length / 2 + site.M(4))));
        if (site.Manner == Manners.Egyptian)
            foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Sign("obelisk", church + along * (length / 2 + site.M(3)) + along.Left * (side * site.M(ChurchDeepM) * grand * 0.3)));
        if (!Steepled(site.Manner)) return;
        parts.Add(BuiltSite.Block("steeple", church - along * (length / 2), along, site.M(SteepleM) * grand, site.M(SteepleM) * grand));
        parts.Add(BuiltSite.Sign("cross", church - new Spot(0, site.M(ChurchDeepM) * grand)));
    }

    internal static void Church(BuiltSite site, Spot church, Spot along, Spot aside, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken, double grand = 1)
    {
        double length = site.M(ChurchAlongM) * grand, room = site.M(YardRoomM);
        if (!site.Free(church, length / 2) || (church - centre).Length > within) return;
        var yard = Rect(church, along, aside, length + 2 * room, site.M(ChurchDeepM) * grand + 2 * room);
        parts.Add(BuiltSite.Area("churchyard", yard));
        parts.Add(BuiltSite.Line("fence", yard.Append(yard[0])));
        Chapel(site, church, along, parts, grand);
        taken.Add((church, length / 2 + room));
    }
}
