using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Server.Tests;

public class ZoneTests
{
    static (GameServer srv, long host, long lead, long co, long obs) Setup()
    {
        var state = new GameState();
        state.Layer(Layers.Umpire).Add(new PieceInstance { Id = "p-000001", Def = "x", Pos = new(5, 5) });
        state.Layer(Layers.Umpire).Add(new PieceInstance { Id = "p-000002", Def = "x", Pos = new(50, 50) });
        state.NextId = 3;
        var srv = new GameServer(state);
        srv.Join(1, new JoinRequest { Name = "Head" });
        foreach (var (id, name, role) in new[] { (2L, "Lead", Role.LeadUmpire), (3L, "Co", Role.Umpire), (4L, "Watcher", Role.Observer) })
        {
            srv.Join(id, new JoinRequest { Name = name, Protocol = ProtocolVersion.Current });
            srv.Control(1, new Admit { PeerId = id, Role = role });
        }
        return (srv, 1, 2, 3, 4);
    }

    static Zone Square(string owner) => new() { Name = "SW", Owner = owner, Polygon = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) } };

    static bool Applied(List<Outgoing> o) => o.All(x => x.Message is not ErrorMessage);

    [Fact]
    public void TheHostAndTheLeadsAssignZones()
    {
        var (srv, host, lead, co, _) = Setup();
        Assert.Equal(Role.HostUmpire, srv.Peer(host)!.Role);
        Assert.False(Applied(srv.Handle(co, new SetZone { Zone = Square("Co") })));
        Assert.True(Applied(srv.Handle(lead, new SetZone { Zone = Square("Co") })));
        Assert.True(Applied(srv.Handle(host, new SetZone { Zone = Square("Co") })));
        Assert.Equal(2, srv.State.Zones.Count);
        var id = srv.State.Zones[0].Id;
        Assert.False(Applied(srv.Handle(co, new RemoveZone { Id = id })));
        Assert.True(Applied(srv.Handle(lead, new RemoveZone { Id = id })));
    }

    [Fact]
    public void AnUmpireIsUnrestrictedUntilZonesExist()
    {
        var (srv, _, _, co, _) = Setup();
        Assert.True(Applied(srv.Handle(co, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(60, 60) })));
    }

    [Fact]
    public void AnUmpireIsLimitedToTheirOwnZoneTheLeadsAreNot()
    {
        var (srv, host, lead, co, _) = Setup();
        srv.Handle(lead, new SetZone { Zone = Square("co") });
        Assert.True(Applied(srv.Handle(co, new MovePiece { Layer = Layers.Umpire, Id = "p-000001", Pos = new(6, 6) })));
        Assert.False(Applied(srv.Handle(co, new MovePiece { Layer = Layers.Umpire, Id = "p-000001", Pos = new(30, 30) })), "may not move a piece out");
        Assert.False(Applied(srv.Handle(co, new SetCounter { Layer = Layers.Umpire, Id = "p-000002", Name = "Ammo", Value = 1 })), "piece outside");
        Assert.False(Applied(srv.Handle(co, new Batch { Items = { new MovePiece { Layer = Layers.Umpire, Id = "p-000001", Pos = new(7, 7) }, new DeletePiece { Layer = Layers.Umpire, Id = "p-000002" } } })));
        Assert.True(Applied(srv.Handle(host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(70, 70) })), "host unrestricted");
        Assert.True(Applied(srv.Handle(lead, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(80, 80) })), "lead unrestricted");
        srv.Handle(host, new SetZone { Zone = Square("Lead") });
        srv.Handle(host, new RemoveZone { Id = srv.State.Zones.First(z => z.Owner == "co").Id });
        Assert.Contains("assigned to you", ((ErrorMessage)srv.Handle(co, new MovePiece { Layer = Layers.Umpire, Id = "p-000001", Pos = new(6, 6) }).Single().Message).Text);
        srv.Control(host, new SetRole { PeerId = lead, Role = Role.Umpire });
        Assert.True(Applied(srv.Handle(lead, new MovePiece { Layer = Layers.Umpire, Id = "p-000001", Pos = new(6, 6) })));
        Assert.False(Applied(srv.Handle(lead, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(90, 90) })));
    }

    [Fact]
    public void ZonesNeverReachObservers()
    {
        var (srv, host, _, _, obs) = Setup();
        var outs = srv.Handle(host, new SetZone { Zone = Square("Co") });
        Assert.DoesNotContain(outs, o => o.PeerId == obs);
        Assert.Empty(Visibility.Filter(srv.State, srv.Peer(obs)!).Zones);
        Assert.Single(Visibility.Filter(srv.State, srv.Peer(host)!).Zones);
        Assert.DoesNotContain(srv.Handle(host, new RemoveZone { Id = srv.State.Zones[0].Id }), o => o.PeerId == obs);
    }
}
