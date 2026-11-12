using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class NoteFace
{
    readonly PieceNode _piece;
    readonly List<Node3D> _nodes = new();
    string? _built;
    bool? _editBody;
    string _editText = "";
    bool _editSelected;

    static readonly Color TitleInk = new(0.10f, 0.10f, 0.11f), BodyInk = new(0.17f, 0.16f, 0.17f), SelectedInk = new(0.50f, 0.30f, 0.04f);
    static readonly Dictionary<(bool Bold, bool Italic), Font> Faces = new();
    static float? _baselineBelowMiddle;
    const float TitleFill = 0.7f, WritingFill = 0.72f, AboveRule = 0.06f, FaceGap = 0.006f, InkLift = 0.002f;
    const float Embolden = 0.8f, Slant = 0.2f, Raised = 0.35f, Lowered = 0.15f, UnderlineBelow = 0.1f, StrikeAbove = 0.27f, StrokeThick = 0.06f;

    public NoteFace(PieceNode piece) => _piece = piece;

    bool Shows => NoteCard.Is(_piece.Def) && !_piece.Piece.FaceDown && !_piece.PrintingOnly;

    public string Line(bool body)
    {
        var name = body ? NoteCard.BodyLine : NoteCard.TitleLine;
        var def = _piece.Def?.TextLines.FirstOrDefault(t => t.Name == name);
        return _piece.Piece.Text.TryGetValue(name, out var v) ? v : def?.Default ?? "";
    }

    public static string Told(string written) => Markup.Plain(written).Replace("\r", "").Trim();

    public void SetEditing(bool body, string text, bool selected)
    {
        _editBody = body;
        _editText = text;
        _editSelected = selected;
        Sync(force: true);
    }

    public void StopEditing()
    {
        if (_editBody == null) return;
        _editBody = null;
        _editText = "";
        _editSelected = false;
        Sync(force: true);
    }

    public void Sync(bool force)
    {
        if (!Shows)
        {
            if (_nodes.Count > 0) Clear();
            _built = null;
            return;
        }
        var title = _editBody == false ? _editText : Line(body: false);
        var body = _editBody == true ? _editText : Line(body: true);
        var key = $"{title}\u001f{body}\u001f{_piece.Footprint}\u001f{_piece.Thickness}\u001f{_editSelected}\u001f{_editBody}\u001f{_piece.Piece.LinesToARule}";
        if (!force && key == _built) return;
        _built = key;
        Clear();
        float w = _piece.Footprint.X, h = _piece.Footprint.Y, faceY = _piece.Thickness + FaceGap;
        float Z(double frac) => -h / 2 + (float)frac * h;
        float left = -w / 2 + (float)NoteCard.SideMargin * w, width = w * (1 - 2 * (float)NoteCard.SideMargin);
        float band = (float)(NoteCard.TitleBottom - NoteCard.TitleTop) * h;
        WriteTitle(title.Replace('\n', ' '), Z((NoteCard.TitleTop + NoteCard.TitleBottom) / 2), width, band * TitleFill, faceY);
        float step = (float)NoteCard.RuleStep * h;
        var (lines, toARule) = Fitted(_editBody == true ? NoteWriting.Raw(body) : Markup.Runs(body), width, step, _piece.Piece.LinesToARule);
        float em = step / toARule * WritingFill;
        for (int i = 0; i < lines.Count && i < NoteCard.Lines * toARule; i++)
        {
            float baseline = Z(NoteCard.RuleAt(i / toARule)) - step * (toARule - 1 - i % toARule) / toARule - step * AboveRule;
            foreach (var stroke in lines[i]) Write(stroke, left, baseline, em, Ink(BodyInk, body: true), faceY);
        }
    }

    Color Ink(Color plain, bool body) => _editSelected && _editBody == body ? SelectedInk : plain;

    static Font Face(MarkupRun style)
    {
        if (!style.Bold && !style.Italic) return Fonts.Hand;
        if (Faces.TryGetValue((style.Bold, style.Italic), out var made)) return made;
        return Faces[(style.Bold, style.Italic)] = new FontVariation
        {
            BaseFont = Fonts.Hand, VariationEmbolden = style.Bold ? Embolden : 0, VariationTransform = new Transform2D(1, style.Italic ? Slant : 0, 0, 1, 0, 0),
        };
    }

    static float Pixel(float em) => em / Fonts.FaceSize;

    static float Small(MarkupRun style) => style.Level == MarkupLevel.Line ? 1 : (float)Markup.SmallScale;

    static float Wide(string text, MarkupRun style, float em) =>
        Face(style).GetStringSize(text, HorizontalAlignment.Left, -1, Fonts.FaceSize).X * Pixel(em) * Small(style);

    static float BaselineBelowMiddle()
    {
        if (_baselineBelowMiddle is { } known) return known;
        var (ascent, descent) = Fonts.OwnLine(Fonts.Hand, Fonts.FaceSize);
        return (_baselineBelowMiddle = (ascent - descent) / 2).Value;
    }

    public static (List<List<NoteWriting.Stroke>> Lines, int ToARule) Fitted(List<MarkupRun> runs, float width, float step, int chosen)
    {
        for (int toARule = chosen > 0 ? Math.Min(chosen, NoteCard.MostLinesToARule) : 1; ; toARule++)
        {
            float em = step / toARule * WritingFill;
            var lines = NoteWriting.Lines(runs, width, (text, style) => Wide(text, style, em));
            if (chosen > 0 || lines.Count <= NoteCard.Lines * toARule || toARule == NoteCard.MostLinesToARule) return (lines, toARule);
        }
    }

    void WriteTitle(string title, float z, float width, float em, float faceY)
    {
        var runs = _editBody == false ? NoteWriting.Raw(title) : Markup.Runs(title);
        var line = NoteWriting.Lines(runs, float.MaxValue, (text, style) => Wide(text, style, em))[0];
        if (line.Count == 0) return;
        float wide = line[^1].Left + line[^1].Wide, fit = wide > width ? width / wide : 1;
        float baseline = z + BaselineBelowMiddle() * Pixel(em * fit);
        foreach (var stroke in line) Write(stroke with { Left = stroke.Left * fit, Wide = stroke.Wide * fit }, -wide * fit / 2, baseline, em * fit, Ink(TitleInk, body: false), faceY);
    }

    void Write(NoteWriting.Stroke stroke, float left, float baseline, float em, Color plain, float faceY)
    {
        float size = em * Small(stroke.Style), pixel = Pixel(size), at = left + stroke.Left;
        float z = baseline + em * (stroke.Style.Level switch { MarkupLevel.Raised => -Raised, MarkupLevel.Lowered => Lowered, _ => 0 });
        var ink = stroke.Style.Color is { } colour ? Coords.ParseColor(colour, plain) : plain;
        if (stroke.Text.Trim().Length > 0)
            Add(new Label3D
            {
                Text = stroke.Text, FontSize = Fonts.FaceSize, OutlineSize = 0, Modulate = ink, DoubleSided = false, Font = Face(stroke.Style),
                PixelSize = Mathf.Max(0.00002f, pixel), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderPriority = 3,
                RotationDegrees = new Vector3(-90, 0, 0), VisibilityRangeEnd = _piece.LetteringRange(), Layers = Table.TextLayer,
                Position = new Vector3(at + stroke.Wide / 2, faceY + InkLift, z - BaselineBelowMiddle() * pixel),
            });
        if (stroke.Style.Underline) Draw(at, stroke.Wide, z + size * UnderlineBelow, size * StrokeThick, ink, faceY);
        if (stroke.Style.Strike) Draw(at, stroke.Wide, z - size * StrikeAbove, size * StrokeThick, ink, faceY);
    }

    void Draw(float left, float wide, float z, float thick, Color ink, float faceY) => Add(new MeshInstance3D
    {
        Mesh = new QuadMesh { Size = new Vector2(wide, thick) }, RotationDegrees = new Vector3(-90, 0, 0), Position = new Vector3(left + wide / 2, faceY + InkLift, z),
        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = ink },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = _piece.LetteringRange(), Layers = Table.TextLayer,
    });

    void Add(Node3D drawn)
    {
        _piece.AddChild(drawn);
        _nodes.Add(drawn);
    }

    void Clear()
    {
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _nodes.Clear();
    }

    public void Forget()
    {
        _nodes.Clear();
        _built = null;
    }

    public void ForPicture(bool on)
    {
        foreach (var n in _nodes)
            if (n is GeometryInstance3D gi && GodotObject.IsInstanceValid(gi)) gi.VisibilityRangeEnd = on ? 0 : _piece.LetteringRange();
    }

    public bool? PartAt(Vector2 local)
    {
        if (!Shows) return null;
        float w = _piece.Footprint.X, h = _piece.Footprint.Y;
        if (Mathf.Abs(local.X) > w / 2 || Mathf.Abs(local.Y) > h / 2) return null;
        float frac = (local.Y + h / 2) / h;
        return frac < NoteCard.HeadRule ? false : true;
    }
}
