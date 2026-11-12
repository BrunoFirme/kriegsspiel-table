using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Hand;

public sealed class Falling<T> where T : class, ITablePiece
{
    const double Drops = 0.005, Above = 0.005;

    readonly Footing<T> _footing;
    readonly Func<Vec2, double, IEnumerable<T>> _around;
    readonly Func<string, string, T?> _node;
    readonly Func<string, bool> _interactive;

    readonly record struct Leaving(T Node, Vec2? To, double Rot);

    public Falling(Footing<T> footing, Func<Vec2, double, IEnumerable<T>> around, Func<string, string, T?> node, Func<string, bool> interactive)
    {
        _footing = footing;
        _around = around;
        _node = node;
        _interactive = interactive;
    }

    public Command With(Command c)
    {
        var leaving = new List<Leaving>();
        Collect(c, leaving);
        if (leaving.Count == 0) return c;
        var falls = Falls(leaving);
        if (falls.Count == 0) return c;
        var items = c is Batch b ? new List<Command>(b.Items) : new List<Command> { c };
        items.AddRange(falls);
        return new Batch { Items = items };
    }

    void Collect(Command c, List<Leaving> into)
    {
        switch (c)
        {
            case Batch b:
                foreach (var i in b.Items) if (i != null) Collect(i, into);
                break;
            case MovePiece m when _node(m.Layer, m.Id) is { } n:
                into.Add(new Leaving(n, m.Pos, m.RotDeg));
                break;
            case DeletePiece d when _node(d.Layer, d.Id) is { } n:
                into.Add(new Leaving(n, null, 0));
                break;
            case Stow s when _node(s.Layer, s.Id) is { } n:
                into.Add(new Leaving(n, null, 0));
                break;
        }
    }

    List<Command> Falls(List<Leaving> leaving)
    {
        var moving = new HashSet<T>();
        foreach (var l in leaving) WithAttached(l.Node, moving);
        var falls = new List<Command>();
        foreach (var l in leaving)
        {
            var under = l.Node;
            double underY = _footing.AbsoluteY(under, under.Piece.Pos, under.Piece.RotDeg, under.Piece.Y);
            foreach (var n in _around(under.Piece.Pos, under.SurfaceReach).ToList())
            {
                if (moving.Contains(n) || !CanFall(n, under) || n.Piece.Y <= underY + Above) continue;
                if ((n.Piece.Pos - under.Piece.Pos).Length > under.SurfaceReach + n.SurfaceReach) continue;
                if (l.To is { } to && under.ContainsAt(n.Piece.Pos, to, l.Rot)) continue;
                var skip = new HashSet<T>(moving) { n };
                foreach (var o in _around(n.Piece.Pos, n.SurfaceReach)) if (o.Piece.Y >= n.Piece.Y - Above) skip.Add(o);
                var (_, y) = _footing.Rest(n, n.Piece.Pos, n.Piece.RotDeg, skip, slide: false);
                double stored = _footing.Stored(n, n.Piece.Pos, n.Piece.RotDeg, y, skip);
                double drop = n.Piece.Y - _footing.AbsoluteY(n, n.Piece.Pos, n.Piece.RotDeg, stored);
                if (drop <= Drops) continue;
                Lower(n, stored, drop, moving, falls);
            }
        }
        return falls;
    }

    void Lower(T n, double y, double drop, HashSet<T> moving, List<Command> falls)
    {
        var riders = _footing.RestingOn(n, _around(n.Piece.Pos, n.SurfaceReach), _interactive);
        WithAttached(n, moving);
        falls.Add(new MovePiece { Layer = n.Layer, Id = n.Piece.Id, Pos = n.Piece.Pos, RotDeg = n.Piece.RotDeg, Stack = n.Piece.Stack, Y = Math.Max(0, y) });
        foreach (var r in riders)
            if (!moving.Contains(r) && CanFall(r, n)) Lower(r, r.Piece.Y - drop, drop, moving, falls);
    }

    void WithAttached(T n, HashSet<T> moving)
    {
        if (!moving.Add(n)) return;
        foreach (var id in n.Piece.Attached ?? [])
            if (id != null && _node(n.Layer, id) is { } child) WithAttached(child, moving);
    }

    bool CanFall(T n, T under) =>
        n.Layer == under.Layer && n.Piece.Y > 0 && !n.Piece.Locked && !n.Piece.PassesThrough && !n.IsDie && !n.IsEffect && !n.Placed && _interactive(n.Layer);
}
