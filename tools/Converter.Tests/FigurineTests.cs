using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    [Fact]
    public void A_standing_picture_comes_over_as_a_card_in_a_round_base_at_the_tabletop_s_size_with_its_picture()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/marshal.png", 40, 60, (_, y) => y < 30 ? SKColors.Navy : SKColors.White);
        var marshal = Obj(DefFactory.Figurine, "Oudinot", 0.5, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/marshal.png", ["ImageSecondaryURL"] = "" } });
        marshal["Transform"]!["scaleY"] = 0.5;
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module("stand", marshal), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.stand", "pack.json"));
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "stand", "scenario.json"));
        var oudinot = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == oudinot.Def);
        Assert.Equal("model", def.Kind);
        Assert.True(pack.Assets.ContainsKey(def.Mesh!) && pack.Assets.ContainsKey(def.Texture!), "its card and base, and the picture laid out for them");
        double k = oudinot.Scale;
        Assert.Equal(DefFactory.FigurineBase * 0.5, def.Size.W * k, 3);
        Assert.Equal(DefFactory.FigurineTop * 0.5, def.Thickness * k, 2);
    }
}
