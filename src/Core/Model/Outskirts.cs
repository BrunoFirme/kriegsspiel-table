namespace KTable.Core.Model;

static class Outskirts
{
    const double BarnLeastM = 16, BarnMostM = 22, BarnDeepM = 9, BarnOffM = 12, BarnsPerM = 120, RickReachM = 11, RickRoomM = 2.5;
    const double PondShare = 0.45, PondsBelow = 0.75, PondLeastM = 7, PondMostM = 13, PondTries = 8;
    const double ShrineShare = 0.4, ShrineOffM = 7, EdgeFrom = 0.85;
    const double WindmillShare = 0.4, WindmillFromM = 140, WindmillAt = 1.12, WindmillM = 8, WatermillShare = 0.6, WatermillOffM = 11, WatermillLongM = 14, WatermillDeepM = 9, WheelOffM = 4;
    const double TreesPerM = 18, TreeRoomM = 3, TreeOffStreetM = 6, TreesFrom = 0.35, TreesTo = 1.08;
    const int MostBarns = 6, MostRicks = 3, PondSteps = 10, Bearings = 10, FewestTrees = 6, MostTrees = 60;

    public static void Lay(BuiltSite site, Dice dice, Spot centre, double within, List<Settlements.Street> streets, double houses, bool open, bool crosses, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken)
    {
        Barns(site, dice, within, streets, parts, taken);
        if (houses < PondsBelow && dice.Chance(PondShare)) Pond(site, dice, centre, within, streets, parts, taken);
        Shrines(site, dice, centre, within, streets, parts);
        if (dice.Chance(WatermillShare)) Watermill(site, centre, within, parts, taken);
        if (open && within >= site.M(WindmillFromM) && dice.Chance(WindmillShare)) Windmill(site, centre, within, streets, parts, taken);
        Trees(site, dice, centre, within, streets, parts, taken);
        Props.Waterside(site, dice, centre, within, parts);
        Props.Wild(site, dice, centre, within, crosses && open, parts, taken);
    }

    static bool Room(BuiltSite site, List<Settlements.Street> streets, List<(Spot At, double Reach)> taken, Spot p, double reach, double offStreet = 0) =>
        site.Free(p, reach) && Settlements.FromStreets(streets, p) > reach + offStreet && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    static void Barns(BuiltSite site, Dice dice, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var lanes = streets.Where(s => s.Class == "lane").ToList();
        int wanted = Math.Min(MostBarns, Math.Min(lanes.Count, 1 + (int)(within / site.M(BarnsPerM))));
        for (int i = 0; i < wanted; i++)
        {
            var lane = lanes[dice.Below(lanes.Count)];
            var way = (lane.Points[^1] - lane.Points[^2]).Unit;
            double length = site.M(dice.Between(BarnLeastM, BarnMostM)), deep = site.M(BarnDeepM);
            var at = lane.Points[^1] + way * (length / 2 + site.M(BarnOffM) * 0.3) + way.Left * (site.M(BarnOffM) * (dice.Chance(0.5) ? 1 : -1));
            if (!Room(site, streets, taken, at, length / 2)) continue;
            parts.Add(BuiltSite.Block("barn", at, way.Turned(dice.Between(-0.2, 0.2)), length, deep));
            taken.Add((at, length / 2));
            for (int r = dice.Below(MostRicks + 1); r > 0; r--)
            {
                var rick = at + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (length / 2 + site.M(dice.Between(RickRoomM, RickReachM)));
                if (Room(site, streets, taken, rick, site.M(RickRoomM))) parts.Add(BuiltSite.Sign("rick", rick));
            }
        }
    }

    public static List<Spot> Blob(Dice dice, Spot at, double radius) =>
        Enumerable.Range(0, PondSteps).Select(i => at + Spot.Toward(i * 2 * Math.PI / PondSteps) * (radius * dice.Between(0.75, 1.15))).ToList();

    static void Pond(BuiltSite site, Dice dice, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        for (int tries = 0; tries < PondTries; tries++)
        {
            double radius = site.M(dice.Between(PondLeastM, PondMostM));
            var at = centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (within * dice.Between(0.15, 0.55));
            if (!Room(site, streets, taken, at, radius * 1.15, site.M(TreeRoomM))) continue;
            parts.Add(BuiltSite.Area("pond", Blob(dice, at, radius)));
            taken.Add((at, radius * 1.15));
            return;
        }
    }

    static void Shrines(BuiltSite site, Dice dice, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        foreach (var street in streets.Where(s => s.Class != "lane"))
            foreach (var (end, before) in new[] { (street.Points[0], street.Points[1]), (street.Points[^1], street.Points[^2]) })
            {
                if ((end - centre).Length < within * EdgeFrom || !dice.Chance(ShrineShare)) continue;
                var at = end + (end - before).Unit.Left * (site.M(ShrineOffM) * (dice.Chance(0.5) ? 1 : -1));
                if (site.Free(at, 0)) parts.Add(BuiltSite.Sign("shrine", at));
            }
    }

    static void Windmill(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        Spot? best = null;
        double highest = double.MinValue, size = site.M(WindmillM);
        for (int i = 0; i < Bearings; i++)
        {
            var at = centre + Spot.Toward(i * 2 * Math.PI / Bearings) * (within * WindmillAt);
            if (!Room(site, streets, taken, at, size) || site.HeightAt(at) <= highest) continue;
            highest = site.HeightAt(at);
            best = at;
        }
        if (best is not { } mill) return;
        parts.Add(BuiltSite.Block("mill", mill, Spot.Toward(Math.PI / 4), size, size));
        parts.Add(BuiltSite.Sign("mill", mill));
        taken.Add((mill, size));
    }

    static void Watermill(BuiltSite site, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (site.NearestWater(centre) is not var (bank, reach) || reach > within || reach < site.M(WatermillOffM)) return;
        var inland = (centre - bank).Unit;
        var at = bank + inland * site.M(WatermillOffM);
        double length = site.M(WatermillLongM);
        if (!site.Buildable(at) || site.InPlace(at) || taken.Any(o => (o.At - at).Length < o.Reach + length / 2)) return;
        parts.Add(BuiltSite.Block("mill", at, inland.Left, length, site.M(WatermillDeepM)));
        parts.Add(BuiltSite.Sign("wheel", bank + inland * site.M(WheelOffM)));
        taken.Add((at, length / 2));
    }

    static void Trees(BuiltSite site, Dice dice, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        int wanted = Math.Min(MostTrees, FewestTrees + (int)(within / site.M(TreesPerM)));
        for (int i = 0; i < wanted; i++)
        {
            var at = centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (within * dice.Between(TreesFrom, TreesTo));
            if (Room(site, streets, taken, at, site.M(TreeRoomM), site.M(TreeOffStreetM))) parts.Add(BuiltSite.Sign("tree", at));
        }
    }
}
