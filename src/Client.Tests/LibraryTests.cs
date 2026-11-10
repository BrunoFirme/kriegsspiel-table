using KTable.Core;

namespace KTable.Client.Tests;

[CollectionDefinition("Problems", DisableParallelization = true)]
public sealed class ProblemsCollection { }

[Collection("Problems")]
public class LibraryTests
{
    public LibraryTests()
    {
        TestFolders.New();
        Directory.CreateDirectory(DataPaths.Local);
        File.Delete(LibraryFile);
        File.Delete(OldFile);
    }

    static string LibraryFile => Path.Combine(DataPaths.Local, "shelves.json");
    static string OldFile => Path.Combine(DataPaths.Local, "library.json");

    static readonly string[] Four = { "a", "b", "c", "d" };

    static string Row(Library lib, string folder, IReadOnlyList<string> present) => string.Join(",", lib.Arranged(false, folder, present).Select(e => e.Key));

    [Fact]
    public void Folder_names_are_cleaned_nest_and_siblings_cannot_share_one()
    {
        var lib = new Library();
        Assert.Equal("Zulu", lib.AddFolder(false, "", "  Zulu "));
        Assert.Equal("alpha·beta·gamma", lib.AddFolder(false, "", "alpha/beta\\gamma"));
        Assert.Null(lib.AddFolder(false, "", "zulu"));
        Assert.Null(lib.AddFolder(false, "", "   "));
        Assert.Equal("Zulu/Inner", lib.AddFolder(false, "Zulu", "Inner"));
        Assert.Null(lib.AddFolder(false, "Zulu", "inner"));
        Assert.Null(lib.AddFolder(false, "Nope", "x"));
        Assert.Equal("Inner", lib.AddFolder(false, "", "Inner"));
        Assert.Empty(lib.PackShelf.Folders);
        Assert.True(File.Exists(LibraryFile));
    }

    [Fact]
    public void Cards_dropped_by_hand_stay_where_they_land_and_newcomers_come_last()
    {
        var lib = new Library();
        Assert.Equal("a,b,c,d", Row(lib, "", Four));
        lib.Drop(false, "", Four, new[] { "d" }, 0);
        Assert.Equal("d,a,b,c", Row(lib, "", Four));
        lib.Drop(false, "", Four, new[] { "a", "b" }, 4);
        Assert.Equal("d,c,a,b", Row(lib, "", Four));
        lib.Drop(false, "", Four, new[] { "c" }, 2);
        Assert.Equal("d,c,a,b", Row(lib, "", Four));
        Assert.Equal("d,c,a,b,e", Row(lib, "", new[] { "a", "b", "c", "d", "e" }));
        Assert.Equal("d,a,e", Row(lib, "", new[] { "a", "d", "e" }));
        Assert.Equal("a,b,c,d", string.Join(",", lib.Ordered(true, Four)));
    }

    [Fact]
    public void Folders_sit_anywhere_take_what_is_dropped_in_them_and_never_go_inside_themselves()
    {
        var lib = new Library();
        Assert.Equal("F", lib.AddFolder(false, "", "F"));
        Assert.Equal("/F,a,b,c,d", Row(lib, "", Four));
        lib.MoveInto(false, "F", Four, new[] { "b" });
        Assert.Equal("/F,a,c,d", Row(lib, "", Four));
        Assert.Equal("b", Row(lib, "F", Four));
        lib.Drop(false, "", Four, new[] { "/F" }, 4);
        Assert.Equal("a,c,d,/F", Row(lib, "", Four));
        lib.Drop(false, "F", Four, new[] { "/F" }, 0);
        Assert.Equal("a,c,d,/F", Row(lib, "", Four));
        Assert.Equal("F/G", lib.AddFolder(false, "F", "G"));
        lib.MoveInto(false, "F/G", Four, new[] { "a", "d" });
        lib.MoveInto(false, "F/G", Four, new[] { "/F" });
        Assert.Equal("c,/F", Row(lib, "", Four));
        Assert.Equal("/F/G,b", Row(lib, "F", Four));
        Assert.Equal(new[] { "a", "d", "b" }, lib.ScenarioShelf.Inside("F", Four, 4));
        Assert.Equal(new[] { "a", "d" }, lib.ScenarioShelf.Inside("F", Four, 2));
        Assert.Equal(3, lib.ScenarioShelf.CountInside("F", Four));
        Assert.Equal(new[] { "c", "a", "d", "b" }, lib.Ordered(false, Four));
    }

