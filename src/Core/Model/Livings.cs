namespace KTable.Core.Model;

static class Livings
{
    const double BankReach = 1.4, HutLongM = 6.5, HutDeepM = 5, HutBackM = 9, HutsApartM = 13, HutRoomM = 3.6, MillLongM = 14, MillDeepM = 9, MillBackM = 8, FerryLeastM = 22, BridgeNearM = 180;
    const double TradeReachM = 70, OsierLongM = 18, OsierDeepM = 9, FewestForTrades = 12, LandingM = 5;
    const int MostHuts = 7;

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    public static void Lay(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var land = Surroundings.Of(site, centre, within);
        if (land.Banks.Count > 0 && !land.Sea) Waterside(site, land, centre, within, streets, parts, taken);
    }

    static void Waterside(BuiltSite site, Surroundings land, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var banks = land.Banks.Where(b => b.Reach <= within * BankReach).ToList();
        if (banks.Count == 0) return;
        var roll = BuiltSite.Roll(centre, 101);
        int manner = site.Manner;
        Fishing(site, roll, banks, within, streets, parts, taken);
        if (land.River && manner is not (Manners.Egyptian or Manners.Tribal) && !parts.Any(p => p.Kind == PartKinds.Sign && p.Class == "wheel")) Mill(site, banks, parts, taken);
        if (land.River) Ferry(site, banks, centre, streets, parts, taken);
        if (manner is Manners.Egyptian or Manners.Northern && !parts.Any(p => p.Class == "sweep"))
            parts.Add(BuiltSite.Sign("sweep", banks[roll.Below(banks.Count)].Land - banks[0].Way * site.M(3), Math.Atan2(banks[0].Way.Y, banks[0].Way.X)));
        Trades(site, banks, parts);
        var bed = banks[roll.Below(banks.Count)];
        var osiers = bed.Land - bed.Way * site.M(OsierDeepM / 2 + 3);
        if (manner != Manners.Egyptian && roll.Chance(0.6) && Room(site, taken, osiers, site.M(OsierLongM / 2)) && Settlements.FromStreets(streets, osiers) > site.M(OsierLongM / 2))
        {
            Spot along = bed.Way.Left * (site.M(OsierLongM) / 2), deep = bed.Way * (site.M(OsierDeepM) / 2);
            parts.Add(BuiltSite.Area("osiers", [osiers - along - deep, osiers + along - deep, osiers + along + deep, osiers - along + deep]));
            taken.Add((osiers, site.M(OsierLongM / 2)));
        }
    }

    static void Fishing(BuiltSite site, Dice roll, List<Surroundings.Bank> banks, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        int wanted = Math.Clamp((int)(within / site.M(45)), 2, MostHuts), laid = 0;
        Spot? last = null;
        int from = roll.Below(banks.Count);
        for (int k = 0; k < banks.Count && laid < wanted; k++)
        {
            int i = (from + k) % banks.Count;
            var bank = banks[i];
            var along = Surroundings.Tangent(banks, i);
            var hut = bank.Land - bank.Way * site.M(HutBackM);
            if (last is { } before && (before - hut).Length < site.M(HutsApartM)) continue;
            if (!Room(site, taken, hut, site.M(HutRoomM)) || Settlements.FromStreets(streets, hut) < site.M(HutLongM)) continue;
            double turn = Math.Atan2(bank.Way.Y, bank.Way.X);
            parts.Add(BuiltSite.Block("fisher", hut, along, site.M(HutLongM), site.M(HutDeepM)));
            parts.Add(BuiltSite.Sign("boat", bank.Land - bank.Way * site.M(1.5) + along * site.M(roll.Between(-2, 2)), turn + roll.Between(-0.3, 0.3)));
            if (roll.Chance(0.75)) parts.Add(BuiltSite.Sign("nets", bank.Land - bank.Way * site.M(4.2) + along * site.M(4.5), Math.Atan2(along.Y, along.X)));
            if (roll.Chance(0.4)) parts.Add(BuiltSite.Sign("racks", hut + along * site.M(HutLongM / 2 + 2.5), turn));
            if (roll.Chance(0.3)) parts.Add(BuiltSite.Sign("barrels", hut - along * site.M(HutLongM / 2 + 1.2), turn));
            taken.Add((hut, site.M(HutRoomM)));
            last = hut;
            laid++;
        }
        if (laid > 0 && !Manners.Old(site.Manner)) parts.Add(BuiltSite.Sign("washing", banks[from].Land - banks[from].Way * site.M(2.8), Math.Atan2(banks[from].Way.Left.Y, banks[from].Way.Left.X)));
    }

