using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class LetterTool
{
    const double DragLeast = 0.3, NearShare = 0.6, LetterGrow = 1.15;
    const int MostLetters = 120;
    const string NotYours = "Only umpires name the map.";
    const float CaretPx = 2f, BlinkSeconds = 0.53f, CaretTall = 0.9f;
    static readonly Color PathInk = new(1, 0.85f, 0.4f), CaretInk = new(0.12f, 0.1f, 0.08f);
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly DrawingLayer _drawings;
    List<Vec2>? _path;
    (Stroke Stroke, Vec2 From)? _moving;
    Vec2? _movedTo;
    Typing? _typing;
    CanvasLayer? _box;
    LineEdit? _keys;

    sealed class Typing
    {
        public required List<Vec2> Points;
        public required Stroke Style;
        public string? Retyping;
        public double Letter;
        public string Text = "";
    }

    public LetterTool(TableContext c, ToolOverlay overlay, DrawingLayer drawings)
    {
        _c = c;
        _overlay = overlay;
        _drawings = drawings;
    }

    public bool Open => _typing != null;

    public string? Typed => _typing?.Text;

    public int StyleTaken { get; private set; }

    bool Scaled => _c.S.View.Calibration.UnitsPerMeter is { } u && u > 0;

    double UnitsPerKept => _c.S.View.Calibration.UnitsPerMeter is { } perMetre && perMetre > 0 ? perMetre : 1;

    double? Kept => Scaled ? _c.Settings.NameMetres : _c.Settings.NameUnits;

    public double LetterKept
    {
        get => Kept is { } kept ? Math.Clamp(kept, Stroke.LeastLetter, Stroke.MostLetter) : PenTool.Tidy(GroundNames.PlaceLetter / UnitsPerKept);
        set
        {
            double tidy = PenTool.Tidy(Math.Clamp(value, Stroke.LeastLetter, Stroke.MostLetter));
            if (Scaled) _c.Settings.NameMetres = tidy;
            else _c.Settings.NameUnits = tidy;
            _c.Settings.Save();
        }
    }

    public double LetterUnits => Math.Clamp(LetterKept * UnitsPerKept, Stroke.LeastLetter, Stroke.MostLetter);

    public double LetterShown
    {
        get => LetterKept * (Scaled ? _c.S.View.Calibration.PerMetre : 1);
        set => LetterKept = value / (Scaled ? _c.S.View.Calibration.PerMetre : 1);
    }

    string Said(double units) => (units / UnitsPerKept * (Scaled ? _c.S.View.Calibration.PerMetre : 1)).ToString("0.###", CultureInfo.InvariantCulture) + " " + _c.S.View.Calibration.UnitName;

    public void Press(Vec2 g, bool twice)
    {
        if (_typing != null) Commit();
        Forget();
        if (!_c.S.CanEdit(Layers.Umpire)) { _c.Say(NotYours); return; }
        var name = Under(g);
        if (name == null)
        {
            _path = new() { g };
            return;
        }
        if (twice)
        {
            Retype(name);
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
            if (d.Length >= DragLeast * Scale(m.Stroke)) Replace(m.Stroke, m.Stroke.Points.Select(p => p + d).ToList(), m.Stroke.Text!, m.Stroke.Letter);
            return;
        }
        if (_path == null) return;
        var path = _path;
        _path = null;
        var points = (path[^1] - path[0]).Length >= DragLeast * LetterUnits ? Polyline.Simplify(path) : new List<Vec2> { path[0] };
        Begin(points, "", Fresh(_drawings.DrawBoard()), LetterUnits, null);
    }

    Stroke Fresh(string board) =>
        new() { Width = 0, Color = _c.Settings.NameInk, Board = board, Align = _c.Settings.NameAlign, Spacing = _c.Settings.NameSpacing };

    void Retype(Stroke name)
    {
        if (name.Align != null)
        {
            _c.Settings.NameInk = name.Color;
            _c.Settings.NameAlign = name.Align;
            _c.Settings.NameSpacing = name.Spacing ?? 0;
            _c.Settings.Save();
        }
        if (name.Letter is { } letter) LetterKept = letter / UnitsPerKept;
        StyleTaken++;
        Begin(name.Points, name.Text!, Json.Clone(name), name.Letter ?? LetterUnits, name.Id);
    }

    public Control? Beside { get; set; }

    void Left(LineEdit keys)
    {
        if (_keys != keys || !GodotObject.IsInstanceValid(keys) || keys.HasFocus()) return;
        var owner = keys.GetViewport()?.GuiGetFocusOwner();
        if (owner == null || Beside != null && GodotObject.IsInstanceValid(Beside) && Beside.IsAncestorOf(owner)) return;
        Commit();
    }

    public void Restyle(bool keys = true, bool sized = false)
    {
        if (_typing is not { } t) return;
        if (sized) t.Letter = LetterUnits;
        else
        {
            t.Style.Color = _c.Settings.NameInk;
            t.Style.Align = _c.Settings.NameAlign;
            t.Style.Spacing = _c.Settings.NameSpacing;
        }
        Show();
        if (keys) TakeKeys();
    }

    public void TakeKeys()
    {
        if (_keys != null && GodotObject.IsInstanceValid(_keys)) _keys.CallDeferred(Control.MethodName.GrabFocus);
    }

    void Begin(List<Vec2> points, string text, Stroke style, double letter, string? retyping)
    {
        if (!_c.S.CanEdit(Layers.Umpire)) return;
        _typing = new Typing { Points = points, Style = style, Retyping = retyping, Letter = letter, Text = text };
        var keys = new LineEdit
        {
            Text = text, MaxLength = MostLetters, Modulate = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = Vector2.One, Size = Vector2.One, ContextMenuEnabled = false, SelectAllOnFocus = false,
        };
        _box = new CanvasLayer();
        _box.AddChild(keys);
        _c.Scene.AddChild(_box);
        _keys = keys;
        keys.TextChanged += t =>
        {
            if (_typing == null) return;
            _typing.Text = t;
            Show();
        };
        keys.TextSubmitted += _ => Commit();
        keys.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
            keys.AcceptEvent();
            Drop();
        };
        keys.FocusExited += () => Callable.From(() => Left(keys)).CallDeferred();
        keys.GrabFocus();
        keys.CaretColumn = text.Length;
        Show();
        _c.Say(Hint());
    }

    void Show()
    {
        if (_typing is not { } t) return;
        _drawings.Lettering.Preview(t.Text.Trim().Length > 0 ? Written(t, t.Text) : null, t.Retyping);
    }

    public static Stroke Named(List<Vec2> points, string text, string board, double? letter = null) =>
        new() { Points = points, Text = text, Width = 0, Color = "", Board = board, Letter = letter };

    static Stroke Written(Typing t, string text)
    {
        var named = Json.Clone(t.Style);
        named.Id = "";
        named.Points = t.Points;
        named.Text = text;
        named.Letter = t.Letter;
        return named;
    }

    public void Commit()
    {
        if (_typing is not { } t) return;
        Close();
        var text = t.Text.Trim();
        if (text.Length == 0 || !_c.S.CanEdit(Layers.Umpire)) return;
        var named = Written(t, text);
        if (t.Retyping == null) { _c.S.Do(new Draw { Layer = Layers.Umpire, Stroke = named }); return; }
        if (Find(t.Retyping) is { } old) _c.S.Do(new Batch { Items = { new EraseStroke { Layer = Layers.Umpire, StrokeId = old.Id }, new Draw { Layer = Layers.Umpire, Stroke = named } } });
    }

    public void Lay(Vec2 at, string text, double? letter = null)
    {
        Begin(new List<Vec2> { at }, text, Fresh(_drawings.DrawBoard()), letter ?? LetterUnits, null);
        Commit();
    }

    public void Type(string text)
    {
        if (_keys == null || _typing == null) return;
        _keys.Text = text;
        _keys.CaretColumn = text.Length;
        _typing.Text = text;
        Show();
    }

    void Replace(Stroke old, List<Vec2> points, string text, double? letter)
    {
        var named = Json.Clone(old);
        named.Id = "";
        named.Points = points;
        named.Text = text;
        named.Letter = letter;
        _c.S.Do(new Batch { Items = { new EraseStroke { Layer = Layers.Umpire, StrokeId = old.Id }, new Draw { Layer = Layers.Umpire, Stroke = named } } });
    }

    static double Scale(Stroke st) => st.Letter ?? GroundNames.PlaceLetter;

    Stroke? Under(Vec2 g) =>
        _c.S.View.DrawLayer(Layers.Umpire).Where(st => st.Text != null && _drawings.OnDrawBoard(st.Board) && Covers(st, g)).OrderBy(st => st.Points.Min(p => (p - g).Length)).FirstOrDefault();

    bool Covers(Stroke st, Vec2 g)
    {
        double letter = Scale(st), reach = Math.Max(NearShare, letter);
        if (st.Points.Count > 1) return Polyline.Near(st.Points, g, reach);
        var (from, to) = GroundNames.Across(st);
        return g.X >= from - letter && g.X <= to + letter && Math.Abs(g.Z - st.Points[0].Z) <= reach;
    }

    Stroke? Find(string id) => _c.S.View.DrawLayer(Layers.Umpire).FirstOrDefault(st => st.Id == id);

    public void Resize(bool larger, Vec2? over)
    {
        double by = larger ? LetterGrow : 1 / LetterGrow;
        if (_typing is { } t)
        {
            t.Letter = Math.Clamp(t.Letter * by, Stroke.LeastLetter, Stroke.MostLetter);
            LetterKept = t.Letter / UnitsPerKept;
            Show();
            _c.Say($"Letters {Said(t.Letter)} tall");
            return;
        }
        if (over is { } g && _c.S.CanEdit(Layers.Umpire) && Under(g) is { } name)
        {
            double letter = Math.Clamp(Scale(name) * by, Stroke.LeastLetter, Stroke.MostLetter);
            Replace(name, name.Points, name.Text!, letter);
            _c.Say($"{name.Text} {Said(letter)} tall");
            return;
        }
        LetterKept = LetterKept * by;
        _c.Say($"Letters {Said(LetterUnits)} tall");
    }

    public string Hint() => _typing != null
        ? "Type the name · the panel sets its ink, size, spacing and alignment · Enter or a click elsewhere lays it, Esc drops it"
        : "Click, or drag a path, and type the name · double-click a name to retype or restyle it, drag to move it · = and − or Ctrl+wheel size the letters, over a name that name";

    void Close()
    {
        _typing = null;
        var box = _box;
        _keys = null;
        _box = null;
        box?.QueueFree();
        _drawings.Lettering.Preview(null, null);
    }

    void Forget()
    {
        _path = null;
        _moving = null;
        _movedTo = null;
    }

    public bool Drop()
    {
        bool open = _typing != null || _path != null || _moving != null;
        Forget();
        if (_typing != null) Close();
        return open;
    }

    public void Draw()
    {
        if (_c.Tool != Tool.Letter) return;
        var mat = new StandardMaterial3D { AlbedoColor = PathInk, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (_path is { Count: >= 2 }) _overlay.RibbonPx(_overlay.Mesh, mat, _path, 0.05f, 2f);
        if (_moving is { } m && _movedTo is { } to) _overlay.RibbonPx(_overlay.Mesh, mat, Moved(m.Stroke, to - m.From), 0.05f, 2f);
        if (_typing is not { } t) return;
        if (t.Points.Count >= 2) _overlay.RibbonPx(_overlay.Mesh, mat, t.Points, 0.05f, 1f);
        var caret = _drawings.Lettering.Caret ?? new MapLettering.Mark(t.Points[0], new Vec2(0, 1), (float)t.Letter);
        if (_keys != null) _keys.Position = _c.Rig.Screen(Coords.ToWorld(caret.At, _c.SheetsTop));
        if (Time.GetTicksMsec() / (ulong)(BlinkSeconds * 1000) % 2 == 1) return;
        var top = new Vec2(caret.At.X + caret.Up.X * caret.Height * CaretTall, caret.At.Z + caret.Up.Z * caret.Height * CaretTall);
        var ink = new StandardMaterial3D { AlbedoColor = CaretInk, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        _overlay.RibbonPx(_overlay.Mesh, ink, new List<Vec2> { caret.At, top }, 0.05f, CaretPx);
    }

    static List<Vec2> Moved(Stroke st, Vec2 by)
    {
        var points = st.Points.Select(p => p + by).ToList();
        if (points.Count >= 2) return points;
        var (from, to) = GroundNames.Across(st);
        return new List<Vec2> { new(from + by.X, points[0].Z), new(to + by.X, points[0].Z) };
    }
}
