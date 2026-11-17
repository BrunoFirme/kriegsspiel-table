using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Server.Tests;

public class PlaceRightsTests
{
    static (GameServer Srv, long Host, long Lead, long Co) Setup()
    {
        var state = new GameState();
        state.Sheets.Add(new SheetDef { Id = "s", Name = "Ground", Size = new Size2(40, 40) });
        state.Built.Add(new BuiltThing
        {
            Id = "b-1", Sheet = "s", Kind = BuiltKinds.Settlement, Settings = { [Manners.Setting] = Manners.Continental },
            Parts = { new BuiltPart { Class = "house", At = [0, 0, 4000, 0, 4000, 4000, 0, 4000] } },
        });
        var srv = new GameServer(state);
        srv.Join(1, new JoinRequest { Name = "Head" });
        foreach (var (id, name, role) in new[] { (2L, "Lead", Role.LeadUmpire), (3L, "Co", Role.Umpire) })
        {
            srv.Join(id, new JoinRequest { Name = name, Protocol = ProtocolVersion.Current });
            srv.Control(1, new Admit { PeerId = id, Role = role });
        }
        return (srv, 1, 2, 3);
    }

    static bool Applied(List<Outgoing> o) => o.All(x => x.Message is not ErrorMessage);

    static BuiltThing Town(GameServer srv) => srv.State.Built.Single();

    [Fact]
    public void Only_the_host_and_the_lead_umpires_set_what_a_place_is_and_other_umpires_still_build()
    {
        var (srv, host, lead, co) = Setup();
        Assert.False(Applied(srv.Handle(co, new SetBuilt { Thing = ZoneOverrides.WithWar(Town(srv), true) })));
        Assert.False(Applied(srv.Handle(co, new SetBuilt { Thing = ZoneOverrides.WithWealth(Town(srv), [0], WealthLevel.Rich) })));
        Assert.False(Applied(srv.Handle(co, new Batch { Items = { new SetBuilt { Thing = ZoneOverrides.WithHolder(Town(srv), Manners.Northern, null) } } })));
        Assert.True(Applied(srv.Handle(co, new SetBuilt { Thing = BuiltGeometry.Moved(Town(srv), 10, 10) })));
        Assert.True(Applied(srv.Handle(lead, new SetBuilt { Thing = ZoneOverrides.WithWar(Town(srv), true) })));
        Assert.True(Applied(srv.Handle(host, new SetBuilt { Thing = ZoneOverrides.WithWealth(Town(srv), [0], WealthLevel.Rich) })));
        Assert.True(Applied(srv.Handle(co, new SetBuilt { Thing = BuiltGeometry.Moved(Town(srv), 10, 10) })));
        Assert.Equal(("rich", 1.0), (Town(srv).Parts[0].Wealth, Town(srv).Settings[PlaceZones.War]));
        Assert.False(Applied(srv.Handle(co, new SetBuilt { Thing = ZoneOverrides.WithWar(Town(srv), false) })));
    }
}
