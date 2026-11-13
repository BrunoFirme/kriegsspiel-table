namespace KTable.Core.Model;

static class Livings
{
    const double BankReach = 1.4, HutLongM = 6.5, HutDeepM = 5, HutBackM = 9, HutsApartM = 13, HutRoomM = 3.6, MillLongM = 14, MillDeepM = 9, MillBackM = 8, FerryLeastM = 22, BridgeNearM = 180;
    const double TradeReachM = 70, OsierLongM = 18, OsierDeepM = 9, FewestForTrades = 12, LandingM = 5;
    const double QuayOutM = 3, PierLeastM = 16, PierMostM = 38, MoleFromM = 150, MoleOutM = 45, MoleHookM = 40, StoreLongM = 16, StoreDeepM = 9, StoreBackM = 13, FrontShare = 0.45, PiersApartM = 22;
    const int MostHuts = 7, PortFromBlocks = 14, MostPiers = 3;

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    public static void Lay(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var land = Surroundings.Of(site, centre, within);
        if (land.Banks.Count == 0) return;
        if (land.Sea) Harbour(site, land, centre, within, streets, parts, taken);
        else Waterside(site, land, centre, within, streets, parts, taken);
    }

    static List<Surroundings.Bank> Front(List<Surroundings.Bank> banks, int middle, double reach)
    {
        int from = middle, to = middle;
        bool Near(int i) => (banks[i].Land - banks[middle].Land).Length < reach;
        while (from > 0 && Near(from) && banks[from].Bearing - banks[from - 1].Bearing == 1) from--;
        while (to + 1 < banks.Count && Near(to) && banks[to + 1].Bearing - banks[to].Bearing == 1) to++;
        return banks.GetRange(from, to - from + 1);
    }

    static void Harbour(BuiltSite site, Surroundings land, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var banks = land.Banks.Where(b => b.Sea && b.Reach <= within * BankReach).ToList();
        if (banks.Count == 0) return;
        var roll = BuiltSite.Roll(centre, 131);
        bool port = site.Manner != Manners.Tribal && parts.Count(p => p.Kind == PartKinds.Block) >= PortFromBlocks;
        var front = port ? Front(banks, banks.IndexOf(banks.MinBy(b => b.Reach)!), within * FrontShare) : [];
        if (front.Count >= 3) Port(site, roll, front, within, streets, parts, taken);
        Fishing(site, roll, front.Count >= 3 ? banks.Except(front).ToList() : banks, within, streets, parts, taken);
    }

    static void Port(BuiltSite site, Dice roll, List<Surroundings.Bank> front, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        Spot Edge(Surroundings.Bank b) => b.Land + b.Way * site.M(QuayOutM);
        double Heading(Spot way) => Math.Atan2(way.Y, way.X);
        parts.Add(BuiltSite.Line("quay", front.Select(Edge)));
        var middle = front[front.Count / 2];
        if (streets.Count > 0 && Settlements.Nearest(streets, middle.Land) is var (from, _, reach) && reach > site.M(6) && reach < within) parts.Add(BuiltSite.Line("lane", [from, middle.Land]));
        int piers = 0;
        Spot? pier = null;
        for (int i = 1; i < front.Count - 1 && piers < MostPiers; i++)
        {
            var b = front[i];
            double length = site.M(roll.Between(PierLeastM, PierMostM));
            var end = Edge(b) + b.Way * length;
            if (pier is { } before && (before - b.Land).Length < site.M(PiersApartM) || !site.Sea(end) || !site.Sea(Edge(b) + b.Way * (length / 2))) continue;
            pier = b.Land;
            parts.Add(BuiltSite.Line("jetty", [Edge(b), end]));
            parts.Add(BuiltSite.Sign("boat", Edge(b) + b.Way * (length * 0.4) + b.Way.Left * site.M(2.6), Heading(b.Way)));
            if (piers == 0) parts.Add(BuiltSite.Sign("ship", end - b.Way * site.M(9) - b.Way.Left * site.M(5.5), Heading(b.Way)));
            else if (roll.Chance(0.6)) parts.Add(BuiltSite.Sign("boat", end - b.Way * site.M(3) - b.Way.Left * site.M(2.6), Heading(b.Way) + Math.PI));
            if (piers == 0 && !Manners.Old(site.Manner)) parts.Add(BuiltSite.Sign("crane", Edge(b) - b.Way * site.M(1) + b.Way.Left * site.M(4), Heading(b.Way)));
            piers++;
        }
        foreach (var b in new[] { front[0], front[^1] })
        {
            var at = b.Land - b.Way * site.M(StoreBackM);
            if (!site.Buildable(at) || !Room(site, taken, at, site.M(StoreLongM / 2)) || Settlements.FromStreets(streets, at) < site.M(StoreDeepM)) continue;
            parts.Add(BuiltSite.Block("warehouse", at, b.Way.Left, site.M(StoreLongM), site.M(StoreDeepM)));
            parts.Add(BuiltSite.Sign(roll.Chance(0.5) ? "barrels" : "sacks", b.Land - b.Way * site.M(2.5) + b.Way.Left * site.M(roll.Between(-4, 4)), Heading(b.Way)));
            taken.Add((at, site.M(StoreLongM / 2)));
        }
        parts.Add(BuiltSite.Sign("nets", middle.Land - middle.Way * site.M(3) + middle.Way.Left * site.M(7), Heading(middle.Way.Left)));
        if (within < site.M(MoleFromM)) return;
        Spot root = Edge(front[^1]), seaward = root + front[^1].Way * site.M(MoleOutM), hook = seaward + (Edge(front[0]) - root).Unit * site.M(MoleHookM);
        if (!site.Sea(seaward) || !site.Sea(hook)) return;
        parts.Add(BuiltSite.Line("mole", [root, seaward, hook]));
        parts.Add(BuiltSite.Sign("beacon", hook));
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
        if (banks.Count == 0) return;
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
