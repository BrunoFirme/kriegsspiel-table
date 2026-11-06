using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    [Fact]
    public void A_triangle_block_comes_over_as_a_tinted_prism_alone_or_on_another_block()
    {
        TempConfig();
        var ramp = Obj("BlockTriangle", "Ramp", 1, new JsonObject { ["ColorDiffuse"] = new JsonObject { ["r"] = 0.5, ["g"] = 0.4, ["b"] = 0.2 } });
        var end = Obj("BlockTriangle", "End", 1, new JsonObject());
        var works = Obj("BlockRectangle", "Breastworks", 1, new JsonObject { ["ChildObjects"] = new JsonArray(end) });
        var module = Module("prism", ramp, works);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.prism", "pack.json"));
        var prisms = pack.Pieces.Where(d => d.Mesh != null && d.Name is "Ramp" or "End" or "Triangle block").ToList();
        Assert.NotEmpty(prisms);
        var prism = prisms[0];
        Assert.Equal(("model", 1.4142, 1.0, 0.707), (prism.Kind, prism.Size.W, prism.Size.H, prism.Thickness));
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "prism", "scenario.json"));
        var placed = table.UmpireBoard.Where(p => prisms.Any(d => d.Id == p.Def)).ToList();
        Assert.Equal(2, placed.Count);
        Assert.Contains(placed, p => p.Label == "Ramp" && string.Equals(p.TintOverride ?? pack.Pieces.First(d => d.Id == p.Def).Tint, "#806633", StringComparison.OrdinalIgnoreCase));
    }
}
