using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class SheetTilesTests
{
    const string Image = "5deff9a657f20295b2948f16d9e4eccec696c20342b596c08519bf42281444eb";

    static PackageStore Store()
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-sheettiles-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new PackageStore(root);
    }

    static void Tiles(PackageStore store, string package, string image = Image)
    {
        var dir = Path.Combine(store.PackageDir(package), "tiles", image);
        Directory.CreateDirectory(Path.Combine(dir, "0"));
        File.WriteAllText(Path.Combine(dir, "0", "0_0.webp"), "tile");
        File.WriteAllText(Path.Combine(dir, "index.json"), "{}");
    }

    [Fact]
    public void A_sheet_naming_a_folder_without_its_tiles_is_pointed_at_one_that_has_them()
    {
        var store = Store();
        Tiles(store, "scenarios/tests");
        Directory.CreateDirectory(store.PackageDir("scenarios/haiti"));
        var sheet = new SheetDef { Name = "InsulaireMEGA", Package = "scenarios/haiti", Image = Image };

        Assert.Equal(1, store.RepointLostTiles(new[] { sheet }));
        Assert.Equal("scenarios/tests", sheet.Package);
        Assert.True(store.HasTiles(sheet));
    }

    [Fact]
    public void The_import_area_is_found_as_well_as_a_scenario()
    {
        var store = Store();
        Tiles(store, "sheets");
        Assert.Equal("sheets", store.FindTilesPackage(Image));
    }

    [Fact]
    public void A_sheet_that_has_its_tiles_keeps_its_folder()
    {
        var store = Store();
        Tiles(store, "scenarios/haiti");
        Tiles(store, "sheets");
        var sheet = new SheetDef { Package = "scenarios/haiti", Image = Image };

        Assert.Equal(0, store.RepointLostTiles(new[] { sheet }));
        Assert.Equal("scenarios/haiti", sheet.Package);
    }

    [Fact]
    public void A_half_copied_pyramid_does_not_count_and_nothing_found_moves_nothing()
    {
        var store = Store();
        Directory.CreateDirectory(Path.Combine(store.PackageDir("scenarios/tests"), "tiles", Image, "0"));
        var sheet = new SheetDef { Package = "scenarios/haiti", Image = Image };

        Assert.Null(store.FindTilesPackage(Image));
        Assert.Equal(0, store.RepointLostTiles(new[] { sheet }));
        Assert.Equal("scenarios/haiti", sheet.Package);
    }
}
