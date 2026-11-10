using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class WhoIsWhoTests
{
    const string U = Layers.Umpire;
    readonly ClientSession _session = new(new PackageStore(Path.Combine(TestFolders.New(), "packages")));

    static JoinResult Answer(List<Outgoing> outs, long peer) => Assert.IsType<JoinResult>(outs.First(o => o.PeerId == peer && o.Message is JoinResult).Message);

    static GameServer TableWithKarl()
    {
        var state = new GameState();
        state.EnsureBoard();
        state.Layer(U).Add(new PieceInstance { Id = "p-000001", Def = "inf", Pos = new(2, 2) });
        var srv = new GameServer(state);
        srv.Join(1, new JoinRequest { Name = "Host" });
        srv.Join(2, new JoinRequest { Name = "Karl", Protocol = ProtocolVersion.Current });
        srv.Control(1, new Admit { PeerId = 2, Role = Role.Umpire });
        return srv;
    }

    void SecondKarlJoins(GameServer srv)
    {
        _session.SetName("Karl");
        srv.Join(3, new JoinRequest { Name = "Karl", Protocol = ProtocolVersion.Current });
        _session.OnJoined(Answer(srv.Control(1, new Admit { PeerId = 3, Role = Role.Umpire }), 3));
    }

    [Fact]
    public void A_joiner_the_host_renamed_goes_by_the_hosts_name()
    {
        var srv = TableWithKarl();
        SecondKarlJoins(srv);
        Assert.Equal("Karl (2)", srv.Peer(3)!.Name);
        Assert.Equal("Karl (2)", _session.Me.Name);
    }

    [Fact]
    public void A_zone_given_to_the_hosts_name_lets_that_person_move_a_piece_in_it()
    {
        var srv = TableWithKarl();
        Assert.DoesNotContain(srv.Handle(1, new SetZone { Zone = new Zone { Name = "East", Owner = "Karl (2)", Polygon = { new(0, 0), new(5, 0), new(5, 5), new(0, 5) } } }),
            o => o.Message is ErrorMessage);
        SecondKarlJoins(srv);
        var piece = _session.View.Layer(U).Single();
        Assert.True(_session.CanTouch(U, piece));
        Assert.DoesNotContain(srv.Handle(3, new MovePiece { Layer = U, Id = piece.Id, Pos = new(3, 3) }), o => o.Message is ErrorMessage);
        Assert.Equal(new Vec2(3, 3), srv.State.Find(U, piece.Id)!.Pos);
    }

    [Fact]
    public void Away_lists_those_let_in_who_are_not_seated()
    {
        var srv = TableWithKarl();
        SecondKarlJoins(srv);
        srv.Leave(2);
        _session.OnAccess(srv.Access.Clone());
        _session.OnPeers(new PeerList { Peers = srv.Peers.ToList() });
        Assert.Equal(new[] { "Karl" }, _session.Away().Select(a => a.Name));
    }
}
