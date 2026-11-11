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
        var droppedIds = dropped.Select(d => d.Piece.Id).ToHashSet();
        var droppedSet = dropped.ToHashSet();
        var parentOf = new Dictionary<string, PieceInstance>();
        foreach (var p in pieces)
            foreach (var child in p.Attached) parentOf.TryAdd(child, p);
        var readyOut = ReadyPiece.OutHolders(pieces);
        var movedTo = new Dictionary<T, MovePiece>();
        foreach (var (mn, mm) in moved) movedTo.TryAdd(mn, mm);
        foreach (var n in dropped)
        {
            if (readyOut.ContainsKey(n.Piece.Id)) continue;
            var m = movedTo.GetValueOrDefault(n);
            var pos = m?.Pos ?? n.Piece.Pos;
            double y = Absolute(n, pos, m?.RotDeg ?? n.Piece.RotDeg, m?.Y ?? n.Piece.Y);
            var parent = parentOf.GetValueOrDefault(n.Piece.Id);
            if (parent != null && (droppedIds.Contains(parent.Id) || LinkedFromAfar(n, parent))) continue;
            var under = BaseUnder(n, pos, y, parent, parentOf, droppedSet, movedTo, simulated);
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

    const double RestGap = 0.03, RestReach = 0.12;
    const float SameSize = 0.95f;

    static bool Carries(Dictionary<string, PieceInstance> parentOf, string carrier, string id)
    {
        var seen = new HashSet<string>();
        for (var at = id; parentOf.TryGetValue(at, out var up) && seen.Add(at); at = up.Id)
            if (up.Id == carrier) return true;
        return false;
    }

    bool LinkedFromAfar(T n, PieceInstance parent) =>
        _nodeOf(n.Layer, parent.Id) is { } p
        && !(p.ContainsAt(n.Piece.Pos, parent.Pos, parent.RotDeg) && Math.Abs(Absolute(n, n.Piece.Pos, n.Piece.RotDeg, n.Piece.Y) + RestGap - p.TopY) <= RestReach);

    T? BaseUnder(T n, Vec2 pos, double y, PieceInstance? parent, Dictionary<string, PieceInstance> parentOf, HashSet<T> droppedSet, Dictionary<T, MovePiece> movedTo, HashSet<T> simulated)
    {
        T? under = null;
        double underTop = 0;
        var parentNode = parent != null ? _nodeOf(n.Layer, parent.Id) : null;
        foreach (var o in parentNode != null ? _near(pos).Append(parentNode) : _near(pos))
        {
            if (o == n || o.Layer != n.Layer || o.IsDie || o.IsEffect) continue;
            if ((droppedSet.Contains(o) || simulated.Contains(o)) && o.Piece.Id != parent?.Id) continue;
            if (o.Footprint.X * o.Footprint.Y < n.Footprint.X * n.Footprint.Y * SameSize) continue;
            if (Carries(parentOf, n.Piece.Id, o.Piece.Id)) continue;
            var om = movedTo.GetValueOrDefault(o);
            var oPos = om?.Pos ?? o.Piece.Pos;
            var oRot = om?.RotDeg ?? o.Piece.RotDeg;
            double oTop = om != null ? Absolute(o, oPos, oRot, om.Y) + RestGap + o.Thickness : o.TopY;
            if (!o.ContainsAt(pos, oPos, oRot)) continue;
            if (Math.Abs(y + RestGap - oTop) > RestReach) continue;
            if (under == null || oTop > underTop) { under = o; underTop = oTop; }
        }
        return under;
    }
}
