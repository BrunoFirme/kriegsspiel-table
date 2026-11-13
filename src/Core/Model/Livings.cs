namespace KTable.Core.Model;

static class Livings
{
    const double BankReach = 1.4, HutLongM = 6.5, HutDeepM = 5, HutBackM = 9, HutsApartM = 13, HutRoomM = 3.6, MillLongM = 14, MillDeepM = 9, MillBackM = 8, FerryLeastM = 22, BridgeNearM = 180;
    const double TradeReachM = 70, OsierLongM = 18, OsierDeepM = 9, FewestForTrades = 12, LandingM = 5;
    const double QuayOutM = 3, PierLeastM = 16, PierMostM = 38, MoleFromM = 150, MoleOutM = 45, MoleHookM = 40, StoreLongM = 16, StoreDeepM = 9, StoreBackM = 13, FrontShare = 0.45, PiersApartM = 22;
    const double FieldLaneM = 45, CoveLeastM = 8, MeadowLongM = 44, MeadowDeepM = 20, SlipOutM = 14, DrainLeastM = 12, RopewalkM = 54, RopewalkBackM = 16, TollOutM = 13, LodgeLongM = 11, LodgeDeepM =
        8, HeadlandRiseM = 4, InTheWood = 0.55, WoodBackM = 8, DrainM =
        36, QuarryLongM = 26, QuarryDeepM = 15, PansLongM = 24, PansDeepM = 13;
    const int MostHuts = 7, PortFromBlocks = 14, MostPiers = 3, MostWoodHuts = 3, WoodBearings = 12, CoveSpan = 3;

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    public static void Lay(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var land = Surroundings.Of(site, centre, within);
        if (land.Banks.Count > 0 && land.Sea) Harbour(site, land, centre, within, streets, parts, taken);
        else if (land.Banks.Count > 0) Waterside(site, land, centre, within, streets, parts, taken);
        Woodland(site, land, centre, within, streets, parts, taken);
        Wetland(site, land, centre, within, parts);
        Stony(site, land, centre, within, streets, parts, taken);
        if (land.Sea) Sandy(site, land, centre, within, parts, taken);
        Tillage(site, land, within, streets, parts, taken);
    }

