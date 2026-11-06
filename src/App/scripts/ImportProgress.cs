using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

public sealed class ImportWatch
{
    public ImportStages Stages { get; } = new();
    public double Began { get; } = ImportProgress.Now;
    public double? Ended { get; private set; }
    public string Outcome { get; private set; } = "";
    public string Sizes { get; set; } = "";

    public bool Read(string line) => Ended == null && Stages.Read(line, ImportProgress.Now);

    public void Between() => Stages.End(ImportProgress.Now);

    public void End(string outcome)
    {
        if (Ended != null) return;
        Ended = ImportProgress.Now;
        Stages.End(Ended.Value);
        Outcome = outcome;
    }
}

public partial class ImportProgress : PanelContainer
{
    const double QuietAfter = 10, ShortStage = 10;
    const string Found = "local";
    const int TitleSize = 15, DetailSize = 12, PastGap = 18;
    static readonly HashSet<string> Streamed = new() { "module", "download" };
    static readonly HashSet<string> Uncounted = new() { "module", "read", "finish" };
    static readonly Dictionary<string, (string Doing, string Done)> Words = new()
    {
        ["module"] = ("Downloading the module from Steam", "Module"),
        ["read"] = ("Reading the module", "Read"),
        ["local"] = ("Reading TTS's copies of its pictures and models", "TTS's copies"),
        ["download"] = ("Downloading pictures and models", "Downloads"),
        ["pieces"] = ("Building pieces", "Pieces"),
        ["maps"] = ("Slicing map sheets into tiles", "Maps"),
        ["table"] = ("Laying out the table", "Table"),
        ["finish"] = ("Writing the report", "Report"),
    };

    readonly Label _title, _clock, _numbers, _item;
    readonly HFlowContainer _past;
    readonly ProgressBar _bar;
    ImportWatch? _watch;
    int _pastShown = -1;
    string _waiting = "";

    public static double Now => Time.GetTicksMsec() / 1000.0;

    public ImportProgress(Button stop)
    {
        Visible = false;
        var card = Ui.Card();
        card.ContentMarginLeft = 12;
        card.ContentMarginRight = 8;
        card.ContentMarginTop = 8;
        card.ContentMarginBottom = 10;
        AddThemeStyleboxOverride("panel", card);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", Ui.Gap.Row);
        AddChild(col);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Gap.Group);
        _title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _title.AddThemeFontOverride("font", Ui.SansStrong());
        _title.AddThemeFontSizeOverride("font_size", TitleSize);
        head.AddChild(_title);
        _clock = Detail(Ui.Muted);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(_clock);
        head.AddChild(stop);
        col.AddChild(head);

