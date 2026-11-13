using System;
using Godot;

namespace KTable.App;

static class ShapeChip
{
    const int Wide = 56, High = 30, Left = 3, Right = Wide - 4, Foot = High - 3, Level = 21;
    static readonly Color Body = new(0.914f, 0.898f, 0.859f, 0.26f), Edge = new(0.914f, 0.898f, 0.859f, 0.92f), Water = new(0.55f, 0.74f, 0.9f, 0.95f);

    public static Button Of(string mode, string name, string tip, bool chosen)
    {
        var picture = Image.CreateEmpty(Wide, High, false, Image.Format.Rgba8);
        Draw(picture, mode);
        var b = new Button
        {
            Text = name, Icon = ImageTexture.CreateFromImage(picture), ToggleMode = true, ButtonPressed = chosen, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
            IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Ui.MarkTool(b);
        return b;
    }

    static double Bell(double x, double middle, double wide) => Math.Exp(-(x - middle) * (x - middle) / (wide * wide));

    static void Draw(Image p, string mode)
    {
        double mid = Wide / 2.0;
        switch (mode)
        {
            case "raise":
                Ground(p, x => Level - 9 * Bell(x, mid, 9));
                Arrow(p, (int)mid, 10, 2);
                break;
            case "lower":
                Ground(p, x => 11 + 10 * Bell(x, mid, 9));
                Arrow(p, (int)mid, 3, 16);
                break;
            case "smooth":
                Ground(p, x => 15 + (x < mid ? Math.Abs((x % 8) - 4) * 2.2 - 4 : 2.5 * Math.Sin(x / 5)));
                break;
            case "flatten":
                Ground(p, x => 15 + 5 * Math.Sin(x / 5));
                for (int x = Left; x <= Right; x += 3) Dot(p, x, 15, Edge);
                break;
            case ReliefTool.LevelMode:
                Ground(p, x => Level - 11 * Math.Clamp(Math.Min(x - 12, 46 - x) / 7.0, 0, 1));
                Line(p, 5, 5, 5, Foot, Edge);
                for (int y = 6; y < Foot; y += 5) Line(p, 5, y, 8, y, Edge);
                break;
            case ReliefTool.HillMode:
                Ground(p, x => Foot - 1 - 16 * Bell(x, mid, 11));
                break;
            case ReliefTool.RidgeMode:
                Line(p, 7, 15, 49, 15, Edge);
                for (int x = 10; x <= 46; x += 6)
                {
                    Line(p, x, 15, x - 3, 7, Edge);
                    Line(p, x, 15, x - 3, 23, Edge);
                }
                break;
            case ReliefTool.ValleyMode:
                Ground(p, x => 9 + 13 * Math.Max(0, 1 - Math.Abs(x - mid) / 13));
                break;
            case ReliefTool.SlopeMode:
                Ground(p, x => 7 + 17 * Math.Clamp((x - 8) / 40, 0, 1));
                break;
            case ReliefTool.ContoursMode:
                Ellipse(p, mid, 15, 23, 11);
                Ellipse(p, mid - 2, 15, 15, 7);
                Ellipse(p, mid - 3, 15, 7, 3);
                break;
            case ReliefTool.RoughenMode:
                Ground(p, x => 15 + Math.Sin(x * 1.9) * 2.5 + Math.Sin(x * 0.7 + 1) * 3);
                break;
            case ReliefTool.ErodeMode:
                Ground(p, x => 9 + 9 * Math.Max(0, 1 - Math.Abs((x + 4) % 16 - 8) / 3.5));
                for (int x = 12; x < Right; x += 16) Line(p, x, 19, x, Foot, Water);
                break;
            case ReliefTool.LiftMode:
                Ground(p, _ => 17);
                Arrow(p, 17, 13, 3);
                Arrow(p, 39, 13, 3);
                break;
        }
    }

    static void Dot(Image p, int x, int y, Color ink)
    {
        if (x >= 0 && y >= 0 && x < Wide && y < High) p.SetPixel(x, y, ink);
    }

    static void Ground(Image p, Func<double, double> top)
    {
        for (int x = Left; x <= Right; x++)
        {
            int y = (int)Math.Round(Math.Clamp(top(x), 1, Foot));
            for (int fill = y + 1; fill <= Foot; fill++) Dot(p, x, fill, Body);
            Dot(p, x, y, Edge);
            Dot(p, x, y - 1, Edge);
        }
    }

    static void Line(Image p, int x0, int y0, int x1, int y1, Color ink)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int i = 0; i <= steps; i++) Dot(p, x0 + (x1 - x0) * i / Math.Max(1, steps), y0 + (y1 - y0) * i / Math.Max(1, steps), ink);
    }

    static void Arrow(Image p, int x, int from, int to)
    {
        int head = to < from ? 1 : -1;
        Line(p, x, from, x, to, Edge);
        Line(p, x + 1, from, x + 1, to, Edge);
        for (int i = 1; i <= 3; i++)
        {
            Dot(p, x - i, to + head * i, Edge);
            Dot(p, x + 1 + i, to + head * i, Edge);
        }
    }

    static void Ellipse(Image p, double cx, double cy, double rx, double ry)
    {
        for (int i = 0; i < 160; i++) Dot(p, (int)Math.Round(cx + Math.Cos(i * Math.Tau / 160) * rx), (int)Math.Round(cy + Math.Sin(i * Math.Tau / 160) * ry), Edge);
    }
}
