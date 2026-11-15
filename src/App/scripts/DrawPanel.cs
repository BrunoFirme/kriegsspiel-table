using System;
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
        T.PenOpacity = Mathf.Clamp(Cfg.PenOpacity, LeastOpacity, 1f);
        T.ShapeKind = Cfg.ShapeKind;
    }

    public void Rebuild()
    {
        if (_box == null || T == null) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        AddShapes();
        if (T.Tool == Tool.Letter) AddLetters();
        else
        {
            AddCircle();
            AddInk();
            AddPen();
        }
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
            ("name", "", "Name: click, or drag a path, and type on the map · click a name to edit it"),
        };
        bool locked = T.DrawingLocked;
        string current = T.Tool == Tool.Draw ? "free" : T.Tool == Tool.Erase ? "erase" : T.Tool == Tool.Letter ? "name" : T.ShapeKind;
        var tools = new GridContainer { Columns = 4 };
        tools.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        tools.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (key, icon, tip) in kinds)
        {
            var b =
                new Button { ToggleMode = true, ButtonPressed = key == current && !locked, Disabled = locked, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    CustomMinimumSize = Ui.Px(30, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
            why.CustomMinimumSize = Ui.Px(96, 0);
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
        sw.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        sw.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        var ink = new ColorPickerButton { Color = T.PenColor, EditAlpha = false, CustomMinimumSize = Ui.Px(0, 24), TooltipText = "Any other colour" };
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
        penRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        const string Tip = "How thick the line lies on the map, the same at any zoom · Ctrl+wheel sets it too";
        var pen =
            new HSlider { MinValue = Math.Log10(PenTool.LeastPen), MaxValue = Math.Log10(PenTool.MostPen), Step = PenSliderStep, Value = Math.Log10(T.PenThickness),
                CustomMinimumSize = Ui.Px(62, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = Tip };
        var typed =
            new SpinBox { MinValue = PenTool.LeastPen, MaxValue = PenBoxMost, Step = PenTool.LeastPen, Value = T.Pen.PenShown, Suffix = S.View.Calibration.UnitName,
                CustomMinimumSize = Ui.Px(PenBoxWidth, 0), TooltipText = Tip };
        pen.ValueChanged += v => { if (!_penSyncing) T.PenThickness = PenTool.Tidy(Math.Pow(10, v)); };
        typed.ValueChanged += v => { if (!_penSyncing) T.Pen.PenShown = v; };
        typed.GetLineEdit().TextSubmitted += _ => typed.GetLineEdit().ReleaseFocus();
        _pen = pen;
        _penTyped = typed;
        penRow.AddChild(pen);
        penRow.AddChild(typed);
        _box.AddChild(penRow);
        var seeRow = new HBoxContainer();
        seeRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var see =
            new HSlider { MinValue = LeastOpacity * 100, MaxValue = 100, Step = 5, Value = Mathf.Round(T.PenOpacity * 100), CustomMinimumSize = Ui.Px(62, 0),
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

    void AddLetters()
    {
        var letters = T.Letters;
        letters.Beside = Panel;
        _styleSeen = letters.StyleTaken;
        AddNameInk(letters);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        const string Tip = "How tall the letters stand on the map, the same at any zoom · = and − or Ctrl+wheel set it, over a name that name";
        var slide =
            new HSlider { MinValue = Math.Log10(Stroke.LeastLetter), MaxValue = Math.Log10(Stroke.MostLetter), Step = PenSliderStep, Value = Math.Log10(letters.LetterKept),
                CustomMinimumSize = Ui.Px(62, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = Tip,
                FocusMode = Control.FocusModeEnum.None };
        var typed =
            new SpinBox { MinValue = Stroke.LeastLetter, MaxValue = PenBoxMost, Step = Stroke.LeastLetter, Value = letters.LetterShown, Suffix = S.View.Calibration.UnitName,
                CustomMinimumSize = Ui.Px(PenBoxWidth, 0), TooltipText = Tip };
        slide.ValueChanged += v =>
        {
            if (_penSyncing) return;
            letters.LetterKept = PenTool.Tidy(Math.Pow(10, v));
            letters.Restyle(sized: true);
        };
        typed.ValueChanged += v =>
        {
            if (_penSyncing) return;
            letters.LetterShown = v;
            letters.Restyle(sized: true);
        };
        typed.GetLineEdit().TextSubmitted += _ => typed.GetLineEdit().ReleaseFocus();
        _letterSlide = slide;
        _letterTyped = typed;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Font" });
        form.AddChild(FontGrid(letters));
        form.AddChild(new Label { Text = "Style" });
        form.AddChild(StyleRow(letters));
        form.AddChild(new Label { Text = "Size" });
        row.AddChild(slide);
        row.AddChild(typed);
        form.AddChild(row);
        form.AddChild(new Label { Text = "Spacing" });
        var spacing =
            new HSlider { MinValue = Stroke.LeastSpacing * 100, MaxValue = Stroke.MostSpacing * 100, Step = SpacingStep, Value = Math.Round(Cfg.NameSpacing * 100),
                CustomMinimumSize = Ui.Px(62, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = "Room added between the letters, as a share of their height", FocusMode = Control.FocusModeEnum.None };
        var spacingRow = new HBoxContainer();
        spacingRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        spacingRow.AddChild(spacing);
        spacingRow.AddChild(Ui.ValueBox(spacing, v => $"{v:0}%", 40));
        spacing.ValueChanged += v =>
        {
            Cfg.NameSpacing = v / 100;
            Cfg.Save();
            letters.Restyle();
        };
        form.AddChild(spacingRow);
        form.AddChild(new Label { Text = "Align" });
        form.AddChild(AlignRow(letters));
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    static readonly (string Key, string Name)[] Faces =
    {
        (Stroke.FontSerif, "Serif"), (Stroke.FontDidone, "Didone"), (Stroke.FontSans, "Sans"),
        (Stroke.FontFraktur, "Fraktur"), (Stroke.FontScript, "Script"), (Stroke.FontCapitals, "Capitals"),
    };

    GridContainer FontGrid(LetterTool letters)
    {
        var grid = new GridContainer { Columns = FaceColumns };
        grid.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        grid.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (key, name) in Faces)
        {
            var b =
                new Button { Text = FaceSample, ToggleMode = true, ButtonPressed = key == Cfg.NameFont, FocusMode = Control.FocusModeEnum.None, TooltipText = name,
                    CustomMinimumSize = Ui.Px(30, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddThemeFontOverride("font", MapLettering.Face(key));
            b.AddThemeFontSizeOverride("font_size", Ui.Px(FaceSampleSize));
            string k = key;
            b.Pressed += () =>
            {
                Cfg.NameFont = k;
                Cfg.Save();
                letters.Restyle();
                Rebuild();
            };
            grid.AddChild(b);
        }
        return grid;
    }

    HBoxContainer StyleRow(LetterTool letters)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        void Toggle(string text, string tip, bool on, Action<bool> set, Font face)
        {
            var b =
                new Button { Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    CustomMinimumSize = Ui.Px(30, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddThemeFontOverride("font", face);
            b.AddThemeFontSizeOverride("font_size", Ui.Px(FaceSampleSize));
            b.Toggled += pressed =>
            {
                set(pressed);
                Cfg.Save();
                letters.Restyle();
            };
            row.AddChild(b);
        }
        Toggle("B", "Bold", Cfg.NameBold, on => Cfg.NameBold = on, MapLettering.Styled(bold: true, italic: false));
        Toggle("I", "Italic", Cfg.NameItalic, on => Cfg.NameItalic = on, MapLettering.Styled(bold: false, italic: true));
        return row;
    }

    HBoxContainer AlignRow(LetterTool letters)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var aligns = new (string Key, string Icon, string Tip)[]
        {
            (Stroke.AlignLeft, "align-left", "Left: the name starts at the click, or at the start of its path"),
            (Stroke.AlignCentre, "align-centre", "Centre: the name is centred on the click, or on its path"),
            (Stroke.AlignRight, "align-right", "Right: the name ends at the click, or at the end of its path"),
            (Stroke.AlignSpread, "align-spread", "Spread: the letters run the length of the path"),
        };
        foreach (var (key, icon, tip) in aligns)
        {
            var b =
                new Button { ToggleMode = true, ButtonPressed = key == Cfg.NameAlign, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    CustomMinimumSize = Ui.Px(30, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddChild(new ToolIcon { Kind = icon });
            string k = key;
            b.Pressed += () =>
            {
                Cfg.NameAlign = k;
                Cfg.Save();
                letters.Restyle();
                Rebuild();
            };
            row.AddChild(b);
        }
        return row;
    }

    void AddNameInk(LetterTool letters)
    {
        var own = MapLettering.OwnInk(MapStyles.Index(S.View.Lighting.MapStyle));
        var sw = new GridContainer { Columns = ColorChoices.All.Length + 1 };
        sw.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        sw.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        void Take(string hex)
        {
            Cfg.NameInk = hex;
            Cfg.Save();
            letters.Restyle();
            Rebuild();
        }
        var mapInk = Ui.Swatch(own, new Vector2(InkSwatch, 22), "The map's own ink", Cfg.NameInk.Length == 0);
        mapInk.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mapInk.Pressed += () => Take("");
        sw.AddChild(mapInk);
        foreach (var (what, hex) in ColorChoices.All)
        {
            var b = Ui.Swatch(Coords.ParseColor(hex, Colors.Black), new Vector2(InkSwatch, 22), what, string.Equals(hex, Cfg.NameInk, StringComparison.OrdinalIgnoreCase));
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.Pressed += () => Take(hex);
            sw.AddChild(b);
        }
        _box.AddChild(sw);
        var ink = new ColorPickerButton { Color = Coords.ParseColor(Cfg.NameInk, own), EditAlpha = false, CustomMinimumSize = Ui.Px(0, 24), TooltipText = "Any other colour" };
        ink.ColorChanged += c =>
        {
            Cfg.NameInk = "#" + c.ToHtml(false);
            Cfg.Save();
            letters.Restyle(keys: false);
        };
        ink.PopupClosed += letters.TakeKeys;
        _box.AddChild(ink);
    }

    HSlider? _pen, _letterSlide;
    SpinBox? _penTyped, _letterTyped;
    bool _penSyncing;
    int _styleSeen;

    public void Sync()
    {
        if (T != null && T.Tool == Tool.Letter && Panel is { Visible: true } && T.Letters.StyleTaken != _styleSeen) Rebuild();
        SyncLetters();
        if (T == null || Panel is not { Visible: true } || _pen == null || _penTyped == null || !GodotObject.IsInstanceValid(_pen) || !GodotObject.IsInstanceValid(_penTyped)) return;
        double now = T.PenThickness, shown = T.Pen.PenShown;
        _penSyncing = true;
        if (Math.Abs(Math.Pow(10, _pen.Value) - now) > now * PenSliderSlack) _pen.Value = Math.Log10(now);
        if (!_penTyped.GetLineEdit().HasFocus() && Math.Abs(_penTyped.Value - shown) > PenTool.LeastPen / 2) _penTyped.Value = shown;
        _penSyncing = false;
    }

    void SyncLetters()
    {
        if (T == null || Panel is not { Visible: true } || _letterSlide is not { } slide || _letterTyped is not { } typed) return;
        if (!GodotObject.IsInstanceValid(slide) || !GodotObject.IsInstanceValid(typed)) return;
        double now = T.Letters.LetterKept, shown = T.Letters.LetterShown;
        _penSyncing = true;
        if (Math.Abs(Math.Pow(10, slide.Value) - now) > now * PenSliderSlack) slide.Value = Math.Log10(now);
        if (!typed.GetLineEdit().HasFocus() && Math.Abs(typed.Value - shown) > Stroke.LeastLetter / 2) typed.Value = shown;
        _penSyncing = false;
    }

    const float LeastOpacity = 0.1f, InkSwatch = 15, PenBoxWidth = 92;
    const int FaceColumns = 3, FaceSampleSize = 17;
    const string FaceSample = "Ag";
    const double PenSliderStep = 0.002, PenSliderSlack = 0.006, PenBoxMost = 1_000_000, SpacingStep = 1;
}
