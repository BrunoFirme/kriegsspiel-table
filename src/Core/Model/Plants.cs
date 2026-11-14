namespace KTable.Core.Model;

static class Plants
{
    const double ShareOfTown = 0.7, LeastM = 140, MostM = 420, DeepShare = 0.6, HallDeepLeastM = 32, HallDeepMostM = 56, HallGapM = 15, HallLongLeast = 0.5, HallLongMost = 0.7, HallsFrom = -0.2;
    const double HallsAside = -0.13, PowerLongM = 54, PowerDeepM = 22, PowerAt = 0.36, TowerM = 26, TanksAt = -0.4, TankM = 17, SidingsAt = -0.4, SidingM = 5.5, GateM = 16, OfficeLongM = 30;
    const double OfficeDeepM = 12, ForecourtLongM = 44, ForecourtDeepM = 18, HeapM = 34, BeyondTrackM = 24, BesideStationM = 120, EdgeFrom = 1.12, EdgeTo = 1.5, SpurJoinM = 60;
    const double RowsOffM = 16, RowApartM = 17, RowLongM = 48, RowDeepM = 9;
    const int MostHalls = 4, Checks = 5, Rows = 3;

    public static bool Heavy(int manner) => manner is Manners.Soviet or Manners.Western or Manners.Nippon;

    readonly record struct Frame(Spot At, Spot Along, Spot Out, double Long, double Deep)
    {
        public Spot On(double u, double v) => At + Along * (u * Long) + Out * (v * Deep);
    }

    static bool Fits(BuiltSite site, Frame f, List<(Spot At, double Reach)> taken)
    {
        for (int i = 0; i <= Checks; i++)
            for (int j = 0; j <= Checks; j++)
                if (!site.Free(f.On(i / (double)Checks - 0.5, j / (double)Checks - 0.5), 0)) return false;
        return !taken.Any(o => (o.At - f.At).Length < o.Reach + f.Deep * 0.5);
    }

    static Frame? Found(BuiltSite site, Dice roll, Spot centre, double within, (Spot At, Spot Along)? line, double longM, List<Settlements.Street> streets, List<(Spot At, double Reach)> taken)
    {
        double wide = site.M(longM), deep = wide * DeepShare;
        if (line is var (rail, way))
        {
            var away = way.Left.Dot(rail - centre) < 0 ? way.Left * -1 : way.Left;
            foreach (int beyond in new[] { 1, -1 })
                foreach (int side in new[] { 1, -1 })
                {
                    var frame = new Frame(rail + way * (side * (site.M(BesideStationM) + wide / 2)) + away * (beyond * (deep / 2 + site.M(BeyondTrackM))), way, away * beyond, wide, deep);
                    if (Fits(site, frame, taken)) return frame;
                }
        }
        if (Industry.Edge(site, roll, centre, within, deep * 0.5, taken, streets, EdgeFrom, EdgeTo) is not { } at) return null;
        var outward = (at - centre).Unit;
        var edge = new Frame(at + outward * (deep * 0.3), outward.Left, outward, wide, deep);
        return Fits(site, edge, taken) ? edge : null;
    }

