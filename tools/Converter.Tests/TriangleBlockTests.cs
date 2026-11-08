using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    [Fact]
    public void A_triangle_block_comes_over_as_a_tinted_prism_alone_and_glued_to_a_long_block_is_stretched_along_it_by_that_block_s_own_length()
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
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "prism", "scenario.json"));
        var placed = table.UmpireBoard.Where(p => prisms.Any(d => d.Id == p.Def)).ToList();
        Assert.Single(placed);
        var loose = prisms.Single(d => d.Id == placed[0].Def);
        Assert.Equal(("model", 1.4142, 1.0, 0.707), (loose.Kind, loose.Size.W, loose.Size.H, loose.Thickness));
        var block = pack.Pieces.Single(d => d.Id == table.UmpireBoard.Single(p => p.Label == "Breastworks").Def);
        var glued = prisms.Single(d => block.Parts!.Any(p => p.Def == d.Id));
        Assert.Equal((1.4142, 2.0, 0.707), (glued.Size.W, glued.Size.H, glued.Thickness));
        Assert.Contains(placed, p => p.Label == "Ramp" && string.Equals(p.TintOverride ?? pack.Pieces.First(d => d.Id == p.Def).Tint, "#806633", StringComparison.OrdinalIgnoreCase));
    }
}
