using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class ReliefPanel : HudPart
{
    const int Wide = 272, ToolsToARow = 3, GroundsToARow = 3, ValueWide = 46;
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;
    readonly BuildPanel _build;
    string _shape = "raise", _ground = "paint:grass";

    public ReliefPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel("relief", "Terrain");
        _box = Panel.Body;
        _box.CustomMinimumSize = new Vector2(Wide, 0);
        _build = new BuildPanel(ctx, _box, Rebuild);
    }

    static readonly (string Key, string Text, string Tip)[] Shapes =
    {
        ("raise", "Raise", "Hold to build up the ground"),
        ("lower", "Lower", "Hold to dig the ground down, never below the table"),
        ("smooth", "Smooth", "Drag to soften slopes and bumps"),
        ("flatten", "Flatten", "Drag to bring the ground to the height where the stroke began"),
        (ReliefTool.LevelMode, "Set height", "Drag to lay the ground at an exact height · Alt+click takes the height there"),
    };

    static readonly (string Key, string Text, string Icon, string Tip)[] Paintings =
    {
        ("brush", "Brush", "", "Drag to paint"),
        ("fill", "Fill", "", "Click to fill the patch of ground clicked, up to other ground types, or a bare sheet whole; on its own kind, lays it afresh"),
        ("rect", "", "shape-rect", "Rectangle: drag across it · Shift for a square"),
        ("ellipse", "", "shape-ellipse", "Ellipse: drag across it · Shift for a circle"),
        ("polygon", "", "shape-polygon", "Polygon: click its corners, then the first again or double-click · Esc drops it"),
        ("route", "Route", "", "Route: click the waypoints, then the last again or double-click · the ground finds its way between them over the relief, as a road does; water runs down from the first as a river · Esc drops it"),
    };

    bool Painting => Cfg.ReliefMode.StartsWith("paint:", StringComparison.Ordinal);

    public void Rebuild()
    {
        if (_box == null || T == null) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        if (ReliefTool.Scaled(S)) AddTools();
        else AddScaleAsk();
        Panel?.Fit();
    }

    void AddScaleAsk()
    {
        _box.AddChild(Hint(GroundKinds.NeedsScale));
        _box.AddChild(Btn("Scale…", () => T.SetTool(Tool.Calibrate), "Drag along the map's scale bar"));
    }

    void AddTools()
    {
        bool building = _build.Building;
        if (!building && Painting) _ground = Cfg.ReliefMode;
        if (!building && !Painting) _shape = Cfg.ReliefMode;
        _box.AddChild(Ui.Segments(new (string, bool, string, Action)[]
        {
            ("Shape", !building && !Painting, "Raise, lower, smooth and flatten the ground", () => Take(_shape)),
            ("Paint", !building && Painting, "Paint grass, fields, woods, marsh, water and bare ground", () => Take(_ground)),
            ("Build", building, "Lay settlements, forts, buildings, roads, streams and country details", () => { T.SetTool(Tool.Build); Rebuild(); }),
        }));
        if (building) _build.Add();
        else if (Painting) AddPaint();
        else AddShape();
    }

    void Take(string mode)
    {
        Cfg.ReliefMode = mode;
        Cfg.Save();
        T.SetTool(Tool.Relief);
        Rebuild();
    }

    void AddShape()
    {
        _box.AddChild(Ui.Chips(Shapes, Cfg.ReliefMode, ToolsToARow, Take, tools: true));
        _box.AddChild(new HSeparator());
        var form = Ui.Form(Ui.PanelLabels);
        Row(form, Cfg, "Size", 0.2, 400, 0.1, Cfg.ReliefBrush, x => $"{x:0.#} u", x => Cfg.ReliefBrush = (float)x, "The brush's radius in map units · = and − or Ctrl+wheel size it on the table", exp: true);
        if (Cfg.ReliefMode is "raise" or "lower") AddRate(form);
        else if (Cfg.ReliefMode == ReliefTool.LevelMode)
            Row(form, Cfg, "Height", 0, Settings.HighestReliefLimit, 1, Cfg.ReliefLevel, x => $"{x:0.#} m", x => Cfg.ReliefLevel = (float)x, "The height laid, in metres · Alt+click on the map takes one");
        else Row(form, Cfg, "Strength", 0.05, 1, 0.05, Cfg.ReliefStrength, x => $"{x * 100:0}%", x => Cfg.ReliefStrength = (float)x, "How much one pass smooths or flattens");
        Row(form, Cfg, "Hardness", 0, Settings.HardestRelief, 0.05, Cfg.ReliefHardness, x => $"{x * 100:0}%", x => Cfg.ReliefHardness = (float)x, "A soft bell, or flat across with a short edge");
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddRate(GridContainer form)
    {
        Row(form, Cfg, "Rate", Settings.SlowestReliefRate, Settings.FastestReliefRate, 0.5, Cfg.ReliefRate, x => $"{x:0.#} m/s", x => Cfg.ReliefRate = (float)x,
            "Metres a second under a held brush, the same for any brush size", exp: true);
        if (Cfg.ReliefMode == "raise")
            Row(form, Cfg, "Up to", 0, Settings.HighestReliefLimit, 1, Cfg.ReliefUpTo, x => x <= 0 ? "off" : $"{x:0} m", x => Cfg.ReliefUpTo = (float)x, "Raising stops at this height; off has no ceiling");
        else
            Row(form, Cfg, "Down to", 0, Settings.HighestReliefLimit, 1, Cfg.ReliefDownTo, x => $"{x:0} m", x => Cfg.ReliefDownTo = (float)x, "Lowering stops at this height; 0 is the table");
    }

    void AddPaint()
    {
        var kinds = GroundKinds.Brushes.ToList();
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        for (int i = 0; i < kinds.Count; i += GroundsToARow)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
            foreach (var kind in kinds.Skip(i).Take(GroundsToARow))
            {
                var mode = "paint:" + GroundKinds.Name(kind);
                var b = GroundChip.Of(kind, Color.FromHtml(GroundKinds.Color(kind, S.View.Lighting.MapStyle)), Title(GroundKinds.Name(kind)), Cfg.ReliefMode == mode);
                b.Pressed += () => Take(mode);
                row.AddChild(b);
            }
            rows.AddChild(row);
        }
        _box.AddChild(rows);
        _box.AddChild(Ui.Chips(new[] { ("paint:none", "Rub out", "Rub out painted ground") }, Cfg.ReliefMode, 1, Take, tools: true));
        _box.AddChild(new HSeparator());
        AddHowToPaint();
        var form = Ui.Form(Ui.PanelLabels);
        if (Cfg.ReliefPaint is "brush" or "route")
            Row(form, Cfg, "Size", 0.2, 400, 0.1, Cfg.ReliefBrush, x => $"{x:0.#} u", x => Cfg.ReliefBrush = (float)x, "The brush's radius in map units · = and − or Ctrl+wheel size it on the table", exp: true);
        Row(form, Cfg, "Density", 0.05, 1, 0.05, Cfg.ReliefDensity, x => $"{x * 100:0}%", x => Cfg.ReliefDensity = (float)x,
            "How much of the ground takes it, in clumps: low for sparse wood or scattered rock; with Rub out it thins what is painted. Going over again adds no more");
        if (GroundKinds.Of(Cfg.ReliefMode[6..]) is { } picked)
        {
            var parameters = GroundKinds.Parameters(picked);
            for (int i = 0; i < parameters.Count; i++)
            {
                int which = i;
                Row(form, Cfg, parameters[i].Name, 0, 100, 5, Cfg.ReliefDetail(picked, which), x => $"{x:0}%", x => Cfg.SetReliefDetail(picked, which, (int)x), parameters[i].Tip);
            }
        }
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddHowToPaint()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        foreach (var (key, text, icon, tip) in Paintings)
        {
            var b = new Button
            {
                Text = text, ToggleMode = true, ButtonPressed = Cfg.ReliefPaint == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                SizeFlagsHorizontal = text.Length > 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill, CustomMinimumSize = new Vector2(text.Length > 0 ? 0 : Ui.ChipHigh, Ui.ChipHigh),
            };
            if (icon.Length > 0) b.AddChild(new ToolIcon { Kind = icon });
            b.Pressed += () =>
            {
                Cfg.ReliefPaint = key;
                Take(Cfg.ReliefMode);
            };
            row.AddChild(b);
        }
        _box.AddChild(row);
    }

    static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    public static HSlider Row(GridContainer form, Settings cfg, string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip,
        bool exp = false)
    {
        var s = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value, ExpEdit = exp, CustomMinimumSize = new Vector2(80, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = tip,
        };
        var line = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        line.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        line.AddChild(s);
        line.AddChild(Ui.ValueBox(s, show, ValueWide));
        s.ValueChanged += x => set(x);
        s.DragEnded += _ => cfg.Save();
        form.AddChild(new Label { Text = label });
        form.AddChild(line);
        return s;
    }
}
