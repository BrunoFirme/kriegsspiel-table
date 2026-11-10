using KTable.Core;
using KTable.Core.Model;
using KTable.Server;

namespace KTable.Client.Tests;

public class FetchedFilesTests
{
    const string Pack = "user.mirror-test";
    const string ScenarioPackage = "scenarios/the-valley";
    const string Image = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f";
    const string Backdrop = "pictures/1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e.png";

    readonly string _installed, _fetched, _save;
    readonly string _picture = "assets/" + new string('a', 64) + ".png";
    string _relief = "";
    List<string> _reliefFiles = new();

    public FetchedFilesTests()
    {
        var dir = TestFolders.New();
        _installed = Path.Combine(dir, "packages");
        _fetched = Path.Combine(dir, "cache", "packages");
        _save = Path.Combine(dir, "mirror.kstable");
        Directory.CreateDirectory(_installed);
    }

    string Fetched(string package, string rel) => Path.Combine(_fetched, package, rel);
    string Installed(string package, string rel) => Path.Combine(_installed, package, rel);

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    static PackManifest Manifest(string name, string picture) => new()
    {
        Id = Pack, Name = name,
        Pieces = [new PieceDef { Id = Pack + ".gun", Name = "Gun", Image = "a" }],
        Assets = new() { ["a"] = new AssetEntry { File = picture } },
    };

    void FetchWhatAJoinerKeeps()
    {
        Json.Save(Fetched(Pack, "pack.json"), Manifest("fetched", _picture));
        Write(Fetched(Pack, _picture), "fetched picture");
        Write(Fetched(ScenarioPackage, $"tiles/{Image}/0/0_0.jpg"), "tile");
        Write(Fetched(ScenarioPackage, $"tiles/{Image}/index.json"), "{}");
        var grid = ReliefGrid.Flat(10, 10);
        grid.H[0] = 5;
        (_relief, _) = ReliefFiles.Save(Path.Combine(_fetched, ScenarioPackage), grid);
        _reliefFiles = ReliefFiles.Files(_relief, Json.Load<ReliefIndex>(Fetched(ScenarioPackage, ReliefFiles.HeaderRel(_relief)))).ToList();
        Write(Fetched("sheets", Backdrop), "backdrop");
    }

    void SaveMirror()
    {
        var game = new GameState { Packs = [Pack], Sheets = [new SheetDef { Id = "s-1", Name = "The valley", Image = Image, Package = ScenarioPackage, Relief = _relief }] };
        game.Lighting.Backdrop = Backdrop;
        game.Layer(Layers.Umpire).Add(new PieceInstance { Id = "p-1", Def = Pack + ".gun" });
        Json.Save(_save, new SaveFile { ScenarioId = "the-valley", Snapshot = game, MirrorOf = "Host" });
    }

    Brought Host() => FetchedFiles.ForSave(_save, new PackageStore(_installed), _fetched);

    [Fact]
    public void A_mirror_whose_packs_and_map_only_this_pc_s_fetched_files_hold_opens_with_none_missing()
    {
        FetchWhatAJoinerKeeps();
        SaveMirror();
        Assert.Contains(Pack, SaveLoader.Load(_save, new PackageStore(_installed)).MissingPacks);

        var brought = Host();

        Assert.Equal([Pack], brought.Packs);
        var installed = new PackageStore(_installed);
        var server = SaveLoader.Load(_save, installed);
        Assert.Empty(server.MissingPacks);
        Assert.NotNull(installed.Def(Pack + ".gun"));
        Assert.Equal("fetched picture", File.ReadAllText(Installed(Pack, _picture)));
        Assert.True(installed.HasTiles(server.State.Sheets[0]));
        Assert.All(_reliefFiles, rel => Assert.True(File.Exists(Installed(ScenarioPackage, rel)), rel));
        Assert.True(File.Exists(Installed("sheets", Backdrop)));
        Assert.Equal(2 + _reliefFiles.Count + 1, brought.MapFiles);
    }

    [Fact]
    public void A_pack_this_pc_has_installed_is_never_replaced_or_added_to()
    {
        FetchWhatAJoinerKeeps();
        var ownPicture = "assets/" + new string('b', 64) + ".png";
        Json.Save(Installed(Pack, "pack.json"), Manifest("my own", ownPicture));
        Write(Installed(Pack, ownPicture), "my picture");
        SaveMirror();

        var brought = Host();

        Assert.Empty(brought.Packs);
        Assert.Equal("my own", Json.Load<PackManifest>(Installed(Pack, "pack.json")).Name);
        Assert.False(File.Exists(Installed(Pack, _picture)));
        Assert.Equal("my picture", File.ReadAllText(Installed(Pack, ownPicture)));
    }

    [Fact]
    public void A_file_already_here_is_never_written_over()
    {
        FetchWhatAJoinerKeeps();
        Write(Installed(ScenarioPackage, $"tiles/{Image}/0/0_0.jpg"), "my tile");
        Write(Installed(Pack, _picture), "my picture");
        SaveMirror();

        Host();

        Assert.Equal("my tile", File.ReadAllText(Installed(ScenarioPackage, $"tiles/{Image}/0/0_0.jpg")));
        Assert.Equal("my picture", File.ReadAllText(Installed(Pack, _picture)));
        Assert.True(File.Exists(Installed(Pack, "pack.json")));
    }

    [Fact]
    public void A_map_this_pc_has_elsewhere_is_left_where_it_is()
    {
        FetchWhatAJoinerKeeps();
        Write(Installed("user.maps", $"tiles/{Image}/index.json"), "{}");
        Write(Installed("user.maps", ReliefFiles.HeaderRel(_relief)), "{}");
        SaveMirror();

        var brought = Host();

        Assert.Equal(1, brought.MapFiles);
        Assert.False(Directory.Exists(Path.Combine(_installed, "scenarios")));
    }

    [Fact]
    public void Hosting_with_the_fetched_files_as_the_installed_ones_copies_nothing()
    {
        FetchWhatAJoinerKeeps();
        SaveMirror();
        Assert.Same(Brought.Nothing, FetchedFiles.ForSave(_save, new PackageStore(_fetched), _fetched));
    }

    [Fact]
    public void An_unreadable_save_brings_nothing()
    {
        FetchWhatAJoinerKeeps();
        Write(_save, "not a save");
        Assert.Same(Brought.Nothing, Host());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_installed));
    }
}