    static void Mill(BuiltSite site, List<Surroundings.Bank> banks, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        foreach (var (bank, i) in banks.Select((b, i) => (b, i)).OrderByDescending(x => site.HeightAt(x.b.Land)))
        {
            var at = bank.Land - bank.Way * site.M(MillBackM);
            if (!Room(site, taken, at, site.M(MillLongM / 2))) continue;
            parts.Add(BuiltSite.Block("mill", at, Surroundings.Tangent(banks, i), site.M(MillLongM), site.M(MillDeepM)));
            parts.Add(BuiltSite.Sign("wheel", bank.Land - bank.Way * site.M(1.5)));
            taken.Add((at, site.M(MillLongM / 2)));
            return;
        }
    }

    static bool Bridged(BuiltSite site, Spot at, List<BuiltPart> parts) =>
        parts.Concat(site.Others.SelectMany(o => o.Parts)).Any(p => p.Kind == PartKinds.Sign && p.Class is "bridge" or "ford" && (Spot.Of(BuiltGeometry.Points(p.At)[0]) - at).Length < site.M(BridgeNearM));

    static void Ferry(BuiltSite site, List<Surroundings.Bank> banks, Spot centre, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var bank = banks.MinBy(b => b.Reach)!;
        if (bank.WideM < FerryLeastM || Bridged(site, bank.Land, parts) || streets.Count == 0) return;
        var far = bank.Land + bank.Way * site.M(bank.WideM + LandingM * 2);
        if (!site.Buildable(far)) return;
        var (from, _, reach) = Settlements.Nearest(streets, bank.Land);
        if (reach > (bank.Land - centre).Length) return;
        if (reach > site.M(6)) parts.Add(BuiltSite.Line("track", [from, bank.Land - bank.Way * site.M(1)]));
        parts.Add(BuiltSite.Line("rope", [bank.Land, far]));
        foreach (var landing in new[] { bank.Land, far - bank.Way * site.M(LandingM) }) parts.Add(BuiltSite.Line("jetty", [landing - bank.Way * site.M(1.5), landing + bank.Way * site.M(LandingM)]));
        parts.Add(BuiltSite.Sign("ferryboat", bank.Land + bank.Way * site.M(bank.WideM * 0.45 + LandingM), Math.Atan2(bank.Way.Y, bank.Way.X)));
        var house = bank.Land - bank.Way * site.M(HutBackM) + bank.Way.Left * site.M(9);
        if (!Room(site, taken, house, site.M(HutRoomM))) return;
        parts.Add(BuiltSite.Block("fisher", house, bank.Way.Left, site.M(HutLongM), site.M(HutDeepM)));
        taken.Add((house, site.M(HutRoomM)));
    }

    static void Trades(BuiltSite site, List<Surroundings.Bank> banks, List<BuiltPart> parts)
    {
        var homes = parts.Where(p => p.Kind == PartKinds.Block && p.Class == "house" && p.At.Count == 8).ToList();
        if (homes.Count < FewestForTrades || site.Manner == Manners.Tribal) return;
        Surroundings.Bank Nearest(BuiltPart home) => banks.MinBy(b => (BuiltSite.Round(home).At - b.Land).Length)!;
        var byWater = homes.Select(h => (Home: h, Reach: (BuiltSite.Round(h).At - Nearest(h).Land).Length)).Where(x => x.Reach < site.M(TradeReachM)).OrderBy(x => x.Reach).Take(2).ToList();
        string[] trades = site.Manner == Manners.Roman ? ["fuller", "tanner"] : ["tanner", "dyer"];
        for (int i = 0; i < byWater.Count; i++)
        {
            var home = byWater[i].Home;
            var at = BuiltSite.Round(home).At;
            var toWater = (Nearest(home).Land - at).Unit;
            home.Class = trades[i];
            parts.Add(BuiltSite.Sign(trades[i] == "tanner" ? "pits" : "vats", at + toWater * (BuiltSite.Round(home).Reach + site.M(2.5)), Math.Atan2(toWater.Y, toWater.X)));
        }
    }
}
