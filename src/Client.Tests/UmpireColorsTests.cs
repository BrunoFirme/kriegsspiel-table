using KTable.Core.Model;

namespace KTable.Client.Tests;

public class UmpireColorsTests
{
    static readonly List<PeerInfo> Peers =
    [
        new() { PeerId = 1, Name = "Host", Role = Role.HostUmpire },
        new() { PeerId = 2, Name = "Boggs", Role = Role.Umpire },
    ];

    static readonly GameAccess Access = new()
    {
        Approved = { new Approved { Name = "Pogue", Role = Role.Umpire }, new Approved { Name = "Watcher", Role = Role.Observer }, new Approved { Name = "Boggs", Role = Role.Umpire } },
    };

    [Fact]
    public void The_host_is_first_and_everyone_else_follows_the_order_they_were_let_in()
    {
        Assert.Equal(UmpireColors.HostSlot, UmpireColors.Slot("Host", Peers, Access));
        Assert.Equal(1, UmpireColors.Slot("Pogue", Peers, Access));
        Assert.Equal(3, UmpireColors.Slot("Boggs", Peers, Access));
    }

    [Fact]
    public void Names_match_whatever_their_case_and_spaces()
    {
        Assert.Equal(1, UmpireColors.Slot(" pogue ", Peers, Access));
    }

    [Fact]
    public void Someone_never_let_in_has_no_colour()
    {
        Assert.Null(UmpireColors.Slot("Stranger", Peers, Access));
        Assert.Null(UmpireColors.Slot("", Peers, Access));
        Assert.Null(UmpireColors.Slot("Pogue", Peers, null));
    }
}
