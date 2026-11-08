using System;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PointerPress
{
    public Vector2 Pos;
    public bool LeftDown;
}

sealed class TableInput
{
    readonly TableContext _c;
    readonly PointerPress _press;
    readonly CameraControl _cam;
    readonly PieceLayer _pieces;
    readonly Sighting _sighting;
    readonly Selection _selection;
    readonly PendingPoses _poses;
    readonly Gizmo _gizmo;
    readonly Carry _hand;
    readonly PieceActions _actions;
    readonly NameEditor _names;
    readonly PaletteDrag _palette;
    readonly PenTool _pen;
    readonly LetterTool _letters;
    readonly RulerTool _ruler;
    internal required StampTool Decals { get; init; }
    readonly RangeRings _ranges;
    readonly CropTool _crop;
    readonly ZoneLayer _zones;
    readonly SelectTool _selectTool;
    readonly ReliefTool _relief;
    readonly FormationTool _forms;

    public TableInput(TableContext c, PointerPress press, CameraControl cam, PieceLayer pieces, Sighting sighting, Selection selection,
        PendingPoses poses, Gizmo gizmo, Carry hand, PieceActions actions, NameEditor names, PaletteDrag palette, PenTool pen, LetterTool letters,
        RulerTool ruler, RangeRings ranges, CropTool crop, ZoneLayer zones, SelectTool selectTool, ReliefTool relief, FormationTool forms)
    {
        _relief = relief;
        _forms = forms;
        _c = c;
        _press = press;
        _cam = cam;
        _pieces = pieces;
        _sighting = sighting;
        _selection = selection;
        _poses = poses;
        _gizmo = gizmo;
        _hand = hand;
        _actions = actions;
        _names = names;
        _palette = palette;
        _pen = pen;
        _letters = letters;
        _ruler = ruler;
        _ranges = ranges;
        _crop = crop;
        _zones = zones;
        _selectTool = selectTool;
    }

    public event Action<InputEventKey>? KeyPressed;
    public event Action<InputEventKey>? KeyReleased;
    public event Action<Vector2, PieceNode?>? ContextMenu;
    public event Action<double>? CalibrationMeasured;
    public event Action<Rect2>? CaptureRequested;
    public Rect2? LastCaptureRect { get; set; }

    public Vector2 PressPos { get => _press.Pos; private set => _press.Pos = value; }
    public bool LeftDown { get => _press.LeftDown; set => _press.LeftDown = value; }
    public bool Panning { get; private set; }
    public bool Orbiting { get; private set; }
    public bool Ordering { get; private set; }
    Vector2 _midPress;

    Session S => _c.S;
    CameraRig Rig => _c.Rig;
    CameraControl Cam => _cam;

    public void Handle(InputEvent e)
    {
        var names = _names;
        if (names.Node != null && e is InputEventKey nk && nk.Pressed)
        {
            names.Key(nk);
            _c.Scene.GetViewport().SetInputAsHandled();
            return;
        }
        if (S == null) return;
        switch (e)
        {
            case InputEventMouseButton mb:
                OnMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                OnMouseMotion(mm);
                break;
            case InputEventKey k when k.Pressed && !k.Echo:
                KeyPressed?.Invoke(k);
                break;
            case InputEventKey k when k.Pressed && k.Echo && RepeatsOnHold(k):
                KeyPressed?.Invoke(k);
                break;
            case InputEventKey k when !k.Pressed && !k.Echo && !Table.AnyTextFocus():
                KeyReleased?.Invoke(k);
                break;
        }
    }

    bool RepeatsOnHold(InputEventKey k) =>
        k.Keycode is Key.Left or Key.Right or Key.Up or Key.Down or Key.Q or Key.E or Key.Equal or Key.Minus
        || _c.Settings.Keys.Is(k, "undo") || _c.Settings.Keys.Is(k, "redo");

    bool WheelOnSelection(Vector2 screen)
    {
        if (_selection.Ids.Count == 0) return false;
        if (_hand.Holding) return true;
        var g = _c.Ground(screen);
        var hit = g != null ? _pieces.Pick(g.Value, screen: screen) : null;
        return hit != null && _selection.Ids.Contains(hit.Piece.Id);
    }

