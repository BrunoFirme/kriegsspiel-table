namespace KTable.Core.Model;

public static class Walls
{
    const double Straight = 0.05, GateNearM = 9;
    public const string TowersSetting = "wallTowers", DitchSetting = "wallDitch";
    static readonly double WalledValue = (Settlements.WalledFrom + Settlements.BastionedFrom) / 2;

    public sealed record Trace(List<Spot> Corners, List<Spot> Gates, bool Masonry, int Towers = Works.ManyTowers, bool Ditch = true);

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static bool Run(BuiltPart part) => part.Kind == PartKinds.Line && part.Class is "wall" or "palisade" && part.At.Count >= 4;

    public static bool Circuit(BuiltPart part) => Run(part);

    public static Trace? Of(BuiltThing place)
    {
        if (place.Kind != BuiltKinds.Settlement) return null;
        var runs = place.Parts.Where(Run).Select(Points).ToList();
        if (runs.Count == 0) return null;
        var around = new List<Spot>();
        var gates = new List<Spot>();
        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            bool closed = runs.Count == 1 && (run[0] - run[^1]).Length < 1e-6;
            around.AddRange(closed ? run.Take(run.Count - 1) : run);
            if (!closed) gates.Add((run[^1] + runs[(i + 1) % runs.Count][0]) * 0.5);
        }
        return new Trace(Corners(around), gates, place.Parts.First(Run).Class == "wall", (int)Math.Clamp(place.Setting(TowersSetting, Works.ManyTowers), Works.NoTowers, Works.ManyTowers),
            place.Setting(DitchSetting, 1) > 0);
    }

    public static Trace? Handles(BuiltThing place) =>
        Of(place) ?? (place.Kind == BuiltKinds.Settlement && Forts.Of(place) is { } star ? new Trace(star.Tips.Select(t => star.Centre + Spot.Toward(t.Bearing) * t.Radius).ToList(), [], true) : null);

    public static Trace? ByHand(BuiltThing place) => place.Parts.Any(p => Run(p) && p.Kept) ? Of(place) : null;

    static List<Spot> Corners(List<Spot> around)
    {
        var corners = new List<Spot>();
        for (int i = 0; i < around.Count; i++)
        {
            Spot into = (around[i] - around[(i + around.Count - 1) % around.Count]).Unit, onward = (around[(i + 1) % around.Count] - around[i]).Unit;
            if (Math.Abs(into.X * onward.Y - into.Y * onward.X) > Straight || into.Dot(onward) < 0) corners.Add(around[i]);
        }
        return corners.Count >= 3 ? corners : around;
    }

    static HashSet<(int, int)> Posts(BuiltThing place) =>
        place.Parts.Where(Run).SelectMany(p => Enumerable.Range(0, p.At.Count / 2).Select(i => (p.At[2 * i], p.At[2 * i + 1]))).ToHashSet();

    static bool Work(BuiltPart part, HashSet<(int, int)> posts) => part.Kind switch
    {
        PartKinds.Line => part.Class is "wall" or "palisade" or "ditch",
        PartKinds.Block => part.Class == "gatehouse",
        PartKinds.Sign => part.Class == "tower" && part.At.Count >= 2 && posts.Contains((part.At[0], part.At[1])),
        _ => false,
    };

    public static BuiltThing Rebuilt(BuiltSite site, BuiltThing place, Trace trace, bool kept = true)
    {
        var rebuilt = Without(place);
        int from = rebuilt.Parts.Count;
        var streets = rebuilt.Parts.Where(BuiltSite.IsRoad).Select(p => new Settlements.Street(Points(p), p.Class, false)).ToList();
        Enclosures.Wall(site, trace.Corners, streets, trace.Masonry, rebuilt.Parts, trace.Gates, trace.Towers, trace.Ditch);
        for (int i = from; i < rebuilt.Parts.Count; i++) rebuilt.Parts[i].Kept = kept;
        rebuilt.Settings[TowersSetting] = trace.Towers;
        rebuilt.Settings[DitchSetting] = trace.Ditch ? 1 : 0;
        return rebuilt;
    }

    public static BuiltThing Ringed(BuiltSite site, BuiltThing place, IReadOnlyList<Spot> ring, bool masonry, int towers, bool ditch)
    {
        var walled = place.Clone();
        walled.Settings[Settlements.Walls] = WalledValue;
        return Rebuilt(site, walled, new Trace(ring.ToList(), [], masonry, towers, ditch));
    }

    public static BuiltThing? Optioned(BuiltSite site, BuiltThing place, bool masonry, int towers, bool ditch) =>
        Of(place) is { } trace ? Rebuilt(site, place, trace with { Masonry = masonry, Towers = towers, Ditch = ditch }) : null;

    public static BuiltThing? CornerAdded(BuiltSite site, BuiltThing place, int leg, Spot at)
    {
        if (Of(place) is not { } trace || leg < 0 || leg >= trace.Corners.Count) return null;
        var corners = trace.Corners.ToList();
        corners.Insert(leg + 1, at);
        return Rebuilt(site, place, trace with { Corners = corners, Gates = trace.Gates.Select(g => Enclosures.Nearest(corners, g).At).ToList() });
    }

    public static BuiltThing? CornerRemoved(BuiltSite site, BuiltThing place, int corner)
    {
        if (Of(place) is not { } trace || corner < 0 || corner >= trace.Corners.Count || trace.Corners.Count <= 3) return null;
        var corners = trace.Corners.ToList();
        corners.RemoveAt(corner);
        return Rebuilt(site, place, trace with { Corners = corners, Gates = trace.Gates.Select(g => Enclosures.Nearest(corners, g).At).ToList() });
    }

    public static (BuiltThing Place, List<Spot> Inner)? Spliced(BuiltSite site, BuiltThing place, IReadOnlyList<Spot> run, double reach)
    {
        if (run.Count < 2 || Of(place) is not { } trace) return null;
        var ring = trace.Corners;
        var (a, legA, offA) = OnRing(ring, run[0]);
        var (b, legB, offB) = OnRing(ring, run[^1]);
        if (offA > reach || offB > reach || (a - b).Length < 1e-6) return null;
        var middle = run.Count > 2 ? run[run.Count / 2] : (run[0] + run[1]) * 0.5;
        if (Enclosures.Inside(ring, middle)) return null;
        var inner = run.Skip(1).Take(run.Count - 2).ToList();
        var forward = Walk(ring, legA, a, legB, b);
        var backward = Walk(ring, legB, b, legA, a);
        var one = new List<Spot> { a };
        one.AddRange(inner);
        one.Add(b);
        one.AddRange(backward.Skip(1).Take(backward.Count - 2));
        var two = new List<Spot> { b };
        two.AddRange(Enumerable.Reverse(inner));
        two.Add(a);
        two.AddRange(forward.Skip(1).Take(forward.Count - 2));
        bool first = Math.Abs(Area(one)) >= Math.Abs(Area(two));
        var corners = first ? one : two;
        var gates = trace.Gates.Where(g => Enclosures.Nearest(corners, g).Reach < site.M(GateNearM)).ToList();
        return (Rebuilt(site, place, trace with { Corners = corners, Gates = gates }), first ? forward : backward);
    }

    static (Spot At, int Leg, double Reach) OnRing(IReadOnlyList<Spot> ring, Spot p)
    {
        (Spot At, int Leg, double Reach) best = (ring[0], 0, double.MaxValue);
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], ab = ring[(i + 1) % ring.Count] - a;
            var at = a + ab * Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if ((at - p).Length < best.Reach) best = (at, i, (at - p).Length);
        }
        return best;
    }

    static List<Spot> Walk(IReadOnlyList<Spot> ring, int fromLeg, Spot from, int toLeg, Spot to)
    {
        var path = new List<Spot> { from };
        if (fromLeg == toLeg && (to - ring[fromLeg]).Length >= (from - ring[fromLeg]).Length)
        {
            path.Add(to);
            return path;
        }
        for (int k = 1; k <= ring.Count; k++)
        {
            int i = (fromLeg + k) % ring.Count;
            path.Add(ring[i]);
            if (i == toLeg) break;
        }
        path.Add(to);
        return path;
    }

    static double Area(IReadOnlyList<Spot> ring) => Enumerable.Range(0, ring.Count).Sum(i => ring[i].X * ring[(i + 1) % ring.Count].Y - ring[(i + 1) % ring.Count].X * ring[i].Y) / 2;

    public static BuiltThing? Towered(BuiltThing place, Spot at, double reach)
    {
        if (Of(place) is not { } trace) return null;
        var towered = place.Clone();
        int near = -1;
        double least = reach;
        for (int i = 0; i < towered.Parts.Count; i++)
        {
            var part = towered.Parts[i];
            if (part.Kind != PartKinds.Sign || part.Class != "tower" || (BuiltSite.Round(part).At - at).Length >= least) continue;
            (near, least) = (i, (BuiltSite.Round(part).At - at).Length);
        }
        if (near >= 0)
        {
            towered.Parts.RemoveAt(near);
            return towered;
        }
        var (on, _, off) = Enclosures.Nearest(trace.Corners, at);
        if (off > reach) return null;
        var tower = BuiltSite.Sign("tower", on);
        tower.Kept = true;
        towered.Parts.Add(tower);
        return towered;
    }

    public static BuiltThing? Opened(BuiltSite site, BuiltThing place, BuiltThing line)
    {
        if (line.Kind != BuiltKinds.Road || Of(place) is not { } trace) return null;
        var through = new List<Spot>();
        foreach (var path in line.Parts.Where(BuiltSite.IsRoad).Select(Points))
            for (int i = 0; i + 1 < path.Count; i++) through.AddRange(Enclosures.Hits(trace.Corners, path[i], path[i + 1]));
        return through.Count == 0 ? null : Rebuilt(site, place, trace with { Gates = trace.Gates.Concat(through).ToList() }, place.Parts.Any(p => Run(p) && p.Kept));
    }

    public static BuiltThing? Joined(BuiltSite site, BuiltThing place, BuiltThing suburb)
    {
        if (place.Id == suburb.Id || Of(place) is not { } trace || Of(suburb) is not { } beside) return null;
        var gate = Enclosures.Nearest(trace.Corners, Enclosures.Middle(beside.Corners)).At;
        if (!Enclosures.Inside(beside.Corners, gate) || trace.Gates.Any(g => Enclosures.Inside(beside.Corners, g))) return null;
        return Rebuilt(site, place, trace with { Gates = trace.Gates.Append(gate).ToList() }, place.Parts.Any(p => Run(p) && p.Kept));
    }

    public static BuiltThing Without(BuiltThing place)
    {
        var posts = Posts(place);
        var bare = place.Clone();
        bare.Parts.RemoveAll(p => Work(p, posts));
        return bare;
    }

    public static int CornerNear(Trace trace, Spot p, double reach)
    {
        int best = -1;
        for (int i = 0; i < trace.Corners.Count; i++)
            if ((trace.Corners[i] - p).Length < reach && (best < 0 || (trace.Corners[i] - p).Length < (trace.Corners[best] - p).Length)) best = i;
        return best;
    }

    public static Trace Moved(Trace trace, int corner, Spot to)
    {
        var corners = trace.Corners.ToList();
        corners[corner] = to;
        return trace with { Corners = corners, Gates = trace.Gates.Select(g => Enclosures.Nearest(corners, g).At).ToList() };
    }

    public static BuiltThing? CornerMoved(BuiltSite site, BuiltThing place, int corner, Spot to) =>
        Of(place) is { } trace ? corner >= 0 && corner < trace.Corners.Count ? Rebuilt(site, place, Moved(trace, corner, to)) : null : Forts.TipMoved(site, place, corner, to);

    public static BuiltThing? Gated(BuiltSite site, BuiltThing place, Spot at, double reach)
    {
        if (Of(place) is not { } trace) return null;
        var (on, _, off) = Enclosures.Nearest(trace.Corners, at);
        if (off > reach) return null;
        var gates = trace.Gates.ToList();
        int shut = gates.FindIndex(g => (g - on).Length < site.M(GateNearM));
        if (shut >= 0) gates.RemoveAt(shut);
        else gates.Add(on);
        return Rebuilt(site, place, trace with { Gates = gates });
    }
}
