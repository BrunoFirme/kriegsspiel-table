using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    [Fact]
    public void A_piece_overlapping_a_tray_s_edge_rests_on_the_tray_though_its_centre_is_outside()
    {
        var cfg0 = TempConfig();
        LocalPicture(cfg0, "http://a/tray.png", 8, 8, (_, _) => SKColors.SaddleBrown);
        LocalPicture(cfg0, "http://a/face.png", 8, 12, (_, _) => SKColors.White);
        var tray = Obj("Custom_Tile", "Tray", 5, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/tray.png" } });
        JsonObject At(string name, double x, double y)
        {
            var c = Card(name, 1, 0, "http://a/face.png", "http://a/face.png", 1, 1);
            c["Transform"]!["posX"] = x;
            c["Transform"]!["posY"] = y;
            return c;
        }
        var module = Module("edges", tray, At("Edge", 5.6, 1.3), At("Clear", 9.0, 1.3), At("Level", 2.0, 1.0));
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "edges", "scenario.json"));
        PieceInstance P(string label) => table.UmpireBoard.Single(p => p.Label == label);
        Assert.Equal((0.2, 1), (P("Edge").Y, P("Edge").Stack));
        Assert.Equal(0.2, P("Clear").Y);
        Assert.Equal((0.0, 0), (P("Level").Y, P("Level").Stack));
    }
}
