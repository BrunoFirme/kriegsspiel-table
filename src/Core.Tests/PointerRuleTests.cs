using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class PointerRuleTests
{
    [Fact]
    public void Players_see_the_umpires_pointers_only_when_the_rule_says_so_and_undo_puts_the_rule_back()
    {
        var g = new GameState();
        Assert.False(g.PointersToPlayers);
        Assert.Null(StateMachine.Apply(g, new SetPointerRule { AlwaysShown = false, ToPlayers = true }, out var inverse));
        Assert.True(g.PointersToPlayers);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.False(g.PointersToPlayers);
    }

    [Fact]
    public void Changing_whether_umpires_may_hide_their_pointer_leaves_what_players_see_alone()
    {
        var g = new GameState { PointersToPlayers = true };
        Assert.Null(StateMachine.Apply(g, new SetPointerRule { AlwaysShown = true }, out var inverse));
        Assert.True(g.PointersAlwaysShown);
        Assert.True(g.PointersToPlayers);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.False(g.PointersAlwaysShown);
        Assert.True(g.PointersToPlayers);
    }

    [Fact]
    public void A_scenario_keeps_whether_players_see_the_pointers()
    {
        var g = new GameState { PointersToPlayers = true };
        var back = GameState.FromScenario(g.ToScenario("s", "S"));
        Assert.True(back.PointersToPlayers);
    }
}
