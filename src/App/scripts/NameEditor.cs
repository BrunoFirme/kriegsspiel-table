using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class NameEditor
{
    readonly TableContext _c;
    public NameEditor(TableContext c) { _c = c; }

    PieceNode? _node;
    int _widget;
    bool? _noteBody;
    string _buf = "", _orig = "";
    double _caretT;
    bool _caretOn = true;

    public PieceNode? Node => _node;

    public void Begin(PieceNode n)
    {
        End(commit: true);
        _node = n;
        _widget = 0;
        _orig = n.CardTitle;
        _buf = _orig;
        _caretT = 0;
        _caretOn = true;
        n.SetTitleText(_buf + "|");
        _c.Say("Type the name · Enter keeps it · Esc leaves it as it was");
    }

    public bool? NotePart => _node == null ? null : _noteBody;

    public void BeginNote(PieceNode n, bool body)
    {
        End(commit: true);
        _node = n;
        _widget = 0;
        _noteBody = body;
        _orig = n.NoteLine(body);
        _buf = _orig;
        _caretT = 0;
        _caretOn = true;
        n.SetNoteText(body, _buf + "|");
        _c.Say(body ? "Type the note · Enter starts a new line · Ctrl+Enter keeps it · Esc leaves it as it was" : "Type the title · Enter keeps it · Esc leaves it as it was");
    }

    public void BeginWidget(PieceNode node, int index, Widget w)
    {
        End(commit: true);
        _node = node;
        _widget = index;
        _orig = w.Value;
        _buf = _orig;
        _caretT = 0;
        _caretOn = true;
        node.SetWidgetText(index, _buf + "|");
        _c.Say($"Type {(w.Placeholder.Length > 0 ? w.Placeholder.ToLowerInvariant() : "the value")} · Enter keeps it · Esc leaves it as it was");
    }

    public void BeginTyped(PieceNode n, string typed)
    {
        Begin(n);
        _buf = typed;
        n.SetTitleText(_buf + "|");
    }

    public void End(bool commit)
    {
        if (_node == null) return;
        var n = _node;
        _node = null;
        int wi = _widget;
        _widget = 0;
        var note = _noteBody;
        _noteBody = null;
        _c.Say("");
        if (!GodotObject.IsInstanceValid(n)) return;
        if (note is { } body)
        {
            n.ClearNoteText();
            var kept = body ? _buf.TrimEnd() : _buf.Trim();
            if (commit && kept != _orig) _c.S.Do(new SetText { Layer = n.Layer, Id = n.Piece.Id, Name = body ? NoteCard.BodyLine : NoteCard.TitleLine, Value = kept });
            return;
        }
        if (wi > 0)
        {
            n.ClearWidgetText();
            var v = commit ? _buf : _orig;
            if (commit || v != _buf) _c.S.Do(new EditWidget { Layer = n.Layer, Id = n.Piece.Id, Index = wi, Value = v });
            return;
        }
        var t = _buf.Trim();
        if (commit && t != _orig)
        {
            bool nameLine = n.Def?.TextLines.Any(x => x.Name == "Name") == true || n.Piece.Text.ContainsKey("Name");
            _c.S.Do(nameLine ? new SetText { Layer = n.Layer, Id = n.Piece.Id, Name = "Name", Value = t } : new SetLabel { Layer = n.Layer, Id = n.Piece.Id, Value = t });
        }
        else n.SetTitleText(_orig);
    }

    public void Key(InputEventKey k)
    {
        switch (k.Keycode)
        {
            case Godot.Key.Escape:
                End(commit: false);
                return;
            case Godot.Key.Enter or Godot.Key.KpEnter when _noteBody == true && !k.CtrlPressed:
                _buf += "\n";
                break;
            case Godot.Key.Enter or Godot.Key.KpEnter:
                End(commit: true);
                return;
            case Godot.Key.V when k.CtrlPressed && _noteBody != null:
                var pasted = DisplayServer.ClipboardGet().Replace("\r", "");
                _buf += _noteBody == true ? pasted : pasted.Replace('\n', ' ');
                break;
            case Godot.Key.Backspace:
                if (_buf.Length > 0) _buf = _buf[..^1];
                break;
            default:
                if (k.Unicode >= 32 && !k.CtrlPressed && !k.AltPressed) _buf += char.ConvertFromUtf32((int)k.Unicode);
                break;
        }
        _caretOn = true;
        _caretT = 0;
        if (_widget > 0 && _node != null)
        {
            _c.S.Do(new EditWidget { Layer = _node.Layer, Id = _node.Piece.Id, Index = _widget, Value = _buf });
            _node.SetWidgetText(_widget, _buf + "|");
        }
        else if (_noteBody is { } body) _node?.SetNoteText(body, _buf + "|");
        else _node?.SetTitleText(_buf + "|");
    }

    public void Tick(double delta)
    {
        if (_node == null) return;
        if (!GodotObject.IsInstanceValid(_node) || (_noteBody != null ? _node.Piece.FaceDown : _widget == 0 && !_node.Piece.FaceDown))
        {
            if (_noteBody != null && GodotObject.IsInstanceValid(_node)) _node.ClearNoteText();
            _node = null;
            _widget = 0;
            _noteBody = null;
            _c.Say("");
            return;
        }
        _caretT += delta;
        if (_caretT < 0.5) return;
        _caretT = 0;
        _caretOn = !_caretOn;
        var caret = _buf + (_caretOn ? "|" : "");
        if (_widget > 0) _node.SetWidgetText(_widget, caret);
        else if (_noteBody is { } body) _node.SetNoteText(body, caret);
        else _node.SetTitleText(caret);
    }
}
