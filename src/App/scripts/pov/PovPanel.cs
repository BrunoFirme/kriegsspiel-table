using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

using KTable.Client;
using KTable.Core.Model;

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
        var form = Ui.Form(Ui.PanelLabels);
        _box.AddChild(form);
        AddMode(form, d);
        if (d.FirstPerson)
        {
            AddFacing(form, d);
            AddEye(form, d);
            AddLens(form, d);
        }
        else
        {
            if (d.Gridded) AddRings(form, d);
            else AddRadius(form, d);
            AddViewshed(form, d);
            if (d.Surveyed) AddWatch(form, d);
            if (d.Drawn) AddOutline(form);
            else if (!d.Outlined) AddLines(form, d);
            AddSharp(form, d);
            if (!d.Outlined && d.BlursPastLines) AddLineBlur(form, d);
        }
        AddQuality(form, d);
        Ui.AlignForm(form);
        AddCount(_box);
        _box.AddChild(Ui.Actions(Btn("Cancel", T.CancelSighting),
            Btn("Record", () => Take(d, gif: true), $"A GIF until Stop or Enter, {GifRecording.FramesPerSecond} frames a second, up to {GifRecording.MostSeconds:0} s or {GifRecording.MostBytes / BytesPerMegabyte:0} MB; it goes to the clipboard as a file and to the capture folder"),
            Ui.Primary("Capture", () => Take(d, gif: false), "One still picture, to the clipboard and the capture folder   (Enter)")));
        Panel.Fit();
    }

    void Take(SightingDraft d, bool gif)
    {
        d.Gif = gif;
        _ = Shell.CaptureSighting();
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

    internal static void AddGifSize(GridContainer grid, KTable.Client.Prefs.Settings cfg, Action? changed = null) =>
        AddChoice(grid, "Quality", cfg.GifLarge, on =>
            {
                cfg.GifLarge = on;
                cfg.Save();
                changed?.Invoke();
            },
            ("Normal", $"Lasts longest before the {GifRecording.MostBytes / BytesPerMegabyte:0} MB stop"),
            ("Large", $"{GifRecording.LargeTimes}× wider and taller; reaches the {GifRecording.MostBytes / BytesPerMegabyte:0} MB stop sooner"));

    internal static Button[] AddScale(GridContainer grid, int now, Action<int> pick, Func<int, string> tip) =>
        AddChoice(grid, "Quality", Math.Clamp(now, 1, KTable.Client.Prefs.Settings.CaptureScaleMost) - 1, i => pick(i + 1),
            Enumerable.Range(1, KTable.Client.Prefs.Settings.CaptureScaleMost).Select(k => ($"{k}×", tip(k))).ToArray());

    void AddQuality(GridContainer grid, SightingDraft d)
    {
        var sizes = AddScale(grid, Cfg.PovScale, k => Keep(() => Cfg.SightingScale = k, _profiles.Sync), QualityTip);
        var text = Ui.IconBtn(d.HideText ? "lettering-off" : "lettering", d.HideText ? "Text hidden" : "Text shown",
            d.HideText ? "No names, counters, notes or studio lettering in the picture; words in imported pictures stay" : "Names, counters, notes and faces as the table shows them");
        (text.ToggleMode, text.ButtonPressed, text.CustomMinimumSize) = (true, d.HideText, Ui.Px(LineToolSide, LineToolSide));
        text.Toggled += on => Keep(() => Cfg.SightingHideText = on, () => T.SetSightingHideText(on));
        sizes[0].GetParent().AddChild(text);
    }

    static string QualityTip(int k) =>
        $"{Captures.SightingSide * k:#,0} pixels across" + (k == 1 ? " · a GIF records at its usual size" : $" · a GIF records {GifRecording.LargeTimes}× larger");

    static readonly (SightingFormat Format, string Text, string Tip)[] Modes =
    {
        (SightingFormat.Round, "Round", "A circle around the piece: what it can see, as far as it can see"),
        (SightingFormat.Square, "Square", "A square around the piece, reaching as far at its edge: the shape a screenshot is"),
        (SightingFormat.Drawn, "Drawn", "Your own outline, drawn by hand inside the square: the picture is cut to it, no lines traced"),
        (SightingFormat.Grid, "Grid", "Whole cells of the scenario's grid around the piece, cut on their edges"),
    };

    void AddMode(GridContainer grid, SightingDraft d)
    {
        bool gridded = S.View.Grid is { } cells && GridSnap.On(cells);
        var flat = Modes.Where(m => m.Format != SightingFormat.Grid || gridded).ToArray();
        int fpv = flat.Length;
        var options = flat.Select(m => (m.Text, m.Tip)).Append(("FPV", "First person view: from the piece's own eyes; drag to look around, hold a direction, capture what it sees")).ToArray();
        int on = d.FirstPerson ? fpv : Array.FindIndex(flat, m => m.Format == d.Format);
        var buttons = AddChoice(grid, "Mode", Math.Max(on, 0), i => PickMode(d, i == fpv ? null : flat[i].Format), options);
        if (!d.Many) return;
        buttons[fpv].Disabled = true;
        buttons[fpv].TooltipText = "First person view, out of one piece's own eyes: pick one piece for it";
    }

    void PickMode(SightingDraft d, SightingFormat? format)
    {
        if (format is not { } f)
        {
            Keep(() => Cfg.SightingFirstPerson = true, () => T.SetSightingFirstPerson(true));
            return;
        }
        Keep(() => (Cfg.SightingFirstPerson, Cfg.SightingFormat) = (false, SightingDraft.NameOf(f)), () =>
        {
            if (d.FirstPerson) T.SetSightingFirstPerson(false);
            T.SetSightingFormat(f);
        });
    }

    void Keep(Action remember, Action apply)
    {
        remember();
        Cfg.Save();
        apply();
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
            d.Radius = cal.ToTableUnits(Math.Max(v, radius.MinValue));
            if (!(sliding && d.Many)) Apply();
        };
    }

    void AddRings(GridContainer grid, SightingDraft d)
    {
        var cells = d.Grid!;
        var rings = new HSlider
        {
            MinValue = 0, MaxValue = GridCells.MostRings, Step = 1, Value = d.Rings, TooltipText = $"How many {Sighting.CellsName(cells)} out from the piece's own",
        };
        SliderRow(grid, "Radius", rings, v => RingsText((int)Math.Round(v), cells));
        rings.ValueChanged += v =>
        {
            d.SetRings((int)Math.Round(v));
            d.Recompute(T);
            T.ShowDraft();
            _showSharp?.Invoke();
            _profiles.Sync();
        };
    }

    internal static string RingsText(int rings, GridDef cells)
    {
        string one = cells.Kind == "hex" ? "hex" : "square";
        return rings switch { 0 => $"Own {one}", 1 => $"1 {one}", _ => $"{rings} {Sighting.CellsName(cells)}" };
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

    internal static HBoxContainer LineKindRow(string current, Action<string> pick)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        foreach (var (kind, text, tip) in LineKinds)
        {
            var b = Choice(text, tip, current == kind, () => pick(kind));
            b.AddChild(Mark(SightLine.InkOf(kind)));
            row.AddChild(b);
        }
        return row;
    }

    void AddLines(GridContainer grid, SightingDraft d)
    {
        grid.AddChild(new Label { Text = "Lines" });
        var row = LineKindRow(d.LineKind, T.Sighting.SetLineKind);
        row.AddChild(KeepPin(d));
        row.AddChild(SightLayer());
        grid.AddChild(row);
        if (T.StrokesDrawn == 0) return;
        grid.AddChild(new Control());
        var below = Tail(TracedCount("line") + (d.Keep ? " on the table" : " traced"));
        if (!d.Keep) below.AddChild(Btn("Clear", T.ClearOutline, "Back to the whole picture"));
        grid.AddChild(below);
    }

    Button SightLayer()
    {
        var b = Ui.IconBtn("draw", "Sight layer", "Opens the Draw tool on the Sight layer: the kept lines every POV capture uses");
        b.CustomMinimumSize = Ui.Px(LineToolSide, LineToolSide);
        b.Pressed += T.DrawOnSight;
        return b;
    }

    void AddOutline(GridContainer grid)
    {
        if (T.StrokesDrawn == 0) return;
        grid.AddChild(new Label { Text = "Outline" });
        var row = Tail(TracedCount("outline") + " drawn");
        row.AddChild(Btn("Clear", T.ClearOutline, "Back to the whole picture"));
        grid.AddChild(row);
    }

    static HBoxContainer Tail(string counted)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var count = new Label { Text = counted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted };
        count.AddThemeFontSizeOverride("font_size", Ui.Px(CountFontSize));
        row.AddChild(count);
        return row;
    }

    const int CountFontSize = 12;

    string TracedCount(string thing) => T.StrokesDrawn == 1 ? $"1 {thing}" : $"{T.StrokesDrawn} {thing}s";

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

    void AddLineBlur(GridContainer grid, SightingDraft d)
    {
        var blur = new HSlider
        {
            MinValue = SightingDraft.LeastBlur, MaxValue = SightingDraft.MostBlur, Step = SightingDraft.BlurStep, ExpEdit = true, Value = d.LineBlur,
            TooltipText = "How far things smudge past every blur line",
        };
        SliderRow(grid, "Line blur", blur, v => Distance(d.Radius * v));
        bool dragging = false;
        blur.DragStarted += () => dragging = true;
        blur.ValueChanged += v => T.Sighting.SetLineBlur(v, keep: !dragging);
        blur.DragEnded += _ =>
        {
            dragging = false;
            T.Sighting.SetLineBlur(blur.Value, keep: true);
        };
    }

    void AddSharp(GridContainer grid, SightingDraft d)
    {
        var sharp = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = SightingDraft.SharpStep, Value = d.Sharp,
            TooltipText = "Where the picture starts to blur towards its edge: all the way right keeps it sharp everywhere",
        };
        string SharpText(double v) => v >= 1 ? "None" : Distance(d.Edged ? d.BlurReach * (1 - v) : d.Radius * v);
        var sharpBox = SliderRow(grid, d.Edged ? "Blur in" : "Blur", sharp, SharpText);
        var blur = new HSlider
        {
            MinValue = SightingDraft.LeastBlur, MaxValue = SightingDraft.MostBlur, Step = SightingDraft.BlurStep, ExpEdit = true, Value = d.Blur,
            TooltipText = "How far things smudge at the very edge: right for smudges, left for a light haze",
        };
        var blurLabel = new Label();
        var blurBox = SliderRow(grid, "Edge blur", blur, v => Distance(d.BlurReach * v), blurLabel);
        void Show()
        {
            sharpBox.Text = SharpText(d.Sharp);
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

}
