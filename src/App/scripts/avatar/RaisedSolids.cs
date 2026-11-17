using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class RaisedSolids
{
    const float ReachMetres = 120f;
    const double LookEvery = 0.5;

    readonly Func<Vector3, float, IEnumerable<MeshInstance3D>> _standing;
    readonly Node3D _root = new() { Name = "RaisedSolids", TopLevel = true };
    readonly Dictionary<ulong, (MeshInstance3D Piece, StaticBody3D Body)> _made = new();
    WalkSpace _space;
    double _lookIn;

    public RaisedSolids(Node3D scene, Func<Vector3, float, IEnumerable<MeshInstance3D>> standing, WalkSpace space)
    {
        _standing = standing;
        _space = space;
        scene.AddChild(_root);
    }

    public int Count => _made.Count;

    public void Tick(double delta, Vector3 around)
    {
        _lookIn -= delta;
        if (_lookIn <= 0 || _made.Values.Any(m => !GodotObject.IsInstanceValid(m.Piece)))
        {
            _lookIn = LookEvery;
            Look(around, now: false);
        }
        foreach (var (piece, body) in _made.Values)
            if (_space.In(new Transform3D(Basis.Identity, piece.Position)) is var placed && !placed.IsEqualApprox(body.GlobalTransform)) body.GlobalTransform = placed;
    }

    public void Look(Vector3 around, bool now = true)
    {
        var seen = new HashSet<ulong>();
        bool built = false;
        foreach (ulong freed in _made.Where(m => !GodotObject.IsInstanceValid(m.Value.Piece)).Select(m => m.Key).ToList()) Drop(freed);
        foreach (var piece in _standing(around, ReachMetres / _space.Scale).OrderBy(p => new Vector2(p.Position.X - around.X, p.Position.Z - around.Z).LengthSquared()))
        {
            ulong id = piece.GetInstanceId();
            if (_made.ContainsKey(id))
            {
                seen.Add(id);
                continue;
            }
            if (built && !now || piece.Mesh?.GetFaces() is not { Length: >= 3 } faces) continue;
            var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
            shape.SetFaces(faces);
            var body = new StaticBody3D { CollisionLayer = AvatarSolids.Layer, CollisionMask = 0 };
            _root.AddChild(body);
            body.AddChild(new CollisionShape3D { Shape = shape });
            body.GlobalTransform = _space.In(new Transform3D(Basis.Identity, piece.Position));
            _made[id] = (piece, body);
            seen.Add(id);
            built = true;
        }
        foreach (ulong gone in _made.Keys.Where(k => !seen.Contains(k)).ToList()) Drop(gone);
    }

    void Drop(ulong id)
    {
        if (!_made.Remove(id, out var made)) return;
        made.Body.CollisionLayer = 0;
        made.Body.QueueFree();
    }

    public void Respace(WalkSpace space)
    {
        _space = space;
        foreach (var (piece, body) in _made.Values)
            if (GodotObject.IsInstanceValid(piece)) body.GlobalTransform = _space.In(new Transform3D(Basis.Identity, piece.Position));
    }

    public void Clear()
    {
        foreach (var made in _made.Values) made.Body.CollisionLayer = 0;
        _made.Clear();
        _root.QueueFree();
    }
}
