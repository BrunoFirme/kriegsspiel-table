using System;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

static class ToolTile
{
    const int Wide = 56, High = 30, Left = 3, Right = Wide - 4, Foot = High - 3, Level = 21, Middle = High / 2;
    static readonly Color Body = new(0.914f, 0.898f, 0.859f, 0.26f), Edge = new(0.914f, 0.898f, 0.859f, 0.92f), Water = new(0.55f, 0.74f, 0.9f, 0.95f);

    public static Button Of(string tool, string name, string tip, bool chosen)
    {
        var picture = Image.CreateEmpty(Wide, High, false, Image.Format.Rgba8);
        if (!Shaping(picture, tool)) Building(picture, tool);
        var b = new Button
        {
            Text = name, Icon = ImageTexture.CreateFromImage(picture), ToggleMode = true, ButtonPressed = chosen, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
            IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Ui.MarkTool(b);
        return b;
    }

    public static VBoxContainer Rows((string Key, string Text, string Tip)[] tools, string now, int toARow, Action<string> pick)
    {
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        for (int i = 0; i < tools.Length; i += toARow)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
            for (int k = i; k < Math.Min(tools.Length, i + toARow); k++)
            {
                var (key, text, tip) = tools[k];
                var b = Of(key, text, tip, key == now);
                b.Pressed += () => pick(key);
                row.AddChild(b);
            }
            rows.AddChild(row);
        }
        return rows;
    }

    static double Bell(double x, double middle, double wide) => Math.Exp(-(x - middle) * (x - middle) / (wide * wide));

    static double Winding(double x) => Middle + 4 * Math.Sin(x / 7);

    static bool Shaping(Image p, string tool)
    {
        double mid = Wide / 2.0;
        switch (tool)
        {
            case "raise":
                Ground(p, x => Level - 9 * Bell(x, mid, 9));
                Arrow(p, (int)mid, 10, 2);
                return true;
            case "lower":
                Ground(p, x => 11 + 10 * Bell(x, mid, 9));
                Arrow(p, (int)mid, 3, 16);
                return true;
            case "smooth":
                Ground(p, x => 15 + (x < mid ? Math.Abs((x % 8) - 4) * 2.2 - 4 : 2.5 * Math.Sin(x / 5)));
                return true;
            case "flatten":
                Ground(p, x => 15 + 5 * Math.Sin(x / 5));
                for (int x = Left; x <= Right; x += 3) Dot(p, x, 15, Edge);
                return true;
            case ReliefTool.LevelMode:
                Ground(p, x => Level - 11 * Math.Clamp(Math.Min(x - 12, 46 - x) / 7.0, 0, 1));
                Line(p, 5, 5, 5, Foot, Edge);
                for (int y = 6; y < Foot; y += 5) Line(p, 5, y, 8, y, Edge);
                return true;
            case ReliefTool.HillMode:
                Ground(p, x => Foot - 1 - 16 * Bell(x, mid, 11));
                return true;
            case ReliefTool.RidgeMode:
                Line(p, 7, Middle, 49, Middle, Edge);
                for (int x = 10; x <= 46; x += 6)
                {
                    Line(p, x, Middle, x - 3, 7, Edge);
                    Line(p, x, Middle, x - 3, 23, Edge);
                }
                return true;
            case ReliefTool.ValleyMode:
                Ground(p, x => 9 + 13 * Math.Max(0, 1 - Math.Abs(x - mid) / 13));
                return true;
            case ReliefTool.SlopeMode:
                Ground(p, x => 7 + 17 * Math.Clamp((x - 8) / 40, 0, 1));
                return true;
            case ReliefTool.ContoursMode:
                Ellipse(p, mid, Middle, 23, 11);
                Ellipse(p, mid - 2, Middle, 15, 7);
                Ellipse(p, mid - 3, Middle, 7, 3);
                return true;
            case ReliefTool.RoughenMode:
                Ground(p, x => 15 + Math.Sin(x * 1.9) * 2.5 + Math.Sin(x * 0.7 + 1) * 3);
                return true;
            case ReliefTool.ErodeMode:
                Ground(p, x => 9 + 9 * Math.Max(0, 1 - Math.Abs((x + 4) % 16 - 8) / 3.5));
                for (int x = 12; x < Right; x += 16) Line(p, x, 19, x, Foot, Water);
                return true;
            case ReliefTool.LiftMode:
                Ground(p, _ => 17);
                Arrow(p, 17, 13, 3);
                Arrow(p, 39, 13, 3);
                return true;
            default:
                return false;
        }
    }

    static void Building(Image p, string tool)
    {
        int mid = Wide / 2;
        switch (tool)
        {
            case BuiltKinds.Settlement:
                Line(p, 6, 17, 50, 13, Edge);
                for (int i = 0; i < 6; i++)
                {
                    Block(p, 8 + i * 7, 8 - i / 2, 5, 4);
                    Block(p, 9 + i * 7, 20 - i / 2, 5, 4);
                }
                Block(p, 26, 3, 6, 5);
                break;
            case BuiltKinds.Fort:
                Star(p, mid, Middle, 13, 7, 5);
                Block(p, mid - 2, Middle - 2, 5, 4);
                break;
            case BuildPrefs.Redoubt:
                Outline(p, mid - 9, Middle - 9, 18, 18);
                Outline(p, mid - 12, Middle - 12, 24, 24);
                break;
            case BuiltKinds.Building:
                Block(p, mid - 11, Middle - 6, 22, 12);
                Line(p, mid - 11, Middle, mid + 10, Middle, Edge);
                break;
            case "track":
                for (int x = Left; x <= Right; x++)
                    if (x / 4 % 2 == 0) Dot(p, x, (int)Math.Round(Winding(x)), Edge);
                break;
            case "road":
                Band(p, 2, Edge, false);
                break;
            case "chaussee":
                Band(p, 4, Edge, true);
                break;
            case "stream":
                Band(p, 0, Water, false);
                break;
            case "river":
                Band(p, 4, Water, true);
                break;
        }
    }

    static void Band(Image p, int half, Color ink, bool filled)
    {
        for (int x = Left; x <= Right; x++)
        {
            int y = (int)Math.Round(Winding(x));
            if (filled)
                for (int fill = -half + 1; fill < half; fill++) Dot(p, x, y + fill, ink with { A = Body.A + 0.2f });
            Dot(p, x, y - half, ink);
            Dot(p, x, y + half, ink);
        }
    }

    static void Block(Image p, int x, int y, int wide, int high)
    {
        for (int row = 0; row < high; row++)
            for (int col = 0; col < wide; col++) Dot(p, x + col, y + row, Edge);
    }

    static void Outline(Image p, int x, int y, int wide, int high)
    {
        Line(p, x, y, x + wide, y, Edge);
        Line(p, x, y + high, x + wide, y + high, Edge);
        Line(p, x, y, x, y + high, Edge);
        Line(p, x + wide, y, x + wide, y + high, Edge);
    }

    static void Star(Image p, int cx, int cy, double outer, double inner, int points)
    {
        for (int i = 0; i < points * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / points, b = a + Math.PI / points, ra = i % 2 == 0 ? outer : inner, rb = i % 2 == 0 ? inner : outer;
            Line(p, cx + (int)Math.Round(Math.Cos(a) * ra), cy + (int)Math.Round(Math.Sin(a) * ra), cx + (int)Math.Round(Math.Cos(b) * rb), cy + (int)Math.Round(Math.Sin(b) * rb), Edge);
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
