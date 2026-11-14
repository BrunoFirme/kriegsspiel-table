using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class AvatarSolids
{
    public const uint Layer = 1u << 20;
    const float Reach = 40f;
    const double LookEvery = 0.25;

    readonly PieceLayer _pieces;
    readonly Node3D _root = new() { Name = "AvatarSolids" };
    readonly Dictionary<ulong, Solid> _made = new();
    double _lookIn;

    sealed record Solid(StaticBody3D Body, Transform3D At, ulong Mesh, Vector3 Size);

    public AvatarSolids(Node3D scene, PieceLayer pieces)
    {
        _pieces = pieces;
        scene.AddChild(_root);
    }

    public int Count => _made.Count;

    public void Tick(double delta, Vector3 around)
    {
        _lookIn -= delta;
        if (_lookIn > 0) return;
        _lookIn = LookEvery;
        Look(around);
    }

    public void Look(Vector3 around)
    {
        var seen = new HashSet<ulong>();
        foreach (var node in _pieces.Nodes)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !node.Visible || node.IsEffect || node.Ghost || node.IsDie) continue;
            var at = node.GlobalPosition;
            float span = node.Footprint.Length();
            if (new Vector2(at.X - around.X, at.Z - around.Z).Length() > Reach + span) continue;
            ulong id = node.GetInstanceId();
            var (pose, mesh) = node.ModelPose();
            var size = new Vector3(node.Footprint.X, node.Thickness, node.Footprint.Y);
            if (node.ShowsModel && mesh == 0) continue;
            seen.Add(id);
            var where = node.ShowsModel ? pose : node.GlobalTransform;
            if (_made.TryGetValue(id, out var had))
            {
                if (had.Mesh == mesh && had.At.IsEqualApprox(where) && had.Size.IsEqualApprox(size)) continue;
                had.Body.QueueFree();
                _made.Remove(id);
            }
            if (Made(node, size) is { } body) _made[id] = new Solid(body, where, mesh, size);
        }
        foreach (var gone in _made.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _made[gone].Body.QueueFree();
            _made.Remove(gone);
        }
    }

    StaticBody3D? Made(PieceNode node, Vector3 size)
    {
        var body = new StaticBody3D { CollisionLayer = Layer, CollisionMask = 0 };
        var holder = new CollisionShape3D();
        if (node.ShowsModel)
        {
            if (node.WorldFaces() is not { Length: >= 3 } faces) return null;
            var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
            shape.SetFaces(faces);
            holder.Shape = shape;
        }
        else
        {
            if (size.X <= 0 || size.Y <= 0 || size.Z <= 0) return null;
            holder.Shape = new BoxShape3D { Size = size };
            body.Transform = node.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(node.FootprintCentre.X, size.Y / 2, node.FootprintCentre.Y));
        }
        body.AddChild(holder);
        _root.AddChild(body);
        return body;
    }

    public void Clear()
    {
        _made.Clear();
        _root.QueueFree();
    }
}
