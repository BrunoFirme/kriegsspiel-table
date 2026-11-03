using System;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

sealed class ItemMenu
{
    readonly TitleScreen _t;
    readonly Main _main;
    readonly IPlatform _platform;

    public ItemMenu(TitleScreen t, Main main, IPlatform platform) { _t = t; _main = main; _platform = platform; }

    Library Lib => _t.Folders.Lib;

    public void Open(Vector2 at, PackageStore store, bool pack, string id, string name, Action refresh, bool readable = true)
    {
        var pm = new PopupMenu();
        var move = new PopupMenu { Name = "move" };
        var current = Lib.FolderOf(pack, id);
        move.AddCheckItem("Unfiled", 1000); move.SetItemChecked(0, current.Length == 0);
        for (int i = 0; i < Lib.Folders.Count; i++) { move.AddCheckItem(Lib.Folders[i], 1001 + i); move.SetItemChecked(i + 1, Lib.Folders[i] == current); }
        move.AddSeparator();
        move.AddItem("New folder…", 1999);
        move.IdPressed += mid =>
        {
            if (mid == 1999) TextPrompt.Open(_main, "New folder", "Name", "", n => { if (Lib.AddFolder(n)) { Lib.Move(pack, id, Library.CleanName(n)); refresh(); } });
            else { Lib.Move(pack, id, mid == 1000 ? "" : Lib.Folders[(int)mid - 1001]); refresh(); }
        };
        pm.AddChild(move);
        pm.AddSubmenuNodeItem("Move to", move);
        if (!pack && Main.Saves.HasGame(id)) pm.AddItem("Saves…", 5);
        if (readable) pm.AddItem("Export…", 1);
        bool scripted = readable && (pack ? store.Pack(id)?.Pieces.Any(d => d.Script != null) == true : File.Exists(Path.Combine(store.Root, "scenarios", id, "scenario.json")));
        if (scripted) pm.AddItem("Check its scripts…", 4);
        if (readable && pack && _platform.Available) pm.AddItem("Publish to the Steam Workshop…", 2);
        pm.AddSeparator();
        pm.AddItem(pack ? "Remove from this PC…" : "Delete…", 3);
        pm.IdPressed += mid =>
        {
            switch (mid)
            {
                case 1:
                {
                    var dlg = new ExportDialog(store, pack ? "pack" : "scenario", id);
                    dlg.Done += m => _t.Status.Text = m;
                    Ui.Open(_main, dlg);
                    break;
                }
                case 2:
                {
                    var dlg = new PublishDialog(_platform, store, "pack", id);
                    dlg.Finished += m => _t.Status.Text = m;
                    Ui.Open(_main, dlg);
                    break;
                }
                case 3: AskDelete(store, pack, id, name); break;
                case 4: CheckScripts(store, pack, id, name); break;
                case 5: _main.HostAndOpenSaves(id); break;
            }
        };
        pm.PopupHide += () => pm.QueueFree();
        _main.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    public void Mirror(Vector2 at, string path, string name, Action refresh)
    {
        var pm = new PopupMenu();
        pm.AddItem("Delete this copy…", 1);
        pm.IdPressed += _ => Ui.Confirm(_main, $"Delete the copy of \"{name}\"", "Delete", () =>
        {
            _main.CancelOpening();
            try { _t.Status.Text = $"Deleted the copy of {name}. It is in the trash for {SaveStore.TrashDays} days: {Main.Saves.TrashMirror(path, Repeatable.UtcNow)}"; }
            catch (Exception e) { _t.Status.Text = "Could not delete: " + e.Message; }
            refresh();
        });
        pm.PopupHide += () => pm.QueueFree();
        _main.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    public void AskDelete(PackageStore store, bool pack, string id, string name)
    {
        var body = pack ? null : Main.Saves.HasGame(id)
            ? $"Its saves go to the trash with it. After {SaveStore.TrashDays} days they are gone for good."
            : $"It goes to the trash. After {SaveStore.TrashDays} days it is gone for good.";
        Ui.Confirm(_main, pack ? $"Remove pack \"{name}\"" : $"Delete scenario \"{name}\"", pack ? "Remove" : "Delete", () =>
        {
            _main.CancelOpening();
            try
            {
                var entry = pack ? Main.Saves.TrashPack(store.Root, id, Repeatable.UtcNow) : Main.Saves.TrashScenario(store.Root, id, Repeatable.UtcNow);
                Lib.Forget(pack, id);
                _t.Status.Text = $"{(pack ? "Removed" : "Deleted")} {name}. It is in the trash for {SaveStore.TrashDays} days: {entry}";
            }
            catch (Exception e) { _t.Status.Text = "Could not delete: " + e.Message; }
            _main.RebuildLobby();
            if (pack) { _t.Select(2); _t.Packs.Show(); }
        }, body);
    }

    void CheckScripts(PackageStore store, bool pack, string id, string name)
    {
        var args = pack ? new[] { "check-scripts", store.Root, id } : new[] { "check-scripts", store.Root, Path.Combine(store.Root, "scenarios", id, "scenario.json") };
        ScriptCheckWindow.Run(_main, $"Scripts of {name}", args,
            pack ? "Every scripted piece of the pack, each on a table of its own away from any game: on_load, every button, three seconds of timers, on_save."
                 : "The scenario as hosted, away from any game: its global script, every piece's, every panel button, every hotkey and table menu row.");
    }
}
