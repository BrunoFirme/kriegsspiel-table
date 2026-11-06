using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class SelectTool
{
    readonly TableContext _c;
    readonly PointerPress _press;
    readonly SheetLayer _sheets;
    readonly PieceLayer _pieces;
    readonly Selection _selection;
    readonly Carry _hand;
    readonly NameEditor _names;
    readonly PieceActions _actions;
    public SelectTool(TableContext c, PointerPress press, SheetLayer sheets, PieceLayer pieces, Selection selection, Carry hand, NameEditor names, PieceActions actions)
    {
        _c = c;
        _press = press;
        _sheets = sheets;
        _pieces = pieces;
        _selection = selection;
        _hand = hand;
        _names = names;
        _actions = actions;
    }

    public bool Dragging { get; private set; }
    public bool BoxSelecting { get; private set; }
    Vector3 _dragStartWorld;
    readonly Dictionary<string, Vec2> _dragOrigin = new();
    Vec2 _sheetDragOrigin;
    string? _pressedInGroup;
    (PieceNode node, string what, Vec2 g, bool toggle)? _onFace;

    Session S => _c.S;

    public void Reset()
    {
        Dragging = false;
        BoxSelecting = false;
        _onFace = null;
    }

    public void Press(InputEventMouseButton mb, Vec2 g)
    {
        var hit = _pieces.Pick(g, screen: mb.Position);
        if (hit != null && PressOnFace(hit, mb, g)) return;
        if (hit == null && _names.Node != null) _names.End(commit: true);
        if (hit != null)
        {
            PressPiece(mb, g, hit);
            return;
        }
        if (S.IsUmpire && _sheets.Selected is { } sn && !sn.Def.Locked && sn.ContainsWorld(Coords.ToWorld(g)))
        {
            _dragStartWorld = Coords.ToWorld(g);
            _sheetDragOrigin = sn.Def.Pos;
            _dragOrigin.Clear();
            return;
        }
        if (!mb.ShiftPressed) _selection.Set(Array.Empty<string>());
        if (mb.AltPressed && S.IsUmpire)
        {
            _sheets.Select(_sheets.TopAt(g)?.Def.Id);
            return;
        }
        BoxSelecting = true;
    }

    public void SelectAlike(PieceNode like, bool add)
    {
        var cam = _c.Rig.Cam;
        var seen = cam.GetViewport().GetVisibleRect();
        _selection.Set(_pieces.Nodes.Where(n => n.Layer == like.Layer && n.Piece.Def == like.Piece.Def && _c.LayerInteractive(n.Layer)
            && !cam.IsPositionBehind(n.GlobalPosition) && seen.HasPoint(cam.UnprojectPosition(n.GlobalPosition))).Select(n => n.Piece.Id), add);
    }

    bool PressOnFace(PieceNode hit, InputEventMouseButton mb, Vec2 g)
    {
        var f = hit.FacePoint(mb.Position) ?? g;
        var names = _names;
        if (names.Node != null)
        {
            bool again = hit == names.Node && (hit.CardTitleAt(f) || (names.NotePart is { } part && hit.NotePartAt(f) == part));
            names.End(commit: true);
            if (again) return true;
        }
        if (FaceItem(hit, f) is not { } what) return false;
        if (what.StartsWith('w') && int.TryParse(what.AsSpan(1), out var wi)) hit.FlashWidget(wi);
        _onFace = (hit, what, g, mb.ShiftPressed || mb.CtrlPressed);
        return true;
    }

    string? FaceItem(PieceNode hit, Vec2 f)
    {
        if (S.CanTouch(hit.Layer, hit.Piece) && hit.WidgetAt(f) is { } wh) return "w" + wh.index;
        if (hit.CardTitleAt(f)) return "title";
        if (S.CanTouch(hit.Layer, hit.Piece) && hit.NotePartAt(f) is { } part) return part ? "note body" : "note title";
        if (hit.CardButtonAt(f) is { } cb) return $"c:{cb.name}:{cb.value}";
        if (S.CanTouch(hit.Layer, hit.Piece) && hit.PageButtonAt(f) is { } page) return $"page:{page}";
        return null;
    }

    void ClickOnFace(PieceNode hit, string what, Vector2 screen, Vec2 g)
    {
        if (!GodotObject.IsInstanceValid(hit)) return;
        var f = hit.FacePoint(screen) ?? g;
        if (FaceItem(hit, f) != what) return;
        if (S.CanTouch(hit.Layer, hit.Piece) && hit.WidgetAt(f) is { } wh)
        {
            if (wh.widget.Kind == "input") { _names.BeginWidget(hit, wh.index, wh.widget); return; }
            _actions.PressWidget(hit, wh.index, wh.widget);
            return;
        }
        if (hit.CardTitleAt(f)) { _names.Begin(hit); return; }
        if (S.CanTouch(hit.Layer, hit.Piece) && hit.NotePartAt(f) is { } part) { _names.BeginNote(hit, part); return; }
        if (hit.CardButtonAt(f) is { } cb) { S.Do(new SetCounter { Layer = hit.Layer, Id = hit.Piece.Id, Name = cb.name, Value = cb.value }); return; }
        if (S.CanTouch(hit.Layer, hit.Piece) && hit.PageButtonAt(f) is { } page) S.Do(new SetState { Layer = hit.Layer, Id = hit.Piece.Id, State = page });
    }

    void PressPiece(InputEventMouseButton mb, Vec2 g, PieceNode hit) => PressPiece(mb.ShiftPressed || mb.CtrlPressed, g, hit);

    void PressPiece(bool toggle, Vec2 g, PieceNode hit)
    {
        var selected = _selection.Ids;
        if (toggle) _selection.Toggle(hit);
        else if (!selected.Contains(hit.Piece.Id)) _selection.Set(new[] { hit.Piece.Id });
        else if (selected.Count > 1) _pressedInGroup = hit.Piece.Id;
        if (_sheets.SelectedId != null) _sheets.Select(null);
        _dragStartWorld = Coords.ToWorld(g);
        _dragOrigin.Clear();
        foreach (var n in _selection.Movable) _dragOrigin[n.Piece.Id] = n.Piece.Pos;
        if (hit.Piece.Locked) _c.Say($"{(hit.Piece.Label is { Length: > 0 } nm ? nm : "That piece")} is pinned down · right-click → Locked to let it go.");
    }

    public void Motion(InputEventMouseMotion mm, Vec2? g)
    {
        var pressPos = _press.Pos;
        if (BoxSelecting) { _c.NotifyScreenRect(new Rect2(pressPos, mm.Position - pressPos).Abs()); return; }
        if (g == null) return;
        if (!Dragging && (mm.Position - pressPos).Length() < 4) return;
        if (_onFace is { } of)
        {
            _onFace = null;
            if (!GodotObject.IsInstanceValid(of.node)) return;
            PressPiece(of.toggle, of.g, of.node);
        }
        Dragging = true;
        var delta = Coords.ToWorld(g.Value) - _dragStartWorld;
        if (_dragOrigin.Count == 0 && _sheets.Selected is { } sn && !sn.Def.Locked)
        {
            var np = _sheets.SnapTo(sn, Coords.FromWorld(Coords.ToWorld(_sheetDragOrigin) + delta), free: mm.AltPressed);
            sn.Position = Coords.ToWorld(np, sn.Def.Z * 0.004f);
            _c.Say(mm.AltPressed ? "Placing freely" : "Edges snap to the other sheets · hold Alt to place freely");
            return;
        }
        if (_dragOrigin.Count == 0) return;
        if (!_hand.Holding) _hand.Start(Coords.FromWorld(_dragStartWorld));
        var d0 = (g.Value - Coords.FromWorld(_dragStartWorld)).Length;
        _c.Say($"Moving: {Fmt.Distance(d0, S.View.Calibration)}");
    }

    public void Release(InputEventMouseButton mb, Vec2? g)
    {
        var narrowTo = _pressedInGroup;
        _pressedInGroup = null;
        if (_onFace is { } of)
        {
            _onFace = null;
            if (Dragging || g == null || !GodotObject.IsInstanceValid(of.node)) return;
            if (of.toggle && of.what.StartsWith("note")) PressPiece(true, of.g, of.node);
            else ClickOnFace(of.node, of.what, mb.Position, g.Value);
            return;
        }
        if (BoxSelecting)
        {
            BoxSelecting = false;
            _c.NotifyScreenRect(null);
            var pressPos = _press.Pos;
            var r = new Rect2(pressPos, mb.Position - pressPos).Abs();
            if (r.Size.Length() > 6)
            {
                var cam = _c.Rig.Cam;
                var ids = _pieces.Nodes.Where(n => _c.LayerInteractive(n.Layer) && !cam.IsPositionBehind(n.GlobalPosition) && r.HasPoint(cam.UnprojectPosition(n.GlobalPosition)))
                    .Select(n => n.Piece.Id);
                _selection.Set(ids, add: mb.ShiftPressed);
            }
            return;
        }
        if (!Dragging)
        {
            if (narrowTo != null && !mb.ShiftPressed && !mb.CtrlPressed && _selection.Ids.Contains(narrowTo)) _selection.Set(new[] { narrowTo });
            return;
        }
        Dragging = false;
        if (_dragOrigin.Count == 0 && _sheets.Selected is { } sn)
        {
            _sheets.SendDragged(sn);
            return;
        }
        if (_hand.Holding) _hand.Drop();
        if (g != null && _dragOrigin.Count > 0)
        {
            var moved = (g.Value - Coords.FromWorld(_dragStartWorld)).Length;
            _c.Say($"Moved {Fmt.Distance(moved, S.View.Calibration)}");
        }
    }

    public void FinishSheetDrag()
    {
        if (Dragging && _dragOrigin.Count == 0 && _sheets.Selected is { } sn && !sn.Def.Locked)
            _sheets.SendDragged(sn);
    }

    public void Forget()
    {
        Dragging = BoxSelecting = false;
        _dragOrigin.Clear();
        _pressedInGroup = null;
        _onFace = null;
    }
}
