using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class WallSlice
{
    const float Bucket = 10f;
    const int KeptSlices = 64;

    static readonly Dictionary<(string File, int Height), List<(Vector2 A, Vector2 B)>> Slices = new();
    static readonly List<(string File, int Height)> Order = new();

    public static Vector2? Snap(PieceNode node, float height, Vector2 at, float reach)
    {
        if (Of(node, height) is not { Count: > 0 } walls) return null;
        Vector2? corner = null, side = null;
        float nearCorner = reach * reach, nearSide = reach * reach;
        foreach (var (a, b) in walls)
        {
            foreach (var end in new[] { a, b })
                if (end.DistanceSquaredTo(at) is var d && d < nearCorner) (corner, nearCorner) = (end, d);
            var ab = b - a;
            if (ab.LengthSquared() < 1e-12f) continue;
            var on = a + ab * Mathf.Clamp((at - a).Dot(ab) / ab.LengthSquared(), 0, 1);
            if (on.DistanceSquaredTo(at) is var e && e < nearSide) (side, nearSide) = (on, e);
        }
        return corner ?? side;
    }

    static List<(Vector2 A, Vector2 B)>? Of(PieceNode node, float height)
    {
        if (node.Model?.Mesh is not { } mesh || ModelCache.PathOf(mesh) is not { } file) return null;
        var key = (file, Mathf.RoundToInt(height * Bucket));
        if (Slices.TryGetValue(key, out var kept)) return kept;
        var reading = ModelTriangles.Of(file);
        if (!reading.IsCompletedSuccessfully || reading.Result is not { } pieces) return null;
        var walls = new List<(Vector2 A, Vector2 B)>();
        foreach (var faces in pieces)
            for (int i = 0; i + 2 < faces.Length; i += 3)
                if (Cut(faces[i], faces[i + 1], faces[i + 2], height) is { } cut) walls.Add(cut);
        Slices[key] = walls;
        Order.Add(key);
        if (Order.Count > KeptSlices)
        {
            Slices.Remove(Order[0]);
            Order.RemoveAt(0);
        }
        return walls;
    }

    static (Vector2 A, Vector2 B)? Cut(Vector3 a, Vector3 b, Vector3 c, float y)
    {
        var points = new List<Vector2>(2);
        foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
        {
            if (p.Y > y == q.Y > y) continue;
            float t = (y - p.Y) / (q.Y - p.Y);
            var at = p.Lerp(q, t);
            points.Add(new Vector2(at.X, at.Z));
        }
        return points.Count == 2 ? (points[0], points[1]) : null;
    }
}
