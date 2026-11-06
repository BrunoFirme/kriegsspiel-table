using System.Numerics;
using KTable.Client.Geometry;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public sealed class ThrowTrail
{
    readonly List<(double t, Vec2 at)> _trail = new();

    public void Clear() => _trail.Clear();

    public void Add(double now, Vec2 at)
    {
        _trail.Add((now, at));
        _trail.RemoveAll(s => now - s.t > 0.5);
    }

    public Vector3 Velocity(double now, float cap = 14, float liftPer = 0.2f, float liftMax = 4)
    {
        var recent = _trail.Where(s => now - s.t <= 0.12).ToList();
        if (recent.Count < 2) return Vector3.Zero;
        var dt = recent[^1].t - recent[0].t;
        if (dt < 0.02) return Vector3.Zero;
        var d = recent[^1].at - recent[0].at;
        var v = new Vector3((float)(d.X / dt), 0, (float)(-d.Z / dt));
        float sp = v.Norm();
        if (sp < 1.5f) return Vector3.Zero;
        if (sp > cap) v *= cap / sp;
        return v + Axis.Up * Mathf.Min(liftMax, v.Norm() * liftPer);
    }
}
