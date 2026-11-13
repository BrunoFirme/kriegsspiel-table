namespace KTable.Core.Model;

static class Props
{
    const double StallPitchM = 4.6, StallInM = 3.5, GoodsOffM = 2.4, TroughOffM = 2.6, CartShare = 0.6, GoodsShare = 0.5;
    const double InnLongM = 16, InnDeepM = 10, SmithyLongM = 10, SmithyDeepM = 8, TradeOffM = 4, SetbackM = 3, SignOffM = 1.6, AnvilOffM = 1.3;
    const double WoodpileShare = 0.3, BarrelShare = 0.22, PrivyShare = 0.3, PrivyBelow = 0.85, PrivyM = 1.4, PrivyBackM = 7, HiveShare = 0.25, HiveApartM = 1.3;
    const double JettyLongM = 10, JettyReach = 1.25, JettyStepM = 4, BoatAsideM = 2.2, RockRoomM = 2, GallowsShare = 0.35, GallowsFromM = 200, GallowsAt = 1.3;
    const double KeepM = 13, KeepRoomM = 15, KeepAt = 0.74, GunAt = 0.8;
    const int MostStalls = 8, Bearings = 16, MostJetties = 2, MostRocks = 4, HiveRow = 4;

    static double Heading(Spot way) => Math.Atan2(way.Y, way.X);

    public static void Market(BuiltSite site, Dice dice, Spot square, Spot along, Spot aside, double wide, double deep, bool town, List<BuiltPart> parts)
    {
        int stalls = Math.Min(MostStalls, town ? 4 + dice.Below(4) : 1 + dice.Below(3));
        for (int i = 0; i < stalls; i++)
        {
            int row = i % 2 == 0 ? 1 : -1;
            var at = square + along * ((i / 2 - (stalls - 1) / 4.0) * site.M(StallPitchM)) + aside * (row * (deep / 2 - site.M(StallInM)));
            parts.Add(BuiltSite.Sign("stall", at, Heading(along)));
            if (dice.Chance(GoodsShare)) parts.Add(BuiltSite.Sign(dice.Chance(0.5) ? "barrels" : "crates", at + along * site.M(GoodsOffM) + aside * (row * site.M(0.4)), dice.Between(0, Math.PI)));
        }
        parts.Add(BuiltSite.Sign("trough", square + along * site.M(TroughOffM), Heading(aside)));
        if (dice.Chance(CartShare)) parts.Add(BuiltSite.Sign("cart", square - along * (wide * 0.32) + aside * (deep * dice.Between(-0.2, 0.2)), dice.Between(0, 2 * Math.PI)));
        if (!town) return;
        parts.Add(BuiltSite.Sign("fountain", square - along * (wide * 0.18)));
        parts.Add(BuiltSite.Sign("stocks", square + along * (wide * 0.4) + aside * (deep * 0.3), Heading(along)));
    }

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    public static void Trades(BuiltSite site, Spot street, Spot square, Spot along, Spot aside, double wide, Spot centre, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var inn = square - along * (wide / 2 + site.M(TradeOffM + InnDeepM / 2));
        if ((inn - centre).Length < within && Room(site, taken, inn, site.M(InnLongM / 2)))
        {
            parts.Add(BuiltSite.Block("inn", inn, along.Left, site.M(InnLongM), site.M(InnDeepM)));
            parts.Add(BuiltSite.Sign("signboard", inn + along * site.M(InnDeepM / 2 + SignOffM) + aside * site.M(InnLongM * 0.35)));
            parts.Add(BuiltSite.Sign("trough", inn + along * site.M(InnDeepM / 2 + SignOffM) - aside * site.M(InnLongM * 0.25), Heading(aside)));
            taken.Add((inn, site.M(InnLongM / 2)));
        }
        var smithy = street - aside * (Homes.HalfWidth(site, "street") + site.M(SetbackM + SmithyDeepM / 2)) - along * (wide * 0.35);
        if ((smithy - centre).Length >= within || !Room(site, taken, smithy, site.M(SmithyLongM / 2))) return;
        parts.Add(BuiltSite.Block("smithy", smithy, aside.Left, site.M(SmithyLongM), site.M(SmithyDeepM)));
        parts.Add(BuiltSite.Sign("anvil", smithy + aside * site.M(SmithyDeepM / 2 + AnvilOffM)));
        taken.Add((smithy, site.M(SmithyLongM / 2)));
    }

