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
    double SeatedY(PieceNode n, Vec2 pos, double rot, HashSet<PieceNode> moving);
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
    readonly Dictionary<(string layer, string id), (SetScale To, Godot.Vector3 Shown)> _stretched = new();
    double _timer = -1;

    public const double MinScale = KTable.Core.Model.PieceInstance.LeastScale, MaxScale = 50;

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
        foreach (var ((layer, id), t) in _stretched)
            if (_pieces.NodeOf(layer, id) is { } pn && !pn.Placed) pn.Stretch.Preview(pn, t.Shown);
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

    public void Nudge(Vec2 by)
    {
        if (_hand.Holding) return;
        foreach (var n in _selection.Movable) { var c = Current(n); Set(n, c.pos + by, c.rot, c.scale); }
    }

    public void LiftSelectionTo(double high)
    {
        Flush();
        if (PieceLift.To(_selection.Handled.Where(PieceLift.Can).Select(n => (n, PieceLift.Settled(high, false)))) is { } lifted) _c.S.Do(lifted);
    }

    public void ScaleSmooth(double factor)
    {
        if (_hand.Holding) { _c.Say("Put the pieces down to resize them"); return; }
        foreach (var n in _selection.Movable) { var c = Current(n); Set(n, c.pos, c.rot, Math.Clamp(c.scale * factor, MinScale, MaxScale)); }
    }

    public (Vec2 Centre, double Rot, double Scale)? SelectionPose()
    {
        var poses = _selection.Handled.Select(Current).ToList();
        if (poses.Count == 0) return null;
        return (new Vec2(poses.Average(p => p.pos.X), poses.Average(p => p.pos.Z)), poses[0].rot, poses[0].scale);
    }

    public void MoveSelectionTo(Vec2 centre)
    {
        if (SelectionPose() is not { } now) return;
        var by = centre - now.Centre;
        foreach (var n in _selection.Handled)
        {
            var c = Current(n);
            Set(n, c.pos + by, c.rot, c.scale);
        }
    }

    public PieceNode? FirstMovable => _selection.Handled.FirstOrDefault();

    public static (double Least, double Most) StretchReach(double now, double by) => (now / by * PieceInstance.LeastStretch, now / by * PieceInstance.MostStretch);

    static double? By(double? wanted, double now, double by) =>
        wanted is { } w && now > 0 && by > 0 ? Math.Clamp(w / (now / by), PieceInstance.LeastStretch, PieceInstance.MostStretch) : null;

    static SetScale Stretched(PieceNode n, double scale, double? across, double? deep, double? tall)
    {
        double? wide = By(across, n.Footprint.X, n.Piece.WideBy), far = By(deep, n.Footprint.Y, n.Piece.DeepBy);
        double kept = n.Stretch.TallKeeping(n, wide ?? n.Piece.WideBy, far ?? n.Piece.DeepBy);
        double? high = By(tall, n.Thickness, n.Piece.TallBy) ?? (kept == n.Piece.TallBy ? null : kept);
        return new SetScale { Layer = n.Layer, Id = n.Piece.Id, Scale = scale, Wide = wide, Deep = far, Tall = high };
    }

    const float RiderReach = 0.12f;

    IEnumerable<Command> WithRiders(PieceNode n, SetScale to)
    {
        yield return to;
        if (to.Tall is not { } tall) yield break;
        double grows = tall / n.Piece.TallBy - 1;
        var rises = new Dictionary<PieceNode, double> { [n] = 0 };
        var carriers = new List<PieceNode> { n };
        for (int i = 0; i < carriers.Count; i++)
        {
            var under = carriers[i];
            foreach (var o in _pieces.Nodes)
            {
                if (rises.ContainsKey(o) || o.Layer != n.Layer || o.Piece.Y <= 0 || o.Piece.Flies || !under.Contains(o.Piece.Pos)) continue;
                if (under.SurfaceAt(Coords.ToWorld(o.Piece.Pos)) is not { } top || Math.Abs(o.RestY - top) > RiderReach) continue;
                rises[o] = under == n ? (top - n.Position.Y + n.LiftY) * grows : rises[under];
                carriers.Add(o);
            }
        }
        foreach (var o in carriers.Skip(1))
            if (Math.Abs(rises[o]) > 1e-9)
                yield return new MovePiece { Layer = o.Layer, Id = o.Piece.Id, Pos = o.Piece.Pos, RotDeg = o.Piece.RotDeg, Stack = o.Piece.Stack, Y = Math.Max(0, o.Piece.Y + rises[o]) };
    }

    public void StretchSelectionTo(double? across, double? deep, double? tall = null)
    {
        Flush();
        _c.DoAll(_selection.Handled.SelectMany(n => WithRiders(n, Stretched(n, n.Piece.Scale, across, deep, tall))).ToList());
    }

    public void Stretch(PieceNode n, double? across, double? deep, double? tall, double delay = 0.35)
    {
        var to = Stretched(n, n.Piece.Scale, across, deep, tall);
        var p = n.Piece;
        float high = tall != null && to.Tall is { } t ? (float)(t / p.TallBy) : 1;
        var shown = new Godot.Vector3((float)((to.Wide ?? p.WideBy) / p.WideBy), high, (float)((to.Deep ?? p.DeepBy) / p.DeepBy));
        _stretched[(n.Layer, p.Id)] = (to, shown);
        n.Stretch.Preview(n, shown);
        _timer = delay;
    }

    public void SetSelectionTransform(double? rot, double? scale)
    {
        foreach (var n in _selection.Handled) { var c = Current(n); Set(n, c.pos, rot ?? c.rot, scale is { } sc ? Math.Clamp(sc, MinScale, MaxScale) : c.scale); }
    }

    public void Flush()
    {
        _timer = -1;
        if (_pending.Count == 0 && _stretched.Count == 0) return;
        var cmds = new List<Command>();
        var moving = _pending.Keys.Select(k => _pieces.NodeOf(k.layer, k.id)).OfType<PieceNode>().ToHashSet();
        foreach (var ((layer, id), t) in _pending)
        {
            var p = _c.S.View.Find(layer, id);
            if (p == null) continue;
            bool moved = (t.pos - p.Pos).Length > 1e-6;
            if (moved || Math.Abs(Table.Norm360(t.rot) - p.RotDeg) > 1e-6)
            {
                double y = moved && !p.Flies && _pieces.NodeOf(layer, id) is { } n ? _hand.SeatedY(n, t.pos, t.rot, moving) : p.Y;
                cmds.Add(new MovePiece { Layer = layer, Id = id, Pos = t.pos, RotDeg = Table.Norm360(t.rot), Stack = p.Stack, Y = y });
            }
            if (Math.Abs(t.scale - p.Scale) > 1e-6 && !_stretched.ContainsKey((layer, id)))
                cmds.Add(new KTable.Core.Protocol.SetScale { Layer = layer, Id = id, Scale = t.scale });
        }
        foreach (var (key, (to, _)) in _stretched)
        {
            if (_pieces.NodeOf(key.layer, key.id) is not { } n) continue;
            n.Stretch.Preview(n, Godot.Vector3.One);
            var p = n.Piece;
            to.Scale = _pending.TryGetValue(key, out var posed) ? posed.scale : p.Scale;
            static bool Same(double a, double b) => Math.Abs(a - b) < 1e-9;
            if (Same(to.Scale, p.Scale) && Same(to.Wide ?? p.WideBy, p.WideBy) && Same(to.Deep ?? p.DeepBy, p.DeepBy) && Same(to.Tall ?? p.TallBy, p.TallBy)) continue;
            cmds.AddRange(WithRiders(n, to));
        }
        _stretched.Clear();
        _pending.Clear();
        _c.DoAll(cmds);
    }
}
