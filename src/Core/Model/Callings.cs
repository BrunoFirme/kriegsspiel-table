namespace KTable.Core.Model;

static class Callings
{
    const double SlopeThatCounts = 0.01;
    const double RuralBelow = 0.8, CoopShare = 0.24, StyShare = 0.16, HeapShare = 0.2, DogShare = 0.08, BenchShare = 0.14, WashingShare = 0.12, JarsShare = 0.34, OvenShare = 0.25;
    const double CoopLongM = 2.2, CoopDeepM = 1.6, StyLongM = 3, StyDeepM = 2, PenLongM = 4, PenDeepM = 3, YardBackM = 6, GoodsOffM = 1.1, CoopRoomM = 1.3, StyRoomM = 2.4;
    const double TradesPerHouse = 1.0 / 8, BathsLongM = 26, BathsDeepM = 14, ArenaLongM = 58, ArenaDeepM = 44, ArenaAt = 1.25, TownFromM = 200;
    const double BarracksLongM = 28, BarracksDeepM = 9, ParadeLongM = 32, ParadeDeepM = 24, ArsenalLongM = 18, ArsenalDeepM = 11, MagazineLongM = 8, MagazineDeepM = 6;
    const double ThreshingM = 6.5, VineyardLongM = 34, VineyardDeepM = 24, KilnM = 4.5, EdgeAt = 1.08, PaddockLongM = 16, PaddockDeepM = 12, DovecoteM = 4.2, SiloM = 3.2;
    const int FewestForTrades = 8, MostTrades = 12, Bearings = 12, ArenaSteps = 28, ThreshingSteps = 14;

    static readonly string[] Later = ["shop", "bakery", "tavern", "cooper", "wheelwright", "shop", "potter", "weaver", "butcher", "chandler"];
    static readonly string[] Roman = ["taberna", "bakery", "tavern", "fuller", "taberna", "potter", "smith"];
    static readonly string[] Egyptian = ["bakery", "brewer", "potter", "weaver", "shop"];

    public static (string Class, double WideM, double DeepM) Home(int manner, double town, Dice roll)
    {
        double pick = roll.Next(), size = roll.Next();
        switch (manner)
        {
            case Manners.Roman when town > 0.4:
                return pick < 0.22 ? ("insula", 14 + size * 6, 16 + size * 8) : pick < 0.46 ? ("domus", 11 + size * 4, 20 + size * 8) : pick < 0.68 ? ("taberna", 4 + size * 2, 6 + size * 2) : ("house", 0, 0);
            case Manners.Roman:
                return pick < 0.18 ? ("domus", 11 + size * 4, 19 + size * 7) : ("house", 0, 0);
            case Manners.Egyptian:
                return pick < 0.1 ? ("villa", 14 + size * 4, 14 + size * 4) : ("house", 6 + size * 4, 7 + size * 5);
            case Manners.Tribal:
                return pick < 0.1 ? ("longhouse", 7, 14 + size * 5) : ("roundhouse", 6 + size * 4.5, 6 + size * 4.5);
            case Manners.Southern when town < 0.5 && pick < 0.12:
                return ("villa", 14 + size * 4, 14 + size * 5);
            case Manners.Northern:
                return ("house", 7 + size * 3, 6 + size * 2.5);
            case Manners.English or Manners.Continental or Manners.Southern when town > 0.5 && pick < 0.14:
                return ("shop", 0, 0);
        }
        return ("house", 0, 0);
    }

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    public static void Yard(BuiltSite site, Spot at, Spot facing, Spot back, double wide, double deep, double houses, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(at, 31);
        int manner = site.Manner;
        bool old = Manners.Old(manner);
        if (old && roll.Chance(JarsShare)) parts.Add(BuiltSite.Sign("jars", at - back * (deep / 2 + site.M(0.7)) + facing * (wide * 0.3 * (roll.Chance(0.5) ? 1 : -1))));
        if (!old && roll.Chance(BenchShare)) parts.Add(BuiltSite.Sign("bench", at - back * (deep / 2 + site.M(0.6)) - facing * (wide * 0.28), Math.Atan2(facing.Y, facing.X)));
        if (houses >= RuralBelow) return;
        double rural = 1 - houses / RuralBelow;
        var behind = at + back * (deep / 2 + site.M(YardBackM));
        if (roll.Chance(CoopShare + 0.2 * rural) && Room(site, taken, behind + facing * (wide * 0.45), site.M(CoopRoomM)))
        {
            var coop = behind + facing * (wide * 0.45);
            parts.Add(BuiltSite.Block("coop", coop, facing, site.M(CoopLongM), site.M(CoopDeepM)));
            parts.Add(BuiltSite.Sign("hens", coop - back * site.M(2.2), roll.Between(0, 2 * Math.PI)));
            taken.Add((coop, site.M(CoopRoomM)));
        }
        if (manner != Manners.Egyptian && roll.Chance(StyShare + 0.2 * rural) && Room(site, taken, behind - facing * (wide * 0.5), site.M(StyRoomM)))
        {
            var sty = behind - facing * (wide * 0.5);
            Spot along = facing * (site.M(PenLongM) / 2), across = back * (site.M(PenDeepM) / 2), pen = sty + back * site.M(StyDeepM / 2 + PenDeepM / 2);
            parts.Add(BuiltSite.Block("pigsty", sty, facing, site.M(StyLongM), site.M(StyDeepM)));
            parts.Add(BuiltSite.Area("pen", [pen - along - across, pen + along - across, pen + along + across, pen - along + across]));
            parts.Add(BuiltSite.Line("fence", [pen - along - across, pen - along + across, pen + along + across, pen + along - across]));
            parts.Add(BuiltSite.Sign("pigs", pen, roll.Between(0, 2 * Math.PI)));
            taken.Add((sty + back * site.M(PenDeepM / 2), site.M(StyRoomM)));
        }
        if (roll.Chance(HeapShare * (0.4 + rural))) parts.Add(BuiltSite.Sign("dungheap", behind + back * site.M(3) + facing * (wide * roll.Between(-0.2, 0.2))));
        if (roll.Chance(DogShare)) parts.Add(BuiltSite.Sign("dog", at - back * (deep / 2 + site.M(1.4)) + facing * (wide * 0.2), roll.Between(0, 2 * Math.PI)));
        if (roll.Chance(WashingShare) && !old) parts.Add(BuiltSite.Sign("washing", at + back * (deep / 2 + site.M(2.2)), Math.Atan2(facing.Y, facing.X)));
        if (manner == Manners.Egyptian && roll.Chance(OvenShare)) parts.Add(BuiltSite.Sign("oven", at + back * (deep / 2 + site.M(1.6)) + facing * (wide * 0.25)));
    }

