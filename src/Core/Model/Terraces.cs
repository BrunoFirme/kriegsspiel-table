namespace KTable.Core.Model;

public static class Terraces
{
    public const string Terrace = "terrace", Wall = "terracewall", Paddy = "paddy", Pool = "pool", Vineyard = "vineyard", Garden = "garden";
    public const double From = 0.1;
    const double ColumnM = 5, LeastTreadM = 1.6, RiserStepM = 0.25, PaddyLongM = 26, Inset = 1e-4, Inner = 0.6, Thin = 1e-9;
    const double WallLeastM = 0.75, WallMostM = 2.5, VineWallMostM = 5.5, LynchetApartM = 16, LynchetLeastM = 0.5, LynchetMostM = 3, PaddyLeastM = 0.8, PaddyMostM = 2.4;
    const int Bisections = 16, FewestColumns = 6;

    readonly record struct Frame(Spot Origin, Spot Along, Spot Down, double Grade)
    {
        public Spot At(double along, double down) => Origin + Along * along + Down * down;
    }

    readonly record struct Column(double At, double Top, double Foot);

    public static double Steepest(BuiltSite site) => Slopes.Of(site.Manner).TerraceSteepest;

    public static bool Shapes(BuiltPart part) => part.Kind == PartKinds.Area && part.Class is Terrace or Paddy or Pool or Ponds.Held;

    public static bool Holds(BuiltPart part) => part.Kind == PartKinds.Area && part.Class is Paddy or Pool or Ponds.Held;

    public static Spot Middle(IReadOnlyList<Spot> ring) => new(ring.Average(p => p.X), ring.Average(p => p.Y));

    public static (Spot Down, double Grade) Fall(BuiltSite site, IReadOnlyList<Spot> ring)
    {
        var middle = Middle(ring);
        var sum = site.Downhill(middle);
        foreach (var p in ring) sum += site.Downhill(middle + (p - middle) * Inner);
        var fall = sum * (1.0 / (ring.Count + 1));
        return (fall.Unit, fall.Length);
    }

    public static bool Lies(BuiltSite site, IReadOnlyList<Spot> ring)
    {
        var (_, grade) = Fall(site, ring);
        return Slopes.Of(site.Manner).Fields != Terracing.None && grade >= From && grade <= Steepest(site);
    }

    public static List<BuiltPart> Cut(BuiltSite site, IReadOnlyList<Spot> ring, string crop)
    {
        var kind = Slopes.Of(site.Manner).Fields;
        if (site.Grid == null || ring.Count < 3 || !Lies(site, ring)) return [];
        var (down, grade) = Fall(site, ring);
        var frame = new Frame(Middle(ring), down.Left, down, grade);
        var columns = Columns(ring, frame, site.M(ColumnM));
        if (columns.Count < 2) return [];
        var edges = Edges(site, frame, columns, site.M(RiserM(kind, crop, grade, Steepest(site))), site.M(LeastTreadM));
        bool paddy = kind == Terracing.Paddy && crop == Garden;
        var parts = new List<BuiltPart>();
        for (int k = 0; edges.Count > 2 && k + 1 < edges.Count; k++) Band(frame, columns, edges[k], edges[k + 1], paddy ? site.M(PaddyLongM) : double.MaxValue, paddy, parts);
        return parts;
    }

    static double RiserM(Terracing kind, string crop, double grade, double steepest)
    {
        double steep = Math.Clamp((grade - From) / Math.Max(steepest - From, Thin), 0, 1);
        double metres = kind switch
        {
            Terracing.Lynchet or Terracing.EarthBank => Math.Clamp(grade * LynchetApartM, LynchetLeastM, LynchetMostM),
            Terracing.Paddy => Settlements.Lerp(PaddyLeastM, PaddyMostM, steep),
            _ when crop == Vineyard => Settlements.Lerp(WallLeastM, VineWallMostM, steep * steep),
            _ => Settlements.Lerp(WallLeastM, WallMostM, steep),
        };
        return Math.Max(RiserStepM, Math.Round(metres / RiserStepM) * RiserStepM);
    }

