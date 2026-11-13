namespace KTable.Core.Model;

static class Homes
{
    const double StreetHalfM = 4, LaneHalfM = 2.5, SetbackM = 3, SetbackPlayM = 2.5, HouseLeastM = 9, HouseMostM = 16, DepthLeastM = 7, DepthMostM = 10;
    const double OpenGapM = 22, PackedGapM = 1.5, KeepOpen = 0.55, KeepPacked = 0.97, Overlap = 0.85, TurnPlay = 0.06, GableShare = 0.2, GablesBelow = 0.5;
    const double EllShare = 0.24, EllsBelow = 0.75, WingLeastM = 5, WingMostM = 9;
    const double ShedShare = 0.4, ShedsBelow = 0.85, ShedLeastM = 4, ShedMostM = 8, ShedDeepM = 4.5, ShedGapM = 4;
    const double CroftsBelow = 0.8, CroftOpen = 0.85, CroftPacked = 0.25, CroftOpenM = 42, CroftPackedM = 14, CroftGapM = 1.5, CroftWiderM = 10, CroftFill = 0.96;
    const double OrchardShare = 0.3, FencedShare = 0.5, TreesApartM = 9, TreeKeep = 0.8, TreePlay = 0.25;
    const double StripLeast = 0.85, StripOrchards = 0.15;
    const int MostTrees = 12;

    public static double HalfWidth(BuiltSite site, string cls) => site.M(cls == "lane" ? LaneHalfM : StreetHalfM);

    public static void Front(BuiltSite site, Dice dice, Spot centre, double within, Settlements.Street street, List<Settlements.Street> streets, double houses, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken, double strips = 0)
    {
        double half = HalfWidth(site, street.Class), gap = site.M(Settlements.Lerp(OpenGapM, PackedGapM, houses)), keep = Settlements.Lerp(KeepOpen, KeepPacked, houses);
        for (int i = 0; i + 1 < street.Points.Count; i++)
        {
            Spot a = street.Points[i], b = street.Points[i + 1];
            var along = (b - a).Unit;
            double length = (b - a).Length;
            foreach (int side in new[] { 1, -1 })
                for (double s = dice.Between(0, gap); s < length;)
                {
                    double wide = site.M(dice.Between(HouseLeastM, HouseMostM)), deep = site.M(dice.Between(DepthLeastM, DepthMostM));
                    if (houses < GablesBelow && dice.Chance(GableShare)) (wide, deep) = (deep, wide);
                    var back = along.Left * side;
                    var at = a + along * (s + wide / 2) + back * (half + site.M(SetbackM + dice.Between(0, SetbackPlayM) * (1 - houses)) + deep / 2);
                    double lot = wide + gap * dice.Between(0.6, 1.4), reach = Math.Max(wide, deep) / 2;
                    s += lot;
                    if (!dice.Chance(keep) || s > length + wide || (at - centre).Length > within || !site.Free(at, reach)) continue;
                    if (Settlements.FromStreets(streets, at, street) < deep / 2 + half || taken.Any(o => (o.At - at).Length < (o.Reach + reach) * Overlap)) continue;
                    var facing = along.Turned(dice.Between(-TurnPlay, TurnPlay));
                    bool ell = houses < EllsBelow && dice.Chance(EllShare);
                    double wing = site.M(dice.Between(WingLeastM, WingMostM)), wingDeep = site.M(dice.Between(WingLeastM, WingMostM));
                    parts.Add(ell ? BuiltSite.Ell("house", at, facing, back, wide, deep, Math.Min(wing, wide * 0.6), wingDeep, dice.Chance(0.5)) : BuiltSite.Block("house", at, facing, wide, deep));
                    int mine = taken.Count;
                    taken.Add((at, reach));
                    Behind(site, dice, new Lot(at, facing, back, wide, deep + (ell ? 2 * wingDeep : 0), lot, mine), !ell, houses, strips, streets, parts, taken);
                }
        }
    }

    readonly record struct Lot(Spot At, Spot Facing, Spot Back, double Wide, double Deep, double Along, int Mine);

    static bool Room(BuiltSite site, List<Settlements.Street> streets, List<(Spot At, double Reach)> taken, int upTo, Spot p, double reach) =>
        site.Free(p, reach) && Settlements.FromStreets(streets, p) > reach && !taken.Take(upTo).Any(o => (o.At - p).Length < o.Reach + reach);

    static void Behind(BuiltSite site, Dice dice, Lot lot, bool shed, double houses, double strips, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double from = lot.Deep / 2 + site.M(CroftGapM);
        if (shed && houses < ShedsBelow && dice.Chance(ShedShare))
        {
            double length = site.M(dice.Between(ShedLeastM, ShedMostM)), deep = site.M(ShedDeepM);
            var at = lot.At + lot.Back * (lot.Deep / 2 + site.M(ShedGapM) + deep / 2) + lot.Facing * ((lot.Wide - length) / 2 * (dice.Chance(0.5) ? 1 : -1));
            if (Room(site, streets, taken, lot.Mine, at, length / 2))
            {
                parts.Add(BuiltSite.Block("shed", at, lot.Facing, length, deep));
                taken.Add((at, length / 2));
                from = lot.Deep / 2 + site.M(ShedGapM) + deep + site.M(CroftGapM);
            }
        }
        double share = houses / CroftsBelow;
        bool strip = strips > 0;
        if (!strip && (houses >= CroftsBelow || !dice.Chance(Settlements.Lerp(CroftOpen, CroftPacked, share)))) return;
        double croft = strip ? strips * dice.Between(StripLeast, 1) : site.M(Settlements.Lerp(CroftOpenM, CroftPackedM, share)) * dice.Between(0.75, 1.25);
        double across = (strip ? lot.Along : Math.Min(lot.Along, lot.Wide + site.M(CroftWiderM))) * CroftFill;
        var mid = lot.At + lot.Back * (from + croft / 2);
        double reach = Math.Min(croft, across) / 2;
        if (!Room(site, streets, taken, lot.Mine, mid, reach) || !site.Free(lot.At + lot.Back * (from + croft), 0)) return;
        Spot near = lot.At + lot.Back * from, far = near + lot.Back * croft, aside = lot.Facing * (across / 2);
        bool orchard = dice.Chance(strip ? StripOrchards : OrchardShare);
        parts.Add(BuiltSite.Area(orchard ? "orchard" : "garden", [near - aside, near + aside, far + aside, far - aside]));
        if (strip) parts.Add(BuiltSite.Line("fence", [near - aside, far - aside]));
        else if (dice.Chance(FencedShare)) parts.Add(BuiltSite.Line("fence", [near - aside, far - aside, far + aside, near + aside]));
        taken.Add((mid, reach));
        if (orchard) Trees(site, dice, near, lot.Back, lot.Facing, croft, across, parts);
    }

    public static void Trees(BuiltSite site, Dice dice, Spot near, Spot back, Spot facing, double deep, double across, List<BuiltPart> parts)
    {
        double apart = site.M(TreesApartM);
        int rows = Math.Max(1, (int)(deep / apart)), cols = Math.Max(1, (int)(across / apart)), laid = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols && laid < MostTrees; c++)
            {
                if (!dice.Chance(TreeKeep)) continue;
                var at = near + back * ((r + 0.5 + dice.Between(-TreePlay, TreePlay)) * deep / rows) + facing * ((c + 0.5 + dice.Between(-TreePlay, TreePlay)) * across / cols - across / 2);
                parts.Add(BuiltSite.Sign("tree", at));
                laid++;
            }
    }
}
