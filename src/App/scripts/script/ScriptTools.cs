using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client.Docs;
using KTable.Client.Scripting;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.App;

public static class Guides
{
    public static string[] Tried()
    {
        var here = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        return new[]
        {
            Path.Combine(here, "docs"),
            Path.Combine(here, "..", "docs"),
            ProjectSettings.GlobalizePath("res://../docs"),
            ProjectSettings.GlobalizePath("res://../../docs"),
        }.Select(Path.GetFullPath).ToArray();
    }

    public static string? Folder() => Tried().FirstOrDefault(Directory.Exists);

    public static bool Open(string file)
    {
        if (Folder() is not { } d || !File.Exists(Path.Combine(d, file))) return false;
        OS.ShellOpen(Path.Combine(d, file));
        return true;
    }

    public static void SaveStubs(Node parent, Action<string> said)
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.lua ; Lua" },
            Title = "The API file for VS Code's Lua extension", CurrentFile = "ktable-api.lua", Size = new Vector2I(820, 520),
        };
        Ui.RememberFolder(fd);
        parent.AddChild(fd);
        fd.FileSelected += path =>
        {
            try { File.WriteAllText(path, ApiDoc.Stubs()); said($"Written: {path}. Put it in your scripts' folder (or Lua.workspace.library) and VS Code's Lua extension completes and explains every call."); }
            catch (Exception e) { said($"Could not write {path}: {e.Message}"); }
        };
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }
}

public static class ScriptAssist
{
    static readonly string[] Keywords = { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" };

    public static CodeHighlighter Lua()
    {
        var h = new CodeHighlighter
        {
            NumberColor = new Color(0.78f, 0.92f, 0.65f), SymbolColor = new Color(0.85f, 0.85f, 0.85f),
            FunctionColor = new Color(0.65f, 0.85f, 1f), MemberVariableColor = new Color(0.9f, 0.9f, 0.85f),
        };
        foreach (var k in Keywords) h.AddKeywordColor(k, new Color(1f, 0.6f, 0.45f));
        var ours = new Color(0.6f, 0.9f, 0.8f);
        foreach (var k in ApiDoc.Keywords.Concat(new[] { "string", "table", "math", "os", "coroutine", "pairs", "ipairs", "tostring", "tonumber", "type", "pcall", "error" }).Distinct()) h.AddKeywordColor(k, ours);
        var events = new Color(0.95f, 0.75f, 0.95f);
        foreach (var e in ApiDoc.All.Where(e => e.Kind == "event").Select(e => e.Name).Distinct()) h.AddKeywordColor(e, events);
        var comment = new Color(0.55f, 0.6f, 0.55f);
        var str = new Color(0.95f, 0.85f, 0.55f);
        h.AddColorRegion("--[[", "]]", comment);
        h.AddColorRegion("--", "", comment, true);
        h.AddColorRegion("\"", "\"", str, true);
        h.AddColorRegion("'", "'", str, true);
        h.AddColorRegion("[[", "]]", str);
        return h;
    }

    public static void Attach(CodeEdit edit, Node owner)
    {
        edit.SyntaxHighlighter = Lua();
        edit.CodeCompletionEnabled = true;
        edit.CodeCompletionPrefixes = new Godot.Collections.Array<string> { "." };
        edit.CodeCompletionRequested += () => Complete(edit);
        edit.TextChanged += () =>
        {
            var (_, part, code) = Before(edit);
            if (code && part.Length >= 2) edit.RequestCodeCompletion();
        };
        edit.SetTooltipRequestFunc(Callable.From<string, string>(word => Tooltip(edit, word)));
        edit.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true, Keycode: Key.F1 }) return;
            GuideReader.ShowEntry(owner, EntryAt(edit, edit.GetCaretLine(), edit.GetCaretColumn()));
            edit.AcceptEvent();
        };
    }

    static (string? receiver, string part, bool code) Before(CodeEdit edit)
    {
        var line = edit.GetLine(edit.GetCaretLine());
        return LuaText.Before(line[..Math.Clamp(edit.GetCaretColumn(), 0, line.Length)]);
    }

    static void Complete(CodeEdit edit)
    {
        var (receiver, part, code) = Before(edit);
        if (!code) return;
        var ours = new Color(0.6f, 0.9f, 0.8f);
        foreach (var e in LuaText.Completions(receiver, part))
        {
            var kind = e.Kind switch { "field" => CodeEdit.CodeCompletionKind.Member, "event" => CodeEdit.CodeCompletionKind.Signal, _ => CodeEdit.CodeCompletionKind.Function };
            edit.AddCodeCompletionOption(kind, e.Kind == "field" ? e.Name : e.Signature, e.Name, ours);
        }
        if (receiver == null)
            foreach (var name in LuaText.OwnNames(edit.Text, part))
                edit.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Variable, name, name);
        edit.UpdateCodeCompletionOptions(false);
    }

    static string Tooltip(CodeEdit edit, string word)
    {
        var mp = Pointer.Local(edit);
        var lc = edit.GetLineColumnAtPos(new Vector2I((int)mp.X, (int)mp.Y), false);
        if (lc.Y < 0 || lc.Y >= edit.GetLineCount()) return "";
        return EntryAt(edit, lc.Y, lc.X) is { } e ? Describe(e) : "";
    }

    public static ApiEntry? EntryAt(CodeEdit edit, int lineNo, int col) => LuaText.EntryAt(edit.GetLine(lineNo), col);

    public static string Describe(ApiEntry e) => LuaText.Describe(e);
}

