using System;
using System.Globalization;
using Godot;

namespace KTable.App;

sealed class AgentPill
{
    const int PillPadding = 20, PillHeight = 24, DotSize = 8, ListWidth = 320, WorkLength = 24;
    const float PulseSeconds = 0.55f, PulseLow = 0.3f;

    readonly AgentEndpoint _a;
    readonly Action _settings;
    readonly Button _pill;
    readonly HBoxContainer _row;
    readonly TextureRect _dot;
    readonly Label _name, _state;
    Tween? _pulse;
    PopupPanel? _pop;
    VBoxContainer? _list;

    AgentPill(AgentEndpoint a, Action settings)
    {
        _a = a;
        _settings = settings;
        _pill = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        foreach (var st in new[] { "normal", "pressed", "disabled" }) _pill.AddThemeStyleboxOverride(st, Ui.Pill());
        _pill.AddThemeStyleboxOverride("hover", Ui.Pill(0.12f));
        _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _row.OffsetLeft = PillPadding / 2;
        _row.OffsetRight = -PillPadding / 2;
        _dot = new TextureRect
        {
            Texture = Ui.Dot, CustomMinimumSize = new Vector2(DotSize, DotSize), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _row.AddChild(_dot);
        _name = Small(Ui.Ink);
        _name.Text = "Agent";
        _row.AddChild(_name);
        _state = Small(Ui.Muted);
        _row.AddChild(_state);
        var chevron = Small(Ui.Faint);
        chevron.Text = "▾";
        _row.AddChild(chevron);
        _pill.AddChild(_row);
        _pill.Pressed += Open;
        _a.Changed += Sync;
        _pill.TreeExiting += () => { _a.Changed -= Sync; if (_pop != null && GodotObject.IsInstanceValid(_pop)) _pop.QueueFree(); };
        Sync();
    }

    public static Button Make(AgentEndpoint a, Action settings) => new AgentPill(a, settings)._pill;

    static Label Small(Color color)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    enum Phase { Connected, Working }

    Phase Now => _a.Working != null ? Phase.Working : Phase.Connected;

    bool Shown => _a.On && _a.Calls > 0;

    static Color DotColor(Phase p) => p == Phase.Working ? Ui.Accent : Ui.Good;

    static string Short(string s) => s.Length <= WorkLength ? s : s[..(WorkLength - 1)] + "…";

    void Sync()
    {
        if (!GodotObject.IsInstanceValid(_pill)) return;
        _pill.Visible = Shown;
        if (!Shown) { if (_pop != null && GodotObject.IsInstanceValid(_pop)) _pop.Hide(); return; }
        var phase = Now;
        _dot.Modulate = DotColor(phase);
        _state.Text = phase == Phase.Working ? Short(_a.Working!) : "";
        _state.Visible = _state.Text.Length > 0;
        _state.AddThemeColorOverride("font_color", phase == Phase.Working ? Ui.AccentInk : Ui.Muted);
        _pill.TooltipText = Summary(phase);
        Pulse(phase == Phase.Working);
        Callable.From(Fit).CallDeferred();
        if (_pop != null && GodotObject.IsInstanceValid(_pop) && _pop.Visible) Fill();
    }

    void Fit()
    {
        if (GodotObject.IsInstanceValid(_pill)) _pill.CustomMinimumSize = new Vector2(_row.GetCombinedMinimumSize().X + PillPadding, PillHeight);
    }

    void Pulse(bool on)
    {
        if (on == (_pulse != null && _pulse.IsValid())) return;
        _pulse?.Kill();
        _pulse = null;
        if (!on) return;
        _pulse = _dot.CreateTween().SetLoops();
        _pulse.TweenProperty(_dot, "modulate:a", PulseLow, PulseSeconds);
        _pulse.TweenProperty(_dot, "modulate:a", 1f, PulseSeconds);
    }

    string Summary(Phase phase) => phase == Phase.Working
        ? "Working: " + _a.Working
        : $"Connected · {Calls(_a.Calls)}{(_a.LastCallAt is { } t ? " · last " + Ago(t) : "")}";

    static string Calls(int n) => n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " call" : " calls");

    static string Ago(DateTime at)
    {
        var s = (DateTime.UtcNow - at).TotalSeconds;
        return s < 10 ? "just now" : s < 60 ? $"{(int)s} s ago" : s < 3600 ? $"{(int)(s / 60)} min ago" : $"{(int)(s / 3600)} h ago";
    }

    void Open()
    {
        if (_pop == null || !GodotObject.IsInstanceValid(_pop))
        {
            _pop = new PopupPanel();
            _list = new VBoxContainer { CustomMinimumSize = new Vector2(ListWidth, 0) };
            _list.AddThemeConstantOverride("separation", Ui.Gap.Row);
            _pop.AddChild(_list);
            _pill.AddChild(_pop);
        }
        Fill();
        _pop.Size = Vector2I.Zero;
        var at = _pill.GetGlobalRect();
        _pop.Popup(new Rect2I((int)(at.End.X - ListWidth - 20), (int)(at.End.Y + 4), 0, 0));
    }

    void Fill()
    {
        foreach (var c in _list!.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        var phase = Now;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Gap.Group);
        head.AddChild(new TextureRect
        {
            Texture = Ui.Dot, CustomMinimumSize = new Vector2(DotSize, DotSize), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = DotColor(phase), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        var title = new Label { Text = "AI agent" };
        title.AddThemeFontOverride("font", Ui.SansStrong());
        head.AddChild(title);
        _list.AddChild(head);
        var state = new Label { Text = Summary(phase), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(ListWidth, 0) };
        state.AddThemeFontSizeOverride("font_size", 13);
        state.AddThemeColorOverride("font_color", phase == Phase.Working ? Ui.AccentInk : Ui.Muted);
        _list.AddChild(state);
        if (_a.Recent.Count > 0)
        {
            _list.AddChild(new HSeparator());
            foreach (var (when, what) in _a.Recent) _list.AddChild(Line(what, Ago(when)));
        }
        _list.AddChild(new HSeparator());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Gap.Group);
        actions.AddChild(Ui.Btn("Settings…", () => { _pop!.Hide(); _settings(); }, "Connect an agent, or the technical settings"));
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var off = Ui.Btn("Switch off", () => { _pop!.Hide(); _a.TurnOff(); }, "Your agent can no longer use the app until you connect again");
        Ui.MakeQuiet(off);
        actions.AddChild(off);
        _list.AddChild(actions);
    }

    static HBoxContainer Line(string what, string when)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var w = new Label { Text = what, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = what };
        w.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(w);
        var t = new Label { Text = when };
        t.AddThemeFontSizeOverride("font_size", 12);
        t.AddThemeColorOverride("font_color", Ui.Faint);
        row.AddChild(t);
        return row;
    }
}
