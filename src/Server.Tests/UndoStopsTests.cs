using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class UndoStopsTests
{
    const string U = Layers.Umpire;
    const string Foot = "p-000002", Flag = "p-000003";

    static long Do(Table t, long by, Command c)
    {
        t.Ok(by, c);
        return t.Server.Log[^1].Seq;
    }

    static MovePiece Move(string id, double x) => new() { Layer = U, Id = id, Pos = new(x, 0), RotDeg = 0 };

    [Fact]
    public void An_undo_is_refused_once_another_umpire_has_changed_the_same_piece_and_names_them()
    {
        using var t = new Table();
        var mine = Do(t, Table.Ump, Move(Foot, 10));
        Do(t, Table.Ump3, Move(Foot, 20));

        var why = t.Error(Table.Ump, new Undo { TargetSeq = mine });

        Assert.Equal(Undo.StopsHere + "Umpire Three has changed the same thing since", why);
        Assert.Equal(new Vec2(20, 0), t.Server.State.Find(U, Foot)!.Pos);
    }

    [Fact]
    public void What_others_did_to_other_pieces_or_said_stops_nothing()
    {
        using var t = new Table();
        var mine = Do(t, Table.Ump, Move(Foot, 10));
        Do(t, Table.Ump3, Move(Flag, 20));
        Do(t, Table.Ump3, new Say { Text = "moved the flag" });

        t.Ok(Table.Ump, new Undo { TargetSeq = mine });

        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, Foot)!.Pos);
    }

    [Fact]
    public void My_own_later_changes_to_the_piece_do_not_stop_my_undo()
    {
        using var t = new Table();
        var first = Do(t, Table.Ump, Move(Foot, 10));
        var second = Do(t, Table.Ump, Move(Foot, 20));

        t.Ok(Table.Ump, new Undo { TargetSeq = second });
        t.Ok(Table.Ump, new Undo { TargetSeq = first });

        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, Foot)!.Pos);
    }

    [Fact]
    public void Placing_a_piece_cannot_be_taken_back_once_someone_else_has_moved_it()
    {
        using var t = new Table();
        var placed = Do(t, Table.Ump, new SpawnPiece { Layer = U, Def = "inf", Pos = new(5, 5) });
        var id = ((SpawnPiece)t.Server.Log[^1]).Id!;
        Do(t, Table.Ump3, Move(id, 30));

        Assert.StartsWith(Undo.StopsHere, t.Error(Table.Ump, new Undo { TargetSeq = placed }));
        Assert.NotNull(t.Server.State.Find(U, id));
    }

    [Fact]
    public void A_setting_of_the_whole_table_stops_at_someone_else_s_later_setting_of_it()
    {
        using var t = new Table();
        var mine = Do(t, Table.Ump, new SetVisionRadius { Radius = 25 });
        Do(t, Table.Lead, new SetVisionRadius { Radius = 60 });

        Assert.StartsWith(Undo.StopsHere, t.Error(Table.Ump, new Undo { TargetSeq = mine }));
        Assert.Equal(60, t.Server.State.VisionRadius);
    }

    [Fact]
    public void A_change_names_the_things_it_touches()
    {
        Assert.Equal([Foot], Touched.By(Move(Foot, 1)));
        Assert.Equal(new[] { Foot, Flag }, Touched.By(new Batch { Items = { Move(Foot, 1), Move(Flag, 2) } }).Order());
        Assert.Equal(new[] { "b-000009", "d-000004" }, Touched.By(new Draw { Layer = U, Stroke = new Stroke { Id = "d-000004", Board = "b-000009" } }).Order());
        Assert.Equal(["kind:SetVisionRadius"], Touched.By(new SetVisionRadius { Radius = 3 }));
        Assert.Empty(Touched.By(new LoadSave { State = new GameState { ScenarioId = "scn" } }));
    }
}
