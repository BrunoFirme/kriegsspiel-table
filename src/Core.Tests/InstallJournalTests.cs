using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class InstallJournalTests
{
    static readonly DateTime T0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateTime T1 = T0.AddMinutes(5);
    static readonly Func<int, long, bool> Gone = (_, _) => false;

    static (string packages, SaveStore saves) Fresh()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-journal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        return (Path.Combine(r, "packages"), new SaveStore(Path.Combine(r, "live"), Path.Combine(r, "history"), Path.Combine(r, "trash")));
    }

    static void UmpiresFolders(string packages)
    {
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Painted" });
        Directory.CreateDirectory(Path.Combine(packages, "tts.feud", "art"));
        File.WriteAllText(Path.Combine(packages, "tts.feud", "art", "face.json"), "the umpire's layers");
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Played" });
    }

    static InstallJournal MakeWayForImport(string packages, SaveStore saves)
    {
        var j = InstallJournal.Begin(packages, saves, "the import of 'Feud'", T0);
        Assert.NotNull(j.MakeWay("pack", "tts.feud", T0));
        Assert.Null(j.MakeWay("pack", "tts.feud.extras", T0));
        Assert.NotNull(j.MakeWay("scenario", "feud", T0));
        return j;
    }

    static string[] Journals(string packages) =>
        Directory.Exists(Path.Combine(packages, InstallJournal.Folder)) ? Directory.GetFiles(Path.Combine(packages, InstallJournal.Folder), "*.journal.json") : Array.Empty<string>();

    static void AssertUmpiresFoldersBack(string packages)
    {
        Assert.Equal("Painted", Json.Load<PackManifest>(Path.Combine(packages, "tts.feud", "pack.json")).Name);
        Assert.Equal("the umpire's layers", File.ReadAllText(Path.Combine(packages, "tts.feud", "art", "face.json")));
        Assert.Equal("Played", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "feud", "scenario.json")).Name);
    }

    static void Edit(string journal, Action<InstallJournal.Record> change)
    {
        var r = Json.Load<InstallJournal.Record>(journal);
        change(r);
        Json.WriteAtomic(journal, Json.Serialize(r, indented: true));
    }

    [Fact]
    public void The_journal_is_on_the_disk_before_anything_moves_and_names_where_it_goes()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = InstallJournal.Begin(packages, saves, "the import of 'Feud'", T0);
        var r = Json.Load<InstallJournal.Record>(j.FilePath);
        Assert.Empty(r.Steps);
        Assert.True(Directory.Exists(Path.Combine(packages, "tts.feud")));

        var entry = j.MakeWay("pack", "tts.feud", T0);
        r = Json.Load<InstallJournal.Record>(j.FilePath);
        var step = Assert.Single(r.Steps);
        Assert.Equal(entry, step.Entry);
        Assert.True(step.Moved);
        Assert.True(Directory.Exists(Path.Combine(entry!, "pack")));
    }

    [Fact]
    public void Cut_off_after_the_old_folders_went_to_the_trash_puts_them_back()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        MakeWayForImport(packages, saves);
        Assert.False(Directory.Exists(Path.Combine(packages, "tts.feud")));

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));

        AssertUmpiresFoldersBack(packages);
        Assert.Empty(u.Problems);
        Assert.Contains("the pack tts.feud", u.Back);
        Assert.Contains("the scenario feud", u.Back);
        Assert.Empty(u.Trashed);
        Assert.Empty(saves.TrashEntries());
        Assert.Empty(Journals(packages));
        Assert.Contains("cut off", u.Describe());
    }

    [Fact]
    public void Cut_off_half_way_through_building_sends_what_it_had_begun_to_the_trash()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        MakeWayForImport(packages, saves);
        Directory.CreateDirectory(Path.Combine(packages, "tts.feud", "assets"));
        File.WriteAllText(Path.Combine(packages, "tts.feud", "assets", "a.png"), "new picture");
        Json.Save(Path.Combine(packages, "tts.feud.extras", "pack.json"), new PackManifest { Id = "tts.feud.extras", Name = "New extras" });
        Directory.CreateDirectory(Path.Combine(packages, "scenarios", "feud", "tiles", "abc", "0"));
        File.WriteAllText(Path.Combine(packages, "scenarios", "feud", "tiles", "abc", "0", "0_0.png"), "tile");

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));

        AssertUmpiresFoldersBack(packages);
        Assert.False(File.Exists(Path.Combine(packages, "tts.feud", "assets", "a.png")));
        Assert.False(Directory.Exists(Path.Combine(packages, "tts.feud.extras")));
        Assert.False(Directory.Exists(Path.Combine(packages, "scenarios", "feud", "tiles")));
        Assert.Empty(u.Problems);
        Assert.Equal(3, u.Trashed.Count);
        var trash = saves.TrashEntries();
        Assert.Equal(3, trash.Count);
        Assert.Equal("new picture", File.ReadAllText(Path.Combine(trash.Single(e => e.Id == "tts.feud").Path, "pack", "assets", "a.png")));
        Assert.Equal("New extras", Json.Load<PackManifest>(Path.Combine(trash.Single(e => e.Id == "tts.feud.extras").Path, "pack", "pack.json")).Name);
        Assert.True(File.Exists(Path.Combine(trash.Single(e => e.Kind == "scenario").Path, "scenario", "tiles", "abc", "0", "0_0.png")));
        Assert.Empty(Journals(packages));
    }

    [Fact]
    public void Cut_off_after_success_before_the_journal_was_removed_keeps_the_new_folders()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Converted again" });
        Json.Save(Path.Combine(packages, "tts.feud.extras", "pack.json"), new PackManifest { Id = "tts.feud.extras", Name = "Extras" });
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Fresh" });
        Edit(j.FilePath, r => r.Finished = true);

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));

        Assert.True(u.WasFinished);
        Assert.False(u.Any);
        Assert.Equal("Converted again", Json.Load<PackManifest>(Path.Combine(packages, "tts.feud", "pack.json")).Name);
        Assert.Equal("Extras", Json.Load<PackManifest>(Path.Combine(packages, "tts.feud.extras", "pack.json")).Name);
        Assert.Equal("Fresh", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "feud", "scenario.json")).Name);
        Assert.Equal(2, saves.TrashEntries().Count);
        Assert.Empty(Journals(packages));
    }

    [Fact]
    public void A_finished_work_removes_its_journal()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        j.Finish();
        Assert.Empty(Journals(packages));
        Assert.Empty(InstallJournal.RecoverAll(packages, saves, T1, Gone));
    }

    [Fact]
    public void Cut_off_before_a_folder_moved_leaves_it_where_it_was()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = InstallJournal.Begin(packages, saves, "the import of 'Feud'", T0);
        j.MakeWay("pack", "tts.feud", T0);
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Half" });
        Edit(j.FilePath, r => r.Steps.Add(new InstallJournal.Step { Kind = "scenario", Id = "feud", Entry = Path.Combine(saves.TrashDir, "20261031-120000 feud") }));

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));

        AssertUmpiresFoldersBack(packages);
        Assert.Empty(u.Problems);
        Assert.Equal(new[] { "the pack tts.feud" }, u.Back);
        Assert.Equal("Half", Json.Load<PackManifest>(Path.Combine(Assert.Single(saves.TrashEntries()).Path, "pack", "pack.json")).Name);
    }

    [Fact]
    public void A_move_made_but_not_yet_written_down_is_still_undone()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Edit(j.FilePath, r => { foreach (var s in r.Steps) s.Moved = false; });

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));

        AssertUmpiresFoldersBack(packages);
        Assert.Empty(u.Problems);
        Assert.Empty(saves.TrashEntries());
    }

    [Fact]
    public void A_journal_whose_work_is_still_running_is_left_alone()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        MakeWayForImport(packages, saves);
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Being built" });

        Assert.Empty(InstallJournal.RecoverAll(packages, saves, T1, (_, _) => true));
        Assert.Empty(InstallJournal.RecoverAll(packages, saves, T1));

        Assert.Equal("Being built", Json.Load<PackManifest>(Path.Combine(packages, "tts.feud", "pack.json")).Name);
        Assert.Single(Journals(packages));
    }

    [Fact]
    public void An_undo_cut_off_in_its_turn_takes_up_where_it_stopped()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Directory.CreateDirectory(Path.Combine(packages, "scenarios", "feud", "tiles"));
        var scenarioEntry = Json.Load<InstallJournal.Record>(j.FilePath).Steps.Single(s => s.Kind == "scenario").Entry!;
        saves.PutBack(saves.TrashEntries().Single(e => e.Path == scenarioEntry), packages, T1);

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1.AddMinutes(1), Gone));

        AssertUmpiresFoldersBack(packages);
        Assert.Empty(u.Problems);
        Assert.Contains("the pack tts.feud", u.Back);
        Assert.Empty(Journals(packages));
    }

    [Fact]
    public void Rolling_back_while_running_puts_everything_back_the_same_way()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Half" });

        var u = j.RollBack(T1);

        AssertUmpiresFoldersBack(packages);
        Assert.Empty(u.Problems);
        Assert.Equal(new[] { "the pack tts.feud" }, u.Trashed);
        Assert.Empty(Journals(packages));
    }

    [Fact]
    public void A_game_set_aside_by_an_older_install_cut_off_comes_back_as_a_play_of_its_own()
    {
        var (packages, saves) = Fresh();
        var entry = saves.NewGameEntry("feud", T0);
        Directory.CreateDirectory(Path.Combine(entry, "checkpoints"));
        Json.Save(Path.Combine(entry, "feud" + SaveStore.Ext), new SaveFile { ScenarioId = "in play here" });
        Json.Save(Path.Combine(entry, "checkpoints", "turn-0001_20261031-120000" + SaveStore.Ext), new SaveFile { ScenarioId = "in play here" });
        var j = InstallJournal.Begin(packages, saves, "the install of scenario 'Feud'", T0);
        var record = Json.Load<InstallJournal.Record>(j.FilePath);
        record.Steps.Add(new InstallJournal.Step { Kind = "game", Id = "feud", Entry = entry, Moved = true });
        Json.WriteAtomic(j.FilePath, Json.Serialize(record, indented: true));

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));

        Assert.Empty(u.Problems);
        Assert.Equal("in play here", Json.Load<SaveFile>(saves.LivePath("feud")).ScenarioId);
        Assert.Single(saves.Checkpoints(saves.LivePath("feud")), c => c.Kind == CheckpointKind.Turn);
        Assert.Empty(saves.TrashEntries());
    }

    [Fact]
    public void An_undo_that_cannot_finish_tries_once_more_with_care_and_then_lets_go()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Half" });
        Undone u;
        using (File.Open(Path.Combine(packages, "scenarios", "feud", "scenario.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            u = j.RollBack(T1);
        Assert.Contains(u.Problems, p => p.Contains("the scenario feud"));
        Assert.Contains("the pack tts.feud", u.Back);
        var left = Json.Load<InstallJournal.Record>(Assert.Single(Journals(packages)));
        Assert.True(left.Abandoned);
        Assert.Equal("feud", Assert.Single(left.Steps).Id);

        var again = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1.AddMinutes(1), Gone));
        Assert.Single(again.Problems);
        Assert.Equal("Half", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "feud", "scenario.json")).Name);
        Assert.Contains(saves.TrashEntries(), e => e.Kind == "scenario" && Json.Load<Scenario>(Path.Combine(e.Path, "scenario", "scenario.json")).Name == "Played");
        Assert.Empty(Journals(packages));
    }

    [Fact]
    public void An_undo_that_cannot_finish_or_write_its_record_is_still_tried_again_with_care()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Half" });
        Undone u;
        Directory.CreateDirectory(j.FilePath + ".tmp");
        using (File.Open(Path.Combine(packages, "scenarios", "feud", "scenario.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            u = j.RollBack(T1);
        Directory.Delete(j.FilePath + ".tmp");
        Assert.Contains(u.Problems, p => p.Contains("the scenario feud"));
        Assert.NotEmpty(u.Notes);
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Worked on since" });

        var again = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1.AddMinutes(1), Gone));
        Assert.Single(again.Problems);
        Assert.Equal("Worked on since", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "feud", "scenario.json")).Name);
        Assert.Empty(Journals(packages));
    }

    [Fact]
    public void A_finished_work_whose_journal_cannot_be_written_or_removed_is_not_undone()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = MakeWayForImport(packages, saves);
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Converted again" });
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Fresh" });
        Directory.CreateDirectory(j.FilePath + ".tmp");
        string? problem;
        using (File.Open(j.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            problem = j.Finish();
        Directory.Delete(j.FilePath + ".tmp");
        Assert.Null(problem);

        var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T1, Gone));
        Assert.True(u.WasFinished);
        Assert.Equal("Converted again", Json.Load<PackManifest>(Path.Combine(packages, "tts.feud", "pack.json")).Name);
        Assert.Equal("Fresh", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "feud", "scenario.json")).Name);
        Assert.Empty(Journals(packages));
        Assert.Empty(Directory.GetFiles(Path.Combine(packages, InstallJournal.Folder)));
    }

    [Fact]
    public void A_folder_made_way_for_twice_in_one_work_is_refused()
    {
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = InstallJournal.Begin(packages, saves, "an install", T0);
        j.MakeWay("pack", "tts.feud", T0);
        Directory.CreateDirectory(Path.Combine(packages, "tts.feud"));
        Assert.Throws<InvalidOperationException>(() => j.MakeWay("pack", "tts.feud", T0));
    }

    [Fact]
    public void A_folder_that_cannot_move_is_taken_off_the_journal_again()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (packages, saves) = Fresh();
        UmpiresFolders(packages);
        var j = InstallJournal.Begin(packages, saves, "an install", T0);
        using (File.Open(Path.Combine(packages, "tts.feud", "pack.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => j.MakeWay("pack", "tts.feud", T0));
        Assert.Empty(Json.Load<InstallJournal.Record>(j.FilePath).Steps);
        Assert.Empty(InstallJournal.RecoverAll(packages, saves, T1, Gone).SelectMany(x => x.Back.Concat(x.Trashed).Concat(x.Problems)));
        AssertUmpiresFoldersBack(packages);
    }

    [Fact]
    public void An_unreadable_record_is_set_aside_and_one_that_cannot_be_says_it_will_be_seen_again()
    {
        var (packages, saves) = Fresh();
        var dir = Directory.CreateDirectory(Path.Combine(packages, InstallJournal.Folder)).FullName;
        var bad = Path.Combine(dir, "x.journal.json");
        File.WriteAllText(bad, "not json");
        using (new FileStream(bad, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var u = Assert.Single(InstallJournal.RecoverAll(packages, saves, T0, Gone));
            Assert.Contains("could not be set aside either", Assert.Single(u.Problems));
        }
        Assert.True(File.Exists(bad));
        var again = Assert.Single(InstallJournal.RecoverAll(packages, saves, T0, Gone));
        Assert.DoesNotContain("set aside either", Assert.Single(again.Problems));
        Assert.Equal(new[] { bad + ".unreadable" }, Directory.GetFiles(dir));
    }
}

public sealed class PackChangesTests
{
    static string Pack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-packchanges-tests", Guid.NewGuid().ToString("N"), "tts.feud");
        Json.Save(Path.Combine(dir, "pack.json"), new PackManifest { Id = "tts.feud", Name = "Feud", Pieces = { new PieceDef { Id = "tts.feud.a", Name = "A" } } });
        return dir;
    }

    [Fact]
    public void No_folder_is_nothing_to_replace() =>
        Assert.Equal(PackChange.None, PackChanges.Check(Path.Combine(Path.GetTempPath(), "ktable-packchanges-tests", Guid.NewGuid().ToString("N"))).change);

    [Fact]
    public void A_sealed_pack_left_alone_is_as_converted()
    {
        var dir = Pack();
        PackChanges.Seal(dir);
        Directory.CreateDirectory(Path.Combine(dir, "art", "src"));
        File.WriteAllText(Path.Combine(dir, "art", "src", "abc.png"), "x");
        File.WriteAllText(Path.Combine(dir, "art", "mesh.thumb.png"), "x");
        Assert.Equal(PackChange.AsConverted, PackChanges.Check(dir).change);
    }

    [Fact]
    public void Any_rewrite_of_a_sealed_pack_is_a_change()
    {
        var dir = Pack();
        PackChanges.Seal(dir);
        var m = Json.Load<PackManifest>(Path.Combine(dir, "pack.json"));
        m.Pieces[0].Name = "Renamed in the studio";
        Json.SaveBig(Path.Combine(dir, "pack.json"), m);
        Assert.Equal(PackChange.Changed, PackChanges.Check(dir).change);

        Json.Save(Path.Combine(dir, "pack.json"), m);
        Assert.Equal(PackChange.Changed, PackChanges.Check(dir).change);
    }

    [Fact]
    public void A_document_of_a_painted_face_or_a_built_model_is_a_change()
    {
        var dir = Pack();
        PackChanges.Seal(dir);
        Directory.CreateDirectory(Path.Combine(dir, "art"));
        File.WriteAllText(Path.Combine(dir, "art", "0123abcd.model.json"), "{}");
        Assert.Equal(PackChange.Changed, PackChanges.Check(dir).change);
    }

    [Fact]
    public void An_unsealed_pack_last_written_by_the_studio_is_a_change()
    {
        var dir = Pack();
        Json.SaveBig(Path.Combine(dir, "pack.json"), Json.Load<PackManifest>(Path.Combine(dir, "pack.json")));
        Assert.Equal(PackChange.Changed, PackChanges.Check(dir).change);
    }

    [Fact]
    public void An_unsealed_pack_written_indented_cannot_be_told_and_is_not_taken_for_untouched()
    {
        var dir = Pack();
        var (change, why) = PackChanges.Check(dir);
        Assert.Equal(PackChange.Unknown, change);
        Assert.Contains("record", why);
    }
}
