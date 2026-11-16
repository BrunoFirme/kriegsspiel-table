namespace KTable.Core.Model;

public static partial class Castles
{
    public const string Talus = "talus", Ishigaki = "ishigaki", Cut = "drymoat";
    const double CoreAt = 0.42, BrowDropM = 10, LeastFit = 0.6, DitchOffM = 14, LeastCutM = 4, GentleFall = 0.3, BridgeReachM = 18, TalusM = 22, StoneOffM = 1.5;
    const double RidgeNarrow = 0.6, RingStepM = 8, TerraceRiseM = 5;
    const int FitSteps = 12, FlankSteps = 24, MostTerraces = 3, CutTries = 4;

    static string Seated(BuiltSite site, Lie lie, string picked) => lie.Seat switch
    {
        Seat.Level => picked,
        _ when site.Manner == Manners.Rus => Kremlin,
        _ when site.Manner is Manners.Japanese or Manners.Nippon => Nested,
        Seat.Summit when site.Manner == Manners.Byzantine => Kastron,
        Seat.Summit or Seat.Spur when site.Manner == Manners.Saracen => Citadel,
        _ when site.Manner is Manners.Byzantine or Manners.Saracen => picked,
        Seat.Wet => Quadrangle,
        Seat.Summit => picked == Concentric ? Concentric : Enclosure,
        Seat.Spur => Spur,
        _ => Enclosure,
    };

    static List<Spot> Chord(Ground g, IReadOnlyList<Spot> ring, Spot at, List<Spot> plain)
    {
        if (!g.Crag) return plain;
        var across = g.U.Left * (g.Radius * 2);
        var hits = Enclosures.Hits(ring, at - across, at + across).OrderBy(p => (p - at).Dot(across)).ToList();
        return hits.Count >= 2 ? [hits[0], hits[^1]] : plain;
    }

