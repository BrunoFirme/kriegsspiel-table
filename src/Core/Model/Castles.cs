namespace KTable.Core.Model;

public static partial class Castles
{
    public const string RoundTower = "roundtower";
    public const string Motte = "motte", Enclosure = "enclosure", Quadrangle = "quadrangle", Concentric = "concentric", Spur = "spur", Kremlin = "kremlin", Kastron = "kastron",
        Citadel = "citadel", Nested = "nested";
    const double PassageReachM = 25, PassageBeyondM = 3;
    const double TurretM = 8, GateLongM = 11, GateDeepM = 7, HallLongM = 28, HallDeepM = 14, MotteShare = 0.3, RangeDeepM = 9, WellOffM = 6, Play = 0.12;
    const int RingSides = 24, DrumSides = 12;

    enum Towers { Round, Square, Drum }

    sealed record Style(string Wall, string Moat, Towers Towers, double ApartM, double TowerM);

    static Style StyleOf(BuiltSite site, string plan, Dice dice) => plan switch
    {
        Kremlin => new("palisade", "drymoat", Towers.Square, dice.Between(38, 60), dice.Between(7, 10)),
        Kastron => new("wall", "drymoat", Towers.Square, dice.Between(18, 26), dice.Between(6, 8)),
        Citadel => new("wall", "drymoat", Towers.Drum, dice.Between(24, 34), dice.Between(10, 14)),
        Nested => new("wall", Forts.Ditch, Towers.Square, dice.Between(60, 90), dice.Between(8, 11)),
        Motte => new(dice.Chance(0.5) ? "palisade" : "wall", Forts.Ditch, Towers.Round, dice.Between(30, 45), 0),
        _ => new("wall", dice.Chance(0.65) ? Forts.Ditch : "drymoat", dice.Chance(0.5) ? Towers.Round : dice.Chance(0.6) ? Towers.Square : Towers.Drum, dice.Between(26, 44),
            dice.Between(8, 11)),
    };

    static string Pick(BuiltSite site, Dice dice, double radius)
    {
        (string Plan, double Weight)[] plans = site.Manner switch
        {
            Manners.Rus => [(Kremlin, 1)],
            Manners.Byzantine => [(Kastron, 3), (Enclosure, 1), (Concentric, 1)],
            Manners.Saracen => [(Citadel, 3), (Concentric, 1)],
            Manners.Japanese or Manners.Nippon => [(Nested, 1)],
            Manners.Gothic => [(Motte, radius < site.M(60) ? 3 : 1), (Enclosure, 3), (Quadrangle, 2), (Concentric, 1), (Spur, 2)],
            _ => [(Quadrangle, 3), (Enclosure, 2), (Concentric, 1)],
        };
        double roll = dice.Next() * plans.Sum(p => p.Weight);
        foreach (var (plan, weight) in plans)
        {
            if (roll < weight) return plan;
            roll -= weight;
        }
        return plans[0].Plan;
    }

    static double Largest(string plan) => plan switch
    {
        Quadrangle => 48,
        Motte => 70,
        Spur => 85,
        Enclosure or Concentric or Citadel => 80,
        Kastron => 100,
        _ => 130,
    };

    static List<Spot> Polygon(Dice dice, Spot centre, Spot u, double along, double across, int sides, double play, double turn = 0) =>
        Enumerable.Range(0, sides).Select(i => turn + (i + dice.Between(-play, play) * 0.5) * 2 * Math.PI / sides)
            .Select(a => centre + (u * (Math.Cos(a) * along) + u.Left * (Math.Sin(a) * across)) * (1 + dice.Between(-play, play))).ToList();

    static List<Spot> Rectangle(Spot centre, Spot u, double along, double across) =>
        [centre - u * along - u.Left * across, centre + u * along - u.Left * across, centre + u * along + u.Left * across, centre - u * along + u.Left * across];

    static List<Spot> Scaled(IReadOnlyList<Spot> ring, Spot about, double share) => ring.Select(p => about + (p - about) * share).ToList();

    static Spot Middle(IReadOnlyList<Spot> ring) => new(ring.Average(p => p.X), ring.Average(p => p.Y));

    internal static BuiltPart Round(string cls, Spot centre, double radius, int sides = DrumSides) => new()
    {
        Kind = PartKinds.Block, Class = cls,
        At = BuiltGeometry.Flat(Enumerable.Range(0, sides).Select(i => (centre + Spot.Toward(i * 2 * Math.PI / sides) * radius).Stored)),
    };

