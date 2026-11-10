using System;
using Godot;
using KTable.Client;

namespace KTable.App;

static class AppSettings
{
    const int LeastPort = 1024, MostPort = 65535, PortFieldWidth = 100;

    public static void AddPort(VBoxContainer box, Settings cfg)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(Ui.RowLabel("Port", Ui.DialogLabels));
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
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(Ui.RowLabel("Volume", Ui.DialogLabels));
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
        box.AddChild(Ui.UnderControls(mute, Ui.DialogLabels));
    }

    public sealed record SizeControls(HSlider Size, LineEdit Shown, Label Capped);

    public static SizeControls SizeSlider(Settings cfg, Action? relaid = null)
    {
        var size = new HSlider { MinValue = Settings.UiScaleMin, MaxValue = Settings.UiScaleMax, Step = 0.05, Value = cfg.UiScale };
        var shown = Ui.ValueBox(size, v => Percent((float)v), 0);
        var capped = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        capped.AddThemeColorOverride("font_color", Ui.Muted);
        Window? Root() => size.IsInsideTree() ? size.GetTree().Root : null;
        void Sync()
        {
            if (Root() is not { } root) return;
            size.SetValueNoSignal(cfg.UiScale);
            shown.Text = Percent(cfg.UiScale);
            float wanted = Ui.WantedScale(root, cfg), fits = Ui.ScaleThatFits(root, wanted);
            capped.Visible = fits < wanted - 0.01f;
            capped.Text = $"This window holds up to {Percent(fits / Ui.Fitted(root))}; a larger window or screen allows more.";
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
            if (Mathf.IsEqualApprox(cfg.UiScale, (float)size.Value)) return;
            cfg.UiScale = (float)size.Value;
            Use();
        }
        size.ValueChanged += _ => { if (!Input.IsMouseButtonPressed(MouseButton.Left)) Chosen(); };
        size.DragEnded += _ => Chosen();
        size.TreeEntered += () => Callable.From(Sync).CallDeferred();
        return new SizeControls(size, shown, capped);
        static string Percent(float v) => $"{v * 100:0}%";
    }

    public static void AddSize(VBoxContainer box, Settings cfg)
    {
        var c = SizeSlider(cfg);
        c.Size.CustomMinimumSize = new Vector2(220, 0);
        c.Shown.CustomMinimumSize = new Vector2(48, 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(Ui.RowLabel("Interface size", Ui.DialogLabels));
        row.AddChild(c.Size);
        row.AddChild(c.Shown);
        box.AddChild(row);
        box.AddChild(Ui.UnderControls(c.Capped, Ui.DialogLabels));
    }

    public static OptionButton WindowPick(Settings cfg)
    {
        var pick = new OptionButton
        {
            TooltipText = $"Borderless fills the screen as a window; Fullscreen takes it over. {cfg.Keys.Label("fullscreen")} switches between the window and the screen.",
            CustomMinimumSize = new Vector2(160, 0),
        };
        foreach (var n in new[] { "Windowed", "Borderless", "Fullscreen" }) pick.AddItem(n);
        void Sync() => pick.Selected = Math.Clamp(cfg.WindowMode, Settings.Windowed, Settings.Fullscreen);
        Sync();
        pick.TreeEntered += () => { Sync(); Graphics.WindowChosen += Sync; };
        pick.TreeExiting += () => Graphics.WindowChosen -= Sync;
        pick.ItemSelected += i =>
        {
            cfg.WindowMode = (int)i;
            cfg.Save();
            Graphics.ApplyWindow(cfg);
        };
        return pick;
    }

    public static void AddWindow(VBoxContainer box, Settings cfg)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(Ui.RowLabel("Window", Ui.DialogLabels));
        row.AddChild(WindowPick(cfg));
        box.AddChild(row);
    }

    public static void AddNewScenarios(VBoxContainer box, Settings cfg)
    {
        var form = Ui.Form(Ui.DialogLabels);
        var backdrop = new BackdropPick(cfg.NewBackdrop, own: false, editable: true);
        backdrop.Pick.ItemSelected += i => { cfg.NewBackdrop = BackdropPick.Chosen(i)?.Id ?? backdrop.Hex; cfg.Save(); };
        Ui.WhenPicked(backdrop.Colour, c => { cfg.NewBackdrop = "#" + c.ToHtml(false); cfg.Save(); });
        form.AddChild(new Label { Text = "Backdrop" });
        form.AddChild(backdrop.Row());
        var surface = new HSlider { MinValue = 0, MaxValue = KTable.Core.Model.Lighting.SeeThrough, Step = 0.05, Value = cfg.NewTableShade, TooltipText = ScenarioDialog.SurfaceTip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(170, 0) };
        surface.ValueChanged += v => cfg.NewTableShade = Math.Round(v, 2);
        surface.DragEnded += _ => cfg.Save();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(surface);
        row.AddChild(Ui.ValueBox(surface, ScenarioDialog.ShadeWord, 58));
        form.AddChild(new Label { Text = "Surface" });
        form.AddChild(row);
        Ui.AlignForm(form);
        box.AddChild(form);
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
        var page = _t.NewPage("Settings", "");
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        page.AddChild(scroll);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", Ui.Gap.Block);
        scroll.AddChild(v);
        v.AddChild(Ui.Header("Interface"));
        var access = new VBoxContainer();
        v.AddChild(access);
        AppSettings.AddSize(access, _cfg);
        AppSettings.AddWindow(access, _cfg);
        access.AddChild(Ui.UnderControls(Ui.Btn("Show the tour again", _t.TourAgain, "The first-run tour of the title screen and the table, ending in Help"), Ui.DialogLabels));
        v.AddChild(Ui.Header("Sound"));
        var sound = new VBoxContainer();
        v.AddChild(sound);
        AppSettings.AddSound(sound, _cfg);
        v.AddChild(Ui.Header("New scenarios"));
        var fresh = new VBoxContainer();
        v.AddChild(fresh);
        AppSettings.AddNewScenarios(fresh, _cfg);
        v.AddChild(Ui.Header("Inviting"));
        var inviting = new VBoxContainer();
        v.AddChild(inviting);
        AppSettings.AddPort(inviting, _cfg);
        v.AddChild(Ui.Header("Storage"));
        var shelf = new CacheShelf(_t.Root!, said => _t.Status.Text = said, Busy);
        foreach (var c in Caches.All) v.AddChild(shelf.Row(c));
        if (_t.OffersUpdates)
        {
            v.AddChild(Ui.Header("Updates"));
            var check = new CheckBox { Text = "Check GitHub for a new version at start", ButtonPressed = _cfg.CheckUpdates, FocusMode = Control.FocusModeEnum.None };
            check.Toggled += on => { _cfg.CheckUpdates = on; _cfg.Save(); };
            v.AddChild(check);
        }
        if (AgentEndpoint.Current is not { } agent) return;
        v.AddChild(Ui.Header("AI agent"));
        var box = new VBoxContainer();
        v.AddChild(box);
        AgentPanel.Simple(box, agent, _cfg);
    }

    string? Busy(CacheKind kind) => kind switch
    {
        CacheKind.ModuleDownloads when ConverterTool.Running => "A conversion is running. Clear its downloads once it ends.",
        _ when _main.OpeningTitle is { } opening => $"{opening} is opening. Clear once it is open or stopped.",
        _ => null,
    };
}
