using System;
using Godot;

using KTable.Client;

namespace KTable.App;

sealed class PovPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    readonly IHudShell Shell;
    readonly Captures _captures;
    readonly ProfileTabs _profiles;
    Label? _recordLine, _countLine;
    Action? _showSharp;

    const double BytesPerMegabyte = 1e6;

    public PovPanel(HudContext ctx, IHudShell shell, Captures captures) : base(ctx)
    {
        Shell = shell;
        _captures = captures;
        Panel = AgentFence.Mark(shell.MakePanel("sighting", "POV capture"));
        _box = Panel.Body;
        _profiles = new ProfileTabs(ctx, Panel, Rebuild);
        captures.RecordingTicked += ShowRecording;
    }

    public void Rebuild()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        _profiles.Rebuild(T.Draft != null && _captures.Recording == null);
        if (T.Draft != null) { Build(); Shell.OpenPanel("sighting"); }
        else Shell.ClosePanel("sighting");
    }

    void Build()
    {
        var d = T.Draft!;
        if (_captures.Recording != null)
        {
            BuildRecording(d);
            return;
        }
        var picture = Ui.Form(Ui.PanelLabels);
        _box.AddChild(picture);
        AddView(picture, d);
        if (!d.FirstPerson) AddFormat(picture, d);
        AddShot(picture, d);
        if (d.Gif) AddGifSize(picture, Cfg, _profiles.Sync);
        AddText(picture, d);
        Ui.AlignForm(picture);
        _box.AddChild(Header(d.FirstPerson ? "Eye" : "Reach"));
        var reach = Ui.Form(Ui.PanelLabels);
        _box.AddChild(reach);
        if (d.FirstPerson)
        {
            AddFacing(reach, d);
            AddEye(reach, d);
            AddLens(reach, d);
        }
        else
        {
            AddRadius(reach, d);
            AddSharp(reach, d);
        }
        Ui.AlignForm(reach);
        if (!d.FirstPerson && T.StrokesDrawn + T.StrokesTakenBack > 0) AddTraced();
        AddCount();
        var take = d.Gif
            ? Ui.Primary("Record", () => _ = Shell.CaptureSighting(), "Records until Stop or Enter; the GIF goes to the clipboard as a file and to the capture folder")
            : Ui.Primary("Capture", () => _ = Shell.CaptureSighting(), "The picture goes to the clipboard and the capture folder");
        _box.AddChild(Ui.Actions(Btn("Cancel", T.CancelSighting), take));
        Panel.Fit();
    }

    void BuildRecording(SightingDraft d)
    {
        _recordLine = Hint("");
        _box.AddChild(_recordLine);
        ShowRecording();
        AddCount();
        _box.AddChild(Ui.Actions(
            Btn("Cancel", T.CancelSighting, "Drops the GIF"),
            Ui.Primary("Stop", () => _ = Shell.CaptureSighting(), "Ends the GIF and hands it over, as Enter does")));
        Panel.Fit();
    }

    void AddCount()
    {
        _countLine = Hint("");
        _box.AddChild(_countLine);
        ShowCount();
    }

    public void ShowCount()
    {
        if (T.Draft is not { } d || _countLine == null || !GodotObject.IsInstanceValid(_countLine)) return;
        _countLine.Text = d.Included.Count == 1 ? "1 piece in the picture" : $"{d.Included.Count} pieces in the picture";
    }

    void ShowRecording()
    {
        if (_captures.Recording is not { } r || _recordLine == null || !GodotObject.IsInstanceValid(_recordLine)) return;
        _recordLine.Text = $"Recording {r.Seconds:0.0} s · {r.Bytes / BytesPerMegabyte:0.0} of {GifRecording.MostBytes / BytesPerMegabyte:0} MB";
    }

    void AddShot(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Shot", d.Gif, on => Keep(() => Cfg.SightingGif = on, () => SetGif(d, on)),
            ("Picture", "One still picture"),
            ("GIF", $"A moving picture, {GifRecording.FramesPerSecond} frames a second, up to {GifRecording.MostSeconds:0} s or {GifRecording.MostBytes / BytesPerMegabyte:0} MB"));

    internal static void AddGifSize(GridContainer grid, KTable.Client.Prefs.Settings cfg, Action? changed = null) =>
        AddChoice(grid, "Size", cfg.GifLarge, on =>
            {
                cfg.GifLarge = on;
                cfg.Save();
                changed?.Invoke();
            },
            ("Normal", $"Lasts longest before the {GifRecording.MostBytes / BytesPerMegabyte:0} MB stop"),
            ("Large", $"{GifRecording.LargeTimes}× wider and taller; reaches the {GifRecording.MostBytes / BytesPerMegabyte:0} MB stop sooner"));

    void SetGif(SightingDraft d, bool on)
    {
        d.Gif = on;
        T.RefreshSightingPanel();
    }

    void Keep(Action remember, Action apply)
    {
        remember();
        Cfg.Save();
        apply();
    }

    void AddView(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "View", d.FirstPerson, on => Keep(() => Cfg.SightingFirstPerson = on, () => T.SetSightingFirstPerson(on)),
            ("2D", "The map from straight above, framed on the reach: trace what blocks the view here"),
            ("First person", "From the piece's own eyes: drag to look around, hold a direction, capture what it sees"));

    void AddRadius(GridContainer grid, SightingDraft d)
    {
        var cal = S.View.Calibration;
        bool scaled = cal.UnitsPerMeter is > 0;
        var radius = new HSlider
        {
            MinValue = scaled ? 10 : 0.5, MaxValue = Math.Max(scaled ? MostRadiusMetres : MostRadiusUnits, cal.FromTableUnits(d.Radius)), Step = scaled ? 10 : 0.5, ExpEdit = true,
            Value = cal.FromTableUnits(d.Radius), TooltipText = "How far the piece sees",
        };
        SliderRow(grid, "Radius", radius, v => scaled ? $"{v:#,0} {cal.UnitName}" : $"{v:0.0} u");
        radius.ValueChanged += v =>
        {
            d.Radius = cal.ToTableUnits(v);
            d.Recompute(T);
            T.ShowDraft();
            _showSharp?.Invoke();
            _profiles.Sync();
        };
    }

    const double MostRadiusMetres = 50000, MostRadiusUnits = 2500;
    const float DetentGain = 0.16f;
    const int ReachValueWidth = 64, SliderWidth = 150;

    void AddSharp(GridContainer grid, SightingDraft d)
    {
        var cal = S.View.Calibration;
        string Distance(double tableUnits) => cal.UnitsPerMeter is > 0 ? $"{cal.FromTableUnits(tableUnits):#,0} {cal.UnitName}" : $"{tableUnits:0.0} u";
        var sharp = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = SightingDraft.SharpStep, Value = d.Sharp,
            TooltipText = "Where the picture starts to blur towards its edge: all the way right keeps it sharp everywhere",
        };
        double SharpDistance(double v) => d.Edged ? d.BlurReach * (1 - v) : d.Radius * v;
        var sharpBox = SliderRow(grid, d.Edged ? "Blur in" : "Sharp to", sharp, v => Distance(SharpDistance(v)));
        var blur = new HSlider
        {
            MinValue = SightingDraft.LeastBlur, MaxValue = SightingDraft.MostBlur, Step = SightingDraft.BlurStep, ExpEdit = true, Value = d.Blur,
            TooltipText = "How far things smudge at the very edge: right for smudges, left for a light haze",
        };
        var blurLabel = new Label();
        var blurBox = SliderRow(grid, "Edge blur", blur, v => Distance(d.BlurReach * v), blurLabel);
        void Show()
        {
            sharpBox.Text = Distance(SharpDistance(d.Sharp));
            blurBox.Text = Distance(d.BlurReach * d.Blur);
            blurLabel.Visible = blur.GetParent<Control>().Visible = d.Sharp < 1;
        }
        Show();
        _showSharp = Show;
        sharp.ValueChanged += v =>
        {
            T.SetSightingSharp(v);
            Show();
            _profiles.Sync();
            Sfx.Current?.PlayFlat(Sound.Tick, DetentGain);
        };
        blur.ValueChanged += v =>
        {
            T.SetSightingBlur(v);
            Show();
            _profiles.Sync();
            Sfx.Current?.PlayFlat(Sound.Tick, DetentGain);
        };
    }

    static LineEdit SliderRow(GridContainer grid, string text, HSlider slider, Func<double, string> show, Label? label = null)
    {
        label ??= new Label();
        label.Text = text;
        grid.AddChild(label);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.CustomMinimumSize = new Vector2(SliderWidth, 0);
        slider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var box = Ui.ValueBox(slider, show, ReachValueWidth);
        row.AddChild(slider);
        row.AddChild(box);
        grid.AddChild(row);
        return box;
    }

    const double EyeStep = 0.05;

    void AddEye(GridContainer grid, SightingDraft d)
    {
        var cal = S.View.Calibration;
        var slider = new HSlider
        {
            MinValue = 0, MaxValue = T.SightingEyeHighest, Step = EyeStep, Value = Math.Min(d.EyeHeight, T.SightingEyeHighest),
            TooltipText = "How high the eye stands over the ground: down to look from behind a crest, up to see over it",
        };
        SliderRow(grid, "Height", slider, v => cal.UnitsPerMeter is > 0 ? $"{cal.FromTableUnits(v):#,0} {cal.UnitName}" : $"{v:0.00} u");
        slider.ValueChanged += v =>
        {
            T.SetSightingEye(v);
            _profiles.Sync();
        };
        slider.DragEnded += _ => Cfg.Save();
    }

    void AddFormat(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Format", (int)d.Format, i => Keep(() => Cfg.SightingFormat = SightingDraft.NameOf((SightingFormat)i), () => T.SetSightingFormat((SightingFormat)i)),
            ("Round", "A circle around the piece: what it can see, as far as it can see"),
            ("Square", "A square around the piece, reaching as far at its edge: the shape a screenshot is"),
            ("Drawn", "Your own outline, drawn by hand inside the square: the picture is cut to it, no lines traced"));

    void AddFacing(GridContainer grid, SightingDraft d)
    {
        grid.AddChild(new Label { Text = "Facing" });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        foreach (var cardinal in Sighting.CardinalNames)
        {
            var name = cardinal;
            row.AddChild(Choice(name, $"Look {Compass(name)} and hold it · dragging lets go", d.Facing == name, () => T.FaceSighting(name)));
        }
        grid.AddChild(row);
    }

    static string Compass(string cardinal) => cardinal switch { "N" => "north", "E" => "east", "S" => "south", _ => "west" };

    void AddLens(GridContainer grid, SightingDraft d)
    {
        var slider = new HSlider
        {
            MinValue = SightingDraft.NarrowestLens, MaxValue = SightingDraft.WidestLens, Step = SightingDraft.LensStep, Value = d.Lens,
            TooltipText = $"How much the picture takes in across: {SightingDraft.NormalLens:0}° is a steady look ahead, {SightingDraft.WholeCircleLens:0}° lets the four directions make the whole circle",
        };
        SliderRow(grid, "Lens", slider, v => $"{v:0}°");
        slider.ValueChanged += v =>
        {
            Cfg.SightingLens = v;
            T.SetSightingLens(v);
            _profiles.Sync();
        };
        slider.DragEnded += _ => Cfg.Save();
    }

    void AddText(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Text", d.HideText, on => Keep(() => Cfg.SightingHideText = on, () => T.SetSightingHideText(on)),
            ("Shown", "Names, counters, notes and faces as the table shows them"),
            ("Hidden", "No names, counters, notes or studio lettering in the picture; words in imported pictures stay"));

    internal static void AddChoice(GridContainer grid, string label, bool second, Action<bool> pick, (string text, string tip) first, (string text, string tip) other) =>
        AddChoice(grid, label, second ? 1 : 0, i => pick(i == 1), first, other);

    static void AddChoice(GridContainer grid, string label, int on, Action<int> pick, params (string text, string tip)[] options)
    {
        grid.AddChild(new Label { Text = label });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        var buttons = new Button[options.Length];
        void Pick(int want)
        {
            for (int i = 0; i < buttons.Length; i++) buttons[i].SetPressedNoSignal(i == want);
            pick(want);
        }
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            buttons[i] = Choice(options[i].text, options[i].tip, i == on, () => Pick(index));
            row.AddChild(buttons[i]);
        }
        grid.AddChild(row);
    }

    static Button Choice(string text, string tip, bool on, Action press)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip };
        b.Pressed += press;
        return b;
    }

    void AddTraced()
    {
        var traced = new HBoxContainer();
        traced.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        bool drawn = T.Draft is { Drawn: true };
        var undoW = Ui.IconBtn("undo", drawn ? $"Back to the outline before   ({Cfg.Keys.Label("undo")})" : $"Undo the last line traced   ({Cfg.Keys.Label("undo")})",
            drawn ? "Takes back the last outline you drew" : "Takes back the last thing you traced");
        var redoW = Ui.IconBtn("redo", $"Put it back   ({Cfg.Keys.Label("redo")})", drawn ? "Draws the last outline you took back again" : "Traces the last line you took back again");
        undoW.CustomMinimumSize = redoW.CustomMinimumSize = new Vector2(28, 24);
        undoW.Disabled = T.StrokesDrawn == 0;
        redoW.Disabled = T.StrokesTakenBack == 0;
        undoW.Pressed += T.UndoStroke;
        redoW.Pressed += T.RedoStroke;
        traced.AddChild(undoW);
        traced.AddChild(redoW);
        var counted = TracedCount(drawn ? "outline" : "line") + (drawn ? " drawn" : " in the way");
        var tracedCount =
            new Label { Text = counted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted };
        tracedCount.AddThemeFontSizeOverride("font_size", 12);
        traced.AddChild(tracedCount);
        var clearW = Btn("Clear", T.ClearOutline, "Back to the whole picture; the arrow puts it back");
        clearW.Disabled = T.StrokesDrawn == 0;
        traced.AddChild(clearW);
        _box.AddChild(traced);
    }

    string TracedCount(string thing) => T.StrokesDrawn == 1 ? $"1 {thing}" : $"{T.StrokesDrawn} {thing}s";
}
