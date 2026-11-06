using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class BriefingTests
{
    [Fact]
    public void A_briefing_is_set_and_its_undo_puts_the_old_one_back()
    {
        var g = new GameState { Briefing = "Morning." };
        Assert.Null(StateMachine.Apply(g, new SetBriefing { Text = "18:00. Plancenoit holds." }, out var inverse));
        Assert.Equal("18:00. Plancenoit holds.", g.Briefing);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Equal("Morning.", g.Briefing);
        StateMachine.Apply(g, new SetBriefing { Text = "   " }, out _);
        Assert.Null(g.Briefing);
    }

    [Fact]
    public void Observers_neither_see_the_briefing_nor_hear_it_change()
    {
        var g = new GameState { Briefing = "The Guard forms in the valley." };
        var observer = new PeerInfo { PeerId = 5, Role = Role.Observer };
        var umpire = new PeerInfo { PeerId = 6, Role = Role.Umpire };
        Assert.Null(Visibility.Filter(g, observer).Briefing);
        Assert.Equal(g.Briefing, Visibility.Filter(g, umpire).Briefing);
        Assert.Null(Visibility.ForPeer(new SetBriefing { Text = "x" }, observer, g));
        Assert.NotNull(Visibility.ForPeer(new SetBriefing { Text = "x" }, umpire, g));
    }

    [Fact]
    public void The_briefing_goes_from_the_scenario_to_the_game_and_back()
    {
        var g = GameState.FromScenario(new Scenario { Id = "s", Name = "Waterloo", Briefing = "# 18:00\nBülow at Plancenoit." });
        Assert.Equal("# 18:00\nBülow at Plancenoit.", g.Briefing);
        Assert.Equal(g.Briefing, g.ToScenario("s", "Waterloo").Briefing);
        Assert.Equal(g.Briefing, g.DeepClone().Briefing);
    }

    [Fact]
    public void Only_a_lead_umpire_sends_a_briefing()
    {
        Assert.Equal(Sender.Lead, new SetBriefing().SentBy());
        Assert.True(new SetBriefing().OnUndoStack());
    }
}
