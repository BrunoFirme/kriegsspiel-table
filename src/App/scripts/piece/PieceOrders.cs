using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PieceOrders
{
    const double IndexEverySeconds = 0.25;
    const float RoutePx = 2f;
    public const double LeastPace = 0.25, MostPace = 2;
    static readonly Color Ink = new(0.35f, 0.75f, 1f);

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly ToolOverlay _overlay;
    readonly Selection _selection;
    readonly List<(string Layer, PieceInstance Piece)> _moved = new();
    readonly List<Vec2> _via = new();
    List<PieceNode> _going = new();
    double _indexIn;
    StandardMaterial3D? _ink, _faint;

    public PieceOrders(TableContext c, PieceLayer pieces, ToolOverlay overlay, Selection selection)
    {
        _c = c;
        _pieces = pieces;
        _overlay = overlay;
        _selection = selection;
        overlay.Drawing += Draw;
    }

    public event Action? Changed;

    public string By { get; private set; } = KTable.Core.Model.Travellers.Foot;
    public double Pace { get; private set; } = 1;
    public bool AddingStops { get; private set; }
    public int Stops => _via.Count;
    public IReadOnlyList<PieceNode> Going => _going.Where(GodotObject.IsInstanceValid).ToList();
    public bool AnyOnOrder => Going.Any(n => n.Piece.Timed != null);

    public IReadOnlyList<string> Travellers()
    {
        var all = TravelMaps.Travellers(_c.S.View, _c.S.Store.Pack).Keys.ToList();
        return KTable.Core.Model.Travellers.Names.Where(all.Contains).Concat(all.Where(t => !KTable.Core.Model.Travellers.Names.Contains(t)).Order(StringComparer.Ordinal)).ToList();
    }

    public void Tick(double delta)
    {
        if (_c.S.Reviewing) return;
        _moved.Clear();
        var live = _c.S.View;
        if (TimedOrders.Play(live, live.Clock.Moment, _moved) == 0) return;
        bool index = (_indexIn -= delta) <= 0;
        if (index) _indexIn = IndexEverySeconds;
        foreach (var (layer, p) in _moved)
        {
            Settle(layer, p.Id, index);
            foreach (string child in p.Attached) Settle(layer, child, index);
        }
    }

    void Settle(string layer, string id, bool index)
    {
        if (_pieces.NodeOf(layer, id) is not { } node || node.Placed) return;
        node.Resettle();
        if (index) _pieces.Grid.Index(node);
    }

    static bool Orderable(PieceNode n) => n.Layer == Layers.Umpire && !n.IsDie;

    public static bool CanOrder(Session s, IEnumerable<PieceNode> nodes)
    {
        var some = nodes.Where(Orderable).ToList();
        if (!s.IsUmpire || some.Count == 0) return false;
        return some.Any(n => n.Piece.Timed != null) || s.View.OrdersOn && some.Any(n => TravelMaps.Routable(s.View, n.Piece.Pos));
    }

    public void Arm(IEnumerable<PieceNode> nodes)
    {
        _going = nodes.Where(n => Orderable(n) && _c.S.CanTouch(n.Layer, n.Piece)).ToList();
        _via.Clear();
        AddingStops = false;
        if (_going.Count > 0) _c.SetTool(Tool.Order);
        Changed?.Invoke();
    }

    public void Stop()
    {
        var stops = Going.Where(n => n.Piece.Timed != null).Select(n => (Command)new OrderPiece { Layer = n.Layer, Id = n.Piece.Id, Stop = true }).ToList();
        if (stops.Count > 0) _c.S.Do(stops.Count == 1 ? stops[0] : new Batch { Items = stops });
        Changed?.Invoke();
    }

    public void Choose(string by)
    {
        By = by;
        Changed?.Invoke();
    }

    public void SetPace(double pace) => Pace = Math.Clamp(pace, LeastPace, MostPace);

    public void SetAddingStops(bool on)
    {
        AddingStops = on;
        _c.Say(Hint());
        Changed?.Invoke();
    }

    public void ClearStops()
    {
        _via.Clear();
        Changed?.Invoke();
    }

    public string Hint() => AddingStops ? "Click the stops on the way · turn Add off to click where to go" : "Click where to go · Shift+click adds a stop";

    public void Press(InputEventMouseButton mb, Vec2 at)
    {
        if (mb.ShiftPressed || AddingStops)
        {
            _via.Add(at);
            Changed?.Invoke();
            return;
        }
        Give(at);
        _c.SetTool(Tool.Select);
    }

    void Give(Vec2 at)
    {
        var going = Going;
        if (going.Count == 0) return;
        var lead = going[0];
        var orders = new List<Command>();
        foreach (var n in going)
        {
            var off = n.Piece.Pos - lead.Piece.Pos;
            var to = _via.Append(at).Select(p => p + off).ToList();
            orders.Add(new OrderPiece { Layer = n.Layer, Id = n.Piece.Id, To = to, By = By, Pace = Math.Abs(Pace - 1) < 1e-9 ? null : Pace });
        }
        _via.Clear();
        _c.S.Do(orders.Count == 1 ? orders[0] : new Batch { Items = orders });
    }

    public bool Pressed(InputEventKey k)
    {
        if (k.Keycode == Godot.Key.Backspace && _via.Count > 0)
        {
            _via.RemoveAt(_via.Count - 1);
            Changed?.Invoke();
            return true;
        }
        if (k.Keycode is < Godot.Key.Key1 or > Godot.Key.Key9 || k.CtrlPressed || k.AltPressed || k.ShiftPressed) return false;
        var all = Travellers();
        int at = (int)(k.Keycode - Godot.Key.Key1);
        if (at >= all.Count) return false;
        Choose(all[at]);
        return true;
    }

    void Draw()
    {
        if (!_c.S.IsUmpire) return;
        _ink ??= Material(Ink);
        _faint ??= Material(new Color(Ink, 0.45f));
        float y = _overlay.Lift;
        foreach (string id in _selection.Ids)
            if (_pieces.NodeOf(Layers.Umpire, id) is { Piece.Timed: { } order } n && !order.At(_c.S.View.Clock.Moment).Arrived)
                _overlay.RibbonPx(_overlay.Mesh, _faint, Ahead(order, n.Piece.Pos), y, RoutePx);
        if (_c.Tool != Tool.Order || Going.FirstOrDefault() is not { } lead) return;
        var line = new List<Vec2> { lead.Piece.Pos };
        line.AddRange(_via);
        if (_c.Ground(Pointer.In(_c.Scene.GetViewport())) is { } here) line.Add(here);
        if (line.Count >= 2) _overlay.RibbonPx(_overlay.Mesh, _ink, line, y, RoutePx);
    }

    List<Vec2> Ahead(TimedOrder order, Vec2 now)
    {
        var line = new List<Vec2> { now };
        double gone = (_c.S.View.Clock.Moment - order.Start) * order.Pace;
        for (int i = 0; i < order.Legs.Count; i++)
        {
            gone -= order.Legs[i];
            if (gone < 0) line.Add(order.Points[i + 1]);
        }
        if (line.Count == 1) line.Add(order.End);
        return line;
    }

    static StandardMaterial3D Material(Color ink) => new()
    {
        AlbedoColor = ink, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };
}
