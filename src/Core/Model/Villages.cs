namespace KTable.Core.Model;

public static class Plans
{
    public const string Grown = "grown", Grid = "grid", Street = "street", Green = "green", Round = "round";
    public static readonly string[] All = [Grown, Street, Green, Round, Grid];
    static readonly string[] ByValue = [Grown, Grid, Street, Green, Round];

    public static double Value(string? plan) => Math.Max(0, Array.IndexOf(ByValue, plan));

    public static string Of(double value) => ByValue[Math.Clamp((int)Math.Round(value), 0, ByValue.Length - 1)];
}

static class Villages
{
    const double GreenLong = 0.6, GreenMostM = 170, GreenWide = 0.2, GreenLeastM = 18, GreenWidestM = 40, GreenKeep = 0.85, ChurchFromM = 24, PondAt = 0.38, ChurchAt = -0.3, PondShare = 0.35;
    const double RingShare = 0.4, RingLeastM = 28, RingMostM = 48, EntranceM = 12, FarmApartM = 21, FarmSetM = 4, FarmLongLeastM = 15, FarmLongMostM = 20, FarmWideLeastM = 9, FarmWideMostM = 11;
    const double WedgeGapM = 3, WedgeMostM = 95, WedgeLeastM = 12, WedgeFill = 0.88, WedgeTo = 0.95, BarnShare = 0.5, BarnLongM = 14, BarnDeepM = 8, OrchardShare = 0.3, Overlap = 0.85;
    const double ChurchOutM = 42, ChurchAsideM = 26, ChurchRoomM = 70;
    const int ArcSteps = 12, RingSteps = 16, Bearings = 16, Rings = 5;
    const double DryRoom = 1.15;

    static bool Dry(BuiltSite site, Spot at, double reach)
    {
        var round = Enumerable.Range(0, Bearings).Select(i => at + Spot.Toward(i * 2 * Math.PI / Bearings) * (reach * DryRoom));
        return site.Free(at, 0) && round.All(p => site.Buildable(p) && site.WaterCrossing(at, p) == null);
    }

    static Spot? Ground(BuiltSite site, Spot centre, double reach, double within)
    {
        if (Dry(site, centre, reach)) return centre;
        for (int ring = 1; ring < Rings; ring++)
            for (int i = 0; i < Bearings; i++)
                if (centre + Spot.Toward(i * 2 * Math.PI / Bearings) * (within * ring / Rings) is var at && Dry(site, at, reach)) return at;
        return null;
    }

    public static void Green(BuiltSite site, Dice dice, Spot at, Spot along, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double half = Math.Min(within * GreenLong, site.M(GreenMostM)), wide = Math.Clamp(within * GreenWide, site.M(GreenLeastM), site.M(GreenWidestM));
        var aside = along.Left;
        if (!Dry(site, at, wide) || !site.Buildable(at + along * half) || !site.Buildable(at - along * half)) return;
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
        if (Ground(site, asked, ring, reach) is not { } centre) return;
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
