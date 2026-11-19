using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

public partial class ImportPage : VBoxContainer
{
    readonly string _packagesRoot;
    readonly Action<string, string> _done;
    readonly bool _keepLog;
    VBoxContainer _rows = null!;
    Label _listState = null!;
    LineEdit _filter = null!, _source = null!;
    CheckBox _skipText = null!;
    Control _skipRow = null!;
    Label _logHead = null!;
    RichTextLabel _log = null!;
    FindBar _find = null!;
    static readonly System.Text.StringBuilder Kept = new();
    static ImportPage? _live;
    Button _go = null!, _stop = null!;
    Label _status = null!;
    ImportProgress _progress = null!;
    readonly List<ModuleEntry> _entries = new();
    readonly Dictionary<ModuleEntry, (PanelContainer Row, Label Detail)> _rowOf = new();
    readonly Dictionary<string, Texture2D?> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    ModuleEntry? _picked;
    Listing _listing = Listing.Looking;
    bool _running;
    ConverterRun? _run, _lister;
    static ImportWatch? _watch;
    const string GoWord = "Convert", InstallWord = "Install", InstallShared = "Install a .ktpack…", NoSource = "Pick a module in the list or browse to one";
    internal const string ListHead = "Modules on this PC", SharedHead = "Shared pack", ConvertHead = "Convert", LogHead = "Log";
    const int ThumbSize = 48, ThumbPixels = 96, RowPadX = 10, RowPadY = 8, TileTall = 72, TilePad = 16, TileIcon = 38, TileNameSize = 17;
    const float ListShare = 1.25f, RowHover = 0.09f, ThumbBlank = 0.04f, TileFill = 0.07f, TilePressed = 0.13f;

    enum Listing { Looking, Listed, Failed }

    public Action<string>? Installs { get; init; }

    public Settings? Cfg { get; init; }

    static readonly ShelfOrder[] ModuleOrders = { ShelfOrder.Newest, ShelfOrder.Name };
    ShelfOrder _order = ShelfOrder.Newest;

