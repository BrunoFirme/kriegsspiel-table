namespace KTable.Core.Model;

public static class Settlements
{
    public const string Houses = "houses", Walls = "walls", Plan = "plan", Old = "old";
    public const double WalledFrom = 1.0 / 3, BastionedFrom = 2.0 / 3;
    const double LanesOpen = 2, LanesPacked = 9, LanesPerM = 150, LaneLeastM = 40, LaneSpacingM = 28, LaneTries = 8, MostLanes = 80, LaneSteepest = 0.2;
    const double BlockOpenM = 95, BlockPackedM = 60, InsideWall = 0.88, InsideBastions = 0.55, OffCentre = 0.35, SpineTo = 0.9, StripShare = 0.7, StripMostM = 140;
    const double HeartFromM = 120, TownFromM = 260, TownHouses = 0.6, SquareAlongM = 34, SquareDeepM = 26, QuarterAt = 0.92;
    const double ChurchAlongM = 24, ChurchDeepM = 11, ChurchOffM = 14, SteepleM = 8, YardRoomM = 8;
    const double HallAlongM = 20, HallDeepM = 13, HallOffM = 4, LindenAt = 0.28, WallRoomM = 16, FenceRoomM = 10, TraceBastionsApartM = 220, SectorReach = 1.3, LeastTip = 0.7;
    const double LinkReach = 2.6, SlopeThatSteers = 0.03, MainWobble = 0.06, BranchPlay = 0.4, BendPlay = 0.12;
    const int QuarterSteps = 20, MainSteps = 4;

    internal sealed record Street(List<Spot> Points, string Class, bool Laid);

