namespace KTable.Core.Model;

public static class Settlements
{
    public const string Houses = "houses", Walls = "walls", Plan = "plan";
    public const double WalledFrom = 1.0 / 3, BastionedFrom = 2.0 / 3, GridFrom = 0.5;
    const double StreetHalfM = 4, LaneHalfM = 2.5, SetbackM = 3, HouseLeastM = 9, HouseMostM = 15, DepthLeastM = 7, DepthMostM = 10;
    const double OpenGapM = 22, PackedGapM = 1.5, KeepOpen = 0.55, KeepPacked = 0.97, GardensBelow = 0.5, GardenDepthM = 24, GardenShare = 0.5;
    const double LanesOpen = 2, LanesPacked = 9, LanesPerM = 150, LaneLeastM = 40, LaneSpacingM = 28, LaneTries = 8, MostLanes = 80;
    const double BlockOpenM = 95, BlockPackedM = 60, InsideWall = 0.88, InsideBastions = 0.6, WallAt = 0.96, WallWobble = 0.03, GateHalfM = 7, TowersApartM = 70;
    const double HeartFromM = 120, SquareAlongM = 34, SquareDeepM = 26, ChurchAlongM = 24, ChurchDeepM = 11, ChurchOffM = 14, QuarterFrom = 0.6, QuarterAt = 0.92;
    const double SlopeThatSteers = 0.03, MainWobble = 0.06, Overlap = 0.85, TurnPlay = 0.06, BranchPlay = 0.4, BendPlay = 0.12;
    const int WallSteps = 40, QuarterSteps = 20, MainSteps = 4, TraceBastionsApartM = 220;

    sealed record Street(List<Spot> Points, string Class, bool Laid);

