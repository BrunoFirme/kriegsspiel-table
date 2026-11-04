using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class ReliabilityTests
{
    const string U = Layers.Umpire, P = "p-000002", M = "p-000003";

    static void Untouched(Table t, string before, long nextId, long seq)
    {
        Assert.Equal(before, t.StateJson);
        Assert.Equal(nextId, t.Server.State.NextId);
        Assert.Equal(seq, t.Server.State.LastSeq);
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = M, Pos = new(3, 3) });
    }

    [Fact]
    public void A_command_that_throws_as_it_applies_is_refused_and_the_table_is_as_it_was()
    {
        using var t = new Table();
        t.Server.State.Find(U, P)!.Counters = null!;
        var (before, nextId, seq) = (t.StateJson, t.Server.State.NextId, t.Server.State.LastSeq);
        var alone = t.Server.Handle(Table.Host, new SetCounter { Layer = U, Id = P, Name = "str", Value = 3 });
        Assert.IsType<ErrorMessage>(Assert.Single(alone).Message);
        Untouched(t, before, nextId, seq);

        (before, nextId, seq) = (t.StateJson, t.Server.State.NextId, t.Server.State.LastSeq);
        var batch = t.Server.Handle(Table.Host, new Batch { Items =
        {
            new SpawnPiece { Layer = U, Def = "marker", Pos = new(4, 4) },
            new MovePiece { Layer = U, Id = M, Pos = new(9, 9) },
            new SetCounter { Layer = U, Id = P, Name = "str", Value = 3 },
        } });
        Assert.Contains("SetCounter failed", Assert.IsType<ErrorMessage>(Assert.Single(batch).Message).Text);
        Untouched(t, before, nextId, seq);
    }

    [Fact]
    public void A_batch_item_that_used_to_throw_half_way_through_changes_nothing()
    {
        using var t = new Table();
        var g = t.Server.State.DeepClone();
        var before = Json.Serialize(g);
        var err = StateMachine.Apply(g, new Batch { Items =
        {
            new MovePiece { Layer = U, Id = P, Pos = new(5, 5) },
            new RollDice { Layer = U, PieceIds = null!, Results = { 3 } },
        } }, out _);
        Assert.NotNull(err);
        Assert.Equal(before, Json.Serialize(g));
    }

    [Fact]
    public void A_position_that_is_not_a_number_is_refused_and_the_table_still_saves()
    {
        using var t = new Table();
        var before = t.StateJson;
        Assert.Contains("not a number", t.Error(Table.Ump, new MovePiece { Layer = U, Id = P, Pos = new Vec2(double.NaN, 0) }));
        Assert.Contains("not a number",
            t.Error(Table.Ump, new Batch { Items = { new MovePiece { Layer = U, Id = M, Pos = new Vec2(1, 1) }, new SetScale { Layer = U, Id = P, Scale = double.PositiveInfinity } } }));
        Assert.Equal(before, t.StateJson);
        Assert.NotEmpty(t.Server.SaveBytes());
    }

    [Fact]
    public void A_script_that_divides_nought_by_nought_cannot_poison_the_save()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetScript { Layer = U, Id = P, Text = "function on_load() self.set_pos(0/0, 0) end" });
        Assert.True(double.IsFinite(t.Server.State.Find(U, P)!.Pos.X));
        Assert.NotEmpty(t.Server.SaveBytes());
        t.Ok(Table.Ump, new AdvanceClock());
        Assert.Single(t.Server.Turns);
        t.LetIn(40, "Late", Role.Umpire);
    }

    [Fact]
    public void A_batch_that_fails_half_way_leaves_the_table_as_it_was()
    {
        using var t = new Table();
        var before = t.StateJson;
        var err = t.Error(Table.Ump, new Batch { Items = { new MovePiece { Layer = U, Id = P, Pos = new Vec2(5, 5) }, new SetState { Layer = U, Id = "p-nothere", State = 1 } } });
        Assert.Contains("no such piece", err);
        Assert.Equal(before, t.StateJson);
    }

    [Fact]
    public void A_batch_applied_by_the_state_machine_is_all_or_nothing()
    {
        var g = GameState.FromScenario(Table.MakeScenario());
        var before = Json.Serialize(g);
        var err =
            StateMachine.Apply(g,
                new Batch { Items = { new MovePiece { Layer = U, Id = P, Pos = new Vec2(5, 5) }, new SetCounter { Layer = U, Id = M, Name = "str", Value = 3 }, new DeletePiece { Layer = U, Id = "p-nothere" } } },
                out _);
        Assert.NotNull(err);
        Assert.Equal(before, Json.Serialize(g));
    }

    [Fact]
    public void Deleting_a_unit_and_its_marker_together_works_when_the_unit_takes_its_marker_with_it()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetScript { Layer = U, Id = P, Text = $"function on_destroy() local m = board.find('{M}') if m then m.delete() end end" });
        var res = t.Server.Handle(Table.Ump, new Batch { Items = { new DeletePiece { Layer = U, Id = P }, new DeletePiece { Layer = U, Id = M } } });
        Assert.DoesNotContain(res.Select(o => o.Message), m => m is ErrorMessage);
        Assert.Null(t.Server.State.Find(U, P));
        Assert.Null(t.Server.State.Find(U, M));
        var view = GameState.FromScenario(t.Scenario);
        foreach (var c in t.Server.Log) Assert.Null(StateMachine.Apply(view, Json.Clone(c), out _));
        Assert.Equal(t.Server.State.Layer(U).Count, view.Layer(U).Count);
    }

    [Fact]
    public void Deleting_a_piece_that_is_not_there_is_refused_before_any_script_hears_of_it()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetGlobalScript { Text = "function on_destroy(p) game.say('gone') end" });
        Assert.Contains("no such piece", t.Error(Table.Ump, new DeletePiece { Layer = U, Id = "p-nothere" }));
    }

    [Fact]
    public void Undoing_the_first_change_of_a_counter_shows_the_kinds_default_again()
    {
        using var t = new Table();
        var spawn = t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "inf", Pos = new Vec2(4, 4) });
        var id = ((SpawnPiece)t.Server.Log[^1]).Id!;
        Assert.False(t.Server.State.Find(U, id)!.Counters.ContainsKey("str"));
        t.Ok(Table.Ump, new SetCounter { Layer = U, Id = id, Name = "str", Value = 7 });
        Assert.Equal(7, t.Server.State.Find(U, id)!.Counters["str"]);
        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.False(t.Server.State.Find(U, id)!.Counters.ContainsKey("str"));
        t.Ok(Table.Ump, new SetCounter { Layer = U, Id = id, Name = "str", Value = 5 });
        t.Ok(Table.Ump, new SetCounter { Layer = U, Id = id, Name = "str", Value = 2 });
        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Equal(5, t.Server.State.Find(U, id)!.Counters["str"]);
    }

    [Fact]
    public void An_umpire_kept_to_a_zone_cannot_undo_a_piece_out_of_someone_elses()
    {
        using var t = new Table();
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Owner = "Umpire Three", Polygon = { new(-10, -10), new(10, -10), new(10, 10), new(-10, 10) } } });
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Owner = "Umpire Two", Polygon = { new(20, -10), new(40, -10), new(40, 10), new(20, 10) } } });
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = P, Pos = new Vec2(5, 5) });
        var mine = t.Server.Log[^1].Seq;
        t.Ok(Table.Lead, new MovePiece { Layer = U, Id = P, Pos = new Vec2(30, 0) });
        Assert.Contains("cannot be taken back", t.Error(Table.Ump3, new Undo { TargetSeq = mine }));
        Assert.Equal(30, t.Server.State.Find(U, P)!.Pos.X);
    }

    [Fact]
    public void A_batch_is_allowed_only_as_far_as_every_change_in_it_is()
    {
        using var t = new Table();
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Owner = "Umpire Three", Polygon = { new(-10, -10), new(10, -10), new(10, 10), new(-10, 10) } } });
        var zone = t.Server.State.Zones[0].Id;
        Assert.Contains("not allowed", t.Error(Table.Ump3, new Batch { Items = { new RemoveZone { Id = zone } } }));
        Assert.Contains("not allowed", t.Error(Table.Ump3, new Batch { Items = { new SetGlobalScript { Text = "x = 1" } } }));
        Assert.Contains("not allowed", t.Error(Table.Ump3, new Batch { Items = { new SetWidgets { Layer = U, Id = P } } }));
        Assert.Contains("not allowed", t.Error(Table.Lead, new Batch { Items = { new RestoreTurn { Index = 0 } } }));
        Assert.Single(t.Server.State.Zones);
        t.Ok(Table.Ump3, new Batch { Items = { new MovePiece { Layer = U, Id = P, Pos = new Vec2(1, 1) }, new MovePiece { Layer = U, Id = M, Pos = new Vec2(2, 2) } } });
    }

    [Fact]
    public void Undoing_the_end_of_a_turn_takes_its_record_back_too()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new AdvanceClock());
        Assert.Single(t.Server.Turns);
        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Empty(t.Server.Turns);
        Assert.Equal(0, t.Server.State.Clock.Turn);
        t.Ok(Table.Ump, new AdvanceClock());
        Assert.Single(t.Server.Turns);
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = P, Pos = new Vec2(3, 3) });
        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Single(t.Server.Turns);
    }

    [Fact]
    public void Undoing_a_redraw_puts_the_old_lettering_back_on_the_state_it_was_drawn_on()
    {
        var g = GameState.FromScenario(Table.MakeScenario());
        var p = g.Find(U, P)!;
        p.Widgets.Add(new Widget { Kind = "label", Label = "1st Bn" });
        p.DrawnOnState = 0;
        Assert.Null(StateMachine.Apply(g, new Batch { Items = { new SetState { Layer = U, Id = P, State = 2 }, new SetWidgets { Layer = U, Id = P, Widgets = { new Widget { Kind = "label", Label = "broken" } } } } },
            out var inverse));
        Assert.Equal(2, p.DrawnOnState);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Equal(0, p.State);
        Assert.Equal("1st Bn", Assert.Single(p.Widgets).Label);
        Assert.Equal(0, p.DrawnOnState);
    }

    [Fact]
    public void A_co_umpire_following_only_what_it_is_sent_ends_with_the_servers_table()
    {
        using var t = new Table(join: false);
        t.HostJoins("Umpire One");
        t.Server.Scripts.Load();
        var jr = t.LetIn(Table.Ump3, "Umpire Three", Role.Umpire);
        var view = jr.View!;
        void Follow(List<Outgoing> outs)
        {
            foreach (var o in outs.Where(o => o.PeerId == Table.Ump3))
                if (o.Message is CommandMessage cm)
                {
                    var c = Json.Clone(cm.Command);
                    Assert.Null(StateMachine.Apply(view, c, out _));
                    if (c.Seq > view.LastSeq) view.LastSeq = c.Seq;
                }
        }
        List<Outgoing> Do(Command c)
        {
            var res = t.Server.Handle(Table.Ump, c);
            Follow(res);
            return res;
        }

        Do(new SetScript { Layer = U, Id = P, Text = "function on_load() self.button({ label = 'go', x = 0, z = 0, on_click = 'go' }) end\nfunction go() self.set_counter('str', 3) end" });
        Do(new MovePiece { Layer = U, Id = P, Pos = new Vec2(4, 4) });
        Do(new Batch { Items = { new MovePiece { Layer = U, Id = M, Pos = new Vec2(6, 6) }, new SetState { Layer = U, Id = M, State = 1 } } });
        Do(new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Do(new ClickWidget { Layer = U, Id = P, Index = 1 });
        Do(new SetCounter { Layer = U, Id = P, Name = "ammo", Value = 2 });
        Do(new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Do(new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(9, 9) });
        Do(new Attach { Layer = U, Parent = P, Child = M });
        Do(new MovePiece { Layer = U, Id = P, Pos = new Vec2(1, 2) });
        Do(new AdvanceClock());
        Do(new DeletePiece { Layer = U, Id = M });
        Do(new AdvanceClock());
        var res = t.Server.LoadSave(t.Server.Turns[0].Unpack(), new List<TurnRecord>(), "Turn 0");
        Follow(res);
        Do(new Draw { Layer = U, Stroke = new Stroke { Points = { new(0, 0), new(2, 2) } } });

        Assert.Equal(3, t.Server.State.Find(U, P)!.Counters["str"]);
        Assert.Equal(Json.Serialize(Visibility.Filter(t.Server.State, t.Server.Peer(Table.Ump3)!)), Json.Serialize(view));
    }

    [Fact]
    public void A_peer_whose_copy_disagrees_is_sent_the_table_it_may_see()
    {
        using var t = new Table();
        var umpire = Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(Table.Ump3)).Message);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(umpire.View));
        var watcher = Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(Table.Obs)).Message);
        Assert.Equal(Json.Serialize(Visibility.Filter(t.Server.State, t.Server.Peer(Table.Obs)!)), Json.Serialize(watcher.View));
        Assert.Empty(t.Server.Resync(99));
    }

    [Fact]
    public void A_turn_record_holds_what_the_scripts_held_when_the_turn_ended()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetScript { Layer = U, Id = P, Text = """
            local clicks = 0
            function on_load(saved) clicks = tonumber(saved) or 0; self.button{ label = "+", x = 0, z = 0, w = 0.3, h = 0.3, on_click = "add" } end
            function add() clicks = clicks + 1 end
            function on_save() return tostring(clicks) end
            """ });
        t.Server.Scripts.SaveAll();
        t.Ok(Table.Ump, new ClickWidget { Layer = U, Id = P, Index = 1 });
        t.Ok(Table.Ump, new ClickWidget { Layer = U, Id = P, Index = 1 });
        t.Ok(Table.Ump, new AdvanceClock());
        Assert.Equal("2", t.Server.Turns[^1].Unpack().Find(U, P)!.ScriptState);
    }

    [Fact]
    public void Board_ids_are_cleared_by_the_id_counter()
    {
        var g = new GameState();
        g.Boards.Add(new BoardDef { Id = "b-000040", Name = "Old" });
        g.EnsureNextId();
        Assert.True(g.NextId > 40);
    }
}