    public ImportPage(string packagesRoot, Action<string, string> done, bool keepLog = false)
    {
        _packagesRoot = packagesRoot;
        _done = done;
        _keepLog = keepLog;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Section));
        AddChild(cols);
        cols.AddChild(ModuleColumn());
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        cols.AddChild(right);
        if (Installs != null) SharedPack(right);
        ConvertForm(right);
        LogPart(right);
        FileDrops.Listen(this, files =>
        {
            if (_running || ImportDoor.Taken(files) is not { } file) return false;
            Prefill(file);
            return true;
        });

        _live = this;
        if (!_keepLog && ConverterTool.Writer == null) Kept.Clear();
        ShowLog();
        if (ConverterTool.Writer is { } earlier)
        {
            _run = earlier;
            _go.Disabled = true;
            _stop.Visible = true;
            if (_watch is { Ended: null } watch) _progress.Follow(watch, "Converting");
            else SetStatus("A conversion started earlier is still running.");
        }
        else if (_keepLog && _watch is { Ended: not null } ended) _progress.Follow(ended, "");
        ListModules();
    }

    VBoxContainer ModuleColumn()
    {
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = ListShare };
        left.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        left.AddChild(Ui.Header(ListHead));
        _filter = new LineEdit { PlaceholderText = "Find", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _filter.TextChanged += _ => Fill();
        _order = Cfg?.ModuleOrder ?? ShelfOrder.Newest;
        var order = ShelfView.OrderMenu(ModuleOrders, _order, o =>
        {
            _order = o;
            if (Cfg != null)
            {
                Cfg.ModuleOrder = o;
                Cfg.Save();
            }
            Fill();
        }, "Order of the modules");
        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        findRow.AddChild(_filter);
        findRow.AddChild(order);
        left.AddChild(findRow);
        _listState = new Label { Modulate = Ui.Muted, Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        left.AddChild(_listState);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        scroll.AddChild(_rows);
        left.AddChild(scroll);
        return left;
    }

    void SharedPack(VBoxContainer right)
    {
        right.AddChild(Ui.Header(SharedHead));
        var tile = new Button
        {
            CustomMinimumSize = Ui.Px(0, TileTall), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Or drop the .ktpack on the window",
        };
        tile.AddThemeStyleboxOverride("normal", TileStyle(TileFill));
        tile.AddThemeStyleboxOverride("hover", TileStyle(RowHover));
        tile.AddThemeStyleboxOverride("pressed", TileStyle(TilePressed));
        tile.AddThemeStyleboxOverride("focus", TileStyle(RowHover));
        tile.Pressed += BrowseShared;
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        h.OffsetLeft = Ui.Px(TilePad);
        h.OffsetRight = -Ui.Px(TilePad);
        h.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        tile.AddChild(h);
        var slot = new Control { CustomMinimumSize = Ui.Px(TileIcon, TileIcon), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        var glyph = new ToolIcon { Kind = "import", Color = TitleStyle.Ink, MouseFilter = MouseFilterEnum.Ignore };
        glyph.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        slot.AddChild(glyph);
        h.AddChild(slot);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        h.AddChild(text);
        text.AddChild(TitleStyle.ItemName(new Label { Text = InstallShared, MouseFilter = MouseFilterEnum.Ignore }, TileNameSize));
        var what = TitleStyle.CardLine("A pack or scenario shared with you", Ui.Muted);
        what.CustomMinimumSize = Vector2.Zero;
        what.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(what);
        right.AddChild(tile);
    }

    static StyleBoxFlat TileStyle(float fill) => Ui.Box(new Color(1, 1, 1, fill), TitleStyle.CardRadius, 0, 0);

    void ConvertForm(VBoxContainer right)
    {
        right.AddChild(Ui.Header(ConvertHead));
        var g = Ui.Form(Ui.PanelLabels);
        right.AddChild(g);
        g.AddChild(new Label { Text = "Source" });
        var srcRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _source = new LineEdit
        {
            TooltipText = "A module or save file, a .ktpack file, a Workshop id, or a steamcommunity.com link",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _source.TextChanged += _ => { if (_picked != null && _source.Text != _picked.Path) Unpick(); FitToSource(); };
        srcRow.AddChild(_source);
        var browse = new Button { Text = "Browse…", TooltipText = "Each module shows its own picture" };
        browse.Pressed += Browse;
        srcRow.AddChild(browse);
        g.AddChild(srcRow);
        Ui.AlignForm(g);
        _skipText = new CheckBox
        {
            Text = "Leave out floating 3D text",
            TooltipText = "Titles and labels a module floats over its table come in as flat label pieces; a big module can hold dozens. Notecards come in either way.",
        };
        _skipRow = Ui.UnderControls(_skipText, Ui.PanelLabels);
        right.AddChild(_skipRow);
        _go = new Button { Text = GoWord, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Ui.MakePrimary(_go);
        _go.Pressed += Go;
        right.AddChild(_go);
        FitToSource();
        _stop = new Button
        {
            Text = "Stop", Visible = false, FocusMode = FocusModeEnum.None,
            TooltipText = "Stops the conversion and puts back what it had moved aside.",
        };
        _stop.Pressed += Stop;
        _progress = new ImportProgress(_stop);
        right.AddChild(_progress);
        _status = new Label { Modulate = Ui.Muted, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, Visible = false };
        right.AddChild(_status);
    }

    void LogPart(VBoxContainer right)
    {
        _logHead = Ui.Header(LogHead);
        right.AddChild(_logHead);
        _log = new RichTextLabel { SizeFlagsVertical = SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = true, CustomMinimumSize = Ui.Px(0, 160) };
        _find = FindBar.ForLabel(_log, () => Kept.ToString());
        _find.Visible = false;
        _find.Dismissed += () => _find.Visible = false;
        right.AddChild(_find);
        right.AddChild(_log);
    }

    void ShowLog()
    {
        bool any = Kept.Length > 0;
        _logHead.Visible = any;
        _log.Visible = any;
        if (!any) _find.Visible = false;
        _find.Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (_log.Visible && _find.Handles(e)) GetViewport().SetInputAsHandled();
    }

    void ListModules()
    {
        _listing = Listing.Looking;
        ShowListState();
        bool listed = false;
        _lister = ConverterTool.Run(new[] { "list-modules" }, line =>
        {
            if (!IsInstanceValid(this) || !line.StartsWith("{")) return;
            var e = ModuleEntryOf(line);
            if (e == null) return;
            if (e.Kind != MeasuredKind) { _entries.Add(e); return; }
            if (!listed) { listed = true; Fill(); }
            Measured(e);
        }, code =>
        {
            if (!IsInstanceValid(this)) return;
            _lister = null;
            if (code == ConverterTool.StoppedCode) return;
            _listing = code == 0 ? Listing.Listed : Listing.Failed;
            if (!listed) Fill();
            else ShowListState();
        });
        if (_lister == null) { _listing = Listing.Failed; ShowListState(); }
    }

    const string MeasuredKind = "size";

    public override void _ExitTree()
    {
        _lister?.Cancel();
        if (_live == this) _live = null;
    }

    void Measured(ModuleEntry m)
    {
        var e = _entries.FirstOrDefault(x => string.Equals(x.Path, m.Path, StringComparison.OrdinalIgnoreCase));
        if (e == null) return;
        e.Size = m.Size;
        e.Fetch = m.Fetch;
        e.Measured = true;
        if (!_rowOf.TryGetValue(e, out var row)) return;
        row.Detail.Text = Detail(e);
        if (e.Fetch > 0) row.Row.TooltipText = $"{e.Fetch} pictures and models come from the web during the conversion";
    }

    static string Detail(ModuleEntry e)
    {
        var parts = new List<string> { e.Kind == "save" ? "Save" : "Workshop " + e.Id };
        if (e.Measured && e.Size > 0) parts.Add(ContentLimits.Describe(e.Size));
        if (e.Measured && e.Fetch > 0) parts.Add($"{e.Fetch} to download");
        return string.Join("  ·  ", parts);
    }

    Texture2D? Thumb(ModuleEntry e)
    {
        if (e.Thumb is not { } file) return null;
        if (_thumbs.TryGetValue(file, out var had)) return had;
        var t = BestEffort.Read<Texture2D?>(() => SmallIcon(file), fallback: null);
        _thumbs[file] = t;
        return t;
    }

    static Texture2D? SmallIcon(string file)
    {
        if (Image.LoadFromFile(file) is not { } img || img.IsEmpty()) return null;
        img.Resize(ThumbPixels, ThumbPixels, Image.Interpolation.Lanczos);
        return ImageTexture.CreateFromImage(img);
    }

    void Fill()
    {
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        _rowOf.Clear();
        foreach (var e in Listed(_entries, _filter.Text, _order)) _rows.AddChild(RowFor(e));
        ShowListState();
    }

    internal static IEnumerable<ModuleEntry> Listed(IEnumerable<ModuleEntry> entries, string query, ShelfOrder order)
    {
        var found = entries.Where(e => TextSearch.Matches(query, e.Name, e.Id));
        return order == ShelfOrder.Name ? found.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase) : found;
    }

    void ShowListState()
    {
        _filter.PlaceholderText = _entries.Count > 0 ? $"Find among {_entries.Count}" : "Find";
        _listState.Text = _entries.Count > 0
            ? (_rowOf.Count == 0 ? "No module matches" : "")
            : _listing switch { Listing.Looking => "Looking for modules…", Listing.Failed => "Could not list the modules on this PC", _ => "No modules on this PC" };
        _listState.Visible = _listState.Text.Length > 0;
    }

    PanelContainer RowFor(ModuleEntry e)
    {
        var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = e.Path };
        row.AddThemeStyleboxOverride("panel", RowStyle(e == _picked, false));
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        row.AddChild(h);
        var plate = new Panel { CustomMinimumSize = Ui.Px(ThumbSize, ThumbSize), MouseFilter = MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", Ui.Box(new Color(1, 1, 1, ThumbBlank), TitleStyle.ControlRadius, 0, 0));
        var pic = new TextureRect
        {
            Texture = Thumb(e), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore,
        };
        pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        plate.AddChild(pic);
        h.AddChild(plate);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        h.AddChild(text);
        text.AddChild(TitleStyle.ItemName(new Label { Text = e.Name.Trim(), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore }));
        var detail = TitleStyle.CardLine(Detail(e), Ui.Muted);
        detail.AutowrapMode = TextServer.AutowrapMode.Off;
        detail.ClipText = true;
        detail.CustomMinimumSize = Vector2.Zero;
        detail.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(detail);
        row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", RowStyle(e == _picked, true));
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", RowStyle(e == _picked, false));
        row.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            Pick(e);
            if (mb.DoubleClick) Go();
        };
        _rowOf[e] = (row, detail);
        return row;
    }

    static StyleBoxFlat RowStyle(bool picked, bool hover) =>
        picked ? Ui.Box(new Color(TitleStyle.Brass, 0.18f), TitleStyle.CardRadius, RowPadX, RowPadY)
        : Ui.Box(new Color(1, 1, 1, hover ? RowHover : 0.05f), TitleStyle.CardRadius, RowPadX, RowPadY);

    void Restyle()
    {
        foreach (var (e, row) in _rowOf) row.Row.AddThemeStyleboxOverride("panel", RowStyle(e == _picked, false));
    }

    void Pick(ModuleEntry e)
    {
        _picked = e;
        Restyle();
        _source.Text = e.Path;
        _source.CaretColumn = e.Path.Length;
        FitToSource();
    }

    void Unpick()
    {
        _picked = null;
        Restyle();
    }

    void Browse()
    {
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { $"*{ImportDoor.ModuleExt}, *{ImportDoor.SharedExt} ; Digital tabletop module or save, or a Kriegsspiel Table file" }, Size = Ui.PxI(900, 600) };
        FileBrowser.Fit(fd);
        if (_entries.FirstOrDefault(e => e.Kind == "workshop") is { } listed && System.IO.Path.GetDirectoryName(listed.Path) is { } workshop) fd.CurrentDir = workshop;
        AddChild(fd);
        FileThumbs.Show(fd);
        fd.FileSelected += p =>
        {
            var known = _entries.FirstOrDefault(e => string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase));
            if (known != null) { Pick(known); return; }
            Prefill(p);
        };
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void Prefill(string source)
    {
        _source.Text = source;
        if (_picked != null && source != _picked.Path) Unpick();
        FitToSource();
    }

    void BrowseShared()
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Size = Ui.PxI(900, 600),
            Filters = new[] { $"*{ImportDoor.SharedExt} ; Kriegsspiel Table pack" },
        };
        FileBrowser.Fit(fd);
        AddChild(fd);
        fd.FileSelected += p => Installs?.Invoke(p);
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    void FitToSource()
    {
        bool shared = Installs != null && ImportDoor.IsShared(_source.Text);
        _skipRow.Visible = !shared;
        if (_running || _run != null) return;
        _go.Text = shared ? InstallWord : GoWord;
        bool empty = _source.Text.Trim().Length == 0;
        _go.Disabled = empty;
        _go.TooltipText = empty ? NoSource : "";
    }

    public void StartImport() => Go();
    public string? StopOn { get; set; }

    void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visible = text.Length > 0;
    }

    public override void _Process(double delta)
    {
        if (_running || _run is not { Finished: true }) return;
        _run = null;
        FitToSource();
        _stop.Visible = false;
        _stop.Disabled = false;
        if (!_progress.Visible) SetStatus("The conversion started earlier has ended.");
    }

    void Stop()
    {
        if (_run == null) return;
        _run.Cancel();
        _stop.Disabled = true;
        SetStatus("Stopping, and putting back what the conversion had moved aside…");
    }

    static readonly JsonSerializerOptions ListedOptions = new() { PropertyNameCaseInsensitive = true };

    static ModuleEntry? ModuleEntryOf(string line)
    {
        try { return JsonSerializer.Deserialize<ModuleEntry>(line, ListedOptions); }
        catch (JsonException) { return null; }
    }

    static string Entry(string path)
    {
        try { return SaveStore.Default.TrashEntries().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))?.Describe() ?? System.IO.Path.GetFileName(path); }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return System.IO.Path.GetFileName(path); }
    }

    void Go()
    {
        if (_running || _source.Text.Trim().Length == 0) return;
        if (Installs is { } install && ImportDoor.IsShared(_source.Text))
        {
            install(_source.Text.Trim());
            return;
        }
        _running = true;
        _go.Disabled = true;
        _go.Text = "Converting…";
        var source = _source.Text.Trim();
        bool skipText = _skipText.ButtonPressed;
        var watch = _watch = new ImportWatch();
        _progress.Follow(watch, "Reading the module");
        SetStatus("");
        var args = new List<string> { "plan", source, "--out", _packagesRoot };
        var plan = new ImportPlan();
        var said = new List<string>();
        _run = ConverterTool.Run(args, line =>
        {
            if (!watch.Read(line) && plan.Read(line)) said.Add(line);
        }, code =>
        {
            if (!IsInstanceValid(this)) { watch.End(NotImported); return; }
            _run = null;
            watch.Between();
            var packs = plan.Packs;
            var scenarios = plan.Scenarios;
            source = plan.File ?? source;
            if (code == ConverterTool.StoppedCode)
            {
                Log("[color=#ffd75e]Stopped before anything was changed.[/color]");
                Reset(Stopped);
                return;
            }
            if (code != 0 || packs.Count == 0)
            {
                foreach (var l in said) Log(Fmt.Bb(l));
                Log($"[color=#f87]The module could not be read, so nothing was converted (exit {code}).[/color]");
                Reset(Failed);
                return;
            }
            var worked = WorkedOn(_packagesRoot, packs);
            if (worked.Count == 0) { Import(source, skipText, packs, scenarios, watch); return; }
            _stop.Visible = false;
            _progress.Waiting("Waiting for your answer");
            AskFirst(worked, () => Import(source, skipText, packs, scenarios, watch), () =>
            {
                Log("Not converted: nothing was changed.");
                Reset(NotImported);
            });
        });
        if (_run != null) { _stop.Visible = true; _stop.Disabled = false; }
    }

    const string Done = "Done", Stopped = "Stopped", Failed = "Failed", NotImported = "Not converted";

    void Reset(string outcome)
    {
        _running = false;
        _go.Disabled = false;
        _go.Text = GoWord;
        FitToSource();
        _stop.Visible = false;
        _stop.Disabled = false;
        _watch?.End(outcome);
        SetStatus("");
    }

    void AskFirst(List<(string id, string name, PackChange change)> worked, Action go, Action cancel) =>
        AskBeforeReplacing(this, worked, "Converting this module again", "Convert anyway", go, cancel);

    public static List<(string id, string name, PackChange change)> WorkedOn(string packagesRoot, IEnumerable<string> packIds)
    {
        var worked = new List<(string id, string name, PackChange change)>();
        foreach (var id in packIds.Distinct())
        {
            var dir = System.IO.Path.Combine(packagesRoot, id);
            PackChange change;
            try { change = PackChanges.Check(dir).change; }
            catch (Exception e) when (SaveStore.IsFileTrouble(e)) { change = PackChange.Unknown; }
            if (change is PackChange.Changed or PackChange.Unknown) worked.Add((id, WorkshopContent.NameIn(System.IO.Path.Combine(dir, "pack.json"), id), change));
        }
        return worked;
    }

    public static void AskBeforeReplacing(Node parent, List<(string id, string name, PackChange change)> worked, string doing, string okText, Action go, Action? cancel)
    {
        var lines = worked.Select(w => w.change == PackChange.Changed
            ? $"·  '{w.name}' ({w.id}): pieces in it were made or changed in the piece studio."
            : $"·  '{w.name}' ({w.id}): whether pieces in it were changed in the piece studio cannot be told (it came here before the app kept a record of what each conversion wrote).");
        bool one = worked.Count == 1;
        var text = $"{doing} replaces {(one ? "a pack you already have" : "packs you already have")}:\n\n{string.Join("\n", lines)}\n\n"
            + $"The pack{(one ? "" : "s")} you have go{(one ? "es" : "")} to the trash whole, with every piece changed in the piece studio, and stay{(one ? "s" : "")} there {SaveStore.TrashDays} days: "
            + "restore from Piece studio → Trash, at the foot of the page. The new copy does not have those changes.";
        bool answered = false;
        var dlg = Ui.Confirm(parent, one ? "Replace a pack you changed?" : "Replace packs you changed?", okText, () => { if (!answered) { answered = true; go(); } }, text, 460);
        dlg.CancelButtonText = "Cancel";
        dlg.Canceled += () => { if (!answered) { answered = true; cancel?.Invoke(); } };
    }

    InUse.Hold? ClaimForImport(List<string> plannedPacks, List<string> plannedScenarios)
    {
        try
        {
            var folders = plannedPacks.Select(p => (InUse.Pack(_packagesRoot, p), $"the pack {p}")).ToList();
            folders.AddRange(plannedScenarios.Select(s => (InUse.Scenario(_packagesRoot, s), $"the scenario {s}")));
            return InUse.Claim(folders, "being converted");
        }
        catch (InUseException e)
        {
            Log($"[color=#f87]Not converted: {Fmt.Bb(e.Message)}[/color]");
            Reset(NotImported);
            return null;
        }
    }

    void Import(string source, bool skipText, List<string> plannedPacks, List<string> plannedScenarios, ImportWatch watch)
    {
        if (!IsInstanceValid(this)) return;
        if (ClaimForImport(plannedPacks, plannedScenarios) is not { } hold) return;
        _progress.Waiting("Starting the converter");
        var args = new List<string> { "import", source, "--out", _packagesRoot };
        if (skipText) args.AddRange(new[] { "--skip", "3DText" });
        string? scenario = null, pack = null, report = null, hostile = null, leftOut = null;
        var also = new List<string>();
        Log($"[b]Converting {Fmt.Bb(source)}[/b]");
        int movedAside = 0, putBack = 0;
        string Left() => movedAside == 0 ? "" : putBack >= movedAside
            ? " What you had before is back in place."
            : " What you had before is in the trash: restore it from there (Piece studio or Scenarios, at the foot of the page).";
        _run = ConverterTool.Run(args, line =>
        {
            if (IsInstanceValid(this) && StopOn != null && line.Contains(StopOn)) { StopOn = null; Stop(); }
            if (watch.Read(line)) return;
            if (line.StartsWith("SIZES ")) { watch.Sizes = Sizes(line[6..]); return; }
            if (line.StartsWith("TRASHED "))
            {
                movedAside++;
                Log($"[color=#ffd75e]The {Fmt.Bb(Entry(line[8..].Trim()))} you had went to the trash first, whole. If the new one is not what you wanted, restore it from the trash at the foot of Piece studio (a pack) or Scenarios (a scenario) within {SaveStore.TrashDays} days.[/color]");
                return;
            }
            if (line.StartsWith("PUTBACK "))
            {
                putBack++;
                Log("[color=#8f8]Put back as it was:[/color] " + Fmt.Bb(line[8..]));
                return;
            }
            if (line.StartsWith("IMPORTED "))
            {
                foreach (var part in line[9..].Split(' '))
                {
                    var kv = part.Split('=', 2);
                    if (kv[0] == "scenario") scenario = kv[1];
                    if (kv[0] == "pack") pack = kv[1];
                }
                return;
            }
            if (line.StartsWith("ALSO "))
            {
                if (line[5..].Split(' ').Select(p => p.Split('=', 2)).FirstOrDefault(kv => kv[0] == "scenario" && kv.Length == 2) is { } kv) also.Add(kv[1]);
                return;
            }
            if (line.StartsWith("REPORT ")) { report = line[7..]; return; }
            if (line.StartsWith("HOSTILE "))
            {
                hostile = line[8..];
                Log($"[color=#f87][b]Hostile script removed[/b] — {Fmt.Bb(hostile)}. The module carried a script that copies itself into objects or reaches the web; it was cut out before anything was read from it, and nothing of it is in the pack. Details in the report.[/color]");
                return;
            }
            if (line.StartsWith("LEFTOUT "))
            {
                var parts = line[8..].Split(' ', 2);
                leftOut = parts[0] == "1" ? "1 piece left out" : $"{parts[0]} pieces left out";
                Log($"[color=#ff5f5f][b]{leftOut}:[/b] {Fmt.Bb(parts.Length > 1 ? parts[1] : "")}. Their pictures or models are gone from the web and this computer has no copy, so the module cannot show them either. Everything else came in; the report lists each one with its link.[/color]");
                return;
            }
            if (line.StartsWith("NOTEXACT "))
            {
                Log($"[color=#fc6][b]{Fmt.Bb(line[9..])} not taken from the module exactly[/b]: left out, stood in for or only partly done, each named in the report with what to do instead.[/color]");
                return;
            }
            if (line.StartsWith("Output") || line.StartsWith("Cache")) return;
            Log(Fmt.Bb(line));
        }, code =>
        {
            bool stopped = code == ConverterTool.StoppedCode;
            watch.End(stopped ? Stopped : pack != null ? Done : Failed);
            if (IsInstanceValid(this))
            {
                _running = false;
                _run = null;
                FitToSource();
                _stop.Visible = false;
                _stop.Disabled = false;
                SetStatus("");
            }
            if (stopped)
                Log(movedAside > 0
                    ? "[color=#ffd75e]Stopped. Nothing of this conversion was kept." + Left() + "[/color]"
                    : "[color=#ffd75e]Stopped before anything was changed.[/color]");
            else if (pack != null)
            {
                Log(scenario != null
                    ? $"[color=#8f8]Done.[/color] {Scenarios(also.Prepend(scenario))} and pack '{Fmt.Bb(pack)}' are ready." + OnDisk(watch) + (report != null ? $" Report: {Fmt.Bb(report)}" : "")
                    : $"[color=#8f8]Done.[/color] Pack '{Fmt.Bb(pack)}' restored exactly as it was shared." + OnDisk(watch));
                if (hostile != null) Log("[color=#f87]A hostile script was removed from this module; see the report.[/color]");
                if (leftOut != null) Log($"[color=#ff5f5f][font_size={Ui.Px(20)}][b]{leftOut}: broken in the module too[/b][/font_size][/color]");
            }
            else Log($"[color=#f87]Conversion failed (exit {code}).[/color]" + Left());
            if (!stopped && pack != null) _done(scenario ?? "", pack);
        }, writesTo: _packagesRoot, hold: hold);
        if (_run != null) { _stop.Visible = true; _stop.Disabled = false; }
    }

    static string Scenarios(IEnumerable<string> ids)
    {
        var quoted = ids.Select(id => $"'{Fmt.Bb(id)}'").ToList();
        return quoted.Count == 1 ? "Scenario " + quoted[0] : "Scenarios " + string.Join(", ", quoted);
    }

    static string OnDisk(ImportWatch watch) => watch.Sizes.Length > 0 ? $" {watch.Sizes}." : "";

    static string Sizes(string said)
    {
        var parts = new List<string>();
        foreach (var kv in said.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)))
            if (kv.Length == 2 && kv[0].Length > 0 && long.TryParse(kv[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bytes) && bytes > 0)
                parts.Add($"{(parts.Count == 0 ? char.ToUpperInvariant(kv[0][0]) + kv[0][1..] : kv[0])} {ContentLimits.Describe(bytes)}");
        return parts.Count == 0 ? "" : string.Join(", ", parts) + " on disk";
    }

    internal static void Log(string s)
    {
        Kept.Append(s).Append('\n');
        if (_live is { } page && IsInstanceValid(page)) page.Write(s);
    }

    void Write(string s)
    {
        if (!_log.Visible) { ShowLog(); return; }
        if (_find.Terms.Count > 0) _find.Refresh();
        else _log.AppendText(s + "\n");
    }
}
