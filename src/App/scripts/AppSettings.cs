using System;
using Godot;

namespace KTable.App;

static class AppSettings
{
    public static void AddSound(VBoxContainer box, Settings cfg)
    {
        var vol = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = cfg.SoundVolume, CustomMinimumSize = new Vector2(220, 0) };
        var volL = new Label { Text = $"{cfg.SoundVolume * 100:0}%", CustomMinimumSize = new Vector2(48, 0) };
        vol.ValueChanged += v => { volL.Text = $"{v * 100:0}%"; cfg.SoundVolume = (float)v; Sfx.Current?.Apply(); };
        vol.DragEnded += _ => cfg.Save();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = "Volume", CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(vol);
        row.AddChild(volL);
        box.AddChild(row);
        var mute = new CheckBox { Text = "Silent", ButtonPressed = cfg.SoundMuted, FocusMode = Control.FocusModeEnum.None };
        mute.Toggled += on => { cfg.SoundMuted = on; cfg.Save(); Sfx.Current?.Apply(); };
        box.AddChild(mute);
    }

    public sealed record SizeControls(HSlider Size, Label Shown, CheckBox Match, Label Capped);

    public static SizeControls SizeSlider(Settings cfg, Action? relaid = null)
    {
        var size = new HSlider { MinValue = Settings.UiScaleMin, MaxValue = Settings.UiScaleMax, Step = 0.05, Value = cfg.UiScale };
        var shown = new Label { Text = Percent(cfg.UiScale) };
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
        size.ValueChanged += v => { shown.Text = Percent((float)v); if (!Input.IsMouseButtonPressed(MouseButton.Left)) Chosen(); };
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
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = "Interface size", CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(c.Size);
        row.AddChild(c.Shown);
        box.AddChild(row);
        var matchAt = new MarginContainer();
        matchAt.AddThemeConstantOverride("margin_left", 118);
        c.Match.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        matchAt.AddChild(c.Match);
        box.AddChild(matchAt);
        box.AddChild(c.Capped);
    }

    public static void OpenWindow(Node parent, Settings cfg)
    {
        var dlg = new AcceptDialog { Title = "Settings", OkButtonText = "Done" };
        var pages = new DialogPages("app-settings", new Vector2(620, 420));
        AddSize(pages.Add("Accessibility"), cfg);
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
    readonly Settings _cfg;

    public SettingsPage(TitleScreen t, Settings cfg) { _t = t; _cfg = cfg; }

    public void Show()
    {
        var v = _t.NewPage("Settings", "");
        v.AddThemeConstantOverride("separation", 10);
        v.AddChild(Ui.Header("Accessibility"));
        var access = new VBoxContainer();
        v.AddChild(access);
        AppSettings.AddSize(access, _cfg);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        v.AddChild(Ui.Header("Sound"));
        var sound = new VBoxContainer();
        v.AddChild(sound);
        AppSettings.AddSound(sound, _cfg);
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
}
