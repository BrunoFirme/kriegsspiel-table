using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class LetterTool
{
    const double DragLeast = 0.3, NearName = 0.6;
    const string NotYours = "Only umpires name the map.";
    static readonly Vector2 BoxOffset = new(8, -14);
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly DrawingLayer _drawings;
    List<Vec2>? _path;
    (Stroke Stroke, Vec2 From)? _moving;
    Vec2? _movedTo;
    List<Vec2>? _pending;
    string? _retyping;
    CanvasLayer? _box;

    public LetterTool(TableContext c, ToolOverlay overlay, DrawingLayer drawings)
    {
        _c = c;
        _overlay = overlay;
        _drawings = drawings;
    }

    public bool Open => _box != null;

    public void Press(Vec2 g, bool twice)
    {
        Drop();
        if (!_c.S.CanEdit(Layers.Umpire)) { _c.Say(NotYours); return; }
        var name = Under(g);
        if (name == null)
        {
            _path = new() { g };
            return;
        }
        if (twice)
        {
            _retyping = name.Id;
            Show(g, name.Text!, name.Points);
            return;
        }
        _moving = (name, g);
        _movedTo = g;
    }

    public void Motion(Vec2 g)
    {
        if (_moving != null) _movedTo = g;
        else _path?.Add(g);
    }

    public void Release()
    {
        if (_moving is { } m && _movedTo is { } to)
        {
            var d = to - m.From;
            _moving = null;
            _movedTo = null;
            if (d.Length >= DragLeast) Replace(m.Stroke, m.Stroke.Points.Select(p => p + d).ToList(), m.Stroke.Text!);
            return;
        }
        if (_path == null) return;
        var path = _path;
        _path = null;
        var points = (path[^1] - path[0]).Length >= DragLeast ? Polyline.Simplify(path) : new List<Vec2> { path[0] };
        Show(path[^1], "", points);
    }

    void Show(Vec2 at, string text, List<Vec2> points)
    {
        _pending = points;
        var entry = new LineEdit { Text = text, PlaceholderText = "Name", CustomMinimumSize = new Vector2(180, 0) };
        _box = new CanvasLayer();
        _box.AddChild(entry);
        _c.Scene.AddChild(_box);
        entry.Position = _c.Rig.Cam.UnprojectPosition(Coords.ToWorld(at, _c.SheetsTop)) + BoxOffset;
        entry.TextSubmitted += Lay;
        entry.FocusExited += () => { if (_box != null && !entry.IsQueuedForDeletion()) Drop(); };
        entry.GrabFocus();
        entry.CaretColumn = text.Length;
    }

    public void Lay(string text)
    {
        var points = _pending;
        var id = _retyping;
        Drop();
        text = text.Trim();
        if (points == null || text.Length == 0 || !_c.S.CanEdit(Layers.Umpire)) return;
        if (id != null)
        {
            if (Find(id) is { } old) Replace(old, old.Points, text);
            return;
        }
        var baseline = points.Count == 1 ? _c.S.Ground.NameAt(points[0], text).Baseline : points;
        _c.S.Do(new Draw { Layer = Layers.Umpire, Stroke = Named(baseline, text, _drawings.DrawBoard()) });
    }

    public void Lay(Vec2 at, string text)
    {
        _pending = new List<Vec2> { at };
        _retyping = null;
        Lay(text);
    }

    public static Stroke Named(List<Vec2> points, string text, string board) => new() { Points = points, Text = text, Width = 0, Color = "", Board = board };

    void Replace(Stroke old, List<Vec2> points, string text) =>
        _c.S.Do(new Batch { Items = { new EraseStroke { Layer = Layers.Umpire, StrokeId = old.Id }, new Draw { Layer = Layers.Umpire, Stroke = Named(points, text, old.Board) } } });

    Stroke? Under(Vec2 g) => _c.S.View.DrawLayer(Layers.Umpire).FirstOrDefault(st => st.Text != null && _drawings.OnDrawBoard(st.Board) && Polyline.Near(st.Points, g, NearName));

    Stroke? Find(string id) => _c.S.View.DrawLayer(Layers.Umpire).FirstOrDefault(st => st.Id == id);

    public bool Drop()
    {
        bool open = _box != null || _path != null || _moving != null;
        _path = null;
        _moving = null;
        _movedTo = null;
        _pending = null;
        _retyping = null;
        if (_box != null) { _box.QueueFree(); _box = null; }
        return open;
    }

    public void Draw()
    {
        if (_c.Tool != Tool.Letter) return;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.85f, 0.4f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (_path is { Count: >= 2 }) _overlay.RibbonPx(_overlay.Mesh, mat, _path, 0.05f, 2f);
        if (_moving is { } m && _movedTo is { } to && m.Stroke.Points.Count >= 2) _overlay.RibbonPx(_overlay.Mesh, mat, m.Stroke.Points.Select(p => p + (to - m.From)).ToList(), 0.05f, 2f);
    }
}
