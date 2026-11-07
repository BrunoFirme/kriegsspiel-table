using System.Text.Json.Nodes;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    [Fact]
    public void A_kind_s_key_and_so_its_id_stays_as_0_8_1_made_it_and_only_a_book_adds_its_pdf()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        string Key(string type, JsonObject extra) => defs.VisualKey(new ModuleObj(Obj(type, "", 1, extra)));
        var picture = new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/x.png" } };
        var block = new JsonObject { ["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0, ["a"] = 0.5 } };
        var d6 = new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/d.png", ["CustomDice"] = new JsonObject { ["Type"] = 1 } } };
        var book = new JsonObject { ["CustomPDF"] = new JsonObject { ["PDFUrl"] = "http://a/rules.pdf" } };

        Assert.Equal("Custom_Token|http://a/x.png||||", Key("Custom_Token", picture));
        Assert.Equal("BlockSquare|||||#FF0000", Key("BlockSquare", block));
        Assert.Equal("Custom_Dice|http://a/d.png||||", Key("Custom_Dice", d6));
        Assert.Equal("Custom_PDF|||||" + "|pdf:http://a/rules.pdf", Key("Custom_PDF", book));
    }
}
