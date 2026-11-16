namespace KTable.Core.Model;

static class Fieldworks
{
    public const string Escarp = "escarp", Cave = "cave", Tunnel = "tunnel", Lookout = "lookout";
    const double ReliefM = 8, FrontReach = 2.5, CrestShare = 0.22, ForwardShare = 0.5, EscarpShare = 0.78, ReverseShare = 0.4, WireShare = 0.15, LeastDropM = 3, HoldDropM = 2;
    const double WalkStepM = 6, Frontage = 1.1, HoldShare = 0.35, WireOffM = 35, CaveApartM = 30, CaveGrade = 0.3, CaveWideM = 4, CaveDeepM = 3, CommandBackM = 40;
    const double BayM = 7, TraverseM = 3.5, StepM = 2.6, CommsStepM = 14, CommsSwingM = 5, PitsApartM = 45, PitsBackM = 8, Flattest = 0.01, LevelTolM = 1.5, RiseTolM = 1;
    const double CommandLongM = 9, CommandDeepM = 7, BunkerM = 6, MineLongM = 60, MineDeepM = 26, TunnelReachM = 140, RingOutM = 40, CaveOutM = 20, FallingGrade = 0.08;
    const int Bearings = 16, MarchSteps = 48, Corrections = 4;

    sealed record Hill(Spot Top, Spot Front, double Fore, double Back, double Reach);

    static IEnumerable<Spot> Round() => Enumerable.Range(0, Bearings).Select(i => Spot.Toward(i * 2 * Math.PI / Bearings));

    public static bool Hilly(BuiltSite site, Spot centre, double radius)
    {
        if (site.Grid == null) return false;
        var heights = Round().Select(way => site.HeightAt(centre + way * radius)).Append(site.HeightAt(centre)).ToList();
        return heights.Max() - heights.Min() >= site.M(ReliefM);
    }

    static double Drop(BuiltSite site, Spot from, Spot way, double reach)
    {
        double high = site.HeightAt(from), low = high;
        for (int k = 1; k <= MarchSteps; k++)
        {
            var p = from + way * (reach * k / MarchSteps);
            if (!site.Inside(p) || site.HeightAt(p) > low + site.M(RiseTolM)) break;
            low = Math.Min(low, site.HeightAt(p));
        }
        return high - low;
    }

    static Spot? Down(BuiltSite site, Spot from, Spot way, double drop, double reach)
    {
        double level = site.HeightAt(from) - drop;
        for (int k = 1; k <= MarchSteps; k++)
        {
            var p = from + way * (reach * k / MarchSteps);
            if (!site.Inside(p)) return null;
            if (site.HeightAt(p) <= level) return p;
        }
        return null;
    }

    public static List<Spot> Contour(BuiltSite site, Spot start, double length)
    {
        double level = site.HeightAt(start), step = site.M(WalkStepM);
        var back = Walk(site, start, level, length / 2, step, -1);
        back.Reverse();
        return back.Concat(Walk(site, start, level, length / 2, step, 1).Skip(1)).ToList();
    }

    static List<Spot> Walk(BuiltSite site, Spot start, double level, double length, double step, int side)
    {
        var line = new List<Spot> { start };
        var p = start;
        Spot? heading = null;
        for (double gone = 0; gone < length; gone += step)
        {
            var fall = site.Downhill(p);
            if (fall.Length < Flattest) break;
            var along = fall.Unit.Left * side;
            if (heading is { } was && along.Dot(was) < 0) along = along * -1;
            if (Levelled(site, p + along * step, level, step) is not { } next || !site.Inside(next) || site.Wet(next)) break;
            heading = (next - p).Unit;
            line.Add(next);
            p = next;
        }
        return line;
    }

    static Spot? Levelled(BuiltSite site, Spot p, double level, double step)
    {
        for (int i = 0; i < Corrections; i++)
        {
            var fall = site.Downhill(p);
            if (fall.Length < Flattest) return null;
            p += fall.Unit * Math.Clamp((site.HeightAt(p) - level) / fall.Length, -step, step);
        }
        return Math.Abs(site.HeightAt(p) - level) <= site.M(LevelTolM) ? p : null;
    }

    static List<Spot>? Line(BuiltSite site, Hill hill, Spot way, double drop, double length)
    {
        if (Down(site, hill.Top, way, Math.Max(drop, site.M(LeastDropM)), hill.Reach) is not { } start) return null;
        var line = Contour(site, start, length);
        return line.Count >= 2 ? line : null;
    }

    static void Trench(BuiltSite site, List<Spot>? line, List<BuiltPart> parts)
    {
        if (line != null) parts.Add(BuiltSite.Line("trench", Defences.Crenellated(line, site.M(BayM), site.M(TraverseM), site.M(StepM))));
    }

