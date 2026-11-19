using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
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
        void Again() { _t.Select(2); Show(); }
        var v = _t.NewPage("Piece studio", "", wide: true);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        v.AddChild(actions);
        var store = new PackageStore(PackageStore.DefaultRoot);
        var page = new Cards();
        var ids = store.ListPackIds().Where(id => !StandardPacks.IsStandard(id)).OrderBy(id => store.Pack(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase).ToList();
        string NameOf(string id) => store.Pack(id)?.Name ?? id;
        var shelf = _t.Shelf;
        shelf.Lib.NoteArrivals(true, ids, id => ShelfView.ArrivedAt(store.PackageDir(id)));
        var sort = shelf.Sort(true, NameOf, _ => null);
        var shelved = shelf.WithTrash(true, ids);
        var entries = shelf.Lib.Arranged(true, shelf.At(true), shelved, sort);
        var shown = shelf.Begin(v, pack: true, shelved, entries, Again, page.Picks, NameOf);
        var grid = Grid();
        var find = new ShelfFind(shelf, shown, sort, grid, (id, where) => FoundCard(page, store, shown, id, where))
        {
            Changed = () => { page.Picks.Clear(); Choose(page, null); },
        };
        actions.AddChild(shelf.NewFolderButton(shown));
        actions.AddChild(find.Box);
        actions.AddChild(shelf.OrderMenu(shown));
        if (shelf.Trail(shown) is { } trail) v.AddChild(trail);
        PickActions(page, store, ids);
        v.AddChild(page.Picks.Bar);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
        v.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Block));
        scroll.AddChild(col);

        col.AddChild(find.None);
        col.AddChild(grid);
        var plus = NewPackCard();
        shelf.Arrange(shown, plus, null);
        grid.AddChild(plus);
        foreach (var e in entries)
        {
            if (e.Key == Shelf.TrashKey)
            {
                var bin = shelf.TrashTile(shown, () => _t.Trash.Show(packs: true), c =>
                {
                    page.Picks.Clear();
                    Choose(page, c);
                });
                page.All.Add(bin);
                grid.AddChild(bin);
                continue;
            }
            if (e.Folder)
            {
                var tile = shelf.FolderTile(shown, e, (id, show) => { if (store.Pack(id) is { } inside) PackCards.Cover(store, inside, show); }, c =>
                {
                    page.Picks.Clear();
                    Choose(page, c);
                });
                page.All.Add(tile);
                grid.AddChild(tile);
                continue;
            }
            var card = FoundCard(page, store, shown, e.Key, "");
            find.Card(e.Key, card);
            grid.AddChild(card);
        }
        shelf.ArrangeEnd(shown, grid);
        if (entries.Count == 0 && shown.Folder.Length > 0) grid.AddChild(new Label { Text = "Empty", Modulate = Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        foot.AddChild(MakersLink());
        v.AddChild(foot);
        find.Ready();
    }

    PanelContainer FoundCard(Cards page, PackageStore store, ShelfShown shown, string id, string where)
    {
        var pack = store.Pack(id);
        var card = pack == null ? DamagedCard(page, store, id) : PackCard(page, store, id, pack, where);
        _t.Shelf.Arrange(shown, card, id);
        page.All.Add(card);
        return card;
    }

    static bool Gathering(InputEventMouseButton mb) => mb.CtrlPressed || mb.MetaPressed || mb.ShiftPressed;

    void PickActions(Cards page, PackageStore store, IReadOnlyList<string> present)
    {
        page.Picks.Changed = () => { if (page.Picks.Count > 1) foreach (var row in page.Actions.Values) row.Visible = false; };
        page.Picks.Act(n => $"Move {n} to…", keys =>
        {
            var pm = _t.Shelf.MoveMenu(true, folder =>
            {
                _t.Shelf.Lib.MoveInto(true, folder, present, keys.ToList());
                _t.Select(2);
                Show();
            });
            pm.PopupHide += () => pm.QueueFree();
            _main.AddChild(pm);
            pm.Popup(new Rect2I((Vector2I)_main.GetViewport().GetMousePosition().Round(), Vector2I.Zero));
        });
        page.Picks.Act(n => $"Remove {n}…", keys => Ui.Confirm(_main, keys.Count == 1 ? "Remove 1 pack" : $"Remove {keys.Count} packs", "Remove", () =>
        {
            _main.CancelOpening();
            var failed = new List<string>();
            foreach (var k in keys)
            {
                try { Main.Saves.TrashPack(store.Root, k, Repeatable.UtcNow); _t.Shelf.Lib.Forget(true, k); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{k}: {e.Message}"); }
            }
            _main.RebuildLobby();
            _t.Select(2);
            Show();
            _t.Status.Say(failed.Count == 0 ? $"Removed {keys.Count}. They are in the trash for {SaveStore.TrashDays} days."
                : $"Removed {keys.Count - failed.Count} of {keys.Count}; not removed: {string.Join("; ", failed)}", failed: failed.Count > 0);
        }, $"They go to the trash for {SaveStore.TrashDays} days; scenarios that use them show them as missing."), deletes: true);
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
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        var edit = Primary("Edit pieces", () => _main.OpenEditor(id));
        edit.TooltipText = "Open the pack on the editor table";
        edit.CustomMinimumSize = Ui.Px(110, 34);
        edit.AddThemeFontSizeOverride("font_size", Ui.Px(14));
        row.AddChild(edit);
        face.AddChild(row);
        page.Actions[card] = row;
    }

    PanelContainer PackCard(Cards page, PackageStore store, string id, PackManifest pack, string where)
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
        more.Pressed += () => _t.Items.Open(more.GetGlobalRect().Position + Ui.Px(0, 26), store, pack: true, id, pname, Refresh);
        nameRow.AddChild(more);
        cv.AddChild(nameRow);
        var by = pack.Source.Authors.Count > 0 ? " · by " + string.Join(", ", pack.Source.Authors) : "";
        var place = where.Length > 0 ? " · " + where : "";
        var line = CardLine($"{PieceCount(pack.Pieces.Count)}{by} · {Origin(id, pack)} · {Math.Max(1, FolderBytes(store.PackageDir(id)) >> 20)} MB{place}", Muted);
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
        : pack.Source.WorkshopId != null || id.StartsWith("tts.") || id.StartsWith("iks.") ? "converted"
        : "installed";

    PanelContainer DamagedCard(Cards page, PackageStore store, string id)
    {
        void Refresh() { _t.Select(2); Show(); }
        var (card, cv, nameRow) = CardFace(null, id, plate: true);
        var more = MoreBtn("Move to a folder, remove (also on right-click)");
        more.Pressed += () => _t.Items.Open(more.GetGlobalRect().Position + Ui.Px(0, 26), store, pack: true, id, id, Refresh, readable: false);
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
