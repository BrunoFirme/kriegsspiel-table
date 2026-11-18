using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

sealed class CrowdRoutes
{
    readonly CrowdTown _town;
    readonly TravelMap? _map;
    readonly Traveller _foot;
    readonly IReadOnlyDictionary<string, Traveller> _ways;
    readonly List<ClearStroke> _clear;
    readonly Func<Vec2, float> _ground;
    readonly Dictionary<int, Task<WayTree?>> _trees = new();
    readonly Dictionary<(int, int), CrowdPath?> _paths = new();
    readonly Dictionary<(int, Traveller), Task<WayTree?>> _wayTrees = new();
    readonly Dictionary<(int, int, Traveller), CrowdPath?> _wayPaths = new();

    public float[] PlaceHeights { get; }
    public TravelMap? Map => _map;
    public Traveller Foot => _foot;
    public IReadOnlyDictionary<string, Traveller> Ways => _ways;
    public List<ClearStroke> Clear => _clear;

    public CrowdRoutes(CrowdTown town, TravelMap? map, IReadOnlyDictionary<string, Traveller> ways, List<ClearStroke> clear, Func<Vec2, float> ground)
    {
        (_town, _map, _ways, _clear, _ground) = (town, map, ways, clear, ground);
        _foot = ways[Travellers.Foot];
        PlaceHeights = town.Places.Select(p => ground(p.At)).ToArray();
    }

    public bool Clears(Vec2 p) => _clear.Count > 0 && _clear.Any(s => s.Covers(p));

    public int Pending => _trees.Values.Count(t => !t.IsCompleted);

    public CrowdPath? Path(int from, int to)
    {
        if (_paths.TryGetValue((from, to), out var known)) return known;
        var goal = _town.Places[to].At;
        if (Clears(goal)) return _paths[(from, to)] = null;
        if (_map == null) return _paths[(from, to)] = Straight(from, to);
        if (!_trees.TryGetValue(to, out var tree))
        {
            var map = _map;
            var clear = _clear;
            _trees[to] = tree = Task.Run(() => map.TreeTo(goal, _foot, clear.Count == 0 ? null : p => clear.Any(s => s.Covers(p))));
        }
        if (!tree.IsCompleted) return Straight(from, to);
        var points = tree.IsCompletedSuccessfully ? tree.Result?.From(_map.Sheeted(_town.Places[from].At)) : null;
        if (points == null) return _paths[(from, to)] = _clear.Count > 0 ? null : Straight(from, to);
        return _paths[(from, to)] = new CrowdPath(points, points.Select(_ground).ToArray());
    }

    public float Height(Vec2 p) => _ground(p);

    public bool Way(int from, int to, Traveller who, out CrowdPath? path)
    {
        path = null;
        if (_wayPaths.TryGetValue((from, to, who), out path)) return true;
        Vec2 start = _town.Places[from].At, goal = _town.Places[to].At;
        if (Clears(goal) || Clears(start)) return _wayPaths.TryAdd((from, to, who), null);
        if (_map == null)
        {
            path = Straight(from, to);
            return _wayPaths.TryAdd((from, to, who), path);
        }
        if (!_wayTrees.TryGetValue((to, who), out var tree))
        {
            var map = _map;
            var clear = _clear;
            _wayTrees[(to, who)] = tree = Task.Run(() => map.TreeTo(goal, who, clear.Count == 0 ? null : p => clear.Any(s => s.Covers(p))));
        }
        if (!tree.IsCompleted) return false;
        var points = tree.IsCompletedSuccessfully ? tree.Result?.From(_map.Sheeted(start)) : null;
        path = points is { Count: > 1 } ? new CrowdPath(points, points.Select(_ground).ToArray()) : null;
        _wayPaths[(from, to, who)] = path;
        return true;
    }

    public WayTree? TreeTo(Vec2 goal) => _map?.TreeTo(goal, _foot, _clear.Count == 0 ? null : p => _clear.Any(s => s.Covers(p)));

    public CrowdPath Between(WayTree? tree, Vec2 from, Vec2 to)
    {
        var points = tree != null && _map != null ? tree.From(_map.Sheeted(from))?.ToList() : null;
        if (points is not { Count: > 1 }) points = [from, to];
        else points[^1] = to;
        return new CrowdPath(points, points.Select(_ground).ToArray());
    }

    public CrowdPath FromPoint(Vec2 from, int place)
    {
        var goal = _town.Places[place].At;
        var tree = _trees.TryGetValue(place, out var task) && task.IsCompletedSuccessfully ? task.Result : null;
        return Between(tree, from, goal);
    }

    CrowdPath Straight(int from, int to) =>
        new([_town.Places[from].At, _town.Places[to].At], [PlaceHeights[from], PlaceHeights[to]]);
}
