using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class GluedPartsTests : ConverterHarness
{
    static JsonObject Block(string nick, double x, double y)
    {
        var b = Obj("BlockSquare", nick, 1);
        (b["Transform"]!["posX"], b["Transform"]!["posY"], b["Transform"]!["rotY"]) = (x, y, 0.0);
        return b;
    }

    (Scenario Table, PackManifest Pack) Import(string name, params JsonObject[] objects)
    {
        var main = typeof(Config).Assembly.EntryPoint!;
        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module(name, objects), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;
        Assert.Equal(0, code);
        return (Json.Load<Scenario>(Path.Combine(Packages, "scenarios", name, "scenario.json")), Json.Load<PackManifest>(Path.Combine(Packages, "tts." + name, "pack.json")));
    }

    [Fact]
    public void Each_state_of_an_object_carries_its_own_glued_blocks_as_parts_of_one_piece()
    {
        TempConfig();
        var second = Block("Routed", 0, 1);
        var banner = Block("Banner", 0.5, 1);
        banner["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0 };
        second["ChildObjects"] = new JsonArray(banner);
        var unit = Block("Formed", 0, 1);
        unit["ChildObjects"] = new JsonArray(Block("Flag", -0.5, 1));
        unit["States"] = new JsonObject { ["2"] = second };

        var (table, pack) = Import("states", unit);

        var piece = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == piece.Def);
        string Named(PartDef p) => pack.Pieces.Single(d => d.Id == p.Def).Name;
        Assert.Null(def.Parts);
        Assert.Equal(new[] { "Flag" }, def.States[0].Parts!.Select(Named));
        Assert.Equal(new[] { "Banner" }, def.States[1].Parts!.Select(Named));
        Assert.Equal((0.5, -0.5), (def.States[0].Parts![0].X, def.States[1].Parts![0].X));
    }

    [Fact]
    public void A_glued_scripted_block_with_its_own_glued_block_is_one_piece_whose_part_runs_no_script()
    {
        TempConfig();
        var inner = Block("Pennant", 0, 1);
        var child = Block("Colours", 0, 1);
        child["LuaScript"] = "function onLoad() self.setName('hi') end";
        child["ChildObjects"] = new JsonArray(inner);
        var unit = Block("Battalion", 0, 1);
        unit["ChildObjects"] = new JsonArray(child);

        var (table, pack) = Import("scripted", unit);

        var piece = Assert.Single(table.UmpireBoard);
        var part = Assert.Single(pack.Pieces.Single(d => d.Id == piece.Def).Parts!);
        var kind = pack.Pieces.Single(d => d.Id == part.Def);
        Assert.Null(kind.Script);
        Assert.Equal("Pennant", pack.Pieces.Single(d => d.Id == Assert.Single(kind.Parts!).Def).Name);
        Assert.Contains("does not run on a glued part", File.ReadAllText(Path.Combine(Packages, "scenarios", "scripted", "import-report.md")));
    }

    [Fact]
    public void A_block_glued_to_a_mirrored_block_is_mirrored_with_it_in_place_turn_and_look()
    {
        TempConfig();
        JsonObject Unit(double sx)
        {
            var flag = Block("Flag", 0.5, 1);
            flag["Transform"]!["rotY"] = 30.0;
            flag["Transform"]!["scaleX"] = 0.5;
            var unit = Block("Battalion", 0, 1);
            unit["Transform"]!["scaleX"] = sx;
            unit["ChildObjects"] = new JsonArray(flag);
            return unit;
        }
        PartDef PartOf(string name, double sx)
        {
            var (table, pack) = Import(name, Unit(sx));
            return Assert.Single(pack.Pieces.Single(d => d.Id == Assert.Single(table.UmpireBoard).Def).Parts!);
        }

        var plain = PartOf("plain", 1);
        var mirrored = PartOf("mirrored", -1);

        Assert.NotEqual(0, plain.Rot);
        Assert.Equal((-plain.X, -plain.Rot), (mirrored.X, mirrored.Rot));
        Assert.NotEqual(plain.Def[plain.Def.IndexOf('.')..], mirrored.Def[mirrored.Def.IndexOf('.')..]);
    }

    [Fact]
    public void Each_baked_look_keeps_its_own_stack_height()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/a.png", 8, 8, (_, _) => SKColors.Red);
        LocalPicture(cfg, "http://a/b.png", 8, 8, (_, _) => SKColors.Blue);
        JsonObject Token(string nick, string url, double y) => Obj("Custom_Token", nick, 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = url, ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2 } },
            ["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = y, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
        });
        var tall = Token("Tall", "http://a/a.png", 1);
        tall["ChildObjects"] = new JsonArray(Token("Top", "http://a/b.png", 0.4));
        var flat = Token("Flat", "http://a/a.png", 1);
        flat["ChildObjects"] = new JsonArray(Token("Top", "http://a/b.png", 0));
        tall["States"] = new JsonObject { ["2"] = flat };

        var (table, pack) = Import("heights", tall);

        var def = pack.Pieces.Single(d => d.Id == Assert.Single(table.UmpireBoard).Def);
        Assert.Equal(0.6, def.Thickness, 3);
        Assert.Null(def.States[0].Thickness);
        Assert.Equal(0.2, def.States[1].Thickness!.Value, 3);
    }
}
