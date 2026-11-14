using KTable.Core;

namespace KTable.Client.Tests;

public class SharedMapsTests
{
    const string Map = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f";
    static readonly string[] Files = ["0/0_0.webp", "0/1_0.webp", "1/0_0.webp", "index.json"];

    readonly string _root, _record;

    public SharedMapsTests()
    {
        var dir = TestFolders.New();
        _root = Path.Combine(dir, "packages");
        _record = Path.Combine(dir, SharedMaps.RecordFile);
        Directory.CreateDirectory(_root);
    }

    string File_(string package, string rel) => Path.Combine(_root, package, "tiles", Map, rel);

    void Holds(string package, params string[] without)
    {
        foreach (var rel in Files.Except(without))
        {
            var path = File_(package, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "tile " + rel);
        }
    }

    Joined Join(Func<bool>? stop = null) => SharedMaps.Join(_root, _record, stop);

    static long Size(string rel) => ("tile " + rel).Length;

    [Fact]
    public void A_map_held_by_the_library_and_a_scenario_ends_as_one_copy_both_still_read_and_the_next_start_reads_nothing_again()
    {
        Holds("sheets");
        Holds("scenarios/haiti");

        var joined = Join();

        Assert.Equal(new Joined(Files.Sum(Size), Files.Length, 0), joined);
        Assert.All(Files, rel => Assert.Equal("tile " + rel, File.ReadAllText(File_("scenarios/haiti", rel))));
        Assert.All(Files, rel => Assert.True(OneCopy.Holds(File_("sheets", rel), File_("scenarios/haiti", rel)), rel));
        Assert.Equal(new Joined(0, 0, 0), Join(() => throw new InvalidOperationException("nothing is looked at a second time")));
        Assert.Equal(SharedMaps.RecordBeside(_root), _record);
    }

    [Fact]
    public void A_tile_that_differs_from_the_library_s_or_that_only_the_scenario_has_is_left_exactly_as_it_is()
    {
        Holds("sheets", "1/0_0.webp");
        Holds("scenarios/haiti");
        File.WriteAllText(File_("scenarios/haiti", "0/1_0.webp"), "tile 0/1_0.wxbp");

        var joined = Join();

        Assert.Equal((2, 0), (joined.Files, joined.Waiting));
        Assert.Equal("tile 0/1_0.wxbp", File.ReadAllText(File_("scenarios/haiti", "0/1_0.webp")));
        Assert.Equal("tile 0/1_0.webp", File.ReadAllText(File_("sheets", "0/1_0.webp")));
        Assert.False(OneCopy.Holds(File_("sheets", "0/1_0.webp"), File_("scenarios/haiti", "0/1_0.webp")));
        Assert.Equal("tile 1/0_0.webp", File.ReadAllText(File_("scenarios/haiti", "1/0_0.webp")));
        Assert.Equal(new Joined(0, 0, 0), Join());
    }

    [Fact]
    public void A_map_either_side_holds_only_part_of_is_not_touched()
    {
        Holds("sheets", "index.json");
        Holds("scenarios/haiti");
        Holds("scenarios/feud", "index.json");

        Assert.Equal(new Joined(0, 0, 0), Join());

        Assert.False(OneCopy.Holds(File_("sheets", "0/0_0.webp"), File_("scenarios/haiti", "0/0_0.webp")));
        Assert.False(OneCopy.Holds(File_("scenarios/feud", "0/0_0.webp"), File_("scenarios/haiti", "0/0_0.webp")));
    }

    [Fact]
    public void Two_scenarios_with_the_same_map_and_no_library_copy_share_it_and_packs_set_aside_are_passed_over()
    {
        Holds("scenarios/feud");
        Holds("scenarios/haiti");
        Holds("_installing");

        Assert.Equal(Files.Length, Join().Files);

        Assert.True(OneCopy.Holds(File_("scenarios/feud", "0/0_0.webp"), File_("scenarios/haiti", "0/0_0.webp")));
        Assert.False(OneCopy.Holds(File_("scenarios/feud", "1/0_0.webp"), File_("_installing", "1/0_0.webp")));
    }

    [Fact]
    public void A_tile_in_use_waits_for_the_next_start_and_is_joined_then()
    {
        if (!OperatingSystem.IsWindows()) return;
        Holds("sheets");
        Holds("scenarios/haiti");

        using (new FileStream(File_("scenarios/haiti", "0/0_0.webp"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var first = Join();
            Assert.Equal((Files.Length - 1, 1), (first.Files, first.Waiting));
        }

        Assert.Equal("tile 0/0_0.webp", File.ReadAllText(File_("scenarios/haiti", "0/0_0.webp")));
        var later = Join();
        Assert.Equal(0, later.Waiting);
        Assert.True(OneCopy.Holds(File_("sheets", "0/0_0.webp"), File_("scenarios/haiti", "0/0_0.webp")));
    }

    [Fact]
    public void A_pass_told_to_stop_changes_nothing_more_and_takes_up_again_next_time()
    {
        Holds("sheets");
        Holds("scenarios/haiti");

        Assert.Equal(new Joined(0, 0, 1), Join(() => true));

        Assert.False(OneCopy.Holds(File_("sheets", "0/0_0.webp"), File_("scenarios/haiti", "0/0_0.webp")));
        Assert.Equal(Files.Length, Join().Files);
    }

    [Fact]
    public void A_data_folder_copied_elsewhere_is_looked_at_again_because_its_folders_are_new()
    {
        Holds("sheets");
        Holds("scenarios/haiti");
        Join();
        foreach (var rel in Files)
        {
            var bytes = File.ReadAllBytes(File_("scenarios/haiti", rel));
            File.Delete(File_("scenarios/haiti", rel));
            File.WriteAllBytes(File_("scenarios/haiti", rel), bytes);
        }
        Directory.SetCreationTimeUtc(Path.Combine(_root, "scenarios/haiti", "tiles", Map), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(Files.Length, Join().Files);

        Assert.True(OneCopy.Holds(File_("sheets", "1/0_0.webp"), File_("scenarios/haiti", "1/0_0.webp")));
    }

    [Fact]
    public void Where_the_drive_cannot_link_every_copy_stays_and_no_room_is_claimed_freed()
    {
        Holds("sheets");
        Holds("scenarios/haiti");
        FileLinks.Maker.Value = (_, _) => false;

        Assert.Equal(new Joined(0, 0, 1), Join());

        Assert.All(Files, rel => Assert.Equal("tile " + rel, File.ReadAllText(File_("scenarios/haiti", rel))));
        Assert.False(File.Exists(_record) && Json.Load<Dictionary<string, string>>(_record).Count > 0);
    }
}
