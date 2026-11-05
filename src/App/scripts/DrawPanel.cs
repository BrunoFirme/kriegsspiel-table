using System.Linq;
using Godot;

namespace KTable.App;

sealed class DrawPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    readonly IHudShell Shell;
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
        AddInk();
        AddPen();
        AddLayerRow();
        Panel?.Fit();
    }

    void AddShapes()
    {
        var kinds = new (string Key, string Icon, string Tip)[]
        {
            ("free", "draw", "Freehand"), ("line", "shape-line", "Line"), ("rect", "shape-rect", "Rectangle"),
            ("ellipse", "shape-ellipse", "Ellipse"), ("arrow", "shape-arrow", "Arrow"), ("erase", "erase", "Erase a drawing"),
        };
        bool locked = T.DrawingLocked;
        string current = T.Tool == Tool.Draw ? "free" : T.Tool == Tool.Erase ? "erase" : T.ShapeKind;
        var tools = new GridContainer { Columns = 3 };
        tools.AddThemeConstantOverride("h_separation", 3);
        tools.AddThemeConstantOverride("v_separation", 3);
        foreach (var (key, icon, tip) in kinds)
        {
            var b =
                new Button { ToggleMode = true, ButtonPressed = key == current && !locked, Disabled = locked, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    CustomMinimumSize = new Vector2(30, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddChild(new ToolIcon { Kind = icon });
            string k = key;
            b.Pressed += () =>
            {
                if (k == "free") T.SetTool(Tool.Draw);
                else if (k == "erase") T.SetTool(Tool.Erase);
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

    void AddInk()
    {
        var sw = new GridContainer { Columns = 3 };
        sw.AddThemeConstantOverride("h_separation", 3);
        sw.AddThemeConstantOverride("v_separation", 3);
        var ink = new ColorPickerButton { Color = T.PenColor, EditAlpha = false, CustomMinimumSize = new Vector2(0, 24), TooltipText = "Any other colour" };
        foreach (var (hex, what) in new[] { ("#1a1a1a", "pencil"), ("#c0392b", "red"), ("#2a6ebb", "blue"), ("#2e8b45", "green"), ("#d9a21b", "yellow"), ("#f2f2f2", "chalk") })
        {
            var col = Coords.ParseColor(hex, Colors.Black);
            var b = Ui.Swatch(col, new Vector2(30, 22), what);
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
        penRow.AddThemeConstantOverride("separation", 4);
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
        seeRow.AddThemeConstantOverride("separation", 4);
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

    const float LeastOpacity = 0.1f;

    void AddLayerRow()
    {
        var board = S.View.Boards.FirstOrDefault(b => b.Id == T.DrawBoard());
        var layRow = new HBoxContainer();
        layRow.AddThemeConstantOverride("separation", 2);
        var lay = new Button { Text = board?.Name ?? "no layer", ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(74, 24), TooltipText = "The layer being drawn on · click for the layers" };
        lay.Pressed += () => { if (!Shell.Panels["boards"].Visible) Shell.TogglePanel("boards"); };
        layRow.AddChild(lay);
        var wipe = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 24), TooltipText = $"Rub out everything on {board?.Name ?? "this layer"}" };
        wipe.AddChild(new ToolIcon { Kind = "erase", Color = Ui.Muted });
        wipe.Pressed += () =>
        {
            int n = T.DrawingCount();
            if (n == 0) { Log("Nothing drawn on this layer."); return; }
            var dlg = new ConfirmationDialog { Title = "Clear layer", DialogText = $"Rub out all {n} drawings on \"{board?.Name}\"? There is no undo.", OkButtonText = "Rub out" };
            HudNode.AddChild(dlg);
            dlg.Confirmed += T.EraseAllDrawings;
            dlg.PopupCentered();
            Ui.FreeOnClose(dlg);
        };
        layRow.AddChild(wipe);
        _box.AddChild(layRow);
    }
}
