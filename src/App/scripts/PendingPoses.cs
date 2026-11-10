using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

interface IHeldPieces
{
    bool Holding { get; }
    void Rotate(double deg);
}

interface IKnobDrag
{
    bool Dragging { get; }
}

sealed class PendingPoses
{
    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Selection _selection;
    public PendingPoses(TableContext c, PieceLayer pieces, Selection selection)
    {
        _c = c;
        _pieces = pieces;
        _selection = selection;
    }

    IHeldPieces _hand = null!;
    IKnobDrag _gizmo = null!;
    public void Connect(IHeldPieces hand, IKnobDrag gizmo) { _hand = hand; _gizmo = gizmo; }

    readonly Dictionary<(string layer, string id), (Vec2 pos, double rot, double scale)> _pending = new();
    double _timer = -1;

    public const double MinScale = 0.02, MaxScale = 50;

    public void Tick(double delta)
    {
        if (_timer < 0 || _gizmo.Dragging) return;
        _timer -= delta;
        if (_timer < 0) Flush();
    }

    public (Vec2 pos, double rot, double scale) Current(PieceNode n) =>
        _pending.TryGetValue((n.Layer, n.Piece.Id), out var p) ? p : (n.Piece.Pos, n.Piece.RotDeg, n.Piece.Scale);

    public void Set(PieceNode n, Vec2 pos, double rot, double scale, double delay = 0.35)
    {
        _pending[(n.Layer, n.Piece.Id)] = (pos, rot, scale);
        n.Preview(pos, rot, scale);
        _timer = delay;
    }

    public void Preview()
    {
        foreach (var ((layer, id), t) in _pending)
            if (_pieces.NodeOf(layer, id) is { } pn && !pn.Placed) pn.Preview(t.pos, t.rot, t.scale);
    }

    public void RotateSmooth(double deg)
    {
        if (_hand.Holding) { _hand.Rotate(deg); return; }
        Turn(_selection.Movable.ToList(), deg);
    }

    public void Turn(IReadOnlyCollection<PieceNode> nodes, double deg)
    {
        foreach (var n in nodes) { var c = Current(n); Set(n, c.pos, c.rot + deg, c.scale); }
        _c.Sound.Turn(nodes);
        _c.Say("The wheel turns 10° a notch · Ctrl 2° · Shift ½° · Alt+wheel zooms · drag the ring knobs");
    }

    public void ScaleSmooth(double factor)
    {
        if (_hand.Holding) { _c.Say("Put the pieces down to resize them"); return; }
        foreach (var n in _selection.Movable) { var c = Current(n); Set(n, c.pos, c.rot, Math.Clamp(c.scale * factor, MinScale, MaxScale)); }
    }

    public void SetSelectionTransform(double? rot, double? scale)
    {
        foreach (var n in _selection.Movable) { var c = Current(n); Set(n, c.pos, rot ?? c.rot, scale is { } sc ? Math.Clamp(sc, MinScale, MaxScale) : c.scale); }
    }

    public void Flush()
    {
        _timer = -1;
        if (_pending.Count == 0) return;
        var cmds = new List<Command>();
        foreach (var ((layer, id), t) in _pending)
        {
            var p = _c.S.View.Find(layer, id);
            if (p == null) continue;
            if ((t.pos - p.Pos).Length > 1e-6 || Math.Abs(Table.Norm360(t.rot) - p.RotDeg) > 1e-6)
                cmds.Add(new MovePiece { Layer = layer, Id = id, Pos = t.pos, RotDeg = Table.Norm360(t.rot), Stack = p.Stack });
            if (Math.Abs(t.scale - p.Scale) > 1e-6)
                cmds.Add(new KTable.Core.Protocol.SetScale { Layer = layer, Id = id, Scale = t.scale });
        }
        _pending.Clear();
        _c.DoAll(cmds);
    }
}
