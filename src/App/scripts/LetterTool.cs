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
    const double DragLeast = 0.3, LetterGrow = 1.15;
    const int MostLetters = 120;
    const string NotYours = "Only umpires name the map.", Placeholder = "M";
    const float CaretPx = 2f, BlinkSeconds = 0.53f, CaretTall = 0.9f;
    static readonly Color PathInk = new(1, 0.85f, 0.4f), CaretInk = new(0.12f, 0.1f, 0.08f);
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly DrawingLayer _drawings;
    readonly NamePick _pick;
    List<Vec2>? _path;
    Typing? _typing;
    NameGesture? _gesture;
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

    public LetterTool(TableContext c, ToolOverlay overlay, DrawingLayer drawings, NamePick pick)
    {
        _c = c;
        _overlay = overlay;
        _drawings = drawings;
        _pick = pick;
    }

    public bool Open => _typing != null;

    public string? Typed => _typing?.Text;

    public string? Editing => _typing?.Retyping;

    public MapLettering.NameBox? Box => _typing is { } t ? Boxed(t) : null;

    public int StyleTaken { get; private set; }

    bool CanEdit => _c.S.CanEdit(Layers.Umpire);

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
        _path = null;
        _gesture = null;
        if (!CanEdit) { _c.Say(NotYours); return; }
        var screen = _pick.Frame.Screen(g);
        if (_typing is { } t)
        {
            if (Boxed(t) is { } box && _pick.Frame.At(box, screen, g) is var grip and not NameGrip.None)
            {
                if (twice && grip == NameGrip.Body) SelectAll();
                else Grab(t, grip, box, g, screen);
                return;
            }
            Commit();
        }
        if (_pick.Under(g) is { } name)
        {
            Edit(name);
            if (_typing is { } opened && Boxed(opened) is { } held) Grab(opened, NameGrip.Body, held, g, screen);
            return;
        }
        _path = new() { g };
    }

    void Grab(Typing t, NameGrip grip, MapLettering.NameBox box, Vec2 g, Vector2 screen) =>
        _gesture = new NameGesture(grip, Written(t, Shown(t)), box, g, screen, t.Letter);

    public void Motion(Vec2 g)
    {
        if (_gesture is { } m && _typing is { } t)
        {
            if (m.To(g, _pick.Frame.Screen(g), Input.IsKeyPressed(Key.Shift), Input.IsKeyPressed(Key.Ctrl)) is not { } now) return;
            t.Points = now.Points;
            t.Letter = now.Letter ?? t.Letter;
            t.Style.Turn = now.Turn;
            Show();
            _c.Say(m.Said(now, Said));
            return;
        }
        _path?.Add(g);
    }

    public void Release()
    {
        if (_gesture is { } m)
        {
            _gesture = null;
            if (m.Moved && _typing is { } t)
            {
                if (m.Grip == NameGrip.Corner) LetterKept = t.Letter / UnitsPerKept;
                LayKept();
                _c.Say(Hint());
            }
            TakeKeys();
            return;
        }
        if (_path == null) return;
        var path = _path;
        _path = null;
        var points = (path[^1] - path[0]).Length >= DragLeast * LetterUnits ? Polyline.Simplify(path) : new List<Vec2> { path[0] };
        Begin(points, "", Fresh(_drawings.DrawBoard()), LetterUnits, null);
    }

    string? FontKept => _c.Settings.NameFont == Stroke.FontSerif ? null : _c.Settings.NameFont;

    Stroke Fresh(string board) =>
        new()
        {
            Width = 0, Color = _c.Settings.NameInk, Board = board, Align = _c.Settings.NameAlign, Spacing = _c.Settings.NameSpacing,
            Font = FontKept, Bold = _c.Settings.NameBold, Italic = _c.Settings.NameItalic,
        };

    public void Edit(Stroke name)
    {
        if (_typing != null) Commit();
        Forget();
        if (!CanEdit) return;
        if (name.Align != null)
        {
            _c.Settings.NameInk = name.Color;
            _c.Settings.NameAlign = name.Align;
            _c.Settings.NameSpacing = name.Spacing ?? 0;
            _c.Settings.NameFont = name.Font ?? Stroke.FontSerif;
            _c.Settings.NameBold = name.Bold;
            _c.Settings.NameItalic = name.Italic;
            _c.Settings.Save();
        }
        double letter = name.Letter ?? _drawings.Lettering.LetterOf(name);
        LetterKept = letter / UnitsPerKept;
        StyleTaken++;
        Begin(name.Points, name.Text!, Json.Clone(name), letter, name.Id);
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
            t.Style.Font = FontKept;
            t.Style.Bold = _c.Settings.NameBold;
            t.Style.Italic = _c.Settings.NameItalic;
        }
        Show();
        if (keys) TakeKeys();
    }

    public void TakeKeys()
    {
        if (_keys != null && GodotObject.IsInstanceValid(_keys)) _keys.CallDeferred(Control.MethodName.GrabFocus);
    }

    void SelectAll()
    {
        if (_keys == null || !GodotObject.IsInstanceValid(_keys)) return;
        _keys.SelectAll();
        TakeKeys();
    }

    void Begin(List<Vec2> points, string text, Stroke style, double letter, string? retyping)
    {
        if (!CanEdit) return;
        _pick.Pick(null);
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
        keys.TextChanged += typed =>
        {
            if (_typing == null) return;
            _typing.Text = typed;
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
        named.Id = t.Retyping ?? "";
        named.Points = t.Points;
        named.Text = text;
        named.Letter = t.Letter;
        return named;
    }

    static string Shown(Typing t) => t.Text.Trim() is { Length: > 0 } text ? text : Placeholder;

    MapLettering.NameBox? Boxed(Typing t) => _drawings.Lettering.BoxOf(Written(t, Shown(t)));

    bool Same(Stroke old, Stroke named)
    {
        if (old.Letter == null && named.Letter is { } letter && Math.Abs(letter - _drawings.Lettering.LetterOf(old)) < Stroke.LeastLetter / 2) named.Letter = null;
        return Json.Serialize(old) == Json.Serialize(named);
    }

    public void Commit()
    {
        if (_typing is not { } t) return;
        Close();
        if (!CanEdit) return;
        var text = t.Text.Trim();
        var old = t.Retyping == null ? null : Find(t.Retyping);
        if (text.Length == 0)
        {
            if (old != null) _c.S.Do(new EraseStroke { Layer = Layers.Umpire, StrokeId = old.Id });
            return;
        }
        var named = Written(t, text);
        if (old == null)
        {
            named.Id = "";
            _c.S.Do(new Draw { Layer = Layers.Umpire, Stroke = named });
        }
        else if (!Same(old, named)) _pick.Lay(old, named);
    }

    void LayKept()
    {
        if (_typing is not { Retyping: { } id } t || !CanEdit || Find(id) is not { } old) return;
        var text = t.Text.Trim();
        if (text.Length > 0 && Written(t, text) is var named && !Same(old, named)) _pick.Lay(old, named);
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

    Stroke? Find(string id) => _c.S.View.DrawLayer(Layers.Umpire).FirstOrDefault(st => st.Id == id);

    public void Resize(bool larger, Vec2? over)
    {
        double by = larger ? LetterGrow : 1 / LetterGrow;
        if (_typing is { } t)
        {
            t.Letter = Math.Clamp(t.Letter * by, Stroke.LeastLetter, Stroke.MostLetter);
            LetterKept = t.Letter / UnitsPerKept;
            Show();
            LayKept();
            _c.Say($"Letters {Said(t.Letter)} tall");
            return;
        }
        if (over is { } g && _pick.Under(g) is { } name)
        {
            var sized = Json.Clone(name);
            sized.Letter = Math.Clamp(_drawings.Lettering.LetterOf(name) * by, Stroke.LeastLetter, Stroke.MostLetter);
            _pick.Lay(name, sized);
            _c.Say($"{name.Text} {Said(sized.Letter.Value)} tall");
            return;
        }
        LetterKept = LetterKept * by;
        _c.Say($"Letters {Said(LetterUnits)} tall");
    }

    public string Hint() => _typing != null
        ? "Type the name · drag it to move it, a corner to size it, the round knob to turn it · the panel sets ink, font, size, spacing and alignment · Enter or a click elsewhere lays it, Esc drops what was typed"
        : "Click, or drag a path, and type a name · click a name to edit it, drag it to move it · = and − or Ctrl+wheel size the letters, over a name that name";

    void Close()
    {
        _typing = null;
        _gesture = null;
        var box = _box;
        _keys = null;
        _box = null;
        box?.QueueFree();
        _drawings.Lettering.Preview(null, null);
    }

    void Forget()
    {
        _path = null;
        _gesture = null;
    }

    public bool Drop()
    {
        bool open = _typing != null || _path != null;
        Forget();
        if (_typing != null) Close();
        return open;
    }

    public void Draw()
    {
        if (_c.Tool != Tool.Letter) return;
        var mat = new StandardMaterial3D { AlbedoColor = PathInk, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (_path is { Count: >= 2 }) _overlay.RibbonPx(_overlay.Mesh, mat, _path, 0.05f, 2f);
        if (_gesture == null && _c.Ground(Pointer.In(_c.Scene.GetViewport())) is { } at && _pick.Under(at) is { } hovered
            && hovered.Id != _typing?.Retyping && _drawings.Lettering.BoxOf(hovered) is { } over)
            _pick.Frame.Outline(over);
        if (_typing is not { } t) return;
        if (t.Points.Count >= 2) _overlay.RibbonPx(_overlay.Mesh, mat, t.Points, 0.05f, 1f);
        if (Boxed(t) is { } box) _pick.Frame.Picked(box, handles: true);
        var up = MapLettering.AlongOf(t.Points.Count == 1 ? t.Style.TurnDeg : 0);
        var caret = _drawings.Lettering.Caret ?? new MapLettering.Mark(t.Points[0], new Vec2(-up.Z, up.X), (float)t.Letter);
        if (_keys != null) _keys.Position = _c.Rig.Cam.UnprojectPosition(Coords.ToWorld(caret.At, _c.SheetsTop));
        if (Time.GetTicksMsec() / (ulong)(BlinkSeconds * 1000) % 2 == 1) return;
        var top = new Vec2(caret.At.X + caret.Up.X * caret.Height * CaretTall, caret.At.Z + caret.Up.Z * caret.Height * CaretTall);
        var ink = new StandardMaterial3D { AlbedoColor = CaretInk, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        _overlay.RibbonPx(_overlay.Mesh, ink, new List<Vec2> { caret.At, top }, 0.05f, CaretPx);
    }
}
