namespace KTable.Core.Model;

static class Callings
{
    const double SlopeThatCounts = 0.01;
    const double RuralBelow = 0.8, CoopShare = 0.24, StyShare = 0.16, HeapShare = 0.2, DogShare = 0.08, BenchShare = 0.14, WashingShare = 0.12, JarsShare = 0.34, OvenShare = 0.25;
    const double CoopLongM = 2.2, CoopDeepM = 1.6, StyLongM = 3, StyDeepM = 2, PenLongM = 4, PenDeepM = 3, YardBackM = 6, GoodsOffM = 1.1, CoopRoomM = 1.3, StyRoomM = 2.4;
    const double TradesPerHouse = 1.0 / 8, BathsLongM = 26, BathsDeepM = 14, ArenaLongM = 58, ArenaDeepM = 44, ArenaAt = 1.25, TownFromM = 200;
    const double BarracksLongM = 28, BarracksDeepM = 9, ParadeLongM = 32, ParadeDeepM = 24, ArsenalLongM = 18, ArsenalDeepM = 11, MagazineLongM = 8, MagazineDeepM = 6;
    const double ThreshingM = 6.5, VineyardLongM = 34, VineyardDeepM = 24, KilnM = 4.5, EdgeAt = 1.08, PaddockLongM = 16, PaddockDeepM = 12, DovecoteM = 4.2, SiloM = 3.2;
    const double QuarterM = 150, HanM = 34, SovietTownFrom = 0.2, FlatsFrom = 0.25, BarracksFrom = 0.04, BarracksApartM = 11, FlatsApartM = 7;
    const int MostMosques = 4, MostParishes = 3;
    const double ParishM = 240, ChapelM = 180, ParishGrand = 0.8;
    const double RichKeep = 0.75, PoorKeep = 0.8, RichScale = 1.25, PoorScale = 0.8, Squalor = 1.7, Comfort = 0.25, PoorYards = 0.6;

    internal static double Scale(double wealth) => Settlements.Lerp(PoorScale, RichScale, wealth);
    const double FriaryM = 260, CloisterM = 38, MinsterM = 30, CisternM = 14, ByzantineParishM = 150, ByzantineGrand = 0.55, RusParishM = 120, MadrasaM = 26, MinsterGrand = 1.25, FounderTombM = 9;
    const double SuqLongM = 44, SuqDeepM = 9, SuqWithin = 0.6, ManorFromM = 90, ManorM = 40, MoatWideM = 6, ManorHallM = 17, BarnInManorM = 18, GroveLongM = 60, GroveDeepM = 34;
    const double StoreShare = 0.65, StoreBackM = 4, StoreRoomM = 3, StoreLongM = 4.5, StoreDeepM = 4;
    const int RusParishesMore = 3;
    const int MostFriaries = 2;
    const int FewestForTrades = 8, MostTrades = 12, Bearings = 12, ArenaSteps = 28, ThreshingSteps = 14;

    static readonly string[] Later = ["shop", "bakery", "tavern", "cooper", "wheelwright", "shop", "potter", "weaver", "butcher", "chandler"];
    static readonly string[] Roman = ["taberna", "bakery", "tavern", "fuller", "taberna", "potter", "smith"];
    static readonly string[] Egyptian = ["bakery", "brewer", "potter", "weaver", "shop"];

    public static double GapM(BuiltSite site, string cls) => site.Manner != Manners.Soviet ? -1 : cls == "barracks" ? BarracksApartM : cls == "flats" ? FlatsApartM : -1;

    public static bool Gardened(string cls) => cls is not ("flats" or "barracks" or "public" or "insula");

    public const string TownHouse = "townhouse", Cottage = "cottage";

