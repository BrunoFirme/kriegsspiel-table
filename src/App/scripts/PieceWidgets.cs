using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceWidgets
{
    readonly PieceNode _piece;
    readonly List<Node3D> _nodes = new();
    readonly record struct Hit(Vector2 Centre, Vector2 Half, float Turn, int Index)
    {
        public bool Holds(Vector2 p)
        {
            var d = p - Centre;
            float c = Mathf.Cos(Turn), s = Mathf.Sin(Turn);
            return Mathf.Abs(d.X * c + d.Y * s) <= Half.X && Mathf.Abs(-d.X * s + d.Y * c) <= Half.Y;
        }
    }

    readonly List<Hit> _hits = new();
    List<Widget>? _built;
    int _builtState = -1;
    bool _builtFaceDown;
    readonly Dictionary<int, (MeshInstance3D? box, Color color)> _boxes = new();
    int _hover;
    static readonly Color WidgetBox = new(1, 1, 1), WidgetBoxText = new(0, 0, 0), WidgetLabelText = new(1, 1, 1), InputBox = new(0.95f, 0.95f, 0.95f);
    static readonly Dictionary<string, StandardMaterial3D> WidgetMats = new();

    int _editWidget;
    string _editText = "";

    public PieceWidgets(PieceNode piece) => _piece = piece;

    public static readonly PlaneMesh UnitPlane = new() { Size = Vector2.One };

    static StandardMaterial3D WidgetMat(Color c)
    {
        var key = c.ToHtml();
        if (!WidgetMats.TryGetValue(key, out var m))
            WidgetMats[key] = m = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 2 };
        return m;
    }

    public int Drawn => _nodes.Count;

    public void Sync(bool force)
    {
        var piece = _piece.Piece;
        if (!_piece.Lettered) { if (_nodes.Count > 0) Clear(); _built = null; _builtState = -1; return; }
        if (!force && ReferenceEquals(_built, piece.Widgets) && _builtFaceDown == piece.FaceDown && _builtState == piece.State) return;
        _built = piece.Widgets; _builtFaceDown = piece.FaceDown; _builtState = piece.State;
        Clear();
        if (_piece.Def?.Kind == "model" || _piece.IsDie || _piece.IsEffect) return;
        if (!_piece.ScriptDrawingShows) return;
        bool back = piece.FaceDown;
        var face = back ? "back" : "front";
        float faceY = _piece.Thickness + 0.006f;
        for (int i = 0; i < piece.Widgets.Count; i++)
        {
            var w = piece.Widgets[i];
            if (w.Face != face) continue;
            Draw(w, i + 1, back, faceY);
        }
    }

    void Clear()
    {
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _nodes.Clear();
        _hits.Clear();
        _boxes.Clear();
        _hover = 0;
    }

    public void Forget()
    {
        _nodes.Clear();
        _hits.Clear();
        _built = null;
    }

    public void ForPicture(bool on)
    {
        foreach (var n in _nodes)
            if (n is GeometryInstance3D gi && GodotObject.IsInstanceValid(gi)) gi.VisibilityRangeEnd = on ? 0 : _piece.LetteringRange();
    }

    public void SetHover(int index)
    {
        if (index == _hover) return;
        Shade(_hover, 0);
        _hover = index;
        Shade(index, 0.18f);
    }

    public void Flash(int index)
    {
        Shade(index, -0.25f);
        _piece.GetTree().CreateTimer(0.12).Timeout += () => { if (GodotObject.IsInstanceValid(_piece)) Shade(index, index == _hover ? 0.18f : 0); };
    }

    void Shade(int index, float amount)
    {
        if (index <= 0 || !_boxes.TryGetValue(index, out var b) || b.box == null || !GodotObject.IsInstanceValid(b.box)) return;
        b.box.MaterialOverride = WidgetMat(amount > 0 ? b.color.Lightened(amount) : amount < 0 ? b.color.Darkened(-amount) : b.color);
    }

    void Draw(Widget w, int index, bool back, float faceY)
    {
        float k = (float)Math.Max(1e-6, _piece.Piece.Scale);
        float x = (float)w.X * k, z = (float)w.Z * k, bw = (float)w.W * k, bh = (float)w.H * k;
        bool input = w.Kind == "input", label = w.Locked;
        bool boxed = bw > 0.0001f && bh > 0.0001f;
        var boxColor = w.Color != null ? Coords.ParseColor(w.Color, WidgetBox) : input ? InputBox : WidgetBox;
        var textColor = w.TextColor != null ? Coords.ParseColor(w.TextColor, WidgetBoxText) : boxed ? WidgetBoxText : WidgetLabelText;
        float turn = Mathf.DegToRad((float)w.Rot);
        MeshInstance3D? box = boxed ? Plane(x, z, bw, bh, turn, WidgetMat(boxColor), back, faceY, 0.002f) : null;
        _boxes[index] = (box, boxColor);
        bool typing = index == _editWidget;
        var text = typing ? _editText : input ? (w.Value.Length > 0 ? w.Value : w.Placeholder) : label && w.Value.Length > 0 ? w.Value : w.Label;
        var color = !typing && input && w.Value.Length == 0 ? textColor with { A = 0.55f } : textColor;
        float grow = Mathf.Max(0.01f, _piece.Look.FaceTextScale);
        float em = Mathf.Max(0.002f, (float)w.Font * k * grow);
        if ((input || label) && boxed && !typing) text = Fitting(text, em, bw * 0.94f);
        Text(text, x, z, boxed ? bw * 0.94f : 0, em, color, w.Align, (float)w.Rot, back, faceY, 0.004f);
        if (boxed && !w.Locked) _hits.Add(new Hit(new Vector2(x, z), new Vector2(bw, bh) / 2, turn, index));
    }

    static string Fitting(string text, float em, float width)
    {
        float pixel = em / Fonts.FaceSize;
        float Wide(string s) => Fonts.Tts.GetStringSize(s, HorizontalAlignment.Left, -1, Fonts.FaceSize).X * pixel;
        int n = text.Length;
        while (n > 0 && Wide(text[..n]) > width) n--;
        return text[..n];
    }

    public static Transform3D OnFace(float x, float z, float sw, float sh, float turn, bool back, float faceY, float above)
    {
        var spin = back ? new Basis(Vector3.Right, Mathf.Pi) * new Basis(Vector3.Up, turn) : new Basis(Vector3.Up, -turn);
        return new Transform3D(spin.Scaled(new Vector3(Mathf.Max(1e-5f, sw), 1, Mathf.Max(1e-5f, sh))), back ? PieceBack.Under(x, z, PieceBack.Below + above) : new Vector3(x, faceY + above, z));
    }

    MeshInstance3D Plane(float x, float z, float sw, float sh, float turn, Material m, bool back, float faceY, float above)
    {
        var n = new MeshInstance3D { Mesh = UnitPlane, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = _piece.LetteringRange() };
        n.Transform = OnFace(x, z, sw, sh, turn, back, faceY, above);
        AddNode(n);
        return n;
    }

    void Text(string text, float x, float z, float boxW, float em, Color color, string align, float rot, bool back, float faceY, float above)
    {
        var l = new Label3D
        {
            Text = text, FontSize = Fonts.FaceSize, OutlineSize = 0, Modulate = color, DoubleSided = false, VisibilityRangeEnd = _piece.LetteringRange(),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderPriority = 3, Font = Fonts.Tts,
        };
        var font = l.Font ?? ThemeDB.FallbackFont;
        var px = font.GetStringSize(text.Length > 0 ? text : " ", HorizontalAlignment.Left, -1, Fonts.FaceSize);
        float pixel = em / Fonts.FaceSize;
        l.PixelSize = Mathf.Max(0.00002f, pixel);
        float wUnits = px.X * pixel;
        float along = boxW > 0 && align == "left" ? -boxW / 2 + wUnits / 2 : boxW > 0 && align == "right" ? boxW / 2 - wUnits / 2 : 0;
        float turn = Mathf.DegToRad(rot);
        float cx = x + along * Mathf.Cos(turn);
        z += along * Mathf.Sin(turn);
        if (back) { l.RotationDegrees = new Vector3(90, 180, rot); l.Position = PieceBack.Under(cx, z, PieceBack.Below + above); }
        else { l.RotationDegrees = new Vector3(-90, -rot, 0); l.Position = new Vector3(cx, faceY + above, z); }
        AddNode(l);
    }

    void AddNode(Node3D n) { _piece.AddChild(n); _nodes.Add(n); }

    public void SetEditText(int index, string text) { _editWidget = index; _editText = text; }

    public bool Editing => _editWidget != 0;

    public void ClearEditText() { _editWidget = 0; _editText = ""; }

    public (int index, Widget widget)? At(Vector2 local)
    {
        if (_hits.Count == 0) return null;
        var widgets = _piece.Piece.Widgets;
        foreach (var hit in _hits)
            if (hit.Holds(local) && hit.Index >= 1 && hit.Index <= widgets.Count) return (hit.Index, widgets[hit.Index - 1]);
        return null;
    }
}
