using KTable.Core;
using Xunit;

namespace KTable.Client.Tests;

public sealed class DataFolderMoveTests
{
    readonly string _root = TestFolders.New();

    string Default => Path.Combine(_root, "default", DataPaths.Name);

    string Put(string folder, string rel, string text)
    {
        var file = Path.Combine(folder, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    string Filled(string folder)
    {
        Put(folder, Path.Combine("packages", "user.blocks", "pack.json"), "{\"id\":\"user.blocks\"}");
        Put(folder, Path.Combine("packages", "scenarios", "ford", "scenario.json"), "{\"name\":\"Ford\"}");
        Put(folder, Path.Combine("saves", "ford", "Game 1", "game.kstable"), new string('k', 70000));
        Put(folder, Path.Combine("trash", "saves", "old.kstable"), "old");
        Put(folder, "clipboard.json", "[]");
        return folder;
    }

    static Dictionary<string, string> Read(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllText);

    [Fact]
    public void The_data_goes_in_a_folder_of_the_app_s_own_name_inside_the_one_picked()
    {
        var picked = Path.Combine(_root, "Games");
        Assert.Equal(Path.Combine(picked, DataPaths.Name), DataFolderMove.Home(picked));
        Assert.Equal(Path.Combine(picked, DataPaths.Name), DataFolderMove.Home(Path.Combine(picked, DataPaths.Name) + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void A_move_copies_everything_checks_it_points_the_app_there_and_only_then_clears_the_old_folder()
    {
        var from = Filled(Default);
        Put(from, DataFolder.AgentLive, "{\"url\":\"x\"}");
        Put(from, Path.Combine("saves", "ford", "Game 1", "game.kstable.lock"), "stale");
        var before = Read(from).Where(f => !DataFolder.StaysBehind(f.Key) && !f.Key.EndsWith(".lock", StringComparison.Ordinal)).ToDictionary(f => f.Key, f => f.Value);
        var plan = DataFolderMove.Look(Path.Combine(_root, "hdd"), from);
        Assert.Null(plan.Refusal);
        Assert.False(plan.UseAsIs);
        Assert.Equal(before.Count, plan.Files);
        Assert.Equal(before.Values.Sum(text => (long)text.Length), plan.Bytes);
        long reported = 0;
        var left = DataFolderMove.Move(plan, Default, (done, all) => reported = done);
        Assert.Empty(left);
        Assert.Equal(plan.Bytes, reported);
        Assert.Equal(before, Read(plan.To));
        Assert.Equal(plan.To, DataFolder.Chosen(Default));
        Assert.Equal(plan.To, DataFolder.Of(Default));
        Assert.Equal(new[] { DataFolder.AgentLive, DataFolder.PointerName, Path.Combine("saves", "ford", "Game 1", "game.kstable.lock") }.OrderBy(n => n),
            Read(from).Keys.OrderBy(n => n));
    }

    [Fact]
    public void A_move_that_cannot_finish_takes_its_copies_away_and_changes_nothing()
    {
        var from = Filled(Default);
        var before = Read(from);
        var plan = DataFolderMove.Look(Path.Combine(_root, "hdd"), from);
        var theirs = Put(plan.To, Path.Combine("saves", "ford", "Game 1", "game.kstable"), "someone else's file, put there after the look");
        var failed = Assert.Throws<DataMoveFailed>(() => DataFolderMove.Move(plan, Default));
        Assert.NotEmpty(failed.Message);
        Assert.Equal(before, Read(from));
        Assert.Null(DataFolder.Chosen(Default));
        Assert.Equal(Default, DataFolder.Of(Default));
        Assert.Equal(new[] { Path.GetRelativePath(plan.To, theirs) }, Read(plan.To).Keys);
        Assert.Equal("someone else's file, put there after the look", File.ReadAllText(theirs));
    }

    [Fact]
    public void A_move_is_refused_while_a_game_in_the_folder_is_open()
    {
        var from = Filled(Default);
        var save = Path.Combine(from, "saves", "ford", "Game 1", "game.kstable");
        var plan = DataFolderMove.Look(Path.Combine(_root, "hdd"), from);
        using (SaveLock.Take(save))
        {
            Assert.Equal("game", DataFolderMove.GameOpenIn(from));
            var failed = Assert.Throws<DataMoveFailed>(() => DataFolderMove.Move(plan, Default));
            Assert.Contains("is open", failed.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(plan.To));
        }
        Assert.Null(DataFolderMove.GameOpenIn(from));
    }

    [Fact]
    public void A_folder_is_refused_when_it_is_the_same_nested_or_holds_other_files_and_used_as_it_is_when_it_holds_the_app_s_data()
    {
        var from = Filled(Default);
        Assert.NotNull(DataFolderMove.Look(Path.GetDirectoryName(from)!, from).Refusal);
        Assert.NotNull(DataFolderMove.Look(Path.Combine(from, "packages"), from).Refusal);
        var foreign = Path.Combine(_root, "photos");
        Put(Path.Combine(foreign, DataPaths.Name), "holiday.txt", "not ours");
        Assert.Contains("empty", DataFolderMove.Look(foreign, from).Refusal, StringComparison.Ordinal);
        var other = Path.Combine(_root, "other-pc");
        Filled(Path.Combine(other, DataPaths.Name));
        var use = DataFolderMove.Look(other, from);
        Assert.True(use.UseAsIs);
        Assert.Null(use.Refusal);
        Assert.Throws<InvalidOperationException>(() => DataFolderMove.Move(use, Default));
        Assert.Equal(5, Read(from).Count);
    }

    [Fact]
    public void Moving_back_to_this_pc_s_folder_takes_the_pointer_away()
    {
        var away = Filled(Path.Combine(_root, "hdd", DataPaths.Name));
        DataFolder.Choose(Default, away);
        Assert.Equal(away, DataFolder.Of(Default));
        var plan = DataFolderMove.Look(Path.GetDirectoryName(Default)!, away);
        Assert.Null(plan.Refusal);
        Assert.False(plan.UseAsIs);
        Assert.Empty(DataFolderMove.Move(plan, Default));
        Assert.False(File.Exists(DataFolder.PointerPath(Default)));
        Assert.Equal(Default, DataFolder.Of(Default));
        Assert.Equal(5, Read(Default).Count);
        Assert.False(Directory.Exists(away));
    }

    [Fact]
    public void A_pointer_to_a_folder_that_is_gone_is_reported_not_followed_silently()
    {
        Assert.Null(DataFolder.Missing(Default));
        var away = Path.Combine(_root, "unplugged", DataPaths.Name);
        DataFolder.Choose(Default, away);
        Assert.Equal(away, DataFolder.Missing(Default));
        Directory.CreateDirectory(away);
        Assert.Null(DataFolder.Missing(Default));
        File.WriteAllText(DataFolder.PointerPath(Default), "not a whole path");
        Assert.Null(DataFolder.Chosen(Default));
        DataFolder.Choose(Default, null);
        Assert.False(File.Exists(DataFolder.PointerPath(Default)));
    }

    [Fact]
    public void A_path_kept_inside_the_old_folder_follows_the_data()
    {
        var from = Path.Combine(_root, "a");
        var to = Path.Combine(_root, "b");
        Assert.Equal(Path.Combine(to, "captures"), DataFolderMove.Rebased(Path.Combine(from, "captures"), from, to));
        Assert.Equal(to, DataFolderMove.Rebased(from, from, to));
        var elsewhere = Path.Combine(_root, "pictures");
        Assert.Equal(elsewhere, DataFolderMove.Rebased(elsewhere, from, to));
    }
}
