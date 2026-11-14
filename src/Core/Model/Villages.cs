namespace KTable.Core.Model;

public static class Plans
{
    public const string Grown = "grown", Grid = "grid", Street = "street", Green = "green", Round = "round", Maze = "maze", Wards = "wards", Rows = "rows", Shore = "shore", Scatter = "scatter";
    public static readonly string[] All = [Grown, Street, Green, Round, Grid, Maze, Wards, Rows, Shore, Scatter];
    static readonly string[] ByValue = [Grown, Grid, Street, Green, Round, Maze, Wards, Rows, Shore, Scatter];

    public static IReadOnlyList<string> Offered(int manner) => Ways.Of(manner).Offered;

    public static string For(int manner, string asked) => Array.IndexOf(Ways.Of(manner).Offered, asked) >= 0 ? asked : Ways.Of(manner).Offered[0];

    public static double Value(string? plan) => Math.Max(0, Array.IndexOf(ByValue, plan));

    public static string Of(double value) => ByValue[Math.Clamp((int)Math.Round(value), 0, ByValue.Length - 1)];
}

static class Villages
{
    const double GreenLong = 0.6, GreenMostM = 170, GreenWide = 0.2, GreenLeastM = 18, GreenWidestM = 40, GreenKeep = 0.85, ChurchFromM = 24, PondAt = 0.38, ChurchAt = -0.3, PondShare = 0.35;
    const double RingShare = 0.4, RingLeastM = 28, RingMostM = 48, EntranceM = 12, FarmApartM = 21, FarmSetM = 4, FarmLongLeastM = 15, FarmLongMostM = 20, FarmWideLeastM = 9, FarmWideMostM = 11;
    const double WedgeGapM = 3, WedgeMostM = 95, WedgeLeastM = 12, WedgeFill = 1, WedgeTo = 0.95, BarnShare = 0.5, BarnLongM = 14, BarnDeepM = 8, OrchardShare = 0.3, Overlap = 0.85;
    const double ChurchOutM = 42, ChurchAsideM = 26, ChurchRoomM = 70;
    const double WardInsetM = 3, WardGateM = 5, WardLeastM = 22, TwoLanesFromM = 36;
    const double CompoundOpenM = 78, CompoundPackedM = 40, CompoundLeastM = 13, CompoundMostM = 20, CompoundGap = 0.55, ClearingM = 17, GreatHouseFromM = 110, LongShare = 0.16;
    const double ShoreFrom = 0.15, ShoreTo = 1.7, ShoreStep = 0.08;
    const int ArcSteps = 12, RingSteps = 16, ShoreBearings = 24, CompoundTries = 5;

    public static double? Shoreward(BuiltSite site, Spot centre, double within)
    {
        (double Bearing, double Far)? best = null;
        for (int i = 0; i < ShoreBearings; i++)
            for (double far = within * ShoreFrom; far <= within * ShoreTo; far += within * ShoreStep)
            {
                if (site.KindAt(centre + Spot.Toward(i * 2 * Math.PI / ShoreBearings) * far) != GroundKinds.Water) continue;
                if (best is not { } known || far < known.Far) best = (i * 2 * Math.PI / ShoreBearings, far);
                break;
            }
        return best?.Bearing;
    }

    public static void Wards(BuiltSite site, List<Settlements.Cell> cells, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double inset = Homes.HalfWidth(site, "street") + site.M(WardInsetM), gate = site.M(WardGateM) / 2;
        foreach (var cell in cells)
        {
            double hu = cell.HalfU - inset, hv = cell.HalfV - inset;
            if (hu < site.M(WardLeastM) || hv < site.M(WardLeastM) || !site.Free(cell.At, 0)) continue;
            bool civic = taken.Any(o => Math.Abs((o.At - cell.At).Dot(cell.U)) < cell.HalfU && Math.Abs((o.At - cell.At).Dot(cell.V)) < cell.HalfV);
            Spot Corner(int su, int sv) => cell.At + cell.U * (su * hu) + cell.V * (sv * hv);
            double[] lanes = civic ? [0] : hv > site.M(TwoLanesFromM) ? [-0.5, 0.5] : [0];
            foreach (int sv in new[] { -1, 1 }) parts.Add(BuiltSite.Line("wardwall", [Corner(-1, sv), Corner(1, sv)]));
            foreach (int su in new[] { -1, 1 })
            {
                var run = new List<Spot> { Corner(su, -1) };
                foreach (double lane in lanes)
                {
                    var mid = cell.At + cell.U * (su * hu) + cell.V * (lane * hv);
                    run.Add(mid - cell.V * gate);
                    parts.Add(BuiltSite.Line("wardwall", run));
                    run = [mid + cell.V * gate];
                }
                run.Add(Corner(su, 1));
                parts.Add(BuiltSite.Line("wardwall", run));
            }
            if (civic) continue;
            foreach (double lane in lanes)
                streets.Add(new Settlements.Street([cell.At - cell.U * cell.HalfU + cell.V * (lane * hv), cell.At + cell.U * cell.HalfU + cell.V * (lane * hv)], "lane", true));
        }
    }

