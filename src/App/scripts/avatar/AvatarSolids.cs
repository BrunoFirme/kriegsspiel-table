using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class AvatarSolids
{
    public const uint Layer = 1u << 20;
    const float Reach = 40f;
    const double LookEvery = 0.25;
    const float SameSizeWithin = 0.001f;

    readonly PieceLayer _pieces;
    readonly Node3D _root = new() { Name = "AvatarSolids" };
    readonly Dictionary<ulong, Solid> _made = new();
    readonly Dictionary<ulong, Solid> _byBody = new();
    double _lookIn;

    sealed record Solid(StaticBody3D Body, PieceNode Node, bool Model, ulong Mesh, Vector3 Size);

    public AvatarSolids(Node3D scene, PieceLayer pieces)
    {
        _pieces = pieces;
        scene.AddChild(_root);
    }

    public int Count => _made.Count;

    public void Tick(double delta, Vector3 around)
    {
        _lookIn -= delta;
        if (_lookIn <= 0)
        {
            _lookIn = LookEvery;
            Look(around);
        }
        foreach (var solid in _made.Values)
            if (Where(solid) is { } at) solid.Body.GlobalTransform = at;
    }

    public PieceNode? Owner(StaticBody3D body) => _byBody.TryGetValue(body.GetInstanceId(), out var solid) ? solid.Node : null;

    static Transform3D? Where(Solid solid) => Where(solid.Node);

    public static Transform3D? Where(PieceNode node)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return null;
        if (!node.ShowsModel) return node.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(node.FootprintCentre.X, node.Thickness / 2, node.FootprintCentre.Y));
        var (pose, mesh) = node.ModelPose();
        return mesh == 0 ? null : new Transform3D(pose.Basis.Orthonormalized(), pose.Origin);
    }

    public void Look(Vector3 around)
    {
        var seen = new HashSet<ulong>();
        foreach (var node in _pieces.Nodes)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.IsEffect || node.Ghost || node.IsDie) continue;
            var at = node.GlobalPosition;
            if (new Vector2(at.X - around.X, at.Z - around.Z).Length() > Reach + node.Footprint.Length()) continue;
            ulong id = node.GetInstanceId();
            var (pose, mesh) = node.ModelPose();
            if (node.ShowsModel && mesh == 0) continue;
            var size = node.ShowsModel ? pose.Basis.Scale : new Vector3(node.Footprint.X, node.Thickness, node.Footprint.Y);
            seen.Add(id);
            if (_made.TryGetValue(id, out var had))
            {
                if (had.Mesh == mesh && (had.Size - size).Length() <= SameSizeWithin * size.Length()) continue;
                Drop(id);
            }
            if (Made(node, mesh, size) is { } solid)
            {
                _made[id] = solid;
                _byBody[solid.Body.GetInstanceId()] = solid;
            }
        }
        foreach (var gone in _made.Keys.Where(k => !seen.Contains(k)).ToList()) Drop(gone);
    }

    void Drop(ulong id)
    {
        if (!_made.Remove(id, out var solid)) return;
        _byBody.Remove(solid.Body.GetInstanceId());
        solid.Body.QueueFree();
    }

    Solid? Made(PieceNode node, ulong mesh, Vector3 size)
    {
        var body = new StaticBody3D { CollisionLayer = Layer, CollisionMask = 0 };
        var holder = new CollisionShape3D();
        if (node.ShowsModel)
        {
            if (node.ModelFaces() is not { Length: >= 3 } faces) return null;
            for (int i = 0; i < faces.Length; i++) faces[i] *= size;
            var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
            shape.SetFaces(faces);
            holder.Shape = shape;
        }
        else
        {
            if (size.X <= 0 || size.Y <= 0 || size.Z <= 0) return null;
            holder.Shape = new BoxShape3D { Size = size };
        }
        body.AddChild(holder);
        _root.AddChild(body);
        var solid = new Solid(body, node, node.ShowsModel, mesh, size);
        if (Where(solid) is { } at) body.GlobalTransform = at;
        return solid;
    }

    public void Clear()
    {
        _made.Clear();
        _byBody.Clear();
        _root.QueueFree();
    }
}
