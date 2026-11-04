using System.Text;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class SaveLogTests
{
    static readonly DateTime T0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    static string TempRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-savelog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        return r;
    }

    static SaveStore Store(string root) => new(Path.Combine(root, "live"), Path.Combine(root, "history"), Path.Combine(root, "trash"));

    static string Stamp(GameServer s) => SaveLog.StampOf(s.Created, s.State.ScenarioId);

    static (Table t, string path, SaveLog log) Played(params Command[] after)
    {
        var t = new Table();
        var path = Path.Combine(TempRoot(), "game.kstable");
        t.Server.SaveTo(path);
        var log = new SaveLog(path, Stamp(t.Server));
        t.Server.Applied = log.Append;
        foreach (var c in after) t.Ok(Table.Host, c);
        log.Flush();
        return (t, path, log);
    }

    [Fact]
    public void The_disk_work_runs_where_it_is_handed_and_a_line_added_after_a_setting_aside_never_goes_with_it()
    {
        using var t = new Table();
        var path = Path.Combine(TempRoot(), "game.kstable");
        t.Server.SaveTo(path);
        var jobs = new Queue<Action>();
        using (var log = new SaveLog(path, Stamp(t.Server), disk: jobs.Enqueue))
        {
            t.Server.Applied = log.Append;
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(1, 1) });
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(2, 2) });
            log.Flush();
            log.Rotate();
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(3, 3) });
            log.Flush();
            Assert.False(File.Exists(SaveLog.PathFor(path)));
            while (jobs.TryDequeue(out var job)) job();
            Assert.Equal(1, log.Count);
        }
        while (jobs.TryDequeue(out var job)) job();
        Assert.Equal(3, File.ReadAllLines(SaveLog.OldPathFor(path)).Length);
        Assert.Equal(2, File.ReadAllLines(SaveLog.PathFor(path)).Length);
        Assert.Equal(3, SaveLoader.Load(path, t.Store).Loaded.ReplayedFromLog);
    }

    [Fact]
    public void Play_after_the_last_whole_save_comes_back_from_the_log()
    {
        var (t, path, log) = Played(
            new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) },
            new SetCounter { Layer = Layers.Umpire, Id = "p-000002", Name = "str", Value = 3 });
        using (t) using (log)
        {
            Assert.Equal(0, Json.Load<SaveFile>(path).Snapshot.Find(Layers.Umpire, "p-000002")!.Pos.X);
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(2, back.Loaded.ReplayedFromLog);
            Assert.False(back.Loaded.LogTorn);
            var p = back.State.Find(Layers.Umpire, "p-000002")!;
            Assert.Equal(7, p.Pos.X);
            Assert.Equal(3, p.Counters["str"]);
            Assert.Equal(t.Server.State.LastSeq, back.State.LastSeq);
        }
    }

    [Fact]
    public void A_line_cut_short_by_the_machine_stopping_costs_only_that_command()
    {
        var (t, path, log) = Played(
            new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) },
            new SetLabel { Layer = Layers.Umpire, Id = "p-000002", Value = "Second Brigade" });
        using (t)
        {
            log.Dispose();
            var lines = File.ReadAllBytes(SaveLog.PathFor(path));
            int lastNl = Array.LastIndexOf(lines, (byte)'\n');
            int cut = lastNl + (lines.Length - lastNl) / 2;
            File.WriteAllBytes(SaveLog.PathFor(path), lines[..cut]);

            var back = SaveLoader.Load(path, t.Store);
            Assert.True(back.Loaded.LogTorn);
            Assert.Equal(1, back.Loaded.ReplayedFromLog);
            var p = back.State.Find(Layers.Umpire, "p-000002")!;
            Assert.Equal(7, p.Pos.X);
            Assert.Null(p.Label);
        }
    }

    [Fact]
    public void A_log_left_by_another_game_at_the_same_path_is_not_replayed()
    {
        var (t, path, log) = Played(new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) });
        using (t) using (log)
        {
            using var other = new Table();
            other.Server.SaveTo(path);
            var back = SaveLoader.Load(path, other.Store);
            Assert.Equal(0, back.Loaded.ReplayedFromLog);
            Assert.Equal(0, back.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
        }
    }

    [Fact]
    public void A_table_written_whole_while_play_goes_on_loses_neither_the_old_log_nor_the_new()
    {
        var (t, path, log) = Played(new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) });
        using (t) using (log)
        {
            log.Rotate();
            t.Ok(Table.Host, new SetCounter { Layer = Layers.Umpire, Id = "p-000002", Name = "str", Value = 5 });
            log.Flush();
            Assert.True(File.Exists(SaveLog.OldPathFor(path)));

            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(2, back.Loaded.ReplayedFromLog);
            var p = back.State.Find(Layers.Umpire, "p-000002")!;
            Assert.Equal(7, p.Pos.X);
            Assert.Equal(5, p.Counters["str"]);
        }
    }

    [Fact]
    public void Commands_the_snapshot_already_holds_are_not_applied_twice()
    {
        var (t, path, log) = Played(new SetCounter { Layer = Layers.Umpire, Id = "p-000002", Name = "str", Value = 5 });
        using (t) using (log)
        {
            log.Rotate();
            t.Server.SaveTo(path);
            Assert.True(File.Exists(SaveLog.OldPathFor(path)));
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(0, back.Loaded.ReplayedFromLog);
            Assert.Equal(5, back.State.Find(Layers.Umpire, "p-000002")!.Counters["str"]);
        }
    }

    [Fact]
    public void A_turn_restore_in_a_log_written_by_an_older_version_replays_as_it_went_in()
    {
        var (t, path, log) = Played(new AdvanceClock());
        using (t) using (log)
        {
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(9, 9) });
            var restored = t.Server.Turns[0].Unpack();
            log.Append(new RestoreTurn { Index = 0, State = restored, Seq = t.Server.State.LastSeq + 1, Actor = Table.Lead });
            log.Flush();
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(restored.Find(Layers.Umpire, "p-000002")!.Pos.X, back.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
            Assert.Equal(restored.Clock.Turn, back.State.Clock.Turn);
            Assert.Empty(back.Turns);
        }
    }

    [Fact]
    public void A_game_recovered_from_a_checkpoint_lets_go_of_the_log_it_could_not_read()
    {
        using var t = new Table();
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        t.Server.SaveTo(live);
        saves.Checkpoint(live, "turn", 0, T0);
        using (var log = new SaveLog(live, Stamp(t.Server)))
        {
            t.Server.Applied = log.Append;
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) });
        }
        t.Server.Applied = null;
        File.WriteAllText(live, "{ not a save");
        var back = SaveLoader.Open(saves, live, t.Store, out var report);
        Assert.NotNull(report?.From);
        Assert.Equal(0, back.Loaded.ReplayedFromLog);
        Assert.False(File.Exists(SaveLog.PathFor(live)));
        Assert.Equal(0, back.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
    }

    [Fact]
    public void A_log_that_cannot_be_removed_after_a_recovery_is_never_replayed_onto_the_recovered_game()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = new Table();
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        t.Server.SaveTo(live);
        saves.Checkpoint(live, "turn", 0, T0);
        t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000003", Pos = new(4, 4) });
        t.Server.SaveTo(live);
        using (var log = new SaveLog(live, Stamp(t.Server)))
        {
            t.Server.Applied = log.Append;
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) });
        }
        t.Server.Applied = null;
        File.WriteAllText(live, "{ not a save");
        GameServer back;
        Recovery? report;
        using (File.Open(SaveLog.PathFor(live), FileMode.Open, FileAccess.Read, FileShare.Read))
            back = SaveLoader.Open(saves, live, t.Store, out report);
        Assert.NotNull(report?.From);
        Assert.NotNull(report!.LogLeft);
        Assert.Equal(0, back.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
        var later = SaveLoader.Load(live, t.Store);
        Assert.Equal(0, later.Loaded.ReplayedFromLog);
        Assert.Equal(0, later.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
        Assert.Equal(1, later.State.Find(Layers.Umpire, "p-000003")!.Pos.X);
    }

    [Fact]
    public void A_log_that_cannot_be_written_leaves_the_game_to_save_itself_whole()
    {
        using var t = new Table();
        var root = TempRoot();
        Directory.CreateDirectory(Path.Combine(root, "game.kstable.log"));
        var log = new SaveLog(Path.Combine(root, "game.kstable"), Stamp(t.Server));
        Assert.False(log.Working);
        log.Append(new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(1, 1) });
        log.Flush();
        log.Dispose();
    }

    [Fact]
    public void A_log_kept_through_a_crash_is_added_to_rather_than_started_over()
    {
        var (t, path, log) = Played(new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) });
        using (t)
        {
            log.Dispose();
            var back = SaveLoader.Load(path, t.Store);
            using var again = new SaveLog(path, Stamp(back));
            Assert.True(again.Working);
            back.Applied = again.Append;
            back.Join(Table.Host, new JoinRequest { Name = "Umpire One" });
            back.Handle(Table.Host, new SetCounter { Layer = Layers.Umpire, Id = "p-000002", Name = "str", Value = 2 });
            again.Flush();
            var twice = SaveLoader.Load(path, t.Store);
            Assert.Equal(7, twice.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
            Assert.Equal(2, twice.State.Find(Layers.Umpire, "p-000002")!.Counters["str"]);
        }
    }

    [Fact]
    public void A_log_with_nothing_but_its_own_header_replays_nothing()
    {
        using var t = new Table();
        var path = Path.Combine(TempRoot(), "game.kstable");
        t.Server.SaveTo(path);
        using (var log = new SaveLog(path, Stamp(t.Server))) log.Flush();
        Assert.Equal(Stamp(t.Server), Encoding.UTF8.GetString(File.ReadAllBytes(SaveLog.PathFor(path))).TrimEnd('\n'));
        var back = SaveLoader.Load(path, t.Store);
        Assert.Equal(0, back.Loaded.ReplayedFromLog);
        Assert.False(back.Loaded.LogTorn);
    }
    [Fact]
    public void A_turn_that_ended_after_the_last_whole_save_keeps_its_record()
    {
        var (t, path, log) = Played();
        using (t) using (log)
        {
            int before = t.Server.Turns.Count;
            t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(5, 5) });
            t.Ok(Table.Host, new AdvanceClock());
            log.Flush();
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(before + 1, back.Turns.Count);
            Assert.Equal(t.Server.Turns.Count, back.Turns.Count);
            Assert.Equal(t.Server.State.Clock.Turn, back.State.Clock.Turn);
            Assert.Equal(5, back.Turns[^1].Unpack().Find(Layers.Umpire, "p-000002")!.Pos.X);
        }
    }

    [Fact]
    public void A_turn_restore_in_an_older_log_after_the_last_whole_save_trims_the_history_as_it_did()
    {
        var (t, path, log) = Played();
        using (t) using (log)
        {
            t.Ok(Table.Host, new AdvanceClock());
            t.Ok(Table.Host, new AdvanceClock());
            log.Append(new RestoreTurn { Index = 1, State = t.Server.Turns[1].Unpack(), Seq = t.Server.State.LastSeq + 1, Actor = Table.Lead });
            log.Flush();
            var back = SaveLoader.Load(path, t.Store);
            Assert.Single(back.Turns);
            Assert.Equal(t.Server.Turns[1].Turn, back.State.Clock.Turn);
        }
    }

    static RollDice Roll() => new() { Sides = 6, Count = 1 };

    static byte[] Shared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    static void HasEveryRoll(GameServer back, Table t)
    {
        Assert.Equal(t.Server.State.Dice.Count, back.State.Dice.Count);
        Assert.Equal(t.Server.State.LastSeq, back.State.LastSeq);
    }

    [Fact]
    public void A_snapshot_that_never_landed_keeps_its_commands_set_aside_through_the_next_save()
    {
        var (t, path, log) = Played(Roll());
        using (t) using (log)
        {
            log.Rotate();
            var first = log.Generations.Of(path);
            t.Ok(Table.Host, Roll());
            log.Rotate();
            var second = log.Generations.Of(path);
            var bytes = t.Server.SaveBytes();
            t.Ok(Table.Host, Roll());
            log.Flush();
            log.Generations.Settled(path, first);
            Assert.True(File.Exists(SaveLog.OldPathFor(path)));
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(3, back.Loaded.ReplayedFromLog);
            HasEveryRoll(back, t);

            Json.WriteAtomic(path, bytes);
            log.Generations.Settled(path, second);
            Assert.False(File.Exists(SaveLog.OldPathFor(path)));
            back = SaveLoader.Load(path, t.Store);
            Assert.Equal(1, back.Loaded.ReplayedFromLog);
            HasEveryRoll(back, t);
        }
    }

    [Fact]
    public void A_write_still_running_when_the_table_closes_cannot_settle_the_closing_log()
    {
        var (t, path, log) = Played(Roll());
        using (t) using (log)
        {
            log.Rotate();
            var running = log.Generations.Of(path);
            var runningBytes = t.Server.SaveBytes();
            t.Ok(Table.Host, Roll());
            log.Rotate();
            var closing = log.Generations.Of(path);
            var closingBytes = t.Server.SaveBytes();
            Json.WriteAtomic(path, runningBytes);
            log.Generations.Settled(path, running);
            var back = SaveLoader.Load(path, t.Store);
            HasEveryRoll(back, t);

            Json.WriteAtomic(path, closingBytes);
            log.Generations.Settled(path, closing);
            Assert.False(File.Exists(SaveLog.OldPathFor(path)));
            HasEveryRoll(SaveLoader.Load(path, t.Store), t);
        }
    }

    [Fact]
    public void The_machine_stopping_at_any_step_of_setting_a_log_aside_loses_and_doubles_nothing()
    {
        var (t, path, log) = Played(Roll());
        using (t) using (log)
        {
            log.Rotate();
            t.Ok(Table.Host, Roll());
            log.Flush();
            var live = SaveLog.PathFor(path);
            var old = SaveLog.OldPathFor(path);
            var liveBefore = Shared(live);
            var oldBefore = Shared(old);
            log.Rotate();
            log.Dispose();
            var oldAfter = Shared(old);
            var freshLive = Shared(live);
            Assert.True(oldAfter.Length > oldBefore.Length);

            var steps = new (string what, byte[] old, byte[]? live)[]
            {
                ("half the live log added to the set-aside one", oldAfter[..(oldBefore.Length + (oldAfter.Length - oldBefore.Length) / 2)], liveBefore),
                ("all of it added, the live log not yet deleted", oldAfter, liveBefore),
                ("the live log deleted, no fresh one yet", oldAfter, null),
                ("the fresh live log begun", oldAfter, freshLive),
            };
            foreach (var (what, oldBytes, liveBytes) in steps)
            {
                File.WriteAllBytes(old, oldBytes);
                if (liveBytes == null) File.Delete(live);
                else File.WriteAllBytes(live, liveBytes);
                var back = SaveLoader.Load(path, t.Store);
                Assert.True(back.Loaded.ReplayedFromLog == 2, what);
                Assert.True(t.Server.State.Dice.Count == back.State.Dice.Count, what);
                Assert.False(back.Loaded.LogTorn, what);
            }
        }
    }

    [Fact]
    public void A_game_reopened_after_a_line_was_cut_short_keeps_what_it_plays_next()
    {
        var (t, path, log) = Played(Roll(), Roll());
        using (t)
        {
            log.Dispose();
            var bytes = File.ReadAllBytes(SaveLog.PathFor(path));
            int lastNl = Array.LastIndexOf(bytes, (byte)'\n', bytes.Length - 2);
            File.WriteAllBytes(SaveLog.PathFor(path), bytes[..(lastNl + 1 + (bytes.Length - lastNl) / 2)]);

            var back = SaveLoader.Load(path, t.Store);
            Assert.True(back.Loaded.LogTorn);
            Assert.Equal(1, back.Loaded.ReplayedFromLog);
            using (var again = new SaveLog(path, Stamp(back)))
            {
                back.Applied = again.Append;
                back.Join(Table.Host, new JoinRequest { Name = "Umpire One" });
                Assert.DoesNotContain(back.Handle(Table.Host, Roll()), o => o.Message is ErrorMessage);
                Assert.DoesNotContain(back.Handle(Table.Host, Roll()), o => o.Message is ErrorMessage);
            }
            var twice = SaveLoader.Load(path, t.Store);
            Assert.Equal(3, twice.Loaded.ReplayedFromLog);
            Assert.Equal(back.State.Dice.Count, twice.State.Dice.Count);
            Assert.Equal(back.State.LastSeq, twice.State.LastSeq);
            Assert.False(twice.Loaded.LogTorn);
        }
    }

    static (Table t, string path) BothLogs()
    {
        var (t, path, log) = Played(Roll());
        log.Rotate();
        t.Ok(Table.Host, Roll());
        log.Dispose();
        return (t, path);
    }

    static string[] Beside(string path) => Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public void A_log_another_program_holds_for_a_moment_is_read_again_and_both_logs_are_replayed()
    {
        var (t, path) = BothLogs();
        using (t)
        {
            var held = new FileStream(SaveLog.OldPathFor(path), FileMode.Open, FileAccess.Read, FileShare.None);
            var letGo = new Thread(() => { Thread.Sleep(400); held.Dispose(); });
            letGo.Start();
            var back = SaveLoader.Load(path, t.Store);
            letGo.Join();
            Assert.Equal(2, back.Loaded.ReplayedFromLog);
            HasEveryRoll(back, t);
        }
    }

    [Fact]
    public void A_log_that_stays_unreadable_is_kept_aside_with_its_partner_and_the_game_opens_without_it()
    {
        var (t, path) = BothLogs();
        using (t)
        {
            var saves = Store(TempRoot());
            using (new FileStream(SaveLog.OldPathFor(path), FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                var back = SaveLoader.Open(saves, path, t.Store, out var report);
                Assert.Null(report);
                Assert.Equal(0, back.Loaded.ReplayedFromLog);
                Assert.Empty(back.State.Dice);
                Assert.Equal(KeptAsideWhy.Unreadable, back.Loaded.KeptAside!.Why);
                Assert.StartsWith("game.kstable.log.old: ", back.Loaded.KeptAside.Detail);
                Assert.Contains(back.Loaded.KeptAside.Files, f => Path.GetFileName(f).StartsWith("game.kstable.log.unreadable-"));
            }
            new SaveLogGenerations().Settled(path, 0, liveToo: true);
            var files = Beside(path);
            Assert.DoesNotContain("game.kstable.log", files);
            Assert.DoesNotContain("game.kstable.log.old", files);
            var keptOld = Path.Combine(Path.GetDirectoryName(path)!, Assert.Single(files, f => f.StartsWith("game.kstable.log.old.unreadable-")));
            var keptLive = Path.Combine(Path.GetDirectoryName(path)!, Assert.Single(files, f => f.StartsWith("game.kstable.log.unreadable-")));
            Assert.Contains("RollDice", File.ReadAllText(keptOld));
            Assert.Contains("RollDice", File.ReadAllText(keptLive));
        }
    }

    [Fact]
    public void A_log_that_can_neither_be_read_nor_kept_aside_stops_the_opening_and_nothing_falls_back_or_goes()
    {
        var (t, path) = BothLogs();
        using (t)
        {
            var saves = Store(TempRoot());
            File.Copy(path, Path.Combine(Directory.CreateDirectory(saves.CheckpointDir(path)).FullName, "turn-0000_20261031-110000.kstable"));
            var before = Beside(path);
            using (new FileStream(SaveLog.OldPathFor(path), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var e = Assert.Throws<SaveHeldException>(() => SaveLoader.Open(saves, path, t.Store, out _));
                Assert.Contains("the game was not opened", e.Message);
            }
            Assert.Equal(before, Beside(path));
            var back = SaveLoader.Load(path, t.Store);
            HasEveryRoll(back, t);
        }
    }

    [Fact]
    public void Changes_in_an_older_save_that_will_not_replay_are_counted_with_the_log_after_them()
    {
        using var t = new Table();
        t.Server.State.Clock.GameTime = "9999-12-31T23:50";
        var path = Path.Combine(TempRoot(), "game.kstable");
        var seq = t.Server.State.LastSeq;
        var save = t.Server.ToSave();
        save.Commands = new() { new AdvanceClock { Seq = seq + 1 }, new RollDice { Sides = 6, Count = 1, Seq = seq + 2 } };
        Json.Save(path, save);
        using (var log = new SaveLog(path, Stamp(t.Server)))
        {
            log.Append(new RollDice { Sides = 6, Count = 1, Seq = seq + 3 });
            log.Append(new RollDice { Sides = 6, Count = 1, Seq = seq + 4 });
        }
        var back = SaveLoader.Load(path, t.Store);
        Assert.Equal(4, back.Loaded.ReplayFailed);
        Assert.True(back.Loaded.SaveHoldsUnreplayed);
        Assert.Equal(0, back.Loaded.ReplayedFromLog);
    }

    [Fact]
    public void A_change_that_will_not_replay_stops_the_replay_there_keeps_no_turn_for_it_and_keeps_the_log()
    {
        var t = new Table();
        using (t)
        {
            t.Server.State.Clock.GameTime = "9999-12-31T23:50";
            var path = Path.Combine(TempRoot(), "game.kstable");
            t.Server.SaveTo(path);
            using (var log = new SaveLog(path, Stamp(t.Server)))
            {
                t.Server.Applied = log.Append;
                t.Ok(Table.Host, Roll());
                var seq = t.Server.State.LastSeq;
                log.Append(new AdvanceClock { Seq = seq + 1 });
                log.Append(new RollDice { Sides = 6, Count = 1, Seq = seq + 2 });
            }
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(1, back.Loaded.ReplayedFromLog);
            Assert.Equal(t.Server.State.LastSeq, back.State.LastSeq);
            Assert.Empty(back.Turns);
            Assert.Single(back.State.Dice);
            Assert.Equal(2, back.Loaded.ReplayFailed);
            Assert.StartsWith("AdvanceClock, change ", back.Loaded.ReplayFailure);
            var opened = SaveLoader.Open(Store(TempRoot()), path, t.Store, out _);
            Assert.Equal((KeptAsideWhy.Unreplayed, 2), (opened.Loaded.KeptAside!.Why, opened.Loaded.KeptAside.Changes));
            Assert.StartsWith("AdvanceClock, change ", opened.Loaded.KeptAside.Detail);
            Assert.False(File.Exists(SaveLog.PathFor(path)));
            Assert.Single(Beside(path), f => f.StartsWith("game.kstable.log.unreplayed-"));
        }
    }

    [Fact]
    public void Commands_after_one_the_disk_lost_are_not_replayed_onto_a_table_without_it()
    {
        var (t, path, log) = Played(Roll(), new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(7, 8) }, Roll());
        using (t)
        {
            log.Dispose();
            var text = File.ReadAllText(SaveLog.PathFor(path)).Split('\n');
            text[2] = text[2][..(text[2].Length / 2)];
            File.WriteAllText(SaveLog.PathFor(path), string.Join('\n', text));
            var back = SaveLoader.Load(path, t.Store);
            Assert.Equal(1, back.Loaded.ReplayedFromLog);
            Assert.Equal(1, back.Loaded.LogNotFollowing);
            Assert.Equal(0, back.State.Find(Layers.Umpire, "p-000002")!.Pos.X);
            var logged = File.ReadAllText(SaveLog.PathFor(path));
            var opened = SaveLoader.Open(Store(TempRoot()), path, t.Store, out _);
            Assert.Equal((KeptAsideWhy.Gap, 1), (opened.Loaded.KeptAside!.Why, opened.Loaded.KeptAside.Changes));
            Assert.False(File.Exists(SaveLog.PathFor(path)));
            var kept = Assert.Single(Beside(path), f => f.StartsWith("game.kstable.log.gap-"));
            Assert.Equal(logged, File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, kept)));
        }
    }

    [Fact]
    public void A_save_another_program_holds_is_not_opened_and_nothing_falls_back_to_an_older_copy()
    {
        var (t, path, log) = Played(Roll());
        using (t)
        {
            log.Dispose();
            var saves = Store(TempRoot());
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(saves.CheckpointDir(path)).FullName, "turn-0001_20260101-000000.kstable"), "{}");
            var before = Beside(path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var e = Assert.Throws<SaveHeldException>(() => SaveLoader.Open(saves, path, t.Store, out _));
                Assert.StartsWith("The save is held by another program", e.Message);
            }
            Assert.Equal(before, Beside(path));
            Assert.NotNull(SaveLoader.Open(saves, path, t.Store, out var report));
            Assert.Null(report);
        }
    }

    [Fact]
    public void Only_a_damaged_save_falls_back_to_an_older_copy_never_a_fault_in_the_app()
    {
        Assert.True(SaveStore.IsDamage(new System.Text.Json.JsonException("cut short")));
        Assert.True(SaveStore.IsDamage(new InvalidDataException("not a save")));
        Assert.True(SaveStore.IsDamage(new EndOfStreamException()));
        Assert.False(SaveStore.IsDamage(new NullReferenceException()));
        Assert.False(SaveStore.IsDamage(new InvalidOperationException()));
        Assert.False(SaveStore.IsDamage(new IOException("held", unchecked((int)0x80070020))));
        Assert.False(SaveStore.IsDamage(new IOException("the device is not ready", unchecked((int)0x80070015))));
        Assert.True(SaveStore.IsUnreachable(new IOException("the device is not ready", unchecked((int)0x80070015))));
        Assert.True(SaveStore.IsHeld(new IOException("held", unchecked((int)0x80070020))));
    }

    [Fact]
    public void A_checkpoint_or_trash_entry_named_with_an_impossible_time_is_still_listed()
    {
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        var dir = Directory.CreateDirectory(saves.CheckpointDir(live)).FullName;
        File.WriteAllText(Path.Combine(dir, "turn-0003_20261399-996099.kstable"), "{}");
        File.WriteAllText(Path.Combine(dir, "turn-0002_20260101-000000.kstable"), "{}");
        var list = saves.Checkpoints(live);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.Kind == CheckpointKind.Other && c.Path.EndsWith("20261399-996099.kstable"));
        Directory.CreateDirectory(Path.Combine(saves.TrashDir, "20261399-996099 feud.game"));
        Assert.Contains(saves.TrashEntries(), e => e.Path.EndsWith("20261399-996099 feud.game"));
    }
}
