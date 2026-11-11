using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class TopLine
{
    readonly PieceStudio _s;
    readonly Dictionary<string, Button> _viewBtns = new();
    LineEdit _packName = null!;
    Label _crumb = null!, _pieceTitle = null!, _hint = null!, _saved = null!;
    HBoxContainer _views = null!;
    double _flashUntil;
    string _hintText = "";

    public TopLine(PieceStudio s) => _s = s;

    internal static readonly Dictionary<string, string> PaintHints = new()
    {
        ["move"] = "Drag a layer to move it · its corners resize it (Shift: freely) · the knob above turns it (Shift: 15°) · arrows nudge · Delete removes it",
        ["brush"] = "Drag to paint · = and − or Ctrl+wheel size the brush · Shift-click draws straight from the last stroke · Alt-click takes a colour · X swaps the two colours",
        ["erase"] = "Drag to rub out — only the layer selected on the right · = and − or Ctrl+wheel size the brush",
        ["shape"] = "Drag out the shape · Shift keeps it square or straight · Alt draws it from the centre",
        ["text"] = "Click to put words down, click words to change them; type them in the tool panel",
        ["fill"] = "Click an area to flood it with the colour",
        ["pick"] = "Click: that colour becomes the ink · right-click: the fill colour",
        ["crop"] = "Drag a picture's handles to crop it · drag across the face to cut the whole face down",
        ["outline"] = "The piece is cut along this line on the table · click to add corners, drag them, right-click to remove one — or pick an outline in the panel",
        ["measure"] = "Drag to measure · drag an end to move it · right-click a line to remove it",
        ["place"] = "Drag a box to where it prints on this side · drag a corner to size it",
        ["pan"] = "Drag to move the view · the wheel zooms",
    };

    public PanelContainer Panel { get; private set; } = null!;

    public string Saved { set => _saved.Text = value; }

    public PanelContainer Build(Action<Control> menu, Action done)
    {
        Panel = new PanelContainer();
        Panel.AddThemeStyleboxOverride("panel", Ui.TopBar());
        Panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _s.Root.AddChild(Panel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        Panel.AddChild(row);
        var menuBtn = new Button { Flat = true, CustomMinimumSize = new Vector2(30, 24), FocusMode = Control.FocusModeEnum.None, TooltipText = "The studio's menu" };
        menuBtn.AddChild(new ToolIcon { Kind = "menu" });
        menuBtn.Pressed += () => menu(menuBtn);
        row.AddChild(menuBtn);
        var who = new Label { Text = "PIECE STUDIO", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        Ui.HeadStyle(who);
        row.AddChild(who);
        _packName = new LineEdit { Text = _s.Pack.Name, CustomMinimumSize = new Vector2(150, 0), TooltipText = "The pack's name: click to rename it", ExpandToTextLength = true, SelectAllOnFocus = true };
        _packName.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4 });
        _packName.TextSubmitted += _ => { RenamePack(); _packName.ReleaseFocus(); };
        _packName.FocusExited += RenamePack;
        row.AddChild(_packName);
        _crumb = new Label { Text = "›", Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(_crumb);
        _pieceTitle = new Label { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(_pieceTitle);
        row.AddChild(new VSeparator());
        _hint =
            new Label { Modulate = Ui.Muted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ClipText = true,
                CustomMinimumSize = new Vector2(80, 0) };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_hint);
        _views = new HBoxContainer();
        _views.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        row.AddChild(_views);
        _saved = new Label { Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(64, 0), HorizontalAlignment = HorizontalAlignment.Right };
        _saved.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(_saved);
        if (AgentEndpoint.Current is { } agent) row.AddChild(AgentPill.Make(agent, () => { DialogPages.LastPage["app-settings"] = "AI agent"; AppSettings.OpenWindow(_s.Root, _s.Cfg); }));
        if (_s.InPlace != null)
        {
            row.AddChild(Ui.Btn("Discard", _s.AskDiscard, "Close the studio: your pieces go back to how they were"));
            row.AddChild(Ui.Primary("Confirm", _s.AskConfirm, "Keep your changes and send them to everyone at the table"));
        }
        else row.AddChild(Ui.Primary("Done", done, "Save everything and go back to the list of packs"));
        return Panel;
    }

    void RenamePack()
    {
        var n = _packName.Text.Trim();
        if (n.Length == 0 || n == _s.Pack.Name) return;
        _s.Pack.Name = n;
        _s.SavePack();
    }

    public void ShowPackName() { if (!_packName.HasFocus()) _packName.Text = _s.Pack.Name; }

    public void Flash(string text, double seconds)
    {
        _hint.Text = text;
        _hint.Modulate = Ui.AccentInk;
        _flashUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    public void Tick()
    {
        if (_flashUntil > 0 && Time.GetTicksMsec() / 1000.0 > _flashUntil)
        {
            _flashUntil = 0;
            _hint.Text = _hintText;
            _hint.Modulate = Ui.Muted;
        }
    }

    public void Show(string hint, PieceDef? sel)
    {
        _hintText = hint;
        if (_flashUntil == 0) { _hint.Text = _hintText; _hint.Modulate = Ui.Muted; }
        _pieceTitle.Text = sel?.Name ?? "";
        _crumb.Visible = sel != null;
    }

    public void ShowViews(List<(string key, string text, string tip)> views, string current, PieceDef? sel)
    {
        foreach (var c in _views.GetChildren()) { _views.RemoveChild(c); c.QueueFree(); }
        _viewBtns.Clear();
        if (sel == null) return;
        foreach (var (key, text, tip) in views)
        {
            var k = key;
            var b = new Button { Text = text, ToggleMode = true, ButtonPressed = current == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(62, 0) };
            b.Pressed += () => _s.SetView(k);
            _views.AddChild(b);
            _viewBtns[key] = b;
        }
        if (current == "model" && _s.Model is { } m)
        {
            var turn = new Button { Text = "Turn over", ToggleMode = true, ButtonPressed = m.Turned, FocusMode = Control.FocusModeEnum.None, TooltipText = "Its back up, as a turned-over piece lies on the table   (F)" };
            turn.Pressed += m.TurnOver;
            _views.AddChild(turn);
        }
    }

    public void PressView(string key)
    {
        foreach (var (k, b) in _viewBtns) b.SetPressedNoSignal(k == key);
    }
}

internal sealed class StudioToolStrip
{
    readonly PieceStudio _s;
    readonly Dictionary<string, Button> _toolBtns = new();
    VBoxContainer _box = null!;
    Button? _gridBtn, _snapBtn, _symBtn, _undoBtn, _redoBtn;

    public StudioToolStrip(PieceStudio s) => _s = s;

    public PanelContainer Panel { get; private set; } = null!;

    StudioCanvas Canvas => _s.Canvas;
    StudioPrefs Prefs => _s.Prefs;

    public PanelContainer Build()
    {
        Panel = new PanelContainer();
        var ss = Ui.Panel();
        ss.ContentMarginLeft = ss.ContentMarginRight = 3;
        ss.ContentMarginTop = ss.ContentMarginBottom = 4;
        Panel.AddThemeStyleboxOverride("panel", ss);
        _s.Root.AddChild(Panel);
        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        Panel.AddChild(_box);
        return Panel;
    }

    public void SyncTool(string tool)
    {
        foreach (var (k, b) in _toolBtns) b.SetPressedNoSignal(k == tool);
    }

    Button StripBtn(string icon, string tip, Action press, bool toggle = false, bool on = false)
    {
        var b = new Button { CustomMinimumSize = new Vector2(30, 30), FocusMode = Control.FocusModeEnum.None, TooltipText = tip, ToggleMode = toggle, ButtonPressed = on };
        Ui.Bare(b);
        b.AddChild(new StudioIcon { Kind = icon });
        b.Pressed += press;
        _box.AddChild(b);
        return b;
    }

    public void Rebuild()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        _toolBtns.Clear();
        if (_s.Sel == null) { Callable.From(_s.Relayout).CallDeferred(); return; }
        switch (_s.View)
        {
            case "paint":
                PaintTools();
                break;
            case "model":
                _s.Model?.Panels.BuildStrip(_box, _toolBtns);
                break;
            default:
                TableTools();
                break;
        }
        Callable.From(_s.Relayout).CallDeferred();
    }

    void PaintTools()
    {
        foreach (var group in new[]
        {
            new[] { ("move", "move", "Move   (V)"), ("brush", "brush", "Brush   (B)"), ("erase", "erase", "Rubber   (E)"), ("shape", "shapes", "Shapes   (U)"), ("text", "text-tool", "Text   (T)"), ("fill", "fill", "Fill   (G)"), ("pick", "pick", "Colour picker   (I)") },
            new[] { ("crop", "crop", "Crop   (C)"), ("outline", "outline", "The piece's outline   (O)"), ("place", "place", "Where the name, text lines and counters print   (N)"), ("measure", "ruler", "Measure   (M)"), ("pan", "hand", "Hand   (H, or hold Space)") },
        })
        {
            foreach (var (key, icon, tip) in group)
            {
                var k = key;
                _toolBtns[key] = Ui.Marked(StripBtn(icon, tip, () => { Canvas.SetTool(k); _s.ToolPanel.Place(); }, true, Canvas.Tool == key));
            }
            _box.AddChild(new HSeparator());
        }
        _gridBtn = StripBtn("grid", "Grid   (Ctrl+')", () => { Prefs.Grid = !Prefs.Grid; SyncToggles(); }, true, Prefs.Grid);
        _snapBtn = StripBtn("snap", "Snap to the grid   (Ctrl+;)", () => { Prefs.Snap = !Prefs.Snap; SyncToggles(); }, true, Prefs.Snap);
        _symBtn = StripBtn("sym", "Symmetry   (Y)", () => ShowSymmetryMenu(_symBtn!), true, Prefs.Symmetry != "off");
        _box.AddChild(new HSeparator());
        _undoBtn = StripBtn("undo", "Undo   (Ctrl+Z)", _s.Undo);
        _redoBtn = StripBtn("redo", "Redo   (Ctrl+Y)", _s.Redo);
        RefreshUndo();
    }

    Table Shown => _s.View == "scenario" && _s.InPlace is { } place ? place.Table : _s.T;

    void TableTools()
    {
        var T = Shown;
        var sel = StripBtn("select", "Select   (Esc)", () => T.SetTool(Tool.Select), true, true);
        _toolBtns["select"] = Ui.Marked(sel);
        _toolBtns["ruler"] = Ui.Marked(StripBtn("ruler", "Ruler   (M)", () => T.SetTool(T.Tool == Tool.Ruler ? Tool.Select : Tool.Ruler), true));
        _box.AddChild(new HSeparator());
        var b3 = StripBtn("3d", "3D view   (F2)", () => { T.Rig.ToggleTopDown(); }, true, !T.Rig.TopDown);
        b3.Pressed += () => b3.SetPressedNoSignal(!T.Rig.TopDown);
        if (_s.View != "scenario")
        {
            StripBtn("frame", "Frame all pieces   (Home)", _s.Paper.FrameAll);
            StripBtn("pieces", "Compare sizes\nSet down a piece from another pack beside these, not part of this pack", _s.Paper.AskReference);
        }
        T.ToolChanged -= SyncTableTools;
        T.ToolChanged += SyncTableTools;
    }

    void SyncTableTools()
    {
        if (_s.View is not ("table" or "scenario")) return;
        var t = Shown;
        if (_toolBtns.TryGetValue("select", out var s) && GodotObject.IsInstanceValid(s)) s.SetPressedNoSignal(t.Tool == Tool.Select);
        if (_toolBtns.TryGetValue("ruler", out var r) && GodotObject.IsInstanceValid(r)) r.SetPressedNoSignal(t.Tool == Tool.Ruler);
    }

    public void SyncToggles()
    {
        if (_gridBtn != null && GodotObject.IsInstanceValid(_gridBtn)) _gridBtn.SetPressedNoSignal(Prefs.Grid);
        if (_snapBtn != null && GodotObject.IsInstanceValid(_snapBtn)) _snapBtn.SetPressedNoSignal(Prefs.Snap);
        if (_symBtn != null && GodotObject.IsInstanceValid(_symBtn)) _symBtn.SetPressedNoSignal(Prefs.Symmetry != "off");
        Canvas.Redraw();
        Prefs.Save();
        _s.RebuildToolPanel();
    }

    public void CycleSymmetry()
    {
        var order = new[] { "off", "x", "y", "xy", "radial" };
        Prefs.Symmetry = order[(Array.IndexOf(order, Prefs.Symmetry) + 1) % order.Length];
        _s.Flash("Symmetry: " + SymName(Prefs.Symmetry));
        SyncToggles();
    }

    void ShowSymmetryMenu(Control at)
    {
        var pm = new PopupMenu();
        var modes = new[] { "off", "x", "y", "xy", "radial" };
        foreach (var m in modes) { pm.AddRadioCheckItem(m == "radial" ? $"Radial ({Prefs.Radial}×)" : char.ToUpperInvariant(SymName(m)[0]) + SymName(m)[1..]); pm.SetItemChecked(pm.ItemCount - 1, Prefs.Symmetry == m); }
        pm.AddSeparator();
        foreach (var n in new[] { 3, 4, 5, 6, 8, 12 }) { pm.AddRadioCheckItem($"Radial, {n} times", 100 + n); pm.SetItemChecked(pm.ItemCount - 1, Prefs.Symmetry == "radial" && Prefs.Radial == n); }
        pm.IdPressed += id =>
        {
            if (id >= 100) { Prefs.Radial = (int)id - 100; Prefs.Symmetry = "radial"; }
            else Prefs.Symmetry = modes[id];
            _s.Flash("Symmetry: " + SymName(Prefs.Symmetry));
            SyncToggles();
        };
        pm.PopupHide += () => { pm.QueueFree(); SyncToggles(); };
        _s.Root.AddChild(pm);
        var r = at.GetGlobalRect();
        pm.Popup(new Rect2I((Vector2I)(r.Position + new Vector2(r.Size.X + 4, 0)).Round(), Vector2I.Zero));
    }

    static string SymName(string s) => s switch { "x" => "mirror left–right", "y" => "mirror top–bottom", "xy" => "four ways", "radial" => "radial", _ => "off" };

    public void RefreshUndo()
    {
        if (_undoBtn != null && GodotObject.IsInstanceValid(_undoBtn)) _undoBtn.Disabled = !_s.CanUndo;
        if (_redoBtn != null && GodotObject.IsInstanceValid(_redoBtn)) _redoBtn.Disabled = !_s.CanRedo;
    }
}
