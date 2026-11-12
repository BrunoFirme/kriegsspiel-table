namespace KTable.Core.Model;

public static class BuiltRoads
{
    public const string FindsWay = "findsWay";
    public static readonly IReadOnlyList<string> Roads = ["track", "road", "chaussee"];
    public static readonly IReadOnlyList<string> Waters = ["stream", "river"];
    const int Rounds = 2;
    const double Corner = 0.25, KeepWithinCells = 0.35, FordWithinM = 25, SampleEveryM = 4;

    public static bool IsWater(string cls) => Waters.Contains(cls);

    public static BuiltThing Lay(BuiltSite site, string sheet, IReadOnlyList<Spot> waypoints, string cls, bool findsWay)
    {
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = IsWater(cls) ? BuiltKinds.Water : BuiltKinds.Road, Settings = { [FindsWay] = findsWay ? 1 : 0 },
            Outline = BuiltGeometry.Flat(waypoints.Select(w => w.Stored)),
        };
        if (waypoints.Count < (IsWater(cls) && findsWay ? 1 : 2)) return thing;
        var path = findsWay && site.Grid != null ? Found(site, waypoints, cls) : waypoints.ToList();
        if (path.Count < 2) return thing;
        path = Thinned(Rounded(path), site.Grid == null ? 0 : site.Width / Math.Max(1, site.Grid.Cols - 1) * KeepWithinCells);
        thing.Parts.Add(BuiltSite.Line(cls, path));
        if (!IsWater(cls)) thing.Parts.AddRange(Crossings(site, path, cls));
        return thing;
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

    static IEnumerable<BuiltPart> Crossings(BuiltSite site, List<Spot> path, string cls)
    {
        double step = site.M(SampleEveryM);
        Spot? entered = null;
        for (int i = 0; i + 1 < path.Count; i++)
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