    public static (string Class, double WideM, double DeepM) Home(BuiltSite site, double town, Dice roll, double wealth = (Wealth.RichFrom + Wealth.PoorBelow) / 2)
    {
        var home = Ordinary(site, town, roll);
        double pick = roll.Next(), size = roll.Next();
        if (wealth >= Wealth.RichFrom && pick < (wealth - Wealth.RichFrom) / (1 - Wealth.RichFrom) * RichKeep && Rich(site.Manner, size) is { } rich) return rich;
        if (wealth <= Wealth.PoorBelow && pick < (Wealth.PoorBelow - wealth) / Wealth.PoorBelow * PoorKeep && Poor(site.Manner, size) is { } poor) return poor;
        double scale = Settlements.Lerp(PoorScale, RichScale, wealth);
        return home.WideM > 0 ? (home.Class, home.WideM * scale, home.DeepM * scale) : home;
    }

    static (string, double, double)? Rich(int manner, double size) => manner switch
    {
        Manners.Gothic or Manners.Continental or Manners.Northern or Manners.American or Manners.Western or Manners.Rus => (TownHouse, 9 + size * 4, 13 + size * 6),
        Manners.Roman => ("domus", 14 + size * 5, 22 + size * 8),
        Manners.Byzantine or Manners.Saracen or Manners.Persian or Manners.Ottoman or Manners.NorthAfrican or Manners.Egyptian => ("villa", 15 + size * 6, 15 + size * 6),
        Manners.Chinese => ("villa", 18 + size * 6, 20 + size * 6),
        Manners.Japanese or Manners.Nippon => ("villa", 16 + size * 5, 16 + size * 5),
        _ => null,
    };

    static (string, double, double)? Poor(int manner, double size) => manner switch
    {
        Manners.Gothic or Manners.Continental or Manners.Northern or Manners.American or Manners.Western or Manners.Rus or Manners.Soviet => (Cottage, 5 + size * 1.5, 5 + size * 2),
        Manners.Roman => size < 0.5 ? ("insula", 14 + size * 4, 16 + size * 4) : ("taberna", 4 + size * 1.5, 6 + size * 1.5),
        Manners.Tribal or Manners.Mongol or Manners.Pacific => null,
        _ => ("house", 4.5 + size * 1.5, 5 + size * 2),
    };

