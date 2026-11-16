using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class Table
{
    const float FenceSlackMetres = 0.6f;

    readonly Dictionary<long, (string Name, AvatarPose Pose)> _fenced = new();
    BoundShimmer _shimmer = null!;
    string _boundsWere = "";
    long _boundsSeq = -1;

    internal BoundSpace Fences { get; private set; } = null!;
    internal BoundEditor Bounding { get; private set; } = null!;
    BoundShapes _boundShapes = null!;

    public event Action? BoundsChanged;

    void MakeFences()
    {
        Fences = new BoundSpace(_c, id => Pieces.NodeOf(Layers.Umpire, id) is { } node ? AvatarSolids.Where(node) : null);
        _shimmer = new BoundShimmer(_c);
        Walk.Fence = Fences;
        Walk.Fenced += (at, away) => _shimmer.Touch(at, away, Walk.Height);
        Bounding = new BoundEditor(_c, StandOn, id => Pieces.NodeOf(Layers.Umpire, id));
        _boundShapes = new BoundShapes(_c, Bounding, id => Pieces.NodeOf(Layers.Umpire, id));
    }

    void TickFences(double delta)
    {
        _shimmer.Tick(delta);
        Bounding.Tick();
        _boundShapes.Tick();
        if (S.View.LastSeq == _boundsSeq) return;
        _boundsSeq = S.View.LastSeq;
        var now = Signature(S.View);
        if (now == _boundsWere) return;
        _boundsWere = now;
        BoundsChanged?.Invoke();
    }

    static string Signature(GameState g)
    {
        var text = new System.Text.StringBuilder();
        foreach (var layer in g.BoundLayers ?? [])
        {
            text.Append(layer.Id).Append('|').Append(layer.Name).Append('|');
            foreach (var shape in layer.Shapes)
            {
                text.Append(shape.Id).Append(shape.On).Append(':').Append(shape.Floor).Append(':').Append(shape.Ceiling);
                foreach (var p in shape.Polygon) text.Append(';').Append(p.X).Append(',').Append(p.Z);
            }
        }
        foreach (var (layer, rule) in g.EveryoneBounds ?? new()) text.Append('*').Append(layer).Append(rule);
        foreach (var (player, rules) in g.PlayerBounds ?? new())
            foreach (var (layer, rule) in rules) text.Append('#').Append(player).Append(layer).Append(rule);
        return text.ToString();
    }

    public Vector3? Fence(long peer, string name, AvatarPose pose)
    {
        var space = Fences.For(name);
        if (space.Empty || !_fenced.TryGetValue(peer, out var was))
        {
            _fenced[peer] = (name, pose);
            return null;
        }
        Vector3 feet = Avatars.FeetOf(pose), last = Avatars.FeetOf(was.Pose);
        float slack = FenceSlackMetres * AvatarPose.UnitsPerMetre(S.View);
        if (Fences.Allows(space, feet) || Fences.Outside(space, feet) <= Fences.Outside(space, last) + slack)
        {
            _fenced[peer] = (name, pose);
            return null;
        }
        return last;
    }

    public void PlayerPlaced(long peer, string name, Vector3 feet, float yaw) => _fenced[peer] = (name, new AvatarPose(feet, yaw, AvatarPose.Usual));

    public void Unfence(long peer) => _fenced.Remove(peer);

    public List<(long Peer, Vector3 Spot, float Yaw)> Strays()
    {
        var strays = new List<(long Peer, Vector3 Spot, float Yaw)>();
        foreach (var (peer, (name, pose)) in _fenced.ToList())
        {
            var space = Fences.For(name);
            var feet = Avatars.FeetOf(pose);
            if (space.Empty || Fences.Allows(space, feet)) continue;
            if (Fences.Nearest(space, feet) is not { } spot)
            {
                Say($"{name} has nowhere to stand");
                continue;
            }
            _fenced[peer] = (name, new AvatarPose(spot, pose.Yaw, pose.Height));
            strays.Add((peer, spot, pose.Yaw));
        }
        return strays;
    }
}
