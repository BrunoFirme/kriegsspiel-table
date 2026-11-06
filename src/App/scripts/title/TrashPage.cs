using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class TrashPage
{
    readonly TitleScreen _t;
    readonly Main _main;

    public TrashPage(TitleScreen t, Main main) { _t = t; _main = main; }

    static SaveStore Saves => Main.Saves;

    static string? SetsGameAside(TrashEntry e) => TrashShelves.SetsGameAside(Saves, e);

    public Control Link(bool packs)
    {
        int n = TrashShelves.Entries(Saves, packs).Count;
        var b = new Button { Text = n == 0 ? "Trash (empty)" : $"Trash ({n})", Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, Disabled = n == 0 };
        b.TooltipText = packs ? $"Packs removed in the last {SaveStore.TrashDays} days, to restore or delete for good" : $"Scenarios deleted, games started over and copies deleted in the last {SaveStore.TrashDays} days, to restore or delete for good";
        b.AddThemeColorOverride("font_color", Muted);
        b.AddThemeColorOverride("font_hover_color", Ink);
        b.Pressed += () => Show(packs);
        return b;
    }

    public void Show(bool packs)
    {
        var v = _t.NewPage(packs ? "Trash: packs" : "Trash: scenarios", "");
        var store = new PackageStore(PackageStore.DefaultRoot);
        var now = DateTime.UtcNow;
        var entries = TrashShelves.Entries(Saves, packs);
        var back = new Button { Text = packs ? "← Piece studio" : "← Scenarios", Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        back.Pressed += () =>
        {
            _main.RebuildLobby();
            if (packs) { _t.Select(2); _t.Packs.Show(); }
        };
        v.AddChild(back);
        if (entries.Count == 0) { v.AddChild(new Label { Text = "The trash is empty.", Modulate = Muted }); return; }
        void Refresh()
        {
            _main.RebuildLobby();
            _t.Select(packs ? 2 : 0);
            Show(packs);
        }
        var picks = new TitlePicks();
        picks.Act(n => $"Restore {n}", keys => RestoreMany(entries.Where(e => keys.Contains(e.Path)).ToList(), store, Refresh), primary: true);
        picks.Act(n => $"Delete {n} for good", keys => PurgeMany(entries.Where(e => keys.Contains(e.Path)).ToList(), Refresh));
        v.AddChild(picks.Bar);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", Ui.Gap.Group);
        scroll.AddChild(col);
        foreach (var e in entries)
        {
            var row = EntryRow(e, store, now, Refresh);
            picks.Add(e.Path, row);
            var key = e.Path;
            row.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: true } mb) return;
                if (mb.ButtonIndex == MouseButton.Left) picks.Click(key, mb);
                else if (mb.ButtonIndex == MouseButton.Right && picks.ForMenu(key)) picks.Menu(_main, mb.GlobalPosition);
            };
            col.AddChild(row);
        }
        var empty = new Button { Text = "Empty this trash", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        empty.Pressed += () => Ui.Confirm(_main, $"Empty the trash ({entries.Count} item{(entries.Count == 1 ? "" : "s")})", "Empty", () =>
        {
            int n = 0;
            foreach (var entry in entries) if (BestEffort.Try(() => Saves.DeleteTrashEntry(entry))) n++;
            Refresh();
            _t.Status.Text = $"Emptied the trash: {n} item{(n == 1 ? "" : "s")} gone.";
        });
        v.AddChild(empty);
    }

    void RestoreMany(IReadOnlyList<TrashEntry> many, PackageStore store, Action refresh)
    {
        void Go()
        {
            _main.CancelOpening();
            var said = new List<string>();
            foreach (var e in many)
            {
                try { said.Add(Saves.Restore(e, store.Root, Repeatable.UtcNow)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { said.Add($"{e.Describe()} could not be restored: {ex.Message}"); }
            }
            refresh();
            _t.Status.Text = string.Join(" ", said);
        }
        var aside = many.Select(SetsGameAside).OfType<string>().ToList();
        if (aside.Count > 0) Ui.Confirm(_main, $"Restore {many.Count}", "Restore", Go, string.Join(" ", aside));
        else Go();
    }

    void PurgeMany(IReadOnlyList<TrashEntry> many, Action refresh) =>
        Ui.Confirm(_main, $"Delete {many.Count} for good", "Delete", () =>
        {
            int n = many.Count(e => BestEffort.Try(() => Saves.DeleteTrashEntry(e)));
            refresh();
            _t.Status.Text = n == many.Count ? $"Deleted {n} for good." : $"Deleted {n} of {many.Count}; the rest are held by another program.";
        }, "They cannot be brought back.");

    PanelContainer EntryRow(TrashEntry entry, PackageStore store, DateTime now, Action refresh)
    {
        var (row, h) = Row(entry.Describe());
        var days = entry.DaysLeft(now);
        h.AddChild(new Label { Text = $"deleted {Ago(entry.Stamp.ToLocalTime())} · {(days == 0 ? "goes at the next start" : days == 1 ? "1 day left" : $"{days} days left")}", Modulate = Muted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true });
        var restore = new Button { Text = "Restore", FocusMode = Control.FocusModeEnum.None, TooltipText = "Put it back where it was" };
        restore.Pressed += () =>
        {
            void Go()
            {
                _main.CancelOpening();
                string msg;
                try { msg = Saves.Restore(entry, store.Root, Repeatable.UtcNow); }
                catch (Exception ex) { msg = "Could not restore: " + ex.Message; }
                refresh();
                _t.Status.Text = msg;
            }
            if (SetsGameAside(entry) is { } aside) Ui.Confirm(_main, $"Restore the {entry.Describe()}", "Restore", Go, aside);
            else Go();
        };
        h.AddChild(restore);
        var del = new Button { Text = "Delete now", FocusMode = Control.FocusModeEnum.None, TooltipText = "Gone for good" };
        del.Pressed += () => Ui.Confirm(_main, $"Delete the {entry.Describe()} for good", "Delete", () =>
        {
            try { Saves.DeleteTrashEntry(entry); _t.Status.Text = $"Deleted the {entry.Describe()}."; }
            catch (Exception ex) { _t.Status.Text = "Could not delete: " + ex.Message; }
            refresh();
        });
        h.AddChild(del);
        return row;
    }
}
