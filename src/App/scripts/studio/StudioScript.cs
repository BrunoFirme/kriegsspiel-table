using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Scripting;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server.Scripting;

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

    public string? Draft(PieceDef? sel) => _script != null && GodotObject.IsInstanceValid(_script) && _scriptFor == sel?.Id ? _script.Text : null;

    public void Let() { _script = null; _log = null; }

    public void Focus() => _script?.GrabFocus();

    public void Build(VBoxContainer p, string? draft)
    {
        var d = _s.Sel!;
        var link = _s.FileWatch.Of(KindKey(d.Id));
        if (link != null) draft = null;
        _script = new CodeEdit
        {
            Text = draft ?? d.Script ?? "", CustomMinimumSize = Ui.Px(0, 320), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true, AutoBraceCompletionEnabled = true, IndentSize = 2, IndentUseSpaces = true,
            WrapMode = TextEdit.LineWrappingMode.None, Editable = link == null,
        };
        _script.AddThemeFontSizeOverride("font_size", Ui.Px(12));
        ScriptAssist.Attach(_script, _s.Root);
        if (draft == null) _scriptBase = _script.Text;
        _scriptFor = d.Id;
        p.AddChild(FindBar.WithFind(_script));
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = Ui.Px(200, 0) };
        status.AddThemeColorOverride("font_color", Ui.Danger);
        var tools = new HBoxContainer();
        var row = new HBoxContainer();
        if (link != null) Linked(p, tools, link);
        else
        {
            var kit = Tools(tools, row, d, status);
            p.AddChild(tools);
            p.AddChild(kit.Form);
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
        var shown = new CheckBox { Text = "Show the script's buttons", ButtonPressed = _s.Prefs.ShowWidgets, TooltipText = "Where the piece's script draws on the 2D face, in green" };
        shown.Toggled += on => { _s.Prefs.ShowWidgets = on; _s.Canvas.Redraw(); };
        p.AddChild(shown);
        p.AddChild(new Label { Text = "What the scripts said", Modulate = Ui.Muted });
        _log = new ScriptConsoleView(_s.S, prompt: true, fontSize: 11) { CustomMinimumSize = Ui.Px(0, 150), Target = () => _s.Paper.Instance(_s.Sel)?.Id ?? "" };
        p.AddChild(_log);
        foreach (var (after, text, color) in _logLines) _log.AddLocal(text, color, after);
    }

    static string KindKey(string defId) => "def:" + defId;

    void Linked(VBoxContainer p, HBoxContainer tools, ScriptLink link)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
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

    ScriptKitBar Tools(HBoxContainer tools, HBoxContainer row, PieceDef d, Label status)
    {
        var id = d.Id;
        KitKind? Kind() => _s.Pack.Pieces.FirstOrDefault(x => x.Id == id) is { } def ? KitKind.Of(def) : null;
        var kit = new ScriptKitBar(_script!, Kind, said => status.Text = said, Ui.PanelLabels);
        kit.AddTo(tools);
        tools.AddChild(Ui.Btn("Wide…", () => ScriptEditor.Open(_s.Root, $"Script — {d.Name}", "The pack's script for every piece of this kind. Save writes it back to the Script tab and runs it.", _script!.Text, text =>
        {
            if (_script == null || !GodotObject.IsInstanceValid(_script)) return;
            _script.Text = text ?? "";
            ((Button)row.GetChild(row.GetChildCount() - 1)).EmitSignal(BaseButton.SignalName.Pressed);
        }, kit: Kind), "The script in a window of its own"));
        tools.AddChild(Ui.Btn("Guide", () => GuideReader.Show(_s.Root, "scripting.md"), "How scripts work, with whole examples"));
        tools.AddChild(Ui.Btn("Reference", () => GuideReader.ShowEntry(_s.Root, null), "Every call, field and event, with the module call each stands for (F1 on a word)"));
        return kit;
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
