using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class StretchTests
{
    const string U = Layers.Umpire;

    static GameState Table()
    {
        var g = new GameState();
        g.Layers[U] = new() { new PieceInstance { Id = "smoke", Def = "x", Scale = 1.5 } };
        return g;
    }

    [Fact]
    public void A_piece_is_made_wider_or_deeper_apart_and_undo_puts_both_back()
    {
        var g = Table();
        var piece = g.Find(U, "smoke")!;
        Assert.Null(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1.5, Wide = 3 }, out var back));
        Assert.Equal((1.5, 3.0, 1.0), (piece.Scale, piece.WideBy, piece.DeepBy));
        Assert.Null(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 2, Deep = 0.5 }, out var backAgain));
        Assert.Equal((2.0, 3.0, 0.5), (piece.Scale, piece.WideBy, piece.DeepBy));
        Assert.Null(StateMachine.Apply(g, backAgain!, out _));
        Assert.Equal((1.5, 3.0, 1.0), (piece.Scale, piece.WideBy, piece.DeepBy));
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal((1.5, 1.0, 1.0), (piece.Scale, piece.WideBy, piece.DeepBy));
        Assert.Null(piece.Wide);
    }

    [Fact]
    public void A_size_change_alone_leaves_the_width_and_depth_as_they_were()
    {
        var g = Table();
        StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1.5, Wide = 2, Deep = 4 }, out _);
        Assert.Null(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1 }, out _));
        var piece = g.Find(U, "smoke")!;
        Assert.Equal((1.0, 2.0, 4.0), (piece.Scale, piece.WideBy, piece.DeepBy));
    }

    [Theory]
    [InlineData(0.01, 1.0)]
    [InlineData(1.0, 50.0)]
    [InlineData(double.NaN, 1.0)]
    public void A_width_or_depth_out_of_range_is_refused_and_changes_nothing(double wide, double deep)
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 3, Wide = wide, Deep = deep }, out _));
        var piece = g.Find(U, "smoke")!;
        Assert.Equal((1.5, 1.0, 1.0), (piece.Scale, piece.WideBy, piece.DeepBy));
    }

    [Fact]
    public void A_piece_of_its_own_shape_writes_no_width_or_depth_and_a_copy_keeps_them()
    {
        var plain = Json.Serialize(new PieceInstance { Id = "a", Def = "x" });
        Assert.DoesNotContain("wide", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deep", plain, StringComparison.OrdinalIgnoreCase);
        var copy = new PieceInstance { Id = "b", Def = "x", Wide = 2.5, Deep = 0.4 }.Clone();
        Assert.Equal((2.5, 0.4), (copy.WideBy, copy.DeepBy));
        Assert.Contains("\"wide\":2.5", Json.Serialize(copy));
    }
}
