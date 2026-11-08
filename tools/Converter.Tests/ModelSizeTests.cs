using System.Text.Json.Nodes;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class ModelSizeTests : ConverterHarness
{
    async Task<(DefFactory Defs, PackOut Pack)> Ready(params string[] obj)
    {
        var cfg = TempConfig();
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        File.WriteAllLines(Path.Combine(models, AssetStore.Key("http://a/ship.obj") + ".obj"), obj);
        var rep = new Report();
        var assets = new AssetStore(cfg, rep);
        await assets.FetchAll(new[] { "http://a/ship.obj" });
        return (new DefFactory(cfg, assets, rep), new PackOut(cfg.Output, "t", "T", "t", null));
    }

    static ModuleObj Ship(double sx, double sy, double sz) => new(new JsonObject
    {
        ["Name"] = "Custom_Model", ["Nickname"] = $"Ship {sy}", ["GUID"] = Guid.NewGuid().ToString("N")[..6],
        ["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 1.0, ["posZ"] = 0.0, ["rotY"] = 0.0, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz },
        ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/ship.obj" },
    });

    [Fact]
    public async Task A_model_is_as_long_as_the_corners_its_faces_use_whatever_stray_points_its_file_also_lists()
    {
        var (defs, pack) = await Ready("v -1 0 -0.5", "v 1 0 -0.5", "v 1 0.5 0.5", "v -1 0.5 0.5", "v 40 0 0", "f 1 2 3 4");
        var d = defs.DefFor(Ship(1, 1, 1), pack)!.Def;
        Assert.Equal((2.0, 1.0), (d.Size.W, d.Size.H));
    }

    [Fact]
    public async Task A_face_of_more_than_four_corners_is_left_out_as_the_game_leaves_it_out()
    {
        var (defs, pack) = await Ready("v -1 0 -1", "v 1 0 -1", "v 1 0 1", "v -1 0 1", "v 9 0 0", "v 9 1 0", "v 9 0 1", "v 9 1 1", "v 8 2 2", "f 1 2 3 4", "f 5 6 7 8 9");
        var d = defs.DefFor(Ship(1, 1, 1), pack)!.Def;
        Assert.Equal((2.0, 2.0), (d.Size.W, d.Size.H));
        Assert.DoesNotContain(File.ReadLines(Path.Combine(pack.Dir, pack.M.Assets[d.Mesh!].File)), l => l.StartsWith("f 5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_file_the_game_cannot_read_is_left_out_as_the_game_shows_none_of_it()
    {
        var (defs, pack) = await Ready("v -1 0 -1 1", "v 1 0 -1", "v 1 1 1", "f 1 2 3");
        Assert.Null(defs.DefFor(Ship(1, 1, 1), pack));
    }

    [Fact]
    public async Task Two_copies_of_one_model_at_different_heights_are_two_kinds_each_with_its_own_stretched_model()
    {
        var (defs, pack) = await Ready("v -1 0 -1", "v 1 0 -1", "v 1 1 1", "v -1 1 1", "f 1 2 3 4");
        var low = defs.DefFor(Ship(1, 1, 1), pack)!.Def;
        var tall = defs.DefFor(Ship(1, 5, 1), pack)!.Def;
        Assert.NotEqual(low.Id, tall.Id);
        Assert.NotEqual(low.Mesh, tall.Mesh);
    }
}
