using KTable.Core;

namespace KTable.Core.Tests;

public sealed class PackFilesTrashTests
{
    static readonly DateTime T0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    static SaveStore Store(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "ktable-packfiles-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new(Path.Combine(root, "live"), Path.Combine(root, "history"), Path.Combine(root, "trash"));
    }

    [Fact]
    public void A_clear_out_is_listed_with_its_pack_and_goes_with_the_trash()
    {
        var saves = Store(out _);
        var bin = Path.Combine(saves.TrashDir, SaveStore.PackFilesDir, $"{T0:yyyyMMdd-HHmmss} user.my-pieces");
        Directory.CreateDirectory(Path.Combine(bin, "assets"));
        File.WriteAllText(Path.Combine(bin, "assets", new string('a', 64) + ".png"), "x");
        Directory.CreateDirectory(Path.Combine(saves.TrashDir, SaveStore.PackFilesDir, "someone's folder"));

        var e = Assert.Single(saves.TrashEntries());
        Assert.Equal("pack files", e.Kind);
        Assert.Equal("user.my-pieces", e.Id);
        Assert.Equal(T0, e.Stamp);
        Assert.Equal("pictures cleared out of user.my-pieces", e.Describe());
        Assert.Throws<IOException>(() => saves.Restore(e, Path.Combine(saves.TrashDir, "..", "packages"), T0));
        Assert.True(Directory.Exists(bin));

        Assert.Equal(0, saves.EmptyTrash(T0.AddDays(SaveStore.TrashDays - 1)));
        Assert.Equal(1, saves.EmptyTrash(T0.AddDays(SaveStore.TrashDays + 1)));
        Assert.False(Directory.Exists(bin));
        Assert.Empty(saves.TrashEntries());
    }

    [Fact]
    public void Restore_puts_the_files_back_and_lists_the_pictures_again_but_never_over_what_is_there()
    {
        var saves = Store(out var root);
        var packages = Path.Combine(root, "packages");
        string Hash(char c) => new string(c, 64);
        var pack = new Model.PackManifest { Id = "user.my-pieces", Name = "My pieces" };
        pack.Assets[Hash('b')] = new Model.AssetEntry { File = $"assets/{Hash('b')}.png", W = 10, H = 10 };
        Json.Save(Path.Combine(packages, "user.my-pieces", "pack.json"), pack);
        Directory.CreateDirectory(Path.Combine(packages, "user.my-pieces", "art"));
        File.WriteAllText(Path.Combine(packages, "user.my-pieces", "art", "doc.json"), "newer");

        var bin = Path.Combine(saves.TrashDir, SaveStore.PackFilesDir, $"{T0:yyyyMMdd-HHmmss} user.my-pieces");
        Directory.CreateDirectory(Path.Combine(bin, "assets"));
        Directory.CreateDirectory(Path.Combine(bin, "art"));
        File.WriteAllText(Path.Combine(bin, "assets", Hash('a') + ".png"), "painted over");
        File.WriteAllText(Path.Combine(bin, "art", "doc.json"), "older");
        Json.Save(Path.Combine(bin, SaveStore.PackFilesListing),
            new Dictionary<string, Model.AssetEntry> { [Hash('a')] = new() { File = $"assets/{Hash('a')}.png", W = 64, H = 32 } });

        var msg = saves.Restore(Assert.Single(saves.TrashEntries()), packages, T0.AddHours(1));

        Assert.Contains("1 file back in the pack My pieces", msg);
        Assert.Equal("painted over", File.ReadAllText(Path.Combine(packages, "user.my-pieces", "assets", Hash('a') + ".png")));
        var now = Json.Load<Model.PackManifest>(Path.Combine(packages, "user.my-pieces", "pack.json"));
        Assert.Equal(64, now.Assets[Hash('a')].W);
        Assert.True(now.Assets.ContainsKey(Hash('b')));
        Assert.Equal("newer", File.ReadAllText(Path.Combine(packages, "user.my-pieces", "art", "doc.json")));
        var left = Assert.Single(saves.TrashEntries());
        Assert.True(File.Exists(Path.Combine(left.Path, "art", "doc.json")));
        Assert.False(File.Exists(Path.Combine(left.Path, SaveStore.PackFilesListing)));
    }
}
