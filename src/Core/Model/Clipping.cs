namespace KTable.Core.Model;

public sealed class ClippedPiece
{
    public PieceInstance Piece { get; set; } = new();
    public double Above { get; set; }
}

public sealed class Clipping
{
    public const int CurrentFormat = 1;
    public int Format { get; set; } = CurrentFormat;
    public string From { get; set; } = "";
    public List<string> Packs { get; set; } = new();
    public Vec2 Center { get; set; }
    public List<ClippedPiece> Pieces { get; set; } = new();
}