    static void Comms(BuiltSite site, List<Spot>? from, Spot to, List<BuiltPart> parts)
    {
        if (from != null) parts.Add(BuiltSite.Line("trench", Defences.Zigzag(from[from.Count / 2], to, site.M(CommsStepM), site.M(CommsSwingM))));
    }

    public static bool Lay(BuiltSite site, Spot centre, double radius, double turn, List<BuiltPart> parts)
    {
        if (!Hilly(site, centre, radius)) return false;
        var front = Spot.Toward(turn);
        var top = Landforms.Of(site, centre, radius).Highest(radius);
        double reach = radius * FrontReach;
        var hill = new Hill(top, front, Drop(site, top, front, reach), Drop(site, top, front * -1, reach), reach);
        double length = radius * 2 * Frontage;
        var rear = top - front * site.M(CommandBackM) * (1 + ReverseShare);
        var main = Slopes.Of(site.Manner).Works switch
        {
            Doctrine.Forward => Forward(site, hill, length, rear, parts),
            Doctrine.Reverse => Reverse(site, hill, length, rear, parts),
            Doctrine.Caves => Caves(site, hill, length, rear, parts),
            _ => Crest(site, hill, length, rear, parts),
        };
        parts.Add(BuiltSite.Block("bunker", rear, front.Left, site.M(CommandLongM), site.M(CommandDeepM)));
        Pits(site, main, front, parts);
        return true;
    }

    static List<Spot>? Crest(BuiltSite site, Hill hill, double length, Spot rear, List<BuiltPart> parts)
    {
        var main = Line(site, hill, hill.Front, hill.Fore * CrestShare, length);
        Trench(site, main, parts);
        Comms(site, main, rear, parts);
        Wired(site, hill, main, parts);
        return main;
    }

    static List<Spot>? Forward(BuiltSite site, Hill hill, double length, Spot rear, List<BuiltPart> parts)
    {
        var second = Line(site, hill, hill.Front, hill.Fore * CrestShare, length);
        var first = Line(site, hill, hill.Front, hill.Fore * ForwardShare, length);
        Trench(site, second, parts);
        Trench(site, first, parts);
        Comms(site, first, second?[second.Count / 2] ?? hill.Top, parts);
        Comms(site, second, rear, parts);
        if (Line(site, hill, hill.Front, hill.Fore * EscarpShare, length * Frontage) is { } escarp) parts.Add(BuiltSite.Line(Escarp, escarp));
        Wired(site, hill, first ?? second, parts);
        return first ?? second;
    }

    static List<Spot>? Reverse(BuiltSite site, Hill hill, double length, Spot rear, List<BuiltPart> parts)
    {
        var main = Line(site, hill, hill.Front * -1, hill.Back * ReverseShare, length);
        var hold = Line(site, hill, hill.Front, site.M(HoldDropM), length * HoldShare);
        Trench(site, main, parts);
        Trench(site, hold, parts);
        Comms(site, hold, main?[main.Count / 2] ?? rear, parts);
        if (hold != null)
            foreach (var end in new[] { hold[0], hold[^1] }) parts.Add(BuiltSite.Sign(Lookout, end, Math.Atan2(hill.Front.Y, hill.Front.X)));
        if (Line(site, hill, hill.Front * -1, hill.Back * WireShare, length) is { } wire) parts.Add(BuiltSite.Line("wire", wire));
        return main;
    }

    static List<Spot>? Caves(BuiltSite site, Hill hill, double length, Spot rear, List<BuiltPart> parts)
    {
        var back = Line(site, hill, hill.Front * -1, hill.Back * ReverseShare, length);
        var face = Line(site, hill, hill.Front, hill.Fore * CrestShare, length);
        Trench(site, back, parts);
        var mouths = new List<Spot>();
        foreach (var line in new[] { face, back })
            if (line != null) mouths.AddRange(Mouths(site, line, parts));
        Tunnels(site, mouths, parts);
        Comms(site, back, rear, parts);
        return back ?? face;
    }

    static IEnumerable<Spot> Mouths(BuiltSite site, List<Spot> line, List<BuiltPart> parts)
    {
        var laid = new List<Spot>();
        foreach (var at in Defences.Spaced(line, site.M(CaveApartM)))
        {
            var fall = site.Downhill(at);
            if (fall.Length < CaveGrade || laid.Any(p => (p - at).Length < site.M(CaveApartM) * 0.8)) continue;
            parts.Add(Mouth(site, at, fall));
            laid.Add(at);
        }
        return laid;
    }

    public static BuiltPart Mouth(BuiltSite site, Spot at, Spot fall)
    {
        var into = (fall * -1).Unit;
        return BuiltSite.Block(Cave, at, new Spot(-into.Y, into.X), site.M(CaveWideM), site.M(CaveDeepM));
    }