    static void Tower(BuiltSite site, Style style, Spot at, Spot inward, double scale, List<BuiltPart> parts)
    {
        double size = site.M(Math.Max(style.TowerM, TurretM) * scale);
        switch (style.Towers)
        {
            case Towers.Round when scale <= 1.05:
                parts.Add(BuiltSite.Sign("tower", at));
                break;
            case Towers.Square:
                parts.Add(BuiltSite.Block("watchtower", at + inward * (size * 0.2), inward, size, size));
                break;
            default:
                parts.Add(Round(RoundTower, at + inward * (size * 0.15), size / 2));
                break;
        }
    }

    static void Curtain(BuiltSite site, Style style, IReadOnlyList<Spot> ring, List<BuiltPart> parts, double towers = 1)
    {
        var middle = Middle(ring);
        parts.Add(BuiltSite.Line(style.Wall, ring.Append(ring[0])));
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            Tower(site, style, a, (middle - a).Unit, towers, parts);
            int between = (int)((b - a).Length / site.M(style.ApartM));
            for (int k = 1; k < between; k++)
            {
                var at = a + (b - a) * (k / (double)between);
                Tower(site, style, at, (middle - at).Unit, towers * 0.85, parts);
            }
        }
    }

    static void Moat(Ground g, IReadOnlyList<Spot> trace, Forts.Profile shape)
    {
        var (site, style, parts) = (g.Site, g.Style, g.Parts);
        if (g.Crag) return;
        var body = new Forts.Body { Trace = trace, Ravelins = [], Gates = [], P = shape, Level = true, Blend = 0, GateHalf = 0, Whole = true };
        parts.Add(BuiltSite.Area(style.Moat, body.Ring(shape.Wide + shape.Scarp / 2, site.M(3))));
    }

    static bool Walled(BuiltPart p) => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade" or "wardwall";

    public static void Pierce(BuiltSite site, List<BuiltPart> parts)
    {
        var walls = parts.Where(Walled).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();
        var houses = parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse")
            .Select(p => Passage(BuiltGeometry.Points(p.At).Select(Spot.Of).ToList(), walls, site.M(PassageReachM), site.M(PassageBeyondM))).ToList();
        for (int i = parts.Count - 1; i >= 0 && houses.Count > 0; i--)
        {
            var line = parts[i];
            if (!Walled(line)) continue;
            var points = BuiltGeometry.Points(line.At).Select(Spot.Of).ToList();
            var runs = new List<List<Spot>> { points };
            foreach (var house in houses) runs = runs.SelectMany(run => Enclosures.Outside(house, run)).ToList();
            if (runs.Count == 1 && runs[0].SequenceEqual(points)) continue;
            parts.RemoveAt(i);
            parts.InsertRange(i, runs.Where(run => run.Count >= 2).Select(run => BuiltSite.Line(line.Class, run) is var piece ? Kept(piece, line.Kept) : piece));
        }
    }

    static List<Spot> Passage(List<Spot> ring, List<List<Spot>> walls, double reach, double beyond)
    {
        if (ring.Count != 4) return ring;
        var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
        var through = ((ring[1] - ring[0]).Length < (ring[2] - ring[1]).Length ? ring[1] - ring[0] : ring[2] - ring[1]).Unit;
        double half = ring.Max(p => Math.Abs(through.Dot(p - middle)));
        var legs = walls.SelectMany(w => w.Zip(w.Skip(1))).ToList();
        double Hit(Spot way) => legs.Select(leg => Along(middle, way, leg.First, leg.Second)).Where(d => d > half && d < half + reach).DefaultIfEmpty(double.MaxValue).Min();
        double ahead = Hit(through), behind = Hit(through * -1);
        if (Math.Min(ahead, behind) == double.MaxValue) return ring;
        var way = ahead <= behind ? through : through * -1;
        double grow = Math.Min(ahead, behind) - half + beyond;
        return ring.Select(p => way.Dot(p - middle) > 0 ? p + way * grow : p).ToList();
    }

    static double Along(Spot from, Spot way, Spot a, Spot b)
    {
        var leg = b - a;
        double cross = way.X * leg.Y - way.Y * leg.X;
        if (Math.Abs(cross) < 1e-12) return double.MaxValue;
        var off = a - from;
        double t = (off.X * leg.Y - off.Y * leg.X) / cross, u = (off.X * way.Y - off.Y * way.X) / cross;
        return u is >= 0 and <= 1 && t > 0 ? t : double.MaxValue;
    }

    static BuiltPart Kept(BuiltPart piece, bool kept)
    {
        piece.Kept = kept;
        return piece;
    }

    static Spot Gate(BuiltSite site, Dice dice, IReadOnlyList<Spot> ring, Spot centre, double bearing, Forts.Profile shape, bool bent, List<BuiltPart> parts, bool crag)
    {
        var way = Spot.Toward(bearing);
        var gate = Forts.Gate(site, ring, centre, bearing, false, true);
        if (!crag || Dug(site, parts, gate)) parts.Add(BuiltSite.Sign("bridge", gate, bearing));
        var house = gate - way * (shape.Wide / 2 + shape.Foot + site.M(GateDeepM / 2));
        parts.Add(BuiltSite.Block("gatehouse", house, way.Left, site.M(GateLongM * dice.Between(0.85, 1.3)), site.M(GateDeepM)));
        if (bent) parts.Add(BuiltSite.Block("watchtower", house - way * site.M(GateDeepM) + way.Left * site.M(GateLongM * 0.6), way, site.M(GateLongM), site.M(GateLongM * 0.8)));
        return gate;
    }

    static void Keep(BuiltSite site, Dice dice, Spot at, Spot u, double size, List<BuiltPart> parts)
    {
        if (site.Manner is Manners.Gothic && dice.Chance(0.35)) parts.Add(Round(RoundTower, at, size * 0.55, RingSides));
        else parts.Add(BuiltSite.Block("keep", at, u.Turned(dice.Between(-0.15, 0.15)), size, size * dice.Between(0.75, 1)));
    }

    static void Faith(BuiltSite site, Dice dice, Spot at, Spot u, List<BuiltPart> parts, double grand)
    {
        if (site.Epoch != Manners.Medieval && site.Manner is not (Manners.Japanese or Manners.Nippon))
        {
            parts.Add(BuiltSite.Block("barracks", at, u, site.M(36), site.M(8)));
            return;
        }
        if (site.Manner is Manners.Japanese or Manners.Nippon) return;
        Settlements.Chapel(site, at, u, parts, grand);
    }

    static void Yard(BuiltSite site, Dice dice, Spot at, Spot u, double room, List<BuiltPart> parts)
    {
        parts.Add(BuiltSite.Sign("well", at + u.Left * site.M(WellOffM)));
        if (dice.Chance(0.6)) parts.Add(BuiltSite.Block("stable", at - u * (room * 0.3), u, Math.Min(site.M(24), room * 0.6), site.M(8)));
        if (dice.Chance(0.5)) parts.Add(BuiltSite.Block("granary", at + u * (room * 0.3) - u.Left * site.M(8), u, site.M(10), site.M(7)));
        if (dice.Chance(0.4)) parts.Add(BuiltSite.Area("garden", Rectangle(at - u.Left * (room * 0.35), u, room * 0.2, room * 0.1)));
    }

    sealed record Ground(BuiltSite Site, Dice Dice, Style Style, Forts.Profile Shape, Spot Centre, double Radius, Spot U, List<BuiltPart> Parts, double Gate, Lie Lie)
    {
        public bool Crag => Lie.Crag;
    }

    static (List<Spot> Outer, double Gate) LayMotte(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        var mound = centre + u * (radius * 0.55);
        double hill = radius * MotteShare;
        outer = Polygon(dice, centre - u * (radius * 0.15), u, radius * 0.7, radius * 0.48, 10, Play);
        Moat(g, outer.Concat(Polygon(dice, mound, u, hill * 1.25, hill * 1.25, 10, 0.05)).ToList() is var both ? Enclosures.Hull(both) : outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        var top = Polygon(dice, mound, u, hill, hill, RingSides, 0.03);
        parts.Add(BuiltSite.Area(Forts.Platform, top));
        parts.Add(BuiltSite.Line(style.Wall, top.Append(top[0])));
        parts.Add(BuiltSite.Line(style.Wall, outer.Append(outer[0])));
        if (dice.Chance(0.6)) parts.Add(Round(RoundTower, mound, hill * 0.45, RingSides));
        else parts.Add(BuiltSite.Block("keep", mound, u, hill * 0.8, hill * 0.8));
        parts.Add(BuiltSite.Line("track", [mound - u * hill, centre + u * (radius * 0.2)]));
        parts.Add(BuiltSite.Block("public", centre - u * (radius * 0.2) + v * (radius * 0.18), u, site.M(HallLongM * 0.8), site.M(HallDeepM * 0.9)));
        Faith(site, dice, centre - u * (radius * 0.35) - v * (radius * 0.2), u, parts, 0.55);
        Yard(site, dice, centre - u * (radius * 0.05), u, radius * 0.5, parts);
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayQuadrangle(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        double along = radius * dice.Between(0.62, 0.8), across = along * dice.Between(0.72, 1);
        outer = Rectangle(centre, u, along, across);
        Moat(g, outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        Curtain(site, style with { Towers = style.Towers == Towers.Square ? Towers.Drum : style.Towers }, outer, parts, 1.3);
        double deep = site.M(RangeDeepM);
        foreach (int side in new[] { 1, -1 })
            if (dice.Chance(0.85)) parts.Add(BuiltSite.Block(side > 0 ? "public" : "stable", centre + v * (side * (across - deep / 2 - site.M(1.5))), u, along * 1.4, deep));
        parts.Add(BuiltSite.Block("public", centre + u * (along - deep / 2 - site.M(1.5)), v, across * 1.4, deep * 1.2));
        foreach (int side in new[] { 1, -1 })
            if (dice.Chance(0.6)) parts.Add(BuiltSite.Block("granary", centre - u * (along - deep / 2 - site.M(1.5)) + v * (side * across * 0.5), v, across * 0.6, deep * 0.9));
        Faith(site, dice, centre - u * (along * 0.45) + v * (across * 0.3), u, parts, 0.45);
        parts.Add(BuiltSite.Sign("well", centre));
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayConcentric(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        outer = Fitted(g, Polygon(dice, centre, u, radius * 0.9, radius * 0.78, dice.Chance(0.5) ? 4 : 6, 0.06, Math.PI / 4));
        var inner = Scaled(outer, centre, dice.Between(0.55, 0.65));
        Moat(g, outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        parts.Add(BuiltSite.Area(Forts.Platform, inner));
        Curtain(site, style, outer, parts, 0.95);
        Curtain(site, style with { ApartM = style.ApartM * 0.8 }, inner, parts, 1.25);
        Keep(site, dice, centre + u * (radius * 0.12), u, site.M(dice.Between(18, 24)), parts);
        parts.Add(BuiltSite.Block("public", centre - u * (radius * 0.18) + v * (radius * 0.12), u, site.M(HallLongM * 0.8), site.M(HallDeepM)));
        Faith(site, dice, centre - u * (radius * 0.1) - v * (radius * 0.2), u, parts, 0.5);
        Yard(site, dice, centre - u * (radius * 0.68), u, radius * 0.25, parts);
        if (site.Manner == Manners.Saracen) Settlements.Church(site, centre + v * (radius * 0.32), u, v, centre, radius * 2, parts, [], 0.5);
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LaySpur(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        outer = Fitted(g, [centre - u * (radius * 0.95), centre - u * (radius * 0.5) - v * (radius * 0.32), centre + u * (radius * 0.55) - v * (radius * 0.34),
            centre + u * (radius * 0.85), centre + u * (radius * 0.55) + v * (radius * 0.3), centre - u * (radius * 0.55) + v * (radius * 0.3)]);
        if (!g.Crag) parts.Add(BuiltSite.Area("drymoat", Rectangle(centre + u * (radius * 0.98), v, radius * 0.45, site.M(6))));
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        Curtain(site, style with { ApartM = style.ApartM * 1.4 }, outer, parts, 0.9);
        parts.Add(BuiltSite.Block("keep", centre + u * (radius * 0.6), v, site.M(dice.Between(12, 16)), site.M(dice.Between(12, 16))));
        parts.Add(BuiltSite.Line("wall", Chord(g, outer, centre + u * (radius * 0.45), [centre + u * (radius * 0.45) - v * (radius * 0.33), centre + u * (radius * 0.45) + v * (radius * 0.31)])));
        parts.Add(BuiltSite.Block("public", centre - u * (radius * 0.35), u, Math.Min(site.M(HallLongM), radius * 0.5), site.M(HallDeepM * 0.85)));
        Faith(site, dice, centre - u * (radius * 0.05) + v * (radius * 0.12), u, parts, 0.45);
        parts.Add(BuiltSite.Sign("well", centre + u * (radius * 0.15) - v * (radius * 0.1)));
        if (!g.Lie.Edge) gateBearing = Math.Atan2(-u.Y, -u.X);
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayKremlin(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        outer = Fitted(g, Polygon(dice, centre, u, radius * 0.92, radius * dice.Between(0.62, 0.8), 3 + dice.Below(3), Play * 1.5, dice.Between(0, Math.PI)));
        Moat(g, outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        Curtain(site, style, outer, parts);
        int churches = 1 + dice.Below(3);
        for (int i = 0; i < churches; i++) Settlements.Chapel(site, centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (radius * dice.Between(0.1, 0.35)), u, parts, i == 0 ? 1.1 : 0.6);
        parts.Add(BuiltSite.Block("public", centre - v * (radius * 0.35), u, site.M(HallLongM * 0.7), site.M(HallDeepM * 0.8)));
        for (int i = 0; i < 2 + dice.Below(3); i++) parts.Add(BuiltSite.Block("granary", centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (radius * 0.5), u, site.M(9), site.M(6)));
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayKastron(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        outer = Fitted(g, Polygon(dice, centre, u, radius * 0.9, radius * dice.Between(0.55, 0.8), 6 + dice.Below(4), Play * 1.8));
        Moat(g, outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        Curtain(site, style, outer, parts);
        var high = outer.MaxBy(site.HeightAt);
        parts.Add(BuiltSite.Block("keep", high + (centre - high).Unit * site.M(14), u, site.M(16), site.M(14)));
        Settlements.Chapel(site, centre - u * (radius * 0.15), u, parts, 0.5);
        parts.Add(BuiltSite.Sign("cistern", centre + v * (radius * 0.2)));
        for (int i = 0; i < 4
            + dice.Below(5); i++) parts.Add(BuiltSite.Block("house", centre + Spot.Toward(dice.Between(0, 2 * Math.PI)) * (radius * dice.Between(0.25, 0.55)), u, site.M(9), site.M(7)));
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayCitadel(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        outer = Fitted(g, Polygon(dice, centre, u, radius * 0.88, radius * dice.Between(0.62, 0.85), 4 + dice.Below(3), Play, Math.PI / 4));
        Moat(g, outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        Curtain(site, style, outer, parts, 1.2);
        parts.Add(BuiltSite.Block("cloister", centre + u * (radius * 0.22), u, site.M(30), site.M(26)));
        Settlements.Church(site, centre - u * (radius * 0.25) + v * (radius * 0.15), u, v, centre, radius * 2, parts, [], 0.5);
        parts.Add(BuiltSite.Sign("cistern", centre - v * (radius * 0.3)));
        parts.Add(BuiltSite.Block("barracks", centre - u * (radius * 0.4) - v * (radius * 0.25), u, site.M(26), site.M(8)));
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayNested(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        double along = radius * 0.9, across = radius * dice.Between(0.7, 0.85);
        if (g.Lie.Seat == Seat.Summit) centre -= u * (along * CoreAt);
        outer = Rectangle(centre, u, along, across);
        var middle = Rectangle(centre + u * (along * 0.22), u, along * 0.68, across * 0.66);
        var core = Rectangle(centre + u * (along * CoreAt), u, along * 0.36, across * 0.38);
        Moat(g, outer, shape);
        foreach (var ward in new[] { outer, middle, core })
        {
            parts.Add(BuiltSite.Area(Forts.Platform, ward));
            Curtain(site, style, ward, parts, ward == core ? 1.1 : 0.9);
        }
        var ring = Forts.Raised(radius / Math.Max(site.UnitsPerMetre, 1e-9), true).In(site.UnitsPerMetre > 0 ? site.UnitsPerMetre : 1);
        Moat(g, middle, ring);
        parts.Add(BuiltSite.Block("keep", centre + u * (along * 0.5), u, site.M(dice.Between(18, 24)), site.M(dice.Between(15, 20))));
        parts.Add(BuiltSite.Block("public", centre + u * (along * 0.3) - v * (across * 0.15), u, site.M(HallLongM), site.M(HallDeepM)));
        for (int i = 0; i < 3 + dice.Below(4); i++)
            parts.Add(BuiltSite.Block("house", centre - u * (along * dice.Between(0.2, 0.75)) + v * (across * dice.Between(-0.7, 0.7)), u, site.M(14), site.M(10)));
        Battered(g, [outer, middle, core]);
        return (outer, gateBearing);
    }

    static (List<Spot> Outer, double Gate) LayEnclosure(Ground g)
    {
        var (site, dice, style, shape, centre, radius, u, parts, gateBearing, _) = g;
        var v = u.Left;
        List<Spot> outer;
        int sides = 5 + dice.Below(4);
        outer = Fitted(g, Polygon(dice, centre, u, radius * 0.9, radius * dice.Between(0.6, 0.85) * Narrowed(g), sides, Play * 1.4));
        Moat(g, outer, shape);
        parts.Add(BuiltSite.Area(Forts.Platform, outer));
        Curtain(site, style, outer, parts);
        var strong = outer.MaxBy(p => site.HeightAt(p) + (p - centre).Dot(u) * 1e-3);
        Keep(site, dice, strong + (centre - strong).Unit * site.M(16), u, site.M(dice.Between(15, 22)), parts);
        parts.Add(BuiltSite.Block("public", centre - u * (radius * 0.1) + v * (radius * 0.22), u, Math.Min(site.M(HallLongM), radius * 0.4), site.M(HallDeepM)));
        Faith(site, dice, centre - u * (radius * 0.35) - v * (radius * 0.15), u, parts, 0.55);
        Yard(site, dice, centre - u * (radius * 0.3) + v * (radius * 0.05), u, radius * 0.4, parts);
        return (outer, gateBearing);
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot asked, double radius, int seed, double turn, BuiltThing thing, Forts.Profile shape, Spot centre, Lie lie,
        bool pierced = true)
    {
        var dice = new Dice(seed);
        string plan = Seated(site, lie, Pick(site, dice, radius));
        var style = StyleOf(site, plan, dice);
        if (lie.Seat == Seat.Wet) style = style with { Moat = Forts.Ditch };
        radius = Room(site, lie, plan, Math.Min(radius, site.M(Largest(plan) * dice.Between(0.8, 1.1))));
        thing.Settings["plan"] = Array.IndexOf(new[] { Motte, Enclosure, Quadrangle, Concentric, Spur, Kremlin, Kastron, Citadel, Nested }, plan);
        var u = lie.Crag ? lie.Axis : Spot.Toward(turn);
        var parts = thing.Parts;
        var g = new Ground(site, dice, style, shape, centre, radius, u, parts, lie.Crag ? lie.Gate : turn + Math.PI, lie);
        var (outer, gateBearing) = plan switch
        {
            Motte => LayMotte(g),
            Quadrangle => LayQuadrangle(g),
            Concentric => LayConcentric(g),
            Spur => LaySpur(g),
            Kremlin => LayKremlin(g),
            Kastron => LayKastron(g),
            Citadel => LayCitadel(g),
            Nested => LayNested(g),
            _ => LayEnclosure(g),
        };
        if (plan != Nested) Terraced(g, outer);
        Ditches(g, outer);
        if (plan == Citadel) Talused(g, outer);
        var gate = Gate(site, dice, outer, centre, gateBearing, shape, plan == Citadel, parts, g.Crag);
        if (plan == Concentric && dice.Chance(0.6)) parts.Add(BuiltSite.Block("watchtower", gate + Spot.Toward(gateBearing) * site.M(12), Spot.Toward(gateBearing).Left, site.M(12), site.M(9)));
        if (dice.Chance(0.35) && plan is Enclosure or Quadrangle or Kastron or Kremlin) Gate(site, dice, outer, centre, gateBearing + Math.PI * dice.Between(0.6, 1.4), shape, false, parts, g.Crag);
        if (pierced) Pierce(site, parts);
        var road = site.NearestRoad(centre) is var (to, off) && off > radius && off < radius * Forts.LinkReach ? to : (Spot?)null;
        var way = Spot.Toward(gateBearing);
        var beyond = gate + way * (shape.Scarp + site.M(14));
        parts.Add(BuiltSite.Line("road", road is { } joined ? [gate - way * site.M(4), beyond, joined] : [gate - way * site.M(4), beyond]));
        return thing;
    }
}
