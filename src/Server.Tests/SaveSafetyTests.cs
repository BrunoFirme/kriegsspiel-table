using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class SaveSafetyTests
{
    static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    static string TempRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-save-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        return r;
    }

    static SaveStore Store(string root) => new(Path.Combine(root, "live"), Path.Combine(root, "history"), Path.Combine(root, "trash"));

    [Fact]
    public void Json_Save_writes_whole_files_and_leaves_no_temp_file()
    {
        var root = TempRoot();
        var p = Path.Combine(root, "deeper", "x.kstable");
        Json.Save(p, new SaveFile { ScenarioId = "one" });
        Json.Save(p, new SaveFile { ScenarioId = "two" });
        Assert.Equal("two", Json.Load<SaveFile>(p).ScenarioId);
        Assert.False(File.Exists(p + ".tmp"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(p)!));
    }

    [Fact]
    public void A_write_with_its_own_temp_file_leaves_none_behind_whether_it_lands_or_fails()
    {
        var root = TempRoot();
        var p = Path.Combine(root, "x.kstable");
        Json.WriteAtomic(p, new byte[] { 1, 2, 3 }, "7");
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(p));
        Assert.Single(Directory.GetFiles(root));
        using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => Json.WriteAtomic(p, new byte[] { 4 }, "8"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(p));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public async Task A_copy_lands_whole_waits_out_a_held_file_and_leaves_no_temp_file_when_it_fails()
    {
        var root = TempRoot();
        var from = Path.Combine(root, "from.json");
        var to = Path.Combine(root, "deeper", "to.json");
        File.WriteAllBytes(from, new byte[] { 1, 2, 3 });
        Json.CopyAtomic(from, to);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(to));
        Assert.Throws<IOException>(() => Json.CopyAtomic(from, to, overwrite: false));
        Assert.Throws<FileNotFoundException>(() => Json.CopyAtomic(Path.Combine(root, "gone.json"), to));
        Assert.Equal(new[] { to }, Directory.GetFiles(Path.GetDirectoryName(to)!));
        File.WriteAllBytes(from, new byte[] { 4 });
        var held = new FileStream(to, FileMode.Open, FileAccess.Read, FileShare.Read);
        var released = Task.Delay(60).ContinueWith(_ => held.Dispose());
        Json.CopyAtomic(from, to);
        await released;
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(to));
        Assert.Equal(new[] { to }, Directory.GetFiles(Path.GetDirectoryName(to)!));
    }

    [Fact]
    public void A_checkpoint_and_an_installed_game_are_whole_copies_with_the_original_s_time_and_no_temp_file()
    {
        var root = TempRoot();
        var store = Store(root);
        var live = store.LegacyLivePath("feud");
        Directory.CreateDirectory(store.LiveDir);
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(live, bytes);
        var written = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(live, written);
        var cp = store.Checkpoint(live, CheckpointKind.Turn, 3, T0);
        Assert.Equal(bytes, File.ReadAllBytes(cp));
        Assert.Equal(written, File.GetLastWriteTimeUtc(cp));
        Assert.Equal(new[] { cp }, Directory.GetFiles(Path.GetDirectoryName(cp)!));

        var handed = Path.Combine(root, "handed.kstable");
        File.WriteAllBytes(handed, new byte[] { 9, 8, 7 });
        File.SetLastWriteTimeUtc(handed, written);
        store.AddGame("other", handed, "Handed over", T0);
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(store.LivePath("other")));
        Assert.Equal(written, File.GetLastWriteTimeUtc(store.LivePath("other")));
        Assert.False(File.Exists(store.LivePath("other") + ".tmp"));
    }

    [Fact]
    public void A_save_from_a_newer_app_is_refused_with_a_clear_message()
    {
        using var t = new Table();
        var p = Path.Combine(TempRoot(), "x.kstable");
        var save = t.Server.ToSave();
        save.Format = SaveFile.CurrentFormat + 1;
        Json.Save(p, save);
        var e = Assert.Throws<SaveTooNewException>(() => SaveLoader.Load(p, t.Store));
        Assert.Contains("newer version", e.Message);
    }

    [Fact]
    public void A_save_that_can_hold_a_wind_is_one_a_format_4_build_refuses()
    {
        using var t = new Table();
        t.Ok(Table.Ump, new SetWind { Wind = new Wind { From = 90, Strength = 2 } });
        var p = Path.Combine(TempRoot(), "x.kstable");
        t.Server.SaveTo(p);
        Assert.True(Json.Load<SaveFile>(p).Format > 4, "a save with a wind is still written as format 4");
        Assert.Equal(90, SaveLoader.Load(p, t.Store).State.Wind.From);
    }

    [Fact]
    public void A_save_from_a_newer_app_is_never_replaced_by_an_older_checkpoint()
    {
        using var t = new Table();
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        t.Server.SaveTo(live);
        saves.Checkpoint(live, "turn", 0, T0);
        var save = t.Server.ToSave();
        save.Format = SaveFile.CurrentFormat + 1;
        Json.Save(live, save);
        Assert.Throws<SaveTooNewException>(() => SaveLoader.Open(saves, live, t.Store, out _));
        Assert.True(File.Exists(live));
        Assert.Equal(SaveFile.CurrentFormat + 1, Json.Load<SaveFile>(live).Format);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(live)!));
    }

    [Fact]
    public void A_format_3_save_opens_with_its_turns_and_is_written_back_packed_without_the_log()
    {
        using var t = new Table();
        var id = t.Server.State.Layer(Layers.Umpire)[0].Id;
        t.Ok(Table.Ump, new MovePiece { Layer = Layers.Umpire, Id = id, Pos = new Vec2(5, 5) });
        t.Ok(Table.Ump, new AdvanceClock());
        t.Ok(Table.Ump, new MovePiece { Layer = Layers.Umpire, Id = id, Pos = new Vec2(9, 9) });
        t.Ok(Table.Ump, new AdvanceClock());
        var old = t.Server.ToSave();
        old.Format = 3;
        old.Commands = t.Server.Log.ToList();
        foreach (var r in old.Turns)
        {
            r.State = r.Unpack();
            r.Packed = null;
            r.Pieces = 0;
        }
        var p = Path.Combine(TempRoot(), "old.kstable");
        Json.Save(p, old);

        var back = SaveLoader.Load(p, t.Store);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(back.State));
        Assert.Equal(2, back.Turns.Count);
        Assert.All(back.Turns, r => { Assert.Null(r.State); Assert.NotNull(r.Packed); });
        Assert.Equal(5, back.Turns[0].Unpack().Find(Layers.Umpire, id)!.Pos.X);
        Assert.Equal(9, back.Turns[1].Unpack().Find(Layers.Umpire, id)!.Pos.X);
        Assert.Equal(t.Server.State.Layer(Layers.Umpire).Count, back.Turns[1].Pieces);
        Assert.Empty(back.Log);

        var again = Path.Combine(TempRoot(), "new.kstable");
        back.SaveTo(again);
        var reread = Json.Load<SaveFile>(again);
        Assert.All(reread.Turns, r => { Assert.Null(r.State); Assert.NotNull(r.Packed); });
        Assert.Equal(SaveFile.CurrentFormat, reread.Format);
        Assert.Empty(reread.Commands);
        Assert.Equal(9, SaveLoader.Load(again, t.Store).Turns[1].Unpack().Find(Layers.Umpire, id)!.Pos.X);
        Assert.True(new FileInfo(again).Length < new FileInfo(p).Length);
    }

    [Fact]
    public void A_turn_checkpoint_that_could_not_be_made_is_made_after_the_next_save()
    {
        using var t = new Table();
        var root = TempRoot();
        var saves = Store(root);
        var live = saves.LegacyLivePath("scn");
        t.Server.SaveTo(live);
        var ck = new Checkpointer(saves, live, 0, T0);
        t.Ok(Table.Ump, new AdvanceClock());
        t.Server.SaveTo(live);
        File.WriteAllText(Path.Combine(root, "history"), "in the way");
        Assert.ThrowsAny<IOException>(() => ck.AfterSave(1, 0, 1, T0.AddSeconds(5)));
        File.Delete(Path.Combine(root, "history"));
        var cp = ck.AfterSave(1, 0, 1, T0.AddSeconds(6));
        Assert.Equal("turn", cp!.Kind);
        Assert.True(File.Exists(cp.Path));
    }

    [Fact]
    public void After_a_turn_is_restored_the_turns_ended_again_are_copied_again()
    {
        var saves = Store(TempRoot());
        var live = saves.LegacyLivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        var ck = new Checkpointer(saves, live, 0, T0);
        for (int turns = 1; turns <= 10; turns++) Assert.Equal("turn", ck.AfterSave(turns, turns, turns + 1, T0.AddSeconds(turns))!.Kind);
        Assert.Null(ck.AfterSave(3, 3, 4, T0.AddSeconds(20)));
        Assert.Equal("turn", ck.AfterSave(4, 4, 5, T0.AddSeconds(30))!.Kind);
    }

    [Fact]
    public void A_turn_s_checkpoint_is_taken_once_however_many_threads_ask_at_once()
    {
        var saves = Store(TempRoot());
        var live = saves.LegacyLivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        var ck = new Checkpointer(saves, live, 0, T0);
        var kept = new System.Collections.Concurrent.ConcurrentBag<Checkpoint?>();
        Parallel.For(0, 32, i => kept.Add(i % 4 == 0 ? ck.Before(CheckpointKind.Restore, 1, T0.AddSeconds(1)) : ck.AfterSave(1, 0, 1, T0.AddSeconds(1))));
        Assert.Equal(1, kept.Count(c => c?.Kind == "turn"));
        Assert.Equal(8, kept.Count(c => c?.Kind == "restore"));
    }

    [Fact]
    public void A_file_that_is_not_a_save_is_refused()
    {
        var p = Path.Combine(TempRoot(), "x.kstable");
        File.WriteAllText(p, "{}");
        Assert.Throws<InvalidDataException>(() => SaveLoader.Load(p));
    }

    [Fact]
    public void Checkpoints_come_at_turn_ends_after_ten_minutes_of_play_and_on_close_and_are_pruned()
    {
        using var t = new Table();
        var saves = Store(TempRoot());
        var live = saves.LegacyLivePath("scn");
        t.Server.SaveTo(live);
        var ck = new Checkpointer(saves, live, t.Server.Turns.Count, T0);

        Assert.Null(ck.AfterSave(0, 0, 0, T0.AddSeconds(5)));

        for (int i = 1; i <= 13; i++)
        {
            t.Server.Handle(Table.Ump, new AdvanceClock());
            t.Server.SaveTo(live);
            var cp = ck.AfterSave(t.Server.Turns.Count, t.Server.Turns[^1].Turn, t.Server.State.Clock.Turn, T0.AddMinutes(i));
            Assert.NotNull(cp);
            Assert.Equal("turn", cp!.Kind);
            Assert.Equal(i - 1, cp.Turn);
            Assert.True(File.Exists(cp.Path));
        }
        var all = saves.Checkpoints(live);
        Assert.Equal(SaveStore.KeepTurns, all.Count(c => c.Kind == "turn"));
        Assert.Equal(12, all.First(c => c.Kind == "turn").Turn);
        Assert.Equal(13 - SaveStore.KeepTurns, all.Last(c => c.Kind == "turn").Turn);

        Assert.Null(ck.AfterSave(13, 12, 13, T0.AddMinutes(13 + 9)));
        var timed = ck.AfterSave(13, 12, 13, T0.AddMinutes(13 + 10));
        Assert.Equal("time", timed!.Kind);
        Assert.Equal(13, timed.Turn);

        Assert.Null(ck.OnClose(13, T0.AddMinutes(30)));
        Assert.Null(ck.AfterSave(13, 12, 13, T0.AddMinutes(31)));
        Assert.Equal("close", ck.OnClose(13, T0.AddMinutes(32))!.Kind);
        Assert.Equal(2, saves.Checkpoints(live).Count(c => c.Kind is "time" or "close"));

        foreach (var c in saves.Checkpoints(live)) Assert.Equal(c.Kind == "turn" ? c.Turn + 1 : 13, SaveLoader.Load(c.Path, t.Store).Turns.Count);
    }

    [Fact]
    public void A_damaged_live_save_is_set_aside_and_the_game_continues_from_the_newest_readable_checkpoint()
    {
        using var t = new Table();
        var saves = Store(TempRoot());
        var live = saves.LegacyLivePath("scn");
        t.Server.Handle(Table.Ump, new AdvanceClock());
        t.Server.SaveTo(live);
        var good = saves.Checkpoint(live, "turn", 0, T0);
        t.Server.Handle(Table.Ump, new AdvanceClock());
        t.Server.SaveTo(live);
        var newerButTorn = saves.Checkpoint(live, "turn", 1, T0.AddMinutes(1));
        File.WriteAllText(newerButTorn, File.ReadAllText(newerButTorn)[..300]);
        File.WriteAllText(live, File.ReadAllText(live)[..200]);

        var srv = SaveLoader.Open(saves, live, t.Store, out var report);
        Assert.NotNull(report?.LiveError);
        Assert.Equal(Path.GetFileName(good), Path.GetFileName(report!.From.Path));
        Assert.Contains(report.FailedCheckpoints, f => f.StartsWith(Path.GetFileName(newerButTorn)));
        Assert.Single(srv.Turns);
        Assert.Single(Directory.GetFiles(saves.LiveDir, "scn.unreadable-*.kstable"));
        Assert.Single(SaveLoader.Load(live, t.Store).Turns);
        Assert.Null(Open(saves, live, t.Store));
    }

    static Recovery? Open(SaveStore saves, string live, PackageStore store) { SaveLoader.Open(saves, live, store, out var report); return report; }

    [Fact]
    public void A_missing_live_save_continues_from_the_newest_checkpoint_and_nothing_at_all_fails_clearly()
    {
        using var t = new Table();
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        t.Server.SaveTo(live);
        saves.Checkpoint(live, "close", 0, T0);
        File.Delete(live);
        Assert.True(saves.HasGame("scn"));

        SaveLoader.Open(saves, live, t.Store, out var report);
        Assert.Null(report!.LiveError);
        Assert.True(File.Exists(live));

        var e = Assert.Throws<InvalidDataException>(() => SaveLoader.Open(saves, saves.LivePath("nothing"), t.Store, out _));
        Assert.Contains("no save and no checkpoint", e.Message);
    }

    [Fact]
    public void A_save_outside_the_live_folder_keeps_its_checkpoints_beside_it()
    {
        var root = TempRoot();
        var saves = Store(root);
        var p = Path.Combine(root, "elsewhere", "game.kstable");
        Json.Save(p, new SaveFile { ScenarioId = "x" });
        var cp = saves.Checkpoint(p, "close", 3, T0);
        Assert.Equal(Path.Combine(root, "elsewhere", "game"), Path.GetDirectoryName(cp));
        Assert.Equal("close-0003_20261029-120000.kstable", Path.GetFileName(cp));
        Assert.Equal("close", Assert.Single(saves.Checkpoints(p)).Kind);
    }

    [Fact]
    public void Two_checkpoints_of_one_kind_and_turn_in_the_same_second_are_both_kept_and_the_later_is_the_newest()
    {
        var root = TempRoot();
        var saves = Store(root);
        var live = saves.LivePath("feud");
        Json.Save(live, new SaveFile { ScenarioId = "first" });
        var a = saves.Checkpoint(live, CheckpointKind.Restore, 4, T0);
        Json.Save(live, new SaveFile { ScenarioId = "second" });
        var b = saves.Checkpoint(live, CheckpointKind.Restore, 4, T0.AddMilliseconds(400));
        Json.Save(live, new SaveFile { ScenarioId = "third" });
        var c = saves.Checkpoint(live, CheckpointKind.Restore, 4, T0.AddMilliseconds(800));
        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
        Assert.Equal("restore-0004_20261029-120000-2.kstable", Path.GetFileName(b));
        Assert.Equal("first", Json.Load<SaveFile>(a).ScenarioId);
        Assert.Equal("second", Json.Load<SaveFile>(b).ScenarioId);
        var all = saves.Checkpoints(live);
        Assert.Equal(new[] { c, b, a }, all.Select(x => x.Path));
        Assert.All(all, x => Assert.Equal((CheckpointKind.Restore, 4), (x.Kind, x.Turn)));
        var one = SaveStore.SetAside(live, "unreadable", T0);
        Json.Save(live, new SaveFile { ScenarioId = "fourth" });
        var two = SaveStore.SetAside(live, "unreadable", T0);
        Assert.NotEqual(one, two);
        Assert.Equal("third", Json.Load<SaveFile>(one!).ScenarioId);
        Assert.Equal("fourth", Json.Load<SaveFile>(two!).ScenarioId);
    }

    [Fact]
    public void A_copy_that_cannot_land_takes_its_temp_file_with_it()
    {
        var root = TempRoot();
        var saves = Store(root);
        var handed = Path.Combine(root, "game.kstable");
        Json.Save(handed, new SaveFile { ScenarioId = "scn" });
        var blocked = saves.LivePath("scn", saves.NewRunId("scn", T0));
        Directory.CreateDirectory(blocked);
        Assert.ThrowsAny<Exception>(() => saves.AddGame("scn", handed, "Handed over", T0));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(blocked)!));
    }

    [Fact]
    public void Restore_checkpoints_are_never_pruned_and_start_overs_keep_their_own_three()
    {
        var root = TempRoot();
        var saves = Store(root);
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        var ck = new Checkpointer(saves, live, 0, T0);
        for (int i = 0; i < 12; i++) Assert.Equal("restore", ck.Before(CheckpointKind.Restore, i, T0.AddMinutes(i))!.Kind);
        for (int i = 0; i < 5; i++) saves.Checkpoint(live, CheckpointKind.StartOver, i, T0.AddHours(1).AddMinutes(i));
        var all = saves.Checkpoints(live);
        Assert.Equal(12, all.Count(c => c.Kind == CheckpointKind.Restore));
        Assert.Equal(SaveStore.KeepStartOver, all.Count(c => c.Kind == CheckpointKind.StartOver));
    }

    [Fact]
    public void Deleted_scenarios_and_packs_go_to_the_trash_which_empties_after_thirty_days()
    {
        var root = TempRoot();
        var saves = Store(root);
        var packages = Path.Combine(root, "packages");
        Json.Save(Path.Combine(packages, "scenarios", "scn", "scenario.json"), new Scenario { Id = "scn", Name = "S" });
        Json.Save(Path.Combine(packages, "mypack", "pack.json"), new PackManifest { Id = "mypack", Name = "P" });
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        saves.Checkpoint(live, "turn", 1, T0);

        var e1 = saves.TrashScenario(packages, "scn", T0);
        var e2 = saves.TrashPack(packages, "mypack", T0);
        Assert.False(Directory.Exists(Path.Combine(packages, "scenarios", "scn")));
        Assert.False(Directory.Exists(Path.Combine(packages, "mypack")));
        Assert.False(saves.HasGame("scn"));
        Assert.True(File.Exists(Path.Combine(e1, "scenario", "scenario.json")));
        Assert.True(File.Exists(Path.Combine(e1, "live", SaveStore.MigratedRun + SaveStore.Ext)));
        Assert.Single(Directory.GetFiles(Path.Combine(e1, "saves", SaveStore.SafetyFolder, SaveStore.MigratedRun)));
        Assert.True(File.Exists(Path.Combine(e2, "pack", "pack.json")));

        Assert.Equal(0, saves.EmptyTrash(T0.AddDays(29)));
        Assert.Equal(2, saves.EmptyTrash(T0.AddDays(31)));
        Assert.Empty(Directory.GetDirectories(saves.TrashDir));
    }

    [Fact]
    public void Trashing_a_scenario_s_game_takes_every_play_its_copies_and_its_saves_and_leaves_the_scenario()
    {
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        saves.Checkpoint(live, "time", 1, T0);
        var other = saves.LivePath("scn", "second");
        Json.Save(other, new SaveFile { ScenarioId = "scn" });
        var folder = saves.MakeRun("scn", "second", "Run 2", null, T0);
        Json.Save(Path.Combine(folder, "Kept" + SaveStore.Ext), new SaveFile { ScenarioId = "scn" });
        var entry = saves.TrashGame("scn", T0);
        Assert.False(saves.HasGame("scn"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(entry, "live"), "*" + SaveStore.Ext).Length);
        Assert.True(File.Exists(Path.Combine(entry, "saves", "Run 2", "Kept" + SaveStore.Ext)));
        Assert.Single(Directory.GetFiles(Path.Combine(entry, "saves", SaveStore.SafetyFolder, SaveStore.MigratedRun)));
    }

    [Fact]
    public void Saves_from_the_earlier_layout_move_into_place_once()
    {
        var root = TempRoot();
        var saves = Store(root);
        var old = Path.Combine(root, "old-docs", "saves");
        Json.Save(Path.Combine(old, "scn.kstable"), new SaveFile { ScenarioId = "scn" });
        Json.Save(Path.Combine(old, "scn.previous.kstable"), new SaveFile { ScenarioId = "scn" });
        Json.Save(Path.Combine(saves.HistoryDir, "other.kstable"), new SaveFile { ScenarioId = "other" });
        var moved = saves.Migrate(old, saves.HistoryDir);
        Assert.Equal(3, moved.Count);
        Assert.True(File.Exists(saves.LivePath("scn")));
        Assert.True(File.Exists(saves.LivePath("other")));
        Assert.Equal("startover", Assert.Single(saves.Checkpoints(saves.LivePath("scn"))).Kind);
        Assert.Empty(Directory.GetFiles(old));
        Assert.Empty(saves.Migrate(old, saves.HistoryDir));
    }
}
