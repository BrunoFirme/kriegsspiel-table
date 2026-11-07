using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class InputBoxTests
{
    const string Script = """
        function on_load()
          self.input{ x = 0, z = -0.2, w = 0.6, h = 0.2, validation = "integer", on_change = "typed" }
          self.input{ x = 0, z = 0.2, w = 0.6, h = 0.2, tab = "spaces", on_change = "typed" }
          self.input{ x = 0, z = 0.4, w = 0.6, h = 0.2, validation = "name", tab = "none", on_change = "typed" }
        end
        function typed(who, value, index, piece, typing) end
        """;

    static void Type(NameEditor names, string text)
    {
        foreach (var ch in text) names.Key(new InputEventKey { Keycode = Key.None, Unicode = ch, Pressed = true });
    }

    static void Press(NameEditor names, Key key) => names.Key(new InputEventKey { Keycode = key, Pressed = true });

    [NodeTest]
    static async Task An_input_box_takes_only_the_keys_its_validation_allows_and_tab_moves_on_or_types_spaces(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        var node = t.Table.Pieces.Nodes.First();
        t.S.Do(new SetScript { Layer = Layers.Umpire, Id = node.Piece.Id, Text = Script });
        await r.Frames(3);
        var widgets = node.Piece.Widgets;
        Check.Equal(3, widgets.Count, "three inputs drawn");
        var names = t.Table.Names;
        names.BeginWidget(node, 1, widgets[0]);
        Type(names, "-a1.2");
        Press(names, Key.Tab);
        await r.Frames(1);
        Check.Equal("-12", node.Piece.Widgets[0].Value, "an integer box keeps a leading minus and digits only");
        Check.That(names.Node == node, "Tab goes on to the next box");
        Type(names, "a");
        Press(names, Key.Tab);
        Press(names, Key.Enter);
        await r.Frames(1);
        Check.Equal("a" + InputKeys.FourSpaces, node.Piece.Widgets[1].Value, "a box whose Tab types spaces types four");
        names.BeginWidget(node, 3, node.Piece.Widgets[2]);
        Type(names, "jean  pAUL");
        Press(names, Key.Tab);
        Press(names, Key.Escape);
        await r.Frames(1);
        Check.Equal("", node.Piece.Widgets[2].Value, "Esc leaves the box as it was and Tab does nothing there");
        Check.Equal('J', InputKeys.Validate("name", "", 'j'), "a name starts upper case");
        Check.Equal('p', InputKeys.Validate("name", "Jean P", 'P'), "a name goes on lower case");
        Check.Equal(InputKeys.Refused, InputKeys.Validate("name", "Jean ", ' '), "a name takes no second space");
        Check.Equal('a', InputKeys.Validate("username", "", 'A'), "a user name is lower case");
        Check.Equal(InputKeys.Refused, InputKeys.Validate("alphanumeric", "", '-'), "letters and digits only");
        Check.Equal(InputKeys.Refused, InputKeys.Validate("float", "1.5", '.'), "one point only");
        t.Table.QueueFree();
    }
}
