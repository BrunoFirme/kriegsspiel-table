using System.Linq;
using Godot;
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
        _layers.AddThemeConstantOverride("separation", Ui.Gap.Hair);
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
        rows.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        foreach (var b in S.View.Boards) rows.AddChild(LayerRow(b));
        if (S.View.Boards.Count > MostLayersShown)
        {
            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, MostLayersShown * (LayerRowHeight + Ui.Gap.Hair)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
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
        foot.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        var add = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, LayerRowHeight), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "A fresh sheet of tracing paper over the map" };
        add.Pressed += () => S.Do(new AddBoard());
        foot.AddChild(add);
        var board = S.View.Boards.FirstOrDefault(b => b.Id == T.DrawBoard());
        var wipe = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(26, LayerRowHeight), TooltipText = $"Rub out everything on {board?.Name ?? "this layer"}" };
        wipe.AddChild(new ToolIcon { Kind = "erase", Color = Ui.Muted });
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

    HBoxContainer LayerRow(BoardDef b)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        var id = b.Id;
        bool shown = !T.HiddenBoards.Contains(id);
        var eye = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 22), TooltipText = shown ? "Hide this layer (on your screen only)" : "Show it again" };
        eye.AddChild(new ToolIcon { Kind = shown ? "eye" : "eye-off", Color = shown ? Ui.Ink : Ui.Muted });
        eye.Pressed += () =>
        {
            T.ShowBoard(id, !shown);
            T.RefreshDrawings();
            LayersChanged();
        };
        row.AddChild(eye);
        var pick = new Button
        {
            Text = b.Name, ToggleMode = true, ButtonPressed = T.DrawBoard() == id, ClipText = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(84, 22),
            TooltipText = "Draw on this layer · double-click to rename",
        };
        pick.Pressed += () =>
        {
            T.Board = id;
            T.CheckDrawingLock();
            LayersChanged();
        };
        pick.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) AskLayerName(id, b.Name); };
        row.AddChild(pick);
        int on = DrawnOn(id);
        if (on > 0)
        {
            var tally = new Label
            {
                Text = on.ToString(), CustomMinimumSize = new Vector2(16, 0), HorizontalAlignment = HorizontalAlignment.Right,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted, MouseFilter = Control.MouseFilterEnum.Stop,
                TooltipText = on == 1 ? "1 drawing on this layer" : $"{on} drawings on this layer",
            };
            tally.AddThemeFontSizeOverride("font_size", 11);
            row.AddChild(tally);
        }
        row.AddChild(RemoveLayerButton(b));
        return row;
    }

    Button RemoveLayerButton(BoardDef b)
    {
        var id = b.Id;
        bool last = S.View.Boards.Count == 1;
        var del = new Button { Text = "✕", Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(20, 22), TooltipText = last ? "Clear this layer and give it its name back" : "Remove this layer and everything on it" };
        del.AddThemeFontSizeOverride("font_size", 11);
        del.Pressed += () =>
        {
            int n = DrawnOn(id);
            string drawn = n == 0 ? "" : n == 1 ? "The drawing on it goes too." : $"The {n} drawings on it go too.";
            if (last) Ui.Confirm(HudNode, $"Clear \"{b.Name}\"?", "Clear", () => S.Do(new RemoveBoard { Id = id }), n == 0 ? "It gets its name back." : drawn + " It gets its name back.");
            else Ui.Confirm(HudNode, $"Remove \"{b.Name}\"?", "Remove", () => S.Do(new RemoveBoard { Id = id }), n == 0 ? null : drawn);
        };
        return del;
    }

    void AskLayerName(string id, string was)
    {
        var dlg = new ConfirmationDialog { Title = "Rename layer", OkButtonText = "Rename" };
        var edit = new LineEdit { Text = was, CustomMinimumSize = new Vector2(240, 0) };
        dlg.AddChild(edit);
        dlg.Confirmed += () => { if (edit.Text.Trim().Length > 0) S.Do(new RenameBoard { Id = id, Name = edit.Text.Trim() }); };
        Ui.Open(HudNode, dlg);
        Ui.FreeOnClose(dlg);
        edit.GrabFocus();
        edit.SelectAll();
    }
}