    static IEnumerable<BuiltPart> Goods(BuiltSite site, string trade, Spot front, Spot along, Spot outward, Spot rear)
    {
        double turn = Math.Atan2(along.Y, along.X);
        Spot before = front + outward * site.M(GoodsOffM);
        switch (trade)
        {
            case "cooper" or "brewer":
                yield return BuiltSite.Sign("barrels", before + along * site.M(1.5), turn);
                yield return BuiltSite.Sign("barrels", before - along * site.M(1.2), turn + 1);
                break;
            case "wheelwright":
                yield return BuiltSite.Sign("wheels", before + along * site.M(1.6), turn);
                yield return BuiltSite.Sign("cart", rear, turn + 0.4);
                break;
            case "potter":
                yield return BuiltSite.Block("kiln", rear, along, site.M(2.6), site.M(2.6));
                yield return BuiltSite.Sign("jars", before + along * site.M(1.4), turn);
                break;
            case "weaver" or "fuller":
                yield return BuiltSite.Sign(trade == "fuller" ? "vats" : "racks", rear, turn);
                break;
            case "tavern":
                yield return BuiltSite.Sign("signboard", before + along * site.M(2));
                yield return BuiltSite.Sign("bench", before - along * site.M(1.5), turn);
                yield return BuiltSite.Sign("barrels", before - along * site.M(3.2), turn);
                break;
            case "bakery":
                yield return BuiltSite.Sign("sacks", before + along * site.M(1.6), turn);
                break;
            case "butcher" or "chandler" or "shop" or "taberna":
                yield return BuiltSite.Sign("crates", before + along * site.M(2.2), turn);
                break;
            case "smith":
                yield return BuiltSite.Sign("anvil", before + along * site.M(1.4));
                break;
        }
    }