    static (string Class, double WideM, double DeepM) Ordinary(BuiltSite site, double town, Dice roll)
    {
        double pick = roll.Next(), size = roll.Next();
        int manner = site.Manner;
        bool urban = town > 0.45 || site.Town > 0.5;
        switch (manner)
        {
            case Manners.Gothic when urban && pick < 0.12:
                return ("shop", 5.5 + size * 2, 13 + size * 5);
            case Manners.Gothic when urban:
                return ("house", 5 + size * 2.5, 11 + size * 6);
            case Manners.Gothic when pick < 0.22:
                return ("longhouse", 6.5 + size, 15 + size * 6);
            case Manners.Gothic:
                return ("house", 6 + size * 2, 8 + size * 3);
            case Manners.Rus when urban && pick < 0.15:
                return ("house", 9 + size * 3, 10 + size * 4);
            case Manners.Rus:
                return ("house", 5.5 + size * 2, 6.5 + size * 2.5);
            case Manners.Byzantine when pick < 0.2:
                return ("villa", 11 + size * 4, 12 + size * 5);
            case Manners.Byzantine when urban && pick < 0.32:
                return ("shop", 5 + size * 2, 9 + size * 3);
            case Manners.Byzantine:
                return ("house", 6.5 + size * 2.5, 8 + size * 3);
            case Manners.Saracen when pick < 0.5:
                return ("villa", 12 + size * 7, 12 + size * 7);
            case Manners.Saracen:
                return ("house", 6 + size * 3, 7 + size * 3);
            case Manners.Mongol when pick < 0.55:
                return ("roundhouse", 5.5 + size * 2, 5.5 + size * 2);
            case Manners.Mongol:
                return ("house", 7 + size * 3, 8 + size * 3);
            case Manners.Chinese:
                return pick < 0.72 ? ("villa", 14 + size * 4, 15 + size * 5) : ("house", 8 + size * 3, 7 + size * 2);
            case Manners.Persian:
                return pick < 0.5 ? ("villa", 12 + size * 6, 12 + size * 6) : ("house", 7 + size * 3, 7 + size * 3);
            case Manners.Egyptian when pick < 0.05:
                return ("villa", 15 + size * 5, 15 + size * 5);
            case Manners.Egyptian:
                return ("house", 5 + size * 2, 9 + size * 2.5);
            case Manners.Ottoman when pick < 0.28:
            case Manners.NorthAfrican when pick < 0.5:
                return ("villa", 12 + size * 4, 12 + size * 4);
            case Manners.Ottoman:
                return ("house", 9 + size * 3, 8 + size * 2.5);
            case Manners.NorthAfrican:
                return ("house", 6 + size * 3, 6 + size * 3);
            case Manners.American when urban && pick < 0.22:
                return ("shop", 7 + size * 3, 16 + size * 6);
            case Manners.American:
                return ("house", 9 + size * 3.5, 7 + size * 2.5);
            case Manners.Japanese or Manners.Nippon when urban && pick < 0.1:
                return ("shed", 5 + size * 1.5, 6 + size * 2);
            case Manners.Japanese or Manners.Nippon when urban:
                return ("house", 5.2 + size * 1.6, 12 + size * 7);
            case Manners.Japanese or Manners.Nippon:
                return ("house", 10 + size * 4, 7 + size * 2);
            case Manners.Pacific:
                return ("house", 5 + size * 2.5, 5 + size * 2);
            case Manners.Western when urban && pick < 0.12:
                return ("insula", 17 + size * 7, 19 + size * 8);
            case Manners.Western when urban && pick < 0.82:
                return ("house", 5 + size * 1.4, 9 + size * 2);
            case Manners.Western when urban:
                return ("shop", 6 + size * 2, 11 + size * 3);
            case Manners.Soviet when site.Town > SovietTownFrom && town > FlatsFrom:
                return pick < 0.74 ? ("flats", 36 + size * 26, 12 + size * 2) : pick < 0.82 ? ("public", 22 + size * 10, 12) : ("house", 20 + size * 8, 10 + size * 2);
            case Manners.Soviet when site.Town > SovietTownFrom && town > BarracksFrom && pick < 0.82:
                return ("barracks", 9.5 + size * 1.5, 40 + size * 16);
            case Manners.Soviet:
                return ("house", 7 + size * 2.5, 6 + size * 2);
        }
        switch (Manners.Kin(manner))
        {
            case Manners.Roman when town > 0.4:
                return pick < 0.22 ? ("insula", 14 + size * 6, 16 + size * 8) : pick < 0.46 ? ("domus", 11 + size * 4, 20 + size * 8) : pick < 0.68 ? ("taberna", 4 + size * 2, 6 + size * 2) : ("house", 0, 0);
            case Manners.Roman:
                return pick < 0.18 ? ("domus", 11 + size * 4, 19 + size * 7) : ("house", 0, 0);
            case Manners.Egyptian:
                return pick < 0.1 ? ("villa", 14 + size * 4, 14 + size * 4) : ("house", 6 + size * 4, 7 + size * 5);
            case Manners.Tribal:
                return pick < 0.1 ? ("longhouse", 7, 14 + size * 5) : ("roundhouse", 6 + size * 4.5, 6 + size * 4.5);
            case Manners.Northern:
                return ("house", 7 + size * 3, 6 + size * 2.5);
            case Manners.Continental when town > 0.5 && pick < 0.14:
                return ("shop", 0, 0);
        }
        return ("house", 0, 0);
    }

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    public static void Yard(BuiltSite site, Spot at, Spot facing, Spot back, double wide, double deep, double houses, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(at, 31);
        int manner = site.Kin;
        bool old = Manners.Old(manner);
        if (old && roll.Chance(JarsShare)) parts.Add(BuiltSite.Sign("jars", at - back * (deep / 2 + site.M(0.7)) + facing * (wide * 0.3 * (roll.Chance(0.5) ? 1 : -1))));
        var store = at + back * (deep / 2 + site.M(StoreBackM));
        if (site.Manner == Manners.Rus && roll.Chance(StoreShare) && Room(site, taken, store, site.M(StoreRoomM)))
        {
            parts.Add(BuiltSite.Block("shed", store, facing, site.M(StoreLongM), site.M(StoreDeepM)));
            taken.Add((store, site.M(StoreRoomM)));
        }
        if (!old && roll.Chance(BenchShare)) parts.Add(BuiltSite.Sign("bench", at - back * (deep / 2 + site.M(0.6)) - facing * (wide * 0.28), Math.Atan2(facing.Y, facing.X)));
        double wealth = site.WealthAt(at), squalor = Settlements.Lerp(Squalor, Comfort, wealth);
        bool squalid = wealth <= Wealth.PoorBelow && !site.Modern;
        if (houses >= RuralBelow && !squalid || !roll.Chance(Math.Min(1, Math.Max(site.Farming * 1.25, squalid ? PoorYards : 0)))) return;
        double rural = (1 - houses / RuralBelow) * site.Farming;
        var behind = at + back * (deep / 2 + site.M(YardBackM));
        if (roll.Chance((CoopShare + 0.2 * rural) * squalor) && Room(site, taken, behind + facing * (wide * 0.45), site.M(CoopRoomM)))
        {
            var coop = behind + facing * (wide * 0.45);
            parts.Add(BuiltSite.Block("coop", coop, facing, site.M(CoopLongM), site.M(CoopDeepM)));
            parts.Add(BuiltSite.Sign("hens", coop - back * site.M(2.2), roll.Between(0, 2 * Math.PI)));
            taken.Add((coop, site.M(CoopRoomM)));
        }
        if (manner != Manners.Egyptian && site.Manner is not (Manners.Saracen or Manners.Mongol) && roll.Chance((StyShare + 0.2 * rural) * squalor)
            && Room(site, taken, behind - facing * (wide * 0.5), site.M(StyRoomM)))
        {
            var sty = behind - facing * (wide * 0.5);
            Spot along = facing * (site.M(PenLongM) / 2), across = back * (site.M(PenDeepM) / 2), pen = sty + back * site.M(StyDeepM / 2 + PenDeepM / 2);
            parts.Add(BuiltSite.Block("pigsty", sty, facing, site.M(StyLongM), site.M(StyDeepM)));
            parts.Add(BuiltSite.Area("pen", [pen - along - across, pen + along - across, pen + along + across, pen - along + across]));
            parts.Add(BuiltSite.Line("fence", [pen - along - across, pen - along + across, pen + along + across, pen + along - across]));
            parts.Add(BuiltSite.Sign("pigs", pen, roll.Between(0, 2 * Math.PI)));
            taken.Add((sty + back * site.M(PenDeepM / 2), site.M(StyRoomM)));
        }
        if (roll.Chance(HeapShare * (0.4 + rural) * squalor)) parts.Add(BuiltSite.Sign("dungheap", behind + back * site.M(3) + facing * (wide * roll.Between(-0.2, 0.2))));
        if (roll.Chance(DogShare * squalor)) parts.Add(BuiltSite.Sign("dog", at - back * (deep / 2 + site.M(1.4)) + facing * (wide * 0.2), roll.Between(0, 2 * Math.PI)));
        if (roll.Chance(WashingShare) && !old) parts.Add(BuiltSite.Sign("washing", at + back * (deep / 2 + site.M(2.2)), Math.Atan2(facing.Y, facing.X)));
        if ((manner == Manners.Egyptian || site.Manner is Manners.Saracen or Manners.Byzantine)
            && roll.Chance(OvenShare)) parts.Add(BuiltSite.Sign("oven", at + back * (deep / 2 + site.M(1.6)) + facing * (wide * 0.25)));
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
        if (homes.Count < FewestForTrades || site.Kin is Manners.Tribal or Manners.Mongol) return;
        var kinds = site.Kin == Manners.Roman ? Roman : site.Kin == Manners.Egyptian ? Egyptian : Later;
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

    internal static Spot? Found(BuiltSite site, Dice roll, Spot centre, double from, double to, double reach, List<(Spot At, double Reach)> taken, List<Settlements.Street> streets,
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

    static Spot Contoured(BuiltSite site, Spot at, Spot along) =>
        site.Downhill(at) is var fall && fall.Length >= Terraces.From && Slopes.Of(site.Manner).Fields != Terracing.None ? fall.Unit.Left * Math.Sign(fall.Unit.Left.Dot(along) + 1e-12) : along;

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
        if (site.Manner is Manners.Gothic or Manners.Rus or Manners.Byzantine && within >= site.M(TownFromM)) Cloisters(site, roll, centre, within, streets, parts, taken);
        if (site.Manner == Manners.Saracen && within >= site.M(TownFromM)) Suq(site, centre, within, streets, parts, taken);
        if (site.Manner == Manners.Gothic && within < site.M(TownFromM) && within >= site.M(ManorFromM)) Manor(site, roll, centre, within, streets, parts, taken);
        if (site.Manner == Manners.Byzantine && within >= site.M(TownFromM))
        {
            if (Found(site, roll, centre, within * 0.2, within * 0.6, site.M(BathsLongM / 2), taken, streets) is { } baths)
            {
                parts.Add(BuiltSite.Block("baths", baths, Spot.Toward(roll.Between(0, Math.PI)), site.M(BathsLongM * 0.7), site.M(BathsDeepM * 0.8)));
                taken.Add((baths, site.M(BathsLongM / 2)));
            }
            if (Found(site, roll, centre, within * 0.3, within * 0.8, site.M(CisternM), taken, streets) is { } cistern)
            {
                Spot wide = new Spot(site.M(CisternM) * 0.8, 0), across = new Spot(0, site.M(CisternM) * 0.6);
                parts.Add(BuiltSite.Area("pond", [cistern - wide - across, cistern + wide - across, cistern + wide + across, cistern - wide + across]));
                taken.Add((cistern, site.M(CisternM)));
            }
        }
        if (site.Manner is Manners.Ottoman or Manners.NorthAfrican or Manners.Persian or Manners.Saracen)
        {
            bool gates = site.Manner == Manners.Saracen;
            if (within >= site.M(TownFromM) && Found(site, roll, centre, within * (gates ? 0.72 : 0.2), within * (gates ? 0.95 : 0.7), site.M(HanM * 0.7), taken, streets) is { } han)
            {
                parts.Add(BuiltSite.Block("cloister", han, Spot.Toward(roll.Between(0, Math.PI)), site.M(HanM), site.M(HanM * 0.85)));
                taken.Add((han, site.M(HanM * 0.7)));
            }
            if (site.Manner == Manners.Saracen && within >= site.M(TownFromM) && Found(site, roll, centre, within * 0.15, within * 0.5, site.M(MadrasaM * 0.7), taken, streets) is { } madrasa)
            {
                parts.Add(BuiltSite.Block("cloister", madrasa, Spot.Toward(Settlements.Qibla), site.M(MadrasaM), site.M(MadrasaM)));
                parts.Add(BuiltSite.Block(Suburbs.Tomb, madrasa + Spot.Toward(Settlements.Qibla) * site.M(MadrasaM / 2 + FounderTombM / 2 + 1), Spot.Toward(Settlements.Qibla),
                    site.M(FounderTombM), site.M(FounderTombM)));
                taken.Add((madrasa, site.M(MadrasaM * 0.7 + FounderTombM)));
            }
            int mosques = site.Manner == Manners.Persian ? 0 : Math.Clamp((int)(within / site.M(QuarterM)), 0, MostMosques);
            for (int i = 0; i < mosques; i++)
                if (Found(site, roll, centre, within * 0.35, within * 0.9, site.M(20), taken, streets) is { } mosque && Spot.Toward(roll.Between(0, Math.PI)) is var facing)
                    Settlements.Church(site, mosque, facing, facing.Left, centre, within, parts, taken, 0.75);
            if (within >= site.M(TownFromM) && site.Manner != Manners.Persian && Found(site, roll, centre, within * 0.2, within * 0.6, site.M(BathsLongM / 2), taken, streets) is { } hammam)
            {
                parts.Add(BuiltSite.Block("baths", hammam, Spot.Toward(roll.Between(0, Math.PI)), site.M(BathsLongM * 0.75), site.M(BathsDeepM * 0.8)));
                taken.Add((hammam, site.M(BathsLongM / 2)));
            }
        }
        if (site.Manner is Manners.Continental or Manners.Western or Manners.American or Manners.Northern or Manners.Gothic or Manners.Rus or Manners.Byzantine)
        {
            double apart = site.Manner switch { Manners.American => ChapelM, Manners.Byzantine => ByzantineParishM, Manners.Rus => RusParishM, _ => ParishM };
            int parishes = Math.Clamp((int)(within / site.M(apart)) - 1, 0, MostParishes + (site.Manner == Manners.Byzantine ? 2 : site.Manner == Manners.Rus ? RusParishesMore : 0));
            for (int i = 0; i < parishes; i++)
                if (Found(site, roll, centre, within * 0.4, within * 0.85, site.M(20), taken, streets) is { } parish && Spot.Toward(roll.Between(0, Math.PI)) is var nave)
                    Settlements.Church(site, parish, nave, nave.Left, centre, within, parts, taken, site.Manner == Manners.Byzantine ? ByzantineGrand : ParishGrand);
        }
        if (!Defences.Bastioned(site.Epoch) || walls < Settlements.BastionedFrom) return;
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

    static void Cloisters(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        bool outside = site.Manner == Manners.Rus;
        int count = site.Manner == Manners.Gothic ? Math.Clamp((int)(within / site.M(FriaryM)), 1, MostFriaries) : 1;
        for (int i = 0; i < count; i++)
        {
            if (Found(site, roll, centre, within * (outside ? 1.05 : 0.7), within * (outside ? 1.3 : 0.95), site.M(CloisterM * 0.8), taken, streets) is not { } cloister) continue;
            var east = new Spot(1, 0);
            parts.Add(BuiltSite.Block("cloister", cloister, east, site.M(CloisterM), site.M(CloisterM * 0.8)));
            Settlements.Chapel(site, cloister + east.Left * site.M(CloisterM * 0.4 + MinsterM * 0.25), east, parts, MinsterGrand);
            if (outside || site.Manner == Manners.Byzantine)
                parts.Add(BuiltSite.Line(outside ? "palisade" : "wardwall", Enumerable.Range(0, 17).Select(k => cloister + Spot.Toward(k * Math.PI / 8) * site.M(CloisterM * 1.1))));
            parts.Add(BuiltSite.Area("garden", [cloister - east * site.M(CloisterM * 0.5) - east.Left * site.M(CloisterM * 0.4), cloister - east * site.M(CloisterM * 0.5)
                + east.Left * site.M(CloisterM * 0.4),
                cloister - east * site.M(CloisterM * 0.9) + east.Left * site.M(CloisterM * 0.4), cloister - east * site.M(CloisterM * 0.9) - east.Left * site.M(CloisterM * 0.4)]));
            taken.Add((cloister, site.M(CloisterM * 0.9)));
        }
    }

    static void Suq(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double half = Homes.HalfWidth(site, "street") + site.M(SuqDeepM / 2), reach = site.M(SuqLongM / 2);
        foreach (var street in streets.Where(s => s.Points.Count > 1).OrderBy(s => s.Main ? 0 : 1))
            for (int i = 0; i + 1 < street.Points.Count; i++)
            {
                Spot a = street.Points[i], b = street.Points[i + 1], way = (b - a).Unit, mid = (a + b) * 0.5;
                if ((b - a).Length < site.M(SuqLongM) || (mid - centre).Length > within * SuqWithin) continue;
                foreach (int side in new[] { 1, -1 })
                {
                    var suq = mid + way.Left * (side * half);
                    if (!site.Free(suq, site.M(SuqDeepM / 2)) || taken.Any(o => (o.At - suq).Length < o.Reach + reach)) continue;
                    parts.Add(BuiltSite.Block("markethall", suq, way, site.M(SuqLongM), site.M(SuqDeepM)));
                    taken.Add((suq, reach));
                    return;
                }
            }
    }

    static void Manor(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Found(site, roll, centre, within * 0.7, within * 1.05, site.M(ManorM * 0.7), taken, streets) is not { } manor) return;
        Spot east = new(1, 0), north = east.Left;
        double moat = site.M(ManorM / 2), isle = site.M(ManorM / 2 - MoatWideM);
        Spot[] Square(double r) => [manor - east * r - north * r, manor + east * r - north * r, manor + east * r + north * r, manor - east * r + north * r];
        parts.Add(BuiltSite.Area("moat", Square(moat)));
        parts.Add(BuiltSite.Area("green", Square(isle)));
        parts.Add(BuiltSite.Block("public", manor + north * (isle * 0.35), east, site.M(ManorHallM), site.M(ManorHallM * 0.45)));
        parts.Add(BuiltSite.Block("barn", manor - north * (isle * 0.45), east, site.M(BarnInManorM), site.M(BarnInManorM * 0.45)));
        parts.Add(BuiltSite.Sign("bridge", manor - east * ((moat + isle) / 2), 0));
        parts.Add(BuiltSite.Block("dovecote", manor + east * (isle * 0.6) - north * (isle * 0.5), east, site.M(DovecoteM), site.M(DovecoteM)));
        taken.Add((manor, moat * 1.2));
    }

    public static string Barn(int manner, Dice roll)
    {
        double pick = roll.Next();
        return manner switch
        {
            Manners.Tribal => "granary",
            Manners.Mongol => "cartshed",
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
        if (!Manners.Old(site.Kin) && roll.Chance(0.18) && Room(site, taken, cote, site.M(DovecoteM)))
        {
            parts.Add(BuiltSite.Block("dovecote", cote, way, site.M(DovecoteM), site.M(DovecoteM)));
            taken.Add((cote, site.M(DovecoteM)));
        }
    }

    public static void Country(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(centre, 83);
        int manner = site.Kin;
        bool warm = manner is Manners.Roman or Manners.Egyptian || site.Manner is Manners.Ottoman or Manners.NorthAfrican or Manners.Byzantine or Manners.Saracen;
        if (warm && roll.Chance(0.65 * site.Farming + 0.1) && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.2), site.M(ThreshingM * 1.3), taken, streets) is { } floor)
        {
            parts.Add(BuiltSite.Area("threshing", Ellipse(floor, new Spot(1, 0), site.M(ThreshingM), site.M(ThreshingM), ThreshingSteps)));
            parts.Add(BuiltSite.Sign("stooks", floor + Spot.Toward(roll.Between(0, 6)) * site.M(ThreshingM + 2.5)));
            taken.Add((floor, site.M(ThreshingM * 1.3)));
        }
        if (site.Manner is Manners.Roman or Manners.Ottoman or Manners.Continental or Manners.Gothic or Manners.Byzantine
            && roll.Chance(site.Manner is Manners.Continental or Manners.Gothic ? 0.2 : 0.55)
            && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.3), site.M(VineyardLongM * 0.6), taken, streets, at => Sunward(site, at)) is { } vines)
        {
            Spot along = Contoured(site, vines, (vines - centre).Unit.Left), half = along * (site.M(VineyardLongM) / 2), deep = along.Left * (site.M(VineyardDeepM) / 2);
            Spot[] ring = [vines - half - deep, vines + half - deep, vines + half + deep, vines - half + deep];
            parts.Add(BuiltSite.Area(Terraces.Vineyard, ring));
            parts.AddRange(Terraces.Cut(site, ring, Terraces.Vineyard));
            taken.Add((vines, site.M(VineyardLongM * 0.6)));
        }
        if (manner != Manners.Tribal && manner != Manners.Egyptian && site.Manner != Manners.Mongol && !site.Modern && roll.Chance(0.3)
            && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.2), site.M(KilnM * 1.5), taken, streets) is { } kiln)
        {
            parts.Add(BuiltSite.Block("kiln", kiln, new Spot(1, 0), site.M(KilnM), site.M(KilnM)));
            parts.Add(BuiltSite.Sign("logs", kiln + Spot.Toward(roll.Between(0, 6)) * site.M(KilnM + 2), roll.Between(0, Math.PI)));
            taken.Add((kiln, site.M(KilnM * 1.5)));
        }
        if (manner == Manners.Tribal && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.25), site.M(3), taken, streets) is { } stone) parts.Add(BuiltSite.Sign("menhir", stone));
        if (site.Manner is Manners.Byzantine or Manners.Saracen
            && Found(site, roll, centre, within * EdgeAt, within * (EdgeAt + 0.35), site.M(GroveLongM * 0.6), taken, streets, at => Sunward(site, at)) is { } grove)
        {
            Spot along = Contoured(site, grove, (grove - centre).Unit.Left), deep = along.Left;
            Spot half = along * (site.M(GroveLongM) / 2), side = deep * (site.M(GroveDeepM) / 2);
            Spot[] ring = [grove - half - side, grove + half - side, grove + half + side, grove - half + side];
            parts.Add(BuiltSite.Area("orchard", ring));
            parts.AddRange(Terraces.Cut(site, ring, "orchard"));
            Homes.Trees(site, roll, grove - side, deep, along, site.M(GroveDeepM), site.M(GroveLongM), parts);
            taken.Add((grove, site.M(GroveLongM * 0.6)));
        }
        if (site.Manner == Manners.Mongol && Found(site, roll, centre, within * (EdgeAt + 0.3), within * (EdgeAt + 0.8), site.M(4), taken, streets, at => site.HeightAt(at)) is { } height)
            parts.Add(BuiltSite.Sign("ovoo", height));
        if ((manner is Manners.Egyptian or Manners.Northern || site.Manner == Manners.Saracen) && site.Manner is not (Manners.Pacific or Manners.Nippon or Manners.Japanese or Manners.American)
            && site.NearestWater(centre) is var (bank, reach) && reach < within * 1.3)
            parts.Add(BuiltSite.Sign("sweep", bank + (centre - bank).Unit * site.M(5), Math.Atan2(bank.Y - centre.Y, bank.X - centre.X)));
    }

    public static void Farmyard(BuiltSite site, Spot at, Spot front, double yard, List<BuiltPart> parts)
    {
        var roll = BuiltSite.Roll(at, 97);
        var side = front.Left;
        double turn = Math.Atan2(front.Y, front.X);
        Spot In(double ahead, double aside) => at + front * (yard * ahead) + side * (yard * aside);
        if (roll.Chance(0.6)) parts.Add(BuiltSite.Sign("dungheap", In(0.06, 0.05)));
        if (roll.Chance(0.4)) parts.Add(BuiltSite.Sign(site.Kin is Manners.Northern or Manners.Egyptian ? "sweep" : "well", In(-0.16, 0.14), turn));
        if (roll.Chance(0.55))
        {
            parts.Add(BuiltSite.Block("coop", In(0.4, -0.42), front, site.M(CoopLongM), site.M(CoopDeepM)));
            parts.Add(BuiltSite.Sign("hens", In(0.3, -0.3), roll.Between(0, 2 * Math.PI)));
        }
        if (roll.Chance(0.4) && site.Kin != Manners.Egyptian && site.Manner is not (Manners.Saracen or Manners.Mongol))
        {
            parts.Add(BuiltSite.Block("pigsty", In(-0.4, -0.42), front, site.M(StyLongM), site.M(StyDeepM)));
            parts.Add(BuiltSite.Sign("pigs", In(-0.32, -0.3), roll.Between(0, 2 * Math.PI)));
        }
        if (roll.Chance(0.16) && !Manners.Old(site.Kin)) parts.Add(BuiltSite.Block("dovecote", In(-0.4, 0.4), front, site.M(DovecoteM), site.M(DovecoteM)));
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
