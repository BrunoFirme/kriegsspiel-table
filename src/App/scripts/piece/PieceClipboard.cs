using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Client;
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

    const double WaitForPacks = 10;

    public Action<string>? BringPack { get; set; }

    readonly HashSet<string> _selectWhenArrived = new();
    (Clipping clip, Vec2 at, List<string> packs, double waited)? _waiting;

    public int Count => Clippings.Held?.Pieces.Count ?? 0;

    public void Copy(bool cut = false) => Copy(_selection.Nodes.ToList(), cut);

    public void Copy(List<PieceNode> nodes, bool cut)
    {
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
        Clippings.Hold(new Clipping
        {
            From = _c.S.View.Name,
            Center = new Vec2(nodes.Average(n => n.Piece.Pos.X), nodes.Average(n => n.Piece.Pos.Z)),
            Packs = all.SelectMany(DefsOf).Select(_c.S.View.PackNamedBy).OfType<string>().Distinct().ToList(),
            Pieces = all.Select(p => new ClippedPiece { Piece = p, Above = Above(p, layer) }).ToList(),
        });
        _c.Say($"{(cut ? "Cut" : "Copied")} {nodes.Count} piece{(nodes.Count > 1 ? "s" : "")} — Ctrl+V pastes at the pointer, here or in another scenario");
    }

    static IEnumerable<string> DefsOf(PieceInstance p)
    {
        yield return p.Def;
        if (p.Picture is { Length: > 0 } pic) yield return pic;
    }

    double Above(PieceInstance p, string layer) => p.Y > 0 ? Math.Max(0, p.Y - _hand.SurfaceY(p.Pos, layer)) : 0;

    public void Paste(Vec2 at)
    {
        _waiting = null;
        if (!_c.S.CanEdit(Layers.Umpire)) return;
        if (Clippings.Held is not { Pieces.Count: > 0 } clip) return;
        var wanted = clip.Packs.Where(p => !_c.S.View.Packs.Contains(p)).ToList();
        var coming = wanted.Where(p => _c.S.Store.Pack(p) != null).ToList();
        if (coming.Count > 0 && BringPack is { } bring)
        {
            foreach (var p in coming) bring(p);
            _waiting = (clip, at, coming, 0);
            return;
        }
        Land(clip, at);
    }

    public void Tick(double delta)
    {
        if (_waiting is not { } w) return;
        if (w.waited + delta < WaitForPacks && !w.packs.All(Arrived)) { _waiting = (w.clip, w.at, w.packs, w.waited + delta); return; }
        _waiting = null;
        Land(w.clip, w.at);
    }

    bool Arrived(string pack) => _c.S.View.Packs.Contains(pack) && _c.S.Store.Pack(pack) != null;

    string Named(IEnumerable<string> packs) =>
        string.Join(", ", packs.Select(p => _c.S.Store.Pack(p)?.Name ?? p));

    void Land(Clipping clip, Vec2 at)
    {
        var layer = Layers.Umpire;
        var away = clip.Packs.Where(p => !_c.S.View.Packs.Contains(p)).ToHashSet();
        var taken = clip.Pieces.Where(cp => PackManifest.NamedBy(away, cp.Piece.Def) == null).ToList();
        if (taken.Count == 0)
        {
            _c.Say($"Nothing pasted: {Named(away)} {(away.Count > 1 ? "are" : "is")} not in the game");
            return;
        }
        var map = taken.ToDictionary(cp => cp.Piece.Id, _ => "x-c" + Guid.NewGuid().ToString("N")[..10]);
        var cmds = new List<Command>();
        foreach (var cp in taken)
        {
            var c = cp.Piece.Clone();
            c.Id = map[cp.Piece.Id];
            c.Pos = at + (cp.Piece.Pos - clip.Center);
            c.Attached = new();
            c.Stack = 0;
            c.Y = _hand.StoredAt(c.Pos, _hand.SurfaceY(c.Pos, layer) + cp.Above);
            cmds.Add(new RestorePiece { Layer = layer, Piece = c });
        }
        foreach (var cp in taken)
            foreach (var child in cp.Piece.Attached)
                if (map.TryGetValue(child, out var nc)) cmds.Add(new Attach { Layer = layer, Parent = map[cp.Piece.Id], Child = nc });
        _selectWhenArrived.Clear();
        foreach (var cp in taken) _selectWhenArrived.Add(map[cp.Piece.Id]);
        _c.S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        int left = clip.Pieces.Count - taken.Count;
        if (left > 0) _c.Say($"{left} left out: {Named(away)} {(away.Count > 1 ? "are" : "is")} not in the game");
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
