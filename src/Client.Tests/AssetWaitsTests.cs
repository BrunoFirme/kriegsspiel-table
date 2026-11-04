namespace KTable.Client.Tests;

public class AssetWaitsTests
{
    readonly string _dir = TestFolders.New();

    [Fact]
    public void A_file_already_here_is_ready_at_once()
    {
        var waits = new AssetWaits<string> { RequestRemote = (_, _) => throw new Exception("asked the host") };
        var abs = Path.Combine(_dir, "a.png");
        File.WriteAllText(abs, "x");
        int ready = 0;
        waits.EnsureFile("p", "a.png", abs, () => ready++);
        Assert.Equal(1, ready);
        Assert.False(waits.Busy);
    }

    [Fact]
    public void A_missing_file_is_asked_for_once_and_everyone_waiting_goes_on_when_it_arrives()
    {
        var asked = new List<string>();
        var waits = new AssetWaits<string> { RequestRemote = (p, r) => asked.Add(p + "/" + r) };
        var abs = Path.Combine(_dir, "b.png");
        int ready = 0;
        waits.EnsureFile("p", "b.png", abs, () => ready++);
        waits.EnsureFile("p", "b.png", abs, () => ready++);
        Assert.Equal(new[] { "p/b.png" }, asked);
        Assert.True(waits.Busy);
        Assert.Equal(1, waits.PendingFiles);
        Assert.Equal((0, 1), (waits.BatchDone, waits.BatchTotal));
        waits.FileArrived(abs);
        Assert.Equal(2, ready);
        Assert.False(waits.Busy);
        Assert.Equal((1, 1), (waits.BatchDone, waits.BatchTotal));
    }

    [Fact]
    public void A_file_removed_after_it_arrived_is_asked_for_again()
    {
        var asked = 0;
        var waits = new AssetWaits<string> { RequestRemote = (_, _) => asked++ };
        var abs = Path.Combine(_dir, "c.png");
        waits.EnsureFile("p", "c.png", abs, () => { });
        waits.FileArrived(abs);
        waits.EnsureFile("p", "c.png", abs, () => { });
        Assert.Equal(2, asked);
        Assert.Equal(1, waits.PendingFiles);
    }

    [Fact]
    public void A_background_fetch_skips_files_here_waited_for_or_known_missing_and_shows_nothing()
    {
        var background = new List<string>();
        var waits = new AssetWaits<string> { RequestRemote = (_, _) => { }, RequestInBackground = (p, r) => background.Add(r) };
        var here = Path.Combine(_dir, "here.png");
        File.WriteAllText(here, "x");
        waits.Prefetch("p", "here.png", here);
        var waited = Path.Combine(_dir, "waited.png");
        waits.EnsureFile("p", "waited.png", waited, () => { });
        waits.Prefetch("p", "waited.png", waited);
        var gone = Path.Combine(_dir, "gone.png");
        waits.EnsureFile("p", "gone.png", gone, () => { });
        waits.FileMissing(gone);
        waits.Prefetch("p", "gone.png", gone);
        waits.Prefetch("p", "new.png", Path.Combine(_dir, "new.png"));
        Assert.Equal(new[] { "new.png" }, background);
        Assert.Equal(1, waits.PendingFiles);
    }

    [Fact]
    public void A_picture_decoding_is_started_once_and_every_waiter_gets_it()
    {
        var waits = new AssetWaits<string>();
        var got = new List<string>();
        Assert.True(waits.StartTexture("k", t => got.Add("1" + t)));
        Assert.False(waits.StartTexture("k", t => got.Add("2" + t)));
        Assert.False(waits.StartTexture("k", null));
        Assert.Equal(1, waits.PendingTextures);
        foreach (var cb in waits.TextureDone("k")) cb("!");
        Assert.Equal(new[] { "1!", "2!" }, got);
        Assert.Empty(waits.TextureDone("k"));
        Assert.Equal((1, 1), (waits.BatchDone, waits.BatchTotal));
    }

    [Fact]
    public void The_batch_starts_over_once_everything_has_arrived()
    {
        var waits = new AssetWaits<string>();
        waits.StartTexture("a", null);
        waits.StartTexture("b", null);
        waits.TextureDone("a");
        waits.TextureDone("b");
        Assert.Equal((2, 2), (waits.BatchDone, waits.BatchTotal));
        waits.StartTexture("c", null);
        Assert.Equal((0, 1), (waits.BatchDone, waits.BatchTotal));
    }

    [Fact]
    public void A_job_shows_as_busy_until_it_ends()
    {
        var waits = new AssetWaits<string>();
        waits.BeginJob("slicing");
        waits.JobProgress = 0.5;
        Assert.True(waits.Busy);
        Assert.Equal(new[] { "slicing" }, waits.Jobs);
        waits.EndJob("slicing");
        Assert.False(waits.Busy);
        Assert.Equal(-1, waits.JobProgress);
    }

    [Fact]
    public void A_missing_file_on_the_host_ends_the_wait_of_the_picture_that_wanted_it()
    {
        var waits = new AssetWaits<string>();
        var abs = Path.Combine(_dir, "missing.png");
        Assert.True(waits.StartTexture(abs, null));
        int ready = 0;
        waits.EnsureFile("p", "missing.png", abs, () => ready++, () => waits.TextureDone(abs));
        Assert.Equal(0, ready);
        Assert.False(waits.Busy);
        Assert.Equal((1, 1), (waits.BatchDone, waits.BatchTotal));
        Assert.True(waits.KnownMissing(abs));
    }

    [Fact]
    public void A_file_the_host_lacks_stops_everyone_waiting_and_is_not_asked_for_again()
    {
        var asked = 0;
        var waits = new AssetWaits<string> { RequestRemote = (_, _) => asked++ };
        var abs = Path.Combine(_dir, "gone.png");
        int ready = 0, missing = 0;
        waits.EnsureFile("p", "gone.png", abs, () => ready++, () => missing++);
        waits.EnsureFile("p", "gone.png", abs, () => ready++);
        waits.FileMissing(abs);
        Assert.Equal((0, 1), (ready, missing));
        Assert.False(waits.Busy);
        Assert.Equal((1, 1), (waits.BatchDone, waits.BatchTotal));
        waits.EnsureFile("p", "gone.png", abs, () => ready++, () => missing++);
        Assert.Equal((1, 2), (asked, missing));
        Assert.False(waits.Busy);
        File.WriteAllText(abs, "x");
        Assert.False(waits.KnownMissing(abs));
    }

    [Fact]
    public void A_map_tile_streaming_in_is_loading_but_not_a_wait_the_bar_shows()
    {
        var waits = new AssetWaits<string>();
        Assert.True(waits.StartTexture("tile", null, quiet: true));
        Assert.True(waits.Busy);
        Assert.False(waits.Shows);
        Assert.Equal(0, waits.BatchTotal);
        waits.StartTexture("piece", null);
        Assert.True(waits.Shows);
        Assert.Equal(1, waits.BatchTotal);
        Assert.False(waits.StartTexture("tile", _ => { }));
        Assert.Equal(2, waits.BatchTotal);
        waits.TextureDone("tile");
        waits.TextureDone("piece");
        Assert.Equal((2, false, false), (waits.BatchDone, waits.Busy, waits.Shows));
    }
}
