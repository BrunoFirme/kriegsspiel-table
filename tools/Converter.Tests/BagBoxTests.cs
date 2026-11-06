using System.Text.Json.Nodes;
using KTable.Client.Effects;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static void InstallBoxes(string packages)
    {
        var pack = new PackManifest { Id = EffectKinds.StandardPack, Name = "Firme's Effects and Props" };
        foreach (var (box, w, h, t) in new[] { (BagBoxes.Box, 1.0, 0.62, 0.48), (BagBoxes.Chest, 1.46, 0.96, 0.8), (BagBoxes.Crate, 1.91, 1.3, 1.04) })
            pack.Pieces.Add(new PieceDef { Id = BagBoxes.IdOf(box), Name = box, Kind = "model", Size = new Size2(w, h), Thickness = t, Shape = "box", Tags = new() { "prop" } });
        Json.Save(Path.Combine(packages, EffectKinds.StandardPack, "pack.json"), pack);
    }

    static JsonObject BagOf(string name, int pieces, (double r, double g, double b)? colour = null)
    {
        var held = new JsonArray();
        for (int i = 0; i < pieces; i++) held.Add(Obj("Checker_white", $"Piece {i}", 1));
        var bag = Obj("Bag", name, 1, new JsonObject { ["ContainedObjects"] = held });
        bag["ColorDiffuse"] = colour is { } c ? new JsonObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b } : new JsonObject { ["r"] = 0.7, ["g"] = 0.7, ["b"] = 0.7 };
        return bag;
    }

    [Theory]
    [InlineData("Supplies", 3, BagBoxes.Crate)]
    [InlineData("Midway airplanes", 80, BagBoxes.Crate)]
    [InlineData("Casualty markers", 200, BagBoxes.Box)]
    [InlineData("Reserve", 2, BagBoxes.Chest)]
    [InlineData("Rebs", 80, BagBoxes.Chest)]
    [InlineData("", 5, BagBoxes.Box)]
    [InlineData("Everything", BagBoxes.ManyPieces, BagBoxes.Crate)]
    public void A_bag_becomes_the_box_its_name_or_its_load_calls_for(string name, int pieces, string expected)
    {
        Assert.Equal(expected, BagBoxes.Pick(new ModuleObj(BagOf(name, pieces))));
    }

    [Fact]
    public void A_coloured_bag_tints_its_box_and_a_grey_or_white_one_leaves_the_wood_bare()
    {
        Assert.NotNull(BagBoxes.Tint(new ModuleObj(BagOf("Red", 1, (0.8, 0.1, 0.1)))));
        Assert.Null(BagBoxes.Tint(new ModuleObj(BagOf("Grey", 1, (0.7, 0.7, 0.7)))));
        Assert.Null(BagBoxes.Tint(new ModuleObj(BagOf("White", 1, (1, 1, 1)))));
    }

    [Fact]
    public void Bags_come_over_as_Firme_s_boxes_holding_their_pieces()
    {
        TempConfig();
        InstallBoxes(Packages);
        var supplies = BagOf("Supplies", 2, (0.8, 0.1, 0.1));
        supplies["Transform"]!["posX"] = 10.0;
        var rebs = BagOf("Rebs", 40);
        rebs["Transform"]!["posX"] = -10.0;
        var module = Module("stores", supplies, rebs);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "stores", "scenario.json"));
        var crate = table.UmpireBoard.Single(p => p.Label == "Supplies");
        var chest = table.UmpireBoard.Single(p => p.Label == "Rebs");
        Assert.Equal((BagBoxes.IdOf(BagBoxes.Crate), 2), (crate.Def, crate.Stowed!.Count));
        Assert.Equal((BagBoxes.IdOf(BagBoxes.Chest), 40), (chest.Def, chest.Stowed!.Count));
        Assert.NotNull(crate.TintOverride);
        Assert.Null(chest.TintOverride);
        Assert.Contains(EffectKinds.StandardPack, table.Packs);
        var own = table.Packs.Where(p => p.StartsWith("tts.stores")).SelectMany(p => Json.Load<PackManifest>(Path.Combine(Packages, p, "pack.json")).Pieces);
        Assert.DoesNotContain(own, d => d.Tags.Contains("bag"));
    }
}
