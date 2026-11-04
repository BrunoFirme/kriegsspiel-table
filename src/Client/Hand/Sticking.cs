using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Hand;

public sealed class Sticking<T> where T : class, ITablePiece
{
    readonly Func<Vec2, IEnumerable<T>> _near;
    readonly Func<string, string, T?> _nodeOf;
    readonly Func<string, IEnumerable<PieceInstance>> _layer;
    readonly Footing<T>? _footing;

    public Sticking(Func<Vec2, IEnumerable<T>> near, Func<string, string, T?> nodeOf, Func<string, IEnumerable<PieceInstance>> layer, Footing<T>? footing = null)
    {
        _near = near;
        _nodeOf = nodeOf;
        _layer = layer;
        _footing = footing;
    }

    public (List<Command> cmds, int stuck, int freed, string to) Stickiness(List<T> dropped, List<(T n, MovePiece m)> moved, HashSet<T> simulated)
    {
        var cmds = new List<Command>();
        int stuck = 0, freed = 0;
        string to = "";
        var pieces = _layer(dropped.Count > 0 ? dropped[0].Layer : Layers.Umpire);
        foreach (var n in dropped)
        {
            var m = moved.FirstOrDefault(x => x.n == n).m;
            var pos = m?.Pos ?? n.Piece.Pos;
            double y = Absolute(n, pos, m?.RotDeg ?? n.Piece.RotDeg, m?.Y ?? n.Piece.Y);
            var parent = pieces.FirstOrDefault(p => p.Attached.Contains(n.Piece.Id));
            var under = BaseUnder(n, pos, y, parent, dropped.ToHashSet(), moved, simulated);
            if (under != null && parent?.Id != under.Piece.Id)
            {
                cmds.Add(new Attach { Layer = n.Layer, Parent = under.Piece.Id, Child = n.Piece.Id });
                stuck++;
                to = under.CardTitle;
            }
            else if (under == null && parent != null)
            {
                cmds.Add(new Detach { Layer = n.Layer, Child = n.Piece.Id });
                freed++;
            }
        }
        return (cmds, stuck, freed, to);
    }

    double Absolute(T n, Vec2 pos, double rot, double stored) => _footing?.AbsoluteY(n, pos, rot, stored) ?? stored;

    T? BaseUnder(T n, Vec2 pos, double y, PieceInstance? parent, HashSet<T> droppedSet, List<(T n, MovePiece m)> moved, HashSet<T> simulated)
    {
        T? under = null;
        double underTop = 0;
        var parentNode = parent != null ? _nodeOf(n.Layer, parent.Id) : null;
        foreach (var o in parentNode != null ? _near(pos).Append(parentNode) : _near(pos))
        {
            if (o == n || o.Layer != n.Layer || o.IsDie || o.IsEffect) continue;
            if ((droppedSet.Contains(o) || simulated.Contains(o)) && o.Piece.Id != parent?.Id) continue;
            if (o.Footprint.X * o.Footprint.Y <= n.Footprint.X * n.Footprint.Y * 1.05f) continue;
            if (n.Piece.Attached.Contains(o.Piece.Id)) continue;
            var om = moved.FirstOrDefault(x => x.n == o).m;
            var oPos = om?.Pos ?? o.Piece.Pos;
            var oRot = om?.RotDeg ?? o.Piece.RotDeg;
            double oTop = om != null ? Absolute(o, oPos, oRot, om.Y) + 0.03 + o.Thickness : o.TopY;
            if (!o.ContainsAt(pos, oPos, oRot)) continue;
            if (Math.Abs(y + 0.03 - oTop) > 0.12) continue;
            if (under == null || oTop > underTop) { under = o; underTop = oTop; }
        }
        return under;
    }
}
