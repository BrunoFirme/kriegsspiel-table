namespace KTable.Core.Model;

static class Hairpins
{
    const double Design = 0.85, LegLeastM = 40, LegMostM = 180, LegShare = 1, TurnShare = 0.4, MergeM = 40, PadM = 15, Impassable = 0.7, Narrower = 0.6, Improved = 0.5;
    const double TurnExcessM = 1.5, TurnLeastM = 3, TurnMostM = 12, JoinM = 30;
    const double LeastRiseM = 6, SplitM = 3, StraightWithinM = 0.3, SampleM = 4;
    const int ArcSteps = 6, Widths = 3, MoreLegs = 4;

    internal static bool Climbs(string cls) => cls is "track" or "lane" or "road" or "chaussee";

    internal static List<Spot> Laid(BuiltSite site, List<Spot> path, double grade)
    {
        if (site.Grid == null || site.UnitsPerMetre <= 0 || site.HeightUnit <= 0 || path.Count < 2) return path;
        var p = Climb(site, path, grade);
        var runs = Steep(p).ToList();
        if (runs.Count == 0) return path;
        var laid = new List<Spot>();
        int next = 0;
        foreach (var (from, to) in runs)
        {
            if (from < next) continue;
            var (line, first, last) = Serpentine(site, p, from, to, grade, next) ?? (p.At.GetRange(from, to - from + 1), from, to);
            laid.AddRange(p.At.GetRange(next, first - next));
            laid.AddRange(line);
            next = last + 1;
        }
        laid.AddRange(p.At.Skip(next));
        return Profiles.Simplified(laid, site.M(StraightWithinM));
    }

    static Profile Climb(BuiltSite site, IReadOnlyList<Spot> path, double grade) =>
        WayClimbs.Net(site).Lay(path, grade, Grades.HalfM("road"), (_, _) => new Band(Hold.Banded, Grades.HeldM, Grades.HeldM));

    static double Excess(Profile p, int from, int to, double grade) =>
        Enumerable.Range(from, to - from).Sum(i => Math.Max(0, Math.Abs(p.Level[i + 1] - p.Level[i]) - grade * (p.Along[i + 1] - p.Along[i])));

    static IEnumerable<(int From, int To)> Steep(Profile p)
    {
        var merged = new List<(int From, int To)>();
        foreach (var (from, to) in Profiles.Runs(p.Count, i => p.Holds[i] == Hold.Natural))
        {
            int a = from, b = to;
            while (a > 0 && p.Along[from] - p.Along[a - 1] <= PadM) a--;
            while (b + 1 < p.Count && p.Along[b + 1] - p.Along[to] <= PadM) b++;
            if (merged.Count > 0 && p.Along[a] - p.Along[merged[^1].To] < MergeM) merged[^1] = (merged[^1].From, b);
            else merged.Add((a, b));
        }
        return merged.SelectMany(run => Split(p, run.From, run.To));
    }

    static IEnumerable<(int From, int To)> Split(Profile p, int from, int to)
    {
        double ends = Math.Max(p.Bare[from], p.Bare[to]), low = Math.Min(p.Bare[from], p.Bare[to]);
        int top = Enumerable.Range(from + 1, Math.Max(0, to - from - 1)).DefaultIfEmpty(from).MaxBy(i => p.Bare[i]);
        int bottom = Enumerable.Range(from + 1, Math.Max(0, to - from - 1)).DefaultIfEmpty(from).MinBy(i => p.Bare[i]);
        int at = p.Bare[top] > ends + SplitM ? top : p.Bare[bottom] < low - SplitM ? bottom : -1;
        if (at <= from || at >= to - 1) return [(from, to)];
        return [(from, at), (at + 1, to)];
    }

