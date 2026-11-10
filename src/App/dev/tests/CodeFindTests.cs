using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class CodeFindTests
{
    const string Lua = "local state = { strength = 9 }\nfunction draw()\n  self.button{ label = tostring(state.Strength) }\nend\nfunction more() state.strength = state.strength + 1 end\n";

    static void Key(CodeEdit edit, Key key, bool ctrl = false, bool shift = false) =>
        edit.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = key, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift });

    static (int line, int col) Sel(CodeEdit edit) => (edit.GetSelectionFromLine(), edit.GetSelectionFromColumn());

    [NodeTest]
    static async Task Ctrl_F_in_a_script_finds_every_match_in_turn_and_Esc_puts_the_caret_back_in_the_script(TestRunner r)
    {
        var edit = new CodeEdit { Text = Lua, CustomMinimumSize = new Vector2(400, 200) };
        var box = FindBar.WithFind(edit);
        r.AddChild(box);
        await r.Frames(1);
        var bar = box.GetChildren().OfType<FindBar>().Single();
        Check.That(!bar.Visible, "the find row stays out of the way until asked for");
        edit.GrabFocus();
        edit.Select(0, 6, 0, 11);
        Key(edit, Godot.Key.F, ctrl: true);
        await r.Frames(1);
        Check.That(bar.Visible, "Ctrl+F in the script opens the find row");
        Check.Equal("state", bar.Query, "it starts from the selected word");
        Check.Equal(4, bar.Total, "every match counts, whatever its case");
        Check.Equal((0, 6), Sel(edit), "the first match is the one under the caret");
        Key(edit, Godot.Key.F3);
        Check.Equal((2, 32), Sel(edit), "F3 selects the next match");
        Key(edit, Godot.Key.F3);
        Key(edit, Godot.Key.F3);
        Key(edit, Godot.Key.F3);
        Check.Equal((0, 6), Sel(edit), "after the last match it comes round to the first");
        Key(edit, Godot.Key.F3, shift: true);
        Check.Equal((4, 33), Sel(edit), "Shift+F3 goes back");
        edit.Deselect();
        edit.InsertTextAtCaret("state");
        await r.Frames(1);
        Check.Equal(5, bar.Total, "the count follows typing");
        edit.Text = Lua;
        await r.Frames(1);
        Check.Equal(4, bar.Total, "and a script put back whole");
        var field = bar.GetChildren().OfType<LineEdit>().Single();
        field.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = Godot.Key.Escape, Pressed = true });
        await r.Frames(1);
        Check.That(!bar.Visible, "Esc closes the find row");
        Check.Equal("", bar.Query, "and clears it");
        Check.That(edit.HasFocus(), "the caret goes back to the script");
        box.QueueFree();
    }
}
