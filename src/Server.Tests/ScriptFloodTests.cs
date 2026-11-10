using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class ScriptFloodTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";
    const double Frame = 1 / 60.0;

    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static List<Outgoing> SetScript(Table t, string script) => t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = script });

    static IEnumerable<Say> ErrorLines(IEnumerable<Command> log) => Flat(log).OfType<Say>().Where(s => s.Text.StartsWith("Script error"));

    [Fact]
    public void An_on_update_that_fails_every_frame_is_told_once_then_switched_off()
    {
        using var t = new Table();
        SetScript(t, "function on_update() local x = nil; x.y = 1 end");
        var before = t.Server.Log.Count;
        for (int i = 1; i <= 1000; i++) t.Server.Scripts.Tick(i * Frame);
        var made = t.Server.Log.Skip(before).ToList();
        var lines = ErrorLines(made).Select(s => s.Text).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("attempt to index a nil value", lines[0]);
        Assert.Contains("Infantry (p-000002)", lines[1]);
        Assert.Contains("the script is off until Save and reload", lines[1]);
        Assert.Equal(2, made.Count);
        Assert.Equal("stopped", Assert.Single(t.Server.Scripts.Describe(), s => s.Id == P).Status);
    }

    [Fact]
    public void An_on_update_that_fails_now_and_then_keeps_running()
    {
        using var t = new Table();
        SetScript(t, "n = 0 function on_update() n = n + 1 if n % 120 == 0 then local x = nil; x.y = 1 end end");
        var before = t.Server.Log.Count;
        for (int i = 1; i <= 600; i++) t.Server.Scripts.Tick(i * Frame);
        Assert.Equal(5, ErrorLines(t.Server.Log.Skip(before)).Count());
        Assert.Equal("error", Assert.Single(t.Server.Scripts.Describe(), s => s.Id == P).Status);
    }

    [Fact]
    public void A_seats_changes_stay_undoable_however_much_a_script_changes_after_them()
    {
        using var t = new Table();
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = Marker, Pos = new(5, 5) });
        var moved = t.Server.Log[^1].Seq;
        SetScript(t, "function on_update() self.set_counter('str', self.counters.str == 8 and 7 or 8) game.say('tick') end");
        for (int i = 1; i <= GameServer.RememberCommands * 2; i++) t.Server.Scripts.Tick(i * Frame);
        Assert.True(t.Server.Log[0].Seq > moved);
        t.Ok(Table.Host, new Undo { TargetSeq = moved });
        Assert.Equal(new Vec2(1, 0), t.Server.State.Find(U, Marker)!.Pos);
    }

    const string DeletesMarkerLater = "function on_load(s) wait.seconds(1, function() board.find('p-000003').delete() end) wait.seconds(2, function() board.find('p-000002').move(4, 4) end) end";

    [Fact]
    public void A_timer_made_delete_can_be_taken_back_by_the_host()
    {
        using var t = new Table();
        SetScript(t, DeletesMarkerLater);
        t.Server.Scripts.Tick(0.1);
        t.Server.Scripts.Tick(1.5);
        Assert.Null(t.Server.State.Find(U, Marker));
        var deleted = t.Server.Log[^1];
        Assert.Equal(0, deleted.Actor);
        t.Ok(Table.Host, new Undo { TargetSeq = deleted.Seq, FromSeq = deleted.Seq });
        Assert.NotNull(t.Server.State.Find(U, Marker));
        Assert.Equal(deleted.Seq, t.Server.Log[^1].UndoOf);
        Assert.Equal(deleted.Seq, t.Server.Log[^1].UndoFrom);
    }

    [Fact]
    public void One_undo_takes_back_every_change_scripts_made_in_a_stretch_and_a_redo_puts_them_back()
    {
        using var t = new Table();
        SetScript(t, DeletesMarkerLater);
        var from = t.Server.State.LastSeq + 1;
        t.Server.Scripts.Tick(0.1);
        t.Server.Scripts.Tick(1.5);
        t.Server.Scripts.Tick(2.5);
        Assert.Null(t.Server.State.Find(U, Marker));
        Assert.Equal(new Vec2(4, 4), t.Server.State.Find(U, P)!.Pos);
        t.Ok(Table.Lead, new Undo { TargetSeq = t.Server.State.LastSeq, FromSeq = from });
        Assert.NotNull(t.Server.State.Find(U, Marker));
        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, P)!.Pos);
        t.Ok(Table.Lead, new Undo { TargetSeq = t.Server.State.LastSeq });
        Assert.Null(t.Server.State.Find(U, Marker));
        Assert.Equal(new Vec2(4, 4), t.Server.State.Find(U, P)!.Pos);
        Assert.Equal("nothing to undo", t.Error(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq - 1, FromSeq = from }));
    }

    [Fact]
    public void A_plain_umpire_takes_back_only_their_own_changes()
    {
        using var t = new Table();
        SetScript(t, DeletesMarkerLater);
        var mine = t.Server.State.LastSeq;
        t.Server.Scripts.Tick(1.5);
        var deleted = t.Server.State.LastSeq;
        Assert.Equal("that change is not yours to take back", t.Error(Table.Ump3, new Undo { TargetSeq = deleted, FromSeq = deleted }));
        Assert.Equal("that change is not yours to take back", t.Error(Table.Ump3, new Undo { TargetSeq = mine }));
        Assert.Equal("that change is not yours to take back", t.Error(Table.Lead, new Undo { TargetSeq = mine }));
        Assert.Null(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void A_script_redrawing_itself_is_not_a_step_to_take_back()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) wait.seconds(1, function() self.clear_widgets() self.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = 'go', on_click = 'go' } end) end function go() end");
        var from = t.Server.State.LastSeq + 1;
        t.Server.Scripts.Tick(1.5);
        Assert.True(t.Server.State.LastSeq >= from);
        Assert.Equal("nothing to undo", t.Error(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq, FromSeq = from }));
    }

    [Fact]
    public void What_a_script_does_on_load_after_a_save_is_loaded_is_the_hosts_to_take_back()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) if board.find('p-000003') then board.find('p-000003').delete() end end");
        Assert.Null(t.Server.State.Find(U, Marker));
        t.Ok(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq });
        Assert.NotNull(t.Server.State.Find(U, Marker));
        t.Ok(Table.Host, new AdvanceClock());
        t.Server.LoadSave(t.Server.Turns[0].Unpack(), t.Server.Turns.ToList(), "Turn 1");
        Assert.Null(t.Server.State.Find(U, Marker));
        var onLoad = t.Server.Log[^1];
        Assert.Equal(Table.Host, onLoad.Actor);
        t.Ok(Table.Host, new Undo { TargetSeq = onLoad.Seq });
        Assert.NotNull(t.Server.State.Find(U, Marker));
    }
}
