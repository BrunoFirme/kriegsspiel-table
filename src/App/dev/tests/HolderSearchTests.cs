using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class HolderSearchTests
{
    const string U = Layers.Umpire;

    const string Script = """
        function on_load()
          self.show_contents(game.umpires()[1], 1)
        end
        """;

    static System.Collections.Generic.IEnumerable<Node> Descendants(Node n)
    {
        yield return n;
        foreach (var c in n.GetChildren()) foreach (var d in Descendants(c)) yield return d;
    }

    [NodeTest]
    static async Task A_script_opens_what_a_piece_holds_on_an_umpires_screen_showing_only_as_many_as_it_asks(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        var root = new Control { Name = "Screen" };
        t.Table.AddChild(root);
        PieceNode? asked = null;
        int most = -2;
        t.Table.ContentsAsked += (holder, n) =>
        {
            asked = holder;
            most = n;
            HolderWindow.Open(root, t.S, t.Table, holder, n);
        };
        await r.Frames(2);
        t.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p2" });
        t.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p3" });
        await r.Frames(2);
        t.S.Do(new SetScript { Layer = U, Id = "p1", Text = Script });
        await r.Frames(3);
        Check.That(asked?.Piece.Id == "p1", "the umpire's screen is asked to open p1");
        Check.Equal(1, most, "it asks for the first one only");
        var w = root.GetChildren().OfType<HolderWindow>().FirstOrDefault() ?? throw new TestFailure("no window of contents opened");
        var list = Descendants(w).OfType<ItemList>().First();
        Check.Equal(1, list.ItemCount, "the window lists only as many as were asked for");
        HolderWindow.Open(root, t.S, t.Table, asked!, -1);
        await r.Frames(2);
        Check.Equal(2, list.ItemCount, "opened again without a limit it lists everything it holds");
        Check.Equal(1, root.GetChildren().OfType<HolderWindow>().Count(), "the same window is reused");
        w.QueueFree();
        t.Table.QueueFree();
    }
}
