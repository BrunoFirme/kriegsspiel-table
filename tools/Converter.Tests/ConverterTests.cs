using System.Globalization;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-converter-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        BestEffort.DeleteFolder(_root);
    }

    string Packages => Path.Combine(_root, "packages");

    Config TempConfig()
    {
        var cfgPath = WriteConfig();
        return Config.Load(cfgPath);
    }

    string WriteConfig(JsonArray? packs = null)
    {
        var cfg = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "converter.json")))!.AsObject();
        if (packs != null) cfg["packs"] = packs;
        cfg["ttsModsPath"] = Path.Combine(_root, "tts", "Mods");
        cfg["cachePath"] = Path.Combine(_root, "cache");
        cfg["outputPath"] = Packages;
        Directory.CreateDirectory(Path.Combine(_root, "tts", "Mods", "Workshop"));
        var path = Path.Combine(_root, "converter.json");
        File.WriteAllText(path, cfg.ToJsonString());
        return path;
    }

    static JsonObject Obj(string type, string nick, double scale, JsonObject? extra = null)
    {
        var o = new JsonObject
        {
            ["Name"] = type, ["GUID"] = Guid.NewGuid().ToString("N")[..6], ["Nickname"] = nick, ["Description"] = "", ["GMNotes"] = "",
            ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = 1, ["posZ"] = 0, ["rotX"] = 0, ["rotY"] = 180, ["rotZ"] = 0, ["scaleX"] = scale, ["scaleY"] = 1, ["scaleZ"] = scale },
            ["ColorDiffuse"] = new JsonObject { ["r"] = 0.5, ["g"] = 0.25, ["b"] = 0.75 },
        };
        if (extra != null) foreach (var (k, v) in extra) o[k] = v?.DeepClone();
        return o;
    }

    string Module(string name, params JsonObject[] objects)
    {
        var save = new JsonObject { ["SaveName"] = name, ["LuaScript"] = "", ["LuaScriptState"] = "", ["ObjectStates"] = new JsonArray(objects.Select(o => (JsonNode)o).ToArray()) };
        var path = Path.Combine(_root, "modules", name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, save.ToJsonString());
        return path;
    }

    List<string> IdsUnder(string culture, params JsonObject[] objects)
    {
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var cfg = TempConfig();
            var rep = new Report();
            var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
            var pack = new PackOut(cfg.Output, "t", "T", "t", null);
            return objects.Select(o => defs.DefFor(new TtsObj((JsonObject)o.DeepClone()), pack)!.Def.Id).ToList();
        }
        finally { CultureInfo.CurrentCulture = was; }
    }

    [Fact]
    public void Piece_ids_are_the_same_whatever_the_windows_locale()
    {
        var block = Obj("BlockSquare", "Brigade", 0.75);
        var text = Obj("3DText", "", 1, new JsonObject { ["Text"] = new JsonObject { ["Text"] = "Ford", ["fontSize"] = 64.5 } });
        Assert.Equal(IdsUnder("", block, text), IdsUnder("pt-BR", block, text));
    }

    [Fact]
    public void A_baked_text_picture_is_named_by_its_bytes()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var card = Obj("Notecard", "Orders", 1);
        card["Description"] = "Hold the ford";
        var text = Obj("3DText", "", 1, new JsonObject { ["Text"] = new JsonObject { ["Text"] = "Ford", ["fontSize"] = 64.0 } });
        foreach (var o in new[] { card, text })
        {
            var def = defs.DefFor(new TtsObj(o), pack)!.Def;
            Assert.NotNull(def.Image);
            var file = Path.Combine(pack.Dir, pack.M.Assets[def.Image].File);
            Assert.Equal(def.Image, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
        }
    }

    [Fact]
    public async Task Restoring_a_pack_exported_through_tts_gives_back_its_pieces_and_no_more()
    {
        var cfg = TempConfig();
        Json.Save(Path.Combine(Packages, "user.feud", "pack.json"), new PackManifest
        {
            Id = "user.feud", Name = "Feud",
            Pieces =
            {
                new PieceDef { Id = "user.feud.foot", Name = "Pogue's Foot", Size = new Size2(1, 0.5) },
                new PieceDef { Id = "user.feud.horse", Name = "Boggs' Horse", Size = new Size2(1, 0.5) },
            },
        });
        var module = new TtsExport(cfg).ExportPack("user.feud", Path.Combine(_root, "modules", "feud.json"));
        Directory.Delete(Path.Combine(Packages, "user.feud"), true);

        var rep = new Report();
        var way = new MakeWay(cfg);
        var built = await new PackBuilder(cfg, new AssetStore(cfg, rep), rep, way).Build(new PackCfg { WorkshopId = module, Id = "user.feud", Short = "kt", Name = "Feud" });
        way.Done();
        Assert.Equal(new[] { "user.feud.foot", "user.feud.horse" }, built.M.Pieces.Select(p => p.Id).Order());
    }

    [Fact]
    public void A_pack_with_object_types_the_converter_cannot_map_exits_with_code_2()
    {
        var module = Module("odd", Obj("BlockSquare", "Brigade", 1), Obj("Strange_Contraption", "What is this", 1));
        var cfg = WriteConfig(new JsonArray(new JsonObject { ["workshopId"] = module, ["id"] = "test.odd", ["short"] = "odd", ["name"] = "Odd" }));
        var main = typeof(Config).Assembly.EntryPoint!;
        var code = (int)main.Invoke(null, new object[] { new[] { "pack", "test.odd", "--config", cfg, "--out", Packages } })!;
        Assert.True(File.Exists(Path.Combine(Packages, "test.odd", "pack.json")));
        Assert.Equal(2, code);
    }

    [Fact]
    public void An_object_s_decals_come_across_on_its_faces_in_piece_units_and_a_missing_picture_is_left_out()
    {
        static JsonObject Decal(string url, double x, double y, double z, double ry, double sx, double sy) => new()
        {
            ["Transform"] = new JsonObject { ["posX"] = x, ["posY"] = y, ["posZ"] = z, ["rotX"] = 90, ["rotY"] = ry, ["rotZ"] = 0, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = 1 },
            ["CustomDecal"] = new JsonObject { ["Name"] = "Arms", ["ImageURL"] = url, ["Size"] = 1 },
        };
        var o = new TtsObj(new JsonObject
        {
            ["Name"] = "Custom_Tile",
            ["AttachedDecals"] = new JsonArray(Decal("http://a/arms.png", 0.5, 0.2, -0.4, 180, 0.6, 0.4), Decal("http://a/under.png", 0.5, -0.2, 0, 200, 1, 1), Decal("http://a/gone.png", 0, 0.2, 0, 180, 1, 1)),
        });
        var def = new PieceDef { Size = new Size2(2, 1) };
        var ds = ScenarioBuilder.DecalsOf(o, def, TokenArea, url => url.Contains("gone") ? null : url);
        Assert.Equal(2, ds.Count);
        Assert.Equal(("http://a/arms.png", "front", -0.25, -0.2, 0.3, 0.2, 0.0), (ds[0].Picture, ds[0].Face, ds[0].X, ds[0].Z, ds[0].W, ds[0].H, ds[0].Rot));
        Assert.Equal(("back", 0.25, 20.0), (ds[1].Face, ds[1].X, ds[1].Rot));
    }

    const double TokenArea = 14;

    [Theory]
    [InlineData(678, 266, 0.26, 0.26, 5.97, 2.34)]
    [InlineData(678, 103, 0.56, 0.3928227, 9.6, 1.45)]
    [InlineData(3069, 150, 0.3278833, 0.23, 16.92, 0.81)]
    public void A_decal_fitted_to_a_token_in_tabletop_simulator_covers_the_token_here_too(int pw, int ph, double sx, double sz, double dw, double dh)
    {
        double aspect = pw / (double)ph;
        var def = new PieceDef { Size = new Size2(Math.Sqrt(TokenArea * aspect) * sx, Math.Sqrt(TokenArea / aspect) * sz), Tags = new() { "token" } };
        var o = new TtsObj(new JsonObject
        {
            ["Name"] = "Custom_Token",
            ["Transform"] = new JsonObject { ["scaleX"] = sx, ["scaleY"] = 1, ["scaleZ"] = sz },
            ["AttachedDecals"] = new JsonArray(new JsonObject
            {
                ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = 0.055, ["posZ"] = 0, ["rotX"] = 90, ["rotY"] = 180, ["rotZ"] = 0, ["scaleX"] = dw, ["scaleY"] = dh, ["scaleZ"] = 1 },
                ["CustomDecal"] = new JsonObject { ["Name"] = "Outline", ["ImageURL"] = "http://a/outline.png", ["Size"] = 1 },
            }),
        });
        var d = Assert.Single(ScenarioBuilder.DecalsOf(o, def, TokenArea, url => url));
        const double Fit = 0.03;
        Assert.InRange(d.W / def.Size.W, 1 - Fit, 1 + Fit);
        Assert.InRange(d.H / def.Size.H, 1 - Fit, 1 + Fit);
    }

    [Fact]
    public async Task A_piece_whose_picture_is_gone_for_good_is_left_out_and_reported_and_one_that_only_failed_to_answer_keeps_a_placeholder()
    {
        var cfg = TempConfig();
        const string Good = "http://a/good.png", Gone = "http://a/gone.png", SteamGone = "https://steamusercontent-a.akamaihd.net/ugc/1/ABCDEF/", Page = "http://a/page.jpg", Busy = "http://a/busy.png", Theirs = @"C:\Kaartjesfolder\Punica\BattalionAla1.jpg";
        var images = Path.Combine(cfg.TtsModsPath, "Images");
        Directory.CreateDirectory(images);
        using (var bmp = new SkiaSharp.SKBitmap(4, 4)) using (var png = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(Path.Combine(images, AssetStore.Key(Good) + ".png"),
            png.ToArray());
        File.WriteAllBytes(Path.Combine(images, AssetStore.Key(Page) + ".jpg"), "<html><body>moved</body></html>"u8.ToArray());
        Directory.CreateDirectory(cfg.Cache);
        File.WriteAllText(Path.Combine(cfg.Cache, "index.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, CacheEntry>
        {
            [AssetStore.Key(Gone)] = new() { Status = "dead", Error = "HTTP 404", Url = Gone },
            [AssetStore.Key(SteamGone)] = new() { Status = "missing", Error = "HTTP 404", Url = SteamGone },
            [AssetStore.Key(Busy)] = new() { Status = "missing", Error = "HTTP 503", Url = Busy },
        }, Config.Opts));
        var rep = new Report();
        var assets = new AssetStore(cfg, rep);
        await assets.FetchAll(new[] { Theirs });
        var defs = new DefFactory(cfg, assets, rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        DefResult? Token(string nick, string url) => defs.DefFor(new TtsObj(Obj("Custom_Token", nick, 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = url } })), pack);

        Assert.Equal(("#8040BF", true), Token("Kept", Good)!.Def is var kept ? (kept.Tint, kept.TintsPictures) : default);
        Assert.False(defs.DefFor(new TtsObj(Obj("Custom_Tile", "Flag", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = Good } })), new PackOut(cfg.Output, "u", "U", "u", null))!.Def.TintsPictures);
        Assert.Null(Token("Swamp", Gone));
        Assert.Null(Token("Jetty", SteamGone));
        Assert.Null(Token("Mill", Page));
        Assert.NotNull(Token("Waiting", Busy));
        Assert.Null(Token("", Theirs));
        Assert.Equal(new[] { "Kept", "Waiting" }, pack.M.Pieces.Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(4, rep.PiecesLeftOut);
        var path = Path.Combine(_root, "report.md");
        rep.Write(path, "import t");
        var text = File.ReadAllText(path);
        Assert.Contains("### PIECES LEFT OUT (4)", text);
        Assert.Contains("- BattalionAla1 — ", text);
        Assert.Contains("- Swamp — `http://a/gone.png` — HTTP 404", text);
    }

    [Fact]
    public void A_big_locked_textured_model_above_the_table_is_terrain_and_a_table_or_a_bare_model_is_furniture()
    {
        TtsObj Model(double y, bool locked, string? texture) => new(Obj("Custom_Model", "", 12.25, new JsonObject
        {
            ["Locked"] = locked,
            ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = y, ["posZ"] = 0, ["rotX"] = 0, ["rotY"] = 180, ["rotZ"] = 0, ["scaleX"] = 12.25, ["scaleY"] = 25, ["scaleZ"] = 12.25 },
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/ground.obj", ["DiffuseURL"] = texture },
        }));
        var map = Model(1.25, true, "http://a/map.png");
        Assert.Equal((true, false), (map.IsTerrain, map.IsFurniture));
        foreach (var other in new[] { Model(-9, true, "http://a/table.png"), Model(1.25, false, "http://a/map.png"), Model(1.25, true, null) })
            Assert.Equal((false, true), (other.IsTerrain, other.IsFurniture));
    }

    [Fact]
    public void A_modules_size_counts_its_pieces_pictures_on_this_pc_once_and_how_many_more_the_import_would_fetch()
    {
        var cfg = TempConfig();
        var images = Path.Combine(_root, "tts", "Mods", "Images");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "httpsexamplecomfrontpng.png"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(images, "httpsexamplecomscriptpng.png"), new byte[5000]);
        static JsonObject Picture(string front, string? back = null)
        {
            var image = new JsonObject { ["ImageURL"] = front };
            if (back != null) image["ImageSecondaryURL"] = back;
            return new JsonObject { ["CustomImage"] = image };
        }
        var token = Obj("Custom_Token", "Ala", 1, Picture("https://example.com/front.png", "https://example.com/back.png"));
        var twin = Obj("Custom_Token", "Ala copy", 1, Picture(" https://example.com/front.png"));
        var lettered = Obj("Custom_Token", "Board", 1, Picture("file:///C:/nowhere/board.png"));
        lettered["LuaScript"] = "local a, b = \"https://example.com/script.png\", \"https://example.com/gone.png\"";
        var save = TtsSave.Load(cfg, Module("feud", token, twin, lettered));
        var assets = new AssetStore(cfg, new Report());
        Assert.Equal((1000L, 2), assets.Footprint(PackBuilder.PieceUrls(save), PackBuilder.Urls(save)));
    }

    [Fact]
    public void Tabletop_effect_bundles_become_the_closest_of_firmes_effects_at_the_size_they_showed()
    {
        static PieceDef Preset(string id, double w, double h) => new() { Id = "std.effects." + id, Name = id, Size = new Size2(w, h), Effect = new EffectDef { Kind = "smoke" }, Tags = new() { "effect" } };
        Directory.CreateDirectory(Path.Combine(Packages, "std.effects"));
        Json.Save(Path.Combine(Packages, "std.effects", "pack.json"), new PackManifest { Id = "std.effects", Name = "Effects", Pieces = new() { Preset("powder-smoke", 6, 1.5), Preset("roundshot-in-water", 0.8, 0.8) } });
        var effects = BundleEffects.Load(Packages);
        static TtsObj Bundle(string nick, string url, double scale) => new(Obj("Custom_Assetbundle", nick, scale, new JsonObject { ["CustomAssetbundle"] = new JsonObject { ["AssetbundleURL"] = url } }));

        var smoke = effects.For(Bundle("", "https://steamusercontent-a.akamaihd.net/ugc/795360302134688407/B89A13D24198F5D661BB3327FB2A516BD4B00EE8/", 0.29));
        Assert.Equal("std.effects.powder-smoke", smoke?.Def.Id);
        Assert.Equal(6 * 0.29 / 3, smoke!.Value.Scale, 3);
        Assert.Equal("std.effects.roundshot-in-water", effects.For(Bundle("", "https://dl.dropbox.com/s/x/water%20fall.unity3d?dl=1", 1))?.Def.Id);
        Assert.Null(effects.For(Bundle("", "https://steamusercontent-a.akamaihd.net/ugc/1/CD4AC621382542AF808FD3A9519EFC894FBE6C9A/", 1)));
        Assert.Null(effects.For(Bundle("Oza", "https://example.com/house.unity3d", 1)));
        Assert.False(BundleEffects.Load(Path.Combine(_root, "elsewhere")).Installed);
    }

    [Fact]
    public void Modules_kept_in_the_game_folder_of_another_steam_library_are_found()
    {
        var steam = Path.Combine(_root, "Steam");
        var library = Path.Combine(_root, "E", "SteamLibrary");
        var gameMods = Path.Combine(library, "steamapps", "common", "Tabletop Simulator", "Tabletop Simulator_Data", "Mods");
        var homeMods = Path.Combine(_root, "Documents", "Mods");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(gameMods, "Workshop"));
        Directory.CreateDirectory(Path.Combine(homeMods, "Workshop"));
        File.WriteAllText(Path.Combine(gameMods, "Workshop", "704986068.json"), "{}");
        File.WriteAllText(Path.Combine(gameMods, "Workshop", "816003783.json"), "{}");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
        Assert.Equal(new[] { steam, library }, TtsFolders.SteamLibraries(steam));
        Assert.Contains(gameMods, TtsFolders.ModsFolders(steam));
        Assert.Equal(gameMods, TtsFolders.MostModules(new[] { homeMods, Path.Combine(_root, "nowhere"), gameMods }));
        Assert.Null(TtsFolders.MostModules(new[] { Path.Combine(_root, "nowhere") }));
        Assert.Empty(TtsFolders.SteamLibraries(null));
    }
}
