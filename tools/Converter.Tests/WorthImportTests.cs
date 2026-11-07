using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    [Fact]
    public void A_stack_of_chips_comes_over_as_that_many_pieces_one_on_another_each_keeping_its_worth()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/chip.png", 8, 8, (_, _) => SKColors.Gold);
        var chips = Obj("Custom_Token", "Gold", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/chip.png" }, ["Number"] = 4, ["Value"] = 10 });
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module("chips", chips), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "chips", "scenario.json"));
        Assert.Equal(4, table.UmpireBoard.Count);
        Assert.All(table.UmpireBoard, p => Assert.Equal(10, p.Worth));
        Assert.Equal(new[] { 0, 1, 2, 3 }, table.UmpireBoard.Select(p => p.Stack).OrderBy(s => s));
        Assert.Single(table.UmpireBoard, p => p.Guid != null);
    }

    [Fact]
    public async Task A_model_keeps_the_material_and_shine_the_module_gave_it()
    {
        var cfg = TempConfig();
        const string Mesh = "http://a/cannon.obj";
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        File.WriteAllLines(Path.Combine(models, AssetStore.Key(Mesh) + ".obj"), new[] { "v 0 0 0", "v 1 0 0", "v 0 1 1", "f 1 2 3" });
        var rep = new Report();
        var assets = new AssetStore(cfg, rep);
        await assets.FetchAll(new[] { Mesh });
        var defs = new DefFactory(cfg, assets, rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        PieceDef Cannon(int material, JsonObject? shader) => defs.DefFor(new ModuleObj(Obj("Custom_Model", $"Cannon {material}", 1, new JsonObject
        {
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = Mesh, ["MaterialIndex"] = material, ["CustomShader"] = shader },
        })), pack)!.Def;

        var metal = Cannon(2, new JsonObject
        {
            ["SpecularColor"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.5, ["b"] = 0.0 }, ["SpecularIntensity"] = 0.4, ["SpecularSharpness"] = 6.0, ["FresnelStrength"] = 0.3,
        });

        Assert.Equal(Finishes.Metal, metal.Finish);
        Assert.Equal(("#FF8000", 0.4, 6.0, 0.3), (metal.Shine!.Color, metal.Shine.Strength, metal.Shine.Sharpness, metal.Shine.Rim));
    }
}
