using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class SelectionReport
{
    readonly TableContext _c;
    readonly Selection _selection;
    List<string> _sent = new();
    bool _due;

    public SelectionReport(TableContext c, Selection selection)
    {
        _c = c;
        _selection = selection;
        c.SelectionChanged += Due;
    }

    public IReadOnlyList<string> Sent => _sent;

    void Due()
    {
        if (_due) return;
        _due = true;
        Callable.From(Flush).CallDeferred();
    }

    void Flush()
    {
        _due = false;
        if (_c.S is not { IsUmpire: true } s) return;
        var now = _selection.Nodes.Where(n => n.Layer == Layers.Umpire).Select(n => n.Piece.Id).Take(ReportSelection.MostPieces).ToList();
        if (now.SequenceEqual(_sent)) return;
        _sent = now;
        s.Do(new ReportSelection { Selected = now });
    }
}
