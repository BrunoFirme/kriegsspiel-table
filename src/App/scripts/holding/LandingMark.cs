using System.Linq;
using Godot;

namespace KTable.App;

public partial class LandingMark : Decal
{
    const float Reach = 8, Gap = 0.02f;
    readonly PieceNode _piece;
    object? _stamp;
    Rect2 _area;

    LandingMark(PieceNode piece)
    {
        _piece = piece;
        TopLevel = true;
        UpperFade = 0;
        LowerFade = 0;
        NormalFade = 0;
        Layers = Table.LiveOnlyLayer;
    }

    internal Rect2 Area => _area;

    public static void Under(PieceNode piece)
    {
        if (piece.IsDie || piece.GetChildren().OfType<LandingMark>().Any()) return;
        var mark = new LandingMark(piece);
        piece.AddChild(mark);
        mark.Aim();
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_piece) || !_piece.Held) { QueueFree(); return; }
        Aim();
    }

    void Take()
    {
        var stamp = TopShadow.Stamp(_piece);
        if (Equals(stamp, _stamp)) return;
        _stamp = stamp;
        (var texture, _area) = TopShadow.Of(_piece);
        TextureAlbedo = texture;
    }

    void Aim()
    {
        if (!IsInsideTree()) return;
        Take();
        float k = _piece.Scale.X;
        var turn = new Basis(Vector3.Up, _piece.Heading);
        var middle = _area.GetCenter();
        var under = _piece.GlobalPosition - Vector3.Up * _piece.LiftY + turn * new Vector3(middle.X * k, 0, middle.Y * k);
        Size = new Vector3(_area.Size.X * k, Reach, _area.Size.Y * k);
        GlobalTransform = new Transform3D(turn, under - Vector3.Up * (Reach / 2 + Gap));
    }
}