    static void Compound(BuiltSite site, Dice dice, Spot at, double reach, List<BuiltPart> parts)
    {
        double turn = dice.Between(0, 2 * Math.PI);
        parts.Add(BuiltSite.Line(dice.Chance(0.35) ? "palisade" : "fence",
            Enumerable.Range(0, RingSteps + 1).Select(i => at + Spot.Toward(turn + CompoundGap / 2 + i * (2 * Math.PI - CompoundGap) / RingSteps) * reach)));
        parts.Add(BuiltSite.Area("pen", Enumerable.Range(0, RingSteps).Select(i => at + Spot.Toward(i * 2 * Math.PI / RingSteps) * reach)));
        Spot In(double bearing, double share) => at + Spot.Toward(turn + bearing) * (reach * share);
        double main = site.M(dice.Between(7, 11));
        if (dice.Chance(LongShare)) parts.Add(BuiltSite.Block("longhouse", In(Math.PI, 0.25), Spot.Toward(turn + Math.PI / 2), site.M(dice.Between(16, 22)), site.M(6.5)));
        else parts.Add(BuiltSite.Block("roundhouse", In(Math.PI, 0.32), Spot.Toward(turn), main, main));
        if (dice.Chance(0.55)) parts.Add(BuiltSite.Block("roundhouse", In(Math.PI / 2, 0.55), Spot.Toward(turn), main * 0.65, main * 0.65));
        if (dice.Chance(0.75)) parts.Add(BuiltSite.Block("granary", In(-Math.PI / 2, 0.58), Spot.Toward(turn), site.M(3.2), site.M(3.2)));
        if (dice.Chance(0.3)) parts.Add(BuiltSite.Block("granary", In(-Math.PI / 2 - 0.7, 0.62), Spot.Toward(turn), site.M(2.6), site.M(2.6)));
        if (dice.Chance(0.5)) parts.Add(BuiltSite.Sign(dice.Chance(0.4) ? "pigs" : dice.Chance(0.5) ? "sheep" : "cattle", In(0.5, 0.5), dice.Between(0, 2 * Math.PI)));
        if (dice.Chance(0.35)) parts.Add(BuiltSite.Sign("dungheap", In(-0.6, 0.6)));
        if (dice.Chance(0.3)) parts.Add(BuiltSite.Sign("woodpile", In(2.4, 0.7), turn));
    }

    public static void Scatter(BuiltSite site, Dice dice, Spot centre, double within, double houses, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double clearing = site.M(ClearingM), apart = site.M(Settlements.Lerp(CompoundOpenM, CompoundPackedM, houses));
        var (near, along, _) = Settlements.Nearest(streets, centre);
        var open = near + along.Left * (clearing + site.M(4));
        if (site.Free(open, clearing))
        {
            parts.Add(BuiltSite.Area("green", Enumerable.Range(0, RingSteps).Select(i => open + Spot.Toward(i * 2 * Math.PI / RingSteps) * clearing)));
            parts.Add(BuiltSite.Sign("menhir", open));
            taken.Add((open, clearing));
            var hall = open + along.Left * (clearing + site.M(9));
            if (within >= site.M(GreatHouseFromM) && site.Free(hall, site.M(7)))
            {
                parts.Add(BuiltSite.Block("greathouse", hall, along, site.M(15), site.M(15)));
                taken.Add((hall, site.M(8)));
            }
        }
        int wanted = (int)(Math.PI * within * within / (apart * apart));
        for (int tries = 0, laid = 0; laid < wanted && tries < wanted * CompoundTries; tries++)
        {
            var at = centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (within * Math.Sqrt(dice.Next()));
            double reach = site.M(dice.Between(CompoundLeastM, CompoundMostM));
            if (!site.Free(at, reach) || Settlements.FromStreets(streets, at) < reach + site.M(2) || taken.Any(o => (o.At - at).Length < o.Reach + reach + apart * 0.12)) continue;
            Compound(site, dice, at, reach, parts);
            taken.Add((at, reach));
            laid++;
        }
    }