    void OnWheel(InputEventMouseButton mb)
    {
        if (!mb.Pressed) return;
        Rig.StopGliding();
        const float NotchIn = 0.9f;
        float f = mb.ButtonIndex == MouseButton.WheelUp ? NotchIn : 1 / NotchIn;
        if (_c.Tool == Tool.Sighting && _sighting.Draft != null)
        {
            _sighting.Wheel(f, mb.Position, mb.AltPressed);
            return;
        }
        if (_c.Tool == Tool.Relief && mb.CtrlPressed)
        {
            _relief.Resize(mb.ButtonIndex == MouseButton.WheelUp);
            return;
        }
        if (_c.Tool == Tool.Formation && (mb.CtrlPressed || mb.ShiftPressed))
        {
            int notch = mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
            if (mb.CtrlPressed) _forms.Space(notch);
            else _forms.Deepen(notch);
            return;
        }
        if (_c.Tool == Tool.Formation && WheelOnSelection(mb.Position) && !mb.AltPressed && _forms.Count >= 2)
        {
            _forms.Turn(mb.ButtonIndex == MouseButton.WheelUp ? -10 : 10);
            return;
        }
        if (_c.Tool == Tool.Decal && Decals.Armed != null && (mb.CtrlPressed || mb.ShiftPressed))
        {
            Decals.Wheel(mb.ButtonIndex == MouseButton.WheelUp, mb.CtrlPressed);
            return;
        }
        if (_c.Tool == Tool.Range && WheelOnSelection(mb.Position))
        {
            _ranges.Wheel(f);
            return;
        }
        if (WheelOnSelection(mb.Position) && !mb.AltPressed)
        {
            bool up = mb.ButtonIndex == MouseButton.WheelUp;
            double step = (up ? -1 : 1) * (mb.ShiftPressed ? 0.5 : mb.CtrlPressed ? 2 : 10);
            if (_hand.Holding) _hand.Rotate(step);
            else _poses.RotateSmooth(step);
            return;
        }
        Rig.Zoom(f, mb.Position);
    }

