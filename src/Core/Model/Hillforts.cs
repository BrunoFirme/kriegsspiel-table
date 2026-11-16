namespace KTable.Core.Model;

static class Hillforts
{
    const double RingsFrom = 0.62, RingsApart = 0.19, StaggerM = 16, HousesApartM = 34, DitchOffM = 11, ReachShare = 1.6, MarchM = 2, StepM = 6, GapM = 7, BeyondM = 20;
    const double NeckAt = 0.6, FlankDropM = 8, SpanShare = 1.2, BowShare = 0.7, TailShare = 0.45, InsideShare = 0.85, BehindNeckM = 10;
    const int RingSteps = 28, BowSteps = 14, HouseTries = 4, MostMarch = 400;

    public static bool Fits(Lie lie) => lie.Seat is Seat.Summit or Seat.Ridge or Seat.Spur;

    public static BuiltThing Lay(BuiltSite site, string sheet, Lie lie, double radius, int seed, double turn)
    {
        var thing = Defences.Thing(site, sheet, lie.Centre, radius, seed, turn, Defences.Hill);
        var dice = new Dice(seed);
        var inside = lie.Seat == Seat.Spur ? Promontory(site, lie, radius, thing.Parts) : Contoured(site, lie, radius, thing.Parts);
        Houses(site, dice, lie.Centre, radius, inside, thing.Parts);
        return thing;
    }

    static double Level(BuiltSite site, Spot top, double at) => Enumerable.Range(0, RingSteps).Average(i => site.HeightAt(top + Spot.Toward(i * 2 * Math.PI / RingSteps) * at));

    static Spot Crossing(BuiltSite site, Spot top, Spot way, double level, double most)
    {
        var before = top;
        for (int k = 1; k <= MostMarch && site.M(MarchM) * k <= most; k++)
        {
            var p = top + way * (site.M(MarchM) * k);
            if (!site.Inside(p)) return before;
            double h = site.HeightAt(p), was = site.HeightAt(before);
            if (h < level) return before + (p - before) * Math.Clamp((was - level) / Math.Max(was - h, 1e-12), 0, 1);
            before = p;
        }
        return before;
    }

    public static List<Spot> Ring(BuiltSite site, Spot top, double level, double most) =>
        Enumerable.Range(0, RingSteps).Select(i => Crossing(site, top, Spot.Toward(i * 2 * Math.PI / RingSteps), level, most)).ToList();

    static Func<Spot, bool> Contoured(BuiltSite site, Lie lie, double radius, List<BuiltPart> parts)
    {
        var top = lie.Centre;
        int rings = radius >= site.M(110) ? 3 : radius >= site.M(60) ? 2 : 1;
        List<Spot>? inner = null;
        Spot gate = top;
        for (int r = 0; r < rings; r++)
        {
            double at = radius * (RingsFrom + r * RingsApart);
            var ring = Ring(site, top, Level(site, top, at), at * ReachShare);
            inner ??= ring;
            double bearing = lie.Gate + r * site.M(StaggerM) / at;
            gate = ring.MinBy(p => Math.Abs(Math.IEEERemainder(Math.Atan2(p.Y - top.Y, p.X - top.X) - bearing, 2 * Math.PI)));
            foreach (var arc in Defences.Arcs(site, Defences.Stepped(ring, site.M(StepM)), [gate], site.M(GapM))) parts.Add(BuiltSite.Line("rampart", arc));
            var ditch = ring.Select(p => p + (p - top).Unit * site.M(DitchOffM)).ToList();
            var through = gate + (gate - top).Unit * site.M(DitchOffM);
            foreach (var arc in Defences.Arcs(site, Defences.Stepped(ditch, site.M(StepM)), [through], site.M(GapM))) parts.Add(BuiltSite.Line(Castles.Cut, arc));
        }
        parts.Add(BuiltSite.Line("track", [top, gate + (gate - top).Unit * site.M(DitchOffM + BeyondM)]));
        var held = inner!;
        return p => Enclosures.Inside(held, top + (p - top) * (1 / InsideShare));
    }

