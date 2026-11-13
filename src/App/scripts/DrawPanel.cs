using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class DrawPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    readonly IHudShell Shell;
    public ToolStrip? Strip { get; set; }
    public DrawPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Shell = shell;
        Panel = shell.MakePanel("paint", "Draw");
        _box = Panel.Body;
    }

    public static void RestorePen(Table T, Settings Cfg)
    {
        T.PenColor = Coords.ParseColor(Cfg.PenInk, new Color(0.1f, 0.1f, 0.1f));
        T.PenPixels = Mathf.Clamp(Cfg.PenSize, 1f, 12f);
        T.PenOpacity = Mathf.Clamp(Cfg.PenOpacity, LeastOpacity, 1f);
        T.ShapeKind = Cfg.ShapeKind;
    }

    public void Rebuild()
    {
        if (_box == null || T == null) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        AddShapes();
        AddCircle();
        AddInk();
        AddPen();
        AddLayers();
        Panel?.Fit();
    }

    void AddShapes()
    {
        var kinds = new (string Key, string Icon, string Tip)[]
        {
            ("free", "draw", "Freehand"), ("line", "shape-line", "Line"), ("arrow", "shape-arrow", "Arrow"),
            (Polyline.Curve, "shape-curve", "Curve: click each bend"), (Polyline.CurvedArrow, "shape-curve-arrow", "Curved arrow: click each bend"),
            (Polyline.Area, "shape-area", "Area: click round it, washed in the ink"),
            ("rect", "shape-rect", "Rectangle"), ("ellipse", "shape-ellipse", "Ellipse"), ("erase", "erase", "Erase a drawing"),
            ("name", "", "Name: click, or drag a path, and type · Enter lays it, Esc drops it · double-click a name to retype it, drag to move it"),
        };
        bool locked = T.DrawingLocked;
        string current = T.Tool == Tool.Draw ? "free" : T.Tool == Tool.Erase ? "erase" : T.Tool == Tool.Letter ? "name" : T.ShapeKind;
        var tools = new GridContainer { Columns = 4 };
        tools.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        tools.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        foreach (var (key, icon, tip) in kinds)
        {
            var b =
                new Button { ToggleMode = true, ButtonPressed = key == current && !locked, Disabled = locked, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    CustomMinimumSize = new Vector2(30, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            if (icon.Length > 0) b.AddChild(new ToolIcon { Kind = icon });
            else b.Text = "Aa";
            string k = key;
            b.Pressed += () =>
            {
                if (k == "free") T.SetTool(Tool.Draw);
                else if (k == "erase") T.SetTool(Tool.Erase);
                else if (k == "name") T.SetTool(Tool.Letter);
                else
                {
                    T.ShapeKind = k;
                    Cfg.ShapeKind = k;
                    Cfg.Save();
                    T.SetTool(Tool.Shape);
                }
                Rebuild();
            };
            tools.AddChild(b);
        }
        _box.AddChild(tools);
        if (locked)
        {
            var why = Hint("Layer is hidden · show it to unlock");
            why.CustomMinimumSize = new Vector2(96, 0);
            _box.AddChild(why);
        }
    }

    void AddCircle()
    {
        if (T.Tool != Tool.Shape || T.ShapeKind != "ellipse" || T.DrawingLocked) return;
        var form = Ui.Form(Ui.PanelLabels);
        form.TooltipText = "The circle a click lays";
        form.AddChild(new Label { Text = "Radius" });
        form.AddChild(RangePanel.ReachBox(S.View.Calibration, T.CircleReach, v => T.CircleReach = v));
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddInk()
    {
        var sw = new GridContainer { Columns = ColorChoices.All.Length };
        sw.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        sw.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        var ink = new ColorPickerButton { Color = T.PenColor, EditAlpha = false, CustomMinimumSize = new Vector2(0, 24), TooltipText = "Any other colour" };
        foreach (var (what, hex) in ColorChoices.All)
        {
            var col = Coords.ParseColor(hex, Colors.Black);
            var b = Ui.Swatch(col, new Vector2(InkSwatch, 22), what);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.Pressed += () =>
            {
                T.PenColor = col;
                Cfg.PenInk = hex;
                Cfg.Save();
                ink.Color = col;
            };
            sw.AddChild(b);
        }
        _box.AddChild(sw);
        ink.ColorChanged += c =>
        {
            T.PenColor = c;
            Cfg.PenInk = "#" + c.ToHtml(false);
            Cfg.Save();
        };
        _box.AddChild(ink);
    }

    void AddPen()
    {
        var penRow = new HBoxContainer();
        penRow.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var pen =
            new HSlider { MinValue = 1, MaxValue = 40, Step = 0.5, Value = T.PenPixels, CustomMinimumSize = new Vector2(62, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = "How thick the line is, in screen pixels" };
        var penL = Ui.ValueBox(pen, v => $"{v:0.#}", 26);
        pen.ValueChanged += v =>
        {
            T.PenPixels = (float)v;
            Cfg.PenSize = (float)v;
            Cfg.Save();
        };
        penRow.AddChild(pen);
        penRow.AddChild(penL);
        _box.AddChild(penRow);
        var seeRow = new HBoxContainer();
        seeRow.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var see =
            new HSlider { MinValue = LeastOpacity * 100, MaxValue = 100, Step = 5, Value = Mathf.Round(T.PenOpacity * 100), CustomMinimumSize = new Vector2(62, 0),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "How see-through new lines are: 100% hides what is under them" };
        var seeL = Ui.ValueBox(see, v => $"{v:0}%", 26);
        see.ValueChanged += v =>
        {
            T.PenOpacity = (float)(v / 100);
            Cfg.PenOpacity = T.PenOpacity;
            Cfg.Save();
        };
        seeRow.AddChild(see);
        seeRow.AddChild(seeL);
        _box.AddChild(seeRow);
    }

    const float LeastOpacity = 0.1f, InkSwatch = 15;
}
