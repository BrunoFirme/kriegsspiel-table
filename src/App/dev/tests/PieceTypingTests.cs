using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceTypingTests
{
    const string Script = """
        function on_load()
          self.input{ x = 0, z = 0, w = 0.6, h = 0.2, on_change = "typed" }
        end
        function typed(who, value, index, piece, typing) end
        """;

    static void Press(TestRunner r, Key key, char unicode = '\0', bool ctrl = false) =>
        r.GetViewport().PushInput(new InputEventKey { Keycode = key, Unicode = unicode, CtrlPressed = ctrl, Pressed = true });

    static void Type(TestRunner r, string text)
    {
        foreach (var ch in text) Press(r, ch is >= 'a' and <= 'z' ? Key.A + (ch - 'a') : Key.None, ch);
    }

    [NodeTest]
    static async Task Every_letter_types_into_a_piece_s_box_and_the_text_shortcuts_work_there(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 1);
        var node = h.Table.Pieces.Nodes.First();
        h.S.Do(new SetScript { Layer = Layers.Umpire, Id = node.Piece.Id, Text = Script });
        await r.Frames(3);
        bool plates = h.Hud.Cfg.LabelPlates;
        h.Table.Names.BeginWidget(node, 1, node.Piece.Widgets[0]);
        Check.That(h.Table.Typing, "the table knows a box is being typed in");
        Type(r, "bianchi");
        await r.Frames(2);
        Check.Equal("bianchi", node.Piece.Widgets[0].Value, "N and every other bound letter type into the box");
        Check.Equal(plates, h.Hud.Cfg.LabelPlates, "N does not switch the label plates while typing");
        Press(r, Key.A, ctrl: true);
        Type(r, "frimont");
        await r.Frames(2);
        Check.Equal("frimont", node.Piece.Widgets[0].Value, "after Ctrl+A typing replaces the whole text");
        Press(r, Key.A, ctrl: true);
        Press(r, Key.Backspace);
        await r.Frames(2);
        Check.Equal("", node.Piece.Widgets[0].Value, "Backspace clears the selected text");
        Type(r, "ab");
        Press(r, Key.A, ctrl: true);
        Press(r, Key.End);
        Type(r, "c");
        await r.Frames(2);
        Check.Equal("abc", node.Piece.Widgets[0].Value, "End drops the selection");
        Type(r, " de");
        Press(r, Key.Backspace, ctrl: true);
        await r.Frames(2);
        Check.Equal("abc ", node.Piece.Widgets[0].Value, "Ctrl+Backspace takes the last word, as in any text field");
        r.GetViewport().PushInput(new InputEventKey { Keycode = Key.Home, ShiftPressed = true, Pressed = true });
        Type(r, "x");
        await r.Frames(2);
        Check.Equal("x", node.Piece.Widgets[0].Value, "Shift+Home selects back to the start and typing replaces it");
        Press(r, Key.Escape);
        await r.Frames(1);
        Check.That(!h.Table.Typing, "Esc leaves the box");
        Press(r, Key.N, 'n');
        await r.Frames(1);
        Check.Equal(!plates, h.Hud.Cfg.LabelPlates, "out of the box N switches the plates again");
        h.Hud.SetLabelPlates(plates);
    }
}
