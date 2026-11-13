using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class BundleFigureTests : ConverterHarness
{
    [Fact]
    public void Figures_baked_out_of_asset_bundles_are_found_by_the_address_the_module_names()
    {
        const string Grenadier = "4D2C12454E8AF58A2D88385723381CBA57487548";
        Directory.CreateDirectory(Path.Combine(Packages, "user.figures"));
        Json.Save(Path.Combine(Packages, "user.figures", "pack.json"), new PackManifest
        {
            Id = "user.figures", Name = "Figures",
            Pieces = new()
            {
                new PieceDef { Id = "user.figures.grenadier", Name = "Grenadier", Kind = "model", Mesh = "m1", Texture = "t1", Tags = new() { "model", "bundle:" + Grenadier } },
                new PieceDef { Id = "user.figures.smoke", Name = "Smoke", Kind = "flat", Tags = new() { "effect", "bundle:B89A13D24198F5D661BB3327FB2A516BD4B00EE8" } },
            },
        });
        var models = BundleModels.Load(Packages);

        Assert.True(models.Any);
        Assert.Equal("user.figures.grenadier", models.For($"https://steamusercontent-a.akamaihd.net/ugc/1743448938349876877/{Grenadier}/")?.Def.Id);
        Assert.Equal("user.figures", models.For($"https://example.com/{Grenadier}/")?.Pack);
        Assert.Null(models.For("https://steamusercontent-a.akamaihd.net/ugc/1/CD4AC621382542AF808FD3A9519EFC894FBE6C9A/"));
        Assert.Null(models.For("https://steamusercontent-a.akamaihd.net/ugc/1/B89A13D24198F5D661BB3327FB2A516BD4B00EE8/"));
        Assert.Null(models.For(null));
        Assert.False(BundleModels.Load(Path.Combine(_root, "elsewhere")).Any);

        Directory.CreateDirectory(Path.Combine(Packages, "tts.a-module"));
        Json.Save(Path.Combine(Packages, "tts.a-module", "pack.json"), new PackManifest
        {
            Id = "tts.a-module", Name = "A module",
            Pieces = new() { new PieceDef { Id = "tts.a-module.grenadier-mirrored-1", Name = "Grenadier", Kind = "model", Mesh = "m2", Texture = "t2", Tags = new() { "model", "bundle:" + Grenadier } } },
        });
        Assert.Equal("user.figures.grenadier", BundleModels.Load(Packages).For($"https://example.com/{Grenadier}/")?.Def.Id);
    }

    const string Gunner = "5E3D23565F9BF69B3E99496834492DCB68598659", Mesh = "aaaa000000000000000000000000000000000000000000000000000000000001",
        Paint = "bbbb000000000000000000000000000000000000000000000000000000000002";

    void Baked(string dir, string packId)
    {
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        File.WriteAllText(Path.Combine(dir, "assets", Mesh + ".obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        File.WriteAllBytes(Path.Combine(dir, "assets", Paint + ".png"), new byte[] { 137, 80, 78, 71 });
        Json.Save(Path.Combine(dir, "pack.json"), new PackManifest
        {
            Id = packId, Name = "Models from module asset bundles",
            Pieces = new()
            {
                new PieceDef
                {
                    Id = packId + ".gunner-5e3d2356", Name = "Gunner", Kind = "model", Mesh = Mesh, Texture = Paint, Size = new Size2(1, 1), Thickness = 2, HeightScale = 1,
                    Tags = new() { "model", "bundle:" + Gunner, "bundle-read:tiled" },
                },
            },
            Assets = new()
            {
                [Mesh] = new AssetEntry { File = $"assets/{Mesh}.obj", Kind = "mesh" },
                [Paint] = new AssetEntry { File = $"assets/{Paint}.png", Kind = "image", W = 2, H = 2 },
            },
        });
    }

    (Scenario Table, PackManifest Pack, string? Extras) Import(string name, params System.Text.Json.Nodes.JsonObject[] objects)
    {
        var main = typeof(Config).Assembly.EntryPoint!;
        Assert.Equal(0, (int)main.Invoke(null, new object[] { new[] { "import", Module(name, objects), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!);
        var extras = Path.Combine(Packages, $"tts.{name}.extras", "pack.json");
        return (Json.Load<Scenario>(Path.Combine(Packages, "scenarios", name, "scenario.json")), Json.Load<PackManifest>(Path.Combine(Packages, "tts." + name, "pack.json")),
            File.Exists(extras) ? File.ReadAllText(extras) : null);
    }

    static System.Text.Json.Nodes.JsonObject Figure(double x, double scaleY = 1)
    {
        var o = Obj("Custom_Assetbundle", "Gun crew", 1, new System.Text.Json.Nodes.JsonObject
        {
            ["CustomAssetbundle"] = new System.Text.Json.Nodes.JsonObject { ["AssetbundleURL"] = $"https://steamusercontent-a.akamaihd.net/ugc/1/{Gunner}/" },
        });
        (o["Transform"]!["posX"], o["Transform"]!["scaleY"]) = (x, scaleY);
        return o;
    }

    [Fact]
    public void Figures_read_from_a_module_s_bundles_go_into_the_module_s_own_pack_and_its_pieces_point_there()
    {
        var cfg = TempConfig();
        Baked(BundleBake.Dir(cfg.Cache), BundleBake.PackId);

        var (table, pack, extras) = Import("battery", Figure(0), Figure(3));

        var figure = Assert.Single(pack.Pieces, d => d.Mesh == Mesh);
        Assert.Equal(new[] { figure.Id, figure.Id }, table.UmpireBoard.Select(p => p.Def));
        Assert.DoesNotContain(figure.Tags, t => t.StartsWith(BundleBake.Tag, StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Packages, "tts.battery", pack.Assets[Mesh].File)));
        Assert.True(File.Exists(Path.Combine(Packages, "tts.battery", pack.Assets[Paint].File)));
        Assert.Contains("tts.battery", table.Packs);
        Assert.DoesNotContain(table.Packs, p => BundleBake.Bakes(p));
        Assert.False(Directory.Exists(Path.Combine(Packages, BundleBake.PackId)));
        Assert.False(Directory.Exists(Path.Combine(Packages, BundleBake.Shared)));
        Assert.True(extras == null || !extras.Contains(Mesh), "the extras pack holds no second copy of the figure");
    }

    [Fact]
    public void Figures_kept_in_the_old_shared_pack_are_copied_into_the_module_and_the_old_pack_is_left_as_it_was()
    {
        TempConfig();
        var old = Path.Combine(Packages, BundleBake.Shared);
        Baked(old, BundleBake.Shared);
        var before = File.ReadAllText(Path.Combine(old, "pack.json"));

        var (table, pack, _) = Import("redoubt", Figure(0));

        var figure = Assert.Single(pack.Pieces, d => d.Mesh == Mesh);
        Assert.Equal(figure.Id, Assert.Single(table.UmpireBoard).Def);
        Assert.DoesNotContain(BundleBake.Shared, table.Packs);
        Assert.Equal(before, File.ReadAllText(Path.Combine(old, "pack.json")));
    }

    [Fact]
    public void A_figure_stretched_in_the_module_is_a_kind_of_the_module_s_own_with_its_files()
    {
        var cfg = TempConfig();
        Baked(BundleBake.Dir(cfg.Cache), BundleBake.PackId);

        var (table, _, _) = Import("tall", Figure(0, scaleY: 2));

        var piece = Assert.Single(table.UmpireBoard);
        Assert.DoesNotContain(table.Packs, p => BundleBake.Bakes(p));
        var owner = table.Packs.Single(p => File.Exists(Path.Combine(Packages, p, "pack.json")) && Json.Load<PackManifest>(Path.Combine(Packages, p, "pack.json")).Pieces.Any(d => d.Id == piece.Def));
        var kind = Json.Load<PackManifest>(Path.Combine(Packages, owner, "pack.json"));
        Assert.StartsWith("tts.tall", owner);
        Assert.Equal(4, kind.Pieces.Single(d => d.Id == piece.Def).Thickness, 3);
        Assert.True(File.Exists(Path.Combine(Packages, owner, kind.Assets[Mesh].File)));
    }

    [Fact]
    public void A_bundle_whose_address_holds_no_hash_is_known_by_its_address_the_same_way_every_time()
    {
        const string Hashed = "https://steamusercontent-a.akamaihd.net/ugc/1/4D2C12454E8AF58A2D88385723381CBA57487548/";
        Assert.Equal("4D2C12454E8AF58A2D88385723381CBA57487548", BundleModels.IdOf(Hashed));
        var plain = BundleModels.IdOf("https://www.dropbox.com/s/abc/flame.unity3d?dl=1");
        Assert.Matches("^[0-9A-F]{40}$", plain);
        Assert.Equal(plain, BundleModels.IdOf(" https://www.dropbox.com/s/abc/flame.unity3d?dl=1 "));
        Assert.NotEqual(plain, BundleModels.IdOf("https://www.dropbox.com/s/xyz/smoke.unity3d?dl=1"));
    }
}