    static List<Spot> Clip(IReadOnlyList<Spot> ring, Spot at, Spot keep)
    {
        var kept = new List<Spot>();
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            double da = (a - at).Dot(keep), db = (b - at).Dot(keep);
            if (da >= 0) kept.Add(a);
            if (da >= 0 != db >= 0) kept.Add(a + (b - a) * (da / (da - db)));
        }
        return kept;
    }

    static void Terraced(Ground g, IReadOnlyList<Spot> outer)
    {
        if (!g.Crag || g.Lie.Seat == Seat.Summit) return;
        var (site, u, centre) = (g.Site, g.U, g.Centre);
        double low = outer.Min(p => (p - centre).Dot(u)), high = outer.Max(p => (p - centre).Dot(u));
        double fall = Math.Abs(site.HeightAt(centre + u * high) - site.HeightAt(centre + u * low));
        int steps = Math.Clamp((int)Math.Round(fall / site.M(TerraceRiseM)), 1, MostTerraces);
        int at = g.Parts.FindIndex(p => p.Kind == PartKinds.Area && p.Class == Forts.Platform);
        if (steps < 2 || at < 0) return;
        g.Parts.RemoveAt(at);
        var spans = g.Parts.Where(p => p.Kind == PartKinds.Block).Select(p => BuiltGeometry.Points(p.At).Select(q => (Spot.Of(q) - centre).Dot(u)).ToList())
            .Select(d => (From: d.Min(), To: d.Max())).ToList();
        double Free(double cut) => Enumerable.Range(0, CutTries * 2 + 1).Select(k => cut + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * (high - low) / (steps * CutTries * 2))
            .FirstOrDefault(t => spans.All(s => t <= s.From || t >= s.To), cut);
        var cuts = Enumerable.Range(0, steps + 1).Select(i => i == 0 ? low : i == steps ? high : Free(low + (high - low) * i / steps)).ToList();
        for (int i = 0; i < steps; i++)
        {
            var slice = Clip(Clip(outer, centre + u * cuts[i], u), centre + u * cuts[i + 1], u * -1);
            if (slice.Count >= 3) g.Parts.Insert(at++, BuiltSite.Area(Forts.Platform, slice));
        }
    }

    static double Narrowed(Ground g) => g.Lie.Seat == Seat.Ridge ? RidgeNarrow : 1;

    static List<Spot> Fitted(Ground g, List<Spot> ring)
    {
        if (g.Lie.Seat is not (Seat.Summit or Seat.Spur or Seat.Ridge)) return ring;
        double floor = g.Site.HeightAt(g.Centre) - g.Site.M(BrowDropM);
        return ring.Select(corner => Brow(g.Site, g.Centre, corner, floor)).ToList();
    }

    static Spot Brow(BuiltSite site, Spot centre, Spot corner, double floor)
    {
        for (int k = 1; k <= FitSteps; k++)
            if (site.HeightAt(centre + (corner - centre) * (k / (double)FitSteps)) < floor) return centre + (corner - centre) * Math.Max(LeastFit, (k - 1) / (double)FitSteps);
        return corner;
    }

    static void Ditches(Ground g, IReadOnlyList<Spot> outer)
    {
        if (!g.Crag) return;
        var (site, u, centre) = (g.Site, g.U, g.Centre);
        double off = site.M(DitchOffM), side = outer.Max(p => Math.Abs((p - centre).Dot(u.Left))) + off;
        if (g.Lie.Seat == Seat.Summit)
        {
            Ringed(g, outer);
            return;
        }
        Across(g, centre + u * (outer.Max(p => (p - centre).Dot(u)) + off), u.Left, side);
        if (g.Lie.Seat == Seat.Ridge) Across(g, centre + u * (outer.Min(p => (p - centre).Dot(u)) - off), u.Left, side);
    }

    static void Across(Ground g, Spot at, Spot across, double side)
    {
        var site = g.Site;
        double floor = site.HeightAt(at) - site.M(BrowDropM);
        Spot End(int sign)
        {
            var end = at;
            for (int k = 1; k <= FlankSteps; k++)
            {
                var p = at + across * (sign * side * k / FlankSteps);
                if (!site.Inside(p) || site.Wet(p) || site.HeightAt(p) < floor) break;
                end = p;
            }
            return end;
        }
        var (left, right) = (End(1), End(-1));
        if ((left - right).Length > site.M(LeastCutM)) g.Parts.Add(BuiltSite.Line(Cut, [right, at, left]));
    }

    static void Ringed(Ground g, IReadOnlyList<Spot> outer)
    {
        var site = g.Site;
        var ring = Defences.Stepped(outer, site.M(RingStepM)).Select(p => p + (p - g.Centre).Unit * site.M(DitchOffM)).ToList();
        bool Gentle(Spot p) => site.Inside(p) && !site.Wet(p) && site.Downhill(p).Length < GentleFall;
        foreach (var run in Enclosures.Runs(ring, Gentle)) g.Parts.Add(BuiltSite.Line(Cut, run));
    }

    static bool Dug(BuiltSite site, List<BuiltPart> parts, Spot gate) =>
        parts.Where(p => p.Kind == PartKinds.Line && p.Class == Cut).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList())
            .Any(run => run.Zip(run.Skip(1)).Any(leg => gate.FromSegment(leg.First, leg.Second) < site.M(BridgeReachM)));

    static void Talused(Ground g, IReadOnlyList<Spot> outer)
    {
        if (g.Lie.Seat is not (Seat.Summit or Seat.Spur)) return;
        var middle = Middle(outer);
        g.Parts.Insert(0, BuiltSite.Area(Talus, outer.Select(p => p + (p - middle).Unit * g.Site.M(TalusM))));
    }

    static void Battered(Ground g, IEnumerable<List<Spot>> wards)
    {
        if (!g.Crag) return;
        foreach (var ward in wards)
        {
            var middle = Middle(ward);
            var stone = ward.Select(p => p + (p - middle).Unit * g.Site.M(StoneOffM)).ToList();
            if (Enclosures.Inside(stone, (stone[0] + stone[1]) * 0.5 + (stone[1] - stone[0]).Unit.Left * g.Site.M(StoneOffM))) stone.Reverse();
            g.Parts.Add(BuiltSite.Line(Ishigaki, stone.Append(stone[0])));
        }
    }
}
