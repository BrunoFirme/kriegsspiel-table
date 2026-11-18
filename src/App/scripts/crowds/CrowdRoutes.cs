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
    readonly List<ClearStroke> _clear;
    readonly Func<Vec2, float> _ground;
    readonly Dictionary<int, Task<WayTree?>> _trees = new();
    readonly Dictionary<(int, int), CrowdPath?> _paths = new();

    public float[] PlaceHeights { get; }
    public TravelMap? Map => _map;
    public Traveller Foot => _foot;
    public List<ClearStroke> Clear => _clear;

    public CrowdRoutes(CrowdTown town, TravelMap? map, Traveller foot, List<ClearStroke> clear, Func<Vec2, float> ground)
    {
        (_town, _map, _foot, _clear, _ground) = (town, map, foot, clear, ground);
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
