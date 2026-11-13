using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class GroundChip
{
    const int Wide = 64, High = 26, Edge = 1;
    const float Strong = 0.85f, Light = 0.45f;
    static readonly Color Ink = new(0.16f, 0.15f, 0.11f), Blue = new(0.2f, 0.33f, 0.44f), Leaf = new(0.2f, 0.27f, 0.17f);

    public static Button Of(byte kind, Color wash, string name, bool chosen)
    {
        var picture = Image.CreateEmpty(Wide, High, false, Image.Format.Rgba8);
        picture.Fill(Ink);
        picture.FillRect(new Rect2I(Edge, Edge, Wide - 2 * Edge, High - 2 * Edge), wash);
        Sign(picture, GroundKinds.Name(kind));
        var b = new Button
        {
            Text = name, Icon = ImageTexture.CreateFromImage(picture), ToggleMode = true, ButtonPressed = chosen, FocusMode = Control.FocusModeEnum.None,
            IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Ui.MarkTool(b);
        return b;
    }

    static void Sign(Image p, string kind)
    {
        var dice = new Random(kind.Length * 31 + kind[0]);
        int X() => dice.Next(5, Wide - 5);
        int Y() => dice.Next(5, High - 5);
        switch (kind)
        {
            case "grass":
                for (int i = 0; i < 7; i++) Tuft(p, 6 + i * 8 + i % 2 * 3, 7 + i * 5 % 14, Ink);
                break;
            case "field":
                for (int x = -High; x < Wide; x += 6) Line(p, x, High - 3, x + High - 5, 2, Ink, Light);
                break;
            case "wood":
                for (int i = 0; i < 8; i++) Blob(p, 6 + i * 7 + i % 2 * 2, 7 + i * 7 % 13, 3, Leaf);
                break;
            case "marsh":
                for (int i = 0; i < 6; i++) Line(p, 5 + i * 9, 7 + i * 5 % 14, 11 + i * 9, 7 + i * 5 % 14, Blue, Strong);
                for (int i = 0; i < 3; i++) Tuft(p, 12 + i * 19, 18 - i * 5, Ink);
                break;
            case "water":
                for (int row = 0; row < 3; row++)
                    for (int x = 4; x < Wide - 4; x++) Dot(p, x, 7 + row * 6 + (int)Math.Round(Math.Sin(x * 0.45 + row) * 1.4), Blue, Strong);
                break;
            case "earth":
                for (int i = 0; i < 22; i++) Dot(p, X(), Y(), Ink, Light);
                break;
            case "rock":
                for (int i = 0; i < 9; i++)
                {
                    int x = 6 + i * 6, y = 6 + i * 7 % 13;
                    Line(p, x, y + 5, x + 4, y, Ink, Strong);
                    Line(p, x + 3, y + 6, x + 6, y + 2, Ink, Light);
                }
                break;
            case "sand":
                for (int i = 0; i < 110; i++) Dot(p, X(), Y(), Ink, Light);
                break;
            case "snow":
                for (int i = 0; i < 4; i++) Line(p, 9 + i * 14, 9 + i % 2 * 8, 15 + i * 14, 8 + i % 2 * 8, Blue, Light);
                break;
        }
    }

    static void Dot(Image p, int x, int y, Color ink, float strength)
    {
        if (x < Edge || y < Edge || x >= Wide - Edge || y >= High - Edge) return;
        p.SetPixel(x, y, p.GetPixel(x, y).Lerp(ink, strength));
    }

    static void Line(Image p, int x0, int y0, int x1, int y1, Color ink, float strength)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int i = 0; i <= steps; i++) Dot(p, x0 + (x1 - x0) * i / Math.Max(1, steps), y0 + (y1 - y0) * i / Math.Max(1, steps), ink, strength);
    }

    static void Tuft(Image p, int x, int y, Color ink)
    {
        Line(p, x, y, x + 3, y, ink, Strong);
        Line(p, x + 5, y, x + 8, y, ink, Strong);
    }

    static void Blob(Image p, int cx, int cy, int r, Color ink)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r) Dot(p, cx + x, cy + y, ink, Strong);
        Dot(p, cx, cy + r + 1, ink, Strong);
    }
}
