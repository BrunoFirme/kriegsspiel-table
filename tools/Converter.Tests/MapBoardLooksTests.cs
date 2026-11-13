using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;
using Xunit;

namespace KTable.Converter.Tests;

public sealed class MapBoardLooksTests : ConverterHarness
{
    static JsonObject Picture(string url) => new() { ["CustomImage"] = new JsonObject { ["ImageURL"] = url } };

    static JsonObject Placed(JsonObject o, double x, double z, double turn)
    {
        o["Transform"]!["posX"] = x;
        o["Transform"]!["posZ"] = z;
        o["Transform"]!["rotY"] = turn;
        return o;
    }

    Scenario Import(string name, JsonObject board)
    {
        var main = typeof(Config).Assembly.EntryPoint!;
        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module(name, board), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;
        Assert.Equal(0, code);
        return Json.Load<Scenario>(Path.Combine(Packages, "scenarios", name, "scenario.json"));
    }

    string Report(string name) => File.ReadAllText(Path.Combine(Packages, "scenarios", name, "import-report.md"));

    [Fact]
    public void Every_picture_look_of_a_map_board_comes_in_as_its_own_sheet_stacked_where_the_board_lies_with_the_shown_look_on_top()
    {
        var cfg = TempConfig();
        foreach (var (url, colour) in new[] { ("http://a/north.png", SKColors.Navy), ("http://a/coast.png", SKColors.Teal), ("http://a/south.png", SKColors.Olive) })
            LocalPicture(cfg, url, 20, 10, (_, _) => colour);
        var board = Placed(Obj("Custom_Board", "Front", 1, Picture("http://a/south.png")), 12, -7, 90);
        board["States"] = new JsonObject
        {
            ["1"] = Placed(Obj("Custom_Board", "", 2, Picture("http://a/north.png")), 300, 300, 0),
            ["2"] = Placed(Obj("Custom_Board", "Coast", 1, Picture("http://a/coast.png")), 300, 300, 0),
        };

        var sheets = Import("looks", board).Sheets;

        Assert.Equal(["Front, state 3", "Front, state 1", "Coast"], sheets.Select(s => s.Name));
        Assert.All(sheets, s => Assert.Equal((12.0, -7.0, 90.0), (s.Pos.X, s.Pos.Z, s.RotDeg)));
        Assert.Equal(["http://a/south.png", "http://a/north.png", "http://a/coast.png"], sheets.Select(s => s.Url));
        Assert.Equal(3, sheets.Select(s => s.Image).Distinct().Count());
        var shown = sheets.Single(s => s.Url == "http://a/south.png");
        Assert.Equal("main", shown.Id);
        Assert.Equal(sheets.Max(s => s.Z), shown.Z);
        Assert.Equal(3, sheets.Select(s => s.Z).Distinct().Count());
        var north = sheets.Single(s => s.Url == "http://a/north.png");
        Assert.Equal((shown.Size.W * 2, shown.Size.H * 2), (north.Size.W, north.Size.H));
        Assert.Contains("'Front' has 3 states", Report("looks"));
    }

    [Fact]
    public void A_map_board_look_that_is_not_a_picture_is_left_out_and_a_single_sheet_keeps_the_board_name()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/sea.png", 20, 10, (_, _) => SKColors.Navy);
        var board = Obj("Custom_Board", "Sea", 1, Picture("http://a/sea.png"));
        board["States"] = new JsonObject { ["2"] = Obj("Custom_Model", "Ship", 1) };

        var sheets = Import("one-look", board).Sheets;

        Assert.Equal("Sea", Assert.Single(sheets).Name);
        Assert.Contains("state 2 of a map board (Custom_Model, not a picture)", Report("one-look"));
    }
}
