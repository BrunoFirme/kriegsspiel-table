namespace KTable.Core.Model;

public static class BuiltDetails
{
    public const string Plots = "plots", Buildings = "buildings", Radius = "radius";
    const byte Grass = 1, Field = 2, Earth = 7;
    const double PlotOpenM = 350, PlotSmallM = 80, PlotLong = 2.2, RowWander = 0.1, RowsOpen = 0.3, RowsSmall = 0.95, CrossOpen = 0.15, CrossSmall = 0.8, NoPlotsBelow = 0.02;
    const double CountryM = 1400, CountryReach = 0.75, HedgedShare = 0.7, WetReachM = 90;
    const int RunSteps = 4, RowSalt = 83492791, StepSalt = 2654435;
    const double FarmsApartFewM = 650, FarmsApartManyM = 200, FarmWander = 0.35, FarmsFew = 0.35, FarmsMany = 0.8, HamletsFrom = 0.66, HamletShare = 0.4;
    const double HouseLongM = 18, HouseDeepM = 9, BarnLongM = 14, BarnDeepM = 8, YardM = 34, LaneReachM = 700, LaneLeastM = 25, LaneBend = 0.15, HamletReachM = 45;
    const double HedgeTreesApartM = 70, HedgeTreeShare = 0.35, YardLeast = 0.85, YardMost = 1.25, FencedShare = 0.45, CourtShare = 0.3, OrchardShare = 0.4, OrchardLeastM = 22, OrchardMostM = 40;
    const double OffRoad = 0.9, PondShare = 0.15, PondLeastM = 6, PondMostM = 10, AroundFrom = 0.75, AroundTo = 1.05;
    const int HamletHouses = 4, CellA = 73856093, CellB = 19349663, MostRicks = 3, MostYardTrees = 3;

    static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    static bool Farmland(BuiltSite site, Spot p) => site.Buildable(p) && !site.InPlace(p) && site.KindAt(p) is GroundKinds.None or Grass or Field or Earth;

