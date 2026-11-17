using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class PlayerRightsTests
{
    [Fact]
    public void A_player_starts_with_every_right_of_their_own_and_a_body_starts_on_the_ground()
    {
        var rights = new GameState().RightsOf("Bruno");
        Assert.True(rights.Take && rights.Dice && rights.Measure && rights.Ping);
        var body = new BodyRights();
        Assert.True(!body.Fly && body.View && body.Walk && body.Usual);
    }

    [Fact]
    public void One_player_rights_differ_from_everyone_and_undo_puts_them_back()
    {
        var g = new GameState();
        var ana = new PlayerRights { Dice = false };
        Assert.Null(StateMachine.Apply(g, new SetPlayerRights { Everyone = new PlayerRights { Ping = false }, Rights = new() { ["Ana"] = ana } }, out var back));
        Assert.False(g.RightsOf("ana").Dice);
        Assert.True(g.RightsOf("ANA").Ping);
        Assert.False(g.RightsOf("Bruno").Ping);
        ana.Dice = true;
        Assert.False(g.RightsOf("Ana").Dice);
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
        var g = new GameState { Everyone = new PlayerRights { Dice = false }, Rights = new() { ["Ana"] = new PlayerRights { Take = false } } };
        Assert.False(GameState.FromScenario(g.ToScenario("s", "S")).RightsOf("Bruno").Dice);
        var back = Json.Deserialize<GameState>(Json.Serialize(g))!;
        Assert.False(back.RightsOf("Ana").Take);
        Assert.False(back.RightsOf("Bruno").Dice);
    }

    [Fact]
    public void A_body_keeps_what_its_wearer_may_do_through_a_save_and_undo_puts_it_back()
    {
        var g = new GameState();
        g.Layers[Layers.Umpire] = [new PieceInstance { Id = "p1", Def = "x" }];
        Assert.Null(StateMachine.Apply(g, new SetBodyRights { Layer = Layers.Umpire, Id = "p1", Rights = new BodyRights { Fly = true, Walk = false } }, out var back));
        var saved = Json.Deserialize<GameState>(Json.Serialize(g))!.Find(Layers.Umpire, "p1")!.Rights!;
        Assert.True(saved.Fly && saved.View && !saved.Walk);
        Assert.NotNull(StateMachine.Apply(g, new SetBodyRights { Layer = Layers.Umpire, Id = "nobody", Rights = new BodyRights() }, out _));
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Null(g.Find(Layers.Umpire, "p1")!.Rights);
    }
}
