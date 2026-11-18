using System;
using System.Linq;
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
    bool _options;

    const double BytesPerMegabyte = 1e6;

    public PovPanel(HudContext ctx, IHudShell shell, Captures captures) : base(ctx)
    {
        Shell = shell;
        _captures = captures;
        Panel = shell.MakePanel("sighting", "POV capture");
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
        BuildEveryday(Page(!_options), d);
        BuildOptions(Page(_options), d);
        var take = d.Gif
            ? Ui.Primary("Record", () => _ = Shell.CaptureSighting(), "Records until Stop or Enter; the GIF goes to the clipboard as a file and to the capture folder")
            : Ui.Primary("Capture", () => _ = Shell.CaptureSighting(), "The picture goes to the clipboard and the capture folder");
        _box.AddChild(Ui.Actions(Btn("Cancel", T.CancelSighting), take));
        Panel.Fit();
    }

    void BuildEveryday(VBoxContainer page, SightingDraft d)
    {
        var everyday = Ui.Form(Ui.PanelLabels);
        page.AddChild(everyday);
        AddView(everyday, d);
        if (d.FirstPerson)
        {
            AddFacing(everyday, d);
            AddEye(everyday, d);
            AddLens(everyday, d);
        }
        else
        {
            AddFormat(everyday, d);
            AddRadius(everyday, d);
            AddViewshed(everyday, d);
            if (!d.Drawn) AddLines(everyday, d);
        }
        Ui.AlignForm(everyday);
        if (!d.FirstPerson && T.StrokesDrawn + T.StrokesTakenBack > 0) page.AddChild(Traced());
        AddCount(page);
        page.AddChild(PageTurn(forward: true));
    }

    void BuildOptions(VBoxContainer page, SightingDraft d)
    {
        page.AddChild(PageTurn(forward: false));
        var picture = Ui.Form(Ui.PanelLabels);
        page.AddChild(picture);
        AddShot(picture, d);
        if (d.Gif) AddGifSize(picture, Cfg, _profiles.Sync);
        else AddScale(picture, Cfg.PovScale, k => Keep(() => Cfg.SightingScale = k, _profiles.Sync), k => $"{Captures.SightingSide * k:#,0} pixels across");
        AddText(picture, d);
        Ui.AlignForm(picture);
        if (d.FirstPerson) return;
        page.AddChild(Header("Sight"));
        var sight = Ui.Form(Ui.PanelLabels);
        page.AddChild(sight);
        if (d.Surveyed) AddWatch(sight, d);
        AddSharp(sight, d);
        if (!d.Drawn) AddLineBlur(sight, d);
        Ui.AlignForm(sight);
    }

    VBoxContainer Page(bool shown)
    {
        var page = new VBoxContainer { Visible = shown };
        page.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        _box.AddChild(page);
        return page;
    }

    Button PageTurn(bool forward)
    {
        var b = Btn(forward ? "Options  ›" : "‹  Options", () =>
        {
            _options = forward;
            Rebuild();
        }, forward ? "Picture size, shot, text and the finer sight settings" : "Back to the everyday settings");
        Ui.MakeQuiet(b);
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    void BuildRecording(SightingDraft d)
    {
        _recordLine = Hint("");
        _box.AddChild(_recordLine);
        ShowRecording();
        AddCount(_box);
        _box.AddChild(Ui.Actions(
            Btn("Cancel", T.CancelSighting, "Drops the GIF"),
            Ui.Primary("Stop", () => _ = Shell.CaptureSighting(), "Ends the GIF and hands it over, as Enter does")));
        Panel.Fit();
    }

    void AddCount(Control into)
    {
        _countLine = Hint("");
        _countLine.MouseFilter = Control.MouseFilterEnum.Stop;
        into.AddChild(_countLine);
        ShowCount();
    }

    public void ShowCount()
    {
        if (T.Draft is not { } d || _countLine == null || !GodotObject.IsInstanceValid(_countLine)) return;
        string pictured = d.Included.Count == 1 ? "1 piece in the picture" : $"{d.Included.Count} pieces in the picture";
        _countLine.Text = d.Many ? $"Seen by {d.Watchers.Count} pieces · {pictured}" : pictured;
        _countLine.TooltipText = d.Many ? "What every picked piece sees from where it stands, in one picture; pieces close together look out as one" : "";
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

    internal static void AddScale(GridContainer grid, int now, Action<int> pick, Func<int, string> tip) =>
        AddChoice(grid, "Size", Math.Clamp(now, 1, KTable.Client.Prefs.Settings.CaptureScaleMost) - 1, i => pick(i + 1),
            Enumerable.Range(1, KTable.Client.Prefs.Settings.CaptureScaleMost).Select(k => ($"{k}×", tip(k))).ToArray());

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

    void AddView(GridContainer grid, SightingDraft d)
    {
        var views = AddChoice(grid, "View", d.FirstPerson, on => Keep(() => Cfg.SightingFirstPerson = on, () => T.SetSightingFirstPerson(on)),
            ("2D", "The map from straight above, framed on the reach: trace what blocks the view here"),
            ("First person", "From the piece's own eyes: drag to look around, hold a direction, capture what it sees"));
        if (!d.Many) return;
        views[1].Disabled = true;
        views[1].TooltipText = "Out of one piece's own eyes: pick one piece for it";
    }

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
        bool sliding = false;
        void Apply()
        {
            d.Recompute(T);
            T.ShowDraft();
            _showSharp?.Invoke();
            _profiles.Sync();
        }
        radius.DragStarted += () => sliding = true;
        radius.DragEnded += _ =>
        {
            sliding = false;
            Apply();
        };
        radius.ValueChanged += v =>
        {
            d.Radius = cal.ToTableUnits(v);
            if (!(sliding && d.Many)) Apply();
        };
    }

    const double MostRadiusMetres = 300000, MostRadiusUnits = 15000;
    const float DetentGain = 0.16f;
    const int ReachValueWidth = 64, SliderWidth = 150;

    string Distance(double tableUnits)
    {
        var cal = S.View.Calibration;
        return cal.UnitsPerMeter is > 0 ? $"{cal.FromTableUnits(tableUnits):#,0} {cal.UnitName}" : $"{tableUnits:0.0} u";
    }

    static readonly (string Kind, string Text, string Tip)[] LineKinds =
    {
        (SightLine.Block, "Block", "What lies behind a line is left out of the picture: walls, wood edges, crests"),
        (SightLine.Blurs, "Blur", "What lies behind a line is blurred: a border, haze, ground too far to make out"),
        (SightLine.Shows, "View", "Drawn round ground the piece sees over what would hide it: a hilltop, a tower, the far slope"),
    };

    const float MarkInset = 0.2f, MarkThick = 2, MarkBottom = 2, LineToolSide = 24;

    void AddLines(GridContainer grid, SightingDraft d)
    {
        grid.AddChild(new Label { Text = "Lines" });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        foreach (var (kind, text, tip) in LineKinds)
        {
            var b = Choice(text, tip, d.LineKind == kind, () => T.Sighting.SetLineKind(kind));
            b.AddChild(Mark(SightLine.InkOf(kind)));
            row.AddChild(b);
        }
        row.AddChild(KeepPin(d));
        if (S.View.Boards.FirstOrDefault(b => b.Sight) is { } sight) row.AddChild(SightShown(sight.Id));
        grid.AddChild(row);
    }

    static ColorRect Mark(string ink) => new()
    {
        Color = Coords.ParseColor(ink, Colors.White), MouseFilter = Control.MouseFilterEnum.Ignore, AnchorLeft = MarkInset, AnchorRight = 1 - MarkInset,
        AnchorTop = 1, AnchorBottom = 1, OffsetTop = -Ui.Pxf(MarkBottom + MarkThick), OffsetBottom = -Ui.Pxf(MarkBottom),
    };

    Button KeepPin(SightingDraft d)
    {
        var b = Ui.IconBtn("pin", d.Keep ? "Kept on the table" : "For this picture only",
            d.Keep ? "Every POV capture uses the lines you draw while they are shown" : "Press to keep the lines you draw on the table, for every POV capture");
        (b.ToggleMode, b.ButtonPressed, b.CustomMinimumSize) = (true, d.Keep, Ui.Px(LineToolSide, LineToolSide));
        b.Toggled += on => Keep(() => Cfg.SightingKeep = on, () => T.Sighting.SetKeep(on));
        return b;
    }

    Button SightShown(string board)
    {
        bool shown = !T.HiddenBoards.Contains(board);
        var b = Ui.IconBtn(shown ? "eye" : "eye-off", shown ? "Kept lines shown" : "Kept lines hidden",
            shown ? "Every POV capture uses them; press to leave them out" : "Left out of every POV capture; press to use them again");
        b.CustomMinimumSize = Ui.Px(LineToolSide, LineToolSide);
        b.Pressed += () =>
        {
            T.ShowBoard(board, !shown);
            T.RefreshDrawings();
            T.Sighting.TableChanged();
            Rebuild();
        };
        return b;
    }

    void AddLineBlur(GridContainer grid, SightingDraft d)
    {
        var blur = new HSlider
        {
            MinValue = SightingDraft.LeastBlur, MaxValue = SightingDraft.MostBlur, Step = SightingDraft.BlurStep, ExpEdit = true, Value = d.LineBlur,
            TooltipText = "How far things smudge past the blur lines you draw next",
        };
        SliderRow(grid, "Line blur", blur, v => Distance(d.Radius * v));
        blur.ValueChanged += v => T.Sighting.SetLineBlur(v);
        blur.DragEnded += _ =>
        {
            Cfg.Save();
            T.Sighting.KeepLineBlur();
        };
    }

    void AddSharp(GridContainer grid, SightingDraft d)
    {
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
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.CustomMinimumSize = Ui.Px(SliderWidth, 0);
        slider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var box = Ui.ValueBox(slider, show, ReachValueWidth);
        row.AddChild(slider);
        row.AddChild(box);
        grid.AddChild(row);
        return box;
    }

    void AddViewshed(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Viewshed", d.Surveyed, on => Keep(() => Cfg.SightingViewshed = on, () => T.SetSightingViewshed(on)),
            ("Off", "Everything in reach is in the picture"),
            ("On", "Leaves out the ground the piece's eye cannot see: what lies behind a crest, while a far slope shows again"));

    void AddWatch(GridContainer grid, SightingDraft d)
    {
        var cal = S.View.Calibration;
        var slider = new HSlider
        {
            MinValue = SightingDraft.LowestWatch * cal.PerMetre, MaxValue = SightingDraft.HighestWatch * cal.PerMetre, Step = SightingDraft.WatchStep, ExpEdit = true,
            Value = d.WatchMetres * cal.PerMetre, TooltipText = "How high the eye stands over the ground: a man's height unless you change it",
        };
        SliderRow(grid, "Eye height", slider, v => $"{v:0.#} {cal.UnitName}");
        slider.ValueChanged += v =>
        {
            T.SetSightingWatch(v / cal.PerMetre);
            _profiles.Sync();
        };
        slider.DragEnded += _ => Cfg.Save();
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
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
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

    internal static Button[] AddChoice(GridContainer grid, string label, bool second, Action<bool> pick, (string text, string tip) first, (string text, string tip) other) =>
        AddChoice(grid, label, second ? 1 : 0, i => pick(i == 1), first, other);

    static Button[] AddChoice(GridContainer grid, string label, int on, Action<int> pick, params (string text, string tip)[] options)
    {
        grid.AddChild(new Label { Text = label });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
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
        return buttons;
    }

    static Button Choice(string text, string tip, bool on, Action press)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip };
        b.Pressed += press;
        return b;
    }

    HBoxContainer Traced()
    {
        var traced = new HBoxContainer();
        traced.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        bool drawn = T.Draft is { Drawn: true };
        var undoW = Ui.IconBtn("undo", drawn ? $"Back to the outline before   ({Cfg.Keys.Label("undo")})" : $"Undo the last line traced   ({Cfg.Keys.Label("undo")})",
            drawn ? "Takes back the last outline you drew" : "Takes back the last thing you traced");
        var redoW = Ui.IconBtn("redo", $"Put it back   ({Cfg.Keys.Label("redo")})", drawn ? "Draws the last outline you took back again" : "Traces the last line you took back again");
        undoW.CustomMinimumSize = redoW.CustomMinimumSize = Ui.Px(28, 24);
        undoW.Disabled = T.StrokesDrawn == 0;
        redoW.Disabled = T.StrokesTakenBack == 0;
        undoW.Pressed += T.UndoStroke;
        redoW.Pressed += T.RedoStroke;
        traced.AddChild(undoW);
        traced.AddChild(redoW);
        bool kept = T.Draft is { Keep: true } && !drawn;
        var counted = TracedCount(drawn ? "outline" : "line") + (drawn ? " drawn" : kept ? " on the table" : " traced");
        var tracedCount =
            new Label { Text = counted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted };
        tracedCount.AddThemeFontSizeOverride("font_size", Ui.Px(12));
        traced.AddChild(tracedCount);
        if (!kept)
        {
            var clearW = Btn("Clear", T.ClearOutline, "Back to the whole picture; the arrow puts it back");
            clearW.Disabled = T.StrokesDrawn == 0;
            traced.AddChild(clearW);
        }
        return traced;
    }

    string TracedCount(string thing) => T.StrokesDrawn == 1 ? $"1 {thing}" : $"{T.StrokesDrawn} {thing}s";
}
