using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class TitleScreen
{
    const string Wordmark = "res://brand/firmeware.png";
    const string FreshWorld = "saxon-meadow.webp";
    const float WordmarkHeight = 13;
    const int Edge = 28, MenuWidth = 340, MenuInset = 28, PanelGap = 20, PageInset = 40, PageTop = 40, PageFoot = 32, ScenariosAt = 0, PacksAt = 2;
    const int PageLeft = Edge + MenuWidth + PanelGap, NavIcon = 22, NavIconAt = 9, NavTextAt = 40;
    const int NavHeight = 36, TightNavHeight = 30, TitleGap = 28, TightTitleGap = 12, ContinueGap = 10, TightContinueGap = 6, TightContinueHeight = 48;
    const int ContinueHeight = 58, ContinueThumbWidth = 64, ContinueThumbHeight = 40, ThumbRadius = 2;
    const float WorldFadeIn = 0.8f, DriftSeconds = 45, DriftReach = 0.035f, DriftAcross = 0.8f, DriftDown = 0.4f, ContinueLit = 0.12f;
    static readonly Rect2 FreshView = new(0.58f, 0.21f, 0.40f, 0.45f);
    static readonly Vector2I UsualCover = new(1920, 1080);
    internal const string GlideBy = "drift";
    const string GlideCode = """
        shader_type canvas_item;
        uniform vec2 drift;
        void vertex() { VERTEX += drift; }
        """;
    static Shader? _glide;
    static Texture2D? _worldShown;
    static float _driftAt;

    readonly Main _main;
    readonly Settings _cfg;
    readonly IPlatform _platform;
    readonly List<Button> _nav = new();
    readonly List<MenuFit.Give> _gives = new();
    Control _page = null!;
    VBoxContainer _column = null!;
    int _statusAt;
    PackageStore _store = null!;
    List<(string id, string path, Scenario? s)> _scenarios = new();
    int _shown = -1;
    bool _pageOpen;
    public int ImportAt { get; private set; } = -1;
    static bool _still;

    public readonly ShelfView Shelf;
    public readonly ItemMenu Items;
    public readonly HostPage Host;
    public readonly JoinPage Join;
    public readonly PacksPage Packs;
    public readonly TrashPage Trash;
    public readonly WorkshopPage Workshop;
    public readonly SettingsPage SettingsPage;
    public readonly ReportPage Report;

    public Control? Root { get; private set; }
    public StatusLine Status { get; private set; } = null!;
    public LineEdit PlayerName { get; private set; } = null!;

    public TitleScreen(Main main, Settings cfg, Library lib, IPlatform platform)
    {
        _main = main;
        _cfg = cfg;
        _platform = platform;
        Shelf = new ShelfView(main, lib, cfg);
        Items = new ItemMenu(this, main, platform);
        Host = new HostPage(this, main);
        Join = new JoinPage(this, main, cfg);
        Packs = new PacksPage(this, main);
        Trash = new TrashPage(this, main);
        Workshop = new WorkshopPage(this, main, platform);
        SettingsPage = new SettingsPage(this, main, cfg);
        Report = new ReportPage(this, main, cfg);
    }

    internal static void HoldStill() => _still = true;

    public bool HasWorkshop => _platform.HasWorkshop;

    public bool OffersUpdates => UpdateOffer.Applies(_platform);

    public const string NoName = "Unnamed";
    public string HostName => PlayerName.Text.Trim().Length > 0 ? PlayerName.Text.Trim() : NoName;

    internal static LineEdit NameEdit(Settings cfg)
    {
        var edit = new LineEdit { Text = cfg.LastName, CustomMinimumSize = Ui.Px(ColumnWidth, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        edit.TextChanged += t => cfg.LastName = t;
        edit.TextSubmitted += _ => cfg.Save();
        edit.FocusExited += cfg.Save;
        return edit;
    }

    public void Close(bool keepPicture = false)
    {
        if (!keepPicture) _worldShown = null;
        Root?.QueueFree();
        Root = null;
    }

    sealed record LastGame(string Target, string Name, DateTime At, Scenario? Scenario, string? Folder);

    public void Build()
    {
        var root = new Control { Theme = LobbyTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root = root;
        _main.AddChild(root);
        bool wasImport = _pageOpen && _shown >= 0 && _shown == ImportAt;
        _shown = -1;

        root.AddChild(new ColorRect { Color = Ui.Bar.Darkened(0.2f), AnchorRight = 1, AnchorBottom = 1 });
        var store = new PackageStore(PackageStore.DefaultRoot);
        _store = store;
        var (rebuilt, lost) = ScenarioFiles.RebuildMissing(store, Main.Saves);
        var scan = System.Diagnostics.Stopwatch.StartNew();
        ScenarioCards.WaitForEarlyRead();
        var scenarios = store.ListScenarios().Select(sc => (sc.id, sc.path, s: ScenarioCards.Card(sc.path).s)).ToList();
        _scenarios = scenarios;
        GD.Print($"[lobby] read {scenarios.Count} scenario file(s) in {scan.ElapsedMilliseconds} ms");
        var last = Latest(scenarios);
        root.AddChild(World(root, store, last, scenarios));

        var menu =
            new PanelContainer { OffsetLeft = Ui.Px(Edge), OffsetTop = Ui.Px(Edge), OffsetRight = Ui.Px(Edge + MenuWidth), AnchorBottom = 1, OffsetBottom = -Ui.Px(Edge) };
        menu.AddThemeStyleboxOverride("panel", Floating(MenuInset, MenuInset));
        root.AddChild(menu);
        var leftRoom = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        menu.AddChild(leftRoom);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        leftRoom.AddChild(left);
        _column = left;
        _gives.Clear();
        Menu(left, store, scenarios, last);
        Identity(left, rebuilt, lost);
        MenuFit.Keep(leftRoom, left, _gives.ToArray());

        _page = new MarginContainer { AnchorRight = 1, AnchorBottom = 1, OffsetLeft = Ui.Px(PageLeft), Visible = false };
        _page.AddThemeConstantOverride("margin_top", Ui.Px(Edge));
        _page.AddThemeConstantOverride("margin_right", Ui.Px(Edge));
        _page.AddThemeConstantOverride("margin_bottom", Ui.Px(Edge));
        root.AddChild(_page);
        if (Report.TellOfUnexpectedClose()) return;
        if (wasImport) ImportDoor.Open(_main, keepLog: true);
        else if (_pageOpen) ShowScenarios();
        else Home();
        Tour.AtTitle(root, _cfg, text => _nav.Find(b => b.Text == text));
    }

    public void TourAgain()
    {
        Tour.Restart(_cfg);
        if (Root != null) Tour.AtTitle(Root, _cfg, text => _nav.Find(b => b.Text == text));
    }

    static LastGame? Latest(List<(string id, string path, Scenario? s)> scenarios)
    {
        var games = new List<LastGame>();
        foreach (var (id, path, s) in scenarios)
        {
            var live = Main.AutosavePath(id);
            if (Main.Saves.HasGame(id) && File.Exists(live)) games.Add(new LastGame(id, s?.Name ?? id, File.GetLastWriteTime(live), s, Path.GetDirectoryName(path)));
        }
        foreach (var mp in Main.Saves.Mirrors())
        {
            var save = ScenarioCards.Save(mp);
            if (save.Error != null) continue;
            var (_, path, s) = scenarios.FirstOrDefault(x => x.s != null && x.s.Id == save.ScenarioId);
            games.Add(new LastGame(mp, save.Name ?? Path.GetFileNameWithoutExtension(mp), File.GetLastWriteTime(mp), s, path != null ? Path.GetDirectoryName(path) : null));
        }
        return games.MaxBy(g => g.At);
    }

    Control World(Control root, PackageStore store, LastGame? last, List<(string id, string path, Scenario? s)> scenarios)
    {
        var stage = new Control { AnchorRight = 1, AnchorBottom = 1, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        TextureRect Picture(Texture2D? texture) => new()
        {
            AnchorLeft = -DriftReach, AnchorTop = -DriftReach, AnchorRight = 1 + DriftReach, AnchorBottom = 1 + DriftReach, Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = texture != null ? Colors.White : Colors.Transparent
        };
        var before = _still ? null : Picture(_worldShown);
        if (before != null) stage.AddChild(before);
        var world = Picture(null);
        stage.AddChild(world);
        if (!_still) Glide(root, world, before, _driftAt);
        (Scenario? s, string? folder) shown = last?.Scenario != null
            ? (last.Scenario, last.Folder)
            : scenarios.Where(x => x.s != null).OrderByDescending(x => File.GetLastWriteTime(x.path)).Select(x => (x.s, Path.GetDirectoryName(x.path))).FirstOrDefault();
        var fresh = TableBackdrop.BuiltInPath(FreshWorld);
        var cover = Cover();
        Image? Drawn() => Covered((shown.s != null ? ScenarioCards.Covering(store, shown.s, cover, shown.folder) : null) ?? Fresh(fresh), cover);
        if (_still)
        {
            Arrived(world, before, Drawn());
            return stage;
        }
        Task.Run(Drawn)
            .ContinueWith(t =>
            {
                if (Assets.Quitting) return;
                var img = t.IsCompletedSuccessfully ? t.Result : null;
                Callable.From(() => Arrived(world, before, img)).CallDeferred();
            });
        return stage;
    }

    static Image? Fresh(string path)
    {
        try
        {
            if (!File.Exists(path) || Image.LoadFromFile(path) is not { } pano || pano.IsEmpty()) return null;
            int w = pano.GetWidth(), h = pano.GetHeight();
            var view = pano.GetRegion(new Rect2I((int)(w * FreshView.Position.X), (int)(h * FreshView.Position.Y), (int)(w * FreshView.Size.X), (int)(h * FreshView.Size.Y)));
            pano.Dispose();
            return view;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PrintErr("[lobby] no picture for the title: " + e.Message); return null; }
    }

    static Vector2I Cover()
    {
        var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        var window = DisplayServer.WindowGetSize();
        var most = new Vector2I(Math.Max(screen.X, window.X), Math.Max(screen.Y, window.Y));
        if (most.X <= 0 || most.Y <= 0) most = UsualCover;
        return (Vector2I)((Vector2)most * (1 + 2 * DriftReach)).Ceil();
    }

    internal static Image? Covered(Image? img, Vector2I cover)
    {
        if (img == null || img.IsEmpty()) return img;
        float scale = Math.Max(cover.X / (float)img.GetWidth(), cover.Y / (float)img.GetHeight());
        int w = Math.Max(1, Mathf.CeilToInt(img.GetWidth() * scale)), h = Math.Max(1, Mathf.CeilToInt(img.GetHeight() * scale));
        if (w != img.GetWidth() || h != img.GetHeight()) img.Resize(w, h, Image.Interpolation.Lanczos);
        return img;
    }

    static void Arrived(TextureRect world, TextureRect? before, Image? img)
    {
        if (img == null || !GodotObject.IsInstanceValid(world)) return;
        world.Texture = ImageTexture.CreateFromImage(img);
        if (_still)
        {
            world.Modulate = Colors.White;
            return;
        }
        _worldShown = world.Texture;
        var fade = world.CreateTween();
        fade.TweenProperty(world, "modulate", Colors.White, WorldFadeIn);
        if (before != null && GodotObject.IsInstanceValid(before)) fade.Finished += before.QueueFree;
    }

    internal static Vector2 Drift(Vector2 size, float t) => (t * 2 - 1) * DriftReach * new Vector2(DriftAcross * size.X, DriftDown * size.Y);

    internal static void Glide(Control root, TextureRect world, TextureRect? before = null, float from = 0)
    {
        var glide = new ShaderMaterial { Shader = _glide ??= new Shader { Code = GlideCode } };
        world.Material = glide;
        if (before != null) before.Material = glide;
        void Place(float at)
        {
            _driftAt = at % 1;
            glide.SetShaderParameter(GlideBy, Drift(root.Size, (1 - Mathf.Cos(Mathf.Tau * _driftAt)) / 2));
        }
        Place(from);
        root.CreateTween().SetLoops().TweenMethod(Callable.From<float>(Place), from, from + 1, 2 * DriftSeconds);
    }

    void Menu(VBoxContainer left, PackageStore store, List<(string id, string path, Scenario? s)> scenarios, LastGame? last)
    {
        var title = new Label { Text = "Kriegsspiel\nTable", MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        title.AddThemeFontOverride("font", Serif());
        title.AddThemeFontSizeOverride("font_size", Ui.Px(54));
        title.AddThemeConstantOverride("line_spacing", Ui.Px(-13));
        title.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Home();
        };
        left.AddChild(title);
        left.AddChild(Gives(new Control(), TitleGap, TightTitleGap));
        if (last != null)
        {
            left.AddChild(Gives(Continue(store, last), ContinueHeight, TightContinueHeight));
            left.AddChild(Gives(new Control(), ContinueGap, TightContinueGap));
        }

        var nav = new VBoxContainer();
        nav.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        left.AddChild(nav);
        _nav.Clear();
        NavItem(nav, "Scenarios", "map", ShowScenarios);
        NavItem(nav, "Join a game", "people", Join.Show);
        NavItem(nav, "Piece studio", "pieces", Packs.Show);
        if (HasWorkshop) NavItem(nav, "Steam Workshop", "stack", Workshop.Show);
        ImportAt = _nav.Count;
        NavItem(nav, ImportDoor.Name, "import", () => ImportDoor.Open(_main)).TooltipText = ImportDoor.Takes;
        var help = NavItem(nav, "Help", "book", () => GuideReader.Show(_main, place: _pageOpen ? null : PageArea()), page: false);
        help.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventKey { Keycode = Key.F1 } } };
        help.TooltipText = "The guides (F1)";
        NavItem(nav, "Settings", "sliders", SettingsPage.Show);
        NavItem(nav, "Quit", "leave", _main.QuitApp, page: false);
    }

    T Gives<T>(T what, int roomy, int tight) where T : Control
    {
        what.CustomMinimumSize = new Vector2(what.CustomMinimumSize.X, Ui.Px(roomy));
        _gives.Add(new MenuFit.Give(what, roomy, tight));
        return what;
    }

    Button Continue(PackageStore store, LastGame last)
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = HostPage.OpensForYou, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        foreach (var (state, colour) in new[] { ("normal", Brass), ("hover", Brass.Lightened(ContinueLit)), ("pressed", Brass.Darkened(ContinueLit)), ("hover_pressed", Brass.Darkened(ContinueLit / 2)) })
            b.AddThemeStyleboxOverride(state, Ui.Box(colour, ControlRadius, 0, 0));
        var inner = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        inner.AddThemeConstantOverride("margin_left", Ui.Px(10));
        inner.AddThemeConstantOverride("margin_right", Ui.Px(14));
        b.AddChild(inner);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        inner.AddChild(row);
        var thumb = last.Scenario != null ? ScenarioCards.Thumb(store, last.Scenario, ScenarioCards.CardWidth, last.Folder) : null;
        var plate = new Panel { CustomMinimumSize = Ui.Px(ContinueThumbWidth, ContinueThumbHeight), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.OnAccent, 0.18f), ThumbRadius, 0, 0));
        if (thumb != null)
        {
            var pic = new TextureRect { Texture = thumb, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore };
            pic.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            plate.AddChild(pic);
        }
        row.AddChild(plate);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", Ui.Gap.None);
        row.AddChild(words);
        var head = new Label { Text = "Continue", MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeFontOverride("font", Ui.SansStrong());
        head.AddThemeColorOverride("font_color", Ui.OnAccent);
        words.AddChild(head);
        var what = new Label { Text = $"{last.Name} · {Ago(last.At)}", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = Ui.Px(1, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        what.AddThemeFontSizeOverride("font_size", Ui.Px(12));
        what.AddThemeColorOverride("font_color", new Color(Ui.OnAccent, 0.78f));
        words.AddChild(what);
        b.Pressed += () => _main.DoHost(last.Target, true);
        return b;
    }

    void Identity(VBoxContainer left, List<string> rebuilt, List<string> lost)
    {
        left.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        Status = new StatusLine();
        left.AddChild(Status);
        _statusAt = Status.GetIndex();
        var slot = new VBoxContainer();
        var slotPad = new MarginContainer { Visible = false };
        slotPad.AddThemeConstantOverride("margin_bottom", Ui.Px(20));
        slotPad.AddChild(slot);
        slot.ChildEnteredTree += _ => slotPad.Visible = true;
        slot.ChildExitingTree += _ => slotPad.Visible = slot.GetChildCount() > 1;
        left.AddChild(slotPad);
        _main.PutOpeningIn(slot);
        left.AddChild(Ui.HeadLabel("Your name"));
        PlayerName = NameEdit(_cfg);
        left.AddChild(PlayerName);
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
        FirmeMessage.Put(left, _cfg);
        if (OffersUpdates) UpdateOffer.Put(left, _main, _cfg);
        left.AddChild(new Control { CustomMinimumSize = Ui.Px(0, 6) });
        if (ResourceLoader.Load<Texture2D>(Wordmark) is { } mark)
            left.AddChild(new TextureRect { Texture = mark, Modulate = Ui.Faint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                CustomMinimumSize = new Vector2(Ui.Pxf(WordmarkHeight) * mark.GetWidth() / mark.GetHeight(), Ui.Px(WordmarkHeight)), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                TooltipText = "by Firme" });
        left.AddChild(new Control { CustomMinimumSize = Ui.Px(0, 4) });
        var contactRow = new HBoxContainer();
        contactRow.AddThemeConstantOverride("separation", Ui.Gap.None);
        var contact = new Label { Text = "Discord b.firme  ·  ", Modulate = Muted };
        contact.AddThemeFontSizeOverride("font_size", Ui.Px(12));
        contactRow.AddChild(contact);
        var report = new LinkButton { Text = "Report a problem", Underline = LinkButton.UnderlineMode.OnHover, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        report.AddThemeFontSizeOverride("font_size", Ui.Px(12));
        report.AddThemeColorOverride("font_color", Muted);
        report.AddThemeColorOverride("font_hover_color", Ink);
        report.Pressed += () =>
        {
            Select(-1);
            Report.Show();
        };
        contactRow.AddChild(report);
        left.AddChild(contactRow);
        var ver = new Label { Text = BuildInfo(), Modulate = Ui.Faint };
        ver.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        left.AddChild(ver);
    }

    Button NavItem(VBoxContainer col, string text, string icon, Action open, bool page = true)
    {
        var b =
            new Button { Text = text, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, ToggleMode = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        DialogPages.NavStyle(b, NavTextAt);
        var slot = new Control { AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = Ui.Px(NavIconAt), OffsetRight = Ui.Px(NavIconAt + NavIcon),
            OffsetTop = -Ui.Px(NavIcon / 2f), OffsetBottom = Ui.Px(NavIcon / 2f), MouseFilter = Control.MouseFilterEnum.Ignore };
        var glyph = new ToolIcon { Kind = icon, Color = Muted };
        slot.AddChild(glyph);
        b.AddChild(slot);
        bool over = false;
        void Tint()
        {
            glyph.Color = b.ButtonPressed ? Colors.White : over ? Ink : Muted;
            glyph.QueueRedraw();
        }
        b.MouseEntered += () => { over = true; Tint(); };
        b.MouseExited += () => { over = false; Tint(); };
        b.Toggled += _ => Tint();
        int idx = _nav.Count;
        b.Pressed += () =>
        {
            if (!page)
            {
                b.SetPressedNoSignal(false);
                Tint();
                open();
                return;
            }
            Select(idx);
            open();
        };
        _nav.Add(b);
        col.AddChild(Gives(b, NavHeight, TightNavHeight));
        return b;
    }

    public void Select(int i)
    {
        if (i != _shown && _shown == ScenariosAt) Shelf.Find(pack: false, "");
        if (i != _shown && _shown == PacksAt) Shelf.Find(pack: true, "");
        _shown = i;
        for (int k = 0; k < _nav.Count; k++)
        {
            _nav[k].SetPressedNoSignal(k == i);
            _nav[k].EmitSignal(BaseButton.SignalName.Toggled, k == i);
        }
    }

    public void ShowScenarios()
    {
        Select(ScenariosAt);
        Host.Show(_store, _scenarios);
    }

    public void Home()
    {
        _pageOpen = false;
        Select(-1);
        Status.GetParent()?.RemoveChild(Status);
        _column.AddChild(Status);
        _column.MoveChild(Status, _statusAt);
        foreach (var c in _page.GetChildren()) c.QueueFree();
        _page.Visible = false;
    }

    public void ShowPacks()
    {
        Select(PacksAt);
        Packs.Show();
    }

    Rect2I PageArea()
    {
        var all = Root!.GetGlobalRect();
        return new Rect2I((int)all.Position.X + PageLeft, (int)all.Position.Y + Edge, (int)all.Size.X - PageLeft - Edge, (int)all.Size.Y - 2 * Edge);
    }

    public VBoxContainer NewPage(string heading, string sub, bool wide = false)
    {
        _pageOpen = true;
        _page.Visible = true;
        Status.GetParent()?.RemoveChild(Status);
        foreach (var c in _page.GetChildren()) c.QueueFree();
        var panel = new PanelContainer();
        var face = Floating(PageInset, PageTop);
        face.ContentMarginBottom = Ui.Px(PageFoot);
        panel.AddThemeStyleboxOverride("panel", face);
        _page.AddChild(panel);
        var held = new VBoxContainer();
        held.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        var v = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        held.AddChild(v);
        held.AddChild(Status);
        if (wide) panel.AddChild(held);
        else
        {
            var column = new Measure();
            panel.AddChild(column);
            column.AddChild(held);
        }
        var h = new Label { Text = heading };
        h.AddThemeFontOverride("font", Serif());
        h.AddThemeFontSizeOverride("font_size", Ui.Px(30));
        v.AddChild(h);
        if (sub.Length > 0) v.AddChild(new Label { Text = sub, Modulate = Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        v.AddChild(new Control { CustomMinimumSize = Ui.Px(0, 6) });
        var settling = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _page.AddChild(settling);
        _main.GetTree().CreateTimer(0.4).Timeout += () => { if (GodotObject.IsInstanceValid(settling)) settling.QueueFree(); };
        return v;
    }
}