    public static void Yard(BuiltSite site, Dice dice, Spot at, Spot facing, Spot back, double wide, double deep, double houses, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (dice.Chance(WoodpileShare)) parts.Add(BuiltSite.Sign("woodpile", at + back * (deep / 2 + site.M(0.9)) + facing * (wide * 0.28 * (dice.Chance(0.5) ? 1 : -1)), Heading(facing)));
        if (dice.Chance(BarrelShare)) parts.Add(BuiltSite.Sign("barrels", at - back * (deep / 2 + site.M(0.8)) + facing * (wide * 0.36 * (dice.Chance(0.5) ? 1 : -1)), dice.Between(0, Math.PI)));
        var privy = at + back * (deep / 2 + site.M(PrivyBackM)) - facing * (wide * 0.4);
        if (houses >= PrivyBelow || !dice.Chance(PrivyShare) || !Room(site, taken, privy, site.M(PrivyM))) return;
        parts.Add(BuiltSite.Block("privy", privy, facing, site.M(PrivyM), site.M(PrivyM)));
        taken.Add((privy, site.M(PrivyM)));
    }

    public static void Hives(BuiltSite site, Dice dice, Spot far, Spot back, Spot facing, List<BuiltPart> parts)
    {
        if (!dice.Chance(HiveShare)) return;
        for (int i = 0; i < HiveRow; i++) parts.Add(BuiltSite.Sign("hive", far - back * site.M(1.6) + facing * ((i - (HiveRow - 1) / 2.0) * site.M(HiveApartM)), Heading(facing)));
    }

    public static void Waterside(BuiltSite site, Dice dice, Spot centre, double within, List<BuiltPart> parts)
    {
        double step = site.M(JettyStepM), reach = site.M(JettyLongM);
        int laid = 0;
        for (int i = 0; i < Bearings && laid < MostJetties; i++)
        {
            var way = Spot.Toward(i * 2 * Math.PI / Bearings + dice.Between(0, 0.25));
            Spot? bank = null;
            for (double far = within * 0.3; far < within * JettyReach && bank == null && site.Inside(centre + way * far); far += step)
                if (site.KindAt(centre + way * far) == GroundKinds.Water) bank = centre + way * (far - step);
            if (bank is not { } shore || !site.Buildable(shore) || site.KindAt(shore + way * reach) != GroundKinds.Water) continue;
            parts.Add(BuiltSite.Line("jetty", [shore - way * site.M(2), shore + way * reach]));
            parts.Add(BuiltSite.Sign("boat", shore + way * (reach * 0.7) + way.Left * site.M(BoatAsideM), Heading(way)));
            if (dice.Chance(0.5)) parts.Add(BuiltSite.Sign("boat", shore + way * (reach + site.M(6)) - way.Left * site.M(5), dice.Between(0, 2 * Math.PI)));
            laid++;
            i += Bearings / 4;
        }
    }

    public static void Wild(BuiltSite site, Dice dice, Spot centre, double within, bool crosses, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        for (int r = dice.Below(MostRocks + 1); r > 0; r--)
        {
            var rock = centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (within * dice.Between(0.5, 1.15));
            if (Room(site, taken, rock, site.M(RockRoomM))) parts.Add(BuiltSite.Sign("rock", rock, dice.Between(0, 2 * Math.PI)));
        }
        var gallows = centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (within * GallowsAt);
        if (crosses && within >= site.M(GallowsFromM) && dice.Chance(GallowsShare) && Room(site, taken, gallows, site.M(RockRoomM))) parts.Add(BuiltSite.Sign("gallows", gallows, dice.Between(0, Math.PI)));
    }

    public static void Keep(BuiltSite site, IReadOnlyList<Spot> ring, IReadOnlyList<Spot> held, List<BuiltPart> parts)
    {
        var middle = Enclosures.Middle(ring);
        double side = site.M(KeepM), room = site.M(KeepRoomM);
        foreach (var corner in ring.OrderByDescending(site.HeightAt))
        {
            var at = middle + (corner - middle) * KeepAt;
            if (!site.Free(at, side / 2) || held.Any(p => (p - at).Length < room)) continue;
            var facing = (corner - middle).Unit;
            parts.Add(BuiltSite.Block("keep", at, facing, side, side));
            foreach (var (x, y) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }) parts.Add(BuiltSite.Sign("tower", at + facing * (x * side / 2) + facing.Left * (y * side / 2)));
            return;
        }
    }

    public static IEnumerable<BuiltPart> Guns(Spot centre, double radius, int bastions, double turn) =>
        Enumerable.Range(0, bastions).Select(i => BuiltSite.Sign("gun", centre + Spot.Toward(turn + i * 2 * Math.PI / bastions) * (radius * GunAt), turn + i * 2 * Math.PI / bastions));
}
