using KTable.Client.Prefs;
using KTable.Core;

namespace KTable.Client.Tests;

[Collection("Problems")]
public class ShelfOrderTests
{
    public ShelfOrderTests()
    {
        TestFolders.New();
        Directory.CreateDirectory(DataPaths.Local);
        File.Delete(LibraryFile);
    }

    static string LibraryFile => Path.Combine(DataPaths.Local, "shelves.json");

    static readonly string[] Four = { "a", "b", "c", "d" };
    static readonly string[] Shelved = { "a", "b", "c", "d", Shelf.TrashKey };
    static readonly Dictionary<string, string> Names = new() { ["a"] = "Delta", ["b"] = "alpha", ["c"] = "Charlie", ["d"] = "bravo" };

    static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    static readonly Dictionary<string, DateTime> Arrived = new() { ["a"] = Day(2020, 1, 3), ["b"] = Day(2020, 1, 1), ["c"] = Day(2020, 1, 4), ["d"] = Day(2020, 1, 2) };

    static ShelfSort By(ShelfOrder order, Func<string, DateTime?>? played = null) => new(order, id => Names[id], played ?? (_ => null));

    static string Row(Library lib, ShelfSort sort, string folder = "") => string.Join(",", lib.Arranged(false, folder, Shelved, sort).Select(e => e.Key));

    static Library Stocked()
    {
        var lib = new Library();
        lib.AddFolder(false, "", "F");
        lib.AddFolder(false, "", "E");
        lib.NoteArrivals(false, Shelved, id => Arrived[id]);
        return lib;
    }

    [Fact]
    public void Each_order_sorts_the_cards_with_folders_first_by_name_and_the_trash_last()
    {
        var lib = Stocked();
        Assert.Equal("/E,/F,a,b,c,d,:trash", Row(lib, ShelfSort.Yours));
        Assert.Equal("/E,/F,b,d,c,a,:trash", Row(lib, By(ShelfOrder.Name)));
        Assert.Equal("/E,/F,c,a,d,b,:trash", Row(lib, By(ShelfOrder.Newest)));
        Assert.Equal("/E,/F,b,d,a,c,:trash", Row(lib, By(ShelfOrder.Oldest)));
        lib.ScenarioShelf.NoteOpened("a", Day(2021, 1, 1));
        lib.ScenarioShelf.NoteOpened("b", Day(2021, 2, 1));
        var played = By(ShelfOrder.Opened, id => id == "d" ? Day(2021, 6, 1) : id == "b" ? Day(2020, 6, 1) : null);
        Assert.Equal("/E,/F,d,b,a,c,:trash", Row(lib, played));
        Assert.Equal("/E,/F,b,a,c,d,:trash", Row(lib, By(ShelfOrder.Opened)));
    }

    [Fact]
    public void A_set_order_leaves_the_hand_order_as_it_was()
    {
        var lib = Stocked();
        lib.Drop(false, "", Shelved, new[] { "d", "/F" }, 0);
        Assert.Equal("d,/F,/E,a,b,c,:trash", Row(lib, ShelfSort.Yours));
        Assert.Equal("/E,/F,b,d,c,a,:trash", Row(lib, By(ShelfOrder.Name)));
        Assert.Equal("d,/F,/E,a,b,c,:trash", Row(lib, ShelfSort.Yours));
    }

    [Fact]
    public void Cards_inside_a_folder_follow_the_order_and_ties_fall_back_to_the_name()
    {
        var lib = Stocked();
        lib.MoveInto(false, "F", Shelved, new[] { "a", "c", "d" });
        Assert.Equal("d,c,a", Row(lib, By(ShelfOrder.Name), "F"));
        var same = new Library();
        same.NoteArrivals(false, Four, _ => Day(2020, 1, 1));
        Assert.Equal("b,d,c,a", string.Join(",", same.Arranged(false, "", Four, By(ShelfOrder.Newest)).Select(e => e.Key)));
    }

