namespace KTable.Core.Model;

public static class BuiltDetails
{
    public const string Plots = "plots", Buildings = "buildings", Radius = "radius";
    const byte Grass = 1, Field = 2, Earth = 7;
    const double PlotOpenM = 350, PlotSmallM = 80, PlotWander = 0.3, EdgesOpen = 0.12, EdgesSmall = 0.85, NoPlotsBelow = 0.02;
    const double FarmsApartFewM = 650, FarmsApartManyM = 200, FarmWander = 0.35, FarmsFew = 0.35, FarmsMany = 0.8, HamletsFrom = 0.66, HamletShare = 0.4;
    const double HouseLongM = 18, HouseDeepM = 9, BarnLongM = 14, BarnDeepM = 8, YardM = 34, LaneReachM = 700, LaneLeastM = 25, LaneBend = 0.15, HamletReachM = 45;
    const double HedgeShare = 0.45, FenceShare = 0.9;
    const int HamletHouses = 4, CellA = 73856093, CellB = 19349663;

    static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    static bool Farmland(BuiltSite site, Spot p) => site.Buildable(p) && site.KindAt(p) is GroundKinds.None or Grass or Field or Earth;

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
            Sheet = sheet, Kind = BuiltKinds.Details, Seed = seed, Settings = { [Plots] = plots, [Buildings] = buildings, [Radius] = radius },
            Outline = BuiltGeometry.Flat(stroke.Select(s => s.Stored)),
        };
        if (stroke.Count == 0) return thing;
        double x0 = stroke.Min(s => s.X) - radius, x1 = stroke.Max(s => s.X) + radius, y0 = stroke.Min(s => s.Y) - radius, y1 = stroke.Max(s => s.Y) + radius;
        if (plots > NoPlotsBelow) Boundaries(site, thing.Parts, stroke, radius, seed, plots, (x0, y0, x1, y1));
        if (buildings > 0) Farms(site, thing.Parts, stroke, radius, seed, buildings, (x0, y0, x1, y1));
        return thing;
    }

    static Spot Corner(int seed, double cell, int ix, int iy, double wander)
    {
        var dice = new Dice(seed, unchecked(ix * CellA ^ iy * CellB));
        return new Spot((ix + dice.Between(-wander, wander)) * cell, (iy + dice.Between(-wander, wander)) * cell);
    }

    static void Boundaries(BuiltSite site, List<BuiltPart> parts, IReadOnlyList<Spot> stroke, double radius, int seed, double plots, (double X0, double Y0, double X1, double Y1) box)
    {
        double cell = site.M(Lerp(PlotOpenM, PlotSmallM, plots)), share = Lerp(EdgesOpen, EdgesSmall, plots);
        for (int iy = (int)Math.Floor(box.Y0 / cell); iy <= (int)Math.Ceiling(box.Y1 / cell); iy++)
            for (int ix = (int)Math.Floor(box.X0 / cell); ix <= (int)Math.Ceiling(box.X1 / cell); ix++)
            {
                var here = Corner(seed, cell, ix, iy, PlotWander);
                var dice = new Dice(seed + 1, unchecked(ix * CellA ^ iy * CellB));
                foreach (var next in new[] { Corner(seed, cell, ix + 1, iy, PlotWander), Corner(seed, cell, ix, iy + 1, PlotWander) })
                {
                    double pick = dice.Next();
                    var mid = here + (next - here) * 0.5;
                    if (!dice.Chance(share) || !Passed(stroke, radius, here) || !Passed(stroke, radius, next) || !Farmland(site, mid) || !Farmland(site, here) || !Farmland(site, next)) continue;
                    parts.Add(BuiltSite.Line(pick < HedgeShare ? "hedge" : pick < FenceShare ? "fence" : "ditch", [here, next]));
                }
            }
    }

    static void Farms(BuiltSite site, List<BuiltPart> parts, IReadOnlyList<Spot> stroke, double radius, int seed, double buildings, (double X0, double Y0, double X1, double Y1) box)
    {
        double cell = site.M(Lerp(FarmsApartFewM, FarmsApartManyM, buildings)), share = Lerp(FarmsFew, FarmsMany, buildings);
        var roads = site.RoadSegments().ToList();
        for (int iy = (int)Math.Floor(box.Y0 / cell); iy <= (int)Math.Ceiling(box.Y1 / cell); iy++)
            for (int ix = (int)Math.Floor(box.X0 / cell); ix <= (int)Math.Ceiling(box.X1 / cell); ix++)
            {
                var dice = new Dice(seed + 2, unchecked(ix * CellA ^ iy * CellB));
                var at = new Spot((ix + 0.5 + dice.Between(-FarmWander, FarmWander)) * cell, (iy + 0.5 + dice.Between(-FarmWander, FarmWander)) * cell);
                if (!dice.Chance(share) || !Passed(stroke, radius, at) || !Farmland(site, at)) continue;
                var (to, reach) = Nearest(roads, at);
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
        double yard = site.M(YardM);
        var side = front.Left;
        parts.Add(BuiltSite.Area("yard", [at - front * (yard / 2) - side * (yard / 2), at + front * (yard / 2) - side * (yard / 2), at + front * (yard / 2) + side * (yard / 2),
            at - front * (yard / 2) + side * (yard / 2)]));
        parts.Add(BuiltSite.Block("farm", at - side * (yard * 0.3), front, site.M(HouseLongM), site.M(HouseDeepM)));
        parts.Add(BuiltSite.Block("barn", at + front * (yard * 0.32) + side * (yard * 0.1), side, site.M(BarnLongM), site.M(BarnDeepM)));
        if (dice.Chance(0.5)) parts.Add(BuiltSite.Block("barn", at - front * (yard * 0.32) + side * (yard * 0.15), side, site.M(BarnLongM * 0.8), site.M(BarnDeepM)));
    }
}
