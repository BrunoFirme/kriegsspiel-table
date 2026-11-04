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
    }

    static string LibraryFile => Path.Combine(DataPaths.Local, "library.json");

    [Fact]
    public void Folder_names_are_trimmed_kept_free_of_slashes_and_sorted_without_twins()
    {
        var lib = new Library();
        Assert.True(lib.AddFolder("  Zulu "));
        Assert.True(lib.AddFolder("alpha/beta\\gamma"));
        Assert.True(lib.AddFolder("Mike"));
        Assert.False(lib.AddFolder("zulu"));
        Assert.False(lib.AddFolder("   "));
        Assert.Equal(new[] { "alpha·beta·gamma", "Mike", "Zulu" }, lib.Folders);
        Assert.True(File.Exists(LibraryFile));
    }

    [Fact]
    public void A_renamed_folder_keeps_what_was_in_it_and_cannot_take_another_folders_name()
    {
        var lib = new Library();
        lib.AddFolder("A");
        lib.AddFolder("B");
        lib.Move(false, "feud", "A");
        lib.Move(true, "pack1", "A");
        lib.Move(true, "pack2", "B");
        Assert.False(lib.RenameFolder("A", "b"));
        Assert.False(lib.RenameFolder("A", "a"));
        Assert.False(lib.RenameFolder("Nope", "C"));
        Assert.False(lib.RenameFolder("A", " "));
        Assert.True(lib.RenameFolder("A", "A"));
        Assert.True(lib.RenameFolder("A", "0 first"));
        Assert.Equal(new[] { "0 first", "B" }, lib.Folders);
        Assert.Equal("0 first", lib.FolderOf(false, "feud"));
        Assert.Equal("0 first", lib.FolderOf(true, "pack1"));
        Assert.Equal("B", lib.FolderOf(true, "pack2"));
    }

    [Fact]
    public void Removing_a_folder_unfiles_its_items_and_a_folder_gone_counts_as_unfiled()
    {
        var lib = new Library();
        lib.AddFolder("A");
        lib.Move(false, "s1", "A");
        lib.Move(false, "s2", "Gone");
        Assert.Equal("A", lib.FolderOf(false, "s1"));
        Assert.Equal("", lib.FolderOf(false, "s2"));
        Assert.Equal("", lib.FolderOf(true, "s1"));
        lib.RemoveFolder("A");
        Assert.Empty(lib.Folders);
        Assert.False(lib.Scenarios.ContainsKey("s1"));
        Assert.True(lib.Scenarios.ContainsKey("s2"));
        lib.Move(false, "s2", "");
        Assert.Empty(lib.Scenarios);
        lib.Move(true, "p", "X");
        lib.Forget(true, "p");
        Assert.Empty(lib.Packs);
    }

    [Fact]
    public void Items_group_unfiled_first_then_by_folder_or_as_the_one_folder_viewed()
    {
        var lib = new Library();
        string G(IEnumerable<(string folder, List<string> items)> g) => string.Join(" | ", g.Select(x => x.folder + ":" + string.Join(",", x.items)));
        Assert.Equal(":a,b", G(lib.Grouped(new[] { "a", "b" }, i => i, false, "")));
        lib.AddFolder("F");
        lib.AddFolder("E");
        lib.AddFolder("Empty");
        Assert.Equal(":a", G(lib.Grouped(new[] { "a" }, i => i, false, "")));
        lib.Move(false, "a", "F");
        lib.Move(false, "c", "E");
        lib.Move(true, "b", "F");
        Assert.Equal("E:c | F:a", G(lib.Grouped(new[] { "a", "c" }, i => i, false, "")));
        Assert.Equal(":b,d | E:c | F:a", G(lib.Grouped(new[] { "a", "b", "c", "d" }, i => i, false, "")));
        Assert.Equal("F:b", G(lib.Grouped(new[] { "a", "b" }, i => i, true, "F")));
        Assert.Equal("Empty:", G(lib.Grouped(new[] { "a", "b" }, i => i, true, "Empty")));
    }

    [Fact]
    public void The_library_reads_back_what_was_saved_and_mends_a_hand_edited_file()
    {
        Assert.Empty(Library.Load().Folders);
        var lib = new Library();
        lib.AddFolder("One");
        lib.Move(false, "feud", "One");
        var back = Library.Load();
        Assert.Equal(new[] { "One" }, back.Folders);
        Assert.Equal("One", back.FolderOf(false, "feud"));
        Assert.Equal("{\n  \"Folders\": [\n    \"One\"\n  ],\n  \"Scenarios\": {\n    \"feud\": \"One\"\n  },\n  \"Packs\": {}\n}", File.ReadAllText(LibraryFile).Replace("\r\n", "\n"));

        File.WriteAllText(LibraryFile, "{\"Folders\":[\"A\",\" \",\"\"],\"Scenarios\":null,\"Packs\":null}");
        var mended = Library.Load();
        Assert.Equal(new[] { "A" }, mended.Folders);
        Assert.Empty(mended.Scenarios);
        Assert.Empty(mended.Packs);
    }

    [Fact]
    public void A_library_that_cannot_be_read_is_copied_aside_and_told_of_and_starts_empty()
    {
        File.WriteAllText(LibraryFile, "{ not json");
        Problems.Take();
        var told = new List<string>();
        Action<string> h = told.Add;
        Problems.Told += h;
        try { Assert.Empty(Library.Load().Folders); }
        finally { Problems.Told -= h; }
        var said = Assert.Single(told);
        Assert.StartsWith("Your folders on the title screen (library.json) could not be read (", said);
        Assert.EndsWith("), so they start empty; a copy of it is kept beside it.", said);
        Assert.Single(Directory.GetFiles(DataPaths.Local, "library.json.bad-*"));
        foreach (var f in Directory.GetFiles(DataPaths.Local, "library.json.bad-*")) File.Delete(f);

        File.WriteAllText(LibraryFile, "null");
        Assert.Empty(Library.Load().Folders);
        Assert.Contains("(the file is empty)", Assert.Single(Problems.Take()));
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
