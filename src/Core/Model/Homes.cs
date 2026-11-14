namespace KTable.Core.Model;

static class Homes
{
    const double TownSetbackM = 0.6, SetbackPlayM = 2.5, YardShare = 0.35, YardDeepM = 7, CroftKeep = 1.3, CroftLeast = 0.12, HouseLeastM = 9, HouseMostM = 16, DepthLeastM = 7, DepthMostM = 10;
    const double TownLeastM = 5.5, TownMostM = 8.5, TownDepthLeastM = 9.5, TownDepthMostM = 14, TownFrom = 0.45, TownFull = 0.95;
    const double OpenGapM = 22, PackedGapM = 0.3, GapCurve = 0.75, KeepOpen = 0.55, KeepPacked = 0.97, Overlap = 0.85, TownOverlap = 0.62, TurnPlay = 0.06, GableShare = 0.2, GablesBelow = 0.5;
    const double EllShare = 0.14, EllsBelow = 0.75, WingLeastM = 5, WingMostM = 9;
    const double ShedShare = 0.4, ShedsBelow = 0.85, ShedLeastM = 4, ShedMostM = 8, ShedDeepM = 4.5, ShedGapM = 4;
    const double CroftsBelow = 0.8, CroftOpenM = 42, CroftPackedM = 14, CroftGapM = 1.5, CroftLeastM = 5, CroftWiderM = 28, Snug = 0.96;
    const double OrchardShare = 0.3, TreesApartM = 9, TreeKeep = 0.8, TreePlay = 0.25;
    const double RowsFrom = 0.9, WalledBelow = 0.97;
    const int BackLaneFrom = 2, BackLaneChecks = 6;
    const double BackLaneStepM = 30;
    const double StripLeast = 0.85, StripOrchards = 0.15;
    const int MostTrees = 12;

    public static double HalfWidth(BuiltSite site, string cls) => site.M(cls is "lane" or "track" ? site.Ways.LaneM : cls == "chaussee" ? site.Ways.StreetM * 1.5 : site.Ways.StreetM);

