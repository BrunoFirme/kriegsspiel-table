using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static JsonObject ModuleLine(double thickness, bool loop, string colour, params (double x, double z)[] points) => new()
    {
        ["points3"] = new JsonArray(points.Select(p => (JsonNode)new JsonObject { ["x"] = p.x, ["y"] = 0.0, ["z"] = p.z }).ToArray()),
        ["color"] = new JsonObject { ["r"] = colour == "red" ? 1.0 : 0.0, ["g"] = 0.0, ["b"] = colour == "blue" ? 1.0 : 0.0 },
        ["thickness"] = thickness, ["loop"] = loop,
    };

    [Fact]
    public void Lines_drawn_on_pieces_and_on_the_table_come_over_and_the_rules_are_the_briefing()
    {
        var cfg0 = TempConfig();
        LocalPicture(cfg0, "http://a/tile.png", 8, 8, (_, _) => SKColors.White);
        var tile = Obj("Custom_Tile", "Range", 2, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/tile.png" },
            ["AttachedVectorLines"] = new JsonArray(ModuleLine(0.2, true, "red", (1, 0), (0, 1), (-1, 0))),
        });
        var module = Module("drawn", tile);
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["VectorLines"] = new JsonArray(ModuleLine(0.3, false, "blue", (10, 10), (20, 10)), ModuleLine(0.0, true, "red", (0, 0), (1, 0), (1, 1)));
        json["Rules"] = "  March orders are written before dawn.  ";
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "drawn", "scenario.json"));
        Assert.Equal("March orders are written before dawn.", table.Briefing);
        var strokes = table.Drawings["umpire"];
        Assert.Equal(2, strokes.Count);
        Assert.Equal((new Vec2(10, 10), new Vec2(20, 10), 0.3, "#0000FF"), (strokes[0].Points[0], strokes[0].Points[1], strokes[0].Width, strokes[0].Color));
        Assert.Equal((4, new Vec2(0, 0), 0.05), (strokes[1].Points.Count, strokes[1].Points[3], strokes[1].Width));
        var range = table.UmpireBoard.Single(p => p.Label == "Range");
        var line = Assert.Single(range.Lines);
        Assert.Equal((3, true, "#FF0000", 0.2), (line.Points.Count, line.Loop, line.Color, line.Width));
        Assert.Equal(-line.Points[0].X, line.Points[2].X, 6);
        Assert.Equal(line.Points[1].Z, -line.Points[0].X, 6);
        Assert.True(line.Points[0].X < 0 && line.Points[1].Z > 0, "a module's local +x lies along the piece's own -x here, as positionToWorld has it; z stays");
    }
}
