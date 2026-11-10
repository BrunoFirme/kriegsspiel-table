using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class SaveLockTests : IDisposable
{
    static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-lock-tests", Guid.NewGuid().ToString("N"));
    readonly SaveStore _saves;

    public SaveLockTests() => _saves = new(Path.Combine(_root, "live"), Path.Combine(_root, "history"), Path.Combine(_root, "trash"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    string Save(string name, string? folder = null)
    {
        var path = _saves.NewSavePath("scn", name, folder);
        Json.Save(path, new SaveFile { ScenarioId = "scn" });
        return path;
    }

    [Fact]
    public void A_save_held_by_one_copy_is_refused_to_another_and_free_again_once_let_go()
    {
        var path = Save("Game 1");
        using (SaveLock.Take(path))
        {
            Assert.True(SaveLock.IsTaken(path));
            var e = Assert.Throws<SaveInUseException>(() => SaveLock.Take(path));
            Assert.Contains("open in another copy of the app", e.Message);
            Assert.Contains("(since ", e.Message);
        }
        Assert.False(SaveLock.IsTaken(path));
        Assert.False(File.Exists(SaveLock.PathFor(path)));
        using var again = SaveLock.Take(path);
        Assert.NotNull(again);
    }

    [Fact]
    public void A_lock_left_behind_by_a_crash_is_taken_again()
    {
        var path = Save("Game 1");
        File.WriteAllText(SaveLock.PathFor(path), "2026-11-01T10:00:00.0000000Z");
        Assert.False(SaveLock.IsTaken(path));
        using var held = SaveLock.Take(path);
        Assert.True(SaveLock.IsTaken(path));
    }

    [Fact]
    public void A_held_save_cannot_be_renamed_moved_or_deleted_nor_its_folder_scenario_or_game()
    {
        var folder = _saves.MakeFolder("scn", _saves.SavesRoot("scn"), "Campaign");
        var path = Save("Game 1", folder);
        var packages = Path.Combine(_root, "packages");
        Json.Save(Path.Combine(packages, "scenarios", "scn", "scenario.json"), new Scenario { Id = "scn", Name = "S" });
        using (SaveLock.Take(path))
        {
            Assert.Throws<SaveInUseException>(() => _saves.Rename("scn", path, "Other"));
            Assert.Throws<SaveInUseException>(() => _saves.MoveSave("scn", path, _saves.SavesRoot("scn")));
            Assert.Throws<SaveInUseException>(() => _saves.TrashSaves("scn", path, T0));
            Assert.Throws<SaveInUseException>(() => _saves.TrashSaves("scn", folder, T0));
            Assert.Throws<SaveInUseException>(() => _saves.TrashGame("scn", T0));
            Assert.Throws<SaveInUseException>(() => _saves.TrashScenario(packages, "scn", T0));
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(Path.Combine(packages, "scenarios", "scn", "scenario.json")));
            Assert.Empty(_saves.TrashEntries());
        }
        Assert.EndsWith("Other" + SaveStore.Ext, _saves.Rename("scn", path, "Other"));
    }

    [Fact]
    public void A_new_save_never_takes_the_name_of_one_being_opened()
    {
        var first = _saves.NewSavePath("scn", "Game 1");
        using var held = SaveLock.Take(first);
        Assert.Equal(Path.Combine(_saves.SavesRoot("scn"), "Game 1 (2)" + SaveStore.Ext), _saves.NewSavePath("scn", "Game 1"));
    }
}