    static (double Top, double Foot)? Across(IReadOnlyList<Spot> ring, Frame f, double at)
    {
        double top = double.MaxValue, foot = double.MinValue;
        for (int i = 0; i < ring.Count; i++)
        {
            Spot p = ring[i], q = ring[(i + 1) % ring.Count];
            double pa = (p - f.Origin).Dot(f.Along) - at, qa = (q - f.Origin).Dot(f.Along) - at;
            if (pa * qa > 0 || pa == qa) continue;
            double t = (p + (q - p) * (pa / (pa - qa)) - f.Origin).Dot(f.Down);
            (top, foot) = (Math.Min(top, t), Math.Max(foot, t));
        }
        return top <= foot ? (top, foot) : null;
    }

    static List<Column> Columns(IReadOnlyList<Spot> ring, Frame f, double apart)
    {
        double from = ring.Min(p => (p - f.Origin).Dot(f.Along)), to = ring.Max(p => (p - f.Origin).Dot(f.Along)), inset = (to - from) * Inset;
        int n = Math.Max(FewestColumns, (int)Math.Ceiling((to - from) / apart));
        var columns = new List<Column>();
        for (int j = 0; j <= n; j++)
        {
            double at = Settlements.Lerp(from + inset, to - inset, j / (double)n);
            if (Across(ring, f, at) is { } span) columns.Add(new Column(at, span.Top, span.Foot));
        }
        return columns;
    }

    static double Crossing(BuiltSite site, Frame f, Column c, double level)
    {
        double At(double t) => site.HeightAt(f.At(c.At, t));
        if (At(c.Top) <= level) return c.Top;
        if (At(c.Foot) >= level) return c.Foot;
        double high = c.Top, low = c.Foot;
        for (int i = 0; i < Bisections; i++)
        {
            double mid = (high + low) / 2;
            (high, low) = At(mid) > level ? (mid, low) : (high, mid);
        }
        return (high + low) / 2;
    }

    static double Widest(double[] upper, double[] lower) => Enumerable.Range(0, upper.Length).Max(j => lower[j] - upper[j]);

    static List<double[]> Edges(BuiltSite site, Frame f, List<Column> columns, double riser, double least)
    {
        double top = columns.Max(c => site.HeightAt(f.At(c.At, c.Top))), bottom = columns.Min(c => site.HeightAt(f.At(c.At, c.Foot)));
        var edges = new List<double[]> { columns.Select(c => c.Top).ToArray() };
        for (double level = Math.Floor(top / riser) * riser; level > bottom; level -= riser)
        {
            var edge = columns.Select(c => Crossing(site, f, c, level)).ToArray();
            if (Widest(edges[^1], edge) >= least) edges.Add(edge);
        }
        var foot = columns.Select(c => c.Foot).ToArray();
        if (edges.Count > 1 && Widest(edges[^1], foot) < least) edges.RemoveAt(edges.Count - 1);
        edges.Add(foot);
        return edges;
    }

    static List<int> Longest(IEnumerable<int> open)
    {
        List<int> best = [], run = [];
        foreach (int j in open)
        {
            if (run.Count > 0 && j != run[^1] + 1) run = [];
            run.Add(j);
            if (run.Count > best.Count) best = run;
        }
        return best;
    }

    static void Band(Frame f, List<Column> columns, double[] upper, double[] lower, double longest, bool paddy, List<BuiltPart> parts)
    {
        var run = Longest(Enumerable.Range(0, columns.Count).Where(j => lower[j] - upper[j] > Thin));
        if (run.Count < 2) return;
        var top = run.Select(j => f.At(columns[j].At, upper[j])).ToList();
        var foot = run.Select(j => f.At(columns[j].At, lower[j])).ToList();
        for (int start = 0; start < run.Count - 1;)
        {
            int end = start + 1;
            while (end < run.Count - 1 && columns[run[end]].At - columns[run[start]].At < longest) end++;
            parts.Add(BuiltSite.Area(paddy ? Paddy : Terrace, top.GetRange(start, end - start + 1).Concat(Enumerable.Reverse(foot.GetRange(start, end - start + 1)))));
            start = end;
        }
        var wall = Longest(run.Where(j => lower[j] > columns[j].Top + Thin && lower[j] < columns[j].Foot - Thin));
        if (wall.Count > 1) parts.Add(BuiltSite.Line(Wall, wall.Select(j => f.At(columns[j].At, lower[j])).Reverse()));
    }
}
