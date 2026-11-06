using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class LayersPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    readonly DrawPanel _paint;
    readonly ToolStrip _strip;

    public LayersPanel(HudContext c, FloatPanel panel, DrawPanel paint, ToolStrip strip) : base(c)
    {
        _paint = paint;
        _strip = strip;
        Panel = panel;
        _box = Panel.Body;
    }

    public void Rebuild()
    {
        if (_box == null) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        foreach (var b in S.View.Boards) _box.AddChild(Row(b));
        var add = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 20), TooltipText = "A fresh sheet of tracing paper over the map" };
        add.Pressed += () => S.Do(new AddBoard());
        _box.AddChild(add);
        Panel?.Fit();
    }

    int Count(string id) => S.View.Drawings.Values.Sum(l => l.Count(s => s.Board == id));

    HBoxContainer Row(BoardDef b)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        var id = b.Id;
        bool shown = !T.HiddenBoards.Contains(id);
        var eye = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 22), TooltipText = shown ? "Hide this layer (on your screen only)" : "Show it again" };
        eye.AddChild(new ToolIcon { Kind = shown ? "eye" : "eye-off", Color = shown ? Ui.Ink : Ui.Muted });
        eye.Pressed += () =>
        {
            if (!T.HiddenBoards.Add(id)) T.HiddenBoards.Remove(id);
            T.RefreshDrawings();
            Rebuild();
            _paint.Rebuild();
            _strip.SyncPenLock();
        };
        row.AddChild(eye);
        var pick = new Button
        {
            Text = b.Name, ToggleMode = true, ButtonPressed = T.DrawBoard() == id, ClipText = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(104, 22),
            TooltipText = "Draw on this layer · double-click to rename",
        };
        pick.Pressed += () =>
        {
            T.Board = id;
            T.CheckDrawingLock();
            Rebuild();
            _paint.Rebuild();
            _strip.SyncPenLock();
        };
        pick.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) AskName(id, b.Name); };
        row.AddChild(pick);
        int on = Count(id);
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
        row.AddChild(RemoveButton(b));
        return row;
    }

    Button RemoveButton(BoardDef b)
    {
        var id = b.Id;
        bool last = S.View.Boards.Count == 1;
        var del = new Button { Text = "✕", Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(20, 22), TooltipText = last ? "Clear this layer and give it its name back" : "Remove this layer and everything on it" };
        del.AddThemeFontSizeOverride("font_size", 11);
        del.Pressed += () =>
        {
            int n = Count(id);
            var dlg = last
                ? new ConfirmationDialog { Title = "Clear layer", DialogText = n == 0 ? $"Give \"{b.Name}\" its name back?" : $"Clear \"{b.Name}\" — the {n} drawings on it and its name?", OkButtonText = "Clear" }
                : new ConfirmationDialog { Title = "Remove layer", DialogText = n == 0 ? $"Remove \"{b.Name}\"?" : $"Remove \"{b.Name}\" and the {n} drawings on it?", OkButtonText = "Remove" };
            HudNode.AddChild(dlg);
            dlg.Confirmed += () => S.Do(new RemoveBoard { Id = id });
            dlg.PopupCentered();
            Ui.FreeOnClose(dlg);
        };
        return del;
    }

    void AskName(string id, string was)
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
