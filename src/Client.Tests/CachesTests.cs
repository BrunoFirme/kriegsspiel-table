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
        Assert.Equal(new[] { "conversion cache", "join cache", "picture cache" }, Caches.All.Select(c => c.InSentence));
    }

    static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    static readonly TimeSpan Day = TimeSpan.FromDays(1);

    string Aged(string rel, TimeSpan age)
    {
        var path = Put(rel, 10);
        File.SetLastWriteTimeUtc(path, Now - age);
        return path;
    }

    [Fact]
    public void A_cache_used_within_the_day_is_kept_whole_however_old_its_files()
    {
        var old = Aged(Path.Combine("text", "a.png"), TimeSpan.FromDays(40));
        Caches.Used(_dir, Now - TimeSpan.FromHours(23));
        Assert.Null(Caches.Expire(_dir, Now, Day));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void A_cache_last_used_a_day_ago_is_cleared_and_a_later_use_starts_the_day_again()
    {
        Aged("a.png", TimeSpan.FromMinutes(5));
        Caches.Used(_dir, Now - Day);
        Assert.Equal(new Cleared(10, 0), Caches.Expire(_dir, Now, Day));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
        Aged("b.png", TimeSpan.FromDays(3));
        Caches.Used(_dir, Now);
        Assert.Null(Caches.Expire(_dir, Now + TimeSpan.FromHours(1), Day));
    }

    [Fact]
    public void A_cache_never_marked_goes_by_its_newest_file_and_an_empty_one_is_left_alone()
    {
        Assert.Null(Caches.Expire(_dir, Now, Day));
        Aged("old.png", TimeSpan.FromDays(9));
        var fresh = Aged(Path.Combine("pdf", "new.png"), TimeSpan.FromHours(2));
        Assert.Null(Caches.Expire(_dir, Now, Day));
        File.SetLastWriteTimeUtc(fresh, Now - TimeSpan.FromHours(25));
        Assert.Equal(new Cleared(20, 0), Caches.Expire(_dir, Now, Day));
    }

    [Fact]
    public void Clearing_stops_when_told_and_the_rest_goes_the_next_time()
    {
        foreach (var name in new[] { "a.png", "b.png", "c.png" }) Aged(name, TimeSpan.FromDays(2));
        int asked = 0;
        Assert.Equal(new Cleared(10, 0), Caches.Expire(_dir, Now, Day, () => asked++ > 0));
        Assert.Equal(2, Directory.EnumerateFiles(_dir).Count());
        Assert.Equal(new Cleared(20, 0), Caches.Expire(_dir, Now, Day));
    }

    [Fact]
    public void A_cached_file_unused_for_thirty_days_goes_and_what_a_host_s_saves_hold_never_does()
    {
        var unused = Aged(Path.Combine("user.blocks", "assets", "a.png"), TimeSpan.FromDays(31));
        var used = Aged(Path.Combine("user.blocks", "assets", "b.png"), TimeSpan.FromDays(29));
        var save = Aged(Path.Combine("_saves", "trash", "old.kstable"), TimeSpan.FromDays(2000));
        Assert.Equal(new Cleared(10, 0), Caches.ClearUnused(_dir, Now));
        Assert.False(File.Exists(unused));
        Assert.True(File.Exists(used));
        Assert.True(File.Exists(save));
    }

    [Fact]
    public void Seeing_a_cached_file_makes_it_used_today_once_its_date_is_a_day_old()
    {
        var inCache = Path.Combine(Caches.Display, Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Caches.Display);
        File.WriteAllBytes(inCache, new byte[3]);
        File.SetLastWriteTimeUtc(inCache, Now.AddDays(-29));
        var elsewhere = Aged("pack.png", TimeSpan.FromDays(400));

        Caches.Seen(inCache, Now);
        Caches.Seen(elsewhere, Now);

        Assert.Equal(Now, File.GetLastWriteTimeUtc(inCache));
        Assert.Equal(Now - TimeSpan.FromDays(400), File.GetLastWriteTimeUtc(elsewhere));
        File.Delete(inCache);
    }

    [Fact]
    public void What_is_named_as_needed_is_spared_and_nothing_goes_when_the_need_cannot_be_read()
    {
        var pack = Aged(Path.Combine("user.blocks", "assets", "a.png"), TimeSpan.FromDays(90));
        var web = Aged(Path.Combine("sheets", "pictures", "b.png"), TimeSpan.FromDays(90));
        var other = Aged(Path.Combine("user.other", "pack.json"), TimeSpan.FromDays(90));
        Assert.Equal(new Cleared(0, 0), Caches.ClearUnused(_dir, Now, null, () => null));
        Assert.Equal(new Cleared(10, 0), Caches.ClearUnused(_dir, Now, null, () => new[] { Path.Combine(_dir, "user.blocks"), web }));
        Assert.True(File.Exists(pack) && File.Exists(web));
        Assert.False(File.Exists(other));
    }

    [Fact]
    public void Only_files_older_than_the_time_given_are_cleared()
    {
        var old = Aged(Path.Combine("work", "old.glb"), TimeSpan.FromHours(30));
        var fresh = Aged(Path.Combine("work", "new.glb"), TimeSpan.FromHours(3));
        Assert.Equal(new Cleared(10, 0), Caches.ClearOlderThan(_dir, Now - Day));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }
}
