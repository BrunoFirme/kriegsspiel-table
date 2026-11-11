using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Client.Tests;

public class ReadyKeyTests
{
    const string U = Layers.Umpire;

    static GameState Table()
    {
        var g = new GameState();
        g.Layers[U] = new()
        {
            new PieceInstance { Id = "a", Pos = new Vec2(0, 0), Stowed = new() { new PieceInstance { Id = "a-smoke" } }, Ready = new ReadyPiece { Id = "a-smoke" } },
            new PieceInstance { Id = "b", Pos = new Vec2(10, 0), Stowed = new() { new PieceInstance { Id = "b-smoke" } }, Ready = new ReadyPiece { Id = "b-smoke" } },
            new PieceInstance { Id = "c", Pos = new Vec2(20, 0) },
        };
        return g;
    }

    static IEnumerable<(Type, string)> Sent(Command? c) =>
        c is Batch b ? b.Items.SelectMany(Sent) : c switch
        {
            ReleaseReady r => [(typeof(ReleaseReady), r.Holder)],
            RecallReady r => [(typeof(RecallReady), r.Holder)],
            null => [],
            _ => [(c.GetType(), "")],
        };

    [Fact]
    public void The_first_piece_put_into_an_empty_holder_becomes_its_ready_piece()
    {
        var g = Table();
        g.Layer(U).Add(new PieceInstance { Id = "d", Pos = new Vec2(30, 0) });
        g.Layer(U).Add(new PieceInstance { Id = "e", Pos = new Vec2(40, 0) });
        List<Command> Put(string holder, params string[] ids) => ids.Select(id => (Command)new Stow { Layer = U, Holder = holder, Id = id }).ToList();
        var stows = ReadyKey.Stowing(g, U, g.Find(U, "c")!, Put("c", "d", "e"));
        Assert.Equal("d", Assert.IsType<SetReady>(stows[^1]).Ready!.Id);
        Assert.Null(StateMachine.Apply(g, new Batch { Items = stows }, out _));
        Assert.Equal("d", g.Find(U, "c")!.Ready!.Id);
        Assert.Equal(Put("a", "c").Count, ReadyKey.Stowing(g, U, g.Find(U, "a")!, Put("a", "c")).Count);
    }

    [Fact]
    public void A_holder_with_its_ready_piece_out_keeps_it_and_one_whose_ready_piece_is_gone_takes_the_new_one()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = "a" }, out _));
        Assert.Single(ReadyKey.Stowing(g, U, g.Find(U, "a")!, [new Stow { Layer = U, Holder = "a", Id = "c" }]));
        g.Find(U, "c")!.Ready = new ReadyPiece { Id = "long-gone" };
        Assert.IsType<SetReady>(ReadyKey.Stowing(g, U, g.Find(U, "c")!, [new Stow { Layer = U, Holder = "c", Id = "b" }])[^1]);
    }

    [Fact]
    public void Dragging_an_out_ready_piece_in_the_piece_tree_leaves_it_on_its_holder()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = "a" }, out _));
        Assert.Empty(PieceTree.Nest(g, U, ["a-smoke"], "c"));
        Assert.Empty(PieceTree.Nest(g, U, ["a-smoke"], null));
        Assert.Single(PieceTree.Nest(g, U, ["b"], "c"));
    }

    [Fact]
    public void Picked_holders_with_their_ready_piece_in_release_it_and_others_are_left_alone()
    {
        Assert.Equal([(typeof(ReleaseReady), "a"), (typeof(ReleaseReady), "b")], Sent(ReadyKey.For(Table(), U, ["a", "b", "c"])));
        Assert.Null(ReadyKey.For(Table(), U, ["c"]));
    }

    [Fact]
    public void If_any_picked_holder_has_its_piece_out_the_key_recalls_instead()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = "a" }, out _));
        Assert.Equal([(typeof(RecallReady), "a")], Sent(ReadyKey.For(g, U, ["a", "b"])));
    }

    [Fact]
    public void The_key_on_a_released_piece_recalls_it_to_its_holder_once()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = "b" }, out _));
        Assert.Equal("b", ReadyKey.HolderOf(g, U, "b-smoke")!.Id);
        Assert.Equal([(typeof(RecallReady), "b")], Sent(ReadyKey.For(g, U, ["b-smoke", "b"])));
        Assert.Null(ReadyKey.HolderOf(g, U, "a-smoke"));
    }
}
