using System;
using System.Linq;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

sealed class StudioWords
{
    readonly StudioCanvas _c;
    TextEdit? _box;
    string? _layer;
    int _fromLine, _fromCol;
    static readonly Color Cursor = new(0.35f, 0.85f, 0.95f), Picked = new(0.35f, 0.85f, 0.95f, 0.35f);

    public StudioWords(StudioCanvas canvas) { _c = canvas; }

    public void Take(TextEdit box, ArtLayer layer)
    {
        _box = box;
        _layer = layer.Id;
        box.DeselectOnFocusLossEnabled = false;
        box.CaretChanged += _c.RedrawOver;
        box.FocusEntered += _c.RedrawOver;
        box.FocusExited += _c.RedrawOver;
    }

    TextEdit? Box(ArtLayer l) => _box is { } b && GodotObject.IsInstanceValid(b) && !b.IsQueuedForDeletion() && _layer == l.Id ? b : null;

    public bool Holds(ArtLayer l) => Box(l) != null;

    public void Focus(bool all)
    {
        var box = _box;
        Callable.From(() =>
        {
            if (box == null || !GodotObject.IsInstanceValid(box) || !box.IsVisibleInTree()) return;
            box.GrabFocus();
            if (all) box.SelectAll();
        }).CallDeferred();
    }

    public void Press(ArtLayer l, Vector2 doc, bool extend, bool word)
    {
        if (Box(l) is not { } box) return;
        var (line, col) = SpotAt(l, doc);
        if (extend)
        {
            (_fromLine, _fromCol) = box.HasSelection() ? (box.GetSelectionOriginLine(), box.GetSelectionOriginColumn()) : (box.GetCaretLine(), box.GetCaretColumn());
            Pick(box, line, col);
        }
        else
        {
            (_fromLine, _fromCol) = (line, col);
            Pick(box, line, col);
            if (word) box.SelectWordUnderCaret();
        }
        Focus(all: false);
    }

    public void DragTo(ArtLayer l, Vector2 doc)
    {
        if (Box(l) is not { } box) return;
        var (line, col) = SpotAt(l, doc);
        Pick(box, line, col);
    }

    void Pick(TextEdit box, int line, int col)
    {
        if (line != _fromLine || col != _fromCol)
        {
            box.Select(_fromLine, _fromCol, line, col);
            return;
        }
        box.Deselect();
        box.SetCaretLine(line);
        box.SetCaretColumn(col);
    }

    internal static (int Line, int Col) SpotAt(ArtLayer l, Vector2 doc)
    {
        var t = ArtDraw.LinesOf(l);
        var p = l.Xf().AffineInverse() * doc;
        int line = Math.Clamp((int)Mathf.Floor((p.Y - t.Top) / t.Height), 0, t.Lines.Length - 1);
        string s = t.Lines[line];
        float left = t.Left(line);
        int best = 0;
        float gap = Mathf.Abs(p.X - left);
        for (int i = 1; i <= s.Length; i++)
        {
            if (i < s.Length && char.IsLowSurrogate(s[i])) continue;
            float g = Mathf.Abs(p.X - left - t.Width(line, i));
            if (g < gap) (best, gap) = (i, g);
        }
        return (line, Points(s, best));
    }

    public void Draw(Control o)
    {
        if (_c.Tool != "text" || _c.Sel is not { Kind: "text" } l || Box(l) is not { } box || !box.HasFocus()) return;
        var t = ArtDraw.LinesOf(l);
        var xf = l.Xf();
        Vector2 On(float x, float y) => _c.ToScreen(xf * new Vector2(x, y));
        if (box.HasSelection())
        {
            int a = box.GetSelectionFromLine(), b = Math.Min(box.GetSelectionToLine(), t.Lines.Length - 1);
            for (int i = a; i <= b; i++)
            {
                float x0 = X(t, i, i == a ? box.GetSelectionFromColumn() : 0), x1 = X(t, i, i == b ? box.GetSelectionToColumn() : int.MaxValue);
                float y0 = t.Top + t.Height * i, y1 = y0 + t.Height;
                o.DrawColoredPolygon(new[] { On(x0, y0), On(x1, y0), On(x1, y1), On(x0, y1) }, Picked);
            }
        }
        int line = Math.Min(box.GetCaretLine(), t.Lines.Length - 1);
        float cx = X(t, line, box.GetCaretColumn()), cy = t.Top + t.Height * line;
        o.DrawLine(On(cx, cy), On(cx, cy + t.Height), Cursor, 1.5f * Ui.Scale, true);
    }

    static float X(ArtDraw.TextLines t, int line, int col) => t.Left(line) + t.Width(line, Units(t.Lines[line], col));

    static int Points(string s, int units) => s.Take(units).Count(ch => !char.IsLowSurrogate(ch));

    static int Units(string s, int points)
    {
        int i = 0;
        for (int n = 0; n < points && i < s.Length; n++) i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? 2 : 1;
        return i;
    }
}