    public static void Front(BuiltSite site, Dice dice, Spot centre, double within, Settlements.Street street, List<Settlements.Street> streets, double houses, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken, double strips = 0)
    {
        double town = Math.Clamp((houses - TownFrom) / (TownFull - TownFrom), 0, 1), overlap = Settlements.Lerp(Overlap, TownOverlap, town);
        var yards = parts.Where(p => p.Kind == PartKinds.Area && p.Class is "churchyard" or "square" or "green" or "pond").ToList();
        var ways = site.Ways;
        bool rows = houses >= Math.Min(RowsFrom, ways.RowsFrom);
        double half = HalfWidth(site, street.Class), gap = rows ? 0 : site.M(Settlements.Lerp(ways.GapOpenM, PackedGapM, Math.Pow(houses, GapCurve)));
        double keep = rows ? 1 : Settlements.Lerp(KeepOpen, KeepPacked, houses), set = Settlements.Lerp(ways.SetbackM, Math.Min(ways.SetbackM, TownSetbackM), town);
        double tilled = ways.Crofts * (YardShare + (1 - YardShare) * site.Farming);
        double Croft() => strips > 0 ? strips * dice.Between(StripLeast, 1)
            : Math.Max(site.M(YardDeepM) * Math.Min(1, ways.Crofts * 4), site.M(Settlements.Lerp(CroftOpenM, CroftPackedM, houses / CroftsBelow)) * tilled) * dice.Between(0.75, 1.25);
        double[] crofts = [Croft(), Croft()];
        List<Spot>[] lanes = [[], []];
        int[] crofted = [0, 0];
        double length = TownStreets.Long(street.Points);
        Spot On(double s) => TownStreets.Along(street.Points, s).At;
        foreach (int side in new[] { 1, -1 })
        {
            if (street.Side != Settlements.BothSides && street.Side != side) continue;
            double far = half + site.M(set + SetbackPlayM * (1 - houses) + Settlements.Lerp(DepthMostM, TownDepthMostM, town) + CroftGapM) + crofts[side > 0 ? 0 : 1];
            var row = new List<(Lot Lot, Plot Plot, double From, double To, double Clear)>();
            for (double s = dice.Between(0, gap); s < length;)
            {
                double from = s;
                double wide = site.M(dice.Between(Settlements.Lerp(HouseLeastM, TownLeastM, town), Settlements.Lerp(HouseMostM, TownMostM, town)));
                double deep = site.M(dice.Between(Settlements.Lerp(DepthLeastM, TownDepthLeastM, town), Settlements.Lerp(DepthMostM, TownDepthMostM, town)));
                if (houses < GablesBelow && dice.Chance(GableShare)) (wide, deep) = (deep, wide);
                var (cls, wideM, deepM) = Callings.Home(site, town, BuiltSite.Roll(On(s), side));
                if (wideM > 0) (wide, deep) = (site.M(wideM), site.M(deepM));
                double stands = half + site.M(set + dice.Between(0, SetbackPlayM) * (1 - houses)) + deep / 2;
                var (mid, along) = TownStreets.Along(street.Points, s + wide / 2);
                var back = along.Left * side;
                var at = mid + back * stands;
                double apart = Callings.GapM(site, cls) is >= 0 and var set_ ? site.M(set_) : gap;
                double lot = wide + apart * dice.Between(0.6, 1.4), reach = Settlements.Lerp(Math.Max(wide, deep), wide, town) / 2;
                s += lot;
                if (!dice.Chance(keep) || s > length + wide || (at - centre).Length > within || !site.Free(at, reach)) continue;
                if (site.KindAt(at) == GroundKinds.Wood && site.KindAt(mid) != GroundKinds.Wood) continue;
                if (Settlements.FromStreets(streets, at, street) < deep / 2 + half || taken.Any(o => (o.At - at).Length < (o.Reach + reach) * overlap)) continue;
                if (yards.Any(yard => BuiltThings.Reach(yard, at) < Math.Min(wide, deep) / 2)) continue;
                var facing = along.Turned(rows ? 0 : dice.Between(-TurnPlay, TurnPlay));
                bool ell = houses < EllsBelow && dice.Chance(EllShare * (1 - town)) && cls == "house" && site.Manner is Manners.Continental or Manners.American or Manners.Western;
                double wing = site.M(dice.Between(WingLeastM, WingMostM)), wingDeep = site.M(dice.Between(WingLeastM, WingMostM));
                var stood = ell ? BuiltSite.Ell("house", at, facing, back, wide, deep, Math.Min(wing, wide * 0.6), wingDeep, dice.Chance(0.5)) : BuiltSite.Block(cls, at, facing * side, wide, deep);
                if (BuiltGeometry.Points(stood.At).Any(corner => !site.Buildable(Spot.Of(corner)))) continue;
                parts.Add(stood);
                int mine = taken.Count;
                taken.Add((at, reach));
                var home = new Lot(at, facing, back, wide, deep + (ell ? 2 * wingDeep : 0), mine);
                if (Callings.Gardened(cls)) row.Add((home, new Plot(On(from), along, stands, far, On(from), back, back), from, from
                    + lot, Behind(site, dice, home, !ell, houses, streets, parts, taken)));
            }
            var plots = new List<(double From, double To)>();
            int which = side > 0 ? 0 : 1;
            for (int k = 0; k < row.Count; k++)
            {
                double start = k == 0 ? Math.Max(0, row[k].From - site.M(CroftWiderM)) : row[k].From;
                double to = Math.Min(k + 1 < row.Count ? row[k + 1].From : length, row[k].To + site.M(CroftWiderM));
                var (ended, leaving) = TownStreets.Along(street.Points, to);
                var plot = row[k].Plot with { Start = On(start), End = ended, BackStart = TownStreets.Along(street.Points, start).Along.Left * side, BackEnd = leaving.Left * side };
                if (Crofted(site, dice, row[k].Lot, plot, row[k].Clear, to - start, houses, strips > 0, streets, parts, taken)) plots.Add((start, to));
            }
            if (plots.Count == 0) continue;
            crofted[which] += plots.Count;
            int steps = Math.Max(1, (int)Math.Ceiling((plots[^1].To - plots[0].From) / site.M(BackLaneStepM)));
            for (int k = 0; k <= steps; k++)
            {
                var (on, way) = TownStreets.Along(street.Points, plots[0].From + (plots[^1].To - plots[0].From) * k / steps);
                lanes[which].Add(on + way.Left * (side * (far + HalfWidth(site, "lane"))));
            }
        }
        for (int which = 0; which < lanes.Length; which++)
            if (strips > 0 && crofted[which] >= BackLaneFrom) BackLane(site, lanes[which], streets, parts, taken);
    }

