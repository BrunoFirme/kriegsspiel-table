namespace KTable.Client.Tests;

public class CachesTests
{
    readonly string _dir = TestFolders.New();

    string Put(string rel, int bytes)
    {
        var path = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void A_cache_measures_every_file_under_it_and_a_missing_one_is_empty()
    {
        Put("index.json", 10);
        Put(Path.Combine("text", "a.png"), 300);
        File.SetAttributes(Put(Path.Combine("text", "hidden.png"), 5), FileAttributes.Hidden);
        Assert.Equal(315, Caches.Size(_dir));
        Assert.Equal(0, Caches.Size(Path.Combine(_dir, "nowhere")));
    }

    [Fact]
    public void Clearing_empties_the_cache_but_keeps_its_folder_for_the_next_write()
    {
        Put("index.json", 10);
        Put(Path.Combine("workshop", "deep", "1.json"), 20);
        new FileInfo(Put("locked-by-flag.bin", 30)).IsReadOnly = true;
        var done = Caches.Clear(_dir);
        Assert.Equal(new Cleared(60, 0), done);
        Assert.True(Directory.Exists(_dir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
        Assert.Equal(new Cleared(0, 0), Caches.Clear(Path.Combine(_dir, "nowhere")));
    }

    [Fact]
    public void A_file_another_program_holds_stays_and_is_counted()
    {
        Put("a.png", 40);
        var held = Put(Path.Combine("sub", "held.png"), 7);
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(new Cleared(40, 1), Caches.Clear(_dir));
        Assert.True(File.Exists(held));
        Assert.Equal(7, Caches.Size(_dir));
    }

    [Fact]
    public void Every_cache_lives_under_the_apps_cache_folder_and_reads_in_a_sentence()
    {
        Assert.All(Caches.All, c => Assert.StartsWith(Caches.Root + Path.DirectorySeparatorChar, c.Path));
        Assert.Equal(new[] { "module files", "packs from hosts", "display pictures" }, Caches.All.Select(c => c.InSentence));
    }
}
