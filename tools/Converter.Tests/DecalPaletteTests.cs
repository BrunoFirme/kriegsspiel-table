using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    [Fact]
    public void A_module_s_decal_palette_and_its_decals_on_the_table_come_over_with_their_pictures_and_sizes()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/guts.png", 8, 8, (_, _) => SKColors.DarkRed);
        LocalPicture(cfg, "http://a/arrow.png", 16, 8, (_, _) => SKColors.Black);
        var module = Module("stamped", Obj("Custom_Tile", "Unit", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/arrow.png" } }));
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["DecalPallet"] = new JsonArray(
            new JsonObject { ["Name"] = "Guts", ["ImageURL"] = "http://a/guts.png", ["Size"] = 0.5 },
            new JsonObject { ["Name"] = "Arrow", ["ImageURL"] = "http://a/arrow.png", ["Size"] = 2.0 });
        json["Decals"] = new JsonArray(new JsonObject
        {
            ["Transform"] = new JsonObject { ["posX"] = 3.0, ["posY"] = 1.0, ["posZ"] = -4.0, ["rotX"] = 90.0, ["rotY"] = 210.0, ["rotZ"] = 0.0, ["scaleX"] = 1.2, ["scaleY"] = 0.5, ["scaleZ"] = 0.5 },
            ["CustomDecal"] = new JsonObject { ["Name"] = "Guts", ["ImageURL"] = "http://a/guts.png", ["Size"] = 0.5 },
        });
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "stamped", "scenario.json"));
        Assert.Equal(new[] { ("Guts", 0.5), ("Arrow", 2.0) }, table.DecalPalette.Select(p => (p.Name, p.Size)));
        var guts = Assert.Single(table.Decals["umpire"]);
        Assert.Equal((3.0, -4.0, 1.2, 0.5, 30.0, table.DecalPalette[0].Picture), (guts.X, guts.Z, guts.W, guts.H, guts.Rot, guts.Picture));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.stamped", "pack.json"));
        Assert.True(table.DecalPalette.All(p => pack.Assets.ContainsKey(p.Picture)), "the palette's pictures are kept in the module's pack");
        Assert.Contains("tts.stamped", table.Packs);
        Assert.DoesNotContain(table.Packs, p => p.EndsWith(".extras"));
        Assert.False(Directory.Exists(Path.Combine(Packages, "tts.stamped.extras")), "a module comes over as one pack");
    }
}
