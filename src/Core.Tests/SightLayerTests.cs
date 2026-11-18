using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class SightLayerTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void A_sight_layer_taken_away_comes_back_on_undo_with_its_kept_lines()
    {
        var g = GameState.FromScenario(CoreSamples.Scenario());
        Assert.Null(StateMachine.Apply(g, new AddBoard { Id = "b-sight", Name = "Sight", Sight = true }, out _));
        var line = new Stroke { Board = "b-sight", Points = { new(0, 0), new(2, 0) }, Sight = Stroke.SightView };
        Assert.Null(StateMachine.Apply(g, new Draw { Layer = U, Stroke = line }, out _));
        Assert.Null(StateMachine.Apply(g, new RemoveBoard { Id = "b-sight" }, out var back));
        Assert.DoesNotContain(g.Boards, b => b.Sight);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.True(g.Boards.Single(b => b.Id == "b-sight").Sight);
        Assert.Equal(Stroke.SightView, Assert.Single(g.DrawLayer(U), s => s.Board == "b-sight").Sight);
    }

    [Fact]
    public void A_sight_line_and_its_layer_come_back_from_a_save_as_they_went_in()
    {
        var g = GameState.FromScenario(CoreSamples.Scenario());
        g.Boards.Add(new BoardDef { Id = "b-sight", Name = "Sight", Sight = true });
        g.DrawLayer(U).Add(new Stroke { Id = "d-sight", Board = "b-sight", Points = { new(0, 0), new(2, 0) }, Sight = Stroke.SightBlur, Blur = 0.05 });
        var back = Json.Clone(g);
        Assert.True(back.Boards.Single(b => b.Id == "b-sight").Sight);
        var line = back.DrawLayer(U).Single(s => s.Id == "d-sight");
        Assert.Equal((Stroke.SightBlur, 0.05), (line.Sight, line.Blur));
        Assert.False(back.Boards.First().Sight);
    }
}
