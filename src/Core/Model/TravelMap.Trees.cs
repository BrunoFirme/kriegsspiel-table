namespace KTable.Core.Model;

public sealed class WayTree
{
    readonly Func<Spot, IReadOnlyList<int>> _near;
    readonly Func<int, Spot> _at;
    readonly Func<Spot, Vec2> _world;
    readonly int[] _came;
    readonly bool[] _reached;
    readonly int _root;
    readonly Spot _goal;

    internal WayTree(int[] came, bool[] reached, int root, Spot goal, Func<Spot, IReadOnlyList<int>> near, Func<int, Spot> at, Func<Spot, Vec2> world) =>
        (_came, _reached, _root, _goal, _near, _at, _world) = (came, reached, root, goal, near, at, world);

    public int Reached => _reached.Count(r => r);

    public IReadOnlyList<Vec2>? From(Spot start)
    {
        int? first = _near(start).Where(i => _reached[i]).OrderBy(i => (_at(i) - start).Length).Select(i => (int?)i).FirstOrDefault();
        if (first is not { } node) return null;
        var path = new List<Vec2> { _world(start) };
        for (int at = node, guard = _came.Length; at >= 0 && guard-- > 0; at = at == _root ? -1 : _came[at]) path.Add(_world(_at(at)));
        path.Add(_world(_goal));
        return path;
    }
}

public sealed partial class TravelMap
{
    public Spot Sheeted(Vec2 world) => OnSheet(world);

    public WayTree? TreeTo(Vec2 goal, Traveller who, Func<Vec2, bool>? blocked = null)
    {
        var g = OnSheet(goal);
        double reach = RoadsideM * _perMetre;
        int? root = NodesNear(g, reach).OrderBy(i => (_at[i] - g).Length).Select(i => (int?)i).FirstOrDefault();
        if (root is not { } start || _at.Count == 0) return null;
        var shut = blocked == null ? null : _at.Select(p => blocked(World(p))).ToArray();
        if (shut?[start] == true) return null;
        var spent = Enumerable.Repeat(double.PositiveInfinity, _at.Count).ToArray();
        var came = Enumerable.Repeat(-1, _at.Count).ToArray();
        var done = new bool[_at.Count];
        var open = new PriorityQueue<int, double>();
        spent[start] = 0;
        open.Enqueue(start, 0);
        while (open.TryDequeue(out int cur, out _))
        {
            if (done[cur]) continue;
            done[cur] = true;
            foreach (var (to, e, forward) in _links[cur])
            {
                if (done[to] || shut?[to] == true) continue;
                var edge = _edges[e];
                double pace = edge.Class < 0 ? StepPace(who) : who.On(_classes[edge.Class]);
                if (pace <= 0) continue;
                double grade = edge.Length > Level ? (forward ? -edge.Rise : edge.Rise) / edge.Length : 0;
                double next = spent[cur] + edge.Length / _perMetre / (pace * HillPace.Factor(grade));
                if (next >= spent[to]) continue;
                spent[to] = next;
                came[to] = cur;
                open.Enqueue(to, next);
            }
        }
        return new WayTree(came, done, start, g, p => NodesNear(p, reach).ToList(), i => _at[i], World);
    }
}
