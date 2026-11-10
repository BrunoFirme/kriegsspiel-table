using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using static KTable.App.TitleStyle;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class HostPage
{
    readonly TitleScreen _t;
    readonly Main _main;

    public HostPage(TitleScreen t, Main main) { _t = t; _main = main; }

    sealed class Cards
    {
        public string? Chosen;
        public readonly List<PanelContainer> All = new();
        public readonly Dictionary<PanelContainer, (HBoxContainer row, Button host, Button over)> Actions = new();
        public readonly Dictionary<string, string> InPlay = new();
        public readonly HashSet<string> Unreadable = new();
        public PanelContainer? Pressed;
        public readonly TitlePicks Picks = new();
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

    public void Show(PackageStore store, List<(string id, string path, Scenario? s)> scenarios)
    {
        void Again() { _t.Select(0); Show(store, scenarios); }
        var v = _t.NewPage("Scenarios", "", wide: true);
        var page = new Cards();
        var byId = scenarios.GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First());
        var present = scenarios.OrderBy(x => x.s?.Name ?? x.id, StringComparer.CurrentCultureIgnoreCase).Select(x => x.id).Distinct().ToList();
        var shelf = _t.Shelf;
        var shelved = shelf.WithTrash(false, present);
        var entries = shelf.Lib.Arranged(false, shelf.At(false), shelved);
        var shown = shelf.Begin(v, pack: false, shelved, entries, Again, page.Picks, id => byId.TryGetValue(id, out var x) ? x.s?.Name ?? id : id);
        var actions = new HBoxContainer();
        actions.AddChild(shelf.NewFolderButton(shown));
        v.AddChild(actions);
        if (shelf.Trail(shown) is { } trail) v.AddChild(trail);
        PickActions(page, store, scenarios, present);
        v.AddChild(page.Picks.Bar);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
        v.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", Ui.Gap.Block);
        scroll.AddChild(col);

        var grid = Grid();
        col.AddChild(grid);
        var plus = NewScenarioCard();
        shelf.Arrange(shown, plus, null);
        grid.AddChild(plus);
        foreach (var e in entries)
        {
            if (e.Key == Shelf.TrashKey)
            {
                var bin = shelf.TrashTile(shown, () => _t.Trash.Show(packs: false), c =>
                {
                    page.Picks.Clear();
                    Choose(page, null, c);
                });
                page.All.Add(bin);
                grid.AddChild(bin);
                continue;
            }
            if (e.Folder)
            {
                var tile = shelf.FolderTile(shown, e, (id, show) =>
                {
                    if (byId.TryGetValue(id, out var x) && x.s != null && ScenarioCards.Thumb(store, x.s, 512) is { } tex) show(tex);
                }, c =>
                {
                    page.Picks.Clear();
                    Choose(page, null, c);
                });
                page.All.Add(tile);
                grid.AddChild(tile);
                continue;
            }
            var (id, path, s) = byId[e.Key];
            var card = ScenarioCard(page, store, scenarios, id, path, s);
            shelf.Arrange(shown, card, id);
            page.All.Add(card);
            grid.AddChild(card);
        }
        shelf.ArrangeEnd(shown, grid);
        if (entries.Count == 0 && shown.Folder.Length > 0) grid.AddChild(new Label { Text = "Empty", Modulate = Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });

        var mirrors = shown.Folder.Length == 0 ? Main.Saves.Mirrors() : new List<string>();
        if (mirrors.Count > 0)
        {
            var mirrorHead = Section("Mirrored games");
            mirrorHead.MouseFilter = Control.MouseFilterEnum.Pass;
            mirrorHead.TooltipText = "Games you joined as a co-umpire, kept on this PC. Hosting one keeps its password and lets back in the people already let in.";
            col.AddChild(mirrorHead);
            var mirrored = Grid();
            col.AddChild(mirrored);
            foreach (var mp in mirrors)
            {
                var card = MirrorCard(page, store, scenarios, mp);
                page.All.Add(card);
                mirrored.AddChild(card);
            }
        }

    }

    static bool Gathering(InputEventMouseButton mb) => mb.CtrlPressed || mb.MetaPressed || mb.ShiftPressed;

    void PickActions(Cards page, PackageStore store, List<(string id, string path, Scenario? s)> scenarios, IReadOnlyList<string> present)
    {
        static bool IsMirror(string key) => key.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase);
        page.Picks.Changed = () => { if (page.Picks.Count > 1) foreach (var a in page.Actions.Values) a.row.Visible = false; };
        page.Picks.Act(n => $"Move {n} to…", keys =>
        {
            var pm = _t.Shelf.MoveMenu(false, folder =>
            {
                _t.Shelf.Lib.MoveInto(false, folder, present, keys.Where(k => !IsMirror(k)).ToList());
                _t.Select(0);
                Show(store, scenarios);
            });
            pm.PopupHide += () => pm.QueueFree();
            _main.AddChild(pm);
            pm.Popup(new Rect2I((Vector2I)_main.GetViewport().GetMousePosition().Round(), Vector2I.Zero));
        });
        page.Picks.Act(n => $"Delete {n}…", keys => Ui.Confirm(_main, keys.Count == 1 ? "Delete 1 item" : $"Delete {keys.Count} items", "Delete", () =>
        {
            _main.CancelOpening();
            var failed = new List<string>();
            foreach (var k in keys)
            {
                try
                {
                    if (IsMirror(k)) Main.Saves.TrashMirror(k, Repeatable.UtcNow);
                    else { Main.Saves.TrashScenario(store.Root, k, Repeatable.UtcNow); _t.Shelf.Lib.Forget(false, k); }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{Path.GetFileNameWithoutExtension(k)}: {e.Message}"); }
            }
            _main.RebuildLobby();
            _t.Status.Text = failed.Count == 0 ? $"Deleted {keys.Count}. They are in the trash for {SaveStore.TrashDays} days."
                : $"Deleted {keys.Count - failed.Count} of {keys.Count}; not deleted: {string.Join("; ", failed)}";
        }, $"They go to the trash with their saves, for {SaveStore.TrashDays} days."), deletes: true);
    }

    PanelContainer NewScenarioCard() =>
        PlusCard("New blank scenario…", "An empty table with its own name and clock: lay map sheets on it and place pieces", () => NewBlankScenarioDialog.Open(_main));

    const string OpensForYou = "Opens the table on this PC: yours alone, to plot or prepare, until you invite someone";

    void Choose(Cards page, string? target, PanelContainer? card)
    {
        page.Chosen = target;
        foreach (var c in page.All)
        {
            c.AddThemeStyleboxOverride("panel", CardStyle(c == card));
            if (page.Actions.TryGetValue(c, out var a)) a.row.Visible = c == card;
        }
        if (card == null || target == null || !page.Actions.TryGetValue(card, out var act)) return;
        act.host.Disabled = page.Unreadable.Contains(target) && !page.InPlay.ContainsKey(target);
        act.host.Text = target.EndsWith(SaveStore.Ext) ? "Host from the copy" : page.InPlay.ContainsKey(target) ? "Resume" : "Open";
        act.over.Visible = page.InPlay.ContainsKey(target) && !target.EndsWith(SaveStore.Ext) && !page.Unreadable.Contains(target);
    }

    void AddActions(Cards page, PanelContainer card, VBoxContainer face, string target)
    {
        var row = new HBoxContainer { Visible = false };
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var host = Primary("Open", () => _main.DoHost(target, page.InPlay.ContainsKey(target)));
        host.TooltipText = OpensForYou;
        host.CustomMinimumSize = new Vector2(110, 34);
        host.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(host);
        var over = new Button { Text = "New play", FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "Host the scenario from its start; the saved play stays where it is" };
        over.AddThemeFontSizeOverride("font_size", 13);
        over.Pressed += () =>
        {
            if (!page.InPlay.ContainsKey(target)) return;
            var store0 = new PackageStore(PackageStore.DefaultRoot);
            var (setup, keep, back) = Main.StartOverPlan(store0, target);
            Ui.Confirm(_main, "New play", "Start it", () => _main.StartOver(store0, target, setup, keep, m => _t.Status.Text = m),
                $"The game starts again in a new folder of saves; every save made so far stays. {back}");
        };
        row.AddChild(over);
        face.AddChild(row);
        page.Actions[card] = (row, host, over);
    }

    PanelContainer ScenarioCard(Cards page, PackageStore store, List<(string id, string path, Scenario? s)> scenarios, string id, string path, Scenario? s)
    {
        void Refresh() { _t.Select(0); Show(store, scenarios); }
        var (card, cv, nameRow) = CardFace(s != null ? ScenarioCards.Thumb(store, s, 512) : null, s?.Name ?? id, plate: true);
        var target = id;
        var sname = s?.Name ?? id;
        var readable = s != null;
        if (!readable) page.Unreadable.Add(id);
        var more = MoreBtn(readable ? "Move to a folder, export, delete (also on right-click)" : "Move to a folder, delete (also on right-click)");
        more.Pressed += () => _t.Items.Open(more.GetGlobalRect().Position + new Vector2(0, 26), store, pack: false, target, sname, Refresh, readable);
        nameRow.AddChild(more);
        cv.AddChild(nameRow);
        var meta = new HBoxContainer();
        meta.AddChild(CardLine(s != null ? PieceCount(ScenarioCards.Card(path).pieces) : "The scenario file is damaged and cannot be read", s != null ? Muted : Warning));
        cv.AddChild(meta);
        var save = Main.AutosavePath(id);
        if (Main.Saves.HasGame(id))
        {
            page.InPlay[id] = save;
            cv.AddChild(CardLine(InPlayText(save), Ink));
        }
        var missing = s != null ? WorkshopContent.MissingPacks(store, s) : new List<string>();
        if (missing.Count > 0) cv.AddChild(CardLine($"Missing {missing.Count} pack{(missing.Count == 1 ? "" : "s")}: {string.Join(", ", missing)}", Warning));
        AddActions(page, card, cv, target);
        page.Picks.Add(target, card);
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                if (Gathering(mb)) { page.Picks.Click(target, mb); return; }
                bool both = page.BothClicksOn(card, mb);
                Choose(page, target, card);
                page.Picks.Click(target, mb);
                if (both && (readable || page.InPlay.ContainsKey(target))) _main.DoHost(target, page.InPlay.ContainsKey(target));
            }
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rb)
            {
                if (page.Picks.ForMenu(target)) { page.Picks.Menu(_main, rb.GlobalPosition); return; }
                page.Pressed = null;
                Choose(page, target, card);
                _t.Items.Open(rb.GlobalPosition, store, pack: false, target, sname, Refresh, readable);
            }
        };
        return card;
    }

    PanelContainer MirrorCard(Cards page, PackageStore store, List<(string id, string path, Scenario? s)> scenarios, string mp)
    {
        void Refresh() { _t.Select(0); Show(store, scenarios); }
        var save = ScenarioCards.Save(mp);
        var ok = save.Error == null;
        var sc0 = ok ? scenarios.FirstOrDefault(x => x.s?.Id == save.ScenarioId).s : null;
        var target = mp;
        var mname = save.Name ?? Path.GetFileNameWithoutExtension(mp);
        var (card, cv, nameRow) = CardFace(sc0 != null ? ScenarioCards.Thumb(store, sc0, 512) : null, mname, plate: false);
        var more = MoreBtn("Delete this copy (also on right-click)");
        more.Pressed += () => _t.Items.Mirror(more.GetGlobalRect().Position + new Vector2(0, 26), target, mname, Refresh);
        nameRow.AddChild(more);
        cv.AddChild(nameRow);
        var what = !ok ? $"The copy cannot be read: {save.Error}" : $"Mirror of {save.MirrorOf ?? "a game"} · turn {save.Clock!.Turn}, {ShortClock(save.Clock)} · {Ago(File.GetLastWriteTime(mp))}";
        cv.AddChild(CardLine(what, Ink));
        if (ok && sc0 == null)
            cv.AddChild(CardLine("The scenario is not installed here: the map tiles you never looked at are missing until you install its .ktpack", Warning));
        page.InPlay[target] = target;
        AddActions(page, card, cv, target);
        page.Picks.Add(target, card);
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                if (Gathering(mb)) { page.Picks.Click(target, mb); return; }
                bool both = page.BothClicksOn(card, mb);
                Choose(page, target, card);
                page.Picks.Click(target, mb);
                if (both) _main.DoHost(target, true);
            }
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rb)
            {
                if (page.Picks.ForMenu(target)) { page.Picks.Menu(_main, rb.GlobalPosition); return; }
                page.Pressed = null;
                Choose(page, target, card);
                _t.Items.Mirror(rb.GlobalPosition, target, mname, Refresh);
            }
        };
        return card;
    }

    static string InPlayText(string live)
    {
        if (File.Exists(live))
        {
            if (ScenarioCards.Save(live) is { Clock: { } c }) return $"Saved · turn {c.Turn}, {ShortClock(c)} · {Ago(File.GetLastWriteTime(live))}";
        }
        var cps = Main.Saves.Checkpoints(live);
        if (cps.Count == 0) return $"Saved · the save cannot be read: {ScenarioCards.Save(live).Error}";
        return $"{(File.Exists(live) ? "Save damaged" : "Save missing")} · Resume continues from the {cps[0].Describe()}";
    }
}
