using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed partial class SavesWindow : Window
{
    const int TileWidth = 196, PictureHeight = 110;
    static readonly Color SelectedFill = new(Ui.Accent, 0.22f);

    readonly HudContext _c;
    readonly SaveStore _store;
    readonly string _scenarioDir;
    readonly Tree _tree = new() { HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    readonly HFlowContainer _tiles = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    readonly HBoxContainer _path = new();
    readonly LineEdit _filter = new() { PlaceholderText = "Filter", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(180, 0) };
    readonly LineEdit _name = new() { PlaceholderText = "Name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    readonly Button _save, _load, _look, _restore, _purge;
    readonly Control[] _saveRow;
    readonly Label _status = new() { Modulate = Ui.Muted, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    readonly Dictionary<string, (DateTime at, Json.SaveHeadline? head)> _heads = new();
    readonly Dictionary<string, PanelContainer> _tileOf = new();
    string _folder;
    readonly Picking<string> _pick = new();
    const char PathsApart = '\n';
    bool _busy;
    bool _fillingTree;
    Texture2D? _mapThumb;

    static SavesWindow? _open;

    sealed record Entry(string Path, string Name, bool Folder, string? Run = null, TrashedSave? Trashed = null);

    const string TrashKey = "trash:";
    bool _inTrash;

    public static void Open(HudContext c)
    {
        if (!c.S.IsHost || c.N.Saves.RunInPlay is not var (dir, _)) return;
        if (_open != null && IsInstanceValid(_open)) { _open.GrabFocus(); return; }
        c.N.RequestPicture?.Invoke();
        var w = new SavesWindow(c, dir);
        _open = w;
        c.Root.AddChild(w);
        w.PopupCentered(new Vector2I(1040, 680));
        w.Show(w.InPlayFolder() ?? w.Root);
    }

    public static void LoadTurn(HudContext c, TurnInfo turn)
    {
        var name = SaveNames.Turn(turn.Turn, turn.GameTime);
        Ui.Confirm(c.Node, "Load " + name, "Load", () =>
        {
            c.S.Review(null, null);
            c.N.Saves.Load(new LoadFrom(name, TurnIndex: turn.Index), why => Report(c, name, why));
        }, LoadText(c, name), 420);
    }

    SavesWindow(HudContext c, string scenarioDir)
    {
        _c = c;
        _store = SaveStore.Default;
        _scenarioDir = scenarioDir;
        _folder = Root;
        Title = "Saves";
        Transient = true;
        Exclusive = false;
        WrapControls = false;
        MinSize = new Vector2I(760, 460);
        CloseRequested += QueueFree;
        WindowInput += OnKey;
        TreeExiting += () =>
        {
            if (_open == this) _open = null;
            foreach (var o in _made) o.Dispose();
            _made.Clear();
        };

        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Surface, 8, 6));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);
        var outer = new HBoxContainer();
        outer.AddThemeConstantOverride("separation", Ui.Gap.Block);
        bg.AddChild(outer);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };
        left.AddThemeConstantOverride("separation", Ui.Gap.Row);
        outer.AddChild(left);
        var make = new HBoxContainer();
        make.AddThemeConstantOverride("separation", Ui.Gap.Row);
        var newPlay = Ui.Btn("New play…", NewPlay, "A new folder for another play of the scenario: from its start, the table now or the save chosen");
        var newFolder = Ui.Btn("New folder", NewFolder, "A folder in the one shown, named in the list");
        newPlay.SizeFlagsHorizontal = newFolder.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        make.AddChild(newPlay);
        make.AddChild(newFolder);
        left.AddChild(make);
        left.AddChild(_tree);
        SetUpTree();

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", Ui.Gap.Group);
        outer.AddChild(right);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _path.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _path.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        top.AddChild(_path);
        top.AddChild(_filter);
        right.AddChild(top);
        _filter.TextChanged += _ => FillTiles();

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tiles.AddThemeConstantOverride("h_separation", Ui.Gap.Block);
        _tiles.AddThemeConstantOverride("v_separation", Ui.Gap.Block);
        scroll.AddChild(_tiles);
        right.AddChild(scroll);
        scroll.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _pick.Clear(); Paint(); } };

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", Ui.Gap.Row);
        bottom.AddChild(_name);
        _save = Ui.Primary("Save", () => SaveHere(false), "Save the table into this folder under that name");
        bottom.AddChild(_save);
        bottom.AddChild(new VSeparator());
        _look = Ui.Btn("Look", () => { if (Chosen() is { } e) Look(e); }, "Show it on the table without loading it");
        _load = Ui.Btn("Load", () => { if (Chosen() is { } e) Load(e); }, "The table becomes this save, for everyone at the table");
        bottom.AddChild(_look);
        bottom.AddChild(_load);
        _restore = Ui.Primary("Restore", () => Restore(PickedTrash()), "Put it back where it was");
        _purge = Ui.Btn("Delete for good", () => Purge(PickedTrash()));
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bottom.AddChild(spacer);
        bottom.AddChild(_purge);
        bottom.AddChild(_restore);
        _saveRow = new Control[] { _name, _save, bottom.GetChild<Control>(2), _look, _load };
        right.AddChild(bottom);
        right.AddChild(_status);
        _name.TextSubmitted += _ => SaveHere(false);
        _name.TextChanged += _ => Sync();
    }

    void SetUpTree()
    {
        _tree.ItemEdited += FolderNamed;
        _tree.ItemSelected += () =>
        {
            if (!_fillingTree && _tree.GetSelected()?.GetMetadata(0).AsString() is { Length: > 0 } p && (p == TrashKey ? !_inTrash : _inTrash || !SaveStore.SamePath(p, _folder)))
                Callable.From(() => { if (IsInstanceValid(this)) Show(p); }).CallDeferred();
        };
        _tree.ItemMouseSelected += (pos, button) =>
        {
            if (button == (long)MouseButton.Right && _tree.GetSelected()?.GetMetadata(0).AsString() is { Length: > 0 } p && p != Root && p != TrashKey)
                FolderMenu(new Entry(p, System.IO.Path.GetFileName(p), true), _tree.GlobalPosition + pos + Position);
        };
        _tree.AllowRmbSelect = true;
        _tree.AddThemeStyleboxOverride("cursor", new StyleBoxEmpty());
        _tree.AddThemeStyleboxOverride("cursor_unfocused", new StyleBoxEmpty());
        _tree.AddThemeConstantOverride("draw_guides", 0);
        _tree.AddThemeConstantOverride("draw_relationship_lines", 0);
        _tree.SetDragForwarding(Callable.From((Vector2 _) => new Variant()), Callable.From((Vector2 at, Variant data) => DropTarget(at, data) != null),
            Callable.From((Vector2 at, Variant data) =>
            {
                if (DropTarget(at, data) is not { } into) return;
                var paths = Dragged(data);
                if (into == TrashKey) Delete(paths.Select(AsEntry).ToList());
                else Move(paths, into);
            }));
    }

    string Root => _store.SavesRoot(_scenarioDir);

    internal string ShownFolder => _folder;

    internal Rect2? TreeRowOf(string name)
    {
        for (var item = _tree.GetRoot(); item != null; item = item.GetNextVisible())
            if (item.GetText(0) == name)
            {
                var r = _tree.GetItemAreaRect(item);
                return new Rect2(_tree.GlobalPosition + r.Position, r.Size);
            }
        return null;
    }

    (string ScenarioDir, string Run)? InPlay => _c.N.Saves.RunInPlay;

    string? InPlayFolder() => InPlay is var (dir, run) ? _store.RunFolderOf(dir, run) : null;

    void Show(string folder)
    {
        _inTrash = folder == TrashKey;
        if (!_inTrash) _folder = Directory.Exists(folder) ? folder : Root;
        else if (!Directory.Exists(_folder)) _folder = Root;
        folder = _folder;
        _pick.Clear();
        foreach (var c in _saveRow) c.Visible = !_inTrash;
        _restore.Visible = _purge.Visible = _inTrash;
        _name.PlaceholderText = $"Save the table in {(SaveStore.SamePath(folder, Root) ? "Saves" : System.IO.Path.GetFileName(folder))} as…";
        FillTree();
        FillPath();
        FillTiles();
    }

    void FillTree()
    {
        _fillingTree = true;
        try { FillTreeNow(); }
        finally { _fillingTree = false; }
    }

    void FillTreeNow()
    {
        _tree.Clear();
        var runs = _store.Runs(_scenarioDir).ToDictionary(r => r.Folder, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var inPlay = InPlayFolder();
        var hidden = _tree.CreateItem();
        var top = _tree.CreateItem(hidden);
        top.SetText(0, "Saves");
        top.SetMetadata(0, Root);
        void Add(TreeItem parent, string dir)
        {
            IEnumerable<string> subs;
            try { subs = Directory.GetDirectories(dir).Where(d => !System.IO.Path.GetFileName(d).StartsWith('.')).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
            foreach (var d in subs)
            {
                var item = _tree.CreateItem(parent);
                item.SetText(0, System.IO.Path.GetFileName(d));
                item.SetMetadata(0, d);
                if (SaveStore.SamePath(d, inPlay)) item.SetCustomColor(0, Ui.AccentInk);
                if (runs.ContainsKey(d)) item.SetTooltipText(0, SaveStore.SamePath(d, inPlay) ? "The game on the table now is saved here" : "A play of the scenario");
                Add(item, d);
                if (!_inTrash && SaveStore.SamePath(d, _folder)) item.Select(0);
            }
        }
        Directory.CreateDirectory(Root);
        Add(top, Root);
        if (!_inTrash && SaveStore.SamePath(Root, _folder)) top.Select(0);
        var trash = _tree.CreateItem(hidden);
        var deleted = _store.SavesTrash(_scenarioDir).Count;
        trash.SetText(0, deleted > 0 ? $"Trash · {deleted}" : "Trash");
        trash.SetMetadata(0, TrashKey);
        trash.SetCustomColor(0, Ui.Muted);
        trash.SetTooltipText(0, $"Deleted saves and folders, kept {SaveStore.TrashDays} days");
        if (_inTrash) trash.Select(0);
    }

    void FillPath()
    {
        foreach (var k in _path.GetChildren()) { _path.RemoveChild(k); k.QueueFree(); }
        if (_inTrash) { _path.AddChild(new Label { Text = "Trash" }); return; }
        var parts = new List<string>();
        for (var d = _folder; d != null && !SaveStore.SamePath(d, Root); d = System.IO.Path.GetDirectoryName(d)) parts.Insert(0, d);
        parts.Insert(0, Root);
        foreach (var p in parts)
        {
            if (_path.GetChildCount() > 0) _path.AddChild(new Label { Text = "/", Modulate = Ui.Faint });
            var b = Ui.Bare(new Button { Text = p == Root ? "Saves" : System.IO.Path.GetFileName(p), FocusMode = Control.FocusModeEnum.None });
            var at = p;
            b.Pressed += () => Show(at);
            _path.AddChild(b);
        }
    }

    List<Entry> Entries()
    {
        if (_inTrash) return _store.SavesTrash(_scenarioDir).Select(t => new Entry(t.Entry.Path, t.Name, t.Folder, Trashed: t)).ToList();
        var list = new List<Entry>();
        if (_store.Runs(_scenarioDir).FirstOrDefault(r => SaveStore.SamePath(r.Folder, _folder)) is { } run && File.Exists(_store.LivePath(_scenarioDir, run.Id)))
            list.Add(new Entry(_store.LivePath(_scenarioDir, run.Id), "Latest", false, run.Id));
        try
        {
            list.AddRange(Directory.GetDirectories(_folder).Where(d => !System.IO.Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase).Select(d => new Entry(d, System.IO.Path.GetFileName(d), true)));
            list.AddRange(Directory.GetFiles(_folder, "*" + SaveStore.Ext).OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(f => new Entry(f, System.IO.Path.GetFileNameWithoutExtension(f), false)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Tell("This folder cannot be read: " + e.Message); }
        return list;
    }

    void FillTiles()
    {
        foreach (var k in _tiles.GetChildren()) { _tiles.RemoveChild(k); k.QueueFree(); }
        _tileOf.Clear();
        var filter = _filter.Text.Trim();
        var shown = Entries().Where(e => filter.Length == 0 || e.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _pick.Show(shown.Select(e => e.Path));
        foreach (var e in shown) _tiles.AddChild(Tile(e));
        if (shown.Count == 0) _tiles.AddChild(new Label { Text = filter.Length > 0 ? "Nothing matches" : "Empty", Modulate = Ui.Faint });
        Sync();
    }

    PanelContainer Tile(Entry e)
    {
        var tile = new PanelContainer { CustomMinimumSize = new Vector2(TileWidth, 0), MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = e.Folder ? "" : e.Path };
        tile.AddThemeStyleboxOverride("panel", Ui.Card(6, 6));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        tile.AddChild(v);
        var picture = new TextureRect
        {
            CustomMinimumSize = new Vector2(TileWidth - 12, PictureHeight), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var plate = new Panel { CustomMinimumSize = new Vector2(TileWidth - 12, PictureHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", Ui.Box(new Color(1, 1, 1, 0.03f), 3));
        plate.AddChild(picture);
        picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (e.Folder && e.Trashed == null) ShowInside(e.Path, plate);
        else if (e.Folder) plate.AddChild(new ToolIcon { Kind = "folder", Color = Ui.Muted });
        v.AddChild(plate);
        var name = new Label { Text = e.Name, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 13);
        if (e.Run != null && e.Run == InPlay?.Run) name.Text = "Latest · on the table";
        v.AddChild(name);
        var meta = new Label { Text = e.Trashed is { } gone ? Gone(gone) : e.Folder ? FolderLine(e.Path) : "", ClipText = true, Modulate = Ui.Muted, MouseFilter = Control.MouseFilterEnum.Ignore };
        meta.AddThemeFontSizeOverride("font_size", 11);
        v.AddChild(meta);
        if (e.Trashed is { Folder: false } trashedSave) ReadHead(e with { Path = trashedSave.Item }, picture, meta);
        else if (!e.Folder && e.Trashed == null) ReadHead(e, picture, meta);
        tile.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true } mb) return;
            tile.AcceptEvent();
            var at = mb.GlobalPosition + Position;
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _pick.PickForMenu(e.Path);
                Paint();
                if (_pick.Count > 1) ManyMenu(at);
                else if (e.Trashed is { } t) TrashMenu(new[] { t }, at);
                else if (e.Folder) FolderMenu(e, at);
                else SaveMenu(e, at);
                return;
            }
            if (mb.ButtonIndex != MouseButton.Left) return;
            if (mb.DoubleClick)
            {
                if (e.Trashed is { } t) Restore(new[] { t });
                else if (e.Folder) Show(e.Path);
                else Load(e);
                return;
            }
            _pick.Click(e.Path, toggle: mb.CtrlPressed || mb.MetaPressed, range: mb.ShiftPressed);
            Paint();
        };
        if (e.Run == null && e.Trashed == null)
            tile.SetDragForwarding(Callable.From((Vector2 _) =>
            {
                var paths = _pick.Has(e.Path) ? _pick.Picked.ToList() : new List<string> { e.Path };
                tile.SetDragPreview(new Label { Text = paths.Count == 1 ? e.Name : $"{paths.Count} items" });
                return Variant.From(string.Join(PathsApart, paths));
            }), Callable.From((Vector2 _, Variant d) => e.Folder && !Dragged(d).Contains(e.Path)), Callable.From((Vector2 _, Variant d) => { if (e.Folder) Move(Dragged(d), e.Path); }));
        _tileOf[e.Path] = tile;
        return tile;
    }

    static string FolderLine(string folder)
    {
        try
        {
            int n = Directory.GetFiles(folder, "*" + SaveStore.Ext).Length;
            return n switch { 0 => "Empty", 1 => "1 save", _ => $"{n} saves" };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    void ReadHead(Entry e, TextureRect picture, Label meta) => HeadOf(e.Path, head => { if (IsInstanceValid(picture)) ShowHead(e, head, picture, meta); });

    void HeadOf(string path, Action<Json.SaveHeadline?> then)
    {
        var at = File.GetLastWriteTimeUtc(path);
        if (_heads.TryGetValue(path, out var known) && known.at == at) { then(known.head); return; }
        Task.Run(() =>
        {
            Json.SaveHeadline? head = null;
            try { head = Json.ReadSaveHeadline(path); }
            catch (Exception ex) when (SaveStore.IsDamage(ex) || ex is IOException or UnauthorizedAccessException) { head = null; }
            Callable.From(() =>
            {
                if (!IsInstanceValid(this)) return;
                _heads[path] = (at, head);
                then(head);
            }).CallDeferred();
        });
    }

    void ShowInside(string folder, Panel plate)
    {
        List<string> newest;
        try
        {
            newest = Directory.EnumerateFiles(folder, "*" + SaveStore.Ext, SearchOption.AllDirectories)
                .Where(f => !SaveStore.IsHidden(folder, System.IO.Path.GetDirectoryName(f) ?? folder))
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(Mosaic.Most).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { newest = new(); }
        if (newest.Count == 0)
        {
            plate.AddChild(new ToolIcon { Kind = "folder", Color = Ui.Muted });
            return;
        }
        var mosaic = new Mosaic { Count = newest.Count };
        mosaic.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        plate.AddChild(mosaic);
        for (int i = 0; i < newest.Count; i++)
        {
            int cell = i;
            HeadOf(newest[i], head =>
            {
                if (IsInstanceValid(mosaic)) mosaic.Set(cell, head?.Preview.Picture is { Length: > 0 } jpg ? Picture(jpg) : MapThumb());
            });
        }
    }

    void ShowHead(Entry e, Json.SaveHeadline? head, TextureRect picture, Label meta)
    {
        var when = TitleStyle.Ago(File.GetLastWriteTime(e.Path));
        bool keepsMeta = e.Trashed != null;
        if (head == null)
        {
            if (!keepsMeta) meta.Text = when;
            picture.Texture = MapThumb();
            return;
        }
        var p = head.Preview;
        var time = SaveNames.ShortTime(p.GameTime);
        if (!keepsMeta) meta.Text = $"Turn {p.Turn}{(time.Length > 0 ? " · " + time : "")} · {when}";
        picture.Texture = p.Picture is { Length: > 0 } jpg ? Picture(jpg) : MapThumb();
    }

    readonly List<GodotObject> _made = new();

    Texture2D? Picture(byte[] jpg)
    {
        using var img = new Image();
        if (img.LoadJpgFromBuffer(jpg) != Error.Ok) return null;
        var tex = ImageTexture.CreateFromImage(img);
        _made.Add(tex);
        return tex;
    }

    Texture2D? MapThumb() => _mapThumb ??= _c.S.View.Sheets.Count > 0 ? ScenarioCards.Thumb(_c.S.Store, new Scenario { Sheets = _c.S.View.Sheets.ToList() }, 512) : null;

    void Paint()
    {
        foreach (var (p, t) in _tileOf)
            if (IsInstanceValid(t)) t.AddThemeStyleboxOverride("panel", _pick.Has(p) ? Ui.Box(SelectedFill, 4, 6, 6) : Ui.Card(6, 6));
        if (Chosen() is { Folder: false, Trashed: null } e && e.Run == null) _name.Text = e.Name;
        Sync();
    }

    Entry? Chosen() => _pick.Single is { } one ? Entries().FirstOrDefault(e => e.Path == one) : null;

    List<Entry> PickedEntries()
    {
        var all = Entries();
        return _pick.Picked.Select(p => all.FirstOrDefault(e => e.Path == p)).OfType<Entry>().ToList();
    }

    List<TrashedSave> PickedTrash() => PickedEntries().Select(e => e.Trashed).OfType<TrashedSave>().ToList();

    static List<string> Dragged(Variant data) =>
        data.VariantType == Variant.Type.String ? data.AsString().Split(PathsApart, StringSplitOptions.RemoveEmptyEntries).ToList() : new();

    static Entry AsEntry(string path) => new(path, System.IO.Path.GetFileNameWithoutExtension(path), Directory.Exists(path));

    static string Items(int n) => n == 1 ? "1 item" : $"{n} items";

    void Sync()
    {
        var e = Chosen();
        bool save = e is { Folder: false, Trashed: null };
        _look.Disabled = _busy || !save || e!.Run == InPlay?.Run;
        _load.Disabled = _busy || !save || e!.Run == InPlay?.Run;
        _save.Disabled = _busy || _name.Text.Trim().Length == 0;
        int trashed = _inTrash ? _pick.Count : 0;
        _restore.Disabled = _purge.Disabled = _busy || trashed == 0;
        _restore.Text = trashed > 1 ? $"Restore {trashed}" : "Restore";
        _purge.Text = trashed > 1 ? $"Delete {trashed} for good" : "Delete for good";
    }

    void OnKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.F1) { GuideReader.Show(this, "umpire-guide.md", "saves"); return; }
        if (GuiGetFocusOwner() is LineEdit or TextEdit || _tree.GetEdited() != null) return;
        if (k.Keycode == Key.A && (k.CtrlPressed || k.MetaPressed))
        {
            _pick.All();
            Paint();
            SetInputAsHandled();
        }
        else if (k.Keycode is Key.Delete or Key.Backspace && _pick.Count > 0)
        {
            if (_inTrash) Purge(PickedTrash());
            else Delete(PickedEntries().Where(x => x.Run == null).ToList());
            SetInputAsHandled();
        }
    }

    void ManyMenu(Vector2 at)
    {
        var picked = PickedEntries();
        var pm = new PopupMenu();
        if (_inTrash)
        {
            TrashMenu(PickedTrash(), at);
            return;
        }
        var movable = picked.Where(x => x.Run == null).ToList();
        AddMoveTo(pm, movable);
        pm.AddSeparator();
        pm.AddItem($"Delete {Items(movable.Count)}…", 5);
        pm.IdPressed += id => { if (id == 5) Delete(movable); };
        Pop(pm, at);
    }

    static string Gone(TrashedSave t) =>
        $"Deleted {TitleStyle.Ago(t.Entry.Stamp.ToLocalTime())} · {t.Entry.DaysLeft(DateTime.UtcNow)} days left";

    void Restore(IReadOnlyList<TrashedSave> many)
    {
        if (many.Count == 0) return;
        OnDisk("Restoring…", () => EachOf(many, t => _store.Restore(t.Entry, _c.S.Store.Root, Repeatable.UtcNow)), said => { Tell(said); Show(TrashKey); });
    }

    void Purge(IReadOnlyList<TrashedSave> many)
    {
        if (many.Count == 0) return;
        var title = many.Count == 1 ? many[0].Name : Items(many.Count);
        Ui.Confirm(this, "Delete " + title + " for good", "Delete", () =>
            OnDisk("Deleting…", () => EachOf(many, t => { _store.DeleteTrashEntry(t.Entry); return ""; }), said => { Tell(said); Show(TrashKey); }),
            many.Count == 1 ? "It cannot be brought back." : "They cannot be brought back.", 320);
    }

    static string EachOf<T>(IReadOnlyList<T> many, Func<T, string> one)
    {
        var said = new List<string>();
        var failed = new List<string>();
        foreach (var x in many)
        {
            try { said.Add(one(x)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add(e.Message); }
        }
        if (failed.Count > 0) return $"{many.Count - failed.Count} of {many.Count} done; not done: " + string.Join("; ", failed);
        return many.Count == 1 ? said[0] : $"{many.Count} done.";
    }

    void TrashMenu(IReadOnlyList<TrashedSave> many, Vector2 at)
    {
        var pm = new PopupMenu();
        pm.AddItem(many.Count > 1 ? $"Restore {many.Count}" : "Restore", 1);
        pm.AddItem(many.Count > 1 ? $"Delete {many.Count} for good…" : "Delete for good…", 2);
        pm.IdPressed += id => { if (id == 1) Restore(many); else Purge(many); };
        Pop(pm, at);
    }

    void Tell(string text) => _status.Text = text;

    void Busy(bool busy, string? text = null)
    {
        _busy = busy;
        if (text != null) Tell(text);
        Sync();
    }

    static string LoadText(HudContext c, string name)
    {
        var inPlay = c.N.Saves.RunInPlay is var (dir, run) ? SaveStore.Default.RunFolderOf(dir, run) : null;
        var kept = inPlay != null ? System.IO.Path.GetFileName(inPlay) : "the game on the table";
        return $"The table becomes {name} for everyone at the table. Play from there is saved in a new folder; {kept} stays as it is.";
    }

    static void Report(HudContext c, string name, string? why)
    {
        if (why == null) c.Log($"[b]Loaded {Fmt.Bb(name)}[/b]");
        else c.Log($"[color=#ff7060]{Fmt.Bb(name)} was not loaded: {Fmt.Bb(why)}. Nothing changed.[/color]");
    }

    void Load(Entry e)
    {
        if (e.Folder || _busy) return;
        if (e.Run != null && e.Run == InPlay?.Run) return;
        var name = e.Run != null && _store.RunFolderOf(_scenarioDir, e.Run) is { } runFolder ? System.IO.Path.GetFileName(runFolder) : e.Name;
        var text = e.Run != null
            ? $"The table becomes the latest of {name} for everyone at the table, and play goes on in that folder. The game on the table now stays where it is."
            : LoadText(_c, name);
        Ui.Confirm(this, "Load " + name, "Load", () =>
        {
            Busy(true, "Loading " + name + "…");
            var from = e.Run != null ? new LoadFrom(name, Run: e.Run) : new LoadFrom(name, File: e.Path);
            _c.S.Review(null, null);
            _c.N.Saves.Load(from, why =>
            {
                Report(_c, name, why);
                if (!IsInstanceValid(this)) return;
                Busy(false, why == null ? null : "Not loaded: " + why);
                if (why == null) QueueFree();
            });
        }, text, 420);
    }

    void Look(Entry e)
    {
        if (e.Folder || _busy) return;
        Busy(true, "Reading " + e.Name + "…");
        var packs = _c.S.Store;
        Task.Run(() =>
        {
            GameState? state = null;
            string? why = null;
            try { state = KTable.Server.SaveLoader.Load(e.Path, packs).State; }
            catch (Exception ex) { why = ex.Message; }
            Callable.From(() =>
            {
                if (state != null)
                {
                    _c.S.Review(state, new TurnInfo { Index = -1, Turn = state.Clock.Turn, GameTime = state.Clock.GameTime, SaveName = e.Name });
                    if (IsInstanceValid(this)) QueueFree();
                    return;
                }
                if (IsInstanceValid(this)) Busy(false, $"{e.Name} could not be read: {why}");
            }).CallDeferred();
        });
    }

    void SaveHere(bool replace)
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || _busy) return;
        Busy(true, "Saving…");
        var folder = _folder;
        _c.N.Saves.SaveAs(folder, name, replace, (path, why) =>
        {
            if (!IsInstanceValid(this)) return;
            Busy(false);
            if (why != null && !replace && File.Exists(System.IO.Path.Combine(folder, SaveNames.Clean(name) + SaveStore.Ext)))
            {
                Ui.Confirm(this, "Replace " + SaveNames.Clean(name), "Replace", () => SaveHere(true), $"{SaveNames.Clean(name)} goes to the trash ({SaveStore.TrashDays} days) and this save takes its name.", 380);
                return;
            }
            if (why != null) { Tell("Not saved: " + why); return; }
            Tell("Saved " + System.IO.Path.GetFileNameWithoutExtension(path));
            _c.Log($"Saved [b]{Fmt.Bb(System.IO.Path.GetFileNameWithoutExtension(path) ?? name)}[/b]");
            _name.Text = "";
            Show(folder);
            if (path != null) { _pick.Click(path, toggle: false, range: false); Paint(); }
        });
    }

    void OnDisk<T>(string doing, Func<T> work, Action<T> done)
    {
        Busy(true, doing);
        _c.N.Saves.OnDisk(work, (result, why) =>
        {
            if (!IsInstanceValid(this)) return;
            Busy(false, why != null ? "Not done: " + why : "");
            if (why == null && result != null) done(result);
            else Show(_folder);
        });
    }

    void NewFolder() => OnDisk("Making the folder…", () => _store.MakeFolder(_scenarioDir, _folder, "New folder"), made =>
    {
        Show(_folder);
        NameInTree(made);
    });

    TreeItem? ItemOf(string path)
    {
        for (var item = _tree.GetRoot(); item != null; item = item.GetNextInTree())
            if (SaveStore.SamePath(item.GetMetadata(0).AsString(), path)) return item;
        return null;
    }

    bool NameInTree(string path)
    {
        if (ItemOf(path) is not { } item) return false;
        _fillingTree = true;
        try
        {
            item.SetEditable(0, true);
            item.Select(0);
            _tree.ScrollToItem(item);
        }
        finally { _fillingTree = false; }
        Callable.From(() => { if (IsInstanceValid(this) && IsInstanceValid(item)) _tree.EditSelected(true); }).CallDeferred();
        return true;
    }

    void FolderNamed()
    {
        if (_tree.GetEdited() is not { } item) return;
        item.SetEditable(0, false);
        var path = item.GetMetadata(0).AsString();
        var name = item.GetText(0).Trim();
        if (name.Length == 0 || name == System.IO.Path.GetFileName(path)) { item.SetText(0, System.IO.Path.GetFileName(path)); return; }
        Callable.From(() =>
        {
            if (!IsInstanceValid(this)) return;
            OnDisk("Renaming…", () => _store.Rename(_scenarioDir, path, name), renamed => Show(SaveStore.SamePath(path, _folder) ? renamed : _folder));
        }).CallDeferred();
    }

    void Rename(Entry e)
    {
        if (e.Folder && NameInTree(e.Path)) return;
        RenameByPrompt(e);
    }

    void RenameByPrompt(Entry e) =>
        Prompt("Rename " + e.Name, "Rename", e.Name, name => OnDisk("Renaming…", () => _store.Rename(_scenarioDir, e.Path, name), p => { Show(e.Folder && SaveStore.SamePath(e.Path, _folder) ? p : _folder); }));

    void Move(IReadOnlyList<string> paths, string into)
    {
        var moving = paths.Where(p => p.Length > 0 && !SaveStore.SamePath(System.IO.Path.GetDirectoryName(p), into) && !Inside(into, p)).ToList();
        if (moving.Count == 0) return;
        OnDisk("Moving…", () => EachOf(moving, p => { _store.MoveSave(_scenarioDir, p, into); return ""; }), said => { if (moving.Count > 1) Tell(said); Show(_folder); });
    }

    static bool Inside(string path, string folder) =>
        SaveStore.SamePath(path, folder) || path.StartsWith(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    void Delete(Entry e) => Delete(new List<Entry> { e });

    void Delete(IReadOnlyList<Entry> many)
    {
        var inPlay = InPlay?.Run;
        var held = many.Where(e => _store.RunsIn(_scenarioDir, e.Path).Any(r => r.Id == inPlay)).ToList();
        var going = many.Except(held).ToList();
        if (going.Count == 0)
        {
            if (held.Count > 0) Tell("The game on the table now is saved in that folder: load another save first.");
            return;
        }
        var title = going.Count == 1 ? going[0].Name : Items(going.Count);
        var what = going.Count > 1 ? $"{Items(going.Count)} go" : going[0].Folder ? $"{going[0].Name} and everything in it go" : $"{going[0].Name} goes";
        var kept = held.Count > 0 ? $" {held[0].Name} stays: the game on the table is saved in it." : "";
        Ui.Confirm(this, "Delete " + title, "Delete", () =>
            OnDisk("Deleting…", () => EachOf(going, e => { _store.TrashSaves(_scenarioDir, e.Path, Repeatable.UtcNow, inPlay); return ""; }),
                said => { if (going.Count > 1) Tell(said); Show(going.Any(e => Inside(_folder, e.Path)) ? Root : _folder); }),
            $"{what} to the trash, where {(going.Count > 1 || going[0].Folder ? "they stay" : "it stays")} {SaveStore.TrashDays} days.{kept}", 360);
    }

    string? DropTarget(Vector2 at, Variant data)
    {
        var paths = Dragged(data);
        var into = _tree.GetItemAtPosition(at)?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(into) || paths.Count == 0 || _inTrash) return null;
        if (into == TrashKey) return paths.Any(p => _store.RunsIn(_scenarioDir, p).Any(r => r.Id == InPlay?.Run)) ? null : into;
        return paths.All(p => Inside(into, p)) ? null : into;
    }

    void SaveMenu(Entry e, Vector2 at)
    {
        var pm = new PopupMenu();
        bool inPlay = e.Run != null && e.Run == InPlay?.Run;
        pm.AddItem("Load", 1);
        pm.SetItemDisabled(0, inPlay);
        pm.AddItem("Look", 2);
        pm.SetItemDisabled(1, inPlay);
        if (e.Run == null)
        {
            pm.AddSeparator();
            pm.AddItem("Rename…", 3);
            AddMoveTo(pm, new[] { e });
            pm.AddSeparator();
            pm.AddItem("Delete…", 5);
        }
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1:
                    Load(e);
                    break;
                case 2:
                    Look(e);
                    break;
                case 3:
                    Rename(e);
                    break;
                case 5:
                    Delete(e);
                    break;
            }
        };
        Pop(pm, at);
    }

    void FolderMenu(Entry e, Vector2 at)
    {
        var pm = new PopupMenu();
        pm.AddItem("Open", 1);
        pm.AddSeparator();
        pm.AddItem("Rename…", 3);
        AddMoveTo(pm, new[] { e });
        pm.AddSeparator();
        pm.AddItem("Delete…", 5);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1:
                    Show(e.Path);
                    break;
                case 3:
                    Rename(e);
                    break;
                case 5:
                    Delete(e);
                    break;
            }
        };
        Pop(pm, at);
    }

    void AddMoveTo(PopupMenu pm, IReadOnlyList<Entry> moving)
    {
        var to = new PopupMenu { Name = "moveTo" };
        var folders = new List<string> { Root };
        try { folders.AddRange(Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Where(d => !SaveStore.IsHidden(Root, d))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var parents = moving.Select(e => System.IO.Path.GetDirectoryName(e.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var targets = folders.Where(f => !(parents.Count == 1 && SaveStore.SamePath(f, parents[0])) && !moving.Any(e => e.Folder && Inside(f, e.Path)))
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (int i = 0; i < targets.Count; i++) to.AddItem(SaveStore.SamePath(targets[i], Root) ? "Saves" : System.IO.Path.GetRelativePath(Root, targets[i]), i);
        to.IdPressed += i => Move(moving.Select(e => e.Path).ToList(), targets[(int)i]);
        pm.AddChild(to);
        pm.AddSubmenuNodeItem(moving.Count > 1 ? $"Move {Items(moving.Count)} to" : "Move to", to);
        if (targets.Count == 0) pm.SetItemDisabled(pm.ItemCount - 1, true);
    }

    void Pop(PopupMenu pm, Vector2 at)
    {
        AddChild(pm);
        pm.PopupHide += () => pm.QueueFree();
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    void Prompt(string title, string ok, string text, Action<string> done)
    {
        var edit = new LineEdit { Text = text, CustomMinimumSize = new Vector2(320, 0), SelectAllOnFocus = true };
        InPlace.Ask(this, title, ok, () => { if (edit.Text.Trim().Length > 0) done(edit.Text.Trim()); }, null, edit, edit);
    }

    void NewPlay()
    {
        if (_c.N.Server is not { } server) return;
        var chosen = Chosen() is { Folder: false, Run: null } save ? save : null;
        var form = Ui.Form(Ui.DialogLabels);
        var name = new LineEdit { Text = _store.NextRunName(_scenarioDir), CustomMinimumSize = new Vector2(280, 0), SelectAllOnFocus = true };
        var from = new OptionButton { FitToLongestItem = false };
        from.AddItem("The scenario's start", 0);
        from.AddItem("The table now", 1);
        if (chosen != null) { from.AddItem(chosen.Name, 2); from.Selected = 2; }
        form.AddChild(new Label { Text = "Name" });
        form.AddChild(name);
        form.AddChild(new Label { Text = "Starts from" });
        form.AddChild(from);
        Ui.AlignForm(form);
        var play = new CheckBox { Text = "Put it on the table now", FocusMode = Control.FocusModeEnum.None };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Group);
        box.AddChild(form);
        box.AddChild(play);
        InPlace.Ask(this, "New play", "Create", () => CreatePlay(server, SaveNames.Clean(name.Text, _store.NextRunName(_scenarioDir)), from.GetSelectedId(), chosen, play.ButtonPressed), null, box, name);
    }

    void CreatePlay(KTable.Server.GameServer server, string name, long startsFrom, Entry? chosen, bool play)
    {
        var shownIsPlay = _store.Runs(_scenarioDir).Any(r => SaveStore.SamePath(r.Folder, _folder));
        var placeIn = shownIsPlay ? System.IO.Path.GetDirectoryName(_folder) : _folder;
        LoadFrom source;
        if (startsFrom == 2 && chosen != null) source = new LoadFrom(chosen.Name, File: chosen.Path, PlayName: name);
        else if (startsFrom == 1)
        {
            var (state, turns) = (server.State.DeepClone(), server.Turns.ToList());
            source = new LoadFrom("the table", Start: () => (state, turns), PlayName: name);
        }
        else
        {
            var (store, dir, first) = (_c.S.Store, _scenarioDir, server.Turns.FirstOrDefault());
            source = new LoadFrom("the scenario's start", Start: () => ScenarioStart(store, dir, first), PlayName: name);
        }
        source = source with { PlaceIn = placeIn };
        if (play)
        {
            Ui.Confirm(this, "Put " + name + " on the table", "Load", () =>
            {
                Busy(true, "Loading " + name + "…");
                _c.S.Review(null, null);
                _c.N.Saves.Load(source, why =>
                {
                    Report(_c, name, why);
                    if (!IsInstanceValid(this)) return;
                    Busy(false, why == null ? null : "Not loaded: " + why);
                    if (why == null) QueueFree();
                });
            }, LoadText(_c, name), 420);
            return;
        }
        Busy(true, "Making " + name + "…");
        _c.N.Saves.MakePlay(source, (folder, why) =>
        {
            if (!IsInstanceValid(this)) return;
            Busy(false, why != null ? "Not made: " + why : "");
            if (folder != null) Show(folder);
        });
    }

    internal static (GameState, List<TurnRecord>) ScenarioStart(PackageStore store, string dir, TurnRecord? firstTurn)
    {
        var file = System.IO.Path.Combine(store.Root, "scenarios", dir, "scenario.json");
        if (ScenarioFiles.StartPath(store, dir) is { } start && ScenarioFiles.TryLoad(start) is { } kept) return (GameState.FromScenario(kept), new());
        bool local = (Json.PeekString(file, "id") ?? "local.").StartsWith("local.", StringComparison.Ordinal);
        if (local && firstTurn != null) return (firstTurn.Unpack(), new());
        if (ScenarioFiles.TryLoad(file) is { } now) return (GameState.FromScenario(now), new());
        throw new IOException("the scenario's start cannot be read");
    }
}