    static Func<Spot, bool> Promontory(BuiltSite site, Lie lie, double radius, List<BuiltPart> parts)
    {
        var (axis, across) = (lie.Axis, lie.Axis.Left);
        List<Spot> bank;
        Spot gate;
        if (lie.Edge)
        {
            gate = lie.Centre + axis * (radius * BowShare);
            bank = Enumerable.Range(0, BowSteps + 1).Select(i => Math.PI * (i / (double)BowSteps - 0.5))
                .Select(a => lie.Centre + axis * (Math.Cos(a) * radius * BowShare) + across * (Math.Sin(a) * radius * BowShare)).ToList();
            bank.Insert(0, bank[0] - axis * (radius * TailShare));
            bank.Add(bank[^1] - axis * (radius * TailShare));
        }
        else
        {
            gate = lie.Centre + axis * (radius * NeckAt);
            double floor = site.HeightAt(gate) - site.M(FlankDropM);
            bank = [Flank(site, gate, across * -1, radius * SpanShare, floor), gate, Flank(site, gate, across, radius * SpanShare, floor)];
        }
        var outward = bank.Select(p => lie.Edge ? (p - lie.Centre).Unit : axis).ToList();
        var ditch = bank.Select((p, i) => p + outward[i] * site.M(DitchOffM)).ToList();
        foreach (var run in Gapped(site, bank, gate)) parts.Add(BuiltSite.Line("rampart", run));
        foreach (var run in Gapped(site, ditch, gate + axis * site.M(DitchOffM))) parts.Add(BuiltSite.Line(Castles.Cut, run));
        parts.Add(BuiltSite.Line("track", [lie.Centre, gate + axis * site.M(DitchOffM + BeyondM)]));
        var (centre, neck) = (lie.Centre, gate);
        double bow = radius * BowShare * InsideShare, rim = site.HeightAt(gate) - site.M(FlankDropM);
        return lie.Edge ? p => (p - centre).Length < bow && (p - centre).Dot(axis) > -radius * TailShare
            : p => (p - neck).Dot(axis) < -site.M(BehindNeckM) && site.HeightAt(p) >= rim;
    }

    static Spot Flank(BuiltSite site, Spot from, Spot way, double most, double floor)
    {
        var end = from;
        for (int k = 1; site.M(MarchM) * k <= most; k++)
        {
            var p = from + way * (site.M(MarchM) * k);
            if (!site.Inside(p) || site.Wet(p) || site.HeightAt(p) < floor) break;
            end = p;
        }
        return end;
    }

    static IEnumerable<List<Spot>> Gapped(BuiltSite site, List<Spot> line, Spot gate)
    {
        var run = new List<Spot>();
        foreach (var p in Defences.Spaced(line, site.M(StepM)))
        {
            if ((p - gate).Length >= site.M(GapM) && site.Buildable(p))
            {
                run.Add(p);
                continue;
            }
            if (run.Count > 1) yield return run;
            run = new();
        }
        if (run.Count > 1) yield return run;
    }

    static void Houses(BuiltSite site, Dice dice, Spot centre, double radius, Func<Spot, bool> inside, List<BuiltPart> parts)
    {
        double reach = radius * RingsFrom, apart = site.M(HousesApartM);
        var taken = new List<(Spot At, double Reach)>();
        int wanted = (int)(Math.PI * reach * reach / (apart * apart));
        for (int tries = 0, laid = 0; laid < wanted && tries < wanted * HouseTries; tries++)
        {
            double a = dice.Between(0, 2 * Math.PI), far = reach * Math.Sqrt(dice.Next()), size = site.M(dice.Between(6, 10));
            var at = centre + Spot.Toward(a) * far;
            if (!inside(at) || !site.Buildable(at) || taken.Any(o => (o.At - at).Length < o.Reach + size)) continue;
            parts.Add(BuiltSite.Block("roundhouse", at, Spot.Toward(a), size, size));
            if (dice.Chance(0.5)) parts.Add(BuiltSite.Block("granary", at + Spot.Toward(a + 1.3) * (size * 0.5 + site.M(4)), Spot.Toward(a), site.M(3), site.M(3)));
            taken.Add((at, size * 0.6 + site.M(5)));
            laid++;
        }
    }
}
