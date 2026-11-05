using System;
using Godot;
using KTable.Client;

namespace KTable.App;

static class AppSettings
{
    public const int LabelWidth = 124, RowGap = 8;

    const int LeastPort = 1024, MostPort = 65535, PortFieldWidth = 100;

    public static void AddPort(VBoxContainer box, Settings cfg)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", RowGap);
        row.AddChild(new Label { Text = "Port", CustomMinimumSize = new Vector2(LabelWidth, 0) });
        var port = new LineEdit
        {
            Text = cfg.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(PortFieldWidth, 0),
            TooltipText = "The UDP port people connect to when you invite them; Invite shows it with your address",
        };
        port.TextChanged += t =>
        {
            if (!int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) || p < LeastPort || p > MostPort) return;
            cfg.Port = p;
            cfg.Save();
        };
        row.AddChild(port);
        box.AddChild(row);
    }

    public static void AddSound(VBoxContainer box, Settings cfg)
    {
        var vol = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = cfg.SoundVolume, CustomMinimumSize = new Vector2(220, 0) };
        var volL = Ui.ValueBox(vol, v => $"{v * 100:0}%", 48);
        vol.ValueChanged += v =>
        {
            cfg.SoundVolume = (float)v;
            Sfx.Current?.Apply();
        };
        vol.DragEnded += _ => cfg.Save();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", RowGap);
        row.AddChild(new Label { Text = "Volume", CustomMinimumSize = new Vector2(LabelWidth, 0) });
        row.AddChild(vol);
        row.AddChild(volL);
        box.AddChild(row);
        var mute = new CheckBox { Text = "Silent", ButtonPressed = cfg.SoundMuted, FocusMode = Control.FocusModeEnum.None };
        mute.Toggled += on =>
        {
            cfg.SoundMuted = on;
            cfg.Save();
            Sfx.Current?.Apply();
        };
        var muteAt = new MarginContainer();
        muteAt.AddThemeConstantOverride("margin_left", LabelWidth + RowGap);
        mute.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        muteAt.AddChild(mute);
        box.AddChild(muteAt);
    }

    public sealed record SizeControls(HSlider Size, LineEdit Shown, CheckBox Match, Label Capped);

    public static SizeControls SizeSlider(Settings cfg, Action? relaid = null)
    {
        var size = new HSlider { MinValue = Settings.UiScaleMin, MaxValue = Settings.UiScaleMax, Step = 0.05, Value = cfg.UiScale };
        var shown = Ui.ValueBox(size, v => Percent((float)v), 0);
        var match = new CheckBox { Text = "Match Windows", ButtonPressed = cfg.FollowsWindows, TooltipText = "Windows' own display scale, screen by screen" };
        var capped = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        capped.AddThemeColorOverride("font_color", Ui.Muted);
        Window? Root() => size.IsInsideTree() ? size.GetTree().Root : null;
        void Sync()
        {
            if (Root() is not { } root) return;
            float wanted = Ui.WantedScale(root, cfg);
            size.SetValueNoSignal(wanted);
            shown.Text = Percent(wanted);
            float fits = Ui.ScaleThatFits(root, wanted);
            capped.Visible = fits < wanted - 0.01f;
            capped.Text = $"This window holds up to {Percent(fits)}; a larger window or screen allows more.";
        }
        void Use()
        {
            if (Root() is not { } root) return;
            cfg.Save();
            Ui.ApplyScale(root, cfg);
            Sync();
            relaid?.Invoke();
        }
        void Chosen()
        {
            if (Root() is not { } root || Mathf.IsEqualApprox(Ui.WantedScale(root, cfg), (float)size.Value)) return;
            cfg.FollowsWindows = false;
            cfg.UiScale = (float)size.Value;
            match.SetPressedNoSignal(false);
            Use();
        }
        size.ValueChanged += _ => { if (!Input.IsMouseButtonPressed(MouseButton.Left)) Chosen(); };
        size.DragEnded += _ => Chosen();
        match.Toggled += on =>
        {
            if (Root() is { } root && !on) cfg.UiScale = Ui.WantedScale(root, cfg);
            cfg.FollowsWindows = on;
            Use();
        };
        size.TreeEntered += () => Callable.From(Sync).CallDeferred();
        return new SizeControls(size, shown, match, capped);
        static string Percent(float v) => $"{v * 100:0}%";
    }

    public static void AddSize(VBoxContainer box, Settings cfg)
    {
        var c = SizeSlider(cfg);
        c.Size.CustomMinimumSize = new Vector2(220, 0);
        c.Shown.CustomMinimumSize = new Vector2(48, 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", RowGap);
        row.AddChild(new Label { Text = "Interface size", CustomMinimumSize = new Vector2(LabelWidth, 0) });
        row.AddChild(c.Size);
        row.AddChild(c.Shown);
        box.AddChild(row);
        var matchAt = new MarginContainer();
        matchAt.AddThemeConstantOverride("margin_left", LabelWidth + RowGap);
        c.Match.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        matchAt.AddChild(c.Match);
        box.AddChild(matchAt);
        box.AddChild(c.Capped);
    }

    public static void OpenWindow(Node parent, Settings cfg)
    {
        var dlg = new AcceptDialog { Title = "Settings", OkButtonText = "Done" };
        var pages = new DialogPages("app-settings", new Vector2(620, 420));
        AddSize(pages.Add("Interface"), cfg);
        AddSound(pages.Add("Sound"), cfg);
        if (AgentEndpoint.Current is { } agent) AgentPanel.Simple(pages.Add("AI agent"), agent, cfg);
        pages.ShowFirst();
        dlg.AddChild(pages.Root);
        dlg.Confirmed += cfg.Save;
        dlg.CloseRequested += cfg.Save;
        Ui.Open(parent, dlg, new Vector2I(660, 500));
        Ui.FreeOnClose(dlg);
    }
}

