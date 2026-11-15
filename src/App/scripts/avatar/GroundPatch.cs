using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class GroundPatch
{
    const int Side = 65;
    const float CellMetres = 1f, RecentreMetres = 16f;

    readonly Func<Vec2, double> _ground;
    readonly StaticBody3D _body = new() { Name = "GroundPatch", CollisionLayer = AvatarSolids.Layer, CollisionMask = 0, TopLevel = true };
    readonly HeightMapShape3D _shape = new() { MapWidth = Side, MapDepth = Side };
    Vector3 _centre;
    WalkSpace? _space;

    public GroundPatch(Node3D scene, Func<Vec2, double> ground)
    {
        _ground = ground;
        _body.AddChild(new CollisionShape3D { Shape = _shape });
        scene.AddChild(_body);
    }

    public void Follow(Vector3 feet, WalkSpace space)
    {
        if (_space == space && new Vector2(feet.X - _centre.X, feet.Z - _centre.Z).Length() * space.Scale < RecentreMetres) return;
        _space = space;
        float cell = CellMetres / space.Scale;
        _centre = new Vector3(Mathf.Round(feet.X / cell) * cell, 0, Mathf.Round(feet.Z / cell) * cell);
        var heights = new float[Side * Side];
        float half = (Side - 1) / 2f;
        for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                var at = _centre + new Vector3((i - half) * cell, 0, (j - half) * cell);
                heights[j * Side + i] = (float)_ground(Coords.FromWorld(at)) * space.Scale;
            }
        _shape.MapData = heights;
        _body.GlobalTransform = new Transform3D(Basis.Identity, space.In(_centre) with { Y = -space.Anchor.Y * space.Scale });
    }

    public void Clear() => _body.QueueFree();
}
