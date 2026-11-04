using System;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PaletteDrag
{
    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Selection _selection;
    readonly Carry _hand;
    public PaletteDrag(TableContext c, PieceLayer pieces, Selection selection, Carry hand)
    {
        _c = c;
        _pieces = pieces;
        _selection = selection;
        _hand = hand;
    }

    string? _def;
    Func<Vector2, bool> _overDrawer = _ => false;
    string? _made;
    bool _inHand;

    public bool Dragging => _def != null;

    public void Begin(string def, Func<Vector2, bool> overDrawer)
    {
        End(drop: true);
        _def = def;
        _overDrawer = overDrawer;
        _c.Say("Hold Shift as you let go to keep placing");
    }

    public void Tick(bool down, Vector2 m)
    {
        if (_def == null) return;
        if (!down) { End(drop: true); return; }
        if (_made == null)
        {
            if (_overDrawer(m) || _c.Ground(m) is not { } g || !_c.S.CanTouch(Layers.Umpire, g)) return;
            _made = "x-p" + Guid.NewGuid().ToString("N")[..10];
            _c.S.Do(new SpawnPiece { Layer = Layers.Umpire, Def = _def, Id = _made, Pos = g, RotDeg = 180, Y = _hand.StoredY(g, Layers.Umpire) });
        }
        if (!_inHand && _pieces.NodeOf(Layers.Umpire, _made) is { } n)
        {
            _selection.Set(new[] { _made });
            _hand.StartFromDrawer(n.Piece.Pos, _overDrawer);
            _inHand = _hand.Holding;
        }
    }

    public void End(bool drop)
    {
        if (_def == null) return;
        var made = _made;
        bool inHand = _inHand && _hand.Holding;
        _def = null;
        _made = null;
        _inHand = false;
        if (made == null) return;
        if (inHand) { if (drop) _hand.Drop(); else _hand.PutBack(); }
        else if (!drop) _c.S.Do(new DeletePiece { Layer = Layers.Umpire, Id = made });
        if (!Input.IsKeyPressed(Key.Shift)) _c.SetTool(Tool.Select);
    }
}