public partial class ScriptConsoleView : VBoxContainer
{
    readonly Session _s;
    public Func<string> Target = () => "";
    public string? Only;
    public event Action<string, int>? SourceClicked;

    readonly RichTextLabel _text;
    readonly LineEdit _filter;
    readonly CheckBox _errors;
    readonly LineEdit? _prompt;
    readonly Label? _where;
    readonly List<string> _history = new();
    int _hist = -1;
    long _clearedTo;
    readonly List<(long after, string bb)> _extra = new();

    public ScriptConsoleView(Session s, bool prompt, int fontSize = 12)
    {
        _s = s;
        AddThemeConstantOverride("separation", Ui.Gap.Tight);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _filter = new LineEdit { PlaceholderText = "Filter: a piece or a word", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _filter.TextChanged += _ => Rebuild();
        bar.AddChild(_filter);
        _errors = new CheckBox { Text = "Errors only", FocusMode = FocusModeEnum.None };
        _errors.Toggled += _ => Rebuild();
        bar.AddChild(_errors);
        bar.AddChild(Ui.Btn("Copy", () => AppClipboard.SetText(string.Join("\n", Shown().Select(Plain))), "Copy the lines shown"));
        bar.AddChild(Ui.Btn("Clear", () =>
        {
            _clearedTo = _s.ConsoleLines.Count > 0 ? _s.ConsoleLines[^1].Seq : 0;
            _extra.Clear();
            Rebuild();
        }, "Clears this view (the scripts keep running; nothing is deleted)"));
        AddChild(bar);
        _text =
            new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 90),
                MetaUnderlined = false };
        _text.AddThemeFontSizeOverride("normal_font_size", fontSize);
        _text.AddThemeFontSizeOverride("mono_font_size", fontSize);
        _text.MetaClicked += meta =>
        {
            var m = meta.AsString();
            var bar2 = m.LastIndexOf('|');
            if (bar2 > 0 && int.TryParse(m[(bar2 + 1)..], out var line)) SourceClicked?.Invoke(m[..bar2], line);
        };
        AddChild(_text);
        if (prompt)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Row);
            _where = new Label { Modulate = Ui.Muted, ClipText = true, CustomMinimumSize = new Vector2(90, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            row.AddChild(_where);
            _prompt = new LineEdit { PlaceholderText = "Lua — Enter runs it; Up and Down go through what was run", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _prompt.AddThemeFontSizeOverride("font_size", fontSize + 1);
            _prompt.TextSubmitted += Run;
            _prompt.GuiInput += e =>
            {
                if (e is not InputEventKey { Pressed: true } k || _history.Count == 0) return;
                if (k.Keycode == Key.Up) { _hist = _hist < 0 ? _history.Count - 1 : Math.Max(0, _hist - 1); }
                else if (k.Keycode == Key.Down)
                {
                    if (_hist < 0) return;
                    _hist++;
                    if (_hist >= _history.Count)
                    {
                        _hist = -1;
                        _prompt.Text = "";
                        _prompt.AcceptEvent();
                        return;
                    }
                }
                else return;
                _prompt.Text = _history[_hist];
                _prompt.CaretColumn = _prompt.Text.Length;
                _prompt.AcceptEvent();
            };
            row.AddChild(_prompt);
            if (!_s.IsLead) { _prompt.Editable = false; _prompt.PlaceholderText = "Only the host and the lead umpires run code here"; }
            AddChild(row);
        }
        _s.ConsoleChanged += OnChanged;
        TreeExiting += () => _s.ConsoleChanged -= OnChanged;
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (_where == null) return;
        var t = Target();
        var name = t.Length == 0 ? "global" : _s.View.Find(Layers.Umpire, t) is { } p ? p.Label ?? _s.Def(p.Def)?.Name ?? t : t;
        var text = "in " + name;
        if (_where.Text != text) { _where.Text = text; _where.TooltipText = t.Length == 0 ? "Typed code runs in the global script" : $"Typed code runs in the script of {name} ({t}); select another piece to run it there"; }
    }

    public void FocusPrompt() => _prompt?.GrabFocus();

    void Run(string code)
    {
        code = code.Trim();
        if (code.Length == 0 || !_s.IsLead) return;
        if (_history.Count == 0 || _history[^1] != code) _history.Add(code);
        _hist = -1;
        _prompt!.Text = "";
        _s.Request(new RunLua { Target = Target(), Code = code });
    }

    public void AddLocal(string text, string? color, long? after = null)
    {
        var bb = $"[color={color ?? "#e8e4dc"}]{Esc(text)}[/color]";
        _extra.Add((after ?? (_s.ConsoleLines.Count > 0 ? _s.ConsoleLines[^1].Seq : 0), bb));
        if (_extra.Count > 300) _extra.RemoveAt(0);
        _text.AppendText(Lit(bb) + "\n");
    }

    string Lit(string bb)
    {
        var terms = DocSearch.Terms(_filter.Text);
        if (terms.Count == 0) return bb;
        int n = 0;
        return TextFind.Mark(bb, terms, -1, ref n, new List<int>());
    }

    void OnChanged(List<ConsoleLine>? fresh)
    {
        if (fresh == null || _filter.Text.Length > 0) { Rebuild(); return; }
        foreach (var l in fresh) if (Passes(l)) _text.AppendText(Lit(Format(l)) + "\n");
    }

    IEnumerable<ConsoleLine> Shown() => _s.ConsoleLines.Where(Passes);

    bool Passes(ConsoleLine l)
    {
        if (l.Seq <= _clearedTo) return false;
        if (Only != null && l.Source != Only) return false;
        if (_errors.ButtonPressed && l.Kind != "error") return false;
        var f = _filter.Text.Trim();
        return f.Length == 0 || l.Text.Contains(f, StringComparison.OrdinalIgnoreCase) || (l.Name ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)
            || l.Source.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void Rebuild()
    {
        _text.Clear();
        int x = 0;
        foreach (var l in Shown())
        {
            while (x < _extra.Count && _extra[x].after < l.Seq) _text.AppendText(Lit(_extra[x++].bb) + "\n");
            _text.AppendText(Lit(Format(l)) + "\n");
        }
        while (x < _extra.Count) _text.AppendText(Lit(_extra[x++].bb) + "\n");
    }

    static string Esc(string t) => t.Replace("[", "[lb]");

    static string Plain(ConsoleLine l) => $"{When(l)} {(l.Source.Length == 0 ? "global" : l.Name ?? l.Source)}: {(l.Kind == "input" ? "> " : l.Kind == "result" ? "= " : "")}{l.Text}";

    static string When(ConsoleLine l) => DateTime.TryParse(l.At, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("HH:mm:ss") : "";

    static string Format(ConsoleLine l)
    {
        var color = l.Kind switch { "error" => "#ff8a70", "todo" => "#c9a35a", "input" => "#8ab4f8", "result" => "#a8d8a0", "system" => "#9aa0a6", _ => "#e8e4dc" };
        var src = l.Source.Length == 0 ? "global" : l.Name ?? l.Source;
        var where = l.Line > 0 ? $" :{l.Line}" : "";
        var prefix = l.Kind == "input" ? "> " : l.Kind == "result" ? "= " : "";
        return $"[color=#6f6b64]{When(l)}[/color] [url={l.Source}|{l.Line}][color=#9fb4c8]{Esc(src)}{where}[/color][/url]  [color={color}]{prefix}{Esc(l.Text)}[/color]";
    }
}

public partial class ScriptWindow : Window
{
    readonly Session _s;
    readonly Table? _t;
    static readonly Color ErrorWash = new(Ui.Danger, 0.3f);
    readonly ItemList _list = new() { CustomMinimumSize = new Vector2(230, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    readonly LineEdit _find = new() { PlaceholderText = "Find a script", ClearButtonEnabled = true };
    readonly CodeEdit _code = new();
    internal CodeEdit Code => _code;
    readonly CodeEdit _xml = new() { Editable = false };
    readonly TextEdit _state = new() { Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary };
    readonly Label _head = new() { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
    readonly Label _note = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    readonly ScriptConsoleView _console;
    readonly Button _save, _back, _show, _asFile;
    readonly ScriptFileWatch _files;
    readonly HBoxContainer _linkRow = new();
    readonly Label _linkLabel = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
    readonly Dictionary<string, string> _drafts = new();
    List<Row> _rows = new();
    string? _key;
    bool _loading;

    sealed record Row(string Key, string Label, List<string> Ids);

    static ScriptWindow? _open;

    public static void Open(Node parent, Session s, Table? t, ScriptFileWatch files, string? select)
    {
        if (_open != null && IsInstanceValid(_open))
        {
            _open.Select(select);
            _open.GrabFocus();
            return;
        }
        var w = new ScriptWindow(s, t, files);
        _open = w;
        parent.AddChild(w);
        w.PopupCentered(new Vector2I(1080, 720));
        w.Select(select);
        s.Request(new GetScripts());
    }

    ScriptWindow(Session s, Table? t, ScriptFileWatch files)
    {
        _s = s;
        _t = t;
        _files = files;
        Title = "Scripting";
        Transient = true;
        Exclusive = false;
        WrapControls = false;
        MinSize = new Vector2I(760, 460);
        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Surface, 8, 6));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);
#pragma warning disable CS0618
        var outer = new HSplitContainer { SplitOffset = 240 };
#pragma warning restore CS0618
        bg.AddChild(outer);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        left.AddChild(_find);
        left.AddChild(_list);
        left.AddChild(Ui.Hint("Red: its last call failed. Amber: stopped, or it does not load. • : changes not saved. A kind whose pieces run their pack's script is one line."));
        outer.AddChild(left);
        _find.TextChanged += _ => FillList();
        _list.ItemSelected += i => { if (i >= 0 && i < _visible.Count) Show(_visible[(int)i].Key); };

#pragma warning disable CS0618
        var right = new VSplitContainer { SplitOffset = 400 };
#pragma warning restore CS0618
        outer.AddChild(right);
        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        right.AddChild(top);
        _head.AddThemeColorOverride("font_color", Colors.White);
        top.AddChild(_head);
        _note.Modulate = Ui.Muted;
        top.AddChild(_note);
        _linkRow.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _linkRow.AddChild(_linkLabel);
        _linkRow.AddChild(Ui.Btn("Open folder", _files.OpenFolder, "The folder with the script, ktable-api.lua and console.log"));
        _linkRow.AddChild(Ui.Btn("Copy path", () => { if (_key != null && _files.Of(_key) is { } l) AppClipboard.SetText(l.Path); }, "The file's full path"));
        _linkRow.AddChild(Ui.Btn("Stop", () => { if (_key != null) _files.Unlink(_key); }, "Edit it here again; the file is kept"));
        top.AddChild(_linkRow);
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        top.AddChild(tabs);
        SetUpEditor();
        tabs.AddChild(_code);
        _xml.Name = "Panels";
        _xml.GuttersDrawLineNumbers = true;
        _xml.AddThemeFontSizeOverride("font_size", 12);
        tabs.AddChild(_xml);
        _state.Name = "Saved state";
        _state.AddThemeFontSizeOverride("font_size", 12);
        tabs.AddChild(_state);
        _show = Ui.Btn("Show on the table", ShowOnTable, "Select the piece and bring the view to it");
        _back = Ui.Btn("Back to the pack's script", BackToPack, "This piece runs its kind's script again; its own copy goes");
        _asFile = Ui.Btn("Edit as a file", EditAsFile, "Writes the script to a .lua file for your own editor or AI agent; each save there runs here, and errors go to console.log beside it");
        _save = Ui.Primary("Save and reload", Save, "Keeps the script and runs it again from on_load   (Ctrl+S)");
        top.AddChild(ButtonRow());

        _console = new ScriptConsoleView(s, prompt: true) { Target = () => ConsoleTarget() };
        _console.SourceClicked += (id, line) => { Select(id); if (line > 0) GoTo(line); };
        right.AddChild(_console);

        CloseRequested += AskClose;
        s.ScriptsChanged += OnScripts;
        s.Applied += OnApplied;
        s.Reset += OnReset;
        files.LinksChanged += Refresh;
        files.Noted += OnNoted;
        TreeExiting += () =>
        {
            s.ScriptsChanged -= OnScripts;
            s.Applied -= OnApplied;
            s.Reset -= OnReset;
            files.LinksChanged -= Refresh;
            files.Noted -= OnNoted;
            if (_open == this) _open = null;
        };
        BuildRows();
    }

    void SetUpEditor()
    {
        _code.Name = "Lua";
        _code.GuttersDrawLineNumbers = true;
        _code.AutoBraceCompletionEnabled = true;
        _code.IndentSize = 2;
        _code.IndentUseSpaces = true;
        _code.WrapMode = TextEdit.LineWrappingMode.None;
        _code.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _code.AddThemeFontSizeOverride("font_size", 13);
        ScriptAssist.Attach(_code, this);
        _code.TextChanged += OnEdited;
        _code.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.S, CtrlPressed: true } || e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter, CtrlPressed: true })
            { Save(); _code.AcceptEvent(); }
        };
    }

    HBoxContainer ButtonRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(_show);
        row.AddChild(Ui.Btn("Reference (F1)", () => GuideReader.ShowEntry(this, ScriptAssist.EntryAt(_code, _code.GetCaretLine(), _code.GetCaretColumn())), "Every call, field and event, with the module call each stands for"));
        _status.AddThemeColorOverride("font_color", Ui.Danger);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(_status);
        row.AddChild(_back);
        row.AddChild(_asFile);
        row.AddChild(_save);
        return row;
    }

    readonly List<Row> _visible = new();

    Dictionary<string, ScriptInfo> Infos() => _s.Scripts.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());

    void BuildRows(string? alsoPiece = null)
    {
        var rows = new List<Row>();
        if (_s.View.Script != null || _s.IsLead) rows.Add(new Row("", "Global script", new()));
        var groups = new Dictionary<string, List<string>>();
        foreach (var p in _s.View.Layer(Layers.Umpire))
        {
            if (p.Script != null) { rows.Add(new Row(p.Id, $"{p.Label ?? _s.Def(p.Def)?.Name ?? p.Def} · {p.Id}", new() { p.Id })); continue; }
            if (_s.Def(p.Def)?.Script is { Length: > 0 }) { if (!groups.TryGetValue(p.Def, out var g)) groups[p.Def] = g = new(); g.Add(p.Id); }
        }
        foreach (var (def, ids) in groups.OrderBy(g => _s.Def(g.Key)?.Name ?? g.Key, StringComparer.OrdinalIgnoreCase))
            rows.Add(new Row("def:" + def, $"{_s.Def(def)?.Name ?? def} — pack script ×{ids.Count}", ids));
        foreach (var id in new[] { alsoPiece, _key }.OfType<string>().Distinct())
            if (id.Length > 0 && !id.StartsWith("def:") && rows.All(r => r.Key != id) && _s.View.Find(Layers.Umpire, id) is { } ap)
                rows.Insert(_s.View.Script != null || _s.IsLead ? 1 : 0, new Row(id, $"{ap.Label ?? _s.Def(ap.Def)?.Name ?? ap.Def} · {id} (the pack's script)", new() { id }));
        _rows = rows;
        FillList();
    }

    void FillList()
    {
        var infos = Infos();
        var q = _find.Text.Trim();
        _visible.Clear();
        _list.Clear();
        foreach (var r in _rows)
        {
            if (q.Length > 0 && !r.Label.Contains(q, StringComparison.OrdinalIgnoreCase) && !r.Key.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var status = Status(r, infos);
            var i = _list.AddItem((_drafts.ContainsKey(r.Key) ? "• " : "") + r.Label);
            _list.SetItemCustomFgColor(i, status switch { "error" => Ui.Danger, "stopped" or "not loaded" => new Color(1, 0.8f, 0.4f), _ => Ui.Ink });
            _list.SetItemTooltip(i, status ?? "");
            _visible.Add(r);
            if (r.Key == _key) _list.Select(i);
        }
    }

    static string? Status(Row r, Dictionary<string, ScriptInfo> infos)
    {
        var mine = (r.Key.StartsWith("def:") ? r.Ids : new List<string> { r.Key }).Select(id => infos.GetValueOrDefault(id)).OfType<ScriptInfo>().ToList();
        if (mine.Count == 0) return null;
        return mine.Any(i => i.Status == "error") ? "error" : mine.Any(i => i.Status is "stopped" or "not loaded") ? "stopped" : "running";
    }

    public void Select(string? key)
    {
        if (key != null && key.Length > 0 && !key.StartsWith("def:") && _rows.All(r => r.Key != key)) BuildRows(alsoPiece: key);
        key ??= _rows.FirstOrDefault()?.Key;
        if (key != null) Show(key);
    }

    string? Saved(string key)
    {
        if (key.Length == 0) return _s.View.Script;
        if (key.StartsWith("def:")) return _s.Def(key[4..])?.Script;
        return _s.View.Find(Layers.Umpire, key) is { } p ? _s.Def(p.Def)?.ScriptFor(p) ?? p.Script : null;
    }

    bool Editable(string key) => key.Length == 0 ? _s.IsLead : !key.StartsWith("def:") && _s.IsUmpire && _s.View.Find(Layers.Umpire, key) != null;

    void Show(string key)
    {
        if (_key != null) Keep();
        _key = key;
        _loading = true;
        _code.Text = _drafts.TryGetValue(key, out var d) ? d : Saved(key) ?? "";
        _loading = false;
        _code.Editable = Editable(key);
        _status.Text = "";
        Refresh();
        FillList();
    }

    void Keep()
    {
        if (_key == null || !Editable(_key)) return;
        if (_code.Text != (Saved(_key) ?? "")) _drafts[_key] = _code.Text;
        else _drafts.Remove(_key);
    }

    void OnEdited()
    {
        if (_loading || _key == null) return;
        bool had = _drafts.ContainsKey(_key);
        Keep();
        if (had != _drafts.ContainsKey(_key)) FillList();
    }

    void OnNoted(string text, bool bad) => _console.AddLocal(text, bad ? "#ff8a70" : "#9aa0a6");

    void EditAsFile()
    {
        if (_key == null || !Editable(_key)) return;
        Keep();
        if (_drafts.ContainsKey(_key)) { _status.Text = "Save and reload first, or undo the changes here: the file starts from the saved script."; return; }
        _files.LinkTable(this, _key);
    }

    void Refresh()
    {
        if (_key == null) return;
        var infos = Infos();
        var key = _key;
        var link = key.StartsWith("def:") ? null : _files.Of(key);
        _linkRow.Visible = link != null;
        _linkLabel.Text = link == null ? "" : "Edited as a file: " + link.File;
        _linkLabel.TooltipText = link?.Path ?? "";
        for (int i = 0; i < _code.GetLineCount(); i++) _code.SetLineBackgroundColor(i, new Color(0, 0, 0, 0));
        string Line(ScriptInfo? info) => info == null ? "" : $" · {info.Status}" + (info.Timers > 0 ? $" · {info.Timers} timer{(info.Timers == 1 ? "" : "s")}" : "");
        if (key.Length == 0)
        {
            var info = infos.GetValueOrDefault("");
            _head.Text = "Global script" + Line(info);
            _note.Text = _s.IsLead ? "Runs once for the whole table; self is nil here. on_load, on_save, on_turn, and for every piece on_spawn, on_destroy, on_drop, on_pick_up." : "Runs once for the whole table. The host and the lead umpires change it.";
            _xml.Text = ScriptHost.ToXml(_s.View.Ui);
            _state.Text = Pretty(_s.View.ScriptState);
            Mark(info);
        }
        else if (key.StartsWith("def:"))
        {
            var r = _rows.FirstOrDefault(x => x.Key == key);
            var name = _s.Def(key[4..])?.Name ?? key[4..];
            _head.Text = $"{name} — the pack's script, on {r?.Ids.Count ?? 0} piece{(r?.Ids.Count == 1 ? "" : "s")}";
            _note.Text = $"Every {name} runs this script from its pack. Change it for all of them in Piece studio → Edit pieces → Script; to give one piece its own, right-click it on the table → Scripting….";
            _xml.Text = "";
            _state.Text = "";
            var errs = (r?.Ids ?? new()).Select(id => infos.GetValueOrDefault(id)).OfType<ScriptInfo>().Where(i => i.LastError != null).ToList();
            if (errs.Count > 0) { _status.Text = $"{errs.Count} of them in error; the first: {errs[0].LastError}"; if (errs[0].ErrorLine > 0) _code.SetLineBackgroundColor(errs[0].ErrorLine - 1, ErrorWash); }
        }
        else if (_s.View.Find(Layers.Umpire, key) is { } p)
        {
            var info = infos.GetValueOrDefault(key);
            var def = _s.Def(p.Def);
            _head.Text = $"{p.Label ?? def?.Name ?? p.Def} · {p.Id}" + (p.Guid != null && p.Guid != p.Id ? $" · guid {p.Guid}" : "") + $" · {p.Def}" + Line(info);
            _note.Text = p.Script != null ? "This piece's own script: it replaces its pack's for this piece only." : def?.Script != null ? "Its pack's script. Saving here gives this one piece its own copy; the pack's stays as it is." : "No script yet. Write one and Save and reload runs it from on_load.";
            _xml.Text = ScriptHost.ToXml(p.Ui);
            _state.Text = Pretty(p.ScriptState);
            Mark(info);
        }
        else
        {
            _head.Text = "That piece is no longer on the table.";
            _note.Text = "";
            _code.Editable = false;
        }
        _back.Visible = key.Length > 0 && !key.StartsWith("def:") && _s.View.Find(Layers.Umpire, key) is { Script: not null } own && _s.Def(own.Def)?.Script != null && _s.IsUmpire;
        _show.Visible = key.Length > 0 && _t != null;
        _save.Disabled = !Editable(key) || link != null;
        _asFile.Visible = Editable(key) && link == null;
        _code.Editable = Editable(key) && link == null;
    }

    void Mark(ScriptInfo? info)
    {
        if (info?.LastError == null) return;
        _status.Text = (info.ErrorLine > 0 ? $"Line {info.ErrorLine}: " : "") + info.LastError;
        if (info.ErrorLine > 0 && info.ErrorLine <= _code.GetLineCount()) _code.SetLineBackgroundColor(info.ErrorLine - 1, ErrorWash);
    }

    static string Pretty(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try { using var doc = System.Text.Json.JsonDocument.Parse(json); return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); }
        catch (System.Text.Json.JsonException) { return json; }
    }

    void GoTo(int line)
    {
        if (line < 1 || line > _code.GetLineCount()) return;
        _code.SetCaretLine(line - 1);
        _code.CenterViewportToCaret();
        _code.GrabFocus();
    }

    string ConsoleTarget()
    {
        if (_key == null) return "";
        if (_key.StartsWith("def:")) return _rows.FirstOrDefault(r => r.Key == _key)?.Ids.FirstOrDefault() ?? "";
        return _key;
    }

    void Save()
    {
        if (_key == null || !Editable(_key)) return;
        var t = _code.Text;
        string? text = t.Trim().Length == 0 ? null : t;
        if (text != null && ScriptSafety.Inspect(text) is { Hostile: true } v) { _status.Text = "Refused: " + string.Join("; ", v.Reasons); return; }
        _status.Text = "";
        if (_key.Length == 0) _s.Do(new SetGlobalScript { Text = text });
        else _s.Do(new KTable.Core.Protocol.SetScript { Layer = Layers.Umpire, Id = _key, Text = text });
        _drafts.Remove(_key);
        _s.Request(new GetScripts());
        BuildRows();
    }

    void BackToPack()
    {
        if (_key == null || _key.Length == 0 || _key.StartsWith("def:")) return;
        var id = _key;
        Ui.Confirm(this, "Back to the pack's script", "Go back", () =>
        {
            _drafts.Remove(id);
            _s.Do(new KTable.Core.Protocol.SetScript { Layer = Layers.Umpire, Id = id, Text = null });
            _s.Request(new GetScripts());
        }, "This piece's own copy of the script goes.");
    }

    void ShowOnTable()
    {
        if (_t == null || ConsoleTarget() is not { Length: > 0 } id || _s.View.Find(Layers.Umpire, id) is not { } p) return;
        _t.SetSelection(new[] { id });
        _t.Rig.Frame(Coords.ToWorld(p.Pos), 14);
    }

    void OnScripts() { FillList(); Refresh(); }

    void OnApplied(Command c)
    {
        if (c is not (KTable.Core.Protocol.SetScript or SetGlobalScript or SpawnPiece or DeletePiece or RestorePiece or SetScriptState or SetGlobalScriptState or SetUi or SetUiAttributes or SetLabel
            or Batch)) return;
        if (c is Batch b
            && !b.Items.Any(i => i is KTable.Core.Protocol.SetScript or SetGlobalScript or SpawnPiece or DeletePiece
                or RestorePiece)) { if (b.Items.Any(i => i is SetScriptState or SetGlobalScriptState or SetUi or SetUiAttributes)) Refresh(); return; }
        if (c is SetScriptState or SetGlobalScriptState or SetUi or SetUiAttributes or SetLabel) { Refresh(); return; }
        Keep();
        BuildRows();
        if (_key != null && !_drafts.ContainsKey(_key) && _code.Text != (Saved(_key) ?? ""))
        {
            _loading = true;
            _code.Text = Saved(_key) ?? "";
            _loading = false;
        }
        Refresh();
        _s.Request(new GetScripts());
    }

    void OnReset()
    {
        Keep();
        BuildRows();
        Refresh();
    }

    void AskClose()
    {
        Keep();
        if (_drafts.Count == 0) { QueueFree(); return; }
        var names = _drafts.Keys.Select(k => _rows.FirstOrDefault(r => r.Key == k)?.Label ?? (k.Length == 0 ? "Global script" : k)).ToList();
        var dlg = Ui.Confirm(this, "Close the script window", "Close without saving", QueueFree,
            $"Not saved: {string.Join(", ", names.Take(6))}{(names.Count > 6 ? $" and {names.Count - 6} more" : "")}.", 420);
        dlg.CancelButtonText = "Keep writing";
    }
}

