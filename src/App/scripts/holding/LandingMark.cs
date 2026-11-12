using System.Linq;
using Godot;

namespace KTable.App;

public partial class LandingMark : Decal
{
    const float Reach = 8, Gap = 0.02f, Dark = 0.55f, Feather = 0.2f;
    const int Pixels = 64;
    static ImageTexture? _square, _round;
    readonly PieceNode _piece;

    LandingMark(PieceNode piece)
    {
        _piece = piece;
        TopLevel = true;
        TextureAlbedo = piece.Shape is "circle" or "hex" ? _round ??= Mark(true) : _square ??= Mark(false);
        UpperFade = 0;
        LowerFade = 0;
        NormalFade = 0;
        Layers = Table.LiveOnlyLayer;
    }

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

    void Aim()
    {
        if (!IsInsideTree()) return;
        float k = _piece.Scale.X;
        var turn = new Basis(Vector3.Up, _piece.Heading);
        var under = _piece.GlobalPosition - Vector3.Up * _piece.LiftY + turn * new Vector3(_piece.FootprintCentre.X * k, 0, _piece.FootprintCentre.Y * k);
        Size = new Vector3(_piece.Footprint.X * k, Reach, _piece.Footprint.Y * k);
        GlobalTransform = new Transform3D(turn, under - Vector3.Up * (Reach / 2 + Gap));
    }

    static ImageTexture Mark(bool round)
    {
        var img = Image.CreateEmpty(Pixels, Pixels, true, Image.Format.Rgba8);
        for (int y = 0; y < Pixels; y++)
            for (int x = 0; x < Pixels; x++)
            {
                float u = (x + 0.5f) / Pixels * 2 - 1, v = (y + 0.5f) / Pixels * 2 - 1;
                float inside = round ? 1 - Mathf.Sqrt(u * u + v * v) : Mathf.Min(1 - Mathf.Abs(u), 1 - Mathf.Abs(v));
                img.SetPixel(x, y, new Color(0, 0, 0, Dark * Mathf.SmoothStep(0, Feather, inside)));
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
