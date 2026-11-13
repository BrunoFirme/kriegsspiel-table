using Godot;

namespace KTable.App;

sealed class CapturePanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;
    readonly IHudShell Shell;
    readonly Captures _captures;
    Label? _recordLine;

    const double BytesPerMegabyte = 1e6;

    public CapturePanel(HudContext ctx, IHudShell shell, Captures captures) : base(ctx)
    {
        Shell = shell;
        _captures = captures;
        Panel = shell.MakePanel("capture", "Capture");
        _box = Panel.Body;
        captures.RegionChanged += Rebuild;
        captures.RegionTicked += ShowRecording;
    }

    public void Rebuild()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        _recordLine = null;
        bool recording = _captures.RegionRecording != null;
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
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Group);
        PovPanel.AddChoice(grid, "Shot", Cfg.CaptureGif, Pick,
            ("Picture", "One still picture"),
            ("GIF", $"A moving picture, {GifRecording.FramesPerSecond} frames a second, up to {GifRecording.MostSeconds:0} s or {GifRecording.MostBytes / BytesPerMegabyte:0} MB"));
        if (Cfg.CaptureGif) PovPanel.AddGifSize(grid, Cfg);
        Ui.AlignForm(grid);
        _box.AddChild(grid);
        Panel.Fit();
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
