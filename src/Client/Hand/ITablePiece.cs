using System.Numerics;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public interface ITablePiece
{
    string Layer { get; }
    PieceInstance Piece { get; }
    Vector2 Footprint { get; }
    Vector2 GroundCentre { get; }
    float Thickness { get; }
    float TopY { get; }
    bool IsDie { get; }
    bool IsEffect { get; }
    bool IsProp { get; }
    bool FollowsGround { get; }
    bool Placed { get; }
    string CardTitle { get; }
    float? SurfaceAt(Vec2 p);
    float SurfaceReach { get; }
    bool ContainsAt(Vec2 point, Vec2 at, double rotDeg);
}

public static class TablePieces
{
    public static Vec2 CentreOffset(this ITablePiece n, double rotDeg) => Vec2.OnPiece(n.GroundCentre.X, n.GroundCentre.Y, rotDeg);

    public static Vec2 MiddleAt(this ITablePiece n, Vec2 pos, double rotDeg) => pos + n.CentreOffset(rotDeg);
}

public sealed class HeldPiece<T> where T : class
{
    public required T Node;
    public string Layer = "", Id = "";
    public Vec2 Offset;
    public double Rot;
    public float Lift;
    public HeldPiece<T>? Parent;
}
