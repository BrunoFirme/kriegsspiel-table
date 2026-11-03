using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Hand;

public sealed class Landing<T> where T : class, ITablePiece
{
    readonly Footing<T> _footing;
    readonly List<HeldPiece<T>> _held;
    readonly Vec2 _cursor;
    readonly HashSet<T> _moving;
    readonly bool _slide;
    readonly Dictionary<HeldPiece<T>, (Vec2 pos, double y, Vec2 slid)> _landed = new();

    public Landing(Footing<T> footing, List<HeldPiece<T>> held, Vec2 cursor, int slideAtMost)
    {
        _footing = footing;
        _held = held;
        _cursor = cursor;
        _moving = held.Select(h => h.Node).ToHashSet();
        _slide = held.Count <= slideAtMost;
    }

    public (Vec2 pos, double y, Vec2 slid) Land(HeldPiece<T> h)
    {
        if (_landed.TryGetValue(h, out var done)) return done;
        var hand = _cursor + h.Offset;
        (Vec2, double, Vec2) r;
        if (h.Parent != null && _held.Contains(h.Parent))
        {
            var under = Land(h.Parent);
            r = (hand + under.slid, under.y + h.Lift, under.slid);
        }
        else
        {
            var (p, y) = _footing.Rest(h.Node, hand, h.Rot, _moving, _slide);
            r = (p, y, p - hand);
        }
        return _landed[h] = r;
    }

    public MovePiece Move(HeldPiece<T> h)
    {
        var (pos, y, _) = Land(h);
        if (h.Parent == null || !_held.Contains(h.Parent)) y = _footing.Stored(h.Node, pos, h.Rot, y, _moving);
        return new MovePiece { Layer = h.Node.Layer, Id = h.Node.Piece.Id, Pos = pos, RotDeg = Angles.Norm360(h.Rot), Stack = 0, Y = y };
    }

    public static List<Command> ParentsFirst(List<(T n, MovePiece m)> moved, IEnumerable<PieceInstance> layer)
    {
        var children = layer.SelectMany(p => p.Attached).ToHashSet();
        return moved.OrderBy(x => children.Contains(x.n.Piece.Id) ? 1 : 0).Select(x => (Command)x.m).ToList();
    }
}

public static class Angles
{
    public static double Norm360(double d) { d %= 360; return d < 0 ? d + 360 : d; }
}