    internal static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    static Spot[] Rect(Spot mid, Spot along, Spot aside, double wide, double deep) =>
        [mid - along * (wide / 2) - aside * (deep / 2), mid + along * (wide / 2) - aside * (deep / 2), mid + along * (wide / 2) + aside * (deep / 2), mid - along * (wide / 2) + aside * (deep / 2)];

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot centre, double radius, int seed, double houses, double walls, double plan, bool old = false)
    {
        var dice = new Dice(seed);
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Settlement, Seed = seed, Settings = { [Houses] = houses, [Walls] = walls, [Plan] = plan },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + new Spot(radius, 0)).Stored]),
        };
        string form = Plans.Of(plan);
        double within = radius * (walls >= BastionedFrom ? InsideBastions : walls >= WalledFrom ? InsideWall : 1);
        var taken = new List<(Spot At, double Reach)>();
        var streets = Streets(site, dice, centre, within, houses, form, thing.Parts, taken);
        if (form is Plans.Grown or Plans.Grid or Plans.Street && radius >= site.M(HeartFromM)) Heart(site, dice, centre, within, streets, thing.Parts, taken);
        streets.RemoveAll(s => s.Class == "lane" && s.Laid && Crosses(s, taken));
        double strips = form == Plans.Street ? Math.Min(within * StripShare, site.M(StripMostM)) : 0;
        if (form != Plans.Round)
            foreach (var street in streets.ToList()) Homes.Front(site, dice, centre, within, street, streets, houses, thing.Parts, taken, strips);
        Outskirts.Lay(site, dice, centre, within, streets, houses, walls < WalledFrom, !old, thing.Parts, taken);
        foreach (var street in streets.Where(s => s.Laid))
        {
            thing.Parts.Add(BuiltSite.Line(street.Class, street.Points));
            for (int i = 0; i + 1 < street.Points.Count; i++)
                if (site.WaterCrossing(street.Points[i], street.Points[i + 1]) is { } over)
                    thing.Parts.Add(BuiltSite.Sign("bridge", over, Math.Atan2(street.Points[i + 1].Y - street.Points[i].Y, street.Points[i + 1].X - street.Points[i].X)));
        }
        if (thing.Parts.All(p => p.Kind != PartKinds.Block))
        {
            thing.Parts.Clear();
            return thing;
        }
        thing.Parts.Add(BuiltSite.Area(BuiltThings.Quarter, Ring(centre, within * QuarterAt, QuarterSteps)));
        if (walls >= WalledFrom) Fortify(site, dice, thing.Parts, streets, within, houses, walls);
        if (!old) return thing;
        thing.Settings[Old] = 1;
        thing.Parts.RemoveAll(p => p.Kind == PartKinds.Sign && p.Class is "cross" or "shrine");
        return thing;
    }

    static bool Crosses(Street lane, List<(Spot At, double Reach)> taken)
    {
        for (int i = 0; i + 1 < lane.Points.Count; i++)
            if (taken.Any(o => o.At.FromSegment(lane.Points[i], lane.Points[i + 1]) < o.Reach)) return true;
        return false;
    }

    static List<Spot> Ring(Spot centre, double radius, int steps) => Enumerable.Range(0, steps).Select(i => centre + Spot.Toward(i * 2 * Math.PI / steps) * radius).ToList();

    static IEnumerable<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of);

    static void Fortify(BuiltSite site, Dice dice, List<BuiltPart> parts, List<Street> streets, double within, double houses, double walls)
    {
        bool trace = walls >= BastionedFrom, town = trace || within >= site.M(TownFromM) || houses >= TownHouses;
        bool Held(BuiltPart p) => p.Kind == PartKinds.Block || p.Kind == PartKinds.Area && p.Class != BuiltThings.Quarter && (!town || p.Class is "square" or "churchyard" or "green");
        var held = parts.Where(Held).SelectMany(Points).ToList();
        if (held.Count < 3) return;
        if (!trace)
        {
            var ring = Enclosures.Round(site, held, site.M(town ? WallRoomM : FenceRoomM));
            if (town) parts.RemoveAll(p => (p.Kind == PartKinds.Area && p.Class is "garden" or "orchard" || p.Kind == PartKinds.Line && p.Class == "fence") && Points(p).Any(q => !Enclosures.Inside(ring, q)));
            Enclosures.Wall(site, ring, streets, town, parts);
            if (town) Props.Keep(site, ring, held, parts);
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
        parts.AddRange(Forts.Around(middle, tips, (walls - BastionedFrom) / (1 - BastionedFrom)));
    }

    public static BuiltThing? Fronted(BuiltSite site, BuiltThing place, BuiltThing line)
    {
        if (place.Kind != BuiltKinds.Settlement || line.Kind != BuiltKinds.Road || Plans.Of(place.Setting(Plan, 0)) == Plans.Round || place.Setting(Walls, 0) >= WalledFrom) return null;
        if (line.Parts.FirstOrDefault(p => p.Kind == PartKinds.Line) is not { } laid || laid.Class == "track") return null;
        var (centre, radius) = BuiltThings.Round(place);
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

    static List<Street> Streets(BuiltSite site, Dice dice, Spot centre, double within, double houses, string form, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
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
        double bearing;
        if (streets.Count > 0)
        {
            var longest = streets.MaxBy(s => (s.Points[1] - s.Points[0]).Length)!;
            var along = longest.Points[1] - longest.Points[0];
            bearing = Math.Atan2(along.Y, along.X);
            var (near, _, off) = Nearest(streets, centre);
            var far = centre + (centre - near).Unit * (within * SpineTo);
            if (off > within * OffCentre && Passable(site, near, centre, far)) streets.Add(new Street([near, centre, far], "street", true));
        }
        else
        {
            var fall = site.Downhill(centre);
            var link = Linked(site, centre, within);
            bearing = link is { } toward ? Math.Atan2(toward.Y - centre.Y, toward.X - centre.X)
                : fall.Length > SlopeThatSteers ? Math.Atan2(fall.Left.Y, fall.Left.X) : dice.Between(0, Math.PI);
            var main = new List<Spot>();
            for (int i = 0; i <= MainSteps; i++)
            {
                double along = -within + 2 * within * i / MainSteps;
                double aside = i is 0 or MainSteps ? 0 : dice.Between(-MainWobble, MainWobble) * within;
                main.Add(centre + Spot.Toward(bearing) * along + Spot.Toward(bearing).Left * aside);
            }
            streets.Add(new Street(main, "street", true));
            if (link is { } joined && site.Buildable(main[^1] + (joined - main[^1]) * 0.5)) streets.Add(new Street([main[^1], joined], within >= site.M(TownFromM) ? "road" : "track", true));
        }
        if (form == Plans.Grid) Gridded(site, centre, within, bearing, houses, streets);
        else if (form == Plans.Grown) Grown(site, dice, centre, within, houses, streets);
        else if (form == Plans.Green && Nearest(streets, centre) is var (at, axis, _)) Villages.Green(site, dice, at, axis, within, streets, parts, taken);
        return streets;
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

    static void Grown(BuiltSite site, Dice dice, Spot centre, double within, double houses, List<Street> streets)
    {
        double per = within / site.M(LanesPerM);
        int wanted = (int)Math.Min(MostLanes, Math.Round(Lerp(LanesOpen, LanesPacked, houses) * per * per));
        for (int tries = 0, laid = 0; laid < wanted && tries < wanted * LaneTries; tries++)
        {
            var from = streets[dice.Below(streets.Count)];
            int leg = dice.Below(from.Points.Count - 1);
            Spot a = from.Points[leg], b = from.Points[leg + 1];
            var start = a + (b - a) * dice.Between(0.15, 0.85);
            var way = ((b - a).Unit.Left * (dice.Chance(0.5) ? 1 : -1)).Turned(dice.Between(-BranchPlay, BranchPlay));
            double length = within * dice.Between(0.3, 0.75);
            while (length > 0 && (start + way * length - centre).Length > within) length -= within * 0.1;
            if (length < site.M(LaneLeastM)) continue;
            var mid = start + way * (length / 2) + way.Left * (length * dice.Between(-BendPlay, BendPlay));
            var end = start + way * length;
            if (!Passable(site, start, mid, end) || Math.Abs(site.HeightAt(end) - site.HeightAt(start)) > length * LaneSteepest) continue;
            double spacing = site.M(LaneSpacingM);
            if (FromStreets(streets, mid, from) < spacing || FromStreets(streets, end, from) < spacing) continue;
            streets.Add(new Street([start, mid, end], "lane", true));
            laid++;
        }
    }

    static void Gridded(BuiltSite site, Spot centre, double within, double bearing, double houses, List<Street> streets)
    {
        double block = site.M(Lerp(BlockOpenM, BlockPackedM, houses));
        Spot u = Spot.Toward(bearing), v = u.Left;
        int reach = (int)(within / block);
        for (int i = -reach; i <= reach; i++)
        {
            double chord = Math.Sqrt(Math.Max(0, within * within - i * block * i * block));
            if (chord < block * 0.6) continue;
            var across = new[] { centre + u * (i * block) - v * chord, centre + u * (i * block) + v * chord };
            if (Passable(site, across)) streets.Add(new Street(across.ToList(), "street", true));
            if (i == 0) continue;
            var along = new[] { centre + v * (i * block) - u * chord, centre + v * (i * block) + u * chord };
            if (Passable(site, along)) streets.Add(new Street(along.ToList(), "street", true));
        }
    }

    static (Spot At, Spot Along, double Reach) Nearest(IEnumerable<Street> streets, Spot p)
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

    static void Heart(BuiltSite site, Dice dice, Spot centre, double within, List<Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var (at, along, _) = Nearest(streets, centre);
        var aside = along.Left * (dice.Chance(0.5) ? 1 : -1);
        double wide = site.M(SquareAlongM), deep = site.M(SquareDeepM);
        var square = at + aside * (Homes.HalfWidth(site, "street") + deep / 2);
        if (!site.Free(square, deep / 2) || (square - centre).Length > within) return;
        parts.Add(BuiltSite.Area("square", Rect(square, along, aside, wide, deep)));
        parts.Add(BuiltSite.Sign("well", square));
        taken.Add((square, Math.Max(wide, deep) / 2));
        var hall = square + along * (wide / 2 + site.M(HallOffM + HallAlongM / 2));
        if (within < site.M(TownFromM)) parts.Add(BuiltSite.Sign("tree", square + along * (wide * LindenAt)));
        else if (site.Free(hall, site.M(HallDeepM / 2)) && (hall - centre).Length < within)
        {
            parts.Add(BuiltSite.Block("public", hall, along, site.M(HallAlongM), site.M(HallDeepM)));
            taken.Add((hall, site.M(HallAlongM) / 2));
        }
        Church(site, square + aside * (deep / 2 + site.M(ChurchOffM)), along, aside, centre, within, parts, taken);
        Props.Market(site, dice, square, along, aside, wide, deep, within >= site.M(TownFromM), parts);
        Props.Trades(site, at, square, along, aside, wide, centre, within, parts, taken);
    }

    internal static void Chapel(BuiltSite site, Spot church, Spot along, List<BuiltPart> parts)
    {
        double length = site.M(ChurchAlongM);
        parts.Add(BuiltSite.Block("church", church, along, length, site.M(ChurchDeepM)));
        parts.Add(BuiltSite.Block("steeple", church - along * (length / 2), along, site.M(SteepleM), site.M(SteepleM)));
        parts.Add(BuiltSite.Sign("cross", church - new Spot(0, site.M(ChurchDeepM))));
    }

    internal static void Church(BuiltSite site, Spot church, Spot along, Spot aside, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double length = site.M(ChurchAlongM), room = site.M(YardRoomM);
        if (!site.Free(church, length / 2) || (church - centre).Length > within) return;
        var yard = Rect(church, along, aside, length + 2 * room, site.M(ChurchDeepM) + 2 * room);
        parts.Add(BuiltSite.Area("churchyard", yard));
        parts.Add(BuiltSite.Line("fence", yard.Append(yard[0])));
        Chapel(site, church, along, parts);
        taken.Add((church, length / 2 + room));
    }
}