sealed class SettingsPage
{
    readonly TitleScreen _t;
    readonly Main _main;
    readonly Settings _cfg;

    public SettingsPage(TitleScreen t, Main main, Settings cfg)
    {
        _t = t;
        _main = main;
        _cfg = cfg;
    }

    public void Show()
    {
        var v = _t.NewPage("Settings", "");
        v.AddThemeConstantOverride("separation", 10);
        v.AddChild(Ui.Header("Interface"));
        var access = new VBoxContainer();
        v.AddChild(access);
        AppSettings.AddSize(access, _cfg);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        v.AddChild(Ui.Header("Sound"));
        var sound = new VBoxContainer();
        v.AddChild(sound);
        AppSettings.AddSound(sound, _cfg);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        v.AddChild(Ui.Header("Inviting"));
        var inviting = new VBoxContainer();
        v.AddChild(inviting);
        AppSettings.AddPort(inviting, _cfg);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        v.AddChild(Ui.Header("Storage"));
        var shelf = new CacheShelf(_t.Root!, said => _t.Status.Text = said, Busy);
        foreach (var c in Caches.All) v.AddChild(shelf.Row(c));
        if (_t.OffersUpdates)
        {
            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
            v.AddChild(Ui.Header("Updates"));
            var check = new CheckBox { Text = "Check GitHub for a new version at start", ButtonPressed = _cfg.CheckUpdates, FocusMode = Control.FocusModeEnum.None };
            check.Toggled += on => { _cfg.CheckUpdates = on; _cfg.Save(); };
            v.AddChild(check);
        }
        if (AgentEndpoint.Current is not { } agent) return;
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        v.AddChild(Ui.Header("AI agent"));
        var box = new VBoxContainer();
        v.AddChild(box);
        AgentPanel.Simple(box, agent, _cfg);
    }

    string? Busy(CacheKind kind) => kind switch
    {
        CacheKind.TtsDownloads when ConverterTool.Running => "An import is running. Clear its downloads once it ends.",
        _ when _main.OpeningTitle is { } opening => $"{opening} is opening. Clear once it is open or stopped.",
        _ => null,
    };
}
