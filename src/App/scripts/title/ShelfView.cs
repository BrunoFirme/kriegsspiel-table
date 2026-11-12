using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed record ShelfShown(bool Pack, string Folder, IReadOnlyList<string> Present, List<string> Keys, Action Refresh, Func<string, IReadOnlyList<string>> Dragged,
    Func<string, string> Name, ShelfMarker Marker, Action<IReadOnlyList<string>> Trash);

sealed class ShelfView
{
    const string Payload = "shelf|";
    const char Lines = '\n';
    const float IntoFrom = 0.25f, IntoTo = 0.75f, FolderGlyph = 20;
    public const int ActionHeight = 40, ActionFont = 16;
    static readonly Vector2 MenuBelow = new(0, 26);

    readonly Main _main;
    public readonly Library Lib;
    readonly Dictionary<bool, string> _at = new() { [false] = "", [true] = "" };
    readonly Dictionary<bool, IReadOnlyList<string>> _present = new() { [false] = Array.Empty<string>(), [true] = Array.Empty<string>() };

    public ShelfView(Main main, Library lib)
    {
        _main = main;
        Lib = lib;
    }

    public static string Noun(bool pack) => pack ? "Packs" : "Scenarios";

    public string At(bool pack) => Lib.Of(pack).Has(_at[pack]) ? _at[pack] : _at[pack] = "";

    public void Go(bool pack, string folder) => _at[pack] = folder;

    public IReadOnlyList<string> Present(bool pack) => _present[pack];

    public IReadOnlyList<string> WithTrash(bool pack, IReadOnlyList<string> present) => At(pack).Length == 0 ? present.Append(Shelf.TrashKey).ToList() : present;

    public ShelfShown Begin(Control host, bool pack, IReadOnlyList<string> present, List<ShelfEntry> entries, Action refresh, TitlePicks picks, Func<string, string> name)
    {
        _present[pack] = present;
        var marker = new ShelfMarker();
        host.AddChild(marker);
        return new ShelfShown(pack, At(pack), present, entries.Select(e => e.Key).ToList(), refresh,
            key => picks.Count > 1 && picks.Picked.Contains(key) ? picks.Picked.Where(present.Contains).ToList() : new[] { key }, name, marker, picks.AskDelete);
    }

    public void FileNew(bool pack, string id)
    {
        var folder = At(pack);
        if (folder.Length == 0) return;
        Lib.MoveInto(pack, folder, _present[pack].Append(id).Distinct().ToList(), new[] { id });
    }

    void NewFolder(ShelfShown s) => TextPrompt.Open(_main, "New folder", "Name", "", n =>
    {
        if (Lib.AddFolder(s.Pack, s.Folder, n) != null) s.Refresh();
        else _main.Title.Status.Text = Library.CleanName(n).Length == 0 ? "A folder needs a name" : $"There is already a folder called {Library.CleanName(n)} here";
    });

