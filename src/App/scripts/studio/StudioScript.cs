using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Scripting;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class ScriptTab
{
    readonly PieceStudio _s;
    CodeEdit? _script;
    string? _scriptFor;
    ScriptConsoleView? _log;
    readonly List<(long after, string text, string? color)> _logLines = new();
    string _scriptBase = "";

    public ScriptTab(PieceStudio s) => _s = s;

    public CodeEdit? Editor => _script;
    public int LogCount => _logLines.Count;

    static readonly (string Name, string Code)[] Snippets =
    {
        ("Skeleton: keep numbers between sessions", """
            -- What the piece remembers; saved with the game.
            local state = { strength = 9 }

            function on_save()
              return json.encode(state)
            end

            function on_load(saved)
              if saved ~= "" then state = json.decode(saved) end
              draw()
            end

            function draw()
              self.clear_widgets()
            end

            """),
        ("A number drawn on the face with − and + (a plain counter: Piece → Counters)", """
            -- Put inside draw(): a number with a button either side (x, z in piece units: see the 2D view's status line).
            self.button{ face = "front", x = 0, z = 0.3, label = tostring(state.strength), font = 0.12, text_color = "white" }
            self.button{ face = "front", x = -0.45, z = 0.3, w = 0.18, h = 0.18, label = "−", font = 0.12, on_click = "less" }
            self.button{ face = "front", x = 0.45, z = 0.3, w = 0.18, h = 0.18, label = "+", font = 0.12, on_click = "more" }

            """ + "\n-- Outside draw():\nfunction less(who) state.strength = math.max(0, state.strength - 1); draw() end\nfunction more(who) state.strength = state.strength + 1; draw() end\n"),
        ("Lettering the script changes (fixed lettering: a text layer in Paint; a name: Text lines)", "self.button{ face = \"front\", x = 0, z = -0.2, label = \"2/IR 14\", font = 0.1, text_color = \"black\" }\n"),
        ("An input box on the back", "self.input{ face = \"back\", x = 0, z = 0, w = 0.8, h = 0.16, font = 0.08, value = \"\", placeholder = \"Commander\", on_change = \"named\" }\n\nfunction named(who, value) self.set_name(value) end\n"),
        ("A row in the right-click menu", "function on_load() self.menu_add(\"Report strength\", \"report\") end\n\nfunction report(who) game.say(self.name .. \": \" .. tostring(self.counters.Strength)) end\n"),
        ("When the piece changes state", "function on_state(n)\n  game.say(self.name .. \" is now \" .. self.states[n])\nend\n"),
        ("A range ring round the piece", "-- A circle of radius r map units drawn on the table round the piece, turning with it.\nlocal r, pts = 6, {}\nfor i = 0, 72 do local a = i / 72 * math.pi * 2; pts[#pts + 1] = { x = math.cos(a) * r, z = math.sin(a) * r } end\nself.set_lines{ { points = pts, color = \"#C0392B\", width = 0.08 } }\n"),
    };

    public string? Draft(PieceDef? sel) => _script != null && GodotObject.IsInstanceValid(_script) && _scriptFor == sel?.Id ? _script.Text : null;

    public void Let() { _script = null; _log = null; }

    public void Focus() => _script?.GrabFocus();

    public void Build(VBoxContainer p, string? draft)
    {
        var d = _s.Sel!;
        var link = _s.FileWatch.Of(KindKey(d.Id));
        if (link != null) draft = null;
        p.AddChild(Ui.Hint("Every piece of this kind runs this Lua at the table and in Try it. Counters, text lines, names, looks and markers need no script (Piece and Markers tabs): script only buttons of your own, numbers drawn on the face and panels. F1 on a word opens the reference."));
        _script = new CodeEdit
        {
            Text = draft ?? d.Script ?? "", CustomMinimumSize = new Vector2(0, 320), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true, AutoBraceCompletionEnabled = true, IndentSize = 2, IndentUseSpaces = true,
            WrapMode = TextEdit.LineWrappingMode.None, PlaceholderText = "-- no script", Editable = link == null,
        };
        _script.AddThemeFontSizeOverride("font_size", 12);
        ScriptAssist.Attach(_script, _s.Root);
        if (draft == null) _scriptBase = _script.Text;
        _scriptFor = d.Id;
        p.AddChild(_script);
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) };
        status.AddThemeColorOverride("font_color", Ui.Danger);
        var tools = new HBoxContainer();
        var row = new HBoxContainer();
        if (link != null) Linked(p, tools, link);
        else
        {
            Tools(tools, row, d);
            p.AddChild(tools);
            row.AddChild(Ui.Btn("Edit as a file", () => EditAsFile(d, status), "Writes the script to a .lua file for your own editor or AI agent; each save there runs here, and errors go to console.log beside it"));
            row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            if (d.Script != null) row.AddChild(Ui.Btn("Remove", () => Ui.Confirm(_s.Root, "Remove the script", "Remove", () =>
            {
                _script.Text = "";
                _scriptBase = "";
                _s.Apply(x => x.Script = null, inspector: true);
                _s.ScriptsChanged?.Invoke();
            })));
            row.AddChild(Ui.Primary("Save and run", () => SaveAndRun(d, status), "Keeps the script in the pack and runs it again from on_load on the Table view   (Ctrl+Enter)"));
            _script.GuiInput += e =>
                { if (e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter,
                    CtrlPressed: true }) { ((Button)row.GetChild(row.GetChildCount() - 1)).EmitSignal(BaseButton.SignalName.Pressed); _script.AcceptEvent(); } };
            p.AddChild(row);
        }
        p.AddChild(status);
        p.AddChild(new Label { Text = "What the scripts said", Modulate = Ui.Muted });
        _log = new ScriptConsoleView(_s.S, prompt: true, fontSize: 11) { CustomMinimumSize = new Vector2(0, 150), Target = () => _s.Paper.Instance(_s.Sel)?.Id ?? "" };
        p.AddChild(_log);
        foreach (var (after, text, color) in _logLines) _log.AddLocal(text, color, after);
        p.AddChild(Ui.Hint("Widget positions are piece units (centre 0, x right, z down the picture): in the 2D view the status line reads them under the pointer, and Ctrl+right-click copies them."));
    }

    static string KindKey(string defId) => "def:" + defId;

    void Linked(VBoxContainer p, HBoxContainer tools, ScriptLink link)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", Ui.Gap.Row);
        line.AddChild(new Label { Text = "Edited as a file: " + link.File, TooltipText = link.Path, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        line.AddChild(Ui.Btn("Open folder", _s.FileWatch.OpenFolder, "The folder with the script, ktable-api.lua and console.log"));
        line.AddChild(Ui.Btn("Copy path", () => AppClipboard.SetText(link.Path), link.Path));
        line.AddChild(Ui.Btn("Stop", () => _s.FileWatch.Unlink(link.Key), "Edit it here again; the file is kept"));
        p.AddChild(line);
        tools.AddChild(Ui.Btn("Guide", () => GuideReader.Show(_s.Root, "scripting.md"), "How scripts work, with whole examples"));
        tools.AddChild(Ui.Btn("Reference", () => GuideReader.ShowEntry(_s.Root, null), "Every call, field and event, with the module call each stands for (F1 on a word)"));
        p.AddChild(tools);
    }

    internal void EditAsFile(PieceDef d, Label? status)
    {
        if (_script != null && GodotObject.IsInstanceValid(_script) && _script.Text != _scriptBase && status != null) { status.Text = "Save and run first, or undo the changes here: the file starts from the saved script."; return; }
        var id = d.Id;
        PieceDef? Def() => _s.Pack.Pieces.FirstOrDefault(x => x.Id == id);
        _s.FileWatch.Link(_s.Root, KindKey(id), ScriptFiles.KindFile(_s.Pack.Id, id), () => Def()?.Script, () => Def() != null, text => SetFromOutside(id, text));
    }

    internal void SetFromOutside(string defId, string? text)
    {
        var def = _s.Pack.Pieces.FirstOrDefault(x => x.Id == defId);
        if (def == null) return;
        if (def == _s.Sel) _s.Apply(x => x.Script = text);
        else
        {
            def.Script = text;
            _s.SavePack();
            _s.T.RebuildPiecesOf(def.Id);
        }
        _s.ScriptsChanged?.Invoke();
        LogLine("— script saved and started again —", "#9aa0a6");
        if (_scriptFor == defId && _script != null && GodotObject.IsInstanceValid(_script)) { _script.Text = text ?? ""; _scriptBase = _script.Text; }
        if (def == _s.Sel && _s.View == "paint" && _s.Paper.Instance(def) is { } pi) { _s.Canvas.Widgets = pi.Widgets.ToList(); _s.Canvas.Redraw(); }
    }

    void Tools(HBoxContainer tools, HBoxContainer row, PieceDef d)
    {
        var ins = new MenuButton { Text = "Insert ▾", FocusMode = Control.FocusModeEnum.None, TooltipText = "Pieces of script to start from" };
        for (int i = 0; i < Snippets.Length; i++) ins.GetPopup().AddItem(Snippets[i].Name, i);
        ins.GetPopup().IdPressed += id => { _script!.InsertTextAtCaret(Snippets[id].Code.Replace("\r", "")); _script.GrabFocus(); };
        tools.AddChild(ins);
        tools.AddChild(Ui.Btn("Wide…", () => ScriptEditor.Open(_s.Root, $"Script — {d.Name}", "The pack's script for every piece of this kind. Save writes it back to the Script tab and runs it.", _script!.Text, text =>
        {
            if (_script == null || !GodotObject.IsInstanceValid(_script)) return;
            _script.Text = text ?? "";
            ((Button)row.GetChild(row.GetChildCount() - 1)).EmitSignal(BaseButton.SignalName.Pressed);
        }), "The script in a window of its own"));
        tools.AddChild(Ui.Btn("Guide", () => GuideReader.Show(_s.Root, "scripting.md"), "How scripts work, with whole examples"));
        tools.AddChild(Ui.Btn("Reference", () => GuideReader.ShowEntry(_s.Root, null), "Every call, field and event, with the module call each stands for (F1 on a word)"));
    }

    void SaveAndRun(PieceDef d, Label status)
    {
        var t = _script!.Text;
        if (t.Trim().Length == 0)
        {
            _scriptBase = t;
            _s.Apply(x => x.Script = null);
            _s.ScriptsChanged?.Invoke();
            status.Text = "";
            return;
        }
        var verdict = ScriptSafety.Inspect(t);
        if (verdict.Hostile) { status.Text = "Refused: " + string.Join("; ", verdict.Reasons); return; }
        status.Text = "";
        LogLine("— script saved and started again —", "#9aa0a6");
        _scriptBase = t;
        _s.Apply(x => x.Script = t);
        _s.ScriptsChanged?.Invoke();
        if (_s.View == "paint" && _s.Paper.Instance(d) is { } pi) { _s.Canvas.Widgets = pi.Widgets.ToList(); _s.Canvas.Redraw(); }
    }

    public string? SaveDraft()
    {
        if (_script == null || !GodotObject.IsInstanceValid(_script) || _scriptFor == null) return null;
        var t = _script.Text;
        if (t == _scriptBase) return null;
        var def = _s.Pack.Pieces.FirstOrDefault(p => p.Id == _scriptFor);
        if (def == null) return null;
        var keep = t.Trim().Length == 0 ? null : t;
        if (keep != null && ScriptSafety.Inspect(keep) is { Hostile: true } verdict)
            return $"the script of {def.Name}, refused: {string.Join("; ", verdict.Reasons)}";
        _scriptBase = t;
        if (keep == def.Script) return null;
        def.Script = keep;
        _s.SavePack();
        _s.T.RebuildPiecesOf(def.Id);
        LogLine("— script saved and started again —", "#9aa0a6");
        _s.ScriptsChanged?.Invoke();
        return null;
    }

    public void LogLine(string text, string? color)
    {
        long after = _s.S.ConsoleLines.Count > 0 ? _s.S.ConsoleLines[^1].Seq : 0;
        _logLines.Add((after, text, color));
        if (_logLines.Count > 300) _logLines.RemoveAt(0);
        if (_log != null && GodotObject.IsInstanceValid(_log)) _log.AddLocal(text, color, after);
    }
}