    public static void Green(BuiltSite site, Dice dice, Spot at, Spot along, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double half = Math.Min(within * GreenLong, site.M(GreenMostM)), wide = Math.Clamp(within * GreenWide, site.M(GreenLeastM), site.M(GreenWidestM));
        var aside = along.Left;
        if (!site.Dry(at, wide) || !site.Buildable(at + along * half) || !site.Buildable(at - along * half)) return;
        Spot On(double t, double side) => at + along * (t * half) + aside * (side * wide * (1 - t * t));
        var ts = Enumerable.Range(0, ArcSteps + 1).Select(i => -1 + 2.0 * i / ArcSteps).ToList();
        foreach (int side in new[] { 1, -1 }) streets.Add(new Settlements.Street(ts.Select(t => On(t, side)).ToList(), "street", true));
        parts.Add(BuiltSite.Area("green", ts.Select(t => On(t, 1)).Concat(Enumerable.Reverse(ts).Skip(1).SkipLast(1).Select(t => On(t, -1)))));
        foreach (double t in ts.Skip(1).SkipLast(1)) taken.Add((at + along * (t * half), wide * (1 - t * t) * GreenKeep));
        parts.Add(BuiltSite.Area("pond", Outskirts.Blob(dice, at + along * (PondAt * half), wide * PondShare)));
        parts.Add(BuiltSite.Sign("well", at + along * (0.05 * half)));
        parts.Add(BuiltSite.Sign("tree", at + along * (0.65 * half)));
        parts.Add(BuiltSite.Sign("tree", at - along * (0.7 * half)));
        if (wide >= site.M(ChurchFromM)) Settlements.Chapel(site, at + along * (ChurchAt * half), along, parts);
    }

    public static void Round(BuiltSite site, Dice dice, Spot asked, double reach, Spot? road, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double ring = Math.Clamp(reach * RingShare, site.M(RingLeastM), site.M(RingMostM));
        if (site.DryNear(asked, ring, reach) is not { } centre) return;
        double within = Math.Max(ring, reach - (centre - asked).Length);
        bool joins = road is { } near && (near - centre).Length > ring;
        double entrance = joins ? Math.Atan2(road!.Value.Y - centre.Y, road.Value.X - centre.X) : dice.Between(0, 2 * Math.PI);
        var way = Spot.Toward(entrance);
        var mouth = centre + way * ring;
        streets.Add(new Settlements.Street([mouth, joins ? road!.Value : centre + way * within], "lane", true));
        parts.Add(BuiltSite.Area("green", Enumerable.Range(0, RingSteps).Select(i => centre + Spot.Toward(i * 2 * Math.PI / RingSteps) * ring)));
        parts.Add(BuiltSite.Sign("tree", centre));
        taken.Add((centre, ring));
        if (within >= ring + site.M(ChurchRoomM))
            Settlements.Church(site, mouth + way * site.M(ChurchOutM) + way.Left * site.M(ChurchAsideM), way, way.Left, centre, within, parts, taken);
        double gap = 2 * Math.Asin(Math.Min(1, site.M(EntranceM) / ring));
        int farms = (int)((2 * Math.PI - gap) * (ring + site.M(FarmSetM + FarmLongMostM / 2)) / site.M(FarmApartM));
        double each = (2 * Math.PI - gap) / Math.Max(1, farms);
        for (int i = 0; i < farms; i++) Farmstead(site, dice, centre, ring, within, entrance + gap / 2 + (i + 0.5) * each, each, parts, taken);
    }

    static void Farmstead(BuiltSite site, Dice dice, Spot centre, double ring, double within, double bearing, double each, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var outward = Spot.Toward(bearing);
        double length = site.M(dice.Between(FarmLongLeastM, FarmLongMostM)), wide = site.M(dice.Between(FarmWideLeastM, FarmWideMostM));
        var at = centre + outward * (ring + site.M(FarmSetM) + length / 2);
        if (!site.Free(at, length / 2) || taken.Any(o => (o.At - at).Length < (o.Reach + length / 2) * Overlap)) return;
        parts.Add(BuiltSite.Block("house", at, outward, length, wide));
        taken.Add((at, length / 2));
        double from = ring + site.M(FarmSetM) + length + site.M(WedgeGapM);
        if (dice.Chance(BarnShare) && site.Free(centre + outward * (from + site.M(BarnDeepM / 2)), site.M(BarnDeepM / 2)))
        {
            parts.Add(BuiltSite.Block("barn", centre + outward * (from + site.M(BarnDeepM / 2)), outward.Left, site.M(BarnLongM), site.M(BarnDeepM)));
            from += site.M(BarnDeepM + WedgeGapM);
        }
        double to = Math.Min(within * WedgeTo, from + site.M(WedgeMostM)), half = each / 2 * WedgeFill;
        Spot At(double reach, double turn) => centre + Spot.Toward(bearing + turn) * reach;
        var middle = At((from + to) / 2, 0);
        if (to - from < site.M(WedgeLeastM) || !site.Free(middle, 0) || !site.Free(At(to, 0), 0) || taken.Any(o => (o.At - middle).Length < o.Reach)) return;
        bool orchard = dice.Chance(OrchardShare);
        parts.Add(BuiltSite.Area(orchard ? "orchard" : "garden", [At(from, -half), At(from, half), At(to, half), At(to, -half)]));
        parts.Add(BuiltSite.Line("fence", [At(from, -half), At(to, -half)]));
        if (orchard) Homes.Trees(site, dice, At(from, 0), outward, outward.Left, to - from, (from + to) * Math.Sin(half), parts);
    }
}
