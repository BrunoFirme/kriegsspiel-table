using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class GluedFrameTests : ConverterHarness
{
    (Scenario Table, PackManifest Pack) Import(string name, params JsonObject[] objects)
    {
        var main = typeof(Config).Assembly.EntryPoint!;
        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module(name, objects), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;
        Assert.Equal(0, code);
        return (Json.Load<Scenario>(Path.Combine(Packages, "scenarios", name, "scenario.json")), Json.Load<PackManifest>(Path.Combine(Packages, "tts." + name, "pack.json")));
    }

    static JsonObject Placed(string type, string nick, double x, double y, double z, double sx = 1, double sy = 1, double sz = 1, JsonObject? extra = null)
    {
        var o = Obj(type, nick, 1, extra);
        var t = o["Transform"]!.AsObject();
        (t["posX"], t["posY"], t["posZ"], t["rotY"]) = (x, y, z, 0.0);
        (t["scaleX"], t["scaleY"], t["scaleZ"]) = (sx, sy, sz);
        return o;
    }

    static (PieceDef Def, PartDef Part) Only(Scenario table, PackManifest pack, string parent)
    {
        var def = pack.Pieces.Single(d => d.Id == table.UmpireBoard.Single(p => p.Label == parent).Def);
        var part = Assert.Single(def.Parts!);
        return (pack.Pieces.Single(d => d.Id == part.Def), part);
    }

    [Fact]
    public void A_part_on_a_long_block_sits_as_far_along_it_as_the_block_s_own_length_carries_it()
    {
        TempConfig();
        var flag = Placed("BlockSquare", "Flag", 0, 1, 0.5, 0.2, 0.2, 0.2);
        var bar = Placed("BlockRectangle", "Bar", 0, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(flag) });
        var (table, pack) = Import("along", bar);
        var (_, part) = Only(table, pack, "Bar");
        Assert.Equal(1.0, Math.Abs(part.Z), 3);
    }

    [Fact]
    public void A_glued_long_block_keeps_the_length_its_saved_scale_already_holds_and_is_not_stretched_again()
    {
        TempConfig();
        var bar = Placed("BlockRectangle", "Bar", 0, 1, 0, 1, 1, 2);
        var stand = Placed("BlockSquare", "Stand", 0, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(bar) });
        var (table, pack) = Import("held", stand);
        var (kind, _) = Only(table, pack, "Stand");
        Assert.Equal((1.0, 2.0), (kind.Size.W, kind.Size.H));
    }

    [Fact]
    public void A_glued_tile_keeps_its_saved_height_which_only_a_loose_tile_ignores()
    {
        TempConfig();
        var tile = Placed("Custom_Tile", "Tall", 0, 1, 0, 1, 3, 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/t.png", ["CustomTile"] = new JsonObject { ["Thickness"] = 0.2 } } });
        var loose = Placed("Custom_Tile", "Loose", 5, 1, 0, 1, 3, 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/t.png", ["CustomTile"] = new JsonObject { ["Thickness"] = 0.2 } } });
        var stand = Placed("BlockSquare", "Stand", 0, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(tile) });
        var (table, pack) = Import("height", stand, loose);
        var (kind, _) = Only(table, pack, "Stand");
        var lone = pack.Pieces.Single(d => d.Id == table.UmpireBoard.Single(p => p.Label == "Loose").Def);
        Assert.Equal((0.6, 0.2), (kind.Thickness, lone.Thickness));
    }

    [Fact]
    public void A_figure_from_a_bundle_saved_lower_than_wide_is_drawn_that_much_lower_glued_or_loose()
    {
        TempConfig();
        const string Soldier = "4444444444444444444444444444444444444444";
        Directory.CreateDirectory(Path.Combine(Packages, "user.figures"));
        Json.Save(Path.Combine(Packages, "user.figures", "pack.json"), new PackManifest
        {
            Id = "user.figures", Name = "Figures",
            Pieces = new() { new PieceDef { Id = "user.figures.soldier", Name = "Soldier", Kind = "model", Mesh = "m", Texture = "t", Size = new Size2(1, 1), Thickness = 2, HeightScale = 1, Tags = new() { "model", "bundle:" + Soldier } } },
        });
        JsonObject Figure(string nick, double x) => Placed("Custom_Assetbundle", nick, x, 1, 0, 0.18, 0.14, 0.18,
            new JsonObject { ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = $"https://example.org/ugc/1/{Soldier}/" } });
        var stand = Placed("BlockSquare", "Stand", 0, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(Figure("On the stand", 0)) });
        var (table, pack) = Import("lower", stand, Figure("Alone", 4));

        var (glued, _) = Only(table, pack, "Stand");
        var alone = pack.Pieces.Single(d => d.Id == table.UmpireBoard.Single(p => p.Label == "Alone").Def);
        foreach (var kind in new[] { glued, alone })
        {
            Assert.Equal(0.778, kind.HeightScale!.Value, 3);
            Assert.Equal(1.5556, kind.Thickness, 3);
        }
    }

    [Fact]
    public void A_glued_figure_stands_on_its_own_feet_wherever_its_bundle_puts_its_origin()
    {
        TempConfig();
        const string AtFeet = "5555555555555555555555555555555555555555", AtWaist = "6666666666666666666666666666666666666666";
        Directory.CreateDirectory(Path.Combine(Packages, "user.figures"));
        PieceDef Figure(string id, string sha, double origin) => new()
        {
            Id = "user.figures." + id, Name = id, Kind = "model", Mesh = "m-" + id, Texture = "t", Size = new Size2(1, 1), Thickness = 2, HeightScale = 1, ModuleOrigin = origin,
            Tags = new() { "model", "bundle:" + sha },
        };
        Json.Save(Path.Combine(Packages, "user.figures", "pack.json"), new PackManifest { Id = "user.figures", Name = "Figures", Pieces = new() { Figure("feet", AtFeet, 0), Figure("waist", AtWaist, 1) } });
        JsonObject Soldier(string sha) => Placed("Custom_Assetbundle", sha[..4], 0, 1, 0, extra: new JsonObject { ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = $"https://example.org/ugc/1/{sha}/" } });
        var a = Placed("BlockSquare", "A", 0, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(Soldier(AtFeet)) });
        var b = Placed("BlockSquare", "B", 5, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(Soldier(AtWaist)) });
        var (table, pack) = Import("feet", a, b);
        PartDef PartOf(string parent) => Assert.Single(pack.Pieces.Single(d => d.Id == table.UmpireBoard.Single(p => p.Label == parent).Def).Parts!);
        var (feet, waist) = (PartOf("A"), PartOf("B"));
        Assert.Equal(1.0, feet.Y - waist.Y, 3);
    }

    [Fact]
    public void A_bag_drawn_from_a_bundle_is_that_figure_holding_what_the_module_put_in_it()
    {
        TempConfig();
        const string Chest = "8888888888888888888888888888888888888888";
        Directory.CreateDirectory(Path.Combine(Packages, "user.figures"));
        Json.Save(Path.Combine(Packages, "user.figures", "pack.json"), new PackManifest
        {
            Id = "user.figures", Name = "Figures",
            Pieces = new() { new PieceDef { Id = "user.figures.chest", Name = "Chest", Kind = "model", Mesh = "m", Texture = "t", Size = new Size2(1, 1), Thickness = 1, HeightScale = 1, Tags = new() { "model", "bundle:" + Chest } } },
        });
        var chest = Placed("Custom_Assetbundle_Bag", "Supplies", 0, 1, 0, extra: new JsonObject
        {
            ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = $"https://example.org/ugc/1/{Chest}/", ["TypeIndex"] = 6 },
            ["ContainedObjects"] = new JsonArray(Placed("BlockSquare", "Ration", 0, 1, 0)),
        });
        var (table, _) = Import("chest", chest);
        var bag = Assert.Single(table.UmpireBoard);
        Assert.Equal("user.figures.chest", bag.Def);
        Assert.Equal("Ration", Assert.Single(bag.Stowed!).Label);
    }

    [Fact]
    public void A_glued_part_saved_with_no_position_and_no_size_stops_the_parts_after_it_as_the_game_stops_loading_them()
    {
        TempConfig();
        var broken = Placed("BlockSquare", "Broken", 0, 0, 0, 0, 0, 0);
        var stand = Placed("BlockSquare", "Stand", 0, 1, 0, extra: new JsonObject { ["ChildObjects"] = new JsonArray(Placed("BlockSquare", "First", 0, 1, 0, 0.5, 0.5, 0.5), broken, Placed("BlockSquare", "After", 0, 1, 0, 0.5, 0.5, 0.5)) });
        var (table, pack) = Import("garbage", stand);
        var def = pack.Pieces.Single(d => d.Id == Assert.Single(table.UmpireBoard).Def);
        Assert.Single(def.Parts!);
    }

    [Fact]
    public void A_stack_inside_a_bag_goes_into_the_bag_whole_none_of_its_copies_left_on_the_table()
    {
        TempConfig();
        FidelityTestsBoxes(Packages);
        var stack = Placed("Custom_Tile_Stack", "Supply", 0, 1, 0, extra: new JsonObject { ["Number"] = 3, ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/t.png", ["CustomTile"] = new JsonObject { ["Thickness"] = 0.2 } } });
        var sack = Placed("Bag", "Sack", 5, 1, 0, extra: new JsonObject { ["ContainedObjects"] = new JsonArray(stack) });
        var (table, _) = Import("stackbag", sack);
        var bag = Assert.Single(table.UmpireBoard);
        Assert.Equal(3, bag.Stowed!.Count);
    }

    static void FidelityTestsBoxes(string packages)
    {
        var pack = new PackManifest { Id = KTable.Client.Effects.EffectKinds.StandardPack, Name = "Firme's Effects and Props" };
        foreach (var (box, w, h, t) in new[] { (BagBoxes.Box, 1.0, 0.62, 0.48), (BagBoxes.Chest, 1.46, 0.96, 0.8), (BagBoxes.Crate, 1.91, 1.3, 1.04) })
            pack.Pieces.Add(new PieceDef { Id = BagBoxes.IdOf(box), Name = box, Kind = "model", Size = new Size2(w, h), Thickness = t, Shape = "box", Tags = new() { "prop" } });
        Json.Save(Path.Combine(packages, KTable.Client.Effects.EffectKinds.StandardPack, "pack.json"), pack);
    }

    [Fact]
    public void A_part_under_a_parent_stretched_in_depth_is_as_deep_as_the_stretch_makes_it()
    {
        TempConfig();
        var cap = Placed("BlockSquare", "Cap", 0, 1, 0, 0.5, 0.5, 0.5);
        var deep = Placed("BlockSquare", "Deep", 0, 1, 0, 1, 1, 3, new JsonObject { ["ChildObjects"] = new JsonArray(cap) });
        var (table, pack) = Import("deep", deep);
        var (kind, _) = Only(table, pack, "Deep");
        Assert.Equal((0.5, 1.5), (kind.Size.W, kind.Size.H));
    }
}
