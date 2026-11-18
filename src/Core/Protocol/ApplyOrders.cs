using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static class TimedOrders
{
    internal static string? Apply(GameState g, OrderPiece o, ref Command? inverse)
    {
        if (o.Asks) return "an order is routed by the host";
        var p = g.Find(o.Layer, o.Id);
        if (p == null) return "no such piece";
        if (o.Order?.Fault() is { } fault) return fault;
        if (o.Order == null && o.Pos == null) return "a piece taken off its order needs a place";
        inverse = Unordered(o.Layer, p);
        p.Timed = o.Order?.Clone();
        var at = o.Order is { } order ? order.At(g.Clock.Moment) : new OrderPoint(o.Pos!.Value, o.RotDeg ?? p.RotDeg, true);
        Shift(g, o.Layer, p, at.Pos, at.RotDeg);
        return null;
    }

    static OrderPiece Unordered(string layer, PieceInstance p) =>
        new() { Layer = layer, Id = p.Id, Order = p.Timed?.Clone(), Pos = p.Pos, RotDeg = p.RotDeg };

    internal static Command Ending(string layer, PieceInstance p, Command back)
    {
        if (p.Timed == null) return back;
        var restore = Unordered(layer, p);
        p.Timed = null;
        return new Batch { Items = new() { back, restore } };
    }

    public static void Shift(GameState g, string layer, PieceInstance p, Vec2 pos, double rotDeg)
    {
        var from = p.Pos;
        double drot = StateMachine.Norm(rotDeg) - p.RotDeg;
        p.Pos = pos;
        p.RotDeg = StateMachine.Norm(rotDeg);
        if (p.Attached is not { Count: > 0 }) return;
        var rad = -drot * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        foreach (var ch in StateMachine.Carried(g, layer, p))
        {
            var off = ch.Pos - from;
            ch.Pos = pos + new Vec2(off.X * cos - off.Z * sin, off.X * sin + off.Z * cos);
            ch.RotDeg = StateMachine.Norm(ch.RotDeg + drot);
        }
    }

    public static int Play(GameState g, double moment, List<(string Layer, PieceInstance Piece)>? moved = null)
    {
        int count = 0;
        foreach (var (layer, pieces) in g.Layers)
        {
            HashSet<string>? riding = null;
            foreach (var p in pieces)
            {
                if (p.Timed is not { } order) continue;
                riding ??= pieces.SelectMany(x => x.Attached ?? []).ToHashSet();
                if (riding.Contains(p.Id)) continue;
                var at = order.At(moment);
                if (at.Pos == p.Pos && Math.Abs(StateMachine.Norm(at.RotDeg) - p.RotDeg) < 1e-9) continue;
                Shift(g, layer, p, at.Pos, at.RotDeg);
                moved?.Add((layer, p));
                count++;
            }
        }
        return count;
    }

    public static IEnumerable<(string Layer, PieceInstance Piece)> Ordered(GameState g) =>
        g.Layers.SelectMany(l => l.Value.Where(p => p.Timed != null).Select(p => (l.Key, p)));
}
