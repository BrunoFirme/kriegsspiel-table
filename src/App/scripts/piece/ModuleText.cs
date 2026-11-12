using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ModuleText
{
    const float BracketTop = 0.726f, BracketBottom = -0.191f;
    const int LargestPixels = 512, Outline = 1;
    static readonly Vector3 ReadsUprightAtModuleTurnZero = new(-90, 180, 0);

    public static Label3D Label(KTable.Core.Model.Lettering lt, double pieceScale, Color tint)
    {
        var font = Fonts.Widget;
        int size = Mathf.Clamp((int)Mathf.Round((float)lt.Size), 1, LargestPixels);
        var ink = Coords.ParseColor(lt.Color, Colors.White) * tint;
        var (ascent, descent) = Fonts.OwnLine(font, size);
        float bracketMiddleAboveLineMiddle = (ascent - descent) / 2 - size * (BracketTop + BracketBottom) / 2;
        return new Label3D
        {
            Text = Markup.Plain(lt.Text), Font = font, FontSize = size, OutlineSize = Outline, OutlineModulate = new Color(0, 0, 0, ink.A),
            Modulate = ink, PixelSize = (float)(lt.UnitsPerPixel * pieceScale), LineSpacing = size - ascent - descent,
            Offset = new Vector2(0, bracketMiddleAboveLineMiddle), RotationDegrees = ReadsUprightAtModuleTurnZero,
            DoubleSided = true, HorizontalAlignment = HorizontalAlignment.Center,
        };
    }
}
