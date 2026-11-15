using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class KeptSizesTests
{
    const string U = Layers.Umpire;

    static GameState Table()
    {
        var g = new GameState();
        g.Layers[U] = new() { new PieceInstance { Id = "inn", Def = "x", Scale = 1.5 } };
        return g;
    }

    [Fact]
    public void Kept_sizes_are_stored_once_each_and_undo_forgets_them()
    {
        var g = Table();
        var piece = g.Find(U, "inn")!;
        Assert.Null(StateMachine.Apply(g, new SetScales { Layer = U, Id = "inn", Scales = [2, 0.5, 2] }, out var back));
        Assert.Equal(new[] { 2.0, 0.5 }, piece.Scales);
        Assert.Equal(1.5, piece.Scale);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Null(piece.Scales);
    }

    [Fact]
    public void Forgetting_the_kept_sizes_leaves_the_size_alone_and_undo_brings_them_back()
    {
        var g = Table();
        StateMachine.Apply(g, new SetScales { Layer = U, Id = "inn", Scales = [3] }, out _);
        Assert.Null(StateMachine.Apply(g, new SetScales { Layer = U, Id = "inn", Scales = [] }, out var back));
        var piece = g.Find(U, "inn")!;
        Assert.Null(piece.Scales);
        Assert.Equal(1.5, piece.Scale);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal(new[] { 3.0 }, piece.Scales);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(500.0)]
    [InlineData(double.NaN)]
    public void A_kept_size_out_of_range_is_refused_and_changes_nothing(double bad)
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new SetScales { Layer = U, Id = "inn", Scales = [2, bad] }, out _));
        Assert.Null(g.Find(U, "inn")!.Scales);
    }

    [Fact]
    public void A_piece_without_kept_sizes_writes_none_and_a_copy_keeps_its_own_list()
    {
        Assert.DoesNotContain("scales", Json.Serialize(new PieceInstance { Id = "a", Def = "x" }), StringComparison.OrdinalIgnoreCase);
        var kept = new PieceInstance { Id = "a", Def = "x", Scales = [2] };
        var copy = kept.Clone();
        copy.Scales!.Add(3);
        Assert.Equal(new[] { 2.0 }, kept.Scales);
        Assert.Contains("scales", Json.Serialize(kept), StringComparison.OrdinalIgnoreCase);
    }
}
