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

    [Fact]
    public void A_piece_is_made_taller_alone_and_undo_puts_its_height_back()
    {
        var g = Table();
        var piece = g.Find(U, "smoke")!;
        StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1.5, Wide = 3, Deep = 0.5 }, out _);
        Assert.Null(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1.5, Tall = 4 }, out var back));
        Assert.Equal((1.5, 3.0, 0.5, 4.0), (piece.Scale, piece.WideBy, piece.DeepBy, piece.TallBy));
        Assert.Null(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 2, Wide = 1 }, out var backAgain));
        Assert.Equal((2.0, 1.0, 0.5, 4.0), (piece.Scale, piece.WideBy, piece.DeepBy, piece.TallBy));
        Assert.Null(StateMachine.Apply(g, backAgain!, out _));
        Assert.Equal((1.5, 3.0, 0.5, 4.0), (piece.Scale, piece.WideBy, piece.DeepBy, piece.TallBy));
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal((1.5, 3.0, 0.5, 1.0), (piece.Scale, piece.WideBy, piece.DeepBy, piece.TallBy));
        Assert.Null(piece.Tall);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(50.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_height_out_of_range_is_refused_and_changes_nothing(double tall)
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 3, Wide = 2, Tall = tall }, out _));
        var piece = g.Find(U, "smoke")!;
        Assert.Equal((1.5, 1.0, 1.0, 1.0), (piece.Scale, piece.WideBy, piece.DeepBy, piece.TallBy));
    }

    [Theory]
    [InlineData(PieceInstance.LeastStretch)]
    [InlineData(PieceInstance.MostStretch)]
    public void A_height_reaches_the_same_limits_as_the_width_and_depth(double tall)
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1.5, Tall = tall }, out _));
        Assert.Equal(tall, g.Find(U, "smoke")!.TallBy);
    }

    [Fact]
    public void A_height_is_kept_in_a_save_and_a_copy_and_a_save_without_one_stands_as_its_kind_does()
    {
        var plain = Json.Serialize(new PieceInstance { Id = "a", Def = "x" });
        Assert.DoesNotContain("tall", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Json.Deserialize<PieceInstance>(plain)!.TallBy);
        var g = Table();
        StateMachine.Apply(g, new SetScale { Layer = U, Id = "smoke", Scale = 1.5, Tall = 2.5 }, out _);
        var saved = Json.Serialize(g);
        Assert.Contains("\"tall\":2.5", saved);
        Assert.Equal(2.5, Json.Deserialize<GameState>(saved)!.Find(U, "smoke")!.TallBy);
        Assert.Equal(2.5, g.Find(U, "smoke")!.Clone().TallBy);
        var sent = Json.Deserialize<Command>(Json.Serialize<Command>(new SetScale { Layer = U, Id = "smoke", Scale = 1, Tall = 0.5 }));
        Assert.Equal(0.5, Assert.IsType<SetScale>(sent).Tall);
    }

    [Fact]
    public void A_block_stands_as_thick_as_its_kind_and_state_say_at_its_size()
    {
        var def = new PieceDef { Id = "x", Thickness = 0.2, States = { new PieceStateDef { Name = "a" }, new PieceStateDef { Name = "b", Thickness = 0.5 } } };
        Assert.Equal(0.4, def.Stands(0, 2), 9);
        Assert.Equal(1.0, def.Stands(1, 2), 9);
        Assert.Equal(PieceDef.MostThickness, def.Stands(1, 40));
        Assert.Equal(PieceDef.LeastThickness, def.Stands(0, 0.01));
    }
}
