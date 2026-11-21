using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CapturePanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;
    readonly IHudShell Shell;
    readonly Captures _captures;
    readonly ProfileTabs _profiles;
    Label? _recordLine;
    SpinBox? _x, _z, _wide, _deep, _pixels;
    bool _syncing;

    const double BytesPerMegabyte = 1e6, PlaceStep = 0.01;
    const float FieldWidth = 104;
    const int LeastForAll = 2;

    public CapturePanel(HudContext ctx, IHudShell shell, Captures captures) : base(ctx)
    {
        Shell = shell;
        _captures = captures;
        Panel = shell.MakePanel("capture", "Capture");
        _box = Panel.Body;
        _profiles = new ProfileTabs(ctx, Panel, Rebuild, boxes: true);
        _profiles.Counted += Rebuild;
        captures.RegionChanged += Rebuild;
        captures.RegionTicked += ShowRecording;
    }

    public void Rebuild()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        _recordLine = null;
        _x = _z = _wide = _deep = _pixels = null;
        bool recording = _captures.RegionRecording != null;
        _profiles.Rebuild(T.Tool == Tool.Capture && Cfg.CaptureBoxed && !recording);
        if (T.Tool != Tool.Capture && !recording)
        {
            Shell.ClosePanel("capture");
            return;
        }
        if (recording) BuildRecording();
        else Build();
        Shell.OpenPanel("capture");
    }

    void Build()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Group));
        PovPanel.AddChoice(grid, "Area", Cfg.CaptureBoxed, PickArea,
            ("Screen", "A part of the view, as it looks on the screen"),
            ("Map box", "A box laid on the map, taken from straight above: the same area every time, wherever the view is"));
        if (Cfg.CaptureBoxed) AddBox(grid);
        else AddShot(grid);
        Ui.AlignForm(grid);
        _box.AddChild(grid);
        if (Cfg.CaptureBoxed) AddBoxActions();
        Panel.Fit();
    }

    void AddShot(GridContainer grid)
    {
        PovPanel.AddChoice(grid, "Shot", Cfg.CaptureGif, Pick,
            ("Picture", "One still picture"),
            ("GIF", $"A moving picture, {GifRecording.FramesPerSecond} frames a second, up to {GifRecording.MostSeconds:0} s or {GifRecording.MostBytes / BytesPerMegabyte:0} MB"));
        if (Cfg.CaptureGif) PovPanel.AddGifSize(grid, Cfg);
        else PovPanel.AddScale(grid, Cfg.CaptureScale, k =>
            {
                Cfg.CaptureScale = k;
                Cfg.Save();
            }, k => k == 1 ? "As sharp as the screen" : $"{k} times as sharp as the screen: maps and text read better in Discord");
    }

    void AddBox(GridContainer grid)
    {
        if (T.Box.Area is not { } a) return;
        _x = Field(grid, "X", a.Centre.X, -MapBox.Farthest, MapBox.Farthest, PlaceStep, "u", "Where the box's middle lies across the table, in table units",
            v => Typed(b => b with { Centre = new Vec2(v, b.Centre.Z) }));
        _z = Field(grid, "Y", a.Centre.Z, -MapBox.Farthest, MapBox.Farthest, PlaceStep, "u", "Where the box's middle lies up the table, in table units",
            v => Typed(b => b with { Centre = new Vec2(b.Centre.X, v) }));
        _wide = Field(grid, "Size X", a.Wide, MapBox.LeastSide, MapBox.MostSide, PlaceStep, "u", "How wide the box is, in table units", v => Typed(b => b with { Wide = v }));
        _deep = Field(grid, "Size Y", a.Deep, MapBox.LeastSide, MapBox.MostSide, PlaceStep, "u", "How deep the box is, top to bottom, in table units", v => Typed(b => b with { Deep = v }));
        _pixels = Field(grid, "Pixels", T.Box.Pixels, MapBox.LeastPixels, MapBox.MostPixels, 1, "px", "The picture's longer side; the other follows the box's shape", v =>
        {
            T.Box.SetPixels((int)v);
            T.Box.Keep();
        });
    }

    void AddBoxActions()
    {
        bool all = S.View.ScenarioId is { Length: > 0 } scenario && KTable.Client.Prefs.CaptureProfiles.Of(Cfg, scenario, boxes: true).Count >= LeastForAll;
        var captureAll = all ? Btn("Capture all", CaptureAll, "Every map box profile, one after another, each to its own file in the capture folder") : null;
        if (T.Box.Area == null)
        {
            _box.AddChild(Hint("No box yet"));
            if (captureAll != null) _box.AddChild(Ui.Actions(captureAll));
            return;
        }
        var clear = Btn("Clear", T.Box.Clear, "Takes the box off the map");
        var capture = Ui.Primary("Capture", T.CaptureBox, "The picture goes to the clipboard and the capture folder, as Enter does");
        _box.AddChild(captureAll != null ? Ui.Actions(clear, captureAll, capture) : Ui.Actions(clear, capture));
    }

    void CaptureAll()
    {
        T.SetTool(Tool.Select);
        _ = _captures.AllBoxes();
    }

    SpinBox Field(GridContainer grid, string name, double value, double least, double most, double step, string suffix, string tip, Action<double> typed)
    {
        grid.AddChild(new Label { Text = name });
        var box = new SpinBox { MinValue = least, MaxValue = most, Step = step, Value = value, Suffix = suffix, TooltipText = tip, CustomMinimumSize = Ui.Px(FieldWidth, 0) };
        box.ValueChanged += v => { if (!_syncing) typed(v); };
        box.GetLineEdit().TextSubmitted += _ => box.GetLineEdit().ReleaseFocus();
        grid.AddChild(box);
        return box;
    }

    void Typed(Func<MapArea, MapArea> change)
    {
        if (T.Box.Area is not { } now) return;
        T.Box.Set(change(now));
        T.Box.Keep();
    }

    public void ShowBox()
    {
        if (T.Tool != Tool.Capture || !Cfg.CaptureBoxed || _captures.RegionRecording != null) return;
        if (T.Box.Area is not { } a || _x == null || !GodotObject.IsInstanceValid(_x))
        {
            Rebuild();
            return;
        }
        _syncing = true;
        _x.Value = a.Centre.X;
        _z!.Value = a.Centre.Z;
        _wide!.Value = a.Wide;
        _deep!.Value = a.Deep;
        _pixels!.Value = T.Box.Pixels;
        _syncing = false;
        _profiles.Sync();
    }

    void PickArea(bool boxed)
    {
        Cfg.CaptureBoxed = boxed;
        Cfg.Save();
        T.RefreshHint();
        Rebuild();
    }

    void Pick(bool gif)
    {
        Cfg.CaptureGif = gif;
        Cfg.Save();
        T.RefreshHint();
        Rebuild();
    }

    void BuildRecording()
    {
        _recordLine = Hint("");
        _box.AddChild(_recordLine);
        ShowRecording();
        _box.AddChild(Ui.Actions(
            Btn("Cancel", () => _captures.CancelRegion(), "Drops the GIF"),
            Ui.Primary("Stop", () => _captures.StopRegion(), "Ends the GIF and hands it over, as Enter does")));
        Panel.Fit();
    }

    void ShowRecording()
    {
        if (_captures.RegionRecording is not { } r || _recordLine == null || !GodotObject.IsInstanceValid(_recordLine)) return;
        _recordLine.Text = $"Recording {r.Seconds:0.0} s · {r.Bytes / BytesPerMegabyte:0.0} of {GifRecording.MostBytes / BytesPerMegabyte:0} MB";
    }
}