    [Fact]
    public void A_folder_renamed_or_moved_carries_what_is_inside_and_takes_a_free_name()
    {
        var lib = new Library();
        lib.AddFolder(false, "", "F");
        lib.AddFolder(false, "F", "G");
        lib.MoveInto(false, "F", Four, new[] { "b" });
        lib.MoveInto(false, "F/G", Four, new[] { "a" });
        lib.Drop(false, "", Four, new[] { "/F" }, 4);
        lib.AddFolder(false, "", "Other");
        Assert.False(lib.RenameFolder(false, "F", "other"));
        Assert.False(lib.RenameFolder(false, "Nope", "X"));
        Assert.True(lib.RenameFolder(false, "F", "H"));
        Assert.Equal("H", lib.FolderOf(false, "b"));
        Assert.Equal("H/G", lib.FolderOf(false, "a"));
        Assert.Equal("/Other,c,d,/H", Row(lib, "", Four));
        Assert.Equal("/H/G,b", Row(lib, "H", Four));
        lib.AddFolder(false, "", "G");
        lib.Drop(false, "", Four, new[] { "/H/G" }, 0);
        Assert.Equal("G 2", lib.FolderOf(false, "a"));
        Assert.Equal("/G 2,/G,/Other,c,d,/H", Row(lib, "", Four));
    }

    [Fact]
    public void Removing_a_folder_puts_what_it_held_where_it_stood()
    {
        var lib = new Library();
        lib.AddFolder(false, "", "F");
        lib.AddFolder(false, "F", "G");
        lib.MoveInto(false, "F", Four, new[] { "b" });
        lib.MoveInto(false, "F/G", Four, new[] { "a" });
        lib.Drop(false, "", Four, new[] { "/F" }, 2);
        Assert.Equal("c,/F,d", Row(lib, "", Four));
        lib.Of(false).Filed["gone"] = "F";
        lib.RemoveFolder(false, "F", Four);
        Assert.Equal("c,/G,b,d", Row(lib, "", Four));
        Assert.Equal("", lib.FolderOf(false, "b"));
        Assert.Equal("G", lib.FolderOf(false, "a"));
        Assert.False(lib.ScenarioShelf.Filed.ContainsKey("gone"));
        Assert.Equal(new[] { "G" }, lib.ScenarioShelf.Folders);
        lib.Forget(false, "a");
        Assert.Equal("", lib.FolderOf(false, "a"));
        Assert.Equal("c,/G,b,d", Row(lib, "", new[] { "b", "c", "d" }));
    }

    [Fact]
    public void The_trash_moves_about_the_top_but_never_into_a_folder_or_the_in_game_order()
    {
        var lib = new Library();
        lib.AddFolder(false, "", "F");
        var shown = new[] { "a", "b", Shelf.TrashKey };
        Assert.Equal("/F,a,b,:trash", Row(lib, "", shown));
        lib.Drop(false, "", shown, new[] { Shelf.TrashKey }, 1);
        Assert.Equal("/F,:trash,a,b", Row(lib, "", shown));
        lib.MoveInto(false, "F", shown, new[] { Shelf.TrashKey, "a" });
        Assert.Equal("/F,:trash,b", Row(lib, "", shown));
        Assert.Equal("a", Row(lib, "F", shown));
        Assert.Equal(new[] { "a" }, lib.ScenarioShelf.Inside("F", shown, 4));
        Assert.Equal(new[] { "a", "b" }, lib.Ordered(false, shown));
    }

