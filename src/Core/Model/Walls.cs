namespace KTable.Core.Model;

public static class Walls
{
    const double Straight = 0.05, GateNearM = 9;

    public sealed record Trace(List<Spot> Corners, List<Spot> Gates, bool Masonry);

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static bool Run(BuiltPart part) => part.Kind == PartKinds.Line && part.Class is "wall" or "palisade" && part.At.Count >= 4;

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
        return new Trace(Corners(around), gates, place.Parts.First(Run).Class == "wall");
    }

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

    public static BuiltThing Rebuilt(BuiltSite site, BuiltThing place, Trace trace)
    {
        var rebuilt = Without(place);
        int from = rebuilt.Parts.Count;
        var streets = rebuilt.Parts.Where(BuiltSite.IsRoad).Select(p => new Settlements.Street(Points(p), p.Class, false)).ToList();
        Enclosures.Wall(site, trace.Corners, streets, trace.Masonry, rebuilt.Parts, trace.Gates);
        for (int i = from; i < rebuilt.Parts.Count; i++) rebuilt.Parts[i].Kept = true;
        return rebuilt;
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
        Of(place) is { } trace && corner >= 0 && corner < trace.Corners.Count ? Rebuilt(site, place, Moved(trace, corner, to)) : null;

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
