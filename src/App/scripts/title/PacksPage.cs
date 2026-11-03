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

    public void Show()
    {
        var v = _t.NewPage("Packs and Pieces", "");
        v.AddChild(Actions());
        v.AddChild(_t.Folders.Strip(() => { _t.Select(2); Show(); }));

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(col);
        var store = new PackageStore(PackageStore.DefaultRoot);
        var picks = new TitlePicks();
        PickActions(picks, store);
        v.AddChild(picks.Bar);
        v.MoveChild(picks.Bar, scroll.GetIndex());
        var ids = store.ListPackIds().Where(id => !StandardPacks.IsStandard(id)).OrderBy(id => store.Pack(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) col.AddChild(new Label { Text = "No packs installed yet.", Modulate = Muted });
        foreach (var (folder, items) in _t.Folders.Grouped(ids, id => id, pack: true))
        {
            if (items.Count == 0 && folder.Length == 0) continue;
            col.AddChild(Section(folder.Length > 0 ? folder : _t.Folders.Lib.Folders.Count > 0 ? "Unfiled" : "Installed packs"));
            if (items.Count == 0) col.AddChild(new Label { Text = "Nothing in this folder. Use Move to on a pack.", Modulate = Muted });
            foreach (var id in items)
            {
                var pack = store.Pack(id);
                var row = pack == null ? DamagedPackRow(store, id) : PackRow(store, id, pack);
                Pickable(picks, id, row, pack == null ? null : () => _t.Items.Open(_main.GetViewport().GetMousePosition(), store, pack: true, id, pack.Name, () => { _t.Select(2); Show(); }));
                col.AddChild(row);
            }
        }
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 4);
        foot.AddChild(MakersLink());
        foot.AddChild(_t.Trash.Link(packs: true));
        v.AddChild(foot);
    }

    HBoxContainer Actions()
    {
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
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
        var tts = new Button { Text = "Import from Tabletop Simulator…", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), TooltipText = "A TTS module or save (local, or a Workshop link) becomes a pack and a scenario" };
        tts.AddThemeFontSizeOverride("font_size", 16);
        tts.Pressed += () =>
        {
            var dlg = new ImportDialog(PackageStore.DefaultRoot, (sc, pk) =>
            {
                _main.RebuildLobby(); _t.Select(2); Show();
                _t.Status.Text = sc.Length > 0 ? $"Imported '{sc}': its pack is below and the scenario is under Host a game." : $"Installed the pack {pk}.";
            });
            Ui.Open(_main, dlg);
        };
        actions.AddChild(tts);
        var newPack = new Button { Text = "New pack…", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), TooltipText = "Start an empty pack and make its pieces: blocks drawn in the app, or your own images" };
        newPack.AddThemeFontSizeOverride("font_size", 16);
        newPack.Pressed += () => NewPackDialog.Open(_main, _t);
        actions.AddChild(newPack);
        return actions;
    }

    void PickActions(TitlePicks picks, PackageStore store)
    {
        picks.Act(n => $"Move {n} to…", keys =>
        {
            var pm = TitlePicks.FolderMenu(_t.Folders.Lib, folder =>
            {
                foreach (var k in keys) _t.Folders.Lib.Move(true, k, folder);
                _t.Select(2); Show();
            });
            _main.AddChild(pm);
            pm.Popup(new Rect2I((Vector2I)_main.GetViewport().GetMousePosition().Round(), Vector2I.Zero));
        });
        picks.Act(n => $"Remove {n}…", keys => Ui.Confirm(_main, $"Remove {keys.Count} packs", "Remove", () =>
        {
            _main.CancelOpening();
            var failed = new List<string>();
            foreach (var k in keys)
            {
                try { Main.Saves.TrashPack(store.Root, k, Repeatable.UtcNow); _t.Folders.Lib.Forget(true, k); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{k}: {e.Message}"); }
            }
            _main.RebuildLobby();
            _t.Select(2); Show();
            _t.Status.Text = failed.Count == 0 ? $"Removed {keys.Count}. They are in the trash for {SaveStore.TrashDays} days."
                : $"Removed {keys.Count - failed.Count} of {keys.Count}; not removed: {string.Join("; ", failed)}";
        }, $"They go to the trash for {SaveStore.TrashDays} days; scenarios that use them show them as missing."));
    }

    void Pickable(TitlePicks picks, string id, PanelContainer row, Action? menu)
    {
        picks.Add(id, row);
        row.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Left) picks.Click(id, mb);
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                if (picks.ForMenu(id)) picks.Menu(_main, mb.GlobalPosition);
                else menu?.Invoke();
            }
        };
    }

    PanelContainer PackRow(PackageStore store, string id, PackManifest pack)
    {
        void Refresh() { _t.Select(2); Show(); }
        var origin = PackManifest.IsUserPackId(id) ? "your pieces" : pack.Partial ? "from a scenario (only its pieces)" : pack.Source.WorkshopId != null || id.StartsWith("tts.") || id.StartsWith("iks.") ? "from Tabletop Simulator" : "installed";
        long bytes = Directory.Exists(store.PackageDir(id)) ? Directory.EnumerateFiles(store.PackageDir(id), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        var (row, h) = Row(pack.Name);
        var by = pack.Source.Authors.Count > 0 ? " · by " + string.Join(", ", pack.Source.Authors) : "";
        h.AddChild(new Label { Text = $"{pack.Pieces.Count} pieces{by} · {origin} · {Math.Max(1, bytes >> 20)} MB", Modulate = Muted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = pack.Description ?? "" });
        var pid = id; var pname = pack.Name;
        var edit = new Button { Text = "Edit pieces", FocusMode = Control.FocusModeEnum.None, TooltipText = "Open the pack on the editor table" };
        edit.Pressed += () => _main.OpenEditor(pid);
        h.AddChild(edit);
        var more = MoreBtn("Move to a folder, export, publish, remove (also on right-click)");
        more.Pressed += () => _t.Items.Open(more.GetGlobalRect().Position + new Vector2(0, 28), store, pack: true, pid, pname, Refresh);
        h.AddChild(more);
        return row;
    }

    PanelContainer DamagedPackRow(PackageStore store, string id)
    {
        var (row, h) = Row(id);
        var why = store.Problem(id) ?? "it cannot be read";
        h.AddChild(new Label { Text = "Damaged: its pack.json cannot be read", Modulate = Warning, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, TooltipText = why, MouseFilter = Control.MouseFilterEnum.Pass });
        var remove = new Button { Text = "Remove…", FocusMode = Control.FocusModeEnum.None, TooltipText = $"Put it in the trash (kept {SaveStore.TrashDays} days); install it again to have it back" };
        remove.Pressed += () => _t.Items.AskDelete(store, pack: true, id, id);
        h.AddChild(remove);
        return row;
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
