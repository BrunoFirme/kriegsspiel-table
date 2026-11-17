using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class BodyUndoTests
{
    const string U = Layers.Umpire, Piece = "p-000001";
    readonly ClientSession _host = new(new PackageStore(Path.Combine(TestFolders.New(), "packages")));
    readonly GameServer _server;

    public BodyUndoTests()
    {
        var state = new GameState();
        state.EnsureBoard();
        state.Layer(U).Add(new PieceInstance { Id = Piece, Def = "inf", Pos = new(0, 0) });
        _server = new GameServer(state);
        _host.Send = c => Deliver(_server.Handle(GameServer.HostPeer, c));
        _host.SetName("Host");
        var joined = _server.Join(GameServer.HostPeer, new JoinRequest { Name = "Host" });
        _host.OnJoined(Assert.IsType<JoinResult>(joined.First(o => o.PeerId == GameServer.HostPeer && o.Message is JoinResult).Message));
    }

    void Deliver(List<Outgoing> outs)
    {
        foreach (var o in outs.Where(o => o.PeerId == GameServer.HostPeer))
            if (o.Message is CommandMessage cm) _host.OnCommand(Json.Clone(cm.Command));
            else if (o.Message is ErrorMessage e) _host.OnError(e.Text);
    }

    void Move(double x) => _host.Do(new MovePiece { Layer = U, Id = Piece, Pos = new(x, 0) });

    string BodyMade()
    {
        var before = _server.State.Layer(U).Select(p => p.Id).ToHashSet();
        Deliver(_server.HandleForNoOne(new SpawnPiece { Layer = U, Def = "inf", Pos = new(9, 9) }));
        return _server.State.Layer(U).Single(p => !before.Contains(p.Id)).Id;
    }

    [Fact]
    public void A_body_made_for_a_walker_stays_out_of_the_hosts_undo_and_leaves_the_hosts_redo_as_it_was()
    {
        Move(1);
        Move(2);
        _host.UndoLast();
        var undoable = _host.MyUndoable.ToList();
        var redoable = _host.MyRedoable.ToList();
        Assert.Single(undoable);
        Assert.Single(redoable);

        string body = BodyMade();
        Assert.NotNull(_host.View.Find(U, body));
        Assert.Equal(undoable, _host.MyUndoable);
        Assert.Equal(redoable, _host.MyRedoable);

        _host.RedoLast();
        Assert.Equal(new Vec2(2, 0), _host.View.Find(U, Piece)!.Pos);
        _host.UndoLast();
        _host.UndoLast();
        Assert.Equal(new Vec2(0, 0), _host.View.Find(U, Piece)!.Pos);
        Assert.Empty(_host.MyUndoable);
        _host.UndoLast();
        Assert.NotNull(_host.View.Find(U, body));
        Assert.NotNull(_server.State.Find(U, body));
    }

    [Fact]
    public void A_body_let_go_is_taken_off_the_hosts_table_without_touching_the_hosts_undo_or_redo()
    {
        string body = BodyMade();
        Move(1);
        Move(2);
        _host.UndoLast();
        var undoable = _host.MyUndoable.ToList();
        var redoable = _host.MyRedoable.ToList();

        Deliver(_server.HandleForNoOne(new DeletePiece { Layer = U, Id = body }));
        Assert.Null(_host.View.Find(U, body));
        Assert.Null(_server.State.Find(U, body));
        Assert.Equal(undoable, _host.MyUndoable);
        Assert.Equal(redoable, _host.MyRedoable);
        Assert.True(_host.CaughtUp);
    }
}
