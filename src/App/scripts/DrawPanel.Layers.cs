using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class DrawPanel
{
    VBoxContainer? _layers;
    const int MostLayersShown = 6;
    const float LayerRowHeight = 24;

    void AddLayers()
    {
        _box.AddChild(Header("Layers"));
        _layers = new VBoxContainer();
        _layers.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        _box.AddChild(_layers);
        FillLayers();
    }

    public void RebuildLayers()
    {
        if (_layers == null || !GodotObject.IsInstanceValid(_layers) || T == null) return;
        FillLayers();
        Panel?.Fit();
    }

    void FillLayers()
    {
        foreach (var c in _layers!.GetChildren()) { _layers.RemoveChild(c); c.QueueFree(); }
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        var boards = S.View.Boards;
        _grip = S.IsUmpire ? new RowGrip(rows, HudNode, "layer-row:", boards.Select(b => b.Id), BoardEdits.Pinned(S.View), LayerGripTip,
            (id, to) => S.Do(new MoveBoard { Id = id, Index = to })) : null;
        for (int place = 0; place < boards.Count; place++) rows.AddChild(LayerRow(boards[place], place));
        if (S.View.Boards.Count > MostLayersShown)
        {
            var scroll = new ScrollContainer
            {
                CustomMinimumSize = Ui.Px(0, MostLayersShown * (LayerRowHeight + Ui.Gap.Hair)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            scroll.AddChild(rows);
            _layers.AddChild(scroll);
        }
        else _layers.AddChild(rows);
        _layers.AddChild(LayerFoot());
    }

    HBoxContainer LayerFoot()
    {
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        var add = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(0, LayerRowHeight), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "A fresh sheet of tracing paper over the map" };
        add.AddChild(new ToolIcon { Kind = "plus", Color = Ui.Muted });
        add.Pressed += () => S.Do(new AddBoard { Index = 0 });
        foot.AddChild(add);
        var board = S.View.Boards.FirstOrDefault(b => b.Id == T.DrawBoard());
        var wipe = RowIcon("erase", $"Rub out everything on {board?.Name ?? "this layer"}");
        wipe.Pressed += () =>
        {
            int n = T.DrawingCount();
            if (n == 0) { Log("Nothing drawn on this layer."); return; }
            Ui.Confirm(HudNode, $"Rub out all {n} drawings on \"{board?.Name}\"?", "Rub out", T.EraseAllDrawings, "There is no undo.");
        };
        foot.AddChild(wipe);
        return foot;
    }

    int DrawnOn(string id) => S.View.Drawings.Values.Sum(l => l.Count(s => s.Board == id));

    void LayersChanged()
    {
        Rebuild();
        Strip?.SyncPenLock();
    }

    HBoxContainer LayerRow(BoardDef b, int place)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        var id = b.Id;
        bool locked = T.DrawLockedToSight && !b.Sight;
        var pick = new Button
        {
            Text = b.Name, ToggleMode = true, ButtonPressed = T.DrawBoard() == id, ClipText = true, Disabled = locked, Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = Ui.Px(84, 22),
            TooltipText = locked ? "Drawing stays on the Sight layer while POV capture is open"
                : b.Sight ? "Draw on this layer · double-click to rename · the POV tool's kept lines, always on top, never in a picture" : "Draw on this layer · double-click to rename",
        };
        pick.Pressed += () =>
        {
            T.Board = id;
            T.CheckDrawingLock();
            FitPenToLayer();
            LayersChanged();
        };
        pick.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) AskLayerName(id, b.Name);
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) LayerMenu(b, pick);
        };
        row.AddChild(pick);
        int on = DrawnOn(id);
        var tally = new Label
        {
            Text = on > 0 ? on.ToString() : "", CustomMinimumSize = Ui.Px(TallyWidth, 0), HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted, MouseFilter = on > 0 ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore,
            TooltipText = on == 0 ? "" : on == 1 ? "1 drawing on this layer" : $"{on} drawings on this layer",
        };
        tally.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        row.AddChild(tally);
        row.AddChild(Eye(id, b.Sight));
        var more = RowIcon("more", "Rename, copy or remove");
        more.Pressed += () => LayerMenu(b, more);
        row.AddChild(more);
        if (_grip != null) row.AddChild(_grip.Handle(place, b.Name));
        _grip?.Takes(row, place);
        return row;
    }

    static Button RowIcon(string kind, string tip) => Ui.RowIcon(kind, tip);

    const string LayerGripTip = "Drag to restack: an upper layer draws over a lower · right-click to move it a step";
    RowGrip? _grip;

    internal RowGrip? LayerGrip => _grip;

    void SyncDrop() => _grip?.Sync();

    Button Eye(string id, bool sight)
    {
        bool shown = !T.HiddenBoards.Contains(id);
        var eye = RowIcon(shown ? "eye" : "eye-off", shown ? "Hide this layer (on your screen only)" : "Show it again");
        eye.Pressed += () =>
        {
            T.ShowBoard(id, !shown);
            T.RefreshDrawings();
            if (sight)
            {
                T.Sighting.TableChanged();
                T.RefreshSightingPanel();
            }
            LayersChanged();
        };
        return eye;
    }

    const int TallyWidth = 22;

    internal enum LayerItem { Rename, Copy, Remove }

    void LayerMenu(BoardDef b, Control under)
    {
        var menu = new PopupMenu();
        menu.AddItem("Rename", (int)LayerItem.Rename);
        menu.AddItem("Copy", (int)LayerItem.Copy);
        menu.AddSeparator();
        menu.AddItem(S.View.Boards.Count == 1 ? "Clear" : "Remove", (int)LayerItem.Remove);
        menu.IdPressed += item => LayerDo(b.Id, (LayerItem)(int)item);
        Ui.PopupBelow(HudNode, menu, under);
    }

    internal void LayerDo(string id, LayerItem item)
    {
        if (S.View.Boards.FirstOrDefault(x => x.Id == id) is not { } b) return;
        switch (item)
        {
            case LayerItem.Rename:
                AskLayerName(id, b.Name);
                break;
            case LayerItem.Copy:
                if (BoardCopy.Of(S.View, id, "x-b" + System.Guid.NewGuid().ToString("N")[..10]) is not { } made) return;
                S.Do(made);
                LayersChanged();
                break;
            case LayerItem.Remove:
                AskRemoveLayer(b);
                break;
        }
    }

    void AskRemoveLayer(BoardDef b)
    {
        var id = b.Id;
        int n = DrawnOn(id);
        string drawn = n == 0 ? "" : n == 1 ? "The drawing on it goes too." : $"The {n} drawings on it go too.";
        if (S.View.Boards.Count == 1) Ui.Confirm(HudNode, $"Clear \"{b.Name}\"?", "Clear", () => S.Do(new RemoveBoard { Id = id }), n == 0 ? "It gets its name back." : drawn + " It gets its name back.");
        else Ui.Confirm(HudNode, $"Remove \"{b.Name}\"?", "Remove", () => S.Do(new RemoveBoard { Id = id }), n == 0 ? null : drawn);
    }

    void AskLayerName(string id, string was)
    {
        var dlg = new ConfirmationDialog { Title = "Rename layer", OkButtonText = "Rename" };
        var edit = new LineEdit { Text = was, CustomMinimumSize = Ui.Px(240, 0) };
        dlg.AddChild(edit);
        dlg.Confirmed += () => { if (edit.Text.Trim().Length > 0) S.Do(new RenameBoard { Id = id, Name = edit.Text.Trim() }); };
        Ui.Open(HudNode, dlg);
        Ui.FreeOnClose(dlg);
        edit.GrabFocus();
        edit.SelectAll();
    }
}
