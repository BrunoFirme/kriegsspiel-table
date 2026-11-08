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
        var parts = pack.Pieces.Single(d => d.Id == piece.Def).Parts!;
        var kinds = parts.Select(p => pack.Pieces.Single(d => d.Id == p.Def)).ToList();
        Assert.Equal(2, kinds.Count);
        Assert.All(kinds, k => Assert.Null(k.Script));
        Assert.All(kinds, k => Assert.Null(k.Parts));
        Assert.Contains("does not run on a glued part", File.ReadAllText(Path.Combine(Packages, "scenarios", "scripted", "import-report.md")));
    }

    [Fact]
    public void Blocks_glued_in_a_chain_all_come_over_as_parts_of_the_first_each_placed_through_the_turns_above_it()
    {
        TempConfig();
        var last = Block("Rear rank", 1, 1);
        var middle = Block("Front rank", 1, 1);
        middle["Transform"]!["rotY"] = 90.0;
        middle["ChildObjects"] = new JsonArray(last);
        var unit = Block("Battalion", 0, 1);
        unit["ChildObjects"] = new JsonArray(middle);

        var (table, pack) = Import("chain", unit);

        var piece = Assert.Single(table.UmpireBoard);
        var parts = pack.Pieces.Single(d => d.Id == piece.Def).Parts!;
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Null(pack.Pieces.Single(d => d.Id == p.Def).Parts));
        Assert.Equal((-1.0, 0.0, 90.0), (parts[0].X, parts[0].Z, parts[0].Rot));
        Assert.Equal((-1.0, -1.0, 90.0), (parts[1].X, parts[1].Z, parts[1].Rot));
    }

    [Fact]
    public void A_figure_from_an_asset_bundle_keeps_every_state_with_its_own_figure_and_the_figures_glued_to_that_state()
    {
        TempConfig();
        const string Line = "1111111111111111111111111111111111111111", Column = "2222222222222222222222222222222222222222", Colonel = "3333333333333333333333333333333333333333";
        Directory.CreateDirectory(Path.Combine(Packages, "user.figures"));
        PieceDef Figure(string id, string sha, double w) => new() { Id = "user.figures." + id, Name = id, Kind = "model", Mesh = "m-" + id, Texture = "t-" + id, Size = new Size2(w, w), Thickness = 1, Tags = new() { "model", "bundle:" + sha } };
        Json.Save(Path.Combine(Packages, "user.figures", "pack.json"), new PackManifest
        {
            Id = "user.figures", Name = "Figures", Pieces = new() { Figure("line", Line, 4), Figure("column", Column, 2), Figure("colonel", Colonel, 0.5) },
        });
        JsonObject Bundle(string nick, string sha, double x)
        {
            var o = Obj("Custom_Assetbundle", nick, 1, new JsonObject { ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = $"https://example.org/ugc/1/{sha}/" } });
            (o["Transform"]!["posX"], o["Transform"]!["rotY"]) = (x, 0.0);
            return o;
        }
        var column = Bundle("In column", Column, 0);
        column["ChildObjects"] = new JsonArray(Bundle("Colonel", Colonel, 0.5));
        var regiment = Bundle("In line", Line, 0);
        regiment["ChildObjects"] = new JsonArray(Bundle("Colonel", Colonel, -1));
        regiment["States"] = new JsonObject { ["2"] = column };

        var (table, pack) = Import("regiment", regiment);

        var piece = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == piece.Def);
        Assert.Equal(new[] { "In line", "In column" }, def.States.Select(s => s.Name));
        Assert.Equal(new[] { "m-line", "m-column" }, def.States.Select(s => s.Mesh));
        Assert.Equal(0, piece.State);
        Assert.Equal(new[] { 1.0, -0.5 }, def.States.Select(s => Assert.Single(s.Parts!).X));
        Assert.All(def.States, s => Assert.Equal("user.figures.colonel", Assert.Single(s.Parts!).Def));
    }

    [Fact]
    public void A_stock_piece_carrying_glued_blocks_keeps_them_as_parts_of_its_own_copy_and_the_shared_kind_stays_bare()
    {
        TempConfig();
        var die = Obj("Die_6", "Command die", 1);
        die["ChildObjects"] = new JsonArray(Block("Pennant", 0.5, 1));
        var plain = Obj("Die_6", "Plain die", 1);
        (plain["Transform"]!["posX"]) = 5.0;

        var (table, pack) = Import("carried", die, plain);

        var carrying = table.UmpireBoard.Single(p => p.Label == "Command die");
        var kind = pack.Pieces.Single(d => d.Id == carrying.Def);
        Assert.Equal("Pennant", pack.Pieces.Single(d => d.Id == Assert.Single(kind.Parts!).Def).Name);
        Assert.NotEqual(carrying.Def, table.UmpireBoard.Single(p => p.Label == "Plain die").Def);
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
    public void Each_look_carries_its_glued_tokens_as_parts_at_their_own_heights_with_the_module_data_of_that_look()
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
        var (high, level) = (Assert.Single(def.States[0].Parts!), Assert.Single(def.States[1].Parts!));
        Assert.True(high.Y - level.Y > 0.3, $"the raised token sits higher: {high.Y} against {level.Y}");
        Assert.NotNull(def.Outline);
        Assert.Equal(new[] { tall["GUID"]!.GetValue<string>(), flat["GUID"]!.GetValue<string>() }, def.States.Select(st => st.Module!.Guid));
    }

    [Fact]
    public void A_decal_on_a_token_keeps_the_token_s_own_size_whatever_is_glued_beside_it()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/block.png", 16, 4, (_, _) => SKColors.Red);
        LocalPicture(cfg, "http://a/strip.png", 16, 4, (_, _) => SKColors.White);
        LocalPicture(cfg, "http://a/frame.png", 8, 8, (_, _) => SKColors.Black);
        JsonObject Token(string nick, string url, double x, double z) => Obj("Custom_Token", nick, 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = url, ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2 } },
            ["Transform"] = new JsonObject { ["posX"] = x, ["posY"] = 1.0, ["posZ"] = z, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
            ["AttachedDecals"] = new JsonArray(new JsonObject
            {
                ["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 0.2, ["posZ"] = 0.0, ["rotX"] = 90.0, ["rotY"] = 180.0, ["rotZ"] = 0.0, ["scaleX"] = 2.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
                ["CustomDecal"] = new JsonObject { ["Name"] = "Frame", ["ImageURL"] = "http://a/frame.png", ["Size"] = 1.0 },
            }),
        });
        var alone = Token("Alone", "http://a/block.png", 0, 0);
        var flanked = Token("Flanked", "http://a/block.png", 10, 0);
        var strip = Token("", "http://a/strip.png", 0, 3);
        strip.Remove("AttachedDecals");
        flanked["ChildObjects"] = new JsonArray(strip);

        var (table, pack) = Import("frames", alone, flanked);

        DecalDef Frame(string label) => Assert.Single(table.UmpireBoard.Single(p => p.Label == label).Decals!);
        Assert.Equal((Frame("Alone").W, Frame("Alone").H), (Frame("Flanked").W, Frame("Flanked").H));
    }

    [Fact]
    public void A_token_shows_its_own_picture_underneath_and_never_a_second_picture_the_save_names()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/front.png", 8, 8, (_, _) => SKColors.Red);
        LocalPicture(cfg, "http://a/back.png", 8, 8, (_, _) => SKColors.Blue);
        var token = Obj("Custom_Token", "Marker", 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/front.png", ["ImageSecondaryURL"] = "http://a/back.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2 } },
        });
        var tile = Obj("Custom_Tile", "Card", 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/front.png", ["ImageSecondaryURL"] = "http://a/back.png" },
        });
        tile["Transform"]!["posX"] = 5.0;

        var (table, pack) = Import("backs", token, tile);

        PieceDef Of(string label) => pack.Pieces.Single(d => d.Id == table.UmpireBoard.Single(p => p.Label == label).Def);
        Assert.Null(Of("Marker").Back);
        Assert.NotNull(Of("Card").Back);
    }
}
