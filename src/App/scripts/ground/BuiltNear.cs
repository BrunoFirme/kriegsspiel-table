using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class BuiltNear
{
    public const float ReachM = 32;
    public const float WidestHalfM = 6;
    const float LeastTexelM = 2;
    const int MostSide = 1024, House = 0, Way = 1;

    static readonly string[] Grounds = ["yard", "square", "garden", "pen", "forum", "churchyard", "parade", "threshing", "orchard", "vineyard", "arena", "quarry", "saltpans", "airstrip", "terreplein", "ravelin", "platform", "moat", "drymoat", "glacis", "allotments",
        "pitch", "minefield"];

    static float HalfM(string cls) => cls switch
    {
        "highway" => WidestHalfM,
        "chaussee" => 4.75f,
        "street" or "road" => 3.25f,
        "railway" => 2.4f,
        "quay" => 3,
        "lane" or "causeway" => 1.8f,
        "track" => 1.3f,
        "wall" or "palisade" or "mole" or "dam" or "trench" or "wardwall" => 1.1f,
        "rampart" => 5.5f,
        _ => -1,
    };

    public readonly record struct Field(int Cols, int Rows, byte[] Texels, float WideM, float DeepM);

    public static Field Of(IReadOnlyList<BuiltPainter.Shape> shapes, float fullWidth, float fullHeight, float perMetre)
    {
        if (shapes.Count == 0 || perMetre <= 0) return new Field(1, 1, [255, 255], 0, 0);
        float wideM = fullWidth / perMetre, deepM = fullHeight / perMetre, texel = Mathf.Max(LeastTexelM, Mathf.Max(wideM, deepM) / MostSide);
        int cols = Math.Max(1, Mathf.CeilToInt(wideM / texel)), rows = Math.Max(1, Mathf.CeilToInt(deepM / texel));
        var field = new Field(cols, rows, new byte[cols * rows * 2], wideM, deepM);
        Array.Fill(field.Texels, (byte)255);
        int bands = Math.Clamp(System.Environment.ProcessorCount, 1, rows);
        Parallel.For(0, bands, band =>
        {
            int from = (int)((long)rows * band / bands), to = (int)((long)rows * (band + 1) / bands);
            foreach (var s in shapes) Lay(field, s, new Rect2I(0, from, cols, to - from));
        });
        return field;
    }

    public static void Again(Field field, IEnumerable<BuiltPainter.Shape> near, Rect2 uv)
    {
        var size = new Vector2(field.WideM, field.DeepM);
        var box = new Rect2(uv.Position * size, uv.Size * size).Grow(ReachM + WidestHalfM);
        int x0 = Math.Clamp((int)(box.Position.X / field.WideM * field.Cols), 0, field.Cols - 1), x1 = Math.Clamp((int)(box.End.X / field.WideM * field.Cols), 0, field.Cols - 1);
        int y0 = Math.Clamp((int)(box.Position.Y / field.DeepM * field.Rows), 0, field.Rows - 1), y1 = Math.Clamp((int)(box.End.Y / field.DeepM * field.Rows), 0, field.Rows - 1);
        var within = new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        for (int y = y0; y <= y1; y++) Array.Fill(field.Texels, (byte)255, (y * field.Cols + x0) * 2, within.Size.X * 2);
        foreach (var s in near) Lay(field, s, within);
    }

    static void Lay(Field field, BuiltPainter.Shape s, Rect2I within)
    {
        bool stands = s.Kind == PartKinds.Block || s.Kind == PartKinds.Area && Array.IndexOf(Grounds, s.Class) >= 0;
        float half = s.Kind == PartKinds.Line ? HalfM(s.Class) : -1;
        if (!stands && half < 0 || s.Uv.Length < 2) return;
        var (cols, rows, texels, wideM, deepM) = field;
        var size = new Vector2(wideM, deepM);
        var box = new Rect2(s.Box.Position * size, s.Box.Size * size).Grow(ReachM + Math.Max(half, 0));
        int y0 = Math.Max(within.Position.Y, Math.Clamp((int)(box.Position.Y / deepM * rows), 0, rows - 1)), y1 = Math.Min(within.End.Y - 1, Math.Clamp((int)(box.End.Y / deepM * rows), 0, rows - 1));
        int x0 = Math.Max(within.Position.X, Math.Clamp((int)(box.Position.X / wideM * cols), 0, cols - 1)), x1 = Math.Min(within.End.X - 1, Math.Clamp((int)(box.End.X / wideM * cols), 0, cols - 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2((x + 0.5f) / cols * wideM, (y + 0.5f) / rows * deepM);
                float off = stands ? Outside(s.Uv, size, p) : Mathf.Max(0, Along(s.Uv, size, p) - half);
                int i = (y * cols + x) * 2 + (stands ? House : Way);
                byte near = (byte)Mathf.Clamp(off / ReachM * 255, 0, 255);
                if (near < texels[i]) texels[i] = near;
            }
    }

    static float Off(Vector2 p, Vector2 a, Vector2 b)
    {
        var run = b - a;
        float along = run.LengthSquared() <= 0 ? 0 : Mathf.Clamp((p - a).Dot(run) / run.LengthSquared(), 0, 1);
        return (p - (a + run * along)).Length();
    }

    static float Along(Vector2[] line, Vector2 size, Vector2 p)
    {
        float least = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++) least = Math.Min(least, Off(p, line[i] * size, line[i + 1] * size));
        return least;
    }

    static float Outside(Vector2[] ring, Vector2 size, Vector2 p)
    {
        bool inside = false;
        float least = float.MaxValue;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            Vector2 a = ring[j] * size, b = ring[i] * size;
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            least = Math.Min(least, Off(p, a, b));
        }
        return inside ? 0 : least;
    }
}
