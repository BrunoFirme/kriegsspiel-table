using System;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class TableKeys
{
    readonly TableContext _c;
    readonly TableInput _router;
    readonly CameraControl _cam;
    readonly SheetLayer _sheets;
    readonly PieceLayer _pieces;
    readonly Sighting _sighting;
    readonly Selection _selection;
    readonly PendingPoses _poses;
    readonly Carry _hand;
    readonly Dice _dice;
    readonly PieceClipboard _clip;
    readonly Hover _hover;
    readonly PieceActions _actions;
    readonly PenTool _pen;
    readonly RulerTool _ruler;
    readonly FormationTool _forms;
    public TableKeys(TableContext c, TableInput router, CameraControl cam, SheetLayer sheets, PieceLayer pieces, Sighting sighting,
        Selection selection, PendingPoses poses, Carry hand, Dice dice, PieceClipboard clip, Hover hover, PieceActions actions,
        PenTool pen, RulerTool ruler, FormationTool forms)
    {
        _forms = forms;
        _c = c;
        _router = router;
        _cam = cam;
        _sheets = sheets;
        _pieces = pieces;
        _sighting = sighting;
        _selection = selection;
        _poses = poses;
        _hand = hand;
        _dice = dice;
        _clip = clip;
        _hover = hover;
        _actions = actions;
        _pen = pen;
        _ruler = ruler;
    }

    public event Action? MenuRequested;
    public event Action? PublishRequested;
    public event Action<Rect2>? CaptureRequested;
    public Func<bool>? StopRecording;
    public event Action<string>? PanelRequested;

    bool RequestPublish()
    {
        if (PublishRequested == null) return false;
        PublishRequested();
        return true;
    }

    Session S => _c.S;

    public void OnKey(InputEventKey k)
    {
        if (Table.AnyTextFocus()) return;
        if (k.Keycode == Key.Escape) { Escape(); return; }
        if (ScriptHotkey(k, up: false)) return;
        if (k.Keycode is Key.Enter or Key.KpEnter && StopRecording?.Invoke() == true) return;
        if (k.Keycode is Key.Enter or Key.KpEnter && _c.Tool == Tool.Sighting && _sighting.Draft != null) { if (!RequestPublish()) _sighting.End(); return; }
        if (k.Keycode is >= Key.Key1 and <= Key.Key9 && !k.AltPressed && k.CtrlPressed != k.ShiftPressed) { NumberWithModifier((int)(k.Keycode - Key.Key1) + 1, k.CtrlPressed); return; }
        if (!k.CtrlPressed && !k.AltPressed && PieceActions.StateKey(k.Keycode) is int stateNo && _actions.SetStateNumber(stateNo)) return;
        if (k.Keycode is Key.Left or Key.Right or Key.Up or Key.Down && _sheets.SelectedId != null && S.IsUmpire) { _sheets.Nudge(k); return; }
        if (_c.Tool == Tool.Formation && _forms.Key(k, _c.Settings.Keys)) return;
        if (_router.ToolKey(k, _c.Settings.Keys)) return;
        Bound(k, _c.Settings.Keys);
    }

    void ReleaseOrRecall()
    {
        if (!S.IsUmpire) return;
        var hover = _hover.Node;
        var targets = hover != null && !_hand.Holding && !_selection.Ids.Contains(hover.Piece.Id) ? new[] { hover } : _selection.Nodes.ToArray();
        var sent = false;
        foreach (var layer in targets.GroupBy(n => n.Layer))
            if (KTable.Client.ReadyKey.For(S.View, layer.Key, layer.Select(n => n.Piece.Id)) is { } c) { S.Do(c); sent = true; }
        if (!sent && targets.Length > 0) _c.Say("No ready piece here: mark one in the holder's window");
    }

    void NumberWithModifier(int n, bool ctrl)
    {
        if (ctrl && (_hand.Holding || _selection.Ids.Count > 1)) _hand.FormRows(n);
        else if (ctrl) _cam.SaveView(n);
        else if (!_cam.GoToView(n)) _c.Say($"No view {n} yet: Ctrl+{n} keeps one");
    }

    void Escape()
    {
        if (_router.DropReliefShape() || _router.DropBuild() || _router.DropLetter()) return;
        bool busy = _c.Tool != Tool.Select || _sighting.Draft != null || _selection.Ids.Count > 0 || _sheets.SelectedId != null
            || _ruler.A != null || _pen.Stroking || _pen.Shaping || _hand.Holding;
        _router.EndGesture();
        if (_c.Tool == Tool.Sighting) _sighting.Cancel();
        _c.SetTool(Tool.Select);
        _ruler.Clear();
        _c.NotifyScreenRect(null);
        _selection.Set(Array.Empty<string>());
        _sheets.Select(null);
        if (!busy) MenuRequested?.Invoke();
    }

    void Bound(InputEventKey k, Keymap km)
    {
        if (km.Is(k, "map2d")) _c.Rig.ToggleTopDown();
        else if (km.Is(k, "frame")) _cam.FrameAll();
        else if (km.Is(k, "north")) _c.Rig.FaceNorth(S.View.Calibration.North);
        else if (km.Is(k, "ruler")) _c.SetTool(_c.Tool == Tool.Ruler ? Tool.Select : Tool.Ruler);
        else if (km.Is(k, "gizmo")) _c.SetTool(_c.Tool == Tool.Transform ? Tool.Select : Tool.Transform);
        else if (km.Is(k, "formations")) _c.SetTool(_c.Tool == Tool.Formation ? Tool.Select : Tool.Formation);
        else if (km.Is(k, "flip")) _actions.Flip(_selection.Nodes.ToList());
        else if (km.Is(k, "ready")) ReleaseOrRecall();
        else if (km.Is(k, "turnLeft")) { if (_hand.Holding) _hand.Rotate(k.ShiftPressed ? -1 : -15); else _poses.RotateSmooth(k.ShiftPressed ? -1 : -15); }
        else if (km.Is(k, "turnRight")) { if (_hand.Holding) _hand.Rotate(k.ShiftPressed ? 1 : 15); else _poses.RotateSmooth(k.ShiftPressed ? 1 : 15); }
        else if (km.Is(k, "capture")) _c.SetTool(Tool.Capture);
        else if (km.Is(k, "captureAgain")) { if (_router.LastCaptureRect != null) CaptureRequested?.Invoke(_router.LastCaptureRect.Value); }
        else if (km.Is(k, "scriptConsole")) PanelRequested?.Invoke("console");
        else if (km.Is(k, "scriptWindow")) PanelRequested?.Invoke("scripts");
        else if (km.Is(k, "saves")) PanelRequested?.Invoke("saves");
        else if (km.Is(k, "cut")) CutUnlocked();
        else if (km.Is(k, "copy")) _clip.Copy();
        else if (km.Is(k, "paste")) { if (_c.Ground(Pointer.In(_c.Scene.GetViewport())) is { } at) _clip.Paste(at); }
        else if (km.Is(k, "selectAll")) _selection.SelectAllEditable();
        else if (km.Is(k, "roll")) { if (!_dice.RollAtHand()) _poses.RotateSmooth(k.ShiftPressed ? -15 : 15); }
        else if (km.Is(k, "larger")) _poses.ScaleSmooth(1.1);
        else if (km.Is(k, "smaller")) _poses.ScaleSmooth(1 / 1.1);
        else if (km.Is(k, "delete")) { if (!_router.DeleteBuilt()) _actions.Delete(_actions.Unlocked()); }
        else if (km.Is(k, "mute"))
        {
            _c.Settings.SoundMuted = !_c.Settings.SoundMuted;
            _c.Settings.Save();
            Sfx.Current?.Apply();
            _c.Say(_c.Settings.SoundMuted ? "Sound off" : "Sound on");
        }
        else if (km.Is(k, "undo")) { if (_c.Tool == Tool.Sighting && _sighting.Draft != null) _sighting.UndoStroke(); else S.UndoLast(); }
        else if (km.Is(k, "redo")) { if (_c.Tool == Tool.Sighting && _sighting.Draft != null) _sighting.RedoStroke(); else S.RedoLast(); }
        else if (km.Is(k, "sighting")) { if (S.IsUmpire) _c.SetTool(Tool.Sighting); }
        else if (km.Is(k, "statePrev")) _actions.CycleState(-1);
        else if (km.Is(k, "stateNext")) _actions.CycleState(1);
    }

    void CutUnlocked()
    {
        var free = _actions.Unlocked();
        _clip.Copy(free, cut: true);
        _actions.Delete(free);
    }

    public void OnKeyUp(InputEventKey k) => ScriptHotkey(k, up: true);

    public bool ScriptHotkey(InputEventKey k, bool up)
    {
        if (S == null || !S.IsUmpire) return false;
        var hotkeys = S.View.Hotkeys;
        for (int i = 0; i < hotkeys.Count; i++)
        {
            if (!_c.Settings.Keys.Is(k, "script:" + hotkeys[i].Label)) continue;
            if (up && !hotkeys[i].OnUp) return true;
            var selected = _selection.Nodes.Where(n => n.Layer == Layers.Umpire).Select(n => n.Piece.Id).ToList();
            var hover = _hover.Node;
            S.Do(new Hotkey { Index = i + 1, Up = up, Layer = hover?.Layer, Piece = hover?.Piece.Id, Pos = _c.Ground(Pointer.In(_c.Scene.GetViewport())), Selected = selected });
            return true;
        }
        return false;
    }
}
