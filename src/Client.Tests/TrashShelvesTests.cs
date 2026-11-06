using KTable.Core;

namespace KTable.Client.Tests;

public class TrashShelvesTests
{
    readonly string _dir = TestFolders.New();
    readonly SaveStore _saves;

    public TrashShelvesTests()
    {
        _saves = new SaveStore(Path.Combine(_dir, "live"), Path.Combine(_dir, "history"), Path.Combine(_dir, "trash"));
    }

    string Entry(string name, string? sub = null)
    {
        var d = Path.Combine(_saves.TrashDir, name);
        Directory.CreateDirectory(sub == null ? d : Path.Combine(d, sub));
        return d;
    }

    [Fact]
    public void Packs_and_the_studios_clear_outs_are_one_trash_and_everything_else_the_other_newest_first()
    {
        Entry("20261101-120000 feud", "scenario");
        Entry("20261101-120001 user.mine", "pack");
        Entry("20261101-120002 feud (game)");
        Entry("20261101-120003 feud (mirror)");
        Entry("20261101-120004 stray");
        Entry(Path.Combine(SaveStore.PackFilesDir, "20261101-120005 user.mine"));
        Entry("not a stamp");
        Assert.Equal(new[] { "PackFiles user.mine", "Pack user.mine" }, Kinds(TrashShelves.Entries(_saves, true)));
        Assert.Equal(new[] { "Other stray", "Mirror feud", "Game feud", "Scenario feud" }, Kinds(TrashShelves.Entries(_saves, false)));
    }

    static IEnumerable<string> Kinds(List<TrashEntry> l) => l.Select(e => (e.Kind == TrashKind.Pack ? "Pack" : e.Kind == TrashKind.PackFiles ? "PackFiles"
        : e.Kind == TrashKind.Scenario ? "Scenario" : e.Kind == TrashKind.Game ? "Game" : e.Kind == TrashKind.Mirror ? "Mirror" : "Other") + " " + e.Id);

    [Fact]
    public void A_restore_says_when_it_sets_this_PCs_mirror_aside_and_never_the_saves_in_play()
    {
        var game = new TrashEntry(Entry("20261101-120002 feud (game)"), TrashKind.Game, "feud", DateTime.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(_saves.LivePath("feud"))!);
        File.WriteAllText(_saves.LivePath("feud"), "{}");
        File.WriteAllText(Path.Combine(game.Path, "feud" + SaveStore.Ext), "{}");
        Assert.Null(TrashShelves.SetsGameAside(_saves, game));
        var scen = new TrashEntry(Entry("20261101-120000 feud", "checkpoints"), TrashKind.Scenario, "feud", DateTime.UtcNow);
        Assert.Null(TrashShelves.SetsGameAside(_saves, scen));

        var mirror = new TrashEntry(Entry("20261101-120003 feud (mirror)"), TrashKind.Mirror, "feud", DateTime.UtcNow);
        Assert.Null(TrashShelves.SetsGameAside(_saves, mirror));
        Directory.CreateDirectory(_saves.MirrorDir);
        File.WriteAllText(Path.Combine(_saves.MirrorDir, "feud" + SaveStore.Ext), "{}");
        Assert.Equal($"The mirror of feud on this PC now goes to the trash in its place, for {SaveStore.TrashDays} days.", TrashShelves.SetsGameAside(_saves, mirror));

        var pack = new TrashEntry(Entry("20261101-120001 feud", "pack"), TrashKind.Pack, "feud", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(pack.Path, "feud" + SaveStore.Ext), "{}");
        Assert.Null(TrashShelves.SetsGameAside(_saves, pack));
    }
}
