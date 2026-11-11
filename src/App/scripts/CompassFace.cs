using Godot;

namespace KTable.App;

static class CompassFace
{
    public static readonly Color Disc = new(0.10f, 0.10f, 0.11f, 0.86f);
    public static readonly Color Ivory = new(0.93f, 0.91f, 0.85f);
    public static readonly Color Shade = new(0.55f, 0.53f, 0.49f);
    public static readonly Color Red = new(0.78f, 0.20f, 0.16f);
    public static readonly Color RedShade = new(0.50f, 0.11f, 0.09f);

    public static void Draw(CanvasItem ci, Font font, Vector2 c, float r, float turn, bool hover)
    {
        Vector2 Dir(float a) => new(Mathf.Sin(a + turn), -Mathf.Cos(a + turn));

        ci.DrawCircle(c, r, Disc, true, -1, true);
        ci.DrawArc(c, r - 0.5f, 0, Mathf.Tau, 96, new Color(Ivory, hover ? 0.95f : 0.7f), 1.2f, true);
        ci.DrawArc(c, r - 13, 0, Mathf.Tau, 96, new Color(Ui.Accent, 0.75f), 1f, true);
        for (int i = 0; i < 36; i++)
        {
            float a = Mathf.DegToRad(i * 10);
            bool major = i % 3 == 0, cardinal = i % 9 == 0;
            if (cardinal) continue;
            float len = major ? 5.5f : 3f;
            ci.DrawLine(c + Dir(a) * (r - 3), c + Dir(a) * (r - 3 - len), new Color(Ivory, major ? 0.75f : 0.4f), major ? 1.2f : 1f, true);
        }

        int fs = Mathf.RoundToInt(Mathf.Clamp(r * 0.24f, 9, 14));
        foreach (var (letter, deg) in new[] { ("N", 0f), ("E", 90f), ("S", 180f), ("W", 270f) })
        {
            var at = c + Dir(Mathf.DegToRad(deg)) * (r - 7);
            var sz = font.GetStringSize(letter, HorizontalAlignment.Left, -1, fs);
            var col = letter == "N" ? new Color(0.95f, 0.42f, 0.36f) : new Color(Ivory, 0.85f);
            ci.DrawString(font, at + new Vector2(-sz.X / 2, font.GetAscent(fs) / 2 - 1), letter, HorizontalAlignment.Left, -1, fs, col);
        }

        float inner = r - 13;
        void Point(float a, float len, float half, Color lit, Color dark)
        {
            var tip = c + Dir(a) * len;
            var left = c + Dir(a - Mathf.Pi / 2) * half;
            var right = c + Dir(a + Mathf.Pi / 2) * half;
            ci.DrawColoredPolygon(new[] { c, left, tip }, lit);
            ci.DrawColoredPolygon(new[] { c, tip, right }, dark);
            ci.DrawPolyline(new[] { left, tip }, lit, 1f, true);
            ci.DrawPolyline(new[] { tip, right }, dark, 1f, true);
        }
        for (int i = 0; i < 4; i++)
            Point(Mathf.DegToRad(45 + i * 90), inner * 0.64f, inner * 0.16f, new Color(Shade, 0.95f), new Color(0.32f, 0.31f, 0.29f, 0.95f));
        for (int i = 1; i < 4; i++)
            Point(Mathf.DegToRad(i * 90), inner * 0.96f, inner * 0.22f, Ivory, Shade);
        Point(0, inner * 0.96f, inner * 0.22f, Red, RedShade);
        ci.DrawCircle(c, 3.4f, Ui.Accent, true, -1, true);
        ci.DrawCircle(c, 1.6f, Disc, true, -1, true);
    }
}
