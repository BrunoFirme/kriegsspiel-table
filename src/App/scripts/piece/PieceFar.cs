using Godot;

namespace KTable.App;

sealed class FarSides
{
    readonly MeshInstance3D _face, _under;

    public FarSides(Node3D piece, float from, Mesh face, Material print, float top, Mesh under, Material back, Vector3 underTurn)
    {
        _face = Quad(piece, from, face, print, new Vector3(0, top, 0), Vector3.Zero);
        _under = Quad(piece, from, under, back, Vector3.Zero, underTurn);
    }

    static MeshInstance3D Quad(Node3D piece, float from, Mesh plane, Material print, Vector3 at, Vector3 turn)
    {
        var quad = new MeshInstance3D
        {
            Mesh = plane, Position = at, RotationDegrees = turn,
            MaterialOverride = print, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = from,
        };
        piece.AddChild(quad);
        return quad;
    }

    public void Show(bool faceDown, bool fromUnderToo)
    {
        _face.Visible = fromUnderToo || !faceDown;
        _under.Visible = fromUnderToo || faceDown;
    }
}
