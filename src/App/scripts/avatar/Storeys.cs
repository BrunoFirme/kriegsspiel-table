using System;
using System.Collections.Generic;
using V = System.Numerics.Vector3;

namespace KTable.App;

static class Storeys
{
    const float Upward = 0.9f, ApartShare = 0.15f, FloorSquares = 2f, Bucket = 0.01f;

    public static float[] Of(V corner, V size, List<(V, V, V)> triangles)
    {
        float apart = size.Y * ApartShare, least = apart * apart * FloorSquares, step = Math.Max(size.Y * Bucket, 1e-6f);
        var area = new SortedDictionary<long, float>();
        foreach (var (a, b, c) in triangles)
        {
            var cross = V.Cross(b - a, c - a);
            float twice = cross.Length();
            if (twice <= 0 || Math.Abs(cross.Y) / twice < Upward) continue;
            long at = (long)Math.Round((a.Y + b.Y + c.Y) / 3 / step);
            area[at] = area.GetValueOrDefault(at) + twice / 2;
        }
        var floors = new List<(float At, float Area)>();
        foreach (var (at, a) in area)
        {
            if (a < least) continue;
            float y = at * step;
            if (floors.Count > 0 && y - floors[^1].At < apart)
            {
                if (a > floors[^1].Area) floors[^1] = (y, a);
                continue;
            }
            floors.Add((y, a));
        }
        return floors.Count > 0 ? floors.ConvertAll(f => f.At).ToArray() : [corner.Y];
    }
}
