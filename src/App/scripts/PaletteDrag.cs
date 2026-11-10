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
    (string Layer, string Holder)? _from;

    public bool Dragging => _def != null;

    string Layer => _from?.Layer ?? Layers.Umpire;

    public void Begin(string def, Func<Vector2, bool> overDrawer)
    {
        End(drop: true);
        _def = def;
        _overDrawer = overDrawer;
        _c.Say("Hold Shift as you let go to keep placing");
    }

    public void BeginTakeOut(string layer, string holder, string id, Func<Vector2, bool> overWindow)
    {
        End(drop: true);
        _def = id;
        _from = (layer, holder);
        _overDrawer = overWindow;
    }

    public void Tick(bool down, Vector2 m)
    {
        if (_def == null) return;
        if (!down) { End(drop: true); return; }
        if (_made == null)
        {
            if (_overDrawer(m) || _c.Ground(m) is not { } g || !_c.S.CanTouch(Layer, g)) return;
            if (_from is { } f)
            {
                if (_c.S.View.Find(f.Layer, f.Holder) is not { } holder) { End(drop: true); return; }
                _made = _def;
                _c.S.Do(KTable.Client.TakeOut.Taking(_c.S, f.Layer, holder, new() { new Unstow { Layer = f.Layer, Holder = f.Holder, Id = _def, Pos = g } }));
            }
            else
            {
                _made = "x-p" + Guid.NewGuid().ToString("N")[..10];
                _c.S.Do(new SpawnPiece { Layer = Layers.Umpire, Def = _def, Id = _made, Pos = g, RotDeg = 180, Y = _hand.StoredY(g, Layers.Umpire) });
            }
        }
        if (!_inHand && _pieces.NodeOf(Layer, _made) is { } n)
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
        var from = _from;
        bool inHand = _inHand && _hand.Holding;
        _def = null;
        _made = null;
        _from = null;
        _inHand = false;
        if (made == null) return;
        if (from is { } f)
        {
            if (drop) { if (inHand) _hand.Drop(); }
            else if (!(inHand && _hand.PutInto(f.Layer, f.Holder)) && _pieces.NodeOf(f.Layer, made) != null) _c.S.Do(new Stow { Layer = f.Layer, Holder = f.Holder, Id = made });
            return;
        }
        if (inHand) { if (drop) _hand.Drop(); else _hand.PutBack(); }
        else if (!drop) _c.S.Do(new DeletePiece { Layer = Layers.Umpire, Id = made });
        if (!Input.IsKeyPressed(Key.Shift)) _c.SetTool(Tool.Select);
    }
}
