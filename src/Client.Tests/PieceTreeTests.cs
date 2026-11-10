using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;
using static KTable.Client.Tests.HandLayouts;

namespace KTable.Client.Tests;

public class PieceTreeTests
{
    const string U = Layers.Umpire;

    static GameState Army()
    {
        var g = new GameState();
        g.Layer(U).Add(new PieceInstance { Id = "corps", Attached = { "div" } });
        g.Layer(U).Add(new PieceInstance { Id = "div", Attached = { "bde", "gone" } });
        g.Layer(U).Add(new PieceInstance { Id = "bde" });
        g.Layer(U).Add(new PieceInstance { Id = "gun" });
        return g;
    }

    public static TheoryData<string, string[], string?, string> Drops => new()
    {
        { "onto another piece", new[] { "gun" }, "bde", "attach gun to bde" },
        { "a branch with its own child keeps it", new[] { "div", "bde" }, "gun", "attach div to gun" },
        { "onto the piece it already rides on", new[] { "bde" }, "div", "" },
        { "onto itself", new[] { "gun" }, "gun", "" },
        { "onto one it carries", new[] { "corps" }, "bde", "" },
        { "clear of every row", new[] { "bde", "gun" }, null, "detach bde" },
        { "onto a piece not on the table", new[] { "gun" }, "nobody", "" },
        { "a piece not on the table", new[] { "nobody" }, "gun", "" },
    };

    [Theory, MemberData(nameof(Drops))]
    public void A_dragged_row_nests_where_it_is_dropped_and_never_under_what_it_carries(string drop, string[] dragged, string? into, string expected)
    {
        _ = drop;
        var g = Army();
        Assert.Equal(expected, Describe(PieceTree.Nest(g, U, dragged, into)));
    }

    [Fact]
    public void Every_piece_has_one_parent_on_the_table_and_a_loop_from_an_old_save_is_cut()
    {
        var g = Army();
        g.Layer(U).Add(new PieceInstance { Id = "a", Attached = { "b" } });
        g.Layer(U).Add(new PieceInstance { Id = "b", Attached = { "a", "b" } });
        g.Find(U, "gun")!.Attached.Add("bde");
        var parents = PieceTree.Parents(g.Layer(U));
        Assert.Equal("div", parents["bde"].Id);
        Assert.False(parents.ContainsKey("gone"));
        Assert.True(parents.ContainsKey("a") != parents.ContainsKey("b"));
        Assert.Equal(new[] { 0, 1, 2 }, new[] { "corps", "div", "bde" }.Select(id => PieceTree.Depth(parents, id)));
        Assert.Equal(new[] { "div", "gun" }, PieceTree.Tops(parents, new[] { "bde", "div", "gun" }));
    }

    [Fact]
    public void A_drop_moves_each_piece_after_the_one_that_carries_it_however_deep()
    {
        var corps = new FakePiece("corps", 0, 0, 4, 4);
        var div = new FakePiece("div", 1, 0, 2, 2);
        var bde = new FakePiece("bde", 2, 0, 1, 1);
        corps.Piece.Attached.Add("div");
        div.Piece.Attached.Add("bde");
        var moved = new[] { bde, div, corps }.Select(n => (n, new MovePiece { Layer = U, Id = n.Piece.Id })).ToList();
        var order = Landing<FakePiece>.ParentsFirst(moved, new[] { corps.Piece, div.Piece, bde.Piece }).Cast<MovePiece>().Select(m => m.Id);
        Assert.Equal(new[] { "corps", "div", "bde" }, order);
    }

    [Fact]
    public void A_piece_linked_from_afar_keeps_its_link_wherever_it_is_moved_alone()
    {
        var hq = new FakePiece("hq", 0, 0, 2, 1);
        var squadron = new FakePiece("squadron", 6, 0, 1, 1);
        var hill = new FakePiece("hill", 20, 0, 8, 8, thickness: 0.3f);
        hq.Piece.Attached.Add("squadron");
        var all = new List<FakePiece> { hq, squadron, hill };
        var sticking = new Sticking<FakePiece>(_ => all, (l, id) => all.FirstOrDefault(n => n.Piece.Id == id), _ => all.Select(n => n.Piece).ToList());
        foreach (var (to, y) in new[] { (new Vec2(9, 9), 0.0), (new Vec2(20, 0), 0.3) })
        {
            var move = new MovePiece { Layer = U, Id = "squadron", Pos = to, Y = y };
            var (cmds, stuck, freed, _) = sticking.Stickiness(new List<FakePiece> { squadron }, new List<(FakePiece, MovePiece)> { (squadron, move) }, new HashSet<FakePiece> { squadron });
            Assert.Equal("", Describe(cmds));
            Assert.Equal((0, 0), (stuck, freed));
        }
    }
}
