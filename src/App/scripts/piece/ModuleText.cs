using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ModuleText
{
    const float BracketTop = 0.726f, BracketBottom = -0.191f;
    const int LargestPixels = 512, Outline = 1;
    static readonly Vector3 ReadsUprightAtModuleTurnZero = new(-90, 180, 0);

    static int Pixels(KTable.Core.Model.Lettering lt) => Mathf.Clamp((int)Mathf.Round((float)lt.Size), 1, LargestPixels);

    static float PixelSize(KTable.Core.Model.Lettering lt, double pieceScale) => (float)(lt.UnitsPerPixel * pieceScale);

    public static float Span(KTable.Core.Model.Lettering lt, double pieceScale) => Writing.Span(Markup.Plain(lt.Text), Pixels(lt) * PixelSize(lt, pieceScale));

    public static Color InkOf(KTable.Core.Model.Lettering lt, string? tint) =>
        Coords.ParseColor(tint ?? Markup.OneColour(Markup.InkLines(lt.Text), lt.Color) ?? lt.Color, Colors.White);

    public static Node3D Drawn(KTable.Core.Model.Lettering lt, double pieceScale, Color layerTint, string? tint)
    {
        var lines = Markup.InkLines(lt.Text);
        if ((tint ?? Markup.OneColour(lines, lt.Color)) is { } one) return Label(Markup.Plain(lt.Text), lt, pieceScale, Coords.ParseColor(one, Colors.White) * layerTint);
        var font = Fonts.Widget;
        int size = Pixels(lt);
        float pixel = PixelSize(lt, pieceScale);
        float Wide(string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X * pixel;
        var drawn = new Node3D { RotationDegrees = ReadsUprightAtModuleTurnZero };
        for (int i = 0; i < lines.Count; i++)
        {
            float middle = ((lines.Count - 1) / 2f - i) * size * pixel, left = -lines[i].Sum(run => Wide(run.Text)) / 2;
            foreach (var run in lines[i])
            {
                float across = Wide(run.Text);
                if (run.Text.Trim().Length > 0)
                {
                    var part = Label(run.Text, lt, pieceScale, Coords.ParseColor(run.Color ?? lt.Color, Colors.White) * layerTint);
                    part.RotationDegrees = Vector3.Zero;
                    part.Position = new Vector3(left + across / 2, middle, 0);
                    drawn.AddChild(part);
                }
                left += across;
            }
        }
        return drawn;
    }

    public static Label3D Label(string text, KTable.Core.Model.Lettering lt, double pieceScale, Color ink)
    {
        var font = Fonts.Widget;
        int size = Pixels(lt);
        var (ascent, descent) = Fonts.OwnLine(font, size);
        float bracketMiddleAboveLineMiddle = (ascent - descent) / 2 - size * (BracketTop + BracketBottom) / 2;
        return new Label3D
        {
            Text = text, Font = font, FontSize = size, OutlineSize = Outline, OutlineModulate = new Color(0, 0, 0, ink.A),
            Modulate = ink, PixelSize = PixelSize(lt, pieceScale), LineSpacing = size - ascent - descent,
            Offset = new Vector2(0, bracketMiddleAboveLineMiddle), RotationDegrees = ReadsUprightAtModuleTurnZero,
            DoubleSided = true, HorizontalAlignment = HorizontalAlignment.Center,
        };
    }
}
