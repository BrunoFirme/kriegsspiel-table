using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceLabels
{
    const int LetterPx = 64, EdgePx = 12;
    const float HalfTurn = 180, LeastPixel = 0.00002f;
    readonly PieceNode _piece;
    readonly List<Label3D> _labels = new();
    string? _key;

    public PieceLabels(PieceNode piece) => _piece = piece;

    public void Sync()
    {
        var key = Key();
        if (key == _key) return;
        Clear();
        _key = key;
        if (key.Length > 0) Build();
    }

    public void Forget()
    {
        _labels.Clear();
        _key = null;
    }

    public void ForPicture(bool on)
    {
        foreach (var l in _labels)
            if (GodotObject.IsInstanceValid(l)) l.VisibilityRangeEnd = on ? 0 : _piece.LetteringRange();
    }

    bool Shows => _piece.Def is { } d && d.TextLines.Any(t => t.Float != null) && !_piece.IsDie && !_piece.IsEffect && _piece.Lettered && _piece.Look.ShowFaceText && !_piece.Piece.FaceDown;

    string Key() => !Shows ? ""
        : $"{_piece.Piece.Scale}|{_piece.Piece.TallBy}\u001e" + string.Join("\u001f", _piece.Def!.TextLines.Where(t => t.Float != null).Select(t => $"{Where(t.Float!)}|{t.Color}|{PieceNode.TextOf(_piece.Piece, t)}"));

    static string Where(FloatPlace f) => $"{f.X:0.####},{f.Y:0.####},{f.Z:0.####},{f.Size:0.####},{f.FacesFront}";

    void Clear()
    {
        foreach (var l in _labels) if (GodotObject.IsInstanceValid(l)) l.QueueFree();
        _labels.Clear();
    }

    void Build()
    {
        float scale = (float)_piece.Piece.Scale;
        foreach (var t in _piece.Def!.TextLines)
        {
            if (t.Float is not { } f || PieceNode.TextOf(_piece.Piece, t) is not { Length: > 0 } text) continue;
            var l = Make(text, f, Hex.To(t.Color, Colors.White), scale, (float)_piece.Piece.TallBy);
            l.VisibilityRangeEnd = _piece.LetteringRange();
            _piece.AddChild(l);
            _labels.Add(l);
        }
    }

    public static Label3D Make(string text, FloatPlace f, Color ink, float scale, float tall = 1)
    {
        var l = new Label3D
        {
            Text = text, FontSize = LetterPx, OutlineSize = EdgePx, Modulate = ink, OutlineModulate = FaceInk.Edge(ink), Font = Fonts.Face, DoubleSided = true,
            Billboard = f.FacesFront ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Layers = Table.TextLayer,
            Position = new Vector3((float)f.X, (float)f.Y * tall, (float)f.Z) * scale,
        };
        if (f.FacesFront) l.RotationDegrees = new Vector3(0, HalfTurn, 0);
        var font = l.Font ?? ThemeDB.FallbackFont;
        l.PixelSize = Mathf.Max(LeastPixel, (float)f.Size * scale / Mathf.Max(1, font.GetHeight(LetterPx)));
        return l;
    }
}