    public Button NewFolderButton(ShelfShown s)
    {
        var b = new Button { Text = "New folder", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, ActionHeight),
            TooltipText = s.Folder.Length == 0 ? "A folder here; drag cards onto it to file them" : $"A folder in {Shelf.NameOf(s.Folder)}" };
        b.AddThemeFontSizeOverride("font_size", ActionFont);
        b.Pressed += () => NewFolder(s);
        return b;
    }

    public Control? Trail(ShelfShown s)
    {
        if (s.Folder.Length == 0) return null;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        Step(row, s, Noun(s.Pack), "");
        var at = "";
        foreach (var name in Shelf.Steps(s.Folder))
        {
            at = Shelf.Join(at, name);
            row.AddChild(new Label { Text = "/", Modulate = Ui.Faint });
            Step(row, s, name, at);
        }
        return row;
    }

    public PanelContainer TrashTile(ShelfShown s, Action open, Action<PanelContainer> choose)
    {
        int n = TrashPage.Count(s.Pack);
        var (card, cv, nameRow) = CardFace(null, "Trash", plate: true);
        if (cv.GetChild(0) is Panel plate) plate.AddChild(new ToolIcon { Kind = "trash", Color = Muted });
        cv.AddChild(nameRow);
        cv.AddChild(CardLine(n == 0 ? "Empty" : n == 1 ? "1 item" : $"{n} items", Muted));
        card.TooltipText = TrashPage.Holds(s.Pack);
        card.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            choose(card);
            if (mb.DoubleClick) open();
        };
        Arrange(s, card, Shelf.TrashKey, Shelf.TrashKey);
        return card;
    }

    void Step(HBoxContainer row, ShelfShown s, string name, string folder)
    {
        var b = Ui.Bare(new Button { Text = name, FocusMode = Control.FocusModeEnum.None });
        if (folder == s.Folder) b.AddThemeColorOverride("font_color", Ink);
        else b.AddThemeColorOverride("font_color", Muted);
        b.Pressed += () =>
        {
            Go(s.Pack, folder);
            s.Refresh();
        };
        if (folder != s.Folder)
            b.SetDragForwarding(Callable.From((Vector2 _) => new Variant()), Callable.From((Vector2 _, Variant d) =>
            {
                if (Keys(s, d) is not { } keys || !Takes(keys, folder)) return false;
                s.Marker.Into(b.GetGlobalRect());
                return true;
            }), Callable.From((Vector2 _, Variant d) => Into(s, folder, d)));
        row.AddChild(b);
    }

    public PanelContainer FolderTile(ShelfShown s, ShelfEntry e, Action<string, Action<Texture2D>> picture, Action<PanelContainer> choose)
    {
        var (card, cv, nameRow) = CardFace(null, e.Name, plate: true);
        var shelf = Lib.Of(s.Pack);
        if (cv.GetChild(0) is Panel plate)
        {
            plate.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            var inside = shelf.Inside(e.Path, s.Present, Mosaic.Most);
            var mosaic = new Mosaic { Count = inside.Count, Fit = s.Pack };
            mosaic.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            plate.AddChild(mosaic);
            for (int i = 0; i < inside.Count; i++)
            {
                int cell = i;
                picture(inside[i], tex => { if (GodotObject.IsInstanceValid(mosaic)) mosaic.Set(cell, tex); });
            }
        }
        if (nameRow.GetChild(0) is Label label) label.CustomMinimumSize = new Vector2(NameWidth - FolderGlyph - Ui.Gap.Tight, MoreWidth);
        nameRow.AddChild(new ToolIcon { Kind = "folder", Color = Muted, CustomMinimumSize = new Vector2(FolderGlyph, MoreWidth), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
        nameRow.MoveChild(nameRow.GetChild(nameRow.GetChildCount() - 1), 0);
        var more = MoreBtn("Open, rename, delete (also on right-click)");
        more.Pressed += () => FolderMenu(s, e, more.GetGlobalRect().Position + MenuBelow);
        nameRow.AddChild(more);
        cv.AddChild(nameRow);
        cv.AddChild(CardLine(Holds(s, e.Path), Muted));
        card.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Left)
            {
                choose(card);
                if (mb.DoubleClick) Open(s, e.Path);
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                choose(card);
                FolderMenu(s, e, mb.GlobalPosition);
            }
        };
        Arrange(s, card, e.Key, e.Path);
        return card;
    }

    string Holds(ShelfShown s, string folder)
    {
        var shelf = Lib.Of(s.Pack);
        int n = shelf.CountInside(folder, s.Present), folders = shelf.SubFolders(folder).Count();
        if (n > 0) return n == 1 ? (s.Pack ? "1 pack" : "1 scenario") : $"{n} {Noun(s.Pack).ToLowerInvariant()}";
        return folders == 0 ? "Empty" : folders == 1 ? "1 folder" : $"{folders} folders";
    }

    void Open(ShelfShown s, string folder)
    {
        Go(s.Pack, folder);
        s.Refresh();
    }

    void FolderMenu(ShelfShown s, ShelfEntry e, Vector2 at)
    {
        var pm = new PopupMenu();
        pm.AddItem("Open", 1);
        pm.AddItem("Rename…", 2);
        pm.AddSeparator();
        pm.AddItem("Delete folder…", 3);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1:
                    Open(s, e.Path);
                    break;
                case 2:
                    TextPrompt.Open(_main, "Rename folder", "Name", e.Name, n =>
                    {
                        if (Lib.RenameFolder(s.Pack, e.Path, n)) s.Refresh();
                        else _main.Title.Status.Text = $"There is already a folder called {Library.CleanName(n)} here";
                    });
                    break;
                case 3:
                    var parent = Shelf.ParentOf(e.Path);
                    Ui.Confirm(_main, $"Delete folder \"{e.Name}\"", "Delete", () =>
                    {
                        Lib.RemoveFolder(s.Pack, e.Path, s.Present);
                        s.Refresh();
                    }, $"What it holds moves to {(parent.Length == 0 ? Noun(s.Pack) : Shelf.NameOf(parent))}.");
                    break;
            }
        };
        pm.PopupHide += () => pm.QueueFree();
        _main.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    public PopupMenu MoveMenu(bool pack, Action<string> into, string? leaveOut = null)
    {
        var pm = new PopupMenu();
        var targets = new List<string> { "" };
        targets.AddRange(Lib.Of(pack).Folders.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase));
        targets.Remove(leaveOut ?? "\0");
        for (int i = 0; i < targets.Count; i++) pm.AddItem(targets[i].Length == 0 ? Noun(pack) : targets[i].Replace("/", " / "), i);
        pm.IdPressed += id => into(targets[(int)id]);
        return pm;
    }

    public void Arrange(ShelfShown s, Control card, string? key, string? folder = null) =>
        card.SetDragForwarding(Callable.From((Vector2 _) =>
        {
            if (key == null) return new Variant();
            var keys = s.Dragged(key);
            card.SetDragPreview(Chip(keys.Count == 1 ? Shown(s, keys[0]) : $"{keys.Count} items"));
            return Variant.From(Payload + (s.Pack ? "p" : "s") + Lines + string.Join(Lines, keys));
        }), Callable.From((Vector2 _, Variant d) => Hover(s, card, key, folder, d)), Callable.From((Vector2 _, Variant d) => Dropped(s, card, key, folder, d)));

    public void ArrangeEnd(ShelfShown s, Control grid)
    {
        grid.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb) return;
            var pm = new PopupMenu();
            pm.AddItem("New folder…");
            pm.IdPressed += _ => NewFolder(s);
            pm.PopupHide += () => pm.QueueFree();
            _main.AddChild(pm);
            pm.Popup(new Rect2I((Vector2I)mb.GlobalPosition.Round(), Vector2I.Zero));
        };
        grid.SetDragForwarding(Callable.From((Vector2 _) => new Variant()), Callable.From((Vector2 _, Variant d) =>
        {
            if (Keys(s, d) == null) return false;
            if (grid.GetChildCount() > 0 && grid.GetChild(grid.GetChildCount() - 1) is Control last) s.Marker.Between(last.GetGlobalRect(), left: false);
            return true;
        }), Callable.From((Vector2 _, Variant d) =>
        {
            if (Keys(s, d) is not { } keys) return;
            Lib.Drop(s.Pack, s.Folder, s.Present, keys, s.Keys.Count);
            Redraw(s);
        }));
    }

    static void Redraw(ShelfShown s) => Callable.From(s.Refresh).CallDeferred();

    static Control Chip(string text)
    {
        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", CardStyle(true));
        chip.AddChild(new Label { Text = text });
        return chip;
    }

    static string Shown(ShelfShown s, string key) => Shelf.IsFolderKey(key) ? Shelf.NameOf(Shelf.PathOf(key)) : s.Name(key);

    static IReadOnlyList<string>? Keys(ShelfShown s, Variant d)
    {
        if (d.VariantType != Variant.Type.String || d.AsString() is not { } text || !text.StartsWith(Payload + (s.Pack ? "p" : "s") + Lines)) return null;
        var keys = text.Split(Lines).Skip(1).Where(k => k.Length > 0).ToList();
        return keys.Count == 0 ? null : keys;
    }

    static bool Takes(IReadOnlyList<string> keys, string folder) => folder == Shelf.TrashKey
        ? keys.All(k => !Shelf.IsFolderKey(k) && !Shelf.IsFixed(k))
        : !keys.Any(k => Shelf.IsFixed(k) || Shelf.IsFolderKey(k) && (folder == Shelf.PathOf(k) || folder.StartsWith(Shelf.PathOf(k) + "/", StringComparison.Ordinal)));

    static float Across(Control card) => Pointer.Local(card).X / Math.Max(1, card.Size.X);

    public static (int slot, string? into) Aim(ShelfShown s, string? key, string? folder, float across, IReadOnlyList<string> keys)
    {
        if (folder != null && across > IntoFrom && across < IntoTo && Takes(keys, folder)) return (-1, folder);
        if (key == null) return (0, null);
        int i = s.Keys.IndexOf(key);
        return (across < 0.5f ? i : i + 1, null);
    }

    bool Hover(ShelfShown s, Control card, string? key, string? folder, Variant d)
    {
        if (Keys(s, d) is not { } keys) return false;
        var (slot, into) = Aim(s, key, folder, Across(card), keys);
        if (into != null) s.Marker.Into(card.GetGlobalRect());
        else s.Marker.Between(card.GetGlobalRect(), left: key != null && slot == s.Keys.IndexOf(key));
        return true;
    }

    void Dropped(ShelfShown s, Control card, string? key, string? folder, Variant d)
    {
        if (Keys(s, d) is { } keys) Drop(s, key, folder, Across(card), keys);
    }

    public void Drop(ShelfShown s, string? key, string? folder, float across, IReadOnlyList<string> keys)
    {
        var (slot, into) = Aim(s, key, folder, across, keys);
        if (into == Shelf.TrashKey)
        {
            Callable.From(() => s.Trash(keys)).CallDeferred();
            return;
        }
        if (into != null) Lib.MoveInto(s.Pack, into, s.Present, keys);
        else Lib.Drop(s.Pack, s.Folder, s.Present, keys, slot);
        Redraw(s);
    }

    void Into(ShelfShown s, string folder, Variant d)
    {
        if (Keys(s, d) is not { } keys) return;
        Lib.MoveInto(s.Pack, folder, s.Present, keys.Where(k => !Shelf.IsFixed(k)).ToList());
        Redraw(s);
    }
}

sealed partial class ShelfMarker : Control
{
    const float Bar = 3;
    const ulong Stale = 2;
    static Color IntoFill => new(Ui.Accent, 0.22f);

    bool _into;
    ulong _seen;
    Vector2 _pointer;

    public ShelfMarker()
    {
        TopLevel = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = 50;
    }

    public void Into(Rect2 target)
    {
        _into = true;
        Put(target);
    }

    public void Between(Rect2 card, bool left)
    {
        _into = false;
        float half = Ui.Gap.Block / 2f;
        float x = left ? card.Position.X - half - Bar / 2 : card.End.X + half - Bar / 2;
        Put(new Rect2(x, card.Position.Y, Bar, card.Size.Y));
    }

    void Put(Rect2 r)
    {
        GlobalPosition = r.Position;
        Size = r.Size;
        Visible = true;
        _seen = Engine.GetProcessFrames();
        _pointer = Pointer.In(GetViewport());
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        bool left = Engine.GetProcessFrames() - _seen > Stale && Pointer.In(GetViewport()).DistanceTo(_pointer) > 1;
        if (!GetViewport().GuiIsDragging() || left) Visible = false;
    }

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), _into ? IntoFill : Ui.Ink);
}
