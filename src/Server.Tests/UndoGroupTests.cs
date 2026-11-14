using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class UndoGroupTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    static Vec2 At(Table t, string id) => t.Server.State.Find(U, id)!.Pos;

    [Fact]
    public void One_undo_takes_back_a_stretch_of_an_umpire_s_own_changes_and_a_redo_puts_them_back()
    {
        using var t = new Table();
        var (wasP, wasMarker) = (At(t, P), At(t, Marker));
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = P, Pos = new(4, 4) });
        var from = t.Server.State.LastSeq;
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = Marker, Pos = new(6, 2) });
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = P, Pos = new(5, 5) });
        t.Ok(Table.Ump3, new Undo { TargetSeq = t.Server.State.LastSeq, FromSeq = from, Own = true });
        Assert.Equal((wasP, wasMarker), (At(t, P), At(t, Marker)));
        Assert.Equal(0, t.Server.Log[^1].UndoFrom);
        t.Ok(Table.Ump3, new Undo { TargetSeq = t.Server.State.LastSeq });
        Assert.Equal((new Vec2(5, 5), new Vec2(6, 2)), (At(t, P), At(t, Marker)));
    }

    [Fact]
    public void A_stretch_is_not_taken_back_over_what_someone_else_changed_since_nor_by_someone_else()
    {
        using var t = new Table();
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = P, Pos = new(4, 4) });
        var from = t.Server.State.LastSeq;
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = Marker, Pos = new(6, 2) });
        var to = t.Server.State.LastSeq;
        Assert.Equal("nothing to undo", t.Error(Table.Host, new Undo { TargetSeq = to, FromSeq = from, Own = true }));
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = P, Pos = new(1, 7) });
        Assert.StartsWith(Undo.StopsHere, t.Error(Table.Ump3, new Undo { TargetSeq = to, FromSeq = from, Own = true }));
        Assert.Equal((new Vec2(1, 7), new Vec2(6, 2)), (At(t, P), At(t, Marker)));
    }
}
