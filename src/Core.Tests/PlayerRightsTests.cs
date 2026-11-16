using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class PlayerRightsTests
{
    [Fact]
    public void A_player_cannot_fly_until_an_umpire_lets_them_and_keeps_the_other_rights()
    {
        var rights = new GameState().RightsOf("Bruno");
        Assert.False(rights.Fly);
        Assert.True(rights.Take && rights.View && rights.Dice && rights.Measure && rights.Ping);
    }

    [Fact]
    public void One_player_rights_differ_from_everyone_and_undo_puts_them_back()
    {
        var g = new GameState();
        var ana = new PlayerRights { Fly = true, View = false };
        Assert.Null(StateMachine.Apply(g, new SetPlayerRights { Everyone = new PlayerRights { Fly = false }, Rights = new() { ["Ana"] = ana } }, out var back));
        Assert.True(g.RightsOf("ana").Fly);
        Assert.False(g.RightsOf("ANA").View);
        Assert.False(g.RightsOf("Bruno").Fly);
        ana.Fly = false;
        Assert.True(g.RightsOf("Ana").Fly);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Null(g.Rights);
        Assert.Null(g.Everyone);
    }

    [Fact]
    public void Rights_without_a_name_are_refused_and_change_nothing()
    {
        var g = new GameState();
        Assert.NotNull(StateMachine.Apply(g, new SetPlayerRights { Rights = new() { [" "] = new PlayerRights() } }, out _));
        Assert.Null(g.Rights);
    }

    [Fact]
    public void A_scenario_keeps_what_every_player_may_do_and_a_save_keeps_each_player()
    {
        var g = new GameState { Everyone = new PlayerRights { Fly = true }, Rights = new() { ["Ana"] = new PlayerRights { Take = false } } };
        Assert.True(GameState.FromScenario(g.ToScenario("s", "S")).RightsOf("Bruno").Fly);
        var back = Json.Deserialize<GameState>(Json.Serialize(g))!;
        Assert.False(back.RightsOf("Ana").Take);
        Assert.True(back.RightsOf("Bruno").Fly);
    }
}
