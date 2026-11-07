using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    [Fact]
    public void A_module_s_own_panels_and_a_piece_s_come_over_with_the_pictures_they_name()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/crest.png", 8, 8, (_, _) => SKColors.Gold);
        LocalPicture(cfg, "http://a/tile.png", 8, 8, (_, _) => SKColors.Gray);
        var crest = new JsonArray(new JsonObject { ["Type"] = 0, ["Name"] = "crest", ["URL"] = "http://a/crest.png" });
        var tile = Obj("Custom_Tile", "Brigade", 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/tile.png" },
            ["XmlUI"] = "<Panel id=\"plate\" width=\"200\" height=\"100\"><Image id=\"badge\" image=\"crest\"/></Panel>",
        });
        var module = Module("panelled", tile);
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["XmlUI"] = "<?xml version=\"1.0\"?><Defaults><Text fontSize=\"18\"/></Defaults><Panel id=\"orders\"><Image id=\"flag\" image=\"crest\"/><Text id=\"title\">Orders</Text></Panel>";
        json["CustomUIAssets"] = crest;
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "panelled", "scenario.json"));
        var picture = Assert.Contains("crest", (IDictionary<string, string>)table.UiPictures);
        Assert.Equal(new[] { "Defaults", "Panel" }, table.Ui.Select(n => n.Tag));
        Assert.Equal(picture, table.Ui[1].Find("flag")!.Attributes["image"]);
        Assert.Equal("Orders", table.Ui[1].Find("title")!.Attributes["text"]);
        var brigade = Assert.Single(table.UmpireBoard);
        Assert.Equal(picture, Assert.Single(brigade.Ui).Find("badge")!.Attributes["image"]);
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.panelled", "pack.json"));
        Assert.True(pack.Assets.ContainsKey(picture), "the picture panels name is kept in the module's pack");
    }
}
