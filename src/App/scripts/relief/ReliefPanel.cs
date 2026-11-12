using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class ReliefPanel : HudPart
{
    public const float LabelWide = 62;
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;
    readonly BuildPanel _build;

    public ReliefPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel("relief", "Terrain");
        _box = Panel.Body;
        _build = new BuildPanel(ctx, _box, Rebuild);
    }

    static readonly (string Key, string Text, string Tip)[] Shapes =
    {
        ("raise", "Raise", "Drag to build up the ground"),
        ("lower", "Lower", "Drag to dig the ground down, never below the table"),
        ("smooth", "Smooth", "Drag to soften slopes and bumps"),
        ("flatten", "Flatten", "Drag to bring the ground to the height where the stroke began"),
    };

    public void Rebuild()
    {
        if (_box == null || T == null) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        if (ReliefTool.Scaled(S)) AddTools();
        else AddScaleAsk();
        Panel?.Fit();
    }

    void AddTools()
    {
        bool building = _build.Building;
        if (!building && Painting) _ground = Cfg.ReliefMode;
        if (!building && !Painting) _shape = Cfg.ReliefMode;
        AddTabs();
        if (building) _build.Add();
        else if (Painting) AddPaint();
        else AddShape();
    }

    string _shape = "raise", _ground = "paint:grass";

    void AddTabs()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        Button Tab(string text, bool on, string tip, Action open)
        {
            var b = new Button
            {
                Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28),
            };
            b.Pressed += () => { open(); Rebuild(); };
            return b;
        }
        row.AddChild(Tab("Shape", !_build.Building && !Painting, "Raise, lower, smooth and flatten the ground", () => Take(_shape)));
        row.AddChild(Tab("Paint", !_build.Building && Painting, "Paint grass, fields, woods, marsh, water and bare ground", () => Take(_ground)));
        row.AddChild(Tab("Build", _build.Building, "Lay settlements, forts, buildings, roads, streams and country details", () => T.SetTool(Tool.Build)));
        _box.AddChild(row);
        _box.AddChild(new HSeparator());
    }

    void Take(string mode)
    {
        Cfg.ReliefMode = mode;
        Cfg.Save();
        T.SetTool(Tool.Relief);
    }

    void AddScaleAsk()
    {
        _box.AddChild(Hint(GroundKinds.NeedsScale));
        _box.AddChild(Btn("Scale…", () => T.SetTool(Tool.Calibrate), "Drag along the map's scale bar"));
    }

    Button Choice(string text, string mode, string tip)
    {
        var b =
            new Button { Text = text, ToggleMode = true, ButtonPressed = Lit(mode), FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 26) };
        b.Pressed += () => { Take(mode); Rebuild(); };
        return b;
    }

    bool Lit(string mode) => !_build.Building && Cfg.ReliefMode == mode;

    void AddShape()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        foreach (var (key, text, tip) in Shapes) grid.AddChild(Choice(text, key, tip));
        _box.AddChild(grid);
        AddSliders();
    }

    void AddPaint()
    {
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        foreach (var kind in GroundKinds.Brushes)
        {
            var mode = "paint:" + GroundKinds.Name(kind);
            var b = Ui.NamedSwatch(Color.FromHtml(GroundKinds.Color(kind, S.View.Lighting.MapStyle)), Title(GroundKinds.Name(kind)), Lit(mode));
            b.Pressed += () => { Take(mode); Rebuild(); };
            grid.AddChild(b);
        }
        grid.AddChild(Choice("Rub out", "paint:none", "Rub out painted ground"));
        _box.AddChild(grid);
        AddHowToPaint();
        AddSliders();
    }

    bool Painting => Cfg.ReliefMode.StartsWith("paint:", StringComparison.Ordinal);

    static readonly (string Key, string Text, string Icon, string Tip)[] Paintings =
    {
        ("brush", "Brush", "", "Drag to paint"),
        ("fill", "Fill", "", "Click to fill the patch of ground clicked, up to other ground types, or a bare sheet whole; on its own kind, lays it afresh"),
        ("rect", "", "shape-rect", "Rectangle: drag across it · Shift for a square"),
        ("ellipse", "", "shape-ellipse", "Ellipse: drag across it · Shift for a circle"),
        ("polygon", "", "shape-polygon", "Polygon: click its corners, then the first again or double-click · Esc drops it"),
        ("route", "Route", "", "Route: click the waypoints, then the last again or double-click · the ground finds its way between them over the relief, as a road does; water runs down from the first as a river · Esc drops it"),
    };

    void AddHowToPaint()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        foreach (var (key, text, icon, tip) in Paintings)
        {
            var b =
                new Button { Text = text, ToggleMode = true, ButtonPressed = Cfg.ReliefPaint == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    SizeFlagsHorizontal = text.Length > 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill, CustomMinimumSize = new Vector2(text.Length > 0 ? 0 : 28, 26) };
            if (icon.Length > 0) b.AddChild(new ToolIcon { Kind = icon });
            b.Pressed += () =>
            {
                Cfg.ReliefPaint = key;
                Take(Cfg.ReliefMode);
                Rebuild();
            };
            row.AddChild(b);
        }
        _box.AddChild(row);
    }

    static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    public static HSlider Row(VBoxContainer box, Settings cfg, string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip,
        bool exp = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(LabelWide, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var s = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value, ExpEdit = exp, CustomMinimumSize = new Vector2(80, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = tip,
        };
        var v = Ui.ValueBox(s, show, 42);
        s.ValueChanged += x => set(x);
        s.DragEnded += _ => cfg.Save();
        row.AddChild(s);
        row.AddChild(v);
        box.AddChild(row);
        return s;
    }

    void AddSliders()
    {
        HSlider Row(string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip, bool exp = false) =>
            ReliefPanel.Row(_box, Cfg, label, min, max, step, value, show, set, tip, exp);
        if (!Painting || Cfg.ReliefPaint is "brush" or "route")
            Row("Size", 0.2, 400, 0.1, Cfg.ReliefBrush, x => $"{x:0.#} u", x => Cfg.ReliefBrush = (float)x, "The brush's radius in map units · = and − or Ctrl+wheel size the brush on the table", exp: true);
        if (Painting)
            Row("Density", 0.05, 1, 0.05, Cfg.ReliefDensity, x => $"{x * 100:0}%", x => Cfg.ReliefDensity = (float)x, "How much of the ground takes it, in clumps: low for sparse wood or scattered rock; with ✕ it thins what is painted. Going over again adds no more");
        else
            Row("Strength", 0.05, 1, 0.05, Cfg.ReliefStrength, x => $"{x * 100:0}%", x => Cfg.ReliefStrength = (float)x, "How much one pass raises, lowers or smooths");
        if (!Painting || GroundKinds.Of(Cfg.ReliefMode[6..]) is not { } kind) return;
        var parameters = GroundKinds.Parameters(kind);
        for (int i = 0; i < parameters.Count; i++)
        {
            int which = i;
            Row(parameters[i].Name, 0, 100, 5, Cfg.ReliefDetail(kind, which), x => $"{x:0}%", x => Cfg.SetReliefDetail(kind, which, (int)x), parameters[i].Tip);
        }
    }
}
