namespace KTable.Core.Model;

static class Suburbs
{
    public const string Tomb = "tomb";
    const double OutM = 6, LongM = 120, Share = 0.65, RibbonShare = 0.45, RibbonLongM = 160, RibbonFrom = 0.7, InnOffM = 12, InnLongM = 16, InnDeepM = 10;
    const double CemeteryOffM = 22, CemeteryLongM = 44, CemeteryDeepM = 28, TombM = 6, WallRoomM = 16;
    const int MostTombs = 4, Salt = 97;
    const double HamletM = 34, HamletShare = 0.45, HamletChance = 0.5;
    static readonly double[] Arms = [1.15, 0.8, 0.5];

    static bool Faithful(int manner) => manner is Manners.Saracen or Manners.Ottoman or Manners.NorthAfrican or Manners.Persian;

    static bool Innless(int manner) => manner is Manners.Tribal or Manners.Mongol or Manners.Pacific;

    internal static void Gates(BuiltSite site, Dice dice, IReadOnlyList<Spot> ring, double houses, List<BuiltPart> parts)
    {
        var middle = Enclosures.Middle(ring);
        var gates = parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(p => BuiltSite.Round(p).At).ToList();
        Beyond(site, dice, ring, gates.Select(gate => (gate, (gate - middle).Unit)), OutM, houses, parts);
    }

    internal static void Beyond(BuiltSite site, Dice dice, IReadOnlyList<Spot> ring, IEnumerable<(Spot Gate, Spot Outward)> gates, double outM, double houses, List<BuiltPart> parts)
    {
        var taken = ring.Select(p => (p, site.M(WallRoomM))).ToList();
        taken.AddRange(parts.Where(p => p.Kind == PartKinds.Block).Select(BuiltSite.Round).Select(r => (r.At, r.Reach)));
        var ordered = gates.OrderBy(g => site.NearestRoad(g.Gate)?.Reach ?? double.MaxValue).ToList();
        for (int k = 0; k < ordered.Count; k++)
        {
            var (gate, outward) = ordered[k];
            bool arm = k < Arms.Length;
            if (!arm && !dice.Chance(HamletChance)) continue;
            double length = arm ? LongM * Arms[k] * dice.Between(0.85, 1.15) : HamletM * dice.Between(0.6, 1.3);
            var from = gate + outward * site.M(outM);
            var road = new Settlements.Street([from, from + outward * site.M(length)], "street", true, arm ? Settlements.BothSides : dice.Chance(0.5) ? 1 : -1, Main: false);
            int before = parts.Count;
            if (arm && Faithful(site.Manner) && k % 2 == 0) Cemetery(site, dice, from, outward, parts, taken);
            if (arm && !Innless(site.Manner)) Inn(site, from + outward * site.M(InnOffM), outward, dice.Chance(0.5) ? 1 : -1, parts, taken);
            Homes.Front(site, dice, gate, site.M(outM + length), road, [road], houses * (arm ? Share : HamletShare), parts, taken);
            var within = parts.Skip(before).Where(p => p.Kind is PartKinds.Area or PartKinds.Line && BuiltGeometry.Points(p.At).Select(Spot.Of).Any(q => Walled(site, ring, q))).ToList();
            parts.RemoveAll(within.Contains);
            if (parts.Count > before) parts.Add(BuiltSite.Line(road.Class, road.Points));
        }
    }

    static bool Walled(BuiltSite site, IReadOnlyList<Spot> ring, Spot p) =>
        Enclosures.Inside(ring, p) || Enumerable.Range(0, ring.Count).Any(i => p.FromSegment(ring[i], ring[(i + 1) % ring.Count]) < site.M(WallRoomM));

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    static void Inn(BuiltSite site, Spot at, Spot outward, double side, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var inn = at + outward.Left * (side * (Homes.HalfWidth(site, "street") + site.M(InnDeepM / 2 + 1)));
        if (!Room(site, taken, inn, site.M(InnLongM / 2))) return;
        parts.Add(BuiltSite.Block("inn", inn, outward, site.M(InnLongM), site.M(InnDeepM)));
        parts.Add(BuiltSite.Sign("trough", inn - outward.Left * (side * site.M(InnDeepM / 2 + 1.4)), Math.Atan2(outward.Y, outward.X)));
        taken.Add((inn, site.M(InnLongM / 2)));
    }

    static void Cemetery(BuiltSite site, Dice dice, Spot from, Spot outward, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double side = dice.Chance(0.5) ? 1 : -1;
        var at = from + outward * site.M(CemeteryOffM + CemeteryLongM / 2) + outward.Left * (side * (Homes.HalfWidth(site, "street") + site.M(CemeteryDeepM / 2 + 2)));
        var ring = Industry.Rect(at, outward, site.M(CemeteryLongM), site.M(CemeteryDeepM));
        if (ring.Any(p => !site.Buildable(p)) || !Room(site, taken, at, site.M(CemeteryLongM / 2))) return;
        parts.Add(BuiltSite.Area("churchyard", ring));
        int tombs = 1 + dice.Below(MostTombs);
        for (int i = 0; i < tombs; i++)
            parts.Add(BuiltSite.Block(Tomb, at + outward * site.M(dice.Between(-0.35, 0.35) * CemeteryLongM) + outward.Left * site.M(dice.Between(-0.3, 0.3) * CemeteryDeepM), outward,
                site.M(TombM * dice.Between(0.8, 1.3)), site.M(TombM * dice.Between(0.8, 1.1))));
        if (site.Manner == Manners.Ottoman)
            for (int i = 0; i < 6; i++) parts.Add(BuiltSite.Sign("tree", at + outward * site.M(dice.Between(-0.45, 0.45) * CemeteryLongM) + outward.Left * site.M(dice.Between(-0.4, 0.4) * CemeteryDeepM)));
        taken.Add((at, site.M(CemeteryLongM / 2)));
    }

    internal static void Ribbons(BuiltSite site, Dice dice, Spot centre, double within, List<Settlements.Street> streets, double houses, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var ends = streets.Where(s => s.Main && s.Laid && s.Points.Count > 1).SelectMany(s => new[] { (End: s.Points[0], Before: s.Points[1]), (End: s.Points[^1], Before: s.Points[^2]) })
            .Where(e => (e.End - centre).Length >= within * RibbonFrom && (e.End - e.Before).Length > 0).OrderBy(e => site.NearestRoad(e.End)?.Reach ?? dice.Next() * within).ToList();
        for (int k = 0; k < Math.Min(Arms.Length, ends.Count); k++)
        {
            var (end, before) = ends[k];
            var outward = (end - before).Unit;
            var road = new Settlements.Street([end, end + outward * site.M(RibbonLongM * Arms[k] * dice.Between(0.85, 1.15))], "road", true, Main: false);
            if (!site.Inside(road.Points[1])) continue;
            int count = parts.Count;
            Homes.Front(site, dice, end, TownStreets.Long(road.Points), road, [road], houses * RibbonShare, parts, taken);
            if (parts.Count > count) parts.Add(BuiltSite.Line(road.Class, road.Points));
        }
    }
}
