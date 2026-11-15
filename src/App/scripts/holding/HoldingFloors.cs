using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class Carry
{
    const float RoomShare = 0.5f, InsideLiftShare = 0.5f;

    float Tallest => _held.Count == 0 ? 0 : _held.Max(h => GodotObject.IsInstanceValid(h.Node) ? h.Node.Thickness : 0);

    float InsideLift => Tallest * InsideLiftShare;

    Vec2 Pointed(Vector2 screen, Vec2 ground)
    {
        var floor = FloorUnder(screen, ground);
        float? ceiling = floor is { } f ? f.Y + Tallest * RoomShare : null;
        if (ceiling != _footing.Ceiling)
        {
            _footing.Ceiling = ceiling;
            _floor = -1;
        }
        return floor is { } hit ? Coords.FromWorld(hit) : ground;
    }

    Vector3? FloorUnder(Vector2 screen, Vec2 ground)
    {
        if (_held.Count == 0) return null;
        var cam = _c.Rig.Cam;
        Vector3 ro = cam.ProjectRayOrigin(screen), rd = cam.ProjectRayNormal(screen);
        string layer = _held[0].Layer;
        float best = float.MaxValue;
        foreach (var n in _pieces.Grid.Near(ground))
            if (n.Layer == layer && !_heldNodes.Contains(n) && n.HasFloors && n.ModelAlong(ro, rd) is { At: { } t } && t > 0 && t < best) best = t;
        return best < float.MaxValue ? WorldShift.ToTable(ro + rd * best) : null;
    }

    void LeaveFloors() => _footing.Ceiling = null;

    public void Point(Vector2 screen)
    {
        if (_c.Ground(screen) is { } g) Move(Pointed(screen, g));
    }
}
