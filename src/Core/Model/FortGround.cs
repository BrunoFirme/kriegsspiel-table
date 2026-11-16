namespace KTable.Core.Model;

static class FortGround
{
    public const double DetachedFrom = 3;
    public const string Fleche = "fleche";
    const double CragGrade = 0.2, SteepFall = 0.35, LeastTip = 0.6, OverlookM = 25, DetachedReachM = 1500, DetachedM = 26, KnollReach = 2.5, KnollRiseM = 4;
    const double OpenGrade = 0.08, LeastFallM = 26, FootReach = 4, FlecheOutM = 50, FlecheApartM = 70, FlecheWingM = 24, FlecheDeepM = 16, FlecheDitchM = 6, CoveredShare = 1.7;
    const int Bearings = 16, Rings = 3, BrowSteps = 16, FootSteps = 24, FallSamples = 8;

    static IEnumerable<Spot> Round() => Enumerable.Range(0, Bearings).Select(i => Spot.Toward(i * 2 * Math.PI / Bearings));

    public static bool Steep(BuiltSite site, Spot centre, double girth) =>
        site.Grid != null && Round().SelectMany(way => Enumerable.Range(0, Rings + 1).Select(k => centre + way * (girth * k / Rings)))
            .Any(p => site.Inside(p) && site.Downhill(p).Length >= CragGrade);

    static bool Falling(BuiltSite site, Spot p, Spot way) => site.Inside(p) && site.Downhill(p).Dot(way) >= SteepFall;

    static bool Falls(BuiltSite site, Spot p, Spot way, double reach) => Enumerable.Range(0, FallSamples + 1).Any(k => Falling(site, p + way * (reach * k / FallSamples), way));

    public static Func<Spot, bool> Open(BuiltSite site, Spot centre, double reach)
    {
        reach = Math.Max(reach, site.M(LeastFallM));
        return p => site.Inside(p) && !site.Wet(p) && !Falls(site, p, (p - centre).Unit, reach);
    }

    public static List<(double Bearing, double Radius)> Tips(BuiltSite site, Spot centre, double radius, int bastions, double turn) =>
        Enumerable.Range(0, bastions).Select(i => turn + i * 2 * Math.PI / bastions).Select(a => (a, Reach(site, centre, Spot.Toward(a), radius))).ToList();

    static double Reach(BuiltSite site, Spot centre, Spot way, double radius)
    {
        for (int k = 1; k <= BrowSteps; k++)
        {
            double at = radius * k / BrowSteps;
            if (Falling(site, centre + way * at, way)) return Math.Max(radius * LeastTip, at - radius / BrowSteps);
        }
        return radius;
    }

    static bool High(BuiltSite site, Spot at, double radius) => Strongholds.SeatAt(site, at, radius) is Seat.Summit or Seat.Spur or Seat.Ridge;

    public static Spot? Knoll(BuiltSite site, Spot asked, double radius)
    {
        if (site.Grid == null || !site.Inside(asked)) return null;
        var top = Landforms.Of(site, asked, radius * KnollReach).Highest(radius * KnollReach);
        if (site.HeightAt(top) - site.HeightAt(asked) < site.M(KnollRiseM) && !High(site, asked, radius)) return null;
        if (!High(site, top, radius)) return null;
        return Strongholds.Holds(site, top, radius) ? top : null;
    }

    static Spot? Foot(BuiltSite site, Spot centre, Spot ahead, double radius)
    {
        for (int k = 1; k <= FootSteps; k++)
        {
            var at = centre + ahead * (radius * FootReach * k / FootSteps);
            if (!site.Inside(at)) return null;
            if (k * FootReach / FootSteps > 1 && site.Downhill(at).Length < OpenGrade) return at;
        }
        return null;
    }

    public static void Fleches(BuiltSite site, Spot centre, double radius, double turn, List<BuiltPart> parts)
    {
        var ahead = Spot.Toward(turn);
        if (Foot(site, centre, ahead, radius) is not { } foot) return;
        foreach (int side in new[] { 1, -1 })
        {
            var at = foot + ahead * site.M(FlecheOutM) + ahead.Left * (side * site.M(FlecheApartM / 2));
            if (!site.Free(at, site.M(FlecheWingM)) || site.Downhill(at).Length >= OpenGrade) continue;
            Spot salient = at + ahead * site.M(FlecheDeepM / 2), back = ahead * -site.M(FlecheDeepM), wing = ahead.Left * site.M(FlecheWingM);
            parts.Add(BuiltSite.Line(Fleche, [salient + back + wing, salient, salient + back - wing]));
            var off = site.M(FlecheDitchM);
            parts.Add(BuiltSite.Line("fosse", [salient + back + wing + wing.Unit * off, salient + ahead * off, salient + back - wing - wing.Unit * off]));
        }
    }

    public static void Detached(BuiltSite site, Spot centre, double girth, double turn, List<BuiltPart> parts)
    {
        if (site.Grid == null) return;
        var top = Landforms.Of(site, centre, site.M(DetachedReachM)).Highest(site.M(DetachedReachM));
        double reach = site.M(DetachedM);
        if ((top - centre).Length < girth * DetachedFrom || site.HeightAt(top) - site.HeightAt(centre) < site.M(OverlookM)) return;
        if (!High(site, top, reach) || !Strongholds.Holds(site, top, reach)) return;
        var trace = Forts.Redoubt(top, reach, turn);
        parts.AddRange(Forts.Works(site, top, trace, [], 0, [], Open(site, top, reach)));
        parts.Add(BuiltSite.Sign("flag", top));
        var way = (top - centre).Unit;
        parts.Add(BuiltSite.Line("track", [centre + way * girth, top - way * (reach * CoveredShare)]));
    }
}
