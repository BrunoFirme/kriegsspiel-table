using System;
using System.Collections.Generic;
using System.Linq;

namespace KTable.App;

sealed class Selection
{
    readonly TableContext _c;
    readonly PieceLayer _pieces;
    public Selection(TableContext c, HashSet<string> ids, PieceLayer pieces) { _c = c; Ids = ids; _pieces = pieces; }

    public readonly HashSet<string> Ids;

    public event Action? Picked;

    public IEnumerable<PieceNode> Nodes => _pieces.Nodes.Where(n => Ids.Contains(n.Piece.Id) && _c.LayerInteractive(n.Layer));

    public IEnumerable<PieceNode> Movable => Nodes.Where(n => !n.Piece.Locked && _c.S.CanTouch(n.Layer, n.Piece));

    public void Set(IEnumerable<string> ids, bool add = false)
    {
        if (!add) { foreach (var n in Nodes.ToList()) n.SetSelected(false); Ids.Clear(); }
        foreach (var id in ids) Ids.Add(id);
        foreach (var n in Nodes) n.SetSelected(true);
        Picked?.Invoke();
        _c.NotifySelectionChanged();
    }

    public void Toggle(PieceNode hit)
    {
        if (Ids.Contains(hit.Piece.Id)) { Ids.Remove(hit.Piece.Id); hit.SetSelected(false); _c.NotifySelectionChanged(); }
        else Set(new[] { hit.Piece.Id }, add: true);
    }

    public void SelectAllEditable() => Set(_pieces.Nodes.Where(n => _c.LayerInteractive(n.Layer)).Select(n => n.Piece.Id));
}
