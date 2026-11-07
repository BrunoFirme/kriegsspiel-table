using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    Scenario PanelledModule(string globalXml, string objectXml)
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/crest.png", 8, 8, (_, _) => SKColors.Gold);
        LocalPicture(cfg, "http://a/tile.png", 8, 8, (_, _) => SKColors.Gray);
        var assets = new JsonArray(new JsonObject { ["Type"] = 0, ["Name"] = "crest", ["URL"] = "http://a/crest.png" }, new JsonObject { ["Type"] = 1, ["Name"] = "font", ["URL"] = "http://a/font.unity3d" });
        var tile = Obj("Custom_Tile", "Brigade", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/tile.png" }, ["XmlUI"] = objectXml,
            ["CustomUIAssets"] = new JsonArray(new JsonObject { ["Type"] = 0, ["Name"] = "badge", ["URL"] = "http://a/tile.png" }) });
        var module = Module("panelled", tile);
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["XmlUI"] = globalXml;
        json["CustomUIAssets"] = assets;
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;
        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;
        Assert.Equal(0, code);
        return Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "panelled", "scenario.json"));
    }

    [Fact]
    public void A_module_s_own_panels_and_a_piece_s_come_over_as_written_with_their_pictures_listed_in_order()
    {
        var table = PanelledModule("<Defaults><Text fontSize=\"18\"/></Defaults><Panel id=\"orders\"><Image id=\"flag\" image=\"crest\"/><Text id=\"title\">Orders</Text></Panel>",
            "<Panel id=\"plate\" width=\"200\" height=\"100\"><Image id=\"badge\" image=\"http://a/crest.png\"/></Panel>");
        Assert.Equal(new[] { "crest", "font" }, table.UiAssets!.Select(a => a.Name));
        Assert.Equal(new[] { 0, 1 }, table.UiAssets!.Select(a => a.Type));
        Assert.Equal("http://a/font.unity3d", table.UiAssets![1].Url);
        var picture = table.UiAssets![0].Url;
        Assert.Equal(new[] { "Defaults", "Panel" }, table.Ui.Select(n => n.Tag));
        Assert.Equal("crest", table.Ui[1].Find("flag")!.Attributes["image"]);
        Assert.Equal("Orders", table.Ui[1].Find("title")!.Value);
        Assert.Equal("18", table.UiLayout!.ById("title")!.Get("fontSize"));
        var brigade = Assert.Single(table.UmpireBoard);
        Assert.Equal("http://a/crest.png", Assert.Single(brigade.Ui).Find("badge")!.Attributes["image"]);
        Assert.NotNull(brigade.UiLayout!.ById("badge"));
        Assert.Equal("badge", Assert.Single(brigade.UiAssets!).Name);
        Assert.NotEqual("http://a/tile.png", brigade.UiAssets![0].Url);
        Assert.Equal(picture, table.UiLinks!["http://a/crest.png"]);
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.panelled", "pack.json"));
        Assert.True(pack.Assets.ContainsKey(picture), "the picture panels name is kept in the module's pack");
    }

    [Fact]
    public void Panels_behind_an_xml_declaration_do_not_build_in_the_tabletop_and_come_over_empty()
    {
        var table = PanelledModule("<?xml version=\"1.0\"?><Panel id=\"orders\"/>", "");
        Assert.Empty(table.Ui);
        Assert.NotNull(table.UiLayout!.Broken);
    }
}