    static void Tillage(BuiltSite site, Surroundings land, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Nearest(land.Fields, within) is not { } edge || streets.Count == 0) return;
        var (from, _, reach) = Settlements.Nearest(streets, edge.At);
        var into = edge.At + edge.Way * site.M(FieldLaneM);
        if (reach < site.M(4) || reach > within * BankReach || site.KindAt(into) != GroundKinds.Field || site.WaterCrossing(from, into) != null) return;
        parts.Add(BuiltSite.Line("track", [from, edge.At, into]));
        var barn = edge.At - edge.Way * site.M(10) + edge.Way.Left * site.M(11);
        if (!Room(site, taken, barn, site.M(8)) || Settlements.FromStreets(streets, barn) < site.M(6)) return;
        parts.Add(BuiltSite.Block("barn", barn, edge.Way, site.M(14), site.M(8)));
        taken.Add((barn, site.M(8)));
    }

    static int Sheltered(BuiltSite site, List<Surroundings.Bank> banks)
    {
        int best = banks.IndexOf(banks.MinBy(b => b.Reach)!);
        double most = site.M(CoveLeastM);
        for (int i = CoveSpan; i + CoveSpan < banks.Count; i++)
        {
            if (banks[i + CoveSpan].Bearing - banks[i - CoveSpan].Bearing != 2 * CoveSpan) continue;
            var chord = (banks[i - CoveSpan].Land + banks[i + CoveSpan].Land) * 0.5 - banks[i].Land;
            double cove = chord.X * banks[i].Way.X + chord.Y * banks[i].Way.Y;
            if (cove <= most) continue;
            most = cove;
            best = i;
        }
        return best;
    }

    static Surroundings.Edge? Nearest(IReadOnlyList<Surroundings.Edge> edges, double within) => edges.Where(e => e.Reach <= within * BankReach).MinBy(e => e.Reach);

    static double Heading(Spot way) => Math.Atan2(way.Y, way.X);

    static void Wetland(BuiltSite site, Surroundings land, Spot centre, double within, List<BuiltPart> parts)
    {
        if (Nearest(land.Marshes, within) is not { } edge) return;
        var roll = BuiltSite.Roll(centre, 171);
        parts.Add(BuiltSite.Sign("peat", edge.At + edge.Way * site.M(7) + edge.Way.Left * site.M(roll.Between(-6, 6)), Heading(edge.Way.Left)));
        parts.Add(BuiltSite.Sign("reeds", edge.At - edge.Way * site.M(4) + edge.Way.Left * site.M(8), Heading(edge.Way)));
        var pond = edge.At - edge.Way * site.M(11) - edge.Way.Left * site.M(9);
        if (site.Manner != Manners.Egyptian && site.Buildable(pond))
        {
            parts.Add(BuiltSite.Area("pond", Enumerable.Range(0, 9).Select(i => pond + Spot.Toward(i * 2 * Math.PI / 9) * site.M(4 + roll.Between(0, 1.6)))));
            parts.Add(BuiltSite.Sign("geese", pond + edge.Way * site.M(6), Heading(edge.Way)));
        }
        var far = edge.At + edge.Way * site.M(DrainM);
        if (site.KindAt(far) != GroundKinds.Marsh) return;
        if (site.Manner != Manners.Tribal) parts.Add(BuiltSite.Line("causeway", [edge.At - edge.Way * site.M(5) + edge.Way.Left * site.M(14), far + edge.Way.Left * site.M(14)]));
        if (Manners.Old(site.Manner)) return;
        double run = 0;
        while (run < DrainM && site.KindAt(edge.At + edge.Way * site.M(run + 3)) == GroundKinds.Marsh && !site.OnWay(edge.At + edge.Way * site.M(run + 3), site.M(4))) run += 3;
        if (run < DrainLeastM) return;
        parts.Add(BuiltSite.Line("ditch", [edge.At - edge.Way * site.M(3), edge.At + edge.Way * site.M(run)]));
        parts.Add(BuiltSite.Sign("windpump", edge.At + edge.Way * site.M(run + 3)));
    }

    static void Stony(BuiltSite site, Surroundings land, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Nearest(land.Rocks, within) is not { } edge || site.Manner == Manners.Tribal) return;
        Spot pit = edge.At + edge.Way * site.M(QuarryDeepM / 2 + 2), along = edge.Way.Left * (site.M(QuarryLongM) / 2), deep = edge.Way * (site.M(QuarryDeepM) / 2);
        if (site.KindAt(pit) != GroundKinds.Rock) return;
        parts.Add(BuiltSite.Area("quarry", [pit - along - deep, pit + along - deep, pit + along * 0.8 + deep, pit - along * 0.7 + deep]));
        parts.Add(BuiltSite.Sign("blocks", edge.At - edge.Way * site.M(4) + edge.Way.Left * site.M(3), Heading(edge.Way.Left)));
        if (streets.Count > 0 && Settlements.Nearest(streets, edge.At) is var (from, _, reach) && reach > site.M(6)
            && reach < within) parts.Add(BuiltSite.Line("track", [from, edge.At - edge.Way * site.M(2)]));
        if (BuiltSite.Roll(centre, 211).Chance(0.6)) parts.Add(BuiltSite.Sign("adit", edge.At + edge.Way * site.M(1) + edge.Way.Left * site.M(QuarryLongM / 2 + 7), Heading(edge.Way)));
        var kiln = edge.At - edge.Way * site.M(9) - edge.Way.Left * site.M(9);
        if (!Room(site, taken, kiln, site.M(3)) || Settlements.FromStreets(streets, kiln) < site.M(5)) return;
        parts.Add(BuiltSite.Block("kiln", kiln, edge.Way.Left, site.M(4.5), site.M(4.5)));
        taken.Add((kiln, site.M(3)));
    }

    static void Sandy(BuiltSite site, Surroundings land, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var edges = land.Sands.Where(e => e.Reach <= within * BankReach).OrderBy(e => e.Reach).ToList();
        if (edges.Count == 0) return;
        var roll = BuiltSite.Roll(centre, 191);
        parts.Add(BuiltSite.Line("fence", [edges[0].At - edges[0].Way.Left * site.M(roll.Between(8, 14)), edges[0].At + edges[0].Way.Left * site.M(roll.Between(8, 14))]));
        if (site.Manner == Manners.Tribal) return;
        foreach (var edge in edges)
        {
            Spot along = edge.Way.Left * (site.M(PansLongM) / 2), deep = edge.Way * (site.M(PansDeepM) / 2), pans = edge.At + edge.Way * site.M(PansDeepM / 2 + 3);
            Spot[] ring = [pans - along - deep, pans + along - deep, pans + along + deep, pans - along + deep];
            if (ring.Any(p => site.KindAt(p) != GroundKinds.Sand) || taken.Any(o => (o.At - pans).Length < o.Reach + site.M(PansLongM / 2))) continue;
            parts.Add(BuiltSite.Area("saltpans", ring));
            taken.Add((pans, site.M(PansLongM / 2)));
            return;
        }
    }

    static void Woodland(BuiltSite site, Surroundings land, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        bool inside = land.Wooded > InTheWood;
        var spots = inside
            ? Enumerable.Range(0, WoodBearings).Select(i => (At: centre + Spot.Toward(i * 2 * Math.PI / WoodBearings + 0.4) * (within * 0.9), Way: Spot.Toward(i * 2 * Math.PI / WoodBearings + 0.4)))
                .ToList()
            : land.Woods.Where(e => e.Reach <= within * BankReach).Select(e => (At: e.At - e.Way * site.M(WoodBackM), e.Way)).ToList();
        if (spots.Count == 0) return;
        var roll = BuiltSite.Roll(centre, 151);
        int from = roll.Below(spots.Count), huts = 0, stride = Math.Max(1, spots.Count / MostWoodHuts);
        for (int k = 0; k < spots.Count && huts < MostWoodHuts; k += stride)
        {
            var (at, way) = spots[(from + k) % spots.Count];
            if (!Room(site, taken, at, site.M(HutRoomM)) || Settlements.FromStreets(streets, at) < site.M(HutLongM)) continue;
            double turn = Math.Atan2(way.Y, way.X);
            bool lodge = huts == 2 && parts.Count(p => p.Kind == PartKinds.Block) >= PortFromBlocks;
            parts.Add(BuiltSite.Block(lodge ? "house" : "hut", at, way.Left, site.M(lodge ? LodgeLongM : HutLongM), site.M(lodge ? LodgeDeepM : HutDeepM)));
            parts.Add(BuiltSite.Sign("logs", at + way.Left * site.M(HutLongM / 2 + 3) + way * site.M(1), turn + roll.Between(-0.4, 0.4)));
            if (huts == 0) parts.Add(BuiltSite.Sign("sawpit", at - way.Left * site.M(HutLongM / 2 + 4.5), turn));
            else if (site.Manner != Manners.Egyptian) parts.Add(BuiltSite.Sign("clamp", at + way * site.M(1.5) - way.Left * site.M(HutLongM / 2 + 4), turn));
            if (huts == 1 && site.Manner != Manners.Egyptian) parts.Add(BuiltSite.Sign("pigs", at + way * site.M(5) + way.Left * site.M(2), turn));
            taken.Add((at, site.M(HutRoomM)));
            huts++;
        }
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
        var front = port ? Front(banks, Sheltered(site, banks), within * FrontShare) : [];
        if (front.Count >= 3) Port(site, roll, front, within, streets, parts, taken);
        Fishing(site, roll, front.Count >= 3 ? banks.Except(front).ToList() : banks, within, streets, parts, taken);
    }

    static void Port(BuiltSite site, Dice roll, List<Surroundings.Bank> front, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        Spot Edge(Surroundings.Bank b) => b.Land + b.Way * site.M(QuayOutM);
        var shore = front.Select(Edge).ToList();
        parts.Add(BuiltSite.Line("quay", shore.Select((p, i) => i == 0 || i == shore.Count - 1 ? p : (shore[i - 1] + p * 2 + shore[i + 1]) * 0.25)));
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
        if (site.Manner == Manners.Egyptian) parts.Add(BuiltSite.Sign("steps", Edge(middle) + middle.Way.Left * site.M(9), Heading(middle.Way)));
        foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Sign("stall", middle.Land - middle.Way * site.M(4) - middle.Way.Left * site.M(5 + 4 * side), Heading(middle.Way.Left)));
        var last = within < site.M(MoleFromM) ? front[^1] : front[0];
        var slip = Edge(last) + (Edge(last) - Edge(middle)).Unit * site.M(SlipOutM) + last.Way * site.M(3);
        if (site.Sea(slip + last.Way * site.M(8))) parts.Add(BuiltSite.Sign("slip", slip, Heading(last.Way)));
        Spot? Stand(double reach, double off, params double[] backs) => backs.SelectMany(back => front.Select(b => b.Land - b.Way * site.M(back))).Cast<Spot?>()
            .FirstOrDefault(at => site.Buildable(at!.Value) && Room(site, taken, at.Value, reach) && Settlements.FromStreets(streets, at.Value) > off);
        if (Stand(site.M(HutRoomM), site.M(HutLongM), 8, 12) is { } loft)
        {
            parts.Add(BuiltSite.Block("fisher", loft, middle.Way.Left, site.M(HutLongM), site.M(HutDeepM)));
            parts.Add(BuiltSite.Sign("nets", loft + middle.Way.Left * site.M(HutLongM / 2 + 4), Heading(middle.Way.Left)));
            taken.Add((loft, site.M(HutRoomM)));
        }
        if (Stand(site.M(6), site.M(6), StoreBackM, StoreBackM + 8, StoreBackM + 16) is { } house)
        {
            parts.Add(BuiltSite.Block("customs", house, middle.Way.Left, site.M(12), site.M(9)));
            taken.Add((house, site.M(6)));
        }
        if (!Manners.Old(site.Manner) && Stand(site.M(RopewalkM / 3), site.M(5), StoreBackM + RopewalkBackM, StoreBackM + RopewalkBackM + 10, StoreBackM + RopewalkBackM + 20) is { } walk)
        {
            parts.Add(BuiltSite.Block("ropewalk", walk, middle.Way.Left, site.M(RopewalkM), site.M(4.5)));
            taken.Add((walk, site.M(RopewalkM / 3)));
        }
        var high = front.MaxBy(b => site.HeightAt(b.Land))!;
        bool headland = site.HeightAt(high.Land) - front.Min(b => site.HeightAt(b.Land)) > site.M(HeadlandRiseM);
        if (headland) parts.Add(BuiltSite.Sign("beacon", high.Land - high.Way * site.M(6)));
        if (headland || within < site.M(MoleFromM)) return;
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
        if (land.River && manner != Manners.Tribal) Toll(site, centre, within, parts, taken);
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
        if (manner == Manners.Egyptian) parts.Add(BuiltSite.Sign("steps", banks.MinBy(b => b.Reach)!.Land, Heading(banks.MinBy(b => b.Reach)!.Way)));
        if (!land.River || manner is Manners.Egyptian or Manners.Tribal) return;
        int first = BuiltSite.Roll(centre, 231).Below(banks.Count);
        for (int k = 0; k < banks.Count; k++)
        {
            var by = banks[(first + k) % banks.Count];
            Spot mead = by.Land - by.Way * site.M(MeadowDeepM / 2 + 5), run = by.Way.Left * (site.M(MeadowLongM) / 2), back = by.Way * (site.M(MeadowDeepM) / 2);
            if (!site.Buildable(mead) || taken.Any(o => (o.At - mead).Length < o.Reach + site.M(MeadowDeepM / 2)) || Settlements.FromStreets(streets, mead) < site.M(MeadowDeepM / 2)) continue;
            parts.Add(BuiltSite.Area("meadow", [mead - run - back, mead + run - back, mead + run + back, mead - run + back]));
            taken.Add((mead, site.M(MeadowLongM / 2)));
            return;
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

    static void Toll(BuiltSite site, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var bridge = parts.Concat(site.Others.SelectMany(o => o.Parts)).Where(p => p.Kind == PartKinds.Sign && p.Class == "bridge")
            .Select(p => (At: Spot.Of(BuiltGeometry.Points(p.At)[0]), Across: Spot.Toward(p.Turn / 10.0 * Math.PI / 180))).Where(b => (b.At - centre).Length < within * BankReach)
            .OrderBy(b => (b.At - centre).Length).FirstOrDefault();
        if (bridge.Across.Length <= 0) return;
        var toward = (centre - bridge.At).X * bridge.Across.X + (centre - bridge.At).Y * bridge.Across.Y >= 0 ? bridge.Across : bridge.Across * -1;
        var at = bridge.At + toward * site.M(TollOutM) + toward.Left * site.M(7);
        if (!site.Buildable(at) || !Room(site, taken, at, site.M(HutRoomM))) return;
        parts.Add(BuiltSite.Block("hut", at, toward, site.M(HutLongM), site.M(HutDeepM)));
        parts.Add(BuiltSite.Sign("signboard", at - toward.Left * site.M(HutDeepM / 2 + 1), Heading(toward)));
        taken.Add((at, site.M(HutRoomM)));
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
