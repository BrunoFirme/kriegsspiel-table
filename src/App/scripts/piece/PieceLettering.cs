using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceLettering
{
    readonly PieceNode _piece;
    Label3D? _label;
    MeshInstance3D? _boxes;
    List<string> _lines = new();

    public PieceLettering(PieceNode piece) => _piece = piece;

    public bool Built => _label != null;

    public void Add()
    {
        _label = new Label3D
        {
            FontSize = Fonts.FaceSize, OutlineSize = Fonts.FaceSize * 6 / 64, Modulate = new Color(0.97f, 0.96f, 0.92f), OutlineModulate = new Color(0, 0, 0, 0.8f),
            RotationDegrees = new Vector3(-90, 0, 0), DoubleSided = true, VisibilityRangeEnd = _piece.LetteringRange(), Font = Fonts.Face,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Layers = Table.TextLayer,
        };
        _piece.AddChild(_label);
    }

    public void Drop()
    {
        if (_label != null) { _label.QueueFree(); _label = null; }
        if (_boxes != null) { _boxes.QueueFree(); _boxes = null; }
    }

    public void Forget() { _label = null; _boxes = null; }

    public void ForPicture(bool on)
    {
        if (_label != null && GodotObject.IsInstanceValid(_label)) _label.VisibilityRangeEnd = on ? 0 : _piece.LetteringRange();
    }

    public void Relayout() => Layout(_lines);

    public void Layout(List<string> lines)
    {
        _lines = lines;
        if (!_piece.Lettered || _label == null) return;
        float w = _piece.Footprint.X, h = _piece.Footprint.Y, faceY = _piece.Thickness + 0.006f;
        var nb = _piece.Def?.NameBox ?? _piece.DetectedNameBox() ?? PieceNode.DefaultNameBox(_piece.Boxy);
        Fit(_label!, lines, nb, w, h, faceY, _piece.Look.FaceTextScale);
        DrawBoxes(nb, w, h, faceY);
    }

    static void Fit(Label3D l, List<string> lines, FaceBox b, float w, float h, float faceY, float scale)
    {
        l.Text = string.Join('\n', lines);
        l.Visible = lines.Count > 0;
        if (lines.Count == 0) return;
        float bw = Mathf.Max(0.01f, (float)b.W * w), bh = Mathf.Max(0.01f, (float)b.H * h);
        var font = l.Font ?? ThemeDB.FallbackFont;
        var px = font.GetMultilineStringSize(l.Text, HorizontalAlignment.Center, -1, l.FontSize);
        float lineHeightPx = Mathf.Max(1, font.GetHeight(l.FontSize));
        float capLineH = Mathf.Clamp(Mathf.Min(w, h) * 0.16f, 0.05f, 2.5f);
        float pixel = Mathf.Min(Mathf.Min(bh * 0.9f * scale / Mathf.Max(1, px.Y), bw * 0.94f / Mathf.Max(1, px.X)), capLineH * scale / lineHeightPx);
        l.PixelSize = Mathf.Max(0.00002f, pixel);
        l.Position = new Vector3(-w / 2 + (float)(b.X + b.W / 2) * w, faceY + 0.002f, -h / 2 + (float)(b.Y + b.H / 2) * h);
    }

    void DrawBoxes(FaceBox nb, float w, float h, float faceY)
    {
        if (!_piece.Look.ShowFaceBoxes || _piece.Def?.Kind == "model" || _piece.Piece.FaceDown) { if (_boxes != null) _boxes.Visible = false; return; }
        var mesh = new ImmediateMesh();
        void Rect(FaceBox b, Color c)
        {
            var mat = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
            float x0 = -w / 2 + (float)b.X * w, x1 = x0 + (float)b.W * w, z0 = -h / 2 + (float)b.Y * h, z1 = z0 + (float)b.H * h;
            mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip, mat);
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1), (x0, z0) }) mesh.SurfaceAddVertex(new Vector3(x, faceY + 0.004f, z));
            mesh.SurfaceEnd();
        }
        Rect(nb, new Color(1, 0.85f, 0.3f));
        if (_boxes == null) { _boxes = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; _piece.AddChild(_boxes); }
        _boxes.Mesh = mesh;
        _boxes.Visible = true;
    }
}
