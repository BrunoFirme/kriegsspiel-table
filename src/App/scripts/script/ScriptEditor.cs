using System;
using Godot;
using KTable.Core;

namespace KTable.App;

public static class ScriptEditor
{
    public static void Open(Node parent, string title, string hint, string? text, Action<string?> save, string? clearLabel = null)
    {
        var dlg = new AcceptDialog { Title = title, OkButtonText = Ui.CloseWord };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        dlg.AddChild(box);
        box.AddChild(Ui.Hint(hint));
        var edit = new CodeEdit
        {
            Text = text ?? "", CustomMinimumSize = new Vector2(720, 440), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true, AutoBraceCompletionEnabled = true, IndentSize = 2, IndentUseSpaces = true,
            WrapMode = TextEdit.LineWrappingMode.None, ScrollFitContentHeight = false,
        };
        edit.AddThemeFontSizeOverride("font_size", 13);
        ScriptAssist.Attach(edit, parent);
        box.AddChild(edit);
        var status = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        status.AddThemeColorOverride("font_color", Ui.Danger);
        box.AddChild(status);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        if (clearLabel != null) row.AddChild(Ui.Btn(clearLabel, () => { save(null); dlg.Hide(); }));
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(Ui.Primary("Save and reload", () =>
        {
            var t = edit.Text;
            if (t.Trim().Length == 0)
            {
                save(null);
                dlg.Hide();
                return;
            }
            var verdict = ScriptSafety.Inspect(t);
            if (verdict.Hostile) { status.Text = "Refused: " + string.Join("; ", verdict.Reasons); return; }
            save(t);
            dlg.Hide();
        }, "Keeps the script and runs it again from on_load"));
        box.AddChild(row);
        Ui.Open(parent, dlg);
        Ui.FreeOnClose(dlg);
        edit.GrabFocus();
    }

    public static CodeHighlighter Lua() => ScriptAssist.Lua();
}
