using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class HolderTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void A_piece_holds_by_its_kind_its_contents_or_its_own_switch()
    {
        var bag = new PieceDef { Id = "bag", Holds = true };
        var block = new PieceDef { Id = "block" };
        var p = CoreSamples.Piece("p-000001");
        Assert.True(p.HoldsPieces(bag));
        Assert.False(p.HoldsPieces(block));
        Assert.False(p.HoldsPieces(null));
        p.Stowed = new() { CoreSamples.Piece("p-000002") };
        Assert.True(p.HoldsPieces(block));
        p.Holds = false;
        Assert.False(p.HoldsPieces(bag));
        p.Stowed = null;
        p.Holds = true;
        Assert.True(p.HoldsPieces(block));
    }

    [Fact]
    public void Undoing_the_switch_gives_the_piece_back_to_its_kind()
    {
        var g = CoreSamples.State();
        Assert.Null(StateMachine.Apply(g, new SetPieceOption { Layer = U, Id = "p-000001", Name = PieceOptions.Holds, Value = false }, out var undo));
        Assert.False(g.Find(U, "p-000001")!.Holds);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Null(g.Find(U, "p-000001")!.Holds);
    }

    [Fact]
    public void Undoing_a_delete_keeps_the_piece_options()
    {
        var g = CoreSamples.State();
        var p = g.Find(U, "p-000001")!;
        p.NoCollision = true;
        p.PhysicalRoll = true;
        p.Holds = false;
        Assert.Null(StateMachine.Apply(g, new DeletePiece { Layer = U, Id = "p-000001" }, out var undo));
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        var back = g.Find(U, "p-000001")!;
        Assert.True(back.NoCollision);
        Assert.True(back.PhysicalRoll);
        Assert.False(back.Holds);
    }
}
