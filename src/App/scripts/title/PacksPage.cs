using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class PacksPage
{
    readonly TitleScreen _t;
    readonly Main _main;

    public PacksPage(TitleScreen t, Main main) { _t = t; _main = main; }

    sealed class Cards
    {
        public readonly List<PanelContainer> All = new();
        public readonly Dictionary<PanelContainer, HBoxContainer> Actions = new();
        public readonly TitlePicks Picks = new();
        public PanelContainer? Pressed;
        public ulong PressedAt;

        public bool BothClicksOn(PanelContainer card, InputEventMouseButton mb)
        {
            var now = Time.GetTicksMsec();
            bool both = mb.DoubleClick && Pressed == card && now - PressedAt < 1000;
            Pressed = card;
            PressedAt = now;
            return both;
        }
    }

    public void Show()
    {
        var v = _t.NewPage("Piece studio", "");
        v.AddChild(Actions());
        v.AddChild(_t.Folders.Strip(() => { _t.Select(2); Show(); }));
        var store = new PackageStore(PackageStore.DefaultRoot);
        var page = new Cards();
        PickActions(page, store);
        v.AddChild(page.Picks.Bar);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", Ui.Gap.Block);
        scroll.AddChild(col);

        var ids = store.ListPackIds().Where(id => !StandardPacks.IsStandard(id)).OrderBy(id => store.Pack(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase).ToList();
        bool newShown = false;
        foreach (var (folder, items) in _t.Folders.Grouped(ids, id => id, pack: true))
        {
            if (items.Count == 0 && folder.Length == 0) continue;
            if (folder.Length > 0 || _t.Folders.Lib.Folders.Count > 0) col.AddChild(Section(folder.Length > 0 ? folder : "Unfiled"));
            if (items.Count == 0 && newShown) col.AddChild(new Label { Text = "Empty", Modulate = Muted });
            var grid = Grid();
            col.AddChild(grid);
            if (!newShown)
            {
                grid.AddChild(NewPackCard());
                newShown = true;
            }
            foreach (var id in items)
            {
                var pack = store.Pack(id);
                var card = pack == null ? DamagedCard(page, store, id) : PackCard(page, store, id, pack);
                page.All.Add(card);
                grid.AddChild(card);
            }
        }
        if (!newShown)
        {
            var grid = Grid();
            col.AddChild(grid);
            grid.AddChild(NewPackCard());
        }

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        foot.AddChild(MakersLink());
        foot.AddChild(_t.Trash.Link(packs: true));
        v.AddChild(foot);
    }

    static bool Gathering(InputEventMouseButton mb) => mb.CtrlPressed || mb.MetaPressed || mb.ShiftPressed;

    HBoxContainer Actions()
    {
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Gap.Block);
        var install = Primary("Install a .ktpack file…", () =>
        {
            var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.ktpack ; Kriegsspiel Table file" }, Size = new Vector2I(820, 520) };
            Ui.RememberFolder(fd);
            _main.AddChild(fd);
            fd.FileSelected += _main.InstallFile;
            fd.PopupCentered();
            Ui.FreeOnClose(fd);
        });
        install.TooltipText = "A pack, or a scenario with its packs, shared as one file (you can also drop it on this window)";
        actions.AddChild(install);
        var convert = new Button { Text = "Convert from…", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), TooltipText = "A TTS module or save (local, or a Workshop link) becomes a pack and a scenario" };
        convert.AddThemeFontSizeOverride("font_size", 16);
        convert.Pressed += () =>
        {
            var dlg = new ImportDialog(PackageStore.DefaultRoot, (sc, pk) =>
            {
                _main.RebuildLobby();
                _t.Select(2);
                Show();
                _t.Status.Text = sc.Length > 0 ? $"Converted '{sc}': its pack is below and the scenario is under Scenarios." : $"Installed the pack {pk}.";
            });
            Ui.Open(_main, dlg);
        };
        actions.AddChild(convert);
        return actions;
    }

    void PickActions(Cards page, PackageStore store)
    {
        page.Picks.Changed = () => { if (page.Picks.Count > 1) foreach (var row in page.Actions.Values) row.Visible = false; };
        page.Picks.Act(n => $"Move {n} to…", keys =>
        {
            var pm = TitlePicks.FolderMenu(_t.Folders.Lib, folder =>
            {
                foreach (var k in keys) _t.Folders.Lib.Move(true, k, folder);
                _t.Select(2);
                Show();
            });
            _main.AddChild(pm);
            pm.Popup(new Rect2I((Vector2I)_main.GetViewport().GetMousePosition().Round(), Vector2I.Zero));
        });
        page.Picks.Act(n => $"Remove {n}…", keys => Ui.Confirm(_main, $"Remove {keys.Count} packs", "Remove", () =>
        {
            _main.CancelOpening();
            var failed = new List<string>();
            foreach (var k in keys)
            {
                try { Main.Saves.TrashPack(store.Root, k, Repeatable.UtcNow); _t.Folders.Lib.Forget(true, k); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{k}: {e.Message}"); }
            }
            _main.RebuildLobby();
            _t.Select(2);
            Show();
            _t.Status.Text = failed.Count == 0 ? $"Removed {keys.Count}. They are in the trash for {SaveStore.TrashDays} days."
                : $"Removed {keys.Count - failed.Count} of {keys.Count}; not removed: {string.Join("; ", failed)}";
        }, $"They go to the trash for {SaveStore.TrashDays} days; scenarios that use them show them as missing."));
    }

    PanelContainer NewPackCard() =>
        PlusCard("New pack…", "An empty pack to make pieces in: blocks drawn in the app, or your own images", () => NewPackDialog.Open(_main, _t));

    static void Choose(Cards page, PanelContainer? card)
    {
        foreach (var c in page.All)
        {
            c.AddThemeStyleboxOverride("panel", CardStyle(c == card));
            if (page.Actions.TryGetValue(c, out var row)) row.Visible = c == card;
        }
    }

    void AddActions(Cards page, PanelContainer card, VBoxContainer face, string id)
    {
        var row = new HBoxContainer { Visible = false };
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var edit = Primary("Edit pieces", () => _main.OpenEditor(id));
        edit.TooltipText = "Open the pack on the editor table";
        edit.CustomMinimumSize = new Vector2(110, 34);
        edit.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(edit);
        face.AddChild(row);
        page.Actions[card] = row;
    }

    PanelContainer PackCard(Cards page, PackageStore store, string id, PackManifest pack)
    {
        void Refresh() { _t.Select(2); Show(); }
        var (card, cv, nameRow) = CardFace(null, pack.Name, plate: true);
        if (cv.GetChild(0) is Panel plate)
        {
            var picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
            picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            plate.AddChild(picture);
            PackCards.Thumb(store, pack, tex => { if (GodotObject.IsInstanceValid(picture)) picture.Texture = tex; });
        }
        var pname = pack.Name;
        var more = MoreBtn("Move to a folder, export, publish, remove (also on right-click)");
        more.Pressed += () => _t.Items.Open(more.GetGlobalRect().Position + new Vector2(0, 26), store, pack: true, id, pname, Refresh);
        nameRow.AddChild(more);
        cv.AddChild(nameRow);
        var by = pack.Source.Authors.Count > 0 ? " · by " + string.Join(", ", pack.Source.Authors) : "";
        var line = CardLine($"{PieceCount(pack.Pieces.Count)}{by} · {Origin(id, pack)} · {Math.Max(1, FolderBytes(store.PackageDir(id)) >> 20)} MB", Muted);
        line.MouseFilter = Control.MouseFilterEnum.Pass;
        line.TooltipText = pack.Description ?? "";
        cv.AddChild(line);
        AddActions(page, card, cv, id);
        page.Picks.Add(id, card);
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                if (Gathering(mb)) { page.Picks.Click(id, mb); return; }
                bool both = page.BothClicksOn(card, mb);
                Choose(page, card);
                page.Picks.Click(id, mb);
                if (both) _main.OpenEditor(id);
            }
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rb)
            {
                if (page.Picks.ForMenu(id)) { page.Picks.Menu(_main, rb.GlobalPosition); return; }
                page.Pressed = null;
                Choose(page, card);
                _t.Items.Open(rb.GlobalPosition, store, pack: true, id, pname, Refresh);
            }
        };
        return card;
    }

    static long FolderBytes(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    static string Origin(string id, PackManifest pack) =>
        PackManifest.IsUserPackId(id) ? "your pieces"
        : pack.Partial ? "from a scenario (only its pieces)"
        : pack.Source.WorkshopId != null || id.StartsWith("tts.") || id.StartsWith("iks.") ? "from TTS"
        : "installed";

    PanelContainer DamagedCard(Cards page, PackageStore store, string id)
    {
        void Refresh() { _t.Select(2); Show(); }
        var (card, cv, nameRow) = CardFace(null, id, plate: true);
        var more = MoreBtn("Move to a folder, remove (also on right-click)");
        more.Pressed += () => _t.Items.Open(more.GetGlobalRect().Position + new Vector2(0, 26), store, pack: true, id, id, Refresh, readable: false);
        nameRow.AddChild(more);
        cv.AddChild(nameRow);
        var line = CardLine("Damaged: its pack.json cannot be read", Warning);
        line.MouseFilter = Control.MouseFilterEnum.Pass;
        line.TooltipText = store.Problem(id) ?? "it cannot be read";
        cv.AddChild(line);
        page.Picks.Add(id, card);
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                if (!Gathering(mb)) Choose(page, card);
                page.Picks.Click(id, mb);
            }
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rb)
            {
                if (page.Picks.ForMenu(id)) { page.Picks.Menu(_main, rb.GlobalPosition); return; }
                Choose(page, card);
                _t.Items.Open(rb.GlobalPosition, store, pack: true, id, id, Refresh, readable: false);
            }
        };
        return card;
    }

    Control MakersLink()
    {
        var b = new Button { Text = "For makers", Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, TooltipText = "docs/makers.md" };
        b.AddThemeColorOverride("font_color", Muted);
        b.AddThemeColorOverride("font_hover_color", Ink);
        b.Pressed += () => GuideReader.Show(_main, "makers.md");
        return b;
    }
}
