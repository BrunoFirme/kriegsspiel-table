using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PieceActions
{
    readonly TableContext _c;
    readonly Selection _selection;
    readonly PendingPoses _poses;
    readonly Carry _hand;
    readonly Hover _hover;
    public PieceActions(TableContext c, Selection selection, PendingPoses poses, Carry hand, Hover hover)
    {
        _c = c;
        _selection = selection;
        _poses = poses;
        _hand = hand;
        _hover = hover;
    }

    Session S => _c.S;

    public void Flip(IEnumerable<PieceNode> nodes)
    {
        _poses.Flush();
        var cmds = nodes.Where(n => !n.IsDie && !n.IsEffect && n.Def?.Kind != "model" && _c.LayerInteractive(n.Layer))
            .Select(n => (Command)new FlipPiece { Layer = n.Layer, Id = n.Piece.Id, FaceDown = !n.Piece.FaceDown }).ToList();
        _c.DoAll(cmds);
    }

    public void Delete() => Delete(_selection.Nodes);

    public void Delete(IEnumerable<PieceNode> nodes) => _c.DoAll(nodes.Select(n => (Command)new DeletePiece { Layer = n.Layer, Id = n.Piece.Id }).ToList());

    public List<PieceNode> Unlocked()
    {
        var nodes = _selection.Nodes.ToList();
        var free = nodes.Where(n => !n.Piece.Locked).ToList();
        int kept = nodes.Count - free.Count;
        if (kept == 1)
        {
            var piece = nodes.First(n => n.Piece.Locked).Piece;
            _c.Say($"{(piece.Label is { Length: > 0 } nm ? nm : "That piece")} is locked and stays · right-click → Delete removes it.");
        }
        else if (kept > 1) _c.Say($"{kept} locked pieces stay · right-click → Delete removes them.");
        return free;
    }

    public void CycleState(int dir)
    {
        var cmds = new List<Command>();
        foreach (var n in _selection.Nodes)
        {
            var count = n.Def?.States.Count ?? 0;
            if (count < 2) continue;
            cmds.Add(new SetState { Layer = n.Layer, Id = n.Piece.Id, State = ((n.Piece.State + dir) % count + count) % count });
        }
        _c.DoAll(cmds);
    }

    public static int? StateKey(Key key) =>
        key is >= Key.Key0 and <= Key.Key9 ? (key == Key.Key0 ? 10 : (int)(key - Key.Key0))
        : key is >= Key.Kp0 and <= Key.Kp9 ? (key == Key.Kp0 ? 10 : (int)(key - Key.Kp0))
        : null;

    public bool SetStateNumber(int n)
    {
        var hover = _hover.Node;
        IEnumerable<PieceNode> targets = hover != null && !_hand.Holding && !_selection.Ids.Contains(hover.Piece.Id) ? new[] { hover } : _selection.Nodes;
        if (S.View.Script != null) return NumberToScript(targets, n);
        var dice = targets.Where(t => t.IsDie && PieceInstance.FacesOf(t.Def, t.Piece).Count >= n && S.CanTouch(t.Layer, t.Piece)).ToList();
        var able = targets.Where(t => !t.IsDie && t.Def != null && t.Def.States.Count > 1 && t.Def.States.Count >= n && S.CanTouch(t.Layer, t.Piece)).ToList();
        if (able.Count == 0 && dice.Count == 0) return false;
        var cmds = able.Where(t => t.Piece.State != n - 1).Select(t => (Command)new SetState { Layer = t.Layer, Id = t.Piece.Id, State = n - 1 })
            .Concat(dice.Where(t => t.Piece.ShownFace != n - 1).Select(t => (Command)new TurnDie { Layer = t.Layer, Id = t.Piece.Id, Face = n - 1 })).ToList();
        _c.DoAll(cmds);
        if (able.Count == 0)
        {
            var value = PieceInstance.FacesOf(dice[0].Def, dice[0].Piece)[n - 1].Value;
            _c.Say($"Side {n}{(value.Length > 0 && value != n.ToString() ? ": " + value : "")}{(dice.Count > 1 ? $" on {dice.Count} dice" : "")}");
            return true;
        }
        var name = able[0].Def!.States[n - 1].Name;
        _c.Say($"Look {n}{(name.Length > 0 ? ": " + name : "")}{(able.Count > 1 ? $" on {able.Count} pieces" : "")}");
        return true;
    }

    bool NumberToScript(IEnumerable<PieceNode> targets, int n)
    {
        var editable = targets.Where(t => S.CanTouch(t.Layer, t.Piece)).ToList();
        if (editable.Count == 0) return false;
        var typed = editable.Select(t => (Command)new NumberTyped { Layer = t.Layer, Id = t.Piece.Id, Number = n }).ToList();
        if (typed.Count == 1) S.Do(typed[0]);
        else S.Do(new Batch { Items = typed });
        var ab = editable.Where(t => t.Def != null && t.Def.States.Count > 1 && t.Def.States.Count >= n).ToList();
        if (ab.Count > 0) { var nm = ab[0].Def!.States[n - 1].Name; _c.Say($"Look {n}{(nm.Length > 0 ? ": " + nm : "")}{(ab.Count > 1 ? $" on {ab.Count} pieces" : "")}"); }
        return true;
    }

    public void PressWidget(PieceNode hit, int index, Widget w)
    {
        var press = SameButtonAcrossSelection(hit, index, w);
        foreach (var n in press) n.FlashWidget(index);
        var clicks = press.Select(n => (Command)new ClickWidget { Layer = n.Layer, Id = n.Piece.Id, Index = index }).ToList();
        if (clicks.Count == 1) S.Do(clicks[0]);
        else S.Do(new Batch { Items = clicks });
    }

    List<PieceNode> SameButtonAcrossSelection(PieceNode hit, int index, Widget w)
    {
        var one = new List<PieceNode> { hit };
        if (_selection.Ids.Count < 2 || !_selection.Ids.Contains(hit.Piece.Id) || string.IsNullOrEmpty(w.OnClick)) return one;
        var all = new List<PieceNode>();
        foreach (var n in _selection.Nodes)
        {
            if (!S.CanTouch(n.Layer, n.Piece) || n.Piece.Widgets.Count < index) continue;
            var o = n.Piece.Widgets[index - 1];
            if (o.Kind == w.Kind && o.Face == w.Face && o.OnClick == w.OnClick) all.Add(n);
        }
        return all.Contains(hit) ? all : one;
    }
}
