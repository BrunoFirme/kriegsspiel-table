using Godot;

namespace KTable.App;

public partial class PieceNode
{
    MeshInstance3D? _lit;
    string? _litColor;

    public void SyncHighlight()
    {
        if (Piece.Highlight == _litColor && (_lit == null || GodotObject.IsInstanceValid(_lit))) return;
        if (_lit != null && GodotObject.IsInstanceValid(_lit)) _lit.QueueFree();
        _lit = null;
        _litColor = Piece.Highlight;
        if (_litColor == null || IsEffect) return;
        var (mesh, mat, halo) = Ring(Coords.ParseColor(_litColor, Colors.White));
        _lit = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, halo ? -0.016f : 0.001f, 0), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_lit);
    }

    public bool Lit => _lit != null;
}
