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
}
