using System;
using Godot;

namespace KTable.App;

sealed partial class Mosaic : Control
{
    public const int Most = 4;
    const float Gap = 3, Margin = 0.03f, TabWidth = 0.40f, TabSlant = 0.07f, TabTop = 0.02f, BackTop = 0.14f;
    const float PapersTop = 0.21f, PapersBottom = 0.92f, FrontTop = 0.58f, PaperInset = 0.07f, PaperEdge = 2;
    const int Round = 5, CornerSteps = 4;
    static readonly Color ManilaBack = new(0.439f, 0.361f, 0.235f), ManilaFront = new(0.588f, 0.486f, 0.322f), Paper = new(0.871f, 0.847f, 0.784f);
    static Color? _back, _front;
    static Color Back => _back ??= Ui.Hued(ManilaBack);
    static Color Front => _front ??= Ui.Hued(ManilaFront);

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
        float m = size.X * Margin, w = size.X - 2 * m, bottom = size.Y, inset = w * PaperInset;
        var back = new Rect2(m, size.Y * BackTop, w, bottom - size.Y * BackTop);
        var tab = new Rect2(m, size.Y * TabTop, w * TabWidth, back.Position.Y - size.Y * TabTop + Round);
        var papers = new Rect2(m + inset, size.Y * PapersTop, w - 2 * inset, size.Y * (PapersBottom - PapersTop));
        var front = new Rect2(m, size.Y * FrontTop, w, bottom - size.Y * FrontTop);
        return new Folder(tab, back, papers, front);
    }

    public static Rect2[] Cells(int count, Rect2 papers)
    {
        if (count <= 0) return Array.Empty<Rect2>();
        float each = (papers.Size.X - Gap * (count - 1)) / count;
        var cells = new Rect2[count];
        for (int i = 0; i < count; i++)
        {
            float x = papers.Position.X + i * (each + Gap);
            cells[i] = new Rect2(x, papers.Position.Y, i == count - 1 ? papers.End.X - x : each, papers.Size.Y);
        }
        return cells;
    }

    static Vector2[] TabOutline(Rect2 tab, float slant)
    {
        var points = new Vector2[CornerSteps + 4];
        points[0] = new Vector2(tab.Position.X, tab.End.Y);
        var centre = tab.Position + new Vector2(Round, Round);
        for (int i = 0; i <= CornerSteps; i++)
        {
            float a = Mathf.Pi + Mathf.Pi / 2 * i / CornerSteps;
            points[i + 1] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Round;
        }
        points[CornerSteps + 2] = new Vector2(tab.End.X - slant, tab.Position.Y);
        points[CornerSteps + 3] = tab.End;
        return points;
    }

    void Fill(Rect2 r, Color c) => Ui.Box(c, Round, 0, 0).Draw(GetCanvasItem(), r);

    public override void _Draw()
    {
        var f = Shape(Size);
        var tab = TabOutline(f.Tab, f.Back.Size.X * TabSlant);
        DrawColoredPolygon(tab, Back);
        DrawPolyline(tab, Back, 1, true);
        Fill(f.Back, Back);
        var cells = Cells(_count, f.Papers);
        for (int i = 0; i < cells.Length; i++)
        {
            DrawRect(cells[i], Paper);
            var sheet = cells[i].Grow(-PaperEdge);
            if (_cells[i] is not { } tex || tex.GetWidth() == 0 || tex.GetHeight() == 0) continue;
            var picture = new Vector2(tex.GetWidth(), tex.GetHeight());
            if (Fit)
            {
                float k = Math.Min(sheet.Size.X / picture.X, sheet.Size.Y / picture.Y);
                var shown = picture * k;
                DrawTextureRect(tex, new Rect2(sheet.Position + new Vector2((sheet.Size.X - shown.X) / 2, 0), shown), false);
                continue;
            }
            float cover = Math.Max(sheet.Size.X / picture.X, sheet.Size.Y / picture.Y);
            var taken = sheet.Size / cover;
            DrawTextureRectRegion(tex, sheet, new Rect2((picture - taken) / 2, taken));
        }
        Fill(f.Front, Front);
    }
}
