using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class GuidTakeOutTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void A_piece_taken_out_of_a_bag_under_a_guid_a_piece_on_the_table_holds_gets_a_fresh_one_and_putting_it_back_by_undo_restores_its_own()
    {
        var g = new GameState();
        var held = new PieceInstance { Id = "p-000003", Guid = "abc123" };
        g.Layers[U] = new() { new PieceInstance { Id = "p-000001", Guid = "abc123" }, new PieceInstance { Id = "p-000002", Stowed = new() { held } } };

        Assert.Null(StateMachine.Apply(g, new Unstow { Layer = U, Holder = "p-000002", Id = "p-000003" }, out var undo));
        var out1 = g.Find(U, "p-000003")!;
        Assert.NotEqual("abc123", out1.Guid);
        Assert.Matches("^[0-9a-f]{6}$", out1.Guid!);
        Assert.Equal("abc123", g.Find(U, "p-000001")!.Guid);

        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Equal("abc123", g.Find(U, "p-000002")!.Stowed!.Single().Guid);
    }

    [Fact]
    public void A_piece_taken_out_under_a_guid_nobody_else_holds_keeps_it()
    {
        var g = new GameState();
        g.Layers[U] = new() { new PieceInstance { Id = "p-000002", Stowed = new() { new PieceInstance { Id = "p-000003", Guid = "abc123" } } } };

        Assert.Null(StateMachine.Apply(g, new Unstow { Layer = U, Holder = "p-000002", Id = "p-000003" }, out var undo));

        Assert.Equal("abc123", g.Find(U, "p-000003")!.Guid);
        Assert.Null(((Stow)undo!).Guid);
    }
}
