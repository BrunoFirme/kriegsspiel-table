using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class PickedUndoTests
{
    const string U = Layers.Umpire, A = "p-000001", B = "p-000002";
    readonly ClientSession _host = new(new PackageStore(Path.Combine(TestFolders.New(), "packages")));
    readonly GameServer _server;

    public PickedUndoTests()
    {
        var state = new GameState();
        state.EnsureBoard();
        state.Layer(U).Add(new PieceInstance { Id = A, Def = "inf", Pos = new(0, 0) });
        state.Layer(U).Add(new PieceInstance { Id = B, Def = "inf", Pos = new(0, 5) });
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

    void Move(string id, double x) => _host.Do(new MovePiece { Layer = U, Id = id, Pos = new(x, 0) });

    [Fact]
    public void Undo_of_a_picked_own_step_takes_back_that_step_alone_and_its_redo_puts_it_back()
    {
        Move(A, 1);
        long a = _host.MyUndoable.Peek();
        Move(B, 2);
        long b = _host.MyUndoable.Peek();

        _host.UndoNewest(seq => seq == a);
        Assert.Equal(new Vec2(0, 0), _host.View.Find(U, A)!.Pos);
        Assert.Equal(new Vec2(2, 0), _host.View.Find(U, B)!.Pos);
        Assert.Equal(new[] { b }, _host.MyUndoable);
        Assert.Single(_host.MyRedoable);

        Move(B, 3);
        Assert.Empty(_host.MyRedoable);
        _host.UndoNewest(seq => seq == a);
        Assert.Equal(new Vec2(0, 0), _host.View.Find(U, A)!.Pos);
        Assert.Equal(2, _host.MyUndoable.Count);
    }

    [Fact]
    public void Redo_of_a_picked_step_leaves_the_newer_taken_back_steps_where_they_are()
    {
        Move(A, 1);
        long a = _host.MyUndoable.Peek();
        Move(B, 2);
        _host.UndoNewest(seq => seq == a);
        long undoneA = _host.MyRedoable.Peek();
        _host.UndoLast();
        Assert.Equal(new Vec2(0, 5), _host.View.Find(U, B)!.Pos);
        Assert.Equal(2, _host.MyRedoable.Count);

        _host.RedoNewest(seq => seq == undoneA);
        Assert.Equal(new Vec2(1, 0), _host.View.Find(U, A)!.Pos);
        Assert.Equal(new Vec2(0, 5), _host.View.Find(U, B)!.Pos);
        Assert.Single(_host.MyRedoable);
        _host.RedoLast();
        Assert.Equal(new Vec2(2, 0), _host.View.Find(U, B)!.Pos);
    }
}
