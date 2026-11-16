namespace KTable.Core.Model;

public enum Seat { Level, Wet, Summit, Spur, Ridge, Slope }

public sealed record Lie(Seat Seat, Landform Form, Spot Centre, Spot Axis, double Gate)
{
    public bool Crag => Seat is Seat.Summit or Seat.Spur or Seat.Ridge or Seat.Slope;

    public bool Edge => Seat == Seat.Spur && Form == Landform.Rim;
}

public static class Strongholds
{
    const double SurveyShare = 2.5, TopReach = 0.8, SnapReach = 0.4, WetAboveM = 6, AxisReach = 0.6, HoldShare = 0.6, GateReach = 1.3, EdgeShift = 0.35;
    const double Leaning = 0.05, Sloping = 0.08, CrownM = 3, CrownShare = 0.75, ClimbShare = 0.25;
    const int Bearings = 16, SnapSteps = 8;

    public static Lie Level(Spot at, double turn) => new(Seat.Level, Landform.Plain, at, Spot.Toward(turn), turn + Math.PI);

    static double Bearing(Spot way) => Math.Atan2(way.Y, way.X);

    static IEnumerable<Spot> Round(int count = Bearings) => Enumerable.Range(0, count).Select(i => Spot.Toward(i * 2 * Math.PI / count));

    static Seat SeatOf(BuiltSite site, Landforms ground, Landform form, Spot at, double reach) => form switch
    {
        Landform.Rim when Crowned(site, at, reach) => Seat.Summit,
        Landform.Plain or Landform.Plateau or Landform.Terrace => Seat.Level,
        Landform.Floodplain => Seat.Wet,
        Landform.Floor when ground.AboveWater(at) is { } above && above < site.M(WetAboveM) => Seat.Wet,
        Landform.Summit or Landform.Knoll => Seat.Summit,
        Landform.Spur or Landform.Rim => Seat.Spur,
        Landform.Ridge => Seat.Ridge,
        _ => ground.Grade(at) < Leaning ? Seat.Level : Seat.Slope,
    };

    public static Landforms Survey(BuiltSite site, Spot at, double radius) => Landforms.Of(site, at, radius * SurveyShare);

    static bool Crowned(BuiltSite site, Spot at, double reach) => Round().Count(way => site.HeightAt(at + way * reach) < site.HeightAt(at) - site.M(CrownM)) >= Bearings * CrownShare;

    public static Seat SeatAt(BuiltSite site, Spot at, double radius)
    {
        if (site.Grid == null || !site.Inside(at)) return Seat.Level;
        var ground = Survey(site, at, radius);
        return SeatOf(site, ground, ground.At(at), at, radius * AxisReach);
    }

    public static Lie Read(BuiltSite site, Spot asked, double radius, double turn)
    {
        if (site.Grid == null || !site.Inside(asked)) return Level(asked, turn);
        var ground = Survey(site, asked, radius);
        var form = ground.At(asked);
        double reach = radius * AxisReach;
        var seat = SeatOf(site, ground, form, asked, reach);
        var heading = Spot.Toward(turn);
        switch (seat)
        {
            case Seat.Summit:
                var top = ground.Highest(radius * TopReach);
                return new(seat, form, top, heading, Gentlest(site, top, radius * GateReach));
            case Seat.Spur when form == Landform.Rim:
                var land = Landward(site, asked, reach);
                return new(seat, form, asked + land * (radius * EdgeShift), land, Bearing(land.Left));
            case Seat.Spur:
                var up = Uphill(site, asked, Crest(site, asked, reach), reach);
                return new(seat, form, Snap(site, asked, up.Left, radius * SnapReach), up, Bearing(up * -1));
            case Seat.Ridge:
                var along = Crest(site, asked, reach);
                along = along.Dot(heading) < 0 ? along * -1 : along;
                return new(seat, form, Snap(site, asked, along.Left, radius * SnapReach), along, Bearing(along * -1));
            case Seat.Slope:
                var fall = Fall(site, asked, reach);
                var uphill = fall.Length > 1e-12 ? (fall * -1).Unit : heading;
                return new(seat, form, asked, uphill, Bearing(uphill * -1));
            default:
                return new(seat, form, asked, heading, turn + Math.PI);
        }
    }

    public static Spot Fall(BuiltSite site, Spot p, double reach) =>
        new((site.HeightAt(p - new Spot(reach, 0)) - site.HeightAt(p + new Spot(reach, 0))) / (2 * reach),
            (site.HeightAt(p - new Spot(0, reach)) - site.HeightAt(p + new Spot(0, reach))) / (2 * reach));

    public static Spot Crest(BuiltSite site, Spot p, double reach) =>
        Round().Take(Bearings / 2).MaxBy(way => site.HeightAt(p + way * reach) + site.HeightAt(p - way * reach));

    static Spot Uphill(BuiltSite site, Spot p, Spot axis, double reach) => site.HeightAt(p + axis * reach) >= site.HeightAt(p - axis * reach) ? axis : axis * -1;

    public static Spot Landward(BuiltSite site, Spot p, double reach) => Round().MinBy(way => site.HeightAt(p + way * reach)) * -1;

    static Spot Snap(BuiltSite site, Spot p, Spot across, double reach) =>
        Enumerable.Range(-SnapSteps, SnapSteps * 2 + 1).Select(i => p + across * (reach * i / SnapSteps)).Where(site.Inside).DefaultIfEmpty(p).MaxBy(site.HeightAt);

    static double Gentlest(BuiltSite site, Spot top, double reach) => Bearing(Round().MaxBy(way => site.HeightAt(top + way * reach)));

    public static bool Holds(BuiltSite site, Spot centre, double radius) =>
        site.Free(centre, 0) && Round().Select(way => centre + way * (radius * HoldShare))
            .All(p => site.Inside(p) && !site.Wet(p) && !site.InPlace(p) && site.WaterCrossing(centre, p) == null);

    static bool Strong(int manner, Landform form) => manner == Manners.Rus ? form is Landform.Rim or Landform.Spur
        : form is Landform.Summit or Landform.Knoll or Landform.Spur or Landform.Rim or Landform.Ridge;

    static bool Held(Landform form) => form is Landform.Summit or Landform.Knoll or Landform.Spur or Landform.Rim or Landform.Ridge;

    public static Spot? Prominent(BuiltSite site, Spot centre, double within, IReadOnlyList<Spot> edge)
    {
        if (site.Grid == null || edge.Count == 0) return null;
        var ground = Landforms.Of(site, centre, within);
        var candidates = site.Manner is Manners.Byzantine or Manners.Japanese or Manners.Nippon ? edge.Append(ground.Highest(within)) : edge;
        var best = candidates.Select(p => (At: p, Form: ground.At(p))).Where(c => Held(c.Form)).OrderByDescending(c => Strong(site.Manner, c.Form))
            .ThenByDescending(c => site.HeightAt(c.At)).Select(c => ((Spot At, Landform Form)?)c).FirstOrDefault();
        if (best is not { } held) return null;
        return held.Form is Landform.Summit or Landform.Knoll ? Landforms.Of(site, held.At, within * ClimbShare).Highest(within * ClimbShare) : held.At;
    }

    public static bool Sloped(BuiltSite site, Spot at) => site.Grid != null && site.Downhill(at).Length >= Sloping;
}