    static void Tunnels(BuiltSite site, List<Spot> mouths, List<BuiltPart> parts)
    {
        for (int i = 1; i < mouths.Count; i++)
        {
            var near = mouths.Take(i).MinBy(p => (p - mouths[i]).Length);
            if ((near - mouths[i]).Length <= site.M(TunnelReachM)) parts.Add(BuiltSite.Line(Tunnel, [near, mouths[i]]));
        }
    }

    static double Long(List<Spot> line) => line.Zip(line.Skip(1)).Sum(leg => (leg.Second - leg.First).Length);

    static void Wired(BuiltSite site, Hill hill, List<Spot>? line, List<BuiltPart> parts)
    {
        if (line == null) return;
        var start = line[line.Count / 2] + hill.Front * site.M(WireOffM);
        if (!site.Inside(start) || site.Wet(start)) return;
        var wire = Contour(site, start, Long(line));
        if (wire.Count >= 2) parts.Add(BuiltSite.Line("wire", wire));
        var mines = start + hill.Front * site.M(MineDeepM);
        Spot along = hill.Front.Left * site.M(MineLongM / 2), deep = hill.Front * site.M(MineDeepM / 2);
        if (site.Free(mines, 0)) parts.Add(BuiltSite.Area("minefield", [mines - along - deep, mines + along - deep, mines + along + deep, mines - along + deep]));
    }

    static void Pits(BuiltSite site, List<Spot>? line, Spot front, List<BuiltPart> parts)
    {
        if (line == null) return;
        double bearing = Math.Atan2(front.Y, front.X);
        foreach (var at in Defences.Spaced(line, site.M(PitsApartM)).Skip(1).SkipLast(1))
            if (site.Free(at - front * site.M(PitsBackM), 0)) parts.Add(BuiltSite.Sign("gunpit", at - front * site.M(PitsBackM), bearing));
        var middle = line[line.Count / 2];
        if (site.Free(middle, 0)) parts.Add(BuiltSite.Block("bunker", middle + front * site.M(BunkerM * 0.4), front.Left, site.M(BunkerM), site.M(BunkerM)));
    }

    public static void Ring(BuiltSite site, IReadOnlyList<Spot> ring, List<BuiltPart> parts)
    {
        var middle = Enclosures.Middle(ring);
        double reach = ring.Max(p => (p - middle).Length);
        if (!Hilly(site, middle, reach)) return;
        var stepped = Defences.Stepped(ring, site.M(CaveApartM));
        var doctrine = Slopes.Of(site.Manner).Works;
        if (doctrine == Doctrine.Forward) Escarped(site, middle, stepped, parts);
        else if (doctrine == Doctrine.Caves) Delved(site, middle, stepped, parts);
        else if (doctrine == Doctrine.Reverse) Watched(site, middle, ring, parts);
    }

    static Spot Out(Spot middle, Spot p) => (p - middle).Unit;

    static void Escarped(BuiltSite site, Spot middle, List<Spot> stepped, List<BuiltPart> parts)
    {
        Spot Beyond(Spot p) => p + Out(middle, p) * site.M(RingOutM);
        bool Falling(Spot p) => site.Inside(Beyond(p)) && Out(middle, p).Dot(site.Downhill(Beyond(p))) >= FallingGrade;
        foreach (var run in Enclosures.Runs(stepped, Falling))
        {
            var start = Beyond(run[run.Count / 2]);
            if (site.Wet(start)) continue;
            var escarp = Contour(site, start, Long(run));
            if (escarp.Count >= 2) parts.Add(BuiltSite.Line(Escarp, escarp));
        }
    }

    static void Delved(BuiltSite site, Spot middle, List<Spot> stepped, List<BuiltPart> parts)
    {
        var mouths = new List<Spot>();
        foreach (var at in stepped.Select(p => p + Out(middle, p) * site.M(CaveOutM)).Where(p => site.Inside(p) && !site.Wet(p) && site.Downhill(p).Length >= CaveGrade))
        {
            parts.Add(Mouth(site, at, site.Downhill(at)));
            mouths.Add(at);
        }
        Tunnels(site, mouths, parts);
    }

    static void Watched(BuiltSite site, Spot middle, IReadOnlyList<Spot> ring, List<BuiltPart> parts)
    {
        var high = ring.MaxBy(site.HeightAt);
        parts.Add(BuiltSite.Sign(Lookout, high, Math.Atan2(Out(middle, high).Y, Out(middle, high).X)));
    }

    public static List<Spot>? Row(BuiltSite site, Spot at, double length)
    {
        if (!Strongholds.Sloped(site, at)) return null;
        var row = Contour(site, at, length);
        return row.Count >= 2 ? row : null;
    }
}
