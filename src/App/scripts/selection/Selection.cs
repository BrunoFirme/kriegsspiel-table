using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;

namespace KTable.App;

sealed class Selection
{
    readonly TableContext _c;
    readonly PieceLayer _pieces;
    public Selection(TableContext c, HashSet<string> ids, PieceLayer pieces)
    {
        _c = c;
        Ids = ids;
        _pieces = pieces;
    }

    public readonly HashSet<string> Ids;

    public event Action? Picked;

    public int Listed { get; private set; }

    public IEnumerable<PieceNode> Nodes
    {
        get
        {
            if (Ids.Count == 0) return [];
            Listed++;
            return _pieces.Nodes.Where(n => Ids.Contains(n.Piece.Id) && _c.LayerInteractive(n.Layer));
        }
    }

    public IEnumerable<PieceNode> Touchable => Nodes.Where(n => _c.S.CanTouch(n.Layer, n.Piece));

    public IEnumerable<PieceNode> Movable => Touchable.Where(n => !n.Piece.Locked);

    public IEnumerable<PieceNode> Handled => _c.Tool == Tool.Transform ? Touchable : Movable;

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
        if (Ids.Contains(hit.Piece.Id))
        {
            Ids.Remove(hit.Piece.Id);
            hit.SetSelected(false);
            _c.NotifySelectionChanged();
        }
        else Set(new[] { hit.Piece.Id }, add: true);
    }

    public void SetGroup(IEnumerable<string> ids, bool add = false)
    {
        var picked = ids.ToList();
        var alongside = picked.Concat(add ? Ids : []).ToHashSet();
        var readyOut = ReadyOut();
        Set(picked.Where(id => !readyOut.TryGetValue(id, out var holder) || alongside.Contains(holder)), add);
    }

    public Dictionary<string, string> ReadyOut() => ReadyPiece.OutHolders(_pieces.Nodes.Select(n => n.Piece));

    public void SelectAllEditable() => SetGroup(_pieces.Nodes.Where(n => _c.LayerInteractive(n.Layer)).Select(n => n.Piece.Id));
}
