namespace KTable.Core.Model;

static class Railworks
{
    const double TunnelFromM = 20, ViaductFromM = 15, BankFromM = 1.2, BankRun = 1.5, HeavyMost = 0.2;
    const int Rounds = 8;

    sealed record Plan(List<Spot> At, double[] Along, double[] Bare, double[] Level, Hold[] Holds, bool[] Wet, double Heavy)
    {
        public int Count => At.Count;

        public double Change(int i) => Level[i] - Bare[i];

        public bool Tunnel(int i) => Holds[i] == Hold.Free && Bare[i] > Level[i];

        public bool Spanned(int i) => Holds[i] == Hold.Free && Bare[i] <= Level[i];

        public bool Banked(int i, int sign) => Holds[i] != Hold.Free && Change(i) * sign > BankFromM;

        public bool Plain(int i) => Holds[i] != Hold.Free && Math.Abs(Change(i)) <= BankFromM;
    }

    internal static List<BuiltPart> Laid(BuiltSite site, IReadOnlyList<Spot> path, string cls) => Laid(site, path, cls, out _);

    internal static List<BuiltPart> Laid(BuiltSite site, IReadOnlyList<Spot> path, string cls, out bool light)
    {
        light = true;
        if (site.Grid == null || site.UnitsPerMetre <= 0 || site.HeightUnit <= 0 || path.Count < 2) return [BuiltSite.Line(cls, path)];
        double step = WayClimbs.Net(site).Step;
        Plan? plan = null;
        foreach (double grade in Grades.Ruling(site.Manner, cls))
            if ((plan = Planned(site, step, path, grade)).Heavy <= HeavyMost) break;
        var parts = Parts(site, path, cls, plan!);
        light = parts.All(p => !Grades.Spanned(p.Class));
        return parts;
    }

    static Plan Planned(BuiltSite site, double step, IReadOnlyList<Spot> path, double grade)
    {
        var at = Profiles.Stations(path, step);
        int n = at.Count;
        double[] along = new double[n], bare = new double[n], pinned = new double[n], level = new double[n];
        var holds = new Hold[n];
        var wet = new bool[n];
        for (int i = 0; i < n; i++)
        {
            along[i] = i == 0 ? 0 : along[i - 1] + (at[i] - at[i - 1]).Length / site.UnitsPerMetre;
            bare[i] = site.HeightAt(at[i]) / site.UnitsPerMetre;
            wet[i] = site.KindAt(at[i]) == GroundKinds.Water;
            holds[i] = wet[i] ? Hold.Free : Hold.Banded;
        }
        var bands = Enumerable.Repeat(new Band(Hold.Banded, double.PositiveInfinity, double.PositiveInfinity), n).ToArray();
        for (int round = 0; round < Rounds; round++)
        {
            Profiles.Fit(along, bare, grade, bands, holds, pinned, level, false, Grades.AimM);
            var deep = Enumerable.Range(0, n).Where(i => holds[i] == Hold.Banded && bare[i] - level[i] > TunnelFromM).ToList();
            if (deep.Count == 0) deep = Enumerable.Range(0, n).Where(i => holds[i] == Hold.Banded && level[i] - bare[i] > ViaductFromM).ToList();
            if (deep.Count == 0) break;
            foreach (int i in deep) holds[i] = Hold.Free;
        }
        double heavy = 0;
        for (int i = 0; i + 1 < n; i++)
            if (holds[i] == Hold.Free && !wet[i] || holds[i + 1] == Hold.Free && !wet[i + 1]) heavy += along[i + 1] - along[i];
        return new Plan(at, along, bare, level, holds, wet, heavy / Math.Max(along[^1], 1e-9));
    }

    static List<BuiltPart> Parts(BuiltSite site, IReadOnlyList<Spot> path, string cls, Plan p)
    {
        double perMetre = site.UnitsPerMetre;
        double From(int i) => p.Along[Math.Max(0, i - 1)] * perMetre;
        double To(int i) => p.Along[Math.Min(p.Count - 1, i + 1)] * perMetre;
        var tunnels = Profiles.Runs(p.Count, p.Tunnel).ToList();
        var parts = new List<BuiltPart>();
        double laid = 0;
        foreach (var (from, to) in tunnels)
        {
            if (From(from) > laid) parts.Add(BuiltSite.Line(cls, Cut(path, laid, From(from))));
            parts.Add(BuiltSite.Line(Grades.Tunnel, Cut(path, From(from), To(to))));
            laid = To(to);
        }
        if (tunnels.Count == 0) parts.Add(BuiltSite.Line(cls, path));
        else if (laid < p.Along[^1] * perMetre) parts.Add(BuiltSite.Line(cls, Cut(path, laid, p.Along[^1] * perMetre)));
        foreach (var (from, to) in Profiles.Runs(p.Count, p.Spanned).Where(r => Enumerable.Range(r.From, r.To - r.From + 1).Any(i => !p.Wet[i])))
            parts.Add(BuiltSite.Line(Grades.Viaduct, Cut(path, From(from), To(to))));
        foreach (int sign in new[] { -1, 1 })
            foreach (var (from, to) in Profiles.Runs(p.Count, i => p.Banked(i, sign)))
                parts.Add(BuiltSite.Area(sign < 0 ? Grades.Cutting : Grades.Embankment, Ring(p, from > 0 && p.Plain(from - 1) ? from - 1 : from,
                    to + 1 < p.Count && p.Plain(to + 1) ? to + 1 : to, Grades.HalfM(cls) * perMetre, perMetre)));
        return parts;
    }

    internal static void Taken(IEnumerable<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        foreach (var bank in parts.Where(p => p.Kind == PartKinds.Area && Grades.Banked(p.Class)))
        {
            var ring = BuiltGeometry.Points(bank.At).Select(Spot.Of).ToList();
            (Spot At, double Reach)? last = null;
            for (int i = 0, n = ring.Count / 2; i < n; i++)
            {
                Spot a = ring[i], b = ring[ring.Count - 1 - i];
                var round = ((a + b) * 0.5, (a - b).Length * 0.5);
                if (last is { } known && (round.Item1 - known.At).Length < known.Reach * 0.5 && i < n - 1) continue;
                taken.Add(round);
                last = round;
            }
        }
    }

    static List<Spot> Ring(Plan p, int from, int to, double half, double perMetre)
    {
        var left = new List<Spot>();
        var right = new List<Spot>();
        for (int i = from; i <= to; i++)
        {
            var aside = (p.At[Math.Min(p.Count - 1, i + 1)] - p.At[Math.Max(0, i - 1)]).Unit.Left * (half + Math.Abs(p.Change(i)) * BankRun * perMetre);
            left.Add(p.At[i] + aside);
            right.Add(p.At[i] - aside);
        }
        right.Reverse();
        return [.. left, .. right];
    }

    internal static List<Spot> Cut(IReadOnlyList<Spot> path, double from, double to)
    {
        var cut = new List<Spot>();
        double gone = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            double leg = (path[i + 1] - path[i]).Length;
            if (gone + leg > from && gone <= to)
            {
                Spot At(double s) => path[i] + (path[i + 1] - path[i]) * (leg <= 0 ? 0 : Math.Clamp((s - gone) / leg, 0, 1));
                if (cut.Count == 0) cut.Add(At(from));
                if (gone + leg < to) cut.Add(path[i + 1]);
                else
                {
                    cut.Add(At(to));
                    break;
                }
            }
            gone += leg;
        }
        return cut;
    }
}