    public static bool Lay(BuiltSite site, Dice roll, Spot centre, double within, (Spot At, Spot Along)? line, List<Settlements.Street> streets, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken)
    {
        double longM = Math.Clamp(within / site.UnitsPerMetre * ShareOfTown * Math.Min(1, site.Ways.Industry) * (0.6 + site.Town * 0.6), LeastM, MostM);
        if (Found(site, roll, centre, within, line, longM, streets, taken) is not { } f) return false;
        double m = site.UnitsPerMetre;
        parts.Add(BuiltSite.Area("works", [f.On(-0.5, -0.5), f.On(0.5, -0.5), f.On(0.5, 0.5), f.On(-0.5, 0.5)]));
        Spot gate = f.On(0, -0.5), post = f.Along * site.M(GateM / 2);
        parts.Add(BuiltSite.Line("fence", [gate + post, f.On(0.5, -0.5), f.On(0.5, 0.5), f.On(-0.5, 0.5), f.On(-0.5, -0.5), gate - post]));
        double deepM = f.Deep / m, room = deepM * (0.5 - HallsFrom) - 10;
        double hallDeep = Math.Clamp(room / 3, HallDeepLeastM, HallDeepMostM);
        int halls = Math.Clamp((int)(room / (hallDeep + HallGapM)), 1, MostHalls);
        for (int i = 0; i < halls; i++)
        {
            double v = HallsFrom + ((i + 0.5) * hallDeep + i * HallGapM) / deepM;
            parts.Add(BuiltSite.Block("factory", f.On(HallsAside + roll.Between(-0.03, 0.03), v), f.Along, f.Long * roll.Between(HallLongLeast, HallLongMost), site.M(hallDeep)));
        }
        parts.Add(BuiltSite.Block("powerhouse", f.On(PowerAt, -0.12), f.Out, site.M(PowerLongM), site.M(PowerDeepM)));
        int towers = site.Manner == Manners.Nippon ? 0 : longM > 200 ? 2 : 1;
        for (int i = 0; i < towers; i++) parts.Add(BuiltSite.Block("coolingtower", f.On(PowerAt + 0.02, 0.2 + i * (TowerM + 8) / deepM), f.Along, site.M(TowerM), site.M(TowerM)));
        foreach (int k in new[] { 0, 1 }) parts.Add(BuiltSite.Block("gasholder", f.On(TanksAt, 0.28) + f.Along * site.M(k * (TankM + 5)), f.Along, site.M(TankM), site.M(TankM)));
        if (line is var (rail, way))
        {
            var join = rail + way * ((f.At - rail).Dot(way) - Math.Sign((f.At - rail).Dot(way)) * (f.Long / 2 + site.M(SpurJoinM)));
            foreach (int track in new[] { 0, 1 })
                parts.Add(BuiltSite.Line("railway", [join, f.On(-0.46, SidingsAt) + f.Out * site.M(track * SidingM), f.On(0.46, SidingsAt) + f.Out * site.M(track * SidingM)]));
        }
        parts.Add(BuiltSite.Area("slagheap", Outskirts.Blob(roll, f.On(-0.36, -0.2), site.M(HeapM))));
        var office = gate - f.Out * site.M(ForecourtDeepM + OfficeDeepM / 2 + 4) + f.Along * site.M(ForecourtLongM / 2 + OfficeLongM / 2);
        if (site.Free(office, site.M(OfficeDeepM))) parts.Add(BuiltSite.Block("public", office, f.Along, site.M(OfficeLongM), site.M(OfficeDeepM)));
        var court = gate - f.Out * site.M(ForecourtDeepM / 2 + 2);
        parts.Add(BuiltSite.Area("square", Industry.Rect(court, f.Along, site.M(ForecourtLongM), site.M(ForecourtDeepM))));
        var (near, _, reach) = Settlements.Nearest(streets, court);
        if (streets.Count > 0 && reach > site.M(ForecourtDeepM))
            streets.Add(new Settlements.Street([court - f.Out * site.M(ForecourtDeepM / 2), near], site.Manner == Manners.Soviet ? "road" : "street", true, Settlements.NoSide));
        taken.Add((f.At, Math.Max(f.Long, f.Deep) * 0.56));
        taken.Add((court, site.M(ForecourtLongM * 0.6)));
        string rows = site.Manner == Manners.Soviet ? "barracks" : "terrace";
        for (int i = 0; i < Rows; i++)
            foreach (int side in new[] { -1, 1 })
            {
                var at = court - f.Out * site.M(ForecourtDeepM / 2 + RowsOffM + i * RowApartM) + f.Along * (side * site.M(ForecourtLongM / 2 + RowLongM / 2 + 6));
                if (!site.Free(at, site.M(RowDeepM)) || !site.Free(at + f.Along * site.M(RowLongM * 0.45), 0) || !site.Free(at - f.Along * site.M(RowLongM * 0.45), 0)) continue;
                if (taken.Any(o => (o.At - at).Length < o.Reach + site.M(RowDeepM)) || Settlements.FromStreets(streets, at) < site.M(RowDeepM)) continue;
                parts.Add(BuiltSite.Block(rows, at, f.Along, site.M(RowLongM), site.M(RowDeepM)));
                taken.Add((at, site.M(RowLongM * 0.3)));
            }
        return true;
    }
}
