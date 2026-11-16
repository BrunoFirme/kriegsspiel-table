namespace KTable.Core.Model;

static class Hairpins
{
    const double Design = 0.85, LegLeastM = 40, LegMostM = 180, LegShare = 0.25, TurnM = 6, TurnShare = 0.4, MergeM = 40, PadM = 15, Impassable = 0.7, Narrower = 0.6;
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
            laid.AddRange(p.At.GetRange(next, from - next));
            laid.AddRange(Serpentine(site, p, from, to, grade) ?? p.At.GetRange(from, to - from + 1));
            next = to + 1;
        }
        laid.AddRange(p.At.Skip(next));
        return Profiles.Simplified(laid, site.M(StraightWithinM));
    }

    static Profile Climb(BuiltSite site, IReadOnlyList<Spot> path, double grade) =>
        WayClimbs.Net(site).Lay(path, grade, Grades.HalfM("road"), (_, _) => new Band(Hold.Banded, Grades.HeldM, Grades.HeldM));

    static int Natural(Profile p, int from, int to) => Enumerable.Range(from, to - from + 1).Count(i => p.Holds[i] == Hold.Natural);

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

    static List<Spot>? Serpentine(BuiltSite site, Profile p, int from, int to, double grade)
    {
        double perMetre = site.UnitsPerMetre, rise = Math.Abs(p.Bare[to] - p.Bare[from]);
        Spot a = p.At[from], b = p.At[to];
        double chord = (b - a).Length / perMetre, need = rise / (grade * Design);
        if (rise < LeastRiseM || need <= chord) return null;
        double across = Math.Sqrt(need * need - chord * chord), leg = Math.Clamp(across * LegShare, LegLeastM, LegMostM);
        List<Spot>? best = null;
        int fewest = Natural(p, from, to);
        for (int w = 0; w < Widths; w++, leg *= Narrower)
            for (int more = 0; more <= MoreLegs; more++)
                foreach (int side in new[] { 1, -1 })
                {
                    var bent = Bent(Zigzag(a, b, (int)Math.Ceiling(across / leg) + more, leg / 2 * perMetre, side), TurnM * perMetre);
                    var climb = Climb(site, bent, grade);
                    int left = Natural(climb, 0, climb.Count - 1);
                    if (left >= fewest) continue;
                    (best, fewest) = (bent, left);
                    if (left == 0) return best;
                }
        return best;
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
