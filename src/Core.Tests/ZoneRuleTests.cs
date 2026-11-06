using KTable.Core.Model;

namespace KTable.Core.Tests;

public class ZoneRuleTests
{
    static GameState Board() => new()
    {
        Zones =
        {
            new Zone { Id = "z-1", Owner = "Pogue", Polygon = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) } },
            new Zone { Id = "z-2", Owner = "Boggs", Polygon = { new(20, 0), new(30, 0), new(30, 10), new(20, 10) } },
        },
    };

    [Fact]
    public void An_umpire_touches_only_what_is_inside_their_own_zone()
    {
        var g = Board();
        Assert.True(ZoneRule.Allows(g, Role.Umpire, "Pogue", Layers.Umpire, new(5, 5)));
        Assert.True(ZoneRule.Allows(g, Role.Umpire, "pogue", Layers.Umpire, new(5, 5)));
        Assert.False(ZoneRule.Allows(g, Role.Umpire, "Pogue", Layers.Umpire, new(25, 5)));
        Assert.False(ZoneRule.Allows(g, Role.Umpire, "Pogue", Layers.Umpire, new(15, 5)));
    }

    [Fact]
    public void Leads_and_the_host_are_never_bound()
    {
        var g = Board();
        Assert.True(ZoneRule.Allows(g, Role.LeadUmpire, "Lead", Layers.Umpire, new(25, 5)));
        Assert.True(ZoneRule.Allows(g, Role.HostUmpire, "Host", Layers.Umpire, new(-50, 5)));
    }

    [Fact]
    public void Without_zones_an_umpire_is_free_and_without_one_of_their_own_they_touch_nothing()
    {
        Assert.True(ZoneRule.Allows(new GameState(), Role.Umpire, "Pogue", Layers.Umpire, new(99, 99)));
        Assert.False(ZoneRule.HasZone(Board(), "Nobody"));
        Assert.False(ZoneRule.Allows(Board(), Role.Umpire, "Nobody", Layers.Umpire, new(5, 5)));
    }

    [Fact]
    public void Zones_bind_only_the_umpire_board()
    {
        Assert.True(ZoneRule.Allows(Board(), Role.Umpire, "Pogue", "scratch:red", new(25, 5)));
    }
}