    [Fact]
    public void Found_cards_from_every_folder_follow_the_hand_order_through_the_folders_or_the_set_order()
    {
        var lib = Stocked();
        lib.MoveInto(false, "F", Shelved, new[] { "b" });
        lib.MoveInto(false, "E", Shelved, new[] { "d" });
        Assert.Equal("d,b,a,c", string.Join(",", lib.Ordered(false, Shelved, new[] { "a", "b", "c", "d", Shelf.TrashKey }, ShelfSort.Yours)));
        Assert.Equal("c,a,d,b", string.Join(",", lib.Ordered(false, Shelved, Four, By(ShelfOrder.Newest))));
        Assert.Equal("b,a", string.Join(",", lib.Ordered(false, Shelved, new[] { "a", "b" }, ShelfSort.Yours)));
    }

    [Fact]
    public void An_arrival_is_stamped_once_kept_in_the_file_and_forgetting_a_card_drops_its_stamps()
    {
        var lib = new Library();
        lib.NoteArrivals(false, new[] { "a", Shelf.TrashKey }, _ => Day(2020, 1, 1));
        lib.NoteArrivals(false, new[] { "a", "b" }, _ => Day(2024, 1, 1));
        lib.NoteOpened(false, "a");
        Assert.DoesNotContain(Shelf.TrashKey, lib.ScenarioShelf.Added.Keys);
        Assert.Empty(lib.PackShelf.Added);
        var back = Library.Load();
        Assert.Equal(Day(2020, 1, 1), DateTime.Parse(back.ScenarioShelf.Added["a"], null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        Assert.Equal(Day(2024, 1, 1), DateTime.Parse(back.ScenarioShelf.Added["b"], null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        Assert.True(back.ScenarioShelf.Opened.ContainsKey("a"));
        back.Forget(false, "a");
        var after = Library.Load();
        Assert.False(after.ScenarioShelf.Added.ContainsKey("a"));
        Assert.False(after.ScenarioShelf.Opened.ContainsKey("a"));
        Assert.True(after.ScenarioShelf.Added.ContainsKey("b"));
    }

    [Fact]
    public void A_shelves_file_from_before_the_stamps_loads_and_a_bad_stamp_counts_as_none()
    {
        File.WriteAllText(LibraryFile, """{"ScenarioShelf":{"Folders":["F"],"Filed":{"a":"F"},"Order":{}},"PackShelf":{"Folders":[],"Filed":{},"Order":{}}}""");
        var lib = Library.Load();
        Assert.Equal("F", lib.FolderOf(false, "a"));
        Assert.Empty(lib.ScenarioShelf.Added);
        Assert.Empty(lib.ScenarioShelf.Opened);
        File.WriteAllText(LibraryFile, """{"ScenarioShelf":{"Folders":["F"],"Filed":{"a":"F"},"Order":{},"Added":{"b":"not a date","c":"2020-01-01T00:00:00.0000000Z"},"Opened":null},"PackShelf":{}}""");
        var bad = Library.Load();
        Assert.Equal("F", bad.FolderOf(false, "a"));
        Assert.NotNull(bad.ScenarioShelf.Opened);
        Assert.Equal("/F,b,c", string.Join(",", bad.Arranged(false, "", new[] { "b", "c" }, By(ShelfOrder.Oldest)).Select(e => e.Key)));
    }

    [Fact]
    public void A_wrong_order_in_the_settings_file_falls_back_to_its_default()
    {
        var path = Path.Combine(DataPaths.Local, "settings-order.json");
        var was = Settings.FileAt;
        Settings.FileAt = () => path;
        try
        {
            File.WriteAllText(path, """{"ScenarioOrder":42,"PackOrder":3,"ModuleOrder":1}""");
            var cfg = Settings.Load();
            Assert.Equal(ShelfOrder.Yours, cfg.ScenarioOrder);
            Assert.Equal(ShelfOrder.Newest, cfg.PackOrder);
            Assert.Equal(ShelfOrder.Newest, cfg.ModuleOrder);
            Assert.Equal(ShelfOrder.Newest, new Settings().ModuleOrder);
        }
        finally { Settings.FileAt = was; }
    }

    [Theory]
    [InlineData("konig 1866", true)]
    [InlineData("KÖNIGGRÄTZ", true)]
    [InlineData("napoleonic", true)]
    [InlineData("1866 ulm", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void A_search_needs_every_word_somewhere_ignoring_case_and_accents(string query, bool found) =>
        Assert.Equal(found, TextSearch.Matches(query, "Königgrätz 1866", null, "Napoleonic / 1813"));
}