    [Fact]
    public void Flat_folders_from_before_go_onto_each_shelf_that_used_them_and_their_file_is_left_for_older_builds()
    {
        const string before = "{\"Folders\":[\"A\",\"B\",\"C\",\" \"],\"Scenarios\":{\"s1\":\"A\",\"s2\":\"Gone\"},\"Packs\":{\"p1\":\"B\",\"p2\":\"A\"}}";
        File.WriteAllText(OldFile, before);
        var lib = Library.Load();
        Assert.Equal(new[] { "A", "C" }, lib.ScenarioShelf.Folders);
        Assert.Equal(new[] { "A", "B", "C" }, lib.PackShelf.Folders);
        Assert.Equal("A", lib.FolderOf(false, "s1"));
        Assert.Equal("", lib.FolderOf(false, "s2"));
        Assert.Equal("B", lib.FolderOf(true, "p1"));
        Assert.Equal("A", lib.FolderOf(true, "p2"));
        lib.Save();
        var text = File.ReadAllText(LibraryFile);
        Assert.DoesNotContain("\"Folders\": [\n    \"A\",\n    \"B\"", text.Replace("\r\n", "\n"));
        Assert.StartsWith("{\n  \"ScenarioShelf\": {", text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("\"Scenarios\"", text);
        Assert.Equal(before, File.ReadAllText(OldFile));
        File.WriteAllText(OldFile, "{\"Folders\":[\"Z\"],\"Scenarios\":{\"s1\":\"Z\"},\"Packs\":{}}");
        Assert.Equal("A", Library.Load().FolderOf(false, "s1"));
        Assert.Equal("B", Library.Load().FolderOf(true, "p1"));
    }

    [Fact]
    public void Shelves_saved_in_the_old_file_by_an_earlier_build_are_still_read()
    {
        File.WriteAllText(OldFile, "{\"ScenarioShelf\":{\"Folders\":[\"F\"],\"Filed\":{\"s1\":\"F\"},\"Order\":{\"\":[\"x\",\"/F\"]}},\"PackShelf\":{}}");
        var lib = Library.Load();
        Assert.Equal("F", lib.FolderOf(false, "s1"));
        Assert.Equal("x,/F", Row(lib, "", new[] { "x", "s1" }));
        Assert.False(File.Exists(LibraryFile));
    }

    [Fact]
    public void The_library_reads_back_what_was_saved_and_mends_a_hand_edited_file()
    {
        Assert.Empty(Library.Load().ScenarioShelf.Folders);
        var lib = new Library();
        lib.AddFolder(false, "", "One");
        lib.MoveInto(false, "One", new[] { "feud" }, new[] { "feud" });
        var back = Library.Load();
        Assert.Equal(new[] { "One" }, back.ScenarioShelf.Folders);
        Assert.Equal("One", back.FolderOf(false, "feud"));
        Assert.Equal("feud", Row(back, "One", new[] { "feud" }));

        File.WriteAllText(LibraryFile, "{\"ScenarioShelf\":{\"Folders\":[\"A/B\",\" \",\"\"],\"Filed\":null,\"Order\":{\"\":null}},\"PackShelf\":null}");
        var mended = Library.Load();
        Assert.Equal(new[] { "A/B", "A" }, mended.ScenarioShelf.Folders);
        Assert.Empty(mended.ScenarioShelf.Filed);
        Assert.Empty(mended.PackShelf.Folders);
        Assert.Equal("/A,x", Row(mended, "", new[] { "x" }));
    }

    [Fact]
    public void A_library_that_cannot_be_read_is_copied_aside_and_told_of_and_starts_empty()
    {
        File.WriteAllText(LibraryFile, "{ not json");
        Problems.Take();
        var told = new List<string>();
        Action<string> h = told.Add;
        Problems.Told += h;
        try { Assert.Empty(Library.Load().ScenarioShelf.Folders); }
        finally { Problems.Told -= h; }
        var said = Assert.Single(told);
        Assert.StartsWith("Your folders on the title screen (shelves.json) could not be read (", said);
        Assert.EndsWith("), so they start empty; a copy of it is kept beside it.", said);
        Assert.Single(Directory.GetFiles(DataPaths.Local, "shelves.json.bad-*"));
        foreach (var f in Directory.GetFiles(DataPaths.Local, "shelves.json.bad-*")) File.Delete(f);

        File.WriteAllText(LibraryFile, "null");
        Assert.Empty(Library.Load().ScenarioShelf.Folders);
        Assert.Contains("(the file is empty)", Assert.Single(Problems.Take()));
        foreach (var f in Directory.GetFiles(DataPaths.Local, "shelves.json.bad-*")) File.Delete(f);

        File.Delete(LibraryFile);
        File.WriteAllText(OldFile, "{ not json");
        Assert.Empty(Library.Load().ScenarioShelf.Folders);
        Assert.Contains("(library.json) could not be read", Assert.Single(Problems.Take()));
        foreach (var f in Directory.GetFiles(DataPaths.Local, "library.json.bad-*")) File.Delete(f);
    }

    [Fact]
    public void Problems_wait_for_a_listener_and_one_told_once_is_told_once()
    {
        Problems.Take();
        var echoed = new List<string>();
        var was = Problems.Echo;
        Problems.Echo = echoed.Add;
        try
        {
            Problems.Tell("first");
            Problems.TellOnce("problems-test-key", "second");
            Problems.TellOnce("problems-test-key", "third");
            Assert.Equal(new[] { "first", "second" }, Problems.Take());
            Assert.Empty(Problems.Take());
            var heard = new List<string>();
            Action<string> h = heard.Add;
            Problems.Told += h;
            Problems.Tell("fourth");
            Problems.Told -= h;
            Assert.Equal(new[] { "fourth" }, heard);
            Assert.Empty(Problems.Take());
            Assert.Equal(new[] { "first", "second", "fourth" }, echoed);
        }
        finally { Problems.Echo = was; }
    }
}

public class InUseTests
{
    static string Root() => TestFolders.New();

