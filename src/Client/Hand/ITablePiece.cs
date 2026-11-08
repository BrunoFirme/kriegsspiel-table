using System.Numerics;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public interface ITablePiece
{
    string Layer { get; }
    PieceInstance Piece { get; }
    Vector2 Footprint { get; }
    float Thickness { get; }
    float TopY { get; }
    bool IsDie { get; }
    bool IsEffect { get; }
    bool IsProp { get; }
    bool Placed { get; }
    string CardTitle { get; }
    float? SurfaceAt(Vec2 p);
    float SurfaceReach { get; }
    bool ContainsAt(Vec2 point, Vec2 at, double rotDeg);
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