    static (List<Spot> Line, int First, int Last)? Serpentine(BuiltSite site, Profile p, int from, int to, double grade, int laidTo)
    {
        double perMetre = site.UnitsPerMetre, rise = Math.Abs(p.Bare[to] - p.Bare[from]);
        Spot a = p.At[from], b = p.At[to];
        double chord = (b - a).Length / perMetre, need = rise / (grade * Design);
        if (rise < LeastRiseM || need <= chord) return null;
        double across = Math.Sqrt(need * need - chord * chord), leg = Math.Clamp(across * LegShare, LegLeastM, LegMostM), slope = rise / chord;
        double turn = Math.Clamp(TurnExcessM / Math.Max(2 * slope - Math.PI * grade, 1e-9), TurnLeastM, TurnMostM) * perMetre;
        (List<Spot> Line, int First, int Last)? best = null;
        double least = Excess(p, from, to, grade) * Improved;
        for (int w = 0; w < Widths; w++, leg *= Narrower)
            for (int more = 0; more <= MoreLegs; more++)
                foreach (int side in new[] { 1, -1 })
                {
                    var bent = Joined(p, from, to, laidTo, Zigzag(a, b, (int)Math.Ceiling(across / leg) + more, leg / 2 * perMetre, side), turn);
                    if (!Clear(site, bent.Line)) continue;
                    var climb = Climb(site, bent.Line, grade);
                    double excess = Excess(climb, 0, climb.Count - 1, grade);
                    if (excess >= least) continue;
                    (best, least) = (bent, excess);
                    if (!climb.Holds.Contains(Hold.Natural)) return best;
                }
        return best;
    }

    static (List<Spot> Line, int First, int Last) Joined(Profile p, int from, int to, int laidTo, List<Spot> line, double turn)
    {
        int before = from, after = to;
        while (before > laidTo && p.Along[from] - p.Along[before] < JoinM) before--;
        while (after + 1 < p.Count && p.Along[after] - p.Along[to] < JoinM) after++;
        bool led = before < from, left = after > to;
        var bent = Bent([.. led ? [p.At[before]] : Array.Empty<Spot>(), .. line, .. left ? [p.At[after]] : Array.Empty<Spot>()], turn);
        return (bent.GetRange(led ? 1 : 0, bent.Count - (led ? 1 : 0) - (left ? 1 : 0)), led ? before + 1 : from, left ? after - 1 : to);
    }

    static List<Spot> Zigzag(Spot a, Spot b, int turns, double half, int side)
    {
        var along = b - a;
        var aside = along.Unit.Left;
        var line = new List<Spot> { a };
        for (int k = 1; k <= turns; k++) line.Add(a + along * ((k - 0.5) / turns) + aside * (half * side * (k % 2 == 1 ? 1 : -1)));
        line.Add(b);
        return line;
    }

    static List<Spot> Bent(List<Spot> line, double radius)
    {
        var bent = new List<Spot> { line[0] };
        for (int i = 1; i + 1 < line.Count; i++)
        {
            Spot corner = line[i], into = (corner - line[i - 1]).Unit, onto = (line[i + 1] - corner).Unit;
            double inner = Math.Acos(Math.Clamp(-into.Dot(onto), -1, 1));
            if (inner < 1e-6 || Math.PI - inner < 1e-6)
            {
                bent.Add(corner);
                continue;
            }
            double most = TurnShare * Math.Min((corner - line[i - 1]).Length, (line[i + 1] - corner).Length), tangent = Math.Min(radius / Math.Tan(inner / 2), most);
            double r = tangent * Math.Tan(inner / 2);
            Spot first = corner - into * tangent, last = corner + onto * tangent, centre = corner + (onto - into).Unit * (r / Math.Sin(inner / 2));
            double start = Math.Atan2(first.Y - centre.Y, first.X - centre.X), sweep = Math.IEEERemainder(Math.Atan2(last.Y - centre.Y, last.X - centre.X) - start, 2 * Math.PI);
            for (int k = 0; k <= ArcSteps; k++) bent.Add(centre + Spot.Toward(start + sweep * k / ArcSteps) * r);
        }
        bent.Add(line[^1]);
        return bent;
    }

    static bool Clear(BuiltSite site, List<Spot> line)
    {
        double step = site.M(SampleM);
        for (int i = 0; i + 1 < line.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((line[i + 1] - line[i]).Length / step));
            for (int k = 0; k <= steps; k++)
            {
                var at = line[i] + (line[i + 1] - line[i]) * (k / (double)steps);
                if (!site.Inside(at) || site.KindAt(at) == GroundKinds.Water || site.Downhill(at).Length > Impassable) return false;
            }
        }
        return true;
    }

}
