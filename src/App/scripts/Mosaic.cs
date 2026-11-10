using System;
using Godot;

namespace KTable.App;

sealed partial class Mosaic : Control
{
    public const int Most = 4;
    const float Gap = 2;
    static readonly Color Cell = new(1, 1, 1, 0.04f);

    readonly Texture2D?[] _cells = new Texture2D?[Most];
    int _count;

    public bool Fit { get; init; }

    public Mosaic() => MouseFilter = MouseFilterEnum.Ignore;

    public int Count
    {
        get => _count;
        set
        {
            _count = Math.Clamp(value, 0, Most);
            QueueRedraw();
        }
    }

    public Texture2D? this[int i] => i >= 0 && i < Most ? _cells[i] : null;

    public void Set(int i, Texture2D? picture)
    {
        if (i < 0 || i >= _count) return;
        _cells[i] = picture;
        QueueRedraw();
    }

    public static Rect2[] Cells(int count, Vector2 size)
    {
        float halfW = (size.X - Gap) / 2, halfH = (size.Y - Gap) / 2, right = halfW + Gap, low = halfH + Gap;
        return count switch
        {
            1 => new[] { new Rect2(0, 0, size) },
            2 => new[] { new Rect2(0, 0, halfW, size.Y), new Rect2(right, 0, halfW, size.Y) },
            3 => new[] { new Rect2(0, 0, halfW, size.Y), new Rect2(right, 0, halfW, halfH), new Rect2(right, low, halfW, halfH) },
            4 => new[] { new Rect2(0, 0, halfW, halfH), new Rect2(right, 0, halfW, halfH), new Rect2(0, low, halfW, halfH), new Rect2(right, low, halfW, halfH) },
            _ => Array.Empty<Rect2>(),
        };
    }

    public override void _Draw()
    {
        var cells = Cells(_count, Size);
        for (int i = 0; i < cells.Length; i++)
        {
            DrawRect(cells[i], Cell);
            if (_cells[i] is not { } tex || tex.GetWidth() == 0 || tex.GetHeight() == 0) continue;
            var picture = new Vector2(tex.GetWidth(), tex.GetHeight());
            if (Fit)
            {
                float k = Math.Min(cells[i].Size.X / picture.X, cells[i].Size.Y / picture.Y);
                var shown = picture * k;
                DrawTextureRect(tex, new Rect2(cells[i].Position + (cells[i].Size - shown) / 2, shown), false);
                continue;
            }
            float cover = Math.Max(cells[i].Size.X / picture.X, cells[i].Size.Y / picture.Y);
            var taken = cells[i].Size / cover;
            DrawTextureRectRegion(tex, cells[i], new Rect2((picture - taken) / 2, taken));
        }
    }
}
