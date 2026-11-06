using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceLines
{
    readonly PieceNode _piece;
    MeshInstance3D? _node;
    List<LineDef>? _built;
    int _builtState = -1;
    const float LinesY = 0.012f;

    public PieceLines(PieceNode piece) => _piece = piece;

    public void Forget() { _node = null; _built = null; }

    public void Sync()
    {
        var piece = _piece.Piece;
        if (ReferenceEquals(_built, piece.Lines) && _builtState == piece.State) return;
        _built = piece.Lines;
        _builtState = piece.State;
        if (_node != null && GodotObject.IsInstanceValid(_node)) _node.QueueFree();
        _node = null;
        if (piece.Lines.Count == 0 || !_piece.ScriptDrawingShows) return;
        var mesh = new ImmediateMesh();
        bool any = false;
        foreach (var ln in piece.Lines)
        {
            var color = Coords.ParseColor(ln.Color, Colors.White);
            if (color.A <= 0.001f || ln.Points.Count < 2) continue;
            var pts = new List<Vector3>();
            foreach (var p in ln.Points) pts.Add(new Vector3((float)p.X, LinesY, (float)p.Z));
            if (ln.Loop && (pts[0] - pts[^1]).Length() > 0.0001f) pts.Add(pts[0]);
            float hw = (float)Mathf.Max(0.01, ln.Width) / 2;
            mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
            mesh.SurfaceSetColor(color);
            for (int i = 0; i < pts.Count; i++)
            {
                var dIn = i > 0 ? (pts[i] - pts[i - 1]).Normalized() : Vector3.Zero;
                var dOut = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).Normalized() : Vector3.Zero;
                var d = (dIn + dOut).LengthSquared() > 0.000001f ? (dIn + dOut).Normalized() : (dIn.LengthSquared() > 0 ? dIn : dOut);
                var side = new Vector3(-d.Z, 0, d.X) * hw;
                mesh.SurfaceSetNormal(Vector3.Up);
                mesh.SurfaceAddVertex(pts[i] - side);
                mesh.SurfaceSetNormal(Vector3.Up);
                mesh.SurfaceAddVertex(pts[i] + side);
            }
            mesh.SurfaceEnd();
            any = true;
        }
        if (!any) return;
        _node = new MeshInstance3D
        {
            Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride =
                new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 1 },
        };
        _piece.AddChild(_node);
    }
}
