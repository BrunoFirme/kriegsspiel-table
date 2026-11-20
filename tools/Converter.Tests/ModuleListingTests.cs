using KTable.Core;

namespace KTable.Converter.Tests;

public sealed class ModuleListingTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-module-listing", Guid.NewGuid().ToString("N"));

    string Put(string rel, int age)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-age));
        return path;
    }

    public void Dispose() => BestEffort.DeleteFolder(_root);

    [Fact]
    public void Saves_in_folders_are_listed_with_their_folder_and_saved_objects_and_indexes_are_not()
    {
        Put("Mods/Workshop/111.json", 1);
        Put("Mods/Workshop/WorkshopFileInfos.json", 0);
        Put("Mods/Workshop/deep/222.json", 0);
        Put("Saves/TS_Save_1.json", 5);
        Put("Saves/SaveFileInfos.json", 0);
        Put("Saves/Finished Games/TS_Save_2.json", 2);
        Put("Saves/Finished Games/2024/TS_Save_3.json", 3);
        Put("Saves/Saved Objects/Gun.json", 0);
        Put("Saves/saved objects/More/Horse.json", 0);
        var found = ModuleListing.Files(Path.Combine(_root, "Mods", "Workshop"), Path.Combine(_root, "Saves"))
            .Select(f => $"{f.Kind}:{f.Folder}:{Path.GetFileNameWithoutExtension(f.File)}").ToList();
        Assert.Equal(new[] { "workshop::111", "save:Finished Games:TS_Save_2", "save:Finished Games/2024:TS_Save_3", "save::TS_Save_1" }, found);
    }

    [Fact]
    public void Missing_folders_list_nothing() =>
        Assert.Empty(ModuleListing.Files(Path.Combine(_root, "none"), Path.Combine(_root, "also none")));
}