    [Fact]
    public void An_open_folder_is_counted_and_refuses_a_claim_until_the_last_close()
    {
        var root = Root();
        var dir = InUse.Scenario(root, "feud");
        Assert.Equal(Path.Combine(root, "scenarios", "feud"), dir);
        Assert.Null(InUse.TryOpen(dir + Path.DirectorySeparatorChar, "is on the table now: leave the table first"));
        Assert.Null(InUse.TryOpen(dir.ToUpperInvariant(), "is on the table now: leave the table first"));
        var e = Assert.Throws<InUseException>(() => InUse.Claim(new[] { (dir, "the scenario 'Feud'") }, "being installed"));
        Assert.Equal("The scenario 'Feud' is on the table now: leave the table first.", e.Message);
        InUse.Close(dir);
        Assert.Throws<InUseException>(() => InUse.Claim(new[] { (dir, "the scenario 'Feud'") }, "being installed"));
        InUse.Close(dir);
        InUse.Close(dir);
        using (InUse.Claim(new[] { (dir, "the scenario 'Feud'") }, "being installed")) { }
    }

    [Fact]
    public void A_folder_being_replaced_refuses_opening_and_other_claims_until_the_hold_goes()
    {
        var root = Root();
        var pack = InUse.Pack(root, "my-pack");
        var scen = InUse.Scenario(root, "s");
        var hold = InUse.Claim(new[] { (pack, "the pack 'Mine'"), (pack + "/", "twice"), (scen, "the scenario 's'") }, "being imported");
        Assert.Equal("being imported", InUse.BeingReplaced(pack));
        Assert.Equal("being imported", InUse.TryOpen(scen, "is open"));
        var e = Assert.Throws<InUseException>(() => InUse.Claim(new[] { (InUse.Pack(root, "other"), "the pack 'Other'"), (pack, "the pack 'Mine'") }, "being installed"));
        Assert.Equal("The pack 'Mine' is being imported right now: try again once that has finished.", e.Message);
        Assert.Null(InUse.BeingReplaced(InUse.Pack(root, "other")));
        hold.Dispose();
        hold.Dispose();
        Assert.Null(InUse.BeingReplaced(pack));
        Assert.Null(InUse.TryOpen(scen, "is open"));
        InUse.Close(scen);
    }
}