    public void OnMouseButton(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown) { OnWheel(mb); return; }
        if (mb.ButtonIndex is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right)) return;
        if (!Heard(mb)) return;
        if (mb.ButtonIndex == MouseButton.Right) { RightButton(mb); return; }
        if (mb.ButtonIndex == MouseButton.Middle) { MiddleButton(mb); return; }
        if (mb.ButtonIndex != MouseButton.Left) return;
        LeftButton(mb);
    }

    bool Heard(InputEventMouseButton mb)
    {
        var bit = Bit(mb.ButtonIndex);
        if (mb.Pressed)
        {
            LetGoUnheard(mb.ButtonIndex);
            _down |= bit;
        }
        else
        {
            if ((_down & bit) == 0) return false;
            _down &= ~bit;
        }
        _lastHeard = mb.Position;
        return true;
    }

    void RightButton(InputEventMouseButton mb)
    {
        if (mb.Pressed && _c.Tool == Tool.Formation && !mb.AltPressed && _c.Ground(mb.Position) is { } front && _forms.Press(front))
        {
            Ordering = true;
            PressPos = mb.Position;
            return;
        }
        if (!mb.Pressed && Ordering)
        {
            Ordering = false;
            bool dragged = (mb.Position - PressPos).Length() >= 5;
            if (dragged && !_unheard) { _forms.Release(); return; }
            _forms.Cancel();
            if (!dragged) RightClick(mb);
            return;
        }
        Orbiting = mb.Pressed;
        if (mb.Pressed)
        {
            PressPos = mb.Position;
            Cam.DragBegin();
            return;
        }
        Cam.LetGo(orbit: true);
        RightClick(mb);
    }

    void RightClick(InputEventMouseButton mb)
    {
        if (!_unheard && (mb.Position - PressPos).Length() < 5 && !_hand.Holding && !_gizmo.Dragging)
        {
            var gg = _c.Ground(mb.Position);
            var hit = gg != null ? _pieces.Pick(gg.Value, screen: mb.Position) : null;
            if (hit != null && gg != null && S.CanTouch(hit.Layer, hit.Piece) && hit.WidgetAt(hit.FacePoint(mb.Position) ?? gg.Value) is { widget.Kind: "button" } wa)
            {
                S.Do(new ClickWidget { Layer = hit.Layer, Id = hit.Piece.Id, Index = wa.index, Alt = true });
                return;
            }
            if (hit != null && !_selection.Ids.Contains(hit.Piece.Id)) _selection.Set(new[] { hit.Piece.Id });
            ContextMenu?.Invoke(mb.Position, hit);
        }
    }

    void MiddleButton(InputEventMouseButton mb)
    {
        if (mb.Pressed)
        {
            Panning = true;
            _midPress = mb.Position;
            Cam.DragBegin();
            return;
        }
        Panning = false;
        Cam.LetGo(orbit: false);
        if (!_unheard && (mb.Position - _midPress).Length() < 5)
        {
            if (_hand.Holding) _actions.Flip(_selection.Nodes.ToList());
            else if (_c.Ground(mb.Position) is { } mg && _pieces.Pick(mg, screen: mb.Position) is { } hit)
                _actions.Flip(_selection.Ids.Contains(hit.Piece.Id) ? _selection.Nodes.ToList() : new[] { hit });
        }
    }

    void LeftButton(InputEventMouseButton mb)
    {
        var g = _c.Ground(mb.Position);
        if (mb.Pressed) _c.Scene.GetViewport().GuiReleaseFocus();
        var gizmo = _gizmo;
        if (mb.Pressed && _c.Tool == Tool.Transform && gizmo.At(mb.Position) is var hnd && hnd != Gizmo.Handle.None)
        {
            LeftDown = true;
            PressPos = mb.Position;
            gizmo.Begin(hnd, mb.Position);
            return;
        }
        if (!mb.Pressed && gizmo.Dragging)
        {
            LeftDown = false;
            gizmo.End();
            return;
        }
        if (mb.Pressed) LeftPress(mb, g);
        else LeftRelease(mb, g);
    }

    void LeftPress(InputEventMouseButton mb, Vec2? g)
    {
        LeftDown = true;
        PressPos = mb.Position;
        _selectTool.Reset();
        _hand.Land();
        if (Input.IsKeyPressed(Key.Space))
        {
            Panning = true;
            Cam.DragBegin();
            return;
        }
        if (_c.Tool == Tool.Sighting && _sighting.Looking)
        {
            Orbiting = true;
            Cam.DragBegin();
            return;
        }
        if (g == null) return;
        switch (_c.Tool)
        {
            case Tool.Select: case Tool.Transform: case Tool.Range:
                if (mb.DoubleClick && _pieces.Pick(g.Value, screen: mb.Position) is { } opened && BookWindow.Is(opened.Def)) BookWindow.Open(_c.Scene, S, opened);
                else _selectTool.Press(mb, g.Value);
                break;
            case Tool.Crop:
                _crop.Press(mb.Position);
                break;
            case Tool.Decal:
                Decals.Press(g.Value, mb.Position);
                break;
            case Tool.Zone:
                _zones.Press(g.Value);
                break;
            case Tool.Place:
                if (_c.PlaceDef != null)
                {
                    var pl = Layers.Umpire;
                    S.Do(new SpawnPiece { Layer = pl, Def = _c.PlaceDef, Pos = g.Value, RotDeg = 180, Y = _hand.StoredY(g.Value, pl) });
                }
                if (!mb.ShiftPressed) _c.SetTool(Tool.Select);
                break;
            case Tool.Ruler: case Tool.Calibrate:
                _ruler.Press(g);
                break;
            case Tool.Draw:
                _pen.PressDraw(g.Value);
                break;
            case Tool.Shape:
                _pen.PressShape(g.Value);
                break;
            case Tool.Erase:
                _pen.PressErase(g.Value);
                break;
            case Tool.Letter:
                _letters.Press(g.Value, mb.DoubleClick);
                break;
            case Tool.Formation:
                if (mb.DoubleClick && _pieces.Pick(g.Value, screen: mb.Position) is { } like) _selectTool.SelectAlike(like, add: mb.ShiftPressed || mb.CtrlPressed);
                else _selectTool.Press(mb, g.Value);
                break;
            case Tool.Sighting:
                _sighting.Press(g.Value);
                break;
            case Tool.Relief:
                _relief.Press(g.Value, mb.ShiftPressed, mb.AltPressed, mb.DoubleClick);
                break;
        }
    }

    void LeftRelease(InputEventMouseButton mb, Vec2? g)
    {
        LeftDown = false;
        if (Panning && !Input.IsMouseButtonPressed(MouseButton.Middle)) { Panning = false; Cam.LetGo(orbit: false); }
        if (Orbiting && !Input.IsMouseButtonPressed(MouseButton.Right)) { Orbiting = false; Cam.LetGo(orbit: true); }
        switch (_c.Tool)
        {
            case Tool.Zone:
                _zones.Release();
                _c.SetTool(Tool.Select);
                break;
            case Tool.Select: case Tool.Transform: case Tool.Range:
                case Tool.Formation: _selectTool.Release(mb, g);
                break;
            case Tool.Crop:
                _crop.Release();
                break;
            case Tool.Calibrate:
                if (_ruler.Measured is { } length) CalibrationMeasured?.Invoke(length);
                break;
            case Tool.Draw:
                _pen.ReleaseDraw();
                break;
            case Tool.Shape:
                _pen.ReleaseShape();
                break;
            case Tool.Letter:
                _letters.Release();
                break;
            case Tool.Sighting:
                _sighting.Release();
                break;
            case Tool.Relief:
                _relief.Release();
                break;
            case Tool.Capture:
                var r = new Rect2(PressPos, mb.Position - PressPos).Abs();
                _c.NotifyScreenRect(null);
                if (r.Size.X > 8 && r.Size.Y > 8) { LastCaptureRect = r; CaptureRequested?.Invoke(r); }
                _c.SetTool(Tool.Select);
                break;
        }
    }

    void OnMouseMotion(InputEventMouseMotion mm)
    {
        CatchLostReleases();
        _lastHeard = mm.Position;
        if (Ordering) { if (_c.Ground(mm.Position) is { } front) _forms.Drag(front); return; }
        if (Orbiting)
        {
            Cam.TrackOrbit(mm.Relative);
            _sighting.Unlock();
            return;
        }
        if (Panning) { Cam.TrackPan(mm.Position - mm.Relative, mm.Position); return; }
        if (!LeftDown) return;
        if (_gizmo.Dragging) { _gizmo.Drag(mm.Position, mm.ShiftPressed); return; }
        var g = _c.Ground(mm.Position);
        switch (_c.Tool)
        {
            case Tool.Crop:
                if (g != null) _crop.Drag(g.Value);
                return;
            case Tool.Select: case Tool.Transform: case Tool.Formation:
                _selectTool.Motion(mm, g);
                break;
            case Tool.Ruler: case Tool.Calibrate:
                if (g != null) _ruler.B = g;
                break;
            case Tool.Zone:
                if (g != null) _zones.Motion(g.Value);
                break;
            case Tool.Draw:
                if (g != null) _pen.MoveDraw(g.Value);
                break;
            case Tool.Shape:
                if (g != null) _pen.MoveShape(g.Value);
                break;
            case Tool.Erase:
                if (g != null) _pen.RubAt(g.Value);
                break;
            case Tool.Letter:
                if (g != null) _letters.Motion(g.Value);
                break;
            case Tool.Capture:
                _c.NotifyScreenRect(new Rect2(PressPos, mm.Position - PressPos).Abs());
                break;
            case Tool.Sighting:
                _sighting.Move(g);
                break;
            case Tool.Relief:
                if (g != null) _relief.Motion(g.Value);
                break;
        }
    }

    MouseButtonMask _down;
    Vector2 _lastHeard;
    bool _unheard;
    static readonly MouseButton[] Hands = { MouseButton.Left, MouseButton.Middle, MouseButton.Right };

    static MouseButtonMask Bit(MouseButton b) => (MouseButtonMask)(1 << ((int)b - 1));

    public void CatchLostReleases()
    {
        var lost = _down & ~Input.GetMouseButtonMask();
        if (lost == 0) return;
        foreach (var b in Hands) if ((lost & Bit(b)) != 0) LetGoUnheard(b);
    }

    void LetGoUnheard(MouseButton b)
    {
        if ((_down & Bit(b)) == 0) return;
        _unheard = true;
        try
        {
            OnMouseButton(new InputEventMouseButton
            {
                ButtonIndex = b, Pressed = false, Position = _lastHeard, GlobalPosition = _lastHeard,
                ShiftPressed = Input.IsKeyPressed(Key.Shift), CtrlPressed = Input.IsKeyPressed(Key.Ctrl), AltPressed = Input.IsKeyPressed(Key.Alt),
            });
        }
        finally { _unheard = false; }
    }

    public void LetGoOfEverything()
    {
        foreach (var b in Hands) LetGoUnheard(b);
        if (Panning || Orbiting) Cam.LetGo(Orbiting);
        Panning = Orbiting = false;
        EndGesture();
        _palette.End(drop: true);
    }

    public bool DropReliefShape() => _c.Tool == Tool.Relief && _relief.DropShape();

    public bool DropLetter() => _c.Tool == Tool.Letter && _letters.Drop();

    public void EndGesture()
    {
        if (_hand.Holding) _hand.Drop();
        if (_gizmo.Dragging) _gizmo.End();
        else _selectTool.FinishSheetDrag();
        LeftDown = false;
        _selectTool.Forget();
        _down &= ~MouseButtonMask.Left;
        _sighting.DropWall();
        _forms.Cancel();
        _pen.Drop();
        _zones.Drop();
        if (!_letters.Open) _letters.Drop();
        _crop.Release();
        _c.NotifyScreenRect(null);
    }
}
