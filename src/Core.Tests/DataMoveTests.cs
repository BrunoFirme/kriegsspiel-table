using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class DataMoveTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-move-tests", Guid.NewGuid().ToString("N"));
    string From => Path.Combine(_root, "Documents", "saves");
    string To => Path.Combine(_root, "Local", "plays");

    string Put(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    [Fact]
    public void A_folder_not_there_yet_moves_whole_and_leaves_nothing_behind()
    {
        Put(From, Path.Combine("feud", "Run 1", "Turn 1.kstable"), "t1");
        Put(From, Path.Combine("feud", ".safety", "20261101-120000", "time-0001.kstable"), "cp");
        SaveStore.MoveTree(From, To);
        Assert.Equal("t1", Read(To, Path.Combine("feud", "Run 1", "Turn 1.kstable")));
        Assert.Equal("cp", Read(To, Path.Combine("feud", ".safety", "20261101-120000", "time-0001.kstable")));
        Assert.False(Directory.Exists(From));
    }

    [Fact]
    public void Nothing_already_there_is_overwritten()
    {
        Put(To, Path.Combine("feud", "Named.kstable"), "theirs");
        Put(To, Path.Combine("feud", "Same.kstable"), "same");
        Put(From, Path.Combine("feud", "Named.kstable"), "mine");
        Put(From, Path.Combine("feud", "Same.kstable"), "same");
        SaveStore.MoveTree(From, To);
        Assert.Equal("theirs", Read(To, Path.Combine("feud", "Named.kstable")));
        Assert.Equal("mine", Read(To, Path.Combine("feud", "Named (2).kstable")));
        Assert.Equal("same", Read(To, Path.Combine("feud", "Same.kstable")));
        Assert.False(File.Exists(Path.Combine(To, "feud", "Same (2).kstable")));
        Assert.False(Directory.Exists(From));
    }

    [Fact]
    public void Plain_folders_of_the_same_name_are_merged()
    {
        Put(To, Path.Combine("Scripts", "packs", "a.lua"), "a");
        Put(From, Path.Combine("Scripts", "packs", "b.lua"), "b");
        SaveStore.MoveTree(From, To);
        Assert.Equal(new[] { "a.lua", "b.lua" }, Directory.GetFiles(Path.Combine(To, "Scripts", "packs")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_file_that_cannot_move_stays_where_it_is_and_moves_at_the_next_try()
    {
        Put(From, Path.Combine("feud", "Run 1", "Turn 1.kstable"), "t1");
        var held = Put(From, Path.Combine("feud", "Run 1", "Turn 2.kstable"), "t2");
        List<string> lines;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None)) lines = SaveStore.MoveTree(From, To);
        Assert.Contains(lines, l => l.StartsWith(held) && l.Contains("not moved"));
        Assert.Equal("t2", File.ReadAllText(held));
        Assert.Equal("t1", Read(To, Path.Combine("feud", "Run 1", "Turn 1.kstable")));
        SaveStore.MoveTree(From, To);
        Assert.Equal("t2", Read(To, Path.Combine("feud", "Run 1", "Turn 2.kstable")));
        Assert.False(Directory.Exists(From));
    }

    [Fact]
    public void The_scripts_and_captures_left_in_documents_are_moved_to_the_app_s_own_data_folder()
    {
        var moves = SaveStore.FromDocuments.ToDictionary(m => Path.GetFileName(m.From), m => m.To);
        Assert.Equal(new[] { "Scripts", "captures" }, moves.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(DataPaths.Scripts, moves["Scripts"]);
        Assert.Equal(DataPaths.Captures, moves["captures"]);
        Assert.All(SaveStore.FromDocuments, m => Assert.StartsWith(DataPaths.Documents, m.From));
    }
}
