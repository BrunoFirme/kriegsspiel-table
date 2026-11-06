using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class RunsTests
{
    static readonly DateTime T0 = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    static string TempRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-runs-tests", Guid.NewGuid().ToString("N"));
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

    static string[] SavesIn(string folder) => Directory.GetFiles(folder, "*" + SaveStore.Ext).Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray()!;

    [Fact]
    public void A_game_kept_the_old_way_becomes_the_first_play_with_its_copies_kept_and_every_turn_saved()
    {
        var saves = Store(TempRoot());
        var legacy = saves.LegacyLivePath("scn");
        PlayedTurns(3).SaveTo(legacy);
        File.WriteAllText(SaveLog.PathFor(legacy), "");
        Directory.CreateDirectory(saves.SavesRoot("scn"));
        var oldCopy = Path.Combine(saves.SavesRoot("scn"), "turn-0002_20261101-120000" + SaveStore.Ext);
        File.Copy(legacy, oldCopy);

        Assert.True(saves.HasGame("scn"));
        Assert.Equal(legacy, saves.LivePath("scn"));
        Assert.Single(saves.MigrateToRuns(T0));

        var live = saves.LivePath("scn");
        Assert.Equal(saves.LivePath("scn", SaveStore.MigratedRun), live);
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(SaveLog.PathFor(live)));
        Assert.Contains(saves.Checkpoints(live), c => c.Kind == CheckpointKind.Turn && Path.GetFileName(c.Path) == Path.GetFileName(oldCopy));
        var run = Assert.Single(saves.Runs("scn"));
        Assert.Equal(("Run 1", true), (Path.GetFileName(run.Folder), run.Mark.Backfill));
        Assert.Empty(saves.MigrateToRuns(T0.AddMinutes(1)));

        var (dir, toFill) = Assert.Single(saves.RunsToFill());
        Assert.Equal(3, saves.FillRun(dir, toFill));
        Assert.Equal(new[] { "Turn 0 · 1 Jan 06h00", "Turn 1 · 1 Jan 06h15", "Turn 2 · 1 Jan 06h30" }, SavesIn(run.Folder));
        Assert.Empty(saves.RunsToFill());
        var turn1 = Json.Load<SaveFile>(Path.Combine(run.Folder, "Turn 1 · 1 Jan 06h15" + SaveStore.Ext));
        Assert.Equal((1, 1), (turn1.Snapshot.Clock.Turn, turn1.Turns.Count));
        Assert.Equal(1, Json.ReadSaveHeadline(Path.Combine(run.Folder, "Turn 1 · 1 Jan 06h15" + SaveStore.Ext))!.Preview.Turn);
        Assert.True(saves.HasGame("scn"));
    }

    [Fact]
    public void A_move_cut_off_after_the_live_save_went_finishes_on_the_next_start()
    {
        var saves = Store(TempRoot());
        var legacy = saves.LegacyLivePath("scn");
        PlayedTurns(1).SaveTo(legacy);
        Directory.CreateDirectory(saves.SavesRoot("scn"));
        File.Copy(legacy, Path.Combine(saves.SavesRoot("scn"), "time-0001_20261101-120000" + SaveStore.Ext));
        Directory.CreateDirectory(Path.GetDirectoryName(saves.LivePath("scn", SaveStore.MigratedRun))!);
        File.Move(legacy, saves.LivePath("scn", SaveStore.MigratedRun));

        Assert.True(saves.HasGame("scn"));
        saves.MigrateToRuns(T0);
        Assert.Single(saves.Runs("scn"));
        Assert.Single(saves.Checkpoints(saves.LivePath("scn")), c => c.Kind == CheckpointKind.Time);
        Assert.Empty(Directory.GetFiles(saves.SavesRoot("scn"), "*" + SaveStore.Ext));
    }

    [Fact]
    public void The_play_in_play_is_the_one_marked_and_else_the_newest_written_one()
    {
        var saves = Store(TempRoot());
        Assert.Null(saves.CurrentRun("scn"));
        Assert.False(saves.HasGame("scn"));
        Json.Save(saves.LivePath("scn", "a"), new SaveFile { ScenarioId = "scn" });
        Json.Save(saves.LivePath("scn", "b"), new SaveFile { ScenarioId = "scn" });
        File.SetLastWriteTimeUtc(saves.LivePath("scn", "a"), T0);
        File.SetLastWriteTimeUtc(saves.LivePath("scn", "b"), T0.AddHours(1));
        Assert.Equal("b", saves.CurrentRun("scn"));
        saves.SetCurrent("scn", "a");
        Assert.Equal("a", saves.CurrentRun("scn"));
        saves.SetCurrent("scn", "gone");
        Assert.Equal("b", saves.CurrentRun("scn"));
        File.WriteAllText(Path.Combine(saves.LiveDir, "scn", SaveStore.CurrentFile), "..\\elsewhere");
        Assert.Equal("b", saves.CurrentRun("scn"));
        var setAside = Path.Combine(saves.LiveDir, "scn", "a.unreadable-20261102-120000" + SaveStore.Ext);
        Json.Save(setAside, new SaveFile { ScenarioId = "scn" });
        File.SetLastWriteTimeUtc(setAside, T0.AddHours(2));
        Assert.Equal("b", saves.CurrentRun("scn"));
    }

    [Fact]
    public void A_play_s_folder_renamed_or_moved_outside_the_app_stays_its_folder()
    {
        var saves = Store(TempRoot());
        var folder = saves.MakeRun("scn", "r1", "Run 1", null, T0);
        Assert.Equal(folder, saves.RunFolderOf("scn", "r1"));
        var moved = Path.Combine(saves.SavesRoot("scn"), "Campaign", "The first group");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(folder, moved);
        Assert.Equal(moved, saves.RunFolderOf("scn", "r1"));
        Json.Save(saves.LivePath("scn", "r1"), new SaveFile { ScenarioId = "scn" });
        Assert.Equal(moved, saves.EnsureRunFolder("scn", "r1", T0));
        Directory.Delete(Path.Combine(saves.SavesRoot("scn"), "Campaign"), true);
        var again = saves.EnsureRunFolder("scn", "r1", T0);
        Assert.Equal("Run 1", Path.GetFileName(again));
    }

    [Fact]
    public void A_damaged_live_save_comes_back_from_the_newest_save_of_its_own_play()
    {
        var saves = Store(TempRoot());
        var srv = PlayedTurns(2);
        var live = saves.LivePath("scn", "r1");
        srv.SaveTo(live);
        saves.MakeRun("scn", "r1", "Run 1", null, T0);
        saves.WriteTurnSave("scn", "r1", SaveFile.OfTurn("scn", srv.Created, srv.Access, srv.Turns, 1, null), T0);
        var other = saves.LivePath("scn", "r2");
        PlayedTurns(5).SaveTo(other);
        saves.MakeRun("scn", "r2", "Run 2", null, T0);
        saves.WriteTurnSave("scn", "r2", SaveFile.OfTurn("scn", srv.Created, srv.Access, PlayedTurns(5).Turns, 4, null), T0);
        File.WriteAllText(live, "{\"format\":9,\"scen");

        var back = SaveLoader.Open(saves, live, null, out var report);
        Assert.Equal(CheckpointKind.Saved, report!.From.Kind);
        Assert.Equal(1, back.State.Clock.Turn);
        Assert.Single(back.Turns);
    }

    [Fact]
    public void A_play_on_the_table_takes_no_turn_copies_of_its_own_but_still_the_timed_ones()
    {
        var saves = Store(TempRoot());
        var live = saves.LivePath("scn", "r1");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        var ck = new Checkpointer(saves, live, 0, T0);
        Assert.Null(ck.AfterSave(1, 0, 1, T0.AddSeconds(1)));
        Assert.Equal(CheckpointKind.Time, ck.AfterSave(1, 0, 1, T0.AddMinutes(11))!.Kind);
        Assert.StartsWith(Path.Combine(saves.SavesRoot("scn"), SaveStore.SafetyFolder, "r1"), Assert.Single(saves.Checkpoints(live)).Path);
    }

    [Fact]
    public void Deleting_saves_goes_to_the_trash_with_the_plays_inside_and_comes_back_where_it_was()
    {
        var saves = Store(TempRoot());
        var root = saves.SavesRoot("scn");
        var group = saves.MakeFolder("scn", root, "Second group");
        var run = saves.MakeRun("scn", "r2", "Run 2", null, T0);
        var inGroup = saves.MoveSave("scn", run, group);
        Json.Save(saves.LivePath("scn", "r2"), new SaveFile { ScenarioId = "scn" });
        var kept = saves.SaveInto("scn", inGroup, "Before the attack", Json.PackedBytes(new SaveFile { ScenarioId = "scn" }), false, T0);

        Assert.Throws<IOException>(() => saves.TrashSaves("scn", group, T0, runInPlay: "r2"));
        Assert.Throws<IOException>(() => saves.TrashSaves("scn", root, T0));
        Assert.Throws<IOException>(() => saves.MoveSave("scn", group, inGroup));

        var single = saves.TrashSaves("scn", kept, T0);
        Assert.False(File.Exists(kept));
        var listed = Assert.Single(saves.SavesTrash("scn"));
        Assert.Equal(("Before the attack", false), (listed.Name, listed.Folder));
        Assert.Empty(saves.SavesTrash("other"));
        var entry = saves.TrashSaves("scn", group, T0.AddSeconds(1));
        Assert.False(Directory.Exists(group));
        Assert.False(File.Exists(saves.LivePath("scn", "r2")));
        Assert.Equal(2, saves.TrashEntries().Count(e => e.Kind == TrashKind.Saves));

        saves.Restore(saves.TrashEntries().Single(e => e.Path == entry), Path.Combine(TempRoot(), "packages"), T0.AddMinutes(1));
        Assert.True(File.Exists(saves.LivePath("scn", "r2")));
        Assert.Equal(Path.Combine(group, "Run 2"), saves.RunFolderOf("scn", "r2"));
        saves.Restore(saves.TrashEntries().Single(e => e.Path == single), Path.Combine(TempRoot(), "packages"), T0.AddMinutes(2));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void Saves_brought_in_keep_their_folders_and_a_play_whose_id_is_taken_gets_its_own()
    {
        var saves = Store(TempRoot());
        Json.Save(saves.LivePath("scn", "r1"), new SaveFile { ScenarioId = "scn", Snapshot = new GameState { Name = "here" } });
        saves.MakeRun("scn", "r1", "Run 1", null, T0);

        var brought = TempRoot();
        var live = Path.Combine(brought, "live");
        Directory.CreateDirectory(live);
        Json.Save(Path.Combine(live, "r1" + SaveStore.Ext), new SaveFile { ScenarioId = "scn", Snapshot = new GameState { Name = "brought" } });
        File.WriteAllText(Path.Combine(live, SaveStore.CurrentFile), "r1");
        var tree = Path.Combine(brought, "saves", "Run 1");
        Directory.CreateDirectory(tree);
        Json.WriteAtomic(Path.Combine(tree, SaveStore.RunMarkFile), Json.Serialize(new RunMark { Id = "r1", Created = "x" }));
        Json.Save(Path.Combine(tree, "Turn 3" + SaveStore.Ext), new SaveFile { ScenarioId = "scn" });

        saves.AddSaves("scn", Path.Combine(brought, "saves"), live, T0);

        Assert.Equal("here", Json.Load<SaveFile>(saves.LivePath("scn", "r1")).Snapshot.Name);
        var current = saves.CurrentRun("scn")!;
        Assert.NotEqual("r1", current);
        Assert.Equal("brought", Json.Load<SaveFile>(saves.LivePath("scn")).Snapshot.Name);
        Assert.Equal("Run 1 (2)", Path.GetFileName(saves.RunFolderOf("scn", current)));
        Assert.Equal(new[] { "Turn 3" }, SavesIn(saves.RunFolderOf("scn", current)!));
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
    }

    [Fact]
    public void A_save_s_picture_and_turn_are_read_from_its_head_without_its_table()
    {
        var root = TempRoot();
        var packed = Path.Combine(root, "packed" + SaveStore.Ext);
        var srv = PlayedTurns(2);
        srv.Picture = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(packed, Json.PackedBytes(srv.ToSave()));
        var head = Json.ReadSaveHeadline(packed)!;
        Assert.Equal((2, 1, "scn"), (head.Preview.Turn, head.Preview.Pieces, head.ScenarioId));
        Assert.Equal(new byte[] { 1, 2, 3 }, head.Preview.Picture);
        var old = Path.Combine(root, "old" + SaveStore.Ext);
        File.WriteAllText(old, "{\"format\":8,\"scenarioId\":\"scn\",\"access\":{},\"snapshot\":{}}");
        Assert.Null(Json.ReadSaveHeadline(old));
        Assert.Equal(2, SaveLoader.Load(packed).State.Clock.Turn);
    }
}
