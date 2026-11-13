using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class BuiltNear
{
    public const float ReachM = 32;
    const float LeastTexelM = 2;
    const int MostSide = 1024, House = 0, Way = 1;

    static readonly string[] Grounds = ["yard", "square", "garden", "pen", "forum", "churchyard", "parade", "threshing", "orchard", "vineyard", "arena", "quarry", "saltpans"];

    static float HalfM(string cls) => cls switch
    {
        "chaussee" => 4.75f,
        "street" or "road" => 3.25f,
        "quay" => 3,
        "lane" or "causeway" => 1.8f,
        "track" => 1.3f,
        "wall" or "palisade" or "mole" => 1.1f,
        "rampart" => 5.5f,
        _ => -1,
    };

    public static (int Cols, int Rows, byte[] Texels) Of(IReadOnlyList<BuiltPainter.Shape> shapes, float fullWidth, float fullHeight, float perMetre)
    {
        if (shapes.Count == 0 || perMetre <= 0) return (1, 1, [255, 255]);
        float wideM = fullWidth / perMetre, deepM = fullHeight / perMetre, texel = Mathf.Max(LeastTexelM, Mathf.Max(wideM, deepM) / MostSide);
        int cols = Math.Max(1, Mathf.CeilToInt(wideM / texel)), rows = Math.Max(1, Mathf.CeilToInt(deepM / texel));
        var texels = new byte[cols * rows * 2];
        Array.Fill(texels, (byte)255);
        var size = new Vector2(wideM, deepM);
        foreach (var s in shapes)
        {
            bool stands = s.Kind == PartKinds.Block || s.Kind == PartKinds.Area && Array.IndexOf(Grounds, s.Class) >= 0;
            float half = s.Kind == PartKinds.Line ? HalfM(s.Class) : -1;
            if (!stands && half < 0 || s.Uv.Length < 2) continue;
            var at = Array.ConvertAll(s.Uv, p => p * size);
            var box = new Rect2(s.Box.Position * size, s.Box.Size * size).Grow(ReachM + Math.Max(half, 0));
            int x0 = Math.Clamp((int)(box.Position.X / wideM * cols), 0, cols - 1), x1 = Math.Clamp((int)(box.End.X / wideM * cols), 0, cols - 1);
            int y0 = Math.Clamp((int)(box.Position.Y / deepM * rows), 0, rows - 1), y1 = Math.Clamp((int)(box.End.Y / deepM * rows), 0, rows - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2((x + 0.5f) / cols * wideM, (y + 0.5f) / rows * deepM);
                    float off = stands ? Outside(at, p) : Mathf.Max(0, Along(at, p) - half);
                    int i = (y * cols + x) * 2 + (stands ? House : Way);
                    byte near = (byte)Mathf.Clamp(off / ReachM * 255, 0, 255);
                    if (near < texels[i]) texels[i] = near;
                }
        }
        return (cols, rows, texels);
    }

    static float Off(Vector2 p, Vector2 a, Vector2 b)
    {
        var run = b - a;
        float along = run.LengthSquared() <= 0 ? 0 : Mathf.Clamp((p - a).Dot(run) / run.LengthSquared(), 0, 1);
        return (p - (a + run * along)).Length();
    }

    static float Along(Vector2[] line, Vector2 p)
    {
        float least = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++) least = Math.Min(least, Off(p, line[i], line[i + 1]));
        return least;
    }

    static float Outside(Vector2[] ring, Vector2 p)
    {
        bool inside = false;
        float least = float.MaxValue;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            Vector2 a = ring[j], b = ring[i];
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            least = Math.Min(least, Off(p, a, b));
        }
        return inside ? 0 : least;
    }
}