public static class ScriptCheckWindow
{
    public static void Run(Node parent, string title, IEnumerable<string> args, string what)
    {
        var w = new Window { Title = title, Transient = true, Exclusive = false, WrapControls = false, MinSize = new Vector2I(560, 320) };
        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Surface, 8, 6));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        w.AddChild(bg);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        bg.AddChild(box);
        box.AddChild(Ui.Hint(what));
        var text = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        text.AddThemeFontSizeOverride("normal_font_size", 12);
        var log = new System.Text.StringBuilder();
        var find = FindBar.ForLabel(text, () => log.ToString());
        box.AddChild(find);
        box.AddChild(text);
        var row = new HBoxContainer();
        var state = new Label { Text = "Running…", Modulate = Ui.Muted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(state);
        ConverterRun? run = null;
        var stop = Ui.Btn("Stop", () => run?.Cancel());
        row.AddChild(stop);
        row.AddChild(Ui.Btn("Copy", () => AppClipboard.SetText(text.GetParsedText())));
        box.AddChild(row);
        w.CloseRequested += () => { run?.Cancel(); w.QueueFree(); };
        parent.AddChild(w);
        w.PopupCentered(new Vector2I(820, 560));
        w.WindowInput += e => { if (find.Handles(e)) w.SetInputAsHandled(); };
        run = ConverterTool.Run(args, line =>
        {
            if (!GodotObject.IsInstanceValid(text)) return;
            var bb = line.Replace("[", "[lb]") + "\n";
            log.Append(bb);
            if (find.Terms.Count > 0) find.Refresh();
            else text.AppendText(bb);
        }, code =>
        {
            if (!GodotObject.IsInstanceValid(state)) return;
            state.Text = code == 0 ? "Done." : code == ConverterTool.StoppedCode ? "Stopped." : $"Finished with problems (exit {code}): read the lines above.";
            stop.Disabled = true;
        });
    }
}
