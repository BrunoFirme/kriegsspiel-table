using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static readonly SKColor[] Cells = { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.Cyan, SKColors.Magenta, SKColors.White, SKColors.Black };

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
    public void A_card_on_a_sheet_is_cut_from_its_own_cell_at_module_card_size_with_its_back()
    {
        var cfg = TempConfig();
        const string Sheet = "http://a/sheet.png", Back = "http://a/back.png";
        LocalPicture(cfg, Sheet, 40, 20, (x, y) => Cells[y / 10 * 4 + x / 10]);
        LocalPicture(cfg, Back, 8, 8, (_, _) => SKColors.Gray);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var sixth = defs.DefFor(new ModuleObj(Card("Hood", 3, 5, Sheet, Back, 4, 2)), pack)!.Def;
        var first = defs.DefFor(new ModuleObj(Card("", 3, 0, Sheet, Back, 4, 2)), pack)!.Def;

        Assert.Equal((SKColors.Magenta, SKColors.Red), (Middle(pack, sixth.Image!), Middle(pack, first.Image!)));
        Assert.Equal(SKColors.Gray, Middle(pack, sixth.Back!));
        Assert.Equal((3.0625, DefFactory.CardHeight, "rounded"), (sixth.Size.W, sixth.Size.H, sixth.Shape));
        Assert.Contains("card", sixth.Tags);
        Assert.Equal(("Hood", "Card"), (sixth.Name, first.Name));
        Assert.NotEqual(sixth.Id, first.Id);
    }

    [Fact]
    public void A_deck_lies_as_a_pile_with_the_card_a_module_would_deal_on_top()
    {
        static ModuleObj Deck(double rotZ, bool locked) => new(Obj("Deck", "", 1, new JsonObject
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

    const string UnitCube = "v -0.5 -0.5 -0.5\nv 0.5 -0.5 -0.5\nv 0.5 0.5 -0.5\nv -0.5 0.5 -0.5\nv -0.5 -0.5 0.5\nv 0.5 -0.5 0.5\nv 0.5 0.5 0.5\nv -0.5 0.5 0.5\n"
                            + "f 1 2 3\nf 1 3 4\nf 5 7 6\nf 5 8 7\nf 1 5 6\nf 1 6 2\nf 4 3 7\nf 4 7 8\nf 1 4 8\nf 1 8 5\nf 2 6 7\nf 2 7 3\n";

    [Fact]
    public void A_model_made_a_die_rolls_on_its_saved_values_and_shows_the_one_facing_up_and_a_plain_model_does_not()
    {
        var cfg = TempConfig();
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, AssetStore.Key("http://a/die.obj") + ".obj"), UnitCube);
        static JsonObject Value(string v, double x) => new() { ["Value"] = v, ["Rotation"] = new JsonObject { ["x"] = x, ["y"] = 0.0, ["z"] = 0.0 } };
        JsonObject Model(int type)
        {
            var o = Obj("Custom_Model", "", 1, new JsonObject
            {
                ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/die.obj", ["TypeIndex"] = type },
                ["RotationValues"] = new JsonArray(Value("1", 0), Value("2", 90), Value("6", 180), Value("6", 0.5)),
            });
            o["Transform"]!["rotX"] = 180.0;
            return o;
        }
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var die = defs.DefFor(new ModuleObj(Model(2)), pack)!;
        var plain = defs.DefFor(new ModuleObj(Model(0)), pack)!;

        Assert.Equal(new[] { "1", "2", "6" }, die.Def.Faces.Select(f => f.Value));
        Assert.Equal(2, die.State);
        Assert.Contains("die", die.Def.Tags);
        Assert.Empty(plain.Def.Faces);
    }

    [Fact]
    public void A_model_keeps_its_normal_map_and_whether_it_casts_a_shadow()
    {
        var cfg = TempConfig();
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, AssetStore.Key("http://a/hill.obj") + ".obj"), UnitCube);
        LocalPicture(cfg, "http://a/bumps.png", 8, 8, (_, _) => new SKColor(128, 128, 255));
        JsonObject Model(bool shadows) => Obj("Custom_Model", "", 1, new JsonObject
        {
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/hill.obj", ["NormalURL"] = "http://a/bumps.png", ["CastShadows"] = shadows },
        });
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var flat = defs.DefFor(new ModuleObj(Model(false)), pack)!.Def;

        Assert.NotNull(flat.Normal);
        Assert.True(pack.M.Assets.ContainsKey(flat.Normal!));
        Assert.True(flat.Shadowless);
        Assert.False(defs.DefFor(new ModuleObj(Model(true)), pack)!.Def.Shadowless);
    }

    [Fact]
    public void A_picture_die_turns_up_the_side_a_module_turns_up_for_each_value_on_the_standard_picture_die()
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

        var def = defs.DefFor(new ModuleObj(die), pack)!.Def;

        Assert.Equal("model", def.Kind);
        Assert.Contains("die", def.Tags);
        Assert.True(pack.M.Assets.ContainsKey(def.Mesh!));
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, def.Faces.Select(s => s.Value));
        Assert.Equal(new[] { (0, 0, 1), (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, -1, 0), (0, 0, -1) },
            def.Faces.Select(s => ((int)Math.Round(s.Up[0]), (int)Math.Round(s.Up[1]), (int)Math.Round(s.Up[2]))));
        Assert.Equal(Math.Round(DefFactory.DieAcross * 1.55, 4), def.Size.W);
        Assert.Null(DefFactory.NotTranslated(new ModuleObj(die)));
        die["CustomImage"]!["CustomDice"]!["Type"] = 5;
        Assert.Null(DefFactory.NotTranslated(new ModuleObj(die)));
        Assert.Equal(((int?)null, 20), (new ModuleObj(die).StockSides, new ModuleObj(die).DieSides));
        die["CustomImage"]!["CustomDice"]!["Type"] = 9;
        Assert.NotNull(DefFactory.NotTranslated(new ModuleObj(die)));
    }

    [Fact]
    public void A_card_whose_server_answers_with_a_web_page_is_left_out_and_one_that_only_failed_to_answer_keeps_a_placeholder()
    {
        var cfg = TempConfig();
        const string Wall = "http://a/wall.jpg", Busy = "http://a/busy.jpg", Back = "http://a/back.jpg";
        Directory.CreateDirectory(cfg.Cache);
        File.WriteAllText(Path.Combine(cfg.Cache, "index.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, CacheEntry>
        {
            [AssetStore.Key(Wall)] = new() { Status = "missing", Error = "HTTP 503: " + AssetStore.WebPage, Url = Wall },
            [AssetStore.Key(Busy)] = new() { Status = "missing", Error = "HTTP 503", Url = Busy },
        }, Config.Opts));
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        Assert.Null(defs.DefFor(new ModuleObj(Card("BB Bismarck", 1, 0, Wall, Back, 1, 1)), pack));
        Assert.NotNull(defs.DefFor(new ModuleObj(Card("CA Exeter", 2, 0, Busy, Back, 1, 1)), pack));
        Assert.Equal(1, rep.PiecesLeftOut);
        var path = Path.Combine(_root, "report.md");
        rep.Write(path, "import t");
        Assert.Contains("- BB Bismarck — `http://a/wall.jpg` — HTTP 503: " + AssetStore.WebPage, File.ReadAllText(path));
    }

    [Fact]
    public void Effects_kept_only_in_bags_still_bring_firmes_effects_to_the_scenario()
    {
        TempConfig();
        Json.Save(Path.Combine(Packages, KTable.Client.Effects.EffectKinds.StandardPack, "pack.json"), new PackManifest
        {
            Id = KTable.Client.Effects.EffectKinds.StandardPack, Name = "Firme's Effects",
            Pieces = { new PieceDef { Id = KTable.Client.Effects.EffectKinds.StandardPack + ".powder-smoke", Name = "Powder smoke", Size = new Size2(2, 2), Effect = new EffectDef { Kind = "smoke" }, Tags = new() { "effect" } } },
        });
        var smoke = Obj("Custom_Assetbundle", "Smoke", 1, new JsonObject { ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = "https://example.org/ugc/1/B89A13D24198F5D661BB3327FB2A516BD4B00EE8/" } });
        var bag = Obj("Infinite_Bag", "Smoke", 1, new JsonObject { ["ContainedObjects"] = new JsonArray(smoke) });
        var module = Module("bagged", Obj("BlockSquare", "Ship", 1), bag);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "bagged", "scenario.json"));
        Assert.Contains(KTable.Client.Effects.EffectKinds.StandardPack, table.Packs);
        Assert.DoesNotContain(table.UmpireBoard, p => p.Label == "Smoke");
    }

    [Fact]
    public void Notecards_the_notes_panel_and_notebook_tabs_become_note_pieces_carrying_their_text()
    {
        var cfg0 = TempConfig();
        LocalPicture(cfg0, "http://a/map.png", 20, 10, (_, _) => SKColors.Navy);
        var board = Obj("Custom_Board", "Sea", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/map.png" } });
        board["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 1.0, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 };
        var card = Obj("Notecard", "Orders", 1);
        card["Description"] = "Hold the ford\nuntil dusk";
        card["Transform"]!["rotY"] = 10.0;
        var module = Module("notes", board, card);
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["Note"] = "\n\n1  Turn\nInitiative\nNaval Movement";
        json["TabStates"] = new JsonObject
        {
            ["0"] = new JsonObject { ["title"] = "Rules", ["body"] = "Spotting at 12 inches", ["id"] = 0 },
            ["1"] = new JsonObject { ["title"] = "Red", ["body"] = "", ["id"] = 1 },
        };
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "notes", "scenario.json"));
        var defs = table.Packs.Select(p => Path.Combine(Packages, p, "pack.json")).Where(File.Exists).SelectMany(p => Json.Load<PackManifest>(p).Pieces).ToDictionary(d => d.Id);
        var notes = table.UmpireBoard.Where(p => NoteCard.Is(defs[p.Def])).Select(p => (p, d: defs[p.Def])).ToList();
        string Line(PieceDef d, string name) => d.TextLines.Single(t => t.Name == name).Default;
        var orders = notes.Single(n => Line(n.d, NoteCard.TitleLine) == "Orders");
        Assert.Equal("Hold the ford\nuntil dusk", Line(orders.d, NoteCard.BodyLine));
        Assert.Equal(190.0, orders.p.RotDeg);
        var panel = notes.Single(n => Line(n.d, NoteCard.TitleLine) == "Notes");
        Assert.Equal("1  Turn\nInitiative\nNaval Movement", Line(panel.d, NoteCard.BodyLine));
        double mapRight = board["Transform"]!["scaleX"]!.GetValue<double>() * 16.8345 / 2;
        Assert.True(panel.p.Pos.X > mapRight, $"the notes lie right of the map: {panel.p.Pos.X}");
        Assert.Equal(180.0, panel.p.RotDeg);
        Assert.Single(notes, n => Line(n.d, NoteCard.TitleLine) == "Rules");
        Assert.DoesNotContain(notes, n => Line(n.d, NoteCard.TitleLine) == "Red");
    }

    [Fact]
    public void A_board_is_as_tall_as_its_base_and_as_wide_as_its_width_setting_says_grown_as_its_table_grows_it()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/sea.png", 28, 20, (_, _) => SKColors.Navy);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        ModuleObj Board(double widthScale) => new(Obj("Custom_Board", "Sea", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/sea.png", ["WidthScale"] = widthScale } }));
        double b = cfg.BaseSizes.CustomBoard;

        Assert.Equal((Math.Round(b * 1.4, 4), b), (defs.BaseSize(Board(1.4)).W, defs.BaseSize(Board(1.4)).H));
        Assert.Equal((Math.Round(b * 1.4, 4), b), (defs.BaseSize(Board(0)).W, defs.BaseSize(Board(0)).H));
        foreach (var (table, growth) in new[] { ("Table_Custom", 1.6), ("Table_RPG", 1.4), ("Table_Circular", 1.2), ("Table_None", 1.0) })
        {
            defs.Table = table;
            Assert.Equal(Math.Round(b * growth, 4), defs.BaseSize(Board(1.4)).H);
        }
    }

    [Fact]
    public void A_note_keeps_its_words_and_drops_module_formatting_codes()
    {
        Assert.Equal("Flex Table\nClick the red button", DefFactory.Plain("[b]Flex Table[/b]\nClick the [FF0000]red[-] button"));
        Assert.Equal("x[2] and [note]", DefFactory.Plain("x[sup][2][/sup] and [note]"));
    }

    static async Task ServePages(System.Net.Sockets.TcpListener server, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var client = await server.AcceptTcpClientAsync(stop);
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer, stop);
            var request = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
            var status = request.Contains("/busy") ? "429 Too Many Requests" : request.Contains("/forbidden") ? "403 Forbidden" : "503 Service Unavailable";
            const string Page = "<!DOCTYPE html><html><body>Just a moment...</body></html>";
            var answer = $"HTTP/1.1 {status}\r\nContent-Type: text/html\r\nContent-Length: {Page.Length}\r\nConnection: close\r\n\r\n{Page}";
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(answer), stop);
        }
    }

    [Fact]
    public async Task A_web_page_instead_of_a_picture_counts_as_broken_only_when_the_server_failed_the_same_way_on_an_earlier_import()
    {
        var cfg = TempConfig();
        var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        using var stop = new CancellationTokenSource();
        var serving = ServePages(server, stop.Token);
        int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;
        string wall = $"http://127.0.0.1:{port}/card.png", busy = $"http://127.0.0.1:{port}/busy.png", forbidden = $"http://127.0.0.1:{port}/forbidden.png";
        try
        {
            var first = new AssetStore(cfg, new Report());
            await first.FetchAll(new[] { wall, busy, forbidden });
            Assert.Equal((null, null, null), (first.Gone(wall, "image"), first.Gone(busy, "image"), first.Gone(forbidden, "image")));
            var second = new AssetStore(cfg, new Report());
            await second.FetchAll(new[] { wall, busy, forbidden });
            Assert.EndsWith(AssetStore.WebPage, second.Gone(wall, "image"));
            Assert.EndsWith(AssetStore.WebPage, second.Gone(forbidden, "image"));
            Assert.Null(second.Gone(busy, "image"));
        }
        finally
        {
            stop.Cancel();
            server.Stop();
            await Task.WhenAny(serving, Task.Delay(1000));
        }
    }

    [Fact]
    public void A_piece_beside_a_tray_at_the_tray_s_level_lies_at_that_level_and_one_on_the_table_stays_down()
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
        var module = Module("levels", tray, At("On", 2.0, 1.3), At("Beside", 12.0, 1.3), At("Down", 30.0, 1.0));
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "levels", "scenario.json"));
        double Y(string label) => table.UmpireBoard.Single(p => p.Label == label).Y;
        Assert.Equal((0.2, 0.2, 0.0), (Y("On"), Y("Beside"), Y("Down")));
    }

    [Fact]
    public void A_checker_is_a_round_counter_in_its_module_colour()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var plain = Obj("Checker_white", "", 1);
        plain["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 };
        var green = Obj("Checker_white", "Objective", 0.65);
        green["ColorDiffuse"] = new JsonObject { ["r"] = 0.0, ["g"] = 0.5, ["b"] = 0.0 };

        var a = defs.DefFor(new ModuleObj(plain), pack)!.Def;
        var b = defs.DefFor(new ModuleObj(green), pack)!.Def;

        Assert.Equal(("White checker", "circle", "#FFFFFF", true), (a.Name, a.Shape, a.Tint, a.TintsPictures));
        Assert.Equal(("Objective", "#008000"), (b.Name, b.Tint));
        Assert.Equal(DefFactory.CheckerAcross, a.Size.W);
        Assert.Equal(Math.Round(DefFactory.CheckerAcross * 0.65, 4), b.Size.W);
    }

    [Fact]
    public void A_token_glued_over_a_backing_token_is_one_piece_with_both_pictures_in_their_own_colours_and_both_heights()
    {
        const double Thickness = 0.1, BackingBelow = -0.1, PieceScale = 0.25, BackingWider = 1.5;
        var cfg = TempConfig();
        const string Face = "http://a/line.png", White = "http://a/white.png";
        LocalPicture(cfg, Face, 64, 16, (_, _) => SKColors.White);
        LocalPicture(cfg, White, 64, 16, (_, _) => SKColors.White);
        static JsonObject Token(string url) => new() { ["ImageURL"] = url, ["CustomToken"] = new JsonObject { ["Thickness"] = Thickness } };
        var backing = Obj("Custom_Token", "Backing", BackingWider, new JsonObject { ["CustomImage"] = Token(White) });
        backing["Transform"]!["posY"] = BackingBelow;
        backing["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 };
        var line = Obj("Custom_Token", "Line", PieceScale, new JsonObject { ["CustomImage"] = Token(Face), ["ChildObjects"] = new JsonArray(backing) });
        line["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0 };
        var module = Module("glued", line);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "glued", "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.glued", "pack.json"));
        var piece = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == piece.Def);
        using var bmp = SKBitmap.Decode(Path.Combine(Packages, "tts.glued", pack.Assets[def.Image!].File));
        Assert.Equal((SKColors.Red, SKColors.White), (bmp.GetPixel(bmp.Width / 2, bmp.Height / 2), bmp.GetPixel(bmp.Width / 2, 2)));
        Assert.Equal(2 * Thickness, def.Thickness, 3);
        Assert.Equal((0.0, 0), (piece.Y, piece.Stack));
        Assert.Empty(piece.Attached);
    }

    [Fact]
    public void A_glued_stack_turned_over_lies_face_down_showing_its_lowest_token_as_the_game_shows_it_from_above()
    {
        const double Thickness = 0.2, GluedAbove = 0.2;
        var cfg = TempConfig();
        const string Square = "http://a/square.png";
        LocalPicture(cfg, Square, 32, 32, (_, _) => SKColors.White);
        static JsonObject Token() => new() { ["ImageURL"] = Square, ["CustomToken"] = new JsonObject { ["Thickness"] = Thickness } };
        var glued = Obj("Custom_Token", "", 1, new JsonObject { ["CustomImage"] = Token() });
        (glued["Transform"]!["posY"], glued["Transform"]!["rotY"]) = (GluedAbove, 0.0);
        glued["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0 };
        var piece = Obj("Custom_Token", "Square", 0.4, new JsonObject { ["CustomImage"] = Token(), ["ChildObjects"] = new JsonArray(glued) });
        piece["Transform"]!["rotZ"] = 180.0;
        piece["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 0.0 };
        var module = Module("turned", piece);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "turned", "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.turned", "pack.json"));
        var shown = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == shown.Def);
        SKColor Middle(string sha)
        {
            using var bmp = SKBitmap.Decode(Path.Combine(Packages, "tts.turned", pack.Assets[sha].File));
            return bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
        }
        Assert.True(shown.FaceDown);
        Assert.Equal((SKColors.Red, SKColors.Yellow), (Middle(def.Image!), Middle(def.Back!)));
    }

    [Fact]
    public void Two_glued_units_alike_but_for_their_colour_are_two_kinds_each_in_its_own_colour()
    {
        const double Thickness = 0.1;
        var cfg = TempConfig();
        const string Bar = "http://a/bar.png";
        LocalPicture(cfg, Bar, 64, 16, (_, _) => SKColors.White);
        static JsonObject Token() => new() { ["ImageURL"] = Bar, ["CustomToken"] = new JsonObject { ["Thickness"] = Thickness } };
        JsonObject Unit(string name, double r, double g, double b)
        {
            var basePart = Obj("Custom_Token", "", 1, new JsonObject { ["CustomImage"] = Token() });
            basePart["Transform"]!["posY"] = -Thickness;
            basePart["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 };
            var unit = Obj("Custom_Token", name, 0.2, new JsonObject { ["CustomImage"] = Token(), ["ChildObjects"] = new JsonArray(basePart) });
            unit["ColorDiffuse"] = new JsonObject { ["r"] = r, ["g"] = g, ["b"] = b };
            return unit;
        }
        var module = Module("twoarmies", Unit("Blue", 0.0, 0.0, 1.0), Unit("Red", 1.0, 0.0, 0.0));
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "twoarmies", "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.twoarmies", "pack.json"));
        SKColor Middle(PieceInstance p)
        {
            var def = pack.Pieces.Single(d => d.Id == p.Def);
            using var bmp = SKBitmap.Decode(Path.Combine(Packages, "tts.twoarmies", pack.Assets[def.Image!].File));
            return bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
        }
        var (blue, red) = (table.UmpireBoard.Single(p => p.Label == "Blue"), table.UmpireBoard.Single(p => p.Label == "Red"));
        Assert.NotEqual(blue.Def, red.Def);
        Assert.Equal((SKColors.Blue, SKColors.Red), (Middle(blue), Middle(red)));
    }

    [Fact]
    public void Each_look_of_a_counter_keeps_the_colour_its_state_was_saved_in()
    {
        var cfg = TempConfig();
        const string Disc = "http://a/disc.png";
        LocalPicture(cfg, Disc, 16, 16, (_, _) => SKColors.White);
        JsonObject Look(string name, double r, double g, double b)
        {
            var o = Obj("Custom_Token", name, 0.3, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = Disc } });
            o["ColorDiffuse"] = new JsonObject { ["r"] = r, ["g"] = g, ["b"] = b };
            return o;
        }
        var counter = Look("Yellow", 1.0, 1.0, 0.0);
        counter["States"] = new JsonObject { ["1"] = Look("Green", 0.0, 1.0, 0.0), ["3"] = Look("Red", 1.0, 0.0, 0.0) };
        var module = Module("counter", counter);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "counter", "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.counter", "pack.json"));
        var piece = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == piece.Def);
        Assert.Equal(new[] { "#00FF00", "#FFFF00", "#FF0000" }, def.States.Select(s => s.Tint ?? def.Tint));
        Assert.Equal((1, (string?)null), (piece.State, piece.TintOverride));
    }

    [Fact]
    public void Each_state_of_a_token_shows_its_own_glued_children_even_a_child_with_states_of_its_own()
    {
        const double Thickness = 0.1;
        var cfg = TempConfig();
        const string Formed = "http://a/formed.png", Broken = "http://a/broken.png", Base = "http://a/base.png", Number = "http://a/number.png";
        LocalPicture(cfg, Formed, 16, 4, (_, _) => SKColors.Red);
        LocalPicture(cfg, Broken, 8, 8, (_, _) => SKColors.Black);
        LocalPicture(cfg, Base, 16, 4, (_, _) => SKColors.White);
        LocalPicture(cfg, Number, 8, 8, (_, _) => SKColors.Blue);
        static JsonObject Token(string url) => new() { ["ImageURL"] = url, ["CustomToken"] = new JsonObject { ["Thickness"] = Thickness } };
        static JsonObject Under(string nick, string url, JsonObject? extra = null)
        {
            var c = Obj("Custom_Token", nick, 1, extra ?? new JsonObject());
            c["CustomImage"] = Token(url);
            c["Transform"]!["posY"] = -Thickness;
            c["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 };
            return c;
        }
        var counting = Under("", Number, new JsonObject { ["States"] = new JsonObject { ["2"] = Under("", Base) } });
        (counting["Transform"]!["scaleX"], counting["Transform"]!["scaleZ"]) = (1.5, 1.5);
        var broken = Obj("Custom_Token", "Broken", 1, new JsonObject { ["CustomImage"] = Token(Broken), ["ChildObjects"] = new JsonArray(counting) });
        var unit = Obj("Custom_Token", "Unit", 1, new JsonObject
        {
            ["CustomImage"] = Token(Formed), ["ChildObjects"] = new JsonArray(Under("Base", Base)), ["States"] = new JsonObject { ["2"] = broken },
        });
        foreach (var o in new[] { unit, broken }) o["ColorDiffuse"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 };
        var module = Module("states", unit);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "states", "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.states", "pack.json"));
        var piece = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == piece.Def);
        SKColor Edge(string sha)
        {
            using var bmp = SKBitmap.Decode(Path.Combine(Packages, "tts.states", pack.Assets[sha].File));
            return bmp.GetPixel(1, bmp.Height / 2);
        }
        Assert.Equal(2, def.States.Count);
        Assert.Equal((SKColors.Red, SKColors.Blue), (Edge(def.States[0].Image!), Edge(def.States[1].Image!)));
        Assert.Empty(def.Attachments);
    }

    [Fact]
    public void Blocks_glued_to_a_flattened_block_are_parts_of_it_at_module_heights_a_cap_on_its_top_and_a_post_standing_beside_it()
    {
        const double Wide = 0.053, Flat = 0.063, PostTall = 2.2, Thin = 0.05, CapAt = 1.0, PostAt = 0.6;
        TempConfig();
        static JsonObject Block(string nick, double sx, double sy, double sz, double x, double y)
        {
            var b = Obj("BlockRectangle", nick, 1);
            var t = b["Transform"]!;
            (t["scaleX"], t["scaleY"], t["scaleZ"], t["posX"], t["posY"], t["rotY"]) = (sx, sy, sz, x, y, 0.0);
            return b;
        }
        var works = Block("Breastworks", Wide, Flat, Wide, 0, 1.2);
        works["ChildObjects"] = new JsonArray(Block("Cap", 1, 1, 1, 0.045, CapAt), Block("Post", 1, PostTall, Thin, -0.925, PostAt));
        var module = Module("works", works);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "works", "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.works", "pack.json"));
        var breastworks = Assert.Single(table.UmpireBoard);
        var def = pack.Pieces.Single(d => d.Id == breastworks.Def);
        PieceDef KindOf(PartDef p) => pack.Pieces.Single(d => d.Id == p.Def);
        PartDef Part(string name) => def.Parts!.Single(p => KindOf(p).Name == name);
        Assert.Equal((0.0, 0.0), (breastworks.Y, Part("Post").Y));
        Assert.Equal(Flat, Part("Cap").Y, 3);
        Assert.Equal(PostTall * Flat, KindOf(Part("Post")).Thickness, 3);
    }

    [Fact]
    public void A_module_that_names_only_a_stock_sky_gets_the_nearest_backdrop()
    {
        TempConfig();
        var module = Module("field", Obj("BlockSquare", "Marker", 1));
        var json = JsonNode.Parse(File.ReadAllText(module))!.AsObject();
        json["Sky"] = "Sky_Field";
        File.WriteAllText(module, json.ToJsonString());
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        Assert.Equal("saxon-meadow", Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "field", "scenario.json")).Lighting.Backdrop);
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

        var held = Named(table, "Midway set-up");
        Assert.Equal((60.0, 60.0), (held.Pos.X, held.Pos.Z));
        var midway = Assert.Single(held.Stowed!);
        Assert.Equal("Midway", midway.Label);
        Assert.False(Directory.Exists(Path.Combine(Packages, "scenarios", "fleet-midway-set-up")));
        var bagDef = table.Packs.Where(p => p.StartsWith("tts.fleet")).Select(p => Json.Load<PackManifest>(Path.Combine(Packages, p, "pack.json")))
            .SelectMany(m => m.Pieces).Single(d => d.Id == held.Def);
        Assert.Contains("bag", bagDef.Tags);
        Assert.Equal("circle", bagDef.Shape);
    }

    [Fact]
    public void A_module_s_bags_make_no_scenarios_of_their_own()
    {
        const string At = "{\"lock\":false,\"pos\":{\"x\":1,\"y\":2,\"z\":3},\"rot\":{\"x\":0,\"y\":0,\"z\":0}}";
        var bag = new ModuleObj(Obj("Bag", "Midway scenario", 1, new JsonObject
        {
            ["LuaScript"] = "memoryList = {} function buttonClick_place() end",
            ["LuaScriptState"] = "{\"ml\":{\"abc123\":" + At + ",\"def456\":" + At + "}}",
        }));
        var plan = ImportPlan.Of(new ModuleSave { SaveName = "Naval", Objects = new() { bag } }, null, null);

        Assert.Equal(new[] { "tts.naval", "tts.naval.extras" }, plan.Packs);
        Assert.Equal(new[] { "naval" }, plan.Scenarios);
    }

    [Fact]
    public async Task A_model_a_module_stretches_taller_or_flatter_than_wide_keeps_the_height_the_module_draws()
    {
        var cfg = TempConfig();
        const string Box = "http://a/tray.obj";
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        var corners = from x in new[] { -18, 18 } from y in new[] { -0.1, 0.1 } from z in new[] { -4.5, 4.5 } select FormattableString.Invariant($"v {x} {y} {z}");
        File.WriteAllLines(Path.Combine(models, AssetStore.Key(Box) + ".obj"), corners.Append("vn 0 1 0").Append("f 1 2 3"));
        var rep = new Report();
        var assets = new AssetStore(cfg, rep);
        await assets.FetchAll(new[] { Box });
        var defs = new DefFactory(cfg, assets, rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        PieceDef Tray(double sx, double sy, double sz) => defs.DefFor(new ModuleObj(Obj("Custom_Model", $"Tray {sx}", sx, new JsonObject
        {
            ["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 1.0, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz },
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = Box },
        })), pack)!.Def;
        double DrawnHeight(PieceDef d)
        {
            var lines = File.ReadLines(Path.Combine(pack.Dir, pack.M.Assets[d.Mesh!].File)).Where(l => l.StartsWith("v "));
            var v = lines.Select(l => l.Split(' ')[1..].Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList();
            double Span(int i) => v.Max(p => p[i]) - v.Min(p => p[i]);
            double across = (d.Size.W / Span(0) + d.Size.H / Span(2)) / 2;
            return Span(1) * across;
        }

        var stretched = Tray(3.15, 3.15, 3.5626);
        var even = Tray(2, 2, 2);
        Assert.Equal(0.63, stretched.Thickness, 3);
        Assert.Equal(0.63, DrawnHeight(stretched), 3);
        Assert.Equal(0.4, DrawnHeight(even), 3);
        Assert.Equal(defs.Entry(Box)!.Sha, even.Mesh);
        Assert.Contains("vn 0 1 0", File.ReadAllText(Path.Combine(pack.Dir, pack.M.Assets[stretched.Mesh!].File)));
    }

    [Fact]
    public async Task A_model_a_script_spawns_keeps_the_local_frame_a_placed_model_of_its_scale_has()
    {
        var cfg = TempConfig();
        const string Gun = "http://a/gun.obj";
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        var corners = from x in new[] { -0.4, 0.4 } from y in new[] { 0, 0.3 } from z in new[] { -0.2, 0.2 } select FormattableString.Invariant($"v {x} {y} {z}");
        File.WriteAllLines(Path.Combine(models, AssetStore.Key(Gun) + ".obj"), corners.Append("f 1 2 3"));
        var rep = new Report();
        var assets = new AssetStore(cfg, rep);
        await assets.FetchAll(new[] { Gun });
        var defs = new DefFactory(cfg, assets, rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var placed = defs.DefFor(new ModuleObj(Obj("Custom_Model", "Gun", 0.6, new JsonObject
        {
            ["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 1.0, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = 0.6, ["scaleY"] = 0.6, ["scaleZ"] = 0.6 },
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = Gun },
        })), pack)!.Def;

        defs.Translate($$"""
            function spawnGun()
              local o = spawnObject({ type = 'Custom_Model', position = {0, 1, 0}, scale = {x = 0.6, y = 0.6, z = 0.6} })
              o.setCustomObject({ mesh = "{{Gun}}" })
            end
            """, pack, "spawner");

        var spawned = Assert.Single(pack.M.Pieces, d => d.Tags.Contains("marker"));
        Assert.NotNull(spawned.LocalUnit);
        Assert.Equal(placed.LocalUnit, spawned.LocalUnit);
    }
}