    public static void Trades(BuiltSite site, Spot centre, List<BuiltPart> parts)
    {
        var homes = parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "taberna" && p.At.Count == 8).OrderBy(p => (BuiltSite.Round(p).At - centre).Length).ToList();
        if (homes.Count < FewestForTrades || site.Manner == Manners.Tribal) return;
        var kinds = site.Manner == Manners.Roman ? Roman : site.Manner == Manners.Egyptian ? Egyptian : Later;
        int wanted = Math.Min(MostTrades, (int)(homes.Count * TradesPerHouse));
        for (int i = 0; i < wanted; i++)
        {
            var home = homes[i * 2];
            var points = BuiltGeometry.Points(home.At).Select(Spot.Of).ToList();
            var roll = BuiltSite.Roll(points[0], 47);
            string trade = kinds[roll.Below(kinds.Length)];
            Spot front = (points[0] + points[1]) * 0.5, along = (points[1] - points[0]).Unit, outward = (points[0] - points[3]).Unit;
            home.Class = trade == "smith" ? "smithy" : trade;
            parts.AddRange(Goods(site, trade, front, along, outward, (points[2] + points[3]) * 0.5 - outward * site.M(2.6)));
        }
    }

    static IEnumerable<Spot> Ellipse(Spot at, Spot along, double longR, double deepR, int steps) =>
        Enumerable.Range(0, steps).Select(i => at + along * (Math.Cos(i * 2 * Math.PI / steps) * longR) + along.Left * (Math.Sin(i * 2 * Math.PI / steps) * deepR));

    static Spot? Found(BuiltSite site, Dice roll, Spot centre, double from, double to, double reach, List<(Spot At, double Reach)> taken, List<Settlements.Street> streets,
        Func<Spot, double>? liked = null)
    {
        double turn = roll.Between(0, 2 * Math.PI), most = 0;
        Spot? best = null;
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < Bearings; i++)
            {
                var at = centre + Spot.Toward(turn + i * 2 * Math.PI / Bearings) * Settlements.Lerp(from, to, ring / 2.0);
                if (!Room(site, taken, at, reach) || Settlements.FromStreets(streets, at) <= reach * 0.8) continue;
                if (liked == null) return at;
                double worth = liked(at);
                if (best != null && worth <= most + SlopeThatCounts) continue;
                most = worth;
                best = at;
            }
        return best;
    }

    static double Sunward(BuiltSite site, Spot at) => site.Downhill(at).Y;

    public static void Works(BuiltSite site, Spot centre, double within, double walls, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(centre, 59);
        if (site.Manner == Manners.Roman && within >= site.M(TownFromM))
        {
            if (Found(site, roll, centre, within * 0.3, within * 0.7, site.M(BathsLongM / 2), taken, streets) is { } baths)
            {
                parts.Add(BuiltSite.Block("baths", baths, Spot.Toward(roll.Between(0, Math.PI)), site.M(BathsLongM), site.M(BathsDeepM)));
                taken.Add((baths, site.M(BathsLongM / 2)));
            }
            if (Found(site, roll, centre, within * ArenaAt, within * (ArenaAt + 0.25), site.M(ArenaLongM / 2), taken, streets) is { } arena)
            {
                parts.Add(BuiltSite.Area("arena", Ellipse(arena, Spot.Toward(roll.Between(0, Math.PI)), site.M(ArenaLongM / 2), site.M(ArenaDeepM / 2), ArenaSteps)));
                taken.Add((arena, site.M(ArenaLongM / 2)));
            }
        }
        if (Manners.Old(site.Manner) || walls < Settlements.BastionedFrom) return;
        if (Found(site, roll, centre, within * 0.45, within * 0.8, site.M(ParadeLongM * 0.7), taken, streets) is not { } ground) return;
        Spot along = Spot.Toward(roll.Between(0, Math.PI)), aside = along.Left;
        Spot half = along * (site.M(ParadeLongM) / 2), deep = aside * (site.M(ParadeDeepM) / 2);
        parts.Add(BuiltSite.Area("parade", [ground - half - deep, ground + half - deep, ground + half + deep, ground - half + deep]));
        foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Block("barracks", ground
            + aside * (side * site.M(ParadeDeepM / 2 + BarracksDeepM / 2 + 1.5)), along, site.M(BarracksLongM), site.M(BarracksDeepM)));
        parts.Add(BuiltSite.Block("arsenal", ground + along * site.M(ParadeLongM / 2 + ArsenalDeepM / 2 + 2), aside, site.M(ArsenalLongM), site.M(ArsenalDeepM)));
        parts.Add(BuiltSite.Block("magazine", ground - along * site.M(ParadeLongM / 2 + MagazineDeepM / 2 + 4), aside, site.M(MagazineLongM), site.M(MagazineDeepM)));
        parts.Add(BuiltSite.Sign("flag", ground));
        parts.Add(BuiltSite.Sign("shot", ground + along * site.M(ParadeLongM * 0.36) + aside * site.M(4)));
        parts.Add(BuiltSite.Sign("gun", ground + along * site.M(ParadeLongM * 0.36) - aside * site.M(3), Math.Atan2(-along.Y, -along.X)));
        parts.Add(BuiltSite.Sign("gun", ground + along * site.M(ParadeLongM * 0.36) - aside * site.M(7), Math.Atan2(-along.Y, -along.X)));
        taken.Add((ground, site.M(ParadeLongM)));
    }

    public static string Barn(int manner, Dice roll)
    {
        double pick = roll.Next();
        return manner switch
        {
            Manners.Tribal => "granary",
            Manners.Roman => pick < 0.5 ? "granary" : pick < 0.8 ? "barn" : "stable",
            _ => pick < 0.42 ? "barn" : pick < 0.58 ? "stable" : pick < 0.74 ? "byre" : pick < 0.86 ? "granary" : "cartshed",
        };
    }

    public static void Stock(BuiltSite site, string barn, Spot at, Spot way, double length, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(at, 71);
        var aside = way.Left * (roll.Chance(0.5) ? 1 : -1);
        var paddock = at + aside * (length * 0.1 + site.M(PaddockDeepM / 2 + 11));
        if (barn != "cartshed" && roll.Chance(0.6) && Room(site, taken, paddock, site.M(PaddockDeepM / 2)))
        {
            Spot half = way * (site.M(PaddockLongM) / 2), deep = aside * (site.M(PaddockDeepM) / 2);
            Spot[] ring = [paddock - half - deep, paddock + half - deep, paddock + half + deep, paddock - half + deep];
            parts.Add(BuiltSite.Area("pen", ring));
            parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
            parts.Add(BuiltSite.Sign(barn == "stable" ? "horses" : barn == "byre" ? "cattle" : roll.Chance(0.5) ? "sheep" : "cattle", paddock, roll.Between(0, 2 * Math.PI)));
            parts.Add(BuiltSite.Sign("trough", paddock - deep * 0.8, Math.Atan2(way.Y, way.X)));
            taken.Add((paddock, site.M(PaddockLongM / 2)));
        }
        if (roll.Chance(0.4)) parts.Add(BuiltSite.Sign("dungheap", at - aside * site.M(7.5) + way * (length * roll.Between(-0.3, 0.3))));
        if (barn == "cartshed" || roll.Chance(0.25)) parts.Add(BuiltSite.Sign(roll.Chance(0.5) ? "cart" : "plough", at - aside * site.M(7) - way * (length * 0.3), roll.Between(0, 2 * Math.PI)));
        var cote = at - aside * site.M(11) + way * (length * 0.4);
        if (!Manners.Old(site.Manner) && roll.Chance(0.18) && Room(site, taken, cote, site.M(DovecoteM)))
        {
            parts.Add(BuiltSite.Block("dovecote", cote, way, site.M(DovecoteM), site.M(DovecoteM)));
            taken.Add((cote, site.M(DovecoteM)));
        }
    }

    public static void Country(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(centre, 83);
        int manner = site.Manner;
        bool warm = manner is Manners.Southern or Manners.Roman or Manners.Egyptian;
        if (warm && roll.Chance(0.65) && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.2), site.M(ThreshingM * 1.3), taken, streets) is { } floor)
        {
            parts.Add(BuiltSite.Area("threshing", Ellipse(floor, new Spot(1, 0), site.M(ThreshingM), site.M(ThreshingM), ThreshingSteps)));
            parts.Add(BuiltSite.Sign("stooks", floor + Spot.Toward(roll.Between(0, 6)) * site.M(ThreshingM + 2.5)));
            taken.Add((floor, site.M(ThreshingM * 1.3)));
        }
        if (manner is Manners.Southern or Manners.Roman && roll.Chance(0.55)
            && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.3), site.M(VineyardLongM * 0.6), taken, streets, at => Sunward(site, at)) is { } vines)
        {
            Spot along = (vines - centre).Unit.Left, half = along * (site.M(VineyardLongM) / 2), deep = along.Left * (site.M(VineyardDeepM) / 2);
            parts.Add(BuiltSite.Area("vineyard", [vines - half - deep, vines + half - deep, vines + half + deep, vines - half + deep]));
            taken.Add((vines, site.M(VineyardLongM * 0.6)));
        }
        if (manner != Manners.Tribal && manner != Manners.Egyptian && roll.Chance(0.3)
            && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.2), site.M(KilnM * 1.5), taken, streets) is { } kiln)
        {
            parts.Add(BuiltSite.Block("kiln", kiln, new Spot(1, 0), site.M(KilnM), site.M(KilnM)));
            parts.Add(BuiltSite.Sign("logs", kiln + Spot.Toward(roll.Between(0, 6)) * site.M(KilnM + 2), roll.Between(0, Math.PI)));
            taken.Add((kiln, site.M(KilnM * 1.5)));
        }
        if (manner == Manners.Tribal && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.25), site.M(3), taken, streets) is { } stone) parts.Add(BuiltSite.Sign("menhir", stone));
        if (manner is Manners.Egyptian or Manners.Northern && site.NearestWater(centre) is var (bank, reach) && reach < within * 1.3)
            parts.Add(BuiltSite.Sign("sweep", bank + (centre - bank).Unit * site.M(5), Math.Atan2(bank.Y - centre.Y, bank.X - centre.X)));
    }

    public static void Farmyard(BuiltSite site, Spot at, Spot front, double yard, List<BuiltPart> parts)
    {
        var roll = BuiltSite.Roll(at, 97);
        var side = front.Left;
        double turn = Math.Atan2(front.Y, front.X);
        Spot In(double ahead, double aside) => at + front * (yard * ahead) + side * (yard * aside);
        if (roll.Chance(0.6)) parts.Add(BuiltSite.Sign("dungheap", In(0.06, 0.05)));
        if (roll.Chance(0.4)) parts.Add(BuiltSite.Sign(site.Manner is Manners.Northern or Manners.Egyptian ? "sweep" : "well", In(-0.16, 0.14), turn));
        if (roll.Chance(0.55))
        {
            parts.Add(BuiltSite.Block("coop", In(0.4, -0.42), front, site.M(CoopLongM), site.M(CoopDeepM)));
            parts.Add(BuiltSite.Sign("hens", In(0.3, -0.3), roll.Between(0, 2 * Math.PI)));
        }
        if (roll.Chance(0.4) && site.Manner != Manners.Egyptian)
        {
            parts.Add(BuiltSite.Block("pigsty", In(-0.4, -0.42), front, site.M(StyLongM), site.M(StyDeepM)));
            parts.Add(BuiltSite.Sign("pigs", In(-0.32, -0.3), roll.Between(0, 2 * Math.PI)));
        }
        if (roll.Chance(0.16) && !Manners.Old(site.Manner)) parts.Add(BuiltSite.Block("dovecote", In(-0.4, 0.4), front, site.M(DovecoteM), site.M(DovecoteM)));
        if (roll.Chance(0.45)) parts.Add(BuiltSite.Sign(roll.Chance(0.5) ? "cart" : "plough", In(0.12, 0.22), roll.Between(0, 2 * Math.PI)));
        if (roll.Chance(0.3)) parts.Add(BuiltSite.Sign("dog", In(-0.1, -0.12), roll.Between(0, 2 * Math.PI)));
        if (roll.Chance(0.2)) parts.Add(BuiltSite.Sign("geese", In(0.2, -0.1), roll.Between(0, 2 * Math.PI)));
        var paddock = at + side * (yard / 2 + site.M(PaddockDeepM / 2 + 3));
        if (!roll.Chance(0.55) || !site.Free(paddock, site.M(PaddockLongM / 2))) return;
        Spot half = front * (site.M(PaddockLongM) / 2), deep = side * (site.M(PaddockDeepM) / 2);
        Spot[] ring = [paddock - half - deep, paddock + half - deep, paddock + half + deep, paddock - half + deep];
        parts.Add(BuiltSite.Area("pen", ring));
        parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
        parts.Add(BuiltSite.Sign(roll.Chance(0.4) ? "sheep" : roll.Chance(0.6) ? "cattle" : "horses", paddock, roll.Between(0, 2 * Math.PI)));
    }

    public static IEnumerable<BuiltPart> Silos(BuiltSite site, Spot at, Spot along, int count)
    {
        for (int i = 0; i < count; i++) yield return BuiltSite.Block("silo", at + along * ((i - (count - 1) / 2.0) * site.M(SiloM * 1.25)), along, site.M(SiloM), site.M(SiloM));
    }
}
