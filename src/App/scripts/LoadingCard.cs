using System;
using System.Globalization;
using Godot;

namespace KTable.App;

public partial class LoadingCard : PanelContainer
{
    public readonly record struct Progress(string Stage, int Done, int Total);

    readonly Func<Progress> _poll;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    readonly Label _stage, _time;
    readonly ProgressBar _bar;
    string _countStage = "";
    double _countStart, _countFrom;

    public LoadingCard(string title, Font titleFont, Func<Progress> poll, Action cancel)
    {
        _poll = poll;
        CustomMinimumSize = new Vector2(260, 0);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var card = Ui.Card();
        card.ContentMarginLeft = 14;
        card.ContentMarginRight = 8;
        card.ContentMarginTop = 10;
        card.ContentMarginBottom = 12;
        AddThemeStyleboxOverride("panel", card);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        AddChild(col);

        var head = new HBoxContainer();
        var name =
            new Label { Text = title, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = title,
                MouseFilter = MouseFilterEnum.Pass };
        name.AddThemeFontOverride("font", titleFont);
        name.AddThemeFontSizeOverride("font_size", 16);
        head.AddChild(name);
        var x = new Button { Text = "✕", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Cancel", CustomMinimumSize = new Vector2(26, 0) };
        x.AddThemeFontSizeOverride("font_size", 12);
        x.AddThemeColorOverride("font_color", Ui.Faint);
        x.AddThemeColorOverride("font_hover_color", Colors.White);
        x.Pressed += cancel;
        head.AddChild(x);
        col.AddChild(head);

        _bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4), Indeterminate = true };
        _bar.AddThemeStyleboxOverride("background", Pill(Ui.Line));
        _bar.AddThemeStyleboxOverride("fill", Pill(Ui.Accent));
        col.AddChild(_bar);

        var foot = new HBoxContainer();
        _stage = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, Modulate = Ui.Muted };
        _stage.AddThemeFontSizeOverride("font_size", 12);
        foot.AddChild(_stage);
        _time = new Label { Modulate = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Right };
        _time.AddThemeFontSizeOverride("font_size", 12);
        foot.AddChild(_time);
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_right", 6);
        pad.AddChild(foot);
        col.AddChild(pad);
    }

    static StyleBoxFlat Pill(Color c) => new() { BgColor = c, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2 };

    public override void _Process(double delta)
    {
        var p = _poll();
        double t = _clock.Elapsed.TotalSeconds;
        if (p.Total <= 0)
        {
            _bar.Indeterminate = true;
            _stage.Text = p.Stage;
            _time.Text = Clock(t);
            return;
        }
        _bar.Indeterminate = false;
        _bar.MaxValue = p.Total;
        _bar.Value = p.Done;
        _stage.Text = $"{p.Stage} · {N(p.Done)} / {N(p.Total)}";
        if (_countStage != p.Stage)
        {
            _countStage = p.Stage;
            _countStart = t;
            _countFrom = p.Done;
        }
        double rate = (p.Done - _countFrom) / Math.Max(0.001, t - _countStart);
        _time.Text = t - _countStart > 2 && rate > 0 && p.Done < p.Total ? Clock((p.Total - p.Done) / rate) + " left" : Clock(t);
    }

    static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
    static string Clock(double s) { int v = (int)Math.Ceiling(s); return $"{v / 60}:{v % 60:00}"; }
}
