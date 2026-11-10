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
    const float TitleFill = 0.7f, BodyFill = 0.6f, BodyLift = 0.42f, FaceGap = 0.006f, InkLift = 0.002f;

    public NoteFace(PieceNode piece) => _piece = piece;

    bool Shows => NoteCard.Is(_piece.Def) && !_piece.Piece.FaceDown && !_piece.PrintingOnly;

    public string Line(bool body)
    {
        var name = body ? NoteCard.BodyLine : NoteCard.TitleLine;
        var def = _piece.Def?.TextLines.FirstOrDefault(t => t.Name == name);
        return _piece.Piece.Text.TryGetValue(name, out var v) ? v : def?.Default ?? "";
    }

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
        var key = $"{title}\u001f{body}\u001f{_piece.Footprint}\u001f{_piece.Thickness}\u001f{_editSelected}";
        if (!force && key == _built) return;
        _built = key;
        Clear();
        float w = _piece.Footprint.X, h = _piece.Footprint.Y, faceY = _piece.Thickness + FaceGap;
        float Z(double frac) => -h / 2 + (float)frac * h;
        float left = -w / 2 + (float)NoteCard.SideMargin * w, width = w * (1 - 2 * (float)NoteCard.SideMargin);
        float titleH = (float)(NoteCard.TitleBottom - NoteCard.TitleTop) * h;
        if (title.Length > 0) Text(title.Replace('\n', ' '), 0, Z((NoteCard.TitleTop + NoteCard.TitleBottom) / 2), width, titleH * TitleFill, Ink(TitleInk, body: false), centred: true, faceY);
        float step = (float)NoteCard.RuleStep * h, em = step * BodyFill;
        var lines = Wrap(body, width, em);
        for (int i = 0; i < lines.Count && i < NoteCard.Lines; i++)
            if (lines[i].Length > 0) Text(lines[i], left, Z(NoteCard.RuleAt(i)) - step * BodyLift, width, em, Ink(BodyInk, body: true), centred: false, faceY);
    }

    Color Ink(Color plain, bool body) => _editSelected && _editBody == body ? SelectedInk : plain;

    static float Pixel(float em) => em / Mathf.Max(1, Fonts.Hand.GetHeight(Fonts.FaceSize));

    static float Wide(string s, float em) => Fonts.Hand.GetStringSize(s, HorizontalAlignment.Left, -1, Fonts.FaceSize).X * Pixel(em);

    public static List<string> Wrap(string text, float width, float em)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                var tried = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Wide(tried, em) > width) { lines.Add(line); line = word; }
                else line = tried;
                while (Wide(line, em) > width && line.Length > 1)
                {
                    int n = line.Length - 1;
                    while (n > 1 && Wide(line[..n], em) > width) n--;
                    lines.Add(line[..n]);
                    line = line[n..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    void Text(string text, float x, float z, float width, float em, Color ink, bool centred, float faceY)
    {
        float pixel = Pixel(em);
        float wide = Wide(text, em);
        if (wide > width) { pixel *= width / wide; wide = width; }
        var l = new Label3D
        {
            Text = text, FontSize = Fonts.FaceSize, OutlineSize = 0, Modulate = ink, DoubleSided = false, Font = Fonts.Hand, PixelSize = Mathf.Max(0.00002f, pixel),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderPriority = 3,
            RotationDegrees = new Vector3(-90, 0, 0), VisibilityRangeEnd = _piece.LetteringRange(), Layers = Table.TextLayer,
            Position = new Vector3(centred ? x : x + wide / 2, faceY + InkLift, z),
        };
        _piece.AddChild(l);
        _nodes.Add(l);
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
