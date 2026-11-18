using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class BoardCopyTests
{
    static GameState Drawn()
    {
        var g = new GameState { Boards = { new BoardDef { Id = "b-1", Name = "Roads" }, new BoardDef { Id = "b-2", Name = "Fronts" }, new BoardDef { Id = "b-3", Name = "Roads copy" } } };
        g.DrawLayer(Layers.Umpire).Add(new Stroke { Id = "d-1", Board = "b-1", Points = { new(0, 0), new(1, 1) }, Color = "#112233" });
        g.DrawLayer(Layers.Umpire).Add(new Stroke { Id = "d-2", Board = "b-2", Points = { new(0, 0), new(2, 2) } });
        return g;
    }

    [Fact]
    public void A_copied_layer_comes_just_below_with_its_drawings_and_a_name_not_yet_taken()
    {
        var g = Drawn();
        var copy = BoardCopy.Of(g, "b-1", "x-b9")!;
        Assert.Null(StateMachine.Apply(g, copy, out var undo));
        Assert.Equal(new[] { "Roads", "Roads copy 2", "Fronts", "Roads copy" }, g.Boards.Select(b => b.Name));
        var drawn = Assert.Single(g.DrawLayer(Layers.Umpire), s => s.Board == "x-b9");
        Assert.Equal(("#112233", 2), (drawn.Color, drawn.Points.Count));
        Assert.NotEqual("d-1", drawn.Id);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Equal(3, g.Boards.Count);
        Assert.Equal(2, g.DrawLayer(Layers.Umpire).Count);
    }

    [Fact]
    public void A_layer_that_is_gone_gives_no_copy() => Assert.Null(BoardCopy.Of(Drawn(), "b-9", "x-b9"));
}
