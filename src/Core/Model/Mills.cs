namespace KTable.Core.Model;

public static class Mills
{
    public const double LeastFallM = 0.3;
    const double StationM = 10, SpanM = 60, WeirUpM = 50, FallStepM = 0.25, StreamHalfM = 2, RiverHalfM = 8, WeirOutM = 2, ProbeM = 3, BankInM = 2, BankOutM = 6;

    public readonly record struct Fall(Spot At, Spot Flow, double Metres, string Class);

    static double Metres(BuiltSite site, double height) => height / Math.Max(site.UnitsPerMetre, 1e-12);

    static IEnumerable<(Spot A, Spot B, string Class)> Streams(BuiltSite site) => site.Waters.Where(w => w.Class is "stream" or "river");

    static Spot Downstream(BuiltSite site, Spot a, Spot b) => site.HeightAt(b) <= site.HeightAt(a) ? (b - a).Unit : (a - b).Unit;

    public static Fall? Steepest(BuiltSite site, Spot centre, double within, double least)
    {
        Fall? best = null;
        double half = site.M(SpanM / 2);
        foreach (var (a, b, cls) in Streams(site))
        {
            var flow = Downstream(site, a, b);
            int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / site.M(StationM)));
            for (int k = 0; k <= steps; k++)
            {
                var at = a + (b - a) * (k / (double)steps);
                double reach = (at - centre).Length, metres = Metres(site, site.HeightAt(at - flow * half) - site.HeightAt(at + flow * half));
                if (reach <= within && reach >= least && (best is not { } known || metres > known.Metres)) best = new Fall(at, flow, metres, cls);
            }
        }
        return best;
    }

    public static Fall Nearest(BuiltSite site, Spot bank)
    {
        var (a, b, cls) = Streams(site).Concat(site.Waters).MinBy(w => bank.FromSegment(w.A, w.B));
        return new Fall(bank, Downstream(site, a, b), 0, cls);
    }

    public static void Works(BuiltSite site, Fall at, Spot inland, Spot wheel, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var up = at.At - at.Flow * site.M(WeirUpM);
        if (site.NearestWater(up) is not var (weir, _) || !site.Inside(weir)) return;
        double half = site.M((at.Class == "river" ? RiverHalfM : StreamHalfM) + WeirOutM);
        var intake = weir + inland * half;
        parts.Add(BuiltSite.Line(Watercourses.Weir, [weir - inland * half, intake]));
        Raced(site, intake, wheel, at.Flow, parts, taken);
    }

    static void Raced(BuiltSite site, Spot intake, Spot wheel, Spot flow, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var race = Watercourses.Race(site, intake, wheel, flow, taken);
        parts.Add(BuiltSite.Line(Watercourses.Leat, race));
        Watercourses.Taken(site, race, taken);
    }

    static double Water(BuiltSite site, Surroundings.Bank bank) => site.HeightAt(bank.Land + bank.Way * site.M(ProbeM));

    static bool Beside(List<Surroundings.Bank> banks, int i, int j) => j >= 0 && j < banks.Count && Math.Abs(banks[j].Bearing - banks[i].Bearing) == Math.Abs(j - i);

    static double Falls(BuiltSite site, List<Surroundings.Bank> banks, int i)
    {
        int before = Beside(banks, i, i - 1) ? i - 1 : i, after = Beside(banks, i, i + 1) ? i + 1 : i;
        double span = (banks[after].Land - banks[before].Land).Length;
        if (span <= 0) return 0;
        return Math.Floor(Math.Abs(Metres(site, Water(site, banks[before]) - Water(site, banks[after]))) * site.M(SpanM) / span / FallStepM);
    }

    public static IEnumerable<int> ByFall(BuiltSite site, List<Surroundings.Bank> banks) =>
        Enumerable.Range(0, banks.Count).OrderByDescending(i => Falls(site, banks, i)).ThenByDescending(i => site.HeightAt(banks[i].Land));

    static int Upstream(BuiltSite site, List<Surroundings.Bank> banks, int i)
    {
        int before = Beside(banks, i, i - 1) ? i - 1 : i, after = Beside(banks, i, i + 1) ? i + 1 : i;
        return Water(site, banks[before]) >= Water(site, banks[after]) ? -1 : 1;
    }

    public static void Weir(BuiltSite site, List<Surroundings.Bank> banks, int i, Spot wheel, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        int way = Upstream(site, banks, i), k = i;
        while (Beside(banks, k, k + way) && Beside(banks, i, k + way) && (banks[k].Land - banks[i].Land).Length < site.M(WeirUpM)) k += way;
        var bank = banks[k];
        if (k == i) bank = bank with { Land = bank.Land + Surroundings.Tangent(banks, i) * site.M(WeirUpM) };
        var intake = bank.Land - bank.Way * site.M(BankInM);
        parts.Add(BuiltSite.Line(Watercourses.Weir, [intake, bank.Land + bank.Way * site.M(bank.WideM + BankOutM)]));
        Raced(site, intake, wheel, (banks[i].Land - bank.Land).Unit, parts, taken);
    }
}
