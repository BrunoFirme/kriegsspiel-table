namespace KTable.Core.Model;

static class WayClimbs
{
    const double HouseNearM = 25, HollowLeastM = 25, HollowWindowM = 10, StairsGapM = 6, StraightWithinM = 0.2, HouseBucketM = 50;

    internal static WayNet Net(BuiltSite site)
    {
        double step = Math.Max(site.Width / Math.Max(1, site.Grid!.Cols - 1), site.Height / Math.Max(1, site.Grid.Rows - 1));
        return new WayNet(step, site.UnitsPerMetre, p => site.HeightAt(p) / site.UnitsPerMetre, p => site.KindAt(p) == GroundKinds.Water);
    }

    static bool Fits(BuiltSite site) => site.Grid != null && site.UnitsPerMetre > 0 && site.HeightUnit > 0;

    internal static void Lay(BuiltSite site, List<BuiltPart> parts)
    {
        if (!Fits(site)) return;
        var net = Net(site);
        var housed = Housed(site, parts);
        bool sinks = Grades.Sinks(site.Manner);
        var added = new List<BuiltPart>();
        foreach (var (part, profile) in WayMarks.Graded(parts, site.Manner, net, site.UnitsPerMetre).ToList())
        {
            var stepped = Grades.Climbs(part.Class) ? Steps(site, part.Class, profile).ToList() : [];
            added.AddRange(stepped.Select(s => s.Part));
            if (sinks && part.Class is "lane" or "track") added.AddRange(Sunk(site, profile, i => housed(profile.At[i]) || stepped.Any(s => i >= s.From && i <= s.To)));
        }
        parts.AddRange(added);
    }

    internal static IEnumerable<BuiltPart> Holloways(BuiltSite site, IReadOnlyList<Spot> path, string cls)
    {
        if (!Fits(site) || !Grades.Sinks(site.Manner) || cls is not ("lane" or "track") || path.Count < 2) return [];
        var profile = Net(site).Lay(path, Grades.Of(site.Manner, cls), Grades.HalfM(cls), (_, _) => new Band(Hold.Banded, Grades.HeldM, Grades.HeldM));
        return Sunk(site, profile, _ => false);
    }

    static IEnumerable<(BuiltPart Part, int From, int To)> Steps(BuiltSite site, string cls, Profile p)
    {
        var runs = Profiles.Runs(p.Count, p.Stepped).Select(r => (r.From, To: r.To + 1)).ToList();
        for (int k = 0; k < runs.Count; k++)
        {
            var (from, to) = runs[k];
            while (k + 1 < runs.Count && p.Along[runs[k + 1].From] - p.Along[to] < StairsGapM) to = runs[++k].To;
            double rise = Math.Abs(p.Bare[to] - p.Bare[from]), grade = rise / Math.Max(p.Along[to] - p.Along[from], 1e-9);
            string kind = Grades.Mule(site.Manner) && cls is "lane" or "path" && grade <= Grades.RampSteepest ? Grades.Ramp : Grades.Stairs;
            var points = Profiles.Simplified(p.At.GetRange(from, to - from + 1), site.M(StraightWithinM));
            if (p.Bare[to] < p.Bare[from]) points.Reverse();
            yield return (BuiltSite.Line(kind, points), from, to);
        }
    }

    static IEnumerable<BuiltPart> Sunk(BuiltSite site, Profile p, Func<int, bool> skipped)
    {
        var steep = new bool[p.Count];
        for (int i = 0, a = 0, b = 0; i < p.Count; i++)
        {
            while (p.Along[i] - p.Along[a] > HollowWindowM) a++;
            while (b + 1 < p.Count && p.Along[b + 1] - p.Along[i] <= HollowWindowM) b++;
            steep[i] = b > a && Math.Abs(p.Bare[b] - p.Bare[a]) / (p.Along[b] - p.Along[a]) > Grades.HollowFrom && !skipped(i);
        }
        foreach (var (from, to) in Profiles.Runs(p.Count, i => steep[i]))
            if (p.Along[to] - p.Along[from] >= HollowLeastM) yield return BuiltSite.Line(Grades.Holloway, Profiles.Simplified(p.At.GetRange(from, to - from + 1), site.M(StraightWithinM)));
    }

    static Func<Spot, bool> Housed(BuiltSite site, List<BuiltPart> parts)
    {
        double bucket = site.M(HouseBucketM), near = site.M(HouseNearM);
        var houses = new Dictionary<(int, int), List<(Spot At, double Reach)>>();
        foreach (var block in parts.Where(p => p.Kind == PartKinds.Block && p.At.Count >= 2).Select(BuiltSite.Round))
        {
            var key = ((int)Math.Floor(block.At.X / bucket), (int)Math.Floor(block.At.Y / bucket));
            if (!houses.TryGetValue(key, out var list)) houses[key] = list = new();
            list.Add(block);
        }
        return p =>
        {
            int bx = (int)Math.Floor(p.X / bucket), by = (int)Math.Floor(p.Y / bucket);
            for (int y = by - 1; y <= by + 1; y++)
                for (int x = bx - 1; x <= bx + 1; x++)
                    if (houses.TryGetValue((x, y), out var list) && list.Any(h => (h.At - p).Length < h.Reach + near)) return true;
            return false;
        };
    }
}
