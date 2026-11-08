using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class SpawnedKindTests : ConverterHarness
{
    async Task<PieceDef> Spawned(string type, string scale, string custom)
    {
        var cfg = TempConfig();
        foreach (var url in new[] { "http://a/front.png", "http://a/back.png" })
            LocalPicture(cfg, url, 64, 32, (x, y) => (x - 32) * (x - 32) + (y - 16) * (y - 16) < 200 ? SKColors.Red : SKColors.Transparent);
        var rep = new Report();
        var assets = new AssetStore(cfg, rep);
        await assets.FetchAll(new[] { "http://a/front.png", "http://a/back.png" });
        var defs = new DefFactory(cfg, assets, rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var script = defs.Translate($$"""
            function spawnIt()
              local o = spawnObject({ type = '{{type}}', position = {0, 1, 0}, scale = {{scale}} })
              o.setCustomObject({ {{custom}} })
            end
            """, pack, "spawner").Script;
        var spawned = Assert.Single(pack.M.Pieces);
        Assert.Contains(spawned.Id, script);
        return spawned;
    }

    [Fact]
    public async Task A_tile_a_script_spawns_keeps_its_own_thickness_back_shape_and_the_depth_its_scale_gives_it()
    {
        var tile = await Spawned("Custom_Tile", "{x = 1.2, y = 1, z = 1.7}", "image = 'http://a/front.png', image_bottom = 'http://a/back.png', thickness = 0.125, type = 2");
        Assert.Equal((4.8, 3.4), (tile.Size.W, tile.Size.H));
        Assert.Equal(0.125, tile.Thickness);
        Assert.NotNull(tile.Back);
        Assert.Equal(PackManifest.ShapeOf("Custom_Tile", 2), tile.Shape);
    }

    [Fact]
    public async Task A_token_a_script_spawns_is_cut_from_its_picture_and_as_thick_as_its_thickness_and_height_scale_make_it()
    {
        var token = await Spawned("Custom_Token", "{0.8, 2, 0.8}", "image = 'http://a/front.png', thickness = 0.3, merge_distance = 5");
        Assert.Equal(0.6, token.Thickness);
        Assert.NotNull(token.Outline);
        Assert.Null(token.Back);
    }

    [Fact]
    public async Task A_stock_checker_a_script_spawns_by_its_type_comes_over_as_the_checker_an_import_gives_and_the_spawn_takes_it()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var script = defs.Translate("""
            function spawnIt()
              local o = spawnObject({ type = 'Checker_white', position = {0, 1, 0}, scale = {0.6, 0.6, 0.6} })
              o.setColorTint({1, 0, 0})
            end
            """, pack, "spawner").Script;
        var checker = Assert.Single(pack.M.Pieces);
        Assert.Contains($"\"{checker.Id}\")", script);
        Assert.Equal(Math.Round(DefFactory.CheckerAcross * 0.6, 4), checker.Size.W, 3);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_tile_a_script_spawns_with_a_secondary_picture_shows_it_on_its_back()
    {
        var tile = await Spawned("Custom_Tile", "{x = 0.5, y = 1, z = 0.5}", "image = 'http://a/front.png', image_secondary = 'http://a/back.png'");
        Assert.NotNull(tile.Back);
        Assert.Equal(0.2, tile.Thickness);
    }
}
