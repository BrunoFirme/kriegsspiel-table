using System;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class BookMark : Control
{
    const float EdgeWidth = 2f;
    static readonly Color Fill = new(1f, 0.85f, 0.2f, 0.25f);
    static readonly Color Edge = new(1f, 0.75f, 0.1f, 0.9f);

    public TextureRect? Sheet { get; set; }
    public Func<double[]?>? Box { get; set; }
    public Rect2? Drawn { get; private set; }

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Drawn = null;
        if (Box?.Invoke() is not { Length: SetBookHighlight.Corners } b || Sheet?.Texture is not { } tex) return;
        var area = Shown(Sheet.Size, tex.GetSize());
        var r = Mark(area, b);
        DrawRect(r, Fill, true);
        DrawRect(r, Edge, false, EdgeWidth);
        Drawn = r;
    }

    public static Rect2 Shown(Vector2 box, Vector2 picture)
    {
        if (picture.X <= 0 || picture.Y <= 0) return new Rect2(Vector2.Zero, box);
        var k = Mathf.Min(box.X / picture.X, box.Y / picture.Y);
        var size = picture * k;
        return new Rect2((box - size) / 2, size);
    }

    public static Rect2 Mark(Rect2 page, double[] b)
    {
        float left = (float)Math.Min(b[0], b[2]), right = (float)Math.Max(b[0], b[2]);
        float bottom = (float)Math.Min(b[1], b[3]), top = (float)Math.Max(b[1], b[3]);
        var at = page.Position + new Vector2(left * page.Size.X, (1 - top) * page.Size.Y);
        return new Rect2(at, new Vector2((right - left) * page.Size.X, (top - bottom) * page.Size.Y));
    }
}
