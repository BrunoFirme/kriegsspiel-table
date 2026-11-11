using System;
using Godot;

namespace KTable.App;

sealed partial class Mosaic : Control
{
    public const int Most = 4;
    const float Gap = 2, Margin = 0.05f, TabWidth = 0.36f, TabTop = 0.04f, BackTop = 0.13f, PapersTop = 0.19f, FrontTop = 0.58f, PaperInset = 0.06f;
    const int Round = 4;
    static readonly Color Back = Ui.Surface.Lightened(0.10f), Front = Ui.Surface.Lightened(0.17f), Cell = new(1, 1, 1, 0.04f);

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

    public readonly record struct Folder(Rect2 Tab, Rect2 Back, Rect2 Papers, Rect2 Front);

    public static Folder Shape(Vector2 size)
    {
        float m = size.X * Margin, w = size.X - 2 * m, bottom = size.Y - m, inset = w * PaperInset;
        var back = new Rect2(m, size.Y * BackTop, w, bottom - size.Y * BackTop);
        var tab = new Rect2(m, size.Y * TabTop, w * TabWidth, back.Position.Y - size.Y * TabTop + Round);
        var papers = new Rect2(m + inset, size.Y * PapersTop, w - 2 * inset, bottom - size.Y * PapersTop);
        var front = new Rect2(m, size.Y * FrontTop, w, bottom - size.Y * FrontTop);
        return new Folder(tab, back, papers, front);
    }

    public static Rect2[] Cells(int count, Rect2 papers)
    {
        if (count <= 0) return Array.Empty<Rect2>();
        float each = (papers.Size.X - Gap * (count - 1)) / count;
        var cells = new Rect2[count];
        for (int i = 0; i < count; i++) cells[i] = new Rect2(papers.Position.X + i * (each + Gap), papers.Position.Y, each, papers.Size.Y);
        return cells;
    }

    static void Fill(CanvasItem on, Rect2 r, Color c) => Ui.Box(c, Round, 0, 0).Draw(on.GetCanvasItem(), r);

    public override void _Draw()
    {
        var f = Shape(Size);
        Fill(this, f.Tab, Back);
        Fill(this, f.Back, Back);
        var cells = Cells(_count, f.Papers);
        for (int i = 0; i < cells.Length; i++)
        {
            DrawRect(cells[i], Cell);
            if (_cells[i] is not { } tex || tex.GetWidth() == 0 || tex.GetHeight() == 0) continue;
            var picture = new Vector2(tex.GetWidth(), tex.GetHeight());
            if (Fit)
            {
                float k = Math.Min(cells[i].Size.X / picture.X, cells[i].Size.Y / picture.Y);
                var shown = picture * k;
                DrawTextureRect(tex, new Rect2(cells[i].Position + new Vector2((cells[i].Size.X - shown.X) / 2, 0), shown), false);
                continue;
            }
            float cover = Math.Max(cells[i].Size.X / picture.X, cells[i].Size.Y / picture.Y);
            var taken = cells[i].Size / cover;
            DrawTextureRectRegion(tex, cells[i], new Rect2((picture - taken) / 2, taken));
        }
        Fill(this, f.Front, Front);
    }
}
