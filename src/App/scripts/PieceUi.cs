using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceUi
{
    readonly PieceNode _piece;
    readonly List<Node> _nodes = new();
    List<UiNode>? _built;
    bool _builtFaceDown;
    const int PlatePixels = 1024;

    public PieceUi(PieceNode piece) => _piece = piece;

    public float ModelTop;

    public void Sync(bool force)
    {
        var piece = _piece.Piece;
        if (!force && ReferenceEquals(_built, piece.Ui) && _builtFaceDown == piece.FaceDown && _nodes.All(n => GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion())) return;
        if (!_piece.IsInsideTree())
        {
            Callable.From(() => { if (GodotObject.IsInstanceValid(_piece) && _piece.IsInsideTree()) _piece.SyncUi(true); }).CallDeferred();
            return;
        }
        _built = piece.Ui; _builtFaceDown = piece.FaceDown;
        Clear();
        if (piece.Ui.Count == 0 || piece.FaceDown || _piece.IsDie || _piece.IsEffect) return;
        foreach (var root in piece.Ui)
            if (!UiBuilder.Attr(root, "active", "true").Equals("false", StringComparison.OrdinalIgnoreCase)) DrawPlate(root);
    }

    void Clear()
    {
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _nodes.Clear();
    }

    public void Forget()
    {
        _built = null;
        _nodes.Clear();
    }

    void DrawPlate(UiNode root)
    {
        float w = Mathf.Max(1, UiBuilder.Num(root, "width", 100)), h = Mathf.Max(1, UiBuilder.Num(root, "height", 100));
        var scale = UiBuilder.Vec3(root, "scale", 1);
        var pos = UiBuilder.Vec3(root, "position", 0);
        var rot = UiBuilder.Vec3(root, "rotation", 0);
        const float thousandth = 0.001f;
        float pw = w * thousandth * scale.X, ph = h * thousandth * scale.Y;
        if (pw < 0.001f || ph < 0.001f) return;

        float f = Mathf.Min(1, PlatePixels / Mathf.Max(w, h));
        var vp = new SubViewport
        {
            TransparentBg = true, Disable3D = true, Size = new Vector2I(Mathf.Max(1, Mathf.RoundToInt(w * f)), Mathf.Max(1, Mathf.RoundToInt(h * f))),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        var canvas = new Control { Size = new Vector2(w, h), Scale = new Vector2(f, f) };
        vp.AddChild(canvas);
        var s = _piece.Session;
        var builder = new UiBuilder((_, _, _) => { }, (id, ready) => UiBuilder.AssetTexture(s, id, t => { ready(t); if (GodotObject.IsInstanceValid(vp)) vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; }));
        var c = builder.Build(root, new Vector2(w, h));
        if (c == null) { vp.QueueFree(); return; }
        c.Position = Vector2.Zero; c.Size = new Vector2(w, h);
        canvas.AddChild(c);
        _piece.AddChild(vp); _nodes.Add(vp);

        var mat = new StandardMaterial3D
        {
            AlbedoTexture = vp.GetTexture(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Back, RenderPriority = 2, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        var quad = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(pw, ph) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        float faceY = (_piece.Def?.Kind == "model" && ModelTop > 0 ? ModelTop : _piece.Thickness) + 0.010f;
        quad.Position = new Vector3(pos.X * thousandth, faceY, -pos.Y * thousandth);
        quad.RotationDegrees = new Vector3(0, -(rot.Z - 180), 0);
        _piece.AddChild(quad); _nodes.Add(quad);
    }
}
