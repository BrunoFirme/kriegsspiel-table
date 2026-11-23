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
        _box.CustomMinimumSize = Ui.Px(BodyWide, 0);
        _sightShown = T.DrawsSight ? T.SightLineKind : null;
        AddShapes();
        if (T.Tool == Tool.Letter) AddLetters();
        else
        {
            AddToolRows();
            if (_sightShown is { } kind) AddSightInk(kind);
            else if (T.Tool != Tool.Erase) AddInk();
        }
        AddLayers();
        Panel?.Fit();
    }

    void AddSightInk(string kind)
    {
        _box.AddChild(Header("Lines"));
        _box.AddChild(PovPanel.LineKindRow(kind, k =>
        {
            T.SetSightLineKind(k);
            T.RefreshHint();
            Rebuild();
        }));
    }

    internal void SightLockChanged()
    {
        FitPenToLayer();
        LayersChanged();
    }

    void FitPenToLayer()
    {
        if (T.DrawsSight && PenTool.Headless(T.ShapeKind) is var plain && plain != T.ShapeKind)
        {
            T.ShapeKind = plain;
            Cfg.ShapeKind = plain;
            Cfg.Save();
        }
        if (T.DrawsSight && T.Tool == Tool.Letter) T.SetTool(Tool.Draw);
        else T.RefreshHint();
    }

    void AddShapes()
    {
        var kinds = new (string Key, string Icon, string Tip)[]
        {
            ("free", "draw", "Freehand"), ("line", "shape-line", "Line"), ("arrow", "shape-arrow", "Arrow"),
            (Polyline.Curve, "shape-curve", "Curve: drag its course, or Shift+click each bend"), (Polyline.CurvedArrow, "shape-curve-arrow", "Curved arrow: drag its course, or Shift+click each bend"),
            ("name", "", "Name: click, or drag a path, and type on the map · Ctrl+drag a text box · click a name to edit it"),
            (Polyline.Area, "shape-area", "Area: drag round it, or Shift+click its corners, washed in the ink"),
            ("rect", "shape-rect", "Rectangle"), ("ellipse", "shape-ellipse", "Ellipse"), (PenTool.FillKind, "fill", "Fill"), ("erase", "erase", "Erase a drawing"),
        };
        bool locked = T.DrawingLocked, sight = _sightShown != null;
        string current = T.Tool == Tool.Draw ? "free" : T.Tool == Tool.Erase ? "erase" : T.Tool == Tool.Letter ? "name" : T.ShapeKind;
        var tools = new GridContainer { Columns = ToolColumns };
        tools.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        tools.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (key, icon, tip) in kinds)
        {
            bool off = locked || sight && (PenTool.Headless(key) != key || key is "name" or PenTool.FillKind);
            var b =
                new Button { ToggleMode = true, ButtonPressed = key == current && !off, Disabled = off, FocusMode = Control.FocusModeEnum.None,
                    TooltipText = off && !locked ? $"{tip} · not for sight lines" : tip, CustomMinimumSize = Ui.Px(30, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
            var why = Hint("Hidden layer");
            why.CustomMinimumSize = Ui.Px(96, 0);
            _box.AddChild(why);
        }
    }

    bool Filling => T.Tool == Tool.Shape && T.ShapeKind == PenTool.FillKind && !T.DrawingLocked && _sightShown == null;

    void AddToolRows()
    {
        var form = Ui.Form(Ui.PanelLabels);
        if (!Filling) PenRow(form);
        if (T.Tool == Tool.Shape && T.ShapeKind == "ellipse" && !T.DrawingLocked)
        {
            var cal = S.View.Calibration;
            ReliefPanel.Row(form, Cfg, "Radius", Math.Log10(RangeRings.LeastReach), Math.Log10(RangeRings.MostReach), LengthStep, Math.Log10(T.CircleReach),
                v => RangeRings.Words(Math.Pow(10, v), cal), v => T.CircleReach = Math.Pow(10, v), "The circle a click lays");
        }
        if (Filling) FillOptions.Reach(form, Cfg);
        Ui.AlignForm(form);
        _box.AddChild(form);
        if (Filling && FillOptions.StopAtGrid(Cfg, S.View.Grid) is { } stops) _box.AddChild(stops);
    }

    void AddInk()
    {
        _box.AddChild(Header("Ink"));
        var sw = new GridContainer { Columns = ColorChoices.All.Length + 1 };
        sw.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        sw.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        var size = new Vector2(InkSwatch, InkSwatchHigh);
        bool listed = false;
        foreach (var (what, hex) in ColorChoices.All)
        {
            var col = Coords.ParseColor(hex, Colors.Black);
            bool chosen = col.ToHtml(false) == T.PenColor.ToHtml(false);
            listed |= chosen;
            var b = Ui.Swatch(col, size, what, chosen);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.Pressed += () => TakeInk(col, hex);
            sw.AddChild(b);
        }
        var own = OtherInk(T.PenColor, !listed);
        Ui.WhenPicked(own, c => TakeInk(c, "#" + c.ToHtml(false)));
        sw.AddChild(own);
        _box.AddChild(Ui.Well(sw));
        var form = Ui.Form(Ui.PanelLabels);
        OpacityRow(form);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    static ColorPickerButton OtherInk(Color now, bool chosen)
    {
        var own = Ui.SwatchPlus(now, new Vector2(InkSwatch, InkSwatchHigh), "Any other colour", chosen);
        own.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return own;
    }

    void TakeInk(Color ink, string hex)
    {
        T.PenColor = ink;
        Cfg.PenInk = hex;
        Cfg.Save();
        Rebuild();
    }

    void PenRow(GridContainer form)
    {
        _pen = ReliefPanel.Row(form, Cfg, "Width", Math.Log10(PenTool.LeastPen), Math.Log10(PenTool.MostPen), LengthStep, Math.Log10(T.PenThickness),
            v => T.Pen.SaidFor(PenTool.Tidy(Math.Pow(10, v))), v => { if (!_penSyncing) T.PenThickness = PenTool.Tidy(Math.Pow(10, v)); },
            "How thick the line lies on the map, the same at any zoom · Ctrl+wheel sets it too");
    }

    void OpacityRow(GridContainer form) =>
        ReliefPanel.Row(form, Cfg, "Opacity", LeastOpacity * 100, 100, OpacityStep, Mathf.Round(T.PenOpacity * 100), v => $"{v:0}%", v =>
        {
            T.PenOpacity = (float)(v / 100);
            Cfg.PenOpacity = T.PenOpacity;
        }, "How see-through new lines and fills are: 100% hides what is under them");

    void AddLetters()
    {
        var letters = T.Letters;
        letters.Beside = Panel;
        _styleSeen = letters.StyleTaken;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Font" });
        form.AddChild(FontGrid(letters));
        form.AddChild(new Label { Text = "Style" });
        form.AddChild(StyleRow(letters));
        _letterSlide = ReliefPanel.Row(form, Cfg, "Size", Math.Log10(Stroke.LeastLetter), Math.Log10(Stroke.MostLetter), LengthStep, Math.Log10(letters.LetterKept),
            v => letters.SaidKept(PenTool.Tidy(Math.Pow(10, v))), v =>
            {
                if (_penSyncing) return;
                letters.LetterKept = PenTool.Tidy(Math.Pow(10, v));
                letters.Restyle(sized: true);
            }, "How tall the letters stand on the map, the same at any zoom · = and − or Ctrl+wheel set it, over a name that name");
        _letterSlide.FocusMode = Control.FocusModeEnum.None;
        var spacing = ReliefPanel.Row(form, Cfg, "Spacing", Stroke.LeastSpacing * 100, Stroke.MostSpacing * 100, SpacingStep, Math.Round(Cfg.NameSpacing * 100), v => $"{v:0}%", v =>
        {
            Cfg.NameSpacing = v / 100;
            letters.Restyle();
        }, "Room added between the letters, as a share of their height");
        spacing.FocusMode = Control.FocusModeEnum.None;
        form.AddChild(new Label { Text = "Align" });
        form.AddChild(AlignRow(letters));
        Ui.AlignForm(form);
        _box.AddChild(form);
        AddNameInk(letters);
    }

    static readonly (string Key, string Name)[] Faces =
    {
        (Stroke.FontSerif, "The map's lettering for places"), (Stroke.FontWater, "The map's lettering for water"), (Stroke.FontRegion, "The map's lettering for regions"),
        (Stroke.FontDidone, "Didone"), (Stroke.FontSans, "Sans"), (Stroke.FontFraktur, "Fraktur"), (Stroke.FontScript, "Script"), (Stroke.FontCapitals, "Capitals"),
    };

    GridContainer FontGrid(LetterTool letters)
    {
        var grid = new GridContainer { Columns = FaceColumns };
        grid.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        grid.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        int style = MapStyles.Index(S.View.Lighting.MapStyle);
        foreach (var (key, name) in Faces)
        {
            var own = MapLettering.MapHand(key) is { } kind ? MapLettering.OwnLettering(style, kind) : (Face: MapLettering.Face(key), Capitals: false);
            var b =
                new Button { Text = own.Capitals ? FaceSample.ToUpperInvariant()
                    : FaceSample, ToggleMode = true, ButtonPressed = key == Cfg.NameFont, FocusMode = Control.FocusModeEnum.None, TooltipText = name,
                    CustomMinimumSize = Ui.Px(30, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddThemeFontOverride("font", own.Face);
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
        _box.AddChild(Header("Ink"));
        var own = MapLettering.OwnInk(MapStyles.Index(S.View.Lighting.MapStyle));
        var sw = new GridContainer { Columns = ColorChoices.All.Length + 2 };
        sw.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        sw.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        void Take(string hex)
        {
            Cfg.NameInk = hex;
            Cfg.Save();
            letters.Restyle();
            Rebuild();
        }
        var mapInk = Ui.Swatch(own, new Vector2(InkSwatch, InkSwatchHigh), "The map's own ink", Cfg.NameInk.Length == 0);
        mapInk.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mapInk.Pressed += () => Take("");
        sw.AddChild(mapInk);
        foreach (var (what, hex) in ColorChoices.All)
        {
            var b = Ui.Swatch(Coords.ParseColor(hex, Colors.Black), new Vector2(InkSwatch, InkSwatchHigh), what, string.Equals(hex, Cfg.NameInk, StringComparison.OrdinalIgnoreCase));
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.Pressed += () => Take(hex);
            sw.AddChild(b);
        }
        bool listed = Cfg.NameInk.Length == 0 || ColorChoices.All.Any(c => string.Equals(c.Hex, Cfg.NameInk, StringComparison.OrdinalIgnoreCase));
        var ink = OtherInk(Coords.ParseColor(Cfg.NameInk, own), !listed);
        ink.ColorChanged += c =>
        {
            Cfg.NameInk = "#" + c.ToHtml(false);
            Cfg.Save();
            letters.Restyle(keys: false);
        };
        ink.PopupClosed += () =>
        {
            letters.TakeKeys();
            Callable.From(Rebuild).CallDeferred();
        };
        sw.AddChild(ink);
        _box.AddChild(Ui.Well(sw));
    }

    HSlider? _pen, _letterSlide;
    bool _penSyncing;
    int _styleSeen;
    string? _sightShown;

    public void Sync()
    {
        SyncDrop();
        if (T != null && T.Tool == Tool.Letter && Panel is { Visible: true } && T.Letters.StyleTaken != _styleSeen) Rebuild();
        if (T != null && Panel is { Visible: true } && (T.DrawsSight ? T.SightLineKind : null) != _sightShown)
        {
            FitPenToLayer();
            Rebuild();
        }
        SyncLetters();
        if (T == null || Panel is not { Visible: true } || _pen == null || !GodotObject.IsInstanceValid(_pen)) return;
        double now = T.PenThickness;
        _penSyncing = true;
        if (Math.Abs(Math.Pow(10, _pen.Value) - now) > now * PenSliderSlack) _pen.Value = Math.Log10(now);
        _penSyncing = false;
    }

    void SyncLetters()
    {
        if (T == null || Panel is not { Visible: true } || _letterSlide is not { } slide || !GodotObject.IsInstanceValid(slide)) return;
        double now = T.Letters.LetterKept;
        _penSyncing = true;
        if (Math.Abs(Math.Pow(10, slide.Value) - now) > now * PenSliderSlack) slide.Value = Math.Log10(now);
        _penSyncing = false;
    }

    const float LeastOpacity = 0.1f, InkSwatch = 15, InkSwatchHigh = 22;
    const int ToolColumns = 6, BodyWide = 266;
    const int FaceColumns = 4, FaceSampleSize = 17;
    const string FaceSample = "Ag";
    const double LengthStep = 0.0002, PenSliderSlack = 0.006, SpacingStep = 1, OpacityStep = 5;
}
