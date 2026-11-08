using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class LookTests : ConverterHarness
{
    (Scenario Table, PackManifest Pack) Import(string name, params JsonObject[] objects)
    {
        var main = typeof(Config).Assembly.EntryPoint!;
        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module(name, objects), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;
        Assert.Equal(0, code);
        return (Json.Load<Scenario>(Path.Combine(Packages, "scenarios", name, "scenario.json")), Json.Load<PackManifest>(Path.Combine(Packages, "tts." + name, "pack.json")));
    }

    static JsonObject Look(string nick, string guid, JsonObject? extra = null)
    {
        var o = Obj("BlockSquare", nick, 1, extra);
        o["GUID"] = guid;
        (o["Transform"]!["posX"], o["Transform"]!["posY"]) = (3.0, 1.0);
        return o;
    }

    static JsonObject Battalion(string guid, string columnLua, string column = "Column")
    {
        var line = Look("1st Bn in line", guid, new JsonObject { ["Tags"] = new JsonArray("line"), ["LuaScript"] = "function onLoad() self.setDescription('line') end" });
        var col = Look("1st Bn in column", guid + "c", new JsonObject
        {
            ["Tags"] = new JsonArray("column"), ["Locked"] = true, ["Description"] = column, ["Memo"] = "m2", ["LuaScriptState"] = "marching", ["LuaScript"] = columnLua,
        });
        var square = Look("1st Bn in square", guid + "s");
        line["States"] = new JsonObject { ["2"] = col, ["3"] = square };
        return line;
    }

    [Fact]
    public void Each_look_of_a_piece_comes_over_as_its_own_object_with_its_name_tags_lock_notes_memo_guid_and_script_state()
    {
        TempConfig();
        var (table, _) = Import("looks", Battalion("aaa111", "function onLoad() self.setDescription('column') end"));

        var p = Assert.Single(table.UmpireBoard);
        Assert.Equal(3, p.Looks!.Count);
        var col = p.Looks[1];
        Assert.Equal(("1st Bn in column", "aaa111c", true, "Column", "m2", "marching"), (col.Label, col.Guid, col.Locked, col.Note, col.Memo, col.ScriptState));
        Assert.Equal(["column"], col.Tags!);
        Assert.Equal(("1st Bn in line", "aaa111"), (p.Looks[0].Label, p.Looks[0].Guid));
        Assert.Equal("1st Bn in square", p.Looks[2].Label);
    }

    [Fact]
    public void Each_look_s_kind_carries_its_own_translated_script_and_a_look_without_one_carries_none()
    {
        TempConfig();
        var (table, pack) = Import("scripts", Battalion("aaa111", "function onLoad() self.setDescription('column') end"));

        var def = pack.Pieces.Single(d => d.Id == Assert.Single(table.UmpireBoard).Def);
        Assert.Contains("'line'", def.States[0].Script);
        Assert.Contains("'column'", def.States[1].Script);
        Assert.Contains("compat.", def.States[1].Script);
        Assert.Equal("", def.States[2].Script);
    }

    [Fact]
    public void Two_pieces_alike_in_their_first_look_but_not_in_a_later_one_are_two_kinds()
    {
        TempConfig();
        var a = Battalion("aaa111", "function onLoad() self.setDescription('column') end");
        var b = Battalion("bbb222", "function onLoad() self.setDescription('routed') end");
        (b["Transform"]!["posX"]) = 6.0;
        var (table, _) = Import("kinds", a, b);
        Assert.Equal(2, table.UmpireBoard.Select(p => p.Def).Distinct().Count());
    }

    [Fact]
    public void A_look_of_another_type_comes_over_as_that_type_a_model_as_a_model_and_a_block_as_a_plain_block()
    {
        TempConfig();
        var tile = Obj("Custom_Tile", "Flag", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/flag.png", ["CustomTile"] = new JsonObject { ["Thickness"] = 0.2 } } });
        var block = Obj("BlockSquare", "Block", 1);
        var model = Obj("Custom_Model", "Tower", 1, new JsonObject { ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/tower.obj", ["DiffuseURL"] = "http://a/tower.png" } });
        tile["States"] = new JsonObject { ["2"] = block, ["3"] = model };
        var (table, pack) = Import("types", tile);

        var def = pack.Pieces.Single(d => d.Id == Assert.Single(table.UmpireBoard).Def);
        Assert.Equal((string?)null, def.States[0].Kind);
        Assert.Equal("box", def.States[1].Shape ?? def.Shape);
        Assert.NotEqual(def.Image, def.States[1].Image);
        Assert.Equal(1.0, def.States[1].Thickness);
        Assert.Equal("model", def.States[2].Kind);
    }

    [Fact]
    public void A_figure_from_a_bundle_keeps_a_look_that_is_a_token_as_a_flat_look_of_its_own()
    {
        TempConfig();
        const string Soldier = "7777777777777777777777777777777777777777";
        Directory.CreateDirectory(Path.Combine(Packages, "user.figures"));
        Json.Save(Path.Combine(Packages, "user.figures", "pack.json"), new PackManifest
        {
            Id = "user.figures", Name = "Figures",
            Pieces = new() { new PieceDef { Id = "user.figures.soldier", Name = "Soldier", Kind = "model", Mesh = "m", Texture = "t", Size = new Size2(1, 1), Thickness = 2, HeightScale = 1, Tags = new() { "model", "bundle:" + Soldier } } },
        });
        var figure = Obj("Custom_Assetbundle", "Company", 1, new JsonObject { ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = $"https://example.org/ugc/1/{Soldier}/" } });
        var token = Obj("Custom_Token", "Company marker", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/marker.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2 } } });
        figure["States"] = new JsonObject { ["2"] = token };
        var (table, pack) = Import("figtoken", figure);

        var def = pack.Pieces.Single(d => d.Id == Assert.Single(table.UmpireBoard).Def);
        Assert.Equal(2, def.States.Count);
        Assert.Equal(("flat", "Company marker"), (def.States[1].Kind, def.States[1].Name));
        Assert.NotNull(def.States[1].Image);
    }

    [Fact]
    public void A_shared_piece_shown_in_a_later_look_keeps_the_glued_parts_of_the_look_it_shows()
    {
        TempConfig();
        var token = Obj("Custom_Token", "Flat", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/t.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2 } } });
        var die = Obj("Die_6", "Die", 1);
        die["ChildObjects"] = new JsonArray(Look("Flag", "fff111"));
        die["States"] = new JsonObject { ["1"] = token };
        var (table, pack) = Import("shownlook", die);

        var p = Assert.Single(table.UmpireBoard);
        var parts = (pack.Pieces.FirstOrDefault(d => d.Id == p.Def)?.AllParts ?? []).ToList();
        Assert.Single(parts);
    }
}
