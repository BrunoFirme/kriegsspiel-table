using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class SavesTests
{
    static readonly DateTime T0 = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    static string TempRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-saves-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        return r;
    }

    static SaveStore Store(string root) => new(Path.Combine(root, "live"), Path.Combine(root, "history"), Path.Combine(root, "trash"));

    static GameServer PlayedTurns(int turns)
    {
        var srv = new GameServer(new GameState { ScenarioId = "scn", Name = "Feud" });
        srv.Join(1, new JoinRequest { Name = "Host" });
        srv.Handle(1, new SpawnPiece { Layer = Layers.Umpire, Def = "d", Pos = new Vec2(1, 1) });
        for (int i = 0; i < turns; i++) srv.Handle(1, new AdvanceClock());
        return srv;
    }

    static void OldPlay(SaveStore saves, string folder, string id, GameServer game, bool live = true, bool current = true)
    {
        Directory.CreateDirectory(folder);
        Json.Save(Path.Combine(folder, SaveStore.RunMarkFile), new RunMark { Id = id, Created = T0.ToString("o") });
        for (int i = 0; i < game.Turns.Count; i++)
            File.WriteAllBytes(Path.Combine(folder, SaveNames.Clean(SaveNames.Turn(game.Turns[i].Turn, game.Turns[i].GameTime)) + SaveStore.Ext),
                Json.PackedBytes(SaveFile.OfTurn("", "scn", game.Created, game.Access, game.Turns, i)));
        var lives = Path.Combine(saves.LiveDir, "scn");
        Directory.CreateDirectory(lives);
        if (live)
        {
            var save = game.ToSave();
            save.Id = "";
            Json.SaveBig(Path.Combine(lives, id + SaveStore.Ext), save);
            File.WriteAllText(SaveLog.PathFor(Path.Combine(lives, id + SaveStore.Ext)), "");
        }
        if (current) File.WriteAllText(Path.Combine(lives, SaveStore.CurrentFile), id);
    }

    static string[] Names(string folder) => Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void A_game_kept_in_one_file_the_oldest_way_becomes_a_save_with_its_copies()
    {
        var saves = Store(TempRoot());
        var legacy = saves.LegacyLivePath("scn");
        PlayedTurns(3).SaveTo(legacy);
        File.WriteAllText(SaveLog.PathFor(legacy), "");
        var history = Path.Combine(saves.HistoryDir, "scn");
        Directory.CreateDirectory(history);
        File.Copy(legacy, Path.Combine(history, "turn-0002_20261101-120000" + SaveStore.Ext));

        Assert.NotEmpty(saves.ConvertToSaves(T0));

        var save = saves.CurrentSave("scn")!;
        Assert.Equal(Path.Combine(saves.SavesRoot("scn"), "Game 1" + SaveStore.Ext), save);
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(SaveLog.PathFor(save)));
        Assert.Contains(saves.Checkpoints(save), c => c.Kind == CheckpointKind.Turn);
        Assert.Equal(3, SaveLoader.Load(save).Turns.Count);
        Assert.False(Directory.Exists(history));
        Assert.Empty(saves.ConvertToSaves(T0.AddMinutes(1)));
    }

    [Fact]
    public void A_play_becomes_one_save_where_its_folder_was_and_its_turn_files_go_to_the_trash()
    {
        var saves = Store(TempRoot());
        var game = PlayedTurns(3);
        var folder = Path.Combine(saves.HistoryDir, "scn", "Campaign", "Run 2");
        OldPlay(saves, folder, "20261101-120000", game);
        File.WriteAllBytes(Path.Combine(folder, "Before the river" + SaveStore.Ext), Json.PackedBytes(game.ToSave()));
        var safety = Path.Combine(saves.HistoryDir, "scn", SaveStore.SafetyFolder, "20261101-120000");
        Directory.CreateDirectory(safety);
        File.WriteAllText(Path.Combine(safety, "time-0003_20261101-130000" + SaveStore.Ext), "{}");

        saves.ConvertToSaves(T0);

        var save = Path.Combine(saves.SavesRoot("scn"), "Campaign", "Run 2" + SaveStore.Ext);
        Assert.Equal(save, saves.CurrentSave("scn"));
        Assert.True(File.Exists(SaveLog.PathFor(save)));
        Assert.Equal(3, SaveLoader.Load(save).Turns.Count);
        Assert.Contains(saves.Checkpoints(save), c => c.Kind == CheckpointKind.Time);
        Assert.Equal(new[] { "Before the river" + SaveStore.Ext }, Names(Path.Combine(saves.SavesRoot("scn"), "Campaign", "Run 2")));
        var trashed = Assert.Single(saves.SavesTrash("scn"));
        Assert.Equal(3, Directory.GetFiles(trashed.Item).Length);
        Assert.False(Directory.Exists(Path.Combine(saves.HistoryDir, "scn")));
        Assert.False(File.Exists(Path.Combine(saves.SavesRoot("scn"), "20261101-120000" + SaveStore.Ext)));
    }

    [Fact]
    public void A_play_whose_live_save_is_gone_takes_its_newest_safety_copy_and_keeps_its_turns()
    {
        var saves = Store(TempRoot());
        var game = PlayedTurns(2);
        var folder = Path.Combine(saves.HistoryDir, "scn", "Run 1");
        OldPlay(saves, folder, "20261101-120000", game, live: false);
        var safety = Path.Combine(saves.HistoryDir, "scn", SaveStore.SafetyFolder, "20261101-120000");
        Directory.CreateDirectory(safety);
        game.SaveTo(Path.Combine(safety, "close-0002_20261101-130000" + SaveStore.Ext));

        saves.ConvertToSaves(T0);

        var save = Path.Combine(saves.SavesRoot("scn"), "Run 1" + SaveStore.Ext);
        Assert.Equal(2, SaveLoader.Load(save).State.Clock.Turn);
        Assert.Single(saves.Checkpoints(save));
        Assert.Equal(2, Directory.GetFiles(Assert.Single(saves.SavesTrash("scn")).Item).Length);
    }

    [Fact]
    public void A_save_named_in_one_word_stays_as_it_is_when_old_saves_are_converted_beside_it()
    {
        var saves = Store(TempRoot());
        var kept = saves.NewSavePath("scn", "Restored");
        PlayedTurns(1).SaveTo(kept);
        OldPlay(saves, Path.Combine(saves.HistoryDir, "scn", "Run 1"), "20261101-120000", PlayedTurns(2), current: false);
        saves.ConvertToSaves(T0);
        Assert.True(File.Exists(kept));
        Assert.Equal(1, SaveLoader.Load(kept).State.Clock.Turn);
        Assert.True(File.Exists(Path.Combine(saves.SavesRoot("scn"), "Run 1" + SaveStore.Ext)));
    }

    [Fact]
    public void The_save_on_the_table_is_the_one_marked_and_else_the_newest_written()
    {
        var saves = Store(TempRoot());
        var older = saves.NewSavePath("scn", "Older");
        PlayedTurns(1).SaveTo(older);
        File.SetLastWriteTimeUtc(older, T0);
        var newer = saves.NewSavePath("scn", "Newer");
        PlayedTurns(2).SaveTo(newer);
        Assert.Equal(newer, saves.CurrentSave("scn"));
        saves.SetCurrent("scn", older);
        Assert.Equal(older, saves.CurrentSave("scn"));
        File.Delete(older);
        Assert.Equal(newer, saves.CurrentSave("scn"));
        Assert.Equal("Game 2", saves.NextGameName("scn"));
    }

    [Fact]
    public void Renaming_or_moving_a_save_keeps_its_log_its_copies_and_its_place_on_the_table()
    {
        var saves = Store(TempRoot());
        var save = saves.NewSavePath("scn", "Game 1");
        var game = PlayedTurns(2);
        game.SaveId = "20261101-120000-abcdef";
        game.SaveTo(save);
        File.WriteAllText(SaveLog.PathFor(save), "#log");
        saves.SetCurrent("scn", save);
        saves.Checkpoint(save, CheckpointKind.Time, 2, T0);

        var renamed = saves.Rename("scn", save, "Day one");
        var folder = saves.MakeFolder("scn", saves.SavesRoot("scn"), "Campaign");
        var moved = saves.MoveSave("scn", renamed, folder);

        Assert.Equal(Path.Combine(folder, "Day one" + SaveStore.Ext), moved);
        Assert.Equal("#log", File.ReadAllText(SaveLog.PathFor(moved)));
        Assert.Equal(moved, saves.CurrentSave("scn"));
        Assert.Single(saves.Checkpoints(moved));
        File.WriteAllText(moved, "{ damaged");
        Assert.Single(saves.Checkpoints(moved));
        Assert.Equal(2, SaveLoader.Open(saves, moved, null, out var recovery).State.Clock.Turn);
        Assert.NotNull(recovery);
    }

    [Fact]
    public void The_save_on_the_table_cannot_be_renamed_moved_or_deleted()
    {
        var saves = Store(TempRoot());
        var folder = saves.MakeFolder("scn", saves.SavesRoot("scn"), "Campaign");
        var save = saves.NewSavePath("scn", "Game 1", folder);
        PlayedTurns(1).SaveTo(save);
        Assert.Throws<IOException>(() => saves.Rename("scn", save, "Other", save));
        Assert.Throws<IOException>(() => saves.MoveSave("scn", save, saves.SavesRoot("scn"), save));
        Assert.Throws<IOException>(() => saves.TrashSaves("scn", folder, T0, save));
        Assert.True(File.Exists(save));
    }

    [Fact]
    public void A_save_on_the_table_takes_no_turn_copies_but_still_the_timed_ones_and_a_mirror_keeps_few()
    {
        var saves = Store(TempRoot());
        var save = saves.NewSavePath("scn", "Game 1");
        PlayedTurns(1).SaveTo(save);
        var cp = new Checkpointer(saves, save, 1, T0);
        Assert.Null(cp.AfterSave(2, 1, 2, T0.AddMinutes(1)));
        Assert.Equal(CheckpointKind.Time, cp.AfterSave(2, 1, 2, T0.AddMinutes(11))!.Kind);

        var mirror = saves.MirrorPath("scn");
        Directory.CreateDirectory(saves.MirrorDir);
        PlayedTurns(1).SaveTo(mirror);
        var mc = new Checkpointer(saves, mirror, 1, T0);
        for (int t = 2; t < 9; t++) Assert.NotNull(mc.AfterSave(t, t - 1, t, T0.AddSeconds(t)));
        Assert.Equal(SaveStore.KeepTurns, saves.Checkpoints(mirror).Count(c => c.Kind == CheckpointKind.Turn));
        Assert.StartsWith(Path.Combine(saves.MirrorDir, SaveStore.SafetyFolder), saves.CheckpointDir(mirror));
    }

    [Fact]
    public void Deleting_saves_takes_their_logs_and_copies_to_the_trash_and_restoring_brings_them_back()
    {
        var saves = Store(TempRoot());
        var folder = saves.MakeFolder("scn", saves.SavesRoot("scn"), "Campaign");
        var save = saves.NewSavePath("scn", "Game 1", folder);
        var game = PlayedTurns(1);
        game.SaveId = "20261101-120000-abcdef";
        game.SaveTo(save);
        File.WriteAllText(SaveLog.PathFor(save), "#log");
        saves.Checkpoint(save, CheckpointKind.Time, 1, T0);

        saves.TrashSaves("scn", folder, T0);
        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(saves.SavesRoot("scn"), SaveStore.SafetyFolder, "20261101-120000-abcdef")));

        saves.Restore(Assert.Single(saves.SavesTrash("scn")).Entry, TempRoot(), T0);
        Assert.Equal("#log", File.ReadAllText(SaveLog.PathFor(save)));
        Assert.Single(saves.Checkpoints(save));

        saves.TrashSaves("scn", save, T0.AddMinutes(1));
        var single = Assert.Single(saves.SavesTrash("scn"));
        File.WriteAllText(save, "{}");
        saves.Restore(single.Entry, TempRoot(), T0);
        var back = Path.Combine(folder, "Game 1 (2)" + SaveStore.Ext);
        Assert.Equal("#log", File.ReadAllText(SaveLog.PathFor(back)));
        Assert.Single(saves.Checkpoints(back));
    }

    [Fact]
    public void A_play_deleted_before_saves_had_no_plays_comes_back_as_a_save()
    {
        var root = TempRoot();
        var saves = Store(root);
        var game = PlayedTurns(2);
        var entry = Path.Combine(saves.TrashDir, "20261101-120000 scn · Run 1 (saves)");
        Directory.CreateDirectory(entry);
        Json.Save(Path.Combine(entry, "from.json"), new SavesInTrash { Scenario = "scn", Parent = ".", Name = "Run 1" });
        var item = Path.Combine(entry, "item", "Run 1");
        Directory.CreateDirectory(item);
        Json.Save(Path.Combine(item, SaveStore.RunMarkFile), new RunMark { Id = "20261101-110000", Created = T0.ToString("o") });
        File.WriteAllBytes(Path.Combine(item, "Turn 0" + SaveStore.Ext), Json.PackedBytes(SaveFile.OfTurn("", "scn", game.Created, game.Access, game.Turns, 0)));
        Directory.CreateDirectory(Path.Combine(entry, "live"));
        game.SaveTo(Path.Combine(entry, "live", "20261101-110000" + SaveStore.Ext));

        saves.Restore(Assert.Single(saves.SavesTrash("scn")).Entry, root, T0);

        var save = Path.Combine(saves.SavesRoot("scn"), "Run 1" + SaveStore.Ext);
        Assert.Equal(2, SaveLoader.Load(save).Turns.Count);
        Assert.False(Directory.Exists(Path.Combine(saves.HistoryDir, "scn")));
    }

    [Fact]
    public void Saves_handed_over_the_old_way_come_in_as_saves()
    {
        var root = TempRoot();
        var saves = Store(root);
        var game = PlayedTurns(2);
        var savesFrom = Path.Combine(root, "in", "saves");
        var liveFrom = Path.Combine(root, "in", "live");
        Directory.CreateDirectory(Path.Combine(savesFrom, "Run 1"));
        Json.Save(Path.Combine(savesFrom, "Run 1", SaveStore.RunMarkFile), new RunMark { Id = "aaaa", Created = T0.ToString("o") });
        Directory.CreateDirectory(liveFrom);
        game.SaveTo(Path.Combine(liveFrom, "aaaa" + SaveStore.Ext));
        File.WriteAllText(Path.Combine(liveFrom, SaveStore.CurrentFile), "aaaa");

        saves.AddLegacySaves("scn", savesFrom, liveFrom, T0);

        Assert.Equal(Path.Combine(saves.SavesRoot("scn"), "Run 1" + SaveStore.Ext), saves.CurrentSave("scn"));
        Assert.Equal(2, SaveLoader.Load(saves.CurrentSave("scn")!).State.Clock.Turn);
    }

    [Fact]
    public void A_handed_over_save_lands_where_it_was_on_the_table()
    {
        var root = TempRoot();
        var saves = Store(root);
        var game = Path.Combine(root, "game" + SaveStore.Ext);
        PlayedTurns(1).SaveTo(game);
        var put = saves.AddGame("scn", game, "Handed over", Path.Combine("Campaign", "Day 2" + SaveStore.Ext));
        Assert.Equal(Path.Combine(saves.SavesRoot("scn"), "Campaign", "Day 2" + SaveStore.Ext), put);
        Assert.Equal(put, saves.CurrentSave("scn"));
        Assert.Equal(Path.Combine(saves.SavesRoot("scn"), "Handed over" + SaveStore.Ext), saves.AddGame("scn", game, "Handed over", Path.Combine("..", "out" + SaveStore.Ext)));
    }

    [Fact]
    public void Turns_are_packed_ten_together_and_every_turn_reads_back()
    {
        var game = PlayedTurns(25);
        Assert.True(game.TurnsSealed(TimeSpan.FromSeconds(30)));
        var save = game.ToSave();
        Assert.Equal(2, save.TurnBlocks!.Count);
        Assert.Equal(20, save.Turns.Count(t => t.Block != null));
        Assert.Equal(5, save.Turns.Count(t => t.Packed != null));
        var root = TempRoot();
        var path = Path.Combine(root, "g" + SaveStore.Ext);
        Json.SaveBig(path, save);
        var back = SaveLoader.Load(path);
        Assert.Equal(Enumerable.Range(0, 25), back.Turns.Select(t => t.Unpack().Clock.Turn));
        Assert.Equal(Json.Serialize(game.Turns[13].Unpack()), Json.Serialize(back.Turns[13].Unpack()));
        Json.SaveBig(path, back.ToSave());
        Assert.Equal(2, Json.Load<SaveFile>(path).TurnBlocks!.Count);
    }

    [Fact, Trait("Category", "Timing")]
    public void A_turn_whose_block_is_missing_loses_only_its_own_table()
    {
        var game = PlayedTurns(12);
        Assert.True(game.TurnsSealed(TimeSpan.FromSeconds(30)));
        var save = game.ToSave();
        save.Turns[3].Block = 7;
        var root = TempRoot();
        var path = Path.Combine(root, "g" + SaveStore.Ext);
        Json.SaveBig(path, save);
        var back = SaveLoader.Load(path);
        Assert.Equal(12, back.State.Clock.Turn);
        Assert.False(back.Turns[3].HasTable);
        Assert.True(back.Turns[4].HasTable);
    }

    const string FromTheFuture = "{\"format\":99,\"id\":\"x\",\"scenarioId\":\"scn\",\"snapshot\":{\"layers\":{\"umpire\":[{\"id\":\"p\",\"kind\":\"not-a-kind\"}]}},\"commands\":[{\"$type\":\"FutureCommand\",\"seq\":1}]}";

    [Fact]
    public void A_save_from_a_newer_version_is_refused_before_anything_in_it_is_read()
    {
        var root = TempRoot();
        var plain = Path.Combine(root, "plain" + SaveStore.Ext);
        File.WriteAllText(plain, FromTheFuture);
        var packed = Path.Combine(root, "packed" + SaveStore.Ext);
        File.WriteAllBytes(packed, Json.Packed(System.Text.Encoding.UTF8.GetBytes(FromTheFuture)));
        Assert.Contains("save format 99", Assert.Throws<SaveTooNewException>(() => SaveLoader.Load(plain)).Message);
        Assert.Contains("save format 99", Assert.Throws<SaveTooNewException>(() => SaveLoader.Load(packed)).Message);
    }

    [Fact]
    public void A_newer_safety_copy_stops_the_fallback_instead_of_an_older_copy_taking_the_save_s_place()
    {
        var saves = Store(TempRoot());
        var save = saves.NewSavePath("scn", "Game 1");
        PlayedTurns(1).SaveTo(save);
        var older = saves.Checkpoint(save, CheckpointKind.Time, 1, T0);
        var newer = saves.Checkpoint(save, CheckpointKind.Time, 5, T0.AddHours(1));
        File.WriteAllText(newer, FromTheFuture);
        File.WriteAllText(save, "{ damaged");

        var e = Assert.Throws<SaveTooNewException>(() => SaveLoader.Open(saves, save, null, out _));
        Assert.Contains("newer version", e.Message);
        Assert.Equal("{ damaged", File.ReadAllText(save));
        Assert.True(File.Exists(older));
        Assert.Equal(FromTheFuture, File.ReadAllText(newer));
    }

    [Fact]
    public void A_handed_over_game_the_app_cannot_open_is_refused_before_anything_is_installed()
    {
        string Item(string scenario, string? game, string? save)
        {
            var dir = Path.Combine(TempRoot(), "item");
            Directory.CreateDirectory(Path.Combine(dir, "saves"));
            File.WriteAllText(Path.Combine(dir, "scenario.json"), scenario);
            if (game != null) File.WriteAllText(Path.Combine(dir, "game.kstable"), game);
            if (save != null) File.WriteAllText(Path.Combine(dir, "saves", "Day 2.kstable"), save);
            return dir;
        }
        var scenario = Json.Serialize(new Scenario { Id = "scn", Name = "Feud" });
        var game = Json.Serialize(PlayedTurns(1).ToSave());
        SaveLoader.CheckHandedOver(Item(scenario, game, game), "game.kstable");
        Assert.Throws<SaveTooNewException>(() => SaveLoader.CheckHandedOver(Item(scenario, FromTheFuture, null), "game.kstable"));
        Assert.Throws<SaveTooNewException>(() => SaveLoader.CheckHandedOver(Item(scenario, game, FromTheFuture), "game.kstable"));
        Assert.Contains("scenario format 9", Assert.Throws<SaveTooNewException>(() => SaveLoader.CheckHandedOver(Item("{\"format\":9,\"name\":\"x\"}", null, null), "game.kstable")).Message);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveLoader.CheckHandedOver(Item(scenario, "{ damaged", null), "game.kstable"));
    }

    [Fact]
    public void A_name_from_the_umpire_becomes_a_file_name_windows_takes()
    {
        Assert.Equal("Turn 6 - 06h30", SaveNames.Clean("Turn 6 : 06h30"));
        Assert.Equal("a-b-c", SaveNames.Clean("a:b?c"));
        Assert.Equal("Save", SaveNames.Clean("  ...  "));
        Assert.Equal("CON_", SaveNames.Clean("CON"));
        Assert.Equal("hidden", SaveNames.Clean(".hidden"));
        Assert.Equal(80, SaveNames.Clean(new string('x', 200)).Length);
        Assert.Equal("Turn 3 · 22 Jul 06h30", SaveNames.Turn(3, "1812-07-22T06:30"));
        Assert.Equal("Turn 3", SaveNames.Turn(3, "not a time"));
        Assert.Equal("Game 1 · from Turn 3", SaveNames.From("Game 1", "Turn 3"));
    }

    [Fact]
    public void A_save_s_picture_turn_and_id_are_read_from_its_head_without_its_table()
    {
        var root = TempRoot();
        var packed = Path.Combine(root, "packed" + SaveStore.Ext);
        var srv = PlayedTurns(2);
        srv.Picture = new byte[] { 1, 2, 3 };
        srv.SaveId = "20261101-120000-abcdef";
        File.WriteAllBytes(packed, Json.PackedBytes(srv.ToSave()));
        var head = Json.ReadSaveHeadline(packed)!;
        Assert.Equal((2, 1, "scn", SaveFile.CurrentFormat), (head.Preview.Turn, head.Preview.Pieces, head.ScenarioId, head.Format));
        Assert.Equal(new byte[] { 1, 2, 3 }, head.Preview.Picture);
        Assert.Equal("20261101-120000-abcdef", Json.ReadSaveId(packed));
        var old = Path.Combine(root, "old" + SaveStore.Ext);
        File.WriteAllText(old, "{\"format\":8,\"scenarioId\":\"scn\",\"access\":{},\"snapshot\":{}}");
        Assert.Null(Json.ReadSaveHeadline(old));
        Assert.Null(Json.ReadSaveId(old));
        Assert.Equal(2, SaveLoader.Load(packed).State.Clock.Turn);
    }
}
