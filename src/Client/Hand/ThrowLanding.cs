using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Hand;

public readonly record struct Thrown<T>(T Node, Vec2 Pos, double Rot, double Bottom) where T : class, ITablePiece;

public static class ThrowLanding
{
    public static List<(T n, MovePiece m)> Land<T>(Footing<T> footing, IReadOnlyList<Thrown<T>> thrown) where T : class, ITablePiece
    {
        var moving = thrown.Select(t => t.Node).ToHashSet();
        var landed = new Dictionary<T, Landed>();
        var moves = new List<(T, MovePiece)>();
        foreach (var t in thrown.OrderBy(t => t.Bottom))
        {
            var (n, pos) = (t.Node, t.Pos);
            double stored = 0;
            if (t.Bottom > footing.GroundUnder(n, pos, t.Rot) + Footing<T>.Settle)
            {
                (pos, var y) = footing.Rest(n, pos, t.Rot, moving, slide: true, landed);
                stored = footing.Stored(n, pos, t.Rot, y, moving, landed);
            }
            landed[n] = new Landed(pos, t.Rot, footing.AbsoluteY(n, pos, t.Rot, stored) + n.Thickness);
            moves.Add((n, new MovePiece { Layer = n.Layer, Id = n.Piece.Id, Pos = pos, RotDeg = t.Rot, Stack = 0, Y = stored }));
        }
        return moves;
    }
}
