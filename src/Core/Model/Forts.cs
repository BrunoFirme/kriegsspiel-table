namespace KTable.Core.Model;

public static class Forts
{
    public const string Bastions = "bastions", Outworks = "outworks", Form = "form";
    public const int FewestBastions = 4, MostBastions = 8;
    const double CurtainAt = 0.7, ShoulderAt = 0.84, FlankShare = 0.36, ShoulderShare = 0.3;
    public const double Reach = 1.42, CurtainClear = 0.65, LinkReach = 3, ShiftReach = 3;
    const double MoatAt = 1.13, GlacisAt = 1.3, GlacisFootAt = Reach, RavelinTipAt = 1.12, RavelinBaseAt = 0.88, RavelinShare = 0.22;
    const double GlacisFrom = 0.1, RavelinsFrom = 0.35;
    const double ParadeAt = 0.3, BarracksAt = 0.52, BarrackDepthM = 10, BarrackMostM = 60, BarrackShare = 0.45, BarrackPlay = 0.7;
    const double FurnishedFromM = 90, CornerAt = 0.5, MagazineLongM = 12, MagazineDeepM = 8, QuartersLongM = 16, QuartersDeepM = 10, WellAt = 0.18;
    const int MostFurnishedSides = 6;

    static double Gap(double from, double to)
    {
        double gap = ((to - from) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        return gap < 1e-9 ? 2 * Math.PI : gap;
    }

    static List<(double Bearing, double Radius)> Even(double radius, int bastions, double turn) =>
        Enumerable.Range(0, bastions).Select(i => (turn + i * 2 * Math.PI / bastions, radius)).ToList();

    public static List<Spot> Trace(Spot centre, double radius, int bastions, double turn) => Trace(centre, Even(radius, bastions, turn));

    public static List<Spot> Trace(Spot centre, IReadOnlyList<(double Bearing, double Radius)> tips)
    {
        var trace = new List<Spot>();
        for (int i = 0; i < tips.Count; i++)
        {
            var (a, radius) = tips[i];
            double before = Gap(tips[(i + tips.Count - 1) % tips.Count].Bearing, a) / 2, after = Gap(a, tips[(i + 1) % tips.Count].Bearing) / 2;
            trace.Add(centre + Spot.Toward(a - before * FlankShare) * (radius * CurtainAt));
            trace.Add(centre + Spot.Toward(a - before * ShoulderShare) * (radius * ShoulderAt));
            trace.Add(centre + Spot.Toward(a) * radius);
            trace.Add(centre + Spot.Toward(a + after * ShoulderShare) * (radius * ShoulderAt));
            trace.Add(centre + Spot.Toward(a + after * FlankShare) * (radius * CurtainAt));
        }
        return trace;
    }

    public static bool Plain(BuiltThing thing) => thing.Kind == BuiltKinds.Fort && thing.Settings.GetValueOrDefault(Bastions) < FewestBastions && !Camps.Is(thing);

    static List<Spot> Redoubt(Spot centre, double radius, double turn) =>
        Enumerable.Range(0, 4).Select(i => centre + Spot.Toward(turn + Math.PI / 4 + i * Math.PI / 2) * radius).ToList();

    static List<Spot> Closed(IEnumerable<Spot> ring)
    {
        var closed = ring.ToList();
        closed.Add(closed[0]);
        return closed;
    }

    static IEnumerable<Spot> Scaled(List<Spot> ring, Spot centre, double by) => ring.Select(p => centre + (p - centre) * by);

    public static List<BuiltPart> Works(Spot centre, double radius, int bastions, double turn, double outworks) =>
        bastions < FewestBastions ? Works(centre, Redoubt(centre, radius, turn), [], outworks) : Around(centre, Even(radius, bastions, turn), outworks);

    public static List<BuiltPart> Around(Spot centre, IReadOnlyList<(double Bearing, double Radius)> tips, double outworks) => Works(centre, Trace(centre, tips), tips, outworks);

    static List<BuiltPart> Works(Spot centre, List<Spot> trace, IReadOnlyList<(double Bearing, double Radius)> tips, double outworks)
    {
        var parts = new List<BuiltPart> { BuiltSite.Line("moat", Closed(Scaled(trace, centre, MoatAt))), BuiltSite.Line("rampart", Closed(trace)) };
        if (outworks >= GlacisFrom)
        {
            parts.Add(BuiltSite.Line("glacis", Closed(Scaled(trace, centre, GlacisAt))));
            parts.Add(BuiltSite.Line("glacis", Closed(Scaled(trace, centre, GlacisFootAt))));
        }
        if (outworks < RavelinsFrom) return parts;
        for (int i = 0; i < tips.Count; i++)
        {
            var next = tips[(i + 1) % tips.Count];
            double half = Gap(tips[i].Bearing, next.Bearing) / 2, mid = tips[i].Bearing + half, radius = (tips[i].Radius + next.Radius) / 2;
            parts.Add(BuiltSite.Line("rampart",
            [
                centre + Spot.Toward(mid - half * RavelinShare) * (radius * RavelinBaseAt), centre + Spot.Toward(mid) * (radius * RavelinTipAt),
                centre + Spot.Toward(mid + half * RavelinShare) * (radius * RavelinBaseAt),
            ]));
        }
        return parts;
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot asked, double radius, int seed, int bastions, double outworks, double turn)
    {
        var centre = site.DryNear(asked, radius * (bastions < FewestBastions ? 1 : MoatAt), radius * ShiftReach) ?? asked;
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Fort, Seed = seed, Settings = { [Bastions] = bastions, [Outworks] = outworks, ["turn"] = turn, [Manners.Setting] = site.Manner },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + Spot.Toward(turn) * radius).Stored]),
        };
        if (!site.Dry(centre, radius * (bastions < FewestBastions ? 1 : MoatAt))) return thing;
        thing.Parts.AddRange(Works(centre, radius, bastions, turn, outworks));
        int sides = Math.Max(4, bastions);
        double inner = radius * (bastions < FewestBastions ? CurtainAt : CurtainAt * Math.Cos(Math.PI / sides));
        thing.Parts.Add(BuiltSite.Area("square", Enumerable.Range(0, sides).Select(i => centre + Spot.Toward(turn + (2 * i + 1) * Math.PI / sides) * (inner * ParadeAt))));
        double length = Math.Min(site.M(BarrackMostM), inner * BarrackShare);
        var dice = new Dice(seed);
        int entry = dice.Below(sides);
        var road = site.NearestRoad(centre) is var (to, off) && off > radius && off < radius * LinkReach ? to : (Spot?)null;
        double Side(int i) => turn + (2 * i + 1) * Math.PI / sides;
        if (road is { } toward) entry = Enumerable.Range(0, sides).MinBy(i => Math.Abs(Math.IEEERemainder(Side(i) - Math.Atan2(toward.Y - centre.Y, toward.X - centre.X), 2 * Math.PI)));
        double gate = Side(entry);
        if (road is { } joined) thing.Parts.Add(BuiltSite.Line("road", [centre + Spot.Toward(gate) * (inner * ParadeAt), centre + Spot.Toward(gate) * (radius * MoatAt), joined]));
        for (int i = 0; i < sides; i++)
        {
            double a = Side(i);
            var at = centre + Spot.Toward(a) * (inner * BarracksAt);
            if (site.Buildable(at) && (road == null || i != entry)) thing.Parts.Add(BuiltSite.Block("barracks", at, Spot.Toward(a).Left, length * dice.Between(BarrackPlay, 1), site.M(BarrackDepthM)));
        }
        thing.Parts.Add(BuiltSite.Sign("bridge", centre + Spot.Toward(gate) * (radius * (bastions < FewestBastions ? Math.Cos(Math.PI / 4) : CurtainAt) * MoatAt), gate));
        thing.Parts.Add(BuiltSite.Sign("flag", centre));
        if (bastions >= FewestBastions) thing.Parts.AddRange(Props.Guns(centre, radius, bastions, turn));
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
