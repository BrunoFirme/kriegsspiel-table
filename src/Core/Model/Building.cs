namespace KTable.Core.Model;

public sealed class Building
{
    const double HalfTurnDeg = 180;

    readonly double _cos, _sin;

    public PieceInstance Piece { get; }
    public double Wide { get; }
    public double Deep { get; }
    public List<(Vec2 At, double Level)> OpenDoors { get; }

    public Building(PieceInstance p, PieceDef d, Func<string, PieceDef?> find)
    {
        Piece = p;
        var size = p.State >= 0 && p.State < d.States.Count && d.States[p.State].Size is { } own ? own : d.Size;
        (Wide, Deep) = (size.W * p.Scale * p.WideBy, size.H * p.Scale * p.DeepBy);
        double rad = p.RotDeg * Math.PI / HalfTurnDeg;
        (_cos, _sin) = (Math.Cos(rad), Math.Sin(rad));
        OpenDoors = Doors.OpenParts(d, p.State, find).Select(part => (p.Pos + Vec2.OnPiece(part.X * p.Scale, part.Z * p.Scale, p.RotDeg), p.Y + part.Y * p.Scale)).ToList();
    }

    public static bool IsOne(PieceDef? d) => d?.Tags.Contains(Doors.BuildingTag) == true;

    public bool Holds(Vec2 q)
    {
        var o = q - Piece.Pos;
        return Math.Abs(o.X * _cos - o.Z * _sin) <= Wide / 2 && Math.Abs(o.X * _sin + o.Z * _cos) <= Deep / 2;
    }
}
