using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class TitleScreen
{
    readonly Main _main;
    readonly Settings _cfg;
    readonly IPlatform _platform;
    readonly List<Button> _nav = new();
    Control _page = null!;

    public readonly FolderView Folders;
    public readonly ItemMenu Items;
    public readonly HostPage Host;
    public readonly JoinPage Join;
    public readonly PacksPage Packs;
    public readonly TrashPage Trash;
    public readonly WorkshopPage Workshop;
    public readonly SettingsPage SettingsPage;
    public readonly ReportPage Report;

    public Control? Root { get; private set; }
    public Label Status { get; private set; } = null!;
    public LineEdit PlayerName { get; private set; } = null!;

    public TitleScreen(Main main, Settings cfg, Library lib, IPlatform platform)
    {
        _main = main;
        _cfg = cfg;
        _platform = platform;
        Folders = new FolderView(main, lib);
        Items = new ItemMenu(this, main, platform);
        Host = new HostPage(this, main);
        Join = new JoinPage(this, main, cfg);
        Packs = new PacksPage(this, main);
        Trash = new TrashPage(this, main);
        Workshop = new WorkshopPage(this, main, platform);
        SettingsPage = new SettingsPage(this, main, cfg);
        Report = new ReportPage(this);
    }

    public bool HasWorkshop => _platform.HasWorkshop;

    public bool OffersUpdates => UpdateOffer.Applies(_platform);

    public const string NoName = "Unnamed";
    public string HostName => PlayerName.Text.Trim().Length > 0 ? PlayerName.Text.Trim() : NoName;

    public void Close()
    {
        Root?.QueueFree();
        Root = null;
    }

    public void Build()
    {
        var root = new Control { Theme = LobbyTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root = root;
        _main.AddChild(root);

        root.AddChild(new ColorRect { Color = Ui.Bar.Darkened(0.2f), AnchorRight = 1, AnchorBottom = 1 });
        var store = new PackageStore(PackageStore.DefaultRoot);
        var (rebuilt, lost) = ScenarioFiles.RebuildMissing(store, Main.Saves);
        var scan = System.Diagnostics.Stopwatch.StartNew();
        ScenarioCards.WaitForEarlyRead();
        var scenarios = store.ListScenarios().Select(sc => (sc.id, sc.path, s: ScenarioCards.Card(sc.path).s)).ToList();
        GD.Print($"[lobby] read {scenarios.Count} scenario file(s) in {scan.ElapsedMilliseconds} ms");
        if (scenarios.FirstOrDefault(x => x.s?.Sheets.Count > 0) is var first && first.s != null && ScenarioCards.Thumb(store, first.s, 1400) is { } backdrop)
            root.AddChild(Backdrop(backdrop));
        var fade = new TextureRect { AnchorRight = 1, AnchorBottom = 1, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        var grad =
            new Gradient { Offsets = new[] { 0f, 0.42f, 1f },
                Colors = new[] { new Color(0.052f, 0.049f, 0.044f, 0.98f), new Color(0.052f, 0.049f, 0.044f, 0.75f), new Color(0.052f, 0.049f, 0.044f, 0.35f) } };
        fade.Texture = new GradientTexture2D { Gradient = grad, Width = 256, Height = 4 };
        root.AddChild(fade);

        var leftRoom =
            new ScrollContainer { OffsetLeft = 72, OffsetTop = 64, OffsetRight = 72 + 360, AnchorBottom = 1, OffsetBottom = -48, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        leftRoom.AddChild(left);
        root.AddChild(leftRoom);
        Menu(left, store, scenarios);
        Identity(left, rebuilt, lost);

        _page = new MarginContainer { AnchorLeft = 0, AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 72 + 360 + 56, OffsetTop = 64, OffsetRight = -72, OffsetBottom = -48 };
        root.AddChild(_page);
        if (Report.TellOfUnexpectedClose()) return;
        Select(0);
        Host.Show(store, scenarios);
        Tour.AtTitle(root, _cfg, text => _nav.Find(b => b.Text == text));
    }

    public void TourAgain()
    {
        Tour.Restart(_cfg);
        if (Root != null) Tour.AtTitle(Root, _cfg, text => _nav.Find(b => b.Text == text));
    }

    static TextureRect Backdrop(ImageTexture map)
    {
        var full = map.GetImage();
        int fw = full.GetWidth(), fh = full.GetHeight();
        var img = full.GetRegion(new Rect2I(fw * 15 / 100, fh * 15 / 100, fw * 70 / 100, fh * 70 / 100));
        int iw = img.GetWidth(), ih = img.GetHeight();
        for (int y = 0; y < ih; y++) for (int x = 0; x < iw; x++)
        {
            var c = img.GetPixel(x, y);
            var l = c.Luminance;
            float k = l > 0.45f ? (0.45f + (l - 0.45f) * 0.25f) / Mathf.Max(l, 0.001f) : 1f;
            float ex = (x + 0.5f) / iw * 2 - 1, ey = (y + 0.5f) / ih * 2 - 1;
            float edge = 1f - 0.45f * Mathf.Clamp((ex * ex + ey * ey - 0.35f) / 0.9f, 0f, 1f);
            img.SetPixel(x, y, new Color(l, l * 0.97f, l * 0.9f).Lerp(c, 0.25f) * (0.42f * k * edge));
        }
        return new TextureRect { Texture = ImageTexture.CreateFromImage(img), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            AnchorRight = 1, AnchorBottom = 1 };
    }

    void Menu(VBoxContainer left, PackageStore store, List<(string id, string path, Scenario? s)> scenarios)
    {
        var title = new Label { Text = "Kriegsspiel\nTable" };
        title.AddThemeFontOverride("font", Serif());
        title.AddThemeFontSizeOverride("font_size", 54);
        title.AddThemeConstantOverride("line_spacing", -13);
        left.AddChild(title);
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        left.AddChild(new ColorRect { Color = Brass, CustomMinimumSize = new Vector2(48, 2), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin });
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 36) });

        _nav.Clear();
        NavItem(left, "Scenarios", () => Host.Show(store, scenarios));
        NavItem(left, "Join a game", Join.Show);
        NavItem(left, "Piece studio", Packs.Show);
        if (HasWorkshop) NavItem(left, "Steam Workshop", Workshop.Show);
        var help = NavItem(left, "Help", () => GuideReader.Show(_main), page: false);
        help.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventKey { Keycode = Key.F1 } } };
        help.TooltipText = "The guides (F1)";
        NavItem(left, "Settings", SettingsPage.Show);
        NavItem(left, "Quit", _main.QuitApp);
    }

    void Identity(VBoxContainer left, List<string> rebuilt, List<string> lost)
    {
        left.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var slot = new VBoxContainer();
        var slotPad = new MarginContainer();
        slotPad.AddThemeConstantOverride("margin_bottom", 20);
        slotPad.AddChild(slot);
        left.AddChild(slotPad);
        _main.PutOpeningIn(slot);
        var who = new Label { Text = "YOUR NAME", Modulate = Muted };
        who.AddThemeFontSizeOverride("font_size", 11);
        left.AddChild(who);
        PlayerName = new LineEdit { Text = _cfg.LastName, PlaceholderText = "your name", CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        PlayerName.TextChanged += t => _cfg.LastName = t;
        left.AddChild(PlayerName);
        Status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = Ui.AccentInk, CustomMinimumSize = new Vector2(320, 0) };
        left.AddChild(Status);
        if (Main.TakePendingNotice() is { } notice) Status.Text = notice;
        foreach (var p in Problems.Take()) Status.Text += (Status.Text.Length > 0 ? "\n" : "") + p;
        var status = Status;
        Action<string> told = p => Callable.From(() => { if (GodotObject.IsInstanceValid(status)) status.Text = p; }).CallDeferred();
        Problems.Told += told;
        status.TreeExiting += () => Problems.Told -= told;
        if (rebuilt.Count > 0)
            Status.Text += (Status.Text.Length > 0 ? "\n" : "") + $"{string.Join(", ", rebuilt)}: the game in play had lost its scenario file, so it was written again from the game's table. Scenarios lists it.";
        if (lost.Count > 0)
            Status.Text += (Status.Text.Length > 0 ? "\n" : "") + $"A game in play has no scenario file and no save of it can be read: {string.Join(", ", lost)} (in {Main.Saves.LiveDir}).";
        if (OffersUpdates) UpdateOffer.Put(left, _main, _cfg);
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var contactRow = new HBoxContainer();
        contactRow.AddThemeConstantOverride("separation", Ui.Gap.None);
        var contact = new Label { Text = "by Firme  ·  Discord b.firme  ·  ", Modulate = Muted };
        contact.AddThemeFontSizeOverride("font_size", 12);
        contactRow.AddChild(contact);
        var report = new LinkButton { Text = "Report a problem", Underline = LinkButton.UnderlineMode.OnHover, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        report.AddThemeFontSizeOverride("font_size", 12);
        report.AddThemeColorOverride("font_color", Muted);
        report.AddThemeColorOverride("font_hover_color", Ink);
        report.Pressed += Report.Show;
        contactRow.AddChild(report);
        left.AddChild(contactRow);
        var ver = new Label { Text = BuildInfo(), Modulate = Ui.Faint };
        ver.AddThemeFontSizeOverride("font_size", 11);
        left.AddChild(ver);
    }

    Button NavItem(VBoxContainer col, string text, Action open, bool page = true)
    {
        var b =
            new Button { Text = text, Flat = false, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, ToggleMode = true, CustomMinimumSize = new Vector2(264, 42),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        b.AddThemeFontOverride("font", SansMedium());
        b.AddThemeFontSizeOverride("font_size", 16);
        b.AddThemeColorOverride("font_color", Muted);
        b.AddThemeColorOverride("font_pressed_color", Ink);
        b.AddThemeColorOverride("font_hover_pressed_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Ink);
        StyleBoxFlat Item(float a) => Ui.Box(new Color(1, 1, 1, a), 6, 16, 8);
        b.AddThemeStyleboxOverride("normal", Item(0));
        b.AddThemeStyleboxOverride("hover", Item(0.05f));
        b.AddThemeStyleboxOverride("pressed", Item(0.10f));
        b.AddThemeStyleboxOverride("hover_pressed", Item(0.12f));
        int idx = _nav.Count;
        b.Pressed += () =>
        {
            if (page) Select(idx);
            else b.SetPressedNoSignal(false);
            open();
        };
        _nav.Add(b);
        col.AddChild(b);
        return b;
    }

    public void Select(int i)
    {
        for (int k = 0; k < _nav.Count; k++) _nav[k].SetPressedNoSignal(k == i);
    }

    public VBoxContainer NewPage(string heading, string sub)
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Paper, ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 30, ContentMarginBottom = 28,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,

        });
        _page.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", Ui.Gap.Block);
        panel.AddChild(v);
        var h = new Label { Text = heading };
        h.AddThemeFontOverride("font", Serif());
        h.AddThemeFontSizeOverride("font_size", 30);
        v.AddChild(h);
        if (sub.Length > 0) v.AddChild(new Label { Text = sub, Modulate = Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var settling = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _page.AddChild(settling);
        _main.GetTree().CreateTimer(0.4).Timeout += () => { if (GodotObject.IsInstanceValid(settling)) settling.QueueFree(); };
        return v;
    }
}
