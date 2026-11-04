using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PieceClipboard
{
    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Selection _selection;
    readonly Carry _hand;
    public PieceClipboard(TableContext c, PieceLayer pieces, Selection selection, Carry hand)
    {
        _c = c;
        _pieces = pieces;
        _selection = selection;
        _hand = hand;
    }

    List<PieceInstance> _clip = new();
    Vec2 _center;
    readonly HashSet<string> _selectWhenArrived = new();

    public void Copy(bool cut = false)
    {
        var nodes = _selection.Nodes.ToList();
        if (nodes.Count == 0) return;
        var ids = nodes.Select(n => n.Piece.Id).ToHashSet();
        var layer = nodes[0].Layer;
        var all = new List<PieceInstance>();
        foreach (var n in nodes.Where(n => n.Layer == layer))
        {
            all.Add(n.Piece.Clone());
            foreach (var cid in n.Piece.Attached)
                if (!ids.Contains(cid) && _c.S.View.Find(layer, cid) is { } ch) { all.Add(ch.Clone()); ids.Add(cid); }
        }
        _clip = all;
        _center = new Vec2(nodes.Average(n => n.Piece.Pos.X), nodes.Average(n => n.Piece.Pos.Z));
        _c.Say($"{(cut ? "Cut" : "Copied")} {nodes.Count} piece{(nodes.Count > 1 ? "s" : "")} — Ctrl+V pastes at the pointer");
    }

    public void Paste(Vec2 at)
    {
        if (_clip.Count == 0) return;
        var layer = Layers.Umpire;
        var map = _clip.ToDictionary(p => p.Id, _ => "x-c" + Guid.NewGuid().ToString("N")[..10]);
        var cmds = new List<Command>();
        foreach (var p in _clip)
        {
            var c = p.Clone();
            c.Id = map[p.Id];
            c.Pos = at + (p.Pos - _center);
            c.Attached = new();
            c.Stack = 0;
            var above = p.Y > 0 ? Math.Max(0, p.Y - _hand.SurfaceY(p.Pos, layer)) : 0;
            c.Y = _hand.StoredAt(c.Pos, _hand.SurfaceY(c.Pos, layer) + above);
            cmds.Add(new RestorePiece { Layer = layer, Piece = c });
        }
        foreach (var p in _clip)
            foreach (var child in p.Attached)
                if (map.TryGetValue(child, out var nc)) cmds.Add(new Attach { Layer = layer, Parent = map[p.Id], Child = nc });
        _selectWhenArrived.Clear();
        foreach (var p in _clip) _selectWhenArrived.Add(map[p.Id]);
        _c.S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
    }

    public void SelectArrived()
    {
        if (_selectWhenArrived.Count == 0) return;
        var present = _pieces.Keys.Where(k => _selectWhenArrived.Contains(k.id)).Select(k => k.id).ToList();
        if (present.Count == 0) return;
        _selectWhenArrived.Clear();
        _selection.Set(present);
    }
}
