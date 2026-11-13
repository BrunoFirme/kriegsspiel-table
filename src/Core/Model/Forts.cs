namespace KTable.Core.Model;

public static class Forts
{
    public const string Bastions = "bastions", Outworks = "outworks";
    public const int FewestBastions = 4, MostBastions = 8;
    const double CurtainAt = 0.7, ShoulderAt = 0.84, FlankShare = 0.36, ShoulderShare = 0.3;
    public const double Reach = 1.42;
    const double MoatAt = 1.13, GlacisAt = 1.3, GlacisFootAt = Reach, RavelinTipAt = 1.12, RavelinBaseAt = 0.88, RavelinShare = 0.22;
    const double GlacisFrom = 0.1, RavelinsFrom = 0.35;
    const double ParadeAt = 0.3, BarracksAt = 0.52, BarrackDepthM = 10, BarrackMostM = 60, BarrackShare = 0.45, BarrackPlay = 0.7;
    const double FurnishedFromM = 90, CornerAt = 0.5, MagazineLongM = 12, MagazineDeepM = 8, QuartersLongM = 16, QuartersDeepM = 10, WellAt = 0.18;
    const int MostFurnishedSides = 6;

    public static List<Spot> Trace(Spot centre, double radius, int bastions, double turn)
    {
        var trace = new List<Spot>();
        double half = Math.PI / bastions;
        for (int i = 0; i < bastions; i++)
        {
            double a = turn + i * 2 * half;
            trace.Add(centre + Spot.Toward(a - half * FlankShare) * (radius * CurtainAt));
            trace.Add(centre + Spot.Toward(a - half * ShoulderShare) * (radius * ShoulderAt));
            trace.Add(centre + Spot.Toward(a) * radius);
            trace.Add(centre + Spot.Toward(a + half * ShoulderShare) * (radius * ShoulderAt));
            trace.Add(centre + Spot.Toward(a + half * FlankShare) * (radius * CurtainAt));
        }
        return trace;
    }

    public static bool Plain(BuiltThing thing) => thing.Kind == BuiltKinds.Fort && thing.Settings.GetValueOrDefault(Bastions) < FewestBastions;

    static List<Spot> Redoubt(Spot centre, double radius, double turn) =>
        Enumerable.Range(0, 4).Select(i => centre + Spot.Toward(turn + Math.PI / 4 + i * Math.PI / 2) * radius).ToList();

    static List<Spot> Closed(IEnumerable<Spot> ring)
    {
        var closed = ring.ToList();
        closed.Add(closed[0]);
        return closed;
    }

    static IEnumerable<Spot> Scaled(List<Spot> ring, Spot centre, double by) => ring.Select(p => centre + (p - centre) * by);

    public static List<BuiltPart> Works(Spot centre, double radius, int bastions, double turn, double outworks)
    {
        bool plain = bastions < FewestBastions;
        var trace = plain ? Redoubt(centre, radius, turn) : Trace(centre, radius, bastions, turn);
        var parts = new List<BuiltPart> { BuiltSite.Line("moat", Closed(Scaled(trace, centre, MoatAt))), BuiltSite.Line("rampart", Closed(trace)) };
        if (outworks >= GlacisFrom)
        {
            parts.Add(BuiltSite.Line("glacis", Closed(Scaled(trace, centre, GlacisAt))));
            parts.Add(BuiltSite.Line("glacis", Closed(Scaled(trace, centre, GlacisFootAt))));
        }
        if (plain || outworks < RavelinsFrom) return parts;
        double half = Math.PI / bastions;
        for (int i = 0; i < bastions; i++)
        {
            double mid = turn + (2 * i + 1) * half;
            parts.Add(BuiltSite.Line("rampart",
            [
                centre + Spot.Toward(mid - half * RavelinShare) * (radius * RavelinBaseAt), centre + Spot.Toward(mid) * (radius * RavelinTipAt),
                centre + Spot.Toward(mid + half * RavelinShare) * (radius * RavelinBaseAt),
            ]));
        }
        return parts;
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot centre, double radius, int seed, int bastions, double outworks, double turn)
    {
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Fort, Seed = seed, Settings = { [Bastions] = bastions, [Outworks] = outworks, ["turn"] = turn },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + Spot.Toward(turn) * radius).Stored]),
        };
        thing.Parts.AddRange(Works(centre, radius, bastions, turn, outworks));
        int sides = Math.Max(4, bastions);
        double inner = radius * (bastions < FewestBastions ? CurtainAt : CurtainAt * Math.Cos(Math.PI / sides));
        thing.Parts.Add(BuiltSite.Area("square", Enumerable.Range(0, sides).Select(i => centre + Spot.Toward(turn + (2 * i + 1) * Math.PI / sides) * (inner * ParadeAt))));
        double length = Math.Min(site.M(BarrackMostM), inner * BarrackShare);
        var dice = new Dice(seed);
        double gate = turn + Math.PI / sides + dice.Below(sides) * 2 * Math.PI / sides;
        for (int i = 0; i < sides; i++)
        {
            double a = turn + (2 * i + 1) * Math.PI / sides;
            var at = centre + Spot.Toward(a) * (inner * BarracksAt);
            if (site.Buildable(at)) thing.Parts.Add(BuiltSite.Block("barracks", at, Spot.Toward(a).Left, length * dice.Between(BarrackPlay, 1), site.M(BarrackDepthM)));
        }
        thing.Parts.Add(BuiltSite.Sign("bridge", centre + Spot.Toward(gate) * (radius * (bastions < FewestBastions ? Math.Cos(Math.PI / 4) : CurtainAt) * MoatAt), gate));
        thing.Parts.Add(BuiltSite.Sign("flag", centre));
        if (bastions >= FewestBastions && sides <= MostFurnishedSides && inner >= site.M(FurnishedFromM)) Furnish(site, dice, centre, turn, sides, inner, thing.Parts);
        return thing;
    }

    static void Furnish(BuiltSite site, Dice dice, Spot centre, double turn, int sides, double inner, List<BuiltPart> parts)
    {
        int first = dice.Below(sides);
        double Bearing(int i) => turn + (first + i) * 2 * Math.PI / sides;
        var magazine = centre + Spot.Toward(Bearing(0)) * (inner * CornerAt);
        if (site.Buildable(magazine)) parts.Add(BuiltSite.Block("magazine", magazine, Spot.Toward(Bearing(0)).Left, site.M(MagazineLongM), site.M(MagazineDeepM)));
        var quarters = centre + Spot.Toward(Bearing(sides / 2)) * (inner * CornerAt);
        if (site.Buildable(quarters)) parts.Add(BuiltSite.Block("public", quarters, Spot.Toward(Bearing(sides / 2)).Left, site.M(QuartersLongM), site.M(QuartersDeepM)));
        parts.Add(BuiltSite.Sign("well", centre + Spot.Toward(Bearing(1)) * (inner * WellAt)));
    }
}