        _bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4), MinValue = 0, MaxValue = 1 };
        _bar.AddThemeStyleboxOverride("background", Pill(Ui.Line));
        _bar.AddThemeStyleboxOverride("fill", Pill(Ui.Accent));
        col.AddChild(_bar);

        _numbers = Detail(Ui.Ink);
        col.AddChild(_numbers);
        _item = Detail(Ui.Muted);
        _item.ClipText = true;
        _item.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        col.AddChild(_item);
        _past = new HFlowContainer();
        _past.AddThemeConstantOverride("h_separation", PastGap);
        _past.AddThemeConstantOverride("v_separation", Ui.Gap.Hair);
        col.AddChild(_past);
    }

    static Label Detail(Color tint, string text = "")
    {
        var l = new Label { Text = text, Modulate = tint, MouseFilter = MouseFilterEnum.Pass };
        l.AddThemeFontSizeOverride("font_size", DetailSize);
        return l;
    }

    static StyleBoxFlat Pill(Color c) => new() { BgColor = c, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2 };

    public void Follow(ImportWatch watch, string waiting)
    {
        _watch = watch;
        _waiting = waiting;
        _pastShown = -1;
        Visible = true;
        Refresh();
    }

    public void Waiting(string waiting) => _waiting = waiting;

    public override void _Process(double delta) { if (Visible && _watch != null) Refresh(); }

    void Refresh()
    {
        var w = _watch!;
        var s = w.Stages;
        double now = w.Ended ?? Now;
        _clock.Text = Clock(now - w.Began);
        if (_pastShown != s.Past.Count) ShowPast(s);
        if (w.Ended != null)
        {
            _title.Text = w.Outcome;
            _bar.Visible = false;
            _numbers.Text = w.Sizes;
            _numbers.Visible = w.Sizes.Length > 0;
            _item.Visible = false;
            return;
        }
        if (s.Stage is not { } stage)
        {
            _title.Text = _waiting;
            _bar.Visible = true;
            _bar.Indeterminate = true;
            _numbers.Visible = false;
            _item.Visible = false;
            return;
        }
        _title.Text = Words.TryGetValue(stage, out var words) ? words.Doing : stage;
        _bar.Visible = true;
        _bar.Indeterminate = s.Share == null;
        if (s.Share is { } share) _bar.Value = share;
        _numbers.Text = Numbers(s, stage, now);
        _numbers.Visible = _numbers.Text.Length > 0;
        _item.Text = s.Item;
        _item.Visible = s.Item.Length > 0;
    }

    void ShowPast(ImportStages s)
    {
        _pastShown = s.Past.Count;
        foreach (var c in _past.GetChildren()) c.QueueFree();
        foreach (var text in s.Past.Select(Summary).OfType<string>()) _past.AddChild(Detail(Ui.Faint, text));
        _past.Visible = _past.GetChildCount() > 0;
    }

    static string Numbers(ImportStages s, string stage, double now)
    {
        var parts = new List<string>();
        if (s.Total > 0 && !Uncounted.Contains(stage)) parts.Add($"{N(s.Done)} of {N(s.Total)}");
        if (s.BytesTotal > 0) parts.Add($"{ContentLimits.Describe(s.Bytes)} of {ContentLimits.Describe(s.BytesTotal)}");
        else if (s.Bytes > 0) parts.Add(ContentLimits.Describe(s.Bytes));
        if (Streamed.Contains(stage) && s.BytesPerSecond(now) is { } rate) parts.Add(ContentLimits.Describe((long)rate) + "/s");
        if (s.Failed > 0) parts.Add($"{N(s.Failed)} failed");
        if (s.SecondsLeft(now) is { } left) parts.Add(Left(left));
        if (now - s.LastNews >= QuietAfter) parts.Add($"nothing new for {Clock(now - s.LastNews)}");
        return string.Join("  ·  ", parts);
    }

    static string? Summary(ImportStages.Finished f)
    {
        if (f.Stage == Found && f.Bytes == 0) return null;
        var parts = new List<string> { Words.TryGetValue(f.Stage, out var w) ? w.Done : f.Stage };
        if (f.Done > 0 && !Uncounted.Contains(f.Stage) && f.Stage != Found) parts.Add(N(f.Done));
        if (f.Bytes > 0) parts.Add(ContentLimits.Describe(f.Bytes));
        if (f.Failed > 0) parts.Add($"{N(f.Failed)} failed");
        parts.Add(Took(f.Seconds));
        return parts[0] + " " + string.Join(" · ", parts.Skip(1));
    }

    static string Left(double seconds)
    {
        const int Few = 10, Minute = 60, Hour = 3600, Rounding = 5;
        if (seconds < Few) return "a few seconds left";
        if (seconds < Minute) return $"about {Math.Ceiling(seconds / Rounding) * Rounding:0} s left";
        if (seconds < Hour) return $"about {Math.Ceiling(seconds / Minute):0} min left";
        return $"about {(int)(seconds / Hour)} h {Math.Ceiling(seconds % Hour / Minute):0} min left";
    }

    static string Took(double s) => s < ShortStage ? FormattableString.Invariant($"{s:0.0} s") : Clock(s);
    static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
    static string Clock(double s) { int v = (int)Math.Max(0, Math.Round(s)); return $"{v / 60}:{v % 60:00}"; }
}
