namespace KTable.Core.Model;

public static class Watercourses
{
    public const string Leat = "leat", Channel = "channel", Arcade = "arcade", Weir = "weir";
    public const double LeatFall = 0.002, ChannelFall = 0.004, AqueductFall = 0.0003;
    const double Flat = 0.01, Keep = 0.6, StepM = 6, ArcadeFromM = 3, ArcadeMostM = 420, Detour = 1.5, TunnelM = 2.5, RoomM = 2.5, Saving = 0.9, Stall = 0.1;
    const int LookAhead = 80;

    public static bool Runs(BuiltPart part) => part.Kind == PartKinds.Line && part.Class is Leat or Channel;

    public static List<Spot> Along(BuiltSite site, Spot from, Spot heading, double level, double fall, double length, Func<Spot, bool>? stops = null)
    {
        var line = new List<Spot> { from };
        var (p, way) = (from, heading.Unit);
        double step = site.M(StepM), run = 0;
        while (run < length)
        {
            var down = site.Downhill(p);
            var contour = down.Length < Flat ? way : down.Unit.Left.Dot(way) >= 0 ? down.Unit.Left : down.Unit.Left * -1;
            var next = p + (contour + way * Keep).Unit * step;
            var slope = site.Downhill(next);
            if (slope.Length >= Flat) next += slope.Unit * Math.Clamp((site.HeightAt(next) - (level - fall * (run + step))) / slope.Length, -step, step);
            if (!site.Inside(next) || site.Wet(next) || stops?.Invoke(next) == true || (next - p).Length < step * Stall) break;
            (run, way, p) = (run + (next - p).Length, (next - p).Unit, next);
            line.Add(p);
        }
        return line;
    }

    public static List<Spot> Race(BuiltSite site, Spot intake, Spot wheel, Spot downstream, List<(Spot At, double Reach)> taken)
    {
        double reach = (wheel - intake).Dot(downstream);
        if (reach <= 0) return [intake, wheel];
        bool Stops(Spot p) => (p - intake).Dot(downstream) >= reach || taken.Any(o => (o.At - p).Length < o.Reach);
        var line = Along(site, intake, downstream, site.HeightAt(intake), LeatFall, reach * Detour, Stops);
        while (line.Count > 1 && site.HeightAt(line[^1]) < site.HeightAt(wheel)) line.RemoveAt(line.Count - 1);
        line.Add(wheel);
        return line;
    }

    public static void Taken(BuiltSite site, IReadOnlyList<Spot> line, List<(Spot At, double Reach)> taken)
    {
        foreach (var p in line) taken.Add((p, site.M(RoomM)));
    }

    public static (List<Spot> Line, List<List<Spot>> Arcades) Aqueduct(BuiltSite site, Spot tank, Spot way, double length)
    {
        double level = site.HeightAt(tank);
        var down = site.Downhill(tank);
        Spot[] sides = down.Length < Flat ? [way] : [down.Unit.Left, down.Unit.Left * -1];
        var course = sides.Select(s => Along(site, tank, s, level, -AqueductFall, length)).MaxBy(t => Run(t, 0, t.Count - 1) + (t[^1] - tank).Length)!;
        var line = new List<Spot> { course[0] };
        var arcades = new List<List<Spot>>();
        for (int i = 0; i < course.Count - 1;)
        {
            int j = Shortcut(site, course, i, level);
            if (j > i + 1 && Arched(site, course[i], course[j], level + AqueductFall * Run(course, 0, i)) is { } arcade) arcades.Add(arcade);
            line.Add(course[j]);
            i = j;
        }
        line.Reverse();
        arcades.ForEach(a => a.Reverse());
        return (line, arcades);
    }

    static double Run(List<Spot> line, int from, int to)
    {
        double run = 0;
        for (int k = from; k < to; k++) run += (line[k + 1] - line[k]).Length;
        return run;
    }

    static int Shortcut(BuiltSite site, List<Spot> course, int i, double level)
    {
        var saved = new List<(int At, double Saved)>();
        for (int j = i + 2; j < Math.Min(course.Count, i + LookAhead); j++)
        {
            double chord = (course[j] - course[i]).Length;
            if (chord > site.M(ArcadeMostM) || Run(course, i, j) < chord * Detour) continue;
            if (Highest(site, course[i], course[j]) <= level + AqueductFall * Run(course, 0, i) + site.M(TunnelM)) saved.Add((j, Run(course, i, j) - chord));
        }
        if (saved.Count == 0) return i + 1;
        double most = saved.Max(s => s.Saved);
        return saved.First(s => s.Saved >= most * Saving).At;
    }

    static List<Spot>? Arched(BuiltSite site, Spot a, Spot b, double level)
    {
        int steps = Math.Max(2, (int)Math.Ceiling((b - a).Length / site.M(StepM)));
        var deep = Enumerable.Range(0, steps + 1).Where(k => level - site.HeightAt(a + (b - a) * (k / (double)steps)) >= site.M(ArcadeFromM)).ToList();
        if (deep.Count == 0) return null;
        double from = Math.Max(0, deep[0] - 1) / (double)steps, to = Math.Min(steps, deep[^1] + 1) / (double)steps;
        return [a + (b - a) * from, a + (b - a) * to];
    }

    static IEnumerable<double> Under(BuiltSite site, Spot a, Spot b)
    {
        int steps = Math.Max(2, (int)Math.Ceiling((b - a).Length / site.M(StepM)));
        for (int k = 0; k <= steps; k++) yield return site.HeightAt(a + (b - a) * (k / (double)steps));
    }

    static double Highest(BuiltSite site, Spot a, Spot b) => Under(site, a, b).Max();
}
