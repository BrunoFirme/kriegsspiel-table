using System.Linq;
using Godot;

namespace KTable.App;

public partial class LandingMark : Decal
{
    const float Reach = 8, Gap = 0.02f, LineWidth = 0.03f, LineShade = 0.15f, LineAlpha = 0.6f;
    readonly PieceNode _piece;
    object? _stamp;
    Rect2 _area;
    MeshInstance3D? _line;

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
    internal bool DrawsLine => _line is { Visible: true };

    internal static bool Drawn(VisualInstance3D vi) => vi is LandingMark || vi.GetParent() is LandingMark;

    public static void Under(PieceNode piece)
    {
        if (piece.IsDie || piece.GetChildren().OfType<LandingMark>().Any()) return;
        var mark = new LandingMark(piece);
        piece.AddChild(mark);
        mark.Aim();
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_piece) || !(_piece.Held || _piece.Piece.Flies)) { QueueFree(); return; }
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
        float k = _piece.Scale.X, aloft = (float)_piece.Piece.Aloft, reach = Reach + aloft;
        var turn = new Basis(Vector3.Up, _piece.Heading);
        var middle = _area.GetCenter();
        var under = _piece.GlobalPosition - Vector3.Up * _piece.LiftY + turn * new Vector3(middle.X * k, 0, middle.Y * k);
        Size = new Vector3(_area.Size.X * k, reach, _area.Size.Y * k);
        GlobalTransform = new Transform3D(turn, under - Vector3.Up * (reach / 2 + Gap));
        AimLine(under, aloft);
    }

    void AimLine(Vector3 under, float aloft)
    {
        if (aloft <= 0 || _piece.Held || !_piece.Ringed)
        {
            if (_line != null) _line.Visible = false;
            return;
        }
        _line ??= MakeLine();
        _line.Visible = true;
        ((BoxMesh)_line.Mesh).Size = new Vector3(LineWidth, aloft, LineWidth);
        _line.GlobalTransform = new Transform3D(Basis.Identity, under - Vector3.Up * (aloft / 2));
    }

    MeshInstance3D MakeLine()
    {
        var line = new MeshInstance3D
        {
            Mesh = new BoxMesh(), TopLevel = true, Layers = Table.LiveOnlyLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(LineShade, LineShade, LineShade, LineAlpha), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        AddChild(line);
        return line;
    }
}
