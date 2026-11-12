using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public class TextPlaceTests : ConverterHarness
{
    const double HalfALineAtSixtyFour = 0.48, Close = 1e-6;

    static JsonObject Text(string words, double fontSize, double x, double y, double z, double rotX, double rotY, double rotZ)
    {
        var o = Obj("3DText", "", 1, new JsonObject { ["Text"] = new JsonObject { ["Text"] = words, ["fontSize"] = fontSize } });
        o["Transform"] = new JsonObject
        {
            ["posX"] = x, ["posY"] = y, ["posZ"] = z, ["rotX"] = rotX, ["rotY"] = rotY, ["rotZ"] = rotZ, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0,
        };
        return o;
    }

    [Theory]
    [InlineData(90, 0, 0, 0, 0, -HalfALineAtSixtyFour)]
    [InlineData(90, 90, 0, -HalfALineAtSixtyFour, 0, 0)]
    [InlineData(90, 180, 0, 0, 0, HalfALineAtSixtyFour)]
    [InlineData(90, 0, 90, HalfALineAtSixtyFour, 0, 0)]
    [InlineData(0, 0, 0, 0, -HalfALineAtSixtyFour, 0)]
    public void Three_d_text_hangs_below_its_saved_place_so_its_middle_is_half_its_height_down_the_way_it_reads(
        double rotX, double rotY, double rotZ, double dx, double dy, double dz)
    {
        var middle = DefFactory.TextMiddle(new ModuleObj(Text("Ford", 64, 3, 1, 5, rotX, rotY, rotZ)));

        Assert.Equal(3 + dx, middle.X, Close);
        Assert.Equal(1 + dy, middle.Y, Close);
        Assert.Equal(5 + dz, middle.Z, Close);
    }

    [Theory]
    [InlineData("Ford", 64, 0.96)]
    [InlineData("Ford\nBridge", 48, 1.44)]
    [InlineData("[b]Ford[/b]\n\nBridge", 100, 4.5)]
    public void A_line_of_three_d_text_is_as_tall_as_its_font_size_whatever_its_letters(string words, double fontSize, double height) =>
        Assert.Equal(height, DefFactory.TextHeight(new ModuleObj(Text(words, fontSize, 0, 1, 0, 90, 0, 0))), Close);

    [Fact]
    public void Three_d_text_is_measured_in_the_game_s_table_face_and_named_without_its_formatting_codes()
    {
        const double PlaceholderWidthInTheGame = 274 * DefFactory.TextUnitsPerFontSize, Slack = 0.05;
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var plain = defs.DefFor(new ModuleObj(Text("Type Here", 64, 0, 1, 0, 90, 0, 0)), pack)!;
        var coded = defs.DefFor(new ModuleObj(Text("[b]Type[/b] [ff0000]Here", 64, 0, 1, 0, 90, 0, 0)), pack)!;

        Assert.InRange(plain.Def.Size.W, PlaceholderWidthInTheGame - Slack, PlaceholderWidthInTheGame + Slack);
        Assert.Equal(0.96, plain.Def.Size.H, Close);
        Assert.Equal(plain.Def.Size, coded.Def.Size);
        Assert.Equal("Type Here", coded.Def.Name);
        Assert.Equal("[b]Type[/b] [ff0000]Here", coded.Lettering!.Text);
    }

    [Fact]
    public void An_imported_three_d_text_lies_where_the_module_showed_it()
    {
        TempConfig();
        var module = Module("labels",Text("Ford", 64, 3, 1, 5, 90, 0, 0), Text("Upper\nFord", 48, -4, 1, 2, 90, 90, 0));
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "labels", "scenario.json"));
        var ford = table.UmpireBoard.Single(p => p.Lettering?.Text == "Ford");
        var upper = table.UmpireBoard.Single(p => p.Lettering?.Text == "Upper\nFord");
        Assert.Equal((3.0, 5 - HalfALineAtSixtyFour), (ford.Pos.X, ford.Pos.Z));
        Assert.Equal((-4 - 0.72, 2.0), (upper.Pos.X, upper.Pos.Z));
    }

    [Fact]
    public void A_note_card_keeps_its_formatting_codes_for_its_face_to_draw_and_is_named_without_them()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var card = Obj("Notecard", "[b]Orders[/b]", 1);
        card["Description"] = "Hold the [ff0000]ford[-]";

        var def = defs.DefFor(new ModuleObj(card), pack)!.Def;

        Assert.Equal("Orders", def.Name);
        Assert.Equal(new[] { "[b]Orders[/b]", "Hold the [ff0000]ford[-]" }, def.TextLines.Select(t => t.Default));
    }
}
