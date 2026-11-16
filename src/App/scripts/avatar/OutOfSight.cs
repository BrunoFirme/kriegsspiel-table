using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class OutOfSight(Node3D scene)
{
    const float Below = 100000f;
    const ulong HoldMs = 500;

    readonly List<(Node3D Node, ulong Until)> _held = new();

    public void Hold(Node3D node)
    {
        scene.AddChild(node);
        node.GlobalPosition = Vector3.Down * Below;
        _held.Add((node, Time.GetTicksMsec() + HoldMs));
    }

    public void Tick()
    {
        ulong now = Time.GetTicksMsec();
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (now < _held[i].Until) continue;
            if (GodotObject.IsInstanceValid(_held[i].Node)) _held[i].Node.QueueFree();
            _held.RemoveAt(i);
        }
    }
}
