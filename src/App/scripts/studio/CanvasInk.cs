using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CanvasInk : FaceInk
{
    public readonly record struct Mark(Rect2 Box, Color Color, string? Text = null, float Pixel = 0);

    public readonly record struct Marker(Rect2 Box, Texture2D? Picture, Color Tint, string Name);

    readonly bool _back;
    readonly List<Mark> _marks = new();

    CanvasInk(bool back) => _back = back;

    public static List<Mark> Of(PieceDef d, PieceInstance p, Vector2 footprint, bool back, float textScale)
    {
        var ink = new CanvasInk(back);
        PiecePrints.Lay(ink, d, p, footprint, textScale, namesUnset: true, new(), new());
        return ink._marks;
    }

    public override void Plane(bool back, float x, float z, float sw, float sh, Fill fill)
    {
        if (back == _back) _marks.Add(new Mark(new Rect2(x - sw / 2, z - sh / 2, sw, sh), FillColor(fill)));
    }

    protected override void Letters(bool back, string text, float x, float z, float pixel, Color color)
    {
        if (back == _back) _marks.Add(new Mark(new Rect2(x, z, 0, 0), color, text, pixel));
    }
}
