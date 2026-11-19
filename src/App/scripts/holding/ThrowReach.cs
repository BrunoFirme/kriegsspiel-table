using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

static class ThrowReach
{
    const float SecondsAhead = 0.8f, Farthest = 160, Step = 10;

    public static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    static float Of(Vector3 velocity) => Mathf.Min(Farthest, Flat(velocity).Length() * SecondsAhead);

    public static List<Vector3> Along(List<Vector3> starts, Vector3 velocity)
    {
        float reach = Of(velocity);
        if (reach <= 0 || starts.Count == 0) return starts;
        var way = Flat(velocity).Normalized();
        var middle = starts.Aggregate(Vector3.Zero, (a, s) => a + s) / starts.Count;
        var points = new List<Vector3>(starts);
        for (float d = Step; d < reach + Step; d += Step) points.Add(middle + way * Mathf.Min(d, reach));
        return points;
    }

    public static (Vector3 at, float span) Ground(Vector3 middle, Vector3 velocity, float span)
    {
        float reach = Of(velocity);
        return (middle + Flat(velocity).Normalized() * (reach / 2), span + reach);
    }
}
