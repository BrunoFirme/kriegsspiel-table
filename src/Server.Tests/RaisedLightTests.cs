using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class RaisedLightTests
{
    static Lighting Raised(double reach) => new() { Raised = true, RaisedReachM = reach, Warmth = 0.3 };

    [Fact]
    public void The_host_raises_the_map_and_sets_its_reach()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetLighting { Lighting = Raised(800) });
        Assert.True(t.Server.State.Lighting.Raised);
        Assert.Equal(800, t.Server.State.Lighting.RaisedReachM);
    }

    [Fact]
    public void Another_umpire_changes_the_light_but_never_raising_or_its_reach()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetLighting { Lighting = Raised(800) });
        t.Server.Handle(Table.Lead, new SetLighting { Lighting = new Lighting { Warmth = -0.4 } });
        t.Server.Handle(Table.Ump3, new SetLighting { Lighting = new Lighting { Raised = false, RaisedReachM = 200, Warmth = 0.1 } });
        var now = t.Server.State.Lighting;
        Assert.True(now.Raised);
        Assert.Equal(800, now.RaisedReachM);
        Assert.Equal(0.1, now.Warmth);
    }

    [Fact]
    public void An_umpire_cannot_raise_a_flat_map()
    {
        using var t = new Table();
        t.Server.Handle(Table.Ump3, new SetLighting { Lighting = Raised(500) });
        Assert.False(t.Server.State.Lighting.Raised);
        Assert.Equal(0, t.Server.State.Lighting.RaisedReachM);
    }

    [Fact]
    public void A_batch_from_an_umpire_keeps_the_hosts_raising()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetLighting { Lighting = Raised(0) });
        t.Server.Handle(Table.Lead, new Batch { Items = { new SetLighting { Lighting = new Lighting() } } });
        Assert.True(t.Server.State.Lighting.Raised);
    }
}
