using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class RaisedRows
{
    const float GapM = 1.2f, OverlapM = 0.4f, Parallel = 0.97f, Overlap = 0.5f, CellM = 24;

    public static float[]?[] Of(IReadOnlyList<Vector3[]?> rings, float upm)
    {
        var gaps = new float[]?[rings.Count];
        float most = GapM * upm, behind = OverlapM * upm, cell = CellM * upm;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < rings.Count; i++)
        {
            if (rings[i] is not { Length: 4 } ring) continue;
            foreach (var key in Cells(ring, cell))
            {
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new();
                list.Add(i);
            }
        }
        for (int i = 0; i < rings.Count; i++)
        {
            if (rings[i] is not { Length: 4 } ring) continue;
            var near = new HashSet<int>();
            foreach (var key in Cells(ring, cell))
                foreach (int j in grid[key])
                    if (j != i) near.Add(j);
            foreach (int j in near)
                for (int e = 0; e < 4; e++)
                    if (Gap(ring, e, rings[j]!, most, behind) is { } gap) (gaps[i] ??= [float.NaN, float.NaN, float.NaN, float.NaN])[e] = gap;
        }
        return gaps;
    }

    static IEnumerable<(int, int)> Cells(Vector3[] ring, float cell)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var p in ring)
        {
            x0 = Mathf.Min(x0, p.X);
            x1 = Mathf.Max(x1, p.X);
            z0 = Mathf.Min(z0, p.Z);
            z1 = Mathf.Max(z1, p.Z);
        }
        for (int x = Mathf.FloorToInt(x0 / cell); x <= Mathf.FloorToInt(x1 / cell); x++)
            for (int z = Mathf.FloorToInt(z0 / cell); z <= Mathf.FloorToInt(z1 / cell); z++)
                yield return (x, z);
    }

    static float? Gap(Vector3[] ring, int e, Vector3[] other, float most, float behind)
    {
        Vector3 a = ring[e] with { Y = 0 }, b = ring[(e + 1) % 4] with { Y = 0 };
        var run = b - a;
        float length = run.Length();
        if (length < 1e-6f) return null;
        var along = run / length;
        var middle = ((ring[0] + ring[2]) / 2) with { Y = 0 };
        var outward = new Vector3(-along.Z, 0, along.X);
        if (outward.Dot(a - middle) < 0) outward = -outward;
        for (int f = 0; f < 4; f++)
        {
            Vector3 c = other[f] with { Y = 0 }, d = other[(f + 1) % 4] with { Y = 0 };
            var theirs = d - c;
            float theirLength = theirs.Length();
            if (theirLength < 1e-6f || Mathf.Abs(along.Dot(theirs / theirLength)) < Parallel) continue;
            float gc = (c - a).Dot(outward), gd = (d - a).Dot(outward);
            if (gc < -behind || gd < -behind || gc > most || gd > most) continue;
            float s0 = (c - a).Dot(along), s1 = (d - a).Dot(along);
            float shared = Mathf.Min(length, Mathf.Max(s0, s1)) - Mathf.Max(0, Mathf.Min(s0, s1));
            if (shared < Overlap * Mathf.Min(length, theirLength)) continue;
            return Mathf.Max(0, (gc + gd) / 2);
        }
        return null;
    }

    public static Vector3[] Closed(Vector3[] ring, float[]? gaps)
    {
        if (gaps == null) return ring;
        var moved = (Vector3[])ring.Clone();
        var middle = (ring[0] + ring[2]) / 2;
        for (int e = 0; e < 4; e++)
        {
            if (float.IsNaN(gaps[e]) || gaps[e] <= 0) continue;
            Vector3 a = ring[e], b = ring[(e + 1) % 4];
            var along = (b - a) with { Y = 0 };
            var outward = new Vector3(-along.Z, 0, along.X).Normalized();
            if (outward.Dot(a - middle) < 0) outward = -outward;
            moved[e] += outward * (gaps[e] / 2);
            moved[(e + 1) % 4] += outward * (gaps[e] / 2);
        }
        return moved;
    }

    public static bool Joined(float[]? gaps, Vector3[] ring, Vector3 a, Vector3 b)
    {
        if (gaps == null) return false;
        const float Near = 1e-3f;
        for (int e = 0; e < 4; e++)
        {
            if (float.IsNaN(gaps[e])) continue;
            Vector3 p = ring[e], q = ring[(e + 1) % 4];
            if (p.DistanceSquaredTo(a) < Near && q.DistanceSquaredTo(b) < Near || p.DistanceSquaredTo(b) < Near && q.DistanceSquaredTo(a) < Near) return true;
        }
        return false;
    }
}