    static bool Passed(IReadOnlyList<Spot> stroke, double radius, Spot p)
    {
        if (stroke.Count == 1) return (p - stroke[0]).Length <= radius;
        for (int i = 0; i + 1 < stroke.Count; i++)
            if (p.FromSegment(stroke[i], stroke[i + 1]) <= radius) return true;
        return false;
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, IReadOnlyList<Spot> stroke, double radius, int seed, double plots, double buildings)
    {
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Details, Seed = seed, Settings = { [Plots] = plots, [Buildings] = buildings, [Radius] = radius, [Manners.Setting] = site.Manner },
            Outline = BuiltGeometry.Flat(stroke.Select(s => s.Stored)),
        };
        if (stroke.Count == 0) return thing;
        double x0 = stroke.Min(s => s.X) - radius, x1 = stroke.Max(s => s.X) + radius, y0 = stroke.Min(s => s.Y) - radius, y1 = stroke.Max(s => s.Y) + radius;
        Settle(site, thing.Parts, p => Passed(stroke, radius, p), (x0, y0, x1, y1), seed, plots, buildings);
        return thing;
    }

    public static BuiltThing Over(BuiltSite site, string sheet, Func<Spot, bool> passed, (double X0, double Y0, double X1, double Y1) box, int seed, double plots, double buildings)
    {
        var thing = new BuiltThing { Sheet = sheet, Kind = BuiltKinds.Details, Seed = seed, Settings = { [Plots] = plots, [Buildings] = buildings } };
        Settle(site, thing.Parts, passed, box, seed, plots, buildings);
        return thing;
    }

    static void Settle(BuiltSite site, List<BuiltPart> parts, Func<Spot, bool> passed, (double X0, double Y0, double X1, double Y1) box, int seed, double plots, double buildings)
    {
        if (plots > NoPlotsBelow) Boundaries(site, parts, passed, seed, plots, box);
        if (buildings > 0) Farms(site, parts, passed, seed, buildings, box);
    }

    static void Boundaries(BuiltSite site, List<BuiltPart> parts, Func<Spot, bool> passed, int seed, double plots, (double X0, double Y0, double X1, double Y1) box)
    {
        double country = site.M(CountryM);
        var roads = site.RoadSegments().ToList();
        for (int ry = (int)Math.Floor(box.Y0 / country); ry <= (int)Math.Floor(box.Y1 / country); ry++)
            for (int rx = (int)Math.Floor(box.X0 / country); rx <= (int)Math.Floor(box.X1 / country); rx++) Enclose(site, parts, passed, seed, plots, roads, rx, ry);
    }

    static void Enclose(BuiltSite site, List<BuiltPart> parts, Func<Spot, bool> passed, int seed, double plots, List<(Spot A, Spot B)> roads, int rx, int ry)
    {
        double country = site.M(CountryM), deep = site.M(Lerp(PlotOpenM, PlotSmallM, plots)), step = deep * PlotLong / 2;
        int here = unchecked(rx * CellA ^ ry * CellB);
        var dice = new Dice(seed + 1, here);
        var middle = new Spot((rx + 0.5) * country, (ry + 0.5) * country);
        var (to, reach) = Nearest(roads, middle);
        var along = reach < country && reach > deep ? (to - middle).Unit : Spot.Toward(dice.Between(0, Math.PI));
        var across = along.Left;
        string kept = dice.Chance(HedgedShare) ? "hedge" : "fence";
        int rows = (int)Math.Ceiling(country * CountryReach / deep), steps = (int)Math.Ceiling(country * CountryReach / step);
        Dice Of(int i, int j, int what) => new(seed + what, unchecked(here ^ i * StepSalt ^ j * RowSalt));
        Spot At(int i, int j) => middle + along * (i * step) + across * ((j + Of(i, j, 3).Between(-RowWander, RowWander)) * deep);
        bool Here(Spot q) => (int)Math.Floor(q.X / country) == rx && (int)Math.Floor(q.Y / country) == ry;
        bool Open(Spot a, Spot b) => site.WaterCrossing(a, b) == null && !roads.Any(r => BuiltSite.Crossing(a, b, r.A, r.B) != null);
        bool Holds(Spot a, Spot b) => Here(a) && Here(b) && passed(a) && passed(b) && Farmland(site, a) && Farmland(site, b) && Farmland(site, a + (b - a) * 0.5) && Open(a, b);
        Spot[] beside = [along * site.M(WetReachM), along * -site.M(WetReachM), across * site.M(WetReachM), across * -site.M(WetReachM)];
        bool Wetted(Spot q) => beside.Any(by => site.Wet(q + by));
        void Lay(List<Spot> line)
        {
            if (line.Count < 2) return;
            string cls = line.Any(Wetted) ? "ditch" : kept;
            parts.Add(BuiltSite.Line(cls, line));
            if (cls == "fence") return;
            for (int i = 0; i + 1 < line.Count; i++)
                for (int n = (int)((line[i + 1] - line[i]).Length / site.M(HedgeTreesApartM)); n > 0; n--)
                    if (dice.Chance(HedgeTreeShare)) parts.Add(BuiltSite.Sign("tree", line[i] + (line[i + 1] - line[i]) * dice.Next()));
        }
        for (int j = -rows; j <= rows; j++)
        {
            var line = new List<Spot>();
            for (int i = -steps; i < steps; i++)
            {
                var (a, b) = (At(i, j), At(i + 1, j));
                if (Of(Math.DivRem(i + steps, RunSteps).Quotient, j, 4).Chance(Lerp(RowsOpen, RowsSmall, plots)) && Holds(a, b))
                {
                    if (line.Count == 0) line.Add(a);
                    line.Add(b);
                    continue;
                }
                Lay(line);
                line = new List<Spot>();
            }
            Lay(line);
            int shift = Of(0, j, 5).Chance(0.5) ? 1 : 0;
            for (int i = -steps; i <= steps; i++)
                if ((i + steps + shift) % 2 == 0 && Of(i, j, 6).Chance(Lerp(CrossOpen, CrossSmall, plots)) && Holds(At(i, j), At(i, j + 1))) Lay([At(i, j), At(i, j + 1)]);
        }
    }

    static void Farms(BuiltSite site, List<BuiltPart> parts, Func<Spot, bool> passed, int seed, double buildings, (double X0, double Y0, double X1, double Y1) box)
    {
        double cell = site.M(Lerp(FarmsApartFewM, FarmsApartManyM, buildings)), share = Lerp(FarmsFew, FarmsMany, buildings);
        var roads = site.RoadSegments().ToList();
        for (int iy = (int)Math.Floor(box.Y0 / cell); iy <= (int)Math.Ceiling(box.Y1 / cell); iy++)
            for (int ix = (int)Math.Floor(box.X0 / cell); ix <= (int)Math.Ceiling(box.X1 / cell); ix++)
            {
                var dice = new Dice(seed + 2, unchecked(ix * CellA ^ iy * CellB));
                var at = new Spot((ix + 0.5 + dice.Between(-FarmWander, FarmWander)) * cell, (iy + 0.5 + dice.Between(-FarmWander, FarmWander)) * cell);
                if (!dice.Chance(share) || !passed(at) || !Farmland(site, at)) continue;
                var (to, reach) = Nearest(roads, at);
                if (reach < site.M(YardM) * OffRoad && reach > 1e-9)
                {
                    at = to + (at - to).Unit * (site.M(YardM) * OffRoad);
                    reach = site.M(YardM) * OffRoad;
                    if (!Farmland(site, at)) continue;
                }
                var front = reach < site.M(LaneReachM) ? (to - at).Unit.Left : Spot.Toward(dice.Between(0, Math.PI));
                Farm(site, parts, dice, at, front);
                if (buildings >= HamletsFrom && dice.Chance(HamletShare))
                    for (int h = 0; h < HamletHouses; h++)
                    {
                        var beside = at + Spot.Toward(dice.Between(0, 2 * Math.PI)) * site.M(dice.Between(HamletReachM * 0.6, HamletReachM * 1.4));
                        if (Farmland(site, beside)) parts.Add(BuiltSite.Block("house", beside, front.Turned(dice.Between(-0.5, 0.5)), site.M(HouseLongM * 0.8), site.M(HouseDeepM)));
                    }
                if (reach >= site.M(LaneReachM) || reach < site.M(LaneLeastM)) continue;
                var bend = at + (to - at) * 0.5 + (to - at).Unit.Left * (reach * dice.Between(-LaneBend, LaneBend));
                parts.Add(BuiltSite.Line("track", [at, bend, to]));
                roads.Add((at, bend));
                roads.Add((bend, to));
            }
    }

    static (Spot To, double Reach) Nearest(List<(Spot A, Spot B)> roads, Spot at)
    {
        Spot best = at;
        double least = double.MaxValue;
        foreach (var (a, b) in roads)
        {
            var ab = b - a;
            var near = a + ab * Math.Clamp((at - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if ((near - at).Length >= least) continue;
            least = (near - at).Length;
            best = near;
        }
        return (best, least);
    }

    static void Farm(BuiltSite site, List<BuiltPart> parts, Dice dice, Spot at, Spot front)
    {
        double yard = site.M(YardM) * dice.Between(YardLeast, YardMost);
        var side = front.Left;
        Spot ahead = front * (yard / 2), aside = side * (yard / 2);
        Spot[] ring = [at - ahead - aside, at + ahead - aside, at + ahead + aside, at - ahead + aside];
        parts.Add(BuiltSite.Area("yard", ring));
        if (dice.Chance(FencedShare)) parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
        var roll = BuiltSite.Roll(at, 89);
        if (site.Manner == Manners.Tribal) parts.Add(BuiltSite.Block("roundhouse", at - side * (yard * 0.3), front, site.M(HouseDeepM), site.M(HouseDeepM)));
        else if (site.Manner == Manners.Roman) parts.Add(BuiltSite.Block("villa", at - side * (yard * 0.26), front, site.M(HouseLongM), site.M(HouseLongM * 0.9)));
        else parts.Add(BuiltSite.Block("farm", at - side * (yard * 0.3), front, site.M(HouseLongM), site.M(HouseDeepM)));
        string first = Callings.Barn(site.Manner, roll), second = Callings.Barn(site.Manner, roll);
        if (site.Manner == Manners.Egyptian) parts.AddRange(Callings.Silos(site, at + front * (yard * 0.32) + side * (yard * 0.1), side, 3));
        else if (site.Manner == Manners.Tribal) parts.Add(BuiltSite.Block("roundhouse", at + front * (yard * 0.3) + side * (yard * 0.1), side, site.M(HouseDeepM * 0.8), site.M(HouseDeepM * 0.8)));
        else parts.Add(BuiltSite.Block(first == "cartshed" ? "barn" : first, at + front * (yard * 0.32) + side * (yard * 0.1), side, site.M(BarnLongM), site.M(BarnDeepM)));
        if (dice.Chance(0.5) && !Manners.Old(site.Manner))
            parts.Add(BuiltSite.Block(second, at - front * (yard * 0.32) + side * (yard * 0.15), side, site.M(BarnLongM * 0.8), site.M(BarnDeepM)));
        Callings.Farmyard(site, at, front, yard, parts);
        if (dice.Chance(CourtShare)) parts.Add(BuiltSite.Block("shed", at + side * (yard * 0.36), front, site.M(BarnLongM * 0.8), site.M(BarnDeepM * 0.6)));
        Around(site, parts, dice, at, front, yard);
    }

    static void Around(BuiltSite site, List<BuiltPart> parts, Dice dice, Spot at, Spot front, double yard)
    {
        var back = front.Left * -1;
        Spot Beside() => at + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (yard * dice.Between(AroundFrom, AroundTo));
        if (dice.Chance(OrchardShare))
        {
            double deep = site.M(dice.Between(OrchardLeastM, OrchardMostM));
            Spot near = at + back * (yard / 2 + site.M(2)), far = near + back * deep, aside = front * (yard / 2);
            if (Farmland(site, far))
            {
                parts.Add(BuiltSite.Area("orchard", [near - aside, near + aside, far + aside, far - aside]));
                Homes.Trees(site, dice, near, back, front, deep, yard, parts);
            }
        }
        for (int r = dice.Below(MostRicks + 1); r > 0; r--)
            if (Beside() is var rick && Farmland(site, rick)) parts.Add(BuiltSite.Sign("rick", rick));
        for (int t = 1 + dice.Below(MostYardTrees); t > 0; t--)
            if (Beside() is var tree && Farmland(site, tree)) parts.Add(BuiltSite.Sign("tree", tree));
        var pond = at + front * (yard * (dice.Chance(0.5) ? 1 : -1));
        if (dice.Chance(PondShare) && Farmland(site, pond)) parts.Add(BuiltSite.Area("pond", Outskirts.Blob(dice, pond, site.M(dice.Between(PondLeastM, PondMostM)))));
    }
}
