using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class NameEditor
{
    readonly TableContext _c;
    public NameEditor(TableContext c) { _c = c; }

    const double Blink = 0.5;
    static readonly Vector2 OffScreen = new(-100000, -100000);

    PieceNode? _node;
    int _widget;
    bool? _noteBody;
    string _orig = "", _sent = "", _shown = "";
    double _caretT;
    bool _caretOn = true;
    LineEdit? _line;
    TextEdit? _area;

    public PieceNode? Node => _node;

    public bool? NotePart => _node == null ? null : _noteBody;

    string Text => _noteBody == true ? _area?.Text ?? "" : _line?.Text ?? "";

    public void BeginNote(PieceNode n, bool body)
    {
        End(commit: true);
        _node = n;
        _widget = 0;
        _noteBody = body;
        _orig = n.NoteLine(body);
        Open(_orig, multiline: body);
        _c.Say(body ? "Type the note · Enter starts a new line · Ctrl+Enter keeps it · Esc leaves it as it was" : "Type the title · Enter keeps it · Esc leaves it as it was");
    }

    public void BeginWidget(PieceNode node, int index, Widget w)
    {
        End(commit: true);
        _node = node;
        _widget = index;
        _orig = w.Value;
        _sent = _orig;
        Open(_orig, multiline: false);
        _c.Say($"Type {(w.Placeholder.Length > 0 ? w.Placeholder.ToLowerInvariant() : "the value")} · click away to finish · Esc leaves it as it was");
    }

    void Open(string text, bool multiline)
    {
        Control field;
        if (multiline)
        {
            var area = _area ??= Field(new TextEdit());
            area.Text = text;
            area.SetCaretLine(area.GetLineCount() - 1);
            area.SetCaretColumn(area.GetLine(area.GetLineCount() - 1).Length);
            field = area;
        }
        else
        {
            var line = _line ??= Field(new LineEdit());
            line.Text = text;
            line.CaretColumn = text.Length;
            field = line;
        }
        field.GrabFocus();
        if (field is LineEdit le) le.Edit();
        _caretT = 0;
        _caretOn = true;
        _shown = "";
        Show();
    }

    T Field<T>(T field) where T : Control
    {
        field.Position = OffScreen;
        field.Size = new Vector2(400, 40);
        field.SelfModulate = Colors.Transparent;
        field.MouseFilter = Control.MouseFilterEnum.Ignore;
        field.GuiInput += OnKey;
        field.FocusExited += () => Callable.From(() => { if (_node != null && !Focused) End(commit: true); }).CallDeferred();
        if (field is LineEdit line) line.TextChanged += _ => Changed();
        if (field is TextEdit area) area.TextChanged += Changed;
        _c.Scene.AddChild(field);
        return field;
    }

    Control? Active => _noteBody == true ? _area : _line;

    bool Focused => Active is { } f && GodotObject.IsInstanceValid(f) && f.HasFocus();

    public void End(bool commit)
    {
        if (_node == null) return;
        var n = _node;
        var text = _widget > 0 ? Allowed(Text) : Text;
        _node = null;
        int wi = _widget;
        _widget = 0;
        var note = _noteBody;
        _noteBody = null;
        _c.Say("");
        _line?.ReleaseFocus();
        _area?.ReleaseFocus();
        if (!GodotObject.IsInstanceValid(n)) return;
        if (note is { } body)
        {
            n.ClearNoteText();
            var kept = body ? text.TrimEnd() : text.Trim();
            if (commit && kept != _orig) _c.S.Do(new SetText { Layer = n.Layer, Id = n.Piece.Id, Name = body ? NoteCard.BodyLine : NoteCard.TitleLine, Value = kept });
            return;
        }
        n.ClearWidgetText();
        _c.S.Do(new EditWidget { Layer = n.Layer, Id = n.Piece.Id, Index = wi, Value = commit ? text : _orig });
    }

    public void Key(InputEventKey k)
    {
        if (_node == null) return;
        if (!Focused) Active?.GrabFocus();
        _c.Scene.GetViewport().PushInput(k);
    }

    void OnKey(InputEvent e)
    {
        if (_node == null || e is not InputEventKey { Pressed: true } k) return;
        bool enter = k.Keycode is Godot.Key.Enter or Godot.Key.KpEnter;
        if (k.Keycode == Godot.Key.Escape) Take(() => End(commit: false));
        else if (enter && _widget > 0) Take(() => { });
        else if (enter && (_noteBody == false || k.CtrlPressed)) Take(() => End(commit: true));
        else if (k.Keycode == Godot.Key.Tab && _widget > 0 && WidgetAt(_widget) is { } typed)
            Take(() =>
            {
                if (typed.Tab == "spaces") _line!.InsertTextAtCaret(InputKeys.FourSpaces);
                else if (typed.Tab is null or "next") NextInput();
            });
        _caretT = 0;
        _caretOn = true;
    }

    void Take(System.Action act)
    {
        _c.Scene.GetViewport().SetInputAsHandled();
        act();
    }

    void Changed()
    {
        if (_node == null) return;
        if (_widget > 0 && _line != null)
        {
            var typed = _line.Text;
            var kept = Allowed(typed);
            if (kept != typed)
            {
                int caret = Mathf.Clamp(_line.CaretColumn - (typed.Length - kept.Length), 0, kept.Length);
                _line.Text = kept;
                _line.CaretColumn = caret;
            }
            if (kept != _sent) _c.S.Do(new EditWidget { Layer = _node.Layer, Id = _node.Piece.Id, Index = _widget, Value = kept, Typing = true });
            _sent = kept;
        }
        _caretT = 0;
        _caretOn = true;
        Show();
    }

    Widget? WidgetAt(int index) => _node != null && index >= 1 && index <= _node.Piece.Widgets.Count ? _node.Piece.Widgets[index - 1] : null;

    string Allowed(string text)
    {
        text = text.Replace('\n', ' ');
        if (WidgetAt(_widget)?.Validation is not { } mode) return text;
        var kept = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            char ch = InputKeys.Validate(mode, kept.ToString(), c);
            if (ch != InputKeys.Refused) kept.Append(ch);
        }
        return kept.ToString();
    }

    (int caret, bool selected) Cursor()
    {
        if (_noteBody == true && _area != null)
        {
            int at = Enumerable.Range(0, _area.GetCaretLine()).Sum(i => _area.GetLine(i).Length + 1) + _area.GetCaretColumn();
            return (at, _area.HasSelection());
        }
        return _line == null ? (0, false) : (_line.CaretColumn, _line.HasSelection());
    }

    void Show()
    {
        if (_node == null || !GodotObject.IsInstanceValid(_node)) return;
        var text = Text;
        var (caret, selected) = Cursor();
        var shown = selected ? text : _caretOn ? text.Insert(Mathf.Clamp(caret, 0, text.Length), "|") : text;
        var key = shown + (selected ? "\u0001" : "");
        if (key == _shown) return;
        _shown = key;
        if (_widget > 0) _node.SetWidgetText(_widget, shown, selected);
        else if (_noteBody is { } body) _node.SetNoteText(body, shown, selected);
    }

    void NextInput()
    {
        var node = _node!;
        var widgets = node.Piece.Widgets;
        int from = _widget;
        End(commit: true);
        for (int step = 1; step <= widgets.Count; step++)
        {
            int i = (from - 1 + step) % widgets.Count;
            if (widgets[i].Kind == "input" && widgets[i].Face == widgets[from - 1].Face)
            {
                BeginWidget(node, i + 1, widgets[i]);
                return;
            }
        }
    }

    public void Tick(double delta)
    {
        if (_node == null) return;
        if (!GodotObject.IsInstanceValid(_node) || (_noteBody != null && _node.Piece.FaceDown))
        {
            if (_noteBody != null && GodotObject.IsInstanceValid(_node)) _node.ClearNoteText();
            _node = null;
            _widget = 0;
            _noteBody = null;
            _line?.ReleaseFocus();
            _area?.ReleaseFocus();
            _c.Say("");
            return;
        }
        _caretT += delta;
        if (_caretT >= Blink)
        {
            _caretT = 0;
            _caretOn = !_caretOn;
        }
        Show();
    }
}
