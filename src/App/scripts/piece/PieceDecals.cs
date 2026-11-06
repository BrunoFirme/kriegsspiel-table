using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PieceDecals
{
    const float FaceGap = 0.006f, Above = 0.001f;
    const int DecalPriority = 1, LargestPicture = 1024;

    readonly PieceNode _piece;
    readonly List<Node3D> _nodes = new();
    List<DecalDef>? _built;
    bool _builtFaceDown;

    public PieceDecals(PieceNode piece) => _piece = piece;

    public void Forget() { _nodes.Clear(); _built = null; }

    public void Sync(bool force)
    {
        var piece = _piece.Piece;
        var shown = DecalDef.ShownOn(piece, _piece.Def);
        if (!force && ReferenceEquals(_built, shown) && _builtFaceDown == piece.FaceDown) return;
        _built = shown;
        _builtFaceDown = piece.FaceDown;
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _nodes.Clear();
        if (shown.Count == 0 || _piece.Def?.Kind == "model" || _piece.IsDie || _piece.IsEffect) return;
        bool back = piece.FaceDown;
        var face = back ? "back" : "front";
        float k = (float)System.Math.Max(1e-6, piece.Scale);
        float faceY = _piece.Thickness + FaceGap;
        foreach (var d in shown.Where(d => d.Face == face))
        {
            if (FileOf(d.Picture) is not var (pack, file)) continue;
            var mat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = DecalPriority,
                AlbedoColor = Colors.White,
            };
            var tex = _piece.Session.Assets.Texture(pack, file, t => { if (GodotObject.IsInstanceValid(_piece)) mat.AlbedoTexture = t; }, maxSize: LargestPicture);
            if (tex != null) mat.AlbedoTexture = tex;
            var n = new MeshInstance3D
            {
                Mesh = PieceWidgets.UnitPlane, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = PieceWidgets.OnFace((float)d.X * k, (float)d.Z * k, (float)d.W * k, (float)d.H * k, Mathf.DegToRad((float)d.Rot), back, faceY, Above),
            };
            _piece.AddChild(n);
            _nodes.Add(n);
        }
    }

    (string pack, string file)? FileOf(string picture) =>
        KTable.Client.PieceFiles.DecalFile(_piece.Session.View, _piece.Session.Store, picture, _piece.Piece.Def) is { } f ? (f.Package, f.Path) : null;
}
