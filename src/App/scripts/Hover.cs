using System;
using Godot;

namespace KTable.App;

sealed class Hover
{
    readonly TableContext _c;
    readonly PointerPress _press;
    readonly PieceLayer _pieces;
    readonly Carry _hand;
    public Hover(TableContext c, PointerPress press, PieceLayer pieces, Carry hand)
    {
        _c = c;
        _press = press;
        _pieces = pieces;
        _hand = hand;
    }

    public event Action<string?, Vector2>? Changed;

    public PieceNode? Node { get; private set; }
    int _widget;
    double _time;

    public void Update(Vector2 screen, double delta)
    {
        if (_press.LeftDown || _hand.Holding)
        {
            if (Node != null)
            {
                Leave(Node);
                Node = null;
                _widget = 0;
                Changed?.Invoke(null, screen);
            }
            return;
        }
        var g = _c.Ground(screen);
        var hit = g != null ? _pieces.Pick(g.Value, interactiveOnly: false, screen: screen) : null;
        int wi = hit != null && g != null && _c.S.CanTouch(hit.Layer, hit.Piece) ? hit.WidgetUnder(screen)?.index ?? 0 : 0;
        if (hit != Node || wi != _widget)
        {
            if (Node != null && Node != hit) Leave(Node);
            Node = hit;
            _widget = wi;
            _time = 0;
            hit?.SetWidgetHover(wi);
            hit?.SetHovered(true);
            Point(hit, wi);
            Changed?.Invoke(null, screen);
            return;
        }
        if (Node == null) return;
        if (!GodotObject.IsInstanceValid(Node))
        {
            Node = null;
            _widget = 0;
            Point(null, 0);
            Changed?.Invoke(null, screen);
            return;
        }
        _time += delta;
        if (_time > 0.45 && _time - delta <= 0.45)
        {
            var tip = _widget > 0 && _widget <= Node.Piece.Widgets.Count ? Node.Piece.Widgets[_widget - 1].Tooltip : null;
            Changed?.Invoke(tip is { Length: > 0 } ? tip : Node.HoverText(), screen);
        }
    }

    public void Clear(Vector2 screen)
    {
        if (Node == null) return;
        Leave(Node);
        Node = null;
        _widget = 0;
        Changed?.Invoke(null, screen);
    }

    void Leave(PieceNode n)
    {
        Point(null, 0);
        if (!GodotObject.IsInstanceValid(n)) return;
        n.SetWidgetHover(0);
        n.SetHovered(false);
    }

    Input.CursorShape _shape = Input.CursorShape.Arrow;

    public Input.CursorShape Pointer => _shape;

    public void Release() => Point(null, 0);

    void Point(PieceNode? n, int widget)
    {
        var shape = n != null && widget > 0 && widget <= n.Piece.Widgets.Count
            ? n.Piece.Widgets[widget - 1].Kind == "input" ? Input.CursorShape.Ibeam : Input.CursorShape.PointingHand
            : Input.CursorShape.Arrow;
        if (shape == _shape) return;
        _shape = shape;
        Input.SetDefaultCursorShape(shape);
    }
}
