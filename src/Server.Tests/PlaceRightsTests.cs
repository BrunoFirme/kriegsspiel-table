using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Server.Tests;

public class PlaceRightsTests
{
    static Table Built()
    {
        var t = new Table();
        t.Server.State.Built.Add(new BuiltThing
        {
            Id = "b-1", Sheet = "s-000001", Kind = BuiltKinds.Settlement, Settings = { [Manners.Setting] = Manners.Continental },
            Parts = { new BuiltPart { Class = "house", At = [0, 0, 4000, 0, 4000, 4000, 0, 4000] } },
        });
        return t;
    }

    static BuiltThing Town(Table t) => t.Server.State.Built.Single();

    static string Refused(Table t, long by, Command c) => Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Handle(by, c)).Message).Text;

    [Fact]
    public void Only_the_host_and_the_lead_umpires_set_what_a_place_is_and_other_umpires_still_build()
    {
        using var t = Built();
        const string leadsOnly = "only the host and the lead umpires set what a place is";
        Assert.Equal(leadsOnly, Refused(t, Table.Ump3, new SetBuilt { Thing = ZoneOverrides.WithWar(Town(t), true) }));
        Assert.Equal(leadsOnly, Refused(t, Table.Ump3, new SetBuilt { Thing = ZoneOverrides.WithWealth(Town(t), [0], WealthLevel.Rich) }));
        Assert.Equal(leadsOnly, Refused(t, Table.Ump3, new Batch { Items = { new SetBuilt { Thing = ZoneOverrides.WithHolder(Town(t), Manners.Northern, null) } } }));
        t.Ok(Table.Ump3, new SetBuilt { Thing = BuiltGeometry.Moved(Town(t), 10, 10) });
        t.Ok(Table.Lead, new SetBuilt { Thing = ZoneOverrides.WithWar(Town(t), true) });
        t.Ok(Table.Host, new SetBuilt { Thing = ZoneOverrides.WithWealth(Town(t), [0], WealthLevel.Rich) });
        t.Ok(Table.Ump3, new SetBuilt { Thing = BuiltGeometry.Moved(Town(t), 10, 10) });
        Assert.Equal(("rich", 1.0), (Town(t).Parts[0].Wealth, Town(t).Settings[PlaceZones.War]));
        Assert.Equal(leadsOnly, Refused(t, Table.Ump3, new SetBuilt { Thing = ZoneOverrides.WithWar(Town(t), false) }));
    }
}
