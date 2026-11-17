using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Server.Tests;

public class BodyTests
{
    const long Ana = 41;
    const string U = Layers.Umpire, Body = "p-000002";

    static Table WithAnaWalking()
    {
        var t = new Table();
        t.LetIn(Ana, "Ana", Role.Player);
        t.Server.Bodies[Body] = "Ana";
        return t;
    }

    static MovePiece Step(double x) => new() { Layer = U, Id = Body, Pos = new(x, 0) };

    static SpawnPiece? Seen(List<Outgoing> outs, long peer) =>
        outs.Where(o => o.PeerId == peer).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command).OfType<SpawnPiece>().FirstOrDefault();

    [Fact]
    public void A_player_who_wore_a_body_cannot_move_that_piece_once_a_save_is_loaded()
    {
        using var t = WithAnaWalking();
        int dropped = 0;
        t.Server.BodiesDropped = () => dropped++;
        t.Ok(Ana, Step(3));
        t.Server.LoadSave(t.Server.State.DeepClone(), new List<TurnRecord>(), "Loaded");
        Assert.Empty(t.Server.Bodies);
        Assert.Equal(1, dropped);
        Assert.Contains("not allowed", t.Error(Ana, Step(5)));
        Assert.Equal(new Vec2(3, 0), t.Server.State.Find(U, Body)!.Pos);
    }

    [Fact]
    public void A_player_who_wore_a_body_cannot_move_that_piece_once_a_turn_is_restored()
    {
        using var t = WithAnaWalking();
        int dropped = 0;
        t.Server.BodiesDropped = () => dropped++;
        t.Ok(Table.Host, new AdvanceClock());
        t.Ok(Ana, Step(3));
        Assert.Equal(0, dropped);
        t.Server.LoadSave(t.Server.Turns[0].Unpack(), new List<TurnRecord>(), "Turn 1");
        Assert.Empty(t.Server.Bodies);
        Assert.Equal(1, dropped);
        Assert.Contains("not allowed", t.Error(Ana, Step(5)));
        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, Body)!.Pos);
    }

    [Fact]
    public void A_body_taken_again_after_a_load_is_the_players_to_move()
    {
        using var t = WithAnaWalking();
        t.Server.LoadSave(t.Server.State.DeepClone(), new List<TurnRecord>(), "Loaded");
        t.Server.Bodies[Body] = "Ana";
        t.Ok(Ana, Step(4));
        Assert.Equal(new Vec2(4, 0), t.Server.State.Find(U, Body)!.Pos);
    }

    [Fact]
    public void A_piece_made_for_no_one_is_on_the_table_for_every_umpire_and_in_the_save_and_is_nobodys_to_take_back()
    {
        using var t = new Table();
        var outs = t.Server.HandleForNoOne(new SpawnPiece { Layer = U, Def = "marker", Pos = new(7, 7) });
        Assert.DoesNotContain(outs, o => o.Message is ErrorMessage);
        var made = Seen(outs, Table.Host)!;
        Assert.Equal(GameServer.NoOne, made.Actor);
        Assert.False(string.IsNullOrEmpty(made.Id));
        Assert.NotNull(Seen(outs, Table.Lead));
        Assert.NotNull(Seen(outs, Table.Ump3));
        Assert.Equal(new Vec2(7, 7), t.Server.State.Find(U, made.Id!)!.Pos);
        Assert.NotNull(t.Server.ToSave().Snapshot.Find(U, made.Id!));
        Assert.Equal("nothing to undo", t.Error(Table.Host, new Undo { TargetSeq = made.Seq }));
        Assert.Equal("nothing to undo", t.Error(Table.Lead, new Undo { TargetSeq = made.Seq, FromSeq = made.Seq }));
        Assert.NotNull(t.Server.State.Find(U, made.Id!));
    }

    [Fact]
    public void A_piece_made_for_no_one_is_taken_off_for_no_one_or_by_any_umpire()
    {
        using var t = new Table();
        string First() => Seen(t.Server.HandleForNoOne(new SpawnPiece { Layer = U, Def = "marker" }), Table.Host)!.Id!;
        string one = First(), two = First();
        var gone = t.Server.HandleForNoOne(new DeletePiece { Layer = U, Id = one });
        Assert.DoesNotContain(gone, o => o.Message is ErrorMessage);
        Assert.Null(t.Server.State.Find(U, one));
        Assert.Equal("nothing to undo", t.Error(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq }));
        t.Ok(Table.Ump3, new DeletePiece { Layer = U, Id = two });
        Assert.Null(t.Server.State.Find(U, two));
    }

    [Fact]
    public void A_change_for_no_one_leaves_the_hosts_own_last_change_the_one_the_host_takes_back()
    {
        using var t = new Table();
        var moved = t.Ok(Table.Host, new MovePiece { Layer = U, Id = Body, Pos = new(2, 0) });
        long seq = moved.Select(o => o.Message).OfType<CommandMessage>().First().Command.Seq;
        string body = Seen(t.Server.HandleForNoOne(new SpawnPiece { Layer = U, Def = "marker" }), Table.Host)!.Id!;
        t.Ok(Table.Host, new Undo { TargetSeq = seq });
        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, Body)!.Pos);
        Assert.NotNull(t.Server.State.Find(U, body));
    }
}
