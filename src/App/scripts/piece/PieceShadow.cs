using System.Linq;
using Godot;

namespace KTable.App;

static class PieceShadow
{
    public static bool None(PieceNode n) => n.Def?.Shadowless == true || n.Piece.NoShadow;

    public static void Cast(PieceNode n)
    {
        if (!n.Piece.NoShadow) return;
        foreach (var shape in n.FindChildren("*", nameof(GeometryInstance3D), recursive: true, owned: false).OfType<GeometryInstance3D>())
            shape.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }
}
