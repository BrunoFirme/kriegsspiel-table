using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class GameFlowTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void Package_store_loads_the_temp_pack()
    {
        using var t = new Table();
        Assert.NotNull(t.Store.Pack("testpack"));
        Assert.Equal(12, t.Store.Def("inf")!.Counters.Single(c => c.Name == "str").Max);
    }

    [Theory]
    [InlineData("str", 99, 12)]
    [InlineData("str", -5, 0)]
    [InlineData("str", 7, 7)]
    [InlineData("ammo", -10, -2)]
    [InlineData("ammo", 10, 3)]
    [InlineData("undefined-counter", 1234, 1234)]
    public void Counters_are_clamped_to_def_range(string name, int value, int expected)
    {
        using var t = new Table();
        t.Ok(Table.Ump, new SetCounter { Layer = U, Id = "p-000002", Name = name, Value = value });
        Assert.Equal(expected, t.Server.State.Find(U, "p-000002")!.Counters[name]);
        Assert.Equal(expected, ((SetCounter)t.Server.Log[^1]).Value);
    }

    [Fact]
    public void Counter_clamping_applies_inside_batches_and_to_spawned_pieces()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "inf", Pos = new(20, 20) });
        var id = ((SpawnPiece)t.Server.Log[^1]).Id!;
        t.Ok(Table.Ump, new Batch { Items = { new SetCounter { Layer = U, Id = id, Name = "str", Value = 50 } } });
        Assert.Equal(12, t.Server.State.Find(U, id)!.Counters["str"]);
        t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "not-in-any-pack" });
        var id2 = ((SpawnPiece)t.Server.Log[^1]).Id!;
        t.Ok(Table.Ump, new SetCounter { Layer = U, Id = id2, Name = "str", Value = 50 });
        Assert.Equal(50, t.Server.State.Find(U, id2)!.Counters["str"]);
    }

    [Fact]
    public void Undo_move_restores_position_and_attached_markers()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new Attach { Layer = U, Parent = "p-000002", Child = "p-000003" });
        var before = t.StateJson;
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new(10, 10), RotDeg = 0 });
        var moveSeq = t.Server.Log[^1].Seq;
        Assert.Equal(new Vec2(11, 10), t.Server.State.Find(U, "p-000003")!.Pos);

        var res = t.Ok(Table.Ump, new Undo { TargetSeq = moveSeq });
        Assert.Contains(res, o => o.PeerId == Table.Obs);
        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, "p-000002")!.Pos);
        Assert.Equal(new Vec2(1, 0), t.Server.State.Find(U, "p-000003")!.Pos);
        Assert.IsType<MovePiece>(t.Server.Log[^1]);
        Assert.Equal(3, t.Server.Log.Count);
        Assert.Equal(StripSeq(before), StripSeq(t.StateJson));

        Assert.Equal("nothing to undo", t.Error(Table.Ump, new Undo { TargetSeq = moveSeq }));
        Assert.Equal("nothing to undo", t.Error(Table.Ump, new Undo { TargetSeq = 999 }));
    }

    [Fact]
    public void Undo_walks_back_through_the_commands_one_at_a_time()
    {
        using var t = new Table();
        var seqs = new List<long>();
        foreach (var x in new[] { 10, 20, 30 })
        {
            t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new(x, 0), RotDeg = 0 });
            seqs.Add(t.Server.Log[^1].Seq);
        }
        Assert.Equal(new Vec2(30, 0), t.Server.State.Find(U, "p-000002")!.Pos);

        var mine = new Stack<long>(seqs);
        void Press()
        {
            var res = t.Ok(Table.Ump, new Undo { TargetSeq = mine.Pop() });
            foreach (var cm in res.Where(o => o.PeerId == Table.Ump).Select(o => o.Message).OfType<CommandMessage>())
                if (cm.Command.UndoOf == 0) mine.Push(cm.Command.Seq);
                else Assert.Equal(cm.Command.UndoOf, t.Server.Log[^1].UndoOf);
        }
        Press();
        Assert.Equal(new Vec2(20, 0), t.Server.State.Find(U, "p-000002")!.Pos);
        Press();
        Assert.Equal(new Vec2(10, 0), t.Server.State.Find(U, "p-000002")!.Pos);
        Press();
        Assert.Equal(new Vec2(0, 0), t.Server.State.Find(U, "p-000002")!.Pos);
        Assert.Empty(mine);
    }

    static string StripSeq(string stateJson)
    {
        var g = Json.Deserialize<GameState>(stateJson);
        g.LastSeq = 0;
        return Json.Serialize(g);
    }

    [Fact]
    public void Undo_delete_restores_the_same_piece_and_attachment()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new Attach { Layer = U, Parent = "p-000002", Child = "p-000003" });
        t.Ok(Table.Ump, new SetText { Layer = U, Id = "p-000003", Name = "note", Value = "hello" });
        var before = Json.Serialize(t.Server.State.Find(U, "p-000003"));
        t.Ok(Table.Ump, new DeletePiece { Layer = U, Id = "p-000003" });
        Assert.Null(t.Server.State.Find(U, "p-000003"));
        Assert.Empty(t.Server.State.Find(U, "p-000002")!.Attached);

        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Equal(before, Json.Serialize(t.Server.State.Find(U, "p-000003")));
        Assert.Equal(new[] { "p-000003" }, t.Server.State.Find(U, "p-000002")!.Attached);

        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Null(t.Server.State.Find(U, "p-000003"));
    }

    [Fact]
    public void Undo_of_erase_brings_back_the_same_stroke_id()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new Draw { Layer = U, Stroke = new Stroke { Points = { new(0, 0), new(2, 2) } } });
        var drawSeq = t.Server.Log[^1].Seq;
        var strokeId = ((Draw)t.Server.Log[^1]).Stroke.Id;
        t.Ok(Table.Ump, new EraseStroke { Layer = U, StrokeId = strokeId });
        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Contains(t.Server.State.DrawLayer(U), s => s.Id == strokeId);
        t.Ok(Table.Ump, new Undo { TargetSeq = drawSeq });
        Assert.DoesNotContain(t.Server.State.DrawLayer(U), s => s.Id == strokeId);
    }

    [Fact]
    public void Failed_undo_can_be_retried()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "inf" });
        var spawnSeq = t.Server.Log[^1].Seq;
        var id = ((SpawnPiece)t.Server.Log[^1]).Id!;
        t.Ok(Table.Ump, new DeletePiece { Layer = U, Id = id });
        var delSeq = t.Server.Log[^1].Seq;
        Assert.Equal("no such piece", t.Error(Table.Ump, new Undo { TargetSeq = spawnSeq }));
        t.Ok(Table.Ump, new Undo { TargetSeq = delSeq });
        t.Ok(Table.Ump, new Undo { TargetSeq = spawnSeq });
        Assert.Null(t.Server.State.Find(U, id));
    }

    [Fact]
    public void Observers_cannot_undo()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) });
        Assert.Contains("not allowed", t.Error(Table.Obs, new Undo { TargetSeq = t.Server.Log[^1].Seq }));
    }

    static void PlaySession(Table t)
    {
        t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "inf", Pos = new(5, 5), RotDeg = 45 });
        var a = ((SpawnPiece)t.Server.Log[^1]).Id!;
        t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "marker", Pos = new(6, 5) });
        var m = ((SpawnPiece)t.Server.Log[^1]).Id!;
        t.Ok(Table.Ump, new Attach { Layer = U, Parent = a, Child = m });
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = a, Pos = new(8, 2), RotDeg = 135 });
        t.Ok(Table.Ump, new SetCounter { Layer = U, Id = a, Name = "str", Value = 40 });
        t.Ok(Table.Ump, new SetText { Layer = U, Id = a, Name = "name", Value = "2nd" });
        t.Ok(Table.Ump2, new SetState { Layer = U, Id = a, State = 1 });
        t.Ok(Table.Ump2, new SetLabel { Layer = U, Id = a, Value = "Grenadiers" });
        t.Ok(Table.Ump2, new SetTint { Layer = U, Id = a, Value = "#00FF00" });
        t.Ok(Table.Ump2, new Draw { Layer = U, Stroke = new Stroke { Points = { new(0, 0), new(5, 5) } } });
        var stroke = ((Draw)t.Server.Log[^1]).Stroke.Id;
        t.Ok(Table.Ump2, new EraseStroke { Layer = U, StrokeId = stroke });
        t.Ok(Table.Ump3, new RollDice { Sides = 6, Count = 3, Label = "red fire" });
        t.Ok(Table.Ump, new RollDice { Sides = 20, Count = 1, Label = "ump" });
        t.Ok(Table.Ump, new AddSheet { Sheet = new SheetDef { Name = "Ref", Image = "sha2", Package = "testpack", Kind = "reference" } });
        var sheet = t.Server.State.Sheets[^1].Id;
        t.Ok(Table.Ump, new MoveSheet { Id = sheet, Pos = new(30, 30), RotDeg = -90 });
        t.Ok(Table.Ump, new LockSheet { Id = sheet, Locked = false });
        t.Ok(Table.Ump, new RemoveSheet { Id = "s-000001" });
        t.Ok(Table.Ump, new SetCalibration { UnitsPerMeter = 0.004 });
        t.Ok(Table.Ump, new SetClock { Turn = 1, GameTime = "1809-05-21T06:00", IncrementMinutes = 20 });
        t.Ok(Table.Ump, new AdvanceClock());
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Name = "North", Owner = "Umpire Three", Polygon = { new(0, 0), new(100, 0), new(100, 100), new(0, 100) } } });
        t.Ok(Table.Ump, new Batch
        {
            Items =
            {
                new SpawnPiece { Layer = U, Def = "inf", Pos = new(0, 9) },
                new MovePiece { Layer = U, Id = m, Pos = new(9, 9) },
                new Draw { Layer = U, Stroke = new Stroke { Points = { new(9, 9) } } },
            },
        });
        var batchSeq = t.Server.Log[^1].Seq;
        Assert.NotNull(t.Server.Handle(Table.Ump, new Batch { Items = { new SpawnPiece { Layer = U, Def = "inf" }, new MovePiece { Layer = U, Id = "nope" } } })
            .Select(o => o.Message).OfType<ErrorMessage>().SingleOrDefault());
        t.Ok(Table.Ump, new DeletePiece { Layer = U, Id = "p-000002" });
        t.Ok(Table.Ump, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        t.Ok(Table.Ump, new Undo { TargetSeq = batchSeq });
        t.Ok(Table.Ump, new RestorePiece { Layer = U, Piece = new PieceInstance { Id = "p-000077", Def = "inf" } });
        t.Ok(Table.Ump, new Detach { Layer = U, Child = m });
        t.Ok(Table.Ump, new Draw { Layer = U, Stroke = new Stroke { Points = { new(3, 3) } } });
    }

    [Fact]
    public void Replay_from_scenario_equals_live_state()
    {
        using var t = new Table();
        var scenarioJson = Json.Serialize(t.Scenario);
        PlaySession(t);
        Assert.Equal(scenarioJson, Json.Serialize(t.Scenario));
        var replayed = SaveLoader.Replay(Json.Deserialize<Scenario>(scenarioJson), t.Server.Log);
        Assert.Equal(t.StateJson, Json.Serialize(replayed));

        var again = SaveLoader.Replay(t.Scenario, t.Server.Log.Reverse().Select(Json.Clone));
        Assert.Equal(t.StateJson, Json.Serialize(again));
    }

    [Fact]
    public void Rejected_commands_leave_state_and_replay_unchanged()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) });
        var before = t.StateJson;
        Assert.Equal("no such piece", t.Error(Table.Ump, new Batch { Items = { new SpawnPiece { Layer = U, Def = "inf" }, new Draw { Layer = U }, new MovePiece { Layer = U, Id = "nope" } } }));
        Assert.Equal("no such piece", t.Error(Table.Ump, new MovePiece { Layer = U, Id = "nope" }));
        Assert.Equal(before, t.StateJson);
        Assert.Equal(t.StateJson, Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
    }

    [Fact]
    public void Ids_generated_after_replay_do_not_collide()
    {
        using var t = new Table();
        PlaySession(t);
        var g = SaveLoader.Replay(t.Scenario, t.Server.Log);
        var srv = new GameServer(g, t.Store);
        srv.Join(1, new JoinRequest { Name = "U" });
        srv.Handle(1, new SpawnPiece { Layer = U, Def = "inf" });
        srv.Handle(1, new Draw { Layer = U, Stroke = new Stroke() });
        srv.Handle(1, new AddSheet { Sheet = new SheetDef() });
        Assert.Equal(3, srv.Log.Count);
        var ids = g.Layers.SelectMany(kv => kv.Value).Select(p => p.Id)
            .Concat(g.Drawings.Values.SelectMany(d => d).Select(s => s.Id))
            .Concat(g.Sheets.Select(s => s.Id)).Concat(g.Zones.Select(z => z.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        using var t = new Table();
        PlaySession(t);
        var path = Path.Combine(t.Root, "saves", "game.kstable");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        t.Server.Dirty = true;
        t.Server.SaveTo(path);
        Assert.False(t.Server.Dirty);
        Assert.False(File.Exists(path + ".tmp"));

        var loaded = SaveLoader.Load(path, t.Store);
        Assert.Equal(t.StateJson, Json.Serialize(loaded.State));
        Assert.Equal(Json.Serialize(t.Server.Access), Json.Serialize(loaded.Access));
        Assert.Equal(t.Server.Created, loaded.Created);
        Assert.Empty(loaded.Log);
        Assert.Empty(Json.Load<SaveFile>(path).Commands);

        var jr = (JoinResult)loaded.Join(10, new JoinRequest { Token = t.Tokens[Table.Ump3], Protocol = ProtocolVersion.Current }).Single(o => o.PeerId == 10 && o.Message is JoinResult).Message;
        Assert.True(jr.Ok, jr.Error);
        Assert.Equal(Role.Umpire, jr.Role);
        Assert.Equal("Umpire Three", loaded.Peer(10)!.Name);
        Assert.True(((JoinResult)loaded.Join(11, new JoinRequest { Name = "new", Password = Table.Password, Protocol = ProtocolVersion.Current }).Single(o => o.PeerId == 11 && o.Message is JoinResult).Message).Waiting);

        foreach (var srv in new[] { t.Server, loaded })
        {
            srv.Join(1, new JoinRequest { Name = "Umpire One" });
            srv.Handle(1, new SpawnPiece { Layer = U, Def = "inf", Pos = new(2, 2) });
            srv.Handle(1, new Draw { Layer = U, Stroke = new Stroke { Points = { new(1, 1) } } });
        }
        Assert.Equal(t.StateJson, Json.Serialize(loaded.State));
    }

    [Fact]
    public void Load_replays_commands_newer_than_the_snapshot()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new(4, 4) });
        var early = t.Server.ToSave();
        PlaySession(t);
        var save = t.Server.ToSave();
        save.Snapshot = early.Snapshot;
        save.Commands = t.Server.Log.ToList();
        var path = Path.Combine(t.Root, "lagging.kstable");
        Json.Save(path, save);
        var loaded = SaveLoader.Load(path, t.Store);
        Assert.Equal(t.StateJson, Json.Serialize(loaded.State));
    }

    [Fact]
    public void Dice_results_are_in_range_and_server_decided()
    {
        using var t = new Table();
        var seen = new HashSet<int>();
        for (int i = 0; i < 300; i++)
        {
            t.Ok(Table.Ump3, new RollDice { Sides = 6, Count = 5, Label = "volley", Results = { 6, 6, 6, 6, 6, 6, 6 } });
            var rd = (RollDice)t.Server.Log[^1];
            Assert.Equal(5, rd.Results.Count);
            Assert.All(rd.Results, r => Assert.InRange(r, 1, 6));
            Assert.Equal("Umpire Three", rd.ActorName);
            seen.UnionWith(rd.Results);
        }
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, seen.OrderBy(x => x));

        t.Ok(Table.Ump, new RollDice { Sides = 1, Count = 0 });
        var low = (RollDice)t.Server.Log[^1];
        Assert.Equal(2, low.Sides);
        Assert.Single(low.Results);
        Assert.InRange(low.Results[0], 1, 2);

        t.Ok(Table.Ump, new RollDice { Sides = 5000, Count = 500 });
        var high = (RollDice)t.Server.Log[^1];
        Assert.Equal(1000, high.Sides);
        Assert.Equal(50, high.Results.Count);
        Assert.All(high.Results, r => Assert.InRange(r, 1, 1000));
    }

    [Fact]
    public void Loose_dice_go_to_umpires_only()
    {
        using var t = new Table();
        var res = t.Ok(Table.Ump3, new RollDice { Sides = 6, Count = 2, Label = "secret roll" });
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3 }, res.Select(o => o.PeerId).OrderBy(x => x));
        var sent = res.Select(o => ((CommandMessage)o.Message).Command).Cast<RollDice>().ToList();
        Assert.All(sent, s => Assert.Equal(((RollDice)t.Server.Log[^1]).Results, s.Results));

        var res2 = t.Ok(Table.Ump, new RollDice { Sides = 6, Count = 1, Label = "umpire roll" });
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3 }, res2.Select(o => o.PeerId).OrderBy(x => x));

        var obsView = Json.Serialize(Visibility.Filter(t.Server.State, t.Server.Peer(Table.Obs)!));
        Assert.DoesNotContain("secret roll", obsView);
        Assert.DoesNotContain("umpire roll", obsView);
        Assert.Contains("secret roll", Json.Serialize(Visibility.Filter(t.Server.State, t.Server.Peer(Table.Ump2)!)));
    }

    [Fact]
    public void Observer_view_after_a_session_is_the_board_without_zones_or_dice()
    {
        using var t = new Table();
        PlaySession(t);
        var view = Visibility.Filter(t.Server.State, t.Server.Peer(Table.Obs)!);
        Assert.Equal(Json.Serialize(t.Server.State.Layer(U)), Json.Serialize(view.Layer(U)));
        Assert.Equal(Json.Serialize(t.Server.State.DrawLayer(U)), Json.Serialize(view.DrawLayer(U)));
        Assert.Empty(view.Zones);
        Assert.Empty(view.Dice);
        var json = Json.Serialize(view);
        Assert.Contains("Grenadiers", json);
        Assert.DoesNotContain("North", json);
        Assert.DoesNotContain("red fire", json);
    }

    [Fact]
    public void Advancing_the_clock_records_the_whole_table_as_the_turn_ended()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new Vec2(5, 5), RotDeg = 0 });
        t.Ok(Table.Ump, new AdvanceClock());
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new Vec2(9, 9), RotDeg = 0 });
        t.Ok(Table.Ump, new SetClock { Turn = 5, GameTime = "1812-07-22T08:00", IncrementMinutes = 15 });
        t.Ok(Table.Ump, new SetClock { Turn = 2, GameTime = "1812-07-22T07:00", IncrementMinutes = 15 });

        Assert.Equal(2, t.Server.Turns.Count);
        Assert.Equal(0, t.Server.Turns[0].Turn);
        Assert.Equal(5, t.Server.Turns[0].Unpack().Find(U, "p-000002")!.Pos.X);
        Assert.Equal(1, t.Server.Turns[1].Turn);
        Assert.Equal(9, t.Server.Turns[1].Unpack().Find(U, "p-000002")!.Pos.X);
        Assert.Equal(9, t.Server.State.Find(U, "p-000002")!.Pos.X);

        var list = t.Server.TurnHistory(Table.Ump);
        var tl = Assert.IsType<TurnList>(list.Single().Message);
        Assert.Equal(new[] { 0, 1 }, tl.Turns.Select(x => x.Turn));
        var snap = Assert.IsType<TurnState>(t.Server.TurnSnapshot(Table.Ump, 0).Single().Message);
        Assert.Equal(5, snap.State.Find(U, "p-000002")!.Pos.X);
        Assert.IsType<ErrorMessage>(t.Server.TurnHistory(Table.Obs).Single().Message);
        Assert.IsType<ErrorMessage>(t.Server.TurnSnapshot(Table.Obs, 0).Single().Message);
        Assert.IsType<TurnList>(t.Server.TurnHistory(Table.Ump3).Single().Message);

        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ktable-turns-test.kstable");
        t.Server.SaveTo(file);
        var back = SaveLoader.Load(file, t.Store);
        Assert.Equal(2, back.Turns.Count);
        Assert.Equal(5, back.Turns[0].Unpack().Find(U, "p-000002")!.Pos.X);
        System.IO.File.Delete(file);
    }

    [Fact]
    public void A_die_thrown_for_real_keeps_the_face_it_landed_on_and_any_other_is_drawn_by_the_host()
    {
        using var t = new Table();
        string Spawn() { t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "die6", Pos = new Vec2(1, 1) }); return ((SpawnPiece)t.Server.Log[^1]).Id!; }
        string real = Spawn(), drawn = Spawn();
        t.Ok(Table.Ump, new SetPieceOption { Layer = U, Id = real, Name = PieceOptions.PhysicalRoll, Value = true });
        Assert.True(t.Server.State.Find(U, real)!.PhysicalRoll);
        var fromDrawn = new HashSet<int>();
        for (int i = 0; i < 40; i++)
        {
            t.Ok(Table.Ump, new RollDice { Layer = U, PieceIds = { real, drawn }, Results = { 4, 4 }, Physical = true });
            var rd = (RollDice)t.Server.Log[^1];
            Assert.Equal(4, rd.Results[0]);
            Assert.Equal(3, t.Server.State.Find(U, real)!.State);
            fromDrawn.Add(rd.Results[1]);
        }
        Assert.True(fromDrawn.Count > 1, "the die not thrown for real took the reported face every time");
        t.Ok(Table.Ump, new RollDice { Layer = U, PieceIds = { real }, Results = { 9 }, Physical = true });
        Assert.InRange(((RollDice)t.Server.Log[^1]).Results[0], 1, 6);
        t.Ok(Table.Ump, new SetPieceOption { Layer = U, Id = real, Name = PieceOptions.NoCollision, Value = true });
        Assert.True(t.Server.State.Find(U, real)!.NoCollision);
    }

    [Fact]
    public void A_die_on_the_table_is_rolled_by_the_server_and_shows_its_result_face()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "die6", Pos = new Vec2(1, 1) });
        var id = ((SpawnPiece)t.Server.Log[^1]).Id!;
        var seen = new HashSet<int>();
        for (int i = 0; i < 40; i++)
        {
            t.Ok(Table.Ump, new RollDice { Layer = U, PieceIds = { id, "no-such-piece" } });
            var rd = (RollDice)t.Server.Log[^1];
            Assert.Equal(new[] { id }, rd.PieceIds);
            Assert.Single(rd.Results);
            Assert.InRange(rd.Results[0], 1, 6);
            Assert.Equal(6, rd.Sides);
            Assert.Equal(rd.Results[0] - 1, t.Server.State.Find(U, id)!.State);
            seen.Add(rd.Results[0]);
        }
        Assert.True(seen.Count >= 3, "forty rolls of a d6 should show several faces");

        Assert.Contains(t.Server.Handle(Table.Obs, new RollDice { Layer = U, PieceIds = { id } }), o => o.Message is ErrorMessage);
        Assert.Contains(t.Server.Handle(Table.Obs, new RollDice { Sides = 6, Count = 2 }), o => o.Message is ErrorMessage);
        Assert.Contains(t.Ok(Table.Ump, new RollDice { Layer = U, PieceIds = { id } }), o => o.PeerId == Table.Obs);
    }
}