    static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot centre, double radius, int seed, double houses, double walls, double plan)
    {
        var dice = new Dice(seed);
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Settlement, Seed = seed, Settings = { [Houses] = houses, [Walls] = walls, [Plan] = plan },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + new Spot(radius, 0)).Stored]),
        };
        double within = radius * (walls >= BastionedFrom ? InsideBastions : walls >= WalledFrom ? InsideWall : 1);
        var streets = Streets(site, dice, centre, within, houses, plan);
        var taken = new List<(Spot At, double Reach)>();
        if (radius >= site.M(HeartFromM)) Heart(site, dice, centre, within, streets, thing.Parts, taken);
        foreach (var street in streets.ToList()) Frontages(site, dice, centre, within, street, streets, houses, thing.Parts, taken);
        foreach (var street in streets.Where(s => s.Laid)) thing.Parts.Add(BuiltSite.Line(street.Class, street.Points));
        if (houses >= QuarterFrom) thing.Parts.Add(BuiltSite.Area("quarter", Ring(centre, within * QuarterAt, QuarterSteps, null)));
        if (walls >= BastionedFrom)
        {
            int bastions = Math.Clamp((int)Math.Round(2 * Math.PI * radius / site.M(TraceBastionsApartM)), Forts.FewestBastions + 1, Forts.MostBastions + 4);
            thing.Parts.AddRange(Forts.Works(centre, radius, bastions, dice.Between(0, Math.PI), (walls - BastionedFrom) / (1 - BastionedFrom)));
        }
        else if (walls >= WalledFrom) Wall(site, dice, centre, radius * WallAt, streets, thing.Parts);
        return thing;
    }

    static List<Spot> Ring(Spot centre, double radius, int steps, Dice? wobble) =>
        Enumerable.Range(0, steps).Select(i => centre + Spot.Toward(i * 2 * Math.PI / steps) * (radius * (1 + (wobble?.Between(-WallWobble, WallWobble) ?? 0)))).ToList();

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

    static List<Street> Streets(BuiltSite site, Dice dice, Spot centre, double within, double houses, double plan)
    {
        var streets = new List<Street>();
        foreach (var (a, b) in site.RoadSegments())
            if (Clipped(a, b, centre, within) is var (p, q)) streets.Add(new Street([p, q], "road", false));
        double bearing;
        if (streets.Count > 0)
        {
            var longest = streets.MaxBy(s => (s.Points[1] - s.Points[0]).Length)!;
            var along = longest.Points[1] - longest.Points[0];
            bearing = Math.Atan2(along.Y, along.X);
        }
        else
        {
            var fall = site.Downhill(centre);
            bearing = fall.Length > SlopeThatSteers ? Math.Atan2(fall.Left.Y, fall.Left.X) : dice.Between(0, Math.PI);
            var main = new List<Spot>();
            for (int i = 0; i <= MainSteps; i++)
            {
                double along = -within + 2 * within * i / MainSteps;
                double aside = i is 0 or MainSteps ? 0 : dice.Between(-MainWobble, MainWobble) * within;
                main.Add(centre + Spot.Toward(bearing) * along + Spot.Toward(bearing).Left * aside);
            }
            streets.Add(new Street(main, "street", true));
        }
        if (plan >= GridFrom) Gridded(site, centre, within, bearing, houses, streets);
        else Grown(site, dice, centre, within, houses, streets);
        return streets;
    }

    static bool Passable(BuiltSite site, params Spot[] path)
    {
        const int Samples = 4;
        for (int i = 0; i + 1 < path.Length; i++)
            for (int s = 0; s <= Samples; s++)
                if (!site.Buildable(path[i] + (path[i + 1] - path[i]) * (s / (double)Samples))) return false;
        return true;
    }

    static double FromStreets(IEnumerable<Street> streets, Spot p, Street? but = null)
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
            if (!Passable(site, start, mid, end)) continue;
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

    static void Heart(BuiltSite site, Dice dice, Spot centre, double within, List<Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        Spot at = centre, along = new(1, 0);
        double least = double.MaxValue;
        foreach (var street in streets)
            for (int i = 0; i + 1 < street.Points.Count; i++)
            {
                Spot a = street.Points[i], b = street.Points[i + 1];
                var ab = b - a;
                var near = a + ab * Math.Clamp((centre - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
                if ((near - centre).Length >= least) continue;
                least = (near - centre).Length;
                at = near;
                along = ab.Unit;
            }
        var aside = along.Left * (dice.Chance(0.5) ? 1 : -1);
        double wide = site.M(SquareAlongM), deep = site.M(SquareDeepM);
        var square = at + aside * (site.M(StreetHalfM) + deep / 2);
        if (!site.Buildable(square) || (square - centre).Length > within) return;
        parts.Add(BuiltSite.Area("square", [square - along * (wide / 2) - aside * (deep / 2), square + along * (wide / 2) - aside * (deep / 2),
            square + along * (wide / 2) + aside * (deep / 2), square - along * (wide / 2) + aside * (deep / 2)]));
        parts.Add(BuiltSite.Sign("well", square));
        taken.Add((square, Math.Max(wide, deep) / 2));
        var church = square + aside * (deep / 2 + site.M(ChurchOffM));
        if (!site.Buildable(church) || (church - centre).Length > within) return;
        parts.Add(BuiltSite.Block("church", church, along, site.M(ChurchAlongM), site.M(ChurchDeepM)));
        parts.Add(BuiltSite.Sign("cross", church - new Spot(0, site.M(ChurchDeepM))));
        taken.Add((church, site.M(ChurchAlongM) / 2));
    }

    static void Frontages(BuiltSite site, Dice dice, Spot centre, double within, Street street, List<Street> streets, double houses, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken)
    {
        double half = site.M(street.Class == "lane" ? LaneHalfM : StreetHalfM), gap = site.M(Lerp(OpenGapM, PackedGapM, houses)), keep = Lerp(KeepOpen, KeepPacked, houses);
        for (int i = 0; i + 1 < street.Points.Count; i++)
        {
            Spot a = street.Points[i], b = street.Points[i + 1];
            var along = (b - a).Unit;
            double length = (b - a).Length;
            foreach (int side in new[] { 1, -1 })
                for (double s = dice.Between(0, gap); s < length;)
                {
                    double wide = site.M(dice.Between(HouseLeastM, HouseMostM)), deep = site.M(dice.Between(DepthLeastM, DepthMostM));
                    var at = a + along * (s + wide / 2) + along.Left * (side * (half + site.M(SetbackM) + deep / 2));
                    s += wide + gap * dice.Between(0.6, 1.4);
                    double reach = Math.Max(wide, deep) / 2;
                    if (!dice.Chance(keep) || s > length + wide || (at - centre).Length > within || !site.Buildable(at)) continue;
                    if (FromStreets(streets, at, street) < deep / 2 + half || taken.Any(o => (o.At - at).Length < (o.Reach + reach) * Overlap)) continue;
                    parts.Add(BuiltSite.Block("house", at, along.Turned(dice.Between(-TurnPlay, TurnPlay)), wide, deep));
                    taken.Add((at, reach));
                    if (houses >= GardensBelow || !dice.Chance(GardenShare)) continue;
                    double croft = site.M(GardenDepthM);
                    var behind = at + along.Left * (side * (deep / 2 + croft / 2));
                    if (!site.Buildable(behind) || FromStreets(streets, behind) < croft / 2 || taken.Any(o => (o.At - behind).Length < o.Reach + croft / 2)) continue;
                    var out_ = along.Left * (side * croft / 2);
                    parts.Add(BuiltSite.Area("garden", [behind - along * (wide / 2) - out_, behind + along * (wide / 2) - out_, behind + along * (wide / 2) + out_, behind - along * (wide / 2) + out_]));
                    taken.Add((behind, croft / 2));
                }
        }
    }

    static void Wall(BuiltSite site, Dice dice, Spot centre, double radius, List<Street> streets, List<BuiltPart> parts)
    {
        var gates = new List<double>();
        foreach (var street in streets)
            foreach (var end in new[] { street.Points[0], street.Points[^1] })
            {
                if (street.Class == "lane" || (end - centre).Length < radius * 0.8) continue;
                double bearing = Math.Atan2(end.Y - centre.Y, end.X - centre.X);
                gates.Add(bearing);
                if (street.Laid) parts.Add(BuiltSite.Line(street.Class, [end, centre + Spot.Toward(bearing) * (radius * 1.04)]));
            }
        double gateHalf = site.M(GateHalfM) / radius;
        var ring = Ring(centre, radius, WallSteps, dice);
        var run = new List<Spot>();
        for (int i = 0; i <= WallSteps; i++)
        {
            double bearing = i * 2 * Math.PI / WallSteps;
            bool open = gates.Any(g => Math.Abs(Math.IEEERemainder(g - bearing, 2 * Math.PI)) < gateHalf + Math.PI / WallSteps);
            if (!open) { run.Add(ring[i % WallSteps]); continue; }
            if (run.Count > 1) parts.Add(BuiltSite.Line("wall", run));
            run = new();
        }
        if (run.Count > 1) parts.Add(BuiltSite.Line("wall", run));
        int towers = Math.Max(4, (int)(2 * Math.PI * radius / site.M(TowersApartM)));
        for (int i = 0; i < towers; i++) parts.Add(BuiltSite.Sign("tower", ring[i * WallSteps / towers % WallSteps]));
    }
}
