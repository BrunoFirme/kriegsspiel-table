namespace KTable.Core.Model;

public static class Enclosures
{
    const double BrowStepM = 5, BrowReachM = 40, BrowFall = 0.14;
    const double GateHalfM = 7, GatesApartM = 40, TowersApartM = 55, StepM = 12, DitchOffM = 12, PullBy = 0.06, GateReachM = 70, BeyondGateM = 16;
    const double GateLongM = 10, GateDeepM = 7, GapM = 6, HandApartM = 14, FlankReach = 3;
    const int Around = 8, PullSteps = 16;

    static double Cross(Spot o, Spot a, Spot b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public static List<Spot> Hull(IEnumerable<Spot> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3) return sorted;
        var hull = new List<Spot>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            int floor = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= floor + 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    public static Spot Middle(IReadOnlyList<Spot> ring) => new(ring.Average(p => p.X), ring.Average(p => p.Y));

    public static (Spot At, Spot Along, double Reach) Nearest(IReadOnlyList<Spot> ring, Spot p)
    {
        Spot at = p, along = new(1, 0);
        double least = double.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], ab = ring[(i + 1) % ring.Count] - a;
            var near = a + ab * Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if ((near - p).Length >= least) continue;
            (at, along, least) = (near, ab.Unit, (near - p).Length);
        }
        return (at, along, least);
    }

    public static bool Inside(IReadOnlyList<Spot> ring, Spot p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            if (ring[i].Y > p.Y != ring[j].Y > p.Y && p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X) inside = !inside;
        return inside;
    }

    public static List<Spot> Round(BuiltSite site, IEnumerable<Spot> points, double room)
    {
        var grown = points.SelectMany(p => Enumerable.Range(0, Around).Select(i => p + Spot.Toward(i * 2 * Math.PI / Around) * room));
        var hull = Hull(grown);
        if (hull.Count < 3) return hull;
        var middle = Middle(hull);
        for (int i = 0; i < hull.Count; i++)
        {
            for (int step = 0; step < PullSteps && (!site.Buildable(hull[i]) || site.WaterCrossing(middle, hull[i]) != null); step++) hull[i] += (middle - hull[i]) * PullBy;
            hull[i] = Brow(site, middle, hull[i]);
        }
        return hull;
    }

    public static Spot Brow(BuiltSite site, Spot middle, Spot corner)
    {
        var outward = (corner - middle).Unit;
        double step = site.M(BrowStepM);
        for (double far = 0; step > 0 && far <= site.M(BrowReachM); far += step)
        {
            var at = corner + outward * far;
            if (!site.Buildable(at) || site.WaterCrossing(middle, at) != null) break;
            if ((site.HeightAt(at) - site.HeightAt(at + outward * step)) / step > BrowFall) return at;
        }
        return corner;
    }

    const double BankStepM = 3;
    const int BankSteps = 80, SheerSamples = 3;

    static Spot Ashore(BuiltSite site, Spot middle, Spot p)
    {
        var inward = (middle - p).Unit;
        for (int step = 0; step < BankSteps && !site.Buildable(p); step++) p += inward * site.M(BankStepM);
        return p;
    }

    static bool Sheer(BuiltSite site, Spot a, Spot b) => Enumerable.Range(1, SheerSamples).Any(k => site.Downhill(a + (b - a) * (k / (SheerSamples + 1.0))).Length >= BuiltSite.SteepestBuilt);

    static List<Spot> Stepped(BuiltSite site, IReadOnlyList<Spot> ring)
    {
        var stepped = new List<Spot>();
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / site.M(StepM)));
            for (int s = 0; s < steps; s++) stepped.Add(a + (b - a) * (s / (double)steps));
        }
        return stepped;
    }

    internal static IEnumerable<Spot> Hits(IReadOnlyList<Spot> ring, Spot a, Spot b)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            Spot c = ring[i], d = ring[(i + 1) % ring.Count];
            double sc = Cross(c, d, a), sd = Cross(c, d, b);
            if (Cross(a, b, c) * Cross(a, b, d) < 0 && sc * sd < 0) yield return a + (b - a) * (sc / (sc - sd));
        }
    }

    internal static List<List<Spot>> Outside(IReadOnlyList<Spot> ring, IReadOnlyList<Spot> line)
    {
        var runs = new List<List<Spot>>();
        var run = new List<Spot>();
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Spot a = line[i], b = line[i + 1];
            var stops = Hits(ring, a, b).OrderBy(hit => (hit - a).Length).Prepend(a).Append(b).ToList();
            for (int k = 0; k + 1 < stops.Count; k++)
            {
                if (Inside(ring, (stops[k] + stops[k + 1]) * 0.5))
                {
                    if (run.Count > 1) runs.Add(run);
                    run = new();
                    continue;
                }
                if (run.Count == 0) run.Add(stops[k]);
                run.Add(stops[k + 1]);
            }
        }
        if (run.Count > 1) runs.Add(run);
        return runs;
    }

    static List<Spot> Gates(BuiltSite site, IReadOnlyList<Spot> ring, IEnumerable<Settlements.Street> streets, List<BuiltPart> parts)
    {
        var gates = new List<Spot>();
        void Open(Spot at)
        {
            if (!gates.Any(g => (g - at).Length < site.M(GatesApartM))) gates.Add(at);
        }
        foreach (var (a, b) in site.RoadSegments())
            foreach (var through in Hits(ring, a, b)) Open(through);
        foreach (var street in streets.Where(s => s.Class != "lane"))
        {
            for (int i = 0; i + 1 < street.Points.Count; i++)
                foreach (var crossed in Hits(ring, street.Points[i], street.Points[i + 1])) Open(crossed);
            if (!street.Laid) continue;
            foreach (var (end, before) in new[] { (street.Points[0], street.Points[1]), (street.Points[^1], street.Points[^2]) })
            {
                var way = (end - before).Unit;
                var ahead = Hits(ring, end, end + way * site.M(GateReachM)).ToList();
                if (!Inside(ring, end) || ahead.Count == 0) continue;
                var gate = ahead[0];
                parts.Add(BuiltSite.Line(street.Class, [end, gate + way * site.M(BeyondGateM)]));
                Open(gate);
            }
        }
        return gates;
    }

    static void Flanked(List<Spot> run, List<Spot> gates, double flank, double reach)
    {
        foreach (bool head in new[] { true, false })
        {
            var end = head ? run[0] : run[^1];
            var gate = gates.MinBy(g => (g - end).Length);
            double off = (gate - end).Length;
            if (off > reach || off <= flank) continue;
            var to = gate + (end - gate) * (flank / off);
            if (head) run.Insert(0, to);
            else run.Add(to);
        }
    }

    static List<List<Spot>> Works(BuiltSite site) =>
        site.Elders.Select(o => Walls.Of(o)?.Corners ?? (Forts.Of(o) is { } star ? Forts.Trace(star.Centre, star.Tips) : null)).Where(w => w is { Count: >= 3 }).Select(w => w!).ToList();

    const double JoinM = 45;

    static List<Spot>? Holding(BuiltSite site, List<List<Spot>> works, Spot p) => works.FirstOrDefault(w => Inside(w, p) || Nearest(w, p).Reach < site.M(JoinM));

    static Spot? Met(BuiltSite site, List<List<Spot>> works, Spot outside, Spot inside) =>
        Holding(site, works, inside) is not { } work ? null
            : Hits(work, outside, inside).OrderBy(hit => (hit - outside).Length).Select(hit => (Spot?)hit).FirstOrDefault() ?? Nearest(work, outside).At;

    internal static IEnumerable<List<Spot>> Runs(List<Spot> ring, Func<Spot, bool> clear)
    {
        int start = ring.FindIndex(p => !clear(p));
        if (start < 0)
        {
            yield return ring.Append(ring[0]).ToList();
            yield break;
        }
        var run = new List<Spot>();
        for (int k = 1; k <= ring.Count; k++)
        {
            var p = ring[(start + k) % ring.Count];
            if (clear(p)) run.Add(p);
            if (clear(p) && k < ring.Count) continue;
            if (run.Count > 1) yield return run;
            run = new();
        }
    }

    internal static void Wall(BuiltSite site, IReadOnlyList<Spot> ring, IEnumerable<Settlements.Street> streets, bool masonry, List<BuiltPart> parts, IEnumerable<Spot>? opened = null)
    {
        var works = Works(site);
        bool Clear(Spot p) => Holding(site, works, p) == null;
        var gates = Gates(site, ring, streets, parts);
        foreach (var gate in opened ?? [])
            if (!gates.Any(g => (g - gate).Length < site.M(HandApartM))) gates.Add(gate);
        var stepped = Stepped(site, ring).Select(p => Ashore(site, Middle(ring), p)).ToList();
        double flank = site.M(masonry ? GateLongM : GapM) / 2;
        bool Shut(int i) => Clear(stepped[i % stepped.Count]) && !gates.Any(g => (g - stepped[i % stepped.Count]).Length < site.M(GateHalfM));
        bool Into(int i) => Sheer(site, stepped[(i + stepped.Count - 1) % stepped.Count], stepped[i]);
        int first = Enumerable.Range(0, stepped.Count).FirstOrDefault(i => !Shut(i) || Into(i), -1), every = Math.Max(2, (int)(TowersApartM / StepM));
        string cls = masonry ? "wall" : "palisade";
        if (first < 0)
        {
            parts.Add(BuiltSite.Line(cls, stepped.Append(stepped[0])));
            if (masonry) parts.AddRange(Enumerable.Range(0, stepped.Count / every).Select(i => BuiltSite.Sign("tower", stepped[i * every])));
        }
        var run = new List<Spot>();
        for (int k = 1; first >= 0 && k <= stepped.Count; k++)
        {
            int i = (first + k) % stepped.Count;
            var before = stepped[(i + stepped.Count - 1) % stepped.Count];
            bool cliff = run.Count > 0 && Into(i);
            if (Shut(i) && !cliff)
            {
                if (run.Count == 0 && Met(site, works, stepped[i], before) is { } from) run.Add(from);
                run.Add(stepped[i]);
                if (masonry && run.Count % every == 0) parts.Add(BuiltSite.Sign("tower", stepped[i]));
                if (k < stepped.Count) continue;
            }
            else if (!cliff && run.Count > 0 && Met(site, works, before, stepped[i]) is { } to) run.Add(to);
            if (run.Count > 1)
            {
                Flanked(run, gates, flank, site.M(GateHalfM) * FlankReach);
                parts.Add(BuiltSite.Line(cls, run));
                if (masonry) parts.AddRange(new[] { run[0], run[^1] }.Select(p => BuiltSite.Sign("tower", p)));
            }
            run = new();
            if (cliff && Shut(i)) run.Add(stepped[i]);
        }
        if (!masonry) return;
        gates = gates.Where(Clear).ToList();
        parts.AddRange(gates.Select(gate => BuiltSite.Block("gatehouse", gate, Nearest(ring, gate).Along, site.M(GateLongM), site.M(GateDeepM))));
        var middle = Middle(ring);
        var ditch = stepped.Select(p => p + (p - middle).Unit * site.M(DitchOffM)).ToList();
        parts.AddRange(Runs(ditch, p => Clear(p) && site.Downhill(p).Length < BuiltSite.SteepestBuilt).Select(dug => BuiltSite.Line("ditch", dug)));
        parts.AddRange(gates.Select(gate => BuiltSite.Sign("bridge", gate + (gate - middle).Unit * site.M(DitchOffM), Math.Atan2((gate - middle).Y, (gate - middle).X))));
    }
}
