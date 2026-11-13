namespace KTable.Core.Model;

public static class BuiltRoads
{
    public const string FindsWay = "findsWay";
    public static readonly IReadOnlyList<string> Roads = ["track", "road", "chaussee"];
    public static readonly IReadOnlyList<string> Waters = ["stream", "river"];
    const int Rounds = 2;
    const double Corner = 0.25, KeepWithinCells = 0.35, FordWithinM = 25, SampleEveryM = 4, JoinWithinM = 30;
    const double WanderEveryM = 90, TrackWanderM = 7, RoadWanderM = 3.5, StreamWanderM = 12, RiverWanderM = 20;
    const double WaysideM = 7, WaysideShare = 0.6;
    const double TrackHalfM = 3, RoadHalfM = 5, ChausseeHalfM = 8, StreamHalfM = 3, RiverHalfM = 12, ClearM = 3;

    public static bool IsWater(string cls) => Waters.Contains(cls);

    public static double HalfWidthM(string cls) => cls switch { "track" => TrackHalfM, "chaussee" => ChausseeHalfM, "stream" => StreamHalfM, "river" => RiverHalfM, _ => RoadHalfM } + ClearM;

    public static BuiltThing Lay(BuiltSite site, string sheet, IReadOnlyList<Spot> waypoints, string cls, bool findsWay)
    {
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = IsWater(cls) ? BuiltKinds.Water : BuiltKinds.Road, Settings = { [FindsWay] = findsWay ? 1 : 0 },
            Outline = BuiltGeometry.Flat(waypoints.Select(w => w.Stored)),
        };
        if (waypoints.Count < (IsWater(cls) && findsWay ? 1 : 2)) return thing;
        var through = IsWater(cls) ? waypoints : Joined(site, waypoints);
        var path = findsWay && site.Grid != null ? Found(site, through, cls) : through.ToList();
        if (path.Count < 2) return thing;
        path = Thinned(Rounded(path), site.Grid == null ? 0 : site.Width / Math.Max(1, site.Grid.Cols - 1) * KeepWithinCells);
        double wander = site.M(cls switch { "track" => TrackWanderM, "road" => RoadWanderM, "stream" => StreamWanderM, "river" => RiverWanderM, _ => 0 });
        if (wander > 0 && !(findsWay && IsWater(cls))) path = Rounded(Wandering(site, path, wander, !IsWater(cls)));
        thing.Parts.Add(BuiltSite.Line(cls, path));
        if (!IsWater(cls)) thing.Parts.AddRange(Wayside(site, waypoints, through));
        thing.Parts.AddRange(IsWater(cls) ? RoadsCrossed(site, path, cls) : Crossings(site, path, cls));
        return thing;
    }

    static List<Spot> Joined(BuiltSite site, IReadOnlyList<Spot> waypoints)
    {
        var joined = waypoints.ToList();
        foreach (int end in new[] { 0, joined.Count - 1 })
            if (site.NearestRoad(joined[end]) is var (at, reach) && reach < site.M(JoinWithinM)) joined[end] = at;
        return joined;
    }

    static IEnumerable<BuiltPart> Wayside(BuiltSite site, IReadOnlyList<Spot> asked, IReadOnlyList<Spot> through)
    {
        foreach (int end in new[] { 0, asked.Count - 1 })
        {
            if (asked[end] == through[end] || through.Count < 2) continue;
            var along = (through[end] - through[end == 0 ? 1 : end - 1]).Unit;
            var dice = new Dice(through[end].Stored.X, through[end].Stored.Y);
            var at = through[end] - along * site.M(WaysideM) + along.Left * (site.M(WaysideM) * (dice.Chance(0.5) ? 1 : -1));
            if (dice.Chance(WaysideShare) && site.Free(at, 0)) yield return BuiltSite.Sign("shrine", at);
        }
    }

    static List<Spot> Wandering(BuiltSite site, List<Spot> path, double most, bool onLand)
    {
        double every = site.M(WanderEveryM);
        var wandering = new List<Spot> { path[0] };
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Spot a = path[i], b = path[i + 1];
            int steps = (int)((b - a).Length / every);
            var dice = new Dice(a.Stored.X ^ b.Stored.Y, a.Stored.Y ^ b.Stored.X);
            double swing = dice.Between(-1, 1);
            for (int s = 1; s < steps; s++)
            {
                double t = s / (double)steps;
                swing = Math.Clamp(swing + dice.Between(-0.6, 0.6), -1, 1);
                var at = a + (b - a) * t + (b - a).Unit.Left * (most * swing * Math.Sin(Math.PI * t));
                if (!onLand || site.Buildable(at)) wandering.Add(at);
            }
            wandering.Add(b);
        }
        return wandering;
    }

    static List<Spot> Found(BuiltSite site, IReadOnlyList<Spot> waypoints, string cls)
    {
        var grid = site.Grid!;
        var uv = waypoints.Select(w => (Math.Clamp(w.X / site.Width, 0, 1), Math.Clamp(w.Y / site.Height, 0, 1))).ToList();
        double cell = site.Width / Math.Max(1, grid.Cols - 1);
        var path = IsWater(cls) ? GroundRoute.River(grid, grid.H, uv[0], out _, 0) : GroundRoute.Road(grid, grid.H, cell / Math.Max(1e-9, site.HeightUnit), uv);
        return path.Select(p => new Spot(p.u * site.Width, p.v * site.Height)).ToList();
    }

    static List<Spot> Rounded(List<Spot> path)
    {
        for (int round = 0; round < Rounds && path.Count > 2; round++)
        {
            var next = new List<Spot> { path[0] };
            for (int i = 0; i + 1 < path.Count; i++)
            {
                if (i > 0) next.Add(path[i] + (path[i + 1] - path[i]) * Corner);
                if (i + 2 < path.Count) next.Add(path[i] + (path[i + 1] - path[i]) * (1 - Corner));
            }
            next.Add(path[^1]);
            path = next;
        }
        return path;
    }

    static List<Spot> Thinned(List<Spot> path, double within)
    {
        if (within <= 0 || path.Count < 3) return path;
        var kept = new List<Spot> { path[0] };
        for (int i = 1; i + 1 < path.Count; i++)
            if (path[i].FromSegment(kept[^1], path[i + 1]) > within) kept.Add(path[i]);
        kept.Add(path[^1]);
        return kept;
    }

    static BuiltPart Over(string road, string water, Spot at, Spot along) =>
        BuiltSite.Sign(road == "track" && water is "stream" or "ditch" ? "ford" : "bridge", at, Math.Atan2(along.Y, along.X));

    static IEnumerable<BuiltPart> RoadsCrossed(BuiltSite site, List<Spot> path, string cls)
    {
        for (int i = 0; i + 1 < path.Count; i++)
            foreach (var (a, b, road) in site.RoadLegs)
                if (BuiltSite.Crossing(path[i], path[i + 1], a, b) is { } at) yield return Over(road, cls, at, b - a);
    }

    static IEnumerable<BuiltPart> Crossings(BuiltSite site, List<Spot> path, string cls)
    {
        double step = site.M(SampleEveryM);
        Spot? entered = null;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            foreach (var (a, b, water) in site.Waters)
                if (BuiltSite.Crossing(path[i], path[i + 1], a, b) is { } over) yield return Over(cls, water, over, path[i + 1] - path[i]);
            for (int s = 1, samples = Math.Max(1, (int)Math.Ceiling((path[i + 1] - path[i]).Length / step)); s <= samples; s++)
            {
                var at = path[i] + (path[i + 1] - path[i]) * (s / (double)samples);
                bool water = site.KindAt(at) == GroundKinds.Water;
                if (water && entered == null) entered = at;
                else if (!water && entered is { } from)
                {
                    var across = at - from;
                    bool ford = cls == "track" && across.Length <= site.M(FordWithinM);
                    yield return BuiltSite.Sign(ford ? "ford" : "bridge", from + across * 0.5, Math.Atan2(across.Y, across.X));
                    entered = null;
                }
            }
        }
    }

    static double Reach(BuiltPart part, IReadOnlyList<Spot> path)
    {
        var points = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        var middle = new Spot(points.Average(p => p.X), points.Average(p => p.Y));
        double least = double.MaxValue;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            least = Math.Min(least, middle.FromSegment(path[i], path[i + 1]));
            for (int k = 0; k < points.Count && part.Kind != PartKinds.Sign; k++)
                if (BuiltSite.Crossing(path[i], path[i + 1], points[k], points[(k + 1) % points.Count]) != null) return 0;
        }
        return least;
    }

    public static BuiltThing? MadeWay(BuiltThing place, BuiltThing line, double unitsPerMetre)
    {
        if (place.Kind is not (BuiltKinds.Settlement or BuiltKinds.Details) || line.Parts.FirstOrDefault(p => p.Kind == PartKinds.Line) is not { } laid) return null;
        var path = BuiltGeometry.Points(laid.At).Select(Spot.Of).ToList();
        double half = HalfWidthM(laid.Class) * unitsPerMetre;
        bool InTheWay(BuiltPart part) => part.Kind switch
        {
            PartKinds.Block => Reach(part, path) < half,
            PartKinds.Area => part.Class is "garden" or "orchard" or "pond" or "yard" or "churchyard" && Reach(part, path) < half,
            PartKinds.Sign => part.Class is not ("bridge" or "ford" or "tower" or "cross" or "flag" or "gun") && Reach(part, path) < half,
            _ => part.Class == "fence" && Reach(part, path) <= 0,
        };
        bool Hedged(BuiltPart part) => part.Kind == PartKinds.Line && part.Class is "hedge" or "ditch" or "palisade" && Legs(part).Any(leg => Across(leg.A, leg.B, path) != null);
        if (!place.Parts.Any(p => InTheWay(p) || Hedged(p))) return null;
        var cleared = place.Clone();
        cleared.Parts.RemoveAll(InTheWay);
        for (int i = cleared.Parts.Count - 1; i >= 0; i--)
        {
            if (!Hedged(cleared.Parts[i])) continue;
            var cut = Cut(cleared.Parts[i], path, half).ToList();
            cleared.Parts.RemoveAt(i);
            cleared.Parts.InsertRange(i, cut);
        }
        return cleared;
    }

    static IEnumerable<(Spot A, Spot B)> Legs(BuiltPart part)
    {
        var points = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        for (int k = 0; k + 1 < points.Count; k++) yield return (points[k], points[k + 1]);
    }

    static Spot? Across(Spot a, Spot b, IReadOnlyList<Spot> path)
    {
        for (int i = 0; i + 1 < path.Count; i++)
            if (BuiltSite.Crossing(a, b, path[i], path[i + 1]) is { } at) return at;
        return null;
    }

    static IEnumerable<BuiltPart> Cut(BuiltPart hedge, IReadOnlyList<Spot> path, double half)
    {
        var points = BuiltGeometry.Points(hedge.At).Select(Spot.Of).ToList();
        var run = new List<Spot> { points[0] };
        for (int k = 1; k < points.Count; k++)
        {
            if (Across(points[k - 1], points[k], path) is { } at)
            {
                var along = (points[k] - points[k - 1]).Unit;
                if ((at - points[k - 1]).Length > half) run.Add(at - along * half);
                if (run.Count > 1) yield return BuiltSite.Line(hedge.Class, run);
                run = (points[k] - at).Length > half ? [at + along * half] : [];
            }
            run.Add(points[k]);
        }
        if (run.Count > 1) yield return BuiltSite.Line(hedge.Class, run);
    }
}