    readonly record struct Lot(Spot At, Spot Facing, Spot Back, double Wide, double Deep, int Mine);

    readonly record struct Plot(Spot Start, Spot Row, double Out, double Far, Spot End, Spot BackStart, Spot BackEnd);

    static void BackLane(BuiltSite site, List<Spot> line, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        for (int i = 0; i + 1 < line.Count; i++)
            for (int k = 0; k <= BackLaneChecks; k++)
            {
                var p = line[i] + (line[i + 1] - line[i]) * (k / (double)BackLaneChecks);
                if (!site.Free(p, 0) || taken.Any(o => (o.At - p).Length < o.Reach * Snug) || Settlements.FromStreets(streets, p) < HalfWidth(site, "lane")) return;
            }
        parts.Add(BuiltSite.Line("track", line));
    }

    static bool Room(BuiltSite site, List<Settlements.Street> streets, List<(Spot At, double Reach)> taken, int upTo, Spot p, double reach) =>
        site.Free(p, reach) && Settlements.FromStreets(streets, p) > reach && !taken.Take(upTo).Any(o => (o.At - p).Length < o.Reach + reach);

    static double Behind(BuiltSite site, Dice dice, Lot lot, bool shed, double houses, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        double from = lot.Deep / 2 + site.M(CroftGapM);
        if (shed && houses < ShedsBelow && dice.Chance(ShedShare * Math.Min(1, site.Ways.Crofts * 2 + site.Farming * 0.5)))
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
        Props.Yard(site, dice, lot.At, lot.Facing, lot.Back, lot.Wide, lot.Deep, houses, parts, taken);
        Callings.Yard(site, lot.At, lot.Facing, lot.Back, lot.Wide, lot.Deep, houses, parts, taken);
        return from;
    }

    static bool Crofted(BuiltSite site, Dice dice, Lot lot, Plot plot, double clear, double wide, double houses, bool strip, List<Settlements.Street> streets, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken)
    {
        if (!strip
            && (houses >= (site.Ways.Walled ? WalledBelow : CroftsBelow) || site.Ways.Crofts < CroftLeast
                || !dice.Chance(Math.Min(1, site.Farming * CroftKeep + site.Ways.Crofts * 0.2)))) return false;
        double near = plot.Out + clear, croft = plot.Far - near;
        if (croft < site.M(CroftLeastM)) return false;
        Spot corner = plot.Start + plot.BackStart * near, end = plot.Start + plot.BackStart * plot.Far, beside = plot.End + plot.BackEnd * near, beyond = plot.End + plot.BackEnd * plot.Far;
        Spot front = corner + (beside - corner) * 0.5, rear = end + (beyond - end) * 0.5;
        double reach = Math.Min(croft, wide) / 2 * Snug;
        int discs = Math.Max(1, (int)(croft / Math.Max(wide, site.M(CroftLeastM))));
        var held = Enumerable.Range(0, discs).Select(k => front + (rear - front) * ((k + 0.5) / discs)).ToList();
        if (held.Any(at => !Room(site, streets, taken, lot.Mine, at, reach)) || !site.Free(rear, 0)) return false;
        bool orchard = dice.Chance(strip ? StripOrchards : OrchardShare);
        parts.Add(BuiltSite.Area(orchard ? "orchard" : "garden", [corner, beside, beyond, end]));
        parts.Add(BuiltSite.Line("fence", [corner, end]));
        taken.AddRange(held.Select(at => (at, reach)));
        if (!orchard) return true;
        Trees(site, dice, front, lot.Back, plot.Row, croft, wide, parts);
        Props.Hives(site, dice, rear, lot.Back, plot.Row, parts);
        return true;
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
