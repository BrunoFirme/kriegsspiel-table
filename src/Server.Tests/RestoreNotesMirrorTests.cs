using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class RestoreNotesMirrorTests
{
    const string U = Layers.Umpire;
    static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    static string TempRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-restore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        return r;
    }

    static SaveStore Store(string root) => new(Path.Combine(root, "live"), Path.Combine(root, "history"), Path.Combine(root, "trash"));

    static (Table t, string id) TwoTurnsWithAMove()
    {
        var t = new Table();
        var id = t.Server.State.Layer(U)[0].Id;
        var p = t.Server.State.Find(U, id)!;
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = id, Pos = new Vec2(5, 5), RotDeg = p.RotDeg });
        t.Ok(Table.Ump, new AdvanceClock());
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = id, Pos = new Vec2(9, 9), RotDeg = p.RotDeg });
        t.Ok(Table.Ump, new AdvanceClock());
        t.Ok(Table.Ump, new MovePiece { Layer = U, Id = id, Pos = new Vec2(13, 13), RotDeg = p.RotDeg });
        return (t, id);
    }

    static List<Outgoing> LoadTurn(Table t, int index) => t.Server.LoadSave(t.Server.Turns[index].Unpack(), t.Server.Turns.Take(index).ToList(), "Turn " + index);

    [Fact]
    public void A_loaded_save_replaces_the_table_for_everyone_each_with_their_view_and_brings_its_own_turns()
    {
        var (t, id) = TwoTurnsWithAMove();
        using var _ = t;
        var seqBefore = t.Server.State.LastSeq;
        var outs = LoadTurn(t, 0);
        var p = t.Server.State.Find(U, id)!;
        Assert.Equal(5, p.Pos.X);
        Assert.Equal(0, t.Server.State.Clock.Turn);
        Assert.Empty(t.Server.Turns);
        Assert.True(t.Server.State.LastSeq > seqBefore);
        var toObs = outs.Single(o => o.PeerId == Table.Obs).Message as CommandMessage;
        var lsO = Assert.IsType<LoadSave>(toObs!.Command);
        Assert.Equal(5, lsO.State!.Find(U, id)!.Pos.X);
        Assert.Empty(lsO.State.Zones);
        var toUmp2 = outs.Single(o => o.PeerId == Table.Ump2).Message as CommandMessage;
        Assert.Equal(5, Assert.IsType<LoadSave>(toUmp2!.Command).State!.Find(U, id)!.Pos.X);
        t.Ok(Table.Ump, new AdvanceClock());
        Assert.Single(t.Server.Turns);
        Assert.Equal(0, t.Server.Turns[0].Turn);
        var sp = t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(1, 1) });
        var spawned = (SpawnPiece)((CommandMessage)sp.First(o => o.PeerId == Table.Ump).Message).Command;
        Assert.Single(t.Server.State.Layer(U), x => x.Id == spawned.Id);
    }

    [Fact]
    public void No_one_at_the_table_sends_a_restore_or_a_load_only_the_host_s_own_saves_load()
    {
        var (t, _) = TwoTurnsWithAMove();
        using var _1 = t;
        foreach (var who in new[] { Table.Host, Table.Lead, Table.Ump3, Table.Obs })
        {
            Assert.Contains("not allowed", t.Error(who, new RestoreTurn { Index = 0 }));
            Assert.Contains("not allowed", t.Error(who, new LoadSave { Name = "x", State = new GameState() }));
        }
        Assert.Equal(2, t.Server.Turns.Count);
    }

    [Fact]
    public void A_load_is_not_undone_by_ctrl_z_and_its_table_is_what_is_saved()
    {
        var (t, id) = TwoTurnsWithAMove();
        using var _ = t;
        var loadSeq = t.Server.State.LastSeq + 1;
        LoadTurn(t, 1);
        Assert.Equal(9, t.Server.State.Find(U, id)!.Pos.X);
        Assert.Contains("nothing to undo", t.Error(Table.Ump, new Undo { TargetSeq = loadSeq }));
        Assert.Contains("nothing to undo", t.Error(Table.Ump, new Undo { TargetSeq = loadSeq - 1 }));
        var path = Path.Combine(TempRoot(), "g.kstable");
        t.Server.SaveTo(path);
        var back = SaveLoader.Load(path, t.Store);
        Assert.Equal(9, back.State.Find(U, id)!.Pos.X);
        Assert.Single(back.Turns);
    }

    [Fact]
    public void Notes_on_pieces_travel_with_the_board()
    {
        using var t = new Table();
        var id = t.Server.State.Layer(U)[0].Id;
        var outs = t.Ok(Table.Ump, new SetNote { Layer = U, Id = id, Value = "Fresh; ammunition short" });
        Assert.Equal("Fresh; ammunition short", t.Server.State.Find(U, id)!.Note);
        Assert.Contains(outs, o => o.PeerId == Table.Ump2 && ((CommandMessage)o.Message).Command is SetNote);
        Assert.Contains(outs, o => o.PeerId == Table.Obs && ((CommandMessage)o.Message).Command is SetNote);
        var obsView = Visibility.Filter(t.Server.State, t.Server.Peer(Table.Obs)!);
        Assert.Equal("Fresh; ammunition short", obsView.Find(U, id)!.Note);
        var del = t.Ok(Table.Ump, new DeletePiece { Layer = U, Id = id });
        var undo = t.Ok(Table.Ump, new Undo { TargetSeq = ((CommandMessage)del.First(o => o.PeerId == Table.Ump).Message).Command.Seq });
        var toObs = (RestorePiece)((CommandMessage)undo.Single(o => o.PeerId == Table.Obs).Message).Command;
        Assert.Equal("Fresh; ammunition short", toObs.Piece.Note);
        Assert.Equal("Fresh; ammunition short", t.Server.State.Find(U, id)!.Note);
        t.Ok(Table.Ump, new SetNote { Layer = U, Id = id, Value = "   " });
        Assert.Null(t.Server.State.Find(U, id)!.Note);
        t.Ok(Table.Ump, new SetNote { Layer = U, Id = id, Value = "kept" });
        var path = Path.Combine(TempRoot(), "n.kstable");
        t.Server.SaveTo(path);
        Assert.Equal("kept", SaveLoader.Load(path, t.Store).State.Find(U, id)!.Note);
    }

    [Fact]
    public void Text_lines_the_def_does_not_declare_are_kept_too()
    {
        using var t = new Table();
        var id = t.Server.State.Layer(U)[0].Id;
        t.Ok(Table.Ump, new SetText { Layer = U, Id = id, Name = "Orders", Value = "hold the bridge" });
        t.Ok(Table.Ump, new SetLabel { Layer = U, Id = id, Value = "1st Bn" });
        var path = Path.Combine(TempRoot(), "t.kstable");
        t.Server.SaveTo(path);
        var p = SaveLoader.Load(path, t.Store).State.Find(U, id)!;
        Assert.Equal("hold the bridge", p.Text["Orders"]);
        Assert.Equal("1st Bn", p.Label);
    }

    [Fact]
    public void A_co_umpires_mirror_opens_as_the_same_game_with_the_same_access_list_and_turns()
    {
        using var t = new Table();
        var saves = Store(TempRoot());
        var ump2 = t.Server.Peer(Table.Ump2)!;
        var view = Visibility.Filter(t.Server.State, ump2);
        var log = new List<Command>();
        var turns = new List<TurnRecord>();
        void Seen(List<Outgoing> outs)
        {
            foreach (var o in outs.Where(o => o.PeerId == Table.Ump2))
                if (o.Message is CommandMessage cm)
                {
                    if (SaveFile.EndsTurn(view, cm.Command)) turns.Add(SaveFile.RecordOf(view, T0));
                    Assert.Null(StateMachine.Apply(view, Json.Clone(cm.Command), out _));
                    view.LastSeq = cm.Command.Seq;
                    log.Add(cm.Command);
                }
        }
        var id = t.Server.State.Layer(U)[0].Id;
        Seen(t.Ok(Table.Ump, new MovePiece { Layer = U, Id = id, Pos = new Vec2(7, 7), RotDeg = 0 }));
        Seen(t.Ok(Table.Ump, new AdvanceClock()));
        Seen(t.Ok(Table.Ump, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(2, 2) }));
        Seen(t.Ok(Table.Ump, new SetNote { Layer = U, Id = id, Value = "note" }));

        var mirror = SaveFile.Mirror(view, t.Server.Access, t.Server.Created, turns, "Umpire One at 10.0.0.2");
        var path = saves.MirrorPath(t.Server.State.ScenarioId);
        Json.Save(path, mirror);
        Assert.StartsWith(Path.Combine(saves.HistoryDir, "mirrors"), saves.CheckpointDir(path));
        Assert.Single(saves.Mirrors());

        var back = SaveLoader.Load(path, t.Store);
        Assert.Equal(Table.Password, back.Access.Password);
        Assert.Equal(3, back.Access.Approved.Count);
        Assert.Equal(t.Server.Created, back.Created);
        Assert.Equal(7, back.State.Find(U, id)!.Pos.X);
        Assert.Equal("note", back.State.Find(U, id)!.Note);
        Assert.Equal(t.Server.State.Layer(U).Count, back.State.Layer(U).Count);
        Assert.Single(back.Turns);
        Assert.Equal(0, back.Turns[0].Turn);
        Assert.Equal(1, back.State.Clock.Turn);
        back.Join(1, new JoinRequest { Name = "Rescuer" });
        var sp = back.Handle(1, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(3, 3) });
        var newId = ((SpawnPiece)((CommandMessage)sp.Single().Message).Command).Id;
        var again = Table.JoinResultOf(back.Join(9, new JoinRequest { Token = t.Tokens[Table.Obs], Protocol = ProtocolVersion.Current }), 9, ok: true);
        Assert.Equal(Role.Observer, again.Role);
        Assert.Equal("Watcher", back.Peer(9)!.Name);
        Assert.Single(back.State.Layer(U), x => x.Id == newId);
        var entry = saves.TrashMirror(path, T0);
        Assert.Empty(saves.Mirrors());
        var e = Assert.Single(saves.TrashEntries());
        Assert.Equal("mirror", e.Kind);
        saves.Restore(e, Path.Combine(saves.LiveDir, "..", "packages"), T0.AddMinutes(1));
        Assert.Single(saves.Mirrors());
        Assert.Empty(saves.TrashEntries());
    }

    [Fact]
    public void Trash_entries_are_listed_and_restored_where_they_came_from()
    {
        var root = TempRoot();
        var saves = Store(root);
        var packages = Path.Combine(root, "packages");
        Json.Save(Path.Combine(packages, "scenarios", "scn", "scenario.json"), new Scenario { Id = "scn", Name = "S" });
        Json.Save(Path.Combine(packages, "mypack", "pack.json"), new PackManifest { Id = "mypack", Name = "P" });
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn", Snapshot = new GameState { Clock = new GameClock { Turn = 4 } } });
        saves.Checkpoint(live, "turn", 3, T0);

        saves.TrashGame("scn", T0);
        Json.Save(live, new SaveFile { ScenarioId = "scn", Snapshot = new GameState { Clock = new GameClock { Turn = 0 } } });
        saves.TrashPack(packages, "mypack", T0.AddMinutes(1));
        saves.TrashScenario(packages, "scn", T0.AddMinutes(2));

        var entries = saves.TrashEntries();
        Assert.Equal(new[] { "scenario", "pack", "game" }, entries.Select(e => e.Kind));
        Assert.Equal(new[] { "scn", "mypack", "scn" }, entries.Select(e => e.Id));
        Assert.Equal(SaveStore.TrashDays, entries[0].DaysLeft(T0.AddMinutes(2)));

        Directory.Delete(saves.LiveDir, true);
        File.WriteAllText(saves.LiveDir, "in the way");
        Assert.ThrowsAny<IOException>(() => saves.Restore(entries[0], packages, T0.AddHours(1)));
        Assert.False(Directory.Exists(Path.Combine(packages, "scenarios", "scn")));
        Assert.Equal("scenario", saves.TrashEntries()[0].Kind);
        File.Delete(saves.LiveDir);

        var msg = saves.Restore(entries[0], packages, T0.AddHours(1));
        Assert.Contains("back under Scenarios", msg);
        Assert.True(File.Exists(Path.Combine(packages, "scenarios", "scn", "scenario.json")));
        Assert.Equal(0, Json.Load<SaveFile>(live).Snapshot.Clock.Turn);
        var game = saves.TrashEntries().Single(e => e.Kind == "game");
        msg = saves.Restore(game, packages, T0.AddHours(2));
        Assert.Contains("are back", msg);
        Assert.Equal(4, Json.Load<SaveFile>(saves.LivePath("scn")).Snapshot.Clock.Turn);
        Assert.Single(saves.Checkpoints(saves.LivePath("scn")), c => c.Kind == CheckpointKind.Turn);
        Assert.Equal(0, Json.Load<SaveFile>(live).Snapshot.Clock.Turn);
        Assert.DoesNotContain(saves.TrashEntries(), e => e.Kind == "game");
        Json.Save(Path.Combine(packages, "mypack", "pack.json"), new PackManifest { Id = "mypack", Name = "P2" });
        var pack = saves.TrashEntries().Single(e => e.Kind == "pack");
        Assert.Throws<IOException>(() => saves.Restore(pack, packages, T0.AddHours(3)));
        Directory.Delete(Path.Combine(packages, "mypack"), true);
        saves.Restore(pack, packages, T0.AddHours(3));
        Assert.Equal("P", Json.Load<PackManifest>(Path.Combine(packages, "mypack", "pack.json")).Name);
        Assert.Empty(saves.TrashEntries());
        saves.TrashGame("scn", T0.AddHours(4));
        Assert.Equal(1, saves.EmptyTrash(T0.AddHours(4), everything: true));
    }

    [Fact]
    public void A_game_handed_over_in_a_scenario_file_becomes_the_game_in_play_beside_the_saves_already_there()
    {
        var root = TempRoot();
        var saves = Store(root);
        var handed = Path.Combine(root, "game.kstable");
        Json.Save(handed, new SaveFile { ScenarioId = "scn", Snapshot = new GameState { Clock = new GameClock { Turn = 9 } } });
        var first = saves.AddGame("scn", handed, "Handed over", T0);
        Assert.Equal(9, Json.Load<SaveFile>(saves.LivePath("scn")).Snapshot.Clock.Turn);
        var second = saves.AddGame("scn", handed, "Handed over", T0.AddMinutes(1));
        Assert.NotEqual(first, second);
        Assert.Equal(second, saves.CurrentRun("scn"));
        Assert.True(File.Exists(saves.LivePath("scn", first)));
        Assert.Equal(new[] { "Handed over", "Handed over (2)" }, saves.Runs("scn").Select(r => Path.GetFileName(r.Folder)).OrderBy(n => n));
        Assert.Empty(saves.TrashEntries());
    }

    [Fact]
    public void A_restore_checkpoint_is_kept_whatever_follows_it()
    {
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        var ck = new Checkpointer(saves, live, 0, T0);
        for (int i = 0; i < 5; i++) Assert.Equal("restore", ck.Before("restore", i, T0.AddMinutes(i))!.Kind);
        var all = saves.Checkpoints(live);
        Assert.Equal(5, all.Count);
        Assert.All(all, c => Assert.Equal("restore", c.Kind));
        Assert.Contains("before a turn was restored", all[0].Describe());
    }

    [Fact]
    public void A_loaded_turn_reaches_every_observer_with_the_observers_view_and_every_umpire_with_the_whole_table()
    {
        using var t = new Table();
        const long SecondWatcher = 31;
        t.LetIn(SecondWatcher, "Second watcher", Role.Observer);
        t.Ok(Table.Ump, new SetBriefing { Text = "UMPIRE-SECRET-PLAN" });
        t.Ok(Table.Ump, new AdvanceClock());
        var res = LoadTurn(t, 0);
        GameState Restored(long peer) => Assert.IsType<LoadSave>(Assert.IsType<CommandMessage>(res.Single(o => o.PeerId == peer && o.Message is CommandMessage).Message).Command).State!;
        var observerViews = new[] { Table.Obs, SecondWatcher }.Select(Restored).ToList();
        Assert.All(observerViews, v => Assert.Null(v.Briefing));
        Assert.Equal(Json.Serialize(observerViews[0]), Json.Serialize(observerViews[1]));
        Assert.All(new[] { Table.Ump, Table.Ump2, Table.Ump3 }, u => Assert.Equal("UMPIRE-SECRET-PLAN", Restored(u).Briefing));
    }
}
