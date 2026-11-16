using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class BoundSpace(TableContext c, Func<string, Transform3D?> frameOf)
{
    const float BelowMetres = 0.35f, PastMetres = 0.05f, StepsAway = 2.5f;

    public readonly record struct Prism(Vector2[] Ring, float Floor, float Ceiling);

    public sealed record Space(List<Prism> In, List<Prism> Out)
    {
        public bool Empty => In.Count == 0 && Out.Count == 0;
    }

    static readonly Space Nothing = new([], []);

    ulong _frame = ulong.MaxValue;
    readonly Dictionary<string, Space> _made = new(StringComparer.OrdinalIgnoreCase);

    float Metre => AvatarPose.UnitsPerMetre(c.S.View);

    public Space For(string player)
    {
        ulong now = Engine.GetPhysicsFrames() + Engine.GetProcessFrames();
        if (now != _frame)
        {
            _made.Clear();
            _frame = now;
        }
        if (_made.TryGetValue(player, out var made)) return made;
        if (!Bounds.Binds(c.S.View, player)) return _made[player] = Nothing;
        var inside = new List<Prism>();
        var outside = new List<Prism>();
        foreach (var (layer, rule) in Bounds.Active(c.S.View, player))
            foreach (var shape in layer.Shapes)
                if (Placed(shape) is { } prism) (rule == BoundRule.In ? inside : outside).Add(prism);
        return _made[player] = new Space(inside, outside);
    }

    public Prism? Placed(BoundShape shape)
    {
        if (shape.Polygon.Count < 3) return null;
        if (shape.On == null)
            return new Prism(shape.Polygon.Select(p => new Vector2((float)p.X, (float)-p.Z)).ToArray(), (float)shape.Floor, (float)shape.Ceiling);
        if (frameOf(shape.On) is not { } frame) return null;
        var ring = shape.Polygon.Select(p => frame * new Vector3((float)p.X, (float)shape.Floor, (float)p.Z)).Select(v => new Vector2(v.X, v.Z)).ToArray();
        var middle = shape.Polygon.Aggregate(Vector2.Zero, (a, p) => a + new Vector2((float)p.X, (float)p.Z)) / shape.Polygon.Count;
        float floor = (frame * new Vector3(middle.X, (float)shape.Floor, middle.Y)).Y, ceiling = (frame * new Vector3(middle.X, (float)shape.Ceiling, middle.Y)).Y;
        return new Prism(ring, Mathf.Min(floor, ceiling), Mathf.Max(floor, ceiling));
    }

    public bool Inside(Prism p, Vector3 at) => at.Y >= p.Floor - BelowMetres * Metre && at.Y < p.Ceiling && InRing(p.Ring, new Vector2(at.X, at.Z));

    public bool Allows(Space s, Vector3 at) => (s.In.Count == 0 || s.In.Any(p => Inside(p, at))) && !s.Out.Any(p => Inside(p, at));

    public float Outside(Space s, Vector3 at) => Allows(s, at) ? 0 : Nearest(s, at) is { } spot ? spot.DistanceTo(at) : float.MaxValue;

    public Vector3? Nearest(Space s, Vector3 at)
    {
        if (Allows(s, at)) return at;
        float past = PastMetres * Metre;
        var tried = new List<Vector3>();
        foreach (var p in s.In)
        {
            var flat = Crossed(p.Ring, new Vector2(at.X, at.Z), past);
            float y = Mathf.Clamp(at.Y, p.Floor, p.Ceiling - past);
            tried.Add(new Vector3(flat.X, y, flat.Y));
            var middle = p.Ring.Aggregate(Vector2.Zero, (a, v) => a + v) / p.Ring.Length;
            foreach (var corner in p.Ring)
            {
                var pulled = corner + (middle - corner).Normalized() * past;
                tried.Add(new Vector3(pulled.X, y, pulled.Y));
            }
        }
        if (s.In.Count == 0) tried.Add(at);
        foreach (var start in tried.ToList())
            foreach (var p in s.Out.Where(p => Inside(p, start)))
                tried.AddRange(Exits(p, start, past));
        foreach (var p in s.Out.Where(p => Inside(p, at))) tried.AddRange(Exits(p, at, past));
        Vector3? best = null;
        float nearest = float.MaxValue;
        foreach (var spot in tried)
        {
            float d = spot.DistanceSquaredTo(at);
            if (d >= nearest || !Allows(s, spot)) continue;
            (best, nearest) = (spot, d);
        }
        return best;
    }

    public Vector3 Slide(Space s, Vector3 from, Vector3 to)
    {
        if (Allows(s, to)) return to;
        if (!Allows(s, from) && Outside(s, to) <= Outside(s, from)) return to;
        float step = from.DistanceTo(to);
        return Nearest(s, to) is { } spot && spot.DistanceTo(to) <= step * StepsAway + PastMetres * Metre ? spot : from;
    }

    IEnumerable<Vector3> Exits(Prism p, Vector3 at, float past)
    {
        var flat = Crossed(p.Ring, new Vector2(at.X, at.Z), past);
        yield return new Vector3(flat.X, at.Y, flat.Y);
        yield return at with { Y = p.Floor - BelowMetres * Metre - past };
        yield return at with { Y = p.Ceiling + past };
    }

    public static bool InRing(Vector2[] ring, Vector2 q)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if (ring[i].Y > q.Y != ring[j].Y > q.Y && q.X < (ring[j].X - ring[i].X) * (q.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X)
                inside = !inside;
        return inside;
    }

    public static Vector2 Edge(Vector2[] ring, Vector2 q)
    {
        var best = ring[0];
        float nearest = float.MaxValue;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var a = ring[j];
            var ab = ring[i] - a;
            float t = ab.LengthSquared() > 1e-12f ? Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0, 1) : 0;
            var on = a + ab * t;
            float d = on.DistanceSquaredTo(q);
            if (d < nearest) (best, nearest) = (on, d);
        }
        return best;
    }

    static Vector2 Crossed(Vector2[] ring, Vector2 q, float past)
    {
        var on = Edge(ring, q);
        var across = on - q;
        if (across.LengthSquared() < 1e-12f)
        {
            var middle = ring.Aggregate(Vector2.Zero, (a, v) => a + v) / ring.Length;
            across = InRing(ring, q) ? on - middle : middle - on;
        }
        return on + across.Normalized() * past;
    }
}
