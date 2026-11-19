using System.Numerics;
using KTable.Client.Geometry;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public sealed class ThrowTrail
{
    const double Kept = 0.5, Window = 0.12, Latest = 0.05, Shortest = 0.02, StillAfter = 0.06, StillFrames = 1.5;
    const float MostTurn = 12;
    readonly List<(double t, Vec2 at)> _trail = new();

    public void Clear() => _trail.Clear();

    public void Add(double now, Vec2 at)
    {
        _trail.Add((now, at));
        _trail.RemoveAll(s => now - s.t > Kept);
    }

    public Fling Hand(double now)
    {
        if (_trail.Count < 2) return default;
        var last = _trail[^1];
        if (now - last.t > Math.Max(StillAfter, (last.t - _trail[^2].t) * StillFrames)) return default;
        var first = _trail[Math.Max(0, _trail.FindLastIndex(s => last.t - s.t >= Latest))];
        if (last.t - first.t < Shortest) return default;
        return new Fling(Along(first, last), Turn(_trail.Where(s => now - s.t <= Window).ToList()));
    }

    static Vector3 Along((double t, Vec2 at) a, (double t, Vec2 at) b)
    {
        double dt = b.t - a.t;
        if (dt <= 0) return Vector3.Zero;
        var d = b.at - a.at;
        return new Vector3((float)(d.X / dt), 0, (float)(-d.Z / dt));
    }

    static float Turn(List<(double t, Vec2 at)> s)
    {
        if (s.Count < 3) return 0;
        var mid = s[s.Count / 2];
        Vector3 a = Along(s[0], mid), b = Along(mid, s[^1]);
        if (a == Vector3.Zero || b == Vector3.Zero) return 0;
        float angle = Mathf.Atan2(a.Z * b.X - a.X * b.Z, a.Dot(b));
        return Mathf.Clamp(angle / (float)((s[^1].t - s[0].t) / 2), -MostTurn, MostTurn);
    }
}
