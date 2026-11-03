using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class MakeWayTests
{
    static readonly DateTime T0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    static string TempRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), "ktable-makeway-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        return r;
    }

    static SaveStore Store(string root) => new(Path.Combine(root, "live"), Path.Combine(root, "history"), Path.Combine(root, "trash"));

    [Fact]
    public void A_scenario_folder_goes_to_the_trash_alone_and_its_game_in_play_stays()
    {
        var root = TempRoot();
        var saves = Store(root);
        var packages = Path.Combine(root, "packages");
        Json.Save(Path.Combine(packages, "scenarios", "scn", "scenario.json"), new Scenario { Id = "scn", Name = "Played" });
        File.WriteAllText(Path.Combine(packages, "scenarios", "scn", "scenario.start.json"), "{}");
        var live = saves.LivePath("scn");
        Json.Save(live, new SaveFile { ScenarioId = "scn" });
        saves.Checkpoint(live, "turn", 1, T0);

        var entry = saves.TrashScenarioFolder(packages, "scn", T0);

        Assert.False(Directory.Exists(Path.Combine(packages, "scenarios", "scn")));
        Assert.True(File.Exists(Path.Combine(entry, "scenario", "scenario.json")));
        Assert.True(File.Exists(Path.Combine(entry, "scenario", "scenario.start.json")));
        Assert.True(saves.HasGame("scn"));
        Assert.False(File.Exists(Path.Combine(entry, "scn.kstable")));
        var e = Assert.Single(saves.TrashEntries());
        Assert.Equal("scenario", e.Kind);
        Assert.Equal("scn", e.Id);

        saves.Restore(e, packages, T0.AddMinutes(1));
        Assert.Equal("Played", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "scn", "scenario.json")).Name);
        Assert.True(File.Exists(live));
        Assert.Empty(saves.TrashEntries());
    }

    [Fact]
    public void Put_back_sends_the_half_made_copy_to_the_trash_and_brings_the_old_folder_back()
    {
        var root = TempRoot();
        var saves = Store(root);
        var packages = Path.Combine(root, "packages");
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Painted" });
        File.WriteAllText(Path.Combine(packages, "tts.feud", "art.json"), "the umpire's paint");
        Json.Save(Path.Combine(packages, "scenarios", "feud", "scenario.json"), new Scenario { Id = "tts.feud", Name = "Old" });

        var packEntry = saves.TrashPackFolder(packages, "tts.feud", T0);
        var scenarioEntry = saves.TrashScenarioFolder(packages, "feud", T0);
        Json.Save(Path.Combine(packages, "tts.feud", "pack.json"), new PackManifest { Id = "tts.feud", Name = "Half" });
        Directory.CreateDirectory(Path.Combine(packages, "scenarios", "feud", "tiles"));

        foreach (var path in new[] { packEntry, scenarioEntry })
            saves.PutBack(saves.TrashEntries().Single(x => x.Path == path), packages, T0.AddMinutes(1));

        Assert.Equal("Painted", Json.Load<PackManifest>(Path.Combine(packages, "tts.feud", "pack.json")).Name);
        Assert.Equal("the umpire's paint", File.ReadAllText(Path.Combine(packages, "tts.feud", "art.json")));
        Assert.Equal("Old", Json.Load<Scenario>(Path.Combine(packages, "scenarios", "feud", "scenario.json")).Name);
        Assert.False(Directory.Exists(Path.Combine(packages, "scenarios", "feud", "tiles")));
        var left = saves.TrashEntries();
        Assert.Equal(2, left.Count);
        Assert.Equal("Half", Json.Load<PackManifest>(Path.Combine(left.Single(e => e.Kind == "pack").Path, "pack", "pack.json")).Name);
        Assert.True(Directory.Exists(Path.Combine(left.Single(e => e.Kind == "scenario").Path, "scenario", "tiles")));
    }

    [Fact]
    public void A_folder_that_cannot_move_stays_whole_where_it_was()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = TempRoot();
        var saves = Store(root);
        var packages = Path.Combine(root, "packages");
        Json.Save(Path.Combine(packages, "scenarios", "scn", "scenario.json"), new Scenario { Id = "scn", Name = "S" });
        Json.Save(Path.Combine(packages, "mypack", "pack.json"), new PackManifest { Id = "mypack", Name = "P" });
        File.WriteAllText(Path.Combine(packages, "mypack", "b.png"), "picture");
        using (File.Open(Path.Combine(packages, "scenarios", "scn", "scenario.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (File.Open(Path.Combine(packages, "mypack", "pack.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => saves.TrashScenarioFolder(packages, "scn", T0));
            Assert.ThrowsAny<Exception>(() => saves.TrashPackFolder(packages, "mypack", T0));
        }
        Assert.True(File.Exists(Path.Combine(packages, "scenarios", "scn", "scenario.json")));
        Assert.True(File.Exists(Path.Combine(packages, "mypack", "b.png")));
        Assert.Empty(saves.TrashEntries());
        Assert.True(!Directory.Exists(saves.TrashDir) || Directory.GetDirectories(saves.TrashDir).Length == 0);
    }

    [Fact]
    public void A_packages_root_of_its_own_keeps_its_trash_under_it()
    {
        var root = TempRoot();
        var saves = SaveStore.ForRoot(root);
        Assert.Equal(Path.Combine(root, "_saves", "trash"), saves.TrashDir);
        Assert.Equal(Path.Combine(root, "_saves", "live"), saves.LiveDir);
        Assert.Equal(SaveStore.Default.TrashDir, SaveStore.ForRoot(PackageStore.DefaultRoot).TrashDir);
    }
}
