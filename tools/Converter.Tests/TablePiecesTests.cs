using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static readonly SKColor[] Cells = { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.Cyan, SKColors.Magenta, SKColors.White, SKColors.Black };

    void LocalPicture(Config cfg, string url, int w, int h, Func<int, int, SKColor> colour)
    {
        var images = Path.Combine(cfg.TtsModsPath, "Images");
        Directory.CreateDirectory(images);
        using var bmp = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) bmp.SetPixel(x, y, colour(x, y));
        using var png = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(images, AssetStore.Key(url) + ".png"), png.ToArray());
    }

    static JsonObject Card(string nick, int sheet, int cell, string face, string back, int across, int down, double rotZ = 0) =>
        Obj("CardCustom", nick, 1, new JsonObject
        {
            ["CardID"] = sheet * 100 + cell,
            ["CustomDeck"] = new JsonObject
            {
                [sheet.ToString()] = new JsonObject { ["FaceURL"] = face, ["BackURL"] = back, ["NumWidth"] = across, ["NumHeight"] = down, ["UniqueBack"] = false, ["Type"] = 0 },
            },
            ["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 1.0, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 180.0, ["rotZ"] = rotZ, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
        });

    static SKColor Middle(PackOut pack, string sha)
    {
        using var bmp = SKBitmap.Decode(Path.Combine(pack.Dir, pack.M.Assets[sha].File));
        return bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
    }

    [Fact]
    public void A_card_on_a_sheet_is_cut_from_its_own_cell_at_tts_card_size_with_its_back()
    {
        var cfg = TempConfig();
        const string Sheet = "http://a/sheet.png", Back = "http://a/back.png";
        LocalPicture(cfg, Sheet, 40, 20, (x, y) => Cells[y / 10 * 4 + x / 10]);
        LocalPicture(cfg, Back, 8, 8, (_, _) => SKColors.Gray);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var sixth = defs.DefFor(new TtsObj(Card("Hood", 3, 5, Sheet, Back, 4, 2)), pack)!.Def;
        var first = defs.DefFor(new TtsObj(Card("", 3, 0, Sheet, Back, 4, 2)), pack)!.Def;

        Assert.Equal((SKColors.Magenta, SKColors.Red), (Middle(pack, sixth.Image!), Middle(pack, first.Image!)));
        Assert.Equal(SKColors.Gray, Middle(pack, sixth.Back!));
        Assert.Equal((DefFactory.CardWidth, DefFactory.CardHeight, "rounded"), (sixth.Size.W, sixth.Size.H, sixth.Shape));
        Assert.Contains("card", sixth.Tags);
        Assert.Equal(("Hood", "Card"), (sixth.Name, first.Name));
        Assert.NotEqual(sixth.Id, first.Id);
    }

    [Fact]
    public void A_deck_lies_as_a_pile_with_the_card_tts_would_deal_on_top()
    {
        static TtsObj Deck(double rotZ, bool locked) => new(Obj("Deck", "", 1, new JsonObject
        {
            ["Locked"] = locked,
            ["Transform"] = new JsonObject { ["posX"] = 4.0, ["posY"] = 1.0, ["posZ"] = -2.0, ["rotX"] = 0.0, ["rotY"] = 90.0, ["rotZ"] = rotZ, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
            ["ContainedObjects"] = new JsonArray(Card("Top", 1, 0, "http://a/s.png", "http://a/b.png", 1, 1), Card("Middle", 1, 0, "http://a/s.png", "http://a/b.png", 1, 1), Card("Bottom", 1, 0, "http://a/s.png", "http://a/b.png", 1, 1)),
        }));

        var down = ScenarioBuilder.Pile(Deck(180, true)).ToList();
        var up = ScenarioBuilder.Pile(Deck(0, false)).ToList();

        Assert.Equal("Top", down.MaxBy(c => c.PosY)!.Nickname);
        Assert.Equal("Bottom", up.MaxBy(c => c.PosY)!.Nickname);
        Assert.All(down, c => Assert.Equal((true, true, 4.0, -2.0, 90.0), (c.Flipped, c.Locked, c.PosX, c.PosZ, c.RotY)));
        Assert.All(up, c => Assert.False(c.Flipped || c.Locked));
    }

    [Fact]
    public void A_picture_die_turns_up_the_side_tts_turns_up_for_each_value_on_the_standard_picture_die()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/die.png", 16, 16, (_, _) => SKColors.SkyBlue);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        static JsonObject Face(string value, double x, double z) => new() { ["Value"] = value, ["Rotation"] = new JsonObject { ["x"] = x, ["y"] = 0, ["z"] = z } };
        var die = Obj("Custom_Dice", "", 1.55, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/die.png", ["CustomDice"] = new JsonObject { ["Type"] = 1 } },
            ["RotationValues"] = new JsonArray(Face("1", -90, 0), Face("2", 0, 0), Face("3", 0, -90), Face("4", 0, 90), Face("5", 0, -180), Face("6", 90, 0)),
        });

        var def = defs.DefFor(new TtsObj(die), pack)!.Def;

        Assert.Equal("model", def.Kind);
        Assert.Contains("die", def.Tags);
        Assert.True(pack.M.Assets.ContainsKey(def.Mesh!));
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, def.States.Select(s => s.Name));
        Assert.Equal(new[] { (0, 0, 1), (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, -1, 0), (0, 0, -1) },
            def.States.Select(s => ((int)Math.Round(s.Up![0]), (int)Math.Round(s.Up[1]), (int)Math.Round(s.Up[2]))));
        Assert.Equal(Math.Round(DefFactory.DieAcross * 1.55, 4), def.Size.W);
        Assert.Null(DefFactory.NotTranslated(new TtsObj(die)));
        die["CustomImage"]!["CustomDice"]!["Type"] = 5;
        Assert.NotNull(DefFactory.NotTranslated(new TtsObj(die)));
    }

    [Fact]
    public void A_checker_is_a_round_counter_in_its_tts_colour()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var plain = Obj("Checker_white", "", 1);
        plain["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 };
        var green = Obj("Checker_white", "Objective", 0.65);
        green["ColorDiffuse"] = new JsonObject { ["r"] = 0.0, ["g"] = 0.5, ["b"] = 0.0 };

        var a = defs.DefFor(new TtsObj(plain), pack)!.Def;
        var b = defs.DefFor(new TtsObj(green), pack)!.Def;

        Assert.Equal(("White checker", "circle", "#EDE8DC", true), (a.Name, a.Shape, a.Tint, a.TintsPictures));
        Assert.Equal(("Objective", "#008000"), (b.Name, b.Tint));
        Assert.Equal(DefFactory.CheckerAcross, a.Size.W);
        Assert.Equal(Math.Round(DefFactory.CheckerAcross * 0.65, 4), b.Size.W);
    }

    [Fact]
    public void Importing_brings_the_cards_onto_what_they_lie_on_the_sky_as_backdrop_and_each_memory_bag_as_its_own_scenario()
    {
        var cfg0 = TempConfig();
        const string Sky = "http://a/sky.png", Map = "http://a/tray.png", Face = "http://a/face.png", Back = "http://a/back.png";
        LocalPicture(cfg0, Sky, 32, 16, (_, y) => y < 8 ? SKColors.SkyBlue : SKColors.SeaGreen);
        LocalPicture(cfg0, Map, 8, 8, (_, _) => SKColors.SaddleBrown);
        LocalPicture(cfg0, Face, 8, 12, (_, _) => SKColors.White);
        LocalPicture(cfg0, Back, 8, 12, (_, _) => SKColors.Navy);
        var tray = Obj("Custom_Tile", "Tray", 5, new JsonObject { ["Locked"] = true, ["CustomImage"] = new JsonObject { ["ImageURL"] = Map } });
        var onTray = Card("Bismarck", 1, 0, Face, Back, 1, 1);
        onTray["Transform"]!["posY"] = 1.3;
        onTray["Transform"]!["posX"] = 3.0;
        var faceDown = Card("Hidden", 1, 0, Face, Back, 1, 1, rotZ: 180);
        faceDown["Transform"]!["posX"] = 30.0;
        var placed = Obj("Checker_white", "Midway", 1);
        var spot = new JsonObject { ["lock"] = true, ["pos"] = new JsonObject { ["x"] = -40, ["y"] = 1, ["z"] = 12 }, ["rot"] = new JsonObject { ["x"] = 0, ["y"] = 90, ["z"] = 0 } };
        var bag = Obj("Bag", "Midway set-up", 1, new JsonObject
        {
            ["LuaScript"] = "memoryList = {}\nfunction buttonClick_place() end",
            ["LuaScriptState"] = new JsonObject
            {
                ["ml"] = new JsonObject
                {
                    [placed["GUID"]!.GetValue<string>()] = spot,
                    [tray["GUID"]!.GetValue<string>()] = new JsonObject { ["lock"] = false, ["pos"] = new JsonObject { ["x"] = 5, ["y"] = 1, ["z"] = 6 }, ["rot"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } },
                },
            }.ToJsonString(),
            ["ContainedObjects"] = new JsonArray(placed),
            ["Transform"] = new JsonObject { ["posX"] = 60.0, ["posY"] = 1.0, ["posZ"] = 60.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
        });
        var module = Module("fleet", tray, onTray, faceDown, bag);
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["SkyURL"] = Sky;
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "fleet", "scenario.json"));
        PieceInstance Named(Scenario s, string label) => s.UmpireBoard.Single(p => p.Label == label);
        Assert.True(Named(table, "Tray").Locked);
        Assert.Equal(0.2, Named(table, "Bismarck").Y);
        Assert.Equal((true, false), (Named(table, "Hidden").FaceDown, Named(table, "Bismarck").FaceDown));
        Assert.Matches(@"^pictures/[0-9a-f]{64}\.png$", table.Lighting.Backdrop);
        Assert.True(File.Exists(Path.Combine(Packages, "sheets", table.Lighting.Backdrop)));
        Assert.DoesNotContain(table.UmpireBoard, p => p.Label == "Midway");

        var layout = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "fleet-midway-set-up", "scenario.json"));
        var midway = Named(layout, "Midway");
        Assert.Equal((-40.0, 12.0, 90.0, true), (midway.Pos.X, midway.Pos.Z, midway.RotDeg, midway.Locked));
        var moved = Named(layout, "Tray");
        Assert.Equal((5.0, 6.0, false), (moved.Pos.X, moved.Pos.Z, moved.Locked));
        Assert.True(File.Exists(Path.Combine(Packages, "scenarios", "fleet-midway-set-up", "import-report.md")));
        Assert.Equal("fleet — Midway set-up", layout.Name);
        Assert.Contains("tts.fleet", layout.Packs);
    }

    [Fact]
    public void The_plan_names_a_scenario_and_an_extras_pack_for_each_memory_bag_that_lays_out_pieces_after_the_main_ones()
    {
        const string At = "{\"lock\":false,\"pos\":{\"x\":1,\"y\":2,\"z\":3},\"rot\":{\"x\":0,\"y\":0,\"z\":0}}";
        var bag = new TtsObj(Obj("Bag", "Midway scenario", 1, new JsonObject
        {
            ["LuaScript"] = "memoryList = {} function buttonClick_place() end",
            ["LuaScriptState"] = "{\"ml\":{\"abc123\":" + At + ",\"def456\":" + At + "}}",
        }));
        var empty = new TtsObj(Obj("Bag", "Spare", 1, new JsonObject { ["LuaScript"] = "memoryList = {} function buttonClick_place() end", ["LuaScriptState"] = "" }));
        var single = new TtsObj(Obj("Bag", "One ruler", 1, new JsonObject { ["LuaScript"] = "memoryList = {} function buttonClick_place() end", ["LuaScriptState"] = "{\"ml\":{\"aaa111\":" + At + "}}" }));
        var plan = ImportPlan.Of(new TtsSave { SaveName = "Naval", Objects = new() { bag, empty, single } }, null, null);

        Assert.Equal(new[] { "tts.naval", "tts.naval.extras", "tts.naval-midway-scenario.extras" }, plan.Packs);
        Assert.Equal(new[] { "naval", "naval-midway-scenario" }, plan.Scenarios);
        Assert.Equal("Naval — Midway scenario", plan.Layouts.Single().Name);
    }
}
